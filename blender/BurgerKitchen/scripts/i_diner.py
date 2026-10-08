# I diner (behind the ORDER windows, outside the play area, look only — no colliders needed):
# I1 booth = table + two benches + ketchup bottle, table length along X, benches on +-Y,
# pivot = floor center of the table. I2 jukebox, front -Y, pivot = floor center.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def booth(name):
    b = bk_common.Builder(name)
    TL = 1.0
    # table: steel pedestal + cream top with chrome edge
    b.cyl("TableFoot", 0.22, 0.03, (0, 0, 0.015), "Z", "steel_dark", 20, 0.01)
    b.cyl("TablePost", 0.05, 0.70, (0, 0, 0.38), "Z", "steel", 14)
    b.box("TableEdge", (TL, 0.72, 0.05), (0, 0), 0.70, "steel", 0.02, 2)
    b.box("TableTop", (TL - 0.03, 0.69, 0.02), (0, 0), 0.745, "cream", 0.008, 1)
    for s, tag in ((1, "N"), (-1, "S")):
        y0 = s * 0.42                       # seat inner edge
        b.box(f"SeatBase_{tag}", (TL, 0.46, 0.40), (0, y0 + s * 0.23), 0.0, "red_dark", 0.02, 1)
        b.box(f"Seat_{tag}", (TL, 0.46, 0.10), (0, y0 + s * 0.23), 0.38, "teal", 0.04, 3)
        b.box(f"Back_{tag}", (TL, 0.16, 0.72), (0, y0 + s * 0.53), 0.40, "teal", 0.05, 3)
        b.box(f"BackTrim_{tag}", (TL + 0.02, 0.18, 0.04), (0, y0 + s * 0.53), 1.10, "steel", 0.015, 1)
        for k in range(3):   # tufted seams
            b.box(f"Seam_{tag}{k}", (0.012, 0.01, 0.50), (-0.3 + k * 0.3, y0 + s * 0.445), 0.52, "green_dark", 0)
    # ketchup bottle + napkin holder on the table
    body = [(0, 0.765), (0.03, 0.765), (0.034, 0.78), (0.034, 0.88), (0.025, 0.9), (0, 0.9)]
    b.lathe("Bottle", body, (0.25, 0.1), "red", 14, scale_y=0.8)
    b.lathe("BottleCap", [(0, 0.895), (0.018, 0.895), (0.018, 0.915), (0.004, 0.94), (0, 0.94)], (0.25, 0.1), "yellow", 10)
    b.box("Napkins", (0.10, 0.07, 0.09), (0.25, -0.08), 0.765, "steel", 0.008, 1)
    return b.bake()


def jukebox(name):
    b = bk_common.Builder(name)
    W, Dp = 0.75, 0.55
    b.box("Body", (W, Dp, 1.15), (0, 0), 0.0, "purple", 0.05, 3)
    b.cyl("Arch", W / 2, Dp, (0, 0, 1.15), "Y", "purple", 24, 0.04, 2)
    b.cyl("ArchGlass", W / 2 - 0.07, 0.02, (0, -Dp / 2 - 0.005, 1.15), "Y", "cream", 24)
    g = b.box("GlowL", (0.05, 0.03, 1.0), (-W / 2 + 0.06, -Dp / 2 - 0.01), 0.12, "yellow", 0.01, 1); g["bk_glow"] = True
    g = b.box("GlowR", (0.05, 0.03, 1.0), (W / 2 - 0.06, -Dp / 2 - 0.01), 0.12, "yellow", 0.01, 1); g["bk_glow"] = True
    b.box("Speaker", (0.48, 0.02, 0.40), (0, -Dp / 2 - 0.006), 0.15, "charcoal", 0.03, 2)
    for k in range(5):
        b.box(f"Slat_{k}", (0.44, 0.012, 0.02), (0, -Dp / 2 - 0.018), 0.20 + k * 0.07, "steel", 0)
    b.box("Panel", (0.5, 0.02, 0.2), (0, -Dp / 2 - 0.006), 0.66, "red", 0.02, 1)
    for k in range(4):
        b.cyl(f"Button_{k}", 0.02, 0.02, (-0.15 + k * 0.1, -Dp / 2 - 0.02, 0.74), "Y", "white", 10)
    return b.bake()


BUILDS = [("I1_Booth", booth), ("I2_Jukebox", jukebox)]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
