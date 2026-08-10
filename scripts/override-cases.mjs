#!/usr/bin/env node
// Chapter 12's @override behaviour, produced on purpose.
//
// Every case below is Mosaic's real set of subgraph schemas under schema/,
// exactly as they are committed, with edits applied. The edits are literal
// string replacements that have to match exactly once, so a case cannot go
// stale quietly - the same rule scripts/composition-cases.mjs works under, and
// for the same reason.
//
// What is being simulated needs saying plainly, because it is the one thing in
// this script that is not the repository as it stands. @override moves a field
// from the subgraph that has it to the subgraph that is taking it over, so a
// case needs a "before" in which two subgraphs declare the same field. At tag
// ch12 no two do: the extraction is finished and every field has exactly one
// owner. So each case adds the field back to the subgraph that used to own it -
// catalog stands in for the monolith - and then applies @override in the
// subgraph that took it. The edit is fictional; everything the composer says
// about it is not.
//
// The cases assert three kinds of thing:
//
//   * whether wgc composes at all, and what it says when it does not
//   * whether it emits a warning, which chapter 9 had no example of
//   * which subgraph the composed routing table gives the field to, which is
//     the only place the effect of a successful @override is visible
//
// Usage:
//   node scripts/override-cases.mjs             run every case, exit non-zero on failure
//   node scripts/override-cases.mjs --list      name the cases and exit
//   node scripts/override-cases.mjs --print <name>
//                                               run one case and print the composer's
//                                               output unwrapped, plus the routing table,
//                                               which is how the chapter's listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const SUBGRAPHS = [
  { name: 'catalog', port: 5101 },
  { name: 'pricing', port: 5102 },
  { name: 'inventory', port: 5103 },
  { name: 'accounts', port: 5104 },
  { name: 'reviews', port: 5105 },
  { name: 'ordering', port: 5106 },
];

// The edit that puts availableQuantity back into catalog, so that two subgraphs
// declare it and there is something to override. Inventory took the field in
// this chapter; catalog is standing in for the service it took it from.
const CATALOG_CLAIMS_QUANTITY = {
  file: 'catalog',
  from: 'type Product implements Node @key(fields: "id") {\n',
  to: 'type Product implements Node @key(fields: "id") {\n  availableQuantity: Int!\n',
};

const inventoryDeclares = (declaration) => ({
  file: 'inventory',
  from: '  availableQuantity: Int!\n',
  to: `  availableQuantity: Int!${declaration}\n`,
});

