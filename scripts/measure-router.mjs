#!/usr/bin/env node
// What the router costs, and how long a hot reload takes. Chapter 10 prints
// both, so both have to be reproducible rather than remembered.
//
// This is not a gate. The numbers it produces are single-machine timings and
// asserting them anywhere would make the verification scripts fail on a busier
// laptop, which is the opposite of useful. What it gives is a way for a reader
// to get their own version of chapter 10's table, and a way for me to get the
// same one twice.
//
// Every row of the latency table is timed in one run, back to back, because
// the chapter 4 open item records the same code measuring 10 to 12 ms one
// afternoon and 19 to 20 ms another purely because the machine was busier.
// Numbers compared across runs say nothing; numbers compared inside one run do.
//
// Needs the six subgraphs on 5101 to 5106 and the router on 3002. Chapter 12
// made that a compose command rather than a list of terminals:
//
//   docker compose up -d --build mosaic-db mosaic-catalog mosaic-pricing \
//                                mosaic-inventory mosaic-accounts \
//                                mosaic-reviews mosaic-ordering mosaic-router
//
// Usage:
//   node scripts/measure-router.mjs             the latency table
//   node scripts/measure-router.mjs --reload    the hot-reload timing
//   node scripts/measure-router.mjs --all       both

import { execFileSync } from 'node:child_process';
import { copyFileSync, readFileSync, writeFileSync, mkdtempSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const CATALOG = process.env.MOSAIC_CATALOG_URL ?? 'http://localhost:5101/graphql';
const PRICING = process.env.MOSAIC_PRICING_URL ?? 'http://localhost:5102/graphql';
const INVENTORY = process.env.MOSAIC_INVENTORY_URL ?? 'http://localhost:5103/graphql';
const REVIEWS = process.env.MOSAIC_REVIEWS_URL ?? 'http://localhost:5105/graphql';
const ROUTER = process.env.MOSAIC_ROUTER_URL ?? 'http://localhost:3002/graphql';

// The six subgraphs and the port each answers on, for the reload timing's
// recompose. Kept in step with federation/mosaic.yaml by hand.
const SUBGRAPHS = [
  { name: 'catalog', port: 5101 },
  { name: 'pricing', port: 5102 },
  { name: 'inventory', port: 5103 },
  { name: 'accounts', port: 5104 },
  { name: 'reviews', port: 5105 },
  { name: 'ordering', port: 5106 },
];

const RUNS = Number(process.env.MOSAIC_MEASURE_RUNS ?? 60);
const WARMUP = Number(process.env.MOSAIC_MEASURE_WARMUP ?? 15);
const FIRST = 10;

async function post(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const json = await res.json();
  if (json.errors) {
    throw new Error(`${url} answered with errors: ${JSON.stringify(json.errors).slice(0, 300)}`);
  }
  return json;
}

function median(xs) {
  const sorted = [...xs].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
}

async function time(label, fn) {
  for (let i = 0; i < WARMUP; i += 1) await fn();
  const timings = [];
  for (let i = 0; i < RUNS; i += 1) {
    const started = process.hrtime.bigint();
    await fn();
    timings.push(Number(process.hrtime.bigint() - started) / 1e6);
  }
  const value = median(timings);
  console.log(`${label.padEnd(46)}${value.toFixed(1).padStart(7)}`);
  return value;
}

async function latencyTable() {
  // Fetched once, outside the timing, so that the hand-assembled row below
  // pays for two calls rather than three.
  const seed = await post(CATALOG, { query: `{ browseProducts(first: ${FIRST}) { nodes { id } } }` });
  const representations = seed.data.browseProducts.nodes.map((n) => ({ __typename: 'Product', id: n.id }));

  const catalogOnly = { query: `{ browseProducts(first: ${FIRST}) { nodes { title } } }` };
  const storefront = {
    query: `{ browseProducts(first: ${FIRST}) { nodes { title price { amount currency } availableQuantity averageRating } } }`,
  };
  // One _entities query per contributing subgraph, because there is one
  // subgraph per field now. This is where chapter 12 shows up in a timing: the
  // hand-assembled row went from two calls to four.
  const entitiesFor = (selection) => ({
    query:
      'query($representations: [_Any!]!) { _entities(representations: $representations) '
      + `{ ... on Product { ${selection} } } }`,
    variables: { representations },
  });
  const pricingEntities = entitiesFor('price { amount currency }');
  const inventoryEntities = entitiesFor('availableQuantity');
  const reviewsEntities = entitiesFor('averageRating');

  console.log(`${''.padEnd(46)} median`);
  const direct = await time('catalog direct, titles only', () => post(CATALOG, catalogOnly));
  const routed = await time('router, titles only (one fetch)', () => post(ROUTER, catalogOnly));
  await time(`pricing direct, _entities for ${FIRST} products`, () => post(PRICING, pricingEntities));
  const byHand = await time('the four calls by hand, in sequence', async () => {
    const first = await post(CATALOG, { query: `{ browseProducts(first: ${FIRST}) { nodes { id title } } }` });
    const reps = first.data.browseProducts.nodes.map((n) => ({ __typename: 'Product', id: n.id }));
    const variables = { representations: reps };
    await post(PRICING, { query: pricingEntities.query, variables });
    await post(INVENTORY, { query: inventoryEntities.query, variables });
    return post(REVIEWS, { query: reviewsEntities.query, variables });
  });
  const fourHop = await time('router, the storefront query (four fetches)', () => post(ROUTER, storefront));

  console.log('\nderived, in milliseconds:');
  console.log(`  router overhead on a single-subgraph query  ${(routed - direct).toFixed(1)}`);
  console.log(`  router overhead on the four-hop query       ${(fourHop - byHand).toFixed(1)}`);
  console.log('\nThe hand-assembled row is deliberately sequential and the router is not:');
  console.log('the three entity fetches do not depend on each other, so the planner runs');
  console.log('them in parallel and this comparison flatters the router. Chapter 12 says so.');
  console.log('\nSingle-machine numbers, and upper bounds: the router reaches the');
  console.log('subgraphs across Docker\'s bridge and back in through a published port,');
  console.log('while the direct rows are loopback on the host. Run it twice before');
  console.log('believing any difference smaller than the spread between the two runs.');
}

// How long the router takes to notice a recomposed graph, and whether anything
// fails while it does. Composes a second config that differs in one visible
// field, drops it over the committed one, polls until the change shows up, and
// puts the healthy config back.
async function reloadTiming() {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-reload-'));
  const supergraph = join(repoRoot, 'federation', 'supergraph.json');
  const backup = join(dir, 'supergraph.json');
  copyFileSync(supergraph, backup);

  try {
    const from = '  averageRating: Float\n';
    for (const subgraph of SUBGRAPHS) {
      const source = readFileSync(join(repoRoot, 'schema', `${subgraph.name}.graphql`), 'utf8')
        .replace(/\r\n/g, '\n');
      if (subgraph.name !== 'reviews') {
        writeFileSync(join(dir, `${subgraph.name}.graphql`), source);
        continue;
      }
      if (source.split(from).length - 1 !== 1) {
        throw new Error('the averageRating line did not match exactly once; fix this script rather than the schema');
      }
      writeFileSync(join(dir, 'reviews.graphql'), source.replace(from, ''));
    }
    writeFileSync(join(dir, 'graph.yaml'), [
      'version: 1',
      'subgraphs:',
      ...SUBGRAPHS.flatMap((s) => [
        `  - name: ${s.name}`,
        `    routing_url: http://localhost:${s.port}/graphql`,
        '    schema:',
        `      file: ${s.name}.graphql`,
      ]),
      '',
    ].join('\n'));

    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    const thin = join(dir, 'thin.json');
    execFileSync(process.execPath, [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', thin],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });

    const fields = async () => {
      const res = await fetch(ROUTER, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: '{ __type(name: "Product") { fields(includeDeprecated: true) { name } } }' }),
      });
      if (res.status !== 200) return `HTTP ${res.status}`;
      return (await res.json()).data.__type.fields.map((f) => f.name).join(',');
    };

    console.log('before the swap:', await fields());

    // wgc writes in place rather than replacing, which is what lets the
    // compose file bind-mount this single file and still see the change.
    const inodeBefore = statSync(supergraph).ino;
    const started = Date.now();
    copyFileSync(thin, supergraph);
    console.log('swapped the composed config over');

    let failures = 0;
    let noticedAfter = null;
    for (let i = 0; i < 150; i += 1) {
      const now = await fields();
      if (now.startsWith('HTTP')) failures += 1;
      if (!now.includes('averageRating')) {
        noticedAfter = Date.now() - started;
        break;
      }
      await new Promise((r) => setTimeout(r, 100));
    }

    console.log('after the swap :', await fields());
    console.log(noticedAfter === null
      ? 'the router never picked it up within 15 s; is execution_config.file.watch on?'
      : `picked up after ${noticedAfter} ms; requests that failed meanwhile: ${failures}`);
    console.log(`the mounted file kept its inode: ${statSync(supergraph).ino === inodeBefore}`);
  } finally {
    // Written rather than copied, on purpose. The router's watcher keys on the
    // file's modification time, and on Windows copyFileSync preserves the
    // source's, so restoring the healthy config by copy gives it a timestamp
    // older than the one already loaded and the router goes on serving the
    // thin graph. Writing the bytes stamps the file now, and the router
    // reloads within the watch interval. This cost me a confused run.
    writeFileSync(supergraph, readFileSync(backup));
    rmSync(dir, { recursive: true, force: true });
    console.log('\nthe committed config has been put back.');
    console.log('the router reloads it within the watch interval; give it a second.');
  }
}

const args = process.argv.slice(2);
if (args.includes('--reload')) {
  await reloadTiming();
} else if (args.includes('--all')) {
  await latencyTable();
  console.log('');
  await reloadTiming();
} else {
  await latencyTable();
}
