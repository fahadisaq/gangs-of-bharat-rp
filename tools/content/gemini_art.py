#!/usr/bin/env python3
"""Gemini 2D art generator for GANGS OF BHARAT (Python 3.11, standard library only).

Prompt presets in an Indian city style (fictional city Mayanagri): shop signs, road textures,
UI icons and loading screens. Images are written to tools/content/out/gemini/<preset>/.
Every generated image must be reviewed by a person before it goes into the game.

The key is read from the GEMINI_API_KEY environment variable and sent in the x-goog-api-key
header (not in the URL, so it never shows up in logs). It is never printed.
--dry-run prints the planned requests without any network access and works without a key.

IMPORTANT: the endpoint and the image model name are the best known values when this was
written offline (no network in the sandbox). Verify them against the current Gemini API docs
(https://ai.google.dev/gemini-api/docs/image-generation) and pass --model if they changed.

Examples:
  python3 tools/content/gemini_art.py --list-presets
  python3 tools/content/gemini_art.py --preset shop_sign --dry-run
  GEMINI_API_KEY=... python3 tools/content/gemini_art.py --preset ui_icon --subject "cutting chai glass"
"""

from __future__ import annotations

import argparse
import base64
import sys
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
    slugify,
)

KEY_ENV = "GEMINI_API_KEY"
API_BASE = "https://generativelanguage.googleapis.com/v1beta"
DEFAULT_MODEL = "gemini-2.5-flash-image"  # verify against current docs
AUTH_HEADER = "x-goog-api-key"

STYLE_GUIDE = (
    "Art direction for GANGS OF BHARAT: a stylized, slightly painterly modern Indian megacity "
    "called Mayanagri (fictional, inspired by Mumbai). Warm, saturated colours, monsoon "
    "grime, hand-painted signage, dense street life. Not photoreal. No real-world brands, logos "
    "or trademarks: invent parody names only. No real people. No text other than the text asked for."
)


@dataclass(frozen=True)
class Preset:
    name: str
    description: str
    template: str
    subjects: tuple[str, ...]
    aspect: str


PRESETS: dict[str, Preset] = {
    "shop_sign": Preset(
        "shop_sign",
        "Shop front signboards with Hindi (Devanagari) and English text",
        "A flat, front-on shop signboard for {subject}. Hand-painted look on a metal board, "
        "bilingual text in Hindi (Devanagari) and English, slight rust and rain stains. "
        "Fill the whole image with the board, no perspective, suitable as a game texture.",
        (
            "a chai and vada pav stall named 'Pandey Chai Centre'",
            "a mobile recharge shop named 'Mayanagri Mobile Point'",
            "a sweet shop named 'Shubh Mithai Bhandar'",
            "a car garage named 'Bharat Auto Works'",
            "a medical store named 'Jeevan Medical'",
        ),
        "4:1",
    ),
    "road_texture": Preset(
        "road_texture",
        "Seamless tileable road and sidewalk textures",
        "A seamless, tileable top-down texture of {subject}. Even lighting, no shadows from "
        "objects, no vehicles or people, game-ready albedo texture.",
        (
            "worn Indian city asphalt with faded white lane markings and small potholes",
            "a cracked concrete footpath with paan stains and interlocking paver tiles",
            "a wet monsoon asphalt road with puddles",
            "a dusty slum lane of packed earth and broken bricks",
        ),
        "1:1",
    ),
    "ui_icon": Preset(
        "ui_icon",
        "Flat mobile game UI icons",
        "A single flat mobile game UI icon of {subject}, bold outline, centred on a plain "
        "transparent-looking light background, readable at 64 px.",
        (
            "a cutting chai glass",
            "a vada pav",
            "a yellow and green auto-rickshaw",
            "a rupee coin",
            "a police badge (fictional Mayanagri Police)",
            "a walkie-talkie for faction radio",
            "a smartphone with a chat bubble",
        ),
        "1:1",
    ),
    "loading_screen": Preset(
        "loading_screen",
        "Wide loading screen illustrations",
        "A cinematic wide illustration for a mobile game loading screen: {subject}. "
        "Leave the lower third calm for the loading bar and tips.",
        (
            "the Mayanagri skyline at dusk seen from a crowded local train",
            "a monsoon night street with autos, food stalls and neon signs",
            "a police jeep chasing a black sedan across a sea-link bridge",
            "a chawl courtyard during a festival with lights and colours",
        ),
        "16:9",
    ),
}


