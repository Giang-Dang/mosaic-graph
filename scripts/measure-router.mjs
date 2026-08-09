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
// Needs both subgraphs on 5100 and 5101 and the router on 3002:
//
//   docker compose up -d mosaic-db
//   dotnet run --project src/Mosaic.Catalog
//   dotnet run --project src/Mosaic.Api
//   docker compose up -d mosaic-router
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
const MOSAIC = process.env.MOSAIC_API_URL ?? 'http://localhost:5100/graphql';
const ROUTER = process.env.MOSAIC_ROUTER_URL ?? 'http://localhost:3002/graphql';

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
  const entities = {
    query: 'query($representations: [_Any!]!) { _entities(representations: $representations) { ... on Product { price { amount currency } availableQuantity averageRating } } }',
    variables: { representations },
  };

  console.log(`${''.padEnd(46)} median`);
  const direct = await time('catalog direct, titles only', () => post(CATALOG, catalogOnly));
  const routed = await time('router, titles only (one fetch)', () => post(ROUTER, catalogOnly));
  await time(`mosaic direct, _entities for ${FIRST} products`, () => post(MOSAIC, entities));
  const byHand = await time('the two calls by hand, in sequence', async () => {
    const first = await post(CATALOG, { query: `{ browseProducts(first: ${FIRST}) { nodes { id title } } }` });
    const reps = first.data.browseProducts.nodes.map((n) => ({ __typename: 'Product', id: n.id }));
    return post(MOSAIC, { query: entities.query, variables: { representations: reps } });
  });
  const twoHop = await time('router, the storefront query (two fetches)', () => post(ROUTER, storefront));

  console.log('\nderived, in milliseconds:');
  console.log(`  router overhead on a single-subgraph query  ${(routed - direct).toFixed(1)}`);
  console.log(`  router overhead on the two-hop query        ${(twoHop - byHand).toFixed(1)}`);
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
    const mosaic = readFileSync(join(repoRoot, 'schema', 'mosaic.graphql'), 'utf8');
    if (mosaic.split(from).length - 1 !== 1) {
      throw new Error('the averageRating line did not match exactly once; fix this script rather than the schema');
    }
    writeFileSync(join(dir, 'mosaic.graphql'), mosaic.replace(from, ''));
    copyFileSync(join(repoRoot, 'schema', 'catalog.graphql'), join(dir, 'catalog.graphql'));
    writeFileSync(join(dir, 'graph.yaml'), [
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
