#!/usr/bin/env node
// Chapter 13's modelling behaviour, produced on purpose.
//
// Same rules as scripts/composition-cases.mjs and scripts/override-cases.mjs:
// every case is a set of real committed schemas with literal edits applied,
// each edit has to match exactly once, and what is asserted is what the
// composer actually said or actually wrote. A schema change that invalidates a
// case fails with "matched 0 times" rather than composing something else and
// asserting nothing.
//
// Two schema sets, because chapter 13 has two subjects that need different
// graphs. Most cases run against Mosaic's seven committed subgraphs. The
// @interfaceObject cases run against samples/interface-object, because Mosaic
// has no interface whose implementations live in more than one service and
// inventing one inside a storefront would be worse than a sample.
//
// What is being asserted goes beyond exit codes, because most of this
// chapter's findings are things that compose:
//
//   * whether wgc composes at all, and what it says when it does not
//   * what the composed CLIENT SCHEMA says, which is where an enum silently
//     gains or loses members and where a value type silently gets weaker
//   * which subgraph the routing table gives a field to, which is the only
//     place two subgraphs both claiming Query.node is visible
//
// Usage:
//   node scripts/modeling-cases.mjs             run every case, exit non-zero on failure
//   node scripts/modeling-cases.mjs --list      name the cases and exit
//   node scripts/modeling-cases.mjs --print <name>
//                                               run one case and print the composer's
//                                               output unwrapped, plus whatever that
//                                               case reads out of the composed config,
//                                               which is how the chapter's listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The graph as it stands at ch13: six domain services and the node service.
const MOSAIC = [
  { name: 'catalog', port: 5101, path: 'schema/catalog.graphql' },
  { name: 'pricing', port: 5102, path: 'schema/pricing.graphql' },
  { name: 'inventory', port: 5103, path: 'schema/inventory.graphql' },
  { name: 'accounts', port: 5104, path: 'schema/accounts.graphql' },
  { name: 'reviews', port: 5105, path: 'schema/reviews.graphql' },
  { name: 'ordering', port: 5106, path: 'schema/ordering.graphql' },
  { name: 'nodes', port: 5107, path: 'schema/nodes.graphql' },
];

// Chapter 13's sample: an entity interface and a subgraph that contributes to
// it without knowing its implementations.
const INTERFACES = [
  { name: 'library', port: 5207, path: 'schema/samples/interface-object-library.graphql' },
  { name: 'ratings', port: 5208, path: 'schema/samples/interface-object-ratings.graphql' },
];

// ---------------------------------------------------------------------------
// Edits the cases share
// ---------------------------------------------------------------------------

// A brand new enum, declared in two subgraphs with different members. Which
// answer the composer gives depends only on where the enum is used, so the two
// cases below differ in exactly that and in nothing else.
const STOCK_CONDITION_A = 'enum StockCondition {\n  NEW\n  REFURBISHED\n}\n\n';
const STOCK_CONDITION_B = 'enum StockCondition {\n  NEW\n  DAMAGED\n}\n\n';
const FIELD_SET_ANCHOR = '"Scalar representing a set of fields."\nscalar FieldSet';

const declaresEnum = (file, block) => ({
  file,
  from: FIELD_SET_ANCHOR,
  to: block + FIELD_SET_ANCHOR,
});

// Node's four stubs, with and without the key that makes them shareable.
const STUB_KEY = ' @key(fields: "id", resolvable: false)';

