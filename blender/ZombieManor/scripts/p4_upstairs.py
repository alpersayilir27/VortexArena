"""Part 4: Kat1 interior (strip C) - bedroom (west) and study (east) split by a partition with a doorway,
chimney breast with a second fireplace (Kolon_1), wainscot + crown, plaster ceiling, boarded side doors,
north window, the P1 arrival cage and the P2 departure cage."""
import importlib
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)
import zm_rooms as zr
importlib.reload(zr)
from zm_common import (Part, west_x, east_x, zone_poly, column_bounds, collection, clear_collection,
                       NW, NE, FLOORS, SLAB, ZONE_Z, CEIL, WALL_T, PORTALS)
from zm_rooms import north_z, s_x, s_z, lining, north_window, fake_door, chimney_breast, lift_cage, wall_decay

ZA, ZB = ZONE_Z
F1, F2 = FLOORS[1], FLOORS[2]
W = lambda z: (west_x(z), z)
E = lambda z: (east_x(z), z)
WB, EB = W(ZB), E(ZB)

DOORS = ((-0.65, 0.55), (3.9, 5.1))     # the balcony's arched doors (Part 3), seen from the rooms
k1 = column_bounds("Kolon_1")
BREAST_X = (1.95, k1[2] + 0.12)         # same chimney mass as Kat0
BREAST_Z = (6.55, 8.85)
FIRE_Z = (6.9, 7.7)
PART_X = 2.64                           # bedroom | study partition, meets the breast's south face
PART_DOOR = (4.3, 5.5)                  # doorway in the partition (z range)
N_WINDOW = (0.4, 1.5)                   # bedroom window (the study's north wall holds the P2 cage)
FAKE_W = (4.3, 5.3)
FAKE_E = (4.3, 5.3)

root = collection("ZM_P4_Upstairs")
clear_collection(root)

# ---------------------------------------------------------------- floor slab (top = Kat1 floor)
s = Part("ZM_K1_RoomsSlab", root)
s.prism(zone_poly("C"), F1 - SLAB + 0.06, F1, "FloorWood")
s.finish()

# ---------------------------------------------------------------- walls
w = Part("ZM_K1_Walls", root)
w.wall(NW, WB, F1, F2, "Siding", "WallpaperGrey", "Trim", side=1)
lining(w, NW, WB, WALL_T / 2, -1, WALL_T, fh=F1, trim=(True, False),
       gaps=[(s_z(NW, WB, FAKE_W[1]) - 0.12, s_z(NW, WB, FAKE_W[0]) + 0.12, "door")])
w.wall(NE, NW, F1, F2, "Siding", "WallpaperGrey", "Trim", side=1,
       openings=[(s_x(NE, NW, N_WINDOW[1]), s_x(NE, NW, N_WINDOW[0]), 2.2, 0.9)])
lining(w, NE, NW, WALL_T / 2, -1, WALL_T, fh=F1)
w.wall(EB, NE, F1, F2, "Siding", "WallpaperGrey", "Trim", side=1)
lining(w, EB, NE, WALL_T / 2, -1, WALL_T, fh=F1, trim=(False, True),
       gaps=[(s_z(EB, NE, FAKE_E[0]) - 0.12, s_z(EB, NE, FAKE_E[1]) + 0.12, "door")])
north_window(w, *N_WINDOW, F1)
fake_door(w, west_x(sum(FAKE_W) / 2), 1, *FAKE_W, fh=F1)
fake_door(w, east_x(sum(FAKE_E) / 2), -1, *FAKE_E, fh=F1)

# facade's room face (the wall is Part 3): wainscot below, crown only between the arches
sb = lambda x: s_x(WB, EB, x)
lining(w, WB, EB, 0.0, -1, WALL_T, fh=F1, trim=(True, True), crown=False,
       gaps=[(sb(x0) - 0.08, sb(x1) + 0.08, "door") for x0, x1 in DOORS])
zc = ZB + WALL_T / 2 + (0.06 - 0.016) / 2
crown_runs = [(WB[0] + 0.064, DOORS[0][0] - 0.12), (DOORS[0][1] + 0.12, DOORS[1][0] - 0.12), (DOORS[1][1] + 0.12, EB[0] - 0.064)]
for xa, xb in crown_runs:
    w.wall((xa, zc), (xb, zc), F1 + CEIL - 0.12, F1 + CEIL, "Trim", t=0.076)

