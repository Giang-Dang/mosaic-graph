#!/usr/bin/env node
// Chapter 11's entity-resolution findings, each reproduced on purpose.
//
// The chapter prints what these produce, so the gate asserts them. Three kinds
// of case live here and they are deliberately in one file, because they are
// three views of the same subject: what a subgraph is asked when the router
// resolves an entity, and what it is allowed to answer.
//
//   composition  the real schema pair with one literal edit, composed with
//                wgc, asserting what the composer said. Chapter 9's
//                composition-cases.mjs does the same and the edit discipline
//                is copied from it: an edit must match exactly once.
//   subgraph     _entities asked directly, with no router in the way, of
//                Mosaic on 5100 and of the sample on 5205.
//   router       the sample graph behind a real Cosmo Router container, where
//                @provides is the only thing that can be measured.
//
// Unlike chapter 10's router-cases.mjs this script starts the two sample
// subgraphs itself, so a reader can run it with one command. What it does not
// start is Mosaic: two of the cases need Mosaic answering on 5100, and a
// script that started a database-backed service would be a second copy of
// verify.ps1.
//
// Usage:
//   node scripts/entity-cases.mjs                run every case
//   node scripts/entity-cases.mjs --list         name the cases and exit
//   node scripts/entity-cases.mjs --print <name> run one case and print the
//                                                evidence, which is how the
//                                                chapter's listings were made

import { execFileSync, spawn, spawnSync } from 'node:child_process';
import {
  mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync, copyFileSync, existsSync
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const CATALOG_SCHEMA = join(repoRoot, 'schema', 'catalog.graphql');
const MOSAIC_SCHEMA = join(repoRoot, 'schema', 'mosaic.graphql');
const SAMPLE_DIR = join(repoRoot, 'samples', 'entity-resolution');

// The image docker-compose.yml pins. Kept in step with it by hand; a mismatch
// would mean the gate checks a router nobody runs.
const IMAGE = 'ghcr.io/wundergraph/cosmo/router:0.337.1';

const MOSAIC_URL = process.env.MOSAIC_URL ?? 'http://localhost:5100/graphql';

// Away from the ports docker-compose.yml and the launch profiles use, so the
// whole composed stack can be up while this runs.
const WIDGETS_PORT = Number(process.env.MOSAIC_WIDGETS_PORT ?? 5305);
const CRATES_PORT = Number(process.env.MOSAIC_CRATES_PORT ?? 5306);
const ROUTER_PORT = Number(process.env.MOSAIC_ENTITIES_ROUTER_PORT ?? 3103);

const WIDGETS_URL = `http://localhost:${WIDGETS_PORT}/graphql`;
const CRATES_URL = `http://localhost:${CRATES_PORT}/graphql`;

const SUBGRAPHS = [
  {
    name: 'catalog',
    port: WIDGETS_PORT,
    project: join(SAMPLE_DIR, 'Mosaic.Sample.Entities.Catalog'),
    assembly: 'Mosaic.Sample.Entities.Catalog',
    schema: join(repoRoot, 'schema', 'samples', 'entities-catalog.graphql')
  },
  {
    name: 'crates',
    port: CRATES_PORT,
    project: join(SAMPLE_DIR, 'Mosaic.Sample.Entities.Crates'),
    assembly: 'Mosaic.Sample.Entities.Crates',
    schema: join(repoRoot, 'schema', 'samples', 'entities-crates.graphql')
  }
];

// ---------------------------------------------------------------------------
// Talking to things
// ---------------------------------------------------------------------------

async function ask(url, query, variables, headers = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json', ...headers },
    body: JSON.stringify({ query, variables })
  });
  const text = await response.text();
  try {
    return { status: response.status, body: JSON.parse(text) };
  } catch {
    // A body that is not JSON is worth seeing rather than guessing at: it is
    // usually a router that answered before it had a graph.
    throw new Error(`${url} answered ${response.status} with a body that is not JSON:\n${text.slice(0, 400)}`);
  }
}

async function plan(url, query) {
  const { body } = await ask(url, query, undefined, {
    'X-WG-Include-Query-Plan': 'true',
    'X-WG-Skip-Loader': 'true'
  });
  return body.extensions?.queryPlan;
}

