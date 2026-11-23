# Firestore schema (UNVERIFIED)

This is the planned Firestore layout for GANGS OF BHARAT. The rules in `firestore.rules` have **not been deployed or run in the emulator**, because Firebase is unreachable from the build sandbox.

## Who writes what

- The **authoritative game server** and **Cloud Functions** write with the **Admin SDK**, which bypasses security rules.
- **Clients** (the Unity app) may only read their own data, change their language and settings, open support tickets and edit their chat block list. Every collection that holds value is `allow write: if false` for clients.
- Anything not listed in the rules is **denied by default**.
- The game server keeps a **`faction` custom claim** (faction key, absent for civilians) on each player's Firebase Auth token, set with the Admin SDK whenever membership changes. Faction member lists and permission data are readable only when this claim matches, so rival factions cannot read each other's org charts, personal grants or treasury limits. The client must refresh its ID token after a faction change.
- Money values are whole rupees (`long`), matching the server `Ledger`. Times are Firestore timestamps (UTC). Daily limits reset at 00:00 IST.

## Collections

| Collection | Key | Main fields | Client access |
| --- | --- | --- | --- |
| `players` | Firebase UID | `displayName`, `language` (`en`/`hi`), `settings`, `cashRupees`, `bankRupees`, `coins`, `vipUntil`, `createdAt`, `lastLoginAt`, `ageConfirmed18` | Read own. Update only `language` and `settings` |
| `characters` | character ID | `uid`, `serverId`, `name`, `appearance`, `jobKey`, `factionKey`, `level`, `playMinutes`. **One character per server (DRAFT – owner review)** | Read own. No writes |
| `inventories` | character ID | `uid`, `slots[]` (`itemKey`, `count`, `durability`), `capacity` | Read own. No writes |
| `vehicles_owned` | vehicle ID | `uid`, `characterId`, `modelKey`, `plate`, `colour`, `fuel`, `damage`, `garageId`, `impounded` | Read own. No writes |
| `houses` | house ID | `ownerCharacterId`, `district`, `priceRupees`, `taxDueAt`, `safe`, `cctv`, `dog`, `lastOwnerActiveAt` | Read (signed in). No writes |
| `businesses` | business ID | `ownerCharacterId`, `kind`, `district`, `staff[]`, `prices`, `lastOwnerActiveAt` (lost after 7 days inactive) | Read (signed in). No writes |
| `factions` | faction key (`police`, `army`, `hospital`, `fire`, `government`, `court`, `media`, gang and mafia keys) | `name`, `headCharacterId`, `headTermEndsAt`, `treasuryRupees`, `slotCap` | Read (signed in). No writes |
| `factions/{key}/members` | character ID | `factionKey`, `rank` (0 = head, larger = lower), `superiorId` (character ID of the direct leader whose unit the member is in; empty for the head), `joinedAt`, `lastActiveAt`, `onDuty` | Read by members of the same faction only (`faction` custom claim). No writes |
| `faction_permissions` | faction key | `rankPowers` (map rank -> list of power keys), `personalGrants` (map character ID -> list of power keys), `rankDailyLimitRupees` (map rank -> ₹), `personalDailyLimitRupees` (map character ID -> ₹), `spentToday` (map character ID -> `{ day, rupees }`), `updatedBy`, `updatedAt` | Read by members of the same faction only (`faction` custom claim). No writes |
| `faction_audit` | auto ID | `factionKey`, `at`, `action` (`hire`, `fire`, `promote`, `demote`, `reassign_unit`, `grant_power`, `revoke_power`, `set_limit`, `spend_treasury`, ...), `actorId`, `targetId`, `detail` | None (server and admin panel only) |
| `transactions` | auto ID | `uid`, `from`, `to` (ledger accounts: cash, bank, faction treasury, escrow, city treasury, sink, source), `amountRupees`, `reason`, `createdAt` | Read own. No writes |
| `auctions` | auction ID | `itemKind` (house, business, vehicle), `itemId`, `status`, `endsAt` (anti-snipe extension), `highBidRupees`, `highBidderId`, `escrowTxId`, `commissionRupees` | Read (signed in). No writes |
| `purchases` | store order ID | `uid`, `store` (`google_play`, `app_store`), `productId`, `purchaseToken` (hashed), `coins`, `status`, `validatedAt` | Read own. No writes |
| `tickets` | auto ID | `uid`, `category`, `message` (max 1000 chars), `createdAt`, then server-only `status`, `assignedAdminCode`, `replies[]`, `rating` | Read own. Create own (only `uid`, `category`, `message`, `createdAt`) |
| `chat_settings` | Firebase UID | `blocked` (max 200 character IDs), `mutedChannels` (max 16) | Read and write own, limited fields |
| `reports` | auto ID | `reporterId`, `targetId`, `reason`, `evidence` (last 2 minutes: positions, chat, voice, combat, transactions), `status`, `handledByAdminCode` | None |
| `bans` | Firebase UID | `kind` (temp, permanent, device), `until`, `reason`, `adminCode`, `appealUsed`, `deviceIds[]` | Read own. No writes |
| `admin_log` | auto ID | `at`, `adminCode` (for example `ADM-27`), `adminLevel` (1-6), `action`, `targetId`, `reason` (required), `evidence` | None |
| `media_ads` | auto ID | `businessId`, `buyerCharacterId`, `channel` (`tv`, `radio`, `billboard`, `paper`, `chat`), `text`, `priceRupees`, `soldByCharacterId` (needs `sell_ads`), `mediaShareRupees` (50%), `cityShareRupees` (50%), `status`, `publishedAt` | Read (signed in). No writes |

