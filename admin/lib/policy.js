'use strict';

// Admin rules from docs/design/12-admin-moderation.md:
// - levels 1-6, an admin acts only on players and on admins at a lower level
// - admins are shown only by code (for example ADM-27), never by name
// - real player info (email, phone, payments) only for level 6 Owner
// - every action needs a reason and is written to the admin log

const LEVEL_TITLES = Object.freeze({
  1: 'Helper',
  2: 'Moderator',
  3: 'Admin',
  4: 'Senior Admin',
  5: 'Head Admin',
  6: 'Owner',
});

const OWNER_LEVEL = 6;
const MIN_REASON_LENGTH = 5;
const MAX_REASON_LENGTH = 500;

// Minimum admin level for each action (DRAFT - owner review, see chapter 12).
const ACTION_MIN_LEVEL = Object.freeze({
  warn: 1,
  mute: 2,
  kick: 2,
  teleport_to: 2,
  spectate: 2,
  jail: 3,
  temp_ban: 3,
  long_temp_ban: 4,
  review_faction_permissions: 4,
  perm_ban: 5,
  set_faction_head: 5,
  set_admin_level: 5,
  close_ticket: 1,
  store_control: 6,
  economy_settings: 6,
});

// Actions whose target is an admin rather than a player.
const ADMIN_TARGET_ACTIONS = new Set(['set_admin_level']);

function isValidLevel(level) {
  return Number.isInteger(level) && level >= 1 && level <= OWNER_LEVEL;
}

function levelTitle(level) {
  return LEVEL_TITLES[level] || 'Unknown';
}

// Players have level 0 and can be acted on by any admin.
function canActOn(actorLevel, targetLevel) {
  if (!isValidLevel(actorLevel)) return false;
  const target = targetLevel || 0;
  return actorLevel > target;
}

function validateReason(reason) {
  if (typeof reason !== 'string') return 'reason is required';
  const trimmed = reason.trim();
  if (trimmed.length < MIN_REASON_LENGTH) {
    return `reason must be at least ${MIN_REASON_LENGTH} characters`;
  }
  if (trimmed.length > MAX_REASON_LENGTH) {
    return `reason must be at most ${MAX_REASON_LENGTH} characters`;
  }
  return null;
}

// Public view of an admin: code, level and title only. No name, no account.
function displayAdmin(admin) {
  return { code: admin.code, level: admin.level, title: levelTitle(admin.level) };
}

// Player view for an admin. Real info is removed unless the viewer is the Owner.
function viewPlayer(player, viewerLevel) {
  const view = {
    id: player.id,
    character: player.character,
    faction: player.faction,
    rank: player.rank,
    district: player.district,
    pingMs: player.pingMs,
    cashRupees: player.cashRupees,
    bankRupees: player.bankRupees,
    adminLevel: player.adminLevel || 0,
  };
  if (viewerLevel === OWNER_LEVEL && player.realInfo) {
    view.realInfo = { ...player.realInfo };
  }
  return view;
}

// Checks an action request. Returns { ok: true } or { ok: false, status, error }.
function checkAction(actor, request, targetLevel) {
  if (!actor || !isValidLevel(actor.level)) {
    return { ok: false, status: 403, error: 'unknown admin' };
  }
  const minLevel = ACTION_MIN_LEVEL[request.action];
  if (minLevel === undefined) {
    return { ok: false, status: 400, error: 'unknown action' };
  }
  const reasonError = validateReason(request.reason);
  if (reasonError) return { ok: false, status: 400, error: reasonError };
  if (actor.level < minLevel) {
    return { ok: false, status: 403, error: `action needs level ${minLevel}` };
  }
  if (targetLevel !== null && !canActOn(actor.level, targetLevel)) {
    return { ok: false, status: 403, error: 'target is not at a lower level' };
  }
  if (request.action === 'set_admin_level') {
    const newLevel = request.newLevel;
    if (!isValidLevel(newLevel) || newLevel >= actor.level) {
      return { ok: false, status: 403, error: 'new level must be below your own' };
    }
  }
  return { ok: true };
}

module.exports = {
  ACTION_MIN_LEVEL,
  ADMIN_TARGET_ACTIONS,
  LEVEL_TITLES,
  MIN_REASON_LENGTH,
  OWNER_LEVEL,
  canActOn,
  checkAction,
  displayAdmin,
  isValidLevel,
  levelTitle,
  validateReason,
  viewPlayer,
};
