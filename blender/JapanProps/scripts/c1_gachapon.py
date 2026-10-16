# C1 gachapon: bank of capsule-toy machines (2 rows x 4) on a dark plinth. Front faces -Y, pivot at
# the bottom center of the 1.4 x 0.6 footprint, z=0 floor, 1.48 m tall. Capsules are painted into
# the window decal (no transparency on Quest). One box collider; __main__ prints it.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.4, 0.6)
PLINTH_H = 0.16
MACH = (0.34, 0.55, 0.66)       # one machine: width, depth, height
COLS, ROWS, GAP = 4, 2, 0.01
FRONT_Y = -0.26                 # machine front plane; the 4 cm left in front holds knob and slot
BEV = 0.008
KNOB_U, KNOB_V = 0.62, 0.40     # painted handle centre in the panel art (from left / from top)

BODY_COLS = ("gacha_blue", "gacha_red", "gacha_yellow", "gacha_white")
PANELS = ("gacha_p1", "gacha_p2", "gacha_p3", "gacha_p4")
WINDOWS = ("gacha_w1", "gacha_w2")


def machine(b, prefix, cx, z0, color, panel, window):
    """One machine: coloured body, front panel + window decals, chrome knob, coin slot, top cap."""
    cy = FRONT_Y + MACH[1] / 2
    b.box(f"{prefix}_Body", MACH, (cx, cy), z0, color, BEV, 2)
    # cap is 5 mm narrower than the body (flush x faces would z-fight) and proud at the front as a lip
    # cap top stands 5 mm over the body top: a flush top would z-fight
    b.box(f"{prefix}_Cap", (MACH[0] - 0.01, 0.58, 0.03), (cx, 0.005), z0 + MACH[2] - 0.025, "gacha_trim", BEV, 1)
    # trim band across the front; its back face is back to back with the body front
    b.box(f"{prefix}_Band", (MACH[0] - 0.01, 0.03, 0.04), (cx, FRONT_Y - 0.015), z0 + 0.30,
          "gacha_trim", 0.004, 1)
    # coin slot and handle are painted in the panel art; the chrome disc sits on the painted handle
    pw, ph, pz = 0.30, 0.26, z0 + 0.16
    b.cyl(f"{prefix}_Knob", 0.045, 0.02, (cx - pw / 2 + KNOB_U * pw, FRONT_Y - 0.01, pz + ph / 2 - KNOB_V * ph),
          "Y", "keg_steel", 16, 0.004, 1)
    b.decal(f"{prefix}_Panel", (pw, ph), (cx, FRONT_Y - 0.003, pz), panel)
    b.decal(f"{prefix}_Window", (0.30, 0.19), (cx, FRONT_Y - 0.003, z0 + 0.535), window)


def build(name):
    b = jp_common.Builder(name)
    b.box("Plinth", (FOOT[0], FOOT[1], PLINTH_H), (0, 0), 0.0, "gacha_trim", BEV, 2)
    # one dark sheet over the backs: in the layout the bank is often seen from behind
    back_y0 = FRONT_Y + MACH[1]
    b.box("BackSheet", (COLS * MACH[0] + (COLS - 1) * GAP - 0.006, FOOT[1] / 2 - back_y0 + 0.004,
                        ROWS * MACH[2] - 0.006), (0, (back_y0 + FOOT[1] / 2) / 2 - 0.002), PLINTH_H + 0.003,
          "gacha_trim", 0.004, 1)
    step = MACH[0] + GAP
    x0 = -(COLS * MACH[0] + (COLS - 1) * GAP) / 2 + MACH[0] / 2
    for row in range(ROWS):
        for col in range(COLS):
            ci = (col + 3 * row) % len(BODY_COLS)   # panel art is painted per body colour: same index
            machine(b, f"M{row}{col}", x0 + col * step, PLINTH_H + row * MACH[2],
                    BODY_COLS[ci], PANELS[ci], WINDOWS[(col + row) % len(WINDOWS)])
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("C1_Gachapon")
    print("C1_Gachapon tris", tris, "->", jp_common.export_fbx(obj, r"Models\C1_Gachapon.fbx"))
    h = PLINTH_H + ROWS * MACH[2]
    print("COLLIDER bank center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (h / 2, FOOT[0], FOOT[1], h))
