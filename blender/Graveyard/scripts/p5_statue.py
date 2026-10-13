"""Part 5: CC0 gothic statue (Poly Haven `gothic_statue`, glTF 1k) on a stone plinth at the second landmark
corner, facing the yard; decimated for the Quest budget. Its material is renamed GY_A_Statue (baked by the
export like the rest). The download cache (blender/Graveyard/asset_cache/) is not in git."""
import importlib
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)
import gy_assets as ga
importlib.reload(ga)

ASSET, RES, TARGET_TRIS = "gothic_statue", "1k", 9000

L = gy.Layout()
coll = gy.collection("GY_P5_Statue")
gy.clear_collection(coll)

for m in [m for m in bpy.data.materials if m.name.startswith("GY_A_Statue")]:
    bpy.data.materials.remove(m)                 # a re-run must not end up with GY_A_Statue.001
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=ga.fetch(ASSET, RES))
new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
for o in bpy.data.objects:
    o.select_set(o in new)
bpy.context.view_layer.objects.active = new[0]
if len(new) > 1:
    bpy.ops.object.join()
statue = bpy.context.view_layer.objects.active
for o in [o for o in bpy.data.objects if o not in before and o is not statue]:
    bpy.data.objects.remove(o, do_unlink=True)
for c in list(statue.users_collection):
    c.objects.unlink(statue)
coll.objects.link(statue)
statue.name = statue.data.name = "GY_Statue"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

tri = gy.tris(statue)
if tri > TARGET_TRIS:
    mod = statue.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = TARGET_TRIS / tri
    bpy.ops.object.modifier_apply(modifier=mod.name)
for i, m in enumerate(statue.data.materials):
    m.name = "GY_A_Statue" if i == 0 else f"GY_A_Statue{i}"
    m["gy_src"] = "polyhaven:" + ASSET

# stand it on a plinth at the second landmark spot, facing the yard
spot = Vector(gy.landmark_spots(L)[1])
h = L.ground_h(spot.x, spot.y)
bb = [statue.matrix_world @ Vector(c) for c in statue.bound_box]
zmin = min(v.z for v in bb)
cx, cy = sum(v.x for v in bb) / 8, sum(v.y for v in bb) / 8
for v in statue.data.vertices:
    v.co.x -= cx
    v.co.y -= cy
    v.co.z -= zmin
face = (gy.centroid(L.fence) - spot).normalized()
statue.location = gy.bl(spot.x, spot.y, h + 0.9)
statue.rotation_euler = (0, 0, math.atan2(-face.y, -face.x) + math.pi / 2)   # model front is -Y (glTF +Z)

plinth = gy.Part("GY_StatuePlinth", coll)
plinth.prism([(spot.x + dx, spot.y + dz) for dx, dz in ((-0.7, -0.7), (0.7, -0.7), (0.7, 0.7), (-0.7, 0.7))],
             h - 0.2, h + 0.25, "StoneWall")
plinth.prism([(spot.x + dx, spot.y + dz) for dx, dz in ((-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5))],
             h + 0.25, h + 0.9, "Sandstone")
plinth.finish()
print("statue:", gy.tris(statue), "tris, height", round(max(v.co.z for v in statue.data.vertices), 2), "m")
