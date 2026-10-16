# Shared helpers for the zombie gore kit: Flesh/Bone materials, part builders, bake + FBX export.
# Run from Blender (MCP or Text Editor); every object script imports this module.
# Axis contract: Blender +Z = Unity +Y (stump outward side), Blender -Y = Unity +Z (front).
import os, math, random
import bpy, bmesh
from mathutils import Vector, Matrix

REPO = r"C:\UnityProjects\VortexArena"
OUT = os.path.join(REPO, "Assets", "Modes", "Zombie", "Art", "Gore")
BLEND = os.path.join(REPO, "blender", "ZombieGore", "ZombieGore.blend")
COLLECTION = "ZombieGore"

# Material index inside part bmeshes; the FBX keeps only the slots an object uses, by NAME.
FLESH, BONE = 0, 1
MATS = [("Flesh", (0.30, 0.025, 0.02), 0.28), ("Bone", (0.80, 0.74, 0.62), 0.6)]
MAX_TRIS = {"stump_neck": 300, "stump_limb": 200, "gib": 80}


def material(idx):
    name, col, rough = MATS[idx]
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value = (*col, 1)
    p.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*col, 1)
    m.roughness = rough
    return m


def _ring(bm, pts):
    return [bm.verts.new(p) for p in pts]


def _bridge(bm, a, b, mat):
    """Quads between two closed rings of equal length."""
    n = len(a)
    for k in range(n):
        bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k])).material_index = mat


def _fan(bm, ring, center, mat, flip=False):
    n = len(ring)
    for k in range(n):
        f = (ring[k], ring[(k + 1) % n], center)
        bm.faces.new(f[::-1] if flip else f).material_index = mat


