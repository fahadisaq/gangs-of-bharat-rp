# Building the Android APK on Unity Build Automation

Status: UNVERIFIED. The repo side (`Assets/Editor/BuildScript.cs`, `scripts/uba-prebuild.sh`) was written offline and has never run in real Unity. Dashboard menu names below are from memory and may differ slightly. Expect to fix small issues on the first build.

This is the recommended build path. GitHub Actions is not used (the account's billing blocks runs), and Codemagic was dropped (it needs a paid Unity license). A local Mac build is only an optional fallback: [BUILD_MAC.md](BUILD_MAC.md).

Free minutes, as stated by the project owner: 200 Windows and 100 Linux build minutes per month. With no payment method on the organization, going over the limit blocks builds instead of billing. Do not add a payment method.

Values to use:

| Setting | Value |
| --- | --- |
| Repository (HTTPS) | `https://github.com/fahadisaq/gangs-of-bharat-rp.git` |
| Repository (SSH) | `git@github.com:fahadisaq/gangs-of-bharat-rp.git` |
| Unity version | `6000.0.32f1` (from `client/GangsOfBharat.Unity/ProjectSettings/ProjectVersion.txt`) |
| Branch | `feature/codemagic-build` until merged, then `main` |
| Project subfolder | `client/GangsOfBharat.Unity` |
| Pre-export method | `GangsOfBharat.EditorTools.BuildScript.PreExport` |
| Pre-build script path | `scripts/uba-prebuild.sh` |

## Steps

1. Sign in at [cloud.unity.com](https://cloud.unity.com) with your Unity ID.
2. Create or select an organization, then create a project named **Gangs of Bharat**.
3. Open **DevOps > Build Automation** and finish the onboarding prompts.
4. Connect source control. Pick one:
   - **GitHub app / OAuth (simplest):** choose GitHub, sign in, and authorize the Unity DevOps app for the repository `fahadisaq/gangs-of-bharat-rp`. Use the HTTPS URL above if asked.
   - **SSH deploy key:** choose a generic Git / SSH option and enter the SSH URL above. Unity shows a public key. In GitHub open the repository, **Settings > Deploy keys > Add deploy key**, paste the key, leave "Allow write access" off, and save.
5. Create a build configuration (**Build Automation > Add new build target**):
   - Platform: **Android**.
   - Unity version: **6000.0.32f1**. If it is not listed, pick the closest 6000.0.x and see Troubleshooting.
   - Branch: `feature/codemagic-build` (switch to `main` after merge).
   - Project subfolder (the Unity project root inside the repo): `client/GangsOfBharat.Unity`.
   - Machine OS: Windows or Linux, whichever you prefer. Linux has fewer free minutes (100) than Windows (200), so Windows lasts longer, but Windows machines start slower.
   - Output: **Android APK**, not AAB (Android App Bundle must be off).
   - Signing: use the **debug keystore** (no custom keystore). Fine for testing, not for the Play Store.
   - Advanced: set **Pre-export method** to `GangsOfBharat.EditorTools.BuildScript.PreExport` and **Pre-build script path** to `scripts/uba-prebuild.sh`.
   - Optional environment variables: `GOB_VERSION` (for example `0.1.0`), `GOB_BUNDLE_CODE` (integer), `GOB_PACKAGE_NAME` (default `com.gangsofbharat.rp`).
6. Save, then press **Build** (start a build for that target).
7. When the build succeeds, open the build entry and use **Download** / **Share** to get the APK. Copy it to an Android phone and install it (allow installs from unknown sources).

## What the repo side does

- `scripts/uba-prebuild.sh` runs `scripts/sync-shared-to-unity.sh --check` and fails the build if `Assets/Scripts/Shared` is out of date with `shared/GangsOfBharat.Shared`. Fix with `scripts/sync-shared-to-unity.sh` and commit. It uses only bash and POSIX tools so it also runs under bash on Windows. If the Windows machine checks out files with CRLF line endings, the check could report a false difference; if so, switch to a Linux machine or tell us.
- `BuildScript.PreExport` sets the Android package name, version, minimum API 24, IL2CPP, ARM64 and APK output, and does not call `BuildPipeline`. If Build Settings has no enabled scene, it creates an empty `Assets/Scenes/Bootstrap.unity` and adds it, so the first build can succeed.

## Scenes

`ProjectSettings/EditorBuildSettings.asset` does not exist in the repo yet, and `Assets/Scenes` is empty, so Build Settings has no scene. That is why `PreExport` creates a bootstrap scene. The resulting APK shows an empty scene until a real scene is made and added in **File > Build Profiles / Build Settings**.

## Troubleshooting

- **"Project not found" or no Assets folder:** the project subfolder is wrong. It must be exactly `client/GangsOfBharat.Unity`.
- **Unity version not installed / not available:** choose the nearest available 6000.0.x version in the configuration. Patch versions are normally compatible, but package versions in `Packages/manifest.json` may then need adjusting.
- **Pre-build script failed with "out of date":** run `scripts/sync-shared-to-unity.sh`, commit, push.
- **Pre-build script not found / not executable:** check the path `scripts/uba-prebuild.sh` has no leading slash. The file is committed as executable.
- **"Scene(s) not found" or "no scenes in build":** check that `PreExport` is set and ran (see the build log for "PreExport done"). Otherwise add a scene in Build Settings and commit `ProjectSettings/EditorBuildSettings.asset`.
- **"Error building player because script class layout is incompatible between the editor and the player":** a pre-export method changed a setting that controls compilation defines, after the editor had already compiled. Build 1 (2026-11-23) hit this because `PreExport` set `activeInputHandler = 2`. That call was removed. Do not set `activeInputHandler`, the scripting backend, or scripting define symbols from `PreExport`; commit them in `ProjectSettings/ProjectSettings.asset` instead, so the editor starts with the right values. Unity requires an editor restart after such a change and Build Automation never restarts the editor.
- **Input does nothing in the APK:** `ProjectSettings/ProjectSettings.asset` is still absent, so `activeInputHandler` is 0 (legacy Input Manager) while the gameplay scripts use `UnityEngine.InputSystem`. Generate that file with a real Unity editor, set Active Input Handling to "Input System Package" or "Both", and commit it.
- **Compile errors in Unity scripts:** expected on the first real build. The Unity code has only been checked against hand-written stubs, never compiled with real Unity. Read the first error in the build log, fix it, push, and rebuild. Each failed build uses free minutes.
- **Pre-export method not found:** the method name must be exactly `GangsOfBharat.EditorTools.BuildScript.PreExport`, and `BuildScript.cs` must be under an `Editor` folder (it is: `Assets/Editor`).
- **Build blocked for minutes:** the monthly free minutes are used up. Wait for the next month. Nothing is billed.
