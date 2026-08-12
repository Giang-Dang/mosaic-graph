#!/usr/bin/env node
// How the composer chooses which route to blame, and how often it says it.
// Chapter 17.
//
// Chapter 9 measured two things about composition errors and explained neither,
// which is what makes them this file's subject rather than that one's.
//
// The first: catalog has four root fields returning a product - products,
// browseProducts, productById and productBySku - and an unresolvable key is
// reported against exactly one of them. That chapter wrote down "the message
// reports a route that fails, not every route that fails" and stopped, because
// nothing it had could say why. The control below is the missing half: vary
// which root fields exist, and which one is declared first, and watch the
// message move. It moves in SDL declaration order, which is what
// Graph.validate() returning on its first failure looks like from outside
// (@wundergraph/composition 0.63.2, resolvability-graph/graph.ts:225-279).
//
// The second: some errors print more than once, identically. This file counts
// rather than explains, because counting is what a gate can do and because the
// count is the thing chapter 9 got wrong by reading a line total as a fault
// total.
//
// Every edit here is a literal replacement that must match exactly once, for
// the reason composition-cases.mjs gives at length: a case that silently edits
// nothing composes the healthy set and asserts something else entirely.
//
// Needs no Docker and no running service. It composes files.
//
// Usage:
//   node scripts/satisfiability-cases.mjs                run every case
//   node scripts/satisfiability-cases.mjs --list         name the cases
//   node scripts/satisfiability-cases.mjs --print <name> run one and print the
//                                                       evidence

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const WGC = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');

// The eight entries of federation/mosaic.yaml, and the port each names. The
// subscription block on reviews is left off deliberately: nothing here starts a
// service, and a composed config's subscription settings are chapter 14's.
const SUBGRAPHS = [
  { name: 'catalog', port: 5101 },
  { name: 'pricing', port: 5102 },
  { name: 'inventory', port: 5103 },
  { name: 'accounts', port: 5104 },
  { name: 'reviews', port: 5105 },
  { name: 'ordering', port: 5106 },
  { name: 'nodes', port: 5107 },
  { name: 'streams', port: 5108 },
];

// Chapter 9's edit, made to pricing since chapter 12 (decision 72). Pricing
// keeps its key and stops answering for it, so everything pricing contributes
// to Product becomes unreachable.
const KEY_FROM = 'type Product @key(fields: "id") {';
const KEY_TO = 'type Product @key(fields: "id", resolvable: false) {';

// products carries a multi-line @deprecated block, so the edit that removes it
// has to take the whole thing.
const PRODUCTS_FIELD = [
  '  products: [Product!]!',
  '    @deprecated(',
  '      reason: "Returns the whole catalog with no upper bound on its size. Use `browseProducts`, which pages, filters and sorts."',
  '    )',
  '',
].join('\n');

const BY_SKU_FIELD = '  productBySku(sku: String!): Product\n';

// ---------------------------------------------------------------------------

// Case scripts normalise line endings on read: `schema export` writes CRLF on
// Windows, every literal below is written with \n, and .gitattributes puts the
// file back at the next commit, so an unnormalised read fails with "matched 0
// times" and then stops failing.
function readSchema(name) {
  return readFileSync(join(repoRoot, 'schema', `${name}.graphql`), 'utf8').replace(/\r\n/g, '\n');
}

function replaceOnce(text, from, to, what) {
  const hits = text.split(from).length - 1;
  if (hits !== 1) {
    throw new Error(
      `the edit "${what}" matched ${hits} times, expected exactly 1.\n` +
      'The committed schema has changed under this case. Fix the case rather than the schema.',
    );
  }
  return text.split(from).join(to);
}

