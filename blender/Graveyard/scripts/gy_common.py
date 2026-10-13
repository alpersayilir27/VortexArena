"""Shared helpers for the Graveyard arena: target outline, fence ring, gate placement, part builder, materials.

Layout numbers are in venue coordinates: (x, z) = Unity world X/Z = dimensions JSON (x, y); h = height.
Blender position = (-x, -z, h)  (Unity (x, y, z) = Blender (-x, z, -y), same as blender/ZombieManor).

The arena adapts to any venue: everything is derived from the target's floor ring and columns
(TARGET: "Map" = generic 10 x 10 m square for Assets/Maps/Graveyard, or a venue name under Assets/Arenas/Venues).
"""
import glob
import importlib
import json
import math
import os

import bmesh
import bpy
from mathutils import Vector

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
PROJ = os.path.join(REPO, "blender", "Graveyard")
MAP_NAME = "Graveyard"

# fence / gate dimensions (metres)
FENCE_OFF = 0.25          # fence centre line outside the floor ring (the floor edge is the venue wall)
PILLAR_W = 0.44           # fence pillar (brick) width
PILLAR_H = 2.35
FENCE_MAX_SPAN = 3.0      # longest iron panel between two pillars
GATE_W = 2.2              # clear gate opening
GATE_PILLAR_W = 0.56
GATE_PILLAR_H = 2.75
EDGE_CLEAR = 0.5          # gate pillars keep this much from a corner pillar / an edge column
PATH_LEN = 14.0           # dirt path from each gate outward (zombie approach)
GROUND_DROP = 0.03        # outside ground sits this much below the yard paving (no shared plane)
WORLD_R = 32.0            # outside ground reaches this far past the fence


def bl(x, z, h=0.0):
    return Vector((-x, -z, h))


# ---------------------------------------------------------------- target outline

def target():
    """Active target name, stored on the scene so a saved .blend remembers what it was built for."""
    return bpy.context.scene.get("gy_target", "Map")


def set_target(name):
    bpy.context.scene["gy_target"] = name


def venue_json(venue):
    """The venue's dimensions file (file name case differs between venues)."""
    hits = [p for p in glob.glob(os.path.join(REPO, "Assets", "Arenas", "Venues", venue, "Data", "*_dimensions.json"))
            if os.path.basename(p).lower() == venue.lower() + "_dimensions.json"]
    if not hits:
        raise FileNotFoundError(f"{venue}: Data/{venue}_dimensions.json yok")
    return hits[0]


def load_outline(name=None):
    """(plane ring CCW [(x, z)], columns [(name, ring, height)]) of the target."""
    name = name or target()
    if name == "Map":
        return [(-5.0, -5.0), (5.0, -5.0), (5.0, 5.0), (-5.0, 5.0)], []
    with open(venue_json(name), encoding="utf-8") as f:
        d = json.load(f)
    plane = [(p["x"], p["y"]) for p in d["plane"]]
    if signed_area(plane) < 0:
        plane.reverse()
    cols = [(c["name"], [(p["x"], p["y"]) for p in c["points"]], c.get("height") or d.get("defaultColumnHeight", 3.0))
            for c in d.get("columns", [])]
    return plane, cols


def art_dir(name=None):
    """Export folder: the map's own art, or the venue scene box's art."""
    name = name or target()
    if name == "Map":
        return os.path.join(REPO, "Assets", "Maps", MAP_NAME, "Art")
    scene = f"{MAP_NAME}_{name}"
    return os.path.join(REPO, "Assets", "Arenas", "Venues", name, "Scenes", scene, "Art")


# ---------------------------------------------------------------- 2D geometry (venue x, z)

def signed_area(ring):
    return sum(ring[i - 1][0] * p[1] - p[0] * ring[i - 1][1] for i, p in enumerate(ring)) / 2


