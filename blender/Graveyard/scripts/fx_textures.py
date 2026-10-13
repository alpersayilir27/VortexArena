"""Particle textures for the Graveyard FX (Assets/Maps/Graveyard/FX/, shared by every Graveyard scene):
T_GY_Glow.png — soft radial glow for lantern halos and wisps (additive);
T_GY_Mist.png — tileless cloud puff for the ground mist (alpha blended, horizontal billboards).
Quest renders without depth texture, so the puffs fade to zero alpha at their rim instead of soft particles."""
import os

import bpy
import numpy as np
from mathutils import Vector, noise

REPO = os.path.normpath(os.path.join(os.path.dirname(bpy.data.filepath), "..", ".."))
OUT = os.path.join(REPO, "Assets", "Maps", "Graveyard", "FX")
os.makedirs(OUT, exist_ok=True)


def save(name, rgba):
    h, w, _ = rgba.shape
    im = bpy.data.images.new("gy_fx", w, h, alpha=True)
    im.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    im.filepath_raw, im.file_format = os.path.join(OUT, name), "PNG"
    im.save()
    bpy.data.images.remove(im)


n = 128
y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
r = np.sqrt(x * x + y * y)
glow = np.exp(-(r * 2.6) ** 2) * 0.85 + np.exp(-(r * 7.0) ** 2) * 0.15      # wide falloff + hot core
glow *= np.clip((1 - r) * 4, 0, 1)                                          # exact zero at the rim
save("T_GY_Glow.png", np.dstack([glow, glow, glow, glow]))

n = 256
y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
r = np.sqrt(x * x + y * y)
cloud = np.array([[noise.fractal(Vector((u * 2.2, v * 2.2, 0.7)), 0.6, 2.0, 5) for u in row] for row, v in
                  zip(x, y[:, 0])])
cloud = (cloud - cloud.min()) / (cloud.max() - cloud.min())
alpha = np.clip(cloud * 1.4 - 0.25, 0, 1) * np.clip(1 - r, 0, 1) ** 1.6
save("T_GY_Mist.png", np.dstack([np.ones_like(alpha), np.ones_like(alpha), np.ones_like(alpha), alpha]))
print("fx textures ->", OUT)
