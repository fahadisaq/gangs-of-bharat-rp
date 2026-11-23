'use strict';

const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const crypto = require('node:crypto');
const path = require('node:path');
const { after, before, test } = require('node:test');

const { createServer } = require('../server');

const TOKEN = crypto.randomBytes(24).toString('hex');
let server;
let base;

function call(pathname, { code = 'ADM-27', token = TOKEN, method = 'GET', body } = {}) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (code) headers['X-Admin-Code'] = code;
  return fetch(base + pathname, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  }).then(async (res) => ({ status: res.status, body: await res.json() }));
}

before(async () => {
  server = createServer({ token: TOKEN });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${server.address().port}`;
});

after(() => new Promise((resolve) => server.close(resolve)));

test('refuses to start without ADMIN_PANEL_TOKEN', () => {
  assert.throws(() => createServer({}), /ADMIN_PANEL_TOKEN/);
  assert.throws(() => createServer({ token: 'short' }), /ADMIN_PANEL_TOKEN/);
  const env = { ...process.env };
  delete env.ADMIN_PANEL_TOKEN;
  const run = spawnSync(process.execPath, [path.join(__dirname, '..', 'server.js')], {
    env,
    encoding: 'utf8',
    timeout: 10000,
  });
  assert.equal(run.status, 1);
  assert.match(run.stderr, /refused to start/);
});

test('API needs the bearer token and a known admin code', async () => {
  assert.equal((await call('/api/me', { token: null })).status, 401);
  assert.equal((await call('/api/me', { token: 'wrong-token-wrong-token' })).status, 401);
  assert.equal((await call('/api/me', { code: 'ADM-99' })).status, 403);
  const me = await call('/api/me');
  assert.equal(me.status, 200);
  assert.deepEqual(me.body, { code: 'ADM-27', level: 3, title: 'Admin' });
});

test('static pages are served without data and unknown paths are 404', async () => {
  const res = await fetch(`${base}/`);
  assert.equal(res.status, 200);
  assert.match(await res.text(), /Faction leaders and permissions/);
  assert.equal((await fetch(`${base}/../server.js`)).status, 404);
  assert.equal((await fetch(`${base}/lib/policy.js`)).status, 404);
});

test('admin list shows codes only', async () => {
  const res = await call('/api/admins');
  assert.equal(res.status, 200);
  for (const admin of res.body) {
    assert.deepEqual(Object.keys(admin).sort(), ['code', 'level', 'title']);
    assert.match(admin.code, /^ADM-\d+$/);
  }
  assert.doesNotMatch(JSON.stringify(res.body), /Mock|acc-/);
});

test('real player info is hidden except for the Owner', async () => {
  const asAdmin = await call('/api/players', { code: 'ADM-12' });
  assert.doesNotMatch(JSON.stringify(asAdmin.body), /example\.invalid|realInfo/);
  const asOwner = await call('/api/players', { code: 'ADM-01' });
  assert.match(JSON.stringify(asOwner.body), /example\.invalid/);
});

test('actions need a reason and are logged', async () => {
  const noReason = await call('/api/actions', {
    method: 'POST',
    body: { action: 'warn', targetId: 'ch-1004' },
  });
  assert.equal(noReason.status, 400);
  assert.equal((await call('/api/log')).body.length, 0);

  const ok = await call('/api/actions', {
    method: 'POST',
    body: { action: 'warn', targetId: 'ch-1004', reason: 'Spamming the ads channel' },
  });
  assert.equal(ok.status, 200);
  const log = (await call('/api/log')).body;
  assert.equal(log.length, 1);
  assert.equal(log[0].adminCode, 'ADM-27');
  assert.equal(log[0].reason, 'Spamming the ads channel');
  assert.doesNotMatch(JSON.stringify(log), /Mock Admin/);
});

test('admins act only on lower-level admins', async () => {
  const reason = 'Level change after review';
  const sameLevel = await call('/api/actions', {
    code: 'ADM-05',
    method: 'POST',
    body: { action: 'set_admin_level', targetId: 'ADM-05', newLevel: 4, reason },
  });
  assert.equal(sameLevel.status, 403);
  const higher = await call('/api/actions', {
    code: 'ADM-05',
    method: 'POST',
    body: { action: 'set_admin_level', targetId: 'ADM-01', newLevel: 4, reason },
  });
  assert.equal(higher.status, 403);
  const lowLevel = await call('/api/actions', {
    code: 'ADM-27',
    method: 'POST',
    body: { action: 'set_admin_level', targetId: 'ADM-48', newLevel: 2, reason },
  });
  assert.equal(lowLevel.status, 403); // level 3 cannot manage admins
  const ok = await call('/api/actions', {
    code: 'ADM-05',
    method: 'POST',
    body: { action: 'set_admin_level', targetId: 'ADM-48', newLevel: 2, reason },
  });
  assert.equal(ok.status, 200);
  const admins = (await call('/api/admins')).body;
  assert.equal(admins.find((a) => a.code === 'ADM-48').level, 2);
});

test('faction page includes Mass Media leaders and permission audit', async () => {
  const res = await call('/api/factions');
  assert.equal(res.status, 200);
  assert.ok(res.body.leaders.some((f) => f.key === 'media'));
  assert.ok(res.body.permissionAudit.some((e) => e.action === 'grant_power'));
});

test('bad JSON body is rejected', async () => {
  const res = await fetch(`${base}/api/actions`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${TOKEN}`, 'X-Admin-Code': 'ADM-27' },
    body: '{not json',
  });
  assert.equal(res.status, 400);
});

test('player, admin and ticket actions need a real target of the right kind', async () => {
  const reason = 'Repeated cheating reports';
  const bansBefore = (await call('/api/bans')).body.length;
  const logBefore = (await call('/api/log')).body.length;
  const post = (body, code = 'ADM-05') => call('/api/actions', { code, method: 'POST', body });

  for (const action of ['perm_ban', 'temp_ban', 'mute', 'kick', 'jail', 'set_admin_level', 'close_ticket']) {
    const missing = await post({ action, reason, newLevel: 1 });
    assert.equal(missing.status, 400, `${action} without targetId`);
    assert.match(missing.body.error, /targetId is required/);
    assert.equal((await post({ action, reason, newLevel: 1, targetId: '' })).status, 400, `${action} empty targetId`);
  }
  assert.equal((await post({ action: 'perm_ban', reason, targetId: 'ch-9999' })).status, 404, 'unknown player');
  assert.equal((await post({ action: 'perm_ban', reason, targetId: 'T-501' })).status, 404, 'a ticket is not a player');
  assert.equal((await post({ action: 'close_ticket', reason, targetId: 'ch-1004' })).status, 404, 'a player is not a ticket');
  assert.equal((await call('/api/bans')).body.length, bansBefore, 'no ban without a target');
  assert.equal((await call('/api/log')).body.length, logBefore, 'nothing logged for refused actions');

  const ok = await post({ action: 'perm_ban', reason, targetId: 'ch-1004' });
  assert.equal(ok.status, 200);
  const bans = (await call('/api/bans')).body;
  assert.equal(bans.length, bansBefore + 1);
  assert.equal(bans[bans.length - 1].playerId, 'ch-1004');
});
