#!/usr/bin/env node
// Chapter 15's authorization behaviour, produced on purpose.
//
// Same contract as scripts/composition-cases.mjs, modeling-cases.mjs,
// override-cases.mjs and realtime-cases.mjs: every case is the real committed
// schemas under schema/ with literal edits applied, each edit has to match
// exactly once, and what is asserted is what the composer actually did. A
// schema change that invalidates a case fails with "matched 0 times" rather
// than composing something else and asserting nothing.
//
// The subject this time is @authorize and @requiresScopes, HotChocolate's own
// authorization directives rather than anything federation defines. Neither
// survives contact with the composer the way a reader would expect:
//
//   * @authorize is discarded outright, with no message at either severity
//   * @requiresScopes is kept, but it drags ApplyPolicy - an enum that exists
//     only to satisfy HotChocolate's [Authorize] attribute - into the public,
//     client-facing schema
//   * the scopes it carries reach engineConfig.fieldConfigurations, which is
//     the only place authorization is actually enforced; the client schema
//     shows the directive but the router never reads it back out of there
//
// Composing the committed set is also the one byte-for-byte check in this
// repository's case scripts: baseline below writes federation/supergraph.json
// again, from scratch, and diffs the two files rather than the two ASTs.
//
// Usage:
//   node scripts/auth-cases.mjs             run every case, exit non-zero on failure
//   node scripts/auth-cases.mjs --list      name the cases and exit
//   node scripts/auth-cases.mjs --case <name>
//                                            run one case through the same gate
//   node scripts/auth-cases.mjs --print <name>
//                                            run one case and print the composer's
//                                            output unwrapped, plus whatever that
//                                            case reads out of the composed config,
//                                            which is how the chapter's listings were made

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The eight entries federation/mosaic.yaml holds at ch15, reproduced here so a
// case's temporary graph.yaml is the committed one with edits, not a
// reconstruction of it. That is what lets `baseline` diff its own output
// against federation/supergraph.json rather than merely re-deriving the same
// facts a different way.
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

// The subscription block chapter 14 pinned reviews to. None of these cases are
// about transport, but the block is part of what makes `baseline`'s output
// identical to the committed federation/supergraph.json, so it travels with
// every case rather than being left out for convenience.
const PINNED = [
  '      protocol: ws',
  '      websocketSubprotocol: graphql-transport-ws',
].join('\n');

/**
 * Pull the reviews subscription block out of the committed composer input, so
 * that `baseline`'s byte-for-byte claim is checked against what the file
 * actually pins rather than a copy of it that has drifted.
 */
function readCommittedSubscriptionBlock() {
  const yaml = readFileSync(join(repoRoot, 'federation', 'mosaic.yaml'), 'utf8').replace(/\r\n/g, '\n');
  if (!yaml.includes(PINNED)) {
    throw new Error(
      'federation/mosaic.yaml no longer pins the reviews subscription to\n'
      + `${PINNED}\n`
      + 'Chapter 14 says it does, and chapter 15 relies on it to reproduce federation/supergraph.json\n'
      + 'byte for byte. Update this script or put it back.',
    );
  }
  return PINNED;
}

// ---------------------------------------------------------------------------
// Edits the cases share
// ---------------------------------------------------------------------------

const ACCOUNTS_IMPORT = '    import: ["@key", "@requiresScopes", "@tag", "FieldSet"]';
const REQUIRES_SCOPES_DIRECTIVE = 'directive @requiresScopes(scopes: [[String!]!]!) on\n'
  + '  | SCALAR\n  | OBJECT\n  | FIELD_DEFINITION\n  | INTERFACE\n  | ENUM';

// pricing and ordering do not import @requiresScopes as committed - case 10 is
// the only case that needs it in either, and needs it in both, so the edit
// that adds it is shared rather than written twice.
const addsRequiresScopesTo = (file, importLine, scope) => [
  { file, from: importLine, to: importLine.replace('@key', '@key", "@requiresScopes') },
  {
    file,
    from: 'type Money @shareable {\n  amount: Decimal!',
    to: `type Money @shareable {\n  amount: Decimal! @requiresScopes(scopes: [["${scope}"]])`,
  },
  { file, from: ') repeatable on SCHEMA', to: `) repeatable on SCHEMA\n\n${REQUIRES_SCOPES_DIRECTIVE}` },
];

