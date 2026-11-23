# 03. Story

The story gives every new player a reason to be in Mayanagri and teaches the basics. After the intro the city is driven by players; story chapters keep adding background and side missions.

## Character creation

Name, gender, face, hair, skin tone, starting clothes, and preferred language (Hindi or English, changeable later). **DRAFT – owner review**: the character creation options and screens are not final.

## Intro (about 25 minutes, fully voiced)

**DRAFT – owner review**: the intro script, the beat timings and all lines below are a first draft.

| # | Beat | Time (approx.) | What the player does / learns |
| --- | --- | --- | --- |
| 1 | **Train arrival.** The player arrives at Mayanagri Central by local train with only **₹500** and one bag, dreaming of a new life | 0:00-3:00 | Camera, walking, crowd, first look at the city |
| 2 | **Stolen bag.** On the platform a thief snatches the bag. The player chases him through the station and loses him | 3:00-6:00 | Running, jumping, the minimap |
| 3 | **Slum.** With no money left except the ₹500 in the pocket, the player ends up in the slum start zone | 6:00-9:00 | Needs (hunger, thirst, energy), buying chai and vada pav, renting a slum bed |
| 4 | **Auto driver mentor.** An old auto-rickshaw driver ("Pandey chacha", draft name) sees the player and offers help: a place to sleep and advice about the city | 9:00-13:00 | Talking to NPCs, the phone, map, voice and chat basics |
| 5 | **First job.** Chacha lends his auto for a few rides | 13:00-18:00 | Driving, fuel, the job system, first pay in ₹ |
| 6 | **The three powers.** On the rides the player sees the three powers of Mayanagri: the **Police**, the **Business families** and the **Gangs**. Each one makes the player an offer | 18:00-23:00 | Introduction to factions, legal vs illegal income, wanted levels |
| 7 | **Free in the city.** Chacha says: "This city gives everyone a chance. What you become is your choice." The bag thief is seen again, setting up chapter 1 | 23:00-25:00 | Hand-off to open play and the first story chapter |

## Story chapters (web-series style)

- After the intro the story continues in **chapters**, like episodes of a web series. Each chapter has a few missions and ends on a hook.
- Chapters are optional. Players can follow them while living their RP life.
- Example arc: finding the bag thief, discovering he works for a gang, and learning who controls the old city.
- Chapters can point players to real factions (for example "visit the police station to file a complaint", which a player officer can respond to).

## Cutscenes

- **Comic-panel cutscenes**: illustrated still panels (made with Gemini 2D, see [14. Art and Audio Pipeline](14-art-audio-pipeline.md)) with light camera motion, speech bubbles and subtitles.
- Every line is **voiced with Sarvam TTS** in Hindi and English. Subtitles follow the player's language.
- This keeps cutscene cost near zero while still feeling cinematic.

## Sample bilingual lines

| Speaker | English | Hindi |
| --- | --- | --- |
| Chacha | Welcome to Mayanagri, son. This city never sleeps, and it never forgives. | मायानगरी में स्वागत है, बेटा। ये शहर कभी सोता नहीं, और कभी माफ़ नहीं करता। |
| Player | Someone stole my bag! Everything I had was in it. | किसी ने मेरा बैग चुरा लिया! मेरा सब कुछ उसी में था। |
| Chacha | Take my auto for today. Drive carefully and bring it back in one piece. | आज के लिए मेरा ऑटो ले जा। ध्यान से चलाना और सही-सलामत वापस लाना। |
| Police officer | Mayanagri needs honest people in uniform. Think about it. | मायानगरी को वर्दी में ईमानदार लोग चाहिए। सोचना ज़रूर। |
| Business family elder | Money makes money. Work for us and you will see. | पैसा पैसे को खींचता है। हमारे साथ काम करो, खुद देख लोगे। |
| Gang member | The police won't feed you. We look after our own. | पुलिस तुझे खाना नहीं खिलाएगी। हम अपनों का ख्याल रखते हैं। |

All final lines are written and checked with Sarvam (dialogue generation and translation), then reviewed by a Hindi speaker before recording with TTS.
