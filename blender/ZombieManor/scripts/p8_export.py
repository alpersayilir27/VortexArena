"""Part 8: exports the art for Unity. Per material (art MATS and the CC0 props' own ZM_A_* materials): baked
albedo (tint/grey applied, alpha where the material has one), tangent normal (strength applied) and a mask
(R metallic, G AO, A smoothness: one texture for URP Lit's metallic and occlusion slots). One FBX per art
object, decor layout, material values, practical lights (from the night preview), budget report.
Output: the arena's own art folder; set ZM_EXPORT_OUT to write elsewhere (dry run)."""
import importlib
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)

ART = os.environ.get("ZM_EXPORT_OUT") or os.path.join(zm.REPO, "Assets", "Arenas", "Venues", "VortexAntep", "Scenes",
                                                      "ZombieManor_VortexAntep", "Art")
MODELS, TEXTURES = os.path.join(ART, "Models"), os.path.join(ART, "Textures")
HOUSE = ["ZM_P1_Courtyard", "ZM_P2_Interior", "ZM_P3_Balcony", "ZM_P4_Upstairs", "ZM_P5_Terrace", "ZM_P6_Exterior"]
SIZE_TEX, SIZE_FLAT = 1024, 256          # image-based materials / flat-colour ones (no detail to keep)
os.makedirs(MODELS, exist_ok=True)
os.makedirs(TEXTURES, exist_ok=True)

objs = [o for c in HOUSE + ["ZM_P7_Kit"] for o in bpy.data.collections[c].objects]
mats = sorted({m for o in objs for m in o.data.materials if m}, key=lambda m: m.name)
PARTIAL = bool(os.environ.get("ZM_EXPORT_MATS"))           # re-bake a few materials only: no FBX, no JSON
if PARTIAL:
    mats = [m for m in mats if m.name in os.environ["ZM_EXPORT_MATS"].split(",")]
    objs = []


def spec_of(m):
    return zm.MATS.get(m.name[3:]) if not m.name.startswith("ZM_A_") else None


# ---------------------------------------------------------------- bake rig: one quad covering UV 0..1 = one tile
scene = bpy.context.scene
prev_engine = scene.render.engine
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.render.bake.margin = 0
me = bpy.data.meshes.new("zm_bake_quad")
me.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [], [(0, 1, 2, 3)])
uvl = me.uv_layers.new()
for lp, uv in zip(me.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
    uvl.data[lp.index].uv = uv
quad = bpy.data.objects.new("zm_bake_quad", me)
scene.collection.objects.link(quad)
for o in bpy.context.view_layer.objects:
    o.select_set(False)
quad.select_set(True)
bpy.context.view_layer.objects.active = quad


def pixels(img):
    import numpy as np
    w, h = img.size
    return np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)


def save(path, arr, data):
    import numpy as np
    h, w, _ = arr.shape
    im = bpy.data.images.new("zm_out", w, h, alpha=True)
    im.colorspace_settings.name = "Non-Color" if data else "sRGB"
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
    return os.path.basename(path)


def bake(mat, kind, size, data):
    """Bakes one pass of `mat` on the quad into a float array (h, w, 4)."""
    img = bpy.data.images.new("zm_bake", size, size, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color" if data else "Linear Rec.709"
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    mat.node_tree.nodes.active = node
    if kind == "DIFFUSE":
        bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True)
    else:
        bpy.ops.object.bake(type=kind, use_clear=True)
    arr = pixels(img)
    mat.node_tree.nodes.remove(node)
    bpy.data.images.remove(img)
    return arr


def via_emission(mat, bsdf, src, size):
    """Bakes a scalar socket (or constant) by routing it through emission; returns one channel."""
    nt = mat.node_tree
    em_col, em_str = bsdf.inputs["Emission Color"], bsdf.inputs["Emission Strength"]
    old_link = em_col.links[0].from_socket if em_col.is_linked else None
    old_col, old_str = tuple(em_col.default_value), em_str.default_value
    if old_link:
        nt.links.remove(em_col.links[0])
    if isinstance(src, (int, float)):
        em_col.default_value = (src, src, src, 1)
    else:
        nt.links.new(src, em_col)
    em_str.default_value = 1.0
    arr = bake(mat, "EMIT", size, True)[..., 0]
    if em_col.is_linked:
        nt.links.remove(em_col.links[0])
    if old_link:
        nt.links.new(old_link, em_col)
    em_col.default_value, em_str.default_value = old_col, old_str
    return arr


def source(sock):
    return sock.links[0].from_socket if sock.is_linked else sock.default_value