def offset_ring(ring, d):
    """CCW ring grown outward by d (mitered corners; same rule as Polygon2D.Offset)."""
    out = []
    n = len(ring)
    for i in range(n):
        p0, p1, p2 = Vector(ring[i - 1]), Vector(ring[i]), Vector(ring[(i + 1) % n])
        e1, e2 = (p1 - p0).normalized(), (p2 - p1).normalized()
        n1, n2 = Vector((e1.y, -e1.x)), Vector((e2.y, -e2.x))
        den = 1 + n1.dot(n2)
        out.append(tuple(p1 + (n1 + n2) * (d / den)) if den > 1e-3 else tuple(p1 + n1 * d))
    return out


def outward(a, b):
    d = (Vector(b) - Vector(a)).normalized()
    return Vector((d.y, -d.x))


def centroid(ring):
    return Vector((sum(p[0] for p in ring) / len(ring), sum(p[1] for p in ring) / len(ring)))


def seg_dist(p, a, b):
    p, a, b = Vector(p), Vector(a), Vector(b)
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)) if ab.length_squared > 1e-9 else 0.0
    return (p - (a + ab * t)).length


def point_in(ring, p):
    inside = False
    for i in range(len(ring)):
        a, b = ring[i], ring[i - 1]
        if (a[1] > p[1]) != (b[1] > p[1]) and p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0]:
            inside = not inside
    return inside


def ring_dist(ring, p):
    return min(seg_dist(p, ring[i - 1], ring[i]) for i in range(len(ring)))


class Layout:
    """Everything derived from the outline: fence ring, edge pillars, gates, interior columns."""

    def __init__(self, name=None):
        self.plane, self.columns = load_outline(name)
        self.fence = offset_ring(self.plane, FENCE_OFF)
        n = len(self.fence)
        self.edges = [(self.fence[i], self.fence[(i + 1) % n]) for i in range(n)]
        # columns on the fence line become fence pillars, the rest stand inside the yard
        self.edge_cols, self.inner_cols = [], []
        for cname, ring, h in self.columns:
            c = centroid(ring)
            r = max((Vector(p) - c).length for p in ring)
            (self.edge_cols if ring_dist(self.fence, c) < r + PILLAR_W else self.inner_cols).append((cname, ring, h))
        self.gates = self._gates()
        # zombie approach paths: gate centre straight outward
        self.paths = [(Vector(c), Vector(c) + n * PATH_LEN, side) for side, _, _, c, n in self.gates]
        xs, zs = [p[0] for p in self.fence], [p[1] for p in self.fence]
        self.bounds = (min(xs), min(zs), max(xs), max(zs))

    def path_dist(self, p):
        return min((seg_dist(p, a, b) for a, b, _ in self.paths), default=1e9)

    def fence_dist(self, p):
        """Distance to the fence line, negative inside the yard."""
        d = ring_dist(self.fence, p)
        return -d if point_in(self.fence, p) else d

    def ground_h(self, x, z):
        """Outside ground height: flat at -GROUND_DROP near the fence and on the paths, gentle mounds further
        out, rising at the far rim (a shallow bowl hides the world edge)."""
        from mathutils import noise
        p = (x, z)
        fd = self.fence_dist(p)
        if fd < 0:
            return -GROUND_DROP
        calm = min(1.0, max(0.0, (fd - 3.0) / 4.0)) * min(1.0, max(0.0, (self.path_dist(p) - 1.6) / 2.0))
        bump = 0.35 * noise.noise(Vector((x * 0.18, z * 0.18, 0.5))) + 0.12 * noise.noise(Vector((x * 0.6, z * 0.6, 3.1)))
        rim = max(0.0, fd - 18.0) * 0.18
        return -GROUND_DROP + calm * bump + rim

    def _gates(self):
        """One gate per cardinal side (+x, -x, +z, -z): on the edge facing it, as near its middle as columns allow.
        Returns [(side, edge index, s along the edge, centre (x, z), outward normal)]."""
        out = []
        for side, want in (("E", Vector((1, 0))), ("W", Vector((-1, 0))), ("N", Vector((0, 1))), ("S", Vector((0, -1)))):
            cands = [(i, a, b) for i, (a, b) in enumerate(self.edges) if outward(a, b).dot(want) > 0.7]
            if not cands:
                continue
            i, a, b = max(cands, key=lambda e: (Vector(e[2]) - Vector(e[1])).length)
            av, bv = Vector(a), Vector(b)
            length = (bv - av).length
            d = (bv - av) / length
            half = GATE_W / 2 + GATE_PILLAR_W
            blocked = []
            for _, ring, _ in self.edge_cols:
                c = centroid(ring)
                if seg_dist(c, a, b) > max((Vector(p) - c).length for p in ring) + PILLAR_W:
                    continue                                    # a column on another edge
                ss = [(Vector(p) - av).dot(d) for p in ring]
                blocked.append((min(ss) - EDGE_CLEAR, max(ss) + EDGE_CLEAR))
            best = None
            for k in range(0, int(length * 10) + 1):
                for s in (length / 2 + k * 0.1, length / 2 - k * 0.1):
                    if s - half < EDGE_CLEAR or s + half > length - EDGE_CLEAR:
                        continue
                    if any(s + half > lo and s - half < hi for lo, hi in blocked):
                        continue
                    best = s
                    break
                if best is not None:
                    break
            if best is None:
                print(f"[Graveyard] {side}: kapı için yer yok (kenar {length:.1f} m)")
                continue
            out.append((side, i, best, tuple(av + d * best), outward(a, b)))
        return out

    def gate_on_edge(self, i):
        return [g for g in self.gates if g[1] == i]


