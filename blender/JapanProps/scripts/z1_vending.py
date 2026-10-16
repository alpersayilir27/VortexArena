# Z1 hot-food vending machine (udon / soba): closed opaque block, it wraps a structural column of
# the venue. Front faces -Y, pivot at the bottom center of the 1.04 x 0.81 footprint, z=0 floor,
# 2.0 m tall. Buttons, header text and food pictures are painted into one front decal; only the
# coin plate, return cup, serving-door frame and the stripes are 3D. One obstacle box; __main__
# prints it in Unity local coords.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.04, 0.81)
HEIGHT = 2.0
BODY = (1.02, 0.79, 1.92)
FRONT_Y = -BODY[1] / 2                  # -0.395
SIDE_X = BODY[0] / 2                    # 0.51
BEV = 0.012

HEADER = (1.03, 0.80, 0.18)
HEADER_Z = 1.72                         # top at 1.90, 2 cm under the body top: no flush top faces
# Rain hood: flat underside on the header top (back to back), top sloping to the back.
HOOD_X, HOOD_Y = 0.512, 0.398
HOOD_BOT, HOOD_FRONT, HOOD_BACK = 1.90, 1.96, HEIGHT

# The body is only 1 cm shallower than the footprint, so every proud detail lives in the 6 mm band
# in front of the decal. Two depth levels, 2 mm apart on BOTH faces: crossing parts at the same
# depth would z-fight, and a detail flush with the decal plane would z-fight with the decal.
DEC_WH = (0.92, 1.58)                   # fits between kick plate and header
DEC_Y = FRONT_Y - 0.002
DEC_Z0, DEC_Z1 = 0.12, 1.70
DET1_T, DET2_T = 0.006, 0.002
DET1_Y = -FOOT[1] / 2 + DET1_T / 2      # outer level: front face on the footprint edge
DET2_Y = DET1_Y                         # inner level: 2 mm behind on both faces

KICK = (1.00, 0.12)                     # width, height
COIN_X, COIN_PLATE, COIN_PLATE_Z = 0.40, (0.11, 0.26), 1.20
CUP, CUP_Z = (0.09, 0.06), 1.08
SERVE_C, SERVE_WH = (-0.22, 0.55), (0.42, 0.34)     # painted serving door centre / opening
SERVE_BAR = 0.03
STRIPE, STRIPE_Z = (0.70, 0.10), 1.20


def shell(b):
    """Red body, cream header crown and the sloped rain hood."""
    b.box("Body", BODY, (0, 0), 0.0, "vend_red", BEV, 2)
    b.box("Header", HEADER, (0, 0), HEADER_Z, "vend_cream", BEV, 2)
    pts = []
    for sx in (-1, 1):
        pts += [(sx * HOOD_X, -HOOD_Y, HOOD_BOT), (sx * HOOD_X, -HOOD_Y, HOOD_FRONT),
                (sx * HOOD_X, HOOD_Y, HOOD_BOT), (sx * HOOD_X, HOOD_Y, HOOD_BACK)]
    b.hull("Hood", pts, "vend_cream", 0.004, 1)


def front(b):
    """Painted front sheet plus the kick plate. Coin slot, return cup and serving door are painted in
    the art: 3D copies would sit next to the painted ones and read as doubles."""
    b.decal("FrontSheet", DEC_WH, (0.0, DEC_Y, (DEC_Z0 + DEC_Z1) / 2), "vend_front")
    b.box("KickPlate", (KICK[0], DET1_T, KICK[1]), (0.0, DET1_Y), 0.0, "metal_dark", 0.003, 1)


def sides(b):
    """A cream stripe on each red side panel, 2 mm proud."""
    for s, sx in enumerate((-1, 1)):
        b.box(f"Stripe{s}", (0.004, STRIPE[0], STRIPE[1]), (sx * (SIDE_X + 0.004), 0.0), STRIPE_Z,
              "vend_cream", 0.002, 1)


def build(name):
    b = jp_common.Builder(name)
    shell(b)
    front(b)
    sides(b)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("Z1_FoodVending")
    print("Z1_FoodVending tris", tris, "->", jp_common.export_fbx(obj, r"Models\Z1_FoodVending.fbx"))
    # one closed block: inset 3 cm inside the body (header and hood sit above the body top)
    jp_common.obstacle_inset("machine", (-SIDE_X, FRONT_Y, 0.0), (SIDE_X, -FRONT_Y, BODY[2]))