/// Every fetch node in a plan, flattened, which is the number that costs
/// something. Chapter 10 makes the point that the dependencies array is not
/// this number.
function fetches(node, found = []) {
  if (!node) return found;
  if (node.fetch) found.push(node.fetch);
  for (const child of node.children ?? []) fetches(child, found);
  return found;
}

/// The lines the sample subgraph's reference resolvers wrote, for one run,
/// with the log cleared first so nothing from an earlier case leaks in.
async function resolutionLog(run) {
  await ask(WIDGETS_URL, '{ resolutionLog(clear: true) }');
  const result = await run();
  const { body } = await ask(WIDGETS_URL, '{ resolutionLog }');
  return { result, log: body.data.resolutionLog };
}

const entitiesQuery = (typeName) => `query($representations: [_Any!]!) {
  _entities(representations: $representations) {
    __typename
    ... on ${typeName} { id name }
  }
}`;

// ---------------------------------------------------------------------------
// Composing an edited pair
// ---------------------------------------------------------------------------

/// One literal replacement that has to match exactly once. A case whose edit
/// silently matches nothing composes the healthy pair and asserts something
/// else entirely, which is worse than failing.
function editedOnce(source, from, to) {
  const hits = source.split(from).length - 1;
  if (hits !== 1) {
    throw new Error(
      `the edit matched ${hits} times, expected exactly 1.\nThe text it looks for is:\n${from}\n`
      + 'The committed schema has changed under this case. Fix the case rather than the schema.'
    );
  }
  return source.replace(from, to);
}

/// Compose catalog plus an edited mosaic, and hand back whatever the composer
/// said along with the config if it wrote one.
function compose(dir, label, edit, extraArgs = []) {
  // Normalised before the edit, because .gitattributes keeps these files at LF
  // and `dotnet run -- schema export` writes CRLF. A re-export on Windows that
  // nobody normalised turns every edit here into "matched 0 times", which is a
  // confusing way to be told about a line ending.
  const mosaic = edit(readFileSync(MOSAIC_SCHEMA, 'utf8').replace(/\r\n/g, '\n'));
  const graphDir = join(dir, label);
  mkdirSync(graphDir, { recursive: true });
  copyFileSync(CATALOG_SCHEMA, join(graphDir, 'catalog.graphql'));
  writeFileSync(join(graphDir, 'mosaic.graphql'), mosaic);
  writeFileSync(join(graphDir, 'graph.yaml'), [
    'version: 1',
    'subgraphs:',
    '  - name: catalog',
    '    routing_url: http://localhost:5101/graphql',
    '    schema:',
    '      file: catalog.graphql',
    '  - name: mosaic',
    '    routing_url: http://localhost:5100/graphql',
    '    schema:',
    '      file: mosaic.graphql',
    ''
  ].join('\n'));

  const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
  const out = join(graphDir, 'supergraph.json');
  let output;
  let code = 0;
  try {
    output = execFileSync(
      process.execPath,
      [wgc, 'router', 'compose', '-i', join(graphDir, 'graph.yaml'), '-o', out, ...extraArgs],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }
    );
  } catch (error) {
    code = error.status ?? 1;
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  }

  return {
    code,
    // Colour codes, and the box the composer draws around its table. The
    // message is what the chapter prints and what this asserts; the drawing is
    // not part of it.
    text: output
      .replace(/\[[0-9;]*m/g, '')
      .split('\n')
      .map((line) => line.replace(/^[|│]\s?/, '').replace(/\s*[|│]\s*$/, '').trimEnd())
      // The box the composer draws its table in, and the column heading
      // inside it. Neither is part of what it said.
      .filter((line) => !/^[─-╿+|-]*$/.test(line.trim()))
      .filter((line) => line.trim() !== 'ERROR_MESSAGE')
      .join('\n')
      .trim(),
    clientSchema: existsSync(out)
      ? JSON.parse(readFileSync(out, 'utf8')).engineConfig.graphqlSchema
      : null
  };
}

/// Whatever the composer said that was not "I wrote the file". A clean
/// composition is not a silent command, and a case that asserts silence would
/// fail on the success line.
function complaints(text) {
  return text
    .split('\n')
    .filter((line) => line.trim().length > 0)
    .filter((line) => !line.startsWith('Router execution config successfully written'))
    .join('\n');
}

