"""CC0 texture sources (Poly Haven, ambientCG): download into the local cache and build image materials.

A MATS entry with tex=(site, asset_id, res) uses real maps; its T must be the asset's real size in metres
(Poly Haven /info 'dimensions', mm). The cache (blender/ZombieManor/tex_cache/) is not in git: every file
is re-downloadable from the ids below."""
import os
import urllib.request
import zipfile

import bpy

CACHE = os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "tex_cache")
PH_URL = "https://dl.polyhaven.org/file/ph-assets/Textures/jpg/{res}/{id}/{id}_{map}_{res}.jpg"
ACG_URL = "https://ambientcg.com/get?file={id}_{res}-JPG.zip"
UA = {"User-Agent": "VortexArena-ZombieManor/1.0"}


def _get(url, path):
    if os.path.exists(path):
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=120) as r, open(path + ".part", "wb") as f:
        f.write(r.read())
    os.replace(path + ".part", path)
    return path


# ---------------------------------------------------------------- generated albedos (no CC0 source exists)

def _save_png(path, rgb):
    """rgb: numpy (H, W, 3) float 0..1 (display/sRGB values), row 0 = bottom."""
    import numpy as np
    h, w, _ = rgb.shape
    im = bpy.data.images.new("zm_gen_tmp", w, h, alpha=False)
    rgba = np.concatenate([np.clip(rgb, 0, 1), np.ones((h, w, 1))], axis=2).astype(np.float32)
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)


