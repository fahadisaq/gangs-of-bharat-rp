# 01. Vision and Scope

## Identity

- **Title:** GANGS OF BHARAT.
- **Genre:** mobile online roleplay (RP) "live a life" city game. Players live a second life in one shared Indian city: they take jobs, earn and spend ₹, buy vehicles, houses and businesses, join factions (police, army, hospital, gangs, mafia and more) and talk to each other with text and voice.
- **Rating:** 18+ only. The game has crime, weapons (including guns, confirmed by the owner; see [05. Justice and Crime](05-justice-and-crime.md#weapons)), alcohol, tobacco parodies and adult themes. An age gate runs at first launch (see [16. Legal and Operations](16-legal-ops.md)).
- **Everything is run by real players.** Police officers, doctors, judges, gang bosses, the mayor and the Mass Media head are players. NPCs exist only for traffic, crowds, shopkeepers in unstaffed shops, the story and AI side content.
- **Engine:** Unity (client for Android and iOS). Authoritative dedicated game server (see [13. Technical Architecture](13-tech-architecture.md)).
- **Setting:** Mayanagri, a fictional Mumbai-inspired megacity (see [02. World](02-world-mayanagri.md)).

## Design pillars

1. **Real people, real roles.** Every important job in the city is a player with a rank, a salary and responsibilities.
2. **Indian culture first.** Chai stalls, dabbawalas, local trains, autos, cows on the road, monsoon, festivals, cricket, Bollywood, Hindi and English side by side.
3. **Fair economy.** Money is earned in-game. Premium items are cosmetic or convenience, fixed price, never random. No cash-out of anything.
4. **Consequences.** Crime leads to wanted levels, arrest, court and jail. Factions have hierarchy, logs and appeals.
5. **Quality on a zero 3D budget.** Free CC0 assets, procedural Blender scripts and AI tools, used carefully (see [14. Art and Audio Pipeline](14-art-audio-pipeline.md)).

## Comparison with existing games

| Game | What they do well | What GANGS OF BHARAT does differently |
| --- | --- | --- |
| Grand Mobile | Large Russian-style RP city on mobile, factions, jobs, car economy | Indian city and culture, Hindi + English voice dialogue, story intro, festivals and cricket events |
| Black Russia | Very large player counts, strong faction RP, admin structure | Clearer anti-abuse rules for admins (codes like ADM-27, full logs), structured court system with real judges and lawyers |
| Rio Rise | Modern mobile presentation, Brazilian setting | Web-series style story chapters with comic-panel cutscenes and Sarvam voice, Indian vehicles and parody brands |

The goal is a game "like these, but better, with Indian culture". It does not try to be a full single-player open-world action game; quality targets are set to what a small team can reach on mobile.

## Release scope

- **All features in this GDD ship in ONE full release.** There is no early-access release with a reduced feature set.
- Before the release there are **private test builds** (team and invited testers) and a **closed beta**.
- Order: private builds -> closed beta -> Android launch -> iOS launch (see [17. Roadmap](17-roadmap.md)).
- Content after release (second city, new events, new vehicles) is added in updates. No wipes.

## Platforms and devices

- **Android first**, then **iOS** (built through a cloud Mac).
- Target: **high-end phones**, with lower graphics presets so mid-range phones can play (see device presets in [14. Art and Audio Pipeline](14-art-audio-pipeline.md)).
- Login: Firebase Auth with Google on Android; on iOS also Sign in with Apple.

## Languages

- **Hindi and English** for UI, subtitles and voiced dialogue.
- All player-facing strings use localization keys with `en` and `hi` tables.
- Players may write chat in any language; auto-translate chat is an extra (see [15. Extras](15-extras.md)).

## Servers and characters

- Launch with **1 server**, up to **1000 players** online. More servers are added later.
- **Each server is a fresh start.** Money, items and characters do not move between servers.
- **One character per server** (assumed). **DRAFT – owner review**: confirm one character per server, or allow more slots per account.
- A character belongs to **one faction at a time**.
