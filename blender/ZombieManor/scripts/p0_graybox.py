"""Part 0: graybox massing of the three-floor manor on the venue outline (layout approval, not export)."""
import importlib
import os
import sys

import bpy

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)

from zm_common import (FLOORS, CEIL, SLAB, STOREY, DOOR_H, SHED_TOP, ZONE_Z, RAIL_H, PORTALS, PORTAL_R,
                       NW, SW, SE, NE, west_x, east_x, zone_poly, column_bounds, box, prism, wall, disc,
                       collection, clear_collection)

ZA, ZB = ZONE_Z
PART_X = 2.8          # north-south partition line through the fireplace column
PART_Z0 = 5.6         # partition starts here; south of it the rooms stay open
NORTH_AT = lambda x: NW[1] + (NE[1] - NW[1]) * (x - NW[0]) / (NE[0] - NW[0])

root = collection("ZM_P0_Graybox")
clear_collection(root)
c_ven = collection("P0_Venue", root)
c_f = [collection(f"P0_Kat{i}", root) for i in range(3)]
c_mark = collection("P0_Markers", root)
c_out = collection("P0_Outside", root)

W = lambda z: (west_x(z), z)
E = lambda z: (east_x(z), z)

# ---------------------------------------------------------------- venue: outline per floor + physical columns
for i, h in enumerate(FLOORS):
    for k, (a, b) in enumerate(((NW, SW), (SW, SE), (SE, NE), (NE, NW))):
        wall(f"VEN_Outline_K{i}_{k}", a, b, h + 0.003, h + 0.03, "column", c_ven, t=0.04, side=-1)
for name, pts, _ in zm.COLUMNS:
    prism(f"PHYS_{name}", pts, 0.0, FLOORS[2] + CEIL, "column", c_ven)

# ---------------------------------------------------------------- Kat0 (ground): courtyard A, foyer B, rooms C
f0 = c_f[0]
prism("K0_Courtyard", zone_poly("A"), -0.10, 0.0, "cobble", f0)
prism("K0_Floor_B", zone_poly("B"), -0.10, 0.0, "floor", f0)
prism("K0_Floor_C", zone_poly("C"), -0.10, 0.0, "floor", f0)

# courtyard iron fence on the boundary (inner face on the line)
wall("K0_Fence_W", W(ZA), SW, 0.0, 1.6, "fence", f0, t=0.05, side=1)
wall("K0_Fence_S", SW, SE, 0.0, 1.6, "fence", f0, t=0.05, side=1)
wall("K0_Fence_E", SE, E(ZA), 0.0, 1.6, "fence", f0, t=0.05, side=1)
x0, z0, x1, z1 = column_bounds("Kolon_3")
box("K0_GatePost_Kolon3", x0 - 0.1, z0 - 0.08, x1 + 0.1, z1 + 0.1, 0.0, 2.4, "wall", f0)

# house front facade on the A|B line: front door + broken bay opening
wa = west_x(ZA)
wall("K0_Facade_Front", W(ZA), E(ZA), 0.0, STOREY, "ext", f0,
     openings=[(0.3 - wa, 1.7 - wa, DOOR_H), (4.2 - wa, 5.4 - wa, DOOR_H)])
box("K0_Canopy", 0.0, ZA - 0.9, 2.0, ZA - 0.08, 2.7, 2.85, "roof", f0)

# house perimeter (B + C) and the B|C wall
wall("K0_Wall_W", NW, W(ZA), 0.0, STOREY, "wall", f0, side=1)
wall("K0_Wall_N", NE, NW, 0.0, STOREY, "wall", f0, side=1)
wall("K0_Wall_E", E(ZA), NE, 0.0, STOREY, "wall", f0, side=1)
wb = west_x(ZB)
wall("K0_Wall_BC", W(ZB), E(ZB), 0.0, STOREY, "wall", f0,
     openings=[(-0.6 - wb, 0.8 - wb, DOOR_H), (3.8 - wb, 5.2 - wb, DOOR_H)])