def build_prompt(preset: Preset, subject: str) -> str:
    return f"{STYLE_GUIDE}\n\n{preset.template.format(subject=subject)}\nAspect ratio {preset.aspect}."


def build_image_request(prompt: str, model: str = DEFAULT_MODEL, purpose: str = "image") -> PlannedRequest:
    url = f"{API_BASE}/models/{model}:generateContent"
    body = {
        "contents": [{"role": "user", "parts": [{"text": prompt}]}],
        "generationConfig": {"responseModalities": ["TEXT", "IMAGE"]},
    }
    headers = {"Content-Type": "application/json", AUTH_HEADER: ""}
    return PlannedRequest("POST", url, headers, json_body=body, secret_headers=(AUTH_HEADER,), purpose=purpose)


def extract_images(response: dict[str, Any]) -> list[tuple[str, bytes]]:
    """Returns (mime_type, bytes) for every inline image part in a generateContent response."""
    images: list[tuple[str, bytes]] = []
    for candidate in response.get("candidates", []) or []:
        content = candidate.get("content") or {}
        for part in content.get("parts", []) or []:
            inline = part.get("inlineData") or part.get("inline_data")
            if inline and inline.get("data"):
                mime = inline.get("mimeType") or inline.get("mime_type") or "image/png"
                images.append((mime, base64.b64decode(inline["data"])))
    return images


def plan(preset_name: str, subjects: list[str] | None, count: int, model: str) -> list[tuple[str, PlannedRequest]]:
    preset = PRESETS[preset_name]
    chosen = subjects or list(preset.subjects)
    out: list[tuple[str, PlannedRequest]] = []
    for subject in chosen:
        for n in range(1, count + 1):
            name = f"{slugify(subject)}_{n}"
            out.append((name, build_image_request(build_prompt(preset, subject), model,
                                                  purpose=f"{preset_name}/{name}")))
    return out


def run(planned: list[tuple[str, PlannedRequest]], preset_name: str, out_dir: Path, key: str) -> list[Path]:
    target = out_dir / "gemini" / preset_name
    target.mkdir(parents=True, exist_ok=True)
    written: list[Path] = []
    for name, req in planned:
        images = extract_images(send(req, key))
        if not images:
            raise ApiError(f"{req.purpose}: response contained no image")
        for i, (mime, data) in enumerate(images):
            ext = ".jpg" if "jpeg" in mime else ".png"
            path = target / (f"{name}{ext}" if i == 0 else f"{name}_{i}{ext}")
            path.write_bytes(data)
            written.append(path)
            print(f"wrote {path} (review before use)")
    return written


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Gemini 2D art presets for GANGS OF BHARAT.")
    p.add_argument("--preset", choices=sorted(PRESETS), help="prompt preset")
    p.add_argument("--subject", action="append", help="custom subject (repeatable); default: preset list")
    p.add_argument("--count", type=int, default=1, help="images per subject (1-4)")
    p.add_argument("--model", default=DEFAULT_MODEL, help=f"image model (default {DEFAULT_MODEL})")
    p.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR, help="output folder (default tools/content/out)")
    p.add_argument("--list-presets", action="store_true", help="print presets and exit")
    p.add_argument("--dry-run", action="store_true", help="print planned requests, no network, no key needed")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.list_presets:
        for preset in PRESETS.values():
            print(f"{preset.name}: {preset.description} ({len(preset.subjects)} default subjects, {preset.aspect})")
        return 0
    if not args.preset:
        print("error: --preset is required (see --list-presets)", file=sys.stderr)
        return 2
    if not 1 <= args.count <= 4:
        print("error: --count must be between 1 and 4", file=sys.stderr)
        return 2
    planned = plan(args.preset, args.subject, args.count, args.model)
    if args.dry_run:
        print_dry_run([req for _, req in planned], KEY_ENV)
        return 0
    try:
        run(planned, args.preset, args.out, require_key(KEY_ENV))
    except MissingKeyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    except ApiError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
