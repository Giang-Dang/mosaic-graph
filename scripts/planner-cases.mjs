#!/usr/bin/env node
// What the router's plan cache keys on, and what asking to see a plan costs.
// Chapter 17.
//
// Four chapters asked a different form of the same question and none could
// answer it, because a stock Cosmo Router publishes no cache metrics at all:
// telemetry.metrics.prometheus.graphql_cache defaults to false, and there are
// seven caches behind it. Chapter 7 asked whether the plan for a query is
// cached across requests; chapter 10 asked whether two documents that plan to
// identical JSON share a cache entry; chapter 11 asked whether any of the
// representation machinery reaches the key; chapter 13 asked whether the node
// field's argument does.
//
// The route this file takes is not the metrics endpoint. The router exposes
// request.operation.queryPlanHash to access-log expressions, and that string is
// strconv of the uint64 the plan cache is keyed by
// (router/core/operation_planner.go:168 reads opContext.internalHash, and
// router/core/graphql_prehandler.go:955 sets it). So a router configured with
// one extra access-log field reports its own cache key per request, exactly,
// with no sampling in the way. Every case below reads that log.
//
// The assertions are equivalence classes and counts, never milliseconds, for
// the reason decision 62 gives: a count is the same on every machine and a
// timing is not. The one place a duration appears is in the evidence of the
// traced-requests case, where it is printed and not asserted.
//
// Needs Docker, and needs the seven subgraphs answering on 5101 to 5107,
// because two of the four cases cross a subgraph boundary.
//
// Usage:
//   node scripts/planner-cases.mjs                run every case
//   node scripts/planner-cases.mjs --list         name the cases and exit
//   node scripts/planner-cases.mjs --print <name> run one case and print the
//                                                evidence, which is how the
//                                                chapter's listings were made

