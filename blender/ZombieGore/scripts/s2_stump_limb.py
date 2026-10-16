# S2 generic limb cut cap, radius 5 cm (code scales it to the joint collider radius).
# Pivot = centre of the cut plane, outward = Blender +Z (Unity +Y).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [
        ("Flesh", zg.stump_flesh(0.05, 0.05, 16, seed=21, dome=0.006, skirt=0.028)),
        ("Bone", zg.bone_nub(0, 0, 0.009, -0.014, 0.022, seed=22)),
    ], zg.MAX_TRIS["stump_limb"], pivot="cut")


if __name__ == "__main__":
    obj, tris = build("ZG_Stump_Limb")
    zg.report(obj, tris, zg.export_fbx(obj, 0.15))
    zg.save_blend()