def ring_point(layout, dist, ang):
    """Point `dist` outside the fence along the ray from the yard centre at angle `ang` (radians)."""
    c = centroid(layout.fence)
    d = Vector((math.cos(ang), math.sin(ang)))
    lo, hi = 0.0, 200.0
    for _ in range(40):                                  # bisect onto the fence line, then step out
        mid = (lo + hi) / 2
        if point_in(layout.fence, tuple(c + d * mid)):
            lo = mid
        else:
            hi = mid
    return c + d * (hi + dist)


def landmark_spots(layout, dist=8.5):
    """Two opposite outside corners for the landmarks (mausoleum, statue): fence corner + outward bisector."""
    ring = layout.fence
    out = []
    for pick in (max, min):
        i = pick(range(len(ring)), key=lambda k: ring[k][0] + ring[k][1])
        p0, p1, p2 = ring[i - 1], ring[i], ring[(i + 1) % len(ring)]
        bis = (outward(p0, p1) + outward(p1, p2)).normalized()
        out.append(tuple(Vector(p1) + bis * dist))
    return out


# ---------------------------------------------------------------- collections

def collection(name, parent=None):
    c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    par = parent or bpy.context.scene.collection
    if c.name not in par.children:
        par.children.link(c)
    return c


def clear_collection(c):
    for ch in list(c.children):
        clear_collection(ch)
        bpy.data.collections.remove(ch)
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)