import { execFileSync, spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SUPERGRAPH = join(repoRoot, 'federation', 'supergraph.json');

// The image docker-compose.yml pins. Kept in step with it by hand, the same way
// router-cases.mjs does: a mismatch would mean the gate checks a router nobody
// runs.
const IMAGE = 'ghcr.io/wundergraph/cosmo/router:0.337.1';

// Away from 3002 and away from router-cases.mjs's 3102, so all three can be up
// at once.
const PORT = Number(process.env.MOSAIC_PLANNER_CASE_PORT ?? 3202);

// The access-log fields every case needs. `label` is echoed from a request
// header rather than inferred from log order, so a case that sends its requests
// out of order still reads them back correctly.
const ACCESS_LOGS = `access_logs:
  enabled: true
  router:
    fields:
      - key: label
        value_from:
          request_header: X-Mosaic-Case
      - key: planHash
        value_from:
          expression: request.operation.queryPlanHash
      - key: planHit
        value_from:
          expression: request.operation.planCacheHit
`;

// The other way to ask the same question. A debug setting puts five cache
// results into response headers as HIT or MISS, which needs no log parsing at
// all and is what a developer at a keyboard should reach for. It shares no code
// with the expression path above, which is what makes it a second witness
// rather than a second reading of the same one.
const CACHE_HEADERS = `engine:
  debug:
    enable_cache_response_headers: true
`;

// ---------------------------------------------------------------------------
// The cases
// ---------------------------------------------------------------------------

// The baseline document every plan-key case compares against. Two products
// deep enough to be worth planning and shallow enough to read.
const BASE = '{ browseProducts(first: 3) { nodes { title } } }';

const CASES = [
  {
    name: 'plan-key-ignores-almost-everything',
    summary: 'eleven documents that differ in name, variables and values share one plan',
    async run(ctx) {
      const router = await ctx.router();

      // Each entry is one document that should land in the same class as the
      // baseline. The comment on each is what it is varying.
      const documents = [
        ['baseline', { query: BASE }],
        // the operation name
        ['named-a', { query: `query A ${BASE}` }],
        ['named-b', { query: `query B ${BASE}` }],
        // the names of declared variables
        ['var-n', { query: 'query ($n: Int) { browseProducts(first: $n) { nodes { title } } }', variables: { n: 3 } }],
        ['var-count', { query: 'query ($count: Int) { browseProducts(first: $count) { nodes { title } } }', variables: { count: 3 } }],
        // an inline literal, against a different inline literal
        ['literal-7', { query: '{ browseProducts(first: 7) { nodes { title } } }' }],
        // an inline literal against a properly declared variable of the type
        // the extraction pass synthesises
        ['declared', { query: 'query ($f: Int) { browseProducts(first: $f) { nodes { title } } }', variables: { f: 3 } }],
        // the value of that variable at request time
        ['declared-9', { query: 'query ($f: Int) { browseProducts(first: $f) { nodes { title } } }', variables: { f: 9 } }],
        // whitespace, and a comment
        ['spaced', { query: '{\n\n   browseProducts(first: 3)   {\n  nodes {\n title\n}  }\n}' }],
        ['commented', { query: `# nothing here matters\n${BASE}` }],
        // a named fragment, against the same selection written out
        ['fragment', { query: 'query { browseProducts(first: 3) { nodes { ...T } } } fragment T on Product { title }' }],
      ];

      for (const [label, body] of documents) await router.ask(label, body);

      const rows = router.rows();
      const problems = [];
      const evidence = [];

      const hashes = new Set();
      for (const [label] of documents) {
        const row = rows.find((r) => r.label === label);
        if (!row) {
          problems.push(`no access-log line came back for "${label}"`);
          continue;
        }
        hashes.add(row.planHash);
        evidence.push(`${label.padEnd(12)} plan=${row.planHash}`);
      }

      if (hashes.size !== 1) {
        problems.push(
          `expected all ${documents.length} documents to share one plan cache key, got ${hashes.size} distinct keys.\n` +
          'If this fails after a router upgrade, the normalization pipeline changed and\n' +
          'chapter 17 section 3 needs re-measuring rather than this assertion loosening.',
        );
      }

      // The control, and the case is worthless without it: one shared plan has
      // to still answer two different questions, or the sharing is a defect.
      const three = await router.ask('control-3', { query: '{ browseProducts(first: 3) { nodes { title } } }' });
      const seven = await router.ask('control-7', { query: '{ browseProducts(first: 7) { nodes { title } } }' });
      const counts = [three.data.browseProducts.nodes.length, seven.data.browseProducts.nodes.length];
      evidence.push(`control: first: 3 -> ${counts[0]} nodes, first: 7 -> ${counts[1]} nodes`);
      if (counts[0] !== 3 || counts[1] !== 7) {
        problems.push(`the shared plan answered ${counts[0]} and ${counts[1]} nodes, expected 3 and 7`);
      }

      return { problems, evidence };
    },
  },

  {
    name: 'plan-key-splits-on-three-things',
    summary: 'an alias, field order and a variable\'s nullability each buy a second plan',
    async run(ctx) {
      const router = await ctx.router();

      // Each pair differs in exactly one thing, which is the whole point: a
      // pair that varied two things would prove nothing about either.
      const pairs = [
        ['alias', 'the alias on a root field',
          { query: '{ x: browseProducts(first: 3) { nodes { title } } }' },
          { query: '{ y: browseProducts(first: 3) { nodes { title } } }' }],
        ['order', 'the order of two fields in a selection set',
          { query: '{ browseProducts(first: 3) { nodes { title sku } } }' },
          { query: '{ browseProducts(first: 3) { nodes { sku title } } }' }],
        ['nullability', 'the declared type of a variable',
          { query: 'query ($f: Int) { browseProducts(first: $f) { nodes { title } } }', variables: { f: 3 } },
          { query: 'query ($f: Int!) { browseProducts(first: $f) { nodes { title } } }', variables: { f: 3 } }],
      ];

      for (const [name, , left, right] of pairs) {
        await router.ask(`${name}-l`, left);
        await router.ask(`${name}-r`, right);
      }

      const rows = router.rows();
      const problems = [];
      const evidence = [];

      for (const [name, what] of pairs) {
        const left = rows.find((r) => r.label === `${name}-l`);
        const right = rows.find((r) => r.label === `${name}-r`);
        if (!left || !right) {
          problems.push(`no access-log line came back for the ${name} pair`);
          continue;
        }
        // Two lines rather than one: the hashes are twenty digits each and a
        // single line carrying both plus the description does not fit a
        // printed page, which is where this output ends up.
        evidence.push(`${name.padEnd(12)} varies ${what}`);
        evidence.push(`${' '.repeat(12)} ${left.planHash} vs ${right.planHash}`);
        if (left.planHash === right.planHash) {
          problems.push(`two documents differing only in ${what} shared a plan cache key`);
        }
      }

      return { problems, evidence };
    },
  },

  {
    name: 'tracing-never-hits-the-plan-cache',
    summary: 'the two headers that show you the plan guarantee it was rebuilt',
    async run(ctx) {
      const router = await ctx.router();
      const document = { query: '{ browseProducts(first: 4) { nodes { title price { amount } } } }' };

      // Warm it first, so that a miss afterwards cannot be explained by the
      // document being new.
      await router.ask('untraced-1', document);
      await router.ask('untraced-2', document);
      await router.ask('untraced-3', document);

      const traced1 = await router.ask('traced-1', document, { 'X-WG-Trace': 'true' });
      await router.ask('traced-2', document, { 'X-WG-Trace': 'true' });
      await router.ask('planonly', document, {
        'X-WG-Include-Query-Plan': 'true',
        'X-WG-Skip-Loader': 'true',
      });

      // And afterwards, to show a traced request does not poison the cache
      // either - it simply is not part of it.
      await router.ask('untraced-4', document);
      await router.ask('untraced-5', document);

      const rows = router.rows();
      const problems = [];
      const evidence = [];

      const expected = {
        'untraced-1': false,   // first sight of the document
        'untraced-2': true,
        'untraced-3': true,
        'traced-1': false,     // operation_planner.go:146
        'traced-2': false,
        planonly: false,
        'untraced-4': true,    // the cache is intact
        'untraced-5': true,
      };

      const hashes = new Set();
      for (const [label, wanted] of Object.entries(expected)) {
        const row = rows.find((r) => r.label === label);
        if (!row) {
          problems.push(`no access-log line came back for "${label}"`);
          continue;
        }
        hashes.add(row.planHash);
        evidence.push(`${label.padEnd(12)} planHit=${row.planHit}`);
        if (row.planHit !== wanted) {
          problems.push(`${label} reported planHit=${row.planHit}, expected ${wanted}`);
        }
      }

      if (hashes.size !== 1) {
        problems.push(`the eight requests were one document and reported ${hashes.size} plan keys`);
      }

      // Printed, never asserted: decision 62 keeps a timing out of a gate. It
      // is here because the point of the case is that this number is always the
      // cold one, and a reader should see it.
      const planner = traced1.extensions?.trace?.info?.planner_stats?.duration_nanoseconds;
      evidence.push(`traced-1 planner_stats: ${planner} ns (always a cold plan, never asserted)`);

      // The second witness, through a mechanism that shares nothing with the
      // access log above: a debug setting puts five cache results into response
      // headers. It says one thing the log cannot, which is that the
      // normalization cache still HITs on the request whose plan cache MISSes.
      // So tracing bypasses one cache rather than starting the request over,
      // and the chapter prints this contrast.
      const headers = await ctx.router({ cacheHeaders: true });
      const seen = [];
      for (const [label, extra] of [
        ['cold', {}], ['warm-1', {}], ['warm-2', {}],
        ['traced', { 'X-WG-Trace': 'true' }],
        ['planonly', { 'X-WG-Include-Query-Plan': 'true', 'X-WG-Skip-Loader': 'true' }],
        ['warm-3', {}],
      ]) {
        const result = await headers.askRaw(label, document, extra);
        const plan = result.headers.get('x-wg-execution-plan-cache');
        const norm = result.headers.get('x-wg-normalization-cache');
        seen.push({ label, plan, norm });
        evidence.push(`${label.padEnd(9)} plan=${plan} normalization=${norm}`);
      }

      const wantedPlan = { cold: 'MISS', 'warm-1': 'HIT', 'warm-2': 'HIT', traced: 'MISS', planonly: 'MISS', 'warm-3': 'HIT' };
      for (const { label, plan, norm } of seen) {
        if (plan !== wantedPlan[label]) {
          problems.push(`${label} reported x-wg-execution-plan-cache=${plan}, expected ${wantedPlan[label]}`);
        }
        // The contrast that makes the point: the traced request is not a new
        // request, it is a request that skipped one cache.
        if (label === 'traced' && norm !== 'HIT') {
          problems.push(`the traced request reported x-wg-normalization-cache=${norm}, expected HIT: ` +
            'if tracing has started bypassing normalization too, the chapter\'s contrast is gone');
        }
      }

      return { problems, evidence };
    },
  },

  {
    name: 'representations-are-deduplicated',
    summary: 'the same key twice goes out once and comes back to both places',
    async run(ctx) {
      const router = await ctx.router();

      // Real identifiers rather than literals, because a global object id
      // encodes a row key and the seed data is reseeded by the gate.
      const seed = await router.ask('seed', {
        query: '{ browseProducts(first: 2) { nodes { id } } }',
      });
      const ids = seed.data.browseProducts.nodes.map((n) => n.id);
      if (ids.length !== 2) throw new Error(`expected 2 product ids from the seed query, got ${ids.length}`);

      // Three positions, two distinct keys, the first one asked for twice.
      const answer = await router.ask('duplicated', {
        query: `{ nodes(ids: ["${ids[0]}", "${ids[0]}", "${ids[1]}"]) { ... on Product { title price { amount } } } }`,
      }, { 'X-WG-Trace': 'true' });

      const problems = [];
      const evidence = [];

      const items = answer.data?.nodes ?? [];
      evidence.push(`asked for 3 positions holding 2 distinct ids, got ${items.length} items back`);
      if (items.length !== 3) {
        problems.push(`expected 3 items in the response, got ${items.length}`);
      } else if (items[0]?.title !== items[1]?.title) {
        problems.push('the two positions holding the same id came back with different titles');
      }

      const fetches = [];
      (function walk(node) {
        if (node?.fetch) fetches.push(node.fetch);
        for (const child of node?.children ?? []) walk(child);
      })(answer.extensions?.trace?.fetches);

      let checked = 0;
      for (const fetch of fetches) {
        const reps = fetch.trace?.input?.body?.variables?.representations;
        if (!reps) continue;
        checked += 1;
        evidence.push(`${String(fetch.source_name).padEnd(10)} sent ${reps.length} representations`);
        if (reps.length !== 2) {
          problems.push(
            `${fetch.source_name} was sent ${reps.length} representations for 3 positions holding 2 distinct ids.\n` +
            'Three would mean the router had stopped deduplicating, which is a better\n' +
            'subgraph load story and a wrong chapter; fix the chapter, not this number.',
          );
        }
      }

      if (checked < 2) {
        problems.push(`expected an entity fetch to catalog and one to pricing, found ${checked} with representations`);
      }

      return { problems, evidence };
    },
  },
];

// ---------------------------------------------------------------------------
// The harness
// ---------------------------------------------------------------------------

function docker(args) {
  return execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

async function waitForHealth(port) {
  for (let i = 0; i < 120; i++) {
    try {
      const response = await fetch(`http://localhost:${port}/health`);
      if (response.ok) return true;
    } catch {
      // not listening yet
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`the router on ${port} never became healthy`);
}

// Everything one case needs: a scratch directory, a router that logs its own
// plan cache key, and the guarantee that every container is removed afterwards.
async function withContext(fn) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-planner-'));
  const containers = [];
  let port = PORT;

  const context = {
    // `cacheHeaders` swaps the access-log fields for the debug setting that
    // reports the same thing in response headers. Two mechanisms that share no
    // code, which is why one case uses both.
    async router({ cacheHeaders = false } = {}) {
      const id = `mosaic-planner-case-${containers.length}-${process.pid}`;
      const mine = port++;
      const execPath = join(dir, `supergraph-${containers.length}.json`);
      copyFileSync(SUPERGRAPH, execPath);

      const configPath = join(dir, `config-${containers.length}.yaml`);
      writeFileSync(configPath, [
        'version: "1"',
        'execution_config:',
        '  file:',
        '    path: /etc/mosaic/supergraph.json',
        'listen_addr: "0.0.0.0:3002"',
        // dev_mode is what makes the two query-plan headers work without a
        // signed request, which the tracing case needs. It also changes the log
        // format, which is why rows() looks for the JSON rather than parsing
        // the whole line.
        'dev_mode: true',
        'log_level: info',
        cacheHeaders ? CACHE_HEADERS : ACCESS_LOGS,
        '',
      ].join('\n'));

      docker(['run', '--detach', '--name', id, '--publish', `${mine}:3002`,
        '--add-host', 'host.docker.internal:host-gateway',
        '--volume', `${execPath}:/etc/mosaic/supergraph.json:ro`,
        '--volume', `${configPath}:/etc/mosaic-router/config.yaml:ro`,
        '--env', 'CONFIG_PATH=/etc/mosaic-router/config.yaml',
        IMAGE]);
      containers.push(id);
      await waitForHealth(mine);

      return {
        async askRaw(label, body, headers = {}) {
          const response = await fetch(`http://localhost:${mine}/graphql`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Mosaic-Case': label, ...headers },
            body: JSON.stringify(body),
          });
          const json = await response.json();
          if (json.errors) {
            throw new Error(`"${label}" came back with errors: ${JSON.stringify(json.errors).slice(0, 300)}`);
          }
          // The body is consumed here, so hand back both halves rather than
          // making a caller that wants headers send the request twice.
          return { json, headers: response.headers };
        },

        async ask(label, body, headers = {}) {
          const { json } = await this.askRaw(label, body, headers);
          return json;
        },

        // The access log, one object per request, in the order the router
        // wrote them. dev_mode prints a console prefix before the JSON payload,
        // so the line is cut at the first field rather than parsed whole.
        rows() {
          const run = spawnSync('docker', ['logs', id], { encoding: 'utf8' });
          const text = `${run.stdout ?? ''}${run.stderr ?? ''}`;
          const out = [];
          for (const line of text.split('\n')) {
            const start = line.indexOf('{"hostname"');
            if (start < 0) continue;
            try {
              const row = JSON.parse(line.slice(start));
              if (row.label) out.push(row);
            } catch {
              // a log line that is not an access-log record
            }
          }
          return out;
        },
      };
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
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(40)}${testCase.summary}`);
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
    console.log(`  FAILED   ${testCase.name.padEnd(40)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  }

  if (result.problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(40)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(40)}${testCase.summary}`);
    for (const problem of result.problems) console.log(`             ${problem}`);
  }

  if (args[0] === '--print') {
    for (const line of result.evidence) console.log(`             ${line}`);
  }
}

if (failures > 0) {
  console.log(`\n${failures} of ${selected.length} planner cases did not behave as chapter 17 says.`);
  process.exit(1);
}
