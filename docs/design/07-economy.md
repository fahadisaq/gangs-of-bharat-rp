# 07. Economy

## Currency

- In-game money is the **Indian Rupee (₹)**. Amounts use Indian units: k = thousand, L = lakh (100,000), Cr = crore (10,000,000).
- Premium coins are separate and bought only with real money through Google Play / App Store IAP (see [11. Monetization](11-monetization.md)).
- ₹ can never be cashed out for real money.
- All money changes happen on the server and are written to a transaction log.

## Money sources and sinks

| Sources | Sinks |
| --- | --- |
| Job pay | Taxes (set by the Mayor) |
| Faction salaries (payday) | Fuel |
| Business profits | Repairs |
| Selling vehicles, houses, items to players | Rent |
| Crime (robbery, hafta, contraband, convoy hijacks) | Fines |
| Event rewards, daily rewards | Property tax (weekly) |
| FD interest | Fees (hospital, license, RTO, impound, court) |
| | Commissions (market, auction house, real estate) |
| | Paid chat (ad chat, group chat fee), phone recharge |

## Jobs

| Job | Pay (₹ per hour) | Notes |
| --- | --- | --- |
| Dabbawala | ₹2.5k-3.5k | Lunch box delivery by cycle/train |
| Construction | ₹3k | Building sites |
| Food delivery | ₹3k-4k | Bike deliveries |
| Auto driver | ₹3k-5k | Taxi-style rides |
| Fisherman | ₹3k-5k | Boats at the port and beach |
| Bus driver | ₹3.5k-4.5k | Fixed routes |
| Farmer | ₹3k-6k | Village outskirts, tractors |
| Taxi driver | ₹4k-6k | Car rides |
| Quarry worker | ₹5k-7k | Outskirts quarry |
| Truck driver | ₹6k-10k | Needs a **heavy vehicle license** |
| Mechanic | Per repair/tuning job | Works at mechanic businesses |
| Lawyer | Per case (client fees) | Licensed civilian job |
| Real estate agent | Commission per deal | Works at real estate businesses |
| Shop staff | Wage set by the business owner | Hired by business owners |

Exact rates for per-service jobs are set during balancing.

## Prices

| Item | Price |
| --- | --- |
| Cycle | ₹5k |
| Bike | ₹60k |
| Marutha (small car) | ₹4L |
| Mahindar (SUV) | ₹15L |
| Ranga Rover | ₹1.5Cr |
| Lambor-Gini | ₹5Cr |
| Slum room | ₹50k |
| Flat | ₹20L |
| Bungalow | ₹3Cr |

## Banks

- Bank **accounts** and **ATMs** around the city.
- **UPI transfers** between players through the phone.
- **Salary** paid into the bank account on payday.
- **Loans** with **repossession** of the asset if the player does not repay.
- **FDs** (fixed deposits) with interest.
- **Bank robberies**: the 16:00 cash van and the Sunday bank vault event (see [06. Events](06-events.md)).

## Businesses

Businesses are bought only with **in-game ₹** (never with coins). Counts are limited per server. **Maximum 1 business per player.**

Rules:

- Stock arrives by **supply trucks** (a job for truck drivers).
- Owners **hire staff** and **set prices within limits** set by the system.
- **Weekly tax** to the treasury.
- Gangs can demand **hafta** (protection money).
- A business is **lost after 7 days of owner inactivity** and goes to a **government auction**.
- When no staff is on shift the business runs as **self-service** (NPC till, lower margin).

| Business | Count | Price |
| --- | --- | --- |
| Chai / pan stall | 30 | ₹10L |
| Dhaba | 15 | ₹40L |
| 24/7 store | 15 | ₹60L |
| Clothing store | 8 | ₹1Cr |
| Petrol pump | 10 | ₹2Cr |
| Mechanic garage | 8 | ₹1.5Cr |
| Bar / club | 6 | ₹3Cr |
| Gym | 5 | ₹80L |
| Used car showroom | 3 | ₹5Cr |
| Cinema | 2 | ₹4Cr |
| Real estate office | 2 | ₹3Cr |
| Taxi company | 2 | ₹2Cr |
| Transport company | 3 | ₹4Cr |
| Port warehouse | 2 | ₹8Cr |
| Medical store | TBD | TBD |
| Casino (chips only) | 1 | ₹15Cr |

The casino uses **chips only**: chips are bought with ₹ and can be exchanged back to ₹ only. Nothing is ever paid out in real money. Medical store count and price are set during balancing.

## Hotels

| Hotel | Purchase price | Room price per night |
| --- | --- | --- |
| 1★ | ₹50L | ₹500 |
| 2★ | ₹1.5Cr | ₹1.5k |
| 3★ | ₹4Cr | ₹4k |
| 4★ | ₹8Cr | ₹10k |
| 5★ sea-facing | ₹20Cr | ₹30k |

The 5★ sea-facing hotel has **suites, a rooftop, a helipad and a wedding hall**.

## Needs

- **Hunger, thirst, energy, health, stress.** Low needs reduce running speed, aim and health over time.
- Vehicles need **fuel**.
- Food from street stalls, dhabas, 24/7 stores and hotels. Medicine from the **medical store**.

## 24/7 items

The 24/7 store sells food, drinks, phone recharge and **parody cigarettes, beedi and pan masala**. These items show **health warnings** (as on real packs in India) and affect health and stress. All brands are parodies.

## Alcohol and drunk stages

Bars and clubs sell alcohol. Each drink adds a drunk stage:

| Stage | Effect |
| --- | --- |
| 1-2 | Slight **sway** when walking |
| 3-4 | **Blurred** screen, **slurred** voice/text, worse **steering** |
| 5+ | **Falling** down and **blackout** |

Police carry a **breathalyzer**. Drunk driving is a crime.

## Vehicles

All vehicle names are **parody brands**; no real trademarks are used.

| Type | Parody models |
| --- | --- |
| Indian cars | Marutha, Mahindar |
| Trucks | Tatva trucks |
| Foreign / luxury cars | Ranga Rover, Mercedez, BMV, Lambor-Gini, Ferraro |
| Others | Autos, bikes, buses, tractors, service vehicles (police, ambulance, fire, army), boats, helicopters, planes |

Ownership systems:

- **Garage** (house garage or paid parking) and **showroom** for new vehicles.
- **Used market** (see [08. Market and Houses](08-market-houses.md)).
- **Mechanics**, **tuning** and **wraps**.
- **Fuel**, **damage** and repairs.
- **RTO license** (driving, heavy vehicle, boat, pilot).
- **Insurance**.
- **Impound** for illegally parked or seized vehicles.
- **VIP number plates**.
- **Car theft** (gang activity; logged).

Premium cars are sold only for IAP coins (see [11. Monetization](11-monetization.md)).
