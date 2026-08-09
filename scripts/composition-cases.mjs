#!/usr/bin/env node
// Chapter 9's composition errors, each produced on purpose.
//
// Every case below is Mosaic's real pair of subgraph schemas - schema/catalog.graphql
// and schema/mosaic.graphql, exactly as they are committed - with one edit applied.
// The edit is a literal string replacement that has to match exactly once, so a
// case cannot go stale quietly: change the real schema in a way that removes the
// text a case edits, and the case fails with "no match" rather than composing
// something nobody meant.
//
// The chapter prints these messages, so the gate asserts them. wgc wraps its
// output in a box and hard-wraps long lines inside it, which makes a literal
// comparison useless; both the captured output and the expected fragment are
// therefore stripped of ANSI colour and box-drawing characters and collapsed to
// single spaces before matching.
//
// There is one implementation rather than one per verify script because the
// alternative was writing the same mutation logic twice, in PowerShell and in
// sh, and letting the two drift. verify.ps1 and verify.sh both call this.
//
// Usage:
//   node scripts/composition-cases.mjs             run every case, exit non-zero on failure
//   node scripts/composition-cases.mjs --list      name the cases and exit
//   node scripts/composition-cases.mjs --print <name>
//                                                  compose one case and print the
//                                                  composer's output unwrapped, which
//                                                  is how the chapter's listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const CATALOG = join(repoRoot, 'schema', 'catalog.graphql');
const MOSAIC = join(repoRoot, 'schema', 'mosaic.graphql');

// ---------------------------------------------------------------------------
// The cases
// ---------------------------------------------------------------------------
//
// `edits` are applied to the named file. `expect` fragments must all appear in
// the composer's output. `expectSuccess` marks the one case that is supposed to
// compose.

const CASES = [
  {
    name: 'baseline',
    summary: 'the two schemas as committed, which compose',
    edits: [],
    expectSuccess: true,
    expect: [],
  },
  {
    name: 'unsatisfiable-key',
    summary: 'mosaic keeps its key but stops answering for it',
    // A key that says resolvable: false is a key the router may not use to enter
    // this subgraph. Mosaic's four fields are then defined and unreachable.
    edits: [
      {
        file: 'mosaic',
        from: 'type Product @key(fields: "id") {',
        to: 'type Product @key(fields: "id", resolvable: false) {',
      },
    ],
    expect: [
      'The field "availableQuantity" is unresolvable at the following path:',
      'The root type field "Query.products" is defined in the following subgraph: "catalog".',
      'The field "Product.availableQuantity" is defined in the following subgraph: "mosaic".',
      'The entity ancestor "Product" in subgraph "catalog" has no accessible target entities (resolvable @key directives) in the subgraphs where "Product.availableQuantity" is defined.',
      'The field "price" is unresolvable at the following path:',
      'The field "reviews" is unresolvable at the following path:',
      'The field "averageRating" is unresolvable at the following path:',
    ],
  },
  {
    name: 'duplicate-field',
    summary: 'both subgraphs declare Product.title, neither says shareable',
    edits: [
      {
        file: 'mosaic',
        from: 'type Product @key(fields: "id") {\n',
        to: 'type Product @key(fields: "id") {\n  title: String!\n',
      },
    ],
    expect: [
      'The Object "Product" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "title" is defined in the following subgraphs: "catalog", "mosaic".',
      'However, it is not declared "@shareable" in any of them.',
    ],
  },
  {
    name: 'incompatible-type',
    summary: 'a shared field whose return type differs',
    edits: [
      {
        file: 'catalog',
        from: 'type Product implements Node @key(fields: "id") {\n',
        to: 'type Product implements Node @key(fields: "id") {\n  averageRating: Int @shareable\n',
      },
      { file: 'mosaic', from: '  averageRating: Float\n', to: '  averageRating: Float @shareable\n' },
    ],
    expect: [
      'Each instance of a shared field must resolve identically across subgraphs.',
      'The field "Product.averageRating" could not be federated due to incompatible types across subgraphs.',
      'The named type "Int" is returned by the following subgraph: "catalog".',
      'The named type "Float" is returned by the following subgraph: "mosaic".',
    ],
  },
  {
    name: 'missing-key',
    summary: 'mosaic declares Product and forgets the key',
    // The error names id and shareability. It does not name the missing key.
    edits: [{ file: 'mosaic', from: 'type Product @key(fields: "id") {', to: 'type Product {' }],
    expect: [
      'The Object "Product" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "id" is defined and declared "@shareable" in the following subgraph: "catalog".',
      'However, it is not declared "@shareable" in the following subgraph: "mosaic".',
    ],
  },
  {
    name: 'enum-drift',
    summary: 'both subgraphs declare ProductCategory with different members',
    // Catalog uses the enum as an output (Product.category) and as an input
    // (ProductFilterInput), which is the condition the composer's advice is
    // about. Mosaic declaring a shorter copy is how an enum drifts in practice:
    // somebody adds a member on one side of a cut.
    edits: [
      {
        file: 'mosaic',
        from: 'type Product @key(fields: "id") {',
        to: 'enum ProductCategory {\n  FURNITURE\n  LIGHTING\n  KITCHEN\n}\n\ntype Product @key(fields: "id") {',
      },
    ],
    expect: [
      'Enum "ProductCategory" was used as both an input and output but was inconsistently defined across inclusive subgraphs.',
      'add any new Enum values with the @inaccessible directive in the origin subgraph',
    ],
  },
  {
    name: 'key-mismatch',
    summary: 'the two subgraphs key the same entity on different fields',
    edits: [
      {
        file: 'mosaic',
        from: 'type Product @key(fields: "id") {\n',
        to: 'type Product @key(fields: "sku") {\n  sku: String!\n',
      },
    ],
    expect: [
      'The field "id" is defined and declared "@shareable" in the following subgraph: "catalog".',
      'However, it is not declared "@shareable" in the following subgraph: "mosaic".',
      'The field "sku" is defined and declared "@shareable" in the following subgraph: "mosaic".',
      'However, it is not declared "@shareable" in the following subgraph: "catalog".',
    ],
  },
];

