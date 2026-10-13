"""Part 1: ground-floor courtyard (strip A) - cobbles, iron fence + chained gate, front facade, canopy, lamps,
and the street outside the south boundary (sidewalk, road, dead trees)."""
import importlib
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)
from zm_common import (Part, bl, west_x, east_x, zone_poly, column_bounds, collection, clear_collection, tree,
                       SW, SE, ZONE_Z, FLOORS, CEIL, DOOR_H)
import zm_rooms as zr
importlib.reload(zr)
from zm_rooms import panel_door, shutter

ZA = ZONE_Z[0]
F1 = FLOORS[1]
CURB_H, CURB_T = 0.35, 0.25
BAR_TOP, PILLAR_W, PILLAR_H = 1.65, 0.5, 2.0
PIER_H, GATE_W = 2.3, 1.4     # the two gate piers are identical; the east one wraps Kolon_3
DOOR_X, BREACH_X = (0.3, 1.7), (4.2, 5.4)
WIN_X = ((-1.4, -0.5), (2.5, 3.5))
WIN_Z = (0.9, 2.1)

root = collection("ZM_P1_Courtyard")
clear_collection(root)
rnd = random.Random(7)

W0, E0 = (west_x(ZA), ZA), (east_x(ZA), ZA)
FZ = ZA - 0.095                         # side fences stop at the facade plinth's outer face
FW0, FE0 = (west_x(FZ), FZ), (east_x(FZ), FZ)
PROUD = 0.02                            # pillars stand proud of the curb face (no coplanar faces)
SINK = 0.02                             # posts start below grade so their bottoms never share the curb's
k0x0, k0z0, k0x1, k0z1 = column_bounds("Kolon_3")
PIER_E = (k0x0 - 0.1, k0x1 + 0.1)                                   # wraps the physical column
PIER_W = (PIER_E[0] - GATE_W - (PIER_E[1] - PIER_E[0]), PIER_E[0] - GATE_W)
PIER_Z = (SW[1] - 0.14, k0z1 + 0.1)                                 # straddles the boundary
GATE_X = (PIER_W[1], PIER_E[0])


