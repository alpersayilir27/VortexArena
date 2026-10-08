# C small appliances on the east counter. Front faces -Y, pivot = old root (on the counter top).
# C1 microwave fits the old box: width 0.61, front -0.208 / back 0.22, height 0.404.
# C2 single hot plate fits the old box: width 0.33, front -0.224 / back 0.183, height 0.064.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def microwave(name):
    b = bk_common.Builder(name)
    W, YF, YB, H = 0.60, -0.198, 0.22, 0.40
    for i, (x, y) in enumerate([(-0.25, YF + 0.05), (0.25, YF + 0.05), (-0.25, YB - 0.05), (0.25, YB - 0.05)]):
        b.cyl(f"Foot_{i}", 0.02, 0.02, (x, y, 0.01), "Z", "charcoal", 10)
    b.box("Body", (W, YB - YF, H - 0.02), (0, (YF + YB) / 2), 0.02, "cream", 0.045, 3)
    # door with dark rounded window (viewer's left) and a bar handle
    b.box("Door", (0.42, 0.02, 0.32), (-0.075, YF - 0.01), 0.06, "cream", 0.02, 2)
    b.box("Window", (0.32, 0.016, 0.23), (-0.085, YF - 0.022), 0.105, "charcoal", 0.035, 3)
    b.box("Handle", (0.026, 0.03, 0.20), (0.11, YF - 0.035), 0.12, "steel", 0.01, 2)
    # red control panel (viewer's right) with two big dials
    b.box("Panel", (0.15, 0.022, 0.34), (0.215, YF - 0.011), 0.04, "red", 0.018, 2)
    for i, (z, col) in enumerate([(0.29, "yellow"), (0.15, "steel")]):
        b.cyl(f"Dial_{i}", 0.038, 0.03, (0.215, YF - 0.035, z), "Y", col, 20, 0.01, 2)
        b.box(f"DialMark_{i}", (0.008, 0.006, 0.032), (0.215, YF - 0.052), z - 0.004, "charcoal", 0)
    return b.bake()


def hot_plate(name):
    b = bk_common.Builder(name)
    W, YF, YB = 0.32, -0.215, 0.175
    b.box("Body", (W, YB - YF, 0.045), (0, (YF + YB) / 2), 0.0, "cream", 0.015, 2)
    b.box("Front", (W - 0.04, 0.012, 0.03), (0, YF - 0.004), 0.008, "red", 0.006, 1)
    b.lathe("Burner", [(0, 0.045), (0.11, 0.045), (0.112, 0.052), (0.105, 0.056), (0, 0.056)], (0, 0.02), "charcoal", 28)
    b.lathe("Ring", [(0.06, 0.056), (0.075, 0.056), (0.075, 0.059), (0.06, 0.059)], (0, 0.02), "red", 28)
    for i, x in enumerate([-0.08, 0.08]):
        b.cyl(f"Knob_{i}", 0.016, 0.018, (x, YF - 0.012, 0.023), "Y", "steel", 14, 0.004)
    return b.bake()


BUILDS = [("C1_Microwave", microwave), ("C2_HotPlate", hot_plate)]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
