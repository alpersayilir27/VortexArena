"""Part 2: wrought-iron fence on a stone plinth along the fence ring, masonry pillars (corners, every
FENCE_MAX_SPAN, wrapped around columns on the fence line), four gates (open leaves, iron arch, pillar lanterns)
and masonry pillars with a lantern around the columns inside the yard. Lanterns also become preview lights.

The lanterns are their own object (GY_Lanterns): the light sits inside the glass, so in Unity that renderer must
not cast shadows or it blocks its own baked light."""
import importlib
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)

L = gy.Layout()
coll = gy.collection("GY_P2_Fence")
gy.clear_collection(coll)
lights = gy.collection("GY_PreviewLights")
gy.clear_collection(lights)

iron = gy.Part("GY_Fence", coll)
masonry = gy.Part("GY_Pillars", coll)
gates = gy.Part("GY_Gates", coll)
lanterns = gy.Part("GY_Lanterns", coll)

PLINTH_W, PLINTH_H = 0.34, 0.14
BAR, BAR_STEP = 0.022, 0.125
TOP = 2.0                       # bar tops (spear tips sit above)


def square(c, d, w, w2=None):
    """Venue-space rectangle centred on c, sides along d (w) and its normal (w2)."""
    c, d = Vector(c), Vector(d).normalized()
    n = Vector((d.y, -d.x))
    hw, hn = w / 2, (w2 or w) / 2
    return [tuple(c - d * hw - n * hn), tuple(c + d * hw - n * hn), tuple(c + d * hw + n * hn), tuple(c - d * hw + n * hn)]


def spear(part, c, h, base=0.045, tip=0.13):
    x, z = c
    pts = [gy.bl(x + dx, z + dz, h) for dx, dz in ((-base / 2, -base / 2), (base / 2, -base / 2),
                                                   (base / 2, base / 2), (-base / 2, base / 2))]
    pts.append(gy.bl(x, z, h + tip))
    part.hull(pts, "CastIron")


def pillar(c, d, w, h, cap_lantern=False):
    masonry.prism(square(c, d, w), -0.05, h, "Masonry")
    masonry.prism(square(c, d, w + 0.09), h, h + 0.07, "Sandstone")
    masonry.prism(square(c, d, w - 0.02), h + 0.07, h + 0.12, "Sandstone")
    x, z = c
    if cap_lantern:
        lantern(x, z, h + 0.12, d)
    else:
        masonry.hull([gy.bl(*p, h + 0.12) for p in square(c, d, w - 0.12)] + [gy.bl(x, z, h + 0.3)], "Sandstone")


def lantern(x, z, h, d):
    """Iron lantern: base plate, four posts, glowing glass, pyramid roof, finial. Registers a preview light."""
    lanterns.prism(square((x, z), d, 0.2), h, h + 0.03, "CastIron")
    for sx in (-1, 1):
        for sz in (-1, 1):
            p = Vector((x, z)) + Vector(d).normalized() * (0.085 * sx) + Vector((d[1], -d[0])).normalized() * (0.085 * sz)
            lanterns.prism(square(p, d, 0.018), h + 0.03, h + 0.3, "CastIron")
    lanterns.prism(square((x, z), d, 0.13), h + 0.04, h + 0.27, "LampGlow")
    lanterns.prism(square((x, z), d, 0.22), h + 0.3, h + 0.32, "CastIron")
    lanterns.hull([gy.bl(*p, h + 0.32) for p in square((x, z), d, 0.22)] + [gy.bl(x, z, h + 0.45)], "CastIron")
    lanterns.cyl(x, z, h + 0.44, h + 0.5, 0.012, "CastIron", segs=6)
    lanterns.lathe(x, z, [(h + 0.5, 0.008), (h + 0.52, 0.025), (h + 0.55, 0.004)], "CastIron", segs=8)
    gy.preview_light("GY_Lantern", gy.bl(x, z, h + 0.16), 25.0)


