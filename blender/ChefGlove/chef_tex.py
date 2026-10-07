# Procedural texture set + Unity mesh dump for the chef glove (Blender 5.x + numpy).
# Run inside ChefGlove.blend after chef_builder.py (Scripting > Run Script = build()); usage: README.md next to this file.
# Hand space: fingers +Y, dorsal +Z, little finger +X on the right hand; procedural math in mm.
# Raster, noise, UV and dump helpers are imported from ../TacticalGlove/glove_tex.py (shared, not copied).
import bpy, math, os, sys, importlib, numpy as np
from mathutils import Vector

def _here():
    f = globals().get("__file__")
    if f and os.path.isfile(f): return os.path.dirname(os.path.abspath(f))
    return os.path.dirname(bpy.data.filepath)  # Text Editor run: the .blend sits next to this script

HERE = _here()
_TACTICAL = os.path.normpath(os.path.join(HERE, "..", "TacticalGlove"))
if _TACTICAL not in sys.path: sys.path.insert(0, _TACTICAL)
import glove_tex as gt
importlib.reload(gt)
from glove_tex import sstep, vnoise, fbm, weave, stitch, rrect, unwrap, transfer_mirror, mesh_arrays, object_mode

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Assets", "Modes", "Burger", "Avatars", "ChefGlove")   # textures, imported by Unity as-is
EXPORT = os.path.join(HERE, "export")                                            # mesh dumps for the Unity importer
RENDERS = os.path.join(HERE, "renders")
GLOVE, CUFF, SLEEVE, INNER = 0, 1, 2, 5                                         # same as chef_builder.py
S = gt.S                                                                         # raster state lives in glove_tex

SRGB = lambda *c: np.array(c, np.float32)
C_COTTON = SRGB(0.93, 0.925, 0.90); C_FLOUR = SRGB(0.975, 0.96, 0.925); C_SPECK = SRGB(0.86, 0.82, 0.74)
C_TWILL = SRGB(0.93, 0.93, 0.915);  C_INNER = SRGB(0.50, 0.50, 0.49)
C_RED = SRGB(0.72, 0.07, 0.06)      # piping, buttons, embroidery; Burger is teamless, so the colour is baked in

HAT_T, HAT_SCALE = 0.42, 1.15       # embroidery: position between middle metacarpal and knuckle, size
BUTTON_ANGLE = 0.35                 # rad from +X (little-finger side) towards the back of the hand
BUTTON_R, BUTTON_GAP = 3.2, 11.0    # mm

# ---------------------------------------------------------------- helpers
def tri3(fn, P, N, sharp=4.0):
    """Triplanar with the second axis along the fingers wherever possible (knit wales run along y)."""
    w = np.abs(N) ** sharp; w /= w.sum(1, keepdims=True)
    return (w[:, 0] * fn(P[:, 2], P[:, 1]) + w[:, 1] * fn(P[:, 0], P[:, 2]) + w[:, 2] * fn(P[:, 0], P[:, 1])).astype(np.float32)

def knit(u, v, wale=0.9, course=0.75):
    """Jersey knit: two leaning legs per wale."""
    a = u / wale; fa = a - np.floor(a) - 0.5
    fb = np.mod(v / course + 0.5 * np.floor(a), 1.0)
    leg = np.abs(np.abs(fa) - (0.22 + 0.16 * (fb - 0.5)))
    return (np.clip(1 - leg / 0.2, 0, 1) ** 0.8 * (0.55 + 0.45 * np.sin(np.pi * fb))).astype(np.float32)

def ring_arc(th, period, rref):
    """Arc length (mm) around the sleeve, rounded so a whole number of periods fits: no seam at th = ±pi."""
    n = max(1, round(2 * np.pi * rref / period))
    return th / (2 * np.pi) * n * period

def bones_mm():
    o, arm = bpy.data.objects["ChefGlove_R"], bpy.data.objects["OXRRightHand"]
    M = o.matrix_world.inverted() @ arm.matrix_world
    return {b.name: np.array(M @ b.head_local) * 1000 for b in arm.data.bones}

