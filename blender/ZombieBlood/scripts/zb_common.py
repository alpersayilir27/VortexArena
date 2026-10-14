# Shared helpers for the zombie blood textures: clean scene, ortho camera, wet blood material,
# transparent render to a pixel array, PNG write. Run inside Blender (bpy).
import os
import random

import bpy
import numpy as np

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT_DIR = os.path.join(REPO, "Assets", "Modes", "Zombie", "FX", "Textures")

# Dark, desaturated arterial red; the particle tint multiplies on top in Unity.
BLOOD_RGB = (0.09, 0.004, 0.004)


def reset_scene():
    # Own scene so an open .blend is never wiped.
    old = bpy.data.scenes.get("ZB_Render")
    if old is not None:
        for obj in list(old.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.scenes.remove(old)
    scene = bpy.data.scenes.new("ZB_Render")
    bpy.context.window.scene = scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = next((e for e in engines if "EEVEE" in e), engines[0])
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("World")
    world.color = (0.02, 0.02, 0.02)
    scene.world = world
    return scene


def ortho_camera(scene, size, res_x, res_y):
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Cam", cam_data)
    cam.location = (0.0, 0.0, 10.0)
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x = res_x
    scene.render.resolution_y = res_y
    scene.render.resolution_percentage = 100
    return cam


def key_light(scene):
    sun_data = bpy.data.lights.new("Key", "SUN")
    sun_data.energy = 3.5
    sun = bpy.data.objects.new("Key", sun_data)
    sun.rotation_euler = (0.6, -0.5, 0.3)
    scene.collection.objects.link(sun)
    fill_data = bpy.data.lights.new("Fill", "SUN")
    fill_data.energy = 0.8
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.rotation_euler = (-0.4, 0.6, 2.4)
    scene.collection.objects.link(fill)


def blood_material():
    mat = bpy.data.materials.new("ZB_Blood")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*BLOOD_RGB, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.18
    return mat


def metaball(name, mat, resolution=0.02):
    mb = bpy.data.metaballs.new(name)
    mb.resolution = resolution
    mb.render_resolution = resolution
    mb.threshold = 0.6
    obj = bpy.data.objects.new(name, mb)
    obj.data.materials.append(mat)
    bpy.context.scene.collection.objects.link(obj)
    return mb, obj


def add_ball(mb, co, radius, stretch=None):
    el = mb.elements.new()
    el.co = co
    el.radius = radius
    if stretch is not None:
        el.type = "ELLIPSOID"
        el.size_x, el.size_y, el.size_z = stretch
    return el


def render_pixels(scene):
    """Renders the scene and returns an (h, w, 4) float array."""
    path = os.path.join(bpy.app.tempdir, "zb_frame.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def write_png(pixels, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    h, w, _ = pixels.shape
    img = bpy.data.images.new(name, width=w, height=h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(pixels, dtype=np.float32).ravel())
    img.filepath_raw = os.path.join(OUT_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return os.path.join(OUT_DIR, name + ".png")


def rng(seed):
    return random.Random(seed)
