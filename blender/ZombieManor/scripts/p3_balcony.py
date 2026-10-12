"""Part 3: Kat1 balcony (strip B, open air) - checker slab, balustrade, wing walls with shuttered windows,
house facade with two arched doors + weapon board + lanterns, and the bronze statue on a pedestal
wrapping Kolon_2."""
import importlib
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)
import zm_rooms as zr
importlib.reload(zr)
from zm_rooms import balustrade, shutter
from zm_common import (Part, bl, west_x, east_x, zone_poly, column_bounds, collection, clear_collection,
                       FLOORS, SLAB, ZONE_Z, WALL_T, RAIL_H)

ZA, ZB = ZONE_Z
F1 = FLOORS[1]
F2 = FLOORS[2]
W = lambda z: (west_x(z), z)
E = lambda z: (east_x(z), z)
RAIL_Z = ZA + 0.02                      # railing centre line, newels 1 cm inside the slab edge
DOORS = ((-0.65, 0.55), (3.9, 5.1))     # arched doors balcony -> rooms (venue x)
SPRING = 2.05                           # arch springing height; keystone stays under the Kat1 ceiling (2.8)
BOARD_X, BOARD_H = (1.0, 3.3), (0.9, 2.05)
WIN_ZC, WIN_W, WIN_H = 1.55, 1.0, (0.9, 2.4)   # flat shuttered window on each wing wall
FACADE_TOP = F2 - 0.03                  # stays under the wing walls' top (corner planes unshared)

root = collection("ZM_P3_Balcony")
clear_collection(root)


def vbl(nx, nz):
    """Venue direction -> Blender vector."""
    return Vector((-nx, -nz, 0.0))


# ---------------------------------------------------------------- slab (top = Kat1 floor; the Kat0 ceiling sits under it)
s = Part("ZM_K1_BalconySlab", root)
poly = [(west_x(ZA - 0.07), ZA - 0.07), (east_x(ZA - 0.07), ZA - 0.07), E(ZB), W(ZB)]   # edge 5 mm inside the facade face
s.prism(poly, F1 - SLAB + 0.06, F1, "Checker")
s.finish()

# ---------------------------------------------------------------- balustrade on the south edge
r = Part("ZM_K1_BalconyRail", root)
x0r, x1r = west_x(RAIL_Z), east_x(RAIL_Z)
# end newels 5 mm off the wing walls: their faces never share the facade's end planes
balustrade(r, [(x0r + 0.085, RAIL_Z), (x1r - 0.085, RAIL_Z)], F1, RAIL_H)
r.finish()

# ---------------------------------------------------------------- wing walls + facade
w = Part("ZM_K1_BalconyWalls", root)
# wing walls (inner face on the boundary), from the facade line of Kat0 to strip C
w.wall(W(ZB), W(ZA - 0.075), F1, F2, "Siding", "Siding", "TrimPaint", side=1)
w.wall(E(ZA - 0.075), E(ZB), F1, F2, "Siding", "Siding", "TrimPaint", side=1)

# facade on the B|C line: siding outside (balcony), wallpaper inside (rooms); runs into the wing walls
fa_a = (west_x(ZB) - 0.15, ZB)
fa_b = (east_x(ZB) + 0.15, ZB)
sx = lambda x: x - fa_a[0]
H0 = F1 - 0.04                           # above the Kat0 wall top (2.95), inside the slab
R_ = [(x1 - x0) / 2 for x0, x1 in DOORS]
open_top = [F1 + SPRING + rr + 0.05 for rr in R_]
w.wall(fa_a, fa_b, H0, FACADE_TOP, "Siding", "WallpaperGrey", "TrimPaint",
       openings=[(sx(x0), sx(x1), ot - H0) for (x0, x1), ot in zip(DOORS, open_top)])

n_face = vbl(0, -1)                      # facade's south (balcony) normal in Blender space
for (x0, x1), rr, ot in zip(DOORS, R_, open_top):
    xm, sp = (x0 + x1) / 2, F1 + SPRING
    N = 12
    arc = [(xm - rr * math.cos(math.pi * i / N), sp + rr * math.sin(math.pi * i / N)) for i in range(N + 1)]
    for (xa, ha), (xb, hb) in zip(arc, arc[1:]):             # arch infill strips (convex quads)
        back = ZB + WALL_T / 2
        pts = [bl(xa, back, ha), bl(xb, back, hb), bl(xb, back, ot), bl(xa, back, ot)]
        w.slab(pts, n_face * WALL_T, "Siding")
    # casings on both faces: jambs to the springing line + a round moulding along the arch
    for fs in (1, -1):
        zf = ZB - fs * WALL_T / 2
        za_, zb_ = sorted((zf + fs * 0.02, zf - fs * 0.04))
        cas = "TrimPaint" if fs > 0 else "Trim"                  # painted on the balcony, dark wood in the rooms
        for xa, xb in ((x0 - 0.08, x0 + 0.01), (x1 - 0.01, x1 + 0.08)):
            w.box(xa, za_, xb, zb_, F1 - 0.03, sp, cas)
        # moulding radius rr + 5 cm: its inner faces stay 1.5 cm off the arch soffit
        ring = [bl(xm - (rr + 0.05) * math.cos(math.pi * i / N), zf, sp + (rr + 0.05) * math.sin(math.pi * i / N))
                for i in range(N + 1)]
        w.pipe(ring, [0.04] * len(ring), cas, segs=6)
        w.box(xm - 0.07, za_, xm + 0.07, zb_, sp + rr - 0.02, sp + rr + 0.16, cas)   # keystone

