#!/usr/bin/env node
// Chapter 10's three router surprises, each reproduced on purpose.
//
// The chapter prints what these produce, so the gate asserts them. Each case
// starts a real Cosmo Router container against a real execution config, asks
// it something, and checks what came back and what it logged. Nothing here is
// a fixture: the graph is federation/supergraph.json as committed, or that
// same pair of schemas with one literal edit applied, the way chapter 9's
// composition-cases.mjs does it.
//
// There is one implementation rather than one per verify script, for the
// reason chapter 9's script gives: verify.ps1 and verify.sh are supposed to
// check the same things and have drifted apart once already.
//
// Needs Docker, and needs both subgraphs answering on 5100 and 5101, because
// two of the three cases are about whether the router can reach them.
//
// Usage:
//   node scripts/router-cases.mjs                run every case
//   node scripts/router-cases.mjs --list         name the cases and exit
//   node scripts/router-cases.mjs --print <name> run one case and print the
//                                                evidence, which is how the
//                                                chapter's listings were made

import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const CATALOG_SCHEMA = join(repoRoot, 'schema', 'catalog.graphql');
const MOSAIC_SCHEMA = join(repoRoot, 'schema', 'mosaic.graphql');
const SUPERGRAPH = join(repoRoot, 'federation', 'supergraph.json');

// The image docker-compose.yml pins. Kept in step with it by hand; a mismatch
// would mean the gate checks a router nobody runs.
const IMAGE = 'ghcr.io/wundergraph/cosmo/router:0.337.1';

// Away from 3002, so a case can run while the composed stack is up.
const PORT = Number(process.env.MOSAIC_ROUTER_CASE_PORT ?? 3102);

// ---------------------------------------------------------------------------
// The cases
// ---------------------------------------------------------------------------

const CASES = [
  {
    name: 'config-beats-env',
    summary: 'a value in config.yaml wins over the environment variable for it',
    async run(ctx) {
      const evidence = [];

      // The control first, because the experiment is worthless without it: if
      // LOG_LEVEL=debug produced no debug lines on its own, the comparison
      // below would prove nothing.
      // DEV_MODE matches what the generated config.yaml below sets, so that
      // the only difference between the two runs is where log_level comes
      // from. It also changes the log format, which is why counting has to
      // handle both.
      const control = await ctx.router({
        env: {
          EXECUTION_CONFIG_FILE_PATH: '/etc/mosaic/supergraph.json',
          LISTEN_ADDR: '0.0.0.0:3002',
          DEV_MODE: 'true',
          LOG_LEVEL: 'debug',
        },
      });
      const controlDebug = countDebug(control.logs);
      // padEnd rather than spaces typed to look right: the two labels differ
      // in length and hand-aligned columns drift the moment either is edited.
      evidence.push(`${'environment alone, LOG_LEVEL=debug:'.padEnd(44)}${controlDebug} debug lines`);

      // Same environment variable, and a config file that says otherwise.
      const both = await ctx.router({
        config: 'log_level: info\n',
        env: { LOG_LEVEL: 'debug' },
      });
      const bothDebug = countDebug(both.logs);
      evidence.push(`${'config.yaml says info, environment debug:'.padEnd(44)}${bothDebug} debug lines`);

      const problems = [];
      if (controlDebug === 0) {
        problems.push('LOG_LEVEL=debug produced no debug lines at all, so this case proves nothing');
      }
      if (bothDebug !== 0) {
        problems.push(`the environment variable won: ${bothDebug} debug lines with log_level: info in the config file`);
      }
      return { problems, evidence };
    },
  },
  {
    name: 'localhost-fallback',
    summary: 'localhost in a routing URL reaches the host, until you turn that off',
    async run(ctx) {
      const evidence = [];
      const query = '{ browseProducts(first: 1) { nodes { title } } }';

      const on = await ctx.router({ config: '' });
      const onAnswer = await on.ask(query);
      evidence.push(`default (localhost_fallback_inside_docker unset): ${describe(onAnswer)}`);

      const off = await ctx.router({ config: 'localhost_fallback_inside_docker: false\n' });
      const offAnswer = await off.ask(query);
      evidence.push(`localhost_fallback_inside_docker: false:          ${describe(offAnswer)}`);

      const problems = [];
      if (!onAnswer.json?.data?.browseProducts) {
        problems.push('the router could not reach catalog with the fallback left at its default; is the subgraph running on 5101?');
      }
      if (offAnswer.json?.data?.browseProducts) {
        problems.push('the router still reached catalog with the fallback off, so localhost resolved some other way');
      }
      if (!JSON.stringify(offAnswer.json?.errors ?? []).includes("Failed to fetch from Subgraph 'catalog'")) {
        problems.push(`expected a failed-to-fetch error from catalog, got: ${JSON.stringify(offAnswer.json)?.slice(0, 200)}`);
      }
      return { problems, evidence };
    },
  },
  {
    name: 'resolvability-off',
    summary: 'a graph composed with --disable-resolvability-validation, in front of a router',
    async run(ctx) {
      const evidence = [];

      // Chapter 9's unsatisfiable-key edit: Mosaic keeps the key and stops
      // answering for it. Composed with the flag, which is the only way this
      // pair produces a config at all.
      const config = ctx.composeUnsatisfiable();
      const router = await ctx.router({ executionConfig: config, config: '' });

      evidence.push(`the router started: ${router.started ? 'yes' : 'no'}`);
      evidence.push(`warnings about the graph in its startup log: ${countGraphWarnings(router.logs)}`);

      const inside = await router.ask('{ browseProducts(first: 2) { nodes { title sku } } }');
      evidence.push(`a query that stays inside catalog: ${describe(inside)}`);

      const across = await router.ask('{ browseProducts(first: 2) { nodes { title price { amount } } } }');
      evidence.push(`a query that crosses the boundary: ${describe(across)}`);

      // The router's `error` field is a JSON string and its contents are full
      // of escaped quotes, so the match has to allow them and the result has
      // to be unescaped. Stopping at the first quote truncates the message
      // exactly where it starts naming the field.
      const logged = router.logs
        .split('\n')
        .filter((line) => line.includes('printOperation planner'))
        .map((line) => {
          const match = line.match(/"error":\s*"((?:[^"\\]|\\.)*)"/);
          if (!match) return '';
          return JSON.parse(`"${match[1]}"`).replace(/\s+/g, ' ').trim();
        });
      evidence.push(`what the router logged: ${logged[0] ?? '(nothing about the planner)'}`);

      const problems = [];
      if (!router.started) {
        problems.push('the router refused to start on the config, which would be better behaviour than the chapter describes');
      }
      if (!inside.json?.data?.browseProducts) {
        problems.push('a catalog-only query failed, so this case is measuring something else');
      }
      if (across.status !== 500) {
        problems.push(`expected HTTP 500 on the crossing query, got ${across.status}: ${JSON.stringify(across.json)?.slice(0, 200)}`);
      }
      if (!logged.some((line) => line.includes('Cannot query field "price" on type "Query"'))) {
        problems.push(`the planner error the chapter prints is not in the log; found: ${logged[0] ?? 'nothing'}`);
      }
      return { problems, evidence };
    },
  },
];