def along(a, b, s, off=0.0, h=0.0):
    """Blender point s metres along venue segment a->b, pushed `off` to its right (outside a CCW outline)."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    p = a + d * s + Vector((d.y, -d.x)) * off
    return bl(p.x, p.y, h)


def seg_len(a, b):
    return (Vector(b) - Vector(a)).length


# ---------------------------------------------------------------- ground + flagstone path
g = Part("ZM_K0_CourtyardGround", root)
g.prism(zone_poly("A"), -0.10, 0.0, "Cobble")
# path from the gate to the front door; stones float 12 mm above the cobbles (no coplanar faces)
gx, dx = sum(GATE_X) / 2, sum(DOOR_X) / 2
z = -4.0
while z < ZA - 0.35:
    t = (z + 4.0) / (ZA + 3.65)
    cx = gx + (dx - gx) * t + rnd.uniform(-0.06, 0.06)
    for k in (-1, 1):
        w, dpt = rnd.uniform(0.42, 0.55), rnd.uniform(0.32, 0.42)
        g.obox((cx + k * 0.29, z, 0.006), (w, dpt, 0.012), "Flagstone", rot=(0, 0, rnd.uniform(-6, 6)))
    z += rnd.uniform(0.44, 0.52)
g.finish()

# ---------------------------------------------------------------- fence: stone curb, brick pillars, iron bars, gate
f = Part("ZM_K0_Fence", root)
edges = [(FW0, SW), (SW, SE), (SE, FE0)]
gate_s = (GATE_X[0] - SW[0], GATE_X[1] - SW[0])   # south edge is ~horizontal: s ~ dx
pier_s = (PIER_W[0] - SW[0], PIER_E[1] - SW[0])


def cap_pyramid(top, half):
    f.hull([top + Vector((x, y, 0)) for x in (-half, half) for y in (-half, half)] + [top + Vector((0, 0, half))], "Stone")


def pillar(a, b, s, w=PILLAR_W, h=PILLAR_H):
    d = (Vector(b) - Vector(a)).normalized()
    n = Vector((d.y, -d.x))
    for ww, ext, h0, h1, mat in ((w, 0.0, -SINK, h, "Brick"), (w + 0.04, 0.04, h, h + 0.08, "Stone")):
        o_ = Vector(a) - n * (PROUD + ext / 2)
        f.wall(tuple(o_ + d * (s - ww / 2)), tuple(o_ + d * (s + ww / 2)), h0, h1, mat, t=ww, side=1)
    cap_pyramid(along(a, b, s, w / 2 - PROUD, h + 0.08), w * 0.4)


def pier(x0, x1):
    """Gate pier: brick shaft, stone cap, lantern on top (both piers identical)."""
    z0, z1 = PIER_Z
    f.box(x0, z0, x1, z1, -SINK, PIER_H, "Brick")
    f.box(x0 - 0.05, z0 - 0.05, x1 + 0.05, z1 + 0.05, PIER_H, PIER_H + 0.1, "Stone")
    f.box(x0 + 0.08, z0 + 0.08, x1 - 0.08, z1 - 0.08, PIER_H + 0.1, PIER_H + 0.16, "Stone")
    cx, cz, h = (x0 + x1) / 2, (z0 + z1) / 2, PIER_H + 0.16
    f.box(cx - 0.12, cz - 0.12, cx + 0.12, cz + 0.12, h, h + 0.05, "Iron")
    f.box(cx - 0.09, cz - 0.09, cx + 0.09, cz + 0.09, h + 0.05, h + 0.33, "LampGlow")
    for px, pz in ((-0.1, -0.1), (-0.1, 0.1), (0.1, -0.1), (0.1, 0.1)):
        f.box(cx + px - 0.012, cz + pz - 0.012, cx + px + 0.012, cz + pz + 0.012, h + 0.05, h + 0.33, "Iron")
    top = bl(cx, cz, h + 0.33)
    f.hull([top + Vector((x, y, 0)) for x in (-0.15, 0.15) for y in (-0.15, 0.15)] + [top + Vector((0, 0, 0.14))], "Iron")


def bars(a, b, s0, s1):
    n = max(1, round((s1 - s0) / 0.13))
    for i in range(n):
        s = s0 + (s1 - s0) * (i + 0.5) / n
        p = along(a, b, s, 0.07)
        f.pipe([p + Vector((0, 0, CURB_H - 0.02)), p + Vector((0, 0, BAR_TOP))], [0.012, 0.012], "Iron", segs=6)
        f.pipe([p + Vector((0, 0, BAR_TOP)), p + Vector((0, 0, BAR_TOP + 0.1))], [0.022, 0.003], "Iron", segs=4)
    for hh in (0.48, BAR_TOP - 0.12):
        pa, pb = Vector(a), Vector(b)
        d = (pb - pa).normalized()
        f.wall(tuple(pa + d * s0), tuple(pa + d * s1), hh, hh + 0.035, "Iron", t=0.03, side=1)


def corner_pillar(a, c, b, w=PILLAR_W, h=PILLAR_H):
    """Pillar in the outside corner square at outline corner c (between edges a->c and c->b)."""
    c = Vector(c)
    n_in = (c - Vector(a)).normalized()
    n_out = (Vector(b) - c).normalized()
    n_in, n_out = Vector((n_in.y, -n_in.x)), Vector((n_out.y, -n_out.x))   # outward normals
    for ww, h0, h1, mat in ((w, -SINK, h, "Brick"), (w + 0.04, h, h + 0.08, "Stone")):
        o_ = c - (n_in + n_out) * 0.02 * (ww > w)
        f.prism([tuple(o_), tuple(o_ + n_in * ww), tuple(o_ + n_in * ww + n_out * ww), tuple(o_ + n_out * ww)], h0, h1, mat)
    m = c + (n_in + n_out) * w / 2
    cap_pyramid(bl(m.x, m.y, h + 0.08), w * 0.4)


corner_pillar(W0, SW, SE)
corner_pillar(SW, SE, E0)
pier(*PIER_W)
pier(*PIER_E)

HW = PILLAR_W / 2
for a, b in edges:
    L = seg_len(a, b)
    gate = (a, b) == (SW, SE)
    # the curb's gate gap ends 5 cm inside the piers so its end caps never sit on a pier face
    opens = [(gate_s[0] - 0.05, gate_s[1] + 0.05, CURB_H, 0.05)] if gate else []
    f.wall(a, b, 0.0, CURB_H, "Stone", t=CURB_T, side=1, openings=opens)
    # fence runs: outline corners have corner_pillar(), house corners an end pillar, the gate its piers
    # house-end pillars stop 1 cm short of the curb end so their faces never share its plane
    lo = HW + 0.01 if a == FW0 else -HW
    hi = L - HW - 0.01 if b == FE0 else L + HW
    # a run end inside a pier is a virtual station: no pillar is drawn there, the bars stop at the pier face
    runs = [(lo, pier_s[0] + HW), (pier_s[1] - HW, hi)] if gate else [(lo, hi)]
    for r0, r1 in runs:
        n = int((r1 - r0) // 3.2)
        stations = [r0 + (r1 - r0) * i / (n + 1) for i in range(n + 2)]
        for s in stations:
            if 0 < s < L and not (gate and pier_s[0] - 0.1 < s < pier_s[1] + 0.1):
                pillar(a, b, s)
        for s0, s1 in zip(stations, stations[1:]):
            if s1 - s0 > PILLAR_W + 0.1:
                bars(a, b, s0 + HW, s1 - HW)

# closed double gate (chained): frames + bars + arched top rail
ga, gb = SW, SE
g0, g1 = gate_s[0] + 0.02, gate_s[1] - 0.02
mid = (g0 + g1) / 2
for s0, s1 in ((g0, mid - 0.01), (mid + 0.01, g1)):
    for hh in (0.12, 1.0):
        f.wall(tuple(Vector(ga) + (Vector(gb) - Vector(ga)).normalized() * s0),
               tuple(Vector(ga) + (Vector(gb) - Vector(ga)).normalized() * s1), hh, hh + 0.04, "Iron", t=0.035, side=1)
    for s in (s0 + 0.02, s1 - 0.02):
        p = along(ga, gb, s, 0.07)
        f.pipe([p + Vector((0, 0, 0.1)), p + Vector((0, 0, 1.75))], [0.02, 0.02], "Iron", segs=6)
    n = 5
    for i in range(1, n):
        s = s0 + (s1 - s0) * i / n
        arch = 1.75 + 0.18 * math.sin(math.pi * (s - g0) / (g1 - g0))
        p = along(ga, gb, s, 0.07)
        f.pipe([p + Vector((0, 0, 0.1)), p + Vector((0, 0, arch))], [0.012, 0.012], "Iron", segs=6)
        f.pipe([p + Vector((0, 0, arch)), p + Vector((0, 0, arch + 0.1))], [0.022, 0.003], "Iron", segs=4)
    pts = [along(ga, gb, s0 + (s1 - s0) * i / 8, 0.07, 1.72 + 0.18 * math.sin(math.pi * (s0 + (s1 - s0) * i / 8 - g0) / (g1 - g0)))
           for i in range(9)]
    f.pipe(pts, [0.016] * 9, "Iron", segs=5)
# chain across the gate centre + padlock
c = along(ga, gb, mid, 0.12, 0.85)
for i in range(7):
    a_ = (i - 3) * 0.05
    p =c + Vector((-a_, 0.0, -0.06 * math.cos(a_ * 6)))
    f.pipe([p + Vector((-0.025, 0, 0)), p + Vector((0.025, 0, 0))], [0.008, 0.008], "Iron", segs=4)
f.hull([c + Vector((x, y, zz)) for x in (-0.04, 0.04) for y in (-0.02, 0.02) for zz in (-0.2, -0.12)], "Iron")
f.finish()

# ---------------------------------------------------------------- front facade (A|B line), siding outside, wallpaper inside
fa = Part("ZM_K0_FrontFacade", root)
s = lambda x: x - W0[0]
base_open = [(s(DOOR_X[0]), s(DOOR_X[1]), CURB_H), (s(BREACH_X[0]), s(BREACH_X[1]), CURB_H)]
# bottom below grade and top inside the balcony slab: the side walls' 0 / F1 planes stay unshared at the corners
fa.wall(W0, E0, -0.035, 0.35, "Stone", "Wallpaper", "Stone", t=0.19, openings=[(a_, b_, CURB_H + 0.035) for a_, b_, _ in base_open])
up_open = [(s(DOOR_X[0]), s(DOOR_X[1]), DOOR_H - 0.35), (s(BREACH_X[0]), s(BREACH_X[1]), DOOR_H - 0.35)]
up_open += [(s(x0), s(x1), WIN_Z[1] - 0.35, WIN_Z[0] - 0.35) for x0, x1 in WIN_X]
fa.wall(W0, E0, 0.35, F1 - 0.02, "SidingCream", "Wallpaper", "TrimPaint", openings=up_open)   # porch front: cream (reference)
OUT = 0.075 + 0.015    # exterior face offset of proud trim (wall half-thickness + 15 mm)

# fascia band where the balcony slab meets the facade
p0, p1 = Vector(W0) + Vector((0, -OUT)), Vector(E0) + Vector((0, -OUT))
fa.wall(tuple(p0), tuple(p1), CEIL, F1, "TrimPaint", t=0.03)


def casing(x0, x1, z0, z1, head=0.16):
    zz = ZA - OUT
    for xa, xb in ((x0 - 0.1, x0 + 0.01), (x1 - 0.01, x1 + 0.1)):   # 1 cm over the reveal: no shared plane
        fa.box(xa, zz - 0.015, xb, zz + 0.015, z0 if z0 > 0 else -SINK, z1 + 0.02, "TrimPaint")
    fa.box(x0 - 0.16, zz - 0.03, x1 + 0.16, zz + 0.015, z1 + 0.02, z1 + 0.02 + head, "TrimPaint")
    fa.hull([bl(x, zz, z1 + 0.02 + head) + Vector(v) for x in (x0 - 0.16, x1 + 0.16) for v in ((0, 0, 0), (0, -0.03, 0))]
            + [bl((x0 + x1) / 2, zz, z1 + head + 0.2)], "TrimPaint")


casing(*DOOR_X, 0.0, DOOR_H)
for x0, x1 in WIN_X:
    casing(x0, x1, WIN_Z[0], WIN_Z[1], head=0.12)
    fa.box(x0 - 0.08, ZA - 0.16, x1 + 0.08, ZA - 0.07, WIN_Z[0] - 0.05, WIN_Z[0] + 0.012, "TrimPaint")   # sill

# west window: dark glass + muntins, one shutter hanging askew
x0, x1 = WIN_X[0]
fa.box(x0, ZA - 0.006, x1, ZA + 0.006, WIN_Z[0], WIN_Z[1], "Glass")
fa.box((x0 + x1) / 2 - 0.02, ZA - 0.03, (x0 + x1) / 2 + 0.02, ZA - 0.01, WIN_Z[0], WIN_Z[1], "TrimPaint")
fa.box(x0, ZA - 0.035, x1, ZA - 0.008, 1.48, 1.52, "TrimPaint")
# louvred shutters: the left one fell and leans against the wall under the window, the right one swings out
lean = Vector((0, 0.2, 0.98)).normalized()                                       # top tipped toward the wall
shutter(fa, (x0 + 0.02, ZA - 0.45, 0.0), (1, 0, 0), lean, Vector((1, 0, 0)).cross(lean), 0.45, 1.15)
a32 = math.radians(32)
shutter(fa, (x1 + 0.11, ZA - OUT - 0.005, 0.93), (math.cos(a32), -math.sin(a32), 0), (0, 0, 1),
        (-math.sin(a32), -math.cos(a32), 0), 0.45, 1.15)
# east window: boarded up
x0, x1 = WIN_X[1]
fa.box(x0, ZA - 0.006, x1, ZA + 0.006, WIN_Z[0], WIN_Z[1], "Glass")
for i, (hh, ang) in enumerate(((1.15, 8), (1.5, -12), (1.85, 5))):
    fa.obox(((x0 + x1) / 2, ZA - 0.115 - 0.025 * i, hh), (1.25, 0.022, 0.16), "DoorWood", rot=(0, ang, 0))

# front door: left leaf swung inward, right leaf ripped off and lying on the cobbles
leaf_w = (DOOR_X[1] - DOOR_X[0]) / 2 - 0.01
a6, a24 = math.radians(6), math.radians(24)
U6 = Vector((-math.sin(a6), math.cos(a6), 0))
panel_door(fa, Vector((DOOR_X[0] + 0.03, ZA + 0.08, 0.01)), U6, (0, 0, 1), (math.cos(a6), math.sin(a6), 0), leaf_w,
           DOOR_H - 0.08)
Ul, Vl = Vector((-math.cos(a24), -math.sin(a24), 0)), Vector((math.sin(a24), -math.cos(a24), 0))
panel_door(fa, Vector((DOOR_X[1] + 0.25, ZA - 1.05, 0.026)) - Ul * leaf_w / 2 - Vl * 1.05, Ul, Vl, (0, 0, 1),
           leaf_w, DOOR_H - 0.1, knobs=(1,))                                          # torn off, lying face up
fa.box(DOOR_X[0], ZA - 0.09, DOOR_X[1], ZA + 0.09, 0.0, 0.02, "Stone")   # threshold

# breach: splintered siding stubs on the jambs (kept inside 0.12 m of each jamb) + low rubble
for side_x, sgn in ((BREACH_X[0], 1), (BREACH_X[1], -1)):
    for hh in (0.6, 0.95, 1.4, 1.75, DOOR_H - 0.15):
        ln = rnd.uniform(0.04, 0.12)
        fa.obox((side_x + sgn * ln / 2, ZA - 0.06, hh), (ln, 0.02, 0.17), "Siding", rot=(0, rnd.uniform(-20, 20), 0))
for i in range(9):
    fa.obox((rnd.uniform(BREACH_X[0] - 0.4, BREACH_X[1] + 0.4), ZA - rnd.uniform(0.25, 0.9), 0.03),
            (rnd.uniform(0.1, 0.5), rnd.uniform(0.05, 0.14), 0.04), rnd.choice(("Siding", "TrimPaint", "Stone")),
            rot=(rnd.uniform(-8, 8), rnd.uniform(-8, 8), rnd.uniform(0, 180)))

# canopy over the door on two knee braces (its wall end stays below the balcony slab's fascia at CEIL)
cx0, cx1, cz = 0.06, 1.94, ZA - 0.9
cn = Part("ZM_K0_Canopy", root)               # roof + fascia as their own object: under ArenaRoof in Unity
cn.hull([bl(x, zz, hh) for x in (cx0, cx1) for zz, hh in ((ZA - 0.075, CEIL - 0.06), (cz, 2.68))]
        + [bl(x, zz, hh - 0.06) for x in (cx0, cx1) for zz, hh in ((ZA - 0.075, CEIL - 0.06), (cz, 2.68))], "Roof")
cn.box(cx0 - 0.01, cz - 0.035, cx1 + 0.01, cz + 0.01, 2.53, 2.66, "TrimPaint")
cn.finish()
for bx in (cx0 + 0.15, cx1 - 0.15):
    fa.pipe([bl(bx, ZA - OUT, 2.15), bl(bx, cz + 0.08, 2.62)], [0.035, 0.035], "TrimPaint", segs=4)
    fa.box(bx - 0.04, ZA - OUT - 0.02, bx + 0.04, ZA - OUT + 0.01, 2.1, 2.65, "TrimPaint")

# wall lanterns flanking the door (bottom at 2.30 m, clear of heads)
for lx in (DOOR_X[0] - 0.43, DOOR_X[1] + 0.43):
    lz = ZA - OUT - 0.18
    fa.box(lx - 0.03, lz, lx + 0.03, ZA - OUT, 2.48, 2.52, "Iron")                 # arm
    fa.box(lx - 0.1, lz - 0.1, lx + 0.1, lz + 0.1, 2.3, 2.33, "Iron")               # base plate
    fa.box(lx - 0.1, lz - 0.1, lx + 0.1, lz + 0.1, 2.53, 2.56, "Iron")              # top plate
    fa.box(lx - 0.075, lz - 0.075, lx + 0.075, lz + 0.075, 2.33, 2.53, "LampGlow")
    for px in (-0.09, 0.09):
        for pz in (-0.09, 0.09):
            fa.box(lx + px - 0.01, lz + pz - 0.01, lx + px + 0.01, lz + pz + 0.01, 2.33, 2.53, "Iron")
    fa.hull([bl(lx, ZA - OUT - 0.18, 2.68)] + [bl(lx + x, ZA - OUT - 0.18 + y, 2.56) for x in (-0.11, 0.11) for y in (-0.1, 0.1)], "Iron")
fa.finish()

# ---------------------------------------------------------------- outside the south boundary: street
o = Part("ZM_K0_StreetSouth", root)
X0, X1 = -14.0, 20.0
SIDE_Z = SW[1] - CURB_T            # sidewalk starts behind the fence curb
o.box(X0, -6.3, X1, SIDE_Z, -0.12, 0.0, "Concrete")
o.box(X0, -6.42, X1, -6.3, -0.27, 0.0, "Stone")                # curb
o.box(X0, -14.0, X1, -6.42, -0.35, -0.15, "Road")
o.box(X0, -14.12, X1, -14.0, -0.27, 0.0, "Stone")
o.box(X0, -16.5, X1, -14.12, -0.12, 0.0, "Concrete")
# garden strips beside the courtyard, outside the side fences (2 cm under the cobbles' level)
o.box(-7.0, SIDE_Z, west_x(SW[1]) - CURB_T, ZA, -0.15, -0.02, "Dirt")
o.box(east_x(SE[1]) + CURB_T, SIDE_Z, 11.0, ZA, -0.15, -0.02, "Dirt")
o.box(-40, -40, 40, 40, -0.6, -0.4, "Dirt")                   # far ground under everything


# street lamps and the covered car are CC0 props (Part 7 layout)
# street litter: two ribbed bins (one lid knocked off), mailbox on a post, manhole cover, old newspapers
for bx, bz, lid in ((-1.7, -5.95, True), (-2.35, -6.0, False)):
    o.lathe(bx, bz, [(-0.02, 0.24), (0.1, 0.25), (0.11, 0.262), (0.12, 0.25), (0.45, 0.26), (0.46, 0.272), (0.47, 0.26),
                     (0.8, 0.27), (0.86, 0.278)], "Iron", segs=16)
    if lid:
        o.lathe(bx, bz, [(0.86, 0.29), (0.9, 0.285), (0.94, 0.12), (0.97, 0.03)], "Iron", segs=16)
o.obox((-2.95, -5.85, 0.06), (0.58, 0.58, 0.05), "Iron", rot=(0, 78, 25))                # the lid, rolled away
o.box(3.4 - 0.04, -5.1 - 0.04, 3.4 + 0.04, -5.1 + 0.04, -0.02, 1.05, "Trim")            # mailbox post
o.hull([bl(3.4 + dx, -5.1 + dz, hh) for dx in (-0.12, 0.12) for dz in (-0.24, 0.24) for hh in (1.04, 1.2)] +
       [bl(3.4 + dx * 0.12 * math.cos(a), -5.1 + dz, 1.2 + 0.12 * math.sin(a)) for a in (0.6, 1.2, 1.9, 2.5)
        for dx in (1,) for dz in (-0.24, 0.24)], "Iron")
o.cyl(1.6, -10.2, -0.165, -0.14, 0.35, "Iron", segs=20)                                    # manhole, 1 cm proud
for k in range(7):
    o.obox((rnd.uniform(-4.0, 6.0), rnd.uniform(-6.25, -5.0) if k < 4 else rnd.uniform(-13.5, -7.0),
            (0.004 if k < 4 else -0.146) + 0.003 * k), (rnd.uniform(0.25, 0.4), rnd.uniform(0.2, 0.3), 0.004), "Paper",
           rot=(rnd.uniform(-3, 3), rnd.uniform(-3, 3), rnd.uniform(0, 180)))
# dead trees outside the boundary
# kept >= 4 m from the wings and the neighbour houses (porches included): the crowns spread ~3 m
for i, (tx, tz, th) in enumerate(((-5.2, -3.4, 6.5), (10.0, -5.0, 7.5), (-16.5, -19.0, 8.0), (17.0, -19.5, 7.0), (-4.5, -28.5, 5.5))):
    tree(o, tx, tz, th, seed=11 + i, h=-0.02 if abs(tz) < 5 else 0.0)
o.finish()

# ---------------------------------------------------------------- hide the graybox pieces this part replaces
for n in ("K0_Courtyard", "K0_Fence_W", "K0_Fence_S", "K0_Fence_E", "K0_GatePost_Kolon3", "K0_Facade_Front", "K0_Canopy",
          "OUT_Street", "OUT_Car", "OUT_Ground"):
    ob = bpy.data.objects.get(n)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"{ob.name}: {tris} tris, mats {[m.name for m in ob.data.materials]}")
