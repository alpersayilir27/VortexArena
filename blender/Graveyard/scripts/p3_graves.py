"""Part 3: old graves outside the fence — rows parallel to each fence edge (each point belongs to its nearest
edge), sparse and leaning; the approach paths and the zombie spawn clearings at the path ends stay free.
Some graves carry a lit candle (CC0 `wooden_candlestick` + flame, GY_Candles, a preview light each)."""
import importlib
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)
import gy_assets as ga
importlib.reload(ga)

L = gy.Layout()
coll = gy.collection("GY_P3_Graves")
gy.clear_collection(coll)
lights = gy.collection("GY_PreviewLights")
for o in [o for o in lights.objects if o.name.startswith("GY_Candle")]:
    bpy.data.objects.remove(o, do_unlink=True)
stones = gy.Part("GY_Graves", coll)
flames = gy.Part("GY_CandleFlames", coll)
rnd = random.Random(7)
rnd_c = random.Random(23)                # candles: own stream, the grave layout stays as it was
MAX_CANDLES = 8
candles = []                             # (venue x, z, ground h, yaw)

ROWS = (1.9, 4.2, 6.5, 8.8, 11.1)        # row distance from the fence line
STEP = 1.55                              # spacing along a row
CLEAR_PATH, CLEAR_SPAWN = 1.9, 3.4


def reserved():
    """Spots kept free for landmarks (part 4 places them at the same corners)."""
    return [Vector(p) for p in gy.landmark_spots(L)]


def edge_frame(i):
    a, b = Vector(L.edges[i][0]), Vector(L.edges[i][1])
    d = (b - a).normalized()
    return a, b, d, Vector((d.y, -d.x))


def nearest_edge(p):
    return min(range(len(L.edges)), key=lambda i: gy.seg_dist(p, *L.edges[i]))


def lean(faces, pivot_xz, h, d, fwd_deg, yaw_deg):
    """Leans the stone forward/back about the row axis and twists it about the vertical, around its foot."""
    pivot = gy.bl(pivot_xz.x, pivot_xz.y, h)
    axis = Vector((-d.x, -d.y, 0.0))
    m = (Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(yaw_deg), 4, "Z")
         @ Matrix.Rotation(math.radians(fwd_deg), 4, axis) @ Matrix.Translation(-pivot))
    stones.transform_last(faces, m)


def headstone(c, d, n, h, w, tall, t=0.1, rounded=True, mat="Grave"):
    """Upright slab in the row plane (width along d, thickness along n), optional rounded top."""
    prof = [(-w / 2, 0.0), (w / 2, 0.0)]
    if rounded:
        for k in range(7):
            a = math.pi * k / 6
            prof.append((w / 2 * math.cos(a), tall - w / 2 + w / 2 * math.sin(a)))
    else:
        prof += [(w / 2, tall), (-w / 2, tall)]
    pts = []
    for u, v in prof:
        for s in (-t / 2, t / 2):
            p = c + d * u + n * s
            pts.append(gy.bl(p.x, p.y, h + v))
    return stones.hull(pts, mat)


def cross(c, d, n, h, tall):
    f = stones.prism(square(c, d, 0.36, 0.26), h - 0.05, h + 0.12, "Sandstone")
    f += stones.prism(square(c, d, 0.1, 0.1), h + 0.12, h + tall, "Grave")
    arm_h = h + tall * 0.72
    f += stones.prism(square(c, d, 0.5, 0.09), arm_h, arm_h + 0.1, "Grave")     # thinner: no shared face plane
    return f


def obelisk(c, d, n, h, tall):
    f = stones.prism(square(c, d, 0.5, 0.5), h - 0.05, h + 0.28, "StoneWall")
    pts = [gy.bl(*p, h + 0.28) for p in square(c, d, 0.3, 0.3)] + [gy.bl(*p, h + tall) for p in square(c, d, 0.19, 0.19)]
    f += stones.hull(pts, "Sandstone")
    f += stones.hull([gy.bl(*p, h + tall) for p in square(c, d, 0.19, 0.19)] + [gy.bl(c.x, c.y, h + tall + 0.22)], "Sandstone")
    return f


def ledger(c, d, n, h):
    """Flat tomb: low chest with a slightly larger lid, long axis outward."""
    m = c + n * 0.9
    f = stones.prism(square(m, d, 0.8, 1.9), h - 0.05, h + 0.32, "StoneWall")
    f += stones.prism(square(m, d, 0.92, 2.02), h + 0.32, h + 0.4, "Sandstone")
    return f


