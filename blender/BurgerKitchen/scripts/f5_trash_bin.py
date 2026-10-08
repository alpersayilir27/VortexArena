# F5 trash bin (burnt patties etc.). Sits on the old sink-cabinet roots and reuses their container
# boxes: floor top 0.788, rim 1.030, walls, back panel up to 1.36 (all relative to the root).
# The bag top stays below the floor box, otherwise dropped items sink into the bag.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W = 0.60
YF, YB = -0.365, 0.31                 # body front and back
BX, BYF, BYB = 0.2215, -0.303, 0.273  # cavity half-width, front, back (= container wall boxes)
FLOOR, RIM, LID_TOP = 0.783, 1.030, 1.36


def build(name):
    b = bk_common.Builder(name)
    yc, by = (YF + YB) / 2, (BYF + BYB) / 2
    b.box("Plinth", (W - 0.06, YB - YF - 0.06, 0.07), (0, yc), 0.0, "charcoal", 0.02, 1)
    b.hollow_box("Body", (W, YB - YF, RIM - 0.05), (0, yc), 0.05,
                 (2 * BX, BYB - BYF, 0.6), (0, by), FLOOR - 0.03, "green", 0.035, 3)

    # full black bag: puffy top just under the floor box, edge folded over the rim
    b.box("Bag", (2 * BX - 0.01, BYB - BYF - 0.01, 0.08), (0, by), FLOOR - 0.08, "charcoal", 0.03, 3)
    b.hollow_box("BagFold", (W + 0.02, YB - YF + 0.02, 0.07), (0, yc), RIM - 0.055,
                 (2 * BX, BYB - BYF, 0.2), (0, by), RIM - 0.1, "charcoal", 0.02, 2)

    # lid standing open at the back, on a steel hinge
    b.cyl("Hinge", 0.025, W - 0.08, (0, YB - 0.03, RIM + 0.015), "X", "steel", 12, 0.006)
    b.box("Lid", (W + 0.02, 0.06, LID_TOP - RIM), (0, YB + 0.0), RIM, "green_dark", 0.025, 2)
    b.box("LidGrip", (0.18, 0.04, 0.035), (0, YB - 0.04), LID_TOP - 0.08, "steel", 0.012, 1)

    # front: yellow band under the rim, recycling symbol, foot pedal
    b.box("Band", (W - 0.12, 0.012, 0.06), (0, YF - 0.004), 0.86, "yellow", 0.006, 1)
    b.decal("RecycleSticker", (0.34, 0.34), (0.0, 0.48), YF - 0.003, "recycle", 0.11)
    b.box("Pedal", (0.20, 0.09, 0.035), (0, YF - 0.035), 0.075, "steel", 0.012, 1)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("F5_TrashBin")
    print("F5_TrashBin tris", tris, "->", bk_common.export_fbx(obj, r"Models\F5_TrashBin.fbx"))
