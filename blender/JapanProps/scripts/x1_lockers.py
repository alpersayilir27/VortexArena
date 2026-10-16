# X1 coin lockers: bank of Japanese coin lockers (3 x 4 doors) on a dark plinth. Front faces -Y,
# pivot at the bottom center of the 1.4 x 0.6 footprint, z=0 floor, 1.75 m tall. The doors, numbers
# and keyholes are painted into one front decal; only the frame grid, key knobs and the hood vent
# are 3D. One obstacle box; __main__ prints it in Unity local coords.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.4, 0.6)
HEIGHT = 1.75
PLINTH = (FOOT[0], FOOT[1], 0.06)
BODY = (1.38, 0.56, 1.55)
BODY_Z = PLINTH[2]
FRONT_Y = -BODY[1] / 2                      # -0.28, body front plane
# hood is 2 cm shallower than the footprint so its vent slats can stand proud without passing it
HOOD = (FOOT[0], FOOT[1] - 0.02, 0.20)
HOOD_Z = 1.55                               # overlaps the body top: no exposed seam at the front
HOOD_FRONT = -HOOD[1] / 2
BEV = 0.01

DEC_WH = (1.30, 1.45)
DEC_Y = FRONT_Y - 0.002                     # decal 2 mm proud, it sits between body and frame
DEC_Z0, DEC_Z1 = 0.09, 1.54                 # stays clear of the hood front (1.55 up)
CELLS = (3, 4)                              # columns x rows of painted doors
# Frame bars share a center plane but differ in thickness: crossing bars would otherwise have
# coplanar same-facing faces at every junction (z-fight).
BAR_W, BAR_CY = 0.015, -0.291
VBAR_T, HBAR_T = 0.010, 0.014
KNOB_R, KNOB_T, KNOB_DX = 0.015, 0.016, 0.06   # knob inset from the right edge of its door
SLATS, SLAT_PITCH, SLAT_X = 10, 0.026, 0.20
SIGN_WH, SIGN_X = (0.14, 0.14), -0.40
PANEL_Z = (0.16, 1.45)                      # side seam lines


def bank(b):
    """Plinth, beige body, painted front sheet and the proud frame grid with its key knobs."""
    b.box("Plinth", PLINTH, (0, 0), 0.0, "metal_dark", BEV, 2)
    b.box("Body", BODY, (0, 0), BODY_Z, "locker_beige", BEV, 2)
    b.decal("FrontSheet", DEC_WH, (0.0, DEC_Y, (DEC_Z0 + DEC_Z1) / 2), "locker_front")

    cols, rows = CELLS
    cw, ch = DEC_WH[0] / cols, DEC_WH[1] / rows
    for i in range(cols + 1):
        x = -DEC_WH[0] / 2 + i * cw
        # verticals run 4 mm past the outer horizontals and those stop 3 mm inside the outer
        # verticals: end faces flush with a crossing bar would z-fight
        b.box(f"VBar{i}", (BAR_W, VBAR_T, DEC_WH[1] + BAR_W + 0.008), (x, BAR_CY), DEC_Z0 - BAR_W / 2 - 0.004,
              "locker_frame", 0.003, 1)
    for j in range(rows + 1):
        z = DEC_Z0 + j * ch
        b.box(f"HBar{j}", (DEC_WH[0] + BAR_W - 0.006, HBAR_T, BAR_W), (0, BAR_CY), z - BAR_W / 2,
              "locker_frame", 0.003, 1)
    for j in range(rows):
        for i in range(cols):
            cx = -DEC_WH[0] / 2 + (i + 0.5) * cw
            cz = DEC_Z1 - (j + 0.5) * ch
            b.cyl(f"Knob{j}{i}", KNOB_R, KNOB_T, (cx + cw / 2 - KNOB_DX, BAR_CY, cz),
                  "Y", "chrome", 12, 0.003, 1)


def hood(b):
    """Top hood with its vent slat row and the small yellow sign beside it."""
    b.box("Hood", HOOD, (0, 0), HOOD_Z, "locker_beige", BEV, 2)
    sz = HOOD_Z + HOOD[2] / 2
    for i in range(SLATS):
        x = SLAT_X + (i - (SLATS - 1) / 2) * SLAT_PITCH
        b.box(f"Slat{i}", (0.012, 0.010, 0.11), (x, HOOD_FRONT - 0.005), sz - 0.055,
              "locker_frame", 0.002, 1)
    b.decal("Sign", SIGN_WH, (SIGN_X, HOOD_FRONT - 0.002, sz), "locker_sign")


def side_lines(b):
    """Two proud seam lines per side: a stylized stand-in for the pressed panel of a locker bank."""
    for s, sx in enumerate((-1, 1)):
        for j, z in enumerate(PANEL_Z):
            b.box(f"Seam{s}{j}", (0.008, 0.46, 0.02), (sx * 0.696, 0.0), z, "locker_frame", 0.002, 1)


def build(name):
    b = jp_common.Builder(name)
    bank(b)
    hood(b)
    side_lines(b)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("X1_CoinLockers")
    print("X1_CoinLockers tris", tris, "->", jp_common.export_fbx(obj, r"Models\X1_CoinLockers.fbx"))
    # one closed block: box inset 3 cm inside the narrowest solid (the body), plinth and hood are wider
    jp_common.obstacle_inset("bank", (-BODY[0] / 2, FRONT_Y, 0.0), (BODY[0] / 2, -FRONT_Y, HEIGHT))
