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

SARVAM_MD = """## Step 1: Sarvam, intro story voices

Translates the English lines that have no Hindi yet, then records all 20 intro lines in English and Hindi (about 54 Sarvam calls). Machine-translated Hindi is marked for review by a Hindi speaker.
"""

SARVAM = r'''run(
    [
        "python3", "tools/content/sarvam_dialogue.py",
        "--mode", "dialogue",
        "--script", "tools/content/data/onboarding_dialogue.json",
        "--languages", "en,hi",
    ],
    cwd=WORK,
    step="sarvam dialogue",
)
'''

GEMINI_MD = """## Step 2: Gemini, signs, textures, icons and loading screens

About 20 images. If Gemini says the image model needs billing or a quota is used up, this step fails and the rest still runs.
"""

GEMINI = r'''for preset in ("shop_sign", "road_texture", "ui_icon", "loading_screen"):
    run(["python3", "tools/content/gemini_art.py", "--preset", preset], cwd=WORK, step=f"gemini {preset}")
'''

BLENDER_MD = """## Step 3: Blender, 3D city blocks and vehicles

Downloads Blender 4.2 LTS (about 350 MB, free) and builds the models as `.fbx`, which Unity imports directly.
"""

BLENDER = r'''BLENDER_URL = "https://download.blender.org/release/Blender4.2/blender-4.2.3-linux-x64.tar.xz"
BLENDER_DIR = pathlib.Path("/content/blender")
BLENDER = BLENDER_DIR / "blender-4.2.3-linux-x64" / "blender"
MODELS = UNITY / "Art" / "Models"

if not BLENDER.exists():
    run(["apt-get", "-qq", "install", "-y", "libxi6", "libxxf86vm1", "libxfixes3", "libxrender1",
         "libgl1", "libxkbcommon0", "libsm6"], step="blender system libraries")
    BLENDER_DIR.mkdir(parents=True, exist_ok=True)
    run(["wget", "-q", "-O", "/content/blender.tar.xz", BLENDER_URL], step="download blender")
    run(["tar", "-xf", "/content/blender.tar.xz", "-C", str(BLENDER_DIR)], step="unpack blender")

jobs = [
    ("gen_city_block.py", ["--seed", "1", "--style", "chawl"], MODELS / "City" / "Block_Chawl_01.fbx"),
    ("gen_city_block.py", ["--seed", "2", "--style", "market"], MODELS / "City" / "Block_Market_01.fbx"),
    ("gen_city_block.py", ["--seed", "3", "--style", "oldcity"], MODELS / "City" / "Block_OldCity_01.fbx"),
    ("gen_vehicle_auto.py", ["--kind", "auto", "--seed", "1"], MODELS / "Vehicles" / "Auto_01.fbx"),
    ("gen_vehicle_auto.py", ["--kind", "hatchback", "--seed", "1"], MODELS / "Vehicles" / "Hatchback_01.fbx"),
]
if BLENDER.exists():
    for script, args, out in jobs:
        run([str(BLENDER), "-b", "-P", script, "--", *args, "--out", str(out)],
            cwd=WORK / "tools" / "blender", step=f"blender {out.name}")
else:
    FAILED.append("blender not installed")
'''

COPY_MD = """## Step 4: put the files into the Unity project
"""

COPY = r'''out = WORK / "tools" / "content" / "out"
copied = 0

voices = out / "onboarding_intro"
if voices.exists():
    target = UNITY / "Audio" / "Dialogue" / "onboarding_intro"
    target.mkdir(parents=True, exist_ok=True)
    for wav in sorted(voices.glob("*.wav")):
        shutil.copy2(wav, target / wav.name)
        copied += 1

translated = out / "onboarding_intro.translated.json"
if translated.exists():
    target = UNITY / "Localization" / "Dialogue"
    target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(translated, target / "onboarding_intro.json")
    copied += 1

art = out / "gemini"
if art.exists():
    for preset_dir in sorted(p for p in art.iterdir() if p.is_dir()):
        target = UNITY / "Art" / "Generated" / preset_dir.name
        target.mkdir(parents=True, exist_ok=True)
        for image in sorted(preset_dir.iterdir()):
            shutil.copy2(image, target / image.name)
            copied += 1

models = sorted((UNITY / "Art" / "Models").rglob("*.fbx"))
print(f"Copied {copied} voice/text/image files. Models built: {len(models)}")
for m in models:
    print("  ", m.relative_to(WORK))
'''

PUSH_MD = """## Step 5: upload to GitHub
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
        md(SARVAM_MD), code(SARVAM),
        md(GEMINI_MD), code(GEMINI),
        md(BLENDER_MD), code(BLENDER),
        md(COPY_MD), code(COPY),
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
