#!/usr/bin/env node
// Chapter 15's composition behaviour, produced on purpose.
//
// Same contract as composition-cases.mjs, override-cases.mjs,
// modeling-cases.mjs and realtime-cases.mjs: every case starts from the real
// committed schemas, each edit is a literal replacement that must match exactly
// once, and what is asserted is what the composer actually wrote. A schema
// change that invalidates a case fails with "matched 0 times" rather than
// composing an unedited pair and asserting nothing.
//
// What this script is about is a split that no single tool will show you.
// Mosaic's subgraphs carry two families of authorization annotation:
//
//   @authenticated / @requiresScopes   federation directives, from
//                                      HotChocolate.ApolloFederation. The
//                                      composer reads them and the router
//                                      enforces them.
//   @authorize                         HotChocolate's own, from
//                                      HotChocolate.AspNetCore.Authorization.
//                                      The subgraph enforces it and the
//                                      composer throws it away.
//
// Both are printed into the SDL a composer reads. Only one of them survives the
// trip, and the cases below are what that looks like from the outside.
//
// Usage:
//   node scripts/auth-cases.mjs             run every case, exit non-zero on failure
//   node scripts/auth-cases.mjs --list      name the cases and exit
//   node scripts/auth-cases.mjs --print <name>
//                                           run one case and print the composed
//                                           evidence, which is how the chapter's
//                                           listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

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

// The reviews subscription block chapter 14 pinned. Reproduced because this
// script writes its own composer input and dropping the block would quietly
// change a setting that chapter argued for.
const REVIEWS_SUBSCRIPTION = [
  '    subscription:',
  '      url: http://localhost:5105/graphql',
  '      protocol: ws',
  '      websocketSubprotocol: graphql-transport-ws',
].join('\n');

// The @authorize definition HotChocolate prints into every subgraph that turns
// authorization on, and the enum its `apply` argument refers to. Both are
// written out here so that a case can remove them and show what changes.
const AUTHORIZE_DEFINITION = `"The authorize directive."
directive @authorize(
  "The name of the authorization policy that determines access to the annotated resource."
  policy: String
  "Roles that are allowed to access the annotated resource."
  roles: [String!]
  "Defines when the authorize directive shall be applied. By default the authorize directive is applied before the resolver is executed."
  apply: ApplyPolicy! = BEFORE_RESOLVER
) repeatable on OBJECT | FIELD_DEFINITION`;

const APPLY_POLICY_ENUM = `"Defines when a policy shall be executed."
enum ApplyPolicy {
  "Before the resolver was executed."
  BEFORE_RESOLVER
  "After the resolver was executed."
  AFTER_RESOLVER
  "The policy is applied in the validation step before the execution."
  VALIDATION
}`;

const POLICY_DEFINITION = `"Indicates to composition that the target element is restricted based on authorization policies."
directive @policy(policies: [[String!]!]!) on
  | SCALAR
  | OBJECT
  | FIELD_DEFINITION
  | INTERFACE
  | ENUM`;

