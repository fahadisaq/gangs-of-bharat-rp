# Building the Android APK on Unity Build Automation

Status: the APK builds on Unity Build Automation (UBA) with Unity 6000.0.32f1 and `BuildScript.PreExport`, and the C# compiled with zero errors there. Later changes (legacy touch controls, the generated TestDistrict scene) were only compiled against hand-written stubs and have not run in real Unity yet. Dashboard menu names below are from memory and may differ slightly.

## Facts from the real builds

- UBA writes Unity version strings with underscores: `6000_0_32f1` means `6000.0.32f1`.
- The pre-build script path is not resolved from the repository root, so `scripts/uba-prebuild.sh` was not found. The field was cleared; leave it empty. Run `scripts/sync-shared-to-unity.sh --check` locally before pushing instead.
- `ProjectSettings/ProjectSettings.asset` is not in the repo, so Active Input Handling is the legacy Input Manager: `ENABLE_LEGACY_INPUT_MANAGER` is defined in the player and `ENABLE_INPUT_SYSTEM` is not.
- Never change `activeInputHandler`, the scripting backend or scripting define symbols from `PreExport` (or any editor code that runs during the build). Doing so broke a build with "script class layout is incompatible between the editor and the player", because UBA never restarts the editor after the change. Do not hand-write `ProjectSettings.asset` either. The one existing `SetScriptingBackend(IL2CPP)` call in `PreExport` was present in the build that passed and is left as is; do not add other calls of this kind.
- Creating scenes and GameObjects and editing `EditorBuildSettings.scenes` from `PreExport` is fine.
- No render pipeline asset is committed, so the player renders with the built-in pipeline even though the URP package is installed.

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
| Pre-build script path | leave empty (see "Facts from the real builds") |

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
   - Advanced: set **Pre-export method** to `GangsOfBharat.EditorTools.BuildScript.PreExport`. Leave **Pre-build script path** empty.
   - Optional environment variables: `GOB_VERSION` (for example `0.1.0`), `GOB_BUNDLE_CODE` (integer), `GOB_PACKAGE_NAME` (default `com.gangsofbharat.rp`).
6. Save, then press **Build** (start a build for that target).
7. When the build succeeds, open the build entry and use **Download** / **Share** to get the APK. Copy it to an Android phone and install it (allow installs from unknown sources).

## What the repo side does

- `scripts/uba-prebuild.sh` (currently not used by UBA, see above) runs `scripts/sync-shared-to-unity.sh --check` and fails the build if `Assets/Scripts/Shared` is out of date with `shared/GangsOfBharat.Shared`. Fix with `scripts/sync-shared-to-unity.sh` and commit. It uses only bash and POSIX tools so it also runs under bash on Windows. If the Windows machine checks out files with CRLF line endings, the check could report a false difference; if so, switch to a Linux machine or tell us.
- `BuildScript.PreExport` sets the Android package name, version, minimum API 24, IL2CPP (this call was already there in the build that passed and is left unchanged), ARM64, landscape orientation and APK output, and does not call `BuildPipeline`. If Build Settings has no enabled scene, it generates `Assets/Scenes/TestDistrict.unity` (see below) and adds it as the only enabled scene. An existing enabled scene is never touched, and an existing `TestDistrict.unity` file is reused, not regenerated. If generation throws, it falls back to an empty `Assets/Scenes/Bootstrap.unity` so the build still produces an APK (look for "TestDistrict generation failed" in the log).

## Scenes

`ProjectSettings/EditorBuildSettings.asset` does not exist in the repo yet, and `Assets/Scenes` is empty, so Build Settings has no scene. `PreExport` therefore calls `TestSceneBuilder` (`Assets/Editor/TestSceneBuilder.cs`) on the build machine, which builds a playable test scene from code:

- A warm directional sun with soft shadows, explicit trilight ambient (no lighting bake is needed) and the default skybox.
- A 400 m ground plane, a main road with lane markings and a cross road, pavements, two rows of box shop-houses in varied heights and colours with awnings and rooftop water tanks, and trees.
- The local player (capsule) with `PlayerController`, `VehicleController` and `ClientPrediction` (offline test mode on), a Main Camera with `FollowCamera`, and a drivable yellow-and-black taxi (`OfflineVehicle`, catalog id 5) next to the spawn point.
- `TestBuildOverlay` in the top-left corner: "Gangs of Bharat test build v0.1", FPS and a controls hint.
- Materials are saved under `Assets/Scenes/TestDistrictMaterials/`. The shader is chosen for the active pipeline: built-in (`GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline` both null) uses `Standard`, then `Legacy Shaders/Diffuse`; URP uses `Universal Render Pipeline/Lit`, then `Simple Lit`, then `Standard`. If none is found, primitives keep their default material instead of getting a null one.
- Optional models: any `Assets/Art/Models/City/Block_*.fbx` are placed along the cross road, and any `Assets/Art/Models/Vehicles/*.fbx` are parked beside the main road as props (their forward axis is unknown, so the drivable car stays the primitive taxi). Each model is scaled to a fixed size from its renderer bounds and set on the ground. With no models the scene is primitives only.

