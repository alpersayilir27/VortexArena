"""Shared helpers for the Zombie Manor arena: venue outline, floor/zone layout, coordinate mapping, part builders.

All layout numbers are in venue coordinates: (x, z) = Unity world X/Z = dimensions JSON (x, y); h = height.
Blender position = (-x, -z, h)  (Unity (x, y, z) = Blender (-x, z, -y), see blender/BurgerKitchen/README.md).
"""
import bpy
import bmesh
import importlib
import json
import math
import os
from mathutils import Vector

REPO = r"C:\UnityProjects\VortexArena"
PROJ = os.path.join(REPO, "blender", "ZombieManor")
DIMENSIONS_JSON = os.path.join(REPO, "Assets", "Arenas", "Venues", "VortexAntep", "Data", "vortexantep_dimensions.json")

FLOORS = (0.0, 3.5, 7.0)   # floor top heights; portals' upperHeight must match
SLAB = 0.20                # slab thickness below an upper floor top
CEIL = FLOORS[1] - SLAB    # visual ceiling height above each floor
STOREY = FLOORS[1] - FLOORS[0]
# equal storeys: one portal rig and one wall height serve both floors; texture tiling is derived from STOREY
assert abs((FLOORS[2] - FLOORS[1]) - STOREY) < 1e-6
DOOR_H = 2.4               # head height of every walkable opening (interior doors, front door, breach)
WIN_Z = (0.9, 2.2)         # window band above a floor; human-size, independent of STOREY
SHED_TOP = 2.8             # terrace shed / cage roof height above the terrace floor
ZONE_Z = (-0.5, 3.5)       # A | B | C split lines (south to north)
RAIL_H = 1.0
WALL_T = 0.15

# floor portal discs (radius 0.25 m); key = (lower floor, upper floor)
PORTAL_R = 0.25
PORTALS = {
    (0, 1): (-0.9, 7.8),
    (1, 2): (5.2, 7.8),
}


def bl(x, z, h=0.0):
    return Vector((-x, -z, h))


# ---------------------------------------------------------------- venue

def load_venue(path=DIMENSIONS_JSON):
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    plane = [(p["x"], p["y"]) for p in d["plane"]]
    cols = [(c["name"], [(p["x"], p["y"]) for p in c["points"]], c.get("height") or d.get("defaultColumnHeight", 3))
            for c in d["columns"]]
    return plane, cols


PLANE, COLUMNS = load_venue()
# plane order in the JSON: NW, SW, SE, NE (counter-clockwise seen from above)
NW, SW, SE, NE = PLANE


def _lerp_x(a, b, z):
    t = (z - a[1]) / (b[1] - a[1])
    return a[0] + (b[0] - a[0]) * t


def west_x(z):
    return _lerp_x(SW, NW, z)


def east_x(z):
    return _lerp_x(SE, NE, z)


def zone_poly(zone):
    """Venue-clipped XZ polygon of strip 'A' | 'B' | 'C' (counter-clockwise)."""
    za, zb = ZONE_Z
    if zone == "A":
        return [SW, SE, (east_x(za), za), (west_x(za), za)]
    if zone == "B":
        return [(west_x(za), za), (east_x(za), za), (east_x(zb), zb), (west_x(zb), zb)]
    return [(west_x(zb), zb), (east_x(zb), zb), NE, NW]


def column_bounds(name):
    pts = next(p for n, p, _ in COLUMNS if n == name)
    xs, zs = [p[0] for p in pts], [p[1] for p in pts]
    return min(xs), min(zs), max(xs), max(zs)


# ---------------------------------------------------------------- collections / materials

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


GRAY = {
    "ground": (0.22, 0.24, 0.22, 1),
    "cobble": (0.32, 0.31, 0.29, 1),
    "floor": (0.45, 0.33, 0.22, 1),
    "checker": (0.80, 0.80, 0.78, 1),
    "deck": (0.38, 0.34, 0.30, 1),
    "wall": (0.62, 0.60, 0.55, 1),
    "ext": (0.40, 0.46, 0.48, 1),
    "roof": (0.25, 0.28, 0.32, 1),
    "rail": (0.85, 0.85, 0.82, 1),
    "fence": (0.15, 0.15, 0.15, 1),
    "column": (0.90, 0.15, 0.10, 1),
    "portal": (0.10, 0.85, 0.95, 1),
    "void": (1.00, 0.20, 0.70, 1),
    "player": (1.00, 0.85, 0.10, 1),
}


