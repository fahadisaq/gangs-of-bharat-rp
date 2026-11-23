# 17. Build Roadmap

All features ship in **one full release** after private test builds and a closed beta. The stages below are the order of building, not separate public releases. The same plan, with owner tasks, is in [docs/ROADMAP.md](../ROADMAP.md).

| # | Stage | Contents | Exit criteria |
| --- | --- | --- | --- |
| 1 | Setup | Repo, GDD, shared C# core, authoritative .NET server, test runner, bot load tester, Unity project scaffold, content scripts, Firebase config, admin panel scaffold | Server builds and tests pass offline; GDD approved by the owner |
| 2 | First playable district | Slum start zone + edge of the market; walk and drive; **~20 players**; local/global/faction chat and proximity voice; Firebase login (Google) and cloud save | 20 real players on phones in one district for 1 hour without desync or crash; save/load works |
| 3 | City life | Jobs, ₹ money and banks, phone (SMS, UPI, apps), houses, showroom, needs | A new player can earn, buy a vehicle and a slum room, and keep them after relog |
| 4 | Factions, police, wanted | Faction hierarchy, duty and payday, all faction tables, wanted 1-5, arrest, jail, court flow, injury and 108 | One full arrest -> court -> verdict cycle with real players; payday matches the salary tables |
| 5 | Story intro | 25-minute voiced intro with comic-panel cutscenes in Hindi and English | New testers finish the intro without help; both languages complete |
| 6 | Store and purchases | IAP coins, premium cars, VIP, battle pass, plates, pets, server-side purchase validation | Test purchases validated and credited on both store sandboxes; refunds remove coins |
| 7 | 1000-player bot load test | 1000 bots on the target server hardware | Tick time and bandwidth inside budget (see `docs/LOADTEST.md`) |
| 8 | Closed beta | Invited players, all features, admins trained | Crash-free sessions and economy stable for the beta period; critical bugs fixed |
| 9 | Android launch | Google Play full release | Store approval; live-ops and admins ready |
| 10 | iOS launch | App Store release via a cloud Mac, Sign in with Apple | App Store approval |

Private test builds run throughout stages 2-7.
