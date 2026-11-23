"""Procedural Indian city block generator for Blender 4.x (UNVERIFIED).

Builds one city block of Mayanagri: chawl buildings with balconies, shop fronts
with sign planes, roads with sidewalks, street poles and overhead wires. Each
building gets an LOD0 mesh (balconies, shop details) and an LOD1 mesh (plain box).
The result is exported as one .glb file.

Run headless:
    blender -b -P gen_city_block.py -- --seed 1 --style chawl --out out/block.glb

The layout is computed by plain Python (build_plan) so it can be tested without
Blender. bpy is imported only inside the functions that create geometry.
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

STYLES = ("chawl", "market", "oldcity")

# Block size in metres (X along the road, Y away from the road).
BLOCK_LENGTH = 60.0
BLOCK_DEPTH = 24.0
ROAD_WIDTH = 8.0
SIDEWALK_WIDTH = 2.5
SIDEWALK_HEIGHT = 0.15
FLOOR_HEIGHT = 3.2
POLE_SPACING = 15.0
POLE_HEIGHT = 7.5

# Per style: (min floors, max floors, min width, max width, shop chance, balcony chance)
STYLE_PARAMS = {
    "chawl": (3, 5, 10.0, 16.0, 0.35, 0.9),
    "market": (1, 3, 5.0, 8.0, 0.95, 0.4),
    "oldcity": (2, 4, 6.0, 10.0, 0.7, 0.7),
}

# Shop sign texts use invented parody names only (no real brands).
SIGN_NAMES = (
    "Mayanagri Kirana",
    "Bombay Chai Point",
    "Lucky Mobile Repair",
    "Shree Ganesh Sweets",
    "Raju Tailors",
    "Apna Medical",
    "Star Video Parlour",
    "Gupta Hardware",
)

# Facade colours (RGBA, linear) typical of painted Indian walls.
WALL_COLOURS = (
    (0.80, 0.70, 0.45, 1.0),
    (0.55, 0.70, 0.75, 1.0),
    (0.85, 0.55, 0.45, 1.0),
    (0.75, 0.75, 0.70, 1.0),
    (0.60, 0.75, 0.55, 1.0),
)


def script_args(argv: list[str]) -> list[str]:
    """Return the arguments after '--' (Blender passes its own args before it)."""
    if "--" in argv:
        return argv[argv.index("--") + 1 :]
    return []


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate a Mayanagri city block.")
    parser.add_argument("--seed", type=int, default=1, help="random seed")
    parser.add_argument("--style", choices=STYLES, default="chawl")
    parser.add_argument("--out", type=Path, default=Path("out/block.glb"))
    args = parser.parse_args(argv)
    if args.out.suffix.lower() != ".glb":
        parser.error("--out must end with .glb")
    return args


def build_plan(seed: int, style: str) -> dict:
    """Compute the block layout as plain data (no bpy)."""
    if style not in STYLE_PARAMS:
        raise ValueError(f"unknown style: {style}")
    min_f, max_f, min_w, max_w, shop_chance, balcony_chance = STYLE_PARAMS[style]
    rng = random.Random(f"{seed}:{style}")

    buildings = []
    x = 0.0
    while x < BLOCK_LENGTH - min_w:
        width = min(rng.uniform(min_w, max_w), BLOCK_LENGTH - x)
        floors = rng.randint(min_f, max_f)
        depth = rng.uniform(BLOCK_DEPTH * 0.6, BLOCK_DEPTH)
        has_shop = rng.random() < shop_chance
        buildings.append(
            {
                "name": f"Building_{len(buildings):02d}",
                "x": x,
                "width": width,
                "depth": depth,
                "floors": floors,
                "height": floors * FLOOR_HEIGHT,
                "colour": rng.choice(WALL_COLOURS),
                "shop": has_shop,
                "sign": rng.choice(SIGN_NAMES) if has_shop else None,
                # Balconies on every floor above the ground floor.
                "balconies": [
                    f for f in range(1, floors) if rng.random() < balcony_chance
                ],
            }
        )
        x += width

    pole_count = int(BLOCK_LENGTH // POLE_SPACING) + 1
    poles = [i * POLE_SPACING for i in range(pole_count)]
    wires = [(poles[i], poles[i + 1]) for i in range(len(poles) - 1)]
    return {
        "seed": seed,
        "style": style,
        "buildings": buildings,
        "poles": poles,
        "wires": wires,
        "road": {"length": BLOCK_LENGTH, "width": ROAD_WIDTH},
        "sidewalk": {"width": SIDEWALK_WIDTH, "height": SIDEWALK_HEIGHT},
    }


# ---------------------------------------------------------------------------
# Blender geometry (needs bpy)
# ---------------------------------------------------------------------------


def _material(bpy, name: str, colour) -> object:
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = colour
    return mat


def _box(bpy, name, size, location, material, collection):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material)
    for c in obj.users_collection:
        c.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def _collection(bpy, name):
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


def build_scene(bpy, plan: dict) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    lod0 = _collection(bpy, "LOD0")
    lod1 = _collection(bpy, "LOD1")
    street = _collection(bpy, "Street")

    asphalt = _material(bpy, "Asphalt", (0.08, 0.08, 0.08, 1.0))
    concrete = _material(bpy, "Sidewalk", (0.45, 0.43, 0.40, 1.0))
    metal = _material(bpy, "PoleMetal", (0.25, 0.25, 0.27, 1.0))
    sign_mat = _material(bpy, "Sign", (0.90, 0.20, 0.10, 1.0))
    shutter = _material(bpy, "Shutter", (0.35, 0.40, 0.45, 1.0))

    length = plan["road"]["length"]
    road_w = plan["road"]["width"]
    walk_w = plan["sidewalk"]["width"]
    walk_h = plan["sidewalk"]["height"]

    _box(bpy, "Road", (length, road_w, 0.05), (length / 2, -walk_w - road_w / 2,
         -0.025), asphalt, street)
    _box(bpy, "Sidewalk", (length, walk_w, walk_h), (length / 2, -walk_w / 2,
         walk_h / 2), concrete, street)

    for b in plan["buildings"]:
        wall = _material(bpy, f"Wall_{b['name']}", b["colour"])
        cx = b["x"] + b["width"] / 2
        cy = b["depth"] / 2
        cz = b["height"] / 2 + walk_h
        _box(bpy, f"{b['name']}_LOD1", (b["width"], b["depth"], b["height"]),
             (cx, cy, cz), wall, lod1)
        _box(bpy, f"{b['name']}_LOD0", (b["width"] - 0.1, b["depth"], b["height"]),
             (cx, cy, cz), wall, lod0)
        for floor in b["balconies"]:
            z = walk_h + floor * FLOOR_HEIGHT
            _box(bpy, f"{b['name']}_Balcony_{floor}", (b["width"] * 0.8, 1.2, 0.15),
                 (cx, -0.6, z), concrete, lod0)
            _box(bpy, f"{b['name']}_Railing_{floor}", (b["width"] * 0.8, 0.05, 1.0),
                 (cx, -1.2, z + 0.5), metal, lod0)
        if b["shop"]:
            _box(bpy, f"{b['name']}_Shutter", (b["width"] * 0.7, 0.05, 2.6),
                 (cx, -0.03, walk_h + 1.3), shutter, lod0)
            bpy.ops.mesh.primitive_plane_add(size=1.0, location=(cx, -0.08,
                                             walk_h + 2.9), rotation=(1.5708, 0, 0))
            sign = bpy.context.active_object
            sign.name = f"{b['name']}_Sign"
            sign.scale = (b["width"] * 0.8, 0.7, 1.0)
            sign.data.materials.append(sign_mat)
            sign["sign_text"] = b["sign"]

    pole_y = -walk_w + 0.3
    for i, px in enumerate(plan["poles"]):
        _box(bpy, f"Pole_{i:02d}", (0.2, 0.2, POLE_HEIGHT),
             (px, pole_y, POLE_HEIGHT / 2), metal, street)
    for i, (a, b) in enumerate(plan["wires"]):
        _box(bpy, f"Wire_{i:02d}", (b - a, 0.03, 0.03),
             ((a + b) / 2, pole_y, POLE_HEIGHT - 0.4), metal, street)


def export_glb(bpy, out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB")


def main(argv: list[str] | None = None) -> int:
    args = parse_args(script_args(sys.argv if argv is None else argv))
    plan = build_plan(args.seed, args.style)
    import bpy  # only available inside Blender

    build_scene(bpy, plan)
    export_glb(bpy, args.out)
    print(f"wrote {args.out} ({len(plan['buildings'])} buildings, style {args.style})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
