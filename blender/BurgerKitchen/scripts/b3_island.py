# B3 L-shaped prep island (old Prop_KitchenTable_07/08, all four share this root-local layout).
# Front faces -Y, pivot = old table root. Collider: two solid CounterSurface boxes, top 1.033 local;
# back splash boxes along the back edge and the short arm's outer side. The visual top is the same L.
# Export turns Blender +X into Unity -X: Unity-local x values from the scene are negated here.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

TOP, TOP_T = 1.033, 0.06
# collider outline (Blender x = -Unity x): long arm at the back, short arm toward -Y on the +X side
L_OUT = [(1.067, -0.967), (0.320, -0.967), (0.320, -0.347), (-0.300, -0.347), (-0.300, 0.400), (1.067, 0.400)]
L_BODY = [(1.052, -0.932), (0.355, -0.932), (0.355, -0.312), (-0.265, -0.312), (-0.265, 0.385), (1.052, 0.385)]
L_PLINTH = [(1.0, -0.87), (0.415, -0.87), (0.415, -0.25), (-0.205, -0.25), (-0.205, 0.33), (1.0, 0.33)]


def door_y(b, tag, x, face_y, dw):
    """Door on a -Y facing side."""
    b.box(f"Door_{tag}", (dw, 0.022, 0.70), (x, face_y - 0.011), 0.15, "wood", 0.012, 1)
    b.box(f"DoorPanel_{tag}", (dw - 0.10, 0.010, 0.58), (x, face_y - 0.026), 0.21, "wood_dark", 0.005, 1)
    b.cyl(f"Handle_{tag}", 0.014, 0.13, (x - dw / 2 + 0.06, face_y - 0.04, 0.72), "Z", "steel", 10, 0.005)


def door_neg_x(b, tag, face_x, y, dw):
    """Door on a -X facing side."""
    b.box(f"Door_{tag}", (0.022, dw, 0.70), (face_x - 0.011, y), 0.15, "wood", 0.012, 1)
    b.box(f"DoorPanel_{tag}", (0.010, dw - 0.10, 0.58), (face_x - 0.026, y), 0.21, "wood_dark", 0.005, 1)
    b.cyl(f"Handle_{tag}", 0.014, 0.13, (face_x - 0.04, y - dw / 2 + 0.06, 0.72), "Z", "steel", 10, 0.005)


def build(name):
    b = bk_common.Builder(name)
    b.prism("Top", L_OUT, TOP - TOP_T, TOP, "white", 0.02, 2)
    b.prism("Body", L_BODY, 0.07, TOP - TOP_T, "wood", 0.02, 2)
    b.prism("Plinth", L_PLINTH, 0.0, 0.07, "charcoal", 0.015, 1)
    b.box("Splash_B", (1.367, 0.02, 0.10), (0.3835, 0.39), TOP, "steel", 0.008, 1)
    b.box("Splash_S", (0.02, 1.367, 0.10), (1.057, -0.2835), TOP, "steel", 0.008, 1)
    door_y(b, "ShortFront", 0.7035, -0.932, 0.60)
    door_y(b, "LongFront", 0.045, -0.312, 0.56)
    door_neg_x(b, "LongSide", -0.265, 0.0365, 0.60)
    door_neg_x(b, "ShortSide", 0.355, -0.622, 0.56)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B3_Island")
    print("B3_Island tris", tris, "->", bk_common.export_fbx(obj, r"Models\B3_Island.fbx"))
