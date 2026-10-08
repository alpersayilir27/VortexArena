# A4 tall double-door fridge. Front faces -Y, pivot at bottom center of the footprint.
# Fits inside the old fridge's obstacle box (1.61 x 0.86 x 2.05 m), which stays in the scene.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W, H = 1.56, 2.02
FRONT = -0.30  # body front face

VARIANTS = {
    "teal": dict(body="teal", door="teal", band="cream", badge="red", handle="steel", groove="steel_dark"),
    "red_cream": dict(body="red", door="cream", band="red_dark", badge="cream", handle="steel", groove="steel_dark"),
}
VARIANT = "teal"


def build(name, c):
    b = bk_common.Builder(name)
    for i, (x, y) in enumerate([(-0.65, -0.22), (0.65, -0.22), (-0.65, 0.34), (0.65, 0.34)]):
        b.cyl(f"Foot_{i}", 0.05, 0.07, (x, y, 0.035), "Z", "steel", 14, 0.01)
    b.box("Plinth", (W - 0.08, 0.66, 0.10), (0, 0.06), 0.05, "charcoal", 0.03, 2)
    b.box("Body", (W, 0.73, H - 0.13), (0, 0.065), 0.13, c["body"], 0.07, 3)

    # top band with badge
    b.box("TopBand", (W - 0.12, 0.03, 0.20), (0, FRONT - 0.015), 1.76, c["band"], 0.012, 2)
    b.box("Badge", (0.26, 0.05, 0.09), (0, FRONT - 0.04), 1.815, c["badge"], 0.024, 3)
    b.box("BandGroove", (W - 0.14, 0.012, 0.022), (0, FRONT - 0.006), 1.735, c["groove"], 0)

    # doors with raised inner panel, dark gap, chunky bar handles
    dw, gap = (W - 0.09) / 2, 0.03
    for i, s in enumerate([-1, 1]):
        x = s * (gap / 2 + dw / 2)
        b.box(f"Door_{i}", (dw, 0.055, 1.52), (x, FRONT - 0.0275), 0.20, c["door"], 0.035, 3)
        b.box(f"DoorPanel_{i}", (dw - 0.14, 0.015, 1.30), (x, FRONT - 0.062), 0.31, c["door"], 0.007, 2)
    b.box("DoorGap", (gap, 0.01, 1.50), (0, FRONT - 0.005), 0.21, "charcoal", 0)
    for i, x in enumerate([-0.085, 0.085]):
        b.cyl(f"HandleBar_{i}", 0.034, 0.80, (x, -0.405, 0.96), "Z", c["handle"], 16, 0.015, 2)
        for j, z in enumerate([0.62, 1.30]):
            b.cyl(f"HandleStem_{i}_{j}", 0.024, 0.07, (x, -0.37, z), "Y", c["handle"], 12, 0.006)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("A4_Fridge", VARIANTS[VARIANT])
    print("A4_Fridge tris", tris, "->", bk_common.export_fbx(obj, r"Models\A4_Fridge.fbx"))
