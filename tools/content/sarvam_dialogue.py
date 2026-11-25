#!/usr/bin/env python3
"""Sarvam AI dialogue pipeline for GANGS OF BHARAT (Python 3.11, standard library only).

Modes:
  dialogue        Reads a JSON dialogue script with English lines, translates lines without a
                  Hindi text to Hindi (Sarvam translate) and voices every line in English and
                  Hindi (Sarvam TTS). Writes WAV files and a translated copy of the script to
                  tools/content/out/.
  translate-chat  Translates one chat message (helper for in-game auto-translate; the game
                  server would call the same endpoint).
  stt             Transcribes a short WAV clip (stub for voice-report moderation).

The key is read from the SARVAM_API_KEY environment variable and is never printed.
--dry-run prints the planned requests without any network access and works without a key.

IMPORTANT: endpoint paths, model names, speaker names and request fields below are the best
known values when this was written offline (no network in the sandbox). Verify them against the
current Sarvam docs (https://docs.sarvam.ai) before the first real run.

Examples:
  python3 tools/content/sarvam_dialogue.py --dry-run --script tools/content/data/onboarding_dialogue.json
  SARVAM_API_KEY=... python3 tools/content/sarvam_dialogue.py --script tools/content/data/onboarding_dialogue.json
  python3 tools/content/sarvam_dialogue.py --mode translate-chat --text "Where is the police station?" --dry-run
  python3 tools/content/sarvam_dialogue.py --mode stt --audio report.wav --dry-run
"""

from __future__ import annotations

import argparse
import base64
import json
import sys
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))

from content_common import (
    DEFAULT_OUT_DIR,
    ApiError,
    MissingKeyError,
    PlannedRequest,
    print_dry_run,
    require_key,
    send,
)

KEY_ENV = "SARVAM_API_KEY"
API_BASE = "https://api.sarvam.ai"
TRANSLATE_URL = f"{API_BASE}/translate"
TTS_URL = f"{API_BASE}/text-to-speech"
STT_URL = f"{API_BASE}/speech-to-text"
AUTH_HEADER = "api-subscription-key"

# Verify against current Sarvam docs.
TRANSLATE_MODEL = "mayura:v1"
TTS_MODEL = "bulbul:v3"
STT_MODEL = "saarika:v2"
TTS_SAMPLE_RATE = 22050
TTS_MAX_CHARS = 1500

LANG_CODES = {"en": "en-IN", "hi": "hi-IN"}
DEFAULT_SPEAKER = {"en": "anushka", "hi": "anushka"}


class DialogueError(ValueError):
    """The dialogue script is malformed."""


@dataclass
class Line:
    id: str
    speaker: str
    en: str
    hi: str
    beat: int | None = None
    note: str = ""


@dataclass
class DialogueScript:
    id: str
    speakers: dict[str, dict[str, Any]]
    lines: list[Line]
    raw: dict[str, Any]

    def voice_for(self, speaker: str, lang: str) -> str:
        voice = self.speakers.get(speaker, {}).get("voice", {})
        return voice.get(lang) or DEFAULT_SPEAKER[lang]


def parse_dialogue(data: Any) -> DialogueScript:
    """Validates a dialogue script (already loaded JSON) and returns it."""
    if not isinstance(data, dict):
        raise DialogueError("dialogue script must be a JSON object")
    script_id = data.get("id")
    if not isinstance(script_id, str) or not script_id.strip():
        raise DialogueError("dialogue script needs a non-empty 'id'")
    speakers = data.get("speakers", {})
    if not isinstance(speakers, dict):
        raise DialogueError("'speakers' must be an object")
    raw_lines = data.get("lines")
    if not isinstance(raw_lines, list) or not raw_lines:
        raise DialogueError("'lines' must be a non-empty list")
    seen: set[str] = set()
    lines: list[Line] = []
    for i, item in enumerate(raw_lines):
        if not isinstance(item, dict):
            raise DialogueError(f"line {i} must be an object")
        line_id = item.get("id")
        if not isinstance(line_id, str) or not line_id:
            raise DialogueError(f"line {i} needs an 'id'")
        if line_id in seen:
            raise DialogueError(f"duplicate line id '{line_id}'")
        seen.add(line_id)
        speaker = item.get("speaker")
        if not isinstance(speaker, str) or speaker not in speakers:
            raise DialogueError(f"line '{line_id}' has unknown speaker '{speaker}'")
        en = item.get("en")
        if not isinstance(en, str) or not en.strip():
            raise DialogueError(f"line '{line_id}' needs English text in 'en'")
        hi = item.get("hi", "")
        if not isinstance(hi, str):
            raise DialogueError(f"line '{line_id}': 'hi' must be a string")
        if len(en) > TTS_MAX_CHARS or len(hi) > TTS_MAX_CHARS:
            raise DialogueError(f"line '{line_id}' is longer than {TTS_MAX_CHARS} characters")
        beat = item.get("beat")
        lines.append(Line(line_id, speaker, en.strip(), hi.strip(), beat if isinstance(beat, int) else None,
                          str(item.get("note", ""))))
    return DialogueScript(script_id.strip(), speakers, lines, data)


