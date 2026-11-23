# Blender procedural generators (UNVERIFIED)

Python scripts for Blender 4.x that generate low-poly 3D content for Mayanagri. They have **not been run in Blender** because Blender cannot be installed in the build sandbox. Only the argument parsing and the plain-data layout are tested (without `bpy`).

| Script | Output |
| --- | --- |
| `gen_city_block.py` | One city block: chawl buildings with balconies, shop fronts with sign planes (parody shop names), road with sidewalk, street poles and overhead wires. Buildings have `_LOD0` (detailed) and `_LOD1` (plain box) meshes in separate collections. |
| `gen_vehicle_auto.py` | Low-poly auto-rickshaw (3 wheels) or hatchback (4 wheels). Wheels are separate objects so Unity can rotate them. Parody model names only. |

## Run

```bash
blender -b -P gen_city_block.py -- --seed 1 --style chawl --out out/block.glb
blender -b -P gen_city_block.py -- --seed 2 --style market --out out/market.glb
blender -b -P gen_city_block.py -- --seed 3 --style oldcity --out out/oldcity.glb
blender -b -P gen_vehicle_auto.py -- --kind auto --seed 1 --out out/auto.glb
blender -b -P gen_vehicle_auto.py -- --kind hatchback --seed 1 --out out/hatchback.glb
```

Arguments after `--` go to the script. The same `--seed` and `--style` always give the same layout.

## Test (no Blender needed)

```bash
python3 -m pytest tools/blender/tests -q
```

## To verify in a real environment

1. Install Blender 4.x and run the commands above.
2. Open the `.glb` files in Blender or Unity and check scale (1 unit = 1 m), normals and the LOD0/LOD1 collections.
3. Import into `client/GangsOfBharat.Unity/Assets/Art/` and set up a LOD Group with the `_LOD0` and `_LOD1` meshes.
