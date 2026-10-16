# Shared helpers for the Japan props kit: palette + decal atlas + tiling materials, part builders,
# bake + FBX export. Run from Blender (MCP or Text Editor); every model script imports this module.
import importlib, os, math, sys
import bpy, bmesh
from mathutils import Matrix

# __file__ is missing when this module is run as a Text Editor block, hence the literal fallback
SCRIPTS = os.path.dirname(os.path.abspath(__file__)) if "__file__" in dir() else \
    r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:   # jp_textures is imported by path, not as part of a package
    sys.path.insert(0, SCRIPTS)

REPO = r"C:\UnityProjects\VortexArena"
KIT = os.path.join(REPO, "Assets", "Maps", "Japan", "Props")
PALETTE_PNG = os.path.join(KIT, "Textures", "JP_Palette.png")

# One column per color; order is the UV contract, append only (exported UVs point at a column index).
PALETTE = [
    ("crate_red", "#C8352B"), ("crate_yellow", "#E9BF2C"), ("crate_orange", "#E0782A"),
    ("crate_inner", "#6E2A20"), ("keg_steel", "#868E97"), ("keg_steel_dark", "#59616A"),
    ("keg_black", "#2B2D30"), ("label_white", "#F4EFE4"), ("label_red", "#B8281F"),
    # C1 gachapon
    ("gacha_blue", "#2F6FD0"), ("gacha_red", "#D8322A"), ("gacha_yellow", "#E9BF2C"),
    ("gacha_white", "#F2F2EE"), ("gacha_trim", "#3A3D42"),
    # E1 sake barrels / G1 boxes
    ("rope", "#C9B07A"), ("tape", "#C8A46A"),
    # J1 fence panel
    ("panel_white", "#EEF0F2"), ("panel_blue", "#2F6FA8"), ("rubber", "#2B2D30"),
    ("hazard_yellow", "#F2C230"),
    # B1 yatai
    ("wood_dark", "#6B4220"), ("tin_dark", "#5A6068"), ("glass_green", "#3E6B45"),
    # X1 coin lockers / X3 rice sacks
    ("locker_beige", "#CFC6AE"), ("locker_frame", "#8D8A80"), ("metal_grey", "#8A9096"),
    ("metal_dark", "#4E5359"), ("concrete", "#A8A49C"), ("sack_white", "#EDE8DC"),
    # Z1 food vending
    ("vend_red", "#C63B2F"), ("vend_cream", "#F1E6CF"), ("chrome", "#C9CDD2"),
]
# Columns past PALETTE stay neutral grey: a new color takes the next free column, so existing
# UVs keep pointing at the same color. COLS is the texture width in columns: raising it moves
# every exported UV, so every model script must be re-run and re-exported in the same pass.
COLS, COL_W, TEX_H = 32, 16, 64
assert len(PALETTE) <= COLS
V_MIN, V_MAX = 0.08, 0.92  # keep away from texture edges
UNUSED_COL = "#808080"


def _hex(h):
    h = h.lstrip("#")
    return [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]


def build_palette():
    """Writes the gradient palette PNG (top of each column lighter, bottom darker)."""
    w = COLS * COL_W
    img = bpy.data.images.get("JP_Palette")
    # a size change recreates the image: scale() would be undone by the colorspace reload from the old PNG
    if img is not None and (img.size[0] != w or img.size[1] != TEX_H):
        bpy.data.images.remove(img)
        img = None
    img = img or bpy.data.images.new("JP_Palette", w, TEX_H, alpha=False)
    # set before writing pixels: changing it afterwards regenerates the buffer to black
    img.colorspace_settings.name = "sRGB"
    px = [0.0] * (w * TEX_H * 4)
    for ci in range(COLS):
        base = _hex(PALETTE[ci][1]) if ci < len(PALETTE) else _hex(UNUSED_COL)
        for y in range(TEX_H):
            # bottom darker, true base color at 60% height, light lift at the top
            t = y / (TEX_H - 1)
            dark = [c * 0.76 for c in base]
            light = [c + (1 - c) * 0.14 for c in base]
            if t < 0.6:
                k = t / 0.6
                col = [d + (c - d) * k for d, c in zip(dark, base)]
            else:
                k = (t - 0.6) / 0.4
                col = [c + (l - c) * k for c, l in zip(base, light)]
            for x in range(ci * COL_W, (ci + 1) * COL_W):
                i = (y * w + x) * 4
                px[i:i + 4] = col + [1.0]
    img.pixels = px
    img.update()
    os.makedirs(os.path.dirname(PALETTE_PNG), exist_ok=True)
    img.filepath_raw = PALETTE_PNG
    img.file_format = "PNG"
    img.save()
    return img


