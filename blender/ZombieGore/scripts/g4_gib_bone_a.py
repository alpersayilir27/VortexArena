# G4 bone splinter ~5 cm with a flesh scrap on one end. Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [
        ("Bone", zg.shard(0.045, 0.008, seed=61)),
        ("Scrap", zg.octa((-0.018, 0.001, 0.0), (0.012, 0.011, 0.009), seed=62)),
    ], zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Bone_A")
    zg.report(obj, tris, zg.export_fbx(obj, 0.58))
    zg.save_blend()