const CASES = [
  {
    name: 'baseline',
    summary: 'the seven schemas as committed, which compose',
    edits: [],
    expectSuccess: true,
    expect: [],
    routes: { 'Query.node': ['nodes'], 'Query.nodes': ['nodes'] },
  },

  // -- how far @requires stretches (chapter 11's two open questions) -------
  {
    name: 'requires-a-nested-field-set',
    summary: 'inventory requires price { amount }, which pricing owns',
    // Chapter 11 left "nested field sets" here. It composes. Inventory
    // declares Pricing's Money-valued field @external and requires a path
    // through it, which is a field set two levels deep across a boundary.
    edits: [
      { file: 'inventory', from: '    import: ["@key", "@tag", "FieldSet"]',
        to: '    import: ["@external", "@key", "@requires", "@shareable", "@tag", "FieldSet"]' },
      { file: 'inventory', from: 'type Product @key(fields: "id") {\n  availableQuantity: Int!\n  id: ID!\n}',
        to: 'type Product @key(fields: "id") {\n  availableQuantity: Int!\n'
          + '  restockThreshold: Int! @requires(fields: "price { amount }")\n  id: ID!\n'
          + '  price: Money! @external\n}\n\ntype Money @shareable {\n  amount: Decimal!\n  currency: String!\n}\n\nscalar Decimal' },
      { file: 'inventory', from: ') repeatable on SCHEMA',
        to: ') repeatable on SCHEMA\n\ndirective @external on OBJECT | FIELD_DEFINITION\n\n'
          + 'directive @requires(fields: FieldSet!) on FIELD_DEFINITION\n\n'
          + 'directive @shareable repeatable on OBJECT | FIELD_DEFINITION' },
    ],
    expectSuccess: true,
    expect: [],
    expectInSchema: ['restockThreshold: Int!'],
    routes: { 'Product.restockThreshold': ['inventory'], 'Product.price': ['pricing'] },
  },
  {
    name: 'requires-across-two-other-subgraphs',
    summary: 'pricing requires one field from catalog and one from inventory',
    // Chapter 11's other question. Also composes, and the routing table is the
    // finding: the required fields stay with their owners and the requiring
    // field stays with its own, so one directive names three services.
    edits: [
      { file: 'pricing', from: '  shippingCost: Money! @requires(fields: "category")',
        to: '  shippingCost: Money! @requires(fields: "category availableQuantity")' },
      { file: 'pricing', from: '  category: ProductCategory! @external',
        to: '  category: ProductCategory! @external\n  availableQuantity: Int! @external' },
    ],
    expectSuccess: true,
    expect: [],
    routes: {
      'Product.category': ['catalog'],
      'Product.shippingCost': ['pricing'],
      'Product.availableQuantity': ['inventory'],
    },
  },

  // -- enums ---------------------------------------------------------------
  {
    name: 'enum-in-both-positions-must-match',
    summary: 'ProductCategory gains a member in pricing and is used as input and output',
    // ProductCategory is an output on Product.category and an input through
    // ProductCategoryOperationFilterInput, so the composer has no safe merge
    // available and refuses. The message is the only one in this book that
    // prescribes a migration.
    edits: [
      { file: 'pricing', from: 'enum ProductCategory {\n  FURNITURE', to: 'enum ProductCategory {\n  OUTDOOR\n  FURNITURE' },
    ],
    expectSuccess: false,
    expect: [
      'Enum "ProductCategory" was used as both an input and output but was inconsistently defined across inclusive subgraphs.',
      'add any new Enum values with the @inaccessible directive in the origin subgraph',
    ],
  },
  {
    name: 'enum-in-output-only-is-unioned',
    summary: 'an output-only enum with different members in two subgraphs composes',
    edits: [
      declaresEnum('inventory', STOCK_CONDITION_A),
      { file: 'inventory', from: 'type Product @key(fields: "id") {\n  availableQuantity: Int!',
        to: 'type Product @key(fields: "id") {\n  condition: StockCondition!\n  availableQuantity: Int!' },
      declaresEnum('pricing', STOCK_CONDITION_B),
      { file: 'pricing', from: 'type Product @key(fields: "id") {\n  price: Money!',
        to: 'type Product @key(fields: "id") {\n  priceCondition: StockCondition!\n  price: Money!' },
    ],
    expectSuccess: true,
    expect: [],
    // Every member either subgraph can emit survives, which is the safe merge
    // for a value travelling towards the client.
    expectInSchema: ['enum StockCondition {\n  NEW\n  DAMAGED\n  REFURBISHED\n}'],
  },
  {
    name: 'enum-in-input-only-is-intersected',
    summary: 'an input-only enum with different members loses the ones not shared',
    edits: [
      declaresEnum('inventory', STOCK_CONDITION_A),
      { file: 'inventory', from: 'type Query {\n  _service: _Service!',
        to: 'type Query {\n  countByCondition(condition: StockCondition!): Int!\n  _service: _Service!' },
      declaresEnum('accounts', STOCK_CONDITION_B),
      { file: 'accounts', from: 'type Query {\n  customerById(id: ID!): Customer',
        to: 'type Query {\n  customersByCondition(condition: StockCondition!): Int!\n  customerById(id: ID!): Customer' },
    ],
    expectSuccess: true,
    expect: [],
    // The other direction, and the quiet one: REFURBISHED and DAMAGED are gone
    // from the graph although each is a value one subgraph accepts. Nothing is
    // reported, at any severity.
    expectInSchema: ['enum StockCondition {\n  NEW\n}'],
    expectAbsentFromSchema: ['REFURBISHED', 'DAMAGED'],
  },

  // -- value types ---------------------------------------------------------
  {
    name: 'value-type-nullability-is-merged-permissively',
    summary: 'Money.currency is nullable in ordering only, and composes',
    edits: [
      { file: 'ordering', from: 'type Money @shareable {\n  amount: Decimal!\n  currency: String!\n}',
        to: 'type Money @shareable {\n  amount: Decimal!\n  currency: String\n}' },
    ],
    expectSuccess: true,
    expect: [],
    // Decision 64 found this for an @external field. It is the same rule for a
    // shared value type: the weaker declaration wins, for every client of the
    // graph, with no error and no warning.
    expectInSchema: ['type Money {\n  amount: Decimal!\n  currency: String\n}'],
  },
  {
    name: 'value-type-extra-field-is-unreachable',
    summary: 'Money gains a field in one subgraph and the graph cannot be satisfied',
    edits: [
      { file: 'ordering', from: 'type Money @shareable {\n  amount: Decimal!\n  currency: String!\n}',
        to: 'type Money @shareable {\n  amount: Decimal!\n  currency: String!\n  formatted: String!\n}' },
    ],
    expectSuccess: false,
    // The error is not about value types at all. A value type is not an entity,
    // so there is no key the router could use to go and get the missing field,
    // and satisfiability says exactly that.
    expect: [
      'The field "formatted" is unresolvable at the following path:',
      'The type "Money" has no accessible target entities (resolvable @key directives) in any other subgraph, so accessing other subgraphs is not possible.',
    ],
  },
  {
    name: 'value-type-must-be-shareable-everywhere',
    summary: 'ordering drops @shareable from Money and the pair stops composing',
    edits: [
      { file: 'ordering', from: 'type Money @shareable {', to: 'type Money {' },
    ],
    expectSuccess: false,
    expect: [
      'The Object "Money" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "amount" is defined and declared "@shareable" in the following subgraph: "pricing".',
      'However, it is not declared "@shareable" in the following subgraph: "ordering".',
    ],
  },

  // -- scalars -------------------------------------------------------------
  {
    name: 'scalar-meaning-is-never-checked',
    summary: 'two subgraphs disagree about what Decimal means and nothing notices',
    edits: [
      { file: 'ordering',
        from: 'scalar Decimal\n  @specifiedBy(url: "https://scalars.graphql.org/chillicream/decimal.html")',
        to: 'scalar Decimal\n  @specifiedBy(url: "https://example.com/mosaic/cents-as-an-integer")' },
    ],
    expectSuccess: true,
    expect: [],
    // The composer checks structure, and a scalar has none. Both @specifiedBy
    // urls are dropped on the way in, so the client schema cannot even show a
    // reader that there was a disagreement to have.
    expectAbsentFromSchema: ['cents-as-an-integer', 'chillicream/decimal.html'],
    expectInSchema: ['scalar Decimal'],
  },

  // -- the node field ------------------------------------------------------
  {
    name: 'node-in-two-subgraphs',
    summary: 'catalog publishes node as well, and neither declares it shareable',
    edits: [
      { file: 'catalog', from: '  productBySku(sku: String!): Product\n',
        to: '  productBySku(sku: String!): Product\n  node(id: ID!): Node\n' },
    ],
    expectSuccess: false,
    expect: [
      'The Object "Query" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'The field "node" is defined in the following subgraphs: "catalog", "nodes".',
    ],
  },
  {
    name: 'node-in-two-subgraphs-shareable',
    summary: 'the same two mark it @shareable, and the composer accepts it',
    // @shareable has to be on both declarations, not just the new one: mark
    // only catalog's and the composer reports the same error the other way
    // round, naming nodes as the subgraph that forgot.
    edits: [
      { file: 'catalog', from: '  productBySku(sku: String!): Product\n',
        to: '  productBySku(sku: String!): Product\n  node(id: ID!): Node @shareable\n' },
      { file: 'nodes', from: '  import: ["@key", "@tag", "FieldSet"]', to: '  import: ["@key", "@shareable", "@tag", "FieldSet"]' },
      { file: 'nodes', from: '  node("ID of the object." id: ID!): Node\n', to: '  node("ID of the object." id: ID!): Node @shareable\n' },
      { file: 'nodes', from: ') repeatable on SCHEMA',
        to: ') repeatable on SCHEMA\n\ndirective @shareable repeatable on OBJECT | FIELD_DEFINITION' },
    ],
    expectSuccess: true,
    expect: [],
    // This is the case worth keeping. It composes, and the routing table shows
    // why that is worse than the error above: two subgraphs are advertised as
    // able to answer node, the router is free to pick either, and catalog can
    // only resolve one of the four addressable types.
    routes: { 'Query.node': ['catalog', 'nodes'] },
  },
  {
    name: 'node-stubs-need-their-keys',
    summary: 'the node service declares its four stubs without a key',
    edits: [
      { file: 'nodes', from: `type Customer implements Node${STUB_KEY} {`, to: 'type Customer implements Node {' },
      { file: 'nodes', from: `type Order implements Node${STUB_KEY} {`, to: 'type Order implements Node {' },
      { file: 'nodes', from: `type Product implements Node${STUB_KEY} {`, to: 'type Product implements Node {' },
      { file: 'nodes', from: `type Review implements Node${STUB_KEY} {`, to: 'type Review implements Node {' },
    ],
    expectSuccess: false,
    // The control for what @key is doing in a stub. A key field is implicitly
    // shareable and an ordinary field is not, so dropping the key turns four
    // harmless two-line types into four shareability errors about `id`.
    expect: [
      'The Object "Product" defines the same fields in multiple subgraphs without the "@shareable" directive:',
      'However, it is not declared "@shareable" in the following subgraph: "nodes".',
    ],
  },

  // -- @interfaceObject, on the sample -------------------------------------
  {
    name: 'interface-object',
    summary: 'the sample as committed: one subgraph adds a field to an interface it cannot enumerate',
    schemas: INTERFACES,
    edits: [],
    expectSuccess: true,
    expect: [],
    // The field lands on the interface and on every implementation of it, and
    // the implementations are types the contributing subgraph has never heard
    // of.
    expectInSchema: ['averageRating: Float'],
    routes: { 'Media.averageRating': ['ratings'], 'Book.averageRating': ['ratings'], 'Film.averageRating': ['ratings'] },
  },
  {
    name: 'interface-object-without-the-directive',
    summary: 'the contributing subgraph declares Media as an ordinary object',
    schemas: INTERFACES,
    edits: [
      { file: 'ratings', from: 'type Media @key(fields: "id") @interfaceObject {', to: 'type Media @key(fields: "id") {' },
    ],
    expectSuccess: false,
    // The clearest error in this chapter, and the only one that names both the
    // subgraphs and the disagreement.
    expect: [
      '"Media" is defined using incompatible types across subgraphs.',
      'It is defined as type "Interface" in subgraph',
      'but type "Object" in subgraph "ratings".',
    ],
  },
  {
    name: 'interface-object-implementation-without-a-key',
    summary: 'one implementation loses its key and the composer stops being a composer',
    schemas: INTERFACES,
    edits: [
      { file: 'library', from: 'type Film implements Media @key(fields: "id") {', to: 'type Film implements Media {' },
      { file: 'library', from: 'union _Entity = Book | Film', to: 'union _Entity = Book' },
    ],
    expectSuccess: false,
    // Not a composition error. @wundergraph/composition 0.63.2 throws out of
    // handleEntityInterfaces, prints a stack trace, and tells you to upgrade or
    // open an issue. This case exists so that a release which turns the crash
    // into a message fails here rather than quietly making the chapter wrong.
    expect: [
      'Fatal: Expected key "Film" to exist in the map "entityDataByTypeName".',
      'handleEntityInterfaces',
    ],
  },
];