def gray_material(kind):
    name = "ZM_Gray_" + kind
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
    col = GRAY[kind]
    m.diffuse_color = col
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = col
    if kind in ("portal", "player"):
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = 2.0
    return m


# ---------------------------------------------------------------- builders (venue coordinates)

def _obj(name, bm, kind, coll, flat=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = not flat
    me.materials.append(gray_material(kind))
    o = bpy.data.objects.new(name, me)
    coll.objects.link(o)
    o["zm_kind"] = kind
    return o


def prism(name, poly_xz, h0, h1, kind, coll):
    """Extrudes a venue XZ polygon from h0 to h1."""
    bm = bmesh.new()
    f = bm.faces.new([bm.verts.new(bl(x, z, h0)) for x, z in poly_xz])
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, vec=(0, 0, h1 - h0), verts=[e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, kind, coll)


def box(name, x0, z0, x1, z1, h0, h1, kind, coll):
    return prism(name, [(x0, z0), (x1, z0), (x1, z1), (x0, z1)], h0, h1, kind, coll)


def _slab_along(bm, p0, p1, s0, s1, h0, h1, t, off):
    """Box along a->b between s0..s1 (metres) and h0..h1; returns its faces (outward-wound)."""
    d = (p1 - p0).normalized()
    n = Vector((d.y, -d.x))   # right-hand normal; outward for a counter-clockwise outline
    c0 = p0 + d * s0 + n * off
    c1 = p0 + d * s1 + n * off
    q = [c0 - n * t / 2, c1 - n * t / 2, c1 + n * t / 2, c0 + n * t / 2]
    vs = [bm.verts.new(bl(v.x, v.y, h)) for h in (h0, h1) for v in q]
    faces = [bm.faces.new([vs[i] for i in f])
             for f in ((0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7))]
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def _wall_pieces(bm, a, b, h0, h1, t, side, openings):
    """Solid pieces of a wall with openings (s0, s1, top[, bottom]); returns faces and the wall's right normal."""
    p0, p1 = Vector(a), Vector(b)
    length = (p1 - p0).length
    off = side * t / 2
    faces, cur = [], 0.0
    for op in sorted(openings):
        s0, s1, top = op[:3]
        bottom = op[3] if len(op) > 3 else 0.0
        if s0 > cur:
            faces += _slab_along(bm, p0, p1, cur, s0, h0, h1, t, off)
        # 1e-6: on upper floors h1 - h0 rounds a hair above the opening's height and would leave a zero-height
        # lintel whose top face shows as a strip across the doorway
        if top < h1 - h0 - 1e-6:
            faces += _slab_along(bm, p0, p1, s0, s1, h0 + top, h1, t, off)
        if bottom > 1e-6:
            faces += _slab_along(bm, p0, p1, s0, s1, h0, h0 + bottom, t, off)
        cur = s1
    if cur < length:
        faces += _slab_along(bm, p0, p1, cur, length, h0, h1, t, off)
    d = (p1 - p0).normalized()
    return faces, Vector((-d.y, d.x, 0.0))   # right normal (d.y, -d.x) mapped through bl()


def wall(name, a, b, h0, h1, kind, coll, t=WALL_T, side=0, openings=()):
    """Graybox wall from venue point a to b. side=+1 pushes it fully to the right of a->b (outside a CCW outline).
    openings: (s0, s1, top[, bottom]) in metres along a->b."""
    bm = bmesh.new()
    _wall_pieces(bm, a, b, h0, h1, t, side, openings)
    return _obj(name, bm, kind, coll)


def disc(name, x, z, h, r, kind, coll, depth=0.02, segs=24):
    bm = bmesh.new()
    from mathutils import Matrix
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=depth,
                          matrix=Matrix.Translation(bl(x, z, h + depth / 2)))
    return _obj(name, bm, kind, coll, flat=False)


