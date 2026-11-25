"""Offline tests for tools/content/build_all.py. Run: python3 -m pytest tools/content/tests -q

Network access is disabled (urlopen fails) and Blender is never started.
"""

from __future__ import annotations

import json
import subprocess
import sys
import urllib.request
from pathlib import Path

import pytest

CONTENT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CONTENT))

import build_all
import gemini_art
import sarvam_dialogue

FAKE_KEY = "test-key-DO-NOT-PRINT-9b1e77"


@pytest.fixture(autouse=True)
def no_network(monkeypatch):
    def blocked(*args, **kwargs):
        raise AssertionError("network access attempted in an offline test")

    monkeypatch.setattr(urllib.request, "urlopen", blocked)
    monkeypatch.delenv("SARVAM_API_KEY", raising=False)
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)


def _fake_out(out: Path) -> None:
    voices = out / "onboarding_intro"
    voices.mkdir(parents=True)
    (voices / "intro_b1_001.en.wav").write_bytes(b"RIFFen")
    (voices / "intro_b1_001.hi.wav").write_bytes(b"RIFFhi")
    (voices / "notes.txt").write_text("not a wav")
    (out / "onboarding_intro.translated.json").write_text(json.dumps({"id": "onboarding_intro"}))
    signs = out / "gemini" / "shop_sign"
    signs.mkdir(parents=True)
    (signs / "a_chai_and_vada_pav_stall_named_pandey_c_1.png").write_bytes(b"\x89PNG")
    (out / "gemini" / "stray.txt").write_text("file directly under gemini is ignored")


def _files(root: Path) -> set[str]:
    return {str(p.relative_to(root)) for p in root.rglob("*") if p.is_file()}


# ---------------------------------------------------------------- copy step


def test_copy_matches_colab_layout(tmp_path):
    out, assets = tmp_path / "out", tmp_path / "Assets"
    _fake_out(out)
    result = build_all.run_copy(out, assets, dry_run=False)
    assert result.ok and result.detail == "4 file(s) copied"
    assert _files(assets) == {
        "Audio/Dialogue/onboarding_intro/intro_b1_001.en.wav",
        "Audio/Dialogue/onboarding_intro/intro_b1_001.hi.wav",
        "Localization/Dialogue/onboarding_intro.json",
        "Art/Generated/shop_sign/a_chai_and_vada_pav_stall_named_pandey_c_1.png",
    }
    assert (assets / "Audio/Dialogue/onboarding_intro/intro_b1_001.hi.wav").read_bytes() == b"RIFFhi"


def test_copy_with_nothing_generated_is_ok(tmp_path):
    result = build_all.run_copy(tmp_path / "missing", tmp_path / "Assets", dry_run=False)
    assert result.ok and result.detail == "0 file(s) copied"
    assert not (tmp_path / "Assets").exists()


def test_copy_dry_run_writes_nothing(tmp_path, capsys):
    out, assets = tmp_path / "out", tmp_path / "Assets"
    _fake_out(out)
    result = build_all.run_copy(out, assets, dry_run=True)
    assert result.ok and "4 file(s) planned" in result.detail
    assert not assets.exists()
    assert "would copy" in capsys.readouterr().out


def test_copy_overwrites_previous_files(tmp_path):
    out, assets = tmp_path / "out", tmp_path / "Assets"
    _fake_out(out)
    build_all.run_copy(out, assets, dry_run=False)
    (out / "onboarding_intro" / "intro_b1_001.en.wav").write_bytes(b"RIFFnew")
    build_all.run_copy(out, assets, dry_run=False)
    assert (assets / "Audio/Dialogue/onboarding_intro/intro_b1_001.en.wav").read_bytes() == b"RIFFnew"


# ---------------------------------------------------------------- planning


def test_api_argv_pass_dry_run_and_out(tmp_path):
    argv = build_all.sarvam_argv(tmp_path, dry_run=True)
    assert argv[-1] == "--dry-run"
    assert argv[argv.index("--languages") + 1] == "en,hi"
    assert Path(argv[argv.index("--script") + 1]).name == "onboarding_dialogue.json"
    assert argv[argv.index("--out") + 1] == str(tmp_path)
    assert "--dry-run" not in build_all.sarvam_argv(tmp_path, dry_run=False)
    assert build_all.gemini_argv("ui_icon", tmp_path, True) == ["--preset", "ui_icon", "--out", str(tmp_path),
                                                                "--dry-run"]


def test_presets_exist_in_gemini_art():
    assert set(build_all.GEMINI_PRESETS) == set(gemini_art.PRESETS)


