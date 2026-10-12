"""CC0 models (Poly Haven): download the glTF into the local cache, import, join into one mesh, decimate to a
Quest budget and set the kit pivot. The cache (blender/ZombieManor/asset_cache/) is not in git: every file is
re-downloadable from the asset id."""
import json
import os
import urllib.request

import bpy
from mathutils import Matrix, Vector

CACHE = os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "asset_cache")
API = "https://api.polyhaven.com/files/{id}"
UA = {"User-Agent": "VortexArena-ZombieManor/1.0"}


def _get(url, path):
    if os.path.exists(path):
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r, open(path + ".part", "wb") as f:
        f.write(r.read())
    os.replace(path + ".part", path)
    return path


def fetch(aid, res="1k", fmt="gltf"):
    """Local model path of a Poly Haven asset (downloads it with its .bin and textures if missing).
    fmt 'fbx' is for assets whose glTF Blender's importer cannot read."""
    d = os.path.join(CACHE, f"{aid}_{res}")
    path = os.path.join(d, f"{aid}_{res}.{fmt}")
    if os.path.exists(path):
        return path
    with urllib.request.urlopen(urllib.request.Request(API.format(id=aid), headers=UA), timeout=60) as r:
        g = json.loads(r.read())[fmt][res][fmt]
    for rel, v in g.get("include", {}).items():
        _get(v["url"], os.path.join(d, *rel.split("/")))
    return _get(g["url"], path)


