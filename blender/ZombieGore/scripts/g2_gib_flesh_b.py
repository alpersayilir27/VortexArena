# G2 torn flesh flap, ~6 x 4 x 1.8 cm. Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [("Flesh", zg.blob((0.06, 0.04, 0.018), seed=41, noise=0.28))],
                  zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Flesh_B")
    zg.report(obj, tris, zg.export_fbx(obj, 0.38))
    zg.save_blend()
