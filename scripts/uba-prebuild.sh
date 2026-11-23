#!/usr/bin/env bash
# Unity Build Automation pre-build script. Fails the build if the Unity copy of the shared code is stale.
# Portable: plain bash and POSIX tools only (also runs under Git Bash on Windows build machines).
set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
echo "uba-prebuild: checking shared code sync in $ROOT"
if ! bash "$ROOT/scripts/sync-shared-to-unity.sh" --check; then
  echo "uba-prebuild: Unity copy of shared code is out of date. Run scripts/sync-shared-to-unity.sh and commit." >&2
  exit 1
fi
echo "uba-prebuild: OK"
