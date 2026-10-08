# Shared helpers for the Burger kitchen kit: palette texture, part builders, bake + FBX export.
# Run from Blender (MCP or Text Editor); every model script imports this module.
import os, math
import bpy, bmesh
from mathutils import Matrix

REPO = r"C:\UnityProjects\VortexArena"
KIT = os.path.join(REPO, "Assets", "Modes", "Burger", "KitchenKit")
PALETTE_PNG = os.path.join(KIT, "Textures", "BK_Palette.png")

# One column per color; order is the UV contract, append only (existing UVs point at column index).
PALETTE = [
    ("red", "#D8342C"), ("red_dark", "#B82A24"), ("steel", "#B8C4CE"), ("charcoal", "#2B2B2B"),
    ("plate", "#232323"), ("rib", "#4A4A4A"), ("white", "#F5F0E6"), ("wood", "#A8662F"),
    ("yellow", "#F4BE2C"), ("teal", "#2FA49B"), ("cream", "#EFE6D2"), ("wood_dark", "#7A4521"),
    ("steel_dark", "#7F8E9B"), ("green", "#4FA84A"), ("green_dark", "#367A33"),
    ("purple", "#8A5CC2"),
]
# PALETTE is full at COLS entries: widening the texture moves every exported UV. A new color
# needs a free column, or COLS raised AND every model script re-run + re-exported together.
assert len(PALETTE) <= 16
COLS, COL_W, TEX_H = 16, 16, 64
V_MIN, V_MAX = 0.08, 0.92  # keep away from texture edges


def _hex(h):
    h = h.lstrip("#")
    return [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]


def build_palette():
    """Writes the gradient palette PNG (top of each column lighter, bottom darker)."""
    w = COLS * COL_W
    img = bpy.data.images.get("BK_Palette") or bpy.data.images.new("BK_Palette", w, TEX_H, alpha=False)
    if img.size[0] != w or img.size[1] != TEX_H:
        img.scale(w, TEX_H)
    # set before writing pixels: changing it afterwards regenerates the buffer to black
    img.colorspace_settings.name = "sRGB"
    px = [0.0] * (w * TEX_H * 4)
    for ci in range(COLS):
        base = _hex(PALETTE[ci][1]) if ci < len(PALETTE) else [1, 0, 1]
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
    m = bpy.data.materials.get("BK_Palette") or bpy.data.materials.new("BK_Palette")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.65
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("BK_Palette") or build_palette()
    tex.interpolation = "Linear"
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def col_u(key):
    idx = next(i for i, (k, _) in enumerate(PALETTE) if k == key)
    return (idx + 0.5) / COLS


# Decal atlas: flat signs/stickers. One slot per entry; order is the UV contract, append only.
# (key, file under ref/, crop box as fractions of the image: x0, y0, x1, y1 from the top-left)
# Fractions keep the crop valid when a ref image is resized; a crop must stay >= DEC_SLOT px.
REF = os.path.join(REPO, "blender", "BurgerKitchen", "ref")
DECALS_PNG = os.path.join(KIT, "Textures", "BK_Decals.png")


def _px(x0, y0, x1, y1, w, h):
    """Crop measured in pixels on a w x h image, as fractions."""
    return (x0 / w, y0 / h, x1 / w, y1 / h)


