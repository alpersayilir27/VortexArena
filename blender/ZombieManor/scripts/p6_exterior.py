"""Part 6: exterior shell and surroundings, all outside the play area - the two gabled wings (front gable on the
facade line, inner roof slope rising beside the terrace), yard ground, back-garden graves + trees (some in leaf),
abandoned neighbour houses, distant mountain ring."""
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
import zm_rooms as zr
importlib.reload(zr)
from zm_common import (Part, bl, west_x, east_x, collection, clear_collection, tree, NW, NE, SW, SE, ZONE_Z,
                       FLOORS, WIN_Z)
from zm_rooms import flat_window, face_box

ZA = ZONE_Z[0]
F1, F2 = FLOORS[1], FLOORS[2]
# neighbour houses grow with the manor: beside a 3.5 m storey an unscaled house reads as a doll house.
# heights are stretched about a base (porch deck / ground), footprints are not touched
HSC = 1.12
hs = lambda h, base=0.0: base + (h - base) * HSC
WING_D = 2.9                     # wing depth beyond the house's boundary wall
PITCH = math.radians(55)
EAVE_U, EAVE_H = 0.18, F2 + 0.08 # inner eave just outside the terrace coping, 2 cm over its top

root = collection("ZM_P6_Exterior")
clear_collection(root)
rnd = random.Random(61)


def wing(name, I0, I1, n):
    """Gabled wing on the outside of a side boundary. I0/I1: boundary points on the facade line and on the
    house's north wall line; n: outward unit normal (venue). u = metres outward, v = metres north from I0."""
    p = Part(name, root)
    I0, I1, n = Vector(I0), Vector(I1), Vector(n)
    td = (I1 - I0).normalized()
    Lb = (I1 - I0).length
    P = lambda u, v: I0 + n * u + td * v
    Do = WING_D + 0.075
    # walls: front + back span the full width (they own the corners), the outer side wall sits between them
    p.wall(tuple(P(0.15, 0)), tuple(P(Do, 0)), -0.05, F2, "Siding", "Siding", "TrimPaint")
    p.wall(tuple(P(0.15, Lb)), tuple(P(Do, Lb)), -0.05, F2, "Siding", "Siding", "TrimPaint")
    p.wall(tuple(P(WING_D, 0.075)), tuple(P(WING_D, Lb - 0.075)), -0.05, F2, "Siding", "Siding", "TrimPaint")
    # stone plinth bands (front/back own the corners, the side band starts behind their embed)
    face_box(p, P(0, -0.075), n, -td, 0.13, Do + 0.02, -0.01, 0.02, -0.04, 0.35, "Stone")   # starts inside the house wall
    face_box(p, P(0, Lb + 0.075), n, td, 0.13, Do + 0.02, -0.01, 0.02, -0.04, 0.35, "Stone")
    face_box(p, P(Do, 0), td, n, -0.063, Lb + 0.063, -0.01, 0.02, -0.04, 0.35, "Stone")
    # corner boards on the front and back faces (the side face shows their edge)
    for v, sgn in ((-0.075, -1), (Lb + 0.075, 1)):
        face_box(p, P(0, v), n, td * sgn, Do - 0.12, Do + 0.035, -0.01, 0.03, 0.35, F2 - 0.01, "TrimPaint")
    # gable pentagons in the front and back walls, top edges under the roof's underside
    nr = (EAVE_U + Do) / 2
    rise = (nr - EAVE_U) * math.tan(PITCH)
    for v in (0.0, Lb):
        poly = [(EAVE_U, F2), (Do, F2), (Do, EAVE_H), (nr, EAVE_H + rise), (EAVE_U, EAVE_H)]
        pts = [bl(*(P(u, v - 0.075)), h) for u, h in poly]
        p.slab(pts, Vector(bl(*(td * 0.15), 0.0)) - Vector(bl(0, 0, 0)), "Siding")
    # roof: two slabs, 8 cm thick, 0.3 m overhang front and back; the outer slab ends 5 mm shorter
    tn = math.tan(PITCH)
    for (u0, h0), (u1, h1), vo in (((EAVE_U, EAVE_H), (nr, EAVE_H + rise), 0.3),
                                   ((nr, EAVE_H + rise), (Do + 0.25, EAVE_H - 0.25 * tn + 0.0), 0.295)):
        q = [bl(*(P(u0, -0.075 - vo)), h0), bl(*(P(u1, -0.075 - vo)), h1),
             bl(*(P(u1, Lb + 0.075 + vo)), h1), bl(*(P(u0, Lb + 0.075 + vo)), h0)]
        e1, e2 = q[1] - q[0], q[3] - q[0]
        nrm = e1.cross(e2).normalized()
        if nrm.z < 0:
            nrm = -nrm
        p.slab(q, nrm * 0.08, "Roof")
    # inner side above the house wall: the 8 cm slit between wall top and inner eave opened onto the
    # hollow wing (seen from the balcony/terrace down to the ground) - closed by a strip into the roof
    a_, b_ = P(0.15, -0.07), P(0.15, Lb + 0.07)
    p.wall(tuple(a_ + n * 0.0275), tuple(b_ + n * 0.0275), F2 - 0.03, EAVE_H + 0.05, "Siding", t=0.045)   # off the walls' end planes
    ridge = [bl(*(P(nr, v)), EAVE_H + rise + 0.07) for v in (-0.075 - 0.31, Lb + 0.075 + 0.31)]
    p.pipe(ridge, [0.06, 0.06], "Iron", segs=6)
    # windows: front (two storeys + attic), outer side, back
    um = (0.15 + Do) / 2
    for v, sgn in ((-0.075, -1), (Lb + 0.075, 1)):
        for h0, h1 in (WIN_Z, (F1 + WIN_Z[0], F1 + WIN_Z[1])):
            flat_window(p, P(um, v), n, td * sgn, 0.9, h0, h1)
        flat_window(p, P(nr, v), n, td * sgn, 0.55, F2 + 0.45, F2 + 1.15, shutters=False)
    for vf in (0.3, 0.7):
        for h0, h1 in (WIN_Z, (F1 + WIN_Z[0], F1 + WIN_Z[1])):
            flat_window(p, P(Do, Lb * vf), td, n, 0.9, h0, h1)
    p.finish()


