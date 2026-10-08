# FX textures for the grill cooking effect (BurgerGrill's Smoke children): cartoon flame tongue,
# soft round spark and a puffy smoke blob. White/yellow-orange RGB with the shape in alpha; particle
# colour-over-lifetime tints them in Unity. Written straight into KitchenKit/Textures.
import importlib, os, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bpy
import numpy as np
import bk_common
importlib.reload(bk_common)


def _save(name, rgba):
    h, w, _ = rgba.shape
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels = rgba[::-1].ravel()          # numpy rows are top-first, Blender bottom-first
    img.filepath_raw = os.path.join(bk_common.KIT, "Textures", name + ".png")
    img.file_format = "PNG"
    img.save()


def flame(S=256):
    """Teardrop flame: round bottom, pointed wavy tip; yellow core fading to orange edge."""
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S
    y = 1.0 - yy                                  # 0 bottom, 1 top
    x = xx - 0.5
    width = 0.34 * np.sqrt(np.clip(1 - y, 0, 1)) * np.clip(y * 3.5, 0, 1) + 1e-4
    x = x - 0.05 * np.sin(y * 7.0) * y           # a little lick
    d = np.abs(x) / width
    a = np.clip(1 - d, 0, 1) ** 0.6 * np.clip((0.97 - y) * 6, 0, 1)
    core = np.clip(1 - d * 1.6, 0, 1) * np.clip(1 - y * 1.2, 0, 1)
    r = np.ones_like(a)
    g = 0.55 + 0.45 * core
    bl = 0.15 + 0.6 * core
    return np.dstack([r, g, bl, a]).astype(np.float32)


def spark(S=64):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / (S - 1) - 0.5
    d = np.sqrt(xx ** 2 + yy ** 2) * 2
    a = np.clip(1 - d, 0, 1) ** 1.8
    return np.dstack([np.ones_like(a), np.ones_like(a) * 0.85, np.ones_like(a) * 0.55, a]).astype(np.float32)


def smoke(S=128):
    rng = np.random.default_rng(5)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32) / S
    a = np.zeros((S, S), np.float32)
    for cx, cy, r in [(0.5, 0.55, 0.28), (0.33, 0.45, 0.2), (0.67, 0.45, 0.2), (0.45, 0.32, 0.18), (0.6, 0.68, 0.17)]:
        a = np.maximum(a, np.clip(1 - np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r, 0, 1))
    a = np.clip(a * 1.6, 0, 1) ** 0.8 * (0.9 + 0.1 * rng.random((S, S), dtype=np.float32))
    return np.dstack([np.ones_like(a), np.ones_like(a), np.ones_like(a), a]).astype(np.float32)


if __name__ == "__main__":
    _save("BK_FxFlame", flame())
    _save("BK_FxSpark", spark())
    _save("BK_FxSmoke", smoke())
    print("fx textures written")