// Compose the eight schemas with whatever edits the case asks for. `edits` is
// keyed by subgraph name and each value is a function from the committed
// schema to the edited one, so a case that needs two subgraphs changed says so
// rather than being limited to catalog.
function compose(dir, edits = {}) {
  const entries = [];
  for (const { name, port } of SUBGRAPHS) {
    let text = readSchema(name);
    if (edits[name]) text = edits[name](text);
    writeFileSync(join(dir, `${name}.graphql`), text);
    entries.push(
      `  - name: ${name}\n    routing_url: http://localhost:${port}/graphql\n    schema:\n      file: ${name}.graphql`,
    );
  }
  writeFileSync(join(dir, 'graph.yaml'), `version: 1\nsubgraphs:\n${entries.join('\n')}\n`);

  let output;
  try {
    output = execFileSync(process.execPath,
      [WGC, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', join(dir, 'out.json')],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (error) {
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  }

  // A gate that cannot tell "the tool disagrees" from "the tool did not run"
  // sends somebody to look at the wrong thing, which is why modeling-cases.mjs
  // retries once before reporting. Same here.
  if (output.trim() === '') {
    throw new Error('wgc printed nothing at all, so it never reported rather than disagreed');
  }

  return output.replace(/\[[0-9;]*m/g, '');
}

// wgc wraps each error inside a drawn table. The box-drawing characters are
// presentation, and composition-cases.mjs strips the same range for the same
// reason; the difference here is that this file counts lines rather than
// comparing one flattened string, so each line is cleaned on its own.
function errorSentences(output) {
  return output
    .split('\n')
    .map((line) => line.replace(/[─-╿]/g, ' ').replace(/\s+/g, ' ').trim())
    // The explanation under "This is because:" is a bullet list, so the
    // sentences that name a subgraph arrive with a leading "- ".
    .map((line) => line.replace(/^- /, ''))
    .filter((line) => /^(The |Each instance|Extension error|Fatal)/.test(line));
}

// Cut to at most `limit` characters, on a word boundary, marking the cut.
function shorten(text, limit) {
  if (text.length <= limit) return text;
  const cut = text.slice(0, limit);
  const lastSpace = cut.lastIndexOf(' ');
  return `${(lastSpace > 0 ? cut.slice(0, lastSpace) : cut).trimEnd()} ...`;
}

function namedRootFields(output) {
  return [...new Set([...output.matchAll(/The root type field "([^"]+)" is defined/g)].map((m) => m[1]))];
}

function unresolvableFields(output) {
  return [...new Set([...output.matchAll(/The field "([^"]+)" is unresolvable/g)].map((m) => m[1]))];
}

// ---------------------------------------------------------------------------
// The cases
// ---------------------------------------------------------------------------

const CASES = [
  {
    name: 'satisfiability-names-the-first-route',
    summary: 'the error blames the first root field declared, not every route that fails',
    run(dir) {
      const problems = [];
      const evidence = [];

      // Four runs. Only catalog's type Query differs between them, and pricing
      // is made unresolvable in all four, so the fault being reported is the
      // same fault every time and only the route to it changes.
      const runs = [
        ['all four, as committed', null, 'Query.products'],

        ['products deleted',
          (t) => replaceOnce(t, PRODUCTS_FIELD, '', 'delete products'),
          'Query.browseProducts'],

        ['products and browseProducts deleted',
          (t) => {
            const withoutProducts = replaceOnce(t, PRODUCTS_FIELD, '', 'delete products');
            const from = withoutProducts.indexOf('  browseProducts(');
            const to = withoutProducts.indexOf('  productById(');
            if (from < 0 || to < 0 || to < from) {
              throw new Error('could not find the browseProducts block to delete; catalog\'s Query type has changed');
            }
            return withoutProducts.slice(0, from) + withoutProducts.slice(to);
          },
          'Query.productById'],

        // The control that makes this a measurement rather than a coincidence:
        // nothing is removed, one field is moved, and the blame follows it.
        ['all four, productBySku moved to the top',
          (t) => {
            const without = replaceOnce(t, BY_SKU_FIELD, '', 'lift productBySku');
            return replaceOnce(without, 'type Query {\n', `type Query {\n${BY_SKU_FIELD}`, 'reinsert productBySku');
          },
          'Query.productBySku'],
      ];

      // pricing keeps its key and stops answering for it in all four runs, so
      // the fault is constant and only the route to it changes.
      const unresolvableKey = (t) => replaceOnce(t, KEY_FROM, KEY_TO, 'pricing stops resolving its key');

      for (const [label, edit, expected] of runs) {
        const output = compose(dir, edit
          ? { pricing: unresolvableKey, catalog: edit }
          : { pricing: unresolvableKey });
        const named = namedRootFields(output);
        const unresolvable = unresolvableFields(output);
        evidence.push(`${label.padEnd(38)} names ${JSON.stringify(named)}, unresolvable ${JSON.stringify(unresolvable)}`);

        if (named.length !== 1) {
          problems.push(`"${label}" named ${named.length} root fields, expected exactly 1: ${JSON.stringify(named)}`);
        } else if (named[0] !== expected) {
          problems.push(`"${label}" named ${named[0]}, expected ${expected}`);
        }

        // The same fault throughout: pricing contributes exactly these two.
        if (unresolvable.length !== 2 || !unresolvable.includes('price') || !unresolvable.includes('shippingCost')) {
          problems.push(
            `"${label}" reported ${JSON.stringify(unresolvable)} unresolvable, expected price and shippingCost.\n` +
            'If pricing has gained or lost a field, this number moves for a good reason\n' +
            'and the chapter says two; update both together.',
          );
        }
      }

      return { problems, evidence };
    },
  },

  {
    name: 'error-lines-are-not-error-counts',
    summary: 'one mistake repeats a sentence, another repeats the whole error',
    run(dir) {
      const problems = [];
      const evidence = [];

      function countSentences(output) {
        const counts = new Map();
        for (const sentence of errorSentences(output)) {
          counts.set(sentence, (counts.get(sentence) ?? 0) + 1);
        }
        return counts;
      }

      // 1. The unresolvable key. Two unresolvable fields under one root field
      //    produce two errors, and each error renders the same root-field
      //    sentence, so that one line appears twice while still naming a single
      //    route. Two errors, one route, and neither number is a count of
      //    mistakes anybody made.
      const unresolvable = countSentences(compose(dir, {
        pricing: (t) => replaceOnce(t, KEY_FROM, KEY_TO, 'pricing stops resolving its key'),
      }));
      const rootLine = [...unresolvable.entries()].find(([s]) => s.startsWith('The root type field'));
      evidence.push(`unsatisfiable-key: ${unresolvable.size} distinct sentences, ${[...unresolvable.values()].reduce((a, b) => a + b, 0)} lines`);
      if (!rootLine) {
        problems.push('no "The root type field" sentence in the unresolvable-key output at all');
      } else {
        evidence.push(`  the root-field sentence appears x${rootLine[1]}, naming one route`);
        if (rootLine[1] !== 2) {
          problems.push(`the root-field sentence appeared ${rootLine[1]} times, expected 2 (one per unresolvable field)`);
        }
      }

      // 2. The incompatible shared field, which is the other shape: not one
      //    line repeated inside an error but the entire error printed twice
      //    over, every sentence of it. This is the case chapter 9 counted and
      //    could not account for, and the composer has no deduplication of any
      //    kind (federation-factory.ts:278 is a bare array, every call site a
      //    plain push), so nothing collapses a message constructed twice.
      //    The two edits are composition-cases.mjs's own `incompatible-type`
      //    pair, kept identical on purpose so that the two files disagree
      //    loudly if either schema moves.
      const incompatible = countSentences(compose(dir, {
        catalog: (t) => replaceOnce(t,
          'type Product implements Node @key(fields: "id") {\n',
          'type Product implements Node @key(fields: "id") {\n  averageRating: Int @shareable\n',
          'catalog gains an incompatible averageRating'),
        reviews: (t) => replaceOnce(t,
          '  averageRating: Float\n',
          '  averageRating: Float @shareable\n',
          'reviews marks averageRating shareable'),
      }));
      evidence.push(`incompatible-type: ${incompatible.size} distinct sentences, ${[...incompatible.values()].reduce((a, b) => a + b, 0)} lines`);
      // Truncated at a word boundary rather than mid-word: this output is what
      // the chapter prints, and a line cut through the middle of a word reads
      // as a defect in the tool rather than as a deliberately shortened quote.
      for (const [sentence, n] of incompatible) evidence.push(`  x${n}  ${shorten(sentence, 72)}`);

      if (incompatible.size < 1) {
        problems.push('the incompatible-type edit produced no error sentences at all');
      } else if (![...incompatible.values()].every((n) => n === 2)) {
        problems.push(
          'expected every sentence of the incompatible-type error to appear exactly twice, got ' +
          JSON.stringify([...incompatible.values()]) + '.\n' +
          'A release that deduplicates composition errors would land here first, which is\n' +
          'the point of asserting it: chapter 17 says the whole error is printed twice.',
        );
      }

      return { problems, evidence };
    },
  },
];

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(40)}${testCase.summary}`);
  process.exit(0);
}

const selected = args[0] === '--print'
  ? CASES.filter((c) => c.name === args[1])
  : CASES;

if (args[0] === '--print' && selected.length === 0) {
  console.error(`No case named "${args[1]}". Try --list.`);
  process.exit(2);
}

let failures = 0;
for (const testCase of selected) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-satisfiability-'));
  let result;
  try {
    result = testCase.run(dir);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(40)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }

  if (result.problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(40)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(40)}${testCase.summary}`);
    for (const problem of result.problems) console.log(`             ${problem}`);
  }

  if (args[0] === '--print') {
    for (const line of result.evidence) console.log(`             ${line}`);
  }
}

if (failures > 0) {
  console.log(`\n${failures} of ${selected.length} satisfiability cases did not behave as chapter 17 says.`);
  process.exit(1);
}
