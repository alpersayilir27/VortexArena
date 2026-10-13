"""Part 6: nature outside the fence — a fir tree line closing the horizon (impostor cards: CC0 `fir_sapling`
rendered once into an albedo + alpha atlas), CC0 mossy rocks, stumps, ferns and dry branches between the grave
rows. Paths, spawn clearings and the landmark corners stay free.

The atlas is a derived file in asset_cache/_impostor/ (not in git); delete it to re-render. Card normals point up
so both sides light the same; GY_FirCard is two-sided (p9 exports the flag)."""
import importlib
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)
import gy_assets as ga
importlib.reload(ga)

L = gy.Layout()
coll = gy.collection("GY_P6_Nature")
gy.clear_collection(coll)
rnd = random.Random(31)
ends = [b for _, b, _ in L.paths]
spots = [Vector(p) for p in gy.landmark_spots(L)]
GRAVE_ROWS = (1.9, 4.2, 6.5, 8.8, 11.1)          # p3 ROWS: props sit between them, not on a grave

IMP_DIR = os.path.join(ga.CACHE, "_impostor")
ATLAS, META = os.path.join(IMP_DIR, "fir_atlas.png"), os.path.join(IMP_DIR, "fir_atlas.json")
CELL_W, CELL_H = 512, 1024                      # two trees side by side -> 1024 x 1024


def free(p, path=2.0, spawn=3.5, land=4.0):
    return (L.path_dist(p) > path and all((p - e).length > spawn for e in ends)
            and all((p - s).length > land for s in spots))


def between_rows(p, half=0.4):
    fd = L.fence_dist(p)
    return fd > GRAVE_ROWS[-1] + 1.2 or any(abs(fd - (a + b) / 2) < half for a, b in zip(GRAVE_ROWS, GRAVE_ROWS[1:]))


