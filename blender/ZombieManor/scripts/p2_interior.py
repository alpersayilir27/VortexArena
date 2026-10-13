"""Part 2: ground-floor interior (strips B + C) - floors, walls with wainscot + crown molding, plaster ceiling,
foyer post (Kolon_2), chimney breast + fireplace (Kolon_1), breach + door in the B|C wall, boarded side doors,
and the P1 cage lift (floor portal 0 <-> 1)."""
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
from zm_common import (Part, bl, west_x, east_x, zone_poly, column_bounds, collection, clear_collection,
                       NW, NE, ZONE_Z, CEIL, STOREY, DOOR_H, WIN_Z, WALL_T, PORTALS)
from zm_rooms import (SINK, north_z, s_x, s_z, lining, north_window, fake_door, chimney_breast, lift_cage, wall_decay,
                      panel_door)

ZA, ZB = ZONE_Z
W = lambda z: (west_x(z), z)
E = lambda z: (east_x(z), z)
W0, E0, WB, EB = W(ZA), E(ZA), W(ZB), E(ZB)

BC_BREACH = (-0.6, 0.8)          # ragged hole foyer -> salon (west)
BC_DOOR = (3.9, 5.1)             # framed doorway foyer -> salon (east)
FRONT_DOOR, FRONT_BREACH = (0.3, 1.7), (4.2, 5.4)
N_WINDOWS = ((0.4, 1.5), (4.3, 5.5))
FAKE_DOOR_W = (4.3, 5.3)         # boarded door into the west wing (z range on the west wall)
FAKE_DOOR_E = (4.0, 5.0)         # boarded door into the east wing (z range on the east wall)
# chimney breast around Kolon_1: fireplace opens west into the salon, mass runs back into the north wall
k1 = column_bounds("Kolon_1")
BREAST_X = (1.95, k1[2] + 0.12)
BREAST_Z = (6.55, 8.85)          # north end buried in the north wall
FIRE_Z = (6.9, 7.7)

root = collection("ZM_P2_Interior")
clear_collection(root)
rnd = random.Random(21)

# ---------------------------------------------------------------- floors
fl = Part("ZM_K0_Floors", root)
fl.prism(zone_poly("B"), -0.10, 0.0, "Checker")
fl.prism(zone_poly("C"), -0.10, 0.0, "Parquet")
fl.finish()

# ---------------------------------------------------------------- walls
w = Part("ZM_K0_Walls", root)
# perimeter on the boundary (inner face on the line); right side of a CCW edge = outside
west_gaps = [(s_z(NW, W0, FAKE_DOOR_W[1]) - 0.12, s_z(NW, W0, FAKE_DOOR_W[0]) + 0.12, "door")]
# side walls run to the facade's outer face so the house corner has no notch
w.wall(NW, W(ZA - 0.075), 0.0, STOREY, "Siding", "Wallpaper", "Trim", side=1)
lining(w, NW, W0, WALL_T / 2, -1, WALL_T, gaps=west_gaps, trim=(True, False))

n_ops = [(s_x(NE, NW, x1), s_x(NE, NW, x0), WIN_Z[1], WIN_Z[0]) for x0, x1 in N_WINDOWS]
w.wall(NE, NW, 0.0, STOREY, "Siding", "Wallpaper", "Trim", side=1, openings=n_ops)
lining(w, NE, NW, WALL_T / 2, -1, WALL_T)

east_gaps = [(s_z(E0, NE, FAKE_DOOR_E[0]) - 0.12, s_z(E0, NE, FAKE_DOOR_E[1]) + 0.12, "door")]
w.wall(E(ZA - 0.075), NE, 0.0, STOREY, "Siding", "Wallpaper", "Trim", side=1)
lining(w, E0, NE, WALL_T / 2, -1, WALL_T, gaps=east_gaps, trim=(False, True))

# B|C wall: breach (west) + framed doorway (east); both faces papered
sb = lambda x: s_x(WB, EB, x)
bc_ops = [(sb(BC_BREACH[0]), sb(BC_BREACH[1]), DOOR_H), (sb(BC_DOOR[0]), sb(BC_DOOR[1]), DOOR_H)]
# inner walls stay off the perimeter's top/bottom planes (both are hidden: below grade / inside the slab)
w.wall(WB, EB, -0.035, STOREY - 0.05, "Wallpaper", "Wallpaper", "Wallpaper",
       openings=[(s0_, s1_, top + 0.035) for s0_, s1_, top in bc_ops])
