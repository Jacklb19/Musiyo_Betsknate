"""Export the reading stands of the museum concept GLB, at their authored places.

Usage: blender -b --python tools/export_lecterns.py -- input.glb output.fbx
Only the stand geometry and its placement are kept. Textures, source materials and
source names are replaced, and the export uses the same axes as the neutral blockout
so both models share one frame in the scene.
"""

import sys
from pathlib import Path

import bpy


SOURCE_PREFIX = "cartela"
COLORS = {
    "Lectern_Wood": (0.30, 0.20, 0.13, 1),
    # The reading surface keeps the base colour of the concept material.
    "Lectern_Board": (0.86, 0.82, 0.72, 1),
}


def parse_paths():
    if "--" not in sys.argv:
        raise SystemExit("Provide the input GLB and output FBX after --")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise SystemExit("Expected exactly two paths: input.glb output.fbx")
    source, destination = map(Path, values)
    if not source.is_file() or source.suffix.lower() != ".glb":
        raise SystemExit("Input GLB does not exist")
    if destination.suffix.lower() != ".fbx":
        raise SystemExit("Output must be an FBX")
    return source, destination


def main():
    source, destination = parse_paths()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    stands = sorted(
        (obj for obj in bpy.data.objects
         if obj.type == "MESH" and obj.name.lower().startswith(SOURCE_PREFIX)),
        key=lambda obj: obj.name,
    )
    if not stands:
        raise SystemExit("The concept has no reading stands")

    materials = {}
    for name, color in COLORS.items():
        material = bpy.data.materials.new(name)
        material.diffuse_color = color
        materials[name] = material

    for index, obj in enumerate(stands, 1):
        world_matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world_matrix
        # The reading surface is the slot whose source material is not the wooden frame.
        slots = ["Lectern_Wood" if "madera" in (slot.name or "").lower() else "Lectern_Board"
                 for slot in obj.data.materials]
        if sorted(set(slots)) != sorted(COLORS):
            raise SystemExit(f"Unexpected materials on {obj.name}: {slots}")
        for slot, name in enumerate(slots):
            obj.data.materials[slot] = materials[name]
        obj.name = f"Lectern_{index:02d}"
        obj.data.name = f"LecternMesh_{index:02d}"

    bpy.ops.object.select_all(action="DESELECT")
    for obj in stands:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = stands[0]

    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(destination), use_selection=True, object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
        use_mesh_modifiers=True, path_mode="STRIP", embed_textures=False,
    )
    print(f"Reading stands exported: {len(stands)}")
    print(f"FBX: {destination} ({destination.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
