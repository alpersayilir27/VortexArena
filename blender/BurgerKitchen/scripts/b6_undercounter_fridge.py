# B6 under-counter fridge (old Prop_Fridge_03). Front faces -Y, pivot = old root. Fits the old
# obstacle box: 1.61 wide, front -0.446 / back 0.418, top 0.846 (root-local, Blender axes).
# Teal to match A4; the old box stays the collider.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W, YF, YB, TOP = 1.61, -0.446, 0.418, 0.846
BODY_F = YF + 0.036   # body front face; doors sit in front of it


def build(name):
    b = bk_common.Builder(name)
    for i, (x, y) in enumerate([(-0.70, -0.32), (0.70, -0.32), (-0.70, 0.32), (0.70, 0.32)]):
        b.cyl(f"Foot_{i}", 0.035, 0.05, (x, y, 0.025), "Z", "steel", 12, 0.008)
    b.box("Plinth", (W - 0.05, YB - BODY_F - 0.02, 0.07), (0, (BODY_F + YB) / 2), 0.04, "steel_dark", 0.02, 1)
    b.box("Body", (W - 0.02, YB - BODY_F, 0.70), (0, (BODY_F + YB) / 2), 0.08, "teal", 0.04, 2)
    b.box("Top", (W, YB - YF, TOP - 0.78), (0, (YF + YB) / 2), 0.78, "white", 0.025, 2)

    gap = 0.025
    dw = (W - 0.06 - gap) / 2
    for i, s in enumerate([-1, 1]):
        b.box(f"Door_{i}", (dw, 0.034, 0.60), (s * (gap / 2 + dw / 2), BODY_F - 0.017), 0.13, "teal", 0.04, 3)
    b.box("DoorGap", (gap, 0.01, 0.58), (0, BODY_F - 0.005), 0.14, "charcoal", 0)
    for i, x in enumerate([-0.10, 0.10]):
        b.cyl(f"HandleBar_{i}", 0.022, 0.30, (x, -0.468, 0.43), "Z", "steel", 14, 0.01, 2)
        for j, z in enumerate([0.32, 0.54]):
            b.cyl(f"HandleStem_{i}_{j}", 0.016, 0.04, (x, -0.445, z), "Y", "steel", 10)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B6_UndercounterFridge")
    print("B6_UndercounterFridge tris", tris, "->", bk_common.export_fbx(obj, r"Models\B6_UndercounterFridge.fbx"))