// ---------------------------------------------------------------------------

// wgc draws a box around its messages and hard-wraps to fit inside it, so a
// literal comparison is useless. Everything outside printable ASCII becomes a
// space, and runs of whitespace collapse. Comparison is on words.
function flatten(text) {
  return text
    .replace(/\[[0-9;]*m/g, '')
    .replace(/[^\x20-\x7E]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function applyEdits(source, edits, label, file) {
  let text = source;
  for (const edit of edits) {
    const occurrences = text.split(edit.from).length - 1;
    if (occurrences !== 1) {
      throw new Error(
        `case "${label}": the edit to ${file} matched ${occurrences} times, expected exactly 1.\n` +
          `The text it looks for is:\n${edit.from}\n` +
          'The committed schema has changed under this case. Fix the case rather than the schema.',
      );
    }
    text = text.replace(edit.from, edit.to);
  }
  return text;
}

function readRoutes(config) {
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

function compose(testCase) {
  const subgraphs = testCase.schemas ?? MOSAIC;
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-modeling-'));
  try {
    for (const subgraph of subgraphs) {
      // Normalised on the way in. Every edit below is written with \n, and
      // `dotnet run -- schema export` writes CRLF on Windows, so a case would
      // stop matching the moment somebody re-exported a schema on this
      // platform - which is a change to the file's line endings and not to its
      // content. The .gitattributes normalisation makes that invisible again
      // at the next commit, which is worse: the failure would come and go.
      const source = readFileSync(join(repoRoot, subgraph.path), 'utf8').replace(/\r\n/g, '\n');
      writeFileSync(
        join(dir, `${subgraph.name}.graphql`),
        applyEdits(source, testCase.edits.filter((e) => e.file === subgraph.name), testCase.name, subgraph.name),
      );
    }

    writeFileSync(
      join(dir, 'graph.yaml'),
      [
        'version: 1',
        'subgraphs:',
        ...subgraphs.flatMap((s) => [
          `  - name: ${s.name}`,
          `    routing_url: http://localhost:${s.port}/graphql`,
          '    schema:',
          `      file: ${s.name}.graphql`,
        ]),
        '',
      ].join('\n'),
    );

    const out = join(dir, 'out.json');
    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    const argv = [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', out];

    // Run it, and be willing to run it twice.
    //
    // A composer that exits non-zero having printed nothing at all has not
    // told us anything about these schemas. That happened once, during a full
    // verify.ps1 run with seven .NET services and PostgreSQL already up, and it
    // reported as "expected these schemas to compose" followed by an empty
    // "what wgc actually said". Those are two different failures and this
    // script used to conflate them: one is wgc disagreeing with the chapter,
    // the other is wgc not having run. A retry costs a second and only happens
    // on the second kind. If both attempts come back silent, the message below
    // says so rather than blaming the schemas.
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
        + 'reported on these schemas. That is an environment problem rather than a '
        + 'composition one: check for memory pressure from anything else running.)';
    }

    let config = null;
    if (!failed && existsSync(out)) {
      config = JSON.parse(readFileSync(out, 'utf8'));
    }
    return {
      output,
      failed,
      routes: config ? readRoutes(config) : null,
      schema: config?.engineConfig?.graphqlSchema ?? null,
    };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

// wgc wraps its messages inside a table and hard-wraps long lines to fit. Undo
// both, so the chapter can print what the composer said rather than what its
// renderer did. A message that arrives outside the box, which is what a crash
// does, is passed through untouched.
function unwrap(output) {
  const plain = output.replace(/\[[0-9;]*m/g, '');
  if (!plain.includes('│')) return plain.trimEnd();

  const lines = plain
    .split('\n')
    .filter((line) => line.includes('│'))
    .map((line) => line.replace(/^[^│]*│/, '').replace(/│[^│]*$/, '').trimEnd())
    .filter((line) => !/^\s*(ERROR|WARNING)_MESSAGE\s*$/.test(line));
  const out = [];
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed === '') continue;
    const startsSomething = /^(- |The |Each |Enum |However, |Cannot |If |This is because:|"|To update)/.test(trimmed);
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
    console.log(`${testCase.name.padEnd(48)}${testCase.summary}`);
  }
  process.exit(0);
}

if (args[0] === '--print') {
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  const { output, failed, routes, schema } = compose(testCase);
  const unwrapped = unwrap(output);
  if (unwrapped) console.log(unwrapped);
  console.log(failed ? '(no execution config written)' : '(composed)');
  for (const fragment of testCase.expectInSchema ?? []) {
    const first = fragment.split('\n')[0];
    const at = schema?.indexOf(first) ?? -1;
    console.log(at >= 0 ? `\n${schema.slice(at, at + fragment.length + 40).split('\n\n')[0]}` : `\n(${first} is not in the client schema)`);
  }
  for (const field of Object.keys(testCase.routes ?? {})) {
    console.log(`${field.padEnd(30)}${(routes?.get(field) ?? ['(nowhere)']).join(', ')}`);
  }
  process.exit(0);
}

let failures = 0;

for (const testCase of CASES) {
  let result;
  try {
    result = compose(testCase);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(48)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  }

  const { output, failed, routes, schema } = result;
  const flat = flatten(output);
  const flatSchema = schema === null ? null : flatten(schema);
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
  for (const fragment of testCase.expectInSchema ?? []) {
    if (flatSchema === null) {
      problems.push(`no client schema to look for ${flatten(fragment)} in`);
    } else if (!flatSchema.includes(flatten(fragment))) {
      problems.push(`missing from the composed client schema: ${flatten(fragment)}`);
    }
  }
  for (const fragment of testCase.expectAbsentFromSchema ?? []) {
    if (flatSchema !== null && flatSchema.includes(flatten(fragment))) {
      problems.push(`the composed client schema should not contain: ${fragment}`);
    }
  }
  for (const [field, expected] of Object.entries(testCase.routes ?? {})) {
    if (!routes) {
      problems.push(`no execution config to read a route for ${field} out of`);
      continue;
    }
    const actual = [...(routes.get(field) ?? [])].sort();
    if (actual.join(',') !== [...expected].sort().join(',')) {
      problems.push(`${field} is routed to [${actual.join(', ') || 'nowhere'}], expected [${expected.join(', ')}]`);
    }
  }

  if (problems.length === 0) {
    console.log(`  ok       ${testCase.name.padEnd(48)}${testCase.summary}`);
  } else {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(48)}${testCase.summary}`);
    for (const problem of problems) console.log(`             ${problem}`);
    console.log('           what wgc actually said:');
    for (const line of output.replace(/\[[0-9;]*m/g, '').split('\n')) console.log(`           ${line}`);
  }
}

if (failures > 0) {
  console.error(
    `\n${failures} modelling case(s) did not behave as chapter 13 describes.\n` +
      'Either the schemas changed, or wgc did. Both are real findings: the chapter\n' +
      'prints these messages, these client schemas and these routing decisions, so a\n' +
      'change here means the chapter is now wrong.',
  );
  process.exit(1);
}