# weapon board between the doors: dark backboard + frame (Unity hangs the weapon canvas here)
zf = ZB - WALL_T / 2
bx0, bx1 = BOARD_X
h0b, h1b = F1 + BOARD_H[0], F1 + BOARD_H[1]
w.box(bx0, zf - 0.03, bx1, zf + 0.015, h0b, h1b, "DoorWood")             # back shallower than the frame's
for xa, xb, ha, hb in ((bx0 - 0.08, bx0 + 0.005, h0b - 0.08, h1b + 0.08), (bx1 - 0.005, bx1 + 0.08, h0b - 0.08, h1b + 0.08),
                       (bx0 + 0.005, bx1 - 0.005, h1b - 0.005, h1b + 0.08), (bx0 + 0.005, bx1 - 0.005, h0b - 0.08, h0b + 0.005)):
    w.box(xa, zf - 0.05, xb, zf + 0.02, ha, hb, "TrimPaint")


def wall_lantern(lx, zface, sgn, h):
    """Bracket lantern on a wall face (sgn = -1: wall faces -z)."""
    lz = zface + sgn * 0.18
    za_, zb_ = sorted((zface - sgn * 0.02, zface + sgn * 0.18))     # arm, 2 cm into the wall
    w.box(lx - 0.03, za_, lx + 0.03, zb_, h + 0.18, h + 0.22, "Iron")
    w.box(lx - 0.1, lz - 0.1, lx + 0.1, lz + 0.1, h, h + 0.03, "Iron")
    w.box(lx - 0.1, lz - 0.1, lx + 0.1, lz + 0.1, h + 0.23, h + 0.26, "Iron")
    w.box(lx - 0.075, lz - 0.075, lx + 0.075, lz + 0.075, h + 0.03, h + 0.23, "LampGlow")
    for px in (-0.09, 0.09):
        for pz in (-0.09, 0.09):
            w.box(lx + px - 0.01, lz + pz - 0.01, lx + px + 0.01, lz + pz + 0.01, h + 0.03, h + 0.23, "Iron")
    w.hull([bl(lx, lz, h + 0.38)] + [bl(lx + x, lz + y, h + 0.26) for x in (-0.11, 0.11) for y in (-0.1, 0.1)], "Iron")


wall_lantern(-1.25, zf, -1, F1 + 2.1)
wall_lantern(5.65, zf, -1, F1 + 2.1)

# cornice under the terrace: facade band, then the wing bands stop at its front
w.wall(W(ZB - WALL_T / 2 - 0.03), E(ZB - WALL_T / 2 - 0.03), F2 - 0.16, FACADE_TOP - 0.005, "TrimPaint", t=0.07)
for sgn in (1, -1):
    if sgn > 0:
        a_, b_ = W(ZB - WALL_T / 2 - 0.065 - 0.004), W(ZA - 0.071)
    else:
        a_, b_ = E(ZA - 0.071), E(ZB - WALL_T / 2 - 0.065 - 0.004)
    d_ = (Vector(b_) - Vector(a_)).normalized()
    n_ = Vector((d_.y, -d_.x))
    o_ = -n_ * 0.03                                    # 6 cm band, 2.5 cm buried in the wing wall
    w.wall(tuple(Vector(a_) + o_ + n_ * 0.005), tuple(Vector(b_) + o_ + n_ * 0.005), F2 - 0.16, F2 - 0.02, "TrimPaint", t=0.07)