def load_dialogue(path: Path) -> DialogueScript:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        raise DialogueError(f"dialogue script not found: {path}") from None
    except json.JSONDecodeError as exc:
        raise DialogueError(f"{path} is not valid JSON: {exc}") from None
    return parse_dialogue(data)


def _headers() -> dict[str, str]:
    return {"Content-Type": "application/json", AUTH_HEADER: ""}


def build_translate_request(text: str, source: str = "en", target: str = "hi",
                            mode: str = "modern-colloquial", purpose: str = "translate") -> PlannedRequest:
    body = {
        "input": text,
        "source_language_code": LANG_CODES.get(source, source),
        "target_language_code": LANG_CODES.get(target, target),
        "model": TRANSLATE_MODEL,
        "mode": mode,
        "enable_preprocessing": True,
    }
    return PlannedRequest("POST", TRANSLATE_URL, _headers(), json_body=body,
                          secret_headers=(AUTH_HEADER,), purpose=purpose)


def build_tts_request(text: str, lang: str, speaker: str, purpose: str = "tts") -> PlannedRequest:
    body = {
        "text": text,
        "target_language_code": LANG_CODES.get(lang, lang),
        "speaker": speaker,
        "model": TTS_MODEL,
        "pitch": 0,
        "pace": 1.0,
        "loudness": 1.0,
        "speech_sample_rate": TTS_SAMPLE_RATE,
        "enable_preprocessing": True,
    }
    return PlannedRequest("POST", TTS_URL, _headers(), json_body=body,
                          secret_headers=(AUTH_HEADER,), purpose=purpose)


def build_stt_request(audio: bytes, filename: str, lang: str = "hi") -> PlannedRequest:
    boundary = "gob" + uuid.uuid4().hex
    parts = []
    for name, value in (("model", STT_MODEL), ("language_code", LANG_CODES.get(lang, lang))):
        parts.append(
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n".encode()
        )
    parts.append(
        (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{filename}\"\r\n"
         "Content-Type: audio/wav\r\n\r\n").encode() + audio + b"\r\n"
    )
    parts.append(f"--{boundary}--\r\n".encode())
    body = b"".join(parts)
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}", AUTH_HEADER: ""}
    summary = (f"multipart form: model={STT_MODEL}, language_code={LANG_CODES.get(lang, lang)}, "
               f"file={filename} ({len(audio)} bytes)")
    return PlannedRequest("POST", STT_URL, headers, raw_body=body, raw_body_summary=summary,
                          secret_headers=(AUTH_HEADER,), purpose="stt")


def plan_dialogue(script: DialogueScript, languages: list[str], skip_tts: bool) -> list[PlannedRequest]:
    """Requests for one script: translations for lines without Hindi, then TTS per language.

    In the plan, TTS for a line still waiting for translation uses a placeholder text; the real
    run substitutes the translated text.
    """
    requests: list[PlannedRequest] = []
    for line in script.lines:
        if "hi" in languages and not line.hi:
            requests.append(build_translate_request(line.en, purpose=f"translate {line.id}"))
    if skip_tts:
        return requests
    for line in script.lines:
        for lang in languages:
            text = line.en if lang == "en" else (line.hi or f"<Hindi translation of {line.id}>")
            requests.append(build_tts_request(text, lang, script.voice_for(line.speaker, lang),
                                              purpose=f"tts {line.id}.{lang}"))
    return requests