# ---------------------------------------------------------------- art materials (procedural; Part 8 bakes them to tileable PNGs)
# T = metres covered by one texture tile. UVs are written in tile units (world box projection / T), so Unity's
# material tiling stays 1. Patterns are periodic over one tile: noise/cells run on a 4D torus, bands/planks/
# bricks/checker use sizes that divide T exactly (change a size -> change T with it).
MATS = {
    # real CC0 maps (zm_textures): tex = (site, id, res); T = the asset's real size in metres
    "Cobble":    dict(tex=("ph", "cobblestone_01", "2k"), T=2.4, c1=(0.13, 0.12, 0.105)),
    "Flagstone": dict(tex=("ph", "old_stone_wall", "1k"), T=2.0, c1=(0.22, 0.21, 0.18)),
    "Road":      dict(tex=("ph", "asphalt_02", "1k"), T=3.0, c1=(0.14, 0.14, 0.14)),
    "Concrete":  dict(tex=("ph", "concrete_pavement", "1k"), T=1.8, c1=(0.34, 0.33, 0.31)),
    "Dirt":      dict(tex=("ph", "dry_decay_leaves", "1k"), T=2.0, c1=(0.13, 0.12, 0.08)),
    "Siding":    dict(tex=("ph", "blue_painted_planks", "2k"), T=1.0, c1=(0.30, 0.36, 0.38)),
    # cream ground-floor front (reference): an off-white source keeps it light; the blue planks greyed read brown
    "SidingCream": dict(tex=("ph", "distressed_painted_planks", "1k"), T=1.6, grey=True, tint=(0.98, 0.86, 0.62),
                        c1=(0.62, 0.55, 0.42)),
    # interior woodwork: worn, scuffed dark wood (a clean veneer read as flat, glossy stripes)
    "Trim":      dict(tex=("ph", "wood_table_worn", "1k"), T=0.55, tint=(0.62, 0.52, 0.46), c1=(0.12, 0.07, 0.04)),
    "Brick":     dict(tex=("ph", "red_brick", "1k"), T=1.4, c1=(0.30, 0.13, 0.09)),
    "Stone":     dict(tex=("ph", "stone_wall", "1k"), T=2.0, c1=(0.33, 0.32, 0.29)),
    "Iron":      dict(tex=("ph", "rusty_metal", "1k"), T=1.5, metal=True, grey=True, tint=(0.24, 0.215, 0.2),
                      c1=(0.06, 0.05, 0.045)),                     # weathered black iron (the source's rust reads copper)
    "DoorWood":  dict(tex=("ph", "weathered_brown_planks", "1k"), T=1.8, c1=(0.20, 0.11, 0.06)),
    "Glass":     dict(pat="noise", T=1.0, scale=2.0, c1=(0.01, 0.015, 0.02), c2=(0.04, 0.05, 0.06), rough=0.15),
    "Roof":      dict(tex=("ph", "roof_slates_02", "1k"), T=3.0, c1=(0.14, 0.16, 0.18)),
    "CarPaint":  dict(pat="noise", T=1.0, scale=2.5, c1=(0.25, 0.10, 0.03), c2=(0.55, 0.45, 0.20), rough=0.6, metal=0.3),
    "Foliage":   dict(tex=("ph", "forest_leaves_02", "1k"), T=3.0, grey=True, tint=(0.11, 0.16, 0.09),
                      c1=(0.04, 0.06, 0.035)),                     # dark night foliage clumps
    "Bark":      dict(tex=("ph", "bark_brown_02", "1k"), T=1.0, c1=(0.10, 0.08, 0.06)),
    "LampGlow":  dict(pat="none", c1=(1.0, 0.62, 0.25), rough=0.5, emit=6.0),
    # generated damask (no CC0 damask exists) over the decrepit paper's normal/roughness and stains
    "Wallpaper": dict(tex=("gen", "damask_beige", "1024"), base=("ph", "decrepit_wallpaper", "2k"), T=0.64,
                      gen=dict(kind="damask", v=2, bg=(0.50, 0.43, 0.31), fg=(0.66, 0.58, 0.42)), c1=(0.36, 0.32, 0.22)),
    "WallpaperGrey": dict(tex=("gen", "damask_grey", "1024"), base=("ph", "decrepit_wallpaper", "2k"), T=0.64,
                          gen=dict(kind="damask", v=2, bg=(0.40, 0.41, 0.40), fg=(0.55, 0.56, 0.54)), c1=(0.3, 0.3, 0.3)),
    # night outside a window: self-lit; a half-storey tile plus the sill offset puts both storeys' window
    # bands (WIN_Z and FLOORS[1] + WIN_Z) at the same place inside one tile
    "WindowView": dict(tex=("gen", "window_view", "512"), T=(1.1, STOREY / 2),
                       off=(0.0, -WIN_Z[0] / (STOREY / 2)), emit=0.8,
                       gen=dict(kind="window", v=2), c1=(0.15, 0.17, 0.22)),
    "ShutterPaint": dict(tex=("ph", "distressed_painted_planks", "1k"), T=1.6, grey=True, tint=(0.2, 0.26, 0.22),
                         c1=(0.05, 0.07, 0.06)),                  # louvred shutters: flaking dark green paint
    "TrimPaint": dict(tex=("ph", "distressed_painted_planks", "1k"), T=1.6, grey=True, tint=(0.86, 0.83, 0.76),
                      c1=(0.7, 0.7, 0.66)),                       # exterior trim: dirty off-white paint
    "Plaster":   dict(tex=("ph", "ceiling_interior", "1k"), T=1.2, grey=True, tint=(0.82, 0.8, 0.74), nstr=0.35,
                      c1=(0.6, 0.58, 0.54)),
    "FloorWood": dict(tex=("ph", "dark_wooden_planks", "2k"), T=2.0, c1=(0.16, 0.09, 0.05)),
    # floors: the scans are freshly polished (smoothness ~0.9) and mirror the lamps; an abandoned house is dull
    "Parquet":   dict(tex=("ph", "herringbone_parquet", "2k"), T=3.4, gloss=0.4, c1=(0.18, 0.1, 0.05)),
    "Checker":   dict(tex=("acg", "Tiles012", "2k"), T=1.3, gloss=0.5, c1=(0.3, 0.3, 0.3)),   # 4 x 4 squares per tile
    "Ceiling":   dict(tex=("ph", "dark_wooden_planks", "2k"), T=2.0, tint=(0.75, 0.68, 0.62), c1=(0.12, 0.07, 0.04)),
    "Gold":      dict(tex=("acg", "Metal048C", "1k"), T=0.5, metal=True, c1=(0.65, 0.46, 0.14)),
    "Fabric":    dict(tex=("ph", "rough_linen", "1k"), T=0.54, grey=True, tint=(0.82, 0.78, 0.7), c1=(0.72, 0.7, 0.64)),
    "Sheet":     dict(tex=("ph", "rough_linen", "1k"), T=0.54, grey=True, tint=(0.78, 0.75, 0.68), two_sided=True,
                      c1=(0.7, 0.68, 0.62)),                       # dust sheets: single cloth surface, two-sided
    "Steel":     dict(tex=("ph", "metal_plate_02", "1k"), T=2.0, metal=True, grey=True, tint=(0.85, 0.85, 0.87),
                      c1=(0.45, 0.45, 0.47)),                      # worn polished steel (the source reads rusty)
    "Roots":     dict(tex=("ph", "bark_brown_01", "1k"), T=0.6, grey=True, tint=(0.55, 0.12, 0.1), c1=(0.28, 0.06, 0.05)),
    "Hide":      dict(pat="noise", T=0.5, scale=12.0, c1=(0.12, 0.07, 0.03), c2=(0.35, 0.22, 0.1), rough=0.9),
    "Bone":      dict(pat="noise", T=0.5, scale=8.0, c1=(0.45, 0.4, 0.32), c2=(0.75, 0.7, 0.6), rough=0.6),
    "Paper":     dict(pat="noise", T=0.5, scale=3.0, c1=(0.48, 0.44, 0.35), c2=(0.66, 0.62, 0.52), rough=0.9),   # aged
    "Velvet":    dict(tex=("ph", "velour_velvet", "1k"), T=0.56, c1=(0.3, 0.03, 0.04)),
    "Shade":     dict(pat="none", c1=(0.95, 0.75, 0.5), rough=0.8, emit=1.2),
    "Mountain":  dict(tex=("ph", "aerial_grass_rock", "1k"), T=15.0, grey=True, tint=(0.09, 0.1, 0.11),
                      c1=(0.02, 0.025, 0.03)),                     # night ridge: rock and scrub from 15 m aerial scans
    "Deck":      dict(tex=("ph", "weathered_brown_planks", "1k"), T=1.8, grey=True, tint=(0.42, 0.4, 0.38), c1=(0.16, 0.14, 0.12)),
    "Copper":    dict(pat="noise", T=0.5, scale=12.0, c1=(0.15, 0.05, 0.02), c2=(0.45, 0.2, 0.08), rough=0.4, metal=0.9),
    "GlowBlue":  dict(pat="none", c1=(0.25, 0.55, 1.0), rough=0.3, emit=8.0),
    "GlowRed":   dict(pat="none", c1=(1.0, 0.08, 0.05), rough=0.3, emit=6.0),
    "Bronze":    dict(tex=("acg", "Metal048C", "1k"), T=0.5, metal=True, grey=True, tint=(0.42, 0.3, 0.17),
                      c1=(0.2, 0.14, 0.08)),                       # dark aged statue bronze
    "Marble":    dict(tex=("ph", "marble_01", "1k"), T=1.5, grey=True, tint=(0.72, 0.7, 0.66), c1=(0.6, 0.58, 0.55)),
    "Wainscot":  dict(tex=("ph", "dark_paneled_wood", "2k"), T=(1.6, 2.6), c1=(0.12, 0.06, 0.03)),   # tall raised panels
    # Victorian fireplace: majolica tiles (one 15.2 cm tile per image), black cast iron, overmantel mirror
    "FireTile":  dict(tex=("gen", "majolica_tile", "512"), base=("ph", "decrepit_wallpaper", "2k"), T=0.152,
                      gen=dict(kind="tile", v=1, bg=(0.06, 0.16, 0.12), fg=(0.62, 0.5, 0.26), acc=(0.32, 0.06, 0.05)),
                      c1=(0.1, 0.18, 0.14)),
    "CastIron":  dict(pat="noise", T=0.5, scale=5.0, c1=(0.025, 0.025, 0.027), c2=(0.06, 0.058, 0.055), rough=0.45,
                      metal=0.6),                                  # black-painted iron (a texture streaks on thin bars)
    "Mirror":    dict(pat="none", c1=(0.55, 0.56, 0.52), rough=0.06, metal=1.0),
    # rug: one image over the whole rug; T = rug size, off = UV shift so the centred rug covers 0..1
    "Rug":       dict(tex=("gen", "rug_persian", "1024"), base=("ph", "rough_linen", "1k"), T=(2.4, 1.6), off=(0.5, 0.5),
                      gen=dict(kind="rug", v=1, size=(2.4, 1.6)), c1=(0.3, 0.06, 0.05)),
}


