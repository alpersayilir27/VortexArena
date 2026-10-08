# F handhelds: knife, spatula, serving tray, cutting board. They replace the Visual of the shared
# NO_knife / NO_spatula / NO_board / NO_cutting_board prefabs (every Burger arena), sitting at the
# prefab root's local identity. Sizes are in ROOT-LOCAL units (knife and spatula roots are scaled
# 1.5, so world size = 1.5x). Values come from the old visuals/colliders, Unity frame:
#   knife   z -0.055..0.245 (handle at -z, blade toward +z, edge down), grip at the origin
#   spatula z -0.070..0.203 (handle at -z, flat blade 0.092..0.205 at +z), CargoAnchor y 0.008
#   tray    0.36 x 0.28, collider y +-0.015, CargoAnchor y 0.03
#   board   0.203 x 0.293, collider y +-0.006
# Blender <- Unity: (x, y, z)_blender = (-x, -z, y)_unity.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def U(x, y, z):
    """Unity root-local point -> Blender point."""
    return (-x, -z, y)


def knife(name):
    b = bk_common.Builder(name)
    cx = -0.0035                         # Unity x center of the old knife
    # handle along Unity z -0.058..0.040; Blender y = -z
    b.box("Handle", (0.024, 0.098, 0.030), (-cx, 0.009), -0.016, "wood", 0.010, 2)
    for i, z in enumerate([-0.035, 0.015]):
        b.cyl(f"Rivet_{i}", 0.0055, 0.027, U(cx, -0.001, z), "X", "steel", 10)
    b.box("Bolster", (0.020, 0.012, 0.036), (-cx, -0.046), -0.020, "steel_dark", 0.004, 1)
    # blade profile in Unity (z, y): heel to tip, spine on top, edge curving up to the tip
    prof = [(0.052, 0.010), (0.215, 0.006), (0.248, -0.004), (0.232, -0.018), (0.200, -0.028), (0.140, -0.035), (0.052, -0.036)]
    t = 0.007
    b.profile_x("Blade", [(-z, y) for z, y in prof], -cx - t / 2, t, "steel", 0.0025, 1)
    return b.bake()


def spatula(name):
    b = bk_common.Builder(name)
    b.box("Handle", (0.022, 0.102, 0.019), (0, 0.021), -0.008, "red", 0.008, 2)   # Unity z -0.072..0.030
    b.cyl("Rivet", 0.005, 0.024, U(0, 0.001, -0.005), "X", "steel", 10)
    b.box("Neck", (0.016, 0.070, 0.005), (0, -0.063), -0.002, "steel", 0.0015, 1)  # z 0.028..0.098
    b.box("Blade", (0.076, 0.113, 0.0045), (0, -0.1485), -0.0015, "steel", 0.003, 1)  # z 0.092..0.205
    for i, x in enumerate([-0.019, 0.0, 0.019]):
        for s, zz in (("T", 0.003), ("B", -0.002)):
            b.box(f"Slot_{i}{s}", (0.008, 0.068, 0.0012), (x, -0.148), zz, "steel_dark", 0)
    return b.bake()


def tray(name):
    b = bk_common.Builder(name)
    b.hollow_box("Tray", (0.36, 0.28, 0.037), (0, 0), -0.015,
                 (0.316, 0.236, 0.1), (0, 0), 0.008, "yellow", 0.008, 2)
    return b.bake()


def cutting_board(name):
    b = bk_common.Builder(name)
    o = b.box("Board", (0.203, 0.293, 0.014), (0, 0), -0.007, "wood", 0.006, 2)
    b.drill(o, 0.017, U(0, 0, 0.112))          # hanging hole at Unity +z end
    b.box("Groove", (0.17, 0.006, 0.0012), (0, 0.06), 0.0068, "wood_dark", 0)
    b.box("Groove2", (0.17, 0.006, 0.0012), (0, -0.05), 0.0068, "wood_dark", 0)
    return b.bake()


BUILDS = [("F1_Knife", knife), ("F2_Spatula", spatula), ("F3_Tray", tray), ("F4_CuttingBoard", cutting_board)]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