def _finish(bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


# ---------------------------------------------------------------- stump parts

def stump_flesh(rx, ry, segs, seed, dome=0.007, tag=(0.010, 0.020), skirt=0.030):
    """Torn flesh cap: domed top at z >= 0, jagged rim at the cut plane (z = 0) whose
    every other vertex is a tag hanging 1-2 cm toward the body (-Z), tucked skirt below."""
    rng = random.Random(seed)
    bm = bmesh.new()
    step = 2 * math.pi / segs
    ang = [k * step + rng.uniform(-0.15, 0.15) * step for k in range(segs)]
    xy = lambda a, f: (rx * f * math.cos(a), ry * f * math.sin(a))

    def ring(fac, z, jf=0.0, jz=0.0):
        return _ring(bm, [(*xy(a, fac + rng.uniform(-jf, jf)), z + rng.uniform(-jz, jz)) for a in ang])

    top = bm.verts.new((0, 0, dome))
    ra = ring(0.45, dome * 0.8, 0.05, 0.0015)
    rb = ring(0.80, dome * 0.4, 0.04, 0.0015)
    rim = []
    for k, a in enumerate(ang):
        if k % 2 == 0:  # tag tip: out and down
            rim.append(bm.verts.new((*xy(a, 1.06 + rng.uniform(0, 0.05)), -rng.uniform(*tag))))
        else:
            rim.append(bm.verts.new((*xy(a, 0.97 + rng.uniform(-0.02, 0.02)), rng.uniform(-0.002, 0.003))))
    sk = ring(0.86, -skirt, 0.03, 0.002)
    bot = bm.verts.new((0, 0, -skirt))
    _fan(bm, ra, top, FLESH)
    _bridge(bm, rb, ra, FLESH)
    _bridge(bm, rim, rb, FLESH)
    _bridge(bm, sk, rim, FLESH)
    _fan(bm, sk, bot, FLESH, flip=True)
    return _finish(bm)


def bone_nub(cx, cy, r, z_base, z_top, seed, segs=8):
    """Cut bone end: column from z_base (buried in the flesh) to a jagged rim at z_top,
    marrow dip in the top fan (Flesh slot)."""
    rng = random.Random(seed)
    bm = bmesh.new()
    ang = [2 * math.pi * k / segs for k in range(segs)]
    pt = lambda a, rr, z: (cx + rr * math.cos(a), cy + rr * math.sin(a), z)
    r0 = _ring(bm, [pt(a, r, z_base) for a in ang])
    r1 = _ring(bm, [pt(a, r * 1.06, z_top - 0.006) for a in ang])
    r2 = _ring(bm, [pt(a, r * 0.92, z_top + (0.002 if k % 2 else -0.002) + rng.uniform(-0.0015, 0.0015))
                    for k, a in enumerate(ang)])
    marrow = bm.verts.new((cx, cy, z_top - 0.003))
    base = bm.verts.new((cx, cy, z_base))
    _bridge(bm, r0, r1, BONE)
    _bridge(bm, r1, r2, BONE)
    _fan(bm, r2, marrow, FLESH)
    _fan(bm, r0, base, BONE, flip=True)
    return _finish(bm)


def tube(cx, cy, r_out, r_in, z_base, z_top, mat, segs=6, sy=1.0):
    """Short hollow ring (trachea cartilage); sy squashes it front-to-back."""
    bm = bmesh.new()
    ang = [2 * math.pi * (k + 0.5) / segs for k in range(segs)]
    ring = lambda rr, z: _ring(bm, [(cx + rr * math.cos(a), cy + sy * rr * math.sin(a), z) for a in ang])
    ob, ot, it, ib = ring(r_out, z_base), ring(r_out, z_top), ring(r_in, z_top), ring(r_in, z_base)
    _bridge(bm, ob, ot, mat)
    _bridge(bm, ot, it, mat)
    _bridge(bm, it, ib, mat)
    _bridge(bm, ib, ob, mat)
    return _finish(bm)


# ---------------------------------------------------------------- gib parts

def blob(dims, seed, noise=0.18, flat=None, mat=FLESH):
    """80-tri lumpy chunk; flat=(axis, fraction) slices one side into a rough torn face."""
    rng = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * (1 + rng.uniform(-noise, noise))
        if flat:
            ax, f = flat
            if v.co[ax] < -f:
                v.co[ax] = -f + rng.uniform(-0.04, 0.04)
        v.co = Vector((v.co.x * dims[0] / 2, v.co.y * dims[1] / 2, v.co.z * dims[2] / 2))
    for f in bm.faces:
        f.material_index = mat
    return _finish(bm)


def shard(length, r, seed, segs=6, flesh_end=True):
    """Broken bone splinter along X: tapered column, jagged slanted breaks, one end's
    cavity in the Flesh slot."""
    rng = random.Random(seed)
    bm = bmesh.new()
    ang = [2 * math.pi * k / segs for k in range(segs)]
    rings = []
    xs = [-length / 2, -length / 6, length / 6, length / 2]
    for i, x in enumerate(xs):
        rr = r * (1.0, 1.08, 0.9, 0.7)[i]
        end = i in (0, len(xs) - 1)
        pts = []
        for k, a in enumerate(ang):
            dx = 0.0
            if end:  # slanted, splintered break
                dx = (0.35 * length * math.cos(a) * (1 if i else -1) * 0.3
                      + (0.004 if k % 2 else -0.003) + rng.uniform(-0.002, 0.002))
            pts.append((x + dx, rr * math.cos(a) * rng.uniform(0.9, 1.1), rr * 0.8 * math.sin(a) * rng.uniform(0.9, 1.1)))
        rings.append(_ring(bm, pts))
    for a, b in zip(rings, rings[1:]):
        _bridge(bm, a, b, BONE)
    c0 = bm.verts.new((xs[0] + 0.004, 0, 0))
    c1 = bm.verts.new((xs[-1] - 0.003, 0, 0))
    _fan(bm, rings[0], c0, FLESH if flesh_end else BONE, flip=True)
    _fan(bm, rings[-1], c1, BONE)
    return _finish(bm)


def octa(center, size, seed, mat=FLESH):
    """8-tri lumpy scrap (flesh tag stuck on a shard)."""
    rng = random.Random(seed)
    bm = bmesh.new()
    cx, cy, cz = center
    sx, sy, sz = size
    j = lambda: rng.uniform(0.75, 1.25)
    v = _ring(bm, [(cx + sx * j(), cy, cz), (cx, cy + sy * j(), cz), (cx - sx * j(), cy, cz), (cx, cy - sy * j(), cz)])
    t = bm.verts.new((cx, cy, cz + sz * j()))
    b = bm.verts.new((cx, cy, cz - sz * j()))
    _fan(bm, v, t, mat)
    _fan(bm, v, b, mat, flip=True)
    return _finish(bm)


def skull_patch(radius, outline, thick, seed):
    """Curved skull fragment on a sphere of `radius`: outline = 12 (angle, arc length) points
    around the pole. Outer face Bone, inner face Flesh, broken edge band Bone. 72 tris."""
    rng = random.Random(seed)
    bm = bmesh.new()
    assert len(outline) == 12

    def sph(a, s, rr):
        phi = s / radius
        return (rr * math.sin(phi) * math.cos(a), rr * math.sin(phi) * math.sin(a), rr * math.cos(phi))

    def shell(rr):
        c = bm.verts.new(sph(0, 0, rr))
        mid = [bm.verts.new(sph(outline[2 * k][0], outline[2 * k][1] * 0.5, rr)) for k in range(6)]
        rim = [bm.verts.new(sph(a, s, rr + rng.uniform(-0.0008, 0.0008))) for a, s in outline]
        return c, mid, rim

    def faces(c, mid, rim, mat, flip):
        tris = []
        for k in range(6):
            m0, m1 = mid[k], mid[(k + 1) % 6]
            o0, o1, o2 = rim[2 * k], rim[2 * k + 1], rim[(2 * k + 2) % 12]
            tris += [(c, m0, m1), (m0, o0, o1), (m0, o1, m1), (m1, o1, o2)]
        for t in tris:
            bm.faces.new(t[::-1] if flip else t).material_index = mat

    oc, om, orim = shell(radius)
    ic, im, irim = shell(radius - thick)
    faces(oc, om, orim, BONE, False)
    faces(ic, im, irim, FLESH, True)
    _bridge(bm, orim, irim, BONE)
    return _finish(bm)


def outline(seed, base, wobble, n=12):
    """Jagged outline for skull_patch: (angle, arc length) pairs, `base` = (rx, ry) radii."""
    rng = random.Random(seed)
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n + rng.uniform(-0.12, 0.12)
        rr = math.hypot(base[0] * math.cos(a), base[1] * math.sin(a))
        pts.append((a, rr * (1 + rng.uniform(-wobble, wobble) * (1.6 if k % 2 else 0.6))))
    return pts


# ---------------------------------------------------------------- z-fight check

# Same-facing faces of two parts closer than this flicker on Quest (near plane 0.05 m).
ZF_GAP = 0.002


def zfight_pairs(meshes, gap=ZF_GAP, min_area=1e-5):
    """meshes: [(part name, Mesh)]. Returns [(a, b, overlap m2)] for same-facing faces
    of different parts within `gap` of each other that overlap; back-to-back faces are ignored."""
    buckets = {}
    for pi, (_, me) in enumerate(meshes):
        me.calc_loop_triangles()
        for lt in me.loop_triangles:
            if lt.area < 1e-9:
                continue
            n = lt.normal.copy()
            vs = [me.vertices[i].co.copy() for i in lt.vertices]
            key = tuple(round(c * 20) for c in n)
            buckets.setdefault(key, []).append((n.dot(vs[0]), pi, n, vs))

    def clip(poly, a, b):
        # keeps the part of poly left of edge a->b (2D Sutherland-Hodgman step)
        side = lambda p: (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        out = []
        for i, p in enumerate(poly):
            q = poly[i - 1]
            sp, sq = side(p), side(q)
            if sp >= 0:
                if sq < 0:
                    t = sq / (sq - sp)
                    out.append((q[0] + (p[0] - q[0]) * t, q[1] + (p[1] - q[1]) * t))
                out.append(p)
            elif sq >= 0:
                t = sq / (sq - sp)
                out.append((q[0] + (p[0] - q[0]) * t, q[1] + (p[1] - q[1]) * t))
        return out

    def area2(poly):
        return sum(poly[i - 1][0] * p[1] - p[0] * poly[i - 1][1] for i, p in enumerate(poly)) / 2

    acc = {}
    for tris in buckets.values():
        tris.sort(key=lambda t: t[0])
        for i, (d1, p1, n1, v1) in enumerate(tris):
            for d2, p2, n2, v2 in tris[i + 1:]:
                if d2 - d1 >= gap - 1e-6:   # exactly `gap` apart is allowed
                    break
                if p1 == p2 or n1.dot(n2) < 0.999:
                    continue
                t1 = n1.orthogonal().normalized()
                t2 = n1.cross(t1)
                a = [(v.dot(t1), v.dot(t2)) for v in v1]
                b = [(v.dot(t1), v.dot(t2)) for v in v2]
                if area2(a) < 0:
                    a.reverse()
                poly = b if area2(b) > 0 else b[::-1]
                for k in range(3):
                    poly = clip(poly, a[k - 1], a[k])
                    if len(poly) < 3:
                        break
                if len(poly) >= 3:
                    key = tuple(sorted((p1, p2)))
                    acc[key] = acc.get(key, 0.0) + abs(area2(poly))
    return [(meshes[a][0], meshes[b][0], s) for (a, b), s in sorted(acc.items(), key=lambda kv: -kv[1]) if s > min_area]


# ---------------------------------------------------------------- bake + export

def _collection():
    c = bpy.data.collections.get(COLLECTION) or bpy.data.collections.new(COLLECTION)
    if c.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(c)
    return c


def _centroid(bm):
    """Volume centroid of a closed mesh (signed tetrahedra from the origin)."""
    vol, acc = 0.0, Vector()
    for loops in bm.calc_loop_triangles():
        a, b, c = (l.vert.co for l in loops)
        v = a.dot(b.cross(c)) / 6
        vol += v
        acc += v * (a + b + c) / 4
    return acc / vol


def bake(name, parts, budget, pivot="cut", sharp_deg=50):
    """parts: [(part name, bmesh)]. Z-fight check, join, pivot ('cut' = keep origin on the cut
    plane, 'mass' = volume centroid), box UVs, smooth + angle-sharp edges. Returns (obj, tris)."""
    tmp = []
    for pname, bm in parts:
        me = bpy.data.meshes.new("_zg_" + pname)
        bm.to_mesh(me)
        tmp.append((pname, me))
    for a, b, s in zfight_pairs(tmp):
        print(f"Z-FIGHT {name}: {a} / {b} ({s * 1e4:.1f} cm2)")
    for _, me in tmp:
        bpy.data.meshes.remove(me)

    out = bmesh.new()
    for _, bm in parts:
        vmap = {v: out.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            out.faces.new([vmap[v] for v in f.verts]).material_index = f.material_index
        bm.free()
    if pivot == "mass":
        c = _centroid(out)
        bmesh.ops.translate(out, vec=-c, verts=out.verts)
    used = sorted({f.material_index for f in out.faces})
    for f in out.faces:
        f.material_index = used.index(f.material_index)
        f.smooth = True
    uvl = out.loops.layers.uv.new("UVMap")
    for f in out.faces:   # box projection, 10 cm per UV unit
        ax = max(range(3), key=lambda i: abs(f.normal[i]))
        u, v = [i for i in range(3) if i != ax]
        for l in f.loops:
            l[uvl].uv = (l.vert.co[u] * 10, l.vert.co[v] * 10)

    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    if bpy.data.meshes.get(name):
        bpy.data.meshes.remove(bpy.data.meshes[name])
    me = bpy.data.meshes.new(name)
    out.to_mesh(me)
    out.free()
    me.set_sharp_from_angle(angle=math.radians(sharp_deg))
    for i in used:
        me.materials.append(material(i))
    obj = bpy.data.objects.new(name, me)
    _collection().objects.link(obj)
    me.calc_loop_triangles()
    tris = len(me.loop_triangles)
    if tris > budget:
        print(f"OVER BUDGET {name}: {tris} > {budget} tris")
    return obj, tris


def export_fbx(obj, layout_x=0.0):
    """Writes Assets/Modes/Zombie/Art/Gore/<obj name>.fbx from the origin, then moves the object
    to its display slot in the .blend (layout only, never exported)."""
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, obj.name + ".fbx")
    obj.location = (0, 0, 0)
    bpy.context.view_layer.update()
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
        path_mode="STRIP", embed_textures=False)
    obj.location = (layout_x, 0, 0)
    return path


def save_blend():
    """Saves ZombieGore.blend; refuses when another .blend is open (would overwrite it with that scene)."""
    cur = bpy.data.filepath
    if cur and os.path.normcase(cur) != os.path.normcase(BLEND):
        print("NOT SAVED: open file is", cur)
        return
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)


def report(obj, tris, path):
    xs, ys, zs = zip(*(v.co for v in obj.data.vertices))
    print(f"{obj.name}: {tris} tris, slots {[m.name for m in obj.data.materials]}, "
          f"X {min(xs):.3f}..{max(xs):.3f} Y {min(ys):.3f}..{max(ys):.3f} Z {min(zs):.3f}..{max(zs):.3f} -> {path}")
