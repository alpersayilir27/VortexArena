# B2 service counter: B1 counter whose +Y (back splash) side faces the customers, dressed red.
# The serving slots and boards above it stay where they are; only the table's look changes.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
import b1_counter
importlib.reload(bk_common)
importlib.reload(b1_counter)


def build(name):
    return b1_counter.build(name, 2.79, service=True)


if __name__ == "__main__":
    obj, tris = build("B2_ServiceCounter")
    print("B2_ServiceCounter tris", tris, "->", bk_common.export_fbx(obj, r"Models\B2_ServiceCounter.fbx"))