bc_gaps = [(sb(BC_BREACH[0]), sb(BC_BREACH[1]), "hole"), (sb(BC_DOOR[0]) - 0.1, sb(BC_DOOR[1]) + 0.1, "door")]
for fs in (1, -1):
    lining(w, WB, EB, 0.0, fs, WALL_T, gaps=bc_gaps, trim=(True, True))

# front facade's inner face (the wall itself is Part 1; its plinth is 0.19 thick)
sf = lambda x: s_x(W0, E0, x)
lining(w, W0, E0, 0.0, -1, 0.19, gaps=[(sf(FRONT_DOOR[0]) - 0.1, sf(FRONT_DOOR[1]) + 0.1, "door"),
                                       (sf(FRONT_BREACH[0]), sf(FRONT_BREACH[1]), "hole")], trim=(True, True), crown=False)
# inner casing of the front door: back buried 5 mm in the upper wall (the plinth is deeper still)
for xa, xb in ((FRONT_DOOR[0] - 0.1, FRONT_DOOR[0] + 0.01), (FRONT_DOOR[1] - 0.01, FRONT_DOOR[1] + 0.1)):
    w.box(xa, ZA + 0.07, xb, ZA + 0.135, -0.03, DOOR_H + 0.02, "Trim")
w.box(FRONT_DOOR[0] - 0.14, ZA + 0.07, FRONT_DOOR[1] + 0.14, ZA + 0.145, DOOR_H + 0.02, DOOR_H + 0.16, "Trim")
# the upper facade is thinner than its plinth: its crown starts at the 0.15 m wall face, 0.08 deep
d_ = (Vector(E0) - Vector(W0)).normalized()
n_ = Vector((d_.y, -d_.x))
off_ = -(WALL_T / 2 + 0.04)
L_f = (Vector(E0) - Vector(W0)).length
w.wall(tuple(Vector(W0) + d_ * 0.06 + n_ * off_), tuple(Vector(W0) + d_ * (L_f - 0.06) + n_ * off_), CEIL - 0.12, CEIL,
       "Trim", t=0.08)

for x0, x1 in N_WINDOWS:
    north_window(w, x0, x1, 0.0, WIN_Z)
fake_door(w, west_x(sum(FAKE_DOOR_W) / 2), 1, *FAKE_DOOR_W)
fake_door(w, east_x(sum(FAKE_DOOR_E) / 2), -1, *FAKE_DOOR_E)

# east doorway in the B|C wall: casings on both faces + leaf swung into the salon
for fs in (1, -1):
    zc = ZB - fs * (WALL_T / 2)            # face plane: fs=+1 -> south (foyer) face
    zz = lambda o: zc - fs * o
    za, zb = sorted((zz(-0.02), zz(0.04)))
    for xa, xb in ((BC_DOOR[0] - 0.1, BC_DOOR[0] + 0.01), (BC_DOOR[1] - 0.01, BC_DOOR[1] + 0.1)):
        w.box(xa, za, xb, zb, -0.03, DOOR_H + 0.02, "Trim")
    za, zb = sorted((zz(-0.02), zz(0.05)))
    w.box(BC_DOOR[0] - 0.14, za, BC_DOOR[1] + 0.14, zb, DOOR_H + 0.02, DOOR_H + 0.16, "Trim")
leaf_w = BC_DOOR[1] - BC_DOOR[0] - 0.02
a4 = math.radians(4)
panel_door(w, (BC_DOOR[1] + 0.03, ZB + 0.095, 0.01), (math.sin(a4), math.cos(a4), 0), (0, 0, 1),
           (math.cos(a4), -math.sin(a4), 0), leaf_w, DOOR_H - 0.06)                # leaf swung into the salon

