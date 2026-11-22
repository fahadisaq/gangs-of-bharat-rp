# Technical Architecture

## Overview

```
 Android / iOS (Unity 6 LTS, URP)
   |  UDP: inputs up, snapshots down, voice, chat
   v
 Dedicated game server (.NET 8, authoritative, Linux VM)
   |  HTTPS (service account): load/save profiles, economy writes
   v
 Firebase: Auth, Firestore, Cloud Functions (receipt validation)
   ^
   |  HTTPS: sign-in, ID token, store purchase flow
 Unity client
```

The client only renders, plays audio and sends inputs. The game server owns the world. Firebase owns identity and persistent data.

## Client (Unity)

- **Engine**: Unity 6 LTS with the Universal Render Pipeline (URP). URP is the standard choice for high-end mobile: forward+ rendering, SRP Batcher, GPU Resident Drawer and good Android/iOS support.
- **Structure**: `client/GangsOfBharat.Unity/` with the shared C# core (`shared/GangsOfBharat.Shared/`) imported as source through an asmdef, so client and server use the exact same protocol, catalogs and localization keys. Shared code stays C# 9 with no nullable annotations to match Unity's compiler.
- **Networking client**: Unity's UDP socket (`System.Net.Sockets`) with the shared BitWriter/BitReader. Remote entities are interpolated about 100 ms behind; the local player and vehicle are predicted and reconciled against server state.
- **Streaming**: the city is split into district scenes loaded with Addressables, plus LOD groups and impostors for far buildings.
- **Input**: virtual joystick, camera drag, contextual buttons (enter car, talk, interact), like Grand Mobile.

## Dedicated server (.NET 8)

- **Authoritative**: clients send inputs (move vector, buttons, vehicle throttle/steer). The server simulates movement, vehicles, jobs, money and combat, then sends snapshots. The client never decides money, inventory, damage or position outcomes.
- **Transport**: UDP with a small custom reliability layer:
  - Every packet has a sequence number and an ack plus a 32-bit ack bitfield.
  - **Unreliable channel**: movement snapshots and voice frames. Lost packets are not resent; the next snapshot replaces them.
  - **Reliable ordered channel**: chat, RPCs (job started, purchase result), entity spawn/despawn. Resent until acked.
  - Connection handshake with a token from Firebase Auth (the server verifies the Firebase ID token before accepting a player).
- **Tick rates**: simulation at 20 Hz (50 ms). Snapshots at 20 Hz for nearby entities, 10 Hz for mid range and 5 Hz for far entities. Client input sent at 20-30 Hz.
- **Serialization**: hand-written bit packing, quantized positions (for example 1 cm precision within a 16 km world), quantized yaw (8-10 bits), delta compression against the last acked snapshot. No reflection and no allocations in the hot loop.
- **Interest management**: a uniform spatial hash grid with 64 m cells. Each player receives entities from the cells within a radius (for example 3x3 to 5x5 cells, about 150-300 m), capped to the **50-100 nearest players** so a crowded spot never sends all 1000 players to one client. The same grid answers "who can hear me" for proximity voice and local chat.
- **Capacity**: launch with **1 server, up to 1000 players**. More servers are added later; each server is a fresh start with its own economy.
- **Not Photon**: the Photon free tier (20 CCU) is far too small, and a relay-style service cannot hold an authoritative 1000-player city.

### Rough bandwidth per player

- One nearby entity update: about 12-16 bytes after quantization and deltas.
- A player in a busy spot sees about 40-60 entities. With distance-based rates the average is about 30 entity updates per tick at 20 Hz.
- 30 x 14 bytes x 20 Hz = about 8.4 KB/s, plus headers, chat and voice from nearby speakers. Plan for **10-20 KB/s down per player** and 2-4 KB/s up.

## Voice chat

Two options:

| Option | How | Pros | Cons |
| --- | --- | --- | --- |
| Self-hosted relay (recommended first) | Client encodes mic with Opus (16 kbps, 20 ms frames). Server relays each frame only to players within about 30 m using the interest grid | No per-minute fees, same server, proximity is exact | Adds 2-4 KB/s per active speaker per listener to server bandwidth; needs a native Opus plugin in Unity |
| Hosted voice SFU (Vivox, Agora, LiveKit) | Client joins a positional voice channel managed by the provider | Echo cancellation, noise suppression and scaling handled for you | Per-user or per-minute pricing at scale; position must be pushed to the provider; another SDK and account |

Unity Vivox has a free tier and built-in positional channels, so it is the fallback if the self-hosted relay is too much work for Phase 1. LiveKit can be self-hosted later.

## Chat channels

| Channel | Type | Audience | Cost |
| --- | --- | --- | --- |
| Proximity voice | Voice | Nearby players (interest grid) | Free |
| Local | Text | Nearby players | Free |
| Faction chat | Text | All faction members, map-wide | Free |
| Faction radio | Voice | All faction members, map-wide | Free |
| Group / crew | Text | Group members | Free or small ₹ fee |
| Public / global | Text | Whole server, slow mode | Free |
| VIP | Text | VIP members | VIP membership |
| Ad | Text | Whole server | ₹ per message (money sink) |
| Phone SMS | Text | One-to-one | Needs ₹ recharge |
| In-game phone call | Voice | One-to-one (DRAFT – owner review) | Uses recharge |
| Admin announcement | Text | Whole server | Admins only |

