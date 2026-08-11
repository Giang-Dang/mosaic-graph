#!/usr/bin/env node
// Chapter 15's runtime half: who the graph answers, and who it refuses.
//
// The composition half lives in scripts/auth-cases.mjs and asserts what the
// composer wrote. This asserts what the running graph does with it, which is a
// different question in three places and spectacularly different in one: a
// field the router guards perfectly over HTTP is a field the router does not
// guard at all inside a subscription, and only a running system says so.
//
// Newman cannot carry the last two checks for the reason chapter 14 gave about
// subscriptions, and cannot carry the first eight comfortably either, because
// every one of them needs a freshly minted token with a chosen subject, scope
// set and lifetime. postman/mosaic-auth carries the half a request and a
// response can carry.
//
// Usage:
//   node scripts/auth-run.mjs --router http://localhost:3002/graphql \
//                             --accounts http://localhost:5104/graphql \
//                             --ordering http://localhost:5106/graphql
//
// Everything else it needs - a customer, an order, a product, somebody who has
// not reviewed it - it finds by asking the graph, because hard-coding a seeded
// identifier here would break the moment the seed changed and would break in a
// way that looked like an authorization failure.

import { execFileSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const mintScript = join(repoRoot, 'scripts', 'mint-token.mjs');

const args = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  args.set(process.argv[i].replace(/^--/, ''), process.argv[i + 1]);
}

const withEndpoint = (url) => {
  const parsed = new URL(url);
  if (parsed.pathname === '' || parsed.pathname === '/') parsed.pathname = '/graphql';
  return parsed.toString().replace(/\/$/, '');
};

const routerUrl = withEndpoint(args.get('router') ?? 'http://localhost:3002/graphql');
const accountsUrl = withEndpoint(args.get('accounts') ?? 'http://localhost:5104/graphql');
const orderingUrl = withEndpoint(args.get('ordering') ?? 'http://localhost:5106/graphql');

