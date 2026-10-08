# A3 deep fryer. Front faces -Y, pivot = old fryer's root (body sits behind it, back at +0.40).
# Body fits the old fryer's obstacle box (0.48 x 0.67 m, top 0.98 m), which stays in the scene.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W, Y0, Y1 = 0.50, -0.27, 0.40   # body width, front and back faces
TOP = 0.95                        # top plate; rim rises to TOP + 0.045


def build(name):
    b = bk_common.Builder(name)
    yc, d = (Y0 + Y1) / 2, Y1 - Y0
    for i, (x, y) in enumerate([(-0.19, Y0 + 0.08), (0.19, Y0 + 0.08), (-0.19, Y1 - 0.08), (0.19, Y1 - 0.08)]):
        b.cyl(f"Foot_{i}", 0.035, 0.07, (x, y, 0.035), "Z", "steel", 12, 0.008)
    b.box("Body", (W, d, TOP - 0.06), (0, yc), 0.06, "steel", 0.045, 3)

    # front red panel with big cream dial and yellow heat arc
    b.box("Panel", (W - 0.08, 0.02, 0.52), (0, Y0 - 0.01), 0.28, "red", 0.012, 2)
    dz = 0.58
    b.cyl("Dial", 0.105, 0.03, (0, Y0 - 0.035, dz), "Y", "cream", 24, 0.012, 2)
    for k in range(7):  # arc on the dial's upper-right
        a =math.radians(-10 + k * 22)
        b.box(f"Arc_{k}", (0.03, 0.012, 0.022), (0.128 * math.cos(a), Y0 - 0.026), dz + 0.128 * math.sin(a) - 0.011, "yellow", 0, rot_y=-math.degrees(a) + 90)
    b.box("Knob", (0.03, 0.035, 0.13), (0, Y0 - 0.06), dz - 0.065, "yellow", 0.012, 2)

    # top: plate, raised rim around a golden oil well
    b.box("TopPlate", (W, d, 0.02), (0, yc), TOP - 0.02, "steel", 0.008, 1)
    b.box("Oil", (W - 0.08, d - 0.12, 0.012), (0, yc - 0.01), TOP, "yellow", 0)
    rim_h = 0.045
    b.box("Rim_F", (W, 0.04, rim_h), (0, Y0 + 0.02), TOP, "steel", 0.01, 1)
    b.box("Rim_B", (W, 0.08, rim_h + 0.06), (0, Y1 - 0.04), TOP, "steel", 0.012, 1)
    for i, sx in enumerate([-1, 1]):
        b.box(f"Rim_S{i}", (0.04, d, rim_h), (sx * (W / 2 - 0.02), yc), TOP, "steel", 0.01, 1)

    # two wire baskets resting on the oil, wooden handles angled up toward the back
    for i, x in enumerate([-0.105, 0.105]):
        bx0, bx1, by0, by1, bz0, bz1 = x - 0.085, x + 0.085, Y0 + 0.07, Y1 - 0.15, TOP - 0.02, TOP + 0.10
        t = 0.012
        for k, (cx, cy, sx, sy) in enumerate([(x, by0, bx1 - bx0, t), (x, by1, bx1 - bx0, t), (bx0, (by0 + by1) / 2, t, by1 - by0), (bx1, (by0 + by1) / 2, t, by1 - by0)]):
            b.box(f"BasketTop_{i}_{k}", (sx, sy, t), (cx, cy), bz1 - t, "steel_dark", 0)
        for k in range(4):  # vertical wires on the front face
            b.box(f"BasketWire_{i}_{k}", (0.008, 0.008, bz1 - bz0), (bx0 + 0.02 + k * 0.043, by0), bz0, "steel_dark", 0)
        b.pipe(f"Hook_{i}", [(x, by1, bz1), (x, by1 + 0.05, bz1 + 0.04), (x, Y1 - 0.06, bz1 + 0.05)], 0.008, "steel_dark", 8)
        b.pipe(f"HandleRod_{i}", [(x, by0 + 0.02, bz1), (x, by0 - 0.01, bz1 + 0.06)], 0.009, "steel_dark", 8)
        b.pipe(f"Handle_{i}", [(x, by0 - 0.01, bz1 + 0.06), (x, by0 - 0.06, bz1 + 0.16)], 0.02, "wood", 10)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("A3_Fryer")
    print("A3_Fryer tris", tris, "->", bk_common.export_fbx(obj, r"Models\A3_Fryer.fbx"))