def hat_strokes(x, y, w=0.6):
    """Chef hat outline (mm, top towards the fingers): puffed crown over a pleated band."""
    circ = lambda cx, cy, r: np.hypot(x - cx, y - cy) - r
    box = lambda cy, hy: rrect(x, y - cy, 5.4, hy, 0.8)[0]
    crown = np.minimum(np.minimum(circ(0, 4.0, 5.4), circ(-5.2, 1.8, 3.8)), np.minimum(circ(5.2, 1.8, 3.8), box(-0.5, 3.0)))
    band = box(-6.5, 3.0)
    d = np.abs(np.minimum(crown, band))
    d = np.minimum(d, np.where(np.abs(x) < 5.4, np.abs(y + 3.5), 9.0))                       # crown / band seam
    d = np.minimum(d, np.where((y > -9.5) & (y < -3.5), np.abs(np.abs(x) - 1.9), 9.0))        # pleats
    return d - w

# ---------------------------------------------------------------- stage 1: UV
UV_ANGLE = 55                       # smart-project limit; at 66 the curled finger sides fold onto each other in UV

def uv_overlap_px(A, res=1024):
    """Texels covered by more than one triangle's interior: shared texels paint one surface's colour on another."""
    cnt = np.zeros((res, res), np.int16)
    for t in range(len(A["TL"])):
        uv = A["LUV"][A["TL"][t]].astype(np.float64) * res - 0.5
        x0, y0 = np.maximum(np.floor(uv.min(0)).astype(int), 0); x1, y1 = np.minimum(np.ceil(uv.max(0)).astype(int), res - 1)
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        (ax, ay), (bx, by), (cx, cy) = uv
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12: continue
        w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den; w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        k = (w0 > 0.01) & (w1 > 0.01) & (1 - w0 - w1 > 0.01)
        cnt[ys[k], xs[k]] += 1
    return int((cnt > 1).sum())

def stage_uv():
    R, L = bpy.data.objects["ChefGlove_R"], bpy.data.objects["ChefGlove_L"]
    for o in (R, L):                                              # glove_tex's unwrap expects a "Palette" layer to keep
        if "Palette" not in o.data.uv_layers: o.data.uv_layers.new(name="Palette")
    unwrap(R, UV_ANGLE); transfer_mirror(R, L)
    for o in (R, L): o.data.uv_layers.remove(o.data.uv_layers["Palette"])
    n = uv_overlap_px(mesh_arrays(R))
    assert n == 0, f"UV islands overlap ({n} px at 1024): lower UV_ANGLE"

# ---------------------------------------------------------------- stage 2: raster + sleeve coordinates
def stage_raster():
    gt.stage_raster("ChefGlove_R")
    o = bpy.data.objects["ChefGlove_R"]; A = S["A"]
    pr = np.zeros(len(o.data.vertices), np.float32); o.data.attributes["prof"].data.foreach_get("value", pr)
    S["PR"] = (pr[A["LV"][A["TL"][S["t"]]]] * S["W"]).sum(1)      # mm along the sleeve profile
    cx, _, cz = o["axis_mm"]; P = S["P"] * 1000
    S["TH"] = np.arctan2(P[:, 2] - cz, P[:, 0] - cx)               # angle around the sleeve axis, 0 = little-finger side
    S["RAD"] = np.hypot(P[:, 2] - cz, P[:, 0] - cx)

