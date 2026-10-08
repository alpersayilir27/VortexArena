# B5 tray rack on casters (old Prop_TrayHolder). Front faces -Y, pivot = old root; trays slide in
# along Y. Frame ~0.58 x 0.64 m like the old model; the old 1.61 m obstacle box stays the collider.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

PX, PY, H = 0.26, 0.29, 1.60     # post centers (half extents) and frame top
P = 0.055                        # post thickness
LEVELS = [0.24 + 0.235 * k for k in range(6)]


def build(name):
    b = bk_common.Builder(name)
    for i, (x, y) in enumerate([(-PX, -PY), (PX, -PY), (-PX, PY), (PX, PY)]):
        b.box(f"Post_{i}", (P, P, H - 0.12), (x, y), 0.12, "steel", 0.012, 1)
        b.box(f"Fork_{i}", (0.05, 0.05, 0.05), (x, y), 0.07, "steel_dark", 0.01, 1)
        b.cyl(f"Wheel_{i}", 0.045, 0.035, (x, y, 0.045), "X", "charcoal", 14, 0.008)
    for z, tag in ((0.12, "Low"), (H - 0.035, "Top")):
        for i, s in enumerate([-1, 1]):
            b.box(f"{tag}X_{i}", (2 * PX, P, 0.035), (0, s * PY), z, "steel", 0.01, 1)
            b.box(f"{tag}Y_{i}", (P, 2 * PY, 0.035), (s * PX, 0), z, "steel", 0.01, 1)
    # side brace on the +X side, like the reference
    b.pipe("Brace", [(PX, -PY + 0.02, 0.16), (PX, PY - 0.02, 0.70)], 0.014, "steel", 8)
    for k, z in enumerate(LEVELS):
        for i, s in enumerate([-1, 1]):
            b.box(f"Runner_{k}_{i}", (0.03, 2 * PY, 0.018), (s * (PX - 0.03), 0), z - 0.018, "steel_dark", 0.005, 1)
        b.hollow_box(f"Tray_{k}", (0.44, 0.56, 0.05), (0, -0.01), z,
                     (0.40, 0.52, 0.1), (0, -0.01), z + 0.014, "yellow", 0.012, 1)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B5_TrayRack")
    print("B5_TrayRack tris", tris, "->", bk_common.export_fbx(obj, r"Models\B5_TrayRack.fbx"))
