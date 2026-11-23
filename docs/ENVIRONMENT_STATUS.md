# Environment Status

This file records what the cloud sandbox used to create this repository can and cannot do. It was probed directly, not assumed.

## Probed facts

| Item | Result |
| --- | --- |
| OS / hardware | Amazon Linux 2023 x86_64, 8 CPU, 30 GB RAM, no GPU, no display |
| Outbound network | Blocked. The sandbox proxy returns HTTP 403 on CONNECT for every tested host |
| Hosts tested | download.unity3d.com, packages.unity.com, download.blender.org, registry.npmjs.org, pypi.org, nuget.org, github.com, api.sarvam.ai, generativelanguage.googleapis.com (Gemini), firebase.googleapis.com, huggingface.co, polyhaven.com, kenney.nl, quaternius.com, dl.google.com, Docker Hub |
| Docker | Unusable (podman `conmon` missing) |
| .NET | SDK 8.0.426 and 9.0.319 work offline. `~/.dotnet` is read-only, so source `scripts/dotnet-env.sh` first |
| NuGet | Unreachable, so projects use `net8.0` with zero package references. `netstandard2.1` fails offline (reference pack missing) |
| Python | 3.11 with ruff, black, mypy, pytest (no new packages installable) |
| Node | 22, no packages installable |
| Git | Available, no remote configured |

## Re-probed reachability

Re-tested with `curl -m 8 https://<host>/` through the sandbox proxy while writing the GDD. Every host failed (curl HTTP code `000`: the proxy refuses the CONNECT).

| Host | Purpose | Result |
| --- | --- | --- |
| public-cdn.cloud.unity3d.com | Unity Editor downloads | Fail (000) |
| packages.unity.com | Unity packages | Fail (000) |
| download.blender.org | Blender | Fail (000) |
| registry.npmjs.org | npm packages | Fail (000) |
| pypi.org | Python packages | Fail (000) |
| api.nuget.org | NuGet packages | Fail (000) |
| registry-1.docker.io | Docker Hub | Fail (000) |
| api.sarvam.ai | Sarvam dialogue, TTS, translation, STT | Fail (000) |
| generativelanguage.googleapis.com | Gemini | Fail (000) |
| firebase.googleapis.com | Firebase | Fail (000) |
| github.com | Git hosting, game-ci | Fail (000) |
| huggingface.co | Image-to-3D models | Fail (000) |

## PDF generation

No PDF tools are installed (no pandoc, wkhtmltopdf, LibreOffice, Chromium or ps2pdf; no reportlab, fpdf or markdown Python modules; groff has no PDF output) and none can be installed. `docs/GangsOfBharat_GDD.pdf` is therefore produced by `tools/docs/md2pdf.py`, a Python standard-library converter that writes PDF objects directly with the built-in Helvetica and Courier fonts (WinAnsi encoding). Limits: ₹ is printed as "Rs.", ★ as "*", and lines with Devanagari Hindi are replaced by "[Hindi text: see Markdown source]" because the built-in fonts have no Devanagari glyphs. Regenerate with:

```bash
python3 tools/docs/md2pdf.py --out docs/GangsOfBharat_GDD.pdf docs/design/*.md
python3 -m pytest tools/docs -q
```

## What cannot be done in this sandbox

- **Unity**: the editor cannot be downloaded or licensed, so no Unity project compile, no scene work, no APK/AAB or iOS build.
- **Blender**: cannot be installed, so the procedural model scripts cannot be run. Only their argument parsing and layout code is tested.
- **API calls**: Sarvam (dialogue, translation, TTS) and Gemini (2D art) cannot be reached. Scripts can be written but not executed against the real APIs.
- **Firebase**: a project cannot be created, and rules/functions cannot be deployed or emulated (the emulator needs downloads).
- **Asset packs**: Kenney, Quaternius, Poly Haven and Mixamo content cannot be downloaded.
- **Image-to-3D**: Hunyuan3D / TRELLIS need a GPU and model downloads; neither is available.
- **Docker**: `server/GangsOfBharat.Server/Dockerfile` (multi-stage, linux/arm64 for Oracle Cloud ARM) is written but has **not been built**: Docker is broken here and the `mcr.microsoft.com/dotnet` base images cannot be pulled.
- **Firebase ID token check**: the game server accepts any non-empty session token as an anonymous civilian (TODO in `ServerCore.TryResolveToken`), because Google's signing certificates cannot be fetched from here.
- **Network tuning**: `net.core.rmem_max` is 212992 here and cannot be raised, so the server's UDP receive buffer is capped at about 416 KB (see `docs/LOADTEST.md`).

## What can be done here

