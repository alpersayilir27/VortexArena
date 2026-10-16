# G6 skull fragment ~7 x 4.5 cm, longer and more jagged than G5. Pivot = centre of mass.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\ZombieGore\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import zg_common as zg
importlib.reload(zg)


def build(name):
    return zg.bake(name, [("Skull", zg.skull_patch(0.085, zg.outline(81, (0.038, 0.02), 0.35), 0.007, seed=82))],
                  zg.MAX_TRIS["gib"], pivot="mass")


if __name__ == "__main__":
    obj, tris = build("ZG_Gib_Skull_B")
    zg.report(obj, tris, zg.export_fbx(obj, 0.78))
    zg.save_blend()
