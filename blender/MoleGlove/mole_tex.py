# Procedural texture set + Unity mesh dump for the mole-game leather glove (Blender 5.x + numpy).
# Run inside MoleGlove.blend after mole_builder.py (Scripting > Run Script = build()); usage: README.md next to this file.
# Hand space: fingers +Y, dorsal +Z, little finger +X on the right hand; procedural math in mm.
# Raster, noise, UV and dump helpers come from ../TacticalGlove/glove_tex.py, sleeve helpers from ../ChefGlove/chef_tex.py.
import bpy, math, os, sys, importlib, numpy as np
from mathutils import Vector

def _here():
    f = globals().get("__file__")
    if f and os.path.isfile(f): return os.path.dirname(os.path.abspath(f))
    return os.path.dirname(bpy.data.filepath)  # Text Editor run: the .blend sits next to this script

HERE = _here()
for _d in ("TacticalGlove", "ChefGlove"):
    _p = os.path.normpath(os.path.join(HERE, "..", _d))
    if _p not in sys.path: sys.path.insert(0, _p)
import glove_tex as gt
importlib.reload(gt)
import chef_tex as ct
importlib.reload(ct)
from glove_tex import sstep, fbm, voronoi, stitch, rrect, unwrap, transfer_mirror, mesh_arrays, object_mode
from chef_tex import ring_arc, uv_overlap_px

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Assets", "Modes", "Mole", "Avatars", "MoleGlove")     # textures, imported by Unity as-is
EXPORT = os.path.join(HERE, "export")                                           # mesh dumps for the Unity importer
RENDERS = os.path.join(HERE, "renders")
GLOVE, CUFF, STRAP, INNER = 0, 1, 3, 5                                          # same as mole_builder.py
S = gt.S                                                                        # raster state lives in glove_tex

SRGB = lambda *c: np.array(c, np.float32)
# Tuned for Unity without tonemapping: AgX in the Blender preview mutes these, the headset does not.
C_LEA = SRGB(0.77, 0.56, 0.33);    C_LEA_WORN = SRGB(0.86, 0.69, 0.47); C_PATCH = SRGB(0.58, 0.39, 0.21)
C_THREAD = SRGB(0.88, 0.78, 0.58); C_INNER = SRGB(0.28, 0.20, 0.13)
C_BADGE = SRGB(0.93, 0.84, 0.66);  C_WOOD = SRGB(0.62, 0.38, 0.18);     C_WOOD_DK = SRGB(0.46, 0.27, 0.12)
C_DIRT = SRGB(0.40, 0.25, 0.13);   C_ICON_LINE = SRGB(0.27, 0.16, 0.08)
# Strap: light grey on purpose, the team colour multiplies it (LocalGloves tints material slot 1).
C_BAND = SRGB(0.80, 0.80, 0.80);   C_BAND_TAB = SRGB(0.86, 0.86, 0.86); C_BAND_THREAD = SRGB(0.97, 0.97, 0.97)
BAND_PREVIEW_TINT = (0.851, 0.20, 0.20)  # Girdap.Red, Blender preview only

BADGE_T, BADGE_R = 0.5, 12.0        # back-of-hand badge: between middle metacarpal and knuckle, radius (mm)
TAB_ANGLE, TAB_HW = math.pi / 2 + 0.2, 13.0   # strap tab: centre (rad from +X, pi/2 = back of the hand), half width (mm)
FINGERS = ("Index", "Middle", "Ring", "Little")

# ---------------------------------------------------------------- helpers
def objs():
    return bpy.data.objects["MoleGlove_R"], bpy.data.objects["MoleGlove_L"]

def bones_mm():
    o, arm = bpy.data.objects["MoleGlove_R"], bpy.data.objects["OXRRightHand"]
    M = o.matrix_world.inverted() @ arm.matrix_world
    return {b.name: np.array(M @ b.head_local) * 1000 for b in arm.data.bones}

