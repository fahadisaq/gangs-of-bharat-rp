# Load Test Results

Bot load test of the authoritative server (`server/GangsOfBharat.Server`) with `tools/GangsOfBharat.Bots`.

## Setup

| Item | Value |
| --- | --- |
| Machine | Build sandbox VM: 8 vCPU (Intel Xeon Platinum 8488C), 30 GB RAM, Amazon Linux 2023 x86_64, no GPU |
| Network | Loopback only (127.0.0.1). No real latency, jitter or internet packet loss |
| Runtime | .NET 8 (SDK 8.0.426), Release build, workstation concurrent GC |
| Process layout | Server and all bots in **one process** (in-process server mode). They share CPU, memory and GC pauses |
| Server settings | 20 Hz tick, 4 snapshot worker threads, cap 100 entities per snapshot, interest radius 300 m, parked vehicles = max(200, bots / 2) |
| UDP receive buffer | Requested 8 MB; the kernel caps it at about 416 KB (`net.core.rmem_max` = 212992, cannot be changed here) |
| Date | 2026-11-22 (sandbox clock) |

Each bot has its own UDP socket and runs the shared `ClientConnection` and `Movement` code. 1000 bots run on 4 threads (250 bots per thread, one 50 ms step each). Bot behaviour:

- 70% walk (and sprint half the time) towards random points inside the test area; 30% are drivers that walk to the nearest free parked vehicle, enter it and drive with random steering.
- Every bot sends input at 20 Hz with its predicted position, which the server checks for teleports.
- Chat about every 20 s per bot: 80% local, 5% global, 10% faction, 5% crew; Hindi and English text.
- Voice: every 30 to 90 s a bot "talks" for 1 to 3 s, sending a 40-byte fake Opus frame every 20 ms (80% proximity, 20% faction radio for faction members).
- Factions from dev tokens: 8% police, 6% army, 4% hospital, 12% gang, the rest civilians; 10% VIP.

The **area** is the side of the square the bots spawn and wander in, centred on the city. 8000 m spreads them over the whole 8 km map; 1000 m and 300 m are crowd tests (a busy district and a packed event such as a turf war or a concert).

## Commands

```bash
source scripts/dotnet-env.sh
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 100 --seconds 20
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 1000 --seconds 20 --area 8000
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 1000 --seconds 20 --area 1000
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 1000 --seconds 20 --area 300
```