def tile_of(key):
    """(Tu, Tv, off_u, off_v) of a material key; untextured materials map 1:1 metres."""
    spec = MATS.get(key, {})
    T = spec.get("T", 1.0)
    Tu, Tv = T if isinstance(T, tuple) else (T, T)
    ou, ov = spec.get("off", (0.0, 0.0))
    return Tu, Tv, ou, ov


def _sock(sockets, ident):
    return next(s for s in sockets if s.identifier == ident)


def material(key):
    name = "ZM_" + key
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    spec = MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nodes, links = nt.nodes, nt.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    if "tex" in spec:                                       # real CC0 maps; UVs are already in tile units
        import zm_textures
        importlib.reload(zm_textures)
        zm_textures.build(m, spec, nodes.new("ShaderNodeTexCoord").outputs["UV"], nodes, links, bsdf)
        return m
    c1, c2 = spec["c1"] + (1,), spec.get("c2", spec["c1"]) + (1,)
    m.diffuse_color = c2
    pat = spec["pat"]
    Tu, Tv, _, _ = tile_of(key)

    def math_(op, a, b=None):
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

    def ramp(fac, stops):
        r = nodes.new("ShaderNodeValToRGB")
        els = r.color_ramp.elements
        els[0].position, els[0].color = stops[0]
        els[1].position, els[1].color = stops[1]
        links.new(fac, r.inputs["Fac"])
        return r.outputs["Color"]

    def combine(x, y, z=0.0):
        c = nodes.new("ShaderNodeCombineXYZ")
        for i, s in enumerate((x, y, z)):
            if isinstance(s, (int, float)):
                c.inputs[i].default_value = s
            else:
                links.new(s, c.inputs[i])
        return c.outputs[0]

    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(nodes.new("ShaderNodeTexCoord").outputs["UV"], sep.inputs[0])
    u, v = sep.outputs["X"], sep.outputs["Y"]               # tile units
    um, vm = math_("MULTIPLY", u, Tu), math_("MULTIPLY", v, Tv)   # metres
    metres = combine(um, vm)
    # one tile wrapped onto a 4D torus whose circumference is the tile size: noise repeats exactly per tile
    tw = lambda s, T, f: math_("MULTIPLY", math_(f, math_("MULTIPLY", s, 2 * math.pi)), T / (2 * math.pi))
    torus_xyz = combine(tw(u, Tu, "COSINE"), tw(u, Tu, "SINE"), tw(v, Tv, "COSINE"))
    torus_w = tw(v, Tv, "SINE")

    def noise4(scale, detail=6.0):
        n = nodes.new("ShaderNodeTexNoise")
        n.noise_dimensions = "4D"
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        links.new(torus_xyz, n.inputs["Vector"])
        links.new(torus_w, n.inputs["W"])
        return n.outputs["Fac"]

    if pat == "cells":
        vo = nodes.new("ShaderNodeTexVoronoi")
        vo.voronoi_dimensions = "4D"
        vo.feature = "DISTANCE_TO_EDGE"
        vo.inputs["Scale"].default_value = spec["scale"]
        links.new(torus_xyz, vo.inputs["Vector"])
        links.new(torus_w, vo.inputs["W"])
        col = ramp(vo.outputs["Distance"], [(0.02, c1), (0.10, c2)])
    elif pat in ("bands", "planks"):
        fr = math_("FRACT", math_("MULTIPLY", vm if pat == "bands" else um, spec["scale"]))
        col = ramp(fr, [(0.06, c1), (0.16, c2)])
    elif pat == "brick":
        b = nodes.new("ShaderNodeTexBrick")
        bw, bh = spec["size"]
        b.inputs["Scale"].default_value = 1.0
        b.inputs["Brick Width"].default_value = bw
        b.inputs["Row Height"].default_value = bh
        b.inputs["Mortar Size"].default_value = 0.012
        b.inputs["Color1"].default_value = c2
        b.inputs["Color2"].default_value = tuple(c * 0.8 for c in c2[:3]) + (1,)
        b.inputs["Mortar"].default_value = c1
        links.new(metres, b.inputs["Vector"])
        col = b.outputs["Color"]
    elif pat == "checker":
        ch = nodes.new("ShaderNodeTexChecker")
        ch.inputs["Scale"].default_value = spec["scale"]   # squares are 1/scale metres
        ch.inputs["Color1"].default_value = c1
        ch.inputs["Color2"].default_value = c2
        links.new(metres, ch.inputs["Vector"])
        col = ch.outputs["Color"]
    elif pat == "noise":
        col = ramp(noise4(spec["scale"]), [(0.35, c1), (0.65, c2)])
    else:
        col = None

    if col is not None:
        # large-scale grime so tiled surfaces do not read as flat repeats (periodic too)
        g = ramp(noise4(0.6, detail=2.0), [(0.3, (0.55, 0.55, 0.55, 1)), (0.7, (1, 1, 1, 1))])
        mx = nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        _sock(mx.inputs, "Factor_Float").default_value = 1.0
        links.new(col, _sock(mx.inputs, "A_Color"))
        links.new(g, _sock(mx.inputs, "B_Color"))
        col = _sock(mx.outputs, "Result_Color")
        links.new(col, bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = c1
    bsdf.inputs["Roughness"].default_value = spec.get("rough", 0.8)
    bsdf.inputs["Metallic"].default_value = spec.get("metal", 0.0)
    if spec.get("emit"):
        bsdf.inputs["Emission Color"].default_value = c1
        bsdf.inputs["Emission Strength"].default_value = spec["emit"]
    return m


# ---------------------------------------------------------------- z-fight check (same algorithm as blender/BurgerKitchen bk_common)
ZF_GAP = 0.002
PART_ATTR = "zm_part"   # per-face id of the builder call that made the face
SMOOTH_ATTR = "zm_smooth"


def zfight_pairs(meshes, gap=ZF_GAP, min_area=1e-5):
    """meshes: [(label, [(normal, verts), ...])] in world space. Returns [(a, b, overlap m2)] for same-facing
    faces of different labels within `gap` of each other that overlap; back-to-back faces are ignored."""
    buckets = {}
    for pi, (_, tris) in enumerate(meshes):
        for n, vs in tris:
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
    for tris in buckets.values():
        tris.sort(key=lambda t: t[0])
        for i, (d1, p1, n1, v1) in enumerate(tris):
            for d2, p2, n2, v2 in tris[i + 1:]:
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


def zfight_report(objects, limit=40):
    """Checks every builder part of the given objects against each other (also across objects)."""
    bpy.context.view_layer.update()       # freshly placed objects have no world matrix until the depsgraph runs
    groups = {}
    for o in objects:
        me = o.data
        attr = me.attributes.get(PART_ATTR)
        me.calc_loop_triangles()
        mw = o.matrix_world
        for lt in me.loop_triangles:
            if lt.area < 1e-9:
                continue
            pid = attr.data[lt.polygon_index].value if attr else 0
            vs = [mw @ me.vertices[i].co for i in lt.vertices]
            n = (vs[1] - vs[0]).cross(vs[2] - vs[0]).normalized()
            groups.setdefault(f"{o.name}#{pid}", []).append((n, vs))
    pairs = zfight_pairs(list(groups.items()))
    for a, b, s in pairs[:limit]:
        print(f"Z-FIGHT {a} / {b}: {s * 1e4:.1f} cm2")
    print(f"z-fight check: {len(objects)} objects, {len(pairs)} pairs")
    return pairs


# ---------------------------------------------------------------- art part: one exportable mesh with material slots

class Part:
    """Accumulates geometry (venue coordinates in, Blender coordinates stored) into one mesh object."""

    def __init__(self, name, coll):
        self.name, self.coll = name, coll
        old = bpy.data.objects.get(name)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        self.bm = bmesh.new()
        self.pid_layer = self.bm.faces.layers.int.new(PART_ATTR)
        self.sm_layer = self.bm.faces.layers.int.new(SMOOTH_ATTR)
        self.pid = 0
        self.mats = []
        self.smooth = False      # faces added while True are smooth-shaded (organic pieces); default flat

    def _set(self, faces, mat, new_part=True):
        if new_part:
            self.pid += 1
        if mat not in self.mats:
            self.mats.append(mat)
        i = self.mats.index(mat)
        for f in faces:
            f.material_index = i
            f[self.pid_layer] = self.pid
            f[self.sm_layer] = int(self.smooth)
        return faces

    def lathe(self, cx, cz, profile, mat, segs=24, squash=1.0, folds=0, fold_amp=0.0):
        """Surface of revolution around venue (cx, cz): profile = [(h, r)] bottom to top, capped.
        squash scales the z axis (oval section); folds/fold_amp ripple the radius (drapery), stronger low down."""
        bm, rings = self.bm, []
        h_lo, h_hi = profile[0][0], profile[-1][0]
        for h, r in profile:
            low = (h_hi - h) / (h_hi - h_lo) if h_hi > h_lo else 0.0
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                rr = r * (1 + fold_amp * low * math.sin(folds * a)) if folds else r
                ring.append(bm.verts.new(bl(cx + rr * math.cos(a), cz + rr * squash * math.sin(a), h)))
            rings.append(ring)
        faces = [bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
                 for a, b in zip(rings, rings[1:]) for k in range(segs)]
        faces += [bm.faces.new(list(reversed(rings[0]))), bm.faces.new(rings[-1])]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._set(faces, mat)

    def skin(self, pts, edges, radii, mat, levels=2):
        """Organic tube body from a skeleton (Skin + Subdivision modifiers, evaluated and baked in).
        pts: Blender-space points, edges: index pairs, radii: (rx, ry) per point; point 0 is the root."""
        me = bpy.data.meshes.new("zm_tmp_skin")
        me.from_pydata([tuple(p) for p in pts], edges, [])
        o = bpy.data.objects.new("zm_tmp_skin", me)
        bpy.context.scene.collection.objects.link(o)
        o.modifiers.new("Skin", "SKIN")
        if not me.skin_vertices:
            me.skin_vertices.new()
        sv = me.skin_vertices[0].data
        for i, r in enumerate(radii):
            sv[i].radius = r
        sv[0].use_root = True
        sub = o.modifiers.new("Sub", "SUBSURF")
        sub.levels = sub.render_levels = levels
        dg = bpy.context.evaluated_depsgraph_get()
        ev = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        vs = [self.bm.verts.new(v.co) for v in ev.vertices]
        faces = [self.bm.faces.new([vs[i] for i in p.vertices]) for p in ev.polygons]
        bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
        bpy.data.meshes.remove(ev)
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._set(faces, mat)

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

    def box(self, x0, z0, x1, z1, h0, h1, mat):
        return self.prism([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], h0, h1, mat)

    def obox(self, center, size, mat, rot=(0, 0, 0)):
        """Oriented box: center in venue (x, z, h), size (sx along venue x, sz along venue z, sh),
        rot = Blender XYZ euler degrees applied about the center."""
        from mathutils import Matrix, Euler
        x, z, h = center
        m = (Matrix.Translation(bl(x, z, h)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
             @ Matrix.Diagonal((size[0], size[1], size[2], 1)))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        faces = list({f for v in r["verts"] for f in v.link_faces})
        return self._set(faces, mat)

    def wall(self, a, b, h0, h1, out, inn=None, edge=None, t=WALL_T, side=0, openings=()):
        """Wall with openings; `out` paints the face on the right of a->b, `inn` the left, `edge` caps/reveals."""
        faces, n = _wall_pieces(self.bm, a, b, h0, h1, t, side, openings)
        self.pid += 1
        for f in faces:
            dot = f.normal.dot(n)
            self._set([f], out if dot > 0.5 else (inn or out) if dot < -0.5 else (edge or inn or out), new_part=False)
        return faces

    def cyl(self, x, z, h0, h1, r, mat, segs=12, r1=None):
        from mathutils import Matrix
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r1 is None else r1,
                                  depth=h1 - h0, matrix=Matrix.Translation(bl(x, z, (h0 + h1) / 2)))
        return self._set(list({f for v in r["verts"] for f in v.link_faces}), mat)

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

    def slab(self, pts, vec, mat):
        """Extrudes a convex Blender-space polygon along `vec` (arch strips, profiles in a wall plane)."""
        bm = self.bm
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        r = bmesh.ops.extrude_face_region(bm, geom=[f])
        top = [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=Vector(vec), verts=top)
        faces = list({f} | {lf for v in top for lf in v.link_faces})
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._set(faces, mat)

    def sphere(self, center, r, mat, segs=12, rings=8, scale=(1, 1, 1)):
        """UV sphere around a Blender-space centre (scale = Blender-axis stretch, for ovals)."""
        from mathutils import Matrix
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=r, matrix=m)
        return self._set(list({f for v in res["verts"] for f in v.link_faces}), mat)

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

    def finish(self, flat=True):
        bm = self.bm
        uvl = bm.loops.layers.uv.verify()
        tiles = [tile_of(k) for k in self.mats]
        for f in bm.faces:
            ax = max(range(3), key=lambda i: abs(f.normal[i]))
            Tu, Tv, ou, ov = tiles[f.material_index]
            for lp in f.loops:
                c = lp.vert.co
                a, b = (c.x, c.y) if ax == 2 else ((c.y, c.z) if ax == 0 else (c.x, c.z))
                lp[uvl].uv = (a / Tu + ou, b / Tv + ov)          # box projection in tile units
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        sm = me.attributes[SMOOTH_ATTR].data
        for p in me.polygons:
            p.use_smooth = bool(sm[p.index].value) or not flat
        me.attributes.remove(me.attributes[SMOOTH_ATTR])
        for k in self.mats:
            me.materials.append(material(k))
        o = bpy.data.objects.new(self.name, me)
        self.coll.objects.link(o)
        return o


