#!/usr/bin/env node
// Chapter 14's runtime half: one write, two subscriptions, both through the
// router.
//
// This script exists to close something the book has been carrying since
// chapter 5, which said in its own prose that `onReviewAdded` was the one thing
// nothing re-checked. Newman speaks request and response and a subscription is
// a connection that stays open, so the argument then was that a second harness
// for one field was not worth building. There are two fields now and they work
// in opposite ways, and the difference between them is the whole of chapter 14,
// so the harness is worth it.
//
// What it asserts, in order:
//
//   1. HotChocolate 16.6.0, offered both WebSocket subprotocols, answers with
//      the LEGACY one. This is a fact about the subgraph and it is why
//      federation/mosaic.yaml pins the modern one.
//   2. The router, offered both, answers with the MODERN one.
//   3. onReviewAdded, opened through the router over graphql-transport-ws,
//      delivers one event per accepted review, with `author.displayName` filled
//      in from a subgraph that is not the one holding the connection.
//   4. reviewPublished, opened through the router over SSE, delivers the same
//      review, from a NATS message whose whole body was a type name and a key,
//      declared by a subgraph that has no process.
//
// Usage:
//   node scripts/subscription-run.mjs --router http://localhost:3002/graphql \
//                                     --reviews http://localhost:5105/graphql \
//                                     --product <global id> --customer <global id>
//
// Both identifiers are global object identifiers as a client would write them,
// and the customer must not already have reviewed the product: a duplicate is
// refused by the domain and publishes nothing, which is correct behaviour and
// would look exactly like a broken subscription.

import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Chapter 15. The write half of this script needs a token now.
const mintScript = fileURLToPath(new URL('./mint-token.mjs', import.meta.url));

const args = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  args.set(process.argv[i].replace(/^--/, ''), process.argv[i + 1]);
}

// Both verification scripts hold the router and the subgraphs as origins and
// append the path at each call site, so half the callers of this script pass
// http://localhost:3002 and half pass http://localhost:3002/graphql. A bare
// origin is not a harmless difference here: it opens a WebSocket against the
// router's playground rather than its GraphQL endpoint, and the failure is a
// refused upgrade with no explanation on either side. Normalising costs one
// line and removes a whole class of confusing failure.
const withEndpoint = (url) => {
  const parsed = new URL(url);
  if (parsed.pathname === '' || parsed.pathname === '/') parsed.pathname = '/graphql';
  return parsed.toString().replace(/\/$/, '');
};

const routerUrl = withEndpoint(args.get('router') ?? 'http://localhost:3002/graphql');
const reviewsUrl = withEndpoint(args.get('reviews') ?? 'http://localhost:5105/graphql');
const productId = args.get('product');
// One or more customers, comma separated. Each one submits one review, so the
// count is how many events the held subscription has to deliver. Chapter 14
// prints what a graph does per event rather than per subscription, and one
// event cannot tell those two apart.
const customerIds = (args.get('customer') ?? '').split(',').filter(Boolean);
const timeoutMs = Number(args.get('timeout') ?? 20000);

if (!productId || customerIds.length === 0) {
  console.error('--product and --customer are required, both as global object identifiers.');
  console.error('--customer takes a comma-separated list; one review is submitted per customer.');
  process.exit(2);
}

const toWs = (url) => url.replace(/^http/, 'ws');

const problems = [];
const note = (ok, what, detail) => {
  console.log(`  ${ok ? 'ok      ' : 'FAILED  '} ${what}${detail ? ` (${detail})` : ''}`);
  if (!ok) problems.push(`${what}${detail ? `: ${detail}` : ''}`);
};

const deadline = (label, ms) =>
  new Promise((_, reject) => setTimeout(() => reject(new Error(`timed out waiting for ${label}`)), ms));

// ---------------------------------------------------------------------------
// 1 and 2. Who picks which subprotocol
// ---------------------------------------------------------------------------

