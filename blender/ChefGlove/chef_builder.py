# Builds the chef glove meshes (ChefGlove_R / ChefGlove_L) on Meta's OpenXR hands (Blender 5.x).
# Run inside ChefGlove.blend (Scripting > Run Script = build_all()); usage: README.md next to this file.
# Hand space: fingers +Y, dorsal +Z, thumb -X on the right hand, metres. Textures come from chef_tex.py.
import bpy, bmesh, math
from mathutils import Vector
from collections import defaultdict

GLOVE, CUFF, SLEEVE, INNER = 0, 1, 2, 5
INFLATE = 0.0026                    # cotton glove thickness over the hand (m)
RING_N = 40                         # sleeve ring resolution; the wrist opening has 20 verts
# Sleeve profile from the wrist opening: (dy below its lowest point, scale around its centre, zone, tag).
# A band between two rings takes the zone of its lower ring. Tags are the anchors chef_tex.py paints from.
SLEEVE_RINGS = [
    (-0.0040, 1.03, GLOVE, None), (-0.0090, 1.06, GLOVE, None),                     # glove tucked into the sleeve
    (-0.0095, 1.30, CUFF, "front"), (-0.0115, 1.41, CUFF, "lip"), (-0.0150, 1.46, CUFF, "band_top"),  # rolled front edge
    (-0.0440, 1.46, CUFF, "band_bot"), (-0.0460, 1.43, CUFF, None),                 # turned-up cuff band
    (-0.0470, 1.40, SLEEVE, "fold"), (-0.0620, 1.41, SLEEVE, None), (-0.0640, 1.37, SLEEVE, "rim"),  # sleeve + rim lip
    (-0.0600, 1.32, INNER, None), (-0.0360, 1.24, INNER, None),                     # inner wall
]
SHARP_ANGLE = 1.0                   # sleeve edges above this (rad) stay hard; the glove is all smooth

def closed_catmull(pts, n):
    """n points on a closed Catmull-Rom curve through pts; even samples hit the input points."""
    out, L = [], len(pts)
    for i in range(n):
        t = i * L / n; k = int(t); u = t - k
        p0, p1, p2, p3 = pts[(k - 1) % L], pts[k % L], pts[(k + 1) % L], pts[(k + 2) % L]
        out.append(0.5 * (2 * p1 + (p2 - p0) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (3 * p1 - p0 - 3 * p2 + p3) * u ** 3))
    return out

def boundary_loop(bm):
    bedges = [e for e in bm.edges if e.is_boundary]
    adj = defaultdict(list)
    for e in bedges:
        a, b = e.verts; adj[a].append(b); adj[b].append(a)
    start = bedges[0].verts[0]; loop, prev, cur = [start], None, start
    while True:
        nx = [n for n in adj[cur] if n is not prev][0]
        if nx is start: break
        loop.append(nx); prev, cur = cur, nx
    # walk direction relative to the existing face decides the winding of every new face
    e0 = bm.edges.get((loop[0], loop[1])); vs = list(e0.link_faces[0].verts)
    fwd = vs[(vs.index(loop[0]) + 1) % len(vs)] is loop[1]
    return loop, fwd

