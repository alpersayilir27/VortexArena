# G1 cardboard boxes: three stacked boxes plus a small one on top, taped and labelled. Front faces
# -Y, pivot at the bottom center of the 1.0 x 0.7 footprint, z=0 floor, 1.59 m tall.
# The bottom box is 3 mm under the footprint on each side so its tape and labels can stand proud.
# One box collider; __main__ prints it.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.0, 0.7)
PROUD = 0.003                   # tape stands this far off the cardboard (>= ZF_GAP)
LBL = 0.0025                    # label plane offset
BEV = 0.006
# (prefix, size, z0, yaw degrees, label key, label size)
STACK = [
    ("Bot", (FOOT[0] - 2 * PROUD, FOOT[1] - 2 * PROUD, 0.55), 0.00, 0.0, "label_ship", (0.26, 0.18)),
    ("Mid", (0.90, 0.62, 0.50), 0.55, 4.0, "label_fragile", (0.20, 0.20)),
    ("Top", (0.78, 0.58, 0.42), 1.05, -6.0, "label_updown", (0.14, 0.14)),
    ("Sml", (0.40, 0.32, 0.12), 1.47, 12.0, None, None),
]


def taped_box(b, prefix, size, z0, label, label_size):
    """Cardboard box with a tape cross over the lid, a strip down the front and its label."""
    sx, sy, sz = size
    b.box(f"{prefix}_Box", size, (0, 0), z0, "tape", BEV, 1, tile="Cardboard")
    top = z0 + sz
    # the two lid strips cross, so they sit at different heights: coplanar tape would flicker
    b.box(f"{prefix}_TapeY", (0.08, sy + 2 * PROUD, 2 * PROUD), (0, 0), top - PROUD, "tape", 0.002, 1)
    b.box(f"{prefix}_TapeX", (sx + 2 * PROUD, 0.07, 4 * PROUD), (0, 0), top - 2 * PROUD, "tape", 0.002, 1)
    b.box(f"{prefix}_TapeFront", (0.08, 2 * PROUD, min(0.16, sz * 0.6)), (0, -(sy / 2 + PROUD)),
          top - min(0.16, sz * 0.6), "tape", 0.002, 1)
    if label:
        # off center in x: the front tape strip runs down the middle
        b.decal(f"{prefix}_Label", label_size, (-0.2, -(sy / 2 + LBL), z0 + sz * 0.55), label)


def build(name):
    b = jp_common.Builder(name)
    for prefix, size, z0, yaw, label, label_size in STACK:
        taped_box(b, prefix, size, z0, label, label_size)
        if yaw:
            b.transform_parts(prefix, math.radians(yaw), (0, 0, 0), (0, 0))
    # brand label on the +X side of the bottom box (that side is open to the alley)
    bot = STACK[0][1]
    b.decal("BotBrand", (0.18, 0.12), (bot[0] / 2 + LBL, 0.0, 0.34), "label_brand", yaw=math.pi / 2)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("G1_CardboardBoxes")
    print("G1_CardboardBoxes tris", tris, "->", jp_common.export_fbx(obj, r"Models\G1_CardboardBoxes.fbx"))
    h = STACK[-1][2] + STACK[-1][1][2] + PROUD
    print("COLLIDER stack center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (h / 2, FOOT[0], FOOT[1], h))