/**
 * Open a WebSocket offering both subprotocols in the order the Cosmo Router
 * offers them, and report which one the server chose. The order matters and is
 * the point: a server free to prefer either has to decide, and the two servers
 * in this stack decide differently from the same list.
 */
function chosenSubprotocol(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(toWs(url), ['graphql-transport-ws', 'graphql-ws']);
    ws.addEventListener('open', () => {
      const chosen = ws.protocol;
      ws.close();
      resolve(chosen);
    });
    ws.addEventListener('error', () => reject(new Error(`could not open a WebSocket to ${url}`)));
  });
}

// ---------------------------------------------------------------------------
// 3. onReviewAdded, over graphql-transport-ws, through the router
// ---------------------------------------------------------------------------

function openWebSocketSubscription(url, query) {
  const events = [];
  let resolveFirst;
  const first = new Promise((r) => { resolveFirst = r; });

  const ws = new WebSocket(toWs(url), ['graphql-transport-ws']);
  const ready = new Promise((resolve, reject) => {
    ws.addEventListener('error', () => reject(new Error(`could not open a WebSocket to ${url}`)));
    ws.addEventListener('message', (e) => {
      const message = JSON.parse(e.data);
      if (message.type === 'connection_ack') {
        ws.send(JSON.stringify({ id: '1', type: 'subscribe', payload: { query } }));
        // The ack is not the subscription. The router has to plan the document
        // and open its own connection to the subgraph before the field is live,
        // and a publish that happens in that window is simply missed.
        setTimeout(resolve, 750);
      }
      if (message.type === 'next') { events.push(message.payload); resolveFirst(message.payload); }
      if (message.type === 'error') { events.push(message); resolveFirst(message); }
    });
    ws.addEventListener('open', () => ws.send(JSON.stringify({ type: 'connection_init', payload: {} })));
  });

  return { ready, first, events, close: () => { try { ws.close(); } catch { /* already gone */ } } };
}

// ---------------------------------------------------------------------------
// 4. reviewPublished, over server-sent events, through the router
// ---------------------------------------------------------------------------

function openSseSubscription(url, query) {
  const controller = new AbortController();
  const events = [];
  let resolveFirst;
  const first = new Promise((r) => { resolveFirst = r; });

  const ready = fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json', accept: 'text/event-stream' },
    body: JSON.stringify({ query }),
    signal: controller.signal,
  }).then(async (res) => {
    if (!res.ok) throw new Error(`the router answered ${res.status} to an SSE subscription`);
    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    (async () => {
      try {
        for (; ;) {
          const { done, value } = await reader.read();
          if (done) break;
          for (const line of decoder.decode(value, { stream: true }).split(/\r?\n/)) {
            if (!line.startsWith('data:')) continue;
            const payload = JSON.parse(line.slice(5).trim());
            events.push(payload);
            resolveFirst(payload);
          }
        }
      } catch { /* aborted, or the router hung up */ }
    })();
    // Same reason as above, and one more: the router has to reach NATS and
    // register a subject before a publish can match.
    await new Promise((r) => setTimeout(r, 750));
  });

  return { ready, first, events, close: () => controller.abort() };
}

// ---------------------------------------------------------------------------