# breach: plaster chunks + splintered lath around the ragged hole, rubble on both sides
for x_edge, sgn in ((BC_BREACH[0], 1), (BC_BREACH[1], -1)):
    for hh in (0.3, 0.7, 1.1, 1.5, 1.85):
        ln = rnd.uniform(0.04, 0.13)
        # chunk rooted 2 cm in the jamb
        w.obox((x_edge + sgn * (ln / 2 - 0.02), ZB + rnd.uniform(-0.005, 0.005), hh), (ln, 0.24, rnd.uniform(0.12, 0.3)),
               "Wallpaper", rot=(0, rnd.uniform(-25, 25), 0))
for i in range(6):
    xx = rnd.uniform(BC_BREACH[0] + 0.1, BC_BREACH[1] - 0.1)
    w.obox((xx, ZB, DOOR_H - 0.08 - rnd.uniform(0, 0.05)), (0.025, 0.12, rnd.uniform(0.1, 0.18)), "Wainscot",
           rot=(0, rnd.uniform(-30, 30), 0))
for i in range(10):
    w.obox((rnd.uniform(BC_BREACH[0] - 0.3, BC_BREACH[1] + 0.3), ZB + rnd.choice((-1, 1)) * rnd.uniform(0.2, 0.7), 0.025),
           (rnd.uniform(0.08, 0.35), rnd.uniform(0.05, 0.18), 0.035), rnd.choice(("Wallpaper", "Ceiling", "Wainscot")),
           rot=(rnd.uniform(-6, 6), rnd.uniform(-6, 6), rnd.uniform(0, 180)))

# fallen paper: bare plaster patches, some with a curled strip (s, h, w, h-size, curl)
wall_decay(w, NW, W0, WALL_T / 2, -1, WALL_T, [(s_z(NW, W0, 6.0), 1.75, 0.5, 0.7, True),
                                               (s_z(NW, W0, 3.0), 1.9, 0.35, 0.5, False),
                                               (s_z(NW, W0, 0.5), 1.6, 0.45, 0.6, True)], seed=3)
wall_decay(w, NE, NW, WALL_T / 2, -1, WALL_T, [(s_x(NE, NW, 5.95), 1.8, 0.4, 0.8, True)], seed=4)
wall_decay(w, E0, NE, WALL_T / 2, -1, WALL_T, [(s_z(E0, NE, 5.6), 1.7, 0.5, 0.7, True),
                                               (s_z(E0, NE, 2.6), 2.0, 0.4, 0.5, False)], seed=5)
wall_decay(w, WB, EB, 0.0, 1, WALL_T, [(sb(1.4), 1.6, 0.5, 0.8, True), (sb(5.65), 2.1, 0.35, 0.5, False)], seed=6)
wall_decay(w, WB, EB, 0.0, -1, WALL_T, [(sb(-1.3), 1.9, 0.4, 0.6, True)], seed=8)   # the roots own x 1-2.2 here
w.finish()

# ---------------------------------------------------------------- ceilings (underside of the Kat1 slab)
c = Part("ZM_K0_Ceiling", root)
# foyer: coffered plaster; the two beam depths differ, so neither bottom meets the crown's, tops end inside the slab
c.prism(zone_poly("B"), CEIL, CEIL + 0.06, "Plaster")
for zz in (0.5, 2.5):
    c.box(west_x(zz) - 0.02, zz - 0.08, east_x(zz) + 0.02, zz + 0.08, CEIL - 0.145, CEIL + 0.03, "Plaster")
for xx in (-0.6, 2.0, 4.6):
    c.box(xx - 0.07, ZA + 0.05, xx + 0.07, ZB - 0.05, CEIL - 0.125, CEIL + 0.04, "Plaster")
c.cyl(0.8, 1.5, CEIL - 0.03, CEIL, 0.32, "Plaster", segs=24)           # medallion over the chandelier
c.cyl(0.8, 1.5, CEIL - 0.06, CEIL - 0.03, 0.18, "Plaster", segs=16)
# salon: dark boards
c.prism(zone_poly("C"), CEIL, CEIL + 0.06, "Ceiling")
for mx, mz in ((0.3, 5.4), (4.7, 5.4)):
    c.cyl(mx, mz, CEIL - 0.03, CEIL, 0.32, "Trim", segs=20)
    c.cyl(mx, mz, CEIL - 0.06, CEIL - 0.03, 0.18, "Trim", segs=16)
c.finish()