def wing_window(x_face, inward):
    """Flat shuttered window on a wing wall face (the wing rooms are not part of the arena)."""
    X = lambda o: x_face + inward * o
    xs = lambda o0, o1: tuple(sorted((X(o0), X(o1))))
    z0, z1 = WIN_ZC - WIN_W / 2, WIN_ZC + WIN_W / 2
    ha, hb = F1 + WIN_H[0], F1 + WIN_H[1]
    xa, xb = xs(-0.01, 0.012)
    w.box(xa, z0, xb, z1, ha, hb, "Glass")
    xa, xb = xs(-0.02, 0.045)
    w.box(xa, z0 - 0.1, xb, z0 + 0.01, ha - 0.05, hb + 0.03, "TrimPaint")
    w.box(xa, z1 - 0.01, xb, z1 + 0.1, ha - 0.05, hb + 0.03, "TrimPaint")
    xa, xb = xs(-0.03, 0.05)                                                     # head (back deeper than jambs)
    w.box(xa, z0 - 0.14, xb, z1 + 0.14, hb + 0.03, hb + 0.15, "TrimPaint")
    xa, xb = xs(-0.025, 0.085)                                                   # sill
    w.box(xa, z0 - 0.12, xb, z1 + 0.12, ha - 0.09, ha - 0.03, "TrimPaint")
    xa, xb = xs(0.008, 0.032)                                                    # muntins on the glass
    w.box(xa, WIN_ZC - 0.02, xb, WIN_ZC + 0.02, ha + 0.005, hb - 0.005, "TrimPaint")
    xa, xb = xs(0.012, 0.04)
    w.box(xa, z0 + 0.01, xb, z1 - 0.01, (ha + hb) / 2 - 0.02, (ha + hb) / 2 + 0.02, "TrimPaint")
    for za_ in (z0 - 0.62, z1 + 0.12):                                          # open shutters, flat on the wall
        shutter(w, (x_face, za_, ha - 0.02), (0, 1, 0), (0, 0, 1), (inward, 0, 0), 0.5, hb - ha + 0.02)


wing_window(west_x(WIN_ZC), 1)
wing_window(east_x(WIN_ZC), -1)
w.finish()

# ---------------------------------------------------------------- statue on a pedestal wrapping Kolon_2
k2 = column_bounds("Kolon_2")
cx, cz = (k2[0] + k2[2]) / 2, (k2[1] + k2[3]) / 2
st = Part("ZM_K1_Statue", root)
hx, hz = (k2[2] - k2[0]) / 2 + 0.12, (k2[3] - k2[1]) / 2 + 0.12      # die: column + 12 cm on every side
st.box(cx - hx - 0.12, cz - hz - 0.12, cx + hx + 0.12, cz + hz + 0.12, F1 - 0.03, F1 + 0.28, "Marble")
st.box(cx - hx - 0.06, cz - hz - 0.06, cx + hx + 0.06, cz + hz + 0.06, F1 + 0.28, F1 + 0.36, "Marble")
st.box(cx - hx, cz - hz, cx + hx, cz + hz, F1 + 0.36, F1 + 2.1, "Marble")
st.box(cx - hx - 0.08, cz - hz - 0.08, cx + hx + 0.08, cz + hz + 0.08, F1 + 2.1, F1 + 2.24, "Marble")
st.box(cx - hx - 0.02, cz - hz - 0.02, cx + hx + 0.02, cz + hz + 0.02, F1 + 2.24, F1 + 2.4, "Marble")
st.box(cx - 0.3, cz - hz - 0.012, cx + 0.3, cz - hz + 0.01, F1 + 1.3, F1 + 1.6, "Bronze")      # plaque
base = F1 + 2.4
P = lambda dx, dz, h: bl(cx + dx, cz + dz, base + h)


def ring(h, rx, rz, n=10):
    return [P(rx * math.cos(2 * math.pi * k / n), rz * math.sin(2 * math.pi * k / n), h) for k in range(n)]


st.cyl(cx, cz, base - 0.01, base + 0.06, 0.36, "Marble", segs=24)                 # round foot plate
st.smooth = True
# robed woman facing the courtyard (-z), arms raised to the sides, each hand lifting a caged lantern globe
st.lathe(cx, cz, [(base + h_, r_) for h_, r_ in
                  [(0.06, 0.34), (0.14, 0.32), (0.32, 0.28), (0.56, 0.23), (0.8, 0.18), (0.96, 0.15), (1.07, 0.13)]],
         "Bronze", segs=40, squash=0.82, folds=9, fold_amp=0.13)                      # skirt, deeper folds at the hem
sk_pts = [(0, 0, 0.9), (0, 0, 1.03), (0, -0.01, 1.2), (0, 0.005, 1.35), (0, 0, 1.44)]
sk_rad = [(0.15, 0.12), (0.125, 0.095), (0.17, 0.12), (0.075, 0.065), (0.055, 0.055)]
sk_edges = [(0, 1), (1, 2), (2, 3), (3, 4)]
for sg in (-1, 1):                                                                   # shoulder, elbow, wrist, hand
    i0 = len(sk_pts)
    sk_pts += [(sg * 0.17, 0, 1.33), (sg * 0.33, -0.03, 1.4), (sg * 0.42, -0.06, 1.58), (sg * 0.44, -0.07, 1.65)]
    sk_rad += [(0.075, 0.07), (0.058, 0.058), (0.044, 0.044), (0.05, 0.035)]
    sk_edges += [(3, i0), (i0, i0 + 1), (i0 + 1, i0 + 2), (i0 + 2, i0 + 3)]