wall("K0_Partition", (PART_X, PART_Z0), (PART_X, NORTH_AT(PART_X)), 0.0, STOREY, "wall", f0)

# Kolon_2 -> foyer post (Kat0), monument pedestal (Kat1 balcony)
x0, z0, x1, z1 = column_bounds("Kolon_2")
box("K0_Post_Kolon2", x0 - 0.1, z0 - 0.1, x1 + 0.1, z1 + 0.1, 0.0, STOREY, "wall", f0)
box("K1_Pedestal_Kolon2", x0 - 0.12, z0 - 0.12, x1 + 0.12, z1 + 0.12, FLOORS[1], FLOORS[1] + 2.4, "wall", c_f[1])
box("K1_Statue_Kolon2", x0 + 0.05, z0 + 0.0, x1 - 0.05, z1 + 0.0, FLOORS[1] + 2.4, FLOORS[1] + 4.0, "rail", c_f[1])

# Kolon_1 -> chimney stack through every floor (fireplace on Kat0, flue on Kat1, chimney top on the terrace)
x0, z0, x1, z1 = column_bounds("Kolon_1")
box("ALL_Chimney_Kolon1", x0 - 0.12, z0 - 0.15, x1 + 0.12, z1 + 0.15, 0.0, FLOORS[2] + 2.6, "ext", f0)


def portal_booth(tag, px, pz, h, coll, door_w=1.0):
    """Lift-booth alcove around a portal disc against the north boundary: side wall + front wall with a door."""
    west_side = px < (NW[0] + NE[0]) / 2
    x_in = px + 0.9 if west_side else px - 0.9          # inner side wall line
    z_front = pz - 0.7
    x_wall = west_x(z_front) if west_side else east_x(z_front)
    wall(f"{tag}_Side", (x_in, z_front), (x_in, NORTH_AT(x_in)), h, h + STOREY, "wall", coll)
    a, b = ((x_wall, z_front), (x_in, z_front)) if west_side else ((x_in, z_front), (x_wall, z_front))
    s_door = abs(px - a[0]) - door_w / 2
    wall(f"{tag}_Front", a, b, h, h + STOREY, "wall", coll, openings=[(s_door, s_door + door_w, DOOR_H)])


# ---------------------------------------------------------------- Kat1: balcony B (open air) + rooms C
h1 = FLOORS[1]
f1 = c_f[1]
prism("K1_Slab_Balcony", zone_poly("B"), h1 - SLAB, h1, "checker", f1)
prism("K1_Slab_Rooms", zone_poly("C"), h1 - SLAB, h1, "floor", f1)
wall("K1_Rail_Balcony_S", W(ZA), E(ZA), h1, h1 + RAIL_H, "rail", f1, t=0.12, side=-1)
wall("K1_WingWall_W", W(ZB), W(ZA), h1, h1 + STOREY, "ext", f1, side=1)
wall("K1_WingWall_E", E(ZA), E(ZB), h1, h1 + STOREY, "ext", f1, side=1)
wall("K1_Facade_BC", W(ZB), E(ZB), h1, h1 + STOREY, "ext", f1,
     openings=[(-0.6 - wb, 0.6 - wb, 2.5), (3.9 - wb, 5.1 - wb, 2.5)])
wall("K1_Wall_W", NW, W(ZB), h1, h1 + STOREY, "wall", f1, side=1)
wall("K1_Wall_N", NE, NW, h1, h1 + STOREY, "wall", f1, side=1)
wall("K1_Wall_E", E(ZB), NE, h1, h1 + STOREY, "wall", f1, side=1)
wall("K1_Partition", (PART_X, PART_Z0), (PART_X, NORTH_AT(PART_X)), h1, h1 + STOREY, "wall", f1)