def mound(c, n, d, h):
    """Low earth mound in front of the stone (outward), irregular convex blob."""
    m = c + n * 0.95
    pts = []
    for k in range(14):
        a = 2 * math.pi * k / 14
        r = 1 + rnd.uniform(-0.08, 0.08)
        p = m + d * (0.36 * r * math.cos(a)) + n * (0.82 * r * math.sin(a))
        pts.append(gy.bl(p.x, p.y, h - 0.04))
    for u in (-0.4, 0.0, 0.4):
        p = m + n * u
        pts.append(gy.bl(p.x, p.y, h + 0.13 + rnd.uniform(-0.03, 0.03)))
    stones.smooth = True
    stones.hull(pts, "Mud")
    stones.smooth = False


def square(c, d, w, w2=None):
    c, d = Vector(c), Vector(d).normalized()
    n = Vector((d.y, -d.x))
    hw, hn = w / 2, (w2 or w) / 2
    return [tuple(c - d * hw - n * hn), tuple(c + d * hw - n * hn), tuple(c + d * hw + n * hn), tuple(c - d * hw + n * hn)]


ends = [b for _, b, _ in L.paths]
keep_free = reserved()
count = 0
for i in range(len(L.edges)):
    a, b, d, n = edge_frame(i)
    length = (b - a).length
    for row, dist in enumerate(ROWS):
        s = -dist + rnd.uniform(0, STEP)
        while s < length + dist:
            s += STEP * rnd.uniform(0.85, 1.25)
            p = a + d * s + n * (dist + rnd.uniform(-0.25, 0.25))
            if nearest_edge(p) != i or L.fence_dist(p) < 1.5:
                continue
            if L.path_dist(p) < CLEAR_PATH or any((p - e).length < CLEAR_SPAWN for e in ends):
                continue
            if any((p - r).length < 3.2 for r in keep_free) or rnd.random() < 0.22:
                continue
            h = L.ground_h(p.x, p.y)
            kind = rnd.random()
            yaw = math.degrees(math.atan2(n.y, n.x))
            lit = row < 2 and len(candles) < MAX_CANDLES and rnd_c.random() < 0.3      # near rows only: seen from the yard
            if kind < 0.55:
                tall = rnd.uniform(0.55, 0.95)
                faces = headstone(p, d, n, h - 0.05, rnd.uniform(0.45, 0.68), tall, rounded=rnd.random() < 0.7,
                                  mat="Grave" if rnd_c.random() < 0.6 else "Sandstone")
                broken = rnd.random() < 0.12
                lean(faces, p, h, d, rnd.uniform(18, 32) * rnd.choice((-1, 1)) if broken else rnd.uniform(-8, 8),
                     rnd.uniform(-7, 7))
                mound(p, n, d, h)
                if lit and not broken:
                    q = p - n * 0.24 + d * rnd_c.uniform(-0.15, 0.15)
                    candles.append((q.x, q.y, L.ground_h(q.x, q.y) - 0.01, yaw))
            elif kind < 0.75:
                faces = cross(p, d, n, h, rnd.uniform(0.85, 1.25))
                lean(faces, p, h, d, rnd.uniform(-10, 10), rnd.uniform(-8, 8))
                mound(p, n, d, h)
            elif kind < 0.85:
                obelisk(p, d, n, h, rnd.uniform(1.3, 1.9))
            else:
                ledger(p, d, n, h)
                if lit:
                    q = p + n * 0.4 + d * rnd_c.uniform(-0.2, 0.2)
                    candles.append((q.x, q.y, h + 0.4, yaw))
            count += 1

stones.finish()

# ---------------------------------------------------------------- candles: CC0 candlestick + flame + preview light
stick = ga.load("wooden_candlestick", "Candle", tris=500)[0]
top = ga.size(stick).z
items = []
for x, z, h, yaw in candles:
    s = rnd_c.uniform(0.9, 1.3)
    items.append((stick, ga.place(stick, x, z, h, yaw, s)))
    fh = h + top * s
    flames.hull([gy.bl(x, z, fh)] + [gy.bl(x + 0.012 * math.cos(a), z + 0.012 * math.sin(a), fh + 0.02)
                                     for a in (0, 2.1, 4.2)] + [gy.bl(x, z, fh + 0.06)], "LampGlow")
    gy.preview_light("GY_Candle", gy.bl(x, z, fh + 0.08), 3.0, (1.0, 0.55, 0.22))
flames.finish()
ga.merge("GY_Candles", coll, items)
print("graves:", count, "tris:", gy.tris(bpy.data.objects["GY_Graves"]), "candles:", len(candles))
