# Content pipeline (Sarvam dialogue and voice, Gemini 2D art)

Python 3.11 scripts using only the standard library (no `pip install`). **Status:** request building, dry runs and parsing are tested offline; the live API calls have **never run** because the sandbox that wrote them has no network. Endpoints, model names and request fields are the best known values at the time of writing. Check them against the current [Sarvam docs](https://docs.sarvam.ai) and [Gemini image generation docs](https://ai.google.dev/gemini-api/docs/image-generation) before the first real run.

## Keys

Keys come only from environment variables and are never printed or written to files:

```bash
export SARVAM_API_KEY=...   # or put it in a local .env (gitignored, see .env.example)
export GEMINI_API_KEY=...
```

Without a key, a real run exits with a clear message (exit code 2). `--dry-run` works without a key and prints every planned request with the key header shown as a placeholder.

## Everything in one command: `build_all.py`

Runs Sarvam (intro dialogue, en and hi), Gemini (all four presets), optionally Blender, and then copies the results into the Unity project. Every step runs even if an earlier one failed; the summary at the end lists each step as `ok` or `FAILED`, and the exit code is 1 if anything failed.

```bash
python3 tools/content/build_all.py --dry-run --skip-gemini   # no key, no network, nothing written
python3 tools/content/build_all.py                            # voices + images, then copy
python3 tools/content/build_all.py --blender /Applications/Blender.app/Contents/MacOS/Blender
python3 tools/content/build_all.py --skip-sarvam --skip-gemini --blender blender   # models only
```

| Flag | Effect |
| --- | --- |
| `--skip-sarvam`, `--skip-gemini` | Leave out that API step |
| `--blender PATH` | Blender executable (or a name on `PATH`). Builds `Block_Chawl_01`, `Block_Market_01`, `Block_OldCity_01` (seeds 1-3) and `Auto_01`, `Hatchback_01` with `tools/blender/*.py`, written straight to `Assets/Art/Models/City` and `Assets/Art/Models/Vehicles` |
| `--dry-run` | Passes `--dry-run` to the API scripts, prints Blender commands without starting Blender, lists the copies without writing |
| `--out`, `--unity-assets` | Other output or Unity folders (used by the tests) |

The copy step is the same as the Colab notebook's (`notebooks/GangsOfBharat_Content.ipynb`, built by `scripts/make_colab_notebook.py`, now calls `build_all.py`):

| From `tools/content/out/` | To `client/GangsOfBharat.Unity/Assets/` |
| --- | --- |
| `onboarding_intro/*.wav` | `Audio/Dialogue/onboarding_intro/` |
| `onboarding_intro.translated.json` | `Localization/Dialogue/onboarding_intro.json` |
| `gemini/<preset>/*` | `Art/Generated/<preset>/` |

The Unity scene generator (`Assets/Editor/TestSceneBuilder.cs`) picks these files up if they exist: intro dialogue with voices, shop signs, the worn asphalt road texture and a loading screen.

## Sarvam: `sarvam_dialogue.py`

| Mode | What it does |
| --- | --- |
| `dialogue` (default) | Reads a JSON dialogue script, translates English lines that have no Hindi (`/translate`), voices every line in English and Hindi (`/text-to-speech`), writes `out/<script id>/<line id>.<lang>.wav` and `out/<script id>.translated.json` |
| `translate-chat` | Translates one chat message (helper for auto-translate in chat) |
| `stt` | Transcribes a WAV clip (`/speech-to-text`), a stub for voice-report moderation |

```bash
python3 tools/content/sarvam_dialogue.py --dry-run --script tools/content/data/onboarding_dialogue.json
python3 tools/content/sarvam_dialogue.py --script tools/content/data/onboarding_dialogue.json
python3 tools/content/sarvam_dialogue.py --script tools/content/data/onboarding_dialogue.json --skip-tts
python3 tools/content/sarvam_dialogue.py --mode translate-chat --text "Where is the hospital?" --target-lang hi
python3 tools/content/sarvam_dialogue.py --mode stt --audio report.wav --stt-lang hi
```

The source script is never overwritten. Machine-translated lines get `"hi_source": "sarvam-machine-translation (needs Hindi speaker review)"` in the output file; copy reviewed lines back into `data/` by hand.

### Dialogue script format

```json
{
  "id": "onboarding_intro",
  "speakers": {"chacha": {"name": "Pandey chacha", "voice": {"en": "abhilash", "hi": "abhilash"}}},
  "lines": [
    {"id": "intro_b4_001", "beat": 4, "speaker": "chacha", "en": "Welcome to Mayanagri, son.", "hi": ""}
  ]
}
```

Line ids must be unique, every line needs English text and a known speaker, and `hi` may be empty (it is then translated). `data/onboarding_dialogue.json` holds the intro from `docs/design/03-story.md` (DRAFT, owner review).

## Gemini: `gemini_art.py`

Presets: `shop_sign`, `road_texture`, `ui_icon`, `loading_screen`. All prompts share a style guide (stylized Indian megacity Mayanagri, parody brands only, no real people or logos).

```bash
python3 tools/content/gemini_art.py --list-presets
python3 tools/content/gemini_art.py --preset shop_sign --dry-run
python3 tools/content/gemini_art.py --preset ui_icon --subject "cutting chai glass" --count 2
```

Images go to `tools/content/out/gemini/<preset>/` (gitignored). A person must review every image before it is used.

## Tests

```bash
ruff check tools
python3 -m pytest tools/content/tests -q
```

The tests block `urllib.request.urlopen`, so they never touch the network, and they check that a key set in the environment never appears in the output. `tests/test_build_all.py` checks the copy layout, the Blender command lines (Blender is never started) and that a failing step does not stop the others.
