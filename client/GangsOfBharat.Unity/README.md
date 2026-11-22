# GANGS OF BHARAT: Unity client

Unity 6 LTS project for Android and iOS. **Status: unverified.** The sandbox that created this project has no Unity Editor and no network, so the project has never been opened, imported or built. The client scripts were type-checked only against hand-written API stubs, which proves the C# is well-formed but not that it matches the real Unity APIs. Expect a short round of fixes on first open.

## Open the project

1. Install [Unity Hub](https://unity.com/download) and the Unity 6 LTS editor (`ProjectSettings/ProjectVersion.txt` says `6000.0.32f1`; any newer 6000.0.x LTS patch is fine, Hub offers to upgrade).
2. In the Hub install modules: **Android Build Support** (with OpenJDK and Android SDK & NDK Tools) and, on a Mac, **iOS Build Support**.
3. Hub > **Add** > **Add project from disk** > select `client/GangsOfBharat.Unity`.
4. The first open imports packages and creates `Library/` (gitignored). Unity also creates `.meta` files for every asset; commit those.

### Packages

`Packages/manifest.json` pins these. The versions are a best guess for Unity 6000.0 and may need adjusting in **Window > Package Manager** when the project is first opened:

| Package | Version | Use |
| --- | --- | --- |
| com.unity.render-pipelines.universal | 17.0.3 | URP for mobile |
| com.unity.inputsystem | 1.11.2 | Touch joystick, keyboard, gamepad |
| com.unity.cinemachine | 3.1.2 | Third-person camera |
| com.unity.ugui | 2.0.0 | uGUI and TextMeshPro (merged into ugui in Unity 6) |
| com.unity.addressables | 2.2.2 | Streaming city districts and vehicles |
| com.unity.localization | 1.5.3 | English and Hindi string tables |

After import: **Edit > Project Settings > Player > Active Input Handling = Input System Package (New)**, and create a URP asset (**Create > Rendering > URP Asset (with Universal Renderer)**) and assign it in **Graphics** and **Quality**.

## Layout

| Path | Contents |
| --- | --- |
| `Assets/Scripts/Shared/` | **Generated** copy of `shared/GangsOfBharat.Shared/` with `GangsOfBharat.Shared.asmdef` (`noEngineReferences: true`). Do not edit here |
| `Assets/Scripts/Client/` | Client scripts, `GangsOfBharat.Client.asmdef` (references GangsOfBharat.Shared, Input System, Cinemachine, TextMeshPro, uGUI, Localization) |
| `Assets/Scenes`, `Art`, `Audio`, `Localization`, `Resources` | Empty folders for content |

### Shared code sync

The client and the server use the same C# core (protocol, serialization, movement, catalogs, localization). The source of truth is `shared/GangsOfBharat.Shared/`. The copy under `Assets/Scripts/Shared/` is **committed to git** so a fresh clone opens in Unity without extra steps. After changing anything in `shared/`, run from the repo root:

```bash
scripts/sync-shared-to-unity.sh          # refresh the copy
scripts/sync-shared-to-unity.sh --check  # CI check: fails if the copy is stale
```

The .NET solution (`GangsOfBharat.sln`) does not compile the Unity copy: no project file sits above `client/`, and the shared `.csproj` only globs its own folder.

### Client scripts

| Script | Purpose |
| --- | --- |
| `NetClient.cs` | UDP socket, background receive thread, main-thread dispatch, 20 Hz send tick, shared `ClientConnection` |
| `ClientPrediction.cs` | Local player prediction with shared `Movement`, input ring buffer, reconciliation on snapshots |
| `EntityInterpolator.cs` | Remote players and vehicles, snapshot buffer interpolation 150 ms behind the server |
| `PlayerController.cs` | On-foot input (Input System: on-screen stick, keyboard, gamepad), camera-relative movement |
| `VehicleController.cs` | Enter/exit requests and arcade driving input (shared kinematics, no Rigidbody) |
| `CameraRig.cs` | Cinemachine 3 on-foot and vehicle cameras |
| `ChatUI.cs` | Tabs for Local, Global, Faction, Crew, VIP, Ad, SMS; admin announcements and system notices in every tab |
| `VoiceChat.cs` | Microphone capture, push-to-talk and open mic, Proximity and Faction radio |
| `LanguageSettings.cs` | English / Hindi toggle, saved in PlayerPrefs, sent to the server |
| `ClientStrings.cs` | Client UI strings in English and Hindi, falls back to the shared table |
| `FirebaseBootstrap.cs` | Sign-in flow, guarded by the `FIREBASE_SDK` define |
| `AgeGate.cs` | 18+ confirmation before login |
| `JobHud.cs` | Taxi and food delivery jobs: start buttons, waypoint marker, distance, pay result |

### Known TODOs

- **Opus voice**: `VoiceChat` uses a placeholder 8 kHz mu-law codec (64 kbps). Add [Concentus](https://github.com/lostromb/concentus) (pure C# Opus port, works with IL2CPP) or a native libopus plugin and implement `IVoiceCodec` with it (about 16 kbps).
- **Firebase**: import the Firebase Unity SDK (Auth, Firestore, Analytics, Crashlytics), add `google-services.json` / `GoogleService-Info.plist`, a Google Sign-In plugin and a Sign in with Apple plugin, then add `FIREBASE_SDK` to **Player > Scripting Define Symbols**. Without it the client uses a development token that the server accepts only when started with its dev-token option.
- **Hindi text**: add Noto Sans Devanagari as a TextMeshPro font asset (fallback for the main font) and check conjunct shaping on a device. TextMeshPro has limited complex-script shaping, so a HarfBuzz-based text plugin may be needed for correct Hindi rendering.
- **Scenes and prefabs**: no scene exists yet. A bootstrap scene needs one GameObject each with `NetClient`, `LanguageSettings`, `FirebaseBootstrap`, `AgeGate` (with its panel), a player root with `ClientPrediction`, `PlayerController`, `VehicleController`, `VoiceChat`, an `EntityInterpolator` with player and vehicle prefabs (indexed by vehicle catalog id), the Cinemachine cameras with `CameraRig`, and a HUD canvas with `ChatUI` and `JobHud`.

## Point the client at a server

- In the Inspector on the `NetClient` component: **Server Host** and **Server Port** (default `127.0.0.1` and `7777`).
- Or on desktop/editor builds, pass `-server <host>:<port>` on the command line.
- Start a local server from the repo root: `source scripts/dotnet-env.sh && dotnet run --project server/GangsOfBharat.Server -c Release` (see `server/GangsOfBharat.Server/README.md` for options, including dev tokens). On a phone, use the PC's LAN IP and allow UDP 7777 through the firewall.

## Android build

**File > Build Profiles > Android > Switch Platform**, then **Player Settings**:

| Setting | Value |
| --- | --- |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 only |
| Minimum API Level | 24 (Android 7.0) |
| Target API Level | Highest installed (Google Play requires a recent level) |
| Graphics APIs | Vulkan first, OpenGLES3 as fallback (untick Auto Graphics API) |
| Internet Access | Require |
| Microphone | Used by VoiceChat; the permission is requested at runtime |
| Package name | `com.<yourcompany>.gangsofbharat` |

Build an **AAB** for Google Play (Build App Bundle) or an APK for testing. Sign release builds with a keystore that is never committed (put the keystore password in CI secrets).

## iOS build

Switch the platform to iOS (needs a Mac with Xcode for the final step). Player Settings: Target minimum iOS 15, **Microphone Usage Description** filled in (`NSMicrophoneUsageDescription`, required for voice chat), Architecture ARM64. **Build** exports an Xcode project; open it on a Mac, set the signing team and archive. Sign in with Apple must be enabled as a capability when Google sign-in is offered.

## CI build on GitHub Actions

`.github/workflows/unity-android.yml` in the repo root builds an Android APK with [game-ci/unity-builder](https://game.ci/docs/github/builder). Add these **repository secrets** (Settings > Secrets and variables > Actions); never put credentials in the workflow file or anywhere in the repo:

| Secret | Value |
| --- | --- |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |
| `UNITY_LICENSE` | Contents of the `.ulf` license file (Personal license, see the game-ci activation guide) |

The workflow is written but has never run (no GitHub access from the sandbox).