def capsule(x, y, a, b, r):
    """SDF of a 2D segment a-b with radius r."""
    pa = np.stack([x - a[0], y - a[1]], -1); ba = np.array(b) - np.array(a)
    t = np.clip((pa @ ba) / (ba @ ba), 0, 1)
    return np.linalg.norm(pa - t[:, None] * ba, axis=1) - r

def badge_icon(u, v):
    """Mallet over a dirt mound (mm, top towards the fingers). Returns (mound, handle, head, head_band) SDFs."""
    mound = (np.hypot(u / 7.8, (v + 5.6) / 3.4) - 1) * 3.4
    for bx, by, br in ((-3.6, -3.4, 1.8), (2.4, -3.0, 2.0)):        # clods
        mound = np.minimum(mound, np.hypot(u - bx, v - by) - br)
    mound = np.maximum(mound, -(v + 6.8))                           # flat ground line
    hx, hy, ex, ey = -1.6, 3.0, 4.4, -2.4                           # head centre, handle end
    handle = capsule(u, v, (hx, hy), (ex, ey), 1.0)
    dx, dy = ex - hx, ey - hy; L = math.hypot(dx, dy); dx, dy = dx / L, dy / L
    p, q = -(u - hx) * dy + (v - hy) * dx, (u - hx) * dx + (v - hy) * dy   # p across the handle = along the head
    head = rrect(p, q, 4.8, 2.9, 0.9)[0]
    band = np.maximum(head + 0.45, np.abs(np.abs(p) - 3.3) - 0.4)   # darker rings near both striking faces
    return mound, handle, head, band

# ---------------------------------------------------------------- stage 1: UV
UV_ANGLE = 50                       # smart-project limit; at 55 the curled finger sides fold onto each other in UV

def stage_uv():
    R, L = objs()
    for o in (R, L):                                              # glove_tex's unwrap expects a "Palette" layer to keep
        if "Palette" not in o.data.uv_layers: o.data.uv_layers.new(name="Palette")
    unwrap(R, UV_ANGLE); transfer_mirror(R, L)
    for o in (R, L): o.data.uv_layers.remove(o.data.uv_layers["Palette"])
    n = uv_overlap_px(mesh_arrays(R))
    assert n == 0, f"UV islands overlap ({n} px at 1024): lower UV_ANGLE"

# ---------------------------------------------------------------- stage 2: raster + cuff coordinates
def stage_raster():
    gt.stage_raster("MoleGlove_R")
    o = objs()[0]; A = S["A"]
    pr = np.zeros(len(o.data.vertices), np.float32); o.data.attributes["prof"].data.foreach_get("value", pr)
    S["PR"] = (pr[A["LV"][A["TL"][S["t"]]]] * S["W"]).sum(1)      # mm along the cuff profile
    cx, _, cz = o["axis_mm"]; P = S["P"] * 1000
    S["TH"] = np.arctan2(P[:, 2] - cz, P[:, 0] - cx)               # angle around the cuff axis, 0 = little-finger side
    S["RAD"] = np.hypot(P[:, 2] - cz, P[:, 0] - cx)

