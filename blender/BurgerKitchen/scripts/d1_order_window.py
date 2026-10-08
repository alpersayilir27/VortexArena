# D1 "ORDER" pass-window frame along the service counters, with D3 number cards (1-2-3) by the
# bells. One piece for all three serving slots: posts sit on the slot borders and counter ends.
# Placement: world (-3.43, 1.04, 0), rotation Y 90 -> Blender X equals world Z, -Y faces the
# kitchen. Positions below come from the scene (slot centers z 2.21 / 0.17 / -1.88, bells x -3.30).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

POSTS = [-2.58, -0.855, 1.19, 2.95]   # world z of the posts
H, HEAD0 = 1.40, 1.10                 # frame height above the counter top, header bottom
PD = 0.12                             # post / header depth
# number card per slot: (label, world z of the bell); card stands 0.16 m past the bell.
# Seen from the kitchen, world -Z is on the left, so 1-2-3 read left to right.
CARDS = [("1", -1.46), ("2", 0.59), ("3", 2.63)]
CARD_Y = -0.13                        # world x -3.30 -> Blender y


def build(name):
    b = bk_common.Builder(name)
    x0, x1 = POSTS[0], POSTS[-1]
    b.box("Sill", (x1 - x0 + 0.12, 0.08, 0.06), ((x0 + x1) / 2, 0.01), 0.0, "red_dark", 0.015, 1)
    b.box("Header", (x1 - x0 + 0.12, PD, H - HEAD0), ((x0 + x1) / 2, 0), HEAD0, "red", 0.025, 2)
    for i, x in enumerate(POSTS):
        b.box(f"Post_{i}", (0.12, PD, H), (x, 0), 0.0, "red", 0.02, 2)
        b.box(f"Cap_{i}", (0.20, 0.18, 0.20), (x, 0), H - 0.10, "steel", 0.04, 2)
    for i, (a, c) in enumerate(zip(POSTS, POSTS[1:])):
        mid, w = (a + c) / 2, c - a
        b.text(f"Order_{i}", "SİPARİŞ", 0.16, (mid, -PD / 2 - 0.006, HEAD0 + (H - HEAD0) / 2 - 0.01), "yellow", 0.008)
        b.cyl(f"Rail_{i}", 0.014, w - 0.30, (mid, -PD / 2 - 0.02, HEAD0 - 0.04), "X", "steel", 12)
        for k in range(3):
            tx = mid - 0.28 + k * 0.28
            b.box(f"Ticket_{i}_{k}", (0.10, 0.006, 0.13), (tx, -PD / 2 - 0.03), HEAD0 - 0.17, "cream", 0)
            b.box(f"Clip_{i}_{k}", (0.03, 0.012, 0.025), (tx, -PD / 2 - 0.03), HEAD0 - 0.055, "steel_dark", 0)
    for label, bz in CARDS:
        cx = bz + 0.16
        b.box(f"Card_{label}", (0.12, 0.025, 0.13), (cx, CARD_Y), 0.0, "white", 0.012, 2)
        b.text(f"Number_{label}", label, 0.10, (cx, CARD_Y - 0.0125 - 0.004, 0.068), "red", 0.006)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("D1_OrderWindow")
    print("D1_OrderWindow tris", tris, "->", bk_common.export_fbx(obj, r"Models\D1_OrderWindow.fbx"))