def panel(a, d, s0, s1):
    """Iron panel between two pillar faces at s0..s1 along the edge starting at a with direction d."""
    p0, p1 = a + d * s0, a + d * s1
    for h0, h1 in ((0.24, 0.28), (1.70, 1.74), (1.86, 1.90)):
        iron.seg_box(p0, p1, 0.03, h0, h1, "CastIron")
    span = s1 - s0
    k = max(1, int(round((span - 0.12) / BAR_STEP)))
    for j in range(k + 1):
        c = a + d * (s0 + 0.06 + (span - 0.12) * j / k)
        iron.prism(square(c, d, BAR), PLINTH_H, TOP, "CastIron")
        spear(iron, tuple(c), TOP)


# ---------------------------------------------------------------- fence edges
for i, (a, b) in enumerate(L.edges):
    a, b = Vector(a), Vector(b)
    length = (b - a).length
    d = (b - a) / length
    busy = [(-gy.PILLAR_W / 2, gy.PILLAR_W / 2, "corner")]           # corner pillar at a (b's is the next edge's)
    for _, ring, _ in L.edge_cols:
        c = gy.centroid(ring)
        if gy.seg_dist(c, a, b) <= max((Vector(p) - c).length for p in ring) + gy.PILLAR_W:
            ss = [(Vector(p) - a).dot(d) for p in ring]
            busy.append((min(ss) - 0.05, max(ss) + 0.05, "column"))
    opening = None
    for g in L.gate_on_edge(i):
        s = g[2]
        o0, o1 = s - gy.GATE_W / 2, s + gy.GATE_W / 2
        opening = (o0, o1)
        busy += [(o0 - gy.GATE_PILLAR_W, o0, "gate"), (o0, o1, "opening"), (o1, o1 + gy.GATE_PILLAR_W, "gate")]
    busy.append((length - gy.PILLAR_W / 2, length + gy.PILLAR_W / 2, "corner_end"))
    busy.sort()

    # plinth everywhere except the gate opening; its ends stop inside the pillars (a flush end face z-fights)
    cuts = [(gy.PILLAR_W / 2 - 0.01, length - gy.PILLAR_W / 2 + 0.01)]   # short: neighbours meet at odd corners
    if opening:
        cuts = [(cuts[0][0], opening[0] - 0.05), (opening[1] + 0.05, cuts[0][1])]
    for s0, s1 in cuts:
        masonry.seg_box(a + d * s0, a + d * s1, PLINTH_W, -0.05, PLINTH_H, "Sandstone")

    for s0, s1, kind in busy:
        c = a + d * ((s0 + s1) / 2)
        if kind == "corner":
            pillar(tuple(c), d, gy.PILLAR_W, gy.PILLAR_H)
        elif kind == "gate":
            pillar(tuple(c), d, gy.GATE_PILLAR_W, gy.GATE_PILLAR_H, cap_lantern=True)
    # free spans between busy intervals -> panels with intermediate pillars
    for (_, e0, k0), (s1, _, k1) in zip(busy[:-1], busy[1:]):
        if k0 == "opening" or k1 == "opening" or s1 - e0 < 0.05:
            continue
        span = s1 - e0
        n = max(1, int(math.ceil((span + gy.PILLAR_W) / (gy.FENCE_MAX_SPAN + gy.PILLAR_W))))
        seg = (span - (n - 1) * gy.PILLAR_W) / n
        cur = e0
        for k in range(n):
            panel(a, d, cur, cur + seg)
            cur += seg
            if k < n - 1:
                pillar(tuple(a + d * (cur + gy.PILLAR_W / 2)), d, gy.PILLAR_W, gy.PILLAR_H)
                cur += gy.PILLAR_W