const CASES = [
  {
    name: 'baseline',
    summary: 'the committed set composes and matches federation/supergraph.json byte for byte',
    edits: [],
    expectSuccess: true,
    matchesSupergraph: true,
  },

  {
    name: 'authorize-is-dropped-silently',
    summary: '@authorize on Query.customerById vanishes from the client schema, with no message',
    edits: [],
    expectSuccess: true,
    expectAbsentFromSchema: ['@authorize'],
  },

  {
    name: 'apply-policy-reaches-the-client-schema',
    summary: 'a HotChocolate implementation detail becomes a public type of the graph',
    edits: [],
    expectSuccess: true,
    expectInSchema: ['enum ApplyPolicy'],
  },

  {
    name: 'requires-scopes-lands-in-three-places',
    summary: 'Customer.email carries its directive in the client schema and its scopes in fieldConfigurations',
    edits: [],
    expectSuccess: true,
    expectInSchema: [
      'email: String! @requiresScopes(scopes: [["read:pii"]])',
      'directive @requiresScopes',
      'scalar openfed__Scope',
    ],
    expectAuth: {
      typeName: 'Customer',
      fieldName: 'email',
      requiresAuthentication: true,
      requiredOrScopes: [['read:pii']],
    },
  },

  {
    name: 'scopes-not-in-the-link-import-still-composes',
    summary: 'accounts stops importing @requiresScopes and the same authorization survives anyway',
    // The composer registers @authorize and @requiresScopes unconditionally,
    // consulting no @link import list to learn either one. Dropping the
    // import is therefore not the control it looks like - see
    // "policy-is-not-a-directive-this-composer-knows" below for the directive
    // that actually needs to be imported to mean anything, and does not.
    edits: [{ file: 'accounts', from: ACCOUNTS_IMPORT, to: '    import: ["@key", "@tag", "FieldSet"]' }],
    expectSuccess: true,
    expectAuth: {
      typeName: 'Customer',
      fieldName: 'email',
      requiresAuthentication: true,
      requiredOrScopes: [['read:pii']],
    },
  },

  {
    name: 'an-unknown-directive-is-rejected',
    summary: 'the control for the case above: a directive nothing declares fails composition',
    edits: [
      { file: 'accounts', from: '  displayName: String!\n', to: '  displayName: String! @totallyMadeUpDirective\n' },
    ],
    expectSuccess: false,
    expect: ['is not defined in the schema'],
  },

  {
    name: 'empty-scopes-composes-and-vanishes',
    summary: 'scopes: [] composes clean, and the directive disappears from both the schema and the config',
    edits: [
      {
        file: 'accounts',
        from: 'email: String! @requiresScopes(scopes: [["read:pii"]])',
        to: 'email: String! @requiresScopes(scopes: [])',
      },
    ],
    expectSuccess: true,
    // The whole Customer type, ending right after email with no directive on
    // it - not a blanket "no @requiresScopes anywhere", because the directive
    // declaration and openfed__Scope stay in the client schema regardless (see
    // the case above): what has to be gone is the usage on this one field.
    expectInSchema: ['displayName: String!\n  email: String!\n}'],
    expectNoAuth: { typeName: 'Customer', fieldName: 'email' },
  },

  {
    name: 'flat-scopes-crashes-the-composer',
    summary: 'a flat scopes list is not a validation error, it is an unhandled TypeError',
    edits: [
      {
        file: 'accounts',
        from: 'email: String! @requiresScopes(scopes: [["read:pii"]])',
        to: 'email: String! @requiresScopes(scopes: ["read:pii"])',
      },
    ],
    expectSuccess: false,
    expect: ['TypeError'],
  },

  {
    name: 'policy-is-not-a-directive-this-composer-knows',
    summary: '@wundergraph/composition 0.63.2 knows exactly two authorization directives, and @policy is not one',
    edits: [
      { file: 'accounts', from: ACCOUNTS_IMPORT, to: '    import: ["@key", "@policy", "@requiresScopes", "@tag", "FieldSet"]' },
      { file: 'accounts', from: '  displayName: String!\n', to: '  displayName: String! @policy(policies: [["admin"]])\n' },
    ],
    expectSuccess: false,
    expect: ['is not defined in the schema'],
  },

  {
    name: 'partial-requires-scopes-merges-to-and',
    summary: 'pricing and ordering each guard Money.amount with a different scope, and the composer flags nothing',
    edits: [
      ...addsRequiresScopesTo('pricing', '    import: ["@external", "@key", "@requires", "@shareable", "@tag", "FieldSet"]', 'read:price'),
      ...addsRequiresScopesTo('ordering', '    import: ["@key", "@shareable", "@tag", "FieldSet"]', 'read:orders'),
    ],
    expectSuccess: true,
    expectNoWarning: true,
    expectAuth: {
      typeName: 'Money',
      fieldName: 'amount',
      requiresAuthentication: true,
      // One AND-group, both scopes required together - a rule neither
      // subgraph author wrote and the composer never announces it produced.
      requiredOrScopes: [['read:orders', 'read:price']],
      // The two rules as originally written are still visible here, as two
      // separate OR-groups, which is the only place the contradiction survives
      // at all.
      requiredOrScopesByOr: [['read:price'], ['read:orders']],
    },
  },
];

