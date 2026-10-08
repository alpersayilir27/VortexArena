# B7 work island (layout A: two islands, worked from both long sides), Cook'd Up style (bk_cabinet).
# Length runs along Blender Y (= Unity z), depth along X; pivot = floor centre, no rotation in the
# scene. Top surface at 1.04 m; the scene island has one solid box collider (Obstacle layer) +
# CounterSurface matching 1.1 x 3.0 x 1.04 — the overhang stays inside it.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
import bk_cabinet as cab
importlib.reload(bk_common)
importlib.reload(cab)

L, D, TOP = 3.0, 1.1, 1.04


def build(name):
    b = bk_common.Builder(name)
    cab.top(b, "Top", (D, L), (0, 0), TOP)
    bx, by = D - 2 * cab.OVER, L - 2 * cab.OVER
    cab.body(b, "Body", (bx, by), (0, 0), TOP)
    for tag, s in (("E", 1), ("W", -1)):
        cab.doors_x(b, tag, -by / 2, by / 2, s * bx / 2, s, TOP)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B7_Island")
    print("B7_Island tris", tris, "->", bk_common.export_fbx(obj, r"Models\B7_Island.fbx"))