const CASES = [
  {
    name: 'baseline',
    summary: 'the committed schemas: four guarded fields, and one enum nobody asked for',
    edits: [],
    expectSuccess: true,
    // What the router is told to enforce. @requiresScopes sets BOTH members:
    // a scope implies a token, so two of these four have
    // requiresAuthentication true without @authenticated being written
    // anywhere near them.
    expectAuthorization: {
      'Query.customerById': { requiresAuthentication: true, scopes: [['customers:read']] },
      'Query.ordersByCustomer': { requiresAuthentication: true, scopes: [['orders:read']] },
      'Mutation.submitReview': { requiresAuthentication: true, scopes: [] },
      'Customer.email': { requiresAuthentication: true, scopes: [] },
    },
    // And what the client-facing schema says. The two federation directives
    // survive into it; @authorize does not survive anything.
    expectClientSchema: {
      contains: [
        'directive @authenticated on',
        'directive @requiresScopes(scopes: [[openfed__Scope!]!]!) on',
        'scalar openfed__Scope',
        'email: String! @authenticated',
        // The leak. @authorize is gone from this document and the enum its
        // argument referred to is still in it, fully documented, referenced by
        // nothing. Anybody who introspects Mosaic is told about the resolver
        // execution phases of one subgraph's server library.
        'enum ApplyPolicy',
      ],
      absent: [
        '@authorize',
        'directive @policy',
      ],
    },
  },
  {
    name: 'authorize-is-dropped-and-takes-nothing-with-it',
    summary: 'remove every trace of @authorize and the routing table is unchanged',
    edits: [
      { file: 'accounts', from: '  email: String! @authenticated @authorize\n', to: '  email: String! @authenticated\n' },
      { file: 'accounts', from: '  customerById(id: ID!): Customer\n    @authorize(policy: "CustomersRead")\n', to: '  customerById(id: ID!): Customer\n' },
      { file: 'accounts', from: `${AUTHORIZE_DEFINITION}\n\n`, to: '' },
      { file: 'accounts', from: `${APPLY_POLICY_ENUM}\n\n`, to: '' },
      { file: 'ordering', from: '  ordersByCustomer(customerId: ID!): [Order!]!\n    @authorize(policy: "OrdersRead")\n', to: '  ordersByCustomer(customerId: ID!): [Order!]!\n' },
      { file: 'ordering', from: `${AUTHORIZE_DEFINITION}\n\n`, to: '' },
      { file: 'ordering', from: `${APPLY_POLICY_ENUM}\n\n`, to: '' },
      { file: 'reviews', from: '    @authorize(policy: "ReviewsWrite")\n', to: '' },
      { file: 'reviews', from: `${AUTHORIZE_DEFINITION}\n\n`, to: '' },
      { file: 'reviews', from: `${APPLY_POLICY_ENUM}\n\n`, to: '' },
    ],
    expectSuccess: true,
    // Identical to the baseline. Everything the router will enforce came from
    // the other two directives, so a subgraph that stops enforcing anything
    // locally composes to the same routing table - which is the whole problem
    // with having the rule written twice.
    expectAuthorization: {
      'Query.customerById': { requiresAuthentication: true, scopes: [['customers:read']] },
      'Query.ordersByCustomer': { requiresAuthentication: true, scopes: [['orders:read']] },
      'Mutation.submitReview': { requiresAuthentication: true, scopes: [] },
      'Customer.email': { requiresAuthentication: true, scopes: [] },
    },
    // And the enum goes away with it, which is how we know where it came from.
    expectClientSchema: {
      contains: ['directive @authenticated on'],
      absent: ['@authorize', 'enum ApplyPolicy'],
    },
  },
  {
    name: 'policy-composes-and-enforces-nothing',
    summary: '@policy is in the spec and in HotChocolate, and Cosmo has never heard of it',
    edits: [
      { file: 'inventory', from: 'import: ["@key", "@tag", "FieldSet"]', to: 'import: ["@key", "@policy", "@tag", "FieldSet"]' },
      { file: 'inventory', from: '  availableQuantity: Int!\n', to: '  availableQuantity: Int! @policy(policies: [["stock:read"]])\n' },
      { file: 'inventory', from: '"Scalar representing a set of fields."', to: `${POLICY_DEFINITION}\n\n"Scalar representing a set of fields."` },
    ],
    expectSuccess: true,
    // No entry at all for the field. Not a warning, not an error, not an empty
    // configuration: the directive is read, discarded, and never mentioned.
    // Apollo's specification defines @policy at federation v2.6 and
    // HotChocolate ships a [Policy] attribute that emits it, so this is a
    // subgraph author writing a supported directive and getting silence.
    expectNoAuthorizationFor: ['Product.availableQuantity'],
    expectClientSchema: {
      contains: ['availableQuantity: Int!'],
      absent: ['@policy', 'directive @policy'],
    },
  },
  {
    name: 'a-scope-implies-a-token',
    summary: '@requiresScopes alone sets requiresAuthentication as well',
    edits: [
      { file: 'catalog', from: 'import: ["@key", "@shareable", "@tag", "FieldSet"]', to: 'import: ["@key", "@requiresScopes", "@shareable", "@tag", "FieldSet"]' },
      { file: 'catalog', from: '  productById(id: ID!): Product\n', to: '  productById(id: ID!): Product @requiresScopes(scopes: [["catalog:read"]])\n' },
      {
        file: 'catalog',
        from: '"Scalar representing a set of fields."',
        to: '"Indicates to composition that the target element is accessible only to the authenticated supergraph users with the appropriate JWT scopes."\ndirective @requiresScopes(scopes: [[String!]!]!) on\n  | SCALAR\n  | OBJECT\n  | FIELD_DEFINITION\n  | INTERFACE\n  | ENUM\n\n"Scalar representing a set of fields."',
      },
    ],
    expectSuccess: true,
    expectAuthorization: {
      // Nothing said @authenticated about this field. The composer decided
      // that a caller who must hold a scope must first be somebody.
      'Query.productById': { requiresAuthentication: true, scopes: [['catalog:read']] },
    },
  },
  {
    name: 'the-scope-scalar-is-not-the-one-the-subgraph-wrote',
    summary: 'the subgraph says [[String!]!]!, the published graph says [[openfed__Scope!]!]!',
    edits: [],
    expectSuccess: true,
    // Three spellings of one argument. Apollo's specification calls the scalar
    // federation__Scope; HotChocolate 16.6.0 declares a Scope scalar and then
    // overrides the directive argument to plain String with a [GraphQLType]
    // attribute, so what a subgraph publishes is [[String!]!]!; and the
    // composer replaces that with its own openfed__Scope on the way to the
    // client schema, declaring the scalar as it goes. Nothing warns, and the
    // three names are interchangeable only because they all serialise as
    // strings.
    expectSubgraphSchema: {
      file: 'accounts',
      contains: ['directive @requiresScopes(scopes: [[String!]!]!) on'],
    },
    expectClientSchema: {
      contains: ['directive @requiresScopes(scopes: [[openfed__Scope!]!]!) on', 'scalar openfed__Scope'],
      absent: ['federation__Scope'],
    },
  },
];