# ---------------------------------------------------------------- stage 3: materials
def stage_material():
    P = S["P"] * 1000; N = S["N"]; Z = S["Z"]; PR = S["PR"]; TH = S["TH"]; RAD = S["RAD"]
    rings = dict(bpy.data.objects["ChefGlove_R"]["ring_prof"])
    K = len(P)
    col = np.zeros((K, 3), np.float32); h = np.zeros(K, np.float32); sm = np.zeros(K, np.float32)
    B = bones_mm()
    tone = fbm(P / 6.0, 3, 7)
    # ---------------- glove: cotton knit, chef-hat embroidery, flour
    m = Z == GLOVE
    Pm, Nm = P[m], N[m]
    kn = tri3(knit, Pm, Nm)
    fuzz = fbm(Pm / 0.35, 2, 13)
    c = C_COTTON * (0.9 + 0.12 * kn[:, None]) * (0.96 + 0.06 * tone[m][:, None]) * (0.97 + 0.05 * fuzz[:, None])
    hh = 0.09 * kn + 0.03 * fuzz; s = 0.10 + 0.05 * kn
    hc = (1 - HAT_T) * B["XRHand_MiddleMetacarpal"] + HAT_T * B["XRHand_MiddleProximal"]
    d = hat_strokes((Pm[:, 0] - hc[0]) / HAT_SCALE, (Pm[:, 1] - hc[1]) / HAT_SCALE) * HAT_SCALE
    emb = (1 - sstep(-0.12, 0.12, d)) * sstep(0.35, 0.55, Nm[:, 2])
    sat = 0.5 + 0.5 * np.cos(2 * np.pi * (Pm[:, 0] + Pm[:, 1]) / 0.32)   # satin-stitch threads
    prof = np.sqrt(np.clip(-d / 0.6, 0, 1))
    c = c * (1 - emb[:, None]) + C_RED * (0.82 + 0.22 * sat[:, None]) * emb[:, None]
    hh = hh * (1 - emb) + (0.12 + 0.18 * prof + 0.04 * sat) * emb; s = s * (1 - emb) + (0.32 + 0.1 * sat) * emb
    c *= (1 - 0.18 * sstep(0.5, 0.0, d) * (1 - emb) * sstep(0.35, 0.55, Nm[:, 2]))[:, None]   # thread pulls the knit in
    palm = sstep(0.1, -0.4, Nm[:, 2])
    tips = sstep(70.0, 95.0, Pm[:, 1])
    film = sstep(0.62, 0.85, fbm(Pm / 3.0, 4, 91) + 0.15 * palm + 0.12 * tips) * 0.18
    spk = sstep(0.86, 0.92, vnoise(Pm / 0.6, 77)) * sstep(0.5, 0.7, fbm(Pm / 7.0, 3, 78) + 0.2 * palm + 0.15 * tips)
    c = c * (1 - film[:, None]) + C_FLOUR * film[:, None]
    c = c * (1 - spk[:, None]) + C_SPECK * spk[:, None]; hh += 0.08 * spk; s *= (1 - 0.5 * film)
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- cuff + sleeve: chef-jacket twill, piping, buttons
    m = (Z == CUFF) | (Z == SLEEVE)
    Pm, Nm, pr, th, rad = P[m], N[m], PR[m], TH[m], RAD[m]
    rref = float(rad.mean())
    tw = 0.5 + 0.5 * np.cos(2 * np.pi * (ring_arc(th, 0.75, rref) + Pm[:, 1]) / 0.75)
    c = C_TWILL * (0.93 + 0.08 * tw[:, None]) * (0.96 + 0.06 * tone[m][:, None]); hh = 0.05 * tw; s = np.full(m.sum(), 0.12, np.float32)
    # piping: a red cord on the rolled front edge
    dp = pr - rings["lip"]
    pip = 1 - sstep(1.7, 2.2, np.abs(dp))
    cord = 0.5 + 0.5 * np.cos(2 * np.pi * (ring_arc(th, 1.2, rref) - dp) / 1.2)
    c = c * (1 - pip[:, None]) + C_RED * (0.85 + 0.2 * cord[:, None]) * pip[:, None]
    hh += pip * (0.3 * np.sqrt(np.clip(1 - (dp / 2.0) ** 2, 0, 1)) + 0.06 * cord); s = s * (1 - pip) + 0.3 * pip
    c *= (1 - 0.25 * sstep(0.7, 0.0, np.abs(np.abs(dp) - 2.2)) * (1 - pip))[:, None]   # piping seam shadow
    # topstitch rows along both cuff band edges, self-coloured
    for row in (rings["band_top"] + 2.2, rings["band_bot"] - 2.2):
        a_, p_, o_ = stitch(pr - row, ring_arc(th, 2.6, rref), w=0.45, L=2.6)
        c *= (1 - 0.06 * a_ - 0.25 * o_)[:, None]; hh += 0.1 * p_ - 0.08 * sstep(0.5, 0.0, np.abs(pr - row))
    # crease where the cuff folds over the sleeve
    fold = sstep(rings["band_bot"] - 1.0, rings["fold"], pr) * sstep(rings["fold"] + 3.0, rings["fold"], pr)
    c *= (1 - 0.3 * fold)[:, None]; hh -= 0.2 * fold
    # two buttons on the little-finger side of the cuff band
    mid = 0.5 * (rings["band_top"] + rings["band_bot"])
    for pb in (mid - BUTTON_GAP * 0.5, mid + BUTTON_GAP * 0.5):
        du = np.angle(np.exp(1j * (th - BUTTON_ANGLE))) * rad; dv = pr - pb
        r = np.hypot(du, dv) / BUTTON_R
        on = (1 - sstep(0.94, 1.0, r)) * (Pm[:, 1] < 0)
        dome = np.sqrt(np.clip(1 - r * r, 0, 1))
        rim = sstep(0.62, 0.72, r) * sstep(0.96, 0.86, r)
        hole = sum(1 - sstep(0.09, 0.14, np.hypot(du / BUTTON_R - ox, dv / BUTTON_R - oy)) for ox, oy in ((-0.2, -0.2), (0.2, -0.2), (-0.2, 0.2), (0.2, 0.2)))
        bc = C_RED * (0.72 + 0.28 * dome[:, None] + 0.12 * rim[:, None]) * (1 - 0.6 * hole[:, None])
        c = c * (1 - on[:, None]) + bc * on[:, None]
        hh = hh * (1 - on) + (0.55 + 0.35 * dome + 0.12 * rim - 0.25 * hole) * on; s = s * (1 - on) + 0.68 * on
        shade = sstep(1.45, 1.0, r) * (1 - on) * (Pm[:, 1] < 0)
        c *= (1 - 0.35 * shade)[:, None]
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- sleeve interior
    m = Z == INNER
    c = C_INNER * (0.9 + 0.15 * tone[m][:, None]); col[m] = c; h[m] = 0.0; sm[m] = 0.05
    # ---------------- ambient occlusion
    col *= (0.5 + 0.5 * S["AO"])[:, None]
    S.update(col=np.clip(col, 0, 1), h=h, sm=np.clip(sm, 0, 1))
    print("material done; zones", {int(z): int((Z == z).sum()) for z in np.unique(Z)})

