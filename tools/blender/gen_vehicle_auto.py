"""Low-poly vehicle generator for Blender 4.x (UNVERIFIED).

Builds a low-poly auto-rickshaw or hatchback with separate wheel objects (so the
Unity client can spin them) and exports a .glb. Vehicles use invented parody
names only; no real brands, logos or model names.

Run headless:
    blender -b -P gen_vehicle_auto.py -- --kind auto --seed 3 --out out/auto.glb

The part list is computed by plain Python (build_parts) so it can be tested
without Blender. bpy is imported only inside the functions that create geometry.
"""

from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

OUT_SUFFIXES = (".fbx", ".glb")

KINDS = ("auto", "hatchback")

# Parody names shown on the body (custom property "model_name").
PARODY_NAMES = {
    "auto": ("Bajrang Tuk-Tuk", "Chhota Rickshaw RE", "Mayanagri Auto 3W"),
    "hatchback": ("Mayur Zippy", "Gully Hatch 800", "Kaveri Mini"),
}

BODY_COLOURS = {
    # Mumbai autos are green and yellow.
    "auto": ((0.10, 0.45, 0.15, 1.0), (0.95, 0.80, 0.10, 1.0)),
    "hatchback": (
        (0.80, 0.80, 0.82, 1.0),
        (0.60, 0.05, 0.05, 1.0),
        (0.05, 0.15, 0.45, 1.0),
        (0.10, 0.10, 0.10, 1.0),
    ),
}


def script_args(argv: list[str]) -> list[str]:
    """Return the arguments after '--' (Blender passes its own args before it)."""
    if "--" in argv:
        return argv[argv.index("--") + 1 :]
    return []


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate a low-poly vehicle.")
    parser.add_argument("--kind", choices=KINDS, default="auto")
    parser.add_argument("--seed", type=int, default=1, help="random seed")
    parser.add_argument("--out", type=Path, default=Path("out/vehicle.glb"))
    args = parser.parse_args(argv)
    if args.out.suffix.lower() not in OUT_SUFFIXES:
        parser.error("--out must end with .fbx or .glb")
    return args


def build_parts(kind: str, seed: int) -> dict:
    """Compute the vehicle parts as plain data (no bpy). Sizes in metres."""
    if kind not in KINDS:
        raise ValueError(f"unknown kind: {kind}")
    rng = random.Random(f"{seed}:{kind}")
    colour = rng.choice(BODY_COLOURS[kind])
    name = rng.choice(PARODY_NAMES[kind])

    if kind == "auto":
        # Three wheels: one front, two rear.
        radius = 0.22
        body = {"size": (2.6, 1.3, 1.1), "location": (0.0, 0.0, 0.85)}
        roof = {"size": (1.9, 1.35, 0.08), "location": (-0.2, 0.0, 1.75)}
        wheels = [
            {"name": "Wheel_Front", "location": (1.05, 0.0, radius)},
            {"name": "Wheel_RearLeft", "location": (-0.85, 0.6, radius)},
            {"name": "Wheel_RearRight", "location": (-0.85, -0.6, radius)},
        ]
    else:
        radius = 0.3
        body = {"size": (3.7, 1.65, 0.75), "location": (0.0, 0.0, 0.65)}
        roof = {"size": (2.0, 1.5, 0.6), "location": (-0.3, 0.0, 1.3)}
        wheels = [
            {"name": "Wheel_FrontLeft", "location": (1.2, 0.8, radius)},
            {"name": "Wheel_FrontRight", "location": (1.2, -0.8, radius)},
            {"name": "Wheel_RearLeft", "location": (-1.2, 0.8, radius)},
            {"name": "Wheel_RearRight", "location": (-1.2, -0.8, radius)},
        ]
    for w in wheels:
        w["radius"] = radius
        w["width"] = 0.18
    return {
        "kind": kind,
        "seed": seed,
        "model_name": name,
        "colour": colour,
        "body": body,
        "roof": roof,
        "wheels": wheels,
    }


# ---------------------------------------------------------------------------
# Blender geometry (needs bpy)
# ---------------------------------------------------------------------------


def _material(bpy, name: str, colour) -> object:
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = colour
    # Exporters read the Principled BSDF base colour, not diffuse_color.
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = colour
    return mat


def build_scene(bpy, parts: dict) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    paint = _material(bpy, "BodyPaint", parts["colour"])
    rubber = _material(bpy, "Tyre", (0.03, 0.03, 0.03, 1.0))
    canvas = _material(bpy, "Roof", (0.05, 0.05, 0.05, 1.0))

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=parts["body"]["location"])
    body = bpy.context.active_object
    body.name = "Body"
    body.scale = parts["body"]["size"]
    body.data.materials.append(paint)
    body["model_name"] = parts["model_name"]

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=parts["roof"]["location"])
    roof = bpy.context.active_object
    roof.name = "Roof"
    roof.scale = parts["roof"]["size"]
    roof.data.materials.append(canvas)
    roof.parent = body
    # Keep the roof where it is in world space instead of inheriting the body's scale.
    roof.matrix_parent_inverse = body.matrix_world.inverted()

    for w in parts["wheels"]:
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=12,
            radius=w["radius"],
            depth=w["width"],
            location=w["location"],
            rotation=(1.5708, 0.0, 0.0),
        )
        wheel = bpy.context.active_object
        wheel.name = w["name"]
        wheel.data.materials.append(rubber)
        # Wheels stay separate objects (not joined) so they can rotate in Unity.


def export_model(bpy, out: Path) -> None:
    """Exports .fbx (Unity imports it natively) or .glb (needs a glTF importer in Unity)."""
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.suffix.lower() == ".fbx":
        bpy.ops.export_scene.fbx(
            filepath=str(out),
            apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z",
            axis_up="Y",
            use_custom_props=True,
        )
    else:
        bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB", export_extras=True)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(script_args(sys.argv if argv is None else argv))
    parts = build_parts(args.kind, args.seed)
    import bpy  # only available inside Blender

    build_scene(bpy, parts)
    export_model(bpy, args.out)
    print(f"wrote {args.out} ({parts['kind']}, {parts['model_name']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