# partition bedroom | study: from inside the facade to inside the breast; doorway with casings
pa, pb = (PART_X, ZB - 0.02), (PART_X, BREAST_Z[0] + 0.03)      # start off the slab/ceiling edge plane at ZB
sp = lambda z: z - pa[1]
w.wall(pa, pb, F1 - 0.045, F2 - 0.05, "WallpaperGrey", "WallpaperGrey", "WallpaperGrey",
       openings=[(sp(PART_DOOR[0]), sp(PART_DOOR[1]), 2.2 + 0.045)])
start = lambda depth, embed: -(0.02 + WALL_T / 2 + depth + 0.004)   # stop at the facade lining's front
end = lambda depth, embed: -(0.03 + depth + 0.004)              # stop at the breast lining's front
for fs in (1, -1):
    lining(w, pa, pb, 0.0, fs, WALL_T, fh=F1, ext=(start, end),
           gaps=[(sp(PART_DOOR[0]) - 0.1, sp(PART_DOOR[1]) + 0.1, "door")])
    xf = PART_X + fs * WALL_T / 2                               # fs=+1: east (study) face
    xa, xb = sorted((xf - fs * 0.02, xf + fs * 0.04))
    for za, zb in ((PART_DOOR[0] - 0.1, PART_DOOR[0] + 0.01), (PART_DOOR[1] - 0.01, PART_DOOR[1] + 0.1)):
        w.box(xa, za, xb, zb, F1 - 0.03, F1 + 2.22, "Trim")
    xa, xb = sorted((xf - fs * 0.02, xf + fs * 0.05))
    w.box(xa, PART_DOOR[0] - 0.14, xb, PART_DOOR[1] + 0.14, F1 + 2.22, F1 + 2.36, "Trim")

# fallen paper (s, h, w, h-size, curl)
wall_decay(w, NW, WB, WALL_T / 2, -1, WALL_T, [(s_z(NW, WB, 3.9), 1.8, 0.4, 0.7, True)], fh=F1,
           paper="WallpaperGrey", seed=11)
wall_decay(w, EB, NE, WALL_T / 2, -1, WALL_T, [(s_z(EB, NE, 6.2), 1.7, 0.45, 0.7, True)], fh=F1,
           paper="WallpaperGrey", seed=12)
wall_decay(w, pa, pb, 0.0, 1, WALL_T, [(sp(3.95), 1.9, 0.35, 0.5, False)], fh=F1, paper="WallpaperGrey", seed=13)
w.finish()

# ---------------------------------------------------------------- ceiling (underside of the terrace slab)
c = Part("ZM_K1_Ceiling", root)
c.prism(zone_poly("C"), F1 + CEIL, F1 + CEIL + 0.06, "Ceiling")
for mx, mz in ((0.4, 5.2), (4.4, 5.6)):
    c.cyl(mx, mz, F1 + CEIL - 0.03, F1 + CEIL, 0.32, "Trim", segs=20)
    c.cyl(mx, mz, F1 + CEIL - 0.06, F1 + CEIL - 0.03, 0.18, "Trim", segs=16)
c.finish()

# ---------------------------------------------------------------- chimney breast (Kolon_1) with the bedroom fireplace
fp = Part("ZM_K1_Fireplace", root)
chimney_breast(fp, F1, BREAST_X, BREAST_Z, FIRE_Z, paper="WallpaperGrey", hearth_d=0.3, mirror=False)   # bed beside it
fp.finish()

# ---------------------------------------------------------------- cages: P1 arrival (west), P2 departure (east)
for key, sgn, needle, name in (((0, 1), 1, -60.0, "ZM_K1_LiftCageP01"), ((1, 2), -1, 0.0, "ZM_K1_LiftCageP12")):
    px, pz = PORTALS[key]
    L_, R_ = Part(name, root), Part(name + "Roof", root)                     # roof: under ArenaRoof in Unity
    lift_cage(L_, px, pz, F1, sgn, needle_deg=needle, roof=R_)
    L_.finish()
    R_.finish()

# ---------------------------------------------------------------- hide graybox pieces this part replaces
for n_ in ("K1_Wall_W", "K1_Wall_N", "K1_Wall_E", "K1_Partition", "K1_BoothP01_Side", "K1_BoothP01_Front",
           "K1_BoothP12_Side", "K1_BoothP12_Front"):
    ob = bpy.data.objects.get(n_)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    print(f"{ob.name}: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