// ---------------------------------------------------------------------------

function buildInput(dir, testCase) {
  const lines = ['version: 1', 'subgraphs:'];
  const edited = new Map();

  for (const subgraph of SUBGRAPHS) {
    // Normalised on the way in: `schema export` writes CRLF on Windows and
    // .gitattributes puts it back at the next commit, so an unnormalised read
    // fails with "matched 0 times" and then stops failing.
    let source = readFileSync(join(repoRoot, subgraph.path), 'utf8').replace(/\r\n/g, '\n');

    for (const edit of testCase.edits.filter((e) => e.file === subgraph.name)) {
      const occurrences = source.split(edit.from).length - 1;
      if (occurrences !== 1) {
        throw new Error(
          `case "${testCase.name}": the edit to ${subgraph.name} matched ${occurrences} times, not once.\n`
          + `Looking for:\n${edit.from}\n`
          + 'The committed schema has changed under this case. Fix the case or the chapter, '
          + 'but do not let it compose an unedited schema and assert nothing.',
        );
      }
      source = source.replace(edit.from, edit.to);
    }

    edited.set(subgraph.name, source);
    writeFileSync(join(dir, `${subgraph.name}.graphql`), source);

    lines.push(
      `  - name: ${subgraph.name}`,
      `    routing_url: http://localhost:${subgraph.port}/graphql`,
      '    schema:',
      `      file: ${subgraph.name}.graphql`,
    );

    if (subgraph.name === 'reviews') lines.push(REVIEWS_SUBSCRIPTION);
  }

  lines.push('');
  writeFileSync(join(dir, 'graph.yaml'), lines.join('\n'));
  return edited;
}

function compose(testCase) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-auth-'));
  try {
    const edited = buildInput(dir, testCase);

    const out = join(dir, 'out.json');
    const wgc = join(repoRoot, 'node_modules', 'wgc', 'dist', 'src', 'index.js');
    const argv = [wgc, 'router', 'compose', '-i', join(dir, 'graph.yaml'), '-o', out];

    // Retried once, then reported as "never ran" rather than as a
    // disagreement, for the reason modeling-cases.mjs records: a composer that
    // exits non-zero having printed nothing has not disagreed with the
    // chapter, it has failed to run.
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
        + 'composition one.)';
    }

    const config = !failed && existsSync(out) ? JSON.parse(readFileSync(out, 'utf8')) : null;
    return { output, failed, config, edited };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

/** Every field the composer told the router to guard, flattened for comparison. */
function authorizationTable(config) {
  const table = {};
  for (const field of config.engineConfig?.fieldConfigurations ?? []) {
    const auth = field.authorizationConfiguration;
    if (!auth) continue;
    table[`${field.typeName}.${field.fieldName}`] = {
      requiresAuthentication: Boolean(auth.requiresAuthentication),
      scopes: (auth.requiredOrScopes ?? []).map((entry) => entry.requiredAndScopes ?? []),
    };
  }
  return table;
}