# ---------------------------------------------------------------- stage 3: materials
def stage_material():
    P = S["P"] * 1000; N = S["N"]; Z = S["Z"]; PR = S["PR"]; TH = S["TH"]; RAD = S["RAD"]
    rings = dict(objs()[0]["ring_prof"])
    K = len(P)
    col = np.zeros((K, 3), np.float32); h = np.zeros(K, np.float32); sm = np.zeros(K, np.float32)
    B = bones_mm()
    tone = fbm(P / 6.0, 3, 7)
    # ---------------- leather: grain, wear, finger seams, knuckle creases, palm patch, badge
    m = (Z == GLOVE) | (Z == CUFF)
    Pm, Nm, pr, th, rad, zm = P[m], N[m], PR[m], TH[m], RAD[m], Z[m]
    F1, F2 = voronoi(Pm / 0.6, 11)
    grain = np.clip((F2 - F1) / 0.35, 0, 1) ** 0.6
    wr = fbm(Pm / 2.5, 3, 21)
    blot = fbm(Pm / 11.0, 3, 17)                                    # hide-to-hide colour drift
    c = C_LEA * (0.9 + 0.12 * grain[:, None]) * (0.92 + 0.16 * tone[m][:, None]) * (0.86 + 0.2 * blot[:, None])
    hh = 0.12 * grain + 0.08 * wr; s = 0.38 + 0.08 * grain
    def near(names, r):
        d = np.full(len(Pm), 1e9, np.float32)
        for n in names: d = np.minimum(d, np.linalg.norm(Pm - B[n], axis=1))
        return sstep(r, r * 0.3, d)
    hot = np.maximum(near([f"XRHand_{f}Tip" for f in FINGERS + ("Thumb",)], 10.0),
                     near([f"XRHand_{f}Proximal" for f in FINGERS], 9.0) * sstep(0.2, 0.6, Nm[:, 2]))
    worn = sstep(0.45, 0.8, 0.5 * fbm(Pm / 3.0, 3, 31) + 0.5 * hot)
    c = c * (1 - 0.6 * worn[:, None]) + C_LEA_WORN * (0.95 + 0.05 * grain[:, None]) * 0.6 * worn[:, None]; s += 0.08 * worn
    # creases across the back of the finger joints
    for f in FINGERS:
        for a, nxt in (("Intermediate", "Distal"), ("Distal", "Tip")):
            J = B[f"XRHand_{f}{a}"]; fd = B[f"XRHand_{f}{nxt}"] - J; fd /= np.linalg.norm(fd)
            rel = Pm - J; tt = rel @ fd; lat = np.linalg.norm(rel - tt[:, None] * fd, axis=1)
            env = sstep(3.4, 1.6, np.abs(tt)) * sstep(10.0, 7.0, lat) * sstep(0.15, 0.55, Nm[:, 2])
            wav = 0.5 + 0.5 * np.cos(2 * np.pi * (tt + 0.04 * lat * lat) / 1.7)
            cr = env * (1 - wav) ** 3
            c *= (1 - 0.28 * cr)[:, None]; hh -= 0.3 * cr
    # side seams (fourchettes) along each finger: where the surface turns from back to palm
    for f in FINGERS:
        J = B[f"XRHand_{f}Proximal"]; fd = B[f"XRHand_{f}Tip"] - J; L = np.linalg.norm(fd); fd /= L
        xa = np.cross(fd, [0, 0, 1.0]); xa /= np.linalg.norm(xa)
        rel = Pm - J; tt = rel @ fd; lat = np.linalg.norm(rel - tt[:, None] * fd, axis=1)
        reg = sstep(12.0, 17.0, tt) * sstep(12.0, 9.5, lat) * (zm == GLOVE)   # starts at the finger web, not on the hand
        across = Nm[:, 2] * 7.0                                      # ~mm on a 7 mm finger
        along = tt - np.abs(rel @ xa)
        groove = sstep(0.6, 0.0, np.abs(across)) * reg
        c *= (1 - 0.4 * groove)[:, None]; hh -= 0.35 * groove
        a_, p_, o_ = stitch(across - 1.3, along, w=0.45, L=2.3)
        a_, p_, o_ = a_ * reg, p_ * reg, o_ * reg
        c = c * (1 - a_[:, None]) + C_THREAD * (0.8 + 0.2 * p_[:, None]) * a_[:, None]; hh += 0.14 * p_; c *= (1 - 0.3 * o_)[:, None]
    # leather gathered above the strap
    warp = ring_arc(th, 9.0, float(rad.mean())) / 9.0 + 0.5 * fbm(Pm / 6.0, 2, 61)   # irregular, or it reads as knit ribbing
    pleat = sstep(1.0, 9.0, pr) * (zm == GLOVE) * (0.5 + 0.5 * np.cos(2 * np.pi * warp)) ** 2
    c *= (1 - 0.06 * pleat)[:, None]; hh += 0.3 * pleat
    # reinforced palm patch: a second leather layer with a stitched border
    wy = B["XRHand_Wrist"][1]; ky = np.mean([B[f"XRHand_{f}Proximal"][1] for f in FINGERS])
    py0, py1 = wy + 6.0, ky - 7.0
    palm = (Nm[:, 2] < -0.5) & (Pm[:, 1] > py0) & (Pm[:, 1] < py1)
    px0, px1 = np.percentile(Pm[palm, 0], 3), np.percentile(Pm[palm, 0], 97)
    face = sstep(-0.2, -0.55, Nm[:, 2])                             # soft fade: a hard normal cut leaves a ragged edge at the thumb
    sdP, alP = rrect(Pm[:, 0] - (px0 + px1) * 0.5, Pm[:, 1] - (py0 + py1) * 0.5, (px1 - px0) * 0.5 - 6.0, (py1 - py0) * 0.5, 9.0)
    ins = (1 - sstep(-0.3, 0.3, sdP)) * face
    G1, G2 = voronoi(Pm / 0.85, 12); g2 = np.clip((G2 - G1) / 0.35, 0, 1) ** 0.6
    c = c * (1 - ins[:, None]) + C_PATCH * (0.9 + 0.1 * g2[:, None]) * (0.92 + 0.16 * tone[m][:, None]) * ins[:, None]
    hh += 0.4 * sstep(0.3, -0.9, sdP) * face; s = s * (1 - ins) + (0.28 + 0.05 * g2) * ins
    c *= (1 - 0.3 * sstep(1.6, 0.0, sdP) * (sdP > 0) * face)[:, None]   # the patch's edge shadow
    a_, p_, o_ = stitch(sdP + 1.6, alP, w=0.5, L=2.6)
    a_, p_, o_ = a_ * face, p_ * face, o_ * face
    c = c * (1 - a_[:, None]) + C_THREAD * (0.8 + 0.2 * p_[:, None]) * a_[:, None]; hh += 0.15 * p_; c *= (1 - 0.35 * o_)[:, None]
    # round badge on the back of the hand: mallet over a dirt mound
    hc = (1 - BADGE_T) * B["XRHand_MiddleMetacarpal"] + BADGE_T * B["XRHand_MiddleProximal"]
    u, v = Pm[:, 0] - hc[0], Pm[:, 1] - hc[1]; rr = np.hypot(u, v)
    top = sstep(0.35, 0.55, Nm[:, 2])
    inB = (1 - sstep(BADGE_R - 0.3, BADGE_R + 0.3, rr)) * top
    cB = C_BADGE * (0.93 + 0.07 * g2[:, None]) * (0.95 + 0.1 * tone[m][:, None]); hB = 0.45 * sstep(BADGE_R + 0.2, BADGE_R - 1.2, rr) + 0.04 * g2
    mound, handle, head, band = badge_icon(u, v)
    for sd, cc, lift in ((mound, C_DIRT, 0.10), (handle, C_WOOD, 0.14), (head, C_WOOD, 0.18)):
        on = 1 - sstep(-0.15, 0.15, sd)
        line = sstep(0.6, 0.3, np.abs(sd + 0.15))                   # dark outline, inked like a stamped badge
        cB = cB * (1 - on[:, None]) + cc * (0.9 + 0.1 * g2[:, None]) * on[:, None]
        cB = cB * (1 - line[:, None]) + C_ICON_LINE * line[:, None]; hB += lift * on
    bon = 1 - sstep(-0.12, 0.12, band); cB = cB * (1 - bon[:, None]) + C_WOOD_DK * bon[:, None]
    a_, p_, o_ = stitch(rr - (BADGE_R - 1.5), np.arctan2(v, u) * (BADGE_R - 1.5), w=0.45, L=2.2)
    cB = cB * (1 - a_[:, None]) + C_THREAD * 0.9 * (0.8 + 0.2 * p_[:, None]) * a_[:, None]; hB += 0.14 * p_; cB *= (1 - 0.35 * o_)[:, None]
    c = c * (1 - inB[:, None]) + cB * inB[:, None]; hh = hh * (1 - inB) + (hh * 0.3 + hB) * inB; s = s * (1 - inB) + 0.26 * inB
    c *= (1 - 0.3 * sstep(BADGE_R + 1.6, BADGE_R, rr) * (rr > BADGE_R) * top)[:, None]
    # hem below the strap
    hem = zm == CUFF
    a_, p_, o_ = stitch(pr - (rings["rim"] - 2.4), ring_arc(th, 2.4, float(rad.mean())), w=0.45, L=2.4)
    a_, p_, o_ = a_ * hem, p_ * hem, o_ * hem
    c = c * (1 - a_[:, None]) + C_THREAD * (0.8 + 0.2 * p_[:, None]) * a_[:, None]; hh += 0.14 * p_; c *= (1 - 0.3 * o_)[:, None]
    c *= (1 - 0.18 * sstep(rings["rim"], rings["lip"], pr) * hem)[:, None]   # rolled edge
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- strap: ribbed elastic webbing + a stitched velcro tab on the back of the wrist
    m = Z == STRAP
    Pm, pr, th, rad = P[m], PR[m], TH[m], RAD[m]
    rref = float(rad.mean())
    face = sstep(rings["s_top"] - 0.3, rings["s_top"] + 0.3, pr) * sstep(rings["s_bot"] + 0.3, rings["s_bot"] - 0.3, pr)
    rib = 0.5 + 0.5 * np.cos(2 * np.pi * (pr - rings["s_top"]) / 1.1)
    cross = 0.5 + 0.5 * np.cos(2 * np.pi * ring_arc(th, 0.6, rref) / 0.6)
    c = C_BAND * (0.86 + 0.10 * rib[:, None] + 0.04 * cross[:, None]) * (0.96 + 0.06 * tone[m][:, None])
    c *= (0.82 + 0.18 * face)[:, None]
    hh = (0.14 * rib + 0.03 * cross) * face; s = np.full(m.sum(), 0.14, np.float32)
    for row in (rings["s_top"] + 1.6, rings["s_bot"] - 1.6):
        a_, p_, o_ = stitch(pr - row, ring_arc(th, 2.4, rref), w=0.45, L=2.4)
        c = c * (1 - a_[:, None]) + C_BAND_THREAD * (0.85 + 0.15 * p_[:, None]) * a_[:, None]; hh += 0.12 * p_; c *= (1 - 0.3 * o_)[:, None]
    du = np.angle(np.exp(1j * (th - TAB_ANGLE))) * rad
    mid = 0.5 * (rings["s_top"] + rings["s_bot"]); hv = 0.5 * (rings["s_bot"] - rings["s_top"]) - 1.4
    sdT, alT = rrect(du, pr - mid, TAB_HW, hv, 3.0)
    sdT = np.where(Pm[:, 2] > -5.0, sdT, 50.0)                      # back half only: the angle wraps on the palm side
    inT = 1 - sstep(-0.3, 0.3, sdT)
    fine = fbm(Pm / 0.4, 2, 41)
    c = c * (1 - inT[:, None]) + C_BAND_TAB * (0.95 + 0.05 * fine[:, None]) * inT[:, None]
    hh = hh * (1 - inT) + (0.5 * sstep(0.3, -1.0, sdT) + 0.02 * fine) * inT
    c *= (1 - 0.35 * sstep(1.8, 0.0, sdT) * (sdT > 0))[:, None]
    a_, p_, o_ = stitch(sdT + 1.4, alT, w=0.45, L=2.2)
    c = c * (1 - a_[:, None]) + C_BAND_THREAD * (0.85 + 0.15 * p_[:, None]) * a_[:, None]; hh += 0.12 * p_; c *= (1 - 0.3 * o_)[:, None]
    s = s * (1 - inT) + 0.2 * inT
    col[m], h[m], sm[m] = c, hh, s
    # ---------------- glove interior: suede
    m = Z == INNER
    col[m] = C_INNER * (0.85 + 0.2 * fbm(P[m] / 0.8, 2, 51)[:, None]); h[m] = 0.0; sm[m] = 0.05
    # ---------------- ambient occlusion
    col *= (0.5 + 0.5 * S["AO"])[:, None]
    S.update(col=np.clip(col, 0, 1), h=h, sm=np.clip(sm, 0, 1))
    print("material done; zones", {int(z): int((Z == z).sum()) for z in np.unique(Z)})