# ---------------------------------------------------------------- materials
# T = metres covered by one texture tile (the CC0 asset's real size). UVs are written in tile units, so Unity's
# material tiling stays 1.
MATS = {
    "Cobble":    dict(tex=("ph", "grassy_cobblestone", "1k"), T=2.0, c1=(0.3, 0.3, 0.26)),
    "Mud":       dict(tex=("ph", "forest_leaves_04", "1k"), T=1.5, c1=(0.2, 0.15, 0.1)),
    "Path":      dict(tex=("ph", "grass_path_2", "1k"), T=1.0, c1=(0.35, 0.33, 0.3),
                      tint=(0.6, 0.57, 0.52)),     # untinted gravel reads near-white under moonlight
    "Masonry":   dict(tex=("ph", "castle_wall_slates", "1k"), T=2.5, c1=(0.3, 0.3, 0.3)),
    "Sandstone": dict(tex=("ph", "mossy_sandstone", "1k"), T=1.89, c1=(0.42, 0.4, 0.33)),
    "Grave":     dict(tex=("ph", "concrete_wall_006", "1k"), T=2.0, c1=(0.3, 0.28, 0.25)),
    "StoneWall": dict(tex=("ph", "stone_wall_04", "1k"), T=1.7, c1=(0.3, 0.3, 0.3)),
    "Roof":      dict(tex=("ph", "roof_slates_03", "1k"), T=3.0, c1=(0.2, 0.2, 0.22)),
    "Bark":      dict(tex=("ph", "bark_willow_02", "1k"), T=2.15, c1=(0.2, 0.17, 0.14)),
    "CastIron":  dict(pat="noise", T=0.5, scale=5.0, c1=(0.03, 0.03, 0.032), c2=(0.13, 0.08, 0.05), rough=0.55,
                      metal=0.5),                 # painted iron with rust blotches (a texture streaks on thin bars)
    # emit = Eevee preview strength; unity_emit = exported strength (Quest renders LDR: above 1 clips to white)
    "LampGlow":  dict(pat="none", c1=(1.0, 0.62, 0.25), rough=0.5, emit=6.0, unity_emit=1.0),
    "DoorDark":  dict(pat="noise", T=1.0, scale=3.0, c1=(0.02, 0.018, 0.016), c2=(0.05, 0.04, 0.035), rough=0.8),
}


# ---------------------------------------------------------------- Unity scene rules (p9 -> GY_Scene.json)
# Per exported object; missing keys = default. shadows: casts shadows (a mesh holding a light must not, or it
# blocks that light's bake); gi: lightmapped (False = lit by light probes); lightmap: scale in lightmap;
# nav: "include" (zombie NavMesh geometry) | "ignore".
UNITY_OBJECTS = {
    "GY_Ground":       dict(lightmap=0.25),
    "GY_Paths":        dict(lightmap=0.5),
    "GY_Graves":       dict(lightmap=0.6),
    "GY_Trees":        dict(lightmap=0.4),
    "GY_Mausoleum":    dict(lightmap=0.6),
    "GY_Rocks":        dict(lightmap=0.5),
    "GY_Boulders":     dict(lightmap=0.4),
    "GY_Stumps":       dict(lightmap=0.5),
    "GY_Statue":       dict(lightmap=0.5),
    "GY_Lanterns":     dict(shadows=False, gi=False, nav="ignore"),
    "GY_LampHeads":    dict(shadows=False, gi=False, nav="ignore"),
    "GY_Candles":      dict(shadows=False, gi=False, nav="ignore"),
    "GY_CandleFlames": dict(shadows=False, gi=False, nav="ignore"),
    "GY_Firs":         dict(shadows=False, gi=False, nav="ignore"),
    "GY_Ferns":        dict(shadows=False, gi=False, nav="ignore"),
    "GY_Branches":     dict(shadows=False, gi=False, nav="ignore"),
}
UNITY_DEFAULT = dict(shadows=True, gi=True, lightmap=1.0, nav="include")
# Baked point light per GY_Lights.json kind; halo = glow billboard size (m), 0 = none.
LIGHT_KINDS = {
    "Lantern": dict(intensity=4.0, range=8.0, halo=1.1),
    "Lamp":    dict(intensity=6.0, range=12.0, halo=1.3),
    "Candle":  dict(intensity=0.12, range=1.5, halo=0.3),    # 0.24 m from a headstone: more saturates its face
}
FX_DIR = "Assets/Maps/Graveyard/FX"           # map-level FX shared by every Graveyard scene (fx_textures.py)


def tile_of(key):
    T = MATS.get(key, {}).get("T", 1.0)
    return (T, T) if not isinstance(T, tuple) else T


def _sock(sockets, ident):
    return next(s for s in sockets if s.identifier == ident)


