#!/usr/bin/env node
// Mints a Mosaic bearer token.
//
// Chapter 15. Every other piece of this repository is a thing under test; this
// one is the thing that lets you test it. A federated graph with authorisation
// on it needs a token before it will answer, and a reader who has to stand up
// an identity provider first will not get as far as the interesting part.
//
// There is no dependency here on purpose. A JWT signed with HS256 is three
// base64url segments and one HMAC, node ships the HMAC, and a twenty-line file
// a reader can read end to end is worth more here than a library call. What
// this is not is an identity provider: it signs whatever it is asked to sign,
// which is exactly what you want from a test fixture and exactly what you do
// not want from anything else.
//
//   node scripts/mint-token.mjs
//   node scripts/mint-token.mjs --scope "read:pii"
//   node scripts/mint-token.mjs --scope "read:pii read:orders" --subject alice
//   node scripts/mint-token.mjs --expires-in -60      # already expired
//
// The signing key comes from MOSAIC_JWT_SECRET, the same variable
// docker-compose.yml passes to the router and to all seven services. Sign with
// a different one and every service rejects the result, which is the whole
// point of a signature and is worth seeing once.

import { createHmac } from 'node:crypto';

// These two are contract, not configuration: they are what
// MosaicJwtDefaults.Issuer and .Audience validate against, and a token that
// disagrees with either is rejected with no useful message. Duplicated here
// rather than read out of the C#, on the same argument decision 48 used for
// ProductKey: this is a wire format two things have to agree about, and a
// shared file would turn that agreement into a build dependency.
const ISSUER = 'https://mosaic.local/identity';
const AUDIENCE = 'mosaic-graph';

// The key id the router matches against authentication.jwt.jwks[0].header_key_id.
// A symmetric jwks entry in Cosmo 0.337.1 requires that key, so a token with no
// kid in its header is refused before a single claim is looked at, and the
// refusal says nothing about kid.
const KEY_ID = 'mosaic-dev';

function base64url(input) {
  return Buffer.from(input).toString('base64')
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replaceAll('=', '');
}

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i === -1 ? fallback : process.argv[i + 1];
}

const secret = process.env.MOSAIC_JWT_SECRET;
if (!secret) {
  console.error(
    'No MOSAIC_JWT_SECRET in the environment. It is the same value the router\n'
    + 'and the seven services read, and docker-compose.yml sets it for all of\n'
    + 'them. Export it here too, or run this through one of the verify scripts.');
  process.exit(2);
}

const subject = arg('subject', 'customer-under-test');
const scope = arg('scope', '');
const expiresIn = Number(arg('expires-in', 3600));

// Seconds since the epoch, which is what every one of these claims is. A
// value in milliseconds validates as a token that expires in the year 56000
// and is the easiest mistake to make here.
const now = Math.floor(Date.now() / 1000);

const header = { alg: 'HS256', typ: 'JWT', kid: KEY_ID };
const payload = {
  iss: ISSUER,
  aud: AUDIENCE,
  sub: subject,
  iat: now,
  nbf: now,
  exp: now + expiresIn,
  // Space separated rather than a list, which is how RFC 8693 writes it and
  // what the router's scope_claim reads by default. Omitted entirely when
  // empty, because a token carrying `"scope": ""` and a token carrying no
  // scope claim at all are different documents and it is worth being able to
  // mint each.
  ...(scope ? { scope } : {})
};

const signingInput =
  `${base64url(JSON.stringify(header))}.${base64url(JSON.stringify(payload))}`;

const signature = createHmac('sha256', secret)
  .update(signingInput)
  .digest('base64')
  .replaceAll('+', '-')
  .replaceAll('/', '_')
  .replaceAll('=', '');

process.stdout.write(`${signingInput}.${signature}`);