# ---------------------------------------------------------------- Blender material + preview
def stage_material_setup():
    mat = bpy.data.materials.get("M_ChefGlove") or bpy.data.materials.new("M_ChefGlove")
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled"); bs.location = (300, 0)
    ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = bpy.data.images["T_ChefGlove_Albedo"]; ta.location = (-400, 200)
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = bpy.data.images["T_ChefGlove_Normal"]; tn.location = (-400, -250)
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
    for name in ("ChefGlove_R", "ChefGlove_L"):
        me = bpy.data.objects[name].data; me.materials.clear(); me.materials.append(mat)

def preview_scene():
    sc = bpy.data.scenes.get("ChefPreview")
    if sc: return sc
    sc = bpy.data.scenes.new("ChefPreview")
    sc.collection.children.link(bpy.data.collections["ChefGlove"])
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try: sc.render.engine = eng; break
        except TypeError: pass
    sc.render.resolution_x, sc.render.resolution_y = 1400, 900
    sc.view_settings.view_transform = 'AgX'
    w = bpy.data.worlds.new("ChefPreviewWorld"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.42, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sc.world = w
    cam = bpy.data.objects.new("ChefPreviewCam", bpy.data.cameras.new("ChefPreviewCam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 85; cam.data.clip_start = 0.01
    for name, rot, e in (("Key", (0.7, 0.2, 0.6), 3.0), ("Fill", (1.2, 0.0, -2.4), 1.0), ("Rim", (-1.0, 0.0, 3.0), 1.6)):
        L = bpy.data.objects.new("ChefPreview" + name, bpy.data.lights.new("ChefPreview" + name, 'SUN')); L.data.energy = e
        L.rotation_euler = rot; sc.collection.objects.link(L)
    return sc

def render(name, eye, target=(0, 0.06, 0), spread=0.075):
    """Hands sit at the origin on top of each other; spread moves the armatures apart for the shot only."""
    sc = preview_scene()
    arms = (bpy.data.objects["OXRRightHand"], bpy.data.objects["OXRLeftHand"])
    arms[0].location.x, arms[1].location.x = spread, -spread
    try:
        cam = sc.camera; cam.location = Vector(target) + Vector(eye)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(RENDERS, name + ".png")
        win = bpy.context.window_manager.windows[0]
        with bpy.context.temp_override(window=win, scene=sc):
            bpy.ops.render.render(write_still=True, scene=sc.name)
    finally:
        arms[0].location.x = arms[1].location.x = 0.0

# ---------------------------------------------------------------- entry point
def stage_export():
    R, L = bpy.data.objects["ChefGlove_R"], bpy.data.objects["ChefGlove_L"]
    gt.export_dump("R", R, S["A"], name="ChefGlove", sub_zone=None, export=EXPORT)
    gt.export_dump("L", L, mesh_arrays(L), name="ChefGlove", sub_zone=None, export=EXPORT)

def build(unwrap_uv=False):
    """Textures into Assets + mesh dumps into export/. UVs are laid out on the first run after chef_builder.py."""
    object_mode()
    if unwrap_uv or "UVMap" not in bpy.data.objects["ChefGlove_R"].data.uv_layers: stage_uv()
    stage_raster(); stage_material(); gt.stage_images("T_ChefGlove", OUT)
    stage_material_setup(); stage_export()

if __name__ == "__main__":
    build()