const checks = [];
const record = (name, ok, detail) => {
  checks.push({ name, ok, detail });
  console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}`);
  if (!ok && detail) console.log(`          ${detail}`);
};

const mint = (mintArgs) =>
  execFileSync(process.execPath, [mintScript, ...mintArgs], { encoding: 'utf8' }).trim();

async function post(url, query, token, variables) {
  const headers = { 'content-type': 'application/json' };
  if (token) headers.authorization = `Bearer ${token}`;
  const res = await fetch(url, {
    method: 'POST',
    headers,
    body: JSON.stringify(variables ? { query, variables } : { query }),
  });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* a 401 from the router is JSON, but be safe */ }
  return { status: res.status, text, json };
}

const errorMessages = (payload) => {
  const out = [];
  for (const error of payload?.errors ?? []) {
    out.push(error.message);
    for (const nested of error.extensions?.errors ?? []) out.push(nested.message);
  }
  return out;
};

const mentions = (payload, needle) => errorMessages(payload).some((m) => m.includes(needle));

// ---------------------------------------------------------------------------
// What the graph is holding today.
// ---------------------------------------------------------------------------

async function discover() {
  // Anonymous, because finding the way in must not depend on the thing being
  // tested. This doubles as the first check.
  const storefront = await post(
    routerUrl,
    '{ products { id title reviews(first: 50) { nodes { author { id displayName } } } } }',
  );

  const products = storefront.json?.data?.products;
  if (!Array.isArray(products) || products.length === 0) {
    throw new Error(`the storefront query answered nothing an anonymous client could use: ${storefront.text.slice(0, 300)}`);
  }

  const everyone = new Map();
  for (const product of products) {
    for (const node of product.reviews.nodes) {
      if (node.author) everyone.set(node.author.id, node.author.displayName);
    }
  }
  if (everyone.size < 2) throw new Error('fewer than two review authors in the seed data');

  // Somebody with an order. Mosaic has no root field listing customers and no
  // root field listing orders, so the only route is to ask each author for
  // their own - which now needs a token per candidate, one of the small ways
  // authorization makes a test harness longer.
  let owner = null;
  let orderId = null;
  for (const [customerId] of everyone) {
    const token = mint(['--customer', customerId]);
    const answer = await post(
      routerUrl,
      'query($id: ID!) { ordersByCustomer(customerId: $id) { id } }',
      token,
      { id: customerId },
    );
    const orders = answer.json?.data?.ordersByCustomer;
    if (Array.isArray(orders) && orders.length > 0) {
      owner = customerId;
      orderId = orders[0].id;
      break;
    }
  }
  if (!owner) throw new Error('no seeded customer has an order');

  const stranger = [...everyone.keys()].find((id) => id !== owner);

  // A product the stranger has not reviewed, so the subscription checks can
  // provoke an event. A duplicate is refused by the domain, publishes nothing,
  // and looks exactly like a broken subscription.
  // Two of them, because the subscription checks provoke two events on the same
  // product and the second write has to come from somebody who has not written
  // the first.
  let product = null;
  let writer = null;
  let writerTwo = null;
  for (const candidate of products) {
    const taken = new Set(candidate.reviews.nodes.map((n) => n.author?.id).filter(Boolean));
    const free = [...everyone.keys()].filter((id) => !taken.has(id));
    if (free.length >= 2) {
      product = candidate.id;
      [writer, writerTwo] = free;
      break;
    }
  }
  if (!product) {
    throw new Error('no seeded product has two customers who have not reviewed it');
  }

  return { storefront, everyone, owner, orderId, stranger, product, writer, writerTwo };
}

// ---------------------------------------------------------------------------
// The subscription, authenticated the only way a browser can authenticate one.
// ---------------------------------------------------------------------------

function subscribe(url, query, token) {
  // The token is offered in connection_init the way Cosmo documents, and on
  // the configuration Mosaic ships nothing reads it: the block that would is
  // commented out in router/config.yaml, because turning it on answers 401 to
  // every anonymous HTTP request. So a subscription here is anonymous however
  // it is opened, which is the point the last two checks make.
  const socket = new WebSocket(url.replace(/^http/, 'ws'), ['graphql-transport-ws']);
  const events = [];
  let acked = null;

  const ready = new Promise((resolvePromise, rejectPromise) => {
    acked = resolvePromise;
    socket.addEventListener('error', () => rejectPromise(new Error(`could not open a WebSocket to ${url}`)));
  });

  socket.addEventListener('open', () => {
    // The token goes here and not in a header, because a WebSocket client
    // cannot set one. The router reads it out of this payload, which is what
    // websocket.authentication.from_initial_payload in router/config.yaml
    // turns on, and it insists on the Bearer prefix.
    socket.send(JSON.stringify({ type: 'connection_init', payload: { Authorization: `Bearer ${token}` } }));
  });

  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data);
    if (message.type === 'connection_ack') {
      socket.send(JSON.stringify({ id: '1', type: 'subscribe', payload: { query } }));
      acked();
    } else if (message.type === 'next' || message.type === 'error') {
      events.push(message.payload);
    }
  });

  return { socket, events, ready, close: () => socket.close() };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function submitReview(customerId, productId, body) {
  return post(
    routerUrl,
    'mutation($input: SubmitReviewInput!) { submitReview(input: $input) { review { id } errors { __typename ... on Error { message } } } }',
    mint(['--customer', customerId]),
    { input: { productId, customerId, rating: 4, body } },
  );
}

// ---------------------------------------------------------------------------

async function main() {
  console.log(`auth-run - router ${routerUrl}`);
  console.log('');

  const found = await discover();
  const { owner, orderId, stranger, product, writer, writerTwo } = found;

  record(
    'the storefront still answers an anonymous client',
    Array.isArray(found.storefront.json?.data?.products) && found.storefront.json.data.products.length > 0,
    'a graph that grew authorization and stopped serving the public is the wrong graph',
  );

  // -- the router's own enforcement, from the composed directives ------------

  const ownerToken = mint(['--customer', owner]);

  const anonEmail = await post(
    routerUrl,
    'query($id: ID!) { customerById(id: $id) { displayName } }',
    null,
    { id: owner },
  );
  record(
    'the router refuses an @requiresScopes field to an anonymous caller',
    mentions(anonEmail.json, 'Unauthorized to load field') && mentions(anonEmail.json, 'not authenticated'),
    anonEmail.text.slice(0, 240),
  );

  const scopeless = mint(['--customer', owner, '--scopes', 'reviews:write']);
  const missingScope = await post(
    routerUrl,
    'query($id: ID!) { customerById(id: $id) { displayName } }',
    scopeless,
    { id: owner },
  );
  const missing = missingScope.json?.extensions?.authorization?.missingScopes?.[0];
  record(
    'a token without the scope is refused, and the router names the scope it wanted',
    mentions(missingScope.json, 'missing required scopes')
      && missing?.required?.[0]?.[0] === 'customers:read'
      && (missingScope.json?.extensions?.authorization?.actualScopes ?? []).includes('reviews:write'),
    missingScope.text.slice(0, 300),
  );

  const withScope = await post(
    routerUrl,
    'query($id: ID!) { customerById(id: $id) { displayName email } }',
    ownerToken,
    { id: owner },
  );
  record(
    'the same field answers a token that holds the scope',
    typeof withScope.json?.data?.customerById?.email === 'string',
    withScope.text.slice(0, 240),
  );

  // -- the subgraph's own enforcement, which the router knows nothing about --

  const direct = await post(accountsUrl, 'query($id: ID!) { customerById(id: $id) { displayName } }', null, { id: owner });
  record(
    'Accounts refuses the same field to a caller who skipped the router',
    mentions(direct.json, 'The current user is not authorized to access this resource'),
    direct.text.slice(0, 240),
  );

  const directWithToken = await post(accountsUrl, 'query($id: ID!) { customerById(id: $id) { email } }', ownerToken, { id: owner });
  record(
    'and answers the same caller once they present the token',
    typeof directWithToken.json?.data?.customerById?.email === 'string',
    directWithToken.text.slice(0, 240),
  );

  // -- ownership, which no directive can state ------------------------------

  const strangerToken = mint(['--customer', stranger]);
  const someoneElsesOrders = await post(
    routerUrl,
    'query($id: ID!) { ordersByCustomer(customerId: $id) { id } }',
    strangerToken,
    { id: owner },
  );
  record(
    'a scoped token cannot read somebody else\'s order history',
    mentions(someoneElsesOrders.json, 'You may only read your own orders'),
    someoneElsesOrders.text.slice(0, 240),
  );

  // -- the same order, reached the other way --------------------------------

  const nodeQuery = 'query($id: ID!) { node(id: $id) { __typename ... on Order { id total { amount } } } }';

  const nodeAsOwner = await post(routerUrl, nodeQuery, ownerToken, { id: orderId });
  record(
    'node() hands an order to the customer who placed it',
    nodeAsOwner.json?.data?.node?.id === orderId,
    nodeAsOwner.text.slice(0, 240),
  );

  const nodeAsStranger = await post(routerUrl, nodeQuery, strangerToken, { id: orderId });
  record(
    'node() refuses the same order to anybody else',
    nodeAsStranger.json?.data?.node === null,
    `the reference resolver on Order is the only thing standing here: ${nodeAsStranger.text.slice(0, 240)}`,
  );

  const nodeAnonymous = await post(routerUrl, nodeQuery, null, { id: orderId });
  record(
    'node() refuses an order to an anonymous caller before any subgraph is asked',
    nodeAnonymous.json?.data?.node === null,
    `Mosaic.Nodes decodes the type out of the identifier and stops: ${nodeAnonymous.text.slice(0, 240)}`,
  );

  // An order that does not exist, in the same shape. The two answers have to be
  // indistinguishable, or a client can enumerate identifiers and learn which
  // ones are real from the difference.
  const rawOrder = Buffer.from(orderId, 'base64');
  rawOrder[rawOrder.length - 1] = rawOrder[rawOrder.length - 1] ^ 0xff;
  const missingOrder = Buffer.from(rawOrder).toString('base64');
  const nodeMissing = await post(routerUrl, nodeQuery, strangerToken, { id: missingOrder });
  record(
    'a forbidden order and an order that does not exist answer identically',
    JSON.stringify(nodeMissing.json) === JSON.stringify(nodeAsStranger.json),
    `forbidden: ${JSON.stringify(nodeAsStranger.json)}\n          missing:   ${JSON.stringify(nodeMissing.json)}`,
  );

  // -- lifetime -------------------------------------------------------------

  const shortLived = mint(['--customer', owner, '--ttl', '1']);
  await sleep(2000);
  const expired = await post(routerUrl, '{ __typename }', shortLived);
  record(
    'an expired token is refused outright, with a 401 and no data',
    expired.status === 401,
    `status ${expired.status}: ${expired.text.slice(0, 200)}`,
  );

  // -- the subscription -----------------------------------------------------

  // Two subscriptions on one product, differing in one field. The first selects
  // only what the graph serves anybody; the second adds the one field that
  // carries @authenticated. Both offer the same valid token in the initial
  // payload, and on this configuration neither of them is authenticated,
  // because a browser cannot put a token anywhere the router will read without
  // the block that closes the storefront.
  const publicSub = subscribe(
    routerUrl,
    `subscription { onReviewAdded(productId: "${product}") { id author { displayName } } }`,
    ownerToken,
  );
  await publicSub.ready;
  await sleep(1200);
  await submitReview(writer, product, 'auth-run, public selection');
  await sleep(2500);
  publicSub.close();

  const publicEvent = publicSub.events[0];
  record(
    'a subscription delivers, and resolves a field from another subgraph per event',
    typeof publicEvent?.data?.onReviewAdded?.author?.displayName === 'string',
    JSON.stringify(publicEvent ?? null).slice(0, 300),
  );

  // The finding. Same connection shape, same token, one more field.
  const guardedSub = subscribe(
    routerUrl,
    `subscription { onReviewAdded(productId: "${product}") { id author { displayName email } } }`,
    ownerToken,
  );
  await guardedSub.ready;
  await sleep(1200);
  await submitReview(writerTwo, product, 'auth-run, guarded selection');
  await sleep(2500);
  guardedSub.close();

  const guardedEvent = guardedSub.events[0];
  const guardedText = JSON.stringify(guardedEvent ?? null);

  record(
    'a guarded field inside a subscription payload does not reach an anonymous subscriber',
    guardedEvent?.data?.onReviewAdded?.author == null
      && guardedText.includes('not authorized to access this resource'),
    guardedText.slice(0, 300),
  );

  // The finding, and the reason Customer.email carries two attributes rather
  // than one. Over HTTP the router refuses an @authenticated field itself,
  // before any subgraph is called - the second check in this run asserts
  // exactly that, with the message "Unauthorized to load field". Inside a
  // subscription payload it does not: the entity fetch goes out, Accounts
  // answers with its own refusal, and the only reason nothing leaked is that
  // the subgraph was told the rule too.
  record(
    'and the refusal came from Accounts rather than from the router',
    guardedText.includes('"serviceName":"accounts"')
      && guardedText.includes('AUTH_NOT_AUTHENTICATED')
      && !guardedText.includes('Unauthorized to load field'),
    'Over HTTP the router refuses this field itself. Inside a subscription it did not, so a graph '
    + 'relying on @authenticated alone would have served this. If this check starts failing because '
    + 'the router\'s own refusal has appeared, Cosmo has closed the gap and chapter 15 needs '
    + 'rewriting rather than the assertion being flipped.\n          '
    + guardedText.slice(0, 300),
  );

  console.log('');
  const failed = checks.filter((c) => !c.ok);
  if (failed.length > 0) {
    console.log(`${failed.length} of ${checks.length} authorization checks failed.`);
    process.exit(1);
  }
  console.log(`all ${checks.length} authorization checks passed.`);
}

main().catch((error) => {
  console.error(`auth-run could not complete: ${error.message}`);
  process.exit(1);
});
