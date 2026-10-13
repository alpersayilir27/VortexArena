"""Night preview (Eevee): moonlight, dark sky, mist, the lanterns from parts 2/4, and a player-eye camera inside
the yard looking out of a gate. Renders to blender/Graveyard/renders/ (not in git). Unity lighting is set up
separately (GY_Lights.json gives it the lantern positions)."""
import importlib
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)

L = gy.Layout()
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in {e.identifier for e in
                                                                      bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items} else "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
scene.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
scene.view_settings.exposure = 0.0

world = scene.world or bpy.data.worlds.new("GY_World")
scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.012, 0.016, 0.03, 1)
bg.inputs["Strength"].default_value = 1.0

coll = gy.collection("GY_PreviewLights")
moon = bpy.data.objects.get("GY_Moon")
if moon is None:
    moon = bpy.data.objects.new("GY_Moon", bpy.data.lights.new("GY_Moon", "SUN"))
    coll.objects.link(moon)
moon.data.energy, moon.data.color, moon.data.angle = 0.35, (0.62, 0.72, 1.0), math.radians(1.5)
moon.rotation_euler = (math.radians(50), 0, math.radians(135))

# mist: volume scatter cube around the arena
mist = bpy.data.objects.get("GY_Mist")
if mist is None:
    bpy.ops.mesh.primitive_cube_add(size=1)
    mist = bpy.context.active_object
    mist.name = "GY_Mist"
    for c in list(mist.users_collection):
        c.objects.unlink(mist)
    coll.objects.link(mist)
    m = bpy.data.materials.new("GY_MistVol")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Density"].default_value = 0.012
    vol.inputs["Color"].default_value = (0.45, 0.5, 0.62, 1)
    nt.links.new(vol.outputs[0], next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL").inputs["Volume"])
    mist.data.materials.append(m)
x0, z0, x1, z1 = L.bounds
mist.location = gy.bl((x0 + x1) / 2, (z0 + z1) / 2, 4)
mist.scale = (x1 - x0 + 70, z1 - z0 + 70, 9)

# camera: eye height inside the yard, looking out through the gate of `side`
side = scene.get("gy_preview_side", "S")
g = next((g for g in L.gates if g[0] == side), L.gates[0])
gate = Vector(g[3])
eye = gate - g[4] * 3.2
cam = bpy.data.objects.get("GY_Cam")
if cam is None:
    cam = bpy.data.objects.new("GY_Cam", bpy.data.cameras.new("GY_Cam"))
    coll.objects.link(cam)
cam.data.lens = 18
cam.location = gy.bl(eye.x, eye.y, 1.65)
look = gy.bl(gate.x + g[4].x * 6, gate.y + g[4].y * 6, 1.2) - cam.location
cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
scene.camera = cam

out = os.path.join(gy.PROJ, "renders")
os.makedirs(out, exist_ok=True)
scene.render.filepath = os.path.join(out, f"preview_{gy.target()}_{side}.png")
bpy.ops.render.render(write_still=True)
print("rendered", scene.render.filepath)