def palette_material():
    m = bpy.data.materials.get("M_JP_Palette") or bpy.data.materials.new("M_JP_Palette")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.65
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("JP_Palette") or build_palette()
    tex.interpolation = "Linear"
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def col_u(key):
    idx = next(i for i, (k, _) in enumerate(PALETTE) if k == key)
    return (idx + 0.5) / COLS


# --- Decal atlas: flat signs, labels, noren and barrel wraps ------------------------------------
REF_DECALS = os.path.join(REPO, "blender", "JapanProps", "ref", "decals")
DECALS_PNG = os.path.join(KIT, "Textures", "JP_Decals.png")
DEC_SIZE, DEC_UNITS = 2048, 8
DEC_UNIT = DEC_SIZE // DEC_UNITS      # 256 px per grid unit
DEC_PAD = 6 / DEC_SIZE                # UV inset inside a slot so mips do not bleed into neighbours
FULL = (0.0, 0.0, 1.0, 1.0)

# (key, file under ref/decals, crop (x0, y0, x1, y1) as fractions from the source's TOP-left,
# slot (col, row, w, h) in grid units with row 0 at the TOP of the atlas).
# Append only: the slot rects are the UV contract, a moved slot silently retextures exported models.
DECALS = [
    ("noren_oden", "noren_oden.png", (0.0, 0.10, 1.0, 1.0), (0, 0, 4, 1)),  # top band = hanging loops over grey
    ("noren_ramen", "noren_ramen.png", (0.0, 0.10, 1.0, 1.0), (4, 0, 4, 1)),
    ("lantern_oden", "lantern_oden.png", FULL, (0, 1, 2, 1)),
    ("lantern_matsuri", "lantern_matsuri.png", FULL, (2, 1, 2, 1)),
    ("sign_kouji", "sign_kouji.png", FULL, (4, 1, 4, 2)),
    # one 2 x 2 sheet of shipping labels, quadrants TL, TR, BL, BR
    ("label_ship", "labels.png", (0.100, 0.046, 0.428, 0.481), (0, 2, 1, 1)),
    ("label_fragile", "labels.png", (0.561, 0.068, 0.938, 0.477), (1, 2, 1, 1)),
    ("label_updown", "labels.png", (0.056, 0.539, 0.472, 0.955), (2, 2, 1, 1)),
    ("label_brand", "labels.png", (0.544, 0.542, 0.955, 0.955), (3, 2, 1, 1)),
    # barrel side is ~3:1 (circumference : height): the 3:1 middle of the ~4.9:1 strip keeps glyphs square
    ("komo_a", "komodaru_a.png", (0.193, 0.0, 0.807, 1.0), (0, 3, 4, 1)),
    ("komo_b", "komodaru_b.png", (0.193, 0.0, 0.807, 1.0), (4, 3, 4, 1)),
    ("komo_c", "komodaru_c.png", (0.193, 0.0, 0.807, 1.0), (0, 4, 4, 1)),
    ("menu_board", "menu_board.png", FULL, (0, 6, 2, 2)),  # art is ~1.2:1, needs a square slot
    ("komo_lid", "komo_lid.png", FULL, (6, 4, 1, 1)),
    ("gacha_p1", "gacha_panels.png", (0.064, 0.026, 0.453, 0.477), (0, 5, 1, 1)),
    ("gacha_p2", "gacha_panels.png", (0.551, 0.026, 0.943, 0.477), (1, 5, 1, 1)),
    ("gacha_p3", "gacha_panels.png", (0.064, 0.522, 0.453, 0.975), (2, 5, 1, 1)),
    ("gacha_p4", "gacha_panels.png", (0.551, 0.522, 0.943, 0.975), (3, 5, 1, 1)),
    ("gacha_w1", "gacha_windows.png", (0.026, 0.032, 0.475, 0.966), (4, 5, 1, 1)),
    ("gacha_w2", "gacha_windows.png", (0.526, 0.032, 0.974, 0.966), (5, 5, 1, 1)),
    # X1 lockers: the doors, numbers and keyholes are painted into one front sheet
    ("locker_front", "locker_front.png", FULL, (2, 6, 2, 2)),
    # one 2 x 2 sheet of small signs, quadrants TL, TR, BL, BR
    ("locker_sign", "labels2.png", (0.039, 0.036, 0.463, 0.461), (7, 4, 1, 1)),
    ("rice_print", "labels2.png", (0.530, 0.104, 0.982, 0.389), (6, 5, 2, 1)),
    ("warn_hv", "labels2.png", (0.040, 0.540, 0.462, 0.958), (4, 6, 1, 1)),
    ("poster_a", "labels2.png", (0.598, 0.524, 0.922, 0.988), (5, 6, 1, 2)),
    # Z1 vending: buttons, header text and food photos are painted into one front sheet
    # header band cropped off: the model's own header box sits there; leaves ~1:1.75 = the front panel
    ("vend_front", "vend_front.png", (0.0, 0.135, 1.0, 1.0), (6, 6, 1, 2)),
]


