# 4x4 flipbook of a blood burst (camera-facing particle in Unity) -> T_ZB_BloodSpray_4x4.png.
# Metaball droplets fly out of a core blob, stretch along their path and thin out over time.
import importlib
import math
import os
import sys

import numpy as np
from mathutils import Quaternion

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zb_common as zb

importlib.reload(zb)

FRAMES = 16
GRID = 4
CELL = 256
SEED = 7


def build_frame(mat, droplets, t):
    mb, _ = zb.metaball("Spray", mat)
    ease = 1.0 - (1.0 - t) ** 2.2
    core = 0.30 * (1.0 - t) ** 1.5 + 0.04
    zb.add_ball(mb, (0.0, 0.0, 0.0), core)
    for ang, speed, size, lag in droplets:
        d = (math.cos(ang), math.sin(ang))
        travel = speed * max(0.0, ease - lag) * 0.85
        r = size * (1.0 - 0.75 * t)
        if r < 0.012 or travel <= 0.0:
            continue
        stretch = 1.0 + 2.4 * (1.0 - t) * speed
        rot = Quaternion((0.0, 0.0, 1.0), ang)
        el = zb.add_ball(mb, (d[0] * travel, d[1] * travel, 0.0), r, (stretch, 1.0, 1.0))
        el.rotation = rot
        if t < 0.55:
            tail = travel * 0.62
            el2 = zb.add_ball(mb, (d[0] * tail, d[1] * tail, 0.0), r * 0.7, (stretch * 0.8, 0.8, 0.8))
            el2.rotation = rot


def main():
    scene = zb.reset_scene()
    zb.ortho_camera(scene, 2.0, CELL, CELL)
    zb.key_light(scene)
    mat = zb.blood_material()
    r = zb.rng(SEED)
    droplets = []
    for i in range(26):
        # Burst biased upward (+Y) so the sheet reads as a spray, not a star.
        ang = math.pi * 0.5 + r.gauss(0.0, 0.95)
        droplets.append((ang, r.uniform(0.35, 1.0), r.uniform(0.035, 0.12), r.uniform(0.0, 0.12)))
    sheet = np.zeros((CELL * GRID, CELL * GRID, 4), dtype=np.float32)
    for f in range(FRAMES):
        for obj in list(scene.objects):
            if obj.type == "META":
                zb.bpy.data.objects.remove(obj, do_unlink=True)
        # Skip the bare sphere at t=0; frame 0 already shows the burst opening.
        build_frame(mat, droplets, 0.08 + 0.92 * f / (FRAMES - 1))
        px = zb.render_pixels(scene)
        col, row = f % GRID, f // GRID
        # Unity flipbooks read top-left first; image rows run bottom-up.
        y0 = (GRID - 1 - row) * CELL
        sheet[y0:y0 + CELL, col * CELL:(col + 1) * CELL] = px
    print(zb.write_png(sheet, "T_ZB_BloodSpray_4x4"))


main()