// ---------------------------------------------------------------------------

// wgc draws a box around its messages and hard-wraps to fit inside it, so a
// literal comparison is useless. Everything outside printable ASCII becomes a
// space, and runs of whitespace collapse. Comparison is on words. A crash like
// flat-scopes-crashes-the-composer's arrives outside the box, as a plain Node
// stack trace, and survives this untouched.
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

/** The fieldConfigurations entry for one field, or null if the composer wrote none. */
function fieldConfig(config, typeName, fieldName) {
  return (config.engineConfig?.fieldConfigurations ?? []).find(
    (f) => f.typeName === typeName && f.fieldName === fieldName,
  ) ?? null;
}

// requiredOrScopes and requiredOrScopesByOr are both arrays of AND-groups. A
// case names the scopes it cares about, not the order the composer happened to
// emit the groups in or the order it happened to emit the scopes within a
// group, so comparison sorts both levels before it looks for a difference.
function scopeGroups(auth, key) {
  return (auth?.[key] ?? []).map((group) => [...(group.requiredAndScopes ?? [])].sort());
}

function sameScopeGroups(actual, expected) {
  const normalise = (groups) => groups.map((g) => [...g].sort().join(',')).sort().join('|');
  return normalise(actual) === normalise(expected);
}

function compose(testCase) {
  const dir = mkdtempSync(join(tmpdir(), 'mosaic-auth-'));
  try {
    for (const subgraph of SUBGRAPHS) {
      // Normalised on the way in. Every edit above is written with \n, and
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

    const lines = ['version: 1', 'subgraphs:'];
    for (const subgraph of SUBGRAPHS) {
      lines.push(
        `  - name: ${subgraph.name}`,
        `    routing_url: http://localhost:${subgraph.port}/graphql`,
        '    schema:',
        `      file: ${subgraph.name}.graphql`,
      );
      if (subgraph.name === 'reviews') {
        lines.push('    subscription:', `      url: http://localhost:${subgraph.port}/graphql`, PINNED);
      }
    }
    lines.push('');
    writeFileSync(join(dir, 'graph.yaml'), lines.join('\n'));

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
    let rawConfig = null;
    if (!failed && existsSync(out)) {
      rawConfig = readFileSync(out, 'utf8');
      config = JSON.parse(rawConfig);
    }
    return {
      output,
      failed,
      config,
      rawConfig,
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
  readCommittedSubscriptionBlock();
  const testCase = CASES.find((c) => c.name === args[1]);
  if (!testCase) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
  const { output, failed, config, schema } = compose(testCase);
  const unwrapped = unwrap(output);
  if (unwrapped) console.log(unwrapped);
  console.log(failed ? '(no execution config written)' : '(composed)');
  for (const fragment of testCase.expectInSchema ?? []) {
    const first = fragment.split('\n')[0];
    const at = schema?.indexOf(first) ?? -1;
    console.log(at >= 0 ? `\n${schema.slice(at, at + fragment.length + 40).split('\n\n')[0]}` : `\n(${first} is not in the client schema)`);
  }
  if (config && (testCase.expectAuth || testCase.expectNoAuth)) {
    const target = testCase.expectAuth ?? testCase.expectNoAuth;
    const entry = fieldConfig(config, target.typeName, target.fieldName);
    console.log(`\n${target.typeName}.${target.fieldName} fieldConfigurations entry:\n${JSON.stringify(entry, null, 2)}`);
  }
  process.exit(0);
}

// --case runs one case through the ordinary gate below, rather than printing
// it - useful when only one case needs re-checking after a schema edit,
// without waiting on the other nine.
let selected = CASES;
if (args[0] === '--case') {
  selected = CASES.filter((c) => c.name === args[1]);
  if (selected.length === 0) {
    console.error(`No case named "${args[1]}". Try --list.`);
    process.exit(2);
  }
}

readCommittedSubscriptionBlock();

let failures = 0;

for (const testCase of selected) {
  let result;
  try {
    result = compose(testCase);
  } catch (error) {
    failures += 1;
    console.log(`  FAILED   ${testCase.name.padEnd(48)}${testCase.summary}`);
    for (const line of String(error.message).split('\n')) console.log(`             ${line}`);
    continue;
  }

  const { output, failed, config, rawConfig, schema } = result;
  const flat = flatten(output);
  const flatSchema = schema === null ? null : flatten(schema);
  const problems = [];

  if (testCase.expectSuccess && failed) {
    problems.push('expected these schemas to compose, and wgc reported errors');
  }
  if (!testCase.expectSuccess && !failed) {
    problems.push('expected wgc to reject these schemas, and it composed');
  }
  for (const fragment of testCase.expect ?? []) {
    if (!flat.includes(flatten(fragment))) {
      problems.push(`missing from the composer's output: expected to find "${fragment}", did not find it`);
    }
  }
  for (const fragment of testCase.expectInSchema ?? []) {
    if (flatSchema === null) {
      problems.push(`expected "${flatten(fragment)}" in the composed client schema, but there is no client schema to look in`);
    } else if (!flatSchema.includes(flatten(fragment))) {
      problems.push(`expected "${flatten(fragment)}" in the composed client schema, did not find it`);
    }
  }
  for (const fragment of testCase.expectAbsentFromSchema ?? []) {
    if (flatSchema !== null && flatSchema.includes(flatten(fragment))) {
      problems.push(`expected "${fragment}" to be absent from the composed client schema, but found it`);
    }
  }
  if (testCase.expectNoWarning && /warning/i.test(flat)) {
    problems.push(`expected no warning in the composer's output, but found the word "warning" in it`);
  }

  if (testCase.expectAuth) {
    const { typeName, fieldName, requiresAuthentication, requiredOrScopes, requiredOrScopesByOr } = testCase.expectAuth;
    if (!config) {
      problems.push(`expected a fieldConfigurations entry for ${typeName}.${fieldName}, but there is no execution config to read it from`);
    } else {
      const entry = fieldConfig(config, typeName, fieldName);
      const auth = entry?.authorizationConfiguration ?? null;
      if (!auth) {
        problems.push(`expected ${typeName}.${fieldName} to carry an authorizationConfiguration, found ${entry ? 'a fieldConfigurations entry with none' : 'no fieldConfigurations entry at all'}`);
      } else {
        if (auth.requiresAuthentication !== requiresAuthentication) {
          problems.push(`${typeName}.${fieldName} authorizationConfiguration.requiresAuthentication is ${JSON.stringify(auth.requiresAuthentication)}, expected ${JSON.stringify(requiresAuthentication)}`);
        }
        const actualOr = scopeGroups(auth, 'requiredOrScopes');
        if (!sameScopeGroups(actualOr, requiredOrScopes)) {
          problems.push(`${typeName}.${fieldName} requiredOrScopes is ${JSON.stringify(actualOr)}, expected ${JSON.stringify(requiredOrScopes)}`);
        }
        if (requiredOrScopesByOr) {
          const actualByOr = scopeGroups(auth, 'requiredOrScopesByOr');
          if (!sameScopeGroups(actualByOr, requiredOrScopesByOr)) {
            problems.push(`${typeName}.${fieldName} requiredOrScopesByOr is ${JSON.stringify(actualByOr)}, expected ${JSON.stringify(requiredOrScopesByOr)}`);
          }
        }
      }
    }
  }

  if (testCase.expectNoAuth) {
    const { typeName, fieldName } = testCase.expectNoAuth;
    if (!config) {
      problems.push(`expected no authorizationConfiguration on ${typeName}.${fieldName}, but there is no execution config to check`);
    } else {
      const entry = fieldConfig(config, typeName, fieldName);
      if (entry?.authorizationConfiguration) {
        problems.push(`expected no authorizationConfiguration on ${typeName}.${fieldName}, found ${JSON.stringify(entry.authorizationConfiguration)}`);
      }
    }
  }

  if (testCase.matchesSupergraph) {
    const committedPath = join(repoRoot, 'federation', 'supergraph.json');
    if (!existsSync(committedPath)) {
      problems.push(`expected to diff against federation/supergraph.json, but the file does not exist`);
    } else if (rawConfig === null) {
      problems.push('expected freshly composed output to compare against federation/supergraph.json, but nothing composed');
    } else {
      const committed = readFileSync(committedPath, 'utf8');
      if (rawConfig !== committed) {
        problems.push(
          `freshly composed federation/supergraph.json differs from the committed one, byte for byte`
          + ` (composed is ${rawConfig.length} bytes, committed is ${committed.length} bytes)`,
        );
      }
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
    `\n${failures} authorization case(s) did not behave as chapter 15 describes.\n` +
      'Either the schemas changed, or wgc did. Both are real findings: the chapter\n' +
      'prints these messages, these client schemas and this authorizationConfiguration,\n' +
      'so a change here means the chapter is now wrong.',
  );
  process.exit(1);
}
