"""Offline tests for tools/blender. Run: python3 -m pytest tools/blender/tests -q

These test argument parsing and the plain-data layout only. bpy is never imported.
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

BLENDER = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(BLENDER))

import gen_city_block
import gen_vehicle_auto


def test_no_bpy_import():
    assert "bpy" not in sys.modules


def test_script_args_after_double_dash():
    argv = ["blender", "-b", "-P", "gen_city_block.py", "--", "--seed", "4"]
    assert gen_city_block.script_args(argv) == ["--seed", "4"]
    assert gen_city_block.script_args(["blender", "-b"]) == []


def test_city_block_args():
    args = gen_city_block.parse_args(
        ["--seed", "1", "--style", "chawl", "--out", "out/block.glb"]
    )
    assert args.seed == 1
    assert args.style == "chawl"
    assert args.out == Path("out/block.glb")


def test_city_block_rejects_bad_style_and_suffix():
    with pytest.raises(SystemExit):
        gen_city_block.parse_args(["--style", "skyscraper"])
    with pytest.raises(SystemExit):
        gen_city_block.parse_args(["--out", "block.obj"])


@pytest.mark.parametrize("style", gen_city_block.STYLES)
def test_city_block_plan(style):
    plan = gen_city_block.build_plan(7, style)
    buildings = plan["buildings"]
    assert buildings
    min_f, max_f = gen_city_block.STYLE_PARAMS[style][:2]
    end = 0.0
    for b in buildings:
        assert b["x"] == pytest.approx(end)
        end = b["x"] + b["width"]
        assert min_f <= b["floors"] <= max_f
        assert all(1 <= f < b["floors"] for f in b["balconies"])
        assert (b["sign"] is not None) == b["shop"]
    assert end <= gen_city_block.BLOCK_LENGTH + 1e-6
    assert len(plan["wires"]) == len(plan["poles"]) - 1


def test_city_block_plan_is_deterministic():
    assert gen_city_block.build_plan(3, "market") == gen_city_block.build_plan(
        3, "market"
    )
    assert gen_city_block.build_plan(3, "market") != gen_city_block.build_plan(
        4, "market"
    )


def test_vehicle_args_and_parts():
    args = gen_vehicle_auto.parse_args(["--kind", "hatchback", "--out", "a.glb"])
    assert args.kind == "hatchback"
    auto = gen_vehicle_auto.build_parts("auto", 1)
    car = gen_vehicle_auto.build_parts("hatchback", 1)
    assert len(auto["wheels"]) == 3
    assert len(car["wheels"]) == 4
    assert len({w["name"] for w in car["wheels"]}) == 4
    assert auto["model_name"] in gen_vehicle_auto.PARODY_NAMES["auto"]
    with pytest.raises(ValueError, match="unknown kind"):
        gen_vehicle_auto.build_parts("truck", 1)


def test_fbx_and_glb_outputs_accepted():
    assert gen_city_block.parse_args(["--out", "a.fbx"]).out == Path("a.fbx")
    assert gen_vehicle_auto.parse_args(["--out", "a.FBX"]).out == Path("a.FBX")
    assert gen_vehicle_auto.parse_args(["--out", "a.glb"]).out == Path("a.glb")
    with pytest.raises(SystemExit):
        gen_vehicle_auto.parse_args(["--out", "a.obj"])
