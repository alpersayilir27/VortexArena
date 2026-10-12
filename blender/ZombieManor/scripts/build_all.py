"""Rebuilds the whole manor from scratch: art materials, Part 0 graybox .. Part 7 decor, then the z-fight check.
Run inside ZombieManor.blend (Blender MCP execute_blender_code or the Text Editor)."""
import os

import bpy

SCRIPTS = os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts")
PARTS = ["p0_graybox", "p1_courtyard", "p2_interior", "p3_balcony", "p4_upstairs", "p5_terrace", "p6_exterior", "p7_decor"]
ART_COLLECTIONS = ["ZM_P1_Courtyard", "ZM_P2_Interior", "ZM_P3_Balcony", "ZM_P4_Upstairs", "ZM_P5_Terrace",
                   "ZM_P6_Exterior", "ZM_P7_Layout"]

# art materials are rebuilt from MATS (material() never updates an existing one); graybox colours stay
for m in list(bpy.data.materials):
    if m.name.startswith("ZM_") and not m.name.startswith("ZM_Gray_"):
        bpy.data.materials.remove(m)
# rebuilds leave the old meshes/images behind: purge them so new data keeps its plain name (no .001)
bpy.data.orphans_purge(do_recursive=True)

for name in PARTS:
    print(f"--- {name}")
    with open(os.path.join(SCRIPTS, name + ".py"), encoding="utf-8") as f:
        exec(f.read(), {"__name__": "__main__"})

for c in ("P0_Venue", "P0_Markers"):            # layout markers: on demand only
    bpy.data.collections[c].hide_viewport = True

# the replaced meshes are orphans only now: purge, then give every art mesh its object's name (FBX sub-asset)
bpy.data.orphans_purge(do_recursive=True)
for c in ART_COLLECTIONS[:-1] + ["ZM_P7_Kit"]:
    for o in bpy.data.collections[c].objects:
        o.data.name = o.name

import zm_common as zm
objs = [o for c in ART_COLLECTIONS for o in bpy.data.collections[c].objects]
zm.zfight_report(objs)
print("total tris:", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs))
