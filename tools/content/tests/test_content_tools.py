"""Offline tests for tools/content. Run: python3 -m pytest tools/content/tests -q

Network access is disabled for every test: urlopen is replaced with a function that fails.
"""

from __future__ import annotations

import base64
import json
import sys
import urllib.request
from pathlib import Path

import pytest

CONTENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CONTENT))

import content_common
import gemini_art
import sarvam_dialogue

FAKE_KEY = "test-key-DO-NOT-PRINT-4f2a9c"
ONBOARDING = CONTENT / "data" / "onboarding_dialogue.json"


@pytest.fixture(autouse=True)
def no_network(monkeypatch):
    def blocked(*args, **kwargs):
        raise AssertionError("network access attempted in an offline test")

    monkeypatch.setattr(urllib.request, "urlopen", blocked)
    monkeypatch.delenv("SARVAM_API_KEY", raising=False)
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)


def _script(**overrides):
    data = {
        "id": "t",
        "speakers": {"a": {"name": "A", "voice": {"en": "karun", "hi": "vidya"}}},
        "lines": [
            {"id": "l1", "speaker": "a", "en": "Hello there.", "hi": ""},
            {"id": "l2", "speaker": "a", "en": "Welcome.", "hi": "स्वागत है।"},
        ],
    }
    data.update(overrides)
    return data


# ----------------------------------------------------------------- dialogue parsing


def test_onboarding_dialogue_parses_and_matches_story_lines():
    script = sarvam_dialogue.load_dialogue(ONBOARDING)
    assert script.id == "onboarding_intro"
    assert len(script.lines) >= 15
    beats = {line.beat for line in script.lines}
    assert beats == {1, 2, 3, 4, 5, 6, 7}
    by_en = {line.en: line for line in script.lines}
    chacha = by_en["Welcome to Mayanagri, son. This city never sleeps, and it never forgives."]
    assert chacha.hi.startswith("मायानगरी")
    assert any(not line.hi for line in script.lines), "some lines must be left for translation"


def test_parse_rejects_duplicate_ids():
    data = _script()
    data["lines"][1]["id"] = "l1"
    with pytest.raises(sarvam_dialogue.DialogueError, match="duplicate"):
        sarvam_dialogue.parse_dialogue(data)


def test_parse_rejects_unknown_speaker_and_empty_english():
    data = _script()
    data["lines"][0]["speaker"] = "nobody"
    with pytest.raises(sarvam_dialogue.DialogueError, match="unknown speaker"):
        sarvam_dialogue.parse_dialogue(data)
    data = _script()
    data["lines"][0]["en"] = "  "
    with pytest.raises(sarvam_dialogue.DialogueError, match="English"):
        sarvam_dialogue.parse_dialogue(data)


def test_parse_rejects_bad_shapes(tmp_path):
    with pytest.raises(sarvam_dialogue.DialogueError):
        sarvam_dialogue.parse_dialogue([])
    with pytest.raises(sarvam_dialogue.DialogueError):
        sarvam_dialogue.parse_dialogue(_script(lines=[]))
    bad = tmp_path / "bad.json"
    bad.write_text("{not json", encoding="utf-8")
    with pytest.raises(sarvam_dialogue.DialogueError, match="not valid JSON"):
        sarvam_dialogue.load_dialogue(bad)


# ----------------------------------------------------------------- request building


def test_translate_request_fields():
    req = sarvam_dialogue.build_translate_request("Hello", "en", "hi")
    assert req.method == "POST"
    assert req.url == "https://api.sarvam.ai/translate"
    assert req.json_body["input"] == "Hello"
    assert req.json_body["source_language_code"] == "en-IN"
    assert req.json_body["target_language_code"] == "hi-IN"
    assert req.secret_headers == ("api-subscription-key",)
    assert req.headers["api-subscription-key"] == ""


def test_plan_translates_only_missing_hindi_and_voices_both_languages():
    script = sarvam_dialogue.parse_dialogue(_script())
    reqs = sarvam_dialogue.plan_dialogue(script, ["en", "hi"], skip_tts=False)
    translates = [r for r in reqs if r.url.endswith("/translate")]
    tts = [r for r in reqs if r.url.endswith("/text-to-speech")]
    assert len(translates) == 1 and translates[0].json_body["input"] == "Hello there."
    assert len(tts) == 4
    hi_l2 = next(r for r in tts if r.purpose == "tts l2.hi")
    assert hi_l2.json_body["text"] == "स्वागत है।"
    assert hi_l2.json_body["speaker"] == "vidya"
    assert hi_l2.json_body["target_language_code"] == "hi-IN"
    assert len(sarvam_dialogue.plan_dialogue(script, ["en", "hi"], skip_tts=True)) == 1


def test_stt_request_is_multipart():
    req = sarvam_dialogue.build_stt_request(b"RIFFxxxx", "clip.wav", "hi")
    assert req.headers["Content-Type"].startswith("multipart/form-data; boundary=")
    assert b'name="file"; filename="clip.wav"' in req.raw_body
    assert "8 bytes" in req.describe(False)["body"]


