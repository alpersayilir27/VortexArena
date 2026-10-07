# Procedural texture set + Unity mesh dump for the tactical glove (Blender 5.x + numpy).
# Run inside TacticalGlove.blend (Scripting > Run Script = build()); usage: README.md next to this file.
# Hand space: fingers +Y, dorsal +Z, units metres (procedural math in mm).
# Outputs: albedo RGBA (sRGB, A = smoothness) + tangent-space normal (OpenGL, MikkTSpace).
import bpy, math, os, numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
from mathutils.bvhtree import BVHTree

def _here():
    f = globals().get("__file__")
    if f and os.path.isfile(f): return os.path.dirname(os.path.abspath(f))
    return os.path.dirname(bpy.data.filepath)  # Text Editor run: the .blend sits next to this script

RES = 2048
HERE = _here()
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Assets", "_Shared", "Avatars", "TacticalGlove")   # textures, imported by Unity as-is
EXPORT = os.path.join(HERE, "export")                                         # mesh dumps for the Unity importer
RENDERS = os.path.join(HERE, "renders")
FAB, LEA, RUB, STR, STI, INN = 0, 1, 2, 3, 4, 5
S = globals().setdefault("S", {})

# ---------------------------------------------------------------- utils
def view3d_override():
    for win in bpy.context.window_manager.windows:
        for area in win.screen.areas:
            if area.type == 'VIEW_3D':
                region = next(r for r in area.regions if r.type == 'WINDOW')
                return dict(window=win, area=area, region=region)

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)

def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = h & 0xffffffff
    h ^= h >> 13
    h = (h * 0x5bd1e995) & 0xffffffff
    h ^= h >> 15
    return (h & 0xffffff).astype(np.float32) / np.float32(0xffffff)

def vnoise(p, seed=0):
    i = np.floor(p); f = (p - i).astype(np.float32); i = i.astype(np.int64)
    u = f * f * (3 - 2 * f)
    r = np.zeros(len(p), np.float32)
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                r += wx * wy * wz * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return r

def fbm(p, octaves=4, seed=0):
    a, s, tot = 0.5, np.zeros(len(p), np.float32), 0.0
    for o in range(octaves):
        s += a * vnoise(p * (2.0 ** o), seed + o * 1013); tot += a; a *= 0.5
    return s / tot

