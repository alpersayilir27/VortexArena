# D2 service bell (decor, one per serving slot). Pivot = bottom center; ~16 cm wide, cartoon-sized
# so it reads from the kitchen side. No collider: items and hands pass through.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def build(name):
    b = bk_common.Builder(name)
    c = (0, 0)
    b.lathe("Base", [(0, 0), (0.078, 0), (0.082, 0.006), (0.082, 0.024), (0.076, 0.031), (0.062, 0.034), (0, 0.034)], c, "charcoal", 28)
    b.lathe("Dome", [(0, 0.033), (0.066, 0.033), (0.067, 0.042), (0.064, 0.058), (0.057, 0.074), (0.045, 0.088),
                     (0.029, 0.098), (0.012, 0.103), (0, 0.104)], c, "red", 28)
    b.lathe("Button", [(0, 0.1), (0.008, 0.1), (0.008, 0.116), (0.019, 0.117), (0.022, 0.122), (0.018, 0.128), (0, 0.129)], c, "steel", 16)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("D2_Bell")
    print("D2_Bell tris", tris, "->", bk_common.export_fbx(obj, r"Models\D2_Bell.fbx"))
