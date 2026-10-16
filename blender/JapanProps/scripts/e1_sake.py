# E1 sake barrels: three straw-wrapped komodaru, two side by side with the third nested in the
# groove on top. Front faces -Y, pivot at the bottom center of the 1.32 x 0.6 footprint, z=0 floor,
# ~1.23 m tall. The barrels interpenetrate on purpose; no two faces of them end up coplanar.
# One box collider for the whole stack; __main__ prints it.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.32, 0.6)
BAR_R, BAR_H = 0.30, 0.62
BAR_X = 0.36                    # bottom barrel centers; BAR_X + BAR_R = half the footprint
TOP_Z = 0.60                    # lower than BAR_H: the top barrel sits down in the groove
SEGS = 24
# Profile as fractions of (BAR_R, BAR_H): gentle bulge, flat straw-bound ends
BAR_FR = [(0.00, 0.00), (0.90, 0.00), (0.95, 0.05), (1.00, 0.50), (0.95, 0.95), (0.90, 1.00), (0.00, 1.00)]
END_R = BAR_R * 0.90            # radius of the flat top/bottom disc
ROPE_FZ = (0.12, 0.88)          # rope ring heights as fractions of BAR_H
ROPE_R = BAR_R * 0.991          # ~1 cm proud of the body at the ring heights
LID_UP = 0.0025                 # lid decal stands off the top disc (>= ZF_GAP)
FACE_UP = -math.pi / 2          # tilt that lays a decal face up


def barrel(b, prefix, cx, z0, wrap, lid_xy, lid_size, turn=0.0):
    """One komodaru: wrapped lathe body, two rope rings and the lid decal above its top disc.
    The printed brand faces the front (-Y); turn (radians) swings it off-axis for variety."""
    prof = [(BAR_R * fr, z0 + BAR_H * fz) for fr, fz in BAR_FR]
    b.lathe(f"{prefix}_Body", prof, (cx, 0.0), "rope", SEGS, wrap_key=wrap, phase=math.pi / 2 + turn)
    for i, fz in enumerate(ROPE_FZ):
        b.cyl(f"{prefix}_Rope{i}", ROPE_R, 0.035, (cx, 0.0, z0 + BAR_H * fz), "Z", "rope", SEGS)
    # round lid art: corner radius = half the size makes the quad a disc, so the grey corners never show
    b.decal(f"{prefix}_Lid", (lid_size, lid_size), (lid_xy[0], lid_xy[1], z0 + BAR_H + LID_UP),
            "komo_lid", tilt=FACE_UP, corner=lid_size * 0.49, segs=6)


def build(name):
    b = jp_common.Builder(name)
    for i, sx in enumerate((-1, 1)):
        # lid decal pushed outboard so the top barrel does not cut through it
        barrel(b, f"Low{i}", sx * BAR_X, 0.0, ("komo_a", "komo_b")[i], (sx * (BAR_X + 0.10), 0.0), 0.26,
               turn=sx * 0.25)
    barrel(b, "Top", 0.0, TOP_Z, "komo_c", (0.0, 0.0), 0.42)
    # rope loop resting on the top lid, clear of the lid decal plane
    b.cyl("Knot", 0.055, 0.015, (0.12, 0.12, TOP_Z + BAR_H + 0.0125), "Z", "rope", 12, 0.004, 1)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("E1_SakeBarrels")
    print("E1_SakeBarrels tris", tris, "->", jp_common.export_fbx(obj, r"Models\E1_SakeBarrels.fbx"))
    h = TOP_Z + BAR_H + 0.01
    print("COLLIDER stack center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (h / 2, FOOT[0], FOOT[1], h))