def _damask(N, bg, fg, stain_path):
    """Tone-on-tone damask, half-drop repeat (motifs at the tile centre and corners): tiles seamlessly."""
    import numpy as np
    v, u = (np.mgrid[0:N, 0:N] + 0.5) / N
    cov = np.zeros((N, N))

    def ell(x, y, cx, cy, rx, ry, ang=0.0):
        c, s = np.cos(np.radians(ang)), np.sin(np.radians(ang))
        dx, dy = x - cx, y - cy
        a, b = dx * c + dy * s, -dx * s + dy * c
        d = np.sqrt((a / rx) ** 2 + (b / ry) ** 2)
        return np.clip((1.0 - d) * min(rx, ry) * N / 1.2, 0, 1)

    for ocx, ocy in ((0.5, 0.5), (0.0, 0.0)):
        x = (u - ocx + 0.5) % 1.0 - 0.5            # wrapped: the corner motif is whole across the four corners
        y = (v - ocy + 0.5) % 1.0 - 0.5
        ax = np.abs(x)                             # mirror symmetry
        parts = [
            ell(ax, y, 0, 0, 0.11, 0.16) - ell(ax, y, 0, 0, 0.078, 0.124),     # medallion ring
            ell(ax, y, 0, 0.0, 0.016, 0.1),                                     # inner fleur: spine + two petals
            ell(ax, y, 0.035, -0.01, 0.014, 0.055, 30), ell(ax, y, 0, -0.085, 0.04, 0.012),
            ell(ax, y, 0.0, -0.25, 0.013, 0.12),                                # stem
            ell(ax, y, 0.085, -0.28, 0.034, 0.11, 35), ell(ax, y, 0.17, -0.05, 0.03, 0.12, -50),
            ell(ax, y, 0.2, 0.12, 0.062, 0.062) - ell(ax, y, 0.2, 0.12, 0.047, 0.047),   # scrolls
            ell(ax, y, 0.2, 0.12, 0.016, 0.016),
            ell(ax, y, 0.07, 0.24, 0.026, 0.1, 25), ell(ax, y, 0.0, 0.33, 0.026, 0.08),
            ell(ax, y, 0.12, 0.33, 0.02, 0.06, 55), ell(ax, y, 0.0, 0.42, 0.018, 0.018),
            ell(ax, y, 0.0, -0.385, 0.1, 0.028), ell(ax, y, 0.14, -0.36, 0.015, 0.015),
        ]
        for p in parts:
            cov = np.maximum(cov, np.clip(p, 0, 1))
    img = np.asarray(bg)[None, None, :] * (1 - cov[..., None]) + np.asarray(fg)[None, None, :] * cov[..., None]
    if stain_path:                                   # age it with the decrepit paper's own stains
        st = bpy.data.images.load(stain_path)
        sw, sh = st.size
        px = np.array(st.pixels[:]).reshape(sh, sw, 4)[..., :3]
        bpy.data.images.remove(st)
        ys = (np.arange(N) * sh // N)
        xs = (np.arange(N) * sw // N)
        lum = px[ys][:, xs].mean(axis=2)
        img *= np.clip(lum / lum.mean(), 0.7, 1.15)[..., None]
    return img


def _window_view(N):
    """Night sky seen through a window: a plain dusk gradient, faint light low on the horizon."""
    import numpy as np
    v = ((np.mgrid[0:N, 0:N] + 0.5) / N)[0]
    lo, hi = np.array([0.2, 0.23, 0.29]), np.array([0.05, 0.065, 0.1])
    return lo[None, None, :] * (1 - v[..., None]) + hi[None, None, :] * v[..., None]


def _rug(N, size, grain_path):
    """Worn Persian-style rug filling the whole canvas (size = (w, h) m): borders, lattice field, central
    medallion with corner spandrels; aged with a fabric grain and faded wear patches."""
    import numpy as np
    W_, H_ = size
    v, u = (np.mgrid[0:N, 0:N] + 0.5) / N
    X, Y = (u - 0.5) * W_, (v - 0.5) * H_
    red, navy = np.array([0.33, 0.05, 0.045]), np.array([0.05, 0.07, 0.16])
    ivory, gold, dark = np.array([0.72, 0.62, 0.42]), np.array([0.62, 0.43, 0.16]), np.array([0.05, 0.03, 0.025])
    img = np.zeros((N, N, 3)) + red
    put = lambda mask, col: img.__setitem__(mask, col)
    d = np.minimum(W_ / 2 - np.abs(X), H_ / 2 - np.abs(Y))          # distance to the nearest edge
    # field: half-drop lattice of small gold diamonds with navy hearts
    for ox, oy in ((0.0, 0.0), (0.1, 0.1)):
        lx = (X - ox + 0.1) % 0.2 - 0.1
        ly = (Y - oy + 0.1) % 0.2 - 0.1
        r = np.abs(lx) + np.abs(ly)
        put((r < 0.055) & (d > 0.21), gold)
        put((r < 0.025) & (d > 0.21), navy)
    # corner spandrels + central medallion (lobed diamond)
    ang = np.arctan2(Y, X)
    for cx, cy, s in [(0, 0, 1.0)] + [(sx * W_ / 2, sy * H_ / 2, 0.9) for sx in (-1, 1) for sy in (-1, 1)]:
        dx, dy = (X - cx) / (0.55 * s), (Y - cy) / (0.36 * s)
        a = np.arctan2(dy, dx)
        rr = (np.abs(dx) ** 1.3 + np.abs(dy) ** 1.3) ** (1 / 1.3) * (1 - 0.08 * np.cos(8 * a))
        inside = d > 0.2
        put(inside & (rr < 1.0), gold)
        put(inside & (rr < 0.94), navy)
        put(inside & (rr < 0.6) & (rr > 0.52), gold)
        put(inside & (rr < 0.4), ivory)
        put(inside & (rr < 0.3), red)
        put(inside & (rr < 0.1), gold)
    # borders: dark rim, ivory guard, navy main band with gold rosettes, ivory guard, dark line
    put(d < 0.21, dark)
    put(d < 0.19, ivory)
    put(d < 0.17, navy)
    t = np.where(W_ / 2 - np.abs(X) < H_ / 2 - np.abs(Y), Y, X)    # position along the nearest edge
    lt = (t + 0.06) % 0.12 - 0.06
    ro = np.hypot(lt, d - 0.11)
    put((d < 0.17) & (d > 0.05) & (ro < 0.04), gold)
    put((d < 0.17) & (d > 0.05) & (ro < 0.018), red)
    put(d < 0.05, ivory)
    put(d < 0.03, dark)
    if grain_path:                                    # weave grain + faded wear
        st = bpy.data.images.load(grain_path)
        sw, sh = st.size
        px = np.array(st.pixels[:]).reshape(sh, sw, 4)[..., :3]
        bpy.data.images.remove(st)
        lum = px[(np.arange(N) * 3 % sh)][:, (np.arange(N) * 3 % sw)].mean(axis=2)
        img *= np.clip(lum / lum.mean(), 0.75, 1.2)[..., None]
    wear = 0.5 + 0.5 * np.sin(X * 3.1 + 1.3) * np.sin(Y * 4.3 + 0.4) * np.cos(X * 1.7 - Y * 2.9)
    img = img * (1 - 0.25 * wear[..., None]) + np.array([0.42, 0.36, 0.3]) * 0.25 * wear[..., None]
    return img


def _tile(N, bg, fg, acc, grain_path):
    """One Victorian majolica fireplace tile (tiles seamlessly): glaze field, framed four-petal flower,
    corner quarter-rosettes that join into a secondary motif across tiles, dark grout at the edges."""
    import numpy as np
    v, u = (np.mgrid[0:N, 0:N] + 0.5) / N
    x, y = u - 0.5, v - 0.5
    img = np.zeros((N, N, 3)) + np.asarray(bg)
    r = np.hypot(x, y)
    a = np.arctan2(y, x)
    put = lambda m, c: img.__setitem__(m, np.asarray(c))
    put(np.abs(np.maximum(np.abs(x), np.abs(y)) - 0.36) < 0.012, acc)                  # inner frame line
    petal = r < 0.25 * (0.55 + 0.45 * np.abs(np.cos(2 * a)))                           # four petals
    put(petal, fg)
    put(petal & (r < 0.25 * (0.4 + 0.35 * np.abs(np.cos(2 * a)))), np.asarray(fg) * 0.8 + np.asarray(bg) * 0.2)
    put(r < 0.06, acc)
    put(r < 0.03, fg)
    for k in range(4):                                                                 # leaves between petals
        ang = np.pi / 4 + k * np.pi / 2
        cx, cy = 0.27 * np.cos(ang), 0.27 * np.sin(ang)
        dx, dy = x - cx, y - cy
        c_, s_ = np.cos(-ang), np.sin(-ang)
        a_, b_ = dx * c_ - dy * s_, dx * s_ + dy * c_
        put((a_ / 0.07) ** 2 + (b_ / 0.025) ** 2 < 1, acc)
    rc = np.hypot(0.5 - np.abs(x), 0.5 - np.abs(y))                                    # corner quarter-rosettes
    put(rc < 0.12, fg)
    put(rc < 0.07, acc)
    edge = 0.5 - np.maximum(np.abs(x), np.abs(y))
    shade = np.clip(edge / 0.03, 0, 1)[..., None]                                      # glaze rolls off at the edge
    img = img * (0.75 + 0.25 * shade)
    img[edge < 0.008] = (0.09, 0.08, 0.07)                                             # grout
    if grain_path:
        st = bpy.data.images.load(grain_path)
        sw, sh = st.size
        px = np.array(st.pixels[:]).reshape(sh, sw, 4)[..., :3]
        bpy.data.images.remove(st)
        lum = px[(np.arange(N) * sh // N)][:, (np.arange(N) * sw // N)].mean(axis=2)
        img *= np.clip(lum / lum.mean(), 0.8, 1.15)[..., None]
    return img


def generated(spec):
    """Path of a generated albedo (cached by its parameters)."""
    import hashlib
    g = spec["gen"]
    key = hashlib.md5(repr(sorted(g.items())).encode()).hexdigest()[:8]
    path = os.path.join(CACHE, f"gen_{spec['tex'][1]}_{key}", "color.png")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        N = int(spec["tex"][2])
        if g["kind"] == "damask":
            stain = maps(spec["base"])["color"] if spec.get("base") else None
            _save_png(path, _damask(N, g["bg"], g["fg"], stain))
        elif g["kind"] == "window":
            _save_png(path, _window_view(N))
        elif g["kind"] == "tile":
            grain = maps(spec["base"])["color"] if spec.get("base") else None
            _save_png(path, _tile(N, g["bg"], g["fg"], g["acc"], grain))
        elif g["kind"] == "rug":
            grain = maps(spec["base"])["color"] if spec.get("base") else None
            _save_png(path, _rug(N, g["size"], grain))
    return path


def maps(tex):
    """{'color', 'normal', 'rough', 'metal'?, 'ao'?} local file paths for a tex tuple (downloads if missing)."""
    site, aid, res = tex
    d = os.path.join(CACHE, f"{site}_{aid}_{res}")
    if site == "ph":
        # arm = AO (R) + roughness (G) + metallic (B); file names come from the API (some assets break the
        # usual {id}_{map}_{res} pattern)
        want = (("color", "diff", "Diffuse"), ("normal", "nor_gl", "nor_gl"), ("arm", "arm", "arm"))
        paths = {k: os.path.join(d, f"{m}.jpg") for k, m, _ in want}
        if not all(os.path.exists(p_) for p_ in paths.values()):
            import json
            with urllib.request.urlopen(urllib.request.Request(f"https://api.polyhaven.com/files/{aid}", headers=UA),
                                        timeout=60) as r:
                files = json.loads(r.read())
            for k, m, key in want:
                _get(files[key][res]["jpg"]["url"], paths[k])
        return paths
    zp = _get(ACG_URL.format(id=aid, res=res.upper()), os.path.join(d, "src.zip"))
    with zipfile.ZipFile(zp) as z:
        names = z.namelist()
        for n in names:
            if n.lower().endswith(".jpg") and not os.path.exists(os.path.join(d, n)):
                z.extract(n, d)
    pick = lambda tag: next((os.path.join(d, n) for n in names if n.endswith(f"_{tag}.jpg")), None)
    out = {"color": pick("Color"), "normal": pick("NormalGL"), "rough": pick("Roughness")}
    if pick("Metalness"):
        out["metal"] = pick("Metalness")
    if pick("AmbientOcclusion"):
        out["ao"] = pick("AmbientOcclusion")
    return out


def _img(path, data):
    name = os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path)
    im = bpy.data.images.get(name)
    if im is None:
        im = bpy.data.images.load(path, check_existing=True)
        im.name = name
    im.colorspace_settings.name = "sRGB" if not data else "Non-Color"
    return im


def build(m, spec, uv_socket, nodes, links, bsdf):
    """Wires real maps into a material: albedo (optionally greyed + tinted), normal, roughness, metallic.
    site 'gen': the albedo is generated, normal/roughness come from spec['base'] (if any)."""
    if spec["tex"][0] == "gen":
        mp = dict(maps(spec["base"])) if spec.get("base") else {}
        mp["color"] = generated(spec)
    else:
        mp = maps(spec["tex"])

    def tex_node(path, data):
        n = nodes.new("ShaderNodeTexImage")
        n.image = _img(path, data)
        links.new(uv_socket, n.inputs["Vector"])
        return n

    col = tex_node(mp["color"], False).outputs["Color"]
    if spec.get("grey"):                                       # recolour a painted texture: keep its shading only
        bw = nodes.new("ShaderNodeRGBToBW")
        links.new(col, bw.inputs[0])
        col = bw.outputs[0]
    if spec.get("tint"):
        mx = nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        next(s for s in mx.inputs if s.identifier == "Factor_Float").default_value = 1.0
        links.new(col, next(s for s in mx.inputs if s.identifier == "A_Color"))
        next(s for s in mx.inputs if s.identifier == "B_Color").default_value = tuple(spec["tint"]) + (1,)
        col = next(s for s in mx.outputs if s.identifier == "Result_Color")
    links.new(col, bsdf.inputs["Base Color"])
    if spec.get("emit"):                                       # self-lit picture (window view)
        links.new(col, bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = spec["emit"]

    if "normal" in mp:
        nm = nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = spec.get("nstr", 1.0)   # R6 bakes this into the exported normal
        links.new(tex_node(mp["normal"], True).outputs["Color"], nm.inputs["Color"])
        links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

    def rough_in(sock):
        """gloss < 1 dulls a scan's gloss: rough' = 1 - (1 - rough) * gloss (the export bakes the result)."""
        g = spec.get("gloss")
        if g is None:
            links.new(sock, bsdf.inputs["Roughness"])
            return
        inv = nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        links.new(sock, inv.inputs[1])
        mul = nodes.new("ShaderNodeMath")
        mul.operation = "MULTIPLY"
        links.new(inv.outputs[0], mul.inputs[0])
        mul.inputs[1].default_value = g
        out = nodes.new("ShaderNodeMath")
        out.operation = "SUBTRACT"
        out.inputs[0].default_value = 1.0
        links.new(mul.outputs[0], out.inputs[1])
        links.new(out.outputs[0], bsdf.inputs["Roughness"])

    if "arm" in mp:
        sep = nodes.new("ShaderNodeSeparateColor")
        links.new(tex_node(mp["arm"], True).outputs["Color"], sep.inputs[0])
        rough_in(sep.outputs[1])
        if spec.get("metal"):
            links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    elif "rough" in mp:
        rough_in(tex_node(mp["rough"], True).outputs["Color"])
        if "metal" in mp:
            links.new(tex_node(mp["metal"], True).outputs["Color"], bsdf.inputs["Metallic"])
    m.diffuse_color = tuple(spec.get("tint", (0.5, 0.5, 0.5))) + (1,)