# ---------------------------------------------------------------- Blender materials + preview
def stage_material_setup():
    mat = bpy.data.materials.get("M_MoleGlove") or bpy.data.materials.new("M_MoleGlove")
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled"); bs.location = (300, 0)
    ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = bpy.data.images["T_MoleGlove_Albedo"]; ta.location = (-400, 200)
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = bpy.data.images["T_MoleGlove_Normal"]; tn.location = (-400, -250)
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
    setup_band_material(BAND_PREVIEW_TINT)

def setup_band_material(tint):
    """Slot 1 = strap: same textures, albedo multiplied by the tint (sRGB); in game LocalGloves sets the team colour."""
    base = bpy.data.materials["M_MoleGlove"]
    old = bpy.data.materials.get("M_MoleGlove_Band")
    if old: bpy.data.materials.remove(old)
    band = base.copy(); band.name = "M_MoleGlove_Band"
    nt = band.node_tree
    bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    ta = next(n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name.endswith("Albedo"))
    mul = nt.nodes.new("ShaderNodeMix"); mul.name = "TeamTint"; mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
    mul.inputs[0].default_value = 1.0; mul.location = (0, 250)
    mul.inputs[7].default_value = (*[c ** 2.2 for c in tint], 1.0)
    nt.links.new(ta.outputs["Color"], mul.inputs[6]); nt.links.new(mul.outputs[2], bs.inputs["Base Color"])
    for o in objs():
        me = o.data
        me.materials.clear(); me.materials.append(base); me.materials.append(band)
        z = np.zeros(len(me.polygons), np.int32); me.attributes["zone"].data.foreach_get("value", z)
        me.polygons.foreach_set("material_index", (z == STRAP).astype(np.int32)); me.update()