const CASES = [
  {
    name: 'baseline',
    summary: 'the six schemas as committed, with no @override anywhere',
    edits: [],
    expectSuccess: true,
    expect: [],
    // The finished state: inventory owns the field and nobody else declares it.
    routes: { 'Product.availableQuantity': ['inventory'] },
  },
  {
    name: 'two-owners-no-override',
    summary: 'catalog takes the field back and nobody says @override',
    // The control. Without the directive this is the shareability error chapter
    // 9 spent a section on, and it says nothing about migration: a reader who
    // has just moved a field sees a complaint about @shareable.
    edits: [CATALOG_CLAIMS_QUANTITY],
    expect: [
      'The Object "Product" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "availableQuantity" is defined in the following subgraphs: "catalog", "inventory".',
      'However, it is not declared "@shareable" in any of them.',
    ],
  },
  {
    name: 'override-takes-the-field',
    summary: 'inventory claims it with @override(from: "catalog")',
    // The whole mechanism. Two subgraphs declare the field, one of them says it
    // is taking over, and the composer stops objecting.
    edits: [CATALOG_CLAIMS_QUANTITY, inventoryDeclares(' @override(from: "catalog")')],
    expectSuccess: true,
    expect: [],
    // The effect, and the only place it is visible: catalog still declares the
    // field in its own schema and the router will never ask it for one.
    routes: { 'Product.availableQuantity': ['inventory'] },
  },
  {
    name: 'override-a-subgraph-that-is-not-there',
    summary: 'the from: argument names a subgraph nobody is running',
    // A typo, or a subgraph deleted last quarter. Nothing else declares the
    // field, so there is no duplication to report instead, and this is the
    // first composition warning in the book: exit code 0, config written,
    // graph unchanged.
    edits: [inventoryDeclares(' @override(from: "legacy-monolith")')],
    expectSuccess: true,
    expect: [
      'The Object type "Product" defines the directive "@override(from: "legacy-monolith")" on the following field: "availableQuantity".',
      'The required "from" argument of type "String!" should be provided with an existing subgraph name.',
      'However, a subgraph by the name of "legacy-monolith" does not exist.',
      'If this subgraph has been recently deleted, remember to clean up unused "@override" directives that reference this subgraph.',
    ],
    routes: { 'Product.availableQuantity': ['inventory'] },
  },
  {
    name: 'override-a-subgraph-that-is-not-there-suppressed',
    summary: 'the same, with --suppress-warnings',
    // Chapter 9 could say nothing about this flag because Mosaic produced no
    // warnings, and chapter 11 could only report that it does not touch an
    // error. This is the first case in the book where it has something to do.
    edits: [inventoryDeclares(' @override(from: "legacy-monolith")')],
    suppressWarnings: true,
    expectSuccess: true,
    expect: [],
    expectAbsent: ['does not exist'],
    // Same config, byte for byte, as the case above. Asserted below.
    routes: { 'Product.availableQuantity': ['inventory'] },
    sameConfigAs: 'override-a-subgraph-that-is-not-there',
  },
  {
    name: 'override-a-typo-with-a-real-loser',
    summary: 'the from: is misspelled and the field really is in two subgraphs',
    // The dangerous one. Misspell the subgraph you are taking the field from
    // and the override does not apply, so the duplication is back and the
    // composer reports it as a shareability problem - naming neither @override
    // nor the name you got wrong.
    edits: [CATALOG_CLAIMS_QUANTITY, inventoryDeclares(' @override(from: "katalog")')],
    expect: [
      'The Object "Product" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "availableQuantity" is defined in the following subgraphs: "catalog", "inventory".',
    ],
    expectAbsent: ['katalog'],
  },
  {
    name: 'override-from-yourself',
    summary: 'the from: argument names the subgraph declaring it',
    edits: [inventoryDeclares(' @override(from: "inventory")')],
    expect: [
      'Cannot override field "Product.availableQuantity" because the source and target subgraph names are both "inventory"',
    ],
  },
  {
    name: 'override-declared-twice',
    summary: 'two subgraphs both claim the same field from a third',
    edits: [
      CATALOG_CLAIMS_QUANTITY,
      inventoryDeclares(' @override(from: "catalog")'),
      {
        file: 'pricing',
        from: 'type Product @key(fields: "id") {\n',
        to: 'type Product @key(fields: "id") {\n  availableQuantity: Int! @override(from: "catalog")\n',
      },
    ],
    // The composer's own message is malformed here, and the case asserts it as
    // written rather than as intended: the second sentence is interpolated
    // already-formatted, so it arrives wrapped in its own quotes with a leading
    // space inside them. Read it in
    // @wundergraph/composition/dist/errors/errors.js if it looks like a typo.
    expect: [
      'The "@override" directive must only be declared on one single instance of a field.',
      'was declared on more than one instance of the following field: " The field "Product.availableQuantity" declares an @override directive in the following subgraphs: "pricing", "inventory".".',
    ],
  },
  {
    name: 'progressive-override-is-rejected',
    summary: 'the federation 2.7 label argument, which this composer does not know',
    // HotChocolate 16.6.0 has [Override(from, label)] and emits the label when
    // the subgraph links federation v2.7. wgc 0.129.7 carries a definition of
    // @override with one argument. The case writes the directive by hand rather
    // than through the attribute, because the point is what the composer does
    // with it.
    edits: [
      CATALOG_CLAIMS_QUANTITY,
      {
        file: 'inventory',
        from: 'url: "https://specs.apollo.dev/federation/v2.6"\n    import: ["@key", "@tag", "FieldSet"]',
        to: 'url: "https://specs.apollo.dev/federation/v2.7"\n    import: ["@key", "@override", "@tag", "FieldSet"]',
      },
      inventoryDeclares(' @override(from: "catalog", label: "percent(50)")'),
    ],
    expect: [
      'The 1st instance of the directive "@override" declared on coordinates "Product.availableQuantity" is invalid for the following reason:',
      'The definition for "@override" does not define the following argument that is provided: "label".',
    ],
  },
];

