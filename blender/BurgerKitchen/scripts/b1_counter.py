# B1 long counter (2.79 m), Cook'd Up style (bk_cabinet). Front faces -Y, pivot = old table root.
# The old table keeps its solid CounterSurface box (top 1.040 world = 1.033 local) and a 0.10 m back
# splash box along +Y; the visual top surface sits exactly on that box.
# build() is shared by B2 (service counter) and B4 (1.8 m table).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
import bk_cabinet as cab
importlib.reload(bk_common)
importlib.reload(cab)

YF, YB = -0.369, 0.403   # collider front / back
TOP = 1.033              # collider top, root-local


def build(name, length, service=False):
    b = bk_common.Builder(name)
    L = length
    bf, bb = YF + cab.OVER, YB - 0.012          # cabinet faces under the overhanging top
    cab.top(b, "Top", (L + 2 * cab.OVER, YB - YF), (0, (YF + YB) / 2), TOP)
    b.box("Splash", (L + 2 * cab.OVER, 0.02, 0.10), (0, YB - 0.01), TOP, "steel", 0.008, 1)
    cab.body(b, "Body", (L, bb - bf), (0, (bf + bb) / 2), TOP)
    cab.doors_y(b, "F", -L / 2, L / 2, bf, -1, TOP)
    if service:
        # customer side: red diner panel with a chrome stripe
        z_top = TOP - cab.TOP_T
        b.box("ServicePanel", (L, 0.02, z_top - cab.PLINTH_H), (0, bb + 0.01), cab.PLINTH_H, "red", 0.01, 1)
        b.box("ServiceStripe", (L, 0.03, 0.045), (0, bb + 0.025), z_top - 0.12, "steel", 0.01, 1)
        b.box("ServiceKick", (L, 0.025, cab.PLINTH_H), (0, bb + 0.0125), 0.0, "red_dark", 0.008, 1)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B1_Counter", 2.79)
    print("B1_Counter tris", tris, "->", bk_common.export_fbx(obj, r"Models\B1_Counter.fbx"))
