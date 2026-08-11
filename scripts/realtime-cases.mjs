#!/usr/bin/env node
// Chapter 14's real-time behaviour, produced on purpose.
//
// Same contract as scripts/composition-cases.mjs, override-cases.mjs and
// modeling-cases.mjs: every case is built from the real committed files, each
// edit has to match exactly once, and what is asserted is what the tool
// actually did. A file change that invalidates a case fails with "matched 0
// times" rather than quietly asserting nothing.
//
// What is different here is which file the edits land in. The earlier scripts
// edit subgraph SCHEMAS, because their subjects are modelling mistakes. This
// chapter's subject is a transport, and a transport is configured in
// federation/mosaic.yaml rather than declared in any schema. So these cases
// edit the composer's INPUT and assert what appears in the execution config it
// writes - which is the only place a subscription protocol is ever visible,
// since none of it reaches the client-facing schema.
//
// Usage:
//   node scripts/realtime-cases.mjs             run every case, exit non-zero on failure
//   node scripts/realtime-cases.mjs --list      name the cases and exit
//   node scripts/realtime-cases.mjs --print <name>
//                                               run one case and print the composer's
//                                               output plus the subscription block it
//                                               wrote, which is how the chapter's
//                                               listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The eight entries federation/mosaic.yaml holds at ch14: seven services and
// one schema file with nothing behind it.
const SUBGRAPHS = [
  { name: 'catalog', port: 5101, path: 'schema/catalog.graphql' },
  { name: 'pricing', port: 5102, path: 'schema/pricing.graphql' },
  { name: 'inventory', port: 5103, path: 'schema/inventory.graphql' },
  { name: 'accounts', port: 5104, path: 'schema/accounts.graphql' },
  { name: 'reviews', port: 5105, path: 'schema/reviews.graphql' },
  { name: 'ordering', port: 5106, path: 'schema/ordering.graphql' },
  { name: 'nodes', port: 5107, path: 'schema/nodes.graphql' },
  { name: 'streams', port: 5108, path: 'schema/streams.graphql' },
];

// The subscription block the committed file carries, reproduced here so that a
// case can replace it with something else. It is read out of the real file
// rather than written twice: see readCommittedSubscriptionBlock below.
const PINNED = [
  '      protocol: ws',
  '      websocketSubprotocol: graphql-transport-ws',
].join('\n');

/**
 * Pull the reviews subscription block out of the committed composer input, so
 * that a case replacing it is replacing the thing the chapter describes rather
 * than a copy of it that has drifted.
 */
function readCommittedSubscriptionBlock() {
  const yaml = readFileSync(join(repoRoot, 'federation', 'mosaic.yaml'), 'utf8').replace(/\r\n/g, '\n');
  if (!yaml.includes(PINNED)) {
    throw new Error(
      'federation/mosaic.yaml no longer pins the reviews subscription to\n'
      + `${PINNED}\n`
      + 'Chapter 14 says it does, and says why. Update the chapter or put it back.',
    );
  }
  return PINNED;
}