FULL = (0.0, 0.0, 1.0, 1.0)
DECALS = [
    ("recycle", "F5_recycle_icon.png", _px(103, 103, 1944, 1944, 2048, 2048)),
    # E4 ingredient cards: rows 1-2 of the sheet (row 3 repeats row 2), square crops on the card outline.
    # ⚠️ E4 stays 2048 px: its crops are ~420 px, already at the slot size.
    ("icon_patty", "E4_icons_sheet.png", _px(85, 338, 514, 767, 2048, 2048)),
    ("icon_bun", "E4_icons_sheet.png", _px(568, 338, 997, 767, 2048, 2048)),
    ("icon_cheese", "E4_icons_sheet.png", _px(1051, 337, 1480, 766, 2048, 2048)),
    ("icon_tomato", "E4_icons_sheet.png", _px(1534, 337, 1963, 766, 2048, 2048)),
    ("icon_lettuce", "E4_icons_sheet.png", _px(90, 817, 510, 1237, 2048, 2048)),
    ("icon_onion", "E4_icons_sheet.png", _px(573, 816, 993, 1236, 2048, 2048)),
    ("icon_pickle", "E4_icons_sheet.png", _px(1056, 817, 1476, 1237, 2048, 2048)),
    ("icon_bacon", "E4_icons_sheet.png", _px(1539, 816, 1959, 1236, 2048, 2048)),
    # G7 wall art, whole images (the slot is square; the decal's aspect restores the proportions)
    ("menu_board", "G7_menu_board.png", FULL),
    ("poster", "G7_poster.png", FULL),
    # I diner wall art
    ("diner_night", "I3_poster_diner_night.png", FULL),
    ("open_sign", "I4_poster_open_sign.png", FULL),
    ("fries", "I5_poster_fries.png", FULL),
    ("milkshake", "I6_poster_milkshake.png", FULL),
    ("chef_mascot", "I7_poster_chef_mascot.png", FULL),
    ("veggies", "I8_poster_veggies.png", FULL),
    ("grill", "I9_poster_grill.png", FULL),
    ("hotdog", "I10_poster_hotdog.png", FULL),
    ("cola", "I11_poster_cola.png", FULL),
]
# UVs depend on DEC_GRID: changing it means re-running every script that uses decals
# (f5_trash_bin, e1_dispenser, g_details) and exporting them together
DEC_GRID, DEC_SLOT = 5, 408
assert len(DECALS) <= DEC_GRID * DEC_GRID
DEC_PAD = 6 / DEC_SLOT  # UV inset inside a slot so mips do not bleed into the neighbour


def build_decals(path=DECALS_PNG):
    """Packs every DECALS entry into its slot of the atlas PNG (another path = a test build)."""
    import numpy as np
    size = DEC_GRID * DEC_SLOT
    atlas = np.ones((size, size, 4), dtype=np.float32)
    for i, (key, fname, (fx0, fy0, fx1, fy1)) in enumerate(DECALS):
        src = bpy.data.images.load(os.path.join(REF, fname), check_existing=False)
        w, h = src.size
        px = np.array(src.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]  # top row first
        x0, y0, x1, y1 = round(fx0 * w), round(fy0 * h), round(fx1 * w), round(fy1 * h)
        crop = px[y0:y1, x0:x1].copy()
        bpy.data.images.remove(src)
        tmp = bpy.data.images.new("_bk_decal_tmp", crop.shape[1], crop.shape[0], alpha=True)
        tmp.colorspace_settings.name = "sRGB"
        tmp.pixels = crop[::-1].ravel()
        tmp.scale(DEC_SLOT, DEC_SLOT)
        slot = np.array(tmp.pixels[:], dtype=np.float32).reshape(DEC_SLOT, DEC_SLOT, 4)  # bottom row first
        bpy.data.images.remove(tmp)
        col, row = i % DEC_GRID, i // DEC_GRID
        ys = size - (row + 1) * DEC_SLOT  # slot rows counted from the top of the atlas
        atlas[ys:ys + DEC_SLOT, col * DEC_SLOT:(col + 1) * DEC_SLOT] = slot
    # recreate every time: a file-backed image reloads its old size when settings change
    name = "BK_Decals" if path == DECALS_PNG else "_bk_decals_test"
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, size, size, alpha=False)
    img.colorspace_settings.name = "sRGB"
    img.pixels = atlas.ravel()
    img.update()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    if path != DECALS_PNG:
        return img
    m = bpy.data.materials.get("BK_Decals")   # rebind: the removed image left the node empty (black)
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE":
                n.image = img
    return img


def decals_material():
    m = bpy.data.materials.get("BK_Decals") or bpy.data.materials.new("BK_Decals")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.65
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("BK_Decals") or build_decals()
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


TILES_PNG = os.path.join(KIT, "Textures", "BK_WallTiles.png")
TILE_REPEAT = 0.60   # meters covered by one repeat of the tile texture (2 x 4 subway tiles of 0.30 x 0.15)


