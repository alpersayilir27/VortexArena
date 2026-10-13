"""Part 5: Kat2 roof terrace (strip C, open air) - weathered deck, balustrade around the open sides with corner
lanterns, coping on the parapet walls, chimney stack around Kolon_1, the P2 arrival cage inside a rooftop shed,
and the generator + Tesla coil."""
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
from zm_common import (Part, bl, west_x, east_x, zone_poly, column_bounds, collection, clear_collection,
                       NW, NE, FLOORS, SLAB, ZONE_Z, SHED_TOP, WALL_T, PORTALS)
from zm_rooms import north_z, balustrade, lift_cage

ZA, ZB = ZONE_Z
F2 = FLOORS[2]
W = lambda z: (west_x(z), z)
E = lambda z: (east_x(z), z)
INSET = 0.1                              # balustrade centre line inside the boundary
px, pz = PORTALS[(1, 2)]
CAGE_Z0 = pz - 0.7                       # cage front line (lift_cage)
SHED_X0 = px - 0.9 - 0.08                # shed's west end, just west of the cage side wall
SHED_Z0 = CAGE_Z0 - 0.08
ROOF_H = SHED_TOP + 0.02                 # shed roof sits on the cage headers

root = collection("ZM_P5_Terrace")
clear_collection(root)

# ---------------------------------------------------------------- deck slab (top = Kat2 floor), edge 5 mm inside the facade face
s = Part("ZM_K2_TerraceSlab", root)
s.prism([(west_x(ZB - 0.07), ZB - 0.07), (east_x(ZB - 0.07), ZB - 0.07), NE, NW], F2 - SLAB + 0.06, F2, "Deck")
s.finish()

# ---------------------------------------------------------------- balustrade: from the shed front, around the open sides, to the shed side
r = Part("ZM_K2_TerraceRail", root)
zs = ZB + 0.02
z_end = CAGE_Z0 - 0.04                   # face of the cage's front corner post
x_end = px - 0.9 - 0.04                  # face of the cage's north side post
zn = north_z(west_x(8.7) + INSET) - INSET
SE_, SW_ = (east_x(zs) - INSET, zs), (west_x(zs) + INSET, zs)
pts = [(east_x(z_end) - INSET, z_end), SE_, SW_, (west_x(zn) + INSET, zn), (x_end, north_z(x_end) - INSET)]
balustrade(r, pts, F2, ends=(False, False), lamps=[SE_, SW_])
r.finish()

# ---------------------------------------------------------------- coping on the parapet (Kat1 perimeter walls), 2 cm over the deck edge
cp = Part("ZM_K2_Coping", root)


def coping(a, b):
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    n = Vector((d.y, -d.x))
    cp.wall(tuple(a - n * 0.02), tuple(b - n * 0.02), F2, F2 + 0.06, "Stone", t=WALL_T + 0.04, side=1)


coping(W(NW[1] - 0.021), W(ZB))                  # stops short of the north coping's inward lip
coping((SHED_X0, north_z(SHED_X0)), NW)
coping(E(ZB), E(SHED_Z0))
cp.finish()

# ---------------------------------------------------------------- chimney stack around Kolon_1 (the breast below ends inside this slab)
k1 = column_bounds("Kolon_1")
ch = Part("ZM_K2_Chimney", root)
cx0, cz0, cx1, cz1 = k1[0] - 0.12, k1[1] - 0.15, k1[2] + 0.12, k1[3] + 0.15
ch.box(cx0, cz0, cx1, cz1, F2 - 0.03, F2 + 2.4, "Brick")
ch.box(cx0 - 0.06, cz0 - 0.06, cx1 + 0.06, cz1 + 0.06, F2 + 2.4, F2 + 2.5, "Stone")
for zz in ((cz0 + cz1) / 2 - 0.18, (cz0 + cz1) / 2 + 0.18):
    ch.cyl((cx0 + cx1) / 2, zz, F2 + 2.48, F2 + 2.8, 0.1, "Brick", segs=10, r1=0.085)
ch.finish()

# ---------------------------------------------------------------- P2 arrival cage inside a rooftop shed
L_, R_ = Part("ZM_K2_LiftCageP12", root), Part("ZM_K2_LiftCageP12Roof", root)  # roof: under ArenaRoof in Unity
lift_cage(L_, px, pz, F2, -1, needle_deg=60.0, roof=R_, top=SHED_TOP)   # a shed, not a room: lower headers
L_.finish()
R_.finish()