Each run measures 20 s after all bots are connected and a 3 s warm-up. Add `--host H --port P` to load a separately running server instead (tick times then come from the server's own `stats` log lines).

## Results

| Metric | 100 bots, 8000 m | 1000 bots, 8000 m | 1000 bots, 1000 m | 1000 bots, 300 m |
| --- | --- | --- | --- | --- |
| Bots connected | 100 / 100 (0.4 s) | 1000 / 1000 (3.1 s) | 1000 / 1000 (3.8 s) | 1000 / 1000 (2.9 s) |
| Server tick rate | 20.00 Hz | 20.00 Hz | 20.00 Hz | 20.00 Hz |
| Tick time avg / p99 / max | 0.65 / 0.84 / 1.28 ms | 4.39 / 5.10 / 5.37 ms | 12.26 / 14.60 / 44.92 ms | 15.86 / 17.82 / 38.17 ms |
| Late ticks (over 50 ms) | 0 | 0 | 0 | 0 |
| Avg entities per snapshot | 0.5 | 2.7 | 56.1 | 94.4 (cap 100) |
| Snapshot bytes per client | 0.52 KB/s | 0.99 KB/s | 12.67 KB/s | 21.16 KB/s |
| All downstream per client (snapshots, chat, voice, headers) | 0.91 KB/s | 1.82 KB/s | 14.03 KB/s | 27.50 KB/s |
| Server upload total | 0.71 Mbit/s | 14.2 Mbit/s | 109.6 Mbit/s | 214.8 Mbit/s |
| Chat deliveries per second | 30 | 1,573 | 1,607 | 2,866 |
| Voice frames relayed per second | 131 | 9,006 | 19,784 | 119,466 |
| Packets server to client, lost | 40,000, 0 | 400,771, 0 | 403,223, 0 | 712,038, 0 |
| Packets client to server, lost (whole run) | 0 | 137 | 7,016 | 32 |
| Movement violations / kicks | 0 / 0 | 0 / 0 | 10 / 0 | 0 / 0 |
| Avg RTT (smoothed, includes up to one tick of queueing) | 50 ms | 50 ms | 69 ms | 50 ms |
| Process CPU (server + bots) | 0.09 cores | 0.51 cores | 1.16 cores | 1.56 cores |
| GC collections gen0/1/2, total pause | 1/0/0, 16 ms | 2/1/0, 35 ms | 2/1/0, 36 ms | 3/2/0, 57 ms |

## Interpretation

- **1000 players at a stable 20 Hz works on this machine.** No tick missed its 50 ms budget in any run. The average tick was 4.4 ms with players spread over the city and 15.9 ms with all 1000 packed into 300 m x 300 m, which is the worst case for interest management. The whole process (server plus 1000 bots) used about 1.6 of 8 cores at worst, so the server alone uses less.
- **The interest cap works.** In the packed test each client received on average 94 entities per tick, never more than the cap of 100 (the unit test `SnapshotsAreCappedAtMaxEntities` checks the hard limit). Without the cap each client would get about 1000.
- **Bandwidth depends on crowding.** Spread over the city a client needs under 2 KB/s. A busy district (1000 m) needs about 14 KB/s, inside the 10 to 20 KB/s per player planned in `docs/ARCHITECTURE.md`. The packed 300 m case needs 27.5 KB/s, above plan: 21 KB/s of snapshots (no delta compression yet) plus heavy proximity voice (about 50 listeners per speaker).
- **Server upload is the real cost driver.** 1000 players in busy districts produce about 110 Mbit/s, and a packed event about 215 Mbit/s. At 110 Mbit/s around the clock that is about 36 TB per month, far above the 10 TB per month of Oracle Cloud Always Free. This confirms the hosting note in `docs/ARCHITECTURE.md`: a full 1000-player server needs a host with unmetered or cheap traffic.
- **Client-to-server loss on loopback.** Server-to-client loss was 0 in every run, but the server's socket dropped some incoming packets: 137 and 32 in two runs and 7,016 in the 1000 m run, where one tick also took 45 ms and the RTT rose to 69 ms. The kernel caps the receive buffer at about 416 KB, which holds only about 20 ms of incoming traffic at 1000 players (roughly 25,000 packets per second, each taking about 1 KB of kernel buffer), so any short stall of the receive thread (GC pause, scheduler) drops packets. The 10 movement violations in that run came from the same burst. No player came close to the kick threshold of 10 violations in 10 s. An earlier run, before the bots stopped allocating an `EndPoint` per received datagram, had more GC pressure and lost 24,676 packets with 5 kicks; the final runs above use the fixed bots.
- **Numbers are optimistic in some ways and pessimistic in others.** Loopback has no real latency, jitter or loss, and the in-game simulation is light (no combat, physics or persistence yet). On the other hand the bots share the server's process, CPU and GC.

## Optimisation ideas (not needed for 20 Hz today)

1. **Raise the socket buffer on the real host** (`sysctl -w net.core.rmem_max=26214400` and `wmem_max`), and use `ReceiveFrom` with `SocketAddress` (.NET 8) on the server to avoid an `EndPoint` allocation per datagram.
2. **Delta-compress snapshots** against the last acked snapshot (skip unchanged yaw or position, send small position deltas). This should cut the 21 KB/s crowd snapshots by about half.
3. **Lower rates in crowds**: when more than about 60 entities are near, send mid and far tiers at 5 and 2.5 Hz, and update the interest set every 2 ticks instead of every tick.
4. **Voice**: limit the number of simultaneous proximity speakers a listener receives (for example the 8 loudest or nearest) and send a speaker's frames in pairs (40 ms) to halve packet overhead.
5. **Run the bots on another machine** (`--host`) to measure the server alone, and repeat on the target host (Oracle Ampere A1 or a paid x86 server) before the closed beta, as planned in `docs/ROADMAP.md`.
