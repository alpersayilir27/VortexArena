# X3 rice sacks: pillow-shaped rice sacks and two wooden crates stacked on a pallet and strapped
# with rope. Front faces -Y, pivot at the bottom center of the 1.4 x 0.6 footprint, z=0 floor,
# ~1.52 m tall. Jitter is seeded, so the stack is identical on every re-export. One obstacle box
# for the stack (the pallet is an open frame and gets none); __main__ prints it in Unity local coords.
import importlib, math, random, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (1.4, 0.6)
PALLET = (1.38, 0.58, 0.12)
RUNNER = (0.10, 0.58, 0.07)
RUNNER_X = (-0.60, 0.0, 0.60)
BOARDS, BOARD = 5, (1.38, 0.09, 0.05)

# Sacks are thicker than a real 30 kg sack on purpose: 6 layers have to reach cover height, and the
# large bevel eats the flat front the rice print needs.
SACK = (0.62, 0.36, 0.24)
SACK_BEV, SACK_SEG = 0.06, 3
CRATE = (0.66, 0.36, 0.22)             # as deep as a sack: the straps must pass in front of the crates too
LAYERS, CRATE_LAYERS = 6, (3, 6)        # 1-based layer indices built as crates instead of sacks
SACK_GAP, CRATE_GAP = 0.005, 0.002      # half gap between the two parts of a layer
RICE_SACK_WH, RICE_CRATE_WH = (0.24, 0.11), (0.34, 0.17)
JIT_DX, JIT_DY, JIT_YAW = 0.015, 0.010, 0.04
CJIT_DX, CJIT_YAW = 0.008, 0.03
SEED = 1979

# Straps stand clear of the most forward jittered sack face, so they read as rope over the stack.
ROPE_X = (-0.34, 0.34)
ROPE_W, ROPE_T, ROPE_Y = 0.05, 0.014, 0.209
ROPE_TOP_W, ROPE_TOP_HALF_Y = 0.046, 0.213   # narrower than the uprights: no coplanar side faces
BEV = 0.004


def pallet(b):
    """Three runners under five top boards; board bottoms sit back to back on the runner tops."""
    for i, x in enumerate(RUNNER_X):
        b.box(f"Runner{i}", RUNNER, (x, 0.0), 0.0, "wood_dark", BEV, 1, tile="Wood")
    span = PALLET[1] - BOARD[1]
    for i in range(BOARDS):
        y = -span / 2 + span * i / (BOARDS - 1)
        b.box(f"Board{i}", BOARD, (0.0, y), RUNNER[2], "wood_dark", BEV, 1, tile="Wood")


def layer(b, idx, z0, rnd):
    """One stack layer: two sacks or two crates side by side, each with the rice print on its front."""
    crate = idx in CRATE_LAYERS
    size = CRATE if crate else SACK
    gap = CRATE_GAP if crate else SACK_GAP
    dec_wh = RICE_CRATE_WH if crate else RICE_SACK_WH
    for s, sx in enumerate((-1, 1)):
        cx = sx * (size[0] / 2 + gap)
        cy = 0.0
        if crate:
            cx += rnd.uniform(-CJIT_DX, CJIT_DX)
            yaw = rnd.uniform(-CJIT_YAW, CJIT_YAW)
        else:
            cx += rnd.uniform(-JIT_DX, JIT_DX)
            cy += rnd.uniform(-JIT_DY, JIT_DY)
            yaw = rnd.uniform(-JIT_YAW, JIT_YAW)
        tag = f"{'Crate' if crate else 'Sack'}{idx}{s}"
        if crate:
            b.box(tag, size, (cx, cy), z0, "wood_dark", 0.012, 2, yaw=yaw, tile="Wood")
        else:
            b.box(tag, size, (cx, cy), z0, "sack_white", SACK_BEV, SACK_SEG, yaw=yaw)
        # print rides on the front face, so its offset turns with the part
        d = size[1] / 2 + 0.002
        b.decal(f"{tag}_Print", dec_wh,
                (cx + d * math.sin(yaw), cy - d * math.cos(yaw), z0 + size[2] / 2),
                "rice_print", yaw=yaw)
    return z0 + size[2]


def straps(b, top):
    """Two rope straps over front, top and back of the whole stack."""
    z0 = 0.10
    for i, x in enumerate(ROPE_X):
        h = top + 0.005 - z0
        for s, sy in enumerate((-1, 1)):
            b.box(f"Rope{i}Side{s}", (ROPE_W, ROPE_T, h), (x, sy * ROPE_Y), z0, "rope", BEV, 1)
        b.box(f"Rope{i}Top", (ROPE_TOP_W, 2 * ROPE_TOP_HALF_Y, 0.012), (x, 0.0), top + 0.003,
              "rope", BEV, 1)


def build(name):
    b = jp_common.Builder(name)
    rnd = random.Random(SEED)
    pallet(b)
    z = PALLET[2]
    for idx in range(1, LAYERS + 1):
        z = layer(b, idx, z, rnd)
    straps(b, z)
    return b.bake()


def stack_top():
    return PALLET[2] + sum((CRATE if i in CRATE_LAYERS else SACK)[2] for i in range(1, LAYERS + 1))


if __name__ == "__main__":
    obj, tris = build("X3_RiceSacks")
    print("X3_RiceSacks tris", tris, "->", jp_common.export_fbx(obj, r"Models\X3_RiceSacks.fbx"))
    # one box per sack column (the 1 cm seam between the two columns stays open, a single wide box
    # there would stop bullets in mid air); the pallet is an open frame and gets no box at all
    top = stack_top()
    inner = SACK_GAP + JIT_DX                        # worst-case edges of a jittered sack column
    outer = SACK[0] + SACK_GAP - JIT_DX
    for s, sx in enumerate((-1, 1)):
        lo_x, hi_x = sorted((sx * inner, sx * outer))
        jp_common.obstacle_inset(f"stack{s}", (lo_x, -SACK[1] / 2, PALLET[2]),
                                 (hi_x, SACK[1] / 2, top))