Chat messages and voice never go through Firestore. They stay on the game server; only reports keep a short evidence window.

## Faction permission power keys

Stored as strings so the order of bits can change in code without migrating data. They match `FactionPower` in `shared/GangsOfBharat.Shared/Gameplay/Factions/FactionPower.cs`:

`recruit`, `fire`, `promote`, `demote`, `reassign_unit`, `run_interviews`, `post_hiring_ads`, `mute_faction_chat`, `spend_treasury`, `start_convoy`, `manage_armory`, `sell_ads`, `go_live`, `publish_paper`, `manage_permissions`.

The game server enforces the rank rules before writing: a leader can promote only up to one rank below their own and acts only on members in their own unit (members whose `superiorId` chain leads to them).

## Planned Cloud Functions (not written yet)

These need the Blaze plan and Node packages (`firebase-functions`, `firebase-admin`, Google APIs) that cannot be installed here, so only the design is recorded.

| Function | Trigger | Job |
| --- | --- | --- |
| `validatePurchase` | HTTPS callable | Check a Google Play purchase token (Play Developer API) or App Store transaction (App Store Server API), reject replays, write `purchases`, then call `grantCoins` |
| `grantCoins` | Internal (called by `validatePurchase` and Owner admin tools) | Add coins to `players/{uid}.coins` in a transaction and write a `transactions` entry |
| `issueGameServerToken` | HTTPS callable | Verify the Firebase ID token and ban status, then return a short-lived signed session token for the UDP game server |
| `settleAuction` | Scheduled (every minute) | Close ended `auctions`, move escrow to the seller minus commission, refund losing bids, transfer ownership |
| `nightlyBackup` | Scheduled (03:00 IST) | Export Firestore to a Cloud Storage bucket and prune old exports |

## Spark vs Blaze

| Item | Spark (free) | Blaze (pay as you go) |
| --- | --- | --- |
| Firestore reads | 50,000 per day | 50,000 per day free, then billed |
| Firestore writes | 20,000 per day | 20,000 per day free, then billed |
| Cloud Functions | Not available | Available (needed for purchases, auctions, tokens, backups) |
| Scheduled exports (backups) | Not available | Available |

With 1000 players the Spark read limit runs out quickly, so **Blaze is needed** at launch. The game server batches writes (for example saving characters every few minutes and at logout) to keep costs low.

## Deploy (once Firebase is reachable)

```bash
cd firebase
firebase login
firebase use <project-id>
firebase emulators:start --only firestore   # test the rules locally first
firebase deploy --only firestore:rules,firestore:indexes
```