// ---------------------------------------------------------------------------

// wgc draws a box around its messages and hard-wraps to fit inside it, so a
// literal comparison is useless. Everything outside printable ASCII becomes a
// space - which covers the box drawing, and would also cover any punctuation
// the composer wrote that is not ASCII - and runs of whitespace collapse.
// Comparison is on words.
function flatten(text) {
  return text
    .replace(/\[[0-9;]*m/g, '')
    .replace(/[^\x20-\x7E]/g, ' ')
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

// Which subgraph does the composed routing table say answers each field? This
// is the only observable effect of an @override that composed, and it is not in
// the client schema: the client schema is identical with and without one.
function readRoutes(configPath) {
  const config = JSON.parse(readFileSync(configPath, 'utf8'));
  const byId = new Map((config.subgraphs ?? []).map((s) => [String(s.id), s.name]));
  const routes = new Map();
  for (const source of config.engineConfig?.datasourceConfigurations ?? []) {
    const name = byId.get(String(source.id)) ?? source.id;
    for (const node of [...(source.rootNodes ?? []), ...(source.childNodes ?? [])]) {
      for (const field of node.fieldNames ?? []) {
        const key = `${node.typeName}.${field}`;
        if (!routes.has(key)) routes.set(key, []);
        routes.get(key).push(name);
      }
    }
  }
  return routes;
}

function compose(testCase, outPath) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-override-'));
  try {
    for (const subgraph of SUBGRAPHS) {
      // Normalised on the way in, for the reason spelled out at length in
      // modeling-cases.mjs: every edit below is written with \n, and
      // `dotnet run -- schema export` writes CRLF on Windows, so a re-export on
      // this platform would make cases fail on line endings rather than on
      // content - and .gitattributes would hide it again at the next commit.
      const source = readFileSync(join(repoRoot, 'schema', `${subgraph.name}.graphql`), 'utf8')
        .replace(/\r\n/g, '\n');
      writeFileSync(
        join(dir, `${subgraph.name}.graphql`),
        applyEdits(
          source,
          testCase.edits.filter((e) => e.file === subgraph.name),
          testCase.name,
        ),
      );
    }

    writeFileSync(
      join(dir, 'graph.yaml'),
      [
        'version: 1',
        'subgraphs:',
        ...SUBGRAPHS.flatMap((s) => [
          `  - name: ${s.name}`,
          `    routing_url: http://localhost:${s.port}/graphql`,
          '    schema:',
          `      file: ${s.name}.graphql`,
        ]),
        '',
      ].join('\n'),
    );

    const out = outPath ?? join(dir, 'out.json');
    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    const argv = [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', out];
    if (testCase.suppressWarnings) argv.push('--suppress-warnings');

    // Run it, and be willing to run it twice. A composer that exits non-zero
    // having printed nothing at all has not told us anything about these
    // schemas, and reporting that as a changed behaviour sends somebody to look
    // at the wrong thing. Seen during a full verify.ps1 run with seven .NET
    // services up. Added in chapter 13; the same guard is in the other two case
    // scripts.
    let output = '';
    let failed = false;
    for (let attempt = 0; attempt < 2; attempt++) {
      try {
        output = execFileSync(process.execPath, argv, {
          encoding: 'utf8',
          stdio: ['ignore', 'pipe', 'pipe'],
        });
        failed = false;
      } catch (error) {
        failed = true;
        output = `${error.stdout || ''}${error.stderr || ''}`;
      }
      if (!failed || output.trim() !== '') break;
    }
    if (failed && output.trim() === '') {
      output = '(wgc exited non-zero twice and printed nothing at all, so it never '
        + 'reported on these schemas. That is an environment problem rather than a '
        + 'composition one: check for memory pressure from anything else running.)';
    }

    const routes = !failed && existsSync(out) ? readRoutes(out) : null;
    return { output, failed, routes };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

// wgc wraps its messages inside a table and hard-wraps long lines to fit. Undo
// both, so the chapter can print what the composer said rather than what its
// renderer did.
function unwrap(output) {
  const lines = output
    .replace(/\[[0-9;]*m/g, '')
    .split('\n')
    .filter((line) => line.includes('│'))
    .map((line) => line.replace(/^[^│]*│/, '').replace(/│[^│]*$/, '').trimEnd())
    .filter((line) => !/^\s*(ERROR|WARNING)_MESSAGE\s*$/.test(line));
  const out = [];
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed === '') continue;
    const startsSomething = /^(- |The |Each |Enum |However, |Cannot |If |This is because:)/.test(trimmed);
    if (!startsSomething && out.length > 0) {
      out[out.length - 1] = `${out[out.length - 1].trimEnd()} ${trimmed}`;
    } else {
      out.push(line);
    }
  }
  return out.join('\n').trimEnd();
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) {
    console.log(`${testCase.name.padEnd(42)}${testCase.summary}`);
  }
  process.exit(0);
}

if (args[0] === '--print') {
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  const { output, failed, routes } = compose(testCase);
  const unwrapped = unwrap(output);
  if (unwrapped) console.log(unwrapped);
  console.log(failed ? '(no execution config written)' : '(composed)');
  if (routes) {
    for (const field of Object.keys(testCase.routes ?? {})) {
      console.log(`${field.padEnd(30)}${(routes.get(field) ?? ['(nowhere)']).join(', ')}`);
    }
  }
  process.exit(0);
}

const configs = new Map();
let failures = 0;

for (const testCase of CASES) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-override-out-'));
  const outPath = join(dir, `${testCase.name}.json`);
  let result;
  try {
    result = compose(testCase, outPath);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(42)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    rmSync(dir, { recursive: true, force: true });
    continue;
  }

  const { output, failed, routes } = result;
  const flat = flatten(output);
  const problems = [];

  if (testCase.expectSuccess && failed) {
    problems.push('expected these schemas to compose, and wgc reported errors');
  }
  if (!testCase.expectSuccess && !failed) {
    problems.push('expected wgc to reject these schemas, and it composed');
  }
  for (const fragment of testCase.expect) {
    if (!flat.includes(flatten(fragment))) {
      problems.push(`missing from the composer's output: ${fragment}`);
    }
  }
  for (const fragment of testCase.expectAbsent ?? []) {
    if (flat.includes(flatten(fragment))) {
      problems.push(`the composer's output should not contain: ${fragment}`);
    }
  }
  for (const [field, expected] of Object.entries(testCase.routes ?? {})) {
    if (!routes) {
      problems.push(`no execution config to read a route for ${field} out of`);
      continue;
    }
    const actual = routes.get(field) ?? [];
    if (actual.join(',') !== expected.join(',')) {
      problems.push(
        `${field} is routed to [${actual.join(', ') || 'nowhere'}], expected [${expected.join(', ')}]`,
      );
    }
  }
  if (testCase.sameConfigAs) {
    const other = configs.get(testCase.sameConfigAs);
    if (!other) {
      problems.push(`no config recorded for "${testCase.sameConfigAs}" to compare against`);
    } else if (existsSync(outPath) && readFileSync(outPath, 'utf8') !== other) {
      problems.push(
        `the config differs from "${testCase.sameConfigAs}", and suppressing a warning should not change what is composed`,
      );
    }
  }
  if (existsSync(outPath)) configs.set(testCase.name, readFileSync(outPath, 'utf8'));
  rmSync(dir, { recursive: true, force: true });

  if (problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(42)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(42)}${testCase.summary}`);
    for (const problem of problems) console.log(`             ${problem}`);
    console.log('           what wgc actually said:');
    for (const line of output.replace(/\[[0-9;]*m/g, '').split('\n')) console.log(`           ${line}`);
  }
}

if (failures > 0) {
  console.error(
    `\n${failures} override case(s) did not behave as chapter 12 describes.\n` +
      'Either the schemas changed, or wgc did. Both are real findings: the chapter\n' +
      'prints these messages and these routing decisions, so a change here means the\n' +
      'chapter is now wrong.',
  );
  process.exit(1);
}
