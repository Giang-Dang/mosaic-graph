#!/usr/bin/env node
// Chapter 15. Mosaic's stand-in for an identity provider.
//
// Everything about authorization in this graph needs a signed token, and
// standing up an OAuth server to get one would spend a chapter on a subject
// this book does not cover. So: an HS256 token, signed with the same secret the
// six services and the router are given, minted here.
//
// What this deliberately is not is a security example. A symmetric key shared
// between every party means every party can mint tokens for every other party,
// which is exactly what a real deployment uses asymmetric keys and a JWKS
// endpoint to avoid. The router's configuration takes either - the block in
// router/config.yaml has the url form commented beside the secret form - and
// nothing else in this repository changes between them. That is the point worth
// carrying: which of the two you run is a deployment decision, and no resolver,
// schema or composition step can tell.
//
// Usage:
//   node scripts/mint-token.mjs --customer <global id>
//   node scripts/mint-token.mjs --customer <global id> --scopes "orders:read reviews:write"
//   node scripts/mint-token.mjs --customer <global id> --ttl 10
//   node scripts/mint-token.mjs --secret
//
// --secret prints a usable signing key and nothing else, which is what
// docker-compose.yml and both verification scripts pass in MOSAIC_JWT_SECRET.

import { createHmac } from 'node:crypto';

// The default is a constant rather than a random value on purpose: every
// service, the router and this script have to agree, and a reader who starts
// one service by hand should get a token the router accepts. A deployment sets
// MOSAIC_JWT_SECRET to something nobody has read in a book.
const DEFAULT_SECRET = 'mosaic-development-signing-key-not-for-anything-real';

const ISSUER = 'mosaic';
const AUDIENCE = 'mosaic-graph';

// The key id, in the JWT's header rather than its payload. The router will not
// START without being told to expect one: the symmetric branch of its
// authentication.jwt.jwks schema is a oneOf that requires header_key_id
// alongside the secret and the algorithm, and leaving it out is a hard startup
// failure naming the exact path.
//
// What it then does with it is the surprise, and it was measured rather than
// assumed: a token carrying no kid at all is accepted anyway. The config key is
// mandatory and the header it names is not. So --no-kid below demonstrates
// something smaller than it looks - it is a valid token by every party's
// reckoning, and it is here because "the config demanded this and then did not
// check it" is worth being able to reproduce.
const KEY_ID = 'mosaic-dev';

// The three scopes this graph knows about, which is the same list as
// Mosaic.ServiceDefaults.Security.MosaicTokens.Scopes. Two copies, no compiler
// between them - the same class of agreement chapter 14 met with a NATS subject
// and chapter 8 met with a key format.
const ALL_SCOPES = ['orders:read', 'customers:read', 'reviews:write'];

function base64url(input) {
  return Buffer.from(input).toString('base64')
    .replace(/=/g, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
}

function sign(payload, secret, keyId = KEY_ID) {
  const header = keyId === null
    ? { alg: 'HS256', typ: 'JWT' }
    : { alg: 'HS256', typ: 'JWT', kid: keyId };
  const signingInput =
    `${base64url(JSON.stringify(header))}.${base64url(JSON.stringify(payload))}`;
  const signature = createHmac('sha256', secret)
    .update(signingInput)
    .digest('base64')
    .replace(/=/g, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
  return `${signingInput}.${signature}`;
}

function parseArgs(argv) {
  const args = { scopes: ALL_SCOPES.join(' '), ttl: 3600 };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--secret') {
      args.printSecret = true;
    } else if (arg === '--help' || arg === '-h') {
      args.help = true;
    } else if (arg === '--customer') {
      args.customer = argv[++i];
    } else if (arg === '--scopes') {
      args.scopes = argv[++i];
    } else if (arg === '--ttl') {
      args.ttl = Number(argv[++i]);
    } else if (arg === '--no-kid') {
      // A token with no kid header. Both ends accept it; see KEY_ID above.
      args.noKeyId = true;
    } else if (arg === '--anonymous-subject') {
      // A validly signed token whose subject is not a Mosaic customer. Used by
      // the gate to show the difference between "no token" and "a token this
      // graph cannot place".
      args.anonymousSubject = true;
    } else {
      throw new Error(`Unrecognised argument: ${arg}`);
    }
  }

  return args;
}

const HELP = `mint-token - a signed token for Mosaic's graph

  --customer <id>       the customer's global object identifier, which becomes
                        the token's sub claim. Get one from a review's author:
                        { products { reviews(first:1) { nodes { author { id } } } } }
  --scopes "a b"        space delimited, default all three:
                        ${ALL_SCOPES.join(' ')}
  --ttl <seconds>       default 3600. Pass a small number to watch a token
                        expire; the services set ClockSkew to zero so it expires
                        when it says it does.
  --anonymous-subject   sign a token whose sub is not a customer identifier
  --no-kid              omit the kid header. The router's config requires
                        header_key_id to start and accepts the token anyway
  --secret              print the signing key and exit

The key comes from ${'MOSAIC_JWT_SECRET'} if it is set, and otherwise from a
constant in this file that docker-compose.yml passes to every service.`;

function main() {
  let args;
  try {
    args = parseArgs(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    console.error(HELP);
    process.exit(2);
  }

  if (args.help) {
    console.log(HELP);
    return;
  }

  const secret = process.env.MOSAIC_JWT_SECRET || DEFAULT_SECRET;

  if (args.printSecret) {
    console.log(secret);
    return;
  }

  if (!args.customer && !args.anonymousSubject) {
    console.error('--customer is required. Pass --help for how to find one.');
    process.exit(2);
  }

  if (!Number.isFinite(args.ttl) || args.ttl <= 0) {
    console.error('--ttl must be a positive number of seconds.');
    process.exit(2);
  }

  const now = Math.floor(Date.now() / 1000);
  const payload = {
    iss: ISSUER,
    aud: AUDIENCE,
    sub: args.anonymousSubject ? 'not-a-mosaic-customer' : args.customer,
    scope: args.scopes,
    iat: now,
    exp: now + args.ttl,
  };

  console.log(sign(payload, secret, args.noKeyId ? null : KEY_ID));
}

main();
