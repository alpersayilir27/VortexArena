# Tiling textures for the Japan props kit: a photo/paint source from ref/tex becomes a seamless
# 1024 albedo plus a normal map derived from its luminance. jp_common.export_fbx runs this once
# per script run; jp_common.TILES holds the source file, meters per repeat and normal strength.
import os, sys
SCRIPTS = os.path.dirname(os.path.abspath(__file__)) if "__file__" in dir() else \
    r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bpy
import numpy as np
import jp_common   # not reloaded: jp_common calls back into this module and would reset its state

SIZE = 1024
SEAM_BAND = 0.45      # fraction of the half-width that cross-fades into the offset copy
NRM_GAIN = 12.0       # turns per-pixel height deltas into a usable slope before `strength`
LUMA = np.array([0.299, 0.587, 0.114], dtype=np.float32)


def _load_rgb(path):
    """Loads an image, center-crops it square and resizes to SIZE. Returns (SIZE, SIZE, 3), top row first."""
    src = bpy.data.images.load(path, check_existing=False)
    w, h = src.size
    px = np.array(src.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
    bpy.data.images.remove(src)
    s = min(w, h)
    x0, y0 = (w - s) // 2, (h - s) // 2
    crop = px[y0:y0 + s, x0:x0 + s].copy()
    tmp = bpy.data.images.new("_jp_tile_tmp", s, s, alpha=True)
    tmp.colorspace_settings.name = "sRGB"
    tmp.pixels = crop[::-1].ravel()
    tmp.scale(SIZE, SIZE)
    out = np.array(tmp.pixels[:], dtype=np.float32).reshape(SIZE, SIZE, 4)[::-1, :, :3].copy()
    bpy.data.images.remove(tmp)
    return out


def _seamless(rgb):
    """Cross-fades a half-offset copy over the borders: the original edges (the seam) end up hidden
    in the center of the offset copy, which is itself continuous across the wrap."""
    s = rgb.shape[0]
    rolled = np.roll(np.roll(rgb, s // 2, axis=0), s // 2, axis=1)
    i = np.arange(s, dtype=np.float32)
    d = np.minimum(i, s - 1 - i) / (s / 2)              # 0 at the border, 1 in the center
    dm = np.minimum(d[:, None], d[None, :])
    t = np.clip((SEAM_BAND - dm) / SEAM_BAND, 0, 1)
    m = (t * t * (3 - 2 * t))[..., None]                # smoothstep: a hard mask is a seam itself
    return rgb * (1 - m) + rolled * m


def _blur(g, r=2, passes=2):
    """Wrapping box blur (np.roll), so the height field stays seamless."""
    for _ in range(passes):
        for ax in (0, 1):
            acc = np.zeros_like(g)
            for d in range(-r, r + 1):
                acc += np.roll(g, d, axis=ax)
            g = acc / (2 * r + 1)
    return g


def _normal(rgb, strength):
    """Sobel normal from the blurred luminance, OpenGL convention (green points up)."""
    hgt = _blur(rgb @ LUMA)

    def at(dx, dy):
        return np.roll(np.roll(hgt, -dy, axis=0), -dx, axis=1)

    gx = (at(1, -1) + 2 * at(1, 0) + at(1, 1) - at(-1, -1) - 2 * at(-1, 0) - at(-1, 1)) / 8.0
    grow = (at(-1, 1) + 2 * at(0, 1) + at(1, 1) - at(-1, -1) - 2 * at(0, -1) - at(1, -1)) / 8.0
    k = strength * NRM_GAIN
    # +row is downward in the image but -v in UV space, hence the plus sign on grow
    n = np.dstack([-k * gx, k * grow, np.ones_like(gx)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return (n * 0.5 + 0.5).astype(np.float32)


def _save(name, rgb, colorspace, path):
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    # set before writing pixels: changing it afterwards regenerates the buffer to black
    img.colorspace_settings.name = colorspace
    rgba = np.concatenate([np.clip(rgb, 0.0, 1.0), np.ones((SIZE, SIZE, 1), dtype=np.float32)], axis=2)
    img.pixels = rgba[::-1].ravel()   # numpy rows are top-first, Blender bottom-first
    img.update()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def build_tiles(names=None):
    """Writes T_JP_<name>_A.png + _N.png for every TILES entry (or just `names`).
    A missing source becomes flat grey + flat normal so the material still works."""
    out = {}
    for name in (names or list(jp_common.TILES)):
        src_file, _, strength = jp_common.TILES[name]
        path = os.path.join(jp_common.REF_TEX, src_file)
        if os.path.exists(path):
            alb = _seamless(_load_rgb(path))
            nrm = _normal(alb, strength)
        else:
            print(f"TILE MISSING {name} ({src_file})")
            alb = np.full((SIZE, SIZE, 3), 0.5, dtype=np.float32)
            nrm = np.dstack([np.full((SIZE, SIZE), 0.5, dtype=np.float32),
                             np.full((SIZE, SIZE), 0.5, dtype=np.float32),
                             np.ones((SIZE, SIZE), dtype=np.float32)])
        a = _save(f"T_JP_{name}_A", alb, "sRGB", jp_common.tile_png(name, "A"))
        n = _save(f"T_JP_{name}_N", nrm, "Non-Color", jp_common.tile_png(name, "N"))
        out[name] = (a, n)
        m = bpy.data.materials.get("M_JP_" + name)   # rebind: removed images left the nodes empty
        if m and m.node_tree:
            for node in m.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.label == "ALB":
                    node.image = a
                elif node.type == "TEX_IMAGE" and node.label == "NRM":
                    node.image = n
    return out


if __name__ == "__main__":
    print("tiles written", list(build_tiles()))
