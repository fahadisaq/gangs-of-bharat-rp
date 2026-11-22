# 04. Factions

All salaries are in ₹ **per payday**. Payday is every **30 minutes of active play** (see Duty and payday).

## Hierarchy rules

- **Admins pick only the top head** of each faction, for a **30-day term**. (Exception: the Mayor is elected every 30 days, see Government.)
- The **head appoints the leaders**. **Leaders add, remove and manage their own members.**
- Nobody can act on a member of **equal or higher rank**.
- A leader can **promote up to one rank below their own rank**.
- **Firing needs a written reason**. The fired member has a **3-day appeal** window (to the head, then to admins).
- **7 days of inactivity** removes a member from the faction automatically.
- Members can start a **no-confidence vote** against the head; if it passes, admins pick a new head.
- **Everything is logged**: hires, fires, promotions, demotions, payouts, armory use.
- **Selling ranks for money or items = ban.**
- A character can be in **one faction at a time**.

### Ranks, units and chain of command

- **Many players can hold the same rank.** A faction can have several Colonels, Majors, DCPs, Area Commanders and so on, up to the **seat count** of that rank in the faction's table. A promotion or hire into a rank is possible only while that rank has a free seat.
- **Every member except the head has exactly one direct superior.** The head (rank 0) has no superior.
- A leader's **unit** is everyone below them in their chain of command: their direct reports, the reports of those reports, and so on (their subtree).
- A leader can **hire, fire, promote, demote and reassign only members inside their own unit**. A Colonel manages only their own unit, never another Colonel's unit.
- **Members of the same rank can never fire, demote or otherwise act on each other**, even when one of them has extra powers. Nobody acts on equal or higher rank.
- The **head (rank 0) commands the whole faction**: every member is inside the head's unit.
- **New hires join the hiring leader's unit.** The leader picks who the new member reports to: the leader themself or a member inside the leader's unit (a sub-unit). The new member's rank must be lower than the rank of the member they report to.
- **When a leader leaves or is fired, their direct reports move up** to that leader's own superior. Nobody is left without a superior. If the head leaves, the head's direct reports wait without a superior until admins pick the new head, then report to the new head.
- A leader can **move a member between sub-units inside their own unit** (change the member's direct superior to another member of the unit, or to the leader). The new superior must have a higher rank than the moved member. Members can never be moved into or out of another leader's unit except by a leader who commands both (for example the head).
- Promotion keeps the member's position in the chain: they still report to the same superior. If the promotion would put them at the same rank as their superior, they must first be moved to a higher superior.
- Demotion works the same way: it is refused while one of the member's direct reports would end at the same rank as the demoted member or higher. Those reports must first be moved to another superior.

Examples:

| Faction | Chain of command |
| --- | --- |
| Army | General -> 2 Colonels. Each Colonel has their own Majors, each Major their own Captains, down to Recruits. Colonel A can fire a Major in unit A but not a Major in Colonel B's unit, and cannot act on Colonel B. |
| Police | Commissioner -> 2 DCPs (for example North and South). Each DCP has their own ACPs, each ACP their own Inspectors, SIs and Constables. A DCP manages only their own zone. |
| Gangs | Bhai -> Right Hand -> 4 Area Commanders, each with their own crew of Shooters, Members and Recruits. An Area Commander recruits into and runs only their own crew. |
| Mass Media | Media Head -> 4 Department Heads (TV, Radio, Paper, Digital). Each Department Head leads their own department of Senior Reporters / RJs / Editors, Reporters / Cameramen and Interns. |

## Permissions

These rules apply to **every faction**: Police, Army, Hospital, Fire, Government, Court, Gangs, Mafia and Mass Media.

### Powers

Each power has a stable key used by the server and the faction app.

| Key | Power | Factions |
| --- | --- | --- |
| `recruit` | Hire new members into their own unit | All |
| `fire` | Fire a member of their own unit (with written reason) | All |
| `promote` | Promote a member of their own unit (up to one rank below their own) | All |
| `demote` | Demote a member of their own unit | All |
| `reassign_unit` | Move a member between sub-units inside their own unit | All |
| `run_interviews` | Shortlist applicants and run recruitment interviews and tests | All |
| `post_hiring_ads` | Post hiring ads in the faction app | All |
| `mute_faction_chat` | Mute members of their own unit in faction chat and faction radio | All |
| `spend_treasury` | Spend from the faction treasury, up to a daily limit | All |
| `start_convoy` | Start a convoy (weapon, rations, contraband or smuggling run) | Army, Police, Gangs, Mafia |
| `manage_armory` | Manage the armory / locker stock and gear issue | All |
| `sell_ads` | Sell TV, radio, billboard and paper ads to businesses | Mass Media |
| `go_live` | Start a live TV or radio broadcast | Mass Media |
| `publish_paper` | Publish the daily newspaper in the phone's news app | Mass Media |
| `manage_permissions` | Open the permissions screen and change powers | All (head only, never grantable) |

### Rules

- The **head has every power** of their faction.
- The head opens the **permissions screen** in the faction phone app and **toggles each power per rank**.
- The head can also **grant or revoke a single power for one specific person** (for example "Captain Rahul can recruit"). Revoking removes that personal grant. To take a rank power away from one person, the head changes the rank's powers or demotes the person.
- **Effective powers = rank powers + personal grants.**
- **Nobody can receive a power the head does not have**, and a faction-specific power can only be given inside that faction. **`manage_permissions` can never be given** to anyone (rank or person); only the head has it.
- **A power never lets someone act outside their own unit or on a member of equal or higher rank.** For example a Captain with `recruit` hires only into their own unit and at a rank lower than their own.
- Personal grants stay when the member is promoted or demoted and are removed when the member leaves the faction. Permission settings belong to the faction and stay when the head changes; the new head can change them.
- **`spend_treasury` has a daily limit in ₹**, set **per rank** and optionally **per person**. A personal limit replaces the rank limit for that person. A personal `spend_treasury` grant without a personal limit uses **₹10,000 per day** (DRAFT – owner review). A rank below rank 1 that the head gives `spend_treasury` also starts with a **₹10,000 per day** limit until the head changes it (DRAFT – owner review). Limits **reset at 00:00 IST** server time. A spend that would go over the remaining daily limit is refused.
- **Every permission change is written to the faction audit log**: who made the change, which power, which rank or person, and old value -> new value (including daily limit changes). **Every treasury spend** is logged too (who, amount, reason, remaining daily limit). The head and admins can read the audit log.

### Default permissions

New factions and factions after a server reset start with this table. The head can change it at any time. Rank numbers count from the head: rank 0 is the head, rank 1 the next rank, and so on.

| Rank | Default powers |
| --- | --- |
| Rank 0 (head) | All powers of the faction, including `manage_permissions`; `spend_treasury` **unlimited** |
| Rank 1 | Every power of the faction except `manage_permissions`; `spend_treasury` limit **₹50,000 per day** |
| Middle leader ranks (any rank below rank 1 that has at least two ranks below it) | `recruit`, `fire`, `promote`, `demote`, `reassign_unit`, `run_interviews`, `post_hiring_ads`, `mute_faction_chat` |
| The two lowest ranks | No powers |

Faction-specific additions to the defaults:

- **`spend_treasury`**: by default only ranks 0 and 1 (rank 0 unlimited, rank 1 ₹50,000 per day).
- **`start_convoy`**: ranks 0, 1 and 2 in Army, Police, Gangs and Mafia.
- **Mass Media**: Department Heads (rank 1) have `sell_ads`, `go_live` and `publish_paper` (already included in "every power"). Senior Reporter / RJ / Editor (rank 2) also has `go_live`.

What this means per faction (ranks 0 and 1 not repeated):

| Faction | Middle leader ranks | No powers by default |
| --- | --- | --- |
| Police | DCP is rank 1; ACP, Inspector, SI, Head Constable (ACP also `start_convoy`) | Constable, Trainee |
| Army | Colonel is rank 1; Major, Captain, Lieutenant, Sergeant (Major also `start_convoy`) | Soldier, Recruit |
| Hospital | Senior Doctor is rank 1; Doctor, Nurse | Paramedic, Intern |
| Fire | Station Officer is rank 1; none | Firefighter, Trainee |
| Government | Deputy Mayor is rank 1; Minister | Officer, Security |
| Court | Judge is rank 1; none | Prosecutor, Clerk |
| Gangs | Right Hand is rank 1; Area Commander, Shooter (Area Commander also `start_convoy`) | Member, Recruit |
| Mafia | Consigliere is rank 1; Captain (also `start_convoy`) | Soldier, Associate |
| Mass Media | Department Head is rank 1; Senior Reporter / RJ / Editor (also `go_live`) | Reporter / Cameraman, Intern |

**DRAFT – owner review**: the default permission table, the ₹50,000 daily limit for rank 1 and the ₹10,000 default for personal treasury grants.

## Duty system

- Faction gear, uniform, weapons and vehicles are available **only while on duty**.
- Duty is **started and ended at the base locker** in the faction HQ. Ending duty returns gear to the locker.
- Off-duty members are civilians for gameplay purposes.

## Payday

- Payday every **30 minutes of active play**. AFK time does not count (no AFK pay).
- **Government factions** (Police, Army, Hospital, Fire, Government, Mass Media, Court) are paid **from the city treasury**, with a **minimum salary floor** so pay never drops to zero when the treasury is low.
- **Gangs and Mafia have no salary.** They earn from their activities.

## Switching factions

- Cooldown of **7 days** after leaving a faction before joining another.
- Cooldown of **14 days** when moving from a gang (or mafia) to **Police or Army**.

## Faction slots (1000-player server)

| Faction | Slots |
| --- | --- |
| Police | 80 |
| Army | 60 |
| Hospital | 40 |
| Fire | 20 |
| Government | 25 |
| Mass Media | 30 |
| Gangs (4 x 40) | 160 |
| Mafia (2 x 30) | 60 |
| Court | 15 |
| **Faction total** | **490** |
| Civilians (the rest) | 510 |
| **Total** | **1000** |

In short: Police 80, Army 60, Hospital 40, Fire 20, Government 25, Mass Media 30, Gangs 4x40, Mafia 2x30, Court ~15, the rest civilians. Civilians are always the remainder: 1000 - 490 = 510. Civilians include licensed lawyers, business owners, drivers and every other job.

## Police (80)

| Rank | Count | Salary per payday |
| --- | --- | --- |
| Commissioner | 1 | ₹12k |
| DCP | 2 | ₹8k |
| ACP | 4 | ₹6k |
| Inspector | 8 | ₹4.5k |
| SI (Sub-Inspector) | 12 | ₹3.5k |
| Head Constable | 15 | ₹2.5k |
| Constable | 25 | ₹2k |
| Trainee | 13 | ₹1.5k |
| **Total** | **80** | |

Duties: patrol, traffic control, arrests, wanted system, evidence and charges for court, escorting weapon convoys, prisoner transfer, bank and house robbery response. The Commissioner orders and pays for weapon crates from the police budget (see [06. Events](06-events.md)).

## Army (60)

| Rank | Count | Salary per payday |
| --- | --- | --- |
| General | 1 | ₹12k |
| Colonel | 2 | ₹8.5k |
| Major | 3 | ₹6.5k |
| Captain | 5 | ₹4.8k |
| Lieutenant | 7 | ₹3.8k |
| Sergeant | 10 | ₹2.8k |
| Soldier | 20 | ₹2.2k |
| Recruit | 12 | ₹1.5k |
| **Total** | **60** | |

The army runs the **weapon factory**, the **depot** and the **convoys** that deliver crates to the police armory. It also runs rations convoys and monthly war games.

## Hospital (40)

| Rank | Count | Salary per payday |
| --- | --- | --- |
| CMO (Chief Medical Officer) | 1 | ₹11k |
| Senior Doctor | 4 | ₹6.5k |
| Doctor | 8 | ₹4.5k |
| Nurse | 10 | ₹2.8k |
| Paramedic | 12 | ₹2.2k |
| Intern | 5 | ₹1.5k |
| **Total** | **40** | |

Plus **50% of hospital fees** (the rest goes to the treasury). Paramedics answer 108 calls (see [09. Injury and Health](09-injury-health.md)).

## Fire (20)

| Rank | Count | Salary per payday |
| --- | --- | --- |
| Chief | 1 | ₹9k |
| Station Officer | 3 | ₹5k |
| Firefighter | 10 | ₹2.8k |
| Trainee | 6 | ₹1.5k |
| **Total** | **20** | |

## Government (25)

| Rank | Count | Salary per payday |
| --- | --- | --- |
| Mayor (elected every 30 days) | 1 | ₹13k |
| Deputy Mayor | 1 | ₹9k |
| Minister | 3 | ₹7k |
| Officer | 8 | ₹2.8k |
| Security | 12 | ₹2k |
| **Total** | **25** | |

The **Mayor sets taxes, the budget and laws** (within system limits). The Mayor is **elected every 30 days** by players; admins do not pick the Mayor.

## Mass Media (30)

Faction key: `media`. Mass Media runs every media outlet in the city: TV, radio, the newspaper, official social media pages and the ad agency.

### Departments

| Department | What it does |
| --- | --- |
| TV news channel | Live and breaking news, election debates, event coverage |
| Radio station | Live player-hosted shows heard in every car radio, song and shout-out requests, radio ads |
| Newspaper | Daily paper in the phone's news app, edited by the Paper department |
| Social media / influencers | Official city pages, interviews and posts in the phone social app (Digital department) |
| Ad agency | Businesses pay for TV, radio, billboard and paper ads. Run by the Media Head's office; members with `sell_ads` close the sales |

### Ranks

| Rank | Count | Salary per payday |
| --- | --- | --- |
| Media Head (picked by admins) | 1 | ₹8k |
| Department Head (TV, Radio, Paper, Digital) | 4 | ₹5.5k |
| Senior Reporter / RJ / Editor | 7 | ₹4k |
| Reporter / Cameraman | 12 | ₹2.6k |
| Intern | 6 | ₹1.2k |
| **Total** | **30** | |

Each Department Head leads their own department unit (see Ranks, units and chain of command). Only members with `go_live` can start a TV or radio broadcast, and only members with `publish_paper` can publish the daily paper.

### Income

- **50% of ad chat fees** and **50% of every TV, radio, billboard and paper ad sale** go to the **Mass Media treasury**. The other 50% goes to the **city treasury**.
- Members get their **salary** (paid from the city treasury like other government factions) **plus an ad share**: at each payday, **30% of the ad income the Mass Media treasury received since the previous payday** is taken from the Mass Media treasury and **split equally among the Mass Media members paid that payday**.

**DRAFT – owner review**: the 30% ad share and the equal split.

## Gangs (4 gangs x 40)

**DRAFT – owner review**: gang names. Working idea: regional names, one gang per area of the city (for example slum, old city, port, outskirts).

| Rank | Count per gang |
| --- | --- |
| Bhai (boss) | 1 |
| Right Hand | 1 |
| Area Commander | 4 |
| Shooter | 10 |
| Member | 14 |
| Recruit | 10 |
| **Total** | **40** |

No salary. Income: **hafta** (protection money from businesses), **contraband**, **robberies**, **car theft**, **convoy hijacks**, and **Saturday turf wars** for territory.

## Mafia (2 families x 30)

| Rank | Count per family |
| --- | --- |
| Don | 1 |
| Consigliere | 1 |
| Captain | 3 |
| Soldier | 10 |
| Associate | 15 |
| **Total** | **30** |

No salary. Income: **port smuggling**, **clubs**, **money laundering**, **arms sales** to gangs.

## Court (about 15)

Roles: **Chief Justice** (picked by admins), **Judges**, **Prosecutor**, **Clerk**.

| Role | Count |
| --- | --- |
| Chief Justice | 1 |
| Judge | 4 |
| Prosecutor | 4 |
| Clerk | 6 |
| **Total** | **15** |

**DRAFT – owner review**: the split of the ~15 court seats across roles and court salaries are not decided.

**Lawyer** is a **licensed civilian job**, not a faction rank. When no lawyer is available, a **government defender** is assigned as a fallback. The full court flow is in [05. Justice and Crime](05-justice-and-crime.md).

## Recruitment pipeline (government factions)

1. **Hiring post** on the in-game phone (faction app).
2. Player fills in a **form**.
3. **Automatic eligibility check** (level, play hours, criminal record, cooldowns, licenses).
4. Leaders make a **shortlist**.
5. **Live voice interview at HQ.**
6. **Faction test**:

| Faction | Test |
| --- | --- |
| Police | Law quiz, driving test, shooting range |
| Army | Fitness course, shooting range, drill |
| Hospital | Medical quiz, revive on a training dummy |
| Fire | Fire drill |
| Government | Interview |
| Mass Media | Mock news report (TV, Paper, Digital) or a 2-minute mock radio show (Radio) |

7. **Result by in-game SMS.**
8. **3-day probation.**
9. **Confirmed** as a full member.

**DRAFT – owner review**: probation length (3 days proposed).
**DRAFT – owner review**: text interviews (whether a text interview is allowed instead of voice).
**DRAFT – owner review**: recruitment windows (fixed hiring periods vs always open).

## Recruitment (gangs and mafia)

Invite -> meet in person -> **loyalty task** -> approval by the Bhai/Don (or Right Hand/Consigliere).

## Promotions

A promotion needs all of: **duty hours**, **completed tasks**, **time in rank**, and **leader approval**. Leaders can promote only up to one rank below their own, only members inside their own unit, and only into a rank with a free seat (see Ranks, units and chain of command).
