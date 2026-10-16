# Rebuilds and exports every gore object in order, then saves ZombieGore.blend.
import os

SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
ORDER = ["s1_stump_neck", "s2_stump_limb", "g1_gib_flesh_a", "g2_gib_flesh_b", "g3_gib_flesh_c",
         "g4_gib_bone_a", "g5_gib_skull_a", "g6_gib_skull_b"]

for s in ORDER:
    path = os.path.join(SCRIPTS, s + ".py")
    exec(compile(open(path, encoding="utf-8").read(), path, "exec"), {"__name__": "__main__"})
