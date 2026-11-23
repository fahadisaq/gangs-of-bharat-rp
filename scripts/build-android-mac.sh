#!/usr/bin/env bash
# Builds the Android APK on a Mac with a locally installed Unity editor.
# Uses the license already activated in Unity Hub. No credentials are read or stored here.
#
# Usage:   scripts/build-android-mac.sh
# Options (environment):
#   UNITY_PATH=/path/to/Unity   override the editor binary
#   INSTALL=1                   run "adb install -r" afterwards if adb is on PATH
# Output:  build/Android/GangsOfBharat.apk (repo root)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/client/GangsOfBharat.Unity"
APK="$ROOT/build/Android/GangsOfBharat.apk"
LOG="$ROOT/build/unity-build.log"

VERSION="$(sed -n 's/^m_EditorVersion: *//p' "$PROJECT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
if [ -z "$VERSION" ]; then
  echo "Could not read the editor version from ProjectSettings/ProjectVersion.txt" >&2
  exit 1
fi

UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"

if [ ! -x "$UNITY" ]; then
  cat >&2 <<MSG
Unity not found: $UNITY

Install it:
  1. Install Unity Hub (https://unity.com/download) and sign in.
  2. In Unity Hub, activate a free Personal license (Preferences > Licenses).
  3. Installs > Install Editor > Archive > install version $VERSION.
     Tick "Android Build Support" with "OpenJDK" and "Android SDK & NDK Tools".
Then run this script again. To use another location: UNITY_PATH=/path/to/Unity $0
MSG
  exit 1
fi

# UNITY_PATH may point anywhere; derive the editor folder from Unity.app when possible.
EDITOR_DIR="${UNITY%/Unity.app/Contents/MacOS/Unity}"
if [ ! -d "$EDITOR_DIR/PlaybackEngines/AndroidPlayer" ]; then
  cat >&2 <<MSG
Android Build Support is missing: $EDITOR_DIR/PlaybackEngines/AndroidPlayer

Add it: Unity Hub > Installs > $VERSION > gear icon > Add modules.
Tick "Android Build Support", "OpenJDK" and "Android SDK & NDK Tools", then run this script again.
MSG
  exit 1
fi

echo "Checking the shared code copy in the Unity project..."
"$ROOT/scripts/sync-shared-to-unity.sh" --check

mkdir -p "$ROOT/build/Android"
rm -f "$APK"
echo "Building with Unity $VERSION. This takes several minutes. Log: $LOG"

set +e
"$UNITY" -quit -batchmode -nographics \
  -projectPath "$PROJECT" \
  -buildTarget Android \
  -executeMethod GangsOfBharat.EditorTools.BuildScript.BuildAndroid \
  -logFile "$LOG"
STATUS=$?
set -e

if [ "$STATUS" -ne 0 ] || [ ! -f "$APK" ]; then
  echo "Build FAILED (exit code $STATUS). Last 60 lines of $LOG:" >&2
  tail -n 60 "$LOG" >&2 || true
  exit 1
fi

echo "Build OK: $APK"

if [ "${INSTALL:-}" = "1" ]; then
  if command -v adb >/dev/null 2>&1; then
    adb install -r "$APK"
  else
    echo "INSTALL=1 set but adb is not on PATH. Copy the APK to the phone manually." >&2
  fi
fi
