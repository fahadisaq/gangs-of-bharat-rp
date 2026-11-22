# Content pipeline (Sarvam dialogue and voice, Gemini 2D art)

Python 3.11 scripts using only the standard library (no `pip install`). **Status:** request building, dry runs and parsing are tested offline; the live API calls have **never run** because the sandbox that wrote them has no network. Endpoints, model names and request fields are the best known values at the time of writing. Check them against the current [Sarvam docs](https://docs.sarvam.ai) and [Gemini image generation docs](https://ai.google.dev/gemini-api/docs/image-generation) before the first real run.

## Keys

Keys come only from environment variables and are never printed or written to files:

```bash
export SARVAM_API_KEY=...   # or put it in a local .env (gitignored, see .env.example)
export GEMINI_API_KEY=...
```

Without a key, a real run exits with a clear message (exit code 2). `--dry-run` works without a key and prints every planned request with the key header shown as a placeholder.

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

The tests block `urllib.request.urlopen`, so they never touch the network, and they check that a key set in the environment never appears in the output.
