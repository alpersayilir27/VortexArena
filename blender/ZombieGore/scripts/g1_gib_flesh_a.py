# G1 meat chunk, ~5 x 3.5 x 3 cm, one sliced torn face. Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [("Flesh", zg.blob((0.05, 0.035, 0.03), seed=31, noise=0.2, flat=(2, 0.45)))],
                  zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Flesh_A")
    zg.report(obj, tris, zg.export_fbx(obj, 0.28))
    zg.save_blend()