def test_gemini_request_and_presets():
    planned = gemini_art.plan("shop_sign", None, 2, gemini_art.DEFAULT_MODEL)
    assert len(planned) == 2 * len(gemini_art.PRESETS["shop_sign"].subjects)
    name, req = planned[0]
    assert name.endswith("_1")
    assert req.url.endswith(f"/models/{gemini_art.DEFAULT_MODEL}:generateContent")
    assert "key=" not in req.url
    prompt = req.json_body["contents"][0]["parts"][0]["text"]
    assert "Mayanagri" in prompt and "No real-world brands" in prompt
    assert req.json_body["generationConfig"]["responseModalities"] == ["TEXT", "IMAGE"]
    for preset in ("shop_sign", "road_texture", "ui_icon", "loading_screen"):
        assert preset in gemini_art.PRESETS


def test_gemini_extract_images():
    data = base64.b64encode(b"\x89PNGfake").decode()
    resp = {"candidates": [{"content": {"parts": [{"text": "here"}, {"inlineData": {"mimeType": "image/png", "data": data}}]}}]}
    assert gemini_art.extract_images(resp) == [("image/png", b"\x89PNGfake")]
    assert gemini_art.extract_images({}) == []


# ----------------------------------------------------------------- dry run and keys


def test_sarvam_dry_run_without_key(capsys):
    rc = sarvam_dialogue.main(["--dry-run", "--script", str(ONBOARDING)])
    out = capsys.readouterr().out
    assert rc == 0
    assert "SARVAM_API_KEY is not set" in out
    assert "planned request(s), nothing sent" in out
    assert "https://api.sarvam.ai/translate" in out
    assert "https://api.sarvam.ai/text-to-speech" in out


def test_sarvam_dry_run_with_key_never_prints_it(capsys, monkeypatch):
    monkeypatch.setenv("SARVAM_API_KEY", FAKE_KEY)
    rc = sarvam_dialogue.main(["--dry-run", "--script", str(ONBOARDING)])
    captured = capsys.readouterr()
    assert rc == 0
    assert FAKE_KEY not in captured.out + captured.err
    assert content_common.REDACTED in captured.out


def test_sarvam_missing_key_clear_message(capsys, tmp_path):
    rc = sarvam_dialogue.main(["--script", str(ONBOARDING), "--out", str(tmp_path)])
    err = capsys.readouterr().err
    assert rc == 2
    assert "SARVAM_API_KEY is not set" in err
    assert not any(tmp_path.iterdir())


def test_translate_chat_and_stt_dry_run(capsys, tmp_path):
    assert sarvam_dialogue.main(["--mode", "translate-chat", "--text", "Where is the hospital?", "--dry-run"]) == 0
    assert "Where is the hospital?" in capsys.readouterr().out
    clip = tmp_path / "clip.wav"
    clip.write_bytes(b"RIFF0000WAVE")
    assert sarvam_dialogue.main(["--mode", "stt", "--audio", str(clip), "--dry-run"]) == 0
    assert "speech-to-text" in capsys.readouterr().out
    assert sarvam_dialogue.main(["--mode", "translate-chat", "--text", "hi"]) == 2
    assert "SARVAM_API_KEY is not set" in capsys.readouterr().err


def test_gemini_dry_run_and_missing_key(capsys, monkeypatch, tmp_path):
    assert gemini_art.main(["--preset", "ui_icon", "--dry-run"]) == 0
    out = capsys.readouterr().out
    assert "GEMINI_API_KEY is not set" in out and "generateContent" in out
    monkeypatch.setenv("GEMINI_API_KEY", FAKE_KEY)
    assert gemini_art.main(["--preset", "ui_icon", "--dry-run", "--subject", "a rupee coin"]) == 0
    captured = capsys.readouterr()
    assert FAKE_KEY not in captured.out + captured.err
    monkeypatch.delenv("GEMINI_API_KEY")
    assert gemini_art.main(["--preset", "ui_icon", "--out", str(tmp_path)]) == 2
    assert "GEMINI_API_KEY is not set" in capsys.readouterr().err


# ----------------------------------------------------------------- live path with a fake server


class _FakeResponse:
    def __init__(self, payload):
        self._data = json.dumps(payload).encode()

    def read(self):
        return self._data

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def test_run_dialogue_with_fake_api(monkeypatch, tmp_path):
    seen = []

    def fake_urlopen(req, timeout):
        seen.append(req)
        assert req.get_header("Api-subscription-key") == FAKE_KEY
        if req.full_url.endswith("/translate"):
            return _FakeResponse({"translated_text": "नमस्ते।"})
        return _FakeResponse({"audios": [base64.b64encode(b"RIFFwav").decode()]})

    monkeypatch.setattr(urllib.request, "urlopen", fake_urlopen)
    script = sarvam_dialogue.parse_dialogue(_script())
    out_json = sarvam_dialogue.run_dialogue(script, ["en", "hi"], False, tmp_path, FAKE_KEY)
    data = json.loads(out_json.read_text(encoding="utf-8"))
    assert data["lines"][0]["hi"] == "नमस्ते।"
    assert data["lines"][1]["hi"] == "स्वागत है।"
    assert (tmp_path / "t" / "l1.hi.wav").read_bytes() == b"RIFFwav"
    assert len(seen) == 5  # 1 translate + 4 TTS
    assert FAKE_KEY not in out_json.read_text(encoding="utf-8")


def test_scrub_removes_echoed_key():
    assert FAKE_KEY not in content_common.scrub(f"bad key {FAKE_KEY}", FAKE_KEY)