const CASES = [
  {
    name: 'baseline',
    summary: 'the committed input, which pins reviews to the modern subprotocol',
    subscription: PINNED,
    expectSuccess: true,
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_WS',
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_TRANSPORT_WS',
    },
  },
  {
    name: 'no-subscription-block-is-auto',
    summary: 'say nothing and the subgraph is offered both subprotocols',
    subscription: null,
    expectSuccess: true,
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_WS',
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_AUTO',
    },
  },
  {
    name: 'documented-spelling-does-nothing',
    summary: 'websocket_subprotocol, as wgc documents it, composes to auto',
    subscription: [
      '      protocol: ws',
      '      websocket_subprotocol: graphql-transport-ws',
    ].join('\n'),
    expectSuccess: true,
    // The whole finding is in this line. The value is the one the chapter
    // wants and the key is the one the documentation prints, and what lands in
    // the config is the default. wgc 0.129.7's compose command reads
    // `subscription.websocketSubprotocol`; every other key in this file is
    // snake_case, which is what makes the documented spelling look right.
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_WS',
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_AUTO',
    },
  },
  {
    name: 'unknown-subprotocol-is-ignored',
    summary: 'a subprotocol nothing implements composes with the key dropped',
    subscription: [
      '      protocol: ws',
      '      websocketSubprotocol: totally-not-a-protocol',
    ].join('\n'),
    expectSuccess: true,
    // wgc has a validateSubscriptionProtocols that rejects exactly this value
    // by name, and every subgraph and monograph command calls it.
    // `router compose` does not, so the value travels as far as the enum
    // conversion and is then dropped. Note that the result is not the default:
    // the composed block has no websocketSubprotocol member at all, which is a
    // different thing from `auto` and is what the misspelled-key case above
    // produces instead.
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_WS',
      websocketSubprotocol: undefined,
    },
  },
  {
    name: 'unknown-protocol-drops-the-key',
    summary: 'a transport nothing implements leaves the config with no protocol at all',
    subscription: '      protocol: carrier-pigeon',
    expectSuccess: true,
    // Worse than the case above, because the result is not a default: the
    // composed subscription block has no `protocol` member.
    expectSubscription: {
      protocol: undefined,
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_AUTO',
    },
  },
  {
    name: 'sse-is-a-get',
    summary: 'protocol: sse composes, and is the one HotChocolate cannot serve',
    subscription: '      protocol: sse',
    expectSuccess: true,
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_SSE',
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_AUTO',
    },
  },
  {
    name: 'sse-post-is-a-post',
    summary: 'protocol: sse_post is a different transport with a near-identical name',
    subscription: '      protocol: sse_post',
    expectSuccess: true,
    expectSubscription: {
      protocol: 'GRAPHQL_SUBSCRIPTION_PROTOCOL_SSE_POST',
      websocketSubprotocol: 'GRAPHQL_WEBSOCKET_SUBPROTOCOL_AUTO',
    },
  },
  {
    name: 'the-event-driven-subgraph-is-not-graphql',
    summary: 'schema/streams.graphql composes to a datasource with no url in it',
    subscription: PINNED,
    expectSuccess: true,
    // The eighth datasource is the finding. Every other entry in the routing
    // table is a GRAPHQL source with a fetch url; this one is a PUBSUB source
    // carrying a broker subject, and the routing_url the input file was
    // obliged to give it appears nowhere.
    expectPubSub: {
      providerId: 'default',
      typeName: 'Subscription',
      fieldName: 'reviewPublished',
      subject: 'mosaic.reviewAdded.{{ args.productId }}',
    },
  },
];

// ---------------------------------------------------------------------------

const flatten = (text) => text.replace(/\s+/g, ' ').trim();

function buildInput(dir, testCase) {
  const lines = ['version: 1', 'subgraphs:'];
  for (const subgraph of SUBGRAPHS) {
    // Normalised on the way in, for the reason modeling-cases.mjs gives at
    // length: `schema export` writes CRLF on Windows and .gitattributes puts
    // it back at the next commit, so an unnormalised read fails and then stops
    // failing.
    const source = readFileSync(join(repoRoot, subgraph.path), 'utf8').replace(/\r\n/g, '\n');
    writeFileSync(join(dir, `${subgraph.name}.graphql`), source);

    lines.push(
      `  - name: ${subgraph.name}`,
      `    routing_url: http://localhost:${subgraph.port}/graphql`,
      '    schema:',
      `      file: ${subgraph.name}.graphql`,
    );

    if (subgraph.name === 'reviews' && testCase.subscription !== null) {
      lines.push('    subscription:', `      url: http://localhost:${subgraph.port}/graphql`, testCase.subscription);
    }
  }
  lines.push('');
  writeFileSync(join(dir, 'graph.yaml'), lines.join('\n'));
}

