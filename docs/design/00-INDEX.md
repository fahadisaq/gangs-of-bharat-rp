# GANGS OF BHARAT: Game Design Document

- **Version:** 0.1
- **Status:** agreed design from the owner discussion, with open items marked for owner review
- **PDF:** [docs/GangsOfBharat_GDD.pdf](../GangsOfBharat_GDD.pdf), generated with `python3 tools/docs/md2pdf.py --out docs/GangsOfBharat_GDD.pdf docs/design/*.md`. Hindi (Devanagari) lines appear only in the Markdown files.

## Status legend

| Label | Meaning |
| --- | --- |
| (no label) | Decided by the owner |
| DRAFT – owner review | Proposal that the owner still has to approve or change |
| TBD / set during balancing | Value chosen during playtests, not a design decision |

## Contents

| # | Chapter |
| --- | --- |
| 01 | [Vision and Scope](01-vision-and-scope.md) |
| 02 | [World: Mayanagri](02-world-mayanagri.md) |
| 03 | [Story](03-story.md) |
| 04 | [Factions](04-factions.md) |
| 05 | [Justice and Crime](05-justice-and-crime.md) |
| 06 | [Events](06-events.md) |
| 07 | [Economy](07-economy.md) |
| 08 | [Market, Auction House and Houses](08-market-houses.md) |
| 09 | [Injury and Health](09-injury-health.md) |
| 10 | [Communication](10-communication.md) |
| 11 | [Monetization](11-monetization.md) |
| 12 | [Admin and Moderation](12-admin-moderation.md) |
| 13 | [Technical Architecture (summary)](13-tech-architecture.md) |
| 14 | [Art and Audio Pipeline](14-art-audio-pipeline.md) |
| 15 | [Extras](15-extras.md) |
| 16 | [Legal and Operations](16-legal-ops.md) |
| 17 | [Build Roadmap](17-roadmap.md) |

Related docs: [Architecture](../ARCHITECTURE.md), [Roadmap](../ROADMAP.md), [Environment Status](../ENVIRONMENT_STATUS.md).

## Open items (DRAFT – owner review)

| Item | Proposal | Where |
| --- | --- | --- |
| Map layout | Seven districts: slum, market/old city, business district, rich hills, sea-link + beach, port + highway, village outskirts | [02. World](02-world-mayanagri.md) |
| City name | "Mayanagri" | [02. World](02-world-mayanagri.md) |
| Intro script | 7 beats over about 25 minutes, sample lines | [03. Story](03-story.md) |
| Character creation | Name, gender, face, hair, skin tone, clothes, language | [03. Story](03-story.md) |
| Gang names | Regional names, one gang per area | [04. Factions](04-factions.md) |
| Court seat split | 1 Chief Justice, 4 Judges, 4 Prosecutors, 6 Clerks; salaries not set | [04. Factions](04-factions.md) |
| Probation length | 3 days | [04. Factions](04-factions.md) |
| Text interviews | Whether text interviews may replace voice interviews | [04. Factions](04-factions.md) |
| Recruitment windows | Fixed hiring periods vs always open | [04. Factions](04-factions.md) |
| Character Kill rule | CK only with the player's agreement | [05. Justice and Crime](05-justice-and-crime.md) |
| Drugs | No drugs; generic contraband instead | [05. Justice and Crime](05-justice-and-crime.md) |
| Bribery | Allowed as RP, logged on the server | [05. Justice and Crime](05-justice-and-crime.md) |
| Default faction permissions | Rank 0 all powers; rank 1 all except `manage_permissions`; middle leader ranks hire/fire/promote/demote/reassign/interviews/hiring ads/mute; two lowest ranks none | [04. Factions](04-factions.md) |
| Treasury daily limits | Rank 0 unlimited, rank 1 ₹50,000 per day, personal grant without a limit and any other rank given `spend_treasury` ₹10,000 per day; reset 00:00 IST | [04. Factions](04-factions.md) |
| Mass Media ad share | 30% of each ad income split equally among current Media members, paid at each member's own payday; shares are locked and cannot be spent from the treasury | [04. Factions](04-factions.md) |
| Ad chat burn | Whether part of the ad chat fee is burned instead of the 50/50 Mass Media / city treasury split | [10. Communication](10-communication.md) |
| Phone apps | Map, bank/UPI, faction, jobs, market, tickets, news (Mass Media paper), social, camera | [10. Communication](10-communication.md) |
| In-game voice calls | In-game player-to-player calls, never real phone calls | [10. Communication](10-communication.md) |
| Coin pack prices | ₹79 / ₹159 / ₹399 / ₹799 / ₹1,999 | [11. Monetization](11-monetization.md) |
| Premium resale | Resale for in-game ₹ with a 10% fee | [11. Monetization](11-monetization.md) |
| Admin details | Powers per level, panel layout | [12. Admin and Moderation](12-admin-moderation.md) |
| One character per server | One character per account per server | [01. Vision and Scope](01-vision-and-scope.md) |
