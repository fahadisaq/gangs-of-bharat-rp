# Build the Android APK on your Mac (free)

This is an optional local fallback, not the recommended path. The recommended path is [Unity Build Automation](BUILD_UNITY_CLOUD.md). This one uses the free Unity Personal license on your own Mac. The build script is unverified until it has run once on a real Unity install.

## Prerequisites

1. [Unity Hub](https://unity.com/download), signed in, with a free **Personal** license activated (Preferences > Licenses).
2. Unity editor **6000.0.32f1** (the version in `client/GangsOfBharat.Unity/ProjectSettings/ProjectVersion.txt`). If it is not in the Hub list, use Installs > Install Editor > Archive.
3. In the install dialog, tick **Android Build Support** and its two submodules **OpenJDK** and **Android SDK & NDK Tools**. Add them later via Installs > gear icon > Add modules.
4. GitHub CLI (`gh`), logged in.

## One-paste command

Paste into Terminal:

```bash
([ -d ~/gangs-of-bharat-rp ] && git -C ~/gangs-of-bharat-rp pull || gh repo clone fahadisaq/gangs-of-bharat-rp ~/gangs-of-bharat-rp) && ~/gangs-of-bharat-rp/scripts/build-android-mac.sh
```

Until the `feature/codemagic-build` branch is merged, the script is not on the default branch. Run this first, then the command above:

```bash
[ -d ~/gangs-of-bharat-rp ] || gh repo clone fahadisaq/gangs-of-bharat-rp ~/gangs-of-bharat-rp
git -C ~/gangs-of-bharat-rp fetch origin && git -C ~/gangs-of-bharat-rp checkout feature/codemagic-build
```

Or in one line:

```bash
([ -d ~/gangs-of-bharat-rp ] || gh repo clone fahadisaq/gangs-of-bharat-rp ~/gangs-of-bharat-rp) && git -C ~/gangs-of-bharat-rp fetch origin && git -C ~/gangs-of-bharat-rp checkout feature/codemagic-build && git -C ~/gangs-of-bharat-rp pull && ~/gangs-of-bharat-rp/scripts/build-android-mac.sh
```

The script finds Unity at `/Applications/Unity/Hub/Editor/6000.0.32f1/Unity.app/Contents/MacOS/Unity`. If Unity is elsewhere, set `UNITY_PATH=/path/to/Unity`. If Unity or Android Build Support is missing, it prints what to install and stops.

## Result

- APK: `~/gangs-of-bharat-rp/build/Android/GangsOfBharat.apk`
- Log: `~/gangs-of-bharat-rp/build/unity-build.log` (the last 60 lines are printed on failure)

The first build takes several minutes. Close Unity Hub's open editor for this project first, because a project open in the editor cannot be opened in batch mode.

## Get it onto the phone

- **USB and adb:** enable Developer options and USB debugging on the phone, connect it, then run `INSTALL=1 ~/gangs-of-bharat-rp/scripts/build-android-mac.sh` (needs `adb` on PATH, for example from Android Platform Tools). Or run `adb install -r ~/gangs-of-bharat-rp/build/Android/GangsOfBharat.apk`.
- **AirDrop or Drive:** send the APK to the phone through Google Drive or any file transfer, open it, and allow "Install unknown apps" for the app you opened it from. AirDrop does not work with Android phones.

## Signing

Without keystore variables the APK is signed with the Unity debug key, which is fine for testing. For a release key, set `ANDROID_KEYSTORE_*` as described in `BuildScript.cs`. Never commit keystores or passwords.
