# 10. Communication

Players talk with **voice and text inside the game**. There are no real phone calls and no real SMS; everything runs through the game server.

## Channels

| Channel | Type | Range / audience | Cost | Moderation |
| --- | --- | --- | --- | --- |
| Proximity voice | Voice | Players near the speaker (fades with distance) | Free | Report, mute, block; AI voice moderation |
| Local chat | Text | Players nearby | Free | Report, mute, block; abuse filter |
| Faction chat | Text | All members of the player's faction, **map-wide** | Free | Abuse filter; leaders with `mute_faction_chat` mute members of their own unit |
| Faction radio | Voice | All members of the player's faction, **map-wide** (push-to-talk) | Free | Leaders with `mute_faction_chat` mute members of their own unit |
| Group / crew chat | Text | Members of a player-made group or crew; visible to all members | Free or a small ₹ fee (set during balancing) | Report, mute, block; abuse filter |
| Public / global chat | Text | Whole server, with **slow mode** (one message every few seconds) | Free | Slow mode; report, mute, block; abuse filter |
| VIP chat | Text | VIP members only | Needs **VIP membership** | Report, mute, block; abuse filter |
| Ad chat | Text | Whole server, ad format | **₹ per message** (money sink: 50% to the Mass Media treasury, 50% to the city treasury) | Abuse filter; report |
| Phone SMS | Text | One-to-one, any distance | Needs **₹ phone recharge** | Block; report; abuse filter |
| In-game phone call | Voice | One-to-one, any distance | Uses phone recharge | Block; report (DRAFT – owner review) |
| Admin announcements | Text | Whole server | Admins only | Admins only |
| Mass Media radio broadcast | Voice (listen-only) | Everyone tuned in on a car radio | Free to listen | Hosted only by Mass Media members with `go_live`; report |
| Mass Media TV broadcast | Voice and video (listen-only) | Everyone watching on TV screens and the phone | Free to watch | Hosted only by Mass Media members with `go_live`; report |

**DRAFT – owner review**: whether part of the ad chat fee is burned (removed from the economy) instead of the full 50/50 split.

## Faction radio

Faction members hear their faction radio anywhere on the map, even when they are not near each other (for example an army or police unit spread across the city). Radio works only for members of that faction.

## Mass Media broadcasts

- Radio shows are hosted live by Mass Media members (RJs and others with `go_live`) and play in **every car radio** tuned to the station. Listeners can send song and shout-out requests by SMS.
- TV broadcasts (live and breaking news, debates, event coverage) are hosted by members with `go_live` and shown on TV screens in the city and in the phone.
- Listeners and viewers cannot talk on a broadcast; guests join only when the host invites them.

## In-game phone

- **Messages (SMS)** one-to-one, needing a ₹ **recharge** bought at 24/7 stores or through the phone.
- **In-game voice calls** between players. **DRAFT – owner review**: the owner's "not real call" was unclear; this is drafted as in-game calls (player to player through the game server, never real phone calls).
- Other apps: map, bank/UPI, faction app (hiring posts, duty, permissions screen for the head), jobs, market and auction house, support tickets, news (the Mass Media daily paper), social app, camera. **DRAFT – owner review**: the final list of phone apps.

## Moderation tools

- **Report, mute and block** on every channel and on every player card.
- **Abuse filter** on all text channels (Hindi and English word lists, plus AI voice moderation as an extra).
- **Faction leaders with `mute_faction_chat`** can mute members **of their own unit** in faction chat and faction radio. The head can mute anyone in the faction. Nobody can mute a member of equal or higher rank. Every mute is written to the faction audit log.
- All chat is **server-logged** for reports; voice used in a report is saved from the last 2 minutes of evidence (see [12. Admin and Moderation](12-admin-moderation.md)).
