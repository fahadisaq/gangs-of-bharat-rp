# 09. Injury and Health

## Knockdown flow

| Time after knockdown | What happens |
| --- | --- |
| 0 s | The player is **knocked down** (not dead). They can **call 108** (ambulance) from the phone |
| 0 s - 3 min | A **paramedic arriving within 3 MIN can revive** the player on the spot |
| After 50 SEC | A **"Go to hospital" button** appears. Pressing it respawns the player at the hospital |
| At 3 MIN | **Automatic hospital respawn** if nobody revived the player |

## Costs

- A **hospital fee** is charged on respawn or treatment (50% goes to the Hospital faction, see [04. Factions](04-factions.md)).
- **Pocket cash is lost** on a hospital respawn (money in the bank is safe).

## Injuries

| Injury | Effect | Treatment |
| --- | --- | --- |
| Broken leg | Limp, no running | Doctor at the hospital |
| Bleeding | Health drains over time | Bandage or paramedic |
| Gunshot | Heavy damage, bleeding | Paramedic and doctor |

## Health and needs

Health is affected by needs (hunger, thirst, energy, stress), tobacco parodies, alcohol and injuries. The medical store sells first aid and medicine. See [07. Economy](07-economy.md) for needs.

After a hospital respawn the **new life rule** applies (see [05. Justice and Crime](05-justice-and-crime.md)).