def material(key):
    name = "GY_" + key
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nodes, links = nt.nodes, nt.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    if "tex" in spec:
        import gy_textures
        importlib.reload(gy_textures)
        gy_textures.build(m, spec, nodes.new("ShaderNodeTexCoord").outputs["UV"], nodes, links, bsdf)
        return m
    c1, c2 = spec["c1"] + (1,), spec.get("c2", spec["c1"]) + (1,)
    m.diffuse_color = c2
    if spec["pat"] == "noise":
        Tu, Tv = tile_of(key)
        sep = nodes.new("ShaderNodeSeparateXYZ")
        links.new(nodes.new("ShaderNodeTexCoord").outputs["UV"], sep.inputs[0])

        def math_(op, a, b):
            n = nodes.new("ShaderNodeMath")
            n.operation = op
            for i, x in enumerate((a, b)):
                if x is None:
                    continue
                if isinstance(x, (int, float)):
                    n.inputs[i].default_value = x
                else:
                    links.new(x, n.inputs[i])
            return n.outputs[0]

        # one tile wrapped onto a 4D torus: the noise repeats exactly per tile (tileable bake)
        tw = lambda s, T, f: math_("MULTIPLY", math_(f, math_("MULTIPLY", s, 2 * math.pi), None), T / (2 * math.pi))
        u, v = sep.outputs["X"], sep.outputs["Y"]
        cx = nodes.new("ShaderNodeCombineXYZ")
        links.new(tw(u, Tu, "COSINE"), cx.inputs[0])
        links.new(tw(u, Tu, "SINE"), cx.inputs[1])
        links.new(tw(v, Tv, "COSINE"), cx.inputs[2])
        noise = nodes.new("ShaderNodeTexNoise")
        noise.noise_dimensions = "4D"
        noise.inputs["Scale"].default_value = spec["scale"]
        noise.inputs["Detail"].default_value = 6.0
        links.new(cx.outputs[0], noise.inputs["Vector"])
        links.new(tw(v, Tv, "SINE"), noise.inputs["W"])
        ramp = nodes.new("ShaderNodeValToRGB")
        els = ramp.color_ramp.elements
        els[0].position, els[0].color = 0.35, c1
        els[1].position, els[1].color = 0.65, c2
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = c1
    bsdf.inputs["Roughness"].default_value = spec.get("rough", 0.8)
    bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0) if not isinstance(spec.get("metal"), bool) else 1.0
    if spec.get("emit"):
        bsdf.inputs["Emission Color"].default_value = c1
        bsdf.inputs["Emission Strength"].default_value = spec["emit"]
    return m


# ---------------------------------------------------------------- z-fight check (same algorithm as blender/BurgerKitchen bk_common)
ZF_GAP = 0.002
PART_ATTR = "gy_part"   # per-face id of the builder call that made the face


def zfight_pairs(meshes, gap=ZF_GAP, min_area=1e-5):
    """meshes: [(label, [(normal, verts), ...])] in world space. Returns [(a, b, overlap m2)] for same-facing
    faces of different labels within `gap` of each other that overlap; back-to-back faces are ignored."""
    buckets = {}
    for pi, (_, tris_) in enumerate(meshes):
        for n, vs in tris_:
            key = tuple(round(c * 20) for c in n)
            buckets.setdefault(key, []).append((n.dot(vs[0]), pi, n, vs))

    def clip(poly, a, b):
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
    for group in buckets.values():
        group.sort(key=lambda t: t[0])
        for i, (d1, p1, n1, v1) in enumerate(group):
            for d2, p2, n2, v2 in group[i + 1:]:
                if d2 - d1 >= gap - 1e-6:
                    break
                if p1 == p2 or n1.dot(n2) < 0.999:
                    continue
                # nearly parallel faces far apart can share a plane offset (d) by accident: measure the real gap
                if max(abs(n1.dot(v - v1[0])) for v in v2) >= gap:
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