def _dec_check():
    """Slots must stay inside the atlas and never overlap: an overlap is invisible in the UVs."""
    used = {}
    for key, _, _, (col, row, w, h) in DECALS:
        assert col + w <= DEC_UNITS and row + h <= DEC_UNITS, f"decal slot out of atlas: {key}"
        for r in range(row, row + h):
            for c in range(col, col + w):
                assert (c, r) not in used, f"decal slot overlap: {key} / {used[(c, r)]}"
                used[(c, r)] = key


_dec_check()


def build_decals(path=DECALS_PNG):
    """Packs every DECALS entry into its atlas slot (another path = a test build).
    A missing source file becomes a magenta slot: models are built before the art exists."""
    import numpy as np
    atlas = np.ones((DEC_SIZE, DEC_SIZE, 4), dtype=np.float32)   # row 0 = bottom (Blender order)
    for key, fname, (fx0, fy0, fx1, fy1), (col, row, w, h) in DECALS:
        sw, sh = w * DEC_UNIT, h * DEC_UNIT
        ys, xs = DEC_SIZE - (row + h) * DEC_UNIT, col * DEC_UNIT
        src_path = os.path.join(REF_DECALS, fname)
        if not os.path.exists(src_path):
            print(f"DECAL MISSING {key} ({fname})")
            atlas[ys:ys + sh, xs:xs + sw] = (1.0, 0.0, 1.0, 1.0)
            continue
        src = bpy.data.images.load(src_path, check_existing=False)
        iw, ih = src.size
        px = np.array(src.pixels[:], dtype=np.float32).reshape(ih, iw, 4)[::-1]   # top row first
        x0, y0, x1, y1 = round(fx0 * iw), round(fy0 * ih), round(fx1 * iw), round(fy1 * ih)
        crop = px[y0:y1, x0:x1].copy()
        bpy.data.images.remove(src)
        tmp = bpy.data.images.new("_jp_decal_tmp", crop.shape[1], crop.shape[0], alpha=True)
        tmp.colorspace_settings.name = "sRGB"
        tmp.pixels = crop[::-1].ravel()
        tmp.scale(sw, sh)
        slot = np.array(tmp.pixels[:], dtype=np.float32).reshape(sh, sw, 4)   # bottom row first
        bpy.data.images.remove(tmp)
        atlas[ys:ys + sh, xs:xs + sw] = slot
    # recreate every time: a file-backed image reloads its old size when settings change
    name = "JP_Decals" if path == DECALS_PNG else "_jp_decals_test"
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, DEC_SIZE, DEC_SIZE, alpha=False)
    img.colorspace_settings.name = "sRGB"
    img.pixels = atlas.ravel()
    img.update()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    if path != DECALS_PNG:
        return img
    m = bpy.data.materials.get("M_JP_Decals")   # rebind: the removed image left the node empty
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE":
                n.image = img
    return img