// ---------------------------------------------------------------------------

// Box-drawing characters, ANSI colour, and the wrapping wgc does inside its
// table are all presentation. Strip them and compare on words.
function flatten(text) {
  return text
    .replace(/\[[0-9;]*m/g, '')
    .replace(/[─-╿]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function applyEdits(source, edits, label) {
  let text = source;
  for (const edit of edits) {
    const occurrences = text.split(edit.from).length - 1;
    if (occurrences !== 1) {
      throw new Error(
        `case "${label}": the edit to ${edit.file} matched ${occurrences} times, expected exactly 1.\n` +
          `The text it looks for is:\n${edit.from}\n` +
          'The committed schema has changed under this case. Fix the case rather than the schema.',
      );
    }
    text = text.replace(edit.from, edit.to);
  }
  return text;
}

function compose(testCase) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-composition-'));
  try {
    const catalog = readFileSync(CATALOG, 'utf8');
    const mosaic = readFileSync(MOSAIC, 'utf8');
    writeFileSync(
      join(dir, 'catalog.graphql'),
      applyEdits(
        catalog,
        testCase.edits.filter((e) => e.file === 'catalog'),
        testCase.name,
      ),
    );
    writeFileSync(
      join(dir, 'mosaic.graphql'),
      applyEdits(
        mosaic,
        testCase.edits.filter((e) => e.file === 'mosaic'),
        testCase.name,
      ),
    );
    writeFileSync(
      join(dir, 'graph.yaml'),
      [
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
      ].join('\n'),
    );

    // The package's own entry point rather than the .bin shim, because the shim
    // is a .cmd on Windows and running that needs a shell.
    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    let output = '';
    let failed = false;
    try {
      output = execFileSync(
        process.execPath,
        [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', join(dir, 'out.json')],
        { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] },
      );
    } catch (error) {
      failed = true;
      output = `${error.stdout || ''}${error.stderr || ''}`;
    }
    return { output, failed };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) {
    console.log(`${testCase.name.padEnd(20)}${testCase.summary}`);
  }
  process.exit(0);
}

if (args[0] === '--print') {
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  const { output } = compose(testCase);
  // Unwrap wgc's table: keep the content between the box borders, then rejoin
  // the lines it hard-wrapped. A message line starts with "The", "Each" or a
  // leading dash after the border; anything else is a continuation.
  const lines = output
    .replace(/\[[0-9;]*m/g, '')
    .split('\n')
    .filter((line) => line.includes('│'))
    .map((line) => line.replace(/^[^│]*│/, '').replace(/│[^│]*$/, '').trimEnd())
    .filter((line) => !/^\s*ERROR_MESSAGE\s*$/.test(line));
  const out = [];
  let depth = 0;
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed === '') {
      continue;
    }
    // A rendered selection set is reproduced as it stands, because its line
    // breaks are the message rather than the box's doing.
    const opensSelectionSet = depth === 0 && trimmed === 'query {';
    if (depth > 0 || opensSelectionSet) {
      out.push(line);
      depth += (line.match(/\{/g) || []).length - (line.match(/\}/g) || []).length;
      continue;
    }
    // Outside one, a line that opens neither a sentence nor a reason is the
    // tail of the line above it, hard-wrapped to fit the box.
    // The line starts the composer's messages use. Read them off the factories
    // in @wundergraph/composition/dist/errors/errors.js rather than guessing:
    // each one joins its parts with an explicit newline.
    const startsSomething = /^(- |The |Each |Enum |However, |This is because:)/.test(trimmed);
    if (!startsSomething && out.length > 0) {
      out[out.length - 1] = `${out[out.length - 1].trimEnd()} ${trimmed}`;
    } else {
      out.push(line);
    }
  }
  console.log(out.join('\n').trimEnd());
  process.exit(0);
}

let failures = 0;
for (const testCase of CASES) {
  let output;
  let failed;
  try {
    ({ output, failed } = compose(testCase));
  } catch (error) {
    // A stale case, almost certainly. Report it as a failure rather than as a
    // node stack trace: the gate's job is to say what is wrong.
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(20)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) {
      console.log(`             ${line}`);
    }
    continue;
  }
  const flat = flatten(output);
  const problems = [];

  if (testCase.expectSuccess && failed) {
    problems.push('expected this pair to compose, and wgc reported errors');
  }
  if (!testCase.expectSuccess && !failed) {
    problems.push('expected wgc to reject this pair, and it composed');
  }
  for (const fragment of testCase.expect) {
    if (!flat.includes(flatten(fragment))) {
      problems.push(`missing from the composer's output: ${fragment}`);
    }
  }

  if (problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(20)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(20)}${testCase.summary}`);
    for (const problem of problems) {
      console.log(`             ${problem}`);
    }
    console.log('           what wgc actually said:');
    for (const line of output.replace(/\[[0-9;]*m/g, '').split('\n')) {
      console.log(`           ${line}`);
    }
  }
}

if (failures > 0) {
  console.error(
    `\n${failures} composition case(s) did not behave as chapter 9 describes.\n` +
      'Either the schemas changed, or wgc did. Both are real findings: the chapter\n' +
      'prints these messages, so a change here means the chapter is now wrong.',
  );
  process.exit(1);
}