def zfight_report(objects, limit=40, below=-0.04):
    """Checks every builder call of the given Part objects against each other (also across objects). Faces
    wholly below `below` (buried footings) are skipped: nobody sees them."""
    bpy.context.view_layer.update()
    groups = {}
    for o in objects:
        me = o.data
        attr = me.attributes.get(PART_ATTR)
        me.calc_loop_triangles()
        mw = o.matrix_world
        for lt in me.loop_triangles:
            if lt.area < 1e-9:
                continue
            vs = [mw @ me.vertices[i].co for i in lt.vertices]
            if max(v.z for v in vs) < below:
                continue
            pid = attr.data[lt.polygon_index].value if attr else 0
            n = (vs[1] - vs[0]).cross(vs[2] - vs[0]).normalized()
            groups.setdefault(f"{o.name}#{pid}", []).append((n, vs))
    pairs = zfight_pairs(list(groups.items()))
    for a, b, s in pairs[:limit]:
        print(f"Z-FIGHT {a} / {b}: {s * 1e4:.1f} cm2")
    print(f"z-fight check: {len(objects)} objects, {len(pairs)} pairs")
    return pairs


# ---------------------------------------------------------------- part: one exportable mesh with material slots

class Part:
    """Accumulates geometry (venue coordinates in, Blender coordinates stored) into one mesh object."""

    def __init__(self, name, coll):
        self.name, self.coll = name, coll
        old = bpy.data.objects.get(name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        self.bm = bmesh.new()
        self.pid_layer = self.bm.faces.layers.int.new(PART_ATTR)
        self.pid = 0
        self.mats = []
        self.smooth_faces = set()
        self.smooth = False

    def _set(self, faces, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        i = self.mats.index(mat)
        self.pid += 1                       # one id per builder call: the z-fight check compares between calls
        for f in faces:
            f.material_index = i
            f[self.pid_layer] = self.pid
            if self.smooth:
                self.smooth_faces.add(f)
        return faces

    def prism(self, poly_xz, h0, h1, mat):
        bm = self.bm
        f = bm.faces.new([bm.verts.new(bl(x, z, h0)) for x, z in poly_xz])
        r = bmesh.ops.extrude_face_region(bm, geom=[f])
        top = [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=(0, 0, h1 - h0), verts=top)
        faces = list({f} | {e for e in r["geom"] if isinstance(e, bmesh.types.BMFace)}
                     | {lf for v in top for lf in v.link_faces})
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._set(faces, mat)

    def floor(self, poly_xz, h, mat):
        """Single up-facing polygon (ground surfaces)."""
        f = self.bm.faces.new([self.bm.verts.new(bl(x, z, h)) for x, z in poly_xz])
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
        return self._set([f], mat)

    def obox(self, center, size, mat, rot=(0, 0, 0)):
        """Oriented box: center venue (x, z, h), size (sx, sz, sh), rot = Blender XYZ euler degrees."""
        from mathutils import Matrix, Euler
        x, z, h = center
        m = (Matrix.Translation(bl(x, z, h)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
             @ Matrix.Diagonal((size[0], size[1], size[2], 1)))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self._set(list({f for v in r["verts"] for f in v.link_faces}), mat)

    def seg_box(self, a, b, w, h0, h1, mat, extend=0.0):
        """Box of width w along venue segment a->b (both ends extended by `extend`), from h0 to h1."""
        a, b = Vector(a), Vector(b)
        d = (b - a).normalized()
        n = Vector((d.y, -d.x)) * (w / 2)
        a, b = a - d * extend, b + d * extend
        return self.prism([tuple(a - n), tuple(b - n), tuple(b + n), tuple(a + n)], h0, h1, mat)

    def cyl(self, x, z, h0, h1, r, mat, segs=12, r1=None):
        from mathutils import Matrix
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r1 is None else r1,
                                    depth=h1 - h0, matrix=Matrix.Translation(bl(x, z, (h0 + h1) / 2)))
        return self._set(list({f for v in res["verts"] for f in v.link_faces}), mat)

    def pipe(self, pts, radii, mat, segs=6, cap=True):
        """Tapered tube through Blender-space points (no twist)."""
        pts = [Vector(p) for p in pts]
        bm, rings, n1 = self.bm, [], None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if n1 is None:
                ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
                n1 = t.cross(ref).normalized()
            else:
                n1 = (n1 - t * n1.dot(t)).normalized()
            n2 = t.cross(n1)
            rings.append([bm.verts.new(p + radii[i] * (math.cos(a) * n1 + math.sin(a) * n2))
                          for a in (2 * math.pi * k / segs for k in range(segs))])
        faces = [bm.faces.new((a[k], a[(k + 1) % segs], b2[(k + 1) % segs], b2[k]))
                 for a, b2 in zip(rings, rings[1:]) for k in range(segs)]
        if cap:
            faces += [bm.faces.new(list(reversed(rings[0]))), bm.faces.new(rings[-1])]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._set(faces, mat)

    def hull(self, pts, mat):
        """Convex hull of Blender-space points."""
        vs = [self.bm.verts.new(p) for p in pts]
        r = bmesh.ops.convex_hull(self.bm, input=vs)
        faces = [g for g in r["geom"] if isinstance(g, bmesh.types.BMFace)]
        for v in r["geom_interior"] + r["geom_unused"]:
            if isinstance(v, bmesh.types.BMVert) and v.is_valid and not v.link_faces:
                self.bm.verts.remove(v)
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._set(faces, mat)

    def lathe(self, cx, cz, profile, mat, segs=12):
        """Surface of revolution around venue (cx, cz): profile = [(h, r)] bottom to top, capped."""
        bm, rings = self.bm, []
        for h, r in profile:
            rings.append([bm.verts.new(bl(cx + r * math.cos(2 * math.pi * k / segs),
                                          cz + r * math.sin(2 * math.pi * k / segs), h)) for k in range(segs)])
        faces = [bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
                 for a, b in zip(rings, rings[1:]) for k in range(segs)]
        faces += [bm.faces.new(list(reversed(rings[0]))), bm.faces.new(rings[-1])]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._set(faces, mat)

    def transform_last(self, faces, matrix):
        """Applies a Blender-space matrix to the vertices of the given faces (tilting a gravestone)."""
        verts = list({v for f in faces for v in f.verts})
        bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)

    def finish(self, flat=True):
        bm = self.bm
        uvl = bm.loops.layers.uv.verify()
        tiles = [tile_of(k) for k in self.mats]
        for f in bm.faces:
            ax = max(range(3), key=lambda i: abs(f.normal[i]))
            Tu, Tv = tiles[f.material_index]
            for lp in f.loops:
                c = lp.vert.co
                a, b = (c.x, c.y) if ax == 2 else ((c.y, c.z) if ax == 0 else (c.x, c.z))
                lp[uvl].uv = (a / Tu, b / Tv)          # box projection in tile units
        smooth = {f.index for f in self.smooth_faces if f.is_valid}
        bm.faces.index_update()
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = (p.index in smooth) or not flat
        for k in self.mats:
            me.materials.append(material(k))
        o = bpy.data.objects.new(self.name, me)
        self.coll.objects.link(o)
        return o


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def reset_materials():
    """Drops the procedural GY_ materials so a MATS edit takes effect (material() reuses existing ones);
    imported GY_A_ asset materials stay with their library meshes."""
    for m in [m for m in bpy.data.materials if m.name.startswith("GY_") and not m.name.startswith("GY_A_")]:
        bpy.data.materials.remove(m)


def preview_light(prefix, pos, watts, color=(1.0, 0.62, 0.3)):
    """Point light in GY_PreviewLights at a Blender-space position; p9 exports it to GY_Lights.json, the name
    prefix (GY_Lantern / GY_Lamp / GY_Candle) is its kind."""
    lights = collection("GY_PreviewLights")
    name = f"{prefix}_{sum(o.name.startswith(prefix + '_') for o in lights.objects)}"
    old = bpy.data.lights.get(name)
    if old is not None and old.users == 0:
        bpy.data.lights.remove(old)                  # an orphan from a previous build would force a .001 name
    li = bpy.data.lights.new(name, "POINT")
    li.energy, li.color, li.shadow_soft_size = watts, color, 0.05
    o = bpy.data.objects.new(name, li)
    o.location = pos
    lights.objects.link(o)
    return o