def preview_scene():
    sc = bpy.data.scenes.get("MolePreview")
    if sc: return sc
    sc = bpy.data.scenes.new("MolePreview")
    sc.collection.children.link(bpy.data.collections["MoleGlove"])
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try: sc.render.engine = eng; break
        except TypeError: pass
    sc.render.resolution_x, sc.render.resolution_y = 1400, 900
    sc.view_settings.view_transform = 'AgX'
    w = bpy.data.worlds.new("MolePreviewWorld"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.42, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sc.world = w
    cam = bpy.data.objects.new("MolePreviewCam", bpy.data.cameras.new("MolePreviewCam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 85; cam.data.clip_start = 0.01
    for name, rot, e in (("Key", (0.7, 0.2, 0.6), 3.0), ("Fill", (1.2, 0.0, -2.4), 1.0), ("Rim", (-1.0, 0.0, 3.0), 1.6)):
        L = bpy.data.objects.new("MolePreview" + name, bpy.data.lights.new("MolePreview" + name, 'SUN')); L.data.energy = e
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
    R, L = objs()
    gt.export_dump("R", R, S["A"], name="MoleGlove", sub_zone=STRAP, export=EXPORT)
    gt.export_dump("L", L, mesh_arrays(L), name="MoleGlove", sub_zone=STRAP, export=EXPORT)

def build(unwrap_uv=False):
    """Textures into Assets + mesh dumps into export/. UVs are laid out on the first run after mole_builder.py."""
    object_mode()
    if unwrap_uv or "UVMap" not in objs()[0].data.uv_layers: stage_uv()
    stage_raster(); stage_material(); gt.stage_images("T_MoleGlove", OUT)
    stage_material_setup(); stage_export()

if __name__ == "__main__":
    build()