def build_wall_tiles():
    """Cartoon subway tiles: cream tiles with a lit top edge and shaded bottom edge, dark grout."""
    import numpy as np
    S, cols, rows, grout = 512, 2, 4, 7
    tw, th = S // cols, S // rows
    cream = np.array(_hex("#F2EDE3"), dtype=np.float32)
    grout_c = np.array(_hex("#6B6660"), dtype=np.float32)
    img = np.empty((S, S, 3), dtype=np.float32)
    img[:] = grout_c
    yy, xx = np.mgrid[0:th, 0:tw].astype(np.float32)
    inner = (xx >= grout / 2) & (xx < tw - grout / 2) & (yy >= grout / 2) & (yy < th - grout / 2)
    t = (yy - grout / 2) / (th - grout)                        # 0 at bottom, 1 at top of a tile
    shade = 0.90 + 0.10 * np.clip(t, 0, 1)                     # soft vertical gradient
    edge_top = (yy > th - grout / 2 - 6) & inner
    edge_bot = (yy < grout / 2 + 5) & inner
    tile = cream[None, None, :] * shade[..., None]
    tile[edge_top] = np.minimum(tile[edge_top] * 1.06, 1.0)
    tile[edge_bot] = tile[edge_bot] * 0.86
    for r in range(rows):
        off = (tw // 2) if r % 2 else 0                        # running bond
        for c in range(cols + 1):
            x0 = c * tw - off
            ys = r * th
            for dx in range(tw):
                x = x0 + dx
                if 0 <= x < S:
                    col_mask = inner[:, dx]
                    img[ys:ys + th, x][col_mask] = tile[:, dx][col_mask]
    px = np.concatenate([img, np.ones((S, S, 1), dtype=np.float32)], axis=2)  # row 0 = bottom
    im = bpy.data.images.get("BK_WallTiles") or bpy.data.images.new("BK_WallTiles", S, S, alpha=False)
    im.colorspace_settings.name = "sRGB"
    im.pixels = px.ravel()
    im.update()
    im.filepath_raw = TILES_PNG
    im.file_format = "PNG"
    im.save()
    return im


WOOD_PNG = os.path.join(KIT, "Textures", "BK_CabinetWood.png")
WOOD_REPEAT = 0.50   # meters per texture repeat on cabinet faces


def build_cabinet_wood():
    """Dark cartoon cabinet wood: vertical planks, wavy darker grain, soft knots."""
    import numpy as np
    S, planks = 512, 4
    rng = np.random.default_rng(11)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    base = np.array(_hex("#5E391C"), dtype=np.float32)
    dark = np.array(_hex("#34200F"), dtype=np.float32)
    pw = S / planks
    shade = np.zeros((S, S), dtype=np.float32)
    for p in range(planks):
        x0 = p * pw
        off = rng.uniform(0, 6.28)
        local = xx - x0
        inside = (local >= 0) & (local < pw)
        # grain: wavy stripes running along the plank (vertical)
        wav = np.sin((local + 9 * np.sin(yy / 70.0 + off)) / pw * 6.28 * rng.uniform(1.5, 2.5) + off)
        g = np.clip((wav - 0.7) * 2.5, 0, 1) * 0.28
        shade[inside] += g[inside] + rng.uniform(-0.06, 0.06)
        # plank groove
        shade[(local >= 0) & (local < 3)] = 0.85
    for _ in range(5):   # knots
        cx, cy, r = rng.uniform(0, S), rng.uniform(0, S), rng.uniform(6, 12)
        d = np.sqrt((xx - cx) ** 2 + ((yy - cy) * 0.6) ** 2)
        shade += np.clip(1 - d / r, 0, 1) * 0.5
    shade = np.clip(shade, 0, 1)
    img = base[None, None, :] * (1 - shade[..., None]) + dark[None, None, :] * shade[..., None]
    img *= (0.94 + 0.06 * (yy / S))[..., None]
    px = np.concatenate([img, np.ones((S, S, 1), dtype=np.float32)], axis=2)
    old = bpy.data.images.get("BK_CabinetWood")
    if old:
        bpy.data.images.remove(old)
    im = bpy.data.images.new("BK_CabinetWood", S, S, alpha=False)
    im.colorspace_settings.name = "sRGB"
    im.pixels = px.ravel()
    im.update()
    im.filepath_raw = WOOD_PNG
    im.file_format = "PNG"
    im.save()
    m = bpy.data.materials.get("BK_CabinetWood")
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE":
                n.image = im
    return im


def wood_material():
    m = bpy.data.materials.get("BK_CabinetWood") or bpy.data.materials.new("BK_CabinetWood")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.6
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("BK_CabinetWood") or build_cabinet_wood()
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def tiles_material():
    m = bpy.data.materials.get("BK_WallTiles") or bpy.data.materials.new("BK_WallTiles")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Roughness"].default_value = 0.5
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("BK_WallTiles") or build_wall_tiles()
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    return m


def lamp_material():
    """Glowing lamp disc; Unity side is an unlit white material (M_BK_Lamp), no real light."""
    m = bpy.data.materials.get("BK_Lamp") or bpy.data.materials.new("BK_Lamp")
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value = (1, 0.97, 0.9, 1)
    p.inputs["Emission Color"].default_value = (1, 0.95, 0.85, 1)
    p.inputs["Emission Strength"].default_value = 3.0
    return m


def glow_material():
    """Palette colors drawn unlit (neon tubes); Unity side is M_BK_Glow (URP Unlit + BK_Palette)."""
    m = bpy.data.materials.get("BK_Glow") or bpy.data.materials.new("BK_Glow")
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.get("BK_Palette") or build_palette()
    nt.links.new(tex.outputs["Color"], p.inputs["Base Color"])
    nt.links.new(tex.outputs["Color"], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 2.0
    return m


def decal_uv_rect(key):
    i = next(i for i, (k, _, _) in enumerate(DECALS) if k == key)
    col, row = i % DEC_GRID, i // DEC_GRID
    u0, v1 = col / DEC_GRID, 1 - row / DEC_GRID
    s = 1 / DEC_GRID
    pad = DEC_PAD * s
    return u0 + pad, v1 - s + pad, u0 + s - pad, v1 - pad


class Builder:
    """Collects parts of one model under a source collection; bake() joins them into one export mesh."""

    def __init__(self, name):
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

    def _finish(self, name, bm, color, bevel, seg):
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
        o["bk_color"] = color
        self.parts.append(o)
        return o

    def box(self, name, size, center_xy, z0, color, bevel=0.02, seg=2, rot_y=0.0, rot_x=0.0):
        """Box around its center; rot_y turns it in the front XZ plane, rot_x tilts it back (degrees)."""
        sx, sy, sz = size
        bm = bmesh.new()
        m = (Matrix.Translation((center_xy[0], center_xy[1], z0 + sz / 2)) @ Matrix.Rotation(math.radians(rot_y), 4, "Y")
             @ Matrix.Rotation(math.radians(-rot_x), 4, "X"))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m @ Matrix.Diagonal((sx, sy, sz, 1)))
        o = self._finish(name, bm, color, bevel, seg)
        if bevel <= 0:
            # no bevel = no hardened normals: smooth shading would bend every face toward its corners
            for p in o.data.polygons:
                p.use_smooth = False
        return o

    def decal(self, name, size_xz, center_xz, y, key, corner=0.1, segs=4, tilt=0.0, yaw=0.0):
        """Flat rounded-rect sticker facing -Y at depth y, UVs mapped onto the atlas slot here.
        tilt leans it back around its center line, then yaw turns it around Z (degrees; -90 faces -X)."""
        w, h = size_xz
        cx, cz = center_xz
        r = corner * min(w, h)
        pts = []
        for qx, qz, a0 in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
            ccx, ccz = cx + qx * (w / 2 - r), cz + qz * (h / 2 - r)
            for k in range(segs + 1):
                a = math.radians(a0 + 90 * k / segs)
                pts.append((ccx + r * math.cos(a), y, ccz + r * math.sin(a)))
        bm = bmesh.new()
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        f.normal_update()
        if f.normal.y > 0:
            f.normal_flip()
        u0, v0, u1, v1 = decal_uv_rect(key)
        uvl = bm.loops.layers.uv.new("UVMap")
        for loop in f.loops:
            co = loop.vert.co
            loop[uvl].uv = (u0 + (u1 - u0) * (co.x - (cx - w / 2)) / w, v0 + (v1 - v0) * (co.z - (cz - h / 2)) / h)
        if tilt:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(cx, y, cz), matrix=Matrix.Rotation(math.radians(-tilt), 3, "X"))
        if yaw:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(cx, y, cz), matrix=Matrix.Rotation(math.radians(yaw), 3, "Z"))
        o = self._finish(name, bm, "white", 0.0, 1)
        o["bk_decal"] = key
        return o

    def quad(self, name, corners, color, inside=(0.0, 0.0, 1.5), tile=False):
        """Single quad (4 corners in order); its normal is turned toward `inside`.
        tile=True maps it to the wall tile material in meters instead of the palette."""
        from mathutils import Vector
        bm = bmesh.new()
        f = bm.faces.new([bm.verts.new(c) for c in corners])
        f.normal_update()
        center = sum((Vector(c) for c in corners), Vector()) / len(corners)
        if f.normal.dot(Vector(inside) - center) < 0:
            f.normal_flip()
        o = self._finish(name, bm, color, 0.0, 1)
        if tile:
            o["bk_tile"] = True
        return o

    def drill(self, o, r, loc, depth=1.0, segs=20):
        """Cuts a vertical cylindrical hole through `o` (boolean placed before its bevel)."""
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=depth, matrix=Matrix.Translation(loc))
        me = bpy.data.meshes.new(o.name + "_drill")
        bm.to_mesh(me)
        bm.free()
        cutter = bpy.data.objects.new(o.name + "_drill", me)
        self.src.objects.link(cutter)
        cutter.display_type = "WIRE"
        cutter.hide_render = True
        m = o.modifiers.new("Drill", "BOOLEAN")
        m.operation, m.object, m.solver = "DIFFERENCE", cutter, "EXACT"
        o.modifiers.move(o.modifiers.find("Drill"), 0)   # cut first, then bevel the hole edge
        return o

    def hollow_box(self, name, size, center_xy, z0, inner_size, inner_center_xy, inner_z0, color, bevel=0.02, seg=2):
        """Box with an open-top cavity (boolean difference), bevelled after the cut so the rim is rounded."""
        o = self.box(name, size, center_xy, z0, color, 0)
        isx, isy, isz = inner_size
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((inner_center_xy[0], inner_center_xy[1], inner_z0 + isz / 2)) @ Matrix.Diagonal((isx, isy, isz, 1)))
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
        if os.path.exists(font):
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

    def lamp(self, name, pts_rz, center_xy, segs=24):
        """Lathe solid on the unlit lamp material (glowing disc)."""
        o = self.lathe(name, pts_rz, center_xy, "white", segs)
        o["bk_lamp"] = True
        return o

    def lathe(self, name, pts_rz, center_xy, color, segs=24, scale_y=1.0):
        """Revolves an (r, z) profile around a vertical axis; r == 0 ends close the solid.
        scale_y < 1 flattens it front-to-back (squeeze bottles)."""
        cx, cy = center_xy
        bm = bmesh.new()
        rings = []
        for r, z in pts_rz:
            if r <= 1e-6:
                rings.append([bm.verts.new((cx, cy, z))])
            else:
                rings.append([bm.verts.new((cx + r * math.cos(2 * math.pi * k / segs), cy + scale_y * r * math.sin(2 * math.pi * k / segs), z))
                              for k in range(segs)])
        for a, c in zip(rings, rings[1:]):
            if len(a) == 1 and len(c) == 1:
                continue
            for k in range(segs):
                if len(a) == 1:
                    bm.faces.new((a[0], c[k], c[(k + 1) % segs]))
                elif len(c) == 1:
                    bm.faces.new((a[k], a[(k + 1) % segs], c[0]))
                else:
                    bm.faces.new((a[k], a[(k + 1) % segs], c[(k + 1) % segs], c[k]))
        if len(rings[0]) > 1:
            bm.faces.new(list(reversed(rings[0])))
        if len(rings[-1]) > 1:
            bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, 0.0, 1)

    def prism(self, name, pts_xy, z0, z1, color, bevel=0.02, seg=2):
        """Extrudes a closed XY polygon (counter-clockwise from above) from z0 to z1 (L shapes etc.)."""
        bm = bmesh.new()
        f = bm.faces.new([bm.verts.new((x, y, z0)) for x, y in pts_xy])
        r = bmesh.ops.extrude_face_region(bm, geom=[f])
        bmesh.ops.translate(bm, vec=(0, 0, z1 - z0), verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, bevel, seg)

    def hull(self, name, points, color, bevel=0.02, seg=2):
        """Convex hull of the given points (slanted / tapered solids)."""
        bm = bmesh.new()
        vs = [bm.verts.new(p) for p in points]
        bmesh.ops.convex_hull(bm, input=vs)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, bevel, seg)

    def pipe(self, name, pts, r, color, segs=12):
        """Capped tube along a polyline (faucets, bent handles); rings use parallel transport, no twist."""
        from mathutils import Vector
        pts = [Vector(p) for p in pts]
        bm = bmesh.new()
        rings, n1 = [], None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if n1 is None:
                ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
                n1 = t.cross(ref).normalized()
            else:
                n1 = (n1 - t * n1.dot(t)).normalized()
            n2 = t.cross(n1)
            rings.append([bm.verts.new(p + r * (math.cos(a) * n1 + math.sin(a) * n2))
                          for a in (2 * math.pi * k / segs for k in range(segs))])
        for a, b2 in zip(rings, rings[1:]):
            for k in range(segs):
                bm.faces.new((a[k], a[(k + 1) % segs], b2[(k + 1) % segs], b2[k]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, 0.0, 1)

    def cyl(self, name, r, depth, loc, axis, color, segs=16, bevel=0.0, seg=1):
        rot = {"Y": Matrix.Rotation(math.radians(90), 4, "X"),
               "X": Matrix.Rotation(math.radians(90), 4, "Y")}.get(axis, Matrix.Identity(4))
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=depth, matrix=Matrix.Translation(loc) @ rot)
        return self._finish(name, bm, color, bevel, seg)

    def profile_x(self, name, pts_yz, x0, thick, color, bevel=0.01, seg=2):
        """Extrudes a closed YZ polygon along +X."""
        bm = bmesh.new()
        f = bm.faces.new([bm.verts.new((x0, y, z)) for y, z in pts_yz])
        r = bmesh.ops.extrude_face_region(bm, geom=[f])
        bmesh.ops.translate(bm, vec=(thick, 0, 0), verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self._finish(name, bm, color, bevel, seg)

    def bake(self):
        """Applies modifiers, writes palette UVs (per-part vertical gradient), joins into one object."""
        out_col = self._collection(self.name)
        old = bpy.data.objects.get(self.name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        mat = palette_material()
        baked = []
        # material slot order = order of first appearance: palette, decals, wall tiles, lamp glow
        kind = lambda p: 5 if "bk_wood" in p else (4 if "bk_glow" in p else (3 if "bk_lamp" in p else (2 if "bk_tile" in p else (1 if "bk_decal" in p else 0))))
        for o in sorted(self.parts, key=kind):
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            me.transform(o.matrix_world)
            xs = [v.co.x for v in me.vertices]
            ys = [v.co.y for v in me.vertices]
            zs = [v.co.z for v in me.vertices]
            x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
            span = max(z1 - z0, 1e-5)
            uv = me.uv_layers.new(name="UVMap") if not me.uv_layers else me.uv_layers[0]
            me.materials.clear()
            if "bk_tile" in o or "bk_wood" in o:
                rep = TILE_REPEAT if "bk_tile" in o else WOOD_REPEAT
                along_x = (x1 - x0) >= (max(ys) - min(ys))
                for loop in me.loops:
                    co = me.vertices[loop.vertex_index].co
                    uv.data[loop.index].uv = ((co.x if along_x else co.y) / rep, co.z / rep)
                me.materials.append(tiles_material() if "bk_tile" in o else wood_material())
            elif "bk_decal" in o:
                me.materials.append(decals_material())   # UVs were written by decal()
            elif "bk_lamp" in o:
                me.materials.append(lamp_material())
            else:
                u = col_u(o["bk_color"])
                flat = (z1 - z0) < 1e-3   # floors/ceilings: no height to grade, use the true base color
                for loop in me.loops:
                    z = me.vertices[loop.vertex_index].co.z
                    t = 0.6 if flat else (z - z0) / span
                    if "bk_glow" in o:
                        t = 0.6 + 0.4 * t      # neon: keep to the lit half of the column
                    uv.data[loop.index].uv = (u, V_MIN + (V_MAX - V_MIN) * t)
                me.materials.append(glow_material() if "bk_glow" in o else mat)
            ob = bpy.data.objects.new(o.name + "_baked", me)
            out_col.objects.link(ob)
            baked.append(ob)
        with bpy.context.temp_override(active_object=baked[0], selected_editable_objects=baked, object=baked[0]):
            bpy.ops.object.join()
        res = baked[0]
        res.name = res.data.name = self.name
        self.src.hide_viewport = True
        self.src.hide_render = True
        res.data.calc_loop_triangles()
        return res, len(res.data.loop_triangles)


_TEXTURES_WRITTEN = False


def export_fbx(obj, rel_path):
    """Writes the FBX under KitchenKit and refreshes the shared palette and decal PNGs next to it."""
    global _TEXTURES_WRITTEN
    if not _TEXTURES_WRITTEN:    # once per script run (the module is reloaded by every script)
        build_palette()
        build_decals()
        build_wall_tiles()
        build_cabinet_wood()
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