# column wraps on the fence line: brick around the measured footprint
for name, ring, h in L.edge_cols + L.inner_cols:
    r = ring if gy.signed_area(ring) > 0 else list(reversed(ring))
    top = gy.PILLAR_H + 0.15 if (name, ring, h) in L.edge_cols else 2.45
    masonry.prism(gy.offset_ring(r, 0.05), -0.05, top, "Masonry")
    masonry.prism(gy.offset_ring(r, 0.1), top, top + 0.08, "Sandstone")
    c = gy.centroid(r)
    if (name, ring, h) in L.inner_cols:
        lantern(c.x, c.y, top + 0.08, Vector((1, 0)))
    else:
        masonry.hull([gy.bl(p[0], p[1], top + 0.08) for p in gy.offset_ring(r, 0.02)] + [gy.bl(c.x, c.y, top + 0.3)],
                     "Sandstone")


# ---------------------------------------------------------------- gates: open leaves + arch
def leaf(hinge, e, n_out, width, sag_deg):
    """One gate leaf from the hinge along venue direction e; sag tilts its free end down."""
    start = len(gates.bm.faces)
    p0, p1 = hinge + e * 0.01, hinge + e * (width - 0.01)                # rail ends inside the end uprights
    for h0, h1 in ((0.10, 0.15), (0.95, 0.99), (1.80, 1.85)):
        gates.seg_box(p0, p1, 0.035, h0, h1, "CastIron")
    for u in (0.02, width - 0.02):
        gates.prism(square(hinge + e * u, e, 0.04), 0.06, 1.95, "CastIron")
    k = int(width / 0.13)
    for j in range(1, k):
        c = hinge + e * (width * j / k)
        gates.prism(square(c, e, BAR), 0.11, 1.95, "CastIron")             # foot inside the bottom rail
        spear(gates, tuple(c), 1.95, tip=0.12)
    if sag_deg:
        gates.bm.faces.ensure_lookup_table()
        faces = [gates.bm.faces[i] for i in range(start, len(gates.bm.faces))]
        axis_dir = Vector((-(n_out.x), -(n_out.y), 0))                  # horizontal, across the leaf
        pivot = gy.bl(hinge.x, hinge.y, 1.85)
        m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(sag_deg), 4, axis_dir) @ Matrix.Translation(-pivot)
        gates.transform_last(faces, m)


for side, i, s, c, n in L.gates:
    a, b = Vector(L.edges[i][0]), Vector(L.edges[i][1])
    d = (b - a).normalized()
    c = Vector(c)
    hl, hr = c - d * (gy.GATE_W / 2), c + d * (gy.GATE_W / 2)
    w = gy.GATE_W / 2 - 0.03
    # leaves swing outward (away from the yard): +d rotated toward n, -d rotated toward n
    el = (d * math.cos(math.radians(105)) + n * math.sin(math.radians(105))).normalized()
    er = (-d * math.cos(math.radians(95)) + n * math.sin(math.radians(95))).normalized()
    leaf(hl, el, n, w, 0)
    leaf(hr, er, n, w, 5)
    # arch: straight bar between pillar tops, arc above, short bars between, cross at the apex
    hb = gy.GATE_PILLAR_H - 0.12
    gates.seg_box(hl, hr, 0.04, hb, hb + 0.05, "CastIron")
    arc = []
    for k in range(13):
        t = k / 12
        p = hl + (hr - hl) * t
        arc.append(gy.bl(p.x, p.y, hb + 0.05 + 0.5 * math.sin(math.pi * t)))
    gates.pipe(arc, [0.022] * len(arc), "CastIron", segs=6)
    for k in range(1, 8):
        t = k / 8
        p = hl + (hr - hl) * t
        gates.prism(square(p, d, 0.016), hb + 0.05, hb + 0.05 + 0.5 * math.sin(math.pi * t), "CastIron")
    gates.prism(square(c, d, 0.035), hb + 0.55, hb + 0.95, "CastIron")
    gates.seg_box(c - d * 0.14, c + d * 0.14, 0.03, hb + 0.78, hb + 0.82, "CastIron")

for p in (iron, masonry, gates, lanterns):
    p.finish()
print("fence:", {o.name: gy.tris(o) for o in coll.objects}, "lanterns:", len(lights.objects))
