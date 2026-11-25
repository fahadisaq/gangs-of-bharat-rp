#!/usr/bin/env python3
"""One command for all generated content of GANGS OF BHARAT (Python 3.11, standard library only).

Steps, in order (each step runs even if an earlier one failed):
  1. sarvam            tools/content/sarvam_dialogue.py on data/onboarding_dialogue.json (en, hi)
  2. gemini <preset>   tools/content/gemini_art.py for shop_sign, road_texture, ui_icon, loading_screen
  3. blender <file>    only with --blender PATH: tools/blender/gen_city_block.py (chawl, market,
                       oldcity, seeds 1-3) and gen_vehicle_auto.py (auto, hatchback); the .fbx files
                       are written straight into Assets/Art/Models/City and Assets/Art/Models/Vehicles
  4. copy              copies tools/content/out into the Unity project, the same way as the Colab
                       notebook (scripts/make_colab_notebook.py, COPY cell):
                         out/onboarding_intro/*.wav            -> Assets/Audio/Dialogue/onboarding_intro/
                         out/onboarding_intro.translated.json  -> Assets/Localization/Dialogue/onboarding_intro.json
                         out/gemini/<preset>/*                 -> Assets/Art/Generated/<preset>/

At the end a summary lists every step as ok or FAILED; the exit code is 1 if any step failed.

Keys come only from SARVAM_API_KEY and GEMINI_API_KEY (environment or your shell); this script
never prints them. --dry-run passes --dry-run to the API scripts (no network, no key needed),
prints the Blender commands without running them and lists the copies without writing anything.

Examples:
  python3 tools/content/build_all.py --dry-run --skip-gemini
  python3 tools/content/build_all.py
  python3 tools/content/build_all.py --blender /Applications/Blender.app/Contents/MacOS/Blender
  python3 tools/content/build_all.py --skip-sarvam --skip-gemini --blender blender
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import traceback
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path

CONTENT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(CONTENT_DIR))

from content_common import DEFAULT_OUT_DIR, read_key

REPO_ROOT = CONTENT_DIR.parents[1]
DEFAULT_UNITY_ASSETS = REPO_ROOT / "client" / "GangsOfBharat.Unity" / "Assets"
BLENDER_SCRIPTS = REPO_ROOT / "tools" / "blender"
DIALOGUE_SCRIPT = CONTENT_DIR / "data" / "onboarding_dialogue.json"
DIALOGUE_ID = "onboarding_intro"
LANGUAGES = "en,hi"
GEMINI_PRESETS = ("shop_sign", "road_texture", "ui_icon", "loading_screen")
BLENDER_TIMEOUT_SECONDS = 900

# Same jobs and file names as the Colab notebook (scripts/make_colab_notebook.py, BLENDER cell).
BLENDER_JOBS: tuple[tuple[str, tuple[str, ...], str], ...] = (
    ("gen_city_block.py", ("--seed", "1", "--style", "chawl"), "City/Block_Chawl_01.fbx"),
    ("gen_city_block.py", ("--seed", "2", "--style", "market"), "City/Block_Market_01.fbx"),
    ("gen_city_block.py", ("--seed", "3", "--style", "oldcity"), "City/Block_OldCity_01.fbx"),
    ("gen_vehicle_auto.py", ("--kind", "auto", "--seed", "1"), "Vehicles/Auto_01.fbx"),
    ("gen_vehicle_auto.py", ("--kind", "hatchback", "--seed", "1"), "Vehicles/Hatchback_01.fbx"),
)


@dataclass
class StepResult:
    name: str
    ok: bool
    detail: str = ""


@dataclass
class CopyPlan:
    """(source, destination) pairs for the copy step."""

    items: list[tuple[Path, Path]] = field(default_factory=list)


# ---------------------------------------------------------------- planning


def sarvam_argv(out_dir: Path, dry_run: bool) -> list[str]:
    argv = ["--mode", "dialogue", "--script", str(DIALOGUE_SCRIPT), "--languages", LANGUAGES,
            "--out", str(out_dir)]
    return argv + ["--dry-run"] if dry_run else argv


def gemini_argv(preset: str, out_dir: Path, dry_run: bool) -> list[str]:
    argv = ["--preset", preset, "--out", str(out_dir)]
    return argv + ["--dry-run"] if dry_run else argv


def blender_commands(blender: str, unity_assets: Path) -> list[tuple[list[str], Path]]:
    """Blender command lines and the .fbx each one writes."""
    models = unity_assets / "Art" / "Models"
    out: list[tuple[list[str], Path]] = []
    for script, args, rel in BLENDER_JOBS:
        target = models / rel
        cmd = [blender, "-b", "-P", str(BLENDER_SCRIPTS / script), "--", *args, "--out", str(target)]
        out.append((cmd, target))
    return out


def plan_copies(out_dir: Path, unity_assets: Path) -> CopyPlan:
    """Mirrors the COPY cell of the Colab notebook. Only files that exist are planned."""
    plan = CopyPlan()
    voices = out_dir / DIALOGUE_ID
    if voices.is_dir():
        target = unity_assets / "Audio" / "Dialogue" / DIALOGUE_ID
        for wav in sorted(voices.glob("*.wav")):
            plan.items.append((wav, target / wav.name))
    translated = out_dir / f"{DIALOGUE_ID}.translated.json"
    if translated.is_file():
        plan.items.append((translated, unity_assets / "Localization" / "Dialogue" / f"{DIALOGUE_ID}.json"))
    art = out_dir / "gemini"
    if art.is_dir():
        for preset_dir in sorted(p for p in art.iterdir() if p.is_dir()):
            target = unity_assets / "Art" / "Generated" / preset_dir.name
            for image in sorted(p for p in preset_dir.iterdir() if p.is_file()):
                plan.items.append((image, target / image.name))
    return plan


def apply_copies(plan: CopyPlan) -> int:
    for source, dest in plan.items:
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
    return len(plan.items)


# ---------------------------------------------------------------- steps


def _call_main(name: str, main: Callable[[list[str]], int], argv: list[str]) -> StepResult:
    """Runs an imported script main() and turns its exit code or exception into a result."""
    print(f"\n=== {name} ===", flush=True)
    try:
        code = main(argv)
    except SystemExit as exc:  # argparse errors
        code = exc.code if isinstance(exc.code, int) else 1
    except Exception as exc:  # noqa: BLE001 - keep going with the other steps
        traceback.print_exc()
        return StepResult(name, False, f"{type(exc).__name__}: {exc}")
    if code == 0:
        return StepResult(name, True)
    hint = " (key not set?)" if code == 2 else ""
    return StepResult(name, False, f"exit code {code}{hint}")


def run_sarvam(out_dir: Path, dry_run: bool) -> StepResult:
    import sarvam_dialogue

    return _call_main("sarvam", sarvam_dialogue.main, sarvam_argv(out_dir, dry_run))


def run_gemini(preset: str, out_dir: Path, dry_run: bool) -> StepResult:
    import gemini_art

    return _call_main(f"gemini {preset}", gemini_art.main, gemini_argv(preset, out_dir, dry_run))


def resolve_blender(path: str) -> str | None:
    candidate = Path(path).expanduser()
    if candidate.is_file():
        return str(candidate)
    return shutil.which(path)


def run_blender(blender: str, unity_assets: Path, dry_run: bool,
                runner: Callable[..., subprocess.CompletedProcess] = subprocess.run) -> list[StepResult]:
    resolved = resolve_blender(blender)
    results: list[StepResult] = []
    for cmd, target in blender_commands(resolved or blender, unity_assets):
        name = f"blender {target.name}"
        print(f"\n=== {name} ===", flush=True)
        print("$ " + " ".join(cmd), flush=True)
        if dry_run:
            results.append(StepResult(name, True, "dry run, not started"))
            continue
        if resolved is None:
            results.append(StepResult(name, False, f"Blender executable not found: {blender}"))
            continue
        try:
            proc = runner(cmd, cwd=str(BLENDER_SCRIPTS), check=False, timeout=BLENDER_TIMEOUT_SECONDS)
        except (OSError, subprocess.SubprocessError) as exc:
            results.append(StepResult(name, False, f"{type(exc).__name__}: {exc}"))
            continue
        if proc.returncode != 0:
            results.append(StepResult(name, False, f"exit code {proc.returncode}"))
        elif not target.is_file():
            results.append(StepResult(name, False, f"Blender finished but {target} is missing"))
        else:
            results.append(StepResult(name, True, str(target.relative_to(unity_assets.parent))
                                      if target.is_relative_to(unity_assets.parent) else str(target)))
    return results


def run_copy(out_dir: Path, unity_assets: Path, dry_run: bool) -> StepResult:
    print("\n=== copy ===", flush=True)
    try:
        plan = plan_copies(out_dir, unity_assets)
        for source, dest in plan.items:
            print(f"{'would copy' if dry_run else 'copy'} {source} -> {dest}")
        if dry_run:
            return StepResult("copy", True, f"dry run, {len(plan.items)} file(s) planned")
        count = apply_copies(plan)
    except OSError as exc:
        return StepResult("copy", False, f"{type(exc).__name__}: {exc}")
    return StepResult("copy", True, f"{count} file(s) copied")


# ---------------------------------------------------------------- main


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="Generate all content (Sarvam voices, Gemini art, optional Blender models) "
                    "and copy it into the Unity project.",
        epilog="Keys: SARVAM_API_KEY and GEMINI_API_KEY from the environment (never printed). "
               "Exit code 1 if any step failed.",
    )
    p.add_argument("--skip-sarvam", action="store_true", help="do not run the Sarvam dialogue step")
    p.add_argument("--skip-gemini", action="store_true", help="do not run the Gemini art steps")
    p.add_argument("--blender", metavar="PATH",
                   help="Blender executable; if given, builds the city block and vehicle .fbx models")
    p.add_argument("--dry-run", action="store_true",
                   help="pass --dry-run to the API scripts (no network, no key), print Blender commands "
                        "without running them, list copies without writing")
    p.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR,
                   help="content output folder (default tools/content/out)")
    p.add_argument("--unity-assets", type=Path, default=DEFAULT_UNITY_ASSETS,
                   help="Unity Assets folder (default client/GangsOfBharat.Unity/Assets)")
    return p


def print_summary(results: list[StepResult], stream=None) -> None:
    stream = stream or sys.stdout
    print("\n=== summary ===", file=stream)
    for r in results:
        status = "ok    " if r.ok else "FAILED"
        print(f"{status} {r.name}" + (f": {r.detail}" if r.detail else ""), file=stream)
    failed = [r for r in results if not r.ok]
    print(f"{len(results) - len(failed)} ok, {len(failed)} failed", file=stream)


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    out_dir: Path = args.out
    unity_assets: Path = args.unity_assets
    print(f"content out: {out_dir}\nunity assets: {unity_assets}"
          + ("\nmode: dry run (no network, nothing written)" if args.dry_run else ""))
    for env in ("SARVAM_API_KEY", "GEMINI_API_KEY"):
        print(f"{env}: {'set' if read_key(env) else 'not set'}")

    results: list[StepResult] = []
    if not args.skip_sarvam:
        results.append(run_sarvam(out_dir, args.dry_run))
    if not args.skip_gemini:
        for preset in GEMINI_PRESETS:
            results.append(run_gemini(preset, out_dir, args.dry_run))
    if args.blender:
        results.extend(run_blender(args.blender, unity_assets, args.dry_run))
    results.append(run_copy(out_dir, unity_assets, args.dry_run))

    print_summary(results)
    return 0 if all(r.ok for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