sh = Part("ZM_K2_Shed", root)
# walls end inside the roof deck: their tops never meet the cage headers' (skewed east wall)
sh.wall(NE, (SHED_X0, north_z(SHED_X0)), F2, F2 + ROOF_H + 0.05, "Siding", "Siding", "TrimPaint", side=1)
sh.wall(E(SHED_Z0), NE, F2, F2 + ROOF_H + 0.05, "Siding", "Siding", "TrimPaint", side=1)
rx0, rz0 = SHED_X0 - 0.12, SHED_Z0 - 0.12
rx1, rz1 = east_x(8.7) + 0.3, north_z(SHED_X0) + 0.3
sh.finish()
sr = Part("ZM_K2_ShedRoof", root)                     # own object: goes under ArenaRoof in Unity (top-down view)
sr.box(rx0, rz0, rx1, rz1, F2 + ROOF_H, F2 + ROOF_H + 0.1, "TrimPaint")                     # roof deck + fascia
top = F2 + ROOF_H + 0.1
zm_ = (rz0 + rz1) / 2
sr.hull([bl(x, z, top) for x in (rx0, rx1) for z in (rz0, rz1)] + [bl(x, zm_, top + 0.45) for x in (rx0 + 0.3, rx1 - 0.3)], "Roof")
sr.finish()

# ---------------------------------------------------------------- generator + Tesla coil against the west balustrade
g = Part("ZM_K2_Generator", root)
gx0 = west_x(5.2) + 0.3
gx1, gz0, gz1 = gx0 + 0.7, 4.6, 5.8
g.box(gx0, gz0, gx1, gz1, F2 - 0.02, F2 + 0.9, "Iron")                                  # cabinet
g.box(gx1 - 0.01, gz0 + 0.15, gx1 + 0.015, gz1 - 0.15, F2 + 0.15, F2 + 0.75, "CarPaint")  # front panel
g.box(gx1 + 0.015, gz0 + 0.45, gx1 + 0.025, gz0 + 0.75, F2 + 0.55, F2 + 0.68, "GlowRed")  # countdown display
for hh in (0.25, 0.32, 0.39):                                                            # vents
    g.box(gx1 + 0.015, gz0 + 0.25, gx1 + 0.03, gz1 - 0.25, F2 + hh, F2 + hh + 0.025, "Iron")
cxg, czg = (gx0 + gx1) / 2, 5.35
g.cyl(cxg, czg, F2 + 0.88, F2 + 1.0, 0.14, "Iron", segs=12)                             # coil base
g.cyl(cxg, czg, F2 + 0.98, F2 + 1.75, 0.075, "Copper", segs=12)                         # winding
g.hull([bl(cxg + 0.22 * math.cos(a), czg + 0.22 * math.sin(a), F2 + h_) for a in [2 * math.pi * k / 16 for k in range(16)]
        for h_ in (1.74, 1.82)] + [bl(cxg, czg, F2 + 1.86)], "Iron")                    # top load
g.sphere(bl(cxg, czg, F2 + 1.9), 0.06, "GlowBlue", segs=10, rings=6)                   # spark
cxt, czt = (gx0 + gx1) / 2, 4.85
g.cyl(cxt, czt, F2 + 0.88, F2 + 0.95, 0.075, "Iron", segs=10)                           # glowing tube
g.cyl(cxt, czt, F2 + 0.95, F2 + 1.45, 0.05, "GlowBlue", segs=10)
g.cyl(cxt, czt, F2 + 1.45, F2 + 1.52, 0.075, "Iron", segs=10)
g.finish()

# ---------------------------------------------------------------- hide the graybox pieces this part replaces
for n_ in ("K2_Slab_Terrace", "K2_Rail_S", "K2_Rail_W", "K2_Rail_N", "K2_Rail_E", "K2_ShedP12_Side", "K2_ShedP12_Front",
           "K2_ShedP12_Roof"):
    ob = bpy.data.objects.get(n_)
    if ob:
        ob.hide_viewport = ob.hide_render = True

for ob in root.objects:
    print(f"{ob.name}: {sum(len(p.vertices) - 2 for p in ob.data.polygons)} tris")