async function main() {
  console.log('subscriptions through the router');

  const subgraphChose = await chosenSubprotocol(reviewsUrl);
  note(
    subgraphChose === 'graphql-ws',
    'the subgraph, offered both subprotocols, picks the legacy one',
    subgraphChose,
  );

  const routerChose = await chosenSubprotocol(routerUrl);
  note(
    routerChose === 'graphql-transport-ws',
    'the router, offered both subprotocols, picks the modern one',
    routerChose,
  );

  const held = openWebSocketSubscription(
    routerUrl,
    `subscription { onReviewAdded(productId: "${productId}") { id rating author { displayName } } }`,
  );
  const driven = openSseSubscription(
    routerUrl,
    `subscription { reviewPublished(productId: "${productId}") { id rating author { displayName } } }`,
  );

  try {
    await Promise.all([held.ready, driven.ready]);

    const submittedIds = [];
    for (const [index, customerId] of customerIds.entries()) {
      const mutation = `mutation { submitReview(input: { productId: "${productId}", `
        + `customerId: "${customerId}", rating: 4, body: "Write ${index + 1} of `
        + `${customerIds.length}, with two subscriptions open." }) `
        + '{ review { id } errors { __typename } } }';
      // Chapter 15. submitReview refuses a review signed in somebody else's
      // name, so the write needs a token whose subject is the customer it
      // names. Nothing about the subscriptions changed; only the write did.
      const token = execFileSync(
        process.execPath,
        [mintScript, '--customer', customerId],
        { encoding: 'utf8' },
      ).trim();

      const response = await fetch(routerUrl, {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
          accept: 'application/json',
          authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ query: mutation }),
      }).then((r) => r.json());

      const id = response?.data?.submitReview?.review?.id;
      if (!id) {
        note(false, `write ${index + 1} was accepted`,
          JSON.stringify(response?.data?.submitReview?.errors ?? response?.errors));
        return;
      }
      submittedIds.push(id);
      // Spaced, because what is being measured is one round trip per message
      // rather than the router's behaviour under a burst. Chapter 24 owns the
      // burst.
      await new Promise((r) => setTimeout(r, 400));
    }

    const submitted = submittedIds[0];
    note(true, `${submittedIds.length} mutation(s) went through the router and were accepted`, submitted);

    const heldEvent = await Promise.race([held.first, deadline('onReviewAdded', timeoutMs)]);
    const heldReview = heldEvent?.data?.onReviewAdded;
    note(
      heldReview?.id === submitted,
      'onReviewAdded delivered the review that was just written',
      heldReview?.id ?? JSON.stringify(heldEvent),
    );

    // One event per write, on one connection. This is the assertion behind the
    // count chapter 14 prints: the trigger happened once and the payload was
    // delivered as many times as the graph was written to.
    await new Promise((r) => setTimeout(r, 1500));
    const heldIds = held.events.map((e) => e?.data?.onReviewAdded?.id).filter(Boolean);
    note(
      heldIds.length === submittedIds.length
        && submittedIds.every((id) => heldIds.includes(id)),
      `the held subscription delivered one event per write (${submittedIds.length})`,
      `${heldIds.length} event(s)`,
    );
    // The subgraph holding the connection has a two-field stub for Customer
    // and no display name anywhere in it. A name here is the router having run
    // an entity fetch against Accounts for this one event.
    note(
      Boolean(heldReview?.author?.displayName),
      'the held subscription resolved author.displayName from another subgraph',
      heldReview?.author?.displayName ?? 'null',
    );

    const drivenEvent = await Promise.race([driven.first, deadline('reviewPublished', timeoutMs)]);
    const drivenReview = drivenEvent?.data?.reviewPublished;
    note(
      drivenReview?.id === submitted,
      'reviewPublished delivered the same review, from a broker message',
      drivenReview?.id ?? JSON.stringify(drivenEvent),
    );
    note(
      drivenReview?.rating === 4 && Boolean(drivenReview?.author?.displayName),
      'the event-driven subscription resolved a whole review from a type name and a key',
      `rating ${drivenReview?.rating}, author ${drivenReview?.author?.displayName ?? 'null'}`,
    );
  } finally {
    held.close();
    driven.close();
  }
}

main()
  .catch((error) => { problems.push(error.message); console.log(`  FAILED   ${error.message}`); })
  .then(() => {
    if (problems.length > 0) {
      console.error(
        `\n${problems.length} subscription check(s) did not behave as chapter 14 describes.\n`
        + 'Chapter 14 prints these two arrangements working from one write, so a failure\n'
        + 'here means either the graph changed or the chapter is now wrong. If only the\n'
        + 'event-driven half failed, check that mosaic-nats is up and that the router\n'
        + 'config still carries the events.providers.nats block.',
      );
      process.exit(1);
    }
    process.exit(0);
  });
