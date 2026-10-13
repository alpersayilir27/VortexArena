"""Builds the whole Graveyard arena for the scene's target (scene["gy_target"]: "Map" or a venue name) and,
when scene["gy_export"] is set, exports it. Run inside Blender:
    import bpy; bpy.context.scene["gy_target"] = "VortexAntep"; bpy.context.scene["gy_export"] = 1
    exec(open(r"<repo>/blender/Graveyard/scripts/build_all.py", encoding="utf-8").read(), {"__name__": "__main__"})"""
import os
import sys

import bpy

S = os.path.join(os.path.dirname(bpy.data.filepath), "scripts")
sys.path.insert(0, S)
import gy_common
gy_common.reset_materials()                    # MATS edits take effect
steps = ["p1_ground.py", "p2_fence.py", "p3_graves.py", "p4_surround.py", "p5_statue.py", "p6_nature.py"]
if bpy.context.scene.get("gy_export"):
    steps.append("p9_export.py")
for f in steps:
    print("==", f)
    exec(open(os.path.join(S, f), encoding="utf-8").read(), {"__name__": "__main__"})
