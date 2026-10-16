# G5 skull fragment ~6 x 5 cm, curved (skull radius 8.5 cm), Bone outside, Flesh inside.
# Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [("Skull", zg.skull_patch(0.085, zg.outline(71, (0.03, 0.025), 0.25), 0.006, seed=72))],
                  zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Skull_A")
    zg.report(obj, tris, zg.export_fbx(obj, 0.68))
    zg.save_blend()
