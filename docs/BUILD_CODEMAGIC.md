# Building the Android APK on Codemagic

Status: UNVERIFIED. `codemagic.yaml` and `Assets/Editor/BuildScript.cs` were written offline and have never run. Expect to fix small issues on the first build.

GitHub Actions is not used because the account's billing blocks every run. Codemagic's free plan gives 500 macOS build minutes per month. The old workflow `.github/workflows/unity-android.yml` is left unchanged.

Unity version: `6000.0.32f1` (from `client/GangsOfBharat.Unity/ProjectSettings/ProjectVersion.txt`). `UNITY_VERSION` in `codemagic.yaml` must match it.

## Steps

1. Go to codemagic.io and sign up with GitHub.
2. Add an application, choose the repository `gangs-of-bharat-rp`, and pick the Unity/other project type that uses `codemagic.yaml` (configuration file).
3. Open Teams > Global variables and secrets (or the app's Environment variables tab) and create a group named `unity_credentials` with:
   - `UNITY_EMAIL`: your Unity account email
   - `UNITY_PASSWORD`: your Unity password, marked Secure
   - `UNITY_SERIAL`: your Unity license serial, marked Secure (only if you have one)
4. Start a build: select workflow `Unity Android APK` on branch `main` and press Start new build. Pushes to `main` also start it automatically.
5. When it finishes, open the build page and download `GangsOfBharat.apk` from Artifacts.

Enter these values only in the Codemagic UI. Never commit them.

## Known caveat: Unity license

As recalled from Codemagic's Unity guide (not checked, the docs were unreachable when this was written), building on their machines needs a Unity Plus/Pro serial. A free Personal license is tied to the machine it was activated on, so email and password alone may be rejected.

The build script behaves like this:
- With `UNITY_SERIAL` set, it activates with `-serial`, `-username` and `-password`.
- Without it, it tries email and password only. If Unity rejects that, the build stops with the message "ACTIVATION FAILED".
- At the end it runs `-returnlicense` so the seat is freed.

If you have Codemagic's own license steps, send them and the script will be adjusted.

## Optional: signed release APK

`codemagic.yaml` contains commented lines for a release keystore. Without them, Unity signs with its debug keystore, which is fine for testing but not for the Play Store. To enable, create secure variables `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, then uncomment the keystore lines in the build step.
