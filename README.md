# GANGS OF BHARAT

GANGS OF BHARAT is a social online roleplay (RP) city game for Android and iOS, set in **Mayanagri**, a fictional Mumbai-inspired megacity. It follows the style of Grand Mobile, Black Russia and Rio Rise: players live a second life in a shared city, take jobs, buy cars and houses, join factions or gangs, and talk to each other with text and proximity voice chat. Dialogue and UI ship in English, Hindi and Hinglish.

- Client: Unity (Unity 6 LTS, URP), Android first, iOS later via a cloud Mac.
- Server: authoritative .NET 8 dedicated server over UDP. Launch with 1 server, up to 1000 players; more servers later.
- Backend: Firebase (Auth with Google and Sign in with Apple, Firestore, Cloud Functions on the Blaze plan, Analytics, Crashlytics, FCM, Remote Config).
- Money: in-game ₹; premium coins only through Google Play / App Store purchases. Rated 18+.
- Content: Sarvam API for Hindi/English dialogue, translation and TTS; Gemini for 2D art; free CC0 packs and procedural Blender scripts for 3D.

## Repository layout

| Path | Contents |
| --- | --- |
| `docs/` | Design and planning docs (see below) |
| `docs/design/` | Game Design Document chapters (Markdown) |
| `tools/docs/` | `md2pdf.py`, a standard-library Markdown-to-PDF converter, and its tests |
| `shared/GangsOfBharat.Shared/` | Unity-compatible C# game core: protocol, reliability layer, serialization, interest grid, catalogs, movement, localization |
| `server/GangsOfBharat.Server/` | .NET 8 authoritative UDP game server ([README](server/GangsOfBharat.Server/README.md), Dockerfile) |
| `tests/GangsOfBharat.Tests/` | Console test runner and tests (no NuGet test framework needed) |
| `tools/GangsOfBharat.Bots/` | Bot load tester (in-process server, or a remote one with `--host`) |
| `tools/content/` | Python scripts for Sarvam dialogue/TTS and Gemini 2D art ([README](tools/content/README.md)) |
| `tools/blender/` | Procedural Blender generators for city blocks and vehicles, unverified ([README](tools/blender/README.md)) |
| `client/GangsOfBharat.Unity/` | Unity 6 project scaffold, unverified ([README](client/GangsOfBharat.Unity/README.md)) |
| `firebase/` | Firebase config, Firestore rules, indexes and schema ([SCHEMA](firebase/SCHEMA.md)), not deployed |
| `admin/` | Zero-dependency Node web admin panel with mock data ([README](admin/README.md)); test with `node --test admin/test` |
| `scripts/dotnet-env.sh` | Sets writable dotnet home dirs for sandboxes with a read-only `$HOME` |

## Build and test the server

The server, shared core and tests target `net8.0` with no NuGet packages, so they build offline.

```bash
source scripts/dotnet-env.sh
dotnet build GangsOfBharat.sln -c Release
dotnet run --project tests/GangsOfBharat.Tests -c Release          # exit code 0 = all tests pass
dotnet run --project tests/GangsOfBharat.Tests -c Release -- Chat  # only tests whose name contains "Chat"
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 100 --seconds 20
dotnet run --project tools/GangsOfBharat.Bots -c Release -- --bots 1000 --seconds 20 --area 300   # crowd test
dotnet run --project server/GangsOfBharat.Server -c Release                                      # run the server on UDP 7777
```

`scripts/dotnet-env.sh` only redirects `HOME` to `/tmp/dnhome` when the current home (or `~/.dotnet`) is not writable.

The tests cover serialization, every protocol message, the reliability layer under loss and reordering, the spatial grid against brute force, interest tiers and the 100-entity cap, every chat channel (slow mode, ad and SMS fees, VIP gating, mute and block), proximity voice and faction radio, anti-cheat, catalogs against the design, localization, and an end-to-end test over real UDP sockets. Load test results for 100 and 1000 bots are in [docs/LOADTEST.md](docs/LOADTEST.md).

The server Dockerfile (linux/arm64 for Oracle Cloud ARM) is `server/GangsOfBharat.Server/Dockerfile`. Build it from the repository root with `docker buildx build --platform linux/arm64 -f server/GangsOfBharat.Server/Dockerfile .`. It has not been built yet because Docker is unusable in the build sandbox.

## Secrets policy

- API keys and account credentials are read from environment variables only (`SARVAM_API_KEY`, `GEMINI_API_KEY`, `FIREBASE_PROJECT_ID`, `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_SERIAL`).
- Copy `.env.example` to `.env` locally. `.env`, `.env.*`, `secrets.local.json` and `config/secrets*.json` are gitignored.
- In CI, store them as encrypted repository secrets.
- Never paste keys into source files, docs, commit messages or logs.

## Documentation

- [Game Design Document](docs/design/00-INDEX.md): 17 chapters covering vision, city, story, factions, justice, events, economy, market, injury, communication, monetization, admin, tech, art/audio, extras, legal and roadmap. Open items are labelled "DRAFT – owner review".
- [Game Design Document (PDF)](docs/GangsOfBharat_GDD.pdf): generated offline with `python3 tools/docs/md2pdf.py --out docs/GangsOfBharat_GDD.pdf docs/design/*.md`. Hindi lines are only in the Markdown.
- [Architecture](docs/ARCHITECTURE.md): client, server, networking, Firebase, hosting costs, scaling, security.
- [Load test](docs/LOADTEST.md): 100 and 1000 bot results, interpretation and optimisation ideas.
- [Roadmap](docs/ROADMAP.md): build stages from setup to Android and iOS launch, and what the project owner must provide.
- [Android build on your Mac](docs/BUILD_MAC.md): recommended free path. One pasted command builds the APK with the free Unity Personal license (unverified).
- [Android build on Codemagic](docs/BUILD_CODEMAGIC.md): fallback, free-plan APK build setup, Unity credentials as environment variables (unverified).
- [Environment Status](docs/ENVIRONMENT_STATUS.md): what this build sandbox can and cannot do, and how to unblock it.
