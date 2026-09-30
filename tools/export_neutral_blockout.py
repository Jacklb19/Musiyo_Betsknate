"""Export only neutral architecture from the private concept GLB.

Usage: blender -b --python tools/export_neutral_blockout.py -- input.glb output.fbx
Textures, source materials, cultural objects, and source mesh names are excluded.
"""

import sys
from pathlib import Path

import bpy


EXCLUDE = (
    "provisional", "mascara", "instrumento_provisional", "cartela", "tsombiach",
    "tapiz", "faja", "cenefa", "rombo", "bandera", "cintas", "portada",
    "mito", "betiyeguagua", "laguna", "isla", "danza", "shinyac", "guanga",
    "tishasha", "bichasha", "fogón_fuego", "fogon_fuego", "maiz", "flores",
    "plantas_medicinales", "ollas", "canastos", "manta", "estera", "urdimbre",
    "arbol", "faroles", "puntos_interes", "pi_", "spawn_", "terreno_valle",
)

ARCHITECTURE = (
    "muro", "piso", "techo", "columna", "viga", "dintel", "jamba",
    "umbral", "zocalo", "estructura", "cabios", "canecillos", "hastial",
    "sendero", "baranda", "banca", "postes", "basas", "anillo_oculo",
    "fleco_paja",
)


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


def category(name):
    if "piso" in name or "sendero" in name or "umbral" in name:
        return "Floor"
    if "techo" in name or "fleco" in name:
        return "Roof"
    if "muro" in name or "dintel" in name or "jamba" in name:
        return "Wall"
    return "Structure"


def main():
    source, destination = parse_paths()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    kept = [
        obj for obj in meshes
        if not any(word in obj.name.lower() for word in EXCLUDE)
        and any(word in obj.name.lower() for word in ARCHITECTURE)
    ]
    if len(kept) < 50:
        raise SystemExit("Too few meshes passed the filter; review it before export")

    colors = {
        "Floor": (0.43, 0.42, 0.38, 1),
        "Roof": (0.39, 0.40, 0.39, 1),
        "Wall": (0.64, 0.63, 0.59, 1),
        "Structure": (0.48, 0.48, 0.46, 1),
    }
    materials = {}
    for name, color in colors.items():
        material = bpy.data.materials.new("Blockout_" + name)
        material.diffuse_color = color
        materials[name] = material

    for index, obj in enumerate(kept, 1):
        source_name = obj.name.lower()
        world_matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world_matrix
        mesh_category = category(source_name)
        obj.name = f"Blockout_{mesh_category}_{index:03d}"
        obj.data.name = f"Mesh_{index:03d}"
        obj.data.materials.clear()
        obj.data.materials.append(materials[mesh_category])

    bpy.ops.object.select_all(action="DESELECT")
    for obj in kept:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = kept[0]

    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(destination), use_selection=True, object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
        use_mesh_modifiers=True, path_mode="STRIP", embed_textures=False,
    )
    print(f"Neutral meshes exported: {len(kept)} of {len(meshes)}")
    print(f"FBX: {destination} ({destination.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