def decals_material():
    m = bpy.data.materials.get("M_JP_Decals") or bpy.data.materials.new("M_JP_Decals")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.7
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("JP_Decals") or build_decals()
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def decal_uv_rect(key):
    """Padded UV rect of a slot: (u0, v0, u1, v1), v0 at the bottom."""
    col, row, w, h = next(d[3] for d in DECALS if d[0] == key)
    s = DEC_UNIT / DEC_SIZE
    return (col * s + DEC_PAD, 1 - (row + h) * s + DEC_PAD,
            (col + w) * s - DEC_PAD, 1 - row * s - DEC_PAD)


# --- Tiling textures: albedo + normal, box-projected in meters ----------------------------------
REF_TEX = os.path.join(REPO, "blender", "JapanProps", "ref", "tex")
# name: (source file under ref/tex, meters per repeat, normal strength)
TILES = {
    "Wood": ("wood_planks.png", 0.6, 2.0),
    "Cardboard": ("cardboard.png", 0.5, 1.0),
    "RoofTin": ("roof_tin.png", 0.8, 3.0),
}


def tile_png(name, suffix):
    return os.path.join(KIT, "Textures", f"T_JP_{name}_{suffix}.png")


def tile_image(name, suffix):
    """Albedo ("A") or normal ("N") map of a tile; generated on first use if the PNG is missing."""
    im_name = f"T_JP_{name}_{suffix}"
    img = bpy.data.images.get(im_name)
    if img is None:
        path = tile_png(name, suffix)
        if not os.path.exists(path):
            import jp_textures
            importlib.reload(jp_textures)
            jp_textures.build_tiles([name])
            img = bpy.data.images.get(im_name)
        else:
            img = bpy.data.images.load(path, check_existing=True)
            img.name = im_name
    if img is not None and suffix == "N":
        img.colorspace_settings.name = "Non-Color"
    return img


def tile_material(name):
    """M_JP_<name>: Principled with the tile albedo and its normal map (strength baked into the map)."""
    mat_name = "M_JP_" + name
    m = bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name)
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.8
    # nodes are found by the labels set here: node names are localized, labels are not
    alb = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.label == "ALB"), None)
    if alb is None:
        alb = nt.nodes.new("ShaderNodeTexImage")
        alb.label, alb.location = "ALB", (-620, 220)
    alb.image = tile_image(name, "A")
    nt.links.new(alb.outputs["Color"], p.inputs["Base Color"])
    nrm = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.label == "NRM"), None)
    if nrm is None:
        nrm = nt.nodes.new("ShaderNodeTexImage")
        nrm.label, nrm.location = "NRM", (-620, -140)
    nrm.image = tile_image(name, "N")
    nmap = next((n for n in nt.nodes if n.type == "NORMAL_MAP"), None) or nt.nodes.new("ShaderNodeNormalMap")
    nmap.location = (-300, -140)
    nmap.inputs["Strength"].default_value = 1.0
    nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], p.inputs["Normal"])
    return m


# Same-facing faces of two parts closer than this flicker on Quest (near plane 0.05 m); layered
# parts (rail on panel, label on wall) stand at least this far off the surface they sit on.
ZF_GAP = 0.002


