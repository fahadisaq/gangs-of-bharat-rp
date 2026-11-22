'use strict';

// Admin panel front end. Builds the DOM with textContent only (no innerHTML),
// so data from players can never inject markup.

const PAGES = {
  players: { title: 'Live players', api: '/api/players' },
  tickets: { title: 'Tickets', api: '/api/tickets' },
  bans: { title: 'Bans', api: '/api/bans' },
  economy: { title: 'Economy charts', api: '/api/economy' },
  store: { title: 'Store control', api: '/api/store' },
  factions: { title: 'Faction leaders and permission audit', api: '/api/factions' },
  events: { title: 'Event scheduler', api: '/api/events' },
  log: { title: 'Admin logs', api: '/api/log' },
  admins: { title: 'Admins (codes only)', api: '/api/admins' },
};

const auth = {
  token: sessionStorage.getItem('token') || '',
  code: sessionStorage.getItem('code') || '',
};

async function api(pathname, options = {}) {
  const res = await fetch(pathname, {
    ...options,
    headers: {
      Authorization: `Bearer ${auth.token}`,
      'X-Admin-Code': auth.code,
      'Content-Type': 'application/json',
    },
  });
  const body = await res.json();
  if (!res.ok) throw new Error(body.error || `HTTP ${res.status}`);
  return body;
}

function el(tag, text) {
  const node = document.createElement(tag);
  if (text !== undefined && text !== null) node.textContent = String(text);
  return node;
}

function table(rows) {
  const t = el('table');
  if (!rows.length) {
    t.append(el('caption', 'No entries'));
    return t;
  }
  const keys = Object.keys(rows[0]);
  const head = el('tr');
  keys.forEach((k) => head.append(el('th', k)));
  t.append(head);
  rows.forEach((row) => {
    const tr = el('tr');
    keys.forEach((k) => {
      const v = row[k];
      tr.append(el('td', typeof v === 'object' && v !== null ? JSON.stringify(v) : v));
    });
    t.append(tr);
  });
  return t;
}

function barChart(labels, values) {
  const ns = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(ns, 'svg');
  const w = 60;
  const h = 160;
  const max = Math.max(...values);
  svg.setAttribute('width', String(labels.length * w));
  svg.setAttribute('height', String(h + 20));
  values.forEach((v, i) => {
    const bh = Math.round((v / max) * h);
    const rect = document.createElementNS(ns, 'rect');
    rect.setAttribute('class', 'bar');
    rect.setAttribute('x', String(i * w + 5));
    rect.setAttribute('y', String(h - bh));
    rect.setAttribute('width', String(w - 10));
    rect.setAttribute('height', String(bh));
    const label = document.createElementNS(ns, 'text');
    label.setAttribute('x', String(i * w + 5));
    label.setAttribute('y', String(h + 15));
    label.setAttribute('fill', '#ccc');
    label.textContent = labels[i];
    svg.append(rect, label);
  });
  return svg;
}

function render(name, data) {
  const page = document.getElementById('page');
  page.replaceChildren(el('h2', PAGES[name].title));
  if (name === 'economy') {
    page.append(el('h3', 'Money supply (Rs.)'), barChart(data.days, data.moneySupplyRupees));
    page.append(el('h3', 'Sources (Rs. today)'), table([data.sourcesRupees]));
    page.append(el('h3', 'Sinks (Rs. today)'), table([data.sinksRupees]));
    page.append(el('h3', 'Suspicious transfers'), table(data.suspiciousTransfers));
  } else if (name === 'factions') {
    page.append(el('h3', 'Leaders'), table(data.leaders));
    page.append(el('h3', 'Permission and rank audit'), table(data.permissionAudit));
  } else {
    page.append(table(data));
  }
}

async function showPage() {
  const name = location.hash.slice(1) || 'players';
  if (!PAGES[name] || !auth.token) return;
  try {
    render(name, await api(PAGES[name].api));
  } catch (err) {
    const p = el('p', err.message);
    p.className = 'error';
    document.getElementById('page').replaceChildren(p);
  }
}

async function signIn() {
  try {
    const me = await api('/api/me');
    document.getElementById('whoami').textContent = `${me.code} (level ${me.level}, ${me.title})`;
    const actions = await api('/api/actions');
    const select = document.getElementById('action-name');
    select.replaceChildren();
    Object.entries(actions).forEach(([action, level]) => {
      const option = el('option', `${action} (level ${level}+)`);
      option.value = action;
      select.append(option);
    });
    await showPage();
  } catch (err) {
    document.getElementById('whoami').textContent = `Sign-in failed: ${err.message}`;
  }
}

document.getElementById('login').addEventListener('submit', (event) => {
  event.preventDefault();
  auth.token = document.getElementById('token').value;
  auth.code = document.getElementById('code').value.trim();
  sessionStorage.setItem('token', auth.token);
  sessionStorage.setItem('code', auth.code);
  signIn();
});

document.getElementById('action').addEventListener('submit', async (event) => {
  event.preventDefault();
  const result = document.getElementById('action-result');
  const level = document.getElementById('action-level').value;
  const body = {
    action: document.getElementById('action-name').value,
    targetId: document.getElementById('action-target').value.trim() || null,
    reason: document.getElementById('action-reason').value,
  };
  if (level) body.newLevel = Number(level);
  try {
    const res = await api('/api/actions', { method: 'POST', body: JSON.stringify(body) });
    result.className = '';
    result.textContent = `Logged: ${res.entry.action} by ${res.entry.adminCode}`;
    await showPage();
  } catch (err) {
    result.className = 'error';
    result.textContent = err.message;
  }
});

window.addEventListener('hashchange', showPage);
if (auth.token) signIn();
