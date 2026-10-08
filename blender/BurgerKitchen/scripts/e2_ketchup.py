# E2 ketchup squeeze bottle = look of the sauce dispenser (stays a bottle, not a machine).
# Pivot = counter top under the dispenser root (root-local y -0.265); size close to the old
# 0.07 x 0.20 m bottle so the dispenser's own box collider still wraps it.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def build(name):
    b = bk_common.Builder(name)
    body = [(0, 0), (0.034, 0), (0.040, 0.007), (0.042, 0.03), (0.040, 0.065), (0.035, 0.088), (0.039, 0.11),
            (0.042, 0.14), (0.039, 0.163), (0.031, 0.172), (0.021, 0.177), (0, 0.177)]
    b.lathe("Body", body, (0, 0), "red", 24, scale_y=0.8)
    cap = [(0, 0.172), (0.024, 0.172), (0.025, 0.194), (0.020, 0.199), (0.011, 0.202), (0.008, 0.206),
           (0.004, 0.236), (0, 0.239)]
    b.lathe("Cap", cap, (0, 0), "yellow", 16)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("E2_Ketchup")
    print("E2_Ketchup tris", tris, "->", bk_common.export_fbx(obj, r"Models\E2_Ketchup.fbx"))
