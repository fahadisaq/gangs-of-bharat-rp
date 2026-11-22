# 11. Monetization

## Principles

- **Fixed prices only. No loot boxes**, no random rewards for money.
- **No cash-out** of anything (coins, ₹, items, accounts). See [16. Legal and Operations](16-legal-ops.md).
- Follow the **CCPA Guidelines for Prevention and Regulation of Dark Patterns, 2023** (India): no false urgency, no basket sneaking, no confirm-shaming, no forced action, no subscription traps, no disguised ads, clear prices before purchase.
- Prices shown include **18% GST**.
- Final pricing is decided by the owner.

## Premium coins

- Coins are bought **only with real money through Google Play Billing (Android) or App Store In-App Purchase (iOS)**.
- Purchases are validated on the server (Cloud Functions) before coins are credited.
- Coins cannot be bought with ₹ and cannot be converted to ₹.

Coin packs (price per pack, coins per pack not decided):

| Pack | Price |
| --- | --- |
| Pack 1 | ₹79 |
| Pack 2 | ₹159 |
| Pack 3 | ₹399 |
| Pack 4 | ₹799 |
| Pack 5 | ₹1,999 |

**DRAFT – owner review**: coin pack prices and coin amounts.

## Premium cars

- **Premium cars are available ONLY with real money via IAP coins.** They cannot be bought with ₹.
- **Rotating store** controlled by the owner (from the admin panel), with **limited editions**.
- Promotion through in-game **billboards**, **news** ads and **push notifications** (opt-in).
- **Resale**: premium cars can be resold to other players for in-game ₹ with a **10% fee**. **DRAFT – owner review**: premium resale.

## Other premium items

| Item | Notes |
| --- | --- |
| VIP membership | **Monthly**. VIP chat, VIP queue, small convenience bonuses |
| Battle pass | **60-day** season, fixed price, rewards shown up front |
| VIP number plates | Custom plates |
| Pets | Cosmetic companions (also give house protection for dogs) |

## Store rules (Google Play and App Store)

- All digital goods (coins, VIP, battle pass) are sold through **Google Play Billing** on Android and **Apple In-App Purchase** on iOS, as both stores require for in-app digital content.
- Prices are set per store in INR. Store fees apply to every sale.
- Refunds follow the store's refund process and our refund policy (see [16. Legal and Operations](16-legal-ops.md)). Refunded coins are removed; abuse leads to a ban.
- No external payment links inside the app.
