# K hanging ketchup (plan/burger-ketcap.md): K1 squeeze bottle, K2 ceiling reel, and the stain
# textures (surface splat + two visor-edge variants). Textures are white with the stain in alpha;
# colour comes from code (_BaseColor).
# K1: built upright (nozzle +Z); the prefab's visual child turns it so the nozzle is local +Z.
# K2: pivot = ceiling contact point, drum hangs below it; its lowest point is the cord anchor.
import importlib, math, os, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bpy
import bk_common
importlib.reload(bk_common)

BOTTLE_HALF = 0.20      # bottle spans z -0.20..0.20 around its grip centre
REEL_DROP = 0.62        # ceiling to the bottom of the drum (cord anchor)


def bottle(name):
    b = bk_common.Builder(name)
    z0 = -BOTTLE_HALF
    body = [(0, z0), (0.052, z0), (0.062, z0 + 0.012), (0.066, z0 + 0.05), (0.062, z0 + 0.13), (0.055, z0 + 0.16),
            (0.062, z0 + 0.19), (0.066, z0 + 0.25), (0.06, z0 + 0.285), (0.045, z0 + 0.3), (0.032, z0 + 0.305), (0, z0 + 0.305)]
    b.lathe("Body", body, (0, 0), "red", 24, scale_y=0.82)
    cap = [(0, z0 + 0.298), (0.038, z0 + 0.298), (0.04, z0 + 0.33), (0.03, z0 + 0.34), (0.014, z0 + 0.345),
           (0.009, z0 + 0.385), (0.004, z0 + 0.4), (0, z0 + 0.4)]
    b.lathe("Cap", cap, (0, 0), "yellow", 16)
    b.lathe("Ring", [(0.012, z0 - 0.03), (0.022, z0 - 0.03), (0.022, z0 + 0.004), (0.012, z0 + 0.004)], (0, 0), "steel", 12)
    return b.bake()


def reel(name):
    b = bk_common.Builder(name)
    b.cyl("Pipe", 0.03, REEL_DROP - 0.26, (0, 0, -(REEL_DROP - 0.26) / 2), "Z", "steel_dark", 12)
    b.cyl("Flange", 0.07, 0.02, (0, 0, -0.01), "Z", "steel", 16, 0.005)
    dz = -REEL_DROP + 0.15
    b.cyl("Drum", 0.15, 0.12, (0, 0, dz), "Y", "red", 28, 0.025, 2)
    for i, y in enumerate([-0.063, 0.063]):
        b.cyl(f"Face_{i}", 0.105, 0.006, (0, y, dz), "Y", "cream", 24)
        b.cyl(f"Hub_{i}", 0.03, 0.012, (0, y * 1.06, dz), "Y", "steel", 12)
    b.box("Spout", (0.05, 0.05, 0.05), (0, 0), -REEL_DROP, "steel_dark", 0.01, 1)
    return b.bake()


PUDDLE_BASE = -0.015    # sauce layer box is 9 x 3 x 9 cm centred on the pivot; bottom = layer below


