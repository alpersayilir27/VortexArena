"""CC0 model source (Poly Haven glTF): download into the local cache, import once into a hidden library
collection, decimate, and merge placed copies into one exportable art mesh.

Every imported material is renamed GY_A_<name> (p9 bakes it like the procedural ones). The cache
(blender/Graveyard/asset_cache/) is not in git: every file is re-downloadable from the asset ids in the part
scripts."""
import json
import math
import os
import urllib.request

import bpy
from mathutils import Matrix, Vector

import gy_common as gy

CACHE = os.path.join(gy.PROJ, "asset_cache")
UA = {"User-Agent": "VortexArena-Graveyard/1.0"}
LIB = "GY_AssetLib"


def fetch(aid, res="1k"):
    """Local path of the asset's glTF; downloads it (with its .bin and textures) if missing."""
    d = os.path.join(CACHE, aid)
    hit = [f for f in os.listdir(d) if f.endswith(".gltf")] if os.path.isdir(d) else []
    if hit:
        return os.path.join(d, hit[0])
    with urllib.request.urlopen(urllib.request.Request(f"https://api.polyhaven.com/files/{aid}", headers=UA), timeout=60) as r:
        g = json.loads(r.read())["gltf"][res]["gltf"]
    main = os.path.join(d, os.path.basename(g["url"]))
    for url, path in [(g["url"], main)] + [(v["url"], os.path.join(d, k)) for k, v in g.get("include", {}).items()]:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=300) as r, open(path + ".part", "wb") as f:
            f.write(r.read())
        os.replace(path + ".part", path)
    return main


def library(exclude):
    """Hidden source collection; it is excluded from the view layer except while load() decimates."""
    c = bpy.data.collections.get(LIB)
    if c is None:
        c = bpy.data.collections.new(LIB)
        bpy.context.scene.collection.children.link(c)
    c.hide_render = True
    bpy.context.view_layer.layer_collection.children[LIB].exclude = exclude
    return c


def _rename_materials(objs, name):
    """GY_A_<name>[n]; a material imported again reuses the existing one (no .001 duplicates)."""
    seen = {}
    for o in objs:
        for slot in o.material_slots:
            m = slot.material
            if m is None:
                continue
            src = m.as_pointer()                        # by identity: the name changes below
            if src not in seen:
                want = f"GY_A_{name}" + (str(len(seen)) if seen else "")
                old = bpy.data.materials.get(want)
                seen[src] = old if old is not None and old is not m else m
                if seen[src] is m:
                    m.name = want
                    m["gy_src"] = "polyhaven:" + o.get("gy_asset", "")
            slot.material = seen[src]


def _only_uv(o):
    """One UV layer named UVMap: joined meshes merge layers by name."""
    uvs = o.data.uv_layers
    for extra in list(uvs)[1:]:
        uvs.remove(extra)
    if uvs:
        uvs[0].name = "UVMap"


def load(aid, name, tris=None, split=False, res="1k"):
    """The asset's source meshes in the library (imported on first use): transforms applied, origin at the
    bottom centre of each piece, each piece decimated to `tris` when given. split=True separates loose parts
    (a 'set' asset shipped as one mesh). Returns the list sorted by size, largest first."""
    key = f"LIB_{name}"
    have = [o for o in library(True).objects if o.get("gy_lib") == key]
    if have:
        return sorted(have, key=lambda o: -size(o).length)
    lib = library(False)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=fetch(aid, res))
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        lib.objects.link(o)
        o.data = o.data.copy()                          # instanced glTF meshes: own data before applying
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        o["gy_asset"] = aid
    if split:
        parts = []
        for o in meshes:
            parts += _separate(o)
        meshes = parts
    for k, o in enumerate(meshes):
        _only_uv(o)
        if tris and gy.tris(o) > tris:
            mod = o.modifiers.new("Decimate", "DECIMATE")
            mod.ratio = tris / gy.tris(o)
            mod.use_collapse_triangulate = True
            me = o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().copy()
            o.modifiers.remove(mod)
            old = o.data
            o.data = me
            bpy.data.meshes.remove(old)
        vs = [v.co for v in o.data.vertices]
        lo = Vector((sum(v.x for v in vs) / len(vs), sum(v.y for v in vs) / len(vs), min(v.z for v in vs)))
        o.data.transform(Matrix.Translation(-lo))
        o.name = o.data.name = f"{key}_{k}"
        o["gy_lib"] = key
        o["gy_shift"] = tuple(lo)                        # source-space point that became the origin
    _rename_materials(meshes, name)
    out = sorted(meshes, key=lambda o: -size(o).length)
    library(True)
    return out


def _separate(o):
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    left, out = set(bm.faces), []
    while left:
        seed = left.pop()
        group, stack = {seed}, [seed]
        while stack:
            f = stack.pop()
            for e in f.edges:
                for g in e.link_faces:
                    if g in left:
                        left.discard(g)
                        group.add(g)
                        stack.append(g)
        out.append(group)
    pieces = []
    for k, group in enumerate(sorted(out, key=len, reverse=True)):
        if len(group) < 12:
            continue                                     # crumbs
        b2 = bm.copy()
        keep = {f.index for f in group}
        b2.faces.ensure_lookup_table()
        bmesh.ops.delete(b2, geom=[f for f in b2.faces if f.index not in keep], context="FACES")
        me = bpy.data.meshes.new(f"{o.name}_{k}")
        b2.to_mesh(me)
        b2.free()
        for m in o.data.materials:
            me.materials.append(m)
        p = bpy.data.objects.new(me.name, me)
        p["gy_asset"] = o.get("gy_asset", "")
        o.users_collection[0].objects.link(p)
        pieces.append(p)
    bm.free()
    bpy.data.objects.remove(o, do_unlink=True)
    return pieces


def size(o):
    """Mesh extents from the vertices (Object.dimensions is stale for objects outside the view layer)."""
    vs = [v.co for v in o.data.vertices]
    return Vector([max(v[i] for v in vs) - min(v[i] for v in vs) for i in range(3)])


def place(src, x, z, h, yaw_deg=0.0, scale=1.0, tilt=(0.0, 0.0)):
    """World matrix for a library piece at venue (x, z, h): yaw about up, optional small tilt (degrees)."""
    return (Matrix.Translation(gy.bl(x, z, h)) @ Matrix.Rotation(math.radians(yaw_deg), 4, "Z")
            @ Matrix.Rotation(math.radians(tilt[0]), 4, "X") @ Matrix.Rotation(math.radians(tilt[1]), 4, "Y")
            @ Matrix.Diagonal((scale, scale, scale, 1)))


def merge(name, coll, items):
    """One art object from [(library piece, world matrix)]: mesh copies baked into world space and joined."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    if not items:
        return None
    temps = []
    for src, mw in items:
        me = src.data.copy()
        me.transform(mw)
        t = bpy.data.objects.new(name, me)
        coll.objects.link(t)
        temps.append(t)
    with bpy.context.temp_override(active_object=temps[0], selected_editable_objects=temps,
                                   selected_objects=temps):
        bpy.ops.object.join()
    o = temps[0]
    o.name = o.data.name = name
    return o
