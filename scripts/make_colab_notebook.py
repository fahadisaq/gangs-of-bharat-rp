"""Builds notebooks/GangsOfBharat_Content.ipynb. Run from the repo root: python3 scripts/make_colab_notebook.py"""

import json
from pathlib import Path

INTRO = """# Gangs of Bharat: content generator (Google Colab)

This notebook runs in Google's cloud, not on your computer. It:

1. Downloads the game code from GitHub.
2. Uses **Sarvam** to translate the intro story into Hindi and record every line as voice (English and Hindi).
3. Uses **Gemini** to draw shop signs, road textures, UI icons and loading screens.
4. Uses **Blender** to build 3D city blocks, an auto-rickshaw and a hatchback.
5. Puts everything into the Unity project folders and uploads it to GitHub on the branch `content/first-batch`.

## One-time setup (about 3 minutes)

**A. Make a GitHub token that can upload** (the Unity token is read-only, so it can't be reused):
1. Open https://github.com/settings/personal-access-tokens/new
2. Name: `Colab content`. Expiration: 30 days.
3. Repository access: **Only select repositories**, then pick `gangs-of-bharat-rp`.
4. Repository permissions, **Contents: Read and write**.
5. Click **Generate token** and copy it.

**B. Add three secrets in Colab:** click the **key icon** in the left sidebar, then **Add new secret** three times:

| Name | Value |
|---|---|
| `SARVAM_API_KEY` | your Sarvam key |
| `GEMINI_API_KEY` | your Gemini key |
| `GITHUB_TOKEN` | the token from step A |

Turn on **Notebook access** for all three. Secrets stay in your Google account and are never saved in this notebook or on GitHub.

**C. Run it:** menu **Runtime, then Run all**. It takes about 10 to 20 minutes. If a step fails, the notebook keeps going with the other steps and lists what failed at the end.
"""

SETUP = r'''import os
import pathlib
import shutil
import subprocess

from google.colab import userdata

REPO = "fahadisaq/gangs-of-bharat-rp"
SOURCE_BRANCH = "feature/codemagic-build"
OUTPUT_BRANCH = "content/first-batch"
WORK = pathlib.Path("/content/gangs-of-bharat-rp")
UNITY = WORK / "client" / "GangsOfBharat.Unity" / "Assets"
FAILED = []

secrets = {}
for name in ("SARVAM_API_KEY", "GEMINI_API_KEY", "GITHUB_TOKEN"):
    try:
        value = userdata.get(name)
    except (userdata.SecretNotFoundError, userdata.NotebookAccessError):
        value = None
    if not value:
        raise SystemExit(f"Secret {name} is missing. Add it with the key icon on the left and turn on Notebook access.")
    secrets[name] = value.strip()

os.environ["SARVAM_API_KEY"] = secrets["SARVAM_API_KEY"]
os.environ["GEMINI_API_KEY"] = secrets["GEMINI_API_KEY"]


def hide(text):
    for value in secrets.values():
        text = text.replace(value, "<secret>")
    return text


def run(cmd, cwd=None, step=None):
    """Runs a command and prints its output with secrets hidden. Returns True on success."""
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, check=False)
    output = hide(result.stdout + result.stderr)
    print(output[-6000:])
    if result.returncode != 0:
        label = step or " ".join(cmd[:2])
        print(f"FAILED: {label} (exit code {result.returncode})")
        FAILED.append(label)
        return False
    return True


if WORK.exists():
    shutil.rmtree(WORK)
url = f"https://x-access-token:{secrets['GITHUB_TOKEN']}@github.com/{REPO}.git"
if not run(["git", "clone", "--branch", SOURCE_BRANCH, "--single-branch", url, str(WORK)], step="clone"):
    raise SystemExit("Could not download the repository. Check GITHUB_TOKEN (Contents: Read and write on gangs-of-bharat-rp).")
run(["git", "checkout", "-b", OUTPUT_BRANCH], cwd=WORK)
run(["git", "config", "user.name", "Gangs of Bharat Colab"], cwd=WORK)
run(["git", "config", "user.email", "colab@users.noreply.github.com"], cwd=WORK)
print("Repository ready.")
'''

BLENDER_MD = """## Step 1: download Blender

Downloads Blender 4.2 LTS (about 350 MB, free). Step 2 uses it to build the 3D city blocks and vehicles as `.fbx`, which Unity imports directly. If the download fails, step 2 still makes the voices and images.
"""