def tree(part, x, z, height, seed, mat="Bark", h=0.0, leaves=False):
    """Gnarled tree: recursive tapered branches (low poly); leaves=True adds dark foliage clumps (irregular convex
    blobs: random facet angles, so overlapping clumps never share a plane) at the outer branches."""
    import random
    rnd = random.Random(seed)

    def grow(p, d, length, r, depth):
        pts, rad, cur = [p], [r], p.copy()
        for k in range(3):
            d = (d + Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), rnd.uniform(-0.05, 0.15)))).normalized()
            cur = cur + d * (length / 3)
            pts.append(cur.copy())
            rad.append(r * (1 - 0.22 * (k + 1)))
        part.pipe(pts, rad, mat, segs=6 if depth < 2 else 5)
        if leaves and depth >= 2:
            R = height * rnd.uniform(0.07, 0.11)
            blob = []
            for _ in range(14):
                v = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 0.8))).normalized()
                blob.append(cur + v * R * rnd.uniform(0.75, 1.1))
            part.smooth = True
            part.hull(blob, "Foliage")
            part.smooth = False
        if depth >= 4 or r < 0.015:
            return
        for _ in range(rnd.choice((2, 2, 3))):
            nd = (d + Vector((rnd.uniform(-0.9, 0.9), rnd.uniform(-0.9, 0.9), rnd.uniform(-0.1, 0.5)))).normalized()
            grow(cur, nd, length * rnd.uniform(0.55, 0.75), rad[-1] * 0.85, depth + 1)

    base = bl(x, z, h - 0.1)
    grow(base, Vector((0, 0, 1)), height * 0.45, 0.06 * height ** 0.8, 0)
