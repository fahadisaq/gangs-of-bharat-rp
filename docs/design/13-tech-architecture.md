# 13. Technical Architecture (summary)

The full technical design is in [docs/ARCHITECTURE.md](../ARCHITECTURE.md). This chapter summarizes the decisions.

## Server model

- **1 server** at launch, **up to 1000 players** online. More servers later; each server is a fresh start.
- **Authoritative dedicated server** (.NET 8, runs natively on linux-x64 and linux-arm64). The client sends **inputs only**; the server simulates and sends snapshots.
- **Interest management**: a spatial grid so **each client receives about 50-100 nearby players**, not all 1000. Proximity voice and local chat use the same grid.
- **Photon free tier (20 CCU) is too small** for this game, so it is not used.

## Hosting

- **Candidate free host: Oracle Cloud Always Free ARM** (Ampere A1): **4 cores, 24 GB RAM, 10 TB egress per month**, regions **Mumbai or Hyderabad**.
- Caveats: Always Free ARM capacity is often unavailable in popular regions, idle instances can be reclaimed, and 10 TB/month is about 3.9 MB/s on average. At 15 KB/s per player that is an **average of about 250 players around the clock**, so a full 1000-player server needs a paid plan or a paid host. The free tier is for testing and early beta.
- Our .NET server runs natively on linux-arm64. If a Unity headless server build is used for any part (for example server-side physics), the **Unity ARM64 Linux server build must be tested** on the Oracle machine first.
- Why a server costs money at all: Firebase handles login and saves, but a live city needs a process running 20 times per second all day, holding every player in memory and sending about 10-20 KB/s to each player. That is constant CPU and bandwidth (see costs in [16. Legal and Operations](16-legal-ops.md)).

## Firebase

| Service | Use | Plan note |
| --- | --- | --- |
| Auth | **Google** sign-in on Android; on iOS also **Sign in with Apple** | Free |
| Firestore | Profiles, inventory, vehicles, houses, factions, bans | **Spark = 50K reads/day** (and 20K writes/day), so **Blaze is needed** with real players |
| Cloud Functions | **Purchase validation** (Google Play / App Store) and **auctions** | **Blaze only** |
| Analytics | Funnels, retention, economy events | Free |
| Crashlytics | Crash reports | Free |
| FCM | Push notifications (events, auctions, store) | Free |
| Remote Config | Feature flags, event times, balancing values | Free |

The Firebase project does not exist yet; the owner will create it.

## Anti-hack

- **Server authority** for money, inventory, health, damage and purchases.
- **Client sends inputs only.**
- **Speed and teleport checks**, interaction distance checks, rate limits.
- **Transaction logs** for every money and item change.
- **Root and emulator detection** on the client.
- **Account and device bans.**

## Operations

- **Backups** of the database and server state (daily, plus before every update).
- **Daily restarts** at a low-traffic time with in-game warnings.
- **Bot load test to 1000** simulated players before closed beta (`tools/GangsOfBharat.Bots`, results in `docs/LOADTEST.md`).