Text goes over the reliable channel, voice over the unreliable channel. The server applies an abuse filter, rate limits and slow mode, keeps report/mute/block lists, lets faction leaders mute members in faction channels, and logs all chat. When a player is reported, the last 2 minutes of evidence are saved. Full rules: [docs/design/10-communication.md](design/10-communication.md).

## Firebase

The Firebase project does not exist yet; the owner will create it.

- **Auth**: Google Sign-In on Android. On iOS, Google plus **Sign in with Apple** (App Store guideline 4.8 requires an equivalent privacy-focused login when a third-party login is offered).
- **Firestore**: player profiles, inventory, owned vehicles, houses, faction membership, bans. Collections are keyed by Firebase UID. The free Spark plan allows only **50K reads/day** (20K writes/day), so the **Blaze plan is needed** once real players join. The game server batches writes to keep costs low.
- **Cloud Functions** (Blaze only): **purchase validation** (Google Play Developer API, App Store Server API) and crediting coins, **auction settlement** (escrowed bids, anti-snipe, commission), admin tools.
- **Analytics, Crashlytics, FCM** (push), **Remote Config** (event times, balancing values, feature flags).
- **Who writes economy data**: only the game server (with a service account) and Cloud Functions. Firestore security rules make economy fields read-only for clients. If the client could write money, any modified APK could give itself infinite ₹. The game server already simulates the world, so it is the right place to validate earnings and spending, then persist them in batches (every 30-60 s and on important events like purchases).

## Hosting cost

Firebase covers login and saves cheaply, but it cannot run a real-time city. A real-time game needs a process that runs 20 times per second, all day, holding every player's position in memory and sending them updates. That needs an always-on CPU and a lot of outbound bandwidth, which is why a dedicated server costs money even when Firebase is free.

Budget from the design (see [docs/design/16-legal-ops.md](design/16-legal-ops.md)): testing ~$0-20/month, 1 server ~$100-250/month, 5 servers ~$600-1,200/month.

| Scale | Bandwidth (15 KB/s per player) | Machine | Rough monthly cost |
| --- | --- | --- | --- |
| 20 players (Phase 1 test) | 0.3 MB/s | 2 vCPU, 4 GB VPS | 5-15 USD |
| 100 CCU | 1.5 MB/s (12 Mbit/s), up to about 4 TB/month at full load | 4 vCPU, 8 GB VPS or small dedicated | 20-50 USD |
| 1000 CCU | 15 MB/s (120 Mbit/s), about 15-40 TB/month | 8-16 core dedicated server with unmetered 1 Gbit/s port | 60-200 USD on providers with included traffic (Hetzner, OVH); 1500+ USD on AWS/GCP because of per-GB egress fees |

Choose a host in or near India (Mumbai region) for low ping. Bandwidth pricing matters more than CPU, so prefer providers with included or unmetered traffic.

### Candidate free host: Oracle Cloud Always Free (ARM)

- Ampere A1: **4 cores, 24 GB RAM, 10 TB egress per month**, regions Mumbai or Hyderabad.
- The .NET 8 server runs natively on linux-arm64. Any Unity headless server build would need its ARM64 Linux build tested first.
- 10 TB/month is about 3.9 MB/s on average, which is about 250 players at 15 KB/s around the clock. That is enough for testing and early beta; a busy 1000-player server needs a paid plan or a paid host.
- Always Free ARM capacity is often unavailable in popular regions and idle instances can be reclaimed, so keep a paid fallback ready.

## Operations

- Daily database backups and a backup before every update.
- Daily server restart at a low-traffic time, with in-game warnings.
- Bot load test to 1000 players before the closed beta (results in `docs/LOADTEST.md`).

## Admin panel

A web admin panel (`admin/`, Node 22 with built-in modules only) talks to the game server and Firebase with admin credentials. Pages: live players, tickets, bans, economy charts, store control, faction leaders, event scheduler and admin logs. Admins appear in game only as codes (for example ADM-27), every action is logged with a reason, and only the Owner can see players' real info. Details: [docs/design/12-admin-moderation.md](design/12-admin-moderation.md).

## Scaling plan

1. **One server, 1000 players**: simulation loop, spatial grid capped to the 50-100 nearest players, delta snapshots, distance-based rate reduction.
2. **Bot load test**: `tools/GangsOfBharat.Bots` spawns N UDP clients that walk and drive around, and reports tick time, bandwidth per client and entities per snapshot. Run at 100, then 300, then 1000.
3. **Headroom**: if the tick goes above 25 ms at 1000, split snapshot building across worker threads (per client) and the simulation into zones. When more capacity is needed, add more servers ("Mumbai-1", "Mumbai-2"), each a fresh start, rather than one shared world.

## Security

- Secrets only in environment variables or CI secrets, never in source. The client ships no private keys.
- Server authority for money, inventory, health, damage and purchases; the client sends inputs only.
- Server-side validation of every input: speed limits, teleport detection, interaction distance checks, cooldowns.
- Transaction logs for every money and item change; root and emulator detection on the client; account and device bans.
- Rate limits on chat, RPCs and connection attempts; reject malformed packets early.
- Firebase ID token verification on connect; ban list checked on connect.
- Purchases credited only after server-side receipt validation.
- Firestore rules deny client writes to economy and inventory fields.
