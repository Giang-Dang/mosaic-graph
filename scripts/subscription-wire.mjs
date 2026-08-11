#!/usr/bin/env node
// Chapter 14's wire capture: what the router and a HotChocolate subgraph
// actually say to each other when a subscription opens.
//
// Deliberately not called by either verification script, for the reason
// decision 62 gives about scripts/measure-router.mjs: a gate should assert
// behaviour, and the behaviour here is already asserted by
// scripts/subscription-run.mjs, which checks which subprotocol each end picks
// without needing to see the bytes. This script is the thing that produced the
// handshake listings in the chapter, kept so that a reader can produce them
// again rather than take them on trust.
//
// It is a transparent TCP tee. Everything is forwarded untouched; the only
// side effect is what it prints. It has to sit in the middle rather than read
// a log because neither end logs this: the router prints nothing about a
// successful upgrade, and HotChocolate's Kestrel logs are at Warning in
// appsettings.json.
//
// Usage, with Reviews moved out of the way first so the tee can take its port:
//
//   ASPNETCORE_URLS=http://localhost:5115 \
//     dotnet run --project src/Mosaic.Reviews -c Release --no-launch-profile
//   node scripts/subscription-wire.mjs 5105 5115
//
// Then open a subscription through the router on 3002 and watch. Nothing else
// changes: federation/supergraph.json still routes reviews to 5105, and 5105
// still answers, so queries, mutations and Postman all keep working.

import net from 'node:net';

const [listenPort, targetPort] = process.argv.slice(2).map(Number);

if (!listenPort || !targetPort) {
  console.error('Usage: node scripts/subscription-wire.mjs <listen-port> <target-port>');
  process.exit(2);
}

const started = Date.now();
const stamp = () => `+${((Date.now() - started) / 1000).toFixed(3)}s`;
let connections = 0;

const OPCODES = { 0: 'cont', 1: 'text', 2: 'binary', 8: 'close', 9: 'ping', 10: 'pong' };

/**
 * Decode as many whole WebSocket frames as the buffer holds, print them, and
 * report how many bytes were consumed so the caller can keep the remainder.
 * Frames from a client are masked and frames from a server are not, which is
 * RFC 6455 and not a choice either end made.
 */
function decodeFrames(buffer, label, id) {
  let offset = 0;
  while (offset + 2 <= buffer.length) {
    const opcode = buffer[offset] & 0x0f;
    const fin = (buffer[offset] & 0x80) !== 0;
    const masked = (buffer[offset + 1] & 0x80) !== 0;
    let length = buffer[offset + 1] & 0x7f;
    let cursor = offset + 2;

    if (length === 126) {
      if (cursor + 2 > buffer.length) return offset;
      length = buffer.readUInt16BE(cursor);
      cursor += 2;
    } else if (length === 127) {
      if (cursor + 8 > buffer.length) return offset;
      length = Number(buffer.readBigUInt64BE(cursor));
      cursor += 8;
    }

    let mask = null;
    if (masked) {
      if (cursor + 4 > buffer.length) return offset;
      mask = buffer.subarray(cursor, cursor + 4);
      cursor += 4;
    }

    if (cursor + length > buffer.length) return offset;

    const payload = Buffer.from(buffer.subarray(cursor, cursor + length));
    if (mask) {
      for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
    }

    const body = opcode === 1 ? payload.toString('utf8') : `<${payload.length} bytes>`;
    console.log(`${stamp()} [${id}] ${label} ${OPCODES[opcode] ?? opcode}${fin ? '' : ' (continued)'} ${body}`);
    offset = cursor + length;
  }
  return offset;
}

net.createServer((client) => {
  const id = ++connections;
  const upstream = net.connect(targetPort, '127.0.0.1');

  const state = {
    client: { headersDone: false, buffer: Buffer.alloc(0) },
    upstream: { headersDone: false, buffer: Buffer.alloc(0) },
    upgraded: false,
  };

  const relay = (side, label, chunk, sink) => {
    const own = state[side];
    if (!own.headersDone) {
      own.buffer = Buffer.concat([own.buffer, chunk]);
      const end = own.buffer.indexOf('\r\n\r\n');
      if (end >= 0) {
        const head = own.buffer.subarray(0, end).toString('utf8');
        console.log(`${stamp()} [${id}] ${label}\n${head}\n`);
        if (side === 'client' && /^\s*upgrade:\s*websocket\s*$/im.test(head)) state.upgraded = true;
        own.headersDone = true;
        own.buffer = own.buffer.subarray(end + 4);
      }
    } else if (state.upgraded) {
      own.buffer = Buffer.concat([own.buffer, chunk]);
      own.buffer = own.buffer.subarray(decodeFrames(own.buffer, label.startsWith('ROUTER') ? 'router ->' : 'subgraph ->', id));
    }
    sink.write(chunk);
  };

  client.on('data', (chunk) => relay('client', 'ROUTER -> SUBGRAPH', chunk, upstream));
  upstream.on('data', (chunk) => relay('upstream', 'SUBGRAPH -> ROUTER', chunk, client));

  const drop = () => { client.destroy(); upstream.destroy(); };
  client.on('error', drop);
  upstream.on('error', drop);
  client.on('close', () => upstream.end());
  upstream.on('close', () => client.end());
}).listen(listenPort, '127.0.0.1', () => {
  console.log(`recording ${listenPort} -> ${targetPort}; open a subscription through the router now`);
});