- Write all design documents.
- Build and test the .NET 8 authoritative server, the Unity-compatible shared C# core, a console test runner and a bot load test (100 and 1000 simulated players over local UDP).
- Write the Unity client C# scripts and project files, the Python content scripts, the Blender scripts and the Firebase config. These are marked **unverified** until they run in a real environment.

## Component status

### Built and verified here

| Component | Path | How it was verified |
| --- | --- | --- |
| Shared C# core | `shared/GangsOfBharat.Shared/` | `dotnet build GangsOfBharat.sln -c Release` with 0 warnings (warnings are errors) |
| Authoritative server | `server/GangsOfBharat.Server/` | Same build, plus the end-to-end UDP test |
| Tests | `tests/GangsOfBharat.Tests/` | `dotnet run --project tests/GangsOfBharat.Tests -c Release` exits 0 |
| Bot load tester | `tools/GangsOfBharat.Bots/` | 100 and 1000 bot runs (see `docs/LOADTEST.md`) |
| GDD PDF | `docs/GangsOfBharat_GDD.pdf`, `tools/docs/` | Generated with `md2pdf.py`; `python3 -m pytest tools/docs -q` |
| Content tools (dry run) | `tools/content/` | `python3 -m pytest tools/content/tests -q` with network calls blocked; `ruff check tools` |
| Shared copy in Unity | `client/GangsOfBharat.Unity/Assets/Scripts/Shared/` | `scripts/sync-shared-to-unity.sh --check` |
| Admin panel (mock data) | `admin/` | `node --test admin/test` (level hierarchy, reasons, code-only admins, real-info hiding, missing-token refusal) |
| Blender argument parsing and layout | `tools/blender/` | `python3 -m pytest tools/blender/tests -q` without `bpy`; `py_compile` and `ruff check tools` |
| Firebase files (static checks only) | `firebase/` | `node --test admin/test` parses the JSON files and checks that `firestore.rules` denies client writes to currency, inventory, faction membership and faction permissions |

### Written but not verified

| Component | Path | What is needed to verify it |
| --- | --- | --- |
| Unity client | `client/GangsOfBharat.Unity/` | Unity 6 LTS with Android Build Support and a license: open the project, let it import, play the scenes, build an APK. A compile smoke check was done here only with throwaway hand-written `UnityEngine` stubs kept outside the repository, so real Unity API use is not proven |
| Android CI workflow | `.github/workflows/unity-android.yml` | A GitHub remote with `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_LICENSE` (or `UNITY_SERIAL`) secrets, then a workflow run |
| Blender geometry | `tools/blender/gen_city_block.py`, `gen_vehicle_auto.py` | Blender 4.x: run the commands in `tools/blender/README.md` and inspect the `.glb` files |
| Firebase rules deploy | `firebase/firestore.rules`, `firestore.indexes.json` | Firebase CLI and the Firestore emulator (both need downloads), then rule unit tests and `firebase deploy` to a real project |
| Cloud Functions | `firebase/SCHEMA.md` (design only) | Blaze plan, `firebase-functions` and `firebase-admin` packages, Play Developer and App Store Server API credentials |
| Live Sarvam and Gemini calls | `tools/content/` | Network access and fresh API keys in a local `.env` |
| Server Dockerfile | `server/GangsOfBharat.Server/Dockerfile` | Working Docker with buildx and access to `mcr.microsoft.com` |
| Admin panel against real data | `admin/` | Firestore access through the Admin SDK and per-admin sign-in (see `admin/README.md`) |

## Options to unblock

1. **Enable open internet for this sandbox.** Unity Hub/Editor (headless, Linux), Blender, npm/PyPI packages and the APIs become reachable. Unity still needs a license activation and Android build support modules.
2. **Build Unity on GitHub Actions** with [game-ci/unity-builder](https://game.ci/docs/github/builder). Store `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_LICENSE` (or `UNITY_SERIAL`) as encrypted repository secrets. The workflow outputs an APK/AAB you can install on an Android phone. iOS builds export an Xcode project that needs a macOS runner (GitHub macOS runners or a rented cloud Mac).
3. **Use a local PC** with Unity Hub installed. Open `client/GangsOfBharat.Unity`, let it import, and build for Android.

- **Unity license activation** in headless batchmode, and any **Unity account 2FA or email verification** step, could not be tested because Unity servers are unreachable. Expect to handle 2FA/email verification once on a real machine or use a serial/license file in CI.

## Credentials

The **Gemini API key**, the **Sarvam API key** and the **Unity account credentials** (email and password) shared in chat were deliberately **not** stored anywhere in this repository, its history, its docs or logs. They are also unusable here because the Gemini, Sarvam and Unity hosts are unreachable. Because they were sent in plain text, **rotate all of them**: regenerate the Gemini and Sarvam keys and change the Unity account password, then put the new values only in a local `.env` file (see `.env.example`) or in CI secrets.
