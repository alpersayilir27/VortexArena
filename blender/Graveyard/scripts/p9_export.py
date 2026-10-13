"""Part 9: exports the target's art for Unity (gy_common.art_dir): per material a baked albedo, tangent normal
and mask (R metallic, G AO, A smoothness), one FBX per art object, GY_Materials.json (read by
Tools > VortexArena > Art > Blender Sanatını Kur), GY_Lights.json (lantern/lamp positions for baked point
lights) and GY_Layout.json (gates, path ends = zombie spawn suggestions). All JSON positions are Unity space
(= venue x, height, z)."""
import importlib
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(bpy.data.filepath), "scripts"))
import gy_common as gy
importlib.reload(gy)

ART = gy.art_dir()
MODELS, TEXTURES = os.path.join(ART, "Models"), os.path.join(ART, "Textures")
PARTS = ["GY_P1_Ground", "GY_P2_Fence", "GY_P3_Graves", "GY_P4_Surround", "GY_P5_Statue", "GY_P6_Nature"]
SIZE_TEX, SIZE_FLAT = 1024, 256
os.makedirs(MODELS, exist_ok=True)
os.makedirs(TEXTURES, exist_ok=True)

objs = [o for c in PARTS for o in bpy.data.collections[c].objects if o.type == "MESH"]
mats = sorted({m for o in objs for m in o.data.materials if m}, key=lambda m: m.name)
gy.zfight_report([o for o in objs if gy.PART_ATTR in o.data.attributes])

# ---------------------------------------------------------------- bake rig: one quad covering UV 0..1 = one tile
scene = bpy.context.scene
prev_engine = scene.render.engine
scene.render.engine = "CYCLES"
scene.cycles.samples = 1
scene.render.bake.margin = 0
me = bpy.data.meshes.new("gy_bake_quad")
me.from_pydata([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)], [], [(0, 1, 2, 3)])
uvl = me.uv_layers.new()
for lp, uv in zip(me.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
    uvl.data[lp.index].uv = uv
quad = bpy.data.objects.new("gy_bake_quad", me)
scene.collection.objects.link(quad)
for o in bpy.context.view_layer.objects:
    o.select_set(False)
quad.select_set(True)
bpy.context.view_layer.objects.active = quad


def pixels(img):
    w, h = img.size
    return np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)


def save(path, arr, data):
    h, w, _ = arr.shape
    im = bpy.data.images.new("gy_out", w, h, alpha=True)
    im.colorspace_settings.name = "Non-Color" if data else "sRGB"
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
    return os.path.basename(path)


def bake(mat, kind, size, data):
    img = bpy.data.images.new("gy_bake", size, size, alpha=False, float_buffer=True)
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


mat_info = {}
for mat in mats:
    me.materials.clear()
    me.materials.append(mat)
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    spec = gy.MATS.get(mat.name[3:])
    textured = any(n.type == "TEX_IMAGE" for n in nt.nodes) or (spec and spec.get("pat") not in (None, "none"))
    size = SIZE_TEX if textured else SIZE_FLAT
    met_sock = bsdf.inputs["Metallic"]
    met_src = source(met_sock)
    if met_sock.is_linked:
        nt.links.remove(met_sock.links[0])
    old_met = met_sock.default_value
    met_sock.default_value = 0.0                                   # a metal has no diffuse colour
    alpha_sock = bsdf.inputs["Alpha"]
    alpha_src = source(alpha_sock)
    has_alpha = not isinstance(alpha_src, float)
    if has_alpha:
        nt.links.remove(alpha_sock.links[0])                       # opaque colour bake: no dark cut-out fringes
    base = bake(mat, "DIFFUSE", size, False)
    if has_alpha:
        nt.links.new(alpha_src, alpha_sock)
    met_sock.default_value = old_met
    if not isinstance(met_src, float):
        nt.links.new(met_src, met_sock)
    base[..., 3] = via_emission(mat, bsdf, alpha_src, size) if has_alpha else 1.0
    lin = base[..., :3]
    base[..., :3] = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.clip(lin, 0, None), 1 / 2.4) - 0.055)
    files = {"base": save(os.path.join(TEXTURES, f"T_{mat.name}_A.png"), base, False)}
    if bsdf.inputs["Normal"].is_linked:
        files["normal"] = save(os.path.join(TEXTURES, f"T_{mat.name}_N.png"), bake(mat, "NORMAL", size, True), True)
    mask = np.zeros((size, size, 4), dtype=np.float32)
    mask[..., 0] = via_emission(mat, bsdf, met_src, size)
    arm = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image and "arm" in n.image.name.lower()), None)
    if arm:
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(arm.outputs["Color"], sep.inputs[0])
        mask[..., 1] = via_emission(mat, bsdf, sep.outputs[0], size)
        nt.nodes.remove(sep)
    else:
        mask[..., 1] = 1.0
    mask[..., 3] = 1.0 - via_emission(mat, bsdf, source(bsdf.inputs["Roughness"]), size)
    files["mask"] = save(os.path.join(TEXTURES, f"T_{mat.name}_M.png"), mask, True)
    em_str = bsdf.inputs["Emission Strength"].default_value
    em = None
    if em_str > 0:
        em_col = bsdf.inputs["Emission Color"]
        em = {"fromBase": em_col.is_linked, "intensity": round((spec or {}).get("unity_emit", em_str), 3),
              "color": [round(c, 4) for c in em_col.default_value[:3]] if not em_col.is_linked else [1, 1, 1]}
    mat_info[mat.name] = {"baseMap": files["base"], "normalMap": files.get("normal"), "maskMap": files["mask"],
                          "alphaClip": has_alpha, "twoSided": bool(mat.get("gy_two_sided")), "emission": em,
                          "source": mat.get("gy_src") or "gy_textures"}