def puddle(name):
    """Look of the `sauce` layer (NO_sauce): thin pool + squeezed zigzag line on top, drips over the
    edge of the layer below. Metaballs so the sauce blends into one gooey surface."""
    import random
    from mathutils import Euler
    b = bk_common.Builder(name)
    rng = random.Random(5)
    base, top = PUDDLE_BASE, PUDDLE_BASE + 0.007
    mb = bpy.data.metaballs.new(name + "_mb")
    mb.resolution = mb.render_resolution = 0.0022
    mb.threshold = 0.6
    z = (base + top) / 2
    # pool: centre + inner ring + wavy outer ring (inner ring closes holes in the thin sheet)
    e = mb.elements.new(type="ELLIPSOID"); e.co = (0, 0, z); e.radius = 0.04; e.size_z = 0.1
    for ring, count, rad in ((0.45, 10, 0.02), (0.82, 24, 0.016)):
        for i in range(count):
            a = i / count * 2 * math.pi
            rr = 0.047 * (0.86 + 0.07 * math.sin(5 * a + 0.7) + 0.03 * rng.random())
            e = mb.elements.new(type="ELLIPSOID"); e.co = (rr * ring * math.cos(a), rr * ring * math.sin(a), z)
            e.radius = rad; e.size_z = 0.16
    # drips hanging over the edge: lip, stem, drop
    for i in range(6):
        a = (i + rng.uniform(-0.3, 0.3)) / 6 * 2 * math.pi
        L, rad = rng.uniform(0.012, 0.026), rng.uniform(0.0055, 0.0075)
        x, y = 0.046 * math.cos(a), 0.046 * math.sin(a)
        e = mb.elements.new(type="BALL"); e.co = (x * 0.97, y * 0.97, base + 0.003); e.radius = rad * 1.6
        e = mb.elements.new(type="CAPSULE"); e.co = (x, y, base + 0.002 - L / 2); e.radius = rad; e.size_x = L / 2
        e.rotation = Euler((0, math.pi / 2, 0)).to_quaternion()
        e = mb.elements.new(type="BALL"); e.co = (x, y, base + 0.001 - L); e.radius = rad * 1.45
    # squeezed zigzag line
    pts = [(-0.036, -0.024), (-0.022, 0.03), (-0.006, -0.033), (0.01, 0.033), (0.026, -0.03), (0.038, 0.02)]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        for k in range(10):
            t = k / 10
            # line top ~ box top (+0.015): the next layer sits on it without a gap
            e = mb.elements.new(type="BALL"); e.co = (x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, base + 0.019); e.radius = 0.0115
    mo = bpy.data.objects.new(name + "_mb", mb)
    b.src.objects.link(mo)
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(mo.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(mo, do_unlink=True)
    o = bpy.data.objects.new("Sauce", me)
    b.src.objects.link(o)
    for p in me.polygons:
        p.use_smooth = True
    d = o.modifiers.new("Decimate", "DECIMATE"); d.ratio = 0.5    # metaball surface is denser than needed
    o["bk_color"] = "red"     # palette UV only; Unity draws it with the glossy M_BK_Ketchup
    b.parts.append(o)
    return b.bake()


def _save_alpha(name, alpha):
    import numpy as np
    h, w = alpha.shape
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., 3] = np.clip(alpha, 0, 1)
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels = px.ravel()
    img.filepath_raw = os.path.join(bk_common.KIT, "Textures", name + ".png")
    img.file_format = "PNG"
    img.save()


def _blobs(size, centers, seed, soft=0.08):
    import numpy as np
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) / size
    field = np.zeros((size, size), dtype=np.float32)
    for cx, cy, r in centers:
        field += (r * r) / ((xx - cx) ** 2 + (yy - cy) ** 2 + 1e-4)
    field += rng.normal(0, 0.06, field.shape).astype(np.float32)
    return np.clip((field - 1.0) / soft, 0, 1)


def textures():
    import numpy as np
    rng = np.random.default_rng(7)
    # surface splat: a fat centre blob, satellite drops, a few streaks
    c = [(0.5, 0.5, 0.17)]
    for _ in range(14):
        a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.18, 0.42)
        c.append((0.5 + d * math.cos(a), 0.5 + d * math.sin(a), rng.uniform(0.018, 0.05)))
    _save_alpha("BK_KetchupSplat", _blobs(256, c, 1, 0.3))
    # visor variants: stains hugging the edges (ring mask in the shader keeps the centre clear)
    for v in range(2):
        rngv = np.random.default_rng(20 + v)
        cs = []
        for _ in range(26):
            a = rngv.uniform(0, 2 * math.pi)
            d = rngv.uniform(0.33, 0.48)
            cs.append((0.5 + d * math.cos(a), 0.5 + d * math.sin(a), rngv.uniform(0.03, 0.08)))
        _save_alpha(f"BK_KetchupVision_{v}", _blobs(512, cs, 30 + v, 0.25))


BUILDS = [("K1_KetchupBottle", bottle), ("K2_KetchupReel", reel), ("K3_KetchupPuddle", puddle)]

if __name__ == "__main__":
    textures()
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
