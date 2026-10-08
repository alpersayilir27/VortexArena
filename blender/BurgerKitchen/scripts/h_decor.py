# H decor: wall clock, wall-mounted fire extinguisher, neon "BURGER" sign. Each is its own FBX,
# front faces -Y, pivot = wall contact point at the bottom center (clock: center of its back).
# Neon tubes use the glow material (palette colors, unlit); no real light is added.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)


def clock(name):
    b = bk_common.Builder(name)
    R = 0.20   # face in the XZ plane, pivot at the center of the back
    b.cyl("Rim", R, 0.06, (0, -0.03, 0), "Y", "red", 32, 0.02, 2)
    b.cyl("Face", R - 0.03, 0.062, (0, -0.031, 0), "Y", "cream", 32)
    for k in range(12):
        a = math.radians(90 - 30 * k)
        big = k % 3 == 0
        r0 = R - 0.055
        b.box(f"Tick_{k}", (0.012 if not big else 0.018, 0.006, 0.026 if not big else 0.04),
              (r0 * math.cos(a), -0.064), r0 * math.sin(a) - (0.013 if not big else 0.02), "charcoal", 0, rot_y=-math.degrees(a) + 90)
    # hands pivot on the centre pin: the box is rotated around its own centre, so the centre is
    # pushed out along the hand direction (angle = clockwise from 12 seen from the front, 10:10)
    for tag, ang, L, wdt, y in (("HourHand", 305, 0.10, 0.018, -0.068), ("MinuteHand", 60, 0.15, 0.012, -0.072)):
        tail = 0.02
        a = math.radians(ang)
        off = L / 2 - tail
        b.box(tag, (wdt, 0.006, L), (off * math.sin(a), y), off * math.cos(a) - L / 2, "charcoal", 0.004, 1, rot_y=ang)
    b.cyl("Pin", 0.014, 0.02, (0, -0.075, 0), "Y", "red", 12)
    return b.bake()


def extinguisher(name):
    b = bk_common.Builder(name)
    yb = -0.10   # body axis distance from the wall
    b.box("Bracket", (0.12, 0.03, 0.20), (0, -0.015), 0.10, "steel_dark", 0.008, 1)
    b.lathe("Body", [(0, 0.0), (0.075, 0.0), (0.085, 0.02), (0.085, 0.42), (0.07, 0.47), (0.035, 0.49), (0, 0.49)], (0, yb), "red", 24)
    b.lathe("Label", [(0.086, 0.16), (0.087, 0.16), (0.087, 0.30), (0.086, 0.30)], (0, yb), "cream", 24)
    b.lathe("Valve", [(0, 0.48), (0.03, 0.48), (0.03, 0.54), (0.02, 0.56), (0, 0.56)], (0, yb), "charcoal", 14)
    b.box("Handle", (0.025, 0.14, 0.02), (0, yb - 0.05), 0.565, "steel", 0.006, 1, rot_x=-12)
    b.pipe("Hose", [(0.02, yb - 0.02, 0.53), (0.09, yb - 0.04, 0.50), (0.11, yb - 0.06, 0.30), (0.10, yb - 0.08, 0.16)], 0.014, "charcoal", 8)
    b.lathe("Nozzle", [(0, 0.10), (0.022, 0.10), (0.018, 0.16), (0, 0.16)], (0.10, yb - 0.08), "charcoal", 10)
    return b.bake()


def neon(name):
    b = bk_common.Builder(name)
    W, H = 1.30, 0.50
    b.box("Board", (W, 0.035, H), (0, -0.0175), 0.0, "charcoal", 0.04, 2)
    o = b.text("Word", "BURGER", 0.28, (0, -0.045, H / 2 - 0.01), "yellow", 0.02)
    o["bk_glow"] = True
    # red neon tube along the board edge
    inset, r = 0.05, 0.012
    x0, x1, z0, z1 = -W / 2 + inset, W / 2 - inset, inset, H - inset
    loop = [(x0, -0.05, z0), (x1, -0.05, z0), (x1, -0.05, z1), (x0, -0.05, z1), (x0, -0.05, z0 + 0.001)]
    t = b.pipe("Tube", loop, r, "red", 8)
    t["bk_glow"] = True
    for i, x in enumerate([-0.45, 0.45]):
        b.box(f"Standoff_{i}", (0.03, 0.03, 0.03), (x, -0.045), H - 0.04, "steel", 0.006, 1)
    return b.bake()


BUILDS = [("H3_Clock", clock), ("H4_Extinguisher", extinguisher), ("H7_Neon", neon)]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
