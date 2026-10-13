"""Part 4: surroundings — gnarled dead trees between the graves, a mausoleum on one outside corner (the statue
in part 5 takes the opposite one) and an iron post with a CC0 gas lantern (`street_lamp_02`, glass swapped for
LampGlow) beside each approach path. Lamps become preview lights. The tree line is part 6."""
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
coll = gy.collection("GY_P4_Surround")
gy.clear_collection(coll)
lights = gy.collection("GY_PreviewLights")
for o in [o for o in lights.objects if o.name.startswith("GY_Lamp")]:
    bpy.data.objects.remove(o, do_unlink=True)
rnd = random.Random(11)
ends = [b for _, b, _ in L.paths]
spots = [Vector(p) for p in gy.landmark_spots(L)]


def free(p, path=2.6, spawn=4.0, land=4.5):
    return (L.path_dist(p) > path and all((p - e).length > spawn for e in ends)
            and all((p - s).length > land for s in spots))


def ring_point(dist, ang):
    return gy.ring_point(L, dist, ang)


# ---------------------------------------------------------------- dead trees (recursive tapered branches)
trees = gy.Part("GY_Trees", coll)


def tree(x, z, height, seed):
    r = random.Random(seed)

    def grow(p, d, length, rad, depth):
        pts, radii, cur = [p], [rad], p.copy()
        for k in range(3):
            d = (d + Vector((r.uniform(-0.25, 0.25), r.uniform(-0.25, 0.25), r.uniform(-0.05, 0.15)))).normalized()
            cur = cur + d * (length / 3)
            pts.append(cur.copy())
            radii.append(rad * (1 - 0.22 * (k + 1)))
        trees.pipe(pts, radii, "Bark", segs=6 if depth < 2 else 4)
        if depth >= 4 or rad < 0.02:
            return
        for _ in range(r.choice((2, 2, 3))):
            nd = (d + Vector((r.uniform(-0.9, 0.9), r.uniform(-0.9, 0.9), r.uniform(-0.1, 0.5)))).normalized()
            grow(cur, nd, length * r.uniform(0.55, 0.75), radii[-1] * 0.85, depth + 1)

    grow(gy.bl(x, z, L.ground_h(x, z) - 0.1), Vector((0, 0, 1)), height * 0.42, 0.07 * height ** 0.8, 0)


placed = []
for k in range(40):
    if len(placed) >= 9:
        break
    p = ring_point(rnd.uniform(4.5, 13.0), rnd.uniform(0, 2 * math.pi))
    if not free(p) or any((p - q).length < 5.0 for q in placed):
        continue
    placed.append(p)
    tree(p.x, p.y, rnd.uniform(5.0, 8.0), 100 + k)

# ---------------------------------------------------------------- mausoleum on the first landmark corner
mz = gy.Part("GY_Mausoleum", coll)
m = spots[0]
c = gy.centroid(L.fence)
face = (c - m).normalized()                              # the door looks at the yard
side = Vector((face.y, -face.x))
h = L.ground_h(m.x, m.y)
W, D, H = 2.8, 3.4, 2.5


def rect(center, along, w, d):
    a2 = Vector((along.y, -along.x))
    return [tuple(center + along * (d / 2) * sy + a2 * (w / 2) * sx) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]


mz.prism(rect(m, face, W + 0.6, D + 0.6), h - 0.3, h + 0.25, "Sandstone")            # stepped base
mz.prism(rect(m - face * 0.1, face, W, D), h + 0.25, h + 0.25 + H, "StoneWall")
top = h + 0.25 + H
mz.prism(rect(m, face, W + 0.25, D + 0.25), top, top + 0.18, "Sandstone")            # cornice
ridge = top + 0.18 + 1.0
for sgn in (-1, 1):                                                                  # two roof slopes
    e0 = m + side * (sgn * (W + 0.35) / 2)
    pts = []
    for dd in (-(D + 0.45) / 2, (D + 0.45) / 2):
        pe, pr = e0 + face * dd, m + face * dd
        pts += [gy.bl(pe.x, pe.y, top + 0.18), gy.bl(pr.x, pr.y, ridge), gy.bl(pe.x, pe.y, top + 0.26),
                gy.bl(pr.x, pr.y, ridge + 0.08)]
    mz.hull(pts, "Roof")
front = m + face * (D / 2 - 0.1)
mz.prism(rect(front + face * 0.02, face, 1.0, 0.06), h + 0.24, h + 0.25 + 1.9, "DoorDark")   # foot inside the base
for sgn in (-1, 1):                                                                  # porch columns
    pc = front + face * 0.45 + side * (sgn * 0.95)
    mz.cyl(pc.x, pc.y, h + 0.25, top, 0.13, "Sandstone", segs=10)
pc = m + face * (D / 2 + 0.12)
mz.prism(rect(pc, face, 0.1, 0.1), ridge - 0.2, ridge + 0.75, "Sandstone")           # cross on the gable
mz.prism(rect(pc, face, 0.42, 0.09), ridge + 0.38, ridge + 0.48, "Sandstone")       # thinner: no shared plane

# ---------------------------------------------------------------- lamp posts beside each path: iron post + CC0 lantern
POST_W, BRACKET_H = 0.16, 2.6                  # the lantern's wall plate hangs on the post face at this height
BULB = Vector((0.0, -0.615, 0.675))            # street_lamp_02 source space: plate at y = 0, lantern toward -y
lantern = ga.load("street_lamp_02", "StreetLamp", tris=3500)[0]
for i in (1, 2):                                # glass + bulb -> one emissive material
    if i < len(lantern.material_slots):
        lantern.material_slots[i].material = gy.material("LampGlow")
shift = Vector(lantern["gy_shift"])
posts = gy.Part("GY_LampPosts", coll)
heads = []
for a, b, side_name in L.paths:
    d = (b - a).normalized()
    n = Vector((d.y, -d.x))
    p = a + d * 8.0 + n * 2.0
    h = L.ground_h(p.x, p.y)
    posts.prism(rect(p, d, 0.32, 0.32), h - 0.1, h + 0.3, "CastIron")
    posts.prism(rect(p, d, 0.24, 0.24), h + 0.3, h + 0.42, "CastIron")
    posts.prism(rect(p, d, POST_W, POST_W), h + 0.42, h + 3.0, "CastIron")
    posts.prism(rect(p, d, 0.22, 0.22), h + 3.0, h + 3.07, "CastIron")
    posts.hull([gy.bl(*q, h + 3.07) for q in rect(p, d, 0.16, 0.16)] + [gy.bl(p.x, p.y, h + 3.3)], "CastIron")
    yaw = math.atan2(n.x, -n.y)                # lantern (source -y) leans over the path (venue -n)
    rot = Matrix.Rotation(yaw, 4, "Z")
    face = p - n * (POST_W / 2 + 0.003)        # plate a hair off the post face (no shared plane)
    origin = gy.bl(face.x, face.y, h + BRACKET_H) + rot @ Vector((shift.x, shift.y, shift.z))
    heads.append((lantern, Matrix.Translation(origin) @ rot))
    gy.preview_light("GY_Lamp", origin + rot @ (BULB - shift), 60.0, (1.0, 0.6, 0.28))
ga.merge("GY_LampHeads", coll, heads)

for p in (trees, mz, posts):
    p.finish(flat=p is not trees)
print("surround:", {o.name: gy.tris(o) for o in coll.objects})
