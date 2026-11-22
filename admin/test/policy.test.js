'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');

const policy = require('../lib/policy');

test('admins act only on lower levels', () => {
  assert.equal(policy.canActOn(3, 0), true); // admin on a player
  assert.equal(policy.canActOn(3, 2), true);
  assert.equal(policy.canActOn(3, 3), false); // same level
  assert.equal(policy.canActOn(3, 4), false); // higher level
  assert.equal(policy.canActOn(6, 5), true);
  assert.equal(policy.canActOn(0, 0), false); // not an admin
  assert.equal(policy.canActOn(7, 0), false); // invalid level
});

test('action needs the minimum level', () => {
  const helper = { code: 'ADM-48', level: 1 };
  const reason = 'Repeated spam in ads chat';
  assert.equal(policy.checkAction(helper, { action: 'warn', reason }, 0).ok, true);
  const ban = policy.checkAction(helper, { action: 'perm_ban', reason }, 0);
  assert.equal(ban.ok, false);
  assert.equal(ban.status, 403);
});

test('reason is required for every action', () => {
  const owner = { code: 'ADM-01', level: 6 };
  for (const reason of [undefined, '', '   ', 'abc', 42]) {
    const result = policy.checkAction(owner, { action: 'warn', reason }, 0);
    assert.equal(result.ok, false, `reason ${String(reason)} should be refused`);
    assert.equal(result.status, 400);
  }
  assert.equal(policy.validateReason('x'.repeat(501)) !== null, true);
});

test('new admin level must be below the actor level', () => {
  const head = { code: 'ADM-05', level: 5 };
  const reason = 'Promotion after review period';
  const ok = policy.checkAction(head, { action: 'set_admin_level', reason, newLevel: 4 }, 3);
  assert.equal(ok.ok, true);
  const tooHigh = policy.checkAction(head, { action: 'set_admin_level', reason, newLevel: 5 }, 3);
  assert.equal(tooHigh.ok, false);
});

test('admins are displayed by code only', () => {
  const view = policy.displayAdmin({ code: 'ADM-27', level: 3, name: 'Secret Name', accountId: 'acc-1' });
  assert.deepEqual(view, { code: 'ADM-27', level: 3, title: 'Admin' });
});

test('real player info is shown only to the Owner', () => {
  const player = { id: 'p1', character: 'Raju', realInfo: { email: 'a@example.invalid' } };
  for (let level = 1; level <= 5; level++) {
    assert.equal(policy.viewPlayer(player, level).realInfo, undefined);
  }
  assert.deepEqual(policy.viewPlayer(player, 6).realInfo, { email: 'a@example.invalid' });
});
