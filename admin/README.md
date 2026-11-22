# Web admin panel (mock data)

A zero-dependency admin panel for GANGS OF BHARAT. It uses only Node 22 built-ins (`node:http`, `node:fs`, `node:crypto`, `node:test`) and serves in-memory **mock data** from `lib/mock-data.js`. It is not connected to Firestore or the game server yet.

## Pages

Live players, tickets, bans, economy charts, store control, faction leaders and faction permission audit (rank changes, power grants, treasury limits, including Mass Media), event scheduler, admin logs and the admin list.

## Rules enforced (from `docs/design/12-admin-moderation.md`)

- Admin levels 1-6 (Helper, Moderator, Admin, Senior Admin, Head Admin, Owner). Each action has a minimum level (`lib/policy.js`, DRAFT – owner review).
- An admin acts only on players and on admins at a **lower** level. A new admin level must be below the actor's own level.
- Admins are shown **only by code** (for example `ADM-27`). Names and account IDs never leave the server.
- Real player info (email, phone, payments) is returned **only to the level 6 Owner**.
- Every action needs a **reason** (5-500 characters) and is appended to the admin log with the admin code, level and time.

## Run

```bash
export ADMIN_PANEL_TOKEN="$(node -e "console.log(require('crypto').randomBytes(32).toString('hex'))")"
node admin/server.js        # http://127.0.0.1:8088/
```

There is **no default token**. The server refuses to start if `ADMIN_PANEL_TOKEN` is missing or shorter than 16 characters. Optional: `ADMIN_PANEL_HOST` (default `127.0.0.1`) and `ADMIN_PANEL_PORT` (default `8088`).

Sign in on the page with the token and an admin code from the mock data (`ADM-01` Owner, `ADM-05`, `ADM-12`, `ADM-27`, `ADM-33`, `ADM-48` Helper).

API calls send `Authorization: Bearer <ADMIN_PANEL_TOKEN>` and `X-Admin-Code: ADM-xx`:

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/me`, `/api/admins` | Admin code, level and title |
| GET | `/api/players`, `/api/tickets`, `/api/bans`, `/api/economy`, `/api/store`, `/api/factions`, `/api/events`, `/api/log` | Page data |
| GET | `/api/actions` | Action names and minimum levels |
| POST | `/api/actions` | `{ "action", "targetId", "reason", "newLevel"? }`, checked and logged |

## Before production

One shared token plus an `X-Admin-Code` header is only suitable for local testing: anyone with the token can pick any admin code. Before real use, replace it with per-admin sign-in (Firebase Auth with an admin level custom claim and 2FA), serve over HTTPS, and read and write Firestore (`admin_log`, `bans`, `tickets`, `faction_audit`) through the Admin SDK.

## Test

```bash
node --test admin/test
```

Tests use an ephemeral port and cover the level hierarchy, the reason requirement, code-only admin display, real-info hiding, missing-token refusal, and static checks of `firebase/firestore.rules` and the Firebase JSON files.