def test_blender_commands_use_colab_names(tmp_path):
    cmds = build_all.blender_commands("/opt/blender/blender", tmp_path)
    targets = [str(t.relative_to(tmp_path)) for _, t in cmds]
    assert targets == [
        "Art/Models/City/Block_Chawl_01.fbx",
        "Art/Models/City/Block_Market_01.fbx",
        "Art/Models/City/Block_OldCity_01.fbx",
        "Art/Models/Vehicles/Auto_01.fbx",
        "Art/Models/Vehicles/Hatchback_01.fbx",
    ]
    cmd, target = cmds[1]
    assert cmd[:3] == ["/opt/blender/blender", "-b", "-P"]
    assert Path(cmd[3]).name == "gen_city_block.py" and Path(cmd[3]).is_file()
    assert cmd[4] == "--"
    assert cmd[5:] == ["--seed", "2", "--style", "market", "--out", str(target)]
    assert Path(cmds[4][0][3]).name == "gen_vehicle_auto.py"
    assert cmds[4][0][5:9] == ["--kind", "hatchback", "--seed", "1"]


def test_blender_dry_run_never_starts_blender(tmp_path):
    def no_run(*args, **kwargs):
        raise AssertionError("blender started in dry run")

    results = build_all.run_blender("/does/not/exist/blender", tmp_path, dry_run=True, runner=no_run)
    assert len(results) == 5 and all(r.ok for r in results)


def test_blender_missing_executable_fails_each_job(tmp_path):
    results = build_all.run_blender("/does/not/exist/blender", tmp_path, dry_run=False)
    assert len(results) == 5 and not any(r.ok for r in results)


def test_blender_runner_results(tmp_path, monkeypatch):
    fake = tmp_path / "blender"
    fake.write_text("")
    calls = []

    def runner(cmd, **kwargs):
        calls.append(cmd)
        out = Path(cmd[cmd.index("--out") + 1])
        if "chawl" in cmd:
            return subprocess.CompletedProcess(cmd, 1)
        if "hatchback" not in cmd:  # hatchback "succeeds" but writes nothing
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_bytes(b"fbx")
        return subprocess.CompletedProcess(cmd, 0)

    results = build_all.run_blender(str(fake), tmp_path / "Assets", dry_run=False, runner=runner)
    assert len(calls) == 5
    assert [r.ok for r in results] == [False, True, True, True, False]
    assert "missing" in results[4].detail


# ---------------------------------------------------------------- main


def test_main_dry_run_skip_gemini_works_without_key(tmp_path, capsys):
    code = build_all.main(["--dry-run", "--skip-gemini", "--out", str(tmp_path / "out"),
                           "--unity-assets", str(tmp_path / "Assets")])
    text = capsys.readouterr().out
    assert code == 0
    assert "SARVAM_API_KEY is not set; showing planned requests only" in text
    assert "ok     sarvam" in text and "ok     copy" in text
    assert "gemini" not in text.split("=== summary ===")[1]
    assert not (tmp_path / "Assets").exists() and not (tmp_path / "out").exists()


def test_main_dry_run_all_steps_with_keys_never_prints_them(tmp_path, capsys, monkeypatch):
    monkeypatch.setenv("SARVAM_API_KEY", FAKE_KEY)
    monkeypatch.setenv("GEMINI_API_KEY", FAKE_KEY)
    code = build_all.main(["--dry-run", "--blender", "/nope/blender", "--out", str(tmp_path / "out"),
                           "--unity-assets", str(tmp_path / "Assets")])
    captured = capsys.readouterr()
    assert code == 0
    assert FAKE_KEY not in captured.out + captured.err
    assert "<redacted>" in captured.out
    summary = captured.out.split("=== summary ===")[1]
    for preset in build_all.GEMINI_PRESETS:
        assert f"ok     gemini {preset}" in summary
    assert "ok     blender Hatchback_01.fbx" in summary
    assert "11 ok, 0 failed" in summary


def test_main_keeps_going_and_exits_1(tmp_path, capsys, monkeypatch):
    def broken(argv):
        raise RuntimeError("boom")

    calls = []

    def gemini_main(argv):
        calls.append(argv[1])
        return 1 if argv[1] == "road_texture" else 0

    monkeypatch.setattr(sarvam_dialogue, "main", broken)
    monkeypatch.setattr(gemini_art, "main", gemini_main)
    out = tmp_path / "out"
    _fake_out(out)
    code = build_all.main(["--out", str(out), "--unity-assets", str(tmp_path / "Assets")])
    summary = capsys.readouterr().out.split("=== summary ===")[1]
    assert code == 1
    assert calls == list(build_all.GEMINI_PRESETS)
    assert "FAILED sarvam: RuntimeError: boom" in summary
    assert "FAILED gemini road_texture: exit code 1" in summary
    assert "ok     copy: 4 file(s) copied" in summary
    assert "4 ok, 2 failed" in summary


def test_real_run_without_key_fails_cleanly(tmp_path, capsys):
    code = build_all.main(["--skip-gemini", "--out", str(tmp_path / "out"),
                           "--unity-assets", str(tmp_path / "Assets")])
    summary = capsys.readouterr().out.split("=== summary ===")[1]
    assert code == 1
    assert "FAILED sarvam: exit code 2 (key not set?)" in summary


def test_help_runs():
    proc = subprocess.run([sys.executable, str(CONTENT / "build_all.py"), "--help"], capture_output=True,
                          text=True, check=False)
    assert proc.returncode == 0
    for flag in ("--skip-sarvam", "--skip-gemini", "--blender", "--dry-run"):
        assert flag in proc.stdout