The district is built around (256, 0, 256) because the shared `Movement` code clamps positions to the world bounds (0 to 8192 m). There are no collisions: the player and the car pass through buildings.

In the editor the same scene can be regenerated with **Gangs of Bharat > Generate Test District**.

Offline test mode: the scene has no `NetClient`, so `ClientPrediction` (with `offlineTestMode` on) runs the shared movement code locally every frame and sends nothing. With a `NetClient` it waits `offlineFallbackSeconds` (5 s) for a connection first, and a later successful connection switches back to the networked path. Vehicles in offline mode are entered and exited locally (`OfflineVehicle`) instead of via `EnterVehicle`/`ExitVehicle` server messages. The flag is off by default, so networked scenes are unchanged.

## Input and touch controls

Until a real `ProjectSettings/ProjectSettings.asset` with Active Input Handling = "Both" is generated by a real Unity editor and committed, the player uses the legacy Input Manager. `PlayerController`, `VehicleController`, `VoiceChat` and `FollowCamera` wrap all `UnityEngine.InputSystem` code in `#if ENABLE_INPUT_SYSTEM` and fall back to `UnityEngine.Input` plus `TouchControls` (IMGUI drawing, multi-touch from `Input.touches`). The asmdef keeps its `Unity.InputSystem` reference; nothing uses it at runtime in this mode.

Phone controls (landscape):

- Left half: virtual joystick. Put a thumb down anywhere; drag up to 8% of the screen height from that point. On foot it moves relative to the camera; in the car up/down is throttle/reverse and left/right steers.
- Right half: drag to turn the camera (and pitch it). In the car the camera drifts back behind the car when you let go.
- Bottom-right buttons. On foot: **Jump**, **Run** (hold), **Enter** (within 5 m of the taxi). In the car: **Brake** (handbrake, hold), **Horn**, **Exit**. **Talk** / **Radio** only appear while voice chat is connected.
- Editor / desktop: WASD, Shift run, Space jump/brake, E enter, F exit, H horn, V/B talk, arrow keys turn the camera, and the left mouse button acts as a finger (left half joystick, right half look).

After "Both" is committed, `ENABLE_INPUT_SYSTEM` is defined and the Input System branch is compiled instead. That branch has no built-in touch UI: it needs `OnScreenStick`/`OnScreenButton` controls on a HUD (see the comments in `PlayerController` and `VehicleController`), otherwise touch input stops working in the test scene. `CameraRig` (Cinemachine) is not used by the test scene; its `CinemachineInputAxisController` does not read the legacy touch input.

## Troubleshooting

- **"Project not found" or no Assets folder:** the project subfolder is wrong. It must be exactly `client/GangsOfBharat.Unity`.
- **Unity version not installed / not available:** choose the nearest available 6000.0.x version in the configuration. Patch versions are normally compatible, but package versions in `Packages/manifest.json` may then need adjusting.
- **Pre-build script failed with "out of date":** run `scripts/sync-shared-to-unity.sh`, commit, push.
- **Pre-build script not found / not executable:** check the path `scripts/uba-prebuild.sh` has no leading slash. The file is committed as executable.
- **"Scene(s) not found" or "no scenes in build":** check that `PreExport` is set and ran (see the build log for "PreExport done"). Otherwise add a scene in Build Settings and commit `ProjectSettings/EditorBuildSettings.asset`.
- **"Error building player because script class layout is incompatible between the editor and the player":** a pre-export method changed a setting that controls compilation defines, after the editor had already compiled. Build 1 (2026-11-23) hit this because `PreExport` set `activeInputHandler = 2`. That call was removed. Do not set `activeInputHandler`, the scripting backend, or scripting define symbols from `PreExport`; commit them in `ProjectSettings/ProjectSettings.asset` instead, so the editor starts with the right values. Unity requires an editor restart after such a change and Build Automation never restarts the editor.
- **Input does nothing in the APK:** with the legacy Input Manager (current state) the legacy branch and `TouchControls` should handle touch. If the on-screen joystick and buttons are not drawn at all, check that `TestBuildOverlay` text is visible and that the scene in the build is `TestDistrict` (log line "No scene in Build Settings; added Assets/Scenes/TestDistrict.unity"). If Active Input Handling was switched to "Input System Package" only, the legacy branch is compiled out; add Input System on-screen controls.
- **Pink materials:** a render pipeline asset was assigned but the materials were generated for the built-in pipeline (or the reverse). Delete `Assets/Scenes/TestDistrict.unity` and `Assets/Scenes/TestDistrictMaterials/` so they are regenerated.
- **Compile errors in Unity scripts:** expected on the first real build. The Unity code has only been checked against hand-written stubs, never compiled with real Unity. Read the first error in the build log, fix it, push, and rebuild. Each failed build uses free minutes.
- **Pre-export method not found:** the method name must be exactly `GangsOfBharat.EditorTools.BuildScript.PreExport`, and `BuildScript.cs` must be under an `Editor` folder (it is: `Assets/Editor`).
- **Build blocked for minutes:** the monthly free minutes are used up. Wait for the next month. Nothing is billed.
