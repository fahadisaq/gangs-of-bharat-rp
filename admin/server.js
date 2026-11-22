#!/usr/bin/env node
'use strict';

// GANGS OF BHARAT web admin panel (mock data, Node 22 built-ins only).
//
//   ADMIN_PANEL_TOKEN=<long random secret> node admin/server.js
//
// Every /api request needs "Authorization: Bearer <ADMIN_PANEL_TOKEN>" and
// "X-Admin-Code: ADM-xx" (the acting admin). There is no default token: the
// server refuses to start without one.

const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const policy = require('./lib/policy');
const { createMockData } = require('./lib/mock-data');

const MIN_TOKEN_LENGTH = 16;
const MAX_BODY_BYTES = 16 * 1024;
const PUBLIC_DIR = path.join(__dirname, 'public');

// Fixed list of static files: no path from the URL ever reaches the filesystem.
const STATIC_FILES = {
  '/': ['index.html', 'text/html; charset=utf-8'],
  '/index.html': ['index.html', 'text/html; charset=utf-8'],
  '/app.js': ['app.js', 'text/javascript; charset=utf-8'],
  '/style.css': ['style.css', 'text/css; charset=utf-8'],
};

const SECURITY_HEADERS = {
  'Content-Security-Policy': "default-src 'self'; frame-ancestors 'none'",
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'Cache-Control': 'no-store',
};

function checkToken(token) {
  if (typeof token !== 'string' || token.length < MIN_TOKEN_LENGTH) {
    throw new Error(
      `ADMIN_PANEL_TOKEN is not set or shorter than ${MIN_TOKEN_LENGTH} characters`,
    );
  }
}

function digest(value) {
  return crypto.createHash('sha256').update(value).digest();
}

function sendJson(res, status, body) {
  res.writeHead(status, { ...SECURITY_HEADERS, 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        reject(Object.assign(new Error('body too large'), { status: 413 }));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      try {
        resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString('utf8')) : {});
      } catch {
        reject(Object.assign(new Error('invalid JSON'), { status: 400 }));
      }
    });
    req.on('error', reject);
  });
}

function createApp({ token, data = createMockData(), now = () => new Date() } = {}) {
  checkToken(token);
  const tokenDigest = digest(token);

  function authenticate(req) {
    const header = req.headers.authorization || '';
    const match = /^Bearer (.+)$/.exec(header);
    if (!match || !crypto.timingSafeEqual(digest(match[1]), tokenDigest)) {
      return { status: 401, error: 'missing or wrong bearer token' };
    }
    const code = req.headers['x-admin-code'];
    const admin = data.admins.find((a) => a.code === code);
    if (!admin) return { status: 403, error: 'unknown admin code' };
    return { admin };
  }

  function resolveTarget(request) {
    if (policy.ADMIN_TARGET_ACTIONS.has(request.action)) {
      const target = data.admins.find((a) => a.code === request.targetId);
      return target ? { level: target.level, admin: target } : null;
    }
    if (request.targetId === undefined || request.targetId === null) {
      return { level: null };
    }
    const player = data.players.find((p) => p.id === request.targetId);
    if (player) return { level: player.adminLevel || 0, player };
    const ticket = data.tickets.find((t) => t.id === request.targetId);
    return ticket ? { level: 0, ticket } : null;
  }

  function applyAction(admin, request, target) {
    switch (request.action) {
      case 'set_admin_level':
        target.admin.level = request.newLevel;
        break;
      case 'temp_ban':
      case 'long_temp_ban':
      case 'perm_ban':
        data.bans.push({
          playerId: request.targetId,
          kind: request.action === 'perm_ban' ? 'permanent' : 'temporary',
          reason: request.reason.trim(),
          adminCode: admin.code,
          appealUsed: false,
        });
        break;
      case 'close_ticket':
        if (target.ticket) target.ticket.status = 'closed';
        break;
      default:
        break;
    }
  }

  async function handleAction(req, res, admin) {
    const request = await readBody(req);
    if (typeof request !== 'object' || request === null || Array.isArray(request)) {
      sendJson(res, 400, { error: 'body must be a JSON object' });
      return;
    }
    const target = resolveTarget(request);
    if (!target) {
      sendJson(res, 404, { error: 'target not found' });
      return;
    }
    const check = policy.checkAction(admin, request, target.level);
    if (!check.ok) {
      sendJson(res, check.status, { error: check.error });
      return;
    }
    applyAction(admin, request, target);
    const entry = {
      at: now().toISOString(),
      adminCode: admin.code,
      adminLevel: admin.level,
      action: request.action,
      targetId: request.targetId ?? null,
      reason: request.reason.trim(),
    };
    data.adminLog.push(entry);
    sendJson(res, 200, { ok: true, entry });
  }

  const getRoutes = {
    '/api/me': (admin) => policy.displayAdmin(admin),
    '/api/admins': () => data.admins.map(policy.displayAdmin),
    '/api/players': (admin) => data.players.map((p) => policy.viewPlayer(p, admin.level)),
    '/api/tickets': () => data.tickets,
    '/api/bans': () => data.bans,
    '/api/economy': () => data.economy,
    '/api/store': () => data.store,
    '/api/factions': () => ({ leaders: data.factions, permissionAudit: data.factionAudit }),
    '/api/events': () => data.events,
    '/api/log': () => data.adminLog,
    '/api/actions': () => policy.ACTION_MIN_LEVEL,
  };

  async function handle(req, res) {
    const url = new URL(req.url, 'http://localhost');
    const pathname = url.pathname;

    if (!pathname.startsWith('/api/')) {
      const file = STATIC_FILES[pathname];
      if (req.method !== 'GET' || !file) {
        sendJson(res, 404, { error: 'not found' });
        return;
      }
      const body = fs.readFileSync(path.join(PUBLIC_DIR, file[0]));
      res.writeHead(200, { ...SECURITY_HEADERS, 'Content-Type': file[1] });
      res.end(body);
      return;
    }

    const auth = authenticate(req);
    if (auth.error) {
      sendJson(res, auth.status, { error: auth.error });
      return;
    }
    if (req.method === 'GET' && getRoutes[pathname]) {
      sendJson(res, 200, getRoutes[pathname](auth.admin));
      return;
    }
    if (req.method === 'POST' && pathname === '/api/actions') {
      await handleAction(req, res, auth.admin);
      return;
    }
    sendJson(res, 404, { error: 'not found' });
  }

  return function listener(req, res) {
    handle(req, res).catch((err) => {
      if (!res.headersSent) sendJson(res, err.status || 500, { error: err.status ? err.message : 'server error' });
    });
  };
}

function createServer(options) {
  return http.createServer(createApp(options));
}

function main() {
  const token = process.env.ADMIN_PANEL_TOKEN;
  try {
    checkToken(token);
  } catch (err) {
    console.error(`admin panel refused to start: ${err.message}`);
    process.exit(1);
  }
  const host = process.env.ADMIN_PANEL_HOST || '127.0.0.1';
  const port = Number(process.env.ADMIN_PANEL_PORT || 8088);
  const server = createServer({ token });
  server.listen(port, host, () => {
    console.log(`admin panel on http://${host}:${server.address().port}/ (mock data)`);
  });
}

if (require.main === module) {
  main();
}

module.exports = { createApp, createServer, checkToken, MIN_TOKEN_LENGTH };
