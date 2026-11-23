'use strict';

// Static checks of firebase/ (the Firebase emulator cannot run in the build sandbox).

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');

const FIREBASE = path.join(__dirname, '..', '..', 'firebase');
const RULES = fs.readFileSync(path.join(FIREBASE, 'firestore.rules'), 'utf8')
  .replace(/\/\/.*$/gm, ''); // drop comments

// Server-only collections: clients must never write them.
const SERVER_ONLY = [
  'characters',
  'inventories',
  'vehicles_owned',
  'houses',
  'businesses',
  'transactions',
  'auctions',
  'purchases',
  'factions',
  'members',
  'faction_permissions',
  'faction_audit',
  'media_ads',
  'reports',
  'bans',
  'admin_log',
];

// Returns the text inside the braces of "match /<name>/{...} {", without nested blocks.
function matchBody(name) {
  const re = new RegExp(`match\\s+/${name}/\\{[^}]+\\}\\s*\\{`);
  const found = re.exec(RULES);
  assert.ok(found, `no match block for ${name}`);
  let depth = 1;
  let body = '';
  for (let i = found.index + found[0].length; i < RULES.length; i++) {
    const ch = RULES[i];
    if (ch === '{') depth++;
    if (ch === '}') depth--;
    if (depth === 0) break;
    if (depth === 1) body += ch;
  }
  return body;
}

function allowStatements(body) {
  return body.split(';').map((s) => s.trim()).filter((s) => s.startsWith('allow'));
}

test('firebase JSON files parse', () => {
  for (const file of ['firebase.json', 'firestore.indexes.json']) {
    const data = JSON.parse(fs.readFileSync(path.join(FIREBASE, file), 'utf8'));
    assert.equal(typeof data, 'object');
  }
  const config = JSON.parse(fs.readFileSync(path.join(FIREBASE, 'firebase.json'), 'utf8'));
  assert.equal(config.firestore.rules, 'firestore.rules');
});

test('server-only collections deny every client write', () => {
  for (const name of SERVER_ONLY) {
    const writes = allowStatements(matchBody(name)).filter((s) =>
      /\b(write|create|update|delete)\b/.test(s.split(':')[0]),
    );
    assert.ok(writes.length > 0, `${name} has no explicit write rule`);
    for (const rule of writes) {
      assert.match(rule, /:\s*if\s+false$/, `${name} allows a client write: ${rule}`);
    }
  }
});

test('players may change only language and settings, never currency', () => {
  const fn = /function onlySettingsChanged\(\)\s*\{([^}]*)\}/.exec(RULES);
  assert.ok(fn, 'onlySettingsChanged() missing');
  const keys = /hasOnly\(\[([^\]]*)\]\)/.exec(fn[1])[1];
  assert.deepEqual(keys.split(',').map((k) => k.trim().replace(/'/g, '')), ['language', 'settings']);
  const rules = allowStatements(matchBody('players'));
  const update = rules.find((s) => s.startsWith('allow update'));
  assert.match(update, /onlySettingsChanged\(\)/);
  assert.ok(rules.some((s) => /allow create, delete:\s*if false$/.test(s)));
});

test('faction member lists and permissions are readable only by the same faction', () => {
  assert.match(RULES, /function inFaction\(factionKey\)\s*\{\s*return signedIn\(\) && request\.auth\.token\.faction == factionKey;\s*\}/);
  for (const name of ['members', 'faction_permissions']) {
    const reads = allowStatements(matchBody(name)).filter((s) => /\bread\b/.test(s.split(':')[0]));
    assert.equal(reads.length, 1, `${name} should have one read rule`);
    assert.match(reads[0], /:\s*if\s+inFaction\(factionKey\)$/, `${name} read is too wide: ${reads[0]}`);
  }
});

test('default deny is present', () => {
  assert.match(RULES, /match \/\{document=\*\*\}\s*\{\s*allow read, write: if false;\s*\}/);
});