def voronoi(p, seed=0):
    i = np.floor(p).astype(np.int64); f = (p - i).astype(np.float32)
    F1 = np.full(len(p), 9.0, np.float32); F2 = np.full(len(p), 9.0, np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx, cy, cz = i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz
                fx = dx + _hash(cx, cy, cz, seed) - f[:, 0]
                fy = dy + _hash(cx, cy, cz, seed + 1) - f[:, 1]
                fz = dz + _hash(cx, cy, cz, seed + 2) - f[:, 2]
                d = fx * fx + fy * fy + fz * fz
                F2 = np.where(d < F1, F1, np.minimum(F2, d)); F1 = np.minimum(F1, d)
    return np.sqrt(F1), np.sqrt(F2)

def weave(u, v, p):
    a, b = u / p, v / p
    ia, ib = np.floor(a), np.floor(b); fa, fb = a - ia, b - ib
    hw = np.sin(np.pi * fb) ** 0.7 * (0.55 + 0.45 * np.cos(np.pi * (a - 0.5 + ib)))
    hf = np.sin(np.pi * fa) ** 0.7 * (0.55 - 0.45 * np.cos(np.pi * (b - 0.5 + ia)))
    return np.maximum(hw, hf).astype(np.float32)

def triplanar(fn, Pm, N, sharp=4.0):
    w = np.abs(N) ** sharp; w /= w.sum(1, keepdims=True)
    return (w[:, 0] * fn(Pm[:, 1], Pm[:, 2]) + w[:, 1] * fn(Pm[:, 0], Pm[:, 2]) + w[:, 2] * fn(Pm[:, 0], Pm[:, 1])).astype(np.float32)

def hexlattice(u, v, s):
    """Local offset from the nearest hex-lattice centre (neighbour spacing s)."""
    q = np.stack([u, v], -1) / s
    r = np.array([1.0, math.sqrt(3.0)]); h = r * 0.5
    a = np.mod(q, r) - h; b = np.mod(q - h, r) - h
    g = np.where(((a * a).sum(-1) < (b * b).sum(-1))[:, None], a, b)
    return g * s

def hexedge(g, s):
    d = np.stack([np.abs(g[:, 0]), np.abs(0.5 * g[:, 0] + 0.8660254 * g[:, 1]), np.abs(-0.5 * g[:, 0] + 0.8660254 * g[:, 1])], -1).max(-1)
    return d / s  # 0 at centre, 0.5 at cell edge

def rrect(x, y, hx, hy, r):
    """Rounded-rect SDF (mm) + a perimeter-ish coordinate for stitch dashes."""
    qx, qy = np.abs(x) - (hx - r), np.abs(y) - (hy - r)
    sdf = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r
    along = np.where(qx > qy, y, x) + np.where((qx > 0) & (qy > 0), np.arctan2(qy, qx) * r, 0)
    return sdf.astype(np.float32), along.astype(np.float32)

def stitch(across, along, w=0.5, L=3.0, duty=0.72):
    ph = np.mod(along, L) / L
    e = np.minimum(ph, duty - ph) * L
    inx = np.clip(1 - (2 * across / w) ** 2, 0, 1)
    prof = np.sqrt(inx) * np.sqrt(np.clip(e / 0.35, 0, 1))
    mask = np.clip((w * 0.5 - np.abs(across)) / 0.12 + 0.5, 0, 1) * np.clip(e / 0.12 + 0.5, 0, 1)
    hole = (ph > duty) * np.clip(1 - np.abs(across) / (w * 0.6), 0, 1)
    return mask.astype(np.float32), prof.astype(np.float32), hole.astype(np.float32)

# ---------------------------------------------------------------- stage 1: zones + UV
def zones_from_palette(me, layer):
    uv = me.uv_layers[layer].data
    z = np.zeros(len(me.polygons), np.int32)
    for p in me.polygons:
        u = np.mean([uv[l].uv[:] for l in p.loop_indices], axis=0)
        z[p.index] = (int(u[1] * 16) // 4) * 4 + int(u[0] * 16) // 4
    return z

def ensure_zone(me):
    if "zone" in me.attributes: return
    z = zones_from_palette(me, me.uv_layers[0].name)
    a = me.attributes.new("zone", 'INT', 'FACE'); a.data.foreach_set("value", z)

def unwrap(o):
    me = o.data
    ensure_zone(me)
    if "Palette" not in me.uv_layers: me.uv_layers[0].name = "Palette"
    uvl = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    me.uv_layers.active = uvl; uvl.active_render = True
    for ob in bpy.context.view_layer.objects: ob.select_set(False)
    o.select_set(True); bpy.context.view_layer.objects.active = o
    with bpy.context.temp_override(**view3d_override()):
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.uv.select_all(action='SELECT')
        bpy.ops.uv.average_islands_scale()
        bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
        bpy.ops.object.mode_set(mode='OBJECT')

def transfer_mirror(src, dst):
    ms, md = src.data, dst.data
    ensure_zone(md)
    kd = KDTree(len(ms.vertices))
    for v in ms.vertices: kd.insert((-v.co.x, v.co.y, v.co.z), v.index)
    kd.balance()
    vmap = np.array([kd.find(v.co)[1] for v in md.vertices])
    polys = {tuple(sorted(p.vertices)): p.index for p in ms.polygons}
    if "Palette" not in md.uv_layers: md.uv_layers[0].name = "Palette"
    dl = md.uv_layers.get("UVMap") or md.uv_layers.new(name="UVMap")
    md.uv_layers.active = dl; dl.active_render = True
    sl = ms.uv_layers["UVMap"].data
    zs = np.zeros(len(ms.polygons), np.int32); ms.attributes["zone"].data.foreach_get("value", zs)
    zd = np.zeros(len(md.polygons), np.int32)
    for p in md.polygons:
        sp = ms.polygons[polys[tuple(sorted(vmap[list(p.vertices)]))]]
        zd[p.index] = zs[sp.index]
        lut = {ms.loops[l].vertex_index: l for l in sp.loop_indices}
        for l in p.loop_indices:
            dl.data[l].uv = sl[lut[vmap[md.loops[l].vertex_index]]].uv
    md.attributes["zone"].data.foreach_set("value", zd)

def stage_uv():
    R, L = bpy.data.objects["Glove_R"], bpy.data.objects["Glove_L"]
    unwrap(R); transfer_mirror(R, L)
    uv = np.zeros(len(R.data.loops) * 2); R.data.uv_layers["UVMap"].data.foreach_get("uv", uv)
    print("uv range", uv.reshape(-1, 2).min(0), uv.reshape(-1, 2).max(0))

# ---------------------------------------------------------------- stage 2: rasterize + per-texel geometry
def mesh_arrays(o):
    me = o.data
    me.calc_loop_triangles()
    me.calc_tangents(uvmap="UVMap")
    nl = len(me.loops)
    A = {}
    A["LUV"] = np.zeros(nl * 2, np.float32); me.uv_layers["UVMap"].data.foreach_get("uv", A["LUV"]); A["LUV"] = A["LUV"].reshape(-1, 2)
    A["LN"] = np.zeros(nl * 3, np.float32); me.corner_normals.foreach_get("vector", A["LN"]); A["LN"] = A["LN"].reshape(-1, 3)
    A["LT"] = np.zeros(nl * 3, np.float32); me.loops.foreach_get("tangent", A["LT"]); A["LT"] = A["LT"].reshape(-1, 3)
    A["LS"] = np.zeros(nl, np.float32); me.loops.foreach_get("bitangent_sign", A["LS"])
    A["LV"] = np.zeros(nl, np.int32); me.loops.foreach_get("vertex_index", A["LV"])
    A["V"] = np.zeros(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", A["V"]); A["V"] = A["V"].reshape(-1, 3)
    nt = len(me.loop_triangles)
    A["TL"] = np.zeros(nt * 3, np.int32); me.loop_triangles.foreach_get("loops", A["TL"]); A["TL"] = A["TL"].reshape(-1, 3)
    A["TP"] = np.zeros(nt, np.int32); me.loop_triangles.foreach_get("polygon_index", A["TP"])
    A["FZ"] = np.zeros(len(me.polygons), np.int32); me.attributes["zone"].data.foreach_get("value", A["FZ"])
    return A

def rasterize(A):
    LUV, TL = A["LUV"], A["TL"]
    tri = -np.ones((RES, RES), np.int32)
    for t in range(len(TL)):
        uv = LUV[TL[t]].astype(np.float64) * RES - 0.5
        x0, y0 = np.maximum(np.floor(uv.min(0)).astype(int), 0); x1, y1 = np.minimum(np.ceil(uv.max(0)).astype(int), RES - 1)
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        (ax, ay), (bx, by), (cx, cy) = uv
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12: continue
        w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
        w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        m = (w0 >= -1e-4) & (w1 >= -1e-4) & (1 - w0 - w1 >= -1e-4)
        tri[ys[m], xs[m]] = t
    return tri

def dilate_ids(ids, n):
    ids = ids.copy()
    for _ in range(n):
        empty = ids < 0
        if not empty.any(): break
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh = np.roll(np.roll(ids, dy, 0), dx, 1)
            fill = empty & (sh >= 0) & (ids < 0)
            ids[fill] = sh[fill]
    return ids

def vertex_ao(A, rays=48, maxd=0.02):
    V = A["V"]; polys = [tuple(int(i) for i in A["LV"][tl]) for tl in A["TL"]]
    bvh = BVHTree.FromPolygons([Vector(v) for v in V], polys)
    # vertex normals = mean of corner normals
    VN = np.zeros_like(V); np.add.at(VN, A["LV"], A["LN"]); VN /= np.linalg.norm(VN, axis=1, keepdims=True) + 1e-9
    k = np.arange(rays) + 0.5; phi = np.arccos(1 - k / rays); th = np.pi * (1 + 5 ** 0.5) * k
    dirs = np.stack([np.cos(th) * np.sin(phi), np.sin(th) * np.sin(phi), np.cos(phi)], -1)
    ao = np.ones(len(V), np.float32)
    for i in range(len(V)):
        n = VN[i]; t = np.cross(n, [0, 0, 1] if abs(n[2]) < 0.9 else [1, 0, 0]); t /= np.linalg.norm(t); b = np.cross(n, t)
        o = Vector(V[i] + n * 0.0004); occ = 0.0
        for d in dirs:
            wd = Vector(d[0] * t + d[1] * b + d[2] * n)
            hit = bvh.ray_cast(o, wd, maxd)
            if hit[0] is not None: occ += 1.0 - 0.6 * hit[3] / maxd
        ao[i] = 1.0 - occ / rays
    return ao

def stage_raster():
    o = bpy.data.objects["Glove_R"]
    A = mesh_arrays(o)
    tri = rasterize(A)
    cov = tri >= 0
    tri_e = dilate_ids(tri, 3)
    ev = tri_e >= 0
    ys, xs = np.nonzero(ev)
    t = tri_e[ys, xs]
    TL, LUV, LV = A["TL"], A["LUV"], A["LV"]
    uv = LUV[TL[t]].astype(np.float64) * RES - 0.5               # (K,3,2) pixel coords
    (ax, ay), (bx, by), (cx, cy) = uv[:, 0].T, uv[:, 1].T, uv[:, 2].T
    den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
    w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
    W = np.stack([w0, w1, 1 - w0 - w1], -1).astype(np.float32)
    def interp(arr): return (arr[TL[t]] * W[:, :, None]).sum(1)
    Pv = A["V"][LV[TL[t]]]                                          # (K,3,3)
    P = (Pv * W[:, :, None]).sum(1)
    N = interp(A["LN"]); N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-9
    T = interp(A["LT"])
    SG = A["LS"][TL[t, 0]]
    # per-triangle Jacobian dP/dpx, dP/dpy (metres per pixel)
    e1, e2 = Pv[:, 1] - Pv[:, 0], Pv[:, 2] - Pv[:, 0]
    du1, dv1, du2, dv2 = bx - ax, by - ay, cx - ax, cy - ay
    det = du1 * dv2 - du2 * dv1; det = np.where(np.abs(det) < 1e-12, 1e-12, det)
    Pu = ((e1 * dv2[:, None] - e2 * dv1[:, None]) / det[:, None]).astype(np.float32)
    Pvv = ((e2 * du1[:, None] - e1 * du2[:, None]) / det[:, None]).astype(np.float32)
    ao_v = vertex_ao(A)
    AO = (ao_v[LV[TL[t]]] * W).sum(1)
    S.update(A=A, tri=tri, cov=cov, ev=ev, ys=ys, xs=xs, t=t, P=P.astype(np.float32), N=N.astype(np.float32), T=T.astype(np.float32),
             SG=SG, Pu=Pu, Pv=Pvv, AO=AO.astype(np.float32), Z=A["FZ"][A["TP"][t]])
    mmpp = np.linalg.norm(Pu, axis=1).mean() * 1000
    print("covered %.1f%%  eval texels %d  mm/px %.3f (%.1f px/mm)  AO min %.2f" % (cov.mean() * 100, len(t), mmpp, 1 / mmpp, ao_v.min()))

# ---------------------------------------------------------------- stage 3: boundary field
def boundary_samples(A, step=0.25e-3):
    me = bpy.data.objects["Glove_R"].data
    FZ = A["FZ"]; V = A["V"].astype(np.float64)
    ef = {}
    for p in me.polygons:
        for ek in p.edge_keys: ef.setdefault(ek, []).append(p.index)
    groups = {}
    for ek, fs in ef.items():
        if len(fs) == 1: typ = (int(FZ[fs[0]]), -1)
        elif FZ[fs[0]] != FZ[fs[1]]: typ = tuple(sorted((int(FZ[fs[0]]), int(FZ[fs[1]]))))
        else: continue
        groups.setdefault(typ, []).append(ek)
    pos, arc, tan, typ_ids, types, chain_ids = [], [], [], [], [], []
    for typ, edges in groups.items():
        types.append(typ); tid = len(types) - 1
        adj = {}
        for a, b in edges: adj.setdefault(a, []).append(b); adj.setdefault(b, []).append(a)
        used = set()
        starts = [v for v in adj if len(adj[v]) != 2] + list(adj)
        for s in starts:
            while any((min(s, n), max(s, n)) not in used for n in adj[s]):
                chain, cur = [s], s
                while True:
                    nxt = [n for n in adj[cur] if (min(cur, n), max(cur, n)) not in used]
                    if not nxt: break
                    n = nxt[0]; used.add((min(cur, n), max(cur, n))); chain.append(n); cur = n
                pts = V[chain].copy()
                closed = chain[0] == chain[-1] and len(chain) > 3
                # the fabric/leather seam follows triangle edges: remove the sawtooth (Taubin, no shrink)
                for it in range(2 * SMOOTH_IT if typ == (FAB, LEA) else 0):
                    lam = 0.5 if it % 2 == 0 else -0.53
                    if closed:
                        q = pts[:-1]; q = q + lam * (0.5 * (np.roll(q, 1, 0) + np.roll(q, -1, 0)) - q); pts = np.vstack([q, q[:1]])
                    else:
                        pts[1:-1] = pts[1:-1] + lam * (0.5 * (pts[:-2] + pts[2:]) - pts[1:-1])
                seg = np.linalg.norm(np.diff(pts, axis=0), axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
                Ln = cum[-1]
                if Ln < 1e-6: continue
                ss = np.linspace(0, Ln, max(2, int(Ln / step) + 1))
                q = np.stack([np.interp(ss, cum, pts[:, k]) for k in range(3)], -1)
                tg = np.gradient(q, axis=0); tg /= np.linalg.norm(tg, axis=1, keepdims=True) + 1e-12
                pos.append(q); arc.append(ss); tan.append(tg); typ_ids.append(np.full(len(q), tid)); chain_ids.append(np.full(len(q), len(chain_ids)))
    S["chain"] = np.concatenate(chain_ids)
    return np.concatenate(pos), np.concatenate(arc), np.concatenate(tan), np.concatenate(typ_ids), types

SMOOTH_IT = 6

def orient_samples(A, bp, bt, bty, types):
    """Snap samples to the surface; side vector points into types[k][0]."""
    me = bpy.data.objects["Glove_R"].data
    bvh = BVHTree.FromPolygons([Vector(v) for v in A["V"]], [tuple(p.vertices) for p in me.polygons])
    FZ = A["FZ"]; side = np.zeros_like(bp); vote = np.zeros(len(bp))
    for i in range(len(bp)):
        loc, n, fi, _ = bvh.find_nearest(Vector(bp[i]))
        bp[i] = loc[:]; s = np.cross(n[:], bt[i]); s /= np.linalg.norm(s) + 1e-12; side[i] = s
        typ = types[bty[i]]
        if typ[1] == -1: continue
        _, _, fj, _ = bvh.find_nearest(Vector(bp[i] + s * 1.5e-3))
        vote[i] = 1 if FZ[fj] == typ[0] else (-1 if FZ[fj] == typ[1] else 0)
    # one orientation per chain (majority) keeps it stable through the sawtooth
    ch = S["chain"]
    for c in np.unique(ch):
        k = ch == c
        if vote[k].sum() < 0: side[k] *= -1
    return bp, side

def stage_boundary(maxd=4.5e-3):
    A = S["A"]; P = S["P"]; t = S["t"]
    bp, ba, bt, bty, types = boundary_samples(A)
    bp, bs = orient_samples(A, bp, bt, bty, types)
    kd = KDTree(len(bp))
    for i, q in enumerate(bp): kd.insert(q, i)
    kd.balance()
    # skip triangles far from every boundary
    TL, LV, V = A["TL"], A["LV"], A["V"]
    tv = V[LV[TL]]; tc = tv.mean(1); tr = np.linalg.norm(tv - tc[:, None], axis=2).max(1)
    near = np.array([kd.find(c)[2] - r < maxd for c, r in zip(tc, tr)])
    D = np.full(len(P), 99.0, np.float32); AL = np.zeros(len(P), np.float32); TY = np.full(len(P), -1, np.int32)
    SD = np.zeros(len(P), np.float32)
    idx = np.nonzero(near[t])[0]
    NI = np.zeros(len(idx), np.int64)
    for j, k in enumerate(idx):
        co, i, d = kd.find(P[k]); NI[j] = i; D[k] = d * 1000
    rel = P[idx] - bp[NI]
    AL[idx] = (ba[NI] + (rel * bt[NI]).sum(1)) * 1000
    SD[idx] = (rel * bs[NI]).sum(1) * 1000
    TY[idx] = bty[NI]
    # effective zone: seams between zones follow the smoothed curve, not the triangle edges
    Z = S["Z"].copy()
    for tid, typ in enumerate(types):
        if typ[1] == -1: continue
        k = (TY == tid) & (D < 4.0) & ((Z == typ[0]) | (Z == typ[1]))
        Z[k] = np.where(SD[k] >= 0, typ[0], typ[1])
    S.update(D=D, AL=AL, TY=TY, SD=SD, types=types, Zeff=Z)
    print("boundary types", types, "samples", len(bp), "queried", len(idx), "of", len(P), "reassigned", int((Z != S["Z"]).sum()))

# ---------------------------------------------------------------- stage 4: materials
SRGB = lambda *c: np.array(c, np.float32)
C_FAB = SRGB(0.345, 0.365, 0.235); C_FAB_PANEL = SRGB(0.285, 0.305, 0.195)
C_LEA = SRGB(0.54, 0.44, 0.315);  C_LEA_PATCH = SRGB(0.40, 0.315, 0.225); C_GRIP = SRGB(0.20, 0.17, 0.135)
C_RUB = SRGB(0.072, 0.074, 0.078); C_RIM = SRGB(0.105, 0.105, 0.11)
C_STR = SRGB(0.155, 0.158, 0.15);  C_LOOP = SRGB(0.12, 0.122, 0.118); C_INN = SRGB(0.085, 0.085, 0.09)
C_THREAD = SRGB(0.60, 0.555, 0.40); C_THREAD_DK = SRGB(0.27, 0.27, 0.255); C_EMBLEM = SRGB(0.34, 0.345, 0.30)
# The strap band is a separate submesh tinted by the team material (_BaseColor), so its texels stay neutral.
C_BAND = SRGB(0.80, 0.80, 0.80); C_BAND_LOOP = SRGB(0.60, 0.60, 0.60); C_BAND_THREAD = SRGB(0.97, 0.97, 0.97)
BAND_NEUTRAL_TINT = (0.19, 0.195, 0.185)                         # reproduces the original dark strap

def bones_mm():
    o, arm = bpy.data.objects["Glove_R"], bpy.data.objects["OXRRightHand"]
    M = o.matrix_world.inverted() @ arm.matrix_world
    return {b.name: np.array(M @ b.head_local) * 1000 for b in arm.data.bones}

def rubber_shells(A):
    """Connected rubber pieces from the cached triangle arrays (works in any object mode)."""
    FZ, TL, LV, TP, V = A["FZ"], A["TL"], A["LV"], A["TP"], A["V"]
    parent = {}
    def find(a):
        while parent.setdefault(a, a) != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    rt = np.nonzero(FZ[TP] == RUB)[0]
    for t in rt:
        vs = LV[TL[t]]
        for v in vs[1:]: parent[find(int(v))] = find(int(vs[0]))
    shell = np.full(len(FZ), -1, np.int32)
    tri_shell = {int(t): find(int(LV[TL[t, 0]])) for t in rt}
    for t, s in tri_shell.items(): shell[TP[t]] = s
    info = {}
    for sid in set(tri_shell.values()):
        ts = [t for t, s in tri_shell.items() if s == sid]
        tv = V[LV[TL[ts]]] * 1000
        c = tv.mean(1); n = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0])   # area-weighted normals
        avg = n.sum(0); avg /= np.linalg.norm(avg)
        pts = tv.reshape(-1, 3)
        info[int(sid)] = dict(c=pts.mean(0), ext=pts.max(0) - pts.min(0), n=avg, faces=len(ts))
    return shell, info

def is_rim(inf): return inf["c"][1] < -55
def is_tab(inf): return -55 < inf["c"][1] < -30 and inf["ext"][2] < 12

def stage_material():
    A = S["A"]; P = S["P"] * 1000; N = S["N"]; Z = S["Zeff"]; D = S["D"]; AL = S["AL"]; TY = S["TY"]; types = S["types"]
    K = len(P)
    col = np.zeros((K, 3), np.float32); h = np.zeros(K, np.float32); sm = np.zeros(K, np.float32)
    B = bones_mm()
    rough_n = fbm(P / 6.0, 3, 7)             # low-frequency tone variation
    # ---------------- fabric
    m = Z == FAB
    wv = triplanar(lambda a, b: weave(a, b, 0.75), P[m], N[m])
    c = C_FAB * (0.86 + 0.18 * wv[:, None]) * (0.95 + 0.1 * rough_n[m][:, None])
    hh = 0.10 * wv; s = 0.10 + 0.06 * wv
    # padded back panel with perforations
    Pm, Nm = P[m], N[m]
    dorsal = (Nm[:, 2] > 0.5) & (Pm[:, 1] > 10) & (Pm[:, 1] < 60)
    x0, x1 = np.percentile(Pm[dorsal, 0], 2), np.percentile(Pm[dorsal, 0], 98)
    shells, sinfo = rubber_shells(A)
    knuckle = [v for v in sinfo.values() if v["ext"][0] > 35 and v["c"][1] > 0]
    kyl = min(v["c"][1] - v["ext"][1] * 0.5 for v in knuckle) if knuckle else 72.0
    pcx, pcy = (x0 + x1) * 0.5, (2.0 + kyl - 7.0) * 0.5
    phx, phy = (x1 - x0) * 0.5 - 8.0, (kyl - 7.0 - 2.0) * 0.5
    sd, al = rrect(Pm[:, 0] - pcx, Pm[:, 1] - pcy, phx, phy, 9.0)
    top = sstep(0.25, 0.45, Nm[:, 2])
    inside = (1 - sstep(-0.35, 0.35, sd)) * top
    puff = sstep(0.0, 3.0, -sd) * top
    c = c * (1 - inside[:, None]) + (C_FAB_PANEL * (0.86 + 0.18 * wv[:, None]) * (0.95 + 0.1 * rough_n[m][:, None])) * inside[:, None]
    hh += 0.45 * puff
    g = hexlattice(Pm[:, 0] - pcx, Pm[:, 1] - pcy, 2.6); r = np.linalg.norm(g, axis=1)
    hole = (1 - sstep(0.30, 0.42, r)) * sstep(-1.0, -2.6, sd) * top
    c *= (1 - 0.72 * hole)[:, None]; hh -= 0.25 * hole; s *= (1 - hole)
    sm_, sp_, sh_ = stitch(sd + 1.6, al)
    sm_ *= top; sp_ *= top; sh_ *= top
    c = c * (1 - sm_[:, None]) + C_THREAD * (0.8 + 0.2 * sp_[:, None]) * sm_[:, None]; hh += 0.16 * sp_; c *= (1 - 0.35 * sh_)[:, None]
    hh -= 0.22 * sstep(0.6, 0.0, np.abs(sd)) * top                    # panel seam groove
    # accordion flex zones over finger joints
    for f in ("Index", "Middle", "Ring", "Little", "Thumb"):
        joints = [("Intermediate", "Distal", 3.6), ("Distal", "Tip", 2.8)] if f != "Thumb" else [("Distal", "Tip", 3.4)]
        for a, b_, half in joints:
            J, Jn = B[f"XRHand_{f}{a}"], B[f"XRHand_{f}{b_}"]
            fd = (Jn - J); fd /= np.linalg.norm(fd)
            rel = Pm - J; tt = rel @ fd; lat = np.linalg.norm(rel - tt[:, None] * fd, axis=1)
            up = Nm @ np.array([0, 0, 1.0])
            wz = sstep(half, half - 1.0, np.abs(tt)) * sstep(11.0, 8.0, lat) * sstep(0.0, 0.35, up)
            rib = 0.5 + 0.5 * np.cos(2 * np.pi * tt / 1.25)
            c *= (1 - wz * (0.10 + 0.12 * (1 - rib)))[:, None]; hh += wz * 0.28 * rib; s += wz * 0.04 * rib
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- leather (palm, finger pads, tips)
    m = Z == LEA
    Pm, Nm = P[m], N[m]
    F1, F2 = voronoi(Pm / 0.55, 11)
    grain = np.clip((F2 - F1) / 0.35, 0, 1) ** 0.6
    wr = fbm(Pm / 2.5, 3, 21)
    c = C_LEA * (0.9 + 0.1 * grain[:, None]) * (0.93 + 0.14 * rough_n[m][:, None])
    hh = 0.09 * grain + 0.10 * wr; s = 0.30 + 0.08 * grain
    # flex creases across the palmar side of finger joints
    for f in ("Index", "Middle", "Ring", "Little", "Thumb"):
        joints = ("Intermediate", "Distal") if f != "Thumb" else ("Distal",)
        for a in joints:
            J = B[f"XRHand_{f}{a}"]; nxt = {"Intermediate": "Distal", "Distal": "Tip"}[a]
            fd = B[f"XRHand_{f}{nxt}"] - J; fd /= np.linalg.norm(fd)
            rel = Pm - J; tt = rel @ fd; lat = np.linalg.norm(rel - tt[:, None] * fd, axis=1)
            cr = sstep(0.4, 0.0, np.abs(np.abs(tt) - 0.8)) * sstep(10.0, 7.0, lat) * sstep(0.0, 0.3, -Nm[:, 2])
            c *= (1 - 0.22 * cr)[:, None]; hh -= 0.22 * cr
    palm = (Nm[:, 2] < -0.5) & (Pm[:, 1] > 0) & (Pm[:, 1] < 50)
    px0, px1 = np.percentile(Pm[palm, 0], 3), np.percentile(Pm[palm, 0], 97)
    py0 = np.percentile(Pm[Nm[:, 2] < -0.5, 1], 1)
    face = sstep(-0.25, -0.45, Nm[:, 2])
    sdA, alA = rrect(Pm[:, 0] - (px0 + px1) * 0.5, Pm[:, 1] - (py0 + 4 + 48) * 0.5, (px1 - px0) * 0.5 - 5, (48 - py0 - 4) * 0.5, 10.0)
    sdA = np.where(face > 0.01, sdA, 50.0)
    for sdP, alP in ((sdA, alA),):
        ins = 1 - sstep(-0.35, 0.35, sdP)
        c = c * (1 - ins[:, None]) + (C_LEA_PATCH * (0.88 + 0.12 * grain[:, None])) * ins[:, None]
        hh += 0.35 * sstep(0.4, -0.6, sdP); s = s * (1 - ins) + 0.2 * ins
        gl = hexlattice(Pm[:, 0], Pm[:, 1], 2.4); gr = np.linalg.norm(gl, axis=1)
        dot = (1 - sstep(0.55, 0.72, gr)) * sstep(-1.2, -2.6, sdP) * sstep(-0.6, -0.8, Nm[:, 2])
        c = c * (1 - dot[:, None]) + C_GRIP * dot[:, None]; hh += 0.22 * dot; s = s * (1 - dot) + 0.55 * dot
        a_, p_, o_ = stitch(sdP + 1.5, alP)
        c = c * (1 - a_[:, None]) + C_THREAD * 0.85 * (0.8 + 0.2 * p_[:, None]) * a_[:, None]; hh += 0.15 * p_; c *= (1 - 0.35 * o_)[:, None]
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- rubber (knuckle guards + cuff rim)
    m = Z == RUB
    Pm, Nm = P[m], N[m]
    sh = shells[A["TP"][S["t"][m]]]
    rim = [sid for sid, inf in sinfo.items() if is_rim(inf)]
    if rim: sh = np.where(sh < 0, rim[0], sh)                     # texels pulled in across the rim seam
    c = np.tile(C_RUB, (m.sum(), 1)); hh = np.zeros(m.sum(), np.float32); s = np.full(m.sum(), 0.42, np.float32)
    strapv = (Z == STR); cxz = P[strapv][:, [0, 2]].mean(0); Rc = np.linalg.norm(P[strapv][:, [0, 2]] - cxz, axis=1).mean()
    for sid, inf in sinfo.items():
        k = sh == sid
        if not k.any(): continue
        Pk, Nk = Pm[k], Nm[k]
        if is_rim(inf):                                            # cuff rim: elastic ribs
            th = np.arctan2(Pk[:, 2] - cxz[1], Pk[:, 0] - cxz[0]) * Rc
            rib = 0.5 + 0.5 * np.cos(2 * np.pi * th / 1.4)
            c[k] = C_RIM * (0.85 + 0.25 * rib[:, None]); hh[k] = 0.22 * rib; s[k] = 0.18 + 0.08 * rib
            continue
        topk = sstep(0.72, 0.9, Nk @ inf["n"])
        edge = (1 - topk) * sstep(0.25, 0.6, Nk @ inf["n"])
        if is_tab(inf):                                            # strap pull tab: emblem, grip grooves, stitched border
            top = sstep(0.85, 0.97, Nk @ inf["n"])
            lx, ly = Pk[:, 0] - inf["c"][0], Pk[:, 1] - inf["c"][1]
            cc = np.tile(C_RUB, (k.sum(), 1)) * (1 + 0.35 * edge[:, None]); hk = np.zeros(k.sum(), np.float32); sk = 0.5 + 0.15 * edge
            ring = sstep(0.55, 0.35, np.abs(np.hypot(lx, ly) - 3.6))
            def seg(ax_, ay_, bx_, by_):
                px, py = lx - ax_, ly - ay_; vx, vy = bx_ - ax_, by_ - ay_
                tt = np.clip((px * vx + py * vy) / (vx * vx + vy * vy), 0, 1)
                return np.hypot(px - tt * vx, py - tt * vy)
            vsh = sstep(0.5, 0.3, np.minimum(seg(-2.1, 1.7, 0, -1.9), seg(2.1, 1.7, 0, -1.9)))
            emb = np.maximum(ring, vsh) * top
            cc = cc * (1 - emb[:, None]) + C_EMBLEM * emb[:, None]; hk += 0.2 * emb; sk = sk * (1 - emb) + 0.3 * emb
            ax_ = np.abs(lx)
            grip = ((ax_ > 7.0) & (ax_ < 10.6)) * sstep(5.2, 4.4, np.abs(ly)) * top
            gv = sstep(0.62, 0.9, 0.5 + 0.5 * np.cos(2 * np.pi * (ax_ - 7.0) / 1.2)) * grip
            cc *= (1 - 0.35 * gv)[:, None]; hk -= 0.25 * gv
            sdT, alT = rrect(lx, ly, 13.0, 7.9, 2.6)
            a_, p_, o_ = stitch(sdT + 1.2, alT, w=0.42, L=2.4)
            a_ *= top; p_ *= top
            cc = cc * (1 - a_[:, None]) + C_THREAD_DK * a_[:, None]; hk += 0.12 * p_; cc *= (1 - 0.3 * o_ * top)[:, None]
            c[k], hh[k], s[k] = cc, hk, sk
            continue
        if inf["ext"][0] > 35:                                     # knuckle bar: hex cells
            g = hexlattice(Pk[:, 0], Pk[:, 1], 3.0); e = hexedge(g, 3.0)
            pat = sstep(0.44, 0.40, e)
        else:                                                      # finger pad: ribs across the finger
            fname = min(("Index", "Middle", "Ring", "Little"), key=lambda f: np.linalg.norm(B[f"XRHand_{f}Proximal"][:2] - inf["c"][:2]))
            J, Jn = B[f"XRHand_{fname}Proximal"], B[f"XRHand_{fname}Intermediate"]
            fd = (Jn - J) / np.linalg.norm(Jn - J); tt = (Pk - J) @ fd
            pat = sstep(0.25, 0.6, 0.5 + 0.5 * np.cos(2 * np.pi * tt / 2.3))
        hh[k] = 0.32 * pat * topk
        c[k] = C_RUB * (1 + 0.35 * edge[:, None] + 0.12 * pat[:, None] * topk[:, None])
        s[k] = 0.40 + 0.18 * edge + 0.08 * pat * topk
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- strap (hook-and-loop band) + logo tab
    m = Z == STR
    Pm, Nm = P[m], N[m]
    th = np.arctan2(Pm[:, 2] - cxz[1], Pm[:, 0] - cxz[0]); arcd = (th - np.pi / 2) * Rc
    arcd = np.where(arcd < -np.pi * Rc, arcd + 2 * np.pi * Rc, arcd)
    ymid = Pm[:, 1].mean()
    web = 0.5 + 0.5 * np.cos(2 * np.pi * Pm[:, 1] / 0.7); fine = vnoise(Pm * np.array([3.0, 0.6, 3.0]), 41)
    c = C_BAND * (0.85 + 0.15 * web[:, None]) * (0.95 + 0.1 * fine[:, None]); hh = 0.12 * web; s = np.full(m.sum(), 0.14, np.float32)
    E = 17.0                                                       # strap end edge (arc mm from dorsal top)
    beyond = sstep(-0.3, 0.3, arcd - E) * sstep(42.0, 40.0, arcd - E)
    fuzz = fbm(Pm / 0.35, 3, 51)
    c = c * (1 - beyond[:, None]) + (C_BAND_LOOP * (0.8 + 0.4 * fuzz[:, None])) * beyond[:, None]
    hh = hh * (1 - beyond) + 0.08 * fuzz * beyond - 0.55 * beyond
    a_, p_, o_ = stitch(arcd - E + 1.6, Pm[:, 1])
    a_ *= (1 - beyond); p_ *= (1 - beyond)
    c = c * (1 - a_[:, None]) + C_BAND_THREAD * a_[:, None]; hh += 0.14 * p_; c *= (1 - 0.3 * o_)[:, None]
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- inner lining (mesh)
    m = Z == INN
    g = triplanar(lambda a, b: hexedge(hexlattice(a, b, 1.6), 1.6), P[m], N[m])
    col[m] = C_INN * (0.7 + 0.6 * g[:, None]); h[m] = 0.15 * g; sm[m] = 0.06
    # ---------------- zone seams: grooves + topstitch rows
    ROWS = {(FAB, LEA): [(FAB, 1.4, C_THREAD), (LEA, 1.4, C_THREAD)], (FAB, STR): [(STR, 1.6, C_BAND_THREAD)], (FAB, RUB): [(FAB, 1.4, C_THREAD)]}
    for tid, typ in enumerate(types):
        k = TY == tid
        if not k.any(): continue
        if typ[1] == -1:                                           # open pad edge: contact shadow on fabric
            kk = k & (Z == FAB)
            col[kk] *= (0.62 + 0.38 * sstep(0.0, 2.2, D[kk]))[:, None]
            continue
        g = sstep(0.55, 0.0, D[k]); col[k] *= (1 - 0.3 * g)[:, None]; h[k] -= 0.25 * g
        for side, off, tc in ROWS.get(typ, []):
            kk = k & (Z == side)
            a_, p_, o_ = stitch(D[kk] - off, AL[kk])
            col[kk] = col[kk] * (1 - a_[:, None]) + tc * (0.8 + 0.2 * p_[:, None]) * a_[:, None]
            h[kk] += 0.16 * p_; sm[kk] = sm[kk] * (1 - a_) + 0.22 * a_; col[kk] *= (1 - 0.35 * o_)[:, None]
    # ---------------- ambient occlusion
    col *= (0.5 + 0.5 * S["AO"])[:, None]
    S.update(col=np.clip(col, 0, 1), h=h, sm=np.clip(sm, 0, 1))
    print("material done; zones", {z: int((Z == z).sum()) for z in np.unique(Z)})

# ---------------------------------------------------------------- stage 5: normal map + images
def grid(vals, fill=0.0):
    g = np.full((RES, RES) + vals.shape[1:], fill, np.float32); g[S["ys"], S["xs"]] = vals; return g

def pad(img, mask, n=40):
    img = img.copy(); mask = mask.copy()
    for _ in range(n):
        if mask.all(): break
        acc = np.zeros_like(img); cnt = np.zeros(mask.shape, np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sm_ = np.roll(np.roll(mask, dy, 0), dx, 1); si = np.roll(np.roll(img, dy, 0), dx, 1)
            acc += si * sm_[..., None]; cnt += sm_
        new = (~mask) & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]; mask = mask | new
    return img

def save_png(name, arr, noncolor):
    path = os.path.join(OUT, name + ".png")
    if bpy.data.images.get(name): bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, RES, RES, alpha=arr.shape[2] == 4)
    img.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    rgba = np.ones((RES, RES, 4), np.float32); rgba[..., :arr.shape[2]] = arr
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    img.alpha_mode = 'CHANNEL_PACKED'
    return img

def stage_images():
    os.makedirs(OUT, exist_ok=True)
    ev = S["ev"]; ys, xs = S["ys"], S["xs"]
    H = grid(S["h"])
    def d(axis):
        f, b = np.roll(H, -1, axis), np.roll(H, 1, axis)
        mf, mb = np.roll(ev, -1, axis), np.roll(ev, 1, axis)
        g = np.where(mf & mb, (f - b) * 0.5, np.where(mf, f - H, np.where(mb, H - b, 0.0)))
        return g[ys, xs]
    hx, hy = d(1), d(0)                                            # mm per pixel step (x = u, y = v)
    Pu, Pv = S["Pu"] * 1000, S["Pv"] * 1000                        # mm per pixel
    a11, a12, a22 = (Pu * Pu).sum(1), (Pu * Pv).sum(1), (Pv * Pv).sum(1)
    det = a11 * a22 - a12 * a12; det = np.where(det < 1e-12, 1e-12, det)
    ca, cb = (a22 * hx - a12 * hy) / det, (a11 * hy - a12 * hx) / det
    g = ca[:, None] * Pu + cb[:, None] * Pv
    gl = np.linalg.norm(g, axis=1, keepdims=True); g *= np.minimum(1.0, 2.5 / (gl + 1e-9))
    N = S["N"]; n2 = N - g; n2 /= np.linalg.norm(n2, axis=1, keepdims=True)
    T = S["T"] - N * (S["T"] * N).sum(1, keepdims=True); T /= np.linalg.norm(T, axis=1, keepdims=True) + 1e-9
    Bt = np.cross(N, T) * S["SG"][:, None]
    ts = np.stack([(n2 * T).sum(1), (n2 * Bt).sum(1), (n2 * N).sum(1)], -1)
    nrm = grid(ts * 0.5 + 0.5); alb = grid(np.concatenate([S["col"], S["sm"][:, None]], 1))
    nrm = pad(nrm, ev); alb = pad(alb, ev)
    nrm[~ev & (nrm.sum(-1) == 0)] = (0.5, 0.5, 1.0)
    save_png("T_TacticalGlove_Albedo", alb, False)
    save_png("T_TacticalGlove_Normal", nrm, True)
    print("saved to", OUT)

# ---------------------------------------------------------------- export for Unity
def vertex_weights(o):
    """Top-4 normalised skin weights per vertex (Unity Bone4); bone list = sorted vertex-group names."""
    bones = sorted(g.name for g in o.vertex_groups)
    col = {g.index: bones.index(g.name) for g in o.vertex_groups}
    out = []
    for v in o.data.vertices:
        inf = sorted(((col[g.group], g.weight) for g in v.groups if g.weight > 0), key=lambda t: -t[1])[:4]
        assert inf, f"{o.name}: vertex {v.index} has no skin weight"
        s = sum(w for _, w in inf)
        out.append(" ".join("%d %.6f" % (b, w / s) for b, w in inf))
    return bones, out

def export_dump(side, o, A):
    bones, W = vertex_weights(o)
    LV, LN, LUV, LT, LS = A["LV"], A["LN"], A["LUV"], A["LT"], A["LS"]
    key2idx, verts, remap = {}, [], np.zeros(len(LV), np.int64)
    for l in range(len(LV)):
        k = (int(LV[l]), *np.round(LN[l], 4), *np.round(LUV[l], 6), *np.round(LT[l], 4), float(LS[l]))
        if k not in key2idx: key2idx[k] = len(verts); verts.append(l)
        remap[l] = key2idx[k]
    os.makedirs(EXPORT, exist_ok=True)
    path = os.path.join(EXPORT, f"TacticalGlove_{side}.txt")
    with open(path, "w") as f:
        f.write("bones " + " ".join(bones) + "\n")
        for l in verts: f.write("v %.7f %.7f %.7f\n" % tuple(A["V"][LV[l]]))
        for l in verts: f.write("n %.6f %.6f %.6f\n" % tuple(LN[l]))
        for l in verts: f.write("t %.7f %.7f\n" % tuple(LUV[l]))
        for l in verts: f.write("g %.6f %.6f %.6f %.0f\n" % (*LT[l], LS[l]))
        for l in verts: f.write("w " + W[LV[l]] + "\n")
        sub = (A["FZ"][A["TP"]] == STR).astype(int)               # submesh 1 = team-tinted strap band
        for tl, sb in zip(A["TL"], sub): f.write("f %d %d %d %d\n" % (*remap[tl], sb))
    print(side, "split verts", len(verts), "tris", len(A["TL"]), "->", path)

def stage_export():
    R, L = bpy.data.objects["Glove_R"], bpy.data.objects["Glove_L"]
    export_dump("R", R, S["A"])
    export_dump("L", L, mesh_arrays(L))

def setup_band_material(tint=BAND_NEUTRAL_TINT):
    """Second slot for the strap band: same textures, albedo multiplied by the team tint (sRGB)."""
    import bmesh
    base = bpy.data.materials["M_TacticalGlove"]
    old = bpy.data.materials.get("M_TacticalGlove_Band")
    if old: bpy.data.materials.remove(old)  # stage_images replaces the images, so an old copy points at nothing
    band = base.copy(); band.name = "M_TacticalGlove_Band"
    nt = band.node_tree
    bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    ta = next(n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.endswith("Albedo"))
    mul = nt.nodes.new("ShaderNodeMix"); mul.name = "TeamTint"; mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs[0].default_value = 1.0; mul.location = (0, 250)
    mul.inputs[7].default_value = (*[c ** 2.2 for c in tint], 1.0)
    nt.links.new(ta.outputs["Color"], mul.inputs[6]); nt.links.new(mul.outputs[2], bs.inputs["Base Color"])
    for name in ("Glove_R", "Glove_L"):
        o = bpy.data.objects[name]; me = o.data
        while len(me.materials) < 2: me.materials.append(None)
        me.materials[0] = base; me.materials[1] = band
        if o.mode == 'EDIT':
            bm = bmesh.from_edit_mesh(me); zl = bm.faces.layers.int.get("zone")
            for f in bm.faces: f.material_index = 1 if f[zl] == STR else 0
            bmesh.update_edit_mesh(me)
        else:
            z = np.zeros(len(me.polygons), np.int32); me.attributes["zone"].data.foreach_get("value", z)
            me.polygons.foreach_set("material_index", (z == STR).astype(np.int32)); me.update()

def preview_scene():
    sc = bpy.data.scenes.get("GlovePreview")
    if sc: return sc
    sc = bpy.data.scenes.new("GlovePreview")
    sc.collection.children.link(bpy.data.collections["TacticalGlove"])
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try: sc.render.engine = eng; break
        except TypeError: pass
    sc.render.resolution_x, sc.render.resolution_y = 1400, 900
    sc.view_settings.view_transform = 'AgX'
    w = bpy.data.worlds.new("GlovePreviewWorld"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.42, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sc.world = w
    cam = bpy.data.objects.new("GlovePreviewCam", bpy.data.cameras.new("GlovePreviewCam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 85; cam.data.clip_start = 0.01
    for name, rot, e in (("Key", (0.7, 0.2, 0.6), 3.5), ("Fill", (1.2, 0.0, -2.4), 1.2), ("Rim", (-1.0, 0.0, 3.0), 2.0)):
        L = bpy.data.objects.new("GlovePreview" + name, bpy.data.lights.new("GlovePreview" + name, 'SUN')); L.data.energy = e
        L.rotation_euler = rot; sc.collection.objects.link(L)
    return sc

def render(name, eye, target=(0, 0.06, 1.3), pose=None):
    sc = preview_scene()
    cam = sc.camera; cam.location = Vector(target) + Vector(eye)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(RENDERS, name + ".png")
    win = bpy.context.window_manager.windows[0]
    with bpy.context.temp_override(window=win, scene=sc):
        bpy.ops.render.render(write_still=True, scene=sc.name)

def fist(on=True):
    for arm in (bpy.data.objects["OXRRightHand"], bpy.data.objects["OXRLeftHand"]):
        for pb in arm.pose.bones:
            pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0)
        if not on: continue
        M = arm.matrix_world
        for f, angs in (("Index", (75, 90, 55)), ("Middle", (80, 92, 55)), ("Ring", (82, 92, 55)), ("Little", (85, 90, 55))):
            for seg, a in zip(("Proximal", "Intermediate", "Distal"), angs):
                pb = arm.pose.bones[f"XRHand_{f}{seg}"]
                pb.rotation_quaternion = __import__("mathutils").Quaternion((1, 0, 0), math.radians(a))
        for seg, a in (("Proximal", 25), ("Distal", 35)):
            arm.pose.bones[f"XRHand_Thumb{seg}"].rotation_quaternion = __import__("mathutils").Quaternion((1, 0, 0), math.radians(a))

def stage_material_setup():
    mat = bpy.data.materials.get("M_TacticalGlove")
    nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled"); bs.location = (300, 0)
    ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = bpy.data.images["T_TacticalGlove_Albedo"]; ta.location = (-400, 200)
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = bpy.data.images["T_TacticalGlove_Normal"]; tn.location = (-400, -250)
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0; inv.location = (-50, 50)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.uv_map = "UVMap"; nm.location = (0, -250)
    uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = "UVMap"; uvn.location = (-650, 0)
    nt.links.new(uvn.outputs[0], ta.inputs[0]); nt.links.new(uvn.outputs[0], tn.inputs[0])
    nt.links.new(ta.outputs["Color"], bs.inputs["Base Color"])
    nt.links.new(ta.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bs.inputs["Normal"])
    nt.links.new(bs.outputs[0], out.inputs[0])
    bs.inputs["Metallic"].default_value = 0.0
    for img in (ta.image, tn.image):
        img.reload()
        if bpy.data.filepath: img.filepath = bpy.path.relpath(img.filepath_raw)  # not packed: the PNGs live in Assets

# ---------------------------------------------------------------- entry point
def object_mode():
    if bpy.context.mode != 'OBJECT':
        with bpy.context.temp_override(**view3d_override()):
            bpy.ops.object.mode_set(mode='OBJECT')

def build(unwrap_uv=False):
    """Textures into Assets + mesh dumps into export/. unwrap_uv only after a geometry edit: it re-lays the UVs."""
    object_mode()
    if unwrap_uv: stage_uv()
    stage_raster(); stage_boundary(); stage_material(); stage_images()
    stage_material_setup(); setup_band_material(); stage_export()

if __name__ == "__main__":
    build()