d_w = (Vector(SW) - Vector(NW)).normalized()
wing("ZM_OUT_WingW", (west_x(ZA), ZA), (west_x(NW[1] + 0.075), NW[1] + 0.075), (d_w.y, -d_w.x))
d_e = (Vector(NE) - Vector(SE)).normalized()
wing("ZM_OUT_WingE", (east_x(ZA), ZA), (east_x(NE[1] + 0.075), NE[1] + 0.075), (d_e.y, -d_e.x))

# ---------------------------------------------------------------- yard, back garden, graves, trees
y = Part("ZM_OUT_Yard", root)
# yard 1 cm under the Part 1 garden strips; around the street block and south of it the same level
# bottom off the garden strips' -0.15, south edge 1 cm under the sidewalk (off the strips' edge plane)
YS = SW[1] - 0.26
y.box(-40, YS, 40, 40, -0.16, -0.03, "Dirt")
y.box(-40, -40, 40, -16.5, -0.16, -0.03, "Dirt")
y.box(-40, -16.5, -14.0, YS, -0.16, -0.03, "Dirt")
y.box(20.0, -16.5, 40, YS, -0.16, -0.03, "Dirt")
y.box(-170, -170, 170, 170, -0.65, -0.41, "Dirt")                         # far ground, under the Part 1 one
# family graves behind the house, inside a low iron fence
gx0, gx1, gz0, gz1 = -0.5, 5.5, 11.0, 15.0
for i in range(8):
    gx = gx0 + 0.6 + (gx1 - gx0 - 1.2) * (i % 4) / 3 + rnd.uniform(-0.15, 0.15)
    gz = gz0 + 1.0 + 1.8 * (i // 4) + rnd.uniform(-0.1, 0.1)
    tilt = (rnd.uniform(-9, 9), rnd.uniform(-6, 6), rnd.uniform(-10, 10))
    if i % 3 == 2:
        y.obox((gx, gz, 0.4), (0.07, 0.07, 0.9), "Stone", rot=tilt)
        y.obox((gx, gz, 0.65), (0.42, 0.07, 0.07), "Stone", rot=tilt)
    else:
        y.obox((gx, gz, 0.3), (0.5, 0.12, 0.7), "Stone", rot=tilt)
for (a, b) in (((gx0, gz0), (gx1, gz0)), ((gx1, gz0), (gx1, gz1)), ((gx1, gz1), (gx0, gz1)), ((gx0, gz1), (gx0, gz0))):
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    for k in range(int(L / 0.15)):
        c = a + (b - a) * (k + 0.5) / int(L / 0.15)
        lean = rnd.uniform(-0.06, 0.06)
        y.pipe([bl(c.x, c.y, -0.05), bl(c.x + lean, c.y, 0.9)], [0.01, 0.008], "Iron", segs=4)
    dd = (b - a).normalized() * 0.011                 # rails stop short of the corners: no overlapping tops
    y.wall(tuple(a + dd), tuple(b - dd), 0.78, 0.81, "Iron", t=0.02)
# trees stand >= 4 m from the wings and the neighbour houses (porches included): their crowns spread ~3 m
for i, (tx, tz, th) in enumerate(((-6.5, 12.5, 7.5), (10.0, 13.0, 8.0), (-10.5, 11.5, 6.5), (13.5, 11.0, 7.0),
                                  (3.0, 17.0, 9.0), (-6.0, 19.0, 7.0), (14.0, 15.0, 8.5))):
    tree(y, tx, tz, th, seed=101 + i, h=-0.03, leaves=i % 2 == 0)            # every other tree in leaf
y.finish()

# ---------------------------------------------------------------- ruined neighbour houses
nb = Part("ZM_OUT_Neighbors", root)


def house(cx, cz, w, d, h, face_deg, body="Siding", boarded=(), broken=(), seed=0):
    """Abandoned two-storey Victorian house: full gable roof (gable to the front) with chimney and ridge,
    front porch on posts, plinth, corner boards, shuttered / boarded / broken windows, front door.
    face_deg turns its front: 0 = toward +z (north), 90 = toward +x. boarded/broken: window indices."""
    rr = random.Random(seed)
    h *= HSC
    a = math.radians(face_deg)
    f = Vector((math.sin(a), math.cos(a)))           # front normal (venue)
    s = Vector((f.y, -f.x))
    c = Vector((cx, cz))
    P = lambda u, v: c + f * u + s * v               # u along the front normal, v along the front
    Q = lambda u, v, hh: bl(*P(u, v), hh)

    def obox(u0, u1, v0, v1, h0, h1, mat):
        nb.prism([tuple(P(u0, v0)), tuple(P(u1, v0)), tuple(P(u1, v1)), tuple(P(u0, v1))], h0, h1, mat)

    obox(-d / 2, d / 2, -w / 2, w / 2, -0.1, h, body)                                    # body, base under the yard
    obox(-d / 2 - 0.04, d / 2 + 0.04, -w / 2 - 0.04, w / 2 + 0.04, -0.12, 0.45, "Stone")   # plinth
    for su in (-1, 1):                                                                   # corner boards
        for sv in (-1, 1):
            obox(su * d / 2 - 0.06, su * d / 2 + 0.06, sv * w / 2 - 0.06, sv * w / 2 + 0.06, 0.4, h - 0.01, "TrimPaint")
    # roof: gable triangles, two 8 cm slopes with 30 cm overhang, iron ridge, brick chimney
    pitch = math.radians(40)
    rise = w / 2 * math.tan(pitch)
    for su in (-1, 1):
        u_ = su * d / 2
        nb.hull([Q(u_ - su * 0.12, v, hh) for v, hh in ((-w / 2, h), (w / 2, h), (0, h + rise - 0.12))] +
                [Q(u_, v, hh) for v, hh in ((-w / 2, h), (w / 2, h), (0, h + rise - 0.12))], body)   # sits on the body
    over = 0.3
    for sv in (-1, 1):
        q = [Q(-d / 2 - over, sv * (w / 2 + over), h - over * math.tan(pitch)), Q(d / 2 + over, sv * (w / 2 + over), h - over * math.tan(pitch)),
             Q(d / 2 + over, 0.0, h + rise), Q(-d / 2 - over, 0.0, h + rise)]
        nrm = (Vector(q[1]) - Vector(q[0])).cross(Vector(q[3]) - Vector(q[0])).normalized()
        if nrm.z < 0:
            nrm = -nrm
        nb.slab(q, nrm * 0.08, "Roof")
    nb.pipe([Q(-d / 2 - over - 0.02, 0, h + rise + 0.07), Q(d / 2 + over + 0.02, 0, h + rise + 0.07)], [0.05, 0.05], "Iron", segs=6)
    cu, cv = -d / 4, w / 4
    obox(cu - 0.28, cu + 0.28, cv - 0.25, cv + 0.25, h - 0.6, h + rise + 0.5, "Brick")
    obox(cu - 0.33, cu + 0.33, cv - 0.3, cv + 0.3, h + rise + 0.5, h + rise + 0.62, "Stone")
    # porch across the front: deck, two steps, four posts, sloped roof with fascia
    pd = 1.6
    obox(d / 2 - 0.02, d / 2 + pd, -w / 2 + 0.3, w / 2 - 0.3, -0.11, 0.48, "Deck")    # bottoms off the body's -0.1
    for k, (u0, h1) in enumerate(((d / 2 + pd, 0.32), (d / 2 + pd + 0.3, 0.16))):
        obox(u0 - 0.005 * k, u0 + 0.3, -0.7 + 0.02 * k, 0.7 - 0.02 * k, -0.1 - 0.015 * k, h1, "Stone")
    for v in (-w / 2 + 0.4, -0.8, 0.8, w / 2 - 0.4):
        obox(d / 2 + pd - 0.16, d / 2 + pd - 0.04, v - 0.06, v + 0.06, 0.47, hs(2.6, 0.47), "TrimPaint")   # tops in the beam
    pq = [Q(d / 2 - 0.02, -w / 2 + 0.15, hs(2.95, 0.47)), Q(d / 2 + pd + 0.15, -w / 2 + 0.15, hs(2.6, 0.47)),
          Q(d / 2 + pd + 0.15, w / 2 - 0.15, hs(2.6, 0.47)), Q(d / 2 - 0.02, w / 2 - 0.15, hs(2.95, 0.47))]
    nb.slab(pq, Vector((0, 0, 0.07)), "Roof")
    obox(d / 2 + pd - 0.17, d / 2 + pd - 0.03, -w / 2 + 0.3, w / 2 - 0.3, hs(2.45, 0.47), hs(2.62, 0.47),
         "TrimPaint")                                                                             # porch beam
    # door + windows: front (two storeys), sides; the door stands on the deck, so it stretches upward only
    face_box(nb, P(d / 2, 0), s, f, -0.45, 0.45, -0.01, 0.035, 0.45, hs(2.55, 0.45), "DoorWood")
    face_box(nb, P(d / 2, 0), s, f, -0.56, 0.56, -0.02, 0.05, hs(2.55, 0.45), hs(2.7, 0.45), "TrimPaint")
    wins = [(P(d / 2, k * w / 4), s, f) for k in (-1, 1)]                                    # ground floor front
    wins += [(P(d / 2, k * w / 4), s, f) for k in (-1, 0, 1)]                                # upper front
    wins += [(P(u, sv * w / 2), -f * sv, s * sv) for sv in (-1, 1) for u in (-d / 4, d / 4)]   # sides (both floors)
    low, up = (hs(0.95), hs(2.15)), (hs(3.25), hs(4.35))
    for i, (pc, tan, nrm) in enumerate(wins):
        storeys = (low,) if i < 2 else (up,) if i < 5 else (low, up)
        for h0, h1 in storeys:
            if h1 > h - 0.3:
                continue
            # the three upper front windows stand too close for open shutters
            flat_window(nb, pc, tan, nrm, 0.9, h0, h1, shutters=not 2 <= i <= 4 and rr.random() < 0.5)
            if i in boarded:                                                             # planks nailed across
                for hh in (h0 + 0.2, (h0 + h1) / 2, h1 - 0.2):
                    face_box(nb, pc, tan, nrm, -0.55, 0.55, 0.09, 0.115, hh - 0.07, hh + 0.07, "DoorWood")
            elif i in broken:                                     # shattered: black void between the muntin layers
                face_box(nb, pc, tan, nrm, -0.4, 0.4, 0.016, 0.026, h0 + 0.08, h1 - 0.25, "CastIron")


house(-9.5, -20.5, 6.5, 6.0, 5.5, 0, body="SidingCream", boarded=(0,), broken=(3,), seed=1)   # across the street
house(0.5, -21.0, 7.0, 6.5, 6.5, 0, body="Siding", boarded=(1, 2), broken=(4,), seed=2)
house(11.0, -20.0, 6.0, 5.5, 5.0, 0, body="Brick", broken=(0, 3), seed=3)
house(-12.5, 4.0, 7.0, 6.0, 5.5, 90, body="Siding", boarded=(1,), seed=4)
house(16.5, 3.0, 7.5, 6.0, 6.0, -90, body="SidingCream", broken=(2,), boarded=(0,), seed=5)
house(2.5, 26.0, 9.0, 6.0, 5.0, 180, body="Brick", boarded=(0, 1), seed=6)
nb.finish()

# ---------------------------------------------------------------- mountain ring
# one continuous ridge ring: a polar height field (rings outward, layered noise along the angle), smooth-shaded
from mathutils import noise
mt = Part("ZM_OUT_Mountains", root)
N_A = 128
RINGS = [(85, 0.0), (96, 0.35), (108, 0.8), (120, 1.0), (134, 0.7), (150, 0.0)]   # (radius, share of the crest height)


def crest(a):
    """Crest height at angle a: broad swells, saw-toothed peaks (ridged octave) and valleys, ~4..48 m."""
    q = Vector((math.cos(a), math.sin(a), 0.0))
    swell = noise.noise(q * 1.8)
    peaks = 1.0 - abs(noise.noise(q * 6.0 + Vector((7, 3, 0))))
    detail = noise.noise(q * 15 + Vector((1, 9, 0)))
    return max(4.0, 20 + 30 * swell + 14 * (peaks - 0.5) + 4 * detail)


grid = []
for ri, (R, share) in enumerate(RINGS):
    row = []
    for k in range(N_A):
        a = 2 * math.pi * k / N_A
        jitter = 1 + 0.06 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, ri)))
        h = -0.45 if share == 0 else share * crest(a) * jitter
        row.append(mt.bm.verts.new(bl(math.cos(a) * R, math.sin(a) * R, h)))
    grid.append(row)
mt.smooth = True
faces = [mt.bm.faces.new((a_[k], a_[(k + 1) % N_A], b_[(k + 1) % N_A], b_[k]))
         for a_, b_ in zip(grid, grid[1:]) for k in range(N_A)]
import bmesh as _bm
_bm.ops.recalc_face_normals(mt.bm, faces=faces)
for f_ in faces:                                    # the ridge faces the manor: normals point up / inward
    if f_.normal.z < 0:
        f_.normal_flip()
mt._set(faces, "Mountain")
mt.smooth = False
mt.finish()

# ---------------------------------------------------------------- hide the graybox pieces this part replaces
for n_ in ("OUT_WingW_Body", "OUT_WingW_Roof", "OUT_WingE_Body", "OUT_WingE_Roof"):
    ob = bpy.data.objects.get(n_)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    print(f"{ob.name}: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
