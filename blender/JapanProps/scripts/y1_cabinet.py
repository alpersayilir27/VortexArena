# Y1 electrical cabinet: grey utility cabinet on a concrete base. Front (door side) faces -Y,
# pivot at the bottom center of the 1.0 x 0.7 footprint, z=0 floor, 1.6 m tall. Nothing passes the
# footprint, so every proud detail stays inside y >= -0.35 / |x| <= 0.5. One obstacle box;
# __main__ prints it in Unity local coords.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.0, 0.7)
HEIGHT = 1.6
BASE = (0.98, 0.68, 0.14)
BODY = (0.94, 0.62, 1.40)
BODY_Z = BASE[2]                            # body top 1.54, under the roof slab
FRONT_Y = -BODY[1] / 2                      # -0.31
SIDE_X = BODY[0] / 2                        # 0.47
FRONT_LIMIT = -FOOT[1] / 2                  # -0.35, no part may pass this
BEV = 0.01

# Roof: flat underside sunk 2 cm into the body top (no exposed seam), top sloping to the back.
ROOF_X, ROOF_Y = 0.49, 0.34
ROOF_BOT, ROOF_FRONT, ROOF_BACK = 1.52, 1.56, HEIGHT

DOOR = (0.45, 0.02, 1.22)
DOOR_X, DOOR_Z = 0.245, 0.20
DOOR_CY = FRONT_Y - 0.003 - DOOR[1] / 2     # panels 3 mm proud of the body face
DOOR_FRONT = DOOR_CY - DOOR[1] / 2
GAP_T = 0.014                               # centre gap strip, back face sits on the body face

HANDLE = (0.022, 0.015, 0.20)
HANDLE_X, HANDLE_Z = 0.06, 0.74
LOCK_Z = 0.70
# Louvres: slats tilt about X, so their proud depth is (t*cos + h*sin) / 2
LOUV, LOUV_TILT, LOUV_N, LOUV_PITCH = (0.30, 0.012, 0.03), 25.0, 5, 0.045
LOUV_Z0, LOUV_SIDE_Z0 = 0.26, 0.80

PIPE_R, PIPE_Y = 0.025, (-0.20, -0.08)
PIPE_X = -SIDE_X - 0.002 - PIPE_R           # 2 mm off the side face
PIPE_Z0, PIPE_Z1 = 0.10, 1.26

WARN_WH, WARN_Z = (0.20, 0.20), 1.15
POSTER_WH, POSTER_Y, POSTER_Z = (0.22, 0.44), 0.14, 0.95
POSTER_OFF = 0.014                          # clears the 3 deg tilt (0.22 * sin3 = 11.5 mm)


def shell(b):
    """Concrete base, grey body and the sloped roof cap."""
    b.box("Base", BASE, (0, 0), 0.0, "concrete", BEV, 2)
    b.box("Body", BODY, (0, 0), BODY_Z, "metal_grey", BEV, 2)
    pts = []
    for sx in (-1, 1):
        pts += [(sx * ROOF_X, -ROOF_Y, ROOF_BOT), (sx * ROOF_X, -ROOF_Y, ROOF_FRONT),
                (sx * ROOF_X, ROOF_Y, ROOF_BOT), (sx * ROOF_X, ROOF_Y, ROOF_BACK)]
    b.hull("Roof", pts, "metal_grey", 0.004, 1)


def doors(b):
    """Two proud door panels, the dark centre gap, handles, padlock and the louvre rows."""
    for s, sx in enumerate((-1, 1)):
        b.box(f"Door{s}", DOOR, (sx * DOOR_X, DOOR_CY), DOOR_Z, "metal_grey", 0.006, 1)
    b.box("GapStrip", (0.02, GAP_T, DOOR[2] + 0.02), (0.0, FRONT_Y - GAP_T / 2), DOOR_Z - 0.01,
          "metal_dark", 0.003, 1)

    for s, sx in enumerate((-1, 1)):
        b.box(f"Handle{s}", HANDLE, (sx * HANDLE_X, DOOR_FRONT - HANDLE[1] / 2), HANDLE_Z,
              "chrome", 0.004, 1)
    padlock(b)

    sw, st, sh = LOUV
    depth = (st * math.cos(math.radians(LOUV_TILT)) + sh * math.sin(math.radians(LOUV_TILT))) / 2
    cy = FRONT_LIMIT + depth + 0.002
    for s, sx in enumerate((-1, 1)):
        for i in range(LOUV_N):
            b.box(f"Louv{s}{i}", LOUV, (sx * DOOR_X, cy), LOUV_Z0 + i * LOUV_PITCH - sh / 2,
                  "metal_dark", 0.002, 1, rot_x=LOUV_TILT)
    # one side vent on the right: the left side is taken by the conduits
    cx = SIDE_X + depth - 0.006          # slat inner edge stays buried in the body face
    for i in range(LOUV_N):
        b.box(f"LouvSide{i}", (st, sw, sh), (cx, 0.0), LOUV_SIDE_Z0 + i * LOUV_PITCH - sh / 2,
              "metal_dark", 0.002, 1, rot_y=LOUV_TILT)


def padlock(b):
    """Padlock on the centre gap: body plus a three-box shackle, thicknesses alternate at the joints."""
    cy = FRONT_LIMIT + 0.002 + 0.014     # lock front stays 2 mm inside the footprint
    b.box("Lock", (0.05, 0.028, 0.06), (0.0, cy), LOCK_Z, "chrome", 0.004, 1)
    for s, sx in enumerate((-1, 1)):
        b.box(f"Shackle{s}", (0.008, 0.010, 0.05), (sx * 0.016, cy), LOCK_Z + 0.06, "chrome", 0.002, 1)
    b.box("ShackleTop", (0.036, 0.014, 0.008), (0.0, cy), LOCK_Z + 0.105, "chrome", 0.002, 1)


def conduits(b):
    """Two cable pipes up the left side, each bending into the cabinet near the top."""
    for i, y in enumerate(PIPE_Y):
        b.cyl(f"Pipe{i}", PIPE_R, PIPE_Z1 - PIPE_Z0, (PIPE_X, y, (PIPE_Z0 + PIPE_Z1) / 2), "Z",
              "metal_dark", 12)
        # elbow cap hides inside the vertical pipe, its other end inside the body
        b.cyl(f"Elbow{i}", PIPE_R, 0.08, (PIPE_X + 0.04, y, PIPE_Z1), "X", "metal_dark", 12)


def build(name):
    b = jp_common.Builder(name)
    shell(b)
    doors(b)
    conduits(b)
    b.decal("WarnHV", WARN_WH, (DOOR_X, DOOR_FRONT - 0.002, WARN_Z), "warn_hv")
    # tilt reads as a peeling poster: in-plane roll is not expressible for a side-facing decal
    b.decal("Poster", POSTER_WH, (-SIDE_X - POSTER_OFF, POSTER_Y, POSTER_Z), "poster_a",
            yaw=-math.pi / 2, tilt=math.radians(3))
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("Y1_ElecCabinet")
    print("Y1_ElecCabinet tris", tris, "->", jp_common.export_fbx(obj, r"Models\Y1_ElecCabinet.fbx"))
    # one closed block: inset 3 cm inside the body cross-section (base and roof are wider). Top
    # measured from the LOW edge of the sloped roof, not from HEIGHT, or the box leaves the slab
    jp_common.obstacle_inset("cabinet", (-BODY[0] / 2, FRONT_Y, 0.0),
                             (BODY[0] / 2, -FRONT_Y, ROOF_FRONT))
