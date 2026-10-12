"""Night look preview in Blender (EEVEE, Rendered viewport): dark sky, dim moonlight, thin fog, and a warm
point light on every glowing surface (lanterns, chandelier shades, embers, statue globes, Tesla spark).
Preview only - nothing here is exported; Unity bakes its own lights at the same spots."""
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm

ART = ["ZM_P1_Courtyard", "ZM_P2_Interior", "ZM_P3_Balcony", "ZM_P4_Upstairs", "ZM_P5_Terrace", "ZM_P6_Exterior",
       "ZM_P7_Layout"]
# glowing material -> (watts per light, colour, cluster radius m)
GLOWS = {"ZM_LampGlow": (60.0, (1.0, 0.62, 0.32), 0.6), "ZM_Shade": (70.0, (1.0, 0.7, 0.42), 0.9),
         "ZM_GlowBlue": (12.0, (0.35, 0.6, 1.0), 0.5), "ZM_GlowRed": (3.0, (1.0, 0.15, 0.1), 0.4),
         # CC0 props: material tagged zm_glow = kind (zm_assets.glow)
         "lamp": (150.0, (1.0, 0.62, 0.32), 0.6), "flame": (6.0, (1.0, 0.6, 0.3), 0.3)}

coll = zm.collection("ZM_PreviewLights")
zm.clear_collection(coll)

scene = bpy.context.scene
try:
    scene.render.engine = "BLENDER_EEVEE"
except TypeError:
    pass
scene.view_settings.view_transform = "AgX"
scene.view_settings.exposure = 0.8

# world: near-black navy sky + thin volumetric haze
w = scene.world or bpy.data.worlds.new("ZM_Night")
scene.world = w
w.use_nodes = True
nt = w.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
out = nt.nodes.new("ShaderNodeOutputWorld")
bg = nt.nodes.new("ShaderNodeBackground")
bg.inputs["Color"].default_value = (0.006, 0.009, 0.018, 1)
bg.inputs["Strength"].default_value = 1.0
nt.links.new(bg.outputs[0], out.inputs["Surface"])
vol = nt.nodes.new("ShaderNodeVolumePrincipled")
vol.inputs["Density"].default_value = 0.006
vol.inputs["Color"].default_value = (0.5, 0.6, 0.8, 1)
nt.links.new(vol.outputs[0], out.inputs["Volume"])

# moonlight
sun = bpy.data.objects.new("ZM_Moon", bpy.data.lights.new("ZM_Moon", "SUN"))
sun.data.energy = 0.35
sun.data.color = (0.55, 0.65, 1.0)
sun.rotation_euler = (0.9, 0.2, 2.4)
coll.objects.link(sun)

# one point light per cluster of glowing faces
bpy.context.view_layer.update()
n_lights = 0
for mat_name, (watts, color, radius) in GLOWS.items():
    pts = []
    for cn in ART:
        for o in bpy.data.collections[cn].objects:
            slots = [i for i, m in enumerate(o.data.materials) if m and mat_name in (m.name, m.get("zm_glow"))]
            if not slots:
                continue
            mw = o.matrix_world
            pts += [mw @ p.center for p in o.data.polygons if p.material_index in slots]
    clusters = []
    for p in pts:
        for c in clusters:
            if (c[0] / c[1] - p).length < radius:
                c[0] += p
                c[1] += 1
                break
        else:
            clusters.append([p.copy(), 1])
    for i, (s, k) in enumerate(clusters):
        lt = bpy.data.lights.new(f"ZM_{mat_name.removeprefix('ZM_')}_{i}", "POINT")
        lt.energy = watts
        lt.color = color
        lt.shadow_soft_size = 0.05
        lt.use_shadow = False            # the light sits inside its own (opaque) glowing lantern body
        o = bpy.data.objects.new(lt.name, lt)
        o.location = s / k
        coll.objects.link(o)
        n_lights += 1
print("preview lights:", n_lights)
