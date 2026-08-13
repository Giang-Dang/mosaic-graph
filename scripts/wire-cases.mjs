#!/usr/bin/env node
// Chapter 18: client-to-router HTTP behaviour, asserted from the wire.
//
// Every router is isolated from the compose stack on a fresh port. The graph is
// the committed execution config; the setting under test is the only thing the
// temporary config changes. These cases need the seven Mosaic subgraphs on
// 5101-5107, exactly as the existing router and planner cases do.

import { execFileSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const image = 'ghcr.io/wundergraph/cosmo/router:0.337.1';
const supergraph = join(root, 'federation', 'supergraph.json');
let nextPort = Number(process.env.MOSAIC_WIRE_CASE_PORT ?? 3302);
const query = '{ browseProducts(first: 1) { nodes { title price { amount } } } }';
const largeQuery = '{ products { id sku title description category reviews(first: 12) { nodes { id rating body createdAt author { id displayName } } } price { amount currency } shippingCost { amount currency } availableQuantity } }';

function docker(args) {
  return execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

async function waitForHealth(value) {
  for (let i = 0; i < 120; i += 1) {
    try {
      if ((await fetch(`http://localhost:${value}/health`)).ok) return;
    } catch { /* container is still starting */ }
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 250));
  }
  throw new Error(`router on ${value} never became healthy`);
}

async function withRouter(config, run) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-wire-'));
  const id = `mosaic-wire-case-${process.pid}`;
  const port = nextPort++;
  try {
    const execution = join(dir, 'supergraph.json');
    const settings = join(dir, 'config.yaml');
    copyFileSync(supergraph, execution);
    writeFileSync(settings, [
      'version: "1"',
      'execution_config:',
      '  file:',
      '    path: /etc/mosaic/supergraph.json',
      'listen_addr: "0.0.0.0:3002"',
      'dev_mode: true',
      config,
      '',
    ].join('\n'));
    docker(['run', '--detach', '--name', id, '--publish', `${port}:3002`,
      '--add-host', 'host.docker.internal:host-gateway',
      '--volume', `${execution}:/etc/mosaic/supergraph.json:ro`,
      '--volume', `${settings}:/etc/mosaic-router/config.yaml:ro`,
      '--env', 'CONFIG_PATH=/etc/mosaic-router/config.yaml', image]);
    await waitForHealth(port);
    return await run(`http://localhost:${port}/graphql`);
  } finally {
    try { docker(['rm', '--force', id]); } catch { /* startup failure */ }
    rmSync(dir, { recursive: true, force: true });
  }
}

async function post(url, body, headers = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  });
  return { status: response.status, headers: response.headers, text: await response.text() };
}

function sha256(text) {
  return crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)).then((bytes) =>
    [...new Uint8Array(bytes)].map((value) => value.toString(16).padStart(2, '0')).join(''));
}

const cases = [
  {
    name: 'apq-is-a-cache-not-a-safelist',
    summary: 'a hash misses, a full document registers it, and the hash then answers',
    async run() {
      return withRouter('automatic_persisted_queries:\n  enabled: true', async (url) => {
        const hash = await sha256(query);
        const extension = { persistedQuery: { version: 1, sha256Hash: hash } };
        const miss = await post(url, { extensions: extension });
        const register = await post(url, { query, extensions: extension });
        const hit = await post(url, { extensions: extension });
        const problems = [];
        if (!miss.text.includes('PersistedQueryNotFound')) problems.push(`hash-only miss was ${miss.text}`);
        if (!register.text.includes('Beech Cutting Board')) problems.push(`registration did not execute: ${register.text}`);
        if (!hit.text.includes('Beech Cutting Board')) problems.push(`hash-only hit did not execute: ${hit.text}`);
        return { problems, evidence: [`miss: ${miss.text}`, `hit: ${hit.text}`] };
      });
    },
  },
  {
    name: 'defer-is-multipart-only-when-enabled',
    summary: 'enabled @defer sends multipart parts rather than one JSON body',
    async run() {
      return withRouter('engine:\n  enable_defer: true', async (url) => {
        const deferred = '{ browseProducts(first: 1) { nodes { title ... @defer { price { amount } } } } }';
        const reply = await post(url, { query: deferred }, { Accept: 'multipart/mixed' });
        const type = reply.headers.get('content-type') ?? '';
        const problems = [];
        if (!type.startsWith('multipart/mixed')) problems.push(`content-type was ${type}`);
        if (!reply.text.includes('incremental') || !reply.text.includes('completed')) problems.push(`multipart body was ${reply.text}`);
        return { problems, evidence: [`content-type: ${type}`, reply.text] };
      });
    },
  },
  {
    name: 'compression-varies-the-representation',
    summary: 'gzip is negotiated and Vary names Accept-Encoding',
    async run() {
      return withRouter('', async (url) => {
        const reply = await post(url, { query: largeQuery }, { 'Accept-Encoding': 'gzip' });
        const encoding = reply.headers.get('content-encoding');
        const vary = reply.headers.get('vary') ?? '';
        const problems = [];
        if (encoding !== 'gzip') problems.push(`content-encoding was ${encoding}`);
        if (!vary.toLowerCase().includes('accept-encoding')) problems.push(`vary was ${vary}`);
        return { problems, evidence: [`content-encoding: ${encoding}`, `vary: ${vary}`] };
      });
    },
  },
];

const args = process.argv.slice(2);
if (args[0] === '--list') {
  for (const item of cases) console.log(`${item.name.padEnd(42)}${item.summary}`);
  process.exit(0);
}
const selected = args[0] === '--print' ? cases.filter((item) => item.name === args[1]) : cases;
if (args[0] === '--print' && selected.length === 0) {
  console.error(`No case named ${args[1]}. Try --list.`);
  process.exit(2);
}
let failures = 0;
for (const item of selected) {
  try {
    const result = await item.run();
    if (result.problems.length) {
      failures += 1;
      console.log(`  FAILED   ${item.name.padEnd(42)}${item.summary}`);
      for (const problem of result.problems) console.log(`             ${problem}`);
    } else {
      console.log(`  ok       ${item.name.padEnd(42)}${item.summary}`);
    }
    if (args[0] === '--print') for (const line of result.evidence) console.log(`             ${line}`);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${item.name.padEnd(42)}${item.summary}`);
    console.log(`             ${error.message}`);
  }
}
if (failures) process.exit(1);