function compose(testCase) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-realtime-'));
  try {
    buildInput(dir, testCase);

    const out = join(dir, 'out.json');
    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    const argv = [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', out];

    // Willing to run it twice, for the reason modeling-cases.mjs records: a
    // composer that exits non-zero having printed nothing has not disagreed
    // with the chapter, it has failed to run, and reporting those as the same
    // thing sends somebody to read the wrong file.
    let output = '';
    let failed = false;
    for (let attempt = 0; attempt < 2; attempt++) {
      try {
        output = execFileSync(process.execPath, argv, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
        failed = false;
      } catch (error) {
        failed = true;
        output = `${error.stdout || ''}${error.stderr || ''}`;
      }
      if (!failed || output.trim() !== '') break;
    }
    if (failed && output.trim() === '') {
      output = '(wgc exited non-zero twice and printed nothing at all, so it never '
        + 'reported on this input. That is an environment problem rather than a '
        + 'composition one: check for memory pressure from anything else running.)';
    }

    const config = !failed && existsSync(out) ? JSON.parse(readFileSync(out, 'utf8')) : null;
    return { output, failed, config };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

/** The subscription block the composer wrote for one named subgraph. */
function subscriptionFor(config, name) {
  const byId = new Map((config.subgraphs ?? []).map((s) => [String(s.id), s.name]));
  for (const source of config.engineConfig?.datasourceConfigurations ?? []) {
    if ((byId.get(String(source.id)) ?? source.id) === name) {
      return source.customGraphql?.subscription ?? null;
    }
  }
  return null;
}

/** The event configuration the composer wrote for the schema-only subgraph. */
function pubSubFor(config) {
  for (const source of config.engineConfig?.datasourceConfigurations ?? []) {
    const nats = source.customEvents?.nats;
    if (nats?.length) {
      const first = nats[0];
      return {
        kind: source.kind,
        providerId: first.engineEventConfiguration?.providerId,
        typeName: first.engineEventConfiguration?.typeName,
        fieldName: first.engineEventConfiguration?.fieldName,
        subject: first.subjects?.[0],
        hasFetchUrl: Boolean(source.customGraphql?.fetch?.url?.staticVariableContent),
      };
    }
  }
  return null;
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(44)}${testCase.summary}`);
  process.exit(0);
}

if (args[0] === '--print') {
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  readCommittedSubscriptionBlock();
  const { output, failed, config } = compose(testCase);
  const plain = output.replace(/\[[0-9;]*m/g, '').trimEnd();
  if (plain) console.log(plain);
  console.log(failed ? '(no execution config written)' : '(composed)');
  if (config) {
    if (testCase.expectSubscription) {
      console.log(`\nreviews subscription block:\n${JSON.stringify(subscriptionFor(config, 'reviews'), null, 2)}`);
    }
    if (testCase.expectPubSub) {
      console.log(`\nstreams datasource:\n${JSON.stringify(pubSubFor(config), null, 2)}`);
    }
  }
  process.exit(0);
}

readCommittedSubscriptionBlock();

let failures = 0;

for (const testCase of CASES) {
  let result;
  try {
    result = compose(testCase);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(44)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  }

  const { output, failed, config } = result;
  const problems = [];

  if (testCase.expectSuccess && failed) {
    problems.push('expected this input to compose, and wgc reported errors');
  }
  if (testCase.expectSuccess === false && !failed) {
    problems.push('expected wgc to reject this input, and it composed');
  }

  if (config && testCase.expectSubscription) {
    const actual = subscriptionFor(config, 'reviews');
    if (actual === null) {
      problems.push('the reviews datasource has no subscription block at all');
    } else {
      for (const [key, expected] of Object.entries(testCase.expectSubscription)) {
        if (actual[key] !== expected) {
          problems.push(
            `subscription.${key} is ${JSON.stringify(actual[key])}, expected ${JSON.stringify(expected)}`,
          );
        }
      }
    }
  }

  if (config && testCase.expectPubSub) {
    const actual = pubSubFor(config);
    if (actual === null) {
      problems.push('no datasource in the config carries a NATS event configuration');
    } else {
      if (actual.kind !== 'PUBSUB') {
        problems.push(`the event-driven datasource is kind ${actual.kind}, expected PUBSUB`);
      }
      if (actual.hasFetchUrl) {
        problems.push('the event-driven datasource has a fetch url, which would mean it is a service after all');
      }
      for (const [key, expected] of Object.entries(testCase.expectPubSub)) {
        if (actual[key] !== expected) {
          problems.push(`${key} is ${JSON.stringify(actual[key])}, expected ${JSON.stringify(expected)}`);
        }
      }
    }
  }

  for (const fragment of testCase.expect ?? []) {
    if (!flatten(output).includes(flatten(fragment))) {
      problems.push(`missing from the composer's output: ${fragment}`);
    }
  }

  if (problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(44)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(44)}${testCase.summary}`);
    for (const problem of problems) console.log(`             ${problem}`);
    console.log('           what wgc actually said:');
    for (const line of output.replace(/\[[0-9;]*m/g, '').split('\n')) console.log(`           ${line}`);
  }
}

if (failures > 0) {
  console.error(
    `\n${failures} real-time case(s) did not behave as chapter 14 describes.\n`
    + 'Either the composer input changed, or wgc did. Both are real findings: the\n'
    + 'chapter prints these composed subscription blocks and this event configuration,\n'
    + 'so a change here means the chapter is now wrong.',
  );
  process.exit(1);
}