/// One field of the composed, client-facing Product type.
function clientField(schema, name) {
  const type = schema?.match(/\ntype Product implements[^{]*\{[\s\S]*?\n\}/);
  const line = type?.[0].split('\n').find((l) => l.trim().startsWith(`${name}:`));
  return line?.trim() ?? null;
}

// ---------------------------------------------------------------------------
// The cases
// ---------------------------------------------------------------------------

const CASES = [
  {
    name: 'external-unused',
    needs: [],
    summary: 'an @external field nothing requires is an error, not a warning',
    async run(ctx) {
      // The flag chapter 9 could not exercise, because Mosaic had no
      // @external field then. It is exercised here and it changes nothing:
      // this is an error, and --suppress-warnings suppresses warnings.
      const plain = compose(ctx.dir, 'external-unused', (s) =>
        editedOnce(s, 'shippingCost: Money! @requires(fields: "category")', 'shippingCost: Money!'));
      const suppressed = compose(ctx.dir, 'external-unused-suppressed', (s) =>
        editedOnce(s, 'shippingCost: Money! @requires(fields: "category")', 'shippingCost: Money!'),
      ['--suppress-warnings']);

      const problems = [];
      if (plain.code === 0) problems.push('the pair composed, so nothing objected to the orphaned @external');
      if (!plain.text.includes('is invalidly declared "@external"')) {
        problems.push('the composer did not name the @external declaration as the fault');
      }
      if (suppressed.code !== plain.code) {
        problems.push('--suppress-warnings changed the outcome, so this is a warning after all');
      }
      return { problems, evidence: plain.text.split('\n') };
    }
  },
  {
    name: 'requires-not-external',
    needs: [],
    summary: '@requires naming a field this subgraph also owns is an error',
    async run(ctx) {
      const result = compose(ctx.dir, 'requires-not-external', (s) =>
        editedOnce(s, 'category: ProductCategory! @external', 'category: ProductCategory!'));

      const problems = [];
      if (result.code === 0) problems.push('the pair composed with a @requires on a field the subgraph provides');
      if (!result.text.includes('are declared "@external"')) {
        problems.push('the composer did not say the required field has to be @external');
      }
      return { problems, evidence: result.text.split('\n') };
    }
  },
  {
    name: 'external-nullability-wins',
    needs: [],
    summary: 'a nullable @external copy makes the field nullable for every client',
    async run(ctx) {
      const healthy = compose(ctx.dir, 'external-healthy', (s) => s);
      const relaxed = compose(ctx.dir, 'external-nullable', (s) =>
        editedOnce(s, 'category: ProductCategory! @external', 'category: ProductCategory @external'));

      const before = clientField(healthy.clientSchema, 'category');
      const after = clientField(relaxed.clientSchema, 'category');

      const problems = [];
      const said = complaints(relaxed.text);
      if (relaxed.code !== 0) problems.push('the edited pair did not compose, so there is nothing to compare');
      if (said.length > 0) {
        problems.push(`the composer had something to say about it: ${said.split('\n')[0]}`);
      }
      if (before !== 'category: ProductCategory!') {
        problems.push(`the committed pair composes category as ${before}, not ProductCategory!`);
      }
      if (after !== 'category: ProductCategory') {
        problems.push(`the relaxed pair composes category as ${after}, so the merge did not take the nullable side`);
      }
      return {
        problems,
        evidence: [
          `catalog owns it and declares it     ProductCategory!`,
          `mosaic declares its @external copy  ProductCategory`,
          `the client-facing schema says       ${after?.replace('category: ', '') ?? '(nothing)'}`,
          `the composer said                   ${said.length === 0 ? 'nothing' : said}`
        ]
      };
    }
  },
  {
    name: 'requires-missing-on-the-wire',
    needs: ['mosaic'],
    summary: 'a representation with no required field costs that one entity and no other',
    async run() {
      const keys = (await ask('http://localhost:5101/graphql',
        '{ browseProducts(first: 3) { nodes { id category } } }')).body.data.browseProducts.nodes;

      const query = `query($representations: [_Any!]!) {
  _entities(representations: $representations) {
    ... on Product { __typename shippingCost { amount currency } }
  }
}`;
      const { body } = await ask(MOSAIC_URL, query, {
        representations: keys.map((k, i) => i === 1
          ? { __typename: 'Product', id: k.id }
          : { __typename: 'Product', id: k.id, category: k.category })
      });

      const entities = body.data?._entities ?? [];
      const problems = [];
      if (entities.length !== 3) problems.push(`three representations went out and ${entities.length} came back`);
      if (entities[1] !== null) problems.push('the representation with no category resolved anyway');
      if (!entities[0]?.shippingCost || !entities[2]?.shippingCost) {
        problems.push('a neighbour of the failing representation lost its answer too');
      }
      const path = (body.errors ?? []).map((e) => (e.path ?? []).join('.'));
      if (!path.includes('_entities.1.shippingCost')) {
        problems.push(`the error is at ${JSON.stringify(path)}, not at _entities.1.shippingCost`);
      }
      return {
        problems,
        evidence: [
          `entities: ${entities.map((e) => (e === null ? 'null' : e.shippingCost.amount)).join(', ')}`,
          ...(body.errors ?? []).map((e) => `error: ${e.message} at ${(e.path ?? []).join('.')}`)
        ]
      };
    }
  },
  {
    name: 'batch-is-one-lookup',
    needs: ['sample'],
    summary: 'every representation enters the resolver before any of them leaves it',
    async run() {
      const { result, log } = await resolutionLog(() => ask(WIDGETS_URL, entitiesQuery('Widget'), {
        representations: ['w1', 'w2', 'w3', 'w4'].map((id) => ({ __typename: 'Widget', id }))
      }));

      const problems = [];
      const names = result.body.data?._entities?.map((e) => e?.name);
      if (names?.join('|') !== 'Hex bolt|Wing nut|Split washer|Coach screw') {
        problems.push(`the answers were ${JSON.stringify(names)}, which is not the four widgets in order`);
      }
      const lookups = log.filter((l) => l.startsWith('lookup'));
      if (lookups.length !== 1) problems.push(`${lookups.length} lookups for one batch of four`);
      if (!lookups[0]?.startsWith('lookup 4 keys')) problems.push(`the one lookup was "${lookups[0]}"`);
      const firstLeave = log.findIndex((l) => l.startsWith('leave'));
      const lastEnter = log.map((l) => l.startsWith('enter')).lastIndexOf(true);
      if (lastEnter > firstLeave) problems.push('a resolver returned before the last one had been entered');
      if (!log.some((l) => l === 'entries: 4 on 1 thread(s)')) {
        problems.push(`the four calls were not made on one thread: ${log[log.length - 1]}`);
      }
      return { problems, evidence: log };
    }
  },
  {
    name: 'naive-is-four-lookups',
    needs: ['sample'],
    summary: 'the same entity without a DataLoader resolves one representation at a time',
    async run() {
      const { result, log } = await resolutionLog(() => ask(WIDGETS_URL, entitiesQuery('Gadget'), {
        representations: ['w1', 'w2', 'w3', 'w4'].map((id) => ({ __typename: 'Gadget', id }))
      }));

      const problems = [];
      if (result.body.data?._entities?.length !== 4) problems.push('the naive resolver did not answer four entities');
      const lookups = log.filter((l) => l.startsWith('lookup'));
      if (lookups.length !== 4) problems.push(`${lookups.length} lookups, expected one per representation`);
      // Strictly sequential: enter, lookup, leave, and only then the next
      // enter. That is the finding, and it is what the batched case is not.
      const shape = log.filter((l) => !l.startsWith('entries:')).map((l) => l.split(' ')[0]).join(',');
      if (shape !== 'enter,lookup,leave,enter,lookup,leave,enter,lookup,leave,enter,lookup,leave') {
        problems.push(`the calls interleaved rather than running in sequence: ${shape}`);
      }
      return { problems, evidence: log };
    }
  },
  {
    name: 'duplicate-keys-collapse',
    needs: ['sample'],
    summary: 'four representations of one entity are four calls and one lookup',
    async run() {
      const { result, log } = await resolutionLog(() => ask(WIDGETS_URL, entitiesQuery('Widget'), {
        representations: Array.from({ length: 4 }, () => ({ __typename: 'Widget', id: 'w1' }))
      }));

      const problems = [];
      if (result.body.data?._entities?.length !== 4) problems.push('four representations did not produce four entities');
      const enters = log.filter((l) => l.startsWith('enter')).length;
      if (enters !== 4) problems.push(`${enters} resolver calls for four representations`);
      const lookups = log.filter((l) => l.startsWith('lookup'));
      if (lookups.join('') !== 'lookup 1 key: w1') {
        problems.push(`the store was asked ${JSON.stringify(lookups)}, not once for one key`);
      }
      return { problems, evidence: log };
    }
  },
  {
    name: 'representation-overwrites-owned-field',
    needs: ['sample'],
    summary: 'a representation can set a field the subgraph owns and never marked @external',
    async run() {
      const { body } = await ask(WIDGETS_URL, entitiesQuery('Widget'), {
        representations: [
          { __typename: 'Widget', id: 'w1' },
          { __typename: 'Widget', id: 'w2', name: 'NOT WHAT THE STORE SAYS' },
          { __typename: 'Widget', id: 'w3', nonsense: 42 }
        ]
      });

      const entities = body.data?._entities ?? [];
      const problems = [];
      if (entities[0]?.name !== 'Hex bolt') problems.push('the untouched representation did not answer the stored name');
      if (entities[1]?.name !== 'NOT WHAT THE STORE SAYS') {
        problems.push(`the representation did not win: the field answered "${entities[1]?.name}"`);
      }
      if (entities[2]?.name !== 'Split washer') {
        problems.push('a field the type does not have changed the answer');
      }
      return {
        problems,
        evidence: entities.map((e, i) => `representation ${i} -> ${e?.name}`)
      };
    }
  },
  {
    name: 'provides-skips-the-hop',
    needs: ['sample', 'router'],
    summary: '@provides turns a two-fetch plan into a one-fetch plan',
    async run(ctx) {
      const provided = fetches(await plan(ctx.routerUrl, '{ crates { label widget { id name } } }'));
      const plain = fetches(await plan(ctx.routerUrl, '{ crates { label widgetByKey { id name } } }'));

      const problems = [];
      if (provided.length !== 1) problems.push(`the @provides path planned ${provided.length} fetches, expected 1`);
      if (plain.length !== 2) problems.push(`the plain path planned ${plain.length} fetches, expected 2`);
      if (provided[0]?.subgraphName !== 'crates') problems.push('the one fetch did not go to crates');
      if (plain[1]?.subgraphName !== 'catalog') problems.push('the second fetch did not go to catalog');
      return {
        problems,
        evidence: [
          `widget      @provides(fields: "name")   ${provided.length} fetch:  ${provided.map((f) => f.subgraphName).join(' then ')}`,
          `widgetByKey no promise                  ${plain.length} fetches: ${plain.map((f) => f.subgraphName).join(' then ')}`,
          `the second fetch's path                 ${plain[1]?.path ?? '(none)'}`
        ]
      };
    }
  },
  {
    name: 'provides-serves-what-it-stored',
    needs: ['sample', 'router'],
    summary: 'the provided value comes from the promising subgraph, right or wrong',
    async run(ctx) {
      const provided = (await ask(ctx.routerUrl, '{ crates { label widget { id name } } }'))
        .body.data.crates;
      const authority = (await ask(WIDGETS_URL, '{ widgets { id name } }')).body.data.widgets;

      const byId = Object.fromEntries(authority.map((w) => [w.id, w.name]));
      const disagreements = provided
        .filter((c) => byId[c.widget.id] !== undefined && byId[c.widget.id] !== c.widget.name);

      const problems = [];
      if (disagreements.length !== 1) {
        problems.push(`${disagreements.length} widgets disagree with catalog, expected exactly the one packed wrong`);
      }
      if (disagreements[0]?.widget.id !== 'w2') problems.push('the disagreement is not the one this sample builds in');
      return {
        problems,
        evidence: provided.map((c) =>
          `${c.label.padEnd(12)}${c.widget.id}  router says ${JSON.stringify(c.widget.name).padEnd(18)}`
          + `catalog says ${JSON.stringify(byId[c.widget.id] ?? null)}`)
      };
    }
  },
  {
    name: 'dangling-key-nulls-the-answer',
    needs: ['sample', 'router'],
    summary: 'one unresolvable key takes the whole response with it',
    async run(ctx) {
      const { body } = await ask(ctx.routerUrl, '{ crates { label widgetByKey { id name } } }');

      const problems = [];
      if (body.data !== null) problems.push('the response survived a key no subgraph could resolve');
      const error = (body.errors ?? [])[0];
      if (!error?.message?.includes('Cannot return null for non-nullable field')) {
        problems.push(`the error was ${JSON.stringify(error?.message)}, not a non-null violation`);
      }
      if ((error?.path ?? []).join('.') !== 'crates.3.widgetByKey.name') {
        problems.push(`the error is at ${(error?.path ?? []).join('.')}, not at the fourth crate's widget name`);
      }
      return {
        problems,
        evidence: [
          `data: ${JSON.stringify(body.data)}`,
          `error: ${error?.message}`,
          `path: ${(error?.path ?? []).join('.')}`
        ]
      };
    }
  }
];

// ---------------------------------------------------------------------------
// Starting what the cases need
// ---------------------------------------------------------------------------

function docker(args) {
  const run = spawnSync('docker', args, { encoding: 'utf8' });
  if (run.status !== 0) {
    throw new Error(`docker ${args.slice(0, 2).join(' ')} failed:\n${run.stderr || run.stdout}`);
  }
  return (run.stdout ?? '').trim();
}

async function waitFor(probe, what, seconds = 60) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    try {
      if (await probe()) return;
    } catch {
      // Not listening yet.
    }
    await new Promise((r) => setTimeout(r, 400));
  }
  throw new Error(`${what} did not answer within ${seconds} seconds.`);
}

