"""Part 1: yard paving (inside the fence, top at h = 0 = the venue floor), outside ground (gentle mounds, rising
rim) and the dirt approach path in front of each gate."""
import importlib
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)

L = gy.Layout()
coll = gy.collection("GY_P1_Ground")
gy.clear_collection(coll)

# ---------------------------------------------------------------- yard: the fence ring, flat at the floor height
yard = gy.Part("GY_Yard", coll)
yard.floor(L.fence, 0.0, "Cobble")
yard.finish()

# ---------------------------------------------------------------- outside ground: grid with the yard cut out
STEP = 1.2
x0, z0, x1, z1 = L.bounds
x0, z0, x1, z1 = x0 - gy.WORLD_R, z0 - gy.WORLD_R, x1 + gy.WORLD_R, z1 + gy.WORLD_R
nx, nz = int(math.ceil((x1 - x0) / STEP)), int(math.ceil((z1 - z0) / STEP))
inner = gy.offset_ring(L.fence, -0.4)            # faces wholly inside this never show (under the paving)
ground = gy.Part("GY_Ground", coll)
bm = ground.bm
grid = {}
for i in range(nx + 1):
    for j in range(nz + 1):
        x, z = x0 + i * STEP, z0 + j * STEP
        grid[i, j] = (x, z, bm.verts.new(gy.bl(x, z, L.ground_h(x, z))))
faces = []
for i in range(nx):
    for j in range(nz):
        q = [grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]]
        if all(gy.point_in(inner, (x, z)) for x, z, _ in q):
            continue
        faces.append(bm.faces.new([v for _, _, v in q]))
bmesh.ops.recalc_face_normals(bm, faces=faces)
for f in faces:
    f.normal_update()
    if f.normal.z < 0:
        f.normal_flip()
for v in [v for v in bm.verts if not v.link_faces]:
    bm.verts.remove(v)
ground._set(faces, "Mud")
ground.smooth = True
ground.smooth_faces.update(faces)
ground.finish()

# ---------------------------------------------------------------- dirt paths: gate -> outward, widening
paths = gy.Part("GY_Paths", coll)
for a, b, side in L.paths:
    d = (b - a).normalized()
    n = Vector((d.y, -d.x))
    seg = int(gy.PATH_LEN)
    left, right = [], []
    for k in range(seg + 1):
        t = k / seg
        c = a + (b - a) * t
        w = 1.0 + 0.35 * t + 0.08 * math.sin(k * 1.7)
        for sgn, lst in ((1, left), (-1, right)):
            p = c + n * (w * sgn)
            lst.append(paths.bm.verts.new(gy.bl(p.x, p.y, L.ground_h(p.x, p.y) + 0.012)))
    strip = [paths.bm.faces.new((left[k], right[k], right[k + 1], left[k + 1])) for k in range(seg)]
    for f in strip:
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
    paths._set(strip, "Path")
paths.finish()

print("ground:", {o.name: gy.tris(o) for o in coll.objects}, "gates:", [(g[0], tuple(round(c, 2) for c in g[3])) for g in L.gates])
