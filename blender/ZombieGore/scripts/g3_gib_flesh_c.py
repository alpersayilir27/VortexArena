# G3 small lump, ~3.5 cm. Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [("Flesh", zg.blob((0.038, 0.033, 0.03), seed=51, noise=0.22, flat=(0, 0.5)))],
                  zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Flesh_C")
    zg.report(obj, tris, zg.export_fbx(obj, 0.48))
    zg.save_blend()
