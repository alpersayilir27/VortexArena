# A4 tall double-door fridge. Front faces -Y, pivot at bottom center of the footprint.
# Fits inside the old fridge's obstacle box (1.61 x 0.86 x 2.05 m), which stays in the scene.
# Three models so the doors can open (BurgerFridge):
#   A4_Fridge       body with an open-front cavity, white liner, shelves and a lamp strip
#   A4_FridgeDoorL  door on the viewer's left (Blender -x, Unity +x), pivot = its hinge
#   A4_FridgeDoorR  door on the viewer's right, pivot = its hinge
# Hinge = the door's back outer corner (x = s * HINGE_X, y = FRONT), so an opening door clears the body.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W, H = 1.56, 2.02
FRONT = -0.30  # body front face
BACK = 0.43
DW, GAP = (W - 0.09) / 2, 0.03       # door width, centre gap
HINGE_X = GAP / 2 + DW
DZ0, DH, DT = 0.20, 1.52, 0.055      # door bottom, height, thickness
# cavity cut into the body, and the liner inside it (liner front recessed 1 cm)
# walls stay >= 9 cm and the liner 2.5 cm: the body bevel rounds the rim and fills the cavity's
# inner corners by ~2 cm, a thinner liner lets it poke through
CAV_X, CAV_BACK, CAV_Z0, CAV_Z1 = 0.69, 0.37, 0.24, 1.70
LT = 0.025
BODY_BEVEL = 0.045
IN_X, IN_BACK, IN_Z0, IN_Z1 = CAV_X - 0.0025 - LT, CAV_BACK - 0.0025 - LT, CAV_Z0 + 0.0025 + LT, CAV_Z1 - 0.0025 - LT
SHELF_FRONT = FRONT + 0.13           # door bins reach FRONT + 0.117 when closed
SHELVES = [0.60, 0.95, 1.30]         # shelf top heights
BINS = [0.42, 0.86, 1.30]            # door bin floor heights
BIN_W = 0.54

VARIANTS = {
    "teal": dict(body="teal", door="teal", band="cream", badge="red", handle="steel", groove="steel_dark", liner="white"),
    "red_cream": dict(body="red", door="cream", band="red_dark", badge="cream", handle="steel", groove="steel_dark", liner="white"),
}
VARIANT = "teal"


def body(name, c):
    b = bk_common.Builder(name)
    for i, (x, y) in enumerate([(-0.65, -0.22), (0.65, -0.22), (-0.65, 0.34), (0.65, 0.34)]):
        b.cyl(f"Foot_{i}", 0.05, 0.07, (x, y, 0.035), "Z", "steel", 14, 0.01)
    b.box("Plinth", (W - 0.08, 0.66, 0.10), (0, 0.06), 0.05, "charcoal", 0.03, 2)
    cav_d = CAV_BACK - (FRONT - 0.05)
    b.hollow_box("Body", (W, BACK - FRONT, H - 0.13), (0, (FRONT + BACK) / 2), 0.13,
                 (2 * CAV_X, cav_d, CAV_Z1 - CAV_Z0), (0, CAV_BACK - cav_d / 2), CAV_Z0, c["body"], BODY_BEVEL, 3)

    # white liner: open-front shell sitting 2.5 mm inside the cavity
    lo_x, lo_back, lo_z0, lo_z1 = CAV_X - 0.0025, CAV_BACK - 0.0025, CAV_Z0 + 0.0025, CAV_Z1 - 0.0025
    lo_front = FRONT + 0.01
    in_d = IN_BACK - (FRONT - 0.05)
    b.hollow_box("Liner", (2 * lo_x, lo_back - lo_front, lo_z1 - lo_z0), (0, (lo_front + lo_back) / 2), lo_z0,
                 (2 * IN_X, in_d, IN_Z1 - IN_Z0), (0, IN_BACK - in_d / 2), IN_Z0, c["liner"], 0.005, 1)
    # shelves: ends sink 2.5 mm into the liner walls instead of touching them
    for i, z in enumerate(SHELVES):
        b.box(f"Shelf_{i}", (2 * IN_X + 0.005, IN_BACK + 0.0025 - SHELF_FRONT, 0.02),
              (0, (SHELF_FRONT + IN_BACK + 0.0025) / 2), z - 0.02, "steel", 0.004, 1)
        b.box(f"ShelfLip_{i}", (2 * IN_X + 0.002, 0.012, 0.035), (0, SHELF_FRONT - 0.004), z - 0.025, "steel_dark", 0.003, 1)
    lamp = b.box("Lamp", (0.50, 0.06, 0.02), (0, (SHELF_FRONT + IN_BACK) / 2), IN_Z1 - 0.0225, "white", 0)
    lamp["bk_lamp"] = True

    # top band with badge
    b.box("TopBand", (W - 0.12, 0.03, 0.20), (0, FRONT - 0.015), 1.76, c["band"], 0.012, 2)
    b.box("Badge", (0.26, 0.05, 0.09), (0, FRONT - 0.04), 1.815, c["badge"], 0.024, 3)
    b.box("BandGroove", (W - 0.14, 0.012, 0.022), (0, FRONT - 0.006), 1.735, c["groove"], 0)
    return b.bake()


def door(name, s, c):
    """One door, built relative to its hinge (x' = x - s * HINGE_X, y' = y - FRONT)."""
    b = bk_common.Builder(name)
    h = s * HINGE_X
    xc = s * (GAP / 2 + DW / 2) - h      # door centre
    b.box("Door", (DW, DT, DH), (xc, -DT / 2), DZ0, c["door"], 0.035, 3)
    b.box("DoorPanel", (DW - 0.14, 0.015, 1.30), (xc, -DT - 0.0075 + 0.0005), 0.31, c["door"], 0.007, 2)
    # handle near the centre gap
    hx = s * 0.085 - h
    b.cyl("HandleBar", 0.034, 0.80, (hx, -0.105, 0.96), "Z", c["handle"], 16, 0.015, 2)
    for j, z in enumerate([0.62, 1.30]):
        b.cyl(f"HandleStem_{j}", 0.024, 0.07, (hx, -0.07, z), "Y", c["handle"], 12, 0.006)
    # hinge knuckles on the outer edge
    for j, z in enumerate([0.36, 1.50]):
        b.cyl(f"Hinge_{j}", 0.014, 0.12, (0.0, -0.01, z), "Z", "steel_dark", 10, 0.003)
    # inner face: liner panel and bins (stay clear of the cavity rim and the shelf fronts when closed)
    b.box("InnerPanel", (0.57, 0.012, 1.40), (s * (0.08 + 0.285) - h, 0.004), 0.26, c["liner"], 0.004, 1)
    for k, z in enumerate(BINS):
        b.box(f"BinFloor_{k}", (BIN_W, 0.099, 0.012), (xc, 0.0585), z, c["liner"], 0.003, 1)
        b.box(f"BinLip_{k}", (BIN_W, 0.012, 0.07), (xc, 0.111), z, c["liner"], 0.004, 1)
    return b.bake()


BUILDS = [
    ("A4_Fridge", lambda n, c: body(n, c)),
    ("A4_FridgeDoorL", lambda n, c: door(n, -1, c)),
    ("A4_FridgeDoorR", lambda n, c: door(n, 1, c)),
]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n, VARIANTS[VARIANT])
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