def run_dialogue(script: DialogueScript, languages: list[str], skip_tts: bool, out_dir: Path, key: str) -> Path:
    target = out_dir / script.id
    target.mkdir(parents=True, exist_ok=True)
    translated = json.loads(json.dumps(script.raw, ensure_ascii=False))
    for line, raw in zip(script.lines, translated["lines"]):
        if "hi" in languages and not line.hi:
            resp = send(build_translate_request(line.en, purpose=f"translate {line.id}"), key)
            text = resp.get("translated_text")
            if not isinstance(text, str) or not text:
                raise ApiError(f"translate {line.id}: response has no 'translated_text'")
            line.hi = text
            raw["hi"] = text
            raw["hi_source"] = "sarvam-machine-translation (needs Hindi speaker review)"
            print(f"translated {line.id}")
        if skip_tts:
            continue
        for lang in languages:
            text = line.en if lang == "en" else line.hi
            resp = send(build_tts_request(text, lang, script.voice_for(line.speaker, lang),
                                          purpose=f"tts {line.id}.{lang}"), key)
            audios = resp.get("audios")
            if not isinstance(audios, list) or not audios:
                raise ApiError(f"tts {line.id}.{lang}: response has no 'audios'")
            wav = target / f"{line.id}.{lang}.wav"
            wav.write_bytes(base64.b64decode(audios[0]))
            raw.setdefault("audio", {})[lang] = str(wav.relative_to(out_dir))
            print(f"wrote {wav}")
    out_json = out_dir / f"{script.id}.translated.json"
    out_json.write_text(json.dumps(translated, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out_json}")
    return out_json


def translate_chat(text: str, source: str, target: str, key: str) -> str:
    """Auto-translate helper for chat. Returns the translated text."""
    resp = send(build_translate_request(text, source, target, purpose="translate-chat"), key)
    out = resp.get("translated_text")
    if not isinstance(out, str):
        raise ApiError("translate-chat: response has no 'translated_text'")
    return out


def transcribe(audio_path: Path, lang: str, key: str) -> str:
    """STT stub for voice moderation: returns the transcript of a short clip."""
    resp = send(build_stt_request(audio_path.read_bytes(), audio_path.name, lang), key)
    transcript = resp.get("transcript")
    if not isinstance(transcript, str):
        raise ApiError("stt: response has no 'transcript'")
    return transcript


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Sarvam dialogue, translation, TTS and STT for GANGS OF BHARAT.")
    p.add_argument("--mode", choices=["dialogue", "translate-chat", "stt"], default="dialogue")
    p.add_argument("--script", type=Path, help="dialogue script JSON (mode dialogue)")
    p.add_argument("--languages", default="en,hi", help="comma list of en,hi (mode dialogue)")
    p.add_argument("--skip-tts", action="store_true", help="only translate (mode dialogue)")
    p.add_argument("--text", help="chat text (mode translate-chat)")
    p.add_argument("--source-lang", default="en", help="en or hi (mode translate-chat)")
    p.add_argument("--target-lang", default="hi", help="en or hi (mode translate-chat)")
    p.add_argument("--audio", type=Path, help="WAV clip (mode stt)")
    p.add_argument("--stt-lang", default="hi", help="language of the clip (mode stt)")
    p.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR, help="output folder (default tools/content/out)")
    p.add_argument("--dry-run", action="store_true", help="print planned requests, no network, no key needed")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.mode == "dialogue":
            if args.script is None:
                print("error: --script is required for mode dialogue", file=sys.stderr)
                return 2
            languages = [x.strip() for x in args.languages.split(",") if x.strip()]
            bad = [x for x in languages if x not in LANG_CODES]
            if bad or not languages:
                print(f"error: unsupported language(s) {bad}; use en and/or hi", file=sys.stderr)
                return 2
            script = load_dialogue(args.script)
            if args.dry_run:
                print_dry_run(plan_dialogue(script, languages, args.skip_tts), KEY_ENV)
                return 0
            run_dialogue(script, languages, args.skip_tts, args.out, require_key(KEY_ENV))
            return 0
        if args.mode == "translate-chat":
            if not args.text:
                print("error: --text is required for mode translate-chat", file=sys.stderr)
                return 2
            if args.dry_run:
                print_dry_run([build_translate_request(args.text, args.source_lang, args.target_lang,
                                                       purpose="translate-chat")], KEY_ENV)
                return 0
            print(translate_chat(args.text, args.source_lang, args.target_lang, require_key(KEY_ENV)))
            return 0
        # stt
        if args.audio is None:
            print("error: --audio is required for mode stt", file=sys.stderr)
            return 2
        if args.dry_run:
            audio = args.audio.read_bytes() if args.audio.exists() else b""
            print_dry_run([build_stt_request(audio, args.audio.name, args.stt_lang)], KEY_ENV)
            return 0
        print(transcribe(args.audio, args.stt_lang, require_key(KEY_ENV)))
        return 0
    except MissingKeyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except DialogueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except ApiError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
