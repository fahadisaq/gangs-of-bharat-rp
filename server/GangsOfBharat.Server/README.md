# GangsOfBharat.Server

Authoritative .NET 8 UDP game server for one Mayanagri city (up to 1000 players). Clients send inputs; the server simulates movement, vehicles, jobs, money and chat, then sends each client a snapshot of the entities near it.

## Run

```bash
source scripts/dotnet-env.sh
dotnet run --project server/GangsOfBharat.Server -c Release -- --port 7777
dotnet run --project server/GangsOfBharat.Server -c Release -- --help
```

Every option can also be set as an environment variable `GOB_<OPTION>` (for example `GOB_PORT=7777`, `GOB_MAX_PLAYERS=1000`). Command-line flags win over environment variables.

| Option | Default | Meaning |
| --- | --- | --- |
| `--port` | 7777 | UDP port |
| `--max-players` | 1000 | Player cap |
| `--tick-rate` | 20 | Simulation and snapshot rate (Hz) |
| `--timeout` | 10 | Seconds without packets before a client is dropped |
| `--max-entities` | 100 | Entities per client snapshot (the nearest ones) |
| `--interest-radius` | 300 | Snapshot radius in metres (near 60 m at 20 Hz, mid 150 m at 10 Hz, far at 5 Hz) |
| `--local-chat-radius` | 30 | Local text chat radius (m) |
| `--voice-radius` | 40 | Proximity voice radius (m) |
| `--slow-mode` | 10 | Global chat: one message per player per N seconds |
| `--ad-cost` / `--sms-cost` | 500 / 2 | Ad chat and SMS prices in rupees (balancing placeholders) |
| `--allow-dev-tokens` | off | Accept `dev:faction=police;crew=3;vip=1;admin=1;wallet=1000;phone=50` tokens. Local testing only |

## What is implemented

- Handshake with protocol version check, server-full and invalid-token rejects, timeouts, kicks.
- 20 Hz fixed tick on a dedicated thread; snapshot building and sending fan out to worker threads.
- Interest management through the shared `SpatialHashGrid` (64 m cells) and `InterestManager` (nearest 100, distance-tiered rates). Large snapshots are split into 64-entity messages.
- Chat routing for every channel in `docs/design/10-communication.md`: Local (30 m), Global (slow mode), Faction (map-wide), Crew, VIP (VIP only), Ad (rupee fee, 50% to the Mass Media fund), SMS (one-to-one, phone balance), Admin announcements (admins only), System (server only). Flood limit of 5 messages per 5 s, mute and block lists, a placeholder abuse word filter, and a log line plus in-memory evidence ring for every chat message.
- Voice relay: proximity frames go to players within 40 m, faction radio to every online member of the sender's faction. Frames are size-capped (256 bytes) and rate-limited (60 per second). Voice content is never logged.
- Vehicle enter/exit (5 m range, one driver), taxi and food delivery jobs (start, reach waypoint, get paid in rupees in memory).
- Anti-cheat: inputs whose reported position teleports or drifts from the server position are rejected; 10 violations in 10 s kicks the player. Jobs finished faster than physically possible are not paid.
- Structured JSON-lines logging to stdout.

## Not implemented yet

- **Firebase ID token verification** (TODO in `ServerCore.TryResolveToken`). Until then every non-dev token joins as an anonymous civilian. Production plan: verify the RS256 signature against Google's public certificates, check `aud`, `iss` and `exp`, then load the profile from Firestore. Real ID tokens are about 1 KB, close to the packet budget, so the client should exchange the ID token over HTTPS (Cloud Function) for a short join ticket and send that instead.
- Persistence of wallets and profiles (everything is in memory).
- Delta compression of snapshots, combat, faction duty and payday.

## Docker

`Dockerfile` builds a framework-dependent image for linux/arm64 (Oracle Cloud Always Free) or linux/amd64. It has not been built yet because Docker is unusable in the build sandbox. Build it from the repository root:

```bash
docker buildx build --platform linux/arm64 -f server/GangsOfBharat.Server/Dockerfile -t gangsofbharat-server .
```

On the host, raise the UDP receive buffer limit before a big test, for example `sysctl -w net.core.rmem_max=26214400` (see `docs/LOADTEST.md`).