def zfight_pairs(meshes, gap=ZF_GAP, min_area=1e-5):
    """meshes: [(part name, world-space Mesh)]. Returns [(a, b, overlap m2)] for same-facing faces
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


class Builder:
    """Collects parts of one model under a source collection; bake() joins them into one export mesh."""

    def __init__(self, name):
        # the Blender session is shared: building into another project's open file pollutes it
        if os.path.basename(bpy.data.filepath) != "JapanProps.blend":
            raise RuntimeError("JapanProps.blend acik degil (acik dosya: %r) — once onu ac" % bpy.data.filepath)
        self.name = name
        self.src = self._collection(name + "_src")
        # a viewport-hidden collection is not evaluated, so its modifiers would be skipped on bake
        self.src.hide_viewport = False
        for o in list(self.src.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        self.parts = []

    @staticmethod
    def _collection(name):
        c = bpy.data.collections.get(name) or bpy.data.collections.new(name)
        if c.name not in bpy.context.scene.collection.children:
            bpy.context.scene.collection.children.link(c)
        return c

    @staticmethod
    def _kind(tile):
        if tile is None:
            return "palette"
        assert tile in TILES, f"unknown tile: {tile}"
        return "tile:" + tile

    def _finish(self, name, bm, color, bevel, seg, kind="palette"):
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(name, me)
        self.src.objects.link(o)
        for p in me.polygons:
            p.use_smooth = True
        if bevel > 0:
            b = o.modifiers.new("Bevel", "BEVEL")
            b.width, b.segments, b.limit_method, b.harden_normals = bevel, seg, "ANGLE", True
        o["jp_color"] = color
        o["jp_kind"] = kind
        self.parts.append(o)
        return o

    def box(self, name, size, center_xy, z0, color, bevel=0.02, seg=2, rot_y=0.0, rot_x=0.0, yaw=0.0,
            tile=None):
        """Box around its center; rot_y turns it in the front XZ plane, rot_x tilts it back (DEGREES),
        yaw spins it around its own Z axis (RADIANS, matches transform_parts)."""
        sx, sy, sz = size
        bm = bmesh.new()
        m = (Matrix.Translation((center_xy[0], center_xy[1], z0 + sz / 2)) @ Matrix.Rotation(yaw, 4, "Z")
             @ Matrix.Rotation(math.radians(rot_y), 4, "Y") @ Matrix.Rotation(math.radians(-rot_x), 4, "X"))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m @ Matrix.Diagonal((sx, sy, sz, 1)))
        o = self._finish(name, bm, color, bevel, seg, self._kind(tile))
        if bevel <= 0:
            # no bevel = no hardened normals: smooth shading would bend every face toward its corners
            for p in o.data.polygons:
                p.use_smooth = False
        return o

    def cyl(self, name, r, depth, loc, axis, color, segs=16, bevel=0.0, seg=1, tile=None):
        rot = {"Y": Matrix.Rotation(math.radians(90), 4, "X"),
               "X": Matrix.Rotation(math.radians(90), 4, "Y")}.get(axis, Matrix.Identity(4))
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=depth,
                              matrix=Matrix.Translation(loc) @ rot)
        return self._finish(name, bm, color, bevel, seg, self._kind(tile))

    def prism(self, name, pts_xy, z0, z1, color, bevel=0.02, seg=2, tile=None):
        """Extrudes a closed XY polygon (counter-clockwise from above) from z0 to z1 (L shapes etc.)."""
        bm = bmesh.new()
        f = bm.faces.new([bm.verts.new((x, y, z0)) for x, y in pts_xy])
        r = bmesh.ops.extrude_face_region(bm, geom=[f])
        bmesh.ops.translate(bm, vec=(0, 0, z1 - z0),
                            verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, bevel, seg, self._kind(tile))

    def hull(self, name, points, color, bevel=0.02, seg=2, tile=None):
        """Convex hull of the given points (slanted / tapered solids, roof slabs)."""
        bm = bmesh.new()
        vs = [bm.verts.new(p) for p in points]
        bmesh.ops.convex_hull(bm, input=vs)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, bevel, seg, self._kind(tile))

    def decal(self, name, size_wh, center_xyz, key, yaw=0.0, tilt=0.0, two_sided=False, corner=0.0,
              segs=3, sub=None):
        """Flat quad textured from the decal atlas. The front face looks at -Y before the rotations:
        tilt turns it around X (RADIANS; -pi/2 lays it face up), then yaw around Z (+pi/2 faces +X),
        both about center_xyz. two_sided adds a back face 2 mm behind with mirrored U so text still
        reads from behind. sub=(u0, v0, u1, v1) takes a window of the slot (noren split into flaps)."""
        w, h = size_wh
        cx, cy, cz = center_xyz
        u0, v0, u1, v1 = decal_uv_rect(key)
        if sub:
            su0, sv0, su1, sv1 = sub
            du, dv = u1 - u0, v1 - v0
            u0, u1, v0, v1 = u0 + du * su0, u0 + du * su1, v0 + dv * sv0, v0 + dv * sv1
        r = corner * min(w, h)
        if r > 1e-6:
            pts = []
            for qx, qz, a0 in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
                ccx, ccz = cx + qx * (w / 2 - r), cz + qz * (h / 2 - r)
                for k in range(segs + 1):
                    a = math.radians(a0 + 90 * k / segs)
                    pts.append((ccx + r * math.cos(a), ccz + r * math.sin(a)))
        else:
            pts = [(cx + w / 2, cz + h / 2), (cx - w / 2, cz + h / 2),
                   (cx - w / 2, cz - h / 2), (cx + w / 2, cz - h / 2)]
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")

        def face(y, flip, mirror_u):
            order = pts[::-1] if flip else pts
            f = bm.faces.new([bm.verts.new((x, y, z)) for x, z in order])
            f.normal_update()
            if (f.normal.y > 0) != flip:
                f.normal_flip()
            for loop in f.loops:
                co = loop.vert.co
                fu = (co.x - (cx - w / 2)) / w
                fv = (co.z - (cz - h / 2)) / h
                loop[uvl].uv = (u0 + (u1 - u0) * (1 - fu if mirror_u else fu), v0 + (v1 - v0) * fv)

        face(cy, False, False)
        if two_sided:
            face(cy + 0.002, True, True)
        if tilt:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(cx, cy, cz), matrix=Matrix.Rotation(tilt, 3, "X"))
        if yaw:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(cx, cy, cz), matrix=Matrix.Rotation(yaw, 3, "Z"))
        o = self._finish(name, bm, "label_white", 0.0, 1, "decal")
        o["jp_decal"] = key
        return o

    def lathe(self, name, pts_rz, center_xy, color, segs=24, scale_y=1.0, wrap_key=None, tile=None, phase=0.0):
        """Revolves an (r, z) profile around a vertical axis; r == 0 ends close the solid.
        scale_y < 1 flattens it front-to-back. wrap_key wraps a decal slot around the body:
        u = angle / 2pi over the slot width, v = profile arc length over its height.
        phase (radians) turns the start angle: pi/2 puts the slot centre (u 0.5) on the -Y front."""
        cx, cy = center_xy
        bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap") if wrap_key else None
        if wrap_key:
            u0, v0, u1, v1 = decal_uv_rect(wrap_key)
            arc = [0.0]
            for (r0, z0), (r1, z1) in zip(pts_rz, pts_rz[1:]):
                arc.append(arc[-1] + math.hypot(r1 - r0, z1 - z0))
            total = max(arc[-1], 1e-6)
            vfr = [v0 + (v1 - v0) * (a / total) for a in arc]
        rings = []
        for r, z in pts_rz:
            if r <= 1e-6:
                rings.append([bm.verts.new((cx, cy, z))])
            else:
                rings.append([bm.verts.new((cx + r * math.cos(2 * math.pi * k / segs + phase),
                                            cy + scale_y * r * math.sin(2 * math.pi * k / segs + phase), z))
                              for k in range(segs)])

        def put(f, uvs):
            if uvl:
                for loop, uv in zip(f.loops, uvs):
                    loop[uvl].uv = uv

        for ri, (a, c) in enumerate(zip(rings, rings[1:])):
            if len(a) == 1 and len(c) == 1:
                continue
            va, vc = (vfr[ri], vfr[ri + 1]) if wrap_key else (0.0, 0.0)
            for k in range(segs):
                ua = u0 + (u1 - u0) * k / segs if wrap_key else 0.0
                ub = u0 + (u1 - u0) * (k + 1) / segs if wrap_key else 0.0
                if len(a) == 1:
                    put(bm.faces.new((a[0], c[k], c[(k + 1) % segs])),
                        [((ua + ub) / 2, va), (ua, vc), (ub, vc)])
                elif len(c) == 1:
                    put(bm.faces.new((a[k], a[(k + 1) % segs], c[0])),
                        [(ua, va), (ub, va), ((ua + ub) / 2, vc)])
                else:
                    put(bm.faces.new((a[k], a[(k + 1) % segs], c[(k + 1) % segs], c[k])),
                        [(ua, va), (ub, va), (ub, vc), (ua, vc)])
        for ring, v in ((rings[0], vfr[0] if wrap_key else 0.0), (rings[-1], vfr[-1] if wrap_key else 0.0)):
            if len(ring) > 1:
                order = list(reversed(ring)) if ring is rings[0] else list(ring)
                f = bm.faces.new(order)
                put(f, [(u0 + (u1 - u0) * (ring.index(vt) / segs), v) if wrap_key else (0.0, 0.0)
                        for vt in order])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        kind = "decal" if wrap_key else self._kind(tile)
        return self._finish(name, bm, color, 0.0, 1, kind)

    def hollow_box(self, name, size, center_xy, z0, inner_size, inner_center_xy, inner_z0, color,
                   bevel=0.02, seg=2, tile=None):
        """Box with an open-top cavity (boolean difference), bevelled after the cut so the rim is rounded."""
        o = self.box(name, size, center_xy, z0, color, 0, tile=tile)
        isx, isy, isz = inner_size
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(
            (inner_center_xy[0], inner_center_xy[1], inner_z0 + isz / 2)) @ Matrix.Diagonal((isx, isy, isz, 1)))
        me = bpy.data.meshes.new(name + "_cutter")
        bm.to_mesh(me)
        bm.free()
        cutter = bpy.data.objects.new(name + "_cutter", me)
        self.src.objects.link(cutter)
        cutter.display_type = "WIRE"
        cutter.hide_render = True
        m = o.modifiers.new("Cut", "BOOLEAN")
        m.operation, m.object, m.solver = "DIFFERENCE", cutter, "EXACT"
        if bevel > 0:
            b = o.modifiers.new("Bevel", "BEVEL")
            b.width, b.segments, b.limit_method, b.harden_normals = bevel, seg, "ANGLE", True
        return o

    def text(self, name, body, size, center, color, depth=0.01, font=r"C:\Windows\Fonts\ariblk.ttf", resolution=2):
        """Chunky 3D letters standing in the XZ plane, readable from -Y, centered at `center`.
        No bevel: a bevelled font multiplies triangles by ~10."""
        cu = bpy.data.curves.new(name + "_curve", "FONT")
        cu.body = body
        cu.size = size
        cu.extrude = depth
        cu.resolution_u = resolution
        cu.align_x, cu.align_y = "CENTER", "CENTER"
        if font and os.path.exists(font):
            cu.font = bpy.data.fonts.load(font, check_existing=True)
        tmp = bpy.data.objects.new(name + "_tmp", cu)
        self.src.objects.link(tmp)
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(tmp, do_unlink=True)
        bpy.data.curves.remove(cu)
        # curve text lies in XY facing +Z; stand it up so it faces -Y
        me.transform(Matrix.Translation(center) @ Matrix.Rotation(math.radians(90), 4, "X"))
        bm = bmesh.new()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        return self._finish(name, bm, color, 0.0, 1)

    def transform_parts(self, name_prefix, yaw=0.0, offset_xyz=(0.0, 0.0, 0.0), pivot_xy=(0.0, 0.0)):
        """Moves parts whose name starts with `name_prefix` (empty prefix = all): yaw RADIANS around
        the vertical axis at pivot_xy, then offset. Edits mesh data in place, so it must run before
        bake() and must not be called twice over the same parts unless the second move is intended."""
        ox, oy, oz = offset_xyz
        px, py = pivot_xy
        m = (Matrix.Translation((px + ox, py + oy, oz)) @ Matrix.Rotation(yaw, 4, "Z")
             @ Matrix.Translation((-px, -py, 0.0)))
        n = 0
        for o in self.parts:
            if o.name.startswith(name_prefix):
                o.data.transform(m)
                n += 1
        return n

    def bake(self):
        """Applies modifiers, writes the UVs of each material kind and joins into one object.
        Material slots end up in part order, so parts are sorted palette, decals, tiles by name."""
        out_col = self._collection(self.name)
        old = bpy.data.objects.get(self.name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        mat = palette_material()
        kinds = [o.get("jp_kind", "palette") for o in self.parts]
        tile_names = sorted({k.split(":", 1)[1] for k in kinds if k.startswith("tile:")})

        def order(o):
            k = o.get("jp_kind", "palette")
            if k == "palette":
                return 0
            if k == "decal":
                return 1
            return 2 + tile_names.index(k.split(":", 1)[1])

        parts = sorted(self.parts, key=order)
        baked = []
        for o in parts:
            kind = o.get("jp_kind", "palette")
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            me.transform(o.matrix_world)
            zs = [v.co.z for v in me.vertices]
            z0, z1 = min(zs), max(zs)
            span = max(z1 - z0, 1e-5)
            uv = me.uv_layers.new(name="UVMap") if not me.uv_layers else me.uv_layers[0]
            me.materials.clear()
            if kind == "decal":
                me.materials.append(decals_material())   # UVs were written by decal() / lathe()
            elif kind.startswith("tile:"):
                tname = kind.split(":", 1)[1]
                rep = TILES[tname][1]
                for poly in me.polygons:
                    n = poly.normal
                    ax = max(range(3), key=lambda i: abs(n[i]))   # box projection on the face normal
                    for li in poly.loop_indices:
                        co = me.vertices[me.loops[li].vertex_index].co
                        a, b = (co.x, co.y) if ax == 2 else ((co.y, co.z) if ax == 0 else (co.x, co.z))
                        uv.data[li].uv = (a / rep, b / rep)
                me.materials.append(tile_material(tname))
            else:
                u = col_u(o["jp_color"])
                flat = (z1 - z0) < 1e-3   # flat plates: no height to grade, use the true base color
                for loop in me.loops:
                    z = me.vertices[loop.vertex_index].co.z
                    t = 0.6 if flat else (z - z0) / span
                    uv.data[loop.index].uv = (u, V_MIN + (V_MAX - V_MIN) * t)
                me.materials.append(mat)
            ob = bpy.data.objects.new(o.name + "_baked", me)
            out_col.objects.link(ob)
            baked.append(ob)
        self.zfights = zfight_pairs([(p.name.split(".")[0], ob.data) for p, ob in zip(parts, baked)])
        for a, b, s in self.zfights:
            print(f"Z-FIGHT {self.name}: {a} / {b} ({s * 1e4:.1f} cm2)")
        with bpy.context.temp_override(active_object=baked[0], selected_editable_objects=baked, object=baked[0]):
            bpy.ops.object.join()
        res = baked[0]
        res.name = res.data.name = self.name
        self.src.hide_viewport = True
        self.src.hide_render = True
        res.data.calc_loop_triangles()
        print(f"SUBMESH ORDER: {[m.name for m in res.data.materials]}")
        return res, len(res.data.loop_triangles)


_TEXTURES_WRITTEN = False


def export_fbx(obj, rel_path):
    """Writes the FBX under KIT and refreshes the shared palette, decal and tile PNGs next to it."""
    global _TEXTURES_WRITTEN
    if not _TEXTURES_WRITTEN:    # once per script run (the module is reloaded by every script)
        build_palette()
        build_decals()
        import jp_textures      # imported here: jp_textures imports this module
        importlib.reload(jp_textures)
        jp_textures.build_tiles()
        _TEXTURES_WRITTEN = True
    path = os.path.join(KIT, rel_path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
        path_mode="STRIP", embed_textures=False)
    return path


def obstacle(name, center, size):
    """Prints one Unity-local obstacle box from Blender coords: Unity (x, y, z) = Blender (-x, z, -y),
    so the y and z sizes swap. The caller passes a box already inset inside the mesh (the bullet
    MeshCollider must be hit first), so this only converts axes."""
    cx, cy, cz = center
    sx, sy, sz = size
    print("OBSTACLE %.3f %.3f %.3f %.3f %.3f %.3f  # %s" % (-cx, cz, -cy, sx, sz, sy, name))


def obstacle_inset(name, bmin, bmax, inset=0.03):
    """obstacle() from a Blender min/max box pulled `inset` in on every side."""
    lo = [a + inset for a in bmin]
    hi = [b - inset for b in bmax]
    assert all(h > l for l, h in zip(lo, hi)), f"obstacle box collapsed: {name}"
    obstacle(name, [(l + h) / 2 for l, h in zip(lo, hi)], [h - l for l, h in zip(lo, hi)])