// ---------------------------------------------------------------------------
// Running one
// ---------------------------------------------------------------------------

// Development mode colours the level and anything else writes JSON, so the
// word DEBUG is wrapped in escape sequences rather than spaces. Strip the
// colour before matching, or a level check silently counts zero of everything.
const plain = (logs) => logs.replace(/\[[0-9;]*m/g, '');

const level = (logs, name) =>
  plain(logs)
    .split('\n')
    .filter((l) => new RegExp(`\\b${name}\\b`).test(l) || new RegExp(`"level"\\s*:\\s*"${name.toLowerCase()}"`).test(l));

const countDebug = (logs) => level(logs, 'DEBUG').length;

// Lines the router writes about the graph it loaded, as opposed to lines about
// itself. A config composed with the resolvability check off produces none.
const countGraphWarnings = (logs) =>
  level(logs, 'WARN').filter((l) => /resolv|unreachable|entity|subgraph/i.test(l)).length;

const describe = (answer) =>
  answer.json?.errors
    ? `HTTP ${answer.status}, ${JSON.stringify(answer.json.errors[0]?.message)}`
    : `HTTP ${answer.status}, answered`;

function docker(args, options = {}) {
  return execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...options });
}

async function ask(port, query) {
  const res = await fetch(`http://localhost:${port}/graphql`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ query }),
  });
  const text = await res.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {
    // A router that answered with something other than JSON is a finding, and
    // the caller sees it as a null body rather than as an exception here.
  }
  return { status: res.status, json, text };
}

async function waitForHealth(port, deadlineMs = 30000) {
  const deadline = Date.now() + deadlineMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(`http://localhost:${port}/health`);
      if (res.status === 200) return true;
    } catch {
      // Not listening yet.
    }
    await new Promise((r) => setTimeout(r, 250));
  }
  return false;
}