function stop(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === 'win32') {
    // dotnet run leaves the application in a child process of its own, so the
    // tree has to go rather than the process.
    spawnSync('taskkill', ['/pid', String(child.pid), '/t', '/f'], { stdio: 'ignore' });
  } else {
    child.kill('SIGTERM');
  }
}

async function startSubgraphs(started) {
  execFileSync('dotnet', ['build', join(repoRoot, 'Mosaic.slnx'), '-c', 'Release', '-v', 'q', '--nologo'],
    { cwd: repoRoot, stdio: ['ignore', 'pipe', 'pipe'] });

  for (const subgraph of SUBGRAPHS) {
    const child = spawn('dotnet',
      ['run', '--project', subgraph.project, '-c', 'Release', '--no-build', '--no-launch-profile'],
      {
        cwd: repoRoot,
        stdio: ['ignore', 'pipe', 'pipe'],
        env: { ...process.env, ASPNETCORE_URLS: `http://localhost:${subgraph.port}` }
      });
    started.push(child);
  }

  for (const subgraph of SUBGRAPHS) {
    await waitFor(async () => {
      const response = await fetch(`http://localhost:${subgraph.port}/graphql`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: '{"query":"{ __typename }"}'
      });
      return response.status === 200;
    }, `the ${subgraph.name} sample subgraph on ${subgraph.port}`);
  }

  // The composer reads the committed files, so a subgraph that has drifted
  // from its own schema is a finding rather than a detail.
  for (const subgraph of SUBGRAPHS) {
    const { body } = await ask(`http://localhost:${subgraph.port}/graphql`, '{ _service { sdl } }');
    const published = body.data._service.sdl.replace(/\r\n/g, '\n').trim();
    const committed = readFileSync(subgraph.schema, 'utf8').replace(/\r\n/g, '\n').trim();
    if (published !== committed) {
      throw new Error(
        `the ${subgraph.name} sample subgraph does not publish ${subgraph.schema}.\n`
        + 'Re-export it with `dotnet run --project <project> -- schema export --output <file>` '
        + 'and recompose, or find out what moved the contract.'
      );
    }
  }
}

