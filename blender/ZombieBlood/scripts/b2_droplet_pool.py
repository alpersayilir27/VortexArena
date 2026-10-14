# Single droplet sprite (stretched-billboard particle) -> T_ZB_BloodDrop.png
# Top-down floor puddle (death pool decal) -> T_ZB_BloodPool.png
import importlib
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zb_common as zb

importlib.reload(zb)


def droplet():
    scene = zb.reset_scene()
    zb.ortho_camera(scene, 0.5, 128, 128)
    zb.key_light(scene)
    mb, _ = zb.metaball("Drop", zb.blood_material(), 0.005)
    zb.add_ball(mb, (0.0, 0.0, 0.0), 0.16)
    zb.add_ball(mb, (0.0, -0.07, 0.0), 0.1)
    print(zb.write_png(zb.render_pixels(scene), "T_ZB_BloodDrop"))


def pool():
    scene = zb.reset_scene()
    zb.ortho_camera(scene, 2.0, 512, 512)
    zb.key_light(scene)
    mat = zb.blood_material()
    # Seen top-down the puddle catches more light than a spray; darker base keeps it from reading pink.
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (0.045, 0.002, 0.002, 1.0)
    mb, _ = zb.metaball("Pool", mat, 0.01)
    r = zb.rng(21)
    flat = (1.0, 1.0, 0.18)
    zb.add_ball(mb, (0.0, 0.0, 0.0), 0.36, flat)
    for i in range(20):
        a = r.uniform(0.0, math.tau)
        d = r.uniform(0.12, 0.5)
        zb.add_ball(mb, (math.cos(a) * d, math.sin(a) * d, 0.0), r.uniform(0.08, 0.22), flat)
    # Loose splatter around the rim: drops with short streaks pointing away from the centre.
    for i in range(26):
        a = r.uniform(0.0, math.tau)
        d = r.uniform(0.52, 0.72)
        rad = r.uniform(0.09, 0.15)
        el = zb.add_ball(mb, (math.cos(a) * d, math.sin(a) * d, 0.0), rad, (1.6, 0.8, 0.25))
        el.rotation = (math.cos(a * 0.5), 0.0, 0.0, math.sin(a * 0.5))
    print(zb.write_png(zb.render_pixels(scene), "T_ZB_BloodPool"))


droplet()
pool()