# ---------------------------------------------------------------- foyer post wrapping Kolon_2
k2 = column_bounds("Kolon_2")
po = Part("ZM_K0_FoyerPost", root)
px0, pz0, px1, pz1 = k2[0] - 0.1, k2[1] - 0.1, k2[2] + 0.1, k2[3] + 0.1
# panelled pier: marble plinth, stepped base mould, shaft with sunk panels + raised fields on every face,
# brass band at rail height, stacked capital mouldings up into the ceiling
po.box(px0 - 0.07, pz0 - 0.07, px1 + 0.07, pz1 + 0.07, -SINK, 0.16, "Marble")
po.box(px0 - 0.045, pz0 - 0.045, px1 + 0.045, pz1 + 0.045, 0.16, 0.28, "Trim")
po.box(px0 - 0.025, pz0 - 0.025, px1 + 0.025, pz1 + 0.025, 0.28, 0.32, "Trim")
# the capital hangs off the ceiling, not off a fixed height: on a taller storey the shaft grows instead
CAP = CEIL - 0.34
po.box(px0, pz0, px1, pz1, 0.32, CAP, "Trim")                                           # shaft
for (ax, a0, a1, c, sg) in (("x", pz0, pz1, px0, -1), ("x", pz0, pz1, px1, 1), ("z", px0, px1, pz0, -1), ("z", px0, px1, pz1, 1)):
    P_ = (lambda a, h, d: (c + sg * d, a, h)) if ax == "x" else (lambda a, h, d: (a, c + sg * d, h))
    for h0, h1 in ((0.42, 0.84), (1.0, CAP - 0.12)):                                    # panels below / above the band
        # sunk moulded border, then a raised field with bevelled sides so light picks it out
        o0, o1 = sorted((c + sg * -0.008, c + sg * 0.006))
        if ax == "x":
            po.box(o0, a0 + 0.05, o1, a1 - 0.05, h0, h1, "Trim")
        else:
            po.box(a0 + 0.05, o0, a1 - 0.05, o1, h0, h1, "Trim")
        po.hull([bl(*P_(a, h, d)) for d, ins in ((0.002, 0.075), (0.022, 0.11))
                 for a in (a0 + ins, a1 - ins) for h in (h0 + ins - 0.05, h1 - ins + 0.05)], "Trim")
po.box(px0 - 0.03, pz0 - 0.03, px1 + 0.03, pz1 + 0.03, 0.88, 0.95, "Gold")              # brass band
po.box(px0 - 0.03, pz0 - 0.03, px1 + 0.03, pz1 + 0.03, CAP, CAP + 0.07, "Trim")          # capital mouldings
po.box(px0 - 0.06, pz0 - 0.06, px1 + 0.06, pz1 + 0.06, CAP + 0.07, CAP + 0.12, "Trim")
po.box(px0 - 0.1, pz0 - 0.1, px1 + 0.1, pz1 + 0.1, CAP + 0.12, CEIL + 0.02, "Trim")
po.finish()

# ---------------------------------------------------------------- chimney breast + fireplace wrapping Kolon_1
fp = Part("ZM_K0_Fireplace", root)
chimney_breast(fp, 0.0, BREAST_X, BREAST_Z, FIRE_Z)
fp.finish()

# ---------------------------------------------------------------- P1 cage lift (floor portal 0 <-> 1)
px, pz = PORTALS[(0, 1)]
L_, R_ = Part("ZM_K0_LiftCage", root), Part("ZM_K0_LiftCageRoof", root)      # roof: under ArenaRoof in Unity
lift_cage(L_, px, pz, 0.0, 1, needle_deg=0.0, roof=R_)
L_.finish()
R_.finish()

# ---------------------------------------------------------------- hide graybox pieces this part replaces
for n_ in ("K0_Floor_B", "K0_Floor_C", "K0_Wall_W", "K0_Wall_N", "K0_Wall_E", "K0_Wall_BC", "K0_Partition",
           "K0_Post_Kolon2", "K0_BoothP01_Side", "K0_BoothP01_Front", "K1_Slab_Balcony", "K1_Slab_Rooms",
           "ALL_Chimney_Kolon1"):
    ob = bpy.data.objects.get(n_)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"{ob.name}: {tris} tris")
