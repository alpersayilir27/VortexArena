# S1 neck cut cap. Pivot = centre of the cut plane, outward = Blender +Z (Unity +Y),
# front = Blender -Y (Unity +Z): spine nub sits behind centre, trachea ring in front.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [
        ("Flesh", zg.stump_flesh(0.055, 0.045, 20, seed=11, dome=0.008)),
        ("Spine", zg.bone_nub(0, 0.012, 0.0125, -0.015, 0.021, seed=12)),
        ("Trachea", zg.tube(0, -0.028, 0.009, 0.006, -0.012, 0.008, zg.BONE, sy=0.8)),
    ], zg.MAX_TRIS["stump_neck"], pivot="cut")


if __name__ == "__main__":
    obj, tris = build("ZG_Stump_Neck")
    zg.report(obj, tris, zg.export_fbx(obj, 0.0))
    zg.save_blend()