def build(src_name, out_name):
    src = bpy.data.objects[src_name]; arm = src.parent
    old = bpy.data.objects.get(out_name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    for m in [m for m in bpy.data.meshes if m.name.split(".")[0] == out_name and m.users == 0]: bpy.data.meshes.remove(m)
    me = src.data.copy(); me.name = out_name
    ob = bpy.data.objects.new(out_name, me); src.users_collection[0].objects.link(ob)
    ob.parent = arm
    for g in src.vertex_groups:                              # the mesh copy usually brings the names; never add ".001" twins
        if g.name not in ob.vertex_groups: ob.vertex_groups.new(name=g.name)
    assert len(ob.vertex_groups) == len(src.vertex_groups), "vertex groups out of sync with the source hand"
    ob.modifiers.new("Armature", 'ARMATURE').object = arm
    wi = ob.vertex_groups["XRHand_Wrist"].index
    bm = bmesh.new(); bm.from_mesh(me)
    dl = bm.verts.layers.deform.verify(); zl = bm.faces.layers.int.new("zone")
    pl = bm.verts.layers.float.new("prof")                   # mm along the sleeve profile from the wrist opening; 0 on the hand
    cap = [f for f in bm.faces if f.calc_center_median().y < -0.008 and f.normal.y < -0.8]
    bmesh.ops.delete(bm, geom=cap, context='FACES')
    bm.normal_update()
    for v, n in [(v, v.normal.copy()) for v in bm.verts]: v.co += n * INFLATE
    bm.normal_update()
    for f in bm.faces: f[zl] = GLOVE
    loop, fwd = boundary_loop(bm)
    c = sum((v.co for v in loop), Vector()) / len(loop); ymin = min(v.co.y for v in loop)
    flat = [Vector((v.co.x, 0, v.co.z)) for v in loop]
    shape = closed_catmull(flat, RING_N)                     # shape[2k] == loop[k]
    def quad(a, b, na, nb, z):
        f = bm.faces.new([b, a, na, nb] if fwd else [a, b, nb, na]); f[zl] = z
    def tri(a, b, cc, z):                                    # a, b: consecutive on the old ring
        f = bm.faces.new([b, a, cc] if fwd else [a, b, cc]); f[zl] = z
    prev, prof, tags = None, 0.0, {}
    for ri, (dy, s, z, tag) in enumerate(SLEEVE_RINGS):
        ring = []
        for p in shape:
            nv = bm.verts.new(Vector((c.x + (p.x - c.x) * s, ymin + dy, c.z + (p.z - c.z) * s))); nv[dl][wi] = 1.0; ring.append(nv)
        pairs = zip(ring[::2], loop) if prev is None else zip(ring, prev)
        prof += sum((a.co - b.co).length for a, b in pairs) / (len(loop) if prev is None else RING_N) * 1000
        for v in ring: v[pl] = prof
        if tag: tags[tag] = prof
        if prev is None:                                     # 20 -> 40: each opening edge meets two ring edges
            L = len(loop)
            for k in range(L):
                a, b = loop[k], loop[(k + 1) % L]
                n0, n1, n2 = ring[2 * k], ring[2 * k + 1], ring[(2 * k + 2) % RING_N]
                if fwd:
                    bm.faces.new([a, n0, n1])[zl] = z; bm.faces.new([n1, n2, b])[zl] = z; bm.faces.new([b, a, n1])[zl] = z
                else:
                    bm.faces.new([b, n2, n1])[zl] = z; bm.faces.new([n1, n0, a])[zl] = z; bm.faces.new([a, b, n1])[zl] = z
        else:
            for k in range(RING_N):
                quad(prev[k], prev[(k + 1) % RING_N], ring[k], ring[(k + 1) % RING_N], z)
        prev = ring
    cv = bm.verts.new(sum((v.co for v in prev), Vector()) / RING_N); cv[dl][wi] = 1.0
    cv[pl] = prof + sum((v.co - cv.co).length for v in prev) / RING_N * 1000
    for k in range(RING_N): tri(prev[k], prev[(k + 1) % RING_N], cv, INNER)
    bm.normal_update()
    for f in bm.faces: f.smooth = True
    for e in bm.edges:                                       # Meta marks the old wrist cap rim sharp: a crease where the glove enters the cuff
        lf = e.link_faces
        e.smooth = not (len(lf) == 2 and (lf[0][zl] != GLOVE or lf[1][zl] != GLOVE) and e.calc_face_angle(0) > SHARP_ANGLE)
    check_normals(bm, zl, c)
    bm.to_mesh(me); bm.free()
    if "custom_normal" in me.attributes: me.attributes.remove(me.attributes["custom_normal"])  # Meta's normals no longer fit the inflated shape
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])    # chef_tex.py lays out UVMap
    mat = bpy.data.materials.get("M_ChefGlove") or bpy.data.materials.new("M_ChefGlove")
    me.materials.clear(); me.materials.append(mat)
    ob["ring_prof"] = tags                                   # tag -> prof (mm)
    ob["axis_mm"] = (c.x * 1000, ymin * 1000, c.z * 1000)    # sleeve axis (x, z) + wrist opening's lowest y
    src.hide_render = True; src.hide_set(True)
    print(out_name, "verts", len(me.vertices), "tris", sum(len(p.vertices) - 2 for p in me.polygons))
    return ob

def check_normals(bm, zl, c):
    """Fails on inward-facing sleeve faces: Unity culls back faces, so a flipped piece silently vanishes in game."""
    bad = 0
    for f in bm.faces:
        if f[zl] == GLOVE: continue
        m = f.calc_center_median(); radial = Vector((m.x - c.x, 0, m.z - c.z))
        if radial.length < 1e-4: continue                    # cap centre
        d = f.normal.dot(radial.normalized())
        if abs(f.normal.y) > 0.9: continue                   # rings' flat front / cap faces: checked by eye
        if (f[zl] == INNER and d > 0.2) or (f[zl] != INNER and d < -0.2): bad += 1
    assert bad == 0, f"{bad} sleeve faces point the wrong way"

def build_all():
    for side in ("Right", "Left"):
        build(f"{side}Hand", f"ChefGlove_{side[0]}")

if __name__ == "__main__":
    build_all()
