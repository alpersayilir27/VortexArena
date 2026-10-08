# B4 middle table: B1 counter cross-section at 1.80 m (old Prop_KitchenTable_06 box).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
import b1_counter
importlib.reload(bk_common)
importlib.reload(b1_counter)


def build(name):
    return b1_counter.build(name, 1.80)


if __name__ == "__main__":
    obj, tris = build("B4_Table")
    print("B4_Table tris", tris, "->", bk_common.export_fbx(obj, r"Models\B4_Table.fbx"))
