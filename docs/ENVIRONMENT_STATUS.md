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
- **Blender**: cannot be installed, so procedural model scripts cannot be run or checked.
- **API calls**: Sarvam (dialogue, translation, TTS) and Gemini (2D art) cannot be reached. Scripts can be written but not executed against the real APIs.
- **Firebase**: a project cannot be created, and rules/functions cannot be deployed or emulated (the emulator needs downloads).
- **Asset packs**: Kenney, Quaternius, Poly Haven and Mixamo content cannot be downloaded.
- **Image-to-3D**: Hunyuan3D / TRELLIS need a GPU and model downloads; neither is available.

## What can be done here

- Write all design documents.
- Build and test the .NET 8 authoritative server, the Unity-compatible shared C# core, a console test runner and a bot load test (100 and 1000 simulated players over local UDP).
- Write the Unity client C# scripts and project files, the Python content scripts, the Blender scripts and the Firebase config. These are marked **unverified** until they run in a real environment.

## Options to unblock

1. **Enable open internet for this sandbox.** Unity Hub/Editor (headless, Linux), Blender, npm/PyPI packages and the APIs become reachable. Unity still needs a license activation and Android build support modules.
2. **Build Unity on GitHub Actions** with [game-ci/unity-builder](https://game.ci/docs/github/builder). Store `UNITY_EMAIL`, `UNITY_PASSWORD` and `UNITY_LICENSE` (or `UNITY_SERIAL`) as encrypted repository secrets. The workflow outputs an APK/AAB you can install on an Android phone. iOS builds export an Xcode project that needs a macOS runner (GitHub macOS runners or a rented cloud Mac).
3. **Use a local PC** with Unity Hub installed. Open `client/GangsOfBharat.Unity`, let it import, and build for Android.

- **Unity license activation** in headless batchmode, and any **Unity account 2FA or email verification** step, could not be tested because Unity servers are unreachable. Expect to handle 2FA/email verification once on a real machine or use a serial/license file in CI.

## Credentials

The **Gemini API key**, the **Sarvam API key** and the **Unity account credentials** (email and password) shared in chat were deliberately **not** stored anywhere in this repository, its history, its docs or logs. They are also unusable here because the Gemini, Sarvam and Unity hosts are unreachable. Because they were sent in plain text, **rotate all of them**: regenerate the Gemini and Sarvam keys and change the Unity account password, then put the new values only in a local `.env` file (see `.env.example`) or in CI secrets.