# ---------------------------------------------------------------- Kat2: roof terrace C (open air)
h2 = FLOORS[2]
f2 = c_f[2]
prism("K2_Slab_Terrace", zone_poly("C"), h2 - SLAB, h2, "deck", f2)
wall("K2_Rail_S", W(ZB), E(ZB), h2, h2 + RAIL_H, "rail", f2, t=0.12, side=-1)
wall("K2_Rail_W", NW, W(ZB), h2, h2 + RAIL_H, "rail", f2, t=0.12, side=1)
wall("K2_Rail_N", NE, NW, h2, h2 + RAIL_H, "rail", f2, t=0.12, side=1)
wall("K2_Rail_E", E(ZB), NE, h2, h2 + RAIL_H, "rail", f2, t=0.12, side=1)

# ---------------------------------------------------------------- portals: booths + discs
(p1x, p1z), (p2x, p2z) = PORTALS[(0, 1)], PORTALS[(1, 2)]
portal_booth("K0_BoothP01", p1x, p1z, FLOORS[0], f0)
portal_booth("K1_BoothP01", p1x, p1z, h1, f1)
portal_booth("K1_BoothP12", p2x, p2z, h1, f1)
portal_booth("K2_ShedP12", p2x, p2z, h2, f2)
x_in = p2x - 0.9
box("K2_ShedP12_Roof", x_in - 0.1, p2z - 0.8, east_x(p2z) + 0.15, NORTH_AT(x_in) + 0.15,
    h2 + SHED_TOP - 0.2, h2 + SHED_TOP - 0.05, "roof", f2)
disc("MK_P01_Lower", p1x, p1z, FLOORS[0], PORTAL_R, "portal", c_mark)
disc("MK_P01_Upper", p1x, p1z, h1, PORTAL_R, "portal", c_mark)
disc("MK_P12_Lower", p2x, p2z, h1, PORTAL_R, "portal", c_mark)
disc("MK_P12_Upper", p2x, p2z, h2, PORTAL_R, "portal", c_mark)

# ---------------------------------------------------------------- void strips (no floor under a walking player)
for name, zone, h in (("VOID_K1_A", "A", h1), ("VOID_K2_A", "A", h2), ("VOID_K2_B", "B", h2)):
    o = prism(name, zone_poly(zone), h, h + 2.2, "void", c_mark)
    o.display_type = "WIRE"

box("MK_Player_1m80", 0.8, -2.6, 1.2, -2.3, 0.0, 1.8, "player", c_mark)

# ---------------------------------------------------------------- outside: ground, street, gable wings, car
box("OUT_Ground", -25, -25, 30, 30, -0.30, -0.12, "ground", c_out)
box("OUT_Street", -25, -11.0, 30, -4.9, -0.12, -0.08, "cobble", c_out)
box("OUT_Car", 3.4, -8.0, 5.4, -5.6, -0.08, 1.3, "rail", c_out)


def gable_wing(tag, x0, x1, z0, z1):
    box(f"{tag}_Body", x0, z0, x1, z1, -0.1, h2, "ext", c_out)
    # ridge runs north-south; the front gable faces the courtyard
    xm = (x0 + x1) / 2
    import bmesh
    bm = bmesh.new()
    tri = lambda z: [bm.verts.new(zm.bl(x, z, hh)) for x, hh in ((x0 - 0.3, h2), (x1 + 0.3, h2), (xm, h2 + 2.6))]
    a, b = tri(z0 - 0.3), tri(z1 + 0.3)
    bm.faces.new(a)
    bm.faces.new(b[::-1])
    for i in range(3):
        bm.faces.new((a[i], a[(i + 1) % 3], b[(i + 1) % 3], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    zm._obj(f"{tag}_Roof", bm, "roof", c_out)


gable_wing("OUT_WingW", -4.9, -2.05, ZA, NW[1] + 0.2)
gable_wing("OUT_WingE", 6.55, 9.4, ZA, NE[1] + 0.2)

print("P0 graybox:", sum(len(c.objects) for c in [c_ven, c_mark, c_out] + c_f), "objects")