# ---------------------------------------------------------------- fir impostor atlas
def render_atlas(trees):
    sc = bpy.data.scenes.new("GY_Impostor")
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 96
    sc.cycles.use_denoising = False
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x, sc.render.resolution_y = CELL_W, CELL_H
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    w = bpy.data.worlds.new("GY_ImpostorWorld")
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (1, 1, 1, 1)                  # even sky light: baked-in self shadowing only
    bg.inputs["Strength"].default_value = 1.0
    sc.world = w
    cam = bpy.data.objects.new("GY_ImpostorCam", bpy.data.cameras.new("GY_ImpostorCam"))
    cam.data.type = "ORTHO"
    cam.rotation_euler = (math.pi / 2, 0, 0)                         # looks along +Y
    sc.collection.objects.link(cam)
    sc.camera = cam
    cells, tiles = [], []
    for k, t in enumerate(trees):
        # the sapling is sparse: three turned, slightly scaled copies make one full crown
        copies = []
        for j, (ang, s) in enumerate(((0, 1.0), (125, 0.93), (245, 0.86))):
            o = bpy.data.objects.new(f"GY_ImpostorTree{k}_{j}", t.data)
            o.rotation_euler = (0, 0, math.radians(ang))
            o.scale = (s, s, s)
            sc.collection.objects.link(o)
            copies.append(o)
        h = ga.size(t).z
        wdt = 2 * max(math.hypot(v.co.x, v.co.y) for v in t.data.vertices)      # turned copies: radius, not box
        frame = max(h, wdt * CELL_H / CELL_W) * 1.03
        cam.data.ortho_scale = frame
        cam.location = (0, -20, frame / 2 - 0.01)
        sc.render.filepath = os.path.join(IMP_DIR, f"_cell{k}.png")
        with bpy.context.temp_override(scene=sc):
            bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(sc.render.filepath)
        tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(CELL_H, CELL_W, 4))
        bpy.data.images.remove(img)
        os.remove(sc.render.filepath)
        for o in copies:
            bpy.data.objects.remove(o)
        cells.append({"u0": k / len(trees), "u1": (k + 1) / len(trees), "frame": frame, "tree": h})
    atlas = np.concatenate(tiles, axis=1)                                # loaded PNG = straight alpha already
    rgb, a = atlas[..., :3], atlas[..., 3]

    def shift(x, dy, dx):                                                # no wrap-around (np.roll would)
        out = np.zeros_like(x)
        H, W = x.shape[:2]
        out[max(dy, 0):H + min(dy, 0), max(dx, 0):W + min(dx, 0)] = x[max(-dy, 0):H + min(-dy, 0), max(-dx, 0):W + min(-dx, 0)]
        return out

    known = a > 0.5
    for _ in range(24):                                                  # bleed opaque colour outward (mips)
        acc, cnt = np.zeros_like(rgb), np.zeros(a.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            m = shift(known, dy, dx)
            acc += shift(rgb, dy, dx) * m[..., None]
            cnt += m
        fill = ~known & (cnt > 0)
        if not fill.any():
            break
        rgb[fill] = acc[fill] / cnt[fill][:, None]
        known |= fill
    out = bpy.data.images.new("gy_fir_atlas", atlas.shape[1], atlas.shape[0], alpha=True)
    out.pixels.foreach_set(np.clip(atlas, 0, 1).ravel())
    out.filepath_raw, out.file_format = ATLAS, "PNG"
    out.save()
    bpy.data.images.remove(out)
    bpy.data.scenes.remove(sc)
    bpy.data.objects.remove(cam)
    bpy.data.worlds.remove(w)
    with open(META, "w", encoding="utf-8") as f:
        json.dump({"source": "polyhaven:fir_sapling", "cells": cells}, f, indent=2)


if not (os.path.exists(ATLAS) and os.path.exists(META)):
    os.makedirs(IMP_DIR, exist_ok=True)
    render_atlas(ga.load("fir_sapling", "FirSrc")[:2])
with open(META, encoding="utf-8") as f:
    cells = json.load(f)["cells"]


def fir_material():
    m = bpy.data.materials.get("GY_FirCard") or bpy.data.materials.new("GY_FirCard")
    m.use_nodes = True
    nt = m.node_tree
    for n in [n for n in nt.nodes if n.type not in ("BSDF_PRINCIPLED", "OUTPUT_MATERIAL")]:
        nt.nodes.remove(n)
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(ATLAS, check_existing=True)
    tex.image.alpha_mode = "STRAIGHT"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 1.0
    m["gy_two_sided"] = True
    m["gy_src"] = "polyhaven:fir_sapling (impostor)"
    return m


firs = bmesh.new()
uvl = firs.loops.layers.uv.new("UVMap")


def fir(x, z, height, cell, yaw):
    c = cells[cell]
    s = height / c["tree"]
    hgt, half = c["frame"] * s, c["frame"] * s * CELL_W / CELL_H / 2
    h0 = L.ground_h(x, z) - 0.15
    base = gy.bl(x, z, h0)
    for k in range(3):                                       # three crossed cards through the trunk
        a = yaw + k * math.pi / 3
        e = Vector((math.cos(a), math.sin(a), 0)) * half
        vs = [firs.verts.new(base + q) for q in (-e, e, e + Vector((0, 0, hgt)), -e + Vector((0, 0, hgt)))]
        f = firs.faces.new(vs)
        for lp, uv in zip(f.loops, ((c["u0"], 0), (c["u1"], 0), (c["u1"], 1), (c["u0"], 1))):
            lp[uvl].uv = uv


n_line = 46
for k in range(n_line):                                      # the closing ring
    p = gy.ring_point(L, rnd.uniform(19.0, 27.0), 2 * math.pi * (k + rnd.uniform(-0.35, 0.35)) / n_line)
    fir(p.x, p.y, rnd.uniform(8.0, 12.5), rnd.randrange(len(cells)), rnd.uniform(0, math.pi))
placed = 0
for k in range(60):                                          # a few nearer ones break the ring's regularity
    if placed >= 12:
        break
    p = gy.ring_point(L, rnd.uniform(13.0, 18.0), rnd.uniform(0, 2 * math.pi))
    if not free(p, path=3.0):
        continue
    fir(p.x, p.y, rnd.uniform(6.0, 9.0), rnd.randrange(len(cells)), rnd.uniform(0, math.pi))
    placed += 1
me = bpy.data.meshes.new("GY_Firs")
firs.to_mesh(me)
firs.free()
me.materials.append(fir_material())
me.normals_split_custom_set([(0.0, 0.0, 1.0)] * len(me.loops))    # top-lit from both sides
fo = bpy.data.objects.new("GY_Firs", me)
coll.objects.link(fo)


# ---------------------------------------------------------------- scattered CC0 props
def scatter(name, pieces, count, dist, scale, sink=0.1, tilt=0.0, test=None, tries=400):
    items, used = [], []
    for _ in range(tries):
        if len(items) >= count:
            break
        p = gy.ring_point(L, rnd.uniform(*dist), rnd.uniform(0, 2 * math.pi))
        if not free(p) or (test and not test(p)) or any((p - q).length < r for q, r in used):
            continue
        src = rnd.choice(pieces)
        s = rnd.uniform(*scale)
        sz = ga.size(src)
        r = max(sz.x, sz.y) * s / 2
        h = L.ground_h(p.x, p.y) - sz.z * s * sink
        items.append((src, ga.place(src, p.x, p.y, h, rnd.uniform(0, 360), s,
                                    (rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt)))))
        used.append((p, r + 0.3))
    ga.merge(name, coll, items)
    return len(items)


rocks = ga.load("rock_moss_set_01", "RockMossA", tris=600) + ga.load("rock_moss_set_02", "RockMossB", tris=600)
small = [r for r in rocks if ga.size(r).z < 1.0]
big = [r for r in rocks if ga.size(r).z >= 1.0]
counts = {"GY_Rocks": scatter("GY_Rocks", small, 14, (1.6, 15.0), (0.45, 0.8), test=between_rows)}
counts["GY_Boulders"] = scatter("GY_Boulders", big, 6, (12.5, 18.0), (0.5, 0.85), sink=0.2)
counts["GY_Stumps"] = scatter("GY_Stumps", ga.load("tree_stump_01", "Stump", tris=1500), 4, (5.0, 14.0), (0.6, 0.9),
                              sink=0.05, test=between_rows)
counts["GY_Ferns"] = scatter("GY_Ferns", ga.load("fern_02", "Fern", tris=700), 28, (0.9, 13.0), (0.8, 1.3),
                             sink=0.02, test=lambda p: L.fence_dist(p) < 1.4 or between_rows(p, 0.55))
counts["GY_Branches"] = scatter("GY_Branches", ga.load("dry_branches_medium_01", "Branches", tris=500), 12,
                                (1.2, 12.0), (0.9, 1.4), sink=0.1, tilt=6.0, test=between_rows)
for m in bpy.data.materials:
    if m.name.startswith("GY_A_Fern"):
        m["gy_two_sided"] = True                            # single-sheet fronds
print("nature:", {o.name: gy.tris(o) for o in coll.objects}, counts)