def components(me):
    """Loose parts of a mesh: [(face indices, bbox size, bbox centre)]."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, fs = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            fs.append(g.index)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        vs = {v.co.copy().freeze() for i in fs for v in bm.faces[i].verts}
        lo = Vector([min(v[i] for v in vs) for i in range(3)])
        hi = Vector([max(v[i] for v in vs) for i in range(3)])
        out.append((fs, hi - lo, (hi + lo) / 2))
    bm.free()
    return out


def restyle(ob, test, mat):
    """Moves the loose parts for which test(size, centre) holds onto material `mat`; returns the face count."""
    me = ob.data
    if mat.name not in me.materials:
        me.materials.append(mat)
    idx = list(me.materials).index(mat)
    n = 0
    for fs, size, centre in components(me):
        if test(size, centre):
            for i in fs:
                me.polygons[i].material_index = idx
            n += len(fs)
    return n


def slot_of(ob, word):
    """Index of the first slot whose source material name contains `word`."""
    return next(i for i, m in enumerate(ob.data.materials) if m and word in m.get("zm_src", m.name).lower())


def glow(ob, slot, kind, strength=3.0):
    """Self-lit slot (flame, bulb): emission from its own albedo; kind tags it for the preview/practical lights."""
    m = ob.data.materials[slot]
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    src = b.inputs["Base Color"].links[0].from_socket if b.inputs["Base Color"].is_linked else None
    if src:
        m.node_tree.links.new(src, b.inputs["Emission Color"])
    else:
        b.inputs["Emission Color"].default_value = (1.0, 0.65, 0.3, 1.0)
    b.inputs["Emission Strength"].default_value = strength
    m["zm_glow"] = kind
    if kind == "flame" and src:            # flame cards ship as JPG (no alpha): cut them out by brightness
        nt = m.node_tree
        bw = nt.nodes.new("ShaderNodeRGBToBW")
        nt.links.new(src, bw.inputs[0])
        mr = nt.nodes.new("ShaderNodeMapRange")
        mr.clamp = True
        mr.inputs["From Min"].default_value, mr.inputs["From Max"].default_value = 0.08, 0.35
        nt.links.new(bw.outputs[0], mr.inputs["Value"])
        nt.links.new(mr.outputs["Result"], b.inputs["Alpha"])


def drop(ob, word):
    """Deletes the faces of the slot whose source material name contains `word` (e.g. a frame's glass)."""
    import bmesh
    i = slot_of(ob, word)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == i], context="FACES")
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.materials.pop(index=i)                          # later slots shift down with their faces


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def load(aid, name, coll, budget, pivot="floor", pick=None, rot_z=0.0, res="1k", smooth_angle=35.0, fmt="gltf",
         scale=1.0):
    """One kit object `name` from asset `aid`: meshes joined (pick(objs) -> subset), turned rot_z degrees,
    decimated to `budget` triangles, origin at the kit pivot. Front = Blender -Y (venue +z).
    scale: only for props without a fixed real size (picture frames).
    pivot: 'floor' bottom back-edge centre, 'floor_c' bottom centre, 'wall' back centre, 'ceiling' top centre."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    before = set(bpy.data.objects)
    try:
        if fmt == "fbx":
            bpy.ops.import_scene.fbx(filepath=fetch(aid, res, fmt))
        else:
            bpy.ops.import_scene.gltf(filepath=fetch(aid, res, fmt))
    except Exception:
        for o in [o for o in bpy.data.objects if o not in before]:     # no half-imported leftovers
            bpy.data.objects.remove(o, do_unlink=True)
        raise
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if pick:
        meshes = pick(meshes)
    keep = []
    for o in meshes:                      # bake the hierarchy into world-space mesh copies
        me = o.data.copy()
        me.transform(o.matrix_world)
        keep.append(me)
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)

    # join: one object per copy, then the join operator (keeps material slots). Worked on in the scene root:
    # a hidden collection (the kit) is not evaluated, so the decimate modifier would silently do nothing
    work = bpy.context.scene.collection
    objs = []
    for me in keep:
        ob = bpy.data.objects.new(name, me)
        work.objects.link(ob)
        objs.append(ob)
    ob = objs[0]
    if len(objs) > 1:
        with bpy.context.temp_override(active_object=ob, selected_editable_objects=objs, selected_objects=objs):
            bpy.ops.object.join()
    ob.name = name
    me = ob.data
    me.name = name
    if rot_z:
        import math
        me.transform(Matrix.Rotation(math.radians(rot_z), 4, "Z"))
    if scale != 1.0:
        me.transform(Matrix.Scale(scale, 4))

    n0 = tris(ob)
    if n0 > budget:
        mod = ob.modifiers.new("dec", "DECIMATE")
        mod.ratio = budget / n0
        mod.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get()
        dec = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        ob.modifiers.remove(mod)
        old_me, ob.data = ob.data, dec
        bpy.data.meshes.remove(old_me)
        me = ob.data
        me.name = name
        if me.has_custom_normals:          # interpolated custom normals go wrong on collapsed edges
            with bpy.context.temp_override(object=ob, active_object=ob):
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=__import__("math").radians(smooth_angle))

    vs = [v.co for v in me.vertices]
    lo = Vector([min(v[i] for v in vs) for i in range(3)])
    hi = Vector([max(v[i] for v in vs) for i in range(3)])
    c = (lo + hi) / 2
    origin = {"floor": Vector((c.x, hi.y, lo.z)), "floor_c": Vector((c.x, c.y, lo.z)),
              "wall": Vector((c.x, hi.y, c.z)), "ceiling": Vector((c.x, c.y, hi.z))}[pivot]
    me.transform(Matrix.Translation(-origin))
    for i, m in enumerate(me.materials):   # ZM_ prefix: build_all rebuilds them with the art materials
        if m and not m.name.startswith("ZM_"):
            m["zm_src"] = m.name             # the asset's own material name (slot_of)
            m.name = f"ZM_A_{aid}_{i}"
    ob.location = (0, 0, 0)
    ob["zm_source"] = f"polyhaven:{aid}"
    coll.objects.link(ob)
    work.objects.unlink(ob)
    print(f"{name} <- {aid}: {n0} -> {tris(ob)} tris, size {tuple(round(x, 2) for x in hi - lo)}")
    return ob