tex_files, mat_info = {}, {}
for mat in mats:
    import numpy as np
    me.materials.clear()
    me.materials.append(mat)
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    spec = spec_of(mat)
    textured = any(n.type == "TEX_IMAGE" for n in nt.nodes) or (spec and spec.get("pat") not in (None, "none"))
    size = SIZE_TEX if textured else SIZE_FLAT
    key = mat.name[3:]
    # albedo: metallic must be 0 while baking diffuse colour (a metal has none), alpha into A
    met_sock = bsdf.inputs["Metallic"]
    met_src = source(met_sock)
    if met_sock.is_linked:
        nt.links.remove(met_sock.links[0])
    old_met = met_sock.default_value
    met_sock.default_value = 0.0
    base = bake(mat, "DIFFUSE", size, False)
    met_sock.default_value = old_met
    if not isinstance(met_src, float):
        nt.links.new(met_src, met_sock)
    alpha_src = source(bsdf.inputs["Alpha"])
    has_alpha = not isinstance(alpha_src, float)
    base[..., 3] = via_emission(mat, bsdf, alpha_src, size) if has_alpha else 1.0
    lin = base[..., :3]
    base[..., :3] = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.clip(lin, 0, None), 1 / 2.4) - 0.055)
    files = {"base": save(os.path.join(TEXTURES, f"T_{mat.name}_A.png"), base, False)}
    # normal (tangent, OpenGL): only where the material has one
    if bsdf.inputs["Normal"].is_linked:
        files["normal"] = save(os.path.join(TEXTURES, f"T_{mat.name}_N.png"), bake(mat, "NORMAL", size, True), True)
    # mask: R metallic, G AO, B 0, A smoothness
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = via_emission(mat, bsdf, met_src, size)
    arm = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image and "arm" in n.image.name.lower()), None)
    ao = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image and "ambientocclusion" in n.image.name.lower()), None)
    if arm:
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(arm.outputs["Color"], sep.inputs[0])
        mask[..., 1] = via_emission(mat, bsdf, sep.outputs[0], size)
        nt.nodes.remove(sep)
    elif ao:
        mask[..., 1] = via_emission(mat, bsdf, ao.outputs["Color"], size)
    else:
        mask[..., 1] = 1.0
    mask[..., 3] = 1.0 - via_emission(mat, bsdf, source(bsdf.inputs["Roughness"]), size)
    files["mask"] = save(os.path.join(TEXTURES, f"T_{mat.name}_M.png"), mask, True)
    tex_files[mat.name] = files
    # emission: constant colour x strength, or "from base" when the albedo itself glows (window view, flames)
    em_str = bsdf.inputs["Emission Strength"].default_value
    em = None
    if em_str > 0:
        em_col = bsdf.inputs["Emission Color"]
        em = {"fromBase": em_col.is_linked, "intensity": round(em_str, 3),
              "color": [round(c, 4) for c in em_col.default_value[:3]] if not em_col.is_linked else [1, 1, 1]}
    mat_info[mat.name] = {"baseMap": files["base"], "normalMap": files.get("normal"), "maskMap": files["mask"],
                          "alphaClip": has_alpha, "twoSided": bool(spec and spec.get("two_sided")),
                          "emission": em, "source": mat.get("zm_src") and "polyhaven" or "zm_textures"}
bpy.data.objects.remove(quad, do_unlink=True)
bpy.data.meshes.remove(me)
scene.render.engine = prev_engine
print(f"baked {len(tex_files)} materials")

# ---------------------------------------------------------------- FBX per object (same axis settings as blender/BurgerKitchen)
for o in objs:
    for x in bpy.context.view_layer.objects:
        x.select_set(False)
    coll = o.users_collection[0]
    was_hidden = coll.hide_viewport
    coll.hide_viewport = False                           # the kit collection is hidden in the viewport
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODELS, o.name + ".fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
        path_mode="STRIP", embed_textures=False)
    o.select_set(False)
    coll.hide_viewport = was_hidden
print(f"exported {len(objs)} FBX")

# ---------------------------------------------------------------- layout, materials, lights (Unity space: x = -bx, y = bz, z = -by)
U = lambda v: [round(-v.x, 4), round(v.z, 4), round(-v.y, 4)]
lights = []
if not PARTIAL:                                             # a partial run would overwrite them with a subset
    layout = [{"prefab": o["zm_prefab"], "position": U(o.location), "rotationY": round(-math.degrees(o.rotation_euler.z), 2),
               "scale": round(o.scale.x, 4)} for o in bpy.data.collections["ZM_P7_Layout"].objects]
    with open(os.path.join(ART, "ZM_Layout.json"), "w", encoding="utf-8") as f:
        json.dump({"props": layout}, f, indent=2)
    with open(os.path.join(ART, "ZM_Materials.json"), "w", encoding="utf-8") as f:
        json.dump({"importHints": {"baseMap": "ASTC 6x6, sRGB", "normalMap": "ASTC 5x5, Normal map",
                                   "maskMap": "ASTC 6x6, Linear", "maxSize": 1024},
                   "materials": mat_info}, f, indent=2)
    pl = bpy.data.collections.get("ZM_PreviewLights")
    for o in (pl.objects if pl else []):
        if o.type == "LIGHT" and o.data.type == "POINT":
            lights.append({"name": o.name, "position": U(o.location), "color": [round(c, 3) for c in o.data.color],
                           "previewWatts": round(o.data.energy, 1)})
    with open(os.path.join(ART, "ZM_Lights.json"), "w", encoding="utf-8") as f:
        json.dump({"note": "practical lights (Baked/Mixed in Unity), one per glowing cluster", "lights": lights}, f, indent=2)

# ---------------------------------------------------------------- budget report
tris = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
house_tris = sum(tris(o) for o in objs if not o.name.startswith("ZM_D_"))
lay = bpy.data.collections["ZM_P7_Layout"].objects
for o in sorted(objs, key=tris, reverse=True)[:12]:
    print(f"  {o.name}: {tris(o)} tris, {len(o.data.materials)} materials")
print(f"scene as laid out: {house_tris + sum(tris(o) for o in lay)} tris, {len(mats)} materials, "
      f"{sum(len(v) for v in tex_files.values())} textures, {len(lights)} practical lights")
