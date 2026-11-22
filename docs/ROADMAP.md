# Roadmap

All features in the [Game Design Document](design/00-INDEX.md) ship in **one full release**, after private test builds and a closed beta. The stages below are the build order. Each stage ends with something testable, so problems with performance, netcode and fun show up early. The same stages are in [docs/design/17-roadmap.md](design/17-roadmap.md).

Launch target: **1 server, up to 1000 players**, Android first, then iOS.

## Stage 1: Setup

- Repo, GDD (Markdown + PDF), secrets policy.
- Shared Unity-compatible C# core, authoritative .NET 8 UDP server with interest management (50-100 nearest players per client), full chat channel set and voice relay.
- Console test runner and bot load tester.
- Gameplay rule services (economy ledger, factions, duty/payday, injury timers, wanted, auctions).
- Scaffolds: Unity client, Sarvam/Gemini content scripts, Blender generators, Firebase config, web admin panel.
- **Exit:** server builds and tests pass offline; owner approves the GDD.

## Stage 2: First playable district

- Slum start zone plus the edge of the market.
- Walk, run, drive (auto, Marutha, Tatva truck, bike, police jeep).
- **About 20 players**, local/global/faction chat and proximity voice.
- Firebase login (Google; Sign in with Apple on iOS) and cloud save.
- Hindi and English UI.
- **Exit:** 20 real players on phones for 1 hour without desync or crash; save/load works.

## Stage 3: City life

- Jobs with ₹ pay, banks, ATMs, UPI, loans, FDs.
- Phone: SMS with recharge, apps.
- Houses (slum room, flat, bungalow), showroom, needs.
- **Exit:** a new player can earn, buy a vehicle and a room, and keep them after relog.

## Stage 4: Factions, police and wanted

- Faction hierarchy, duty and payday, all rank/salary tables, recruitment and tests.
- Wanted 1-5, arrest, jail, court with real judges and lawyers.
- Injury, 108 ambulance, hospital.
- **Exit:** one full arrest -> court -> verdict cycle with real players; payday matches the tables.

## Stage 5: Story intro

- 25-minute voiced intro with comic-panel cutscenes, Sarvam voice in Hindi and English.
- **Exit:** new testers finish the intro without help.

## Stage 6: Store and purchases

- IAP coins (Google Play Billing, App Store IAP), premium cars, VIP, 60-day battle pass, plates, pets.
- Server-side purchase validation in Cloud Functions (Blaze plan).
- **Exit:** sandbox purchases validated and credited on both stores; refunds remove coins.

## Stage 7: 1000-player bot load test

- 1000 bots against the target server machine; numbers in `docs/LOADTEST.md`.
- **Exit:** tick time and bandwidth inside budget.

## Stage 8: Closed beta

- Invited players, all features, trained admins, events running on schedule.
- **Exit:** stable sessions and economy for the beta period; critical bugs fixed.

## Stage 9: Android launch

- Full release on Google Play.

## Stage 10: iOS launch

- App Store release built on a cloud Mac.

## What the project owner must provide or do

| Need | Why | When |
| --- | --- | --- |
| Firebase project on the Blaze plan | Auth, Firestore beyond Spark limits, Cloud Functions for purchases and auctions | Stage 2 |
| New Gemini API key (rotate the one shared in chat) | Textures, signs, UI icons, story stills | Stage 2 |
| New Sarvam API key (rotate the one shared in chat) | Dialogue, translation, TTS, STT | Stage 2-5 |
| A machine or CI with Unity (GitHub Actions with game-ci, or a PC) | This sandbox cannot run Unity | Stage 2 |
| Unity license secrets in CI (change the password shared in chat first) | Headless builds | Stage 2 |
| Android phones (high-end and mid-range) | Real performance and feel | Stage 2 onwards |
| Game server host (Oracle Cloud Always Free ARM to start, paid host for 1000 players) | Always-on real-time simulation | Stage 2 test, Stage 8 beta |
| Google Play developer account | Testing tracks and release | Stage 2-6 |
| Apple developer account and a cloud Mac | iOS builds and App Store | Stage 6-10 |
| Company and GST registration, ToS, Privacy Policy, trademark check | Store payouts and legal compliance | Before Stage 8 |
| Playtesters (10-30 friends), then closed beta players | Social RP needs real people | Stage 2 onwards |
| Human admins and community managers | Moderation from day one | Stage 8 onwards |
| Decisions on all "DRAFT – owner review" items | Final design | Before Stage 8 |