BLENDER = r'''BLENDER_URL = "https://download.blender.org/release/Blender4.2/blender-4.2.3-linux-x64.tar.xz"
BLENDER_DIR = pathlib.Path("/content/blender")
BLENDER = BLENDER_DIR / "blender-4.2.3-linux-x64" / "blender"

if not BLENDER.exists():
    run(["apt-get", "-qq", "install", "-y", "libxi6", "libxxf86vm1", "libxfixes3", "libxrender1",
         "libgl1", "libxkbcommon0", "libsm6"], step="blender system libraries")
    BLENDER_DIR.mkdir(parents=True, exist_ok=True)
    run(["wget", "-q", "-O", "/content/blender.tar.xz", BLENDER_URL], step="download blender")
    run(["tar", "-xf", "/content/blender.tar.xz", "-C", str(BLENDER_DIR)], step="unpack blender")
print("Blender ready." if BLENDER.exists() else "Blender is not installed; step 2 skips the 3D models.")
'''

BUILD_MD = """## Step 2: make the voices, images and 3D models

Runs `tools/content/build_all.py`, the same command you can run on any computer:

1. **Sarvam** translates the intro lines that have no Hindi yet and records all 20 lines in English and Hindi (about 54 calls). Machine-translated Hindi is marked for review by a Hindi speaker.
2. **Gemini** draws about 20 images: shop signs, road textures, UI icons and loading screens. If Gemini says the image model needs billing or a quota is used up, those steps fail and the rest still runs.
3. **Blender** builds 3 city blocks, an auto-rickshaw and a hatchback.
4. Everything is copied into the Unity project folders.

It takes about 10 to 20 minutes and ends with a summary of what worked.
"""

BUILD = r'''cmd = ["python3", "tools/content/build_all.py"]
if BLENDER.exists():
    cmd += ["--blender", str(BLENDER)]
result = subprocess.run(cmd, cwd=WORK, capture_output=True, text=True, check=False)
output = hide(result.stdout + result.stderr)
print(output[-12000:])
summary = output.split("=== summary ===")[-1]
for line in summary.splitlines():
    if line.startswith("FAILED "):
        FAILED.append(line[len("FAILED "):])
if result.returncode != 0 and not any(line.startswith("FAILED ") for line in summary.splitlines()):
    FAILED.append(f"build_all.py (exit code {result.returncode})")

assets = sorted(p for p in UNITY.rglob("*") if p.is_file() and p.suffix.lower() in {".wav", ".json", ".png", ".jpg", ".fbx"}
                and ("Dialogue" in p.parts or "Generated" in p.parts or "Models" in p.parts))
print(f"Files in the Unity project: {len(assets)}")
for p in assets:
    print("  ", p.relative_to(WORK))
'''

PUSH_MD = """## Step 3: upload to GitHub
"""

PUSH = r'''run(["git", "add", "client/GangsOfBharat.Unity/Assets"], cwd=WORK)
status = subprocess.run(["git", "status", "--porcelain"], cwd=WORK, capture_output=True, text=True,
                        check=False).stdout
if not status.strip():
    print("Nothing new to upload.")
else:
    run(["git", "commit", "-q", "-m", "content: first batch of voices, art and 3D models (generated in Colab)"],
        cwd=WORK, step="commit")
    run(["git", "push", "-u", "origin", OUTPUT_BRANCH], cwd=WORK, step="push")
    print(f"Uploaded: https://github.com/{REPO}/tree/{OUTPUT_BRANCH}")

if FAILED:
    print("\nThese steps failed (copy this list to Kiro):")
    for item in FAILED:
        print("  -", item)
else:
    print("\nAll steps finished. Tell Kiro: content pushed.")
'''

END_MD = """## Done

Tell Kiro **"content pushed"** (and paste the failed-steps list if there is one). Kiro will check the files and add them to the build branch.
"""


def md(text):
    return {"cell_type": "markdown", "metadata": {}, "source": text.splitlines(keepends=True)}


def code(text):
    return {"cell_type": "code", "metadata": {}, "execution_count": None, "outputs": [],
            "source": text.splitlines(keepends=True)}


nb = {
    "nbformat": 4,
    "nbformat_minor": 5,
    "metadata": {
        "colab": {"name": "GangsOfBharat_Content.ipynb", "provenance": []},
        "kernelspec": {"name": "python3", "display_name": "Python 3"},
        "language_info": {"name": "python"},
    },
    "cells": [
        md(INTRO), code(SETUP),
        md(BLENDER_MD), code(BLENDER),
        md(BUILD_MD), code(BUILD),
        md(PUSH_MD), code(PUSH),
        md(END_MD),
    ],
}
for i, cell in enumerate(nb["cells"]):
    cell["id"] = f"cell-{i:02d}"

out = Path("notebooks/GangsOfBharat_Content.ipynb")
out.parent.mkdir(exist_ok=True)
out.write_text(json.dumps(nb, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"wrote {out}")
