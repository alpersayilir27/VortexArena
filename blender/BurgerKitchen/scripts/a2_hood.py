# A2 exhaust hood over both grills and the fryer. Front faces -Y; pivot = bottom center of the
# trim band, placed 2.0 m above the floor with the back (+Y) against the north wall.
# Duct runs up into the ceiling: DUCT_TOP above the pivot = g_shell.CEIL (3.2) - 2.0 + 0.02.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W, D = 3.60, 0.90
TRIM_H, TOP = 0.12, 0.70
DUCT_TOP = 1.22


def build(name):
    b = bk_common.Builder(name)
    b.box("Trim", (W + 0.08, D + 0.06, TRIM_H), (0, 0), 0.0, "steel", 0.035, 2)
    # tapered red body: narrower and shallower at the top
    hx0, hx1, yb = W / 2, W / 2 - 0.18, D / 2
    pts = [(sx * hx0, y, TRIM_H) for sx in (-1, 1) for y in (-D / 2, yb)]
    pts += [(sx * hx1, y, TOP) for sx in (-1, 1) for y in (-0.06, yb)]
    b.hull("Body", pts, "red", 0.04, 2)
    # underside grilles: dark panel + slats, seen from below by players at the grill
    for i, x in enumerate([-1.17, 0.0, 1.17]):
        b.box(f"Grille_{i}", (0.98, 0.56, 0.015), (x, -0.04), -0.015, "charcoal", 0)
        for k in range(5):
            b.box(f"Slat_{i}_{k}", (0.92, 0.035, 0.018), (x, -0.26 + k * 0.11), -0.033, "steel_dark", 0.006, 1)
    # duct to the ceiling
    b.box("DuctCollar", (0.66, 0.52, 0.08), (0, 0.18), TOP - 0.02, "steel_dark", 0.015, 1)
    b.box("Duct", (0.56, 0.44, DUCT_TOP - TOP), (0, 0.18), TOP, "steel", 0.02, 1)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("A2_Hood")
    print("A2_Hood tris", tris, "->", bk_common.export_fbx(obj, r"Models\A2_Hood.fbx"))