const clientSchema = (config) => config.engineConfig?.graphqlSchema ?? '';

function check(testCase, result) {
  const problems = [];

  if (testCase.expectSuccess && result.failed) {
    problems.push('expected composition to succeed and it failed');
    return problems;
  }
  if (!testCase.expectSuccess && !result.failed) {
    problems.push('expected composition to fail and it succeeded');
    return problems;
  }
  if (result.failed) return problems;

  const table = authorizationTable(result.config);

  if (testCase.expectAuthorization) {
    for (const [coordinate, expected] of Object.entries(testCase.expectAuthorization)) {
      const actual = table[coordinate];
      if (!actual) {
        problems.push(`no authorization configuration for ${coordinate}`);
        continue;
      }
      if (actual.requiresAuthentication !== expected.requiresAuthentication) {
        problems.push(
          `${coordinate}: requiresAuthentication is ${actual.requiresAuthentication}, `
          + `expected ${expected.requiresAuthentication}`,
        );
      }
      const actualScopes = JSON.stringify(actual.scopes);
      const expectedScopes = JSON.stringify(expected.scopes);
      if (actualScopes !== expectedScopes) {
        problems.push(`${coordinate}: scopes are ${actualScopes}, expected ${expectedScopes}`);
      }
    }
  }

  for (const coordinate of testCase.expectNoAuthorizationFor ?? []) {
    if (table[coordinate]) {
      problems.push(
        `${coordinate} has an authorization configuration and the case says it should have none: `
        + JSON.stringify(table[coordinate]),
      );
    }
  }

  if (testCase.expectClientSchema) {
    const schema = clientSchema(result.config);
    for (const needle of testCase.expectClientSchema.contains ?? []) {
      if (!schema.includes(needle)) problems.push(`client schema does not contain: ${needle}`);
    }
    for (const needle of testCase.expectClientSchema.absent ?? []) {
      if (schema.includes(needle)) problems.push(`client schema still contains: ${needle}`);
    }
  }

  if (testCase.expectSubgraphSchema) {
    const source = result.edited.get(testCase.expectSubgraphSchema.file) ?? '';
    for (const needle of testCase.expectSubgraphSchema.contains ?? []) {
      if (!source.includes(needle)) {
        problems.push(`${testCase.expectSubgraphSchema.file}.graphql does not contain: ${needle}`);
      }
    }
  }

  return problems;
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);

if (args[0] === '--list') {
  for (const testCase of CASES) console.log(`${testCase.name.padEnd(48)}${testCase.summary}`);
  process.exit(0);
}

if (args[0] === '--print') {
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  const result = compose(testCase);
  console.log(`# ${testCase.name}: ${testCase.summary}\n`);
  console.log(result.output.trim() || '(the composer printed nothing)');
  if (result.config) {
    console.log('\n# what the router is told to enforce\n');
    const table = authorizationTable(result.config);
    if (Object.keys(table).length === 0) {
      console.log('(nothing)');
    } else {
      for (const [coordinate, auth] of Object.entries(table)) {
        const scopes = auth.scopes.length ? JSON.stringify(auth.scopes) : '-';
        console.log(`${coordinate.padEnd(28)}authenticated=${String(auth.requiresAuthentication).padEnd(6)}scopes=${scopes}`);
      }
    }
    console.log('\n# directives in the client-facing schema\n');
    for (const line of clientSchema(result.config).split('\n')) {
      if (line.startsWith('directive @') || line.startsWith('scalar openfed')) console.log(line);
    }
  }
  process.exit(0);
}

let failures = 0;
for (const testCase of CASES) {
  const result = compose(testCase);
  const problems = check(testCase, result);
  if (problems.length === 0) {
    console.log(`  ok    ${testCase.name}`);
  } else {
    failures++;
    console.log(`  FAIL  ${testCase.name}`);
    for (const problem of problems) console.log(`          ${problem}`);
    if (result.output.trim()) {
      console.log('        composer said:');
      for (const line of result.output.trim().split('\n')) console.log(`          ${line}`);
    }
  }
}

console.log('');
if (failures > 0) {
  console.log(`${failures} of ${CASES.length} authorization cases did not behave as chapter 15 describes.`);
  process.exit(1);
}
console.log(`all ${CASES.length} authorization cases behave as chapter 15 describes.`);