/// Two runs of this script at once would fight over the fixed ports below, and
/// the way that shows up is a router answering /health from the other run and
/// 404 to everything else. Checked rather than diagnosed.
async function refuseIfTaken(port, what) {
  try {
    await fetch(`http://localhost:${port}/`, { signal: AbortSignal.timeout(1500) });
  } catch {
    return;
  }
  throw new Error(
    `Something is already listening on ${port}, where ${what} goes.\n`
    + 'An earlier run of this script, or a second one alongside it. Stop it, or set '
    + 'MOSAIC_WIDGETS_PORT, MOSAIC_CRATES_PORT and MOSAIC_ENTITIES_ROUTER_PORT.'
  );
}

async function startRouter(dir, started) {
  const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
  const config = join(dir, 'entities-supergraph.json');

  // Composed here rather than read from samples/entity-resolution, because the
  // committed one routes to the ports the launch profiles use and these
  // subgraphs are on ports of their own.
  const graph = join(dir, 'entities-graph.yaml');
  writeFileSync(graph, [
    'version: 1',
    'subgraphs:',
    '  - name: catalog',
    `    routing_url: http://host.docker.internal:${WIDGETS_PORT}/graphql`,
    '    schema:',
    `      file: ${join(repoRoot, 'schema', 'samples', 'entities-catalog.graphql').replace(/\\/g, '/')}`,
    '  - name: crates',
    `    routing_url: http://host.docker.internal:${CRATES_PORT}/graphql`,
    '    schema:',
    `      file: ${join(repoRoot, 'schema', 'samples', 'entities-crates.graphql').replace(/\\/g, '/')}`,
    ''
  ].join('\n'));

  execFileSync(process.execPath, [wgc, 'router', 'compose', '-i', graph, '-o', config],
    { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });

  const id = docker(['run', '--detach', '--rm',
    '--publish', `${ROUTER_PORT}:3002`,
    '--add-host', 'host.docker.internal:host-gateway',
    '--volume', `${config.replace(/\\/g, '/')}:/etc/entities/supergraph.json:ro`,
    '--env', 'EXECUTION_CONFIG_FILE_PATH=/etc/entities/supergraph.json',
    '--env', 'DEV_MODE=true',
    '--env', 'LISTEN_ADDR=0.0.0.0:3002',
    IMAGE]);
  started.push(id);

  await waitFor(async () => (await fetch(`http://localhost:${ROUTER_PORT}/health`)).status === 200,
    `the sample router on ${ROUTER_PORT}`);

  return `http://localhost:${ROUTER_PORT}/graphql`;
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(38)}${testCase.summary}`);
  process.exit(0);
}

const selected = args[0] === '--print' ? CASES.filter((c) => c.name === args[1]) : CASES;

if (args[0] === '--print' && selected.length === 0) {
  console.error(`No case named "${args[1]}". Try --list.`);
  process.exit(2);
}

const needs = new Set(selected.flatMap((c) => c.needs));
const dir = mkdtempSync(join(tmpdir(), 'entity-cases-'));
const children = [];
const containers = [];
let routerUrl = null;
let failures = 0;

try {
  if (needs.has('mosaic')) {
    await waitFor(async () => (await fetch(MOSAIC_URL, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: '{"query":"{ __typename }"}'
    })).status === 200, `Mosaic on ${MOSAIC_URL}`, 5);
  }
  if (needs.has('sample')) {
    await refuseIfTaken(WIDGETS_PORT, 'the catalog sample subgraph');
    await refuseIfTaken(CRATES_PORT, 'the crates sample subgraph');
    await startSubgraphs(children);
  }
  if (needs.has('router')) {
    await refuseIfTaken(ROUTER_PORT, 'the sample router');
    routerUrl = await startRouter(dir, containers);
  }

  for (const testCase of selected) {
    let result;
    try {
      result = await testCase.run({ dir, routerUrl });
    } catch (error) {
      failures += 1;
      console.log(`  FAILED   ${testCase.name.padEnd(38)}${testCase.summary}`);
      for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
      continue;
    }

    if (result.problems.length === 0) {
      console.log(`  ok       ${testCase.name.padEnd(38)}${testCase.summary}`);
    } else {
      failures += 1;
      console.log(`  FAILED   ${testCase.name.padEnd(38)}${testCase.summary}`);
      for (const problem of result.problems) console.log(`             ${problem}`);
    }

    if (args[0] === '--print') {
      for (const line of result.evidence) console.log(`             ${line}`);
    }
  }
} finally {
  for (const child of children) stop(child);
  for (const id of containers) {
    try {
      docker(['rm', '--force', id]);
    } catch {
      // Already gone, or docker is. Either way the results stand.
    }
  }
  rmSync(dir, { recursive: true, force: true });
}

if (failures > 0) {
  console.log(`\n${failures} of ${selected.length} entity cases did not behave as chapter 11 says.`);
  process.exit(1);
}