// Everything one case needs: a scratch directory, a way to start a router in
// it, and the guarantee that every container is removed afterwards.
async function withContext(fn) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-router-'));
  const containers = [];
  let port = PORT;

  const context = {
    // A router on the healthy composed config unless the case supplies its own.
    // `config` is the body of config.yaml below the keys every case needs.
    async router({ config, env = {}, executionConfig } = {}) {
      const id = `mosaic-router-case-${containers.length}-${process.pid}`;
      const mine = port++;
      const execPath = executionConfig ?? join(dir, 'supergraph.json');
      if (!executionConfig) copyFileSync(SUPERGRAPH, execPath);

      const args = ['run', '--detach', '--name', id, '--publish', `${mine}:3002`,
        '--add-host', 'host.docker.internal:host-gateway',
        '--volume', `${execPath}:/etc/mosaic/supergraph.json:ro`];

      if (config !== undefined) {
        // A case that passes `config` gets a config file; one that passes only
        // `env` gets none, which is what makes the first case a comparison.
        const path = join(dir, `config-${containers.length}.yaml`);
        writeFileSync(path, [
          'version: "1"',
          'execution_config:',
          '  file:',
          '    path: /etc/mosaic/supergraph.json',
          'listen_addr: "0.0.0.0:3002"',
          'dev_mode: true',
          config,
          '',
        ].join('\n'));
        args.push('--volume', `${path}:/etc/mosaic-router/config.yaml:ro`);
        args.push('--env', 'CONFIG_PATH=/etc/mosaic-router/config.yaml');
      }
      for (const [key, value] of Object.entries(env)) args.push('--env', `${key}=${value}`);
      args.push(IMAGE);

      docker(args);
      containers.push(id);
      const started = await waitForHealth(mine);
      return {
        started,
        // The router writes its structured log to stderr, so both streams have
        // to be read. execFileSync hands back stdout alone.
        get logs() {
          const run = spawnSync('docker', ['logs', id], { encoding: 'utf8' });
          return `${run.stdout ?? ''}${run.stderr ?? ''}`;
        },
        ask: (query) => ask(mine, query),
      };
    },

    // Chapter 9's unsatisfiable pair, composed with the flag that lets it
    // through. The edit has to match exactly once, for the reason
    // composition-cases.mjs gives: a case that silently edits nothing composes
    // the healthy pair and asserts something else entirely.
    composeUnsatisfiable() {
      const from = 'type Product @key(fields: "id") {';
      const to = 'type Product @key(fields: "id", resolvable: false) {';
      const mosaic = readFileSync(MOSAIC_SCHEMA, 'utf8');
      const hits = mosaic.split(from).length - 1;
      if (hits !== 1) {
        throw new Error(
          `the edit to mosaic matched ${hits} times, expected exactly 1.\n` +
            `The text it looks for is:\n${from}\n` +
            'The committed schema has changed under this case. Fix the case rather than the schema.',
        );
      }
      writeFileSync(join(dir, 'broken-mosaic.graphql'), mosaic.replace(from, to));
      copyFileSync(CATALOG_SCHEMA, join(dir, 'broken-catalog.graphql'));
      writeFileSync(join(dir, 'broken-graph.yaml'), [
        'version: 1',
        'subgraphs:',
        '  - name: catalog',
        '    routing_url: http://localhost:5101/graphql',
        '    schema:',
        '      file: broken-catalog.graphql',
        '  - name: mosaic',
        '    routing_url: http://localhost:5100/graphql',
        '    schema:',
        '      file: broken-mosaic.graphql',
        '',
      ].join('\n'));

      const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
      const out = join(dir, 'broken-supergraph.json');
      execFileSync(process.execPath, [wgc, 'router', 'compose', '--disable-resolvability-validation',
        '-i', join(dir, 'broken-graph.yaml'), '-o', out],
        { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
      return out;
    },
  };

  try {
    return await fn(context);
  } finally {
    for (const id of containers) {
      try {
        docker(['rm', '--force', id]);
      } catch {
        // Already gone, or docker is. Either way the case's result stands.
      }
    }
    rmSync(dir, { recursive: true, force: true });
  }
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(20)}${testCase.summary}`);
  process.exit(0);
}

const selected = args[0] === '--print'
  ? CASES.filter((c) => c.name === args[1])
  : CASES;

if (args[0] === '--print' && selected.length === 0) {
  console.error(`No case named "${args[1]}". Try --list.`);
  process.exit(2);
}

let failures = 0;
for (const testCase of selected) {
  let result;
  try {
    result = await withContext((ctx) => testCase.run(ctx));
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(20)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  }

  if (result.problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(20)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(20)}${testCase.summary}`);
    for (const problem of result.problems) console.log(`             ${problem}`);
  }

  if (args[0] === '--print') {
    for (const line of result.evidence) console.log(`             ${line}`);
  }
}

if (failures > 0) {
  console.log(`\n${failures} of ${selected.length} router cases did not behave as chapter 10 says.`);
  process.exit(1);
}