bpy.data.objects.remove(quad, do_unlink=True)
bpy.data.meshes.remove(me)
scene.render.engine = prev_engine
print(f"baked {len(mat_info)} materials")

# ---------------------------------------------------------------- FBX per object (same axis settings as ZombieManor)
for o in objs:
    if o.matrix_world != o.matrix_world.Identity(4):                # the statue: bake its placement into the mesh
        for x in bpy.context.view_layer.objects:
            x.select_set(False)
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in objs:
    for x in bpy.context.view_layer.objects:
        x.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODELS, o.name + ".fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
        path_mode="STRIP", embed_textures=False)
    o.select_set(False)
print(f"exported {len(objs)} FBX")

# ---------------------------------------------------------------- JSON: materials, lights, layout (Unity space)
U = lambda v: [round(-v.x, 4), round(v.z, 4), round(-v.y, 4)]
with open(os.path.join(ART, "GY_Materials.json"), "w", encoding="utf-8") as f:
    json.dump({"importHints": {"baseMap": "ASTC 6x6, sRGB", "normalMap": "ASTC 5x5, Normal map",
                               "maskMap": "ASTC 6x6, Linear", "maxSize": 1024}, "materials": mat_info}, f, indent=2)
lights = [{"name": o.name, "kind": o.name.split("_")[1], "position": U(o.location),
           "color": [round(c, 3) for c in o.data.color], "previewWatts": round(o.data.energy, 1)}
          for o in bpy.data.collections["GY_PreviewLights"].objects if o.type == "LIGHT" and o.data.type == "POINT"]
with open(os.path.join(ART, "GY_Lights.json"), "w", encoding="utf-8") as f:
    json.dump({"note": "point lights (Baked in Unity); kind = Lantern | Lamp | Candle", "lights": lights}, f, indent=2)

# stale outputs of objects/materials that no longer exist (their .meta goes with them; Unity drops the asset)
keep = {o.name + ".fbx" for o in objs} | {fn for e in mat_info.values()
                                          for fn in (e["baseMap"], e["normalMap"], e["maskMap"]) if fn}
for folder, prefix, ext in ((MODELS, "GY_", ".fbx"), (TEXTURES, "T_GY_", ".png")):
    for fn in os.listdir(folder):
        if fn.startswith(prefix) and fn.endswith(ext) and fn not in keep:
            for p in (os.path.join(folder, fn), os.path.join(folder, fn + ".meta")):
                if os.path.exists(p):
                    os.remove(p)
            print("removed stale", fn)
L = gy.Layout()
gates, spawns = [], []
for side, _, _, c, n in L.gates:
    gates.append({"side": side, "center": [round(c[0], 3), 0.0, round(c[1], 3)], "outward": [round(n.x, 3), 0.0, round(n.y, 3)]})
for a, b, side in L.paths:
    d = (b - a).normalized()
    side_v = Vector((d.y, -d.x))
    for k, p in enumerate((b - d * 0.8, b - d * 1.5 + side_v * 2.2)):
        yaw = math.degrees(math.atan2(-d.x, -d.y))                  # Unity yaw facing back along the path
        spawns.append({"name": f"ZombieSpawn_{side}{k + 1}", "position": [round(p.x, 3), round(L.ground_h(p.x, p.y), 3), round(p.y, 3)],
                       "yaw": round(yaw, 1)})
with open(os.path.join(ART, "GY_Layout.json"), "w", encoding="utf-8") as f:
    json.dump({"target": gy.target(), "gates": gates, "zombieSpawns": spawns}, f, indent=2)

# scene rules for the Unity scene dresser: renderer/NavMesh per object, light kinds, FX rings, reflection probe
c = gy.centroid(L.fence)
R = max((Vector(p) - c).length for p in L.fence)
x0, z0, x1, z1 = L.bounds
scene_rules = {
    "root": gy.MAP_NAME + "_Art",
    "lightsRoot": gy.MAP_NAME + "_Lights",
    "objects": {o.name: {**gy.UNITY_DEFAULT, **gy.UNITY_OBJECTS.get(o.name, {})} for o in objs},
    "lightKinds": gy.LIGHT_KINDS,
    "haloPrefab": gy.FX_DIR + "/GY_Halo.prefab",
    "fx": [
        {"prefab": gy.FX_DIR + "/GY_GroundMist.prefab", "position": [round(c.x, 3), 0.25, round(c.y, 3)],
         "ring": [round(R + 1.5, 2), round(R + 16.0, 2)]},
        {"prefab": gy.FX_DIR + "/GY_Wisps.prefab", "position": [round(c.x, 3), 0.9, round(c.y, 3)],
         "ring": [round(R + 1.5, 2), round(R + 10.0, 2)]},
    ],
    "reflectionProbe": {"name": gy.MAP_NAME + "_ReflectionProbe", "center": [round(c.x, 3), 1.5, round(c.y, 3)],
                        "size": [round(x1 - x0 + 4, 2), 4.0, round(z1 - z0 + 4, 2)]},
}
with open(os.path.join(ART, "GY_Scene.json"), "w", encoding="utf-8") as f:
    json.dump(scene_rules, f, indent=2)

tri_total = sum(gy.tris(o) for o in objs)
for o in sorted(objs, key=gy.tris, reverse=True):
    print(f"  {o.name}: {gy.tris(o)} tris, {len(o.data.materials)} materials")
print(f"total {tri_total} tris, {len(mat_info)} materials, {len(lights)} lights -> {ART}")