st.skin([P(*q) for q in sk_pts], sk_edges, sk_rad, "Bronze", levels=2)
belt = [P(0.14 * math.cos(2 * math.pi * k / 24), 0.105 * math.sin(2 * math.pi * k / 24), 1.03) for k in range(25)]
st.pipe(belt, [0.02] * 25, "Bronze", segs=6, cap=False)                              # girdle
st.pipe([P(-0.16, -0.06, 1.31), P(-0.05, -0.115, 1.22), P(0.07, -0.11, 1.12), P(0.15, -0.08, 1.04)],
        [0.045, 0.05, 0.05, 0.04], "Bronze", segs=8)                                 # shawl across the chest
st.pipe([P(0, 0.01, 1.43), P(0, 0.0, 1.5)], [0.05, 0.048], "Bronze", segs=10)        # neck
st.sphere(P(0, -0.012, 1.58), 0.1, "Bronze", segs=20, rings=14, scale=(0.82, 0.92, 1.12))   # head, chin lifted
st.hull([P(dx, -0.105, h) for dx in (-0.012, 0.012) for h in (1.57, 1.6)] + [P(0, -0.128, 1.565)], "Bronze")  # nose
st.sphere(P(0, 0.016, 1.61), 0.104, "Bronze", segs=20, rings=14, scale=(0.9, 1.0, 1.05))    # hair, swept back
for sg in (-1, 1):
    st.sphere(P(sg * 0.075, 0.03, 1.55), 0.055, "Bronze", segs=12, rings=8, scale=(0.8, 1.1, 1.3))   # hair falling at the sides
st.sphere(P(0, 0.105, 1.64), 0.062, "Bronze", segs=14, rings=10)                             # bun
wreath = [P(0.093 * math.cos(2 * math.pi * k / 20), 0.016 + 0.1 * math.sin(2 * math.pi * k / 20), 1.66) for k in range(21)]
st.pipe(wreath, [0.016] * 21, "Bronze", segs=6, cap=False)                                    # laurel band
# wings: two fans of feather blades rising behind the shoulders, swept back toward the house
for sg in (-1, 1):
    for layer, (n_f, l0, wd, back) in enumerate(((7, 1.0, 0.085, 0.75), (5, 0.55, 0.07, 0.6))):   # flight feathers, coverts
        root_ = P(sg * 0.08, 0.11 - 0.03 * layer, 1.28 + 0.02 * layer)   # coverts sit in front: no shared root faces
        for i in range(n_f):
            ang = math.radians(32 + 6 * layer + 11 * i)    # fan from steep (near the head) to wide
            L = l0 - 0.07 * i
            d = Vector((-sg * math.sin(ang), -back, math.cos(ang) * 0.9)).normalized()   # Blender: -x = venue +x, -y = back
            side = d.cross(Vector((0, 1, 0))).normalized() * wd
            tip = root_ + d * L + Vector((0, 0, -0.04 * i))
            mid = root_ + d * L * 0.55
            th = Vector((0, 0.014 + 0.01 * layer, 0))
            st.hull([root_ + side * 0.4, root_ - side * 0.4, mid + side, mid - side, tip,
                     root_ + side * 0.4 + th, mid + side + th, mid - side + th, tip + th], "Bronze")
for sg in (-1, 1):
    hx, hz = sg * 0.44, -0.07
    st.cyl(cx + hx, cz + hz, base + 1.63, base + 1.71, 0.035, "Bronze", segs=16, r1=0.07)    # cup on the palm
    gc = P(hx, hz, 1.83)
    st.sphere(gc, 0.12, "LampGlow", segs=18, rings=12)                                       # glowing globe
    for k in range(3):                                                                       # bronze cage rings
        ax = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0.7, 0.7, 0))][k]
        up = Vector((0, 0, 1)) if k < 3 else ax
        pts = [gc + (ax * math.cos(2 * math.pi * j / 24) + up * math.sin(2 * math.pi * j / 24)) * 0.128 for j in range(25)]
        st.pipe(pts, [0.008] * 25, "Bronze", segs=5, cap=False)
    st.cyl(cx + hx, cz + hz, base + 1.945, base + 2.01, 0.035, "Bronze", segs=12, r1=0.0)   # finial
st.smooth = False
st.finish()

# ---------------------------------------------------------------- hide the graybox pieces this part replaces
for n_ in ("K1_Rail_Balcony_S", "K1_WingWall_W", "K1_WingWall_E", "K1_Facade_BC", "K1_Pedestal_Kolon2", "K1_Statue_Kolon2"):
    ob = bpy.data.objects.get(n_)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    print(f"{ob.name}: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
