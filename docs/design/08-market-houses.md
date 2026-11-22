# 08. Market, Auction House and Houses

## Car market (fixed price)

- Players list vehicles for a **fixed ₹ price**.
- Payment is **escrowed**: the server holds the buyer's ₹ and transfers the vehicle and the money at the same moment. Neither side can scam the other.
- A commission is taken as a money sink.

## Auction house

Items: **cars, houses, businesses, plates and rare items**.

| Rule | Value |
| --- | --- |
| Starting price | Seller minimum, never below a **system floor** |
| Bid step | **5%** above the current bid |
| Duration | **24-72 hours** (seller chooses) |
| Anti-snipe | A bid in the final 2 minutes extends the auction by **+2 minutes** |
| Bids | **Escrowed**: the bid amount is locked from the bidder's balance and returned when outbid |
| Commission | **5-10%** of the final price |

Government auctions sell businesses lost after 7 days of owner inactivity.

## Property sale and rent

- Houses can be **sold** to other players (escrowed) or **rented** out for a weekly rent.
- Real estate agents earn a commission on deals.

## Houses

- Range from a **slum room** (₹50k) to a **flat** (₹20L), **bungalow** (₹3Cr) and **villa**.
- **Storage** for items and money, **decoration**, garage space for larger houses.
- **Weekly property tax**.

## House robbery rules

A house can be robbed only when all of these are true:

- The **owner is online**.
- **At least 3 police are online.**
- The robber has a **lockpick or drill**.

When a robbery starts:

- An **alarm** goes to the **owner and the police**.
- A robbery takes **at most 30% of the storage**.
- **Premium items are never stolen.**
- Owners can protect a house with a **safe**, **CCTV** and a **dog**.
