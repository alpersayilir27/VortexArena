"""CC0 texture source (Poly Haven): download into the local cache and build image materials.

A MATS entry with tex=("ph", asset_id, res) uses real maps; its T must be the asset's real size in metres
(Poly Haven /info 'dimensions', mm). The cache (blender/Graveyard/tex_cache/) is not in git: every file is
re-downloadable from the ids in gy_common.MATS."""
import json
import os
import urllib.request

import bpy

import gy_common as gy

CACHE = os.path.join(gy.PROJ, "tex_cache")
UA = {"User-Agent": "VortexArena-Graveyard/1.0"}


def _get(url, path):
    if os.path.exists(path):
        return path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r, open(path + ".part", "wb") as f:
        f.write(r.read())
    os.replace(path + ".part", path)
    return path


def maps(tex):
    """{'color', 'normal', 'arm'} local file paths (arm = AO R, roughness G, metallic B); downloads if missing."""
    _, aid, res = tex
    d = os.path.join(CACHE, f"ph_{aid}_{res}")
    want = (("color", "diff", "Diffuse"), ("normal", "nor_gl", "nor_gl"), ("arm", "arm", "arm"))
    paths = {k: os.path.join(d, f"{m}.jpg") for k, m, _ in want}
    if not all(os.path.exists(p) for p in paths.values()):
        with urllib.request.urlopen(urllib.request.Request(f"https://api.polyhaven.com/files/{aid}", headers=UA),
                                    timeout=60) as r:
            files = json.loads(r.read())
        for k, m, key in want:
            _get(files[key][res]["jpg"]["url"], paths[k])
    return paths


def _img(path, data):
    name = os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path)
    im = bpy.data.images.get(name)
    if im is None:
        im = bpy.data.images.load(path, check_existing=True)
        im.name = name
    im.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return im


def build(m, spec, uv_socket, nodes, links, bsdf):
    """Wires real maps into a material: albedo (optionally greyed + tinted), normal, roughness, metallic."""
    mp = maps(spec["tex"])

    def tex_node(path, data):
        n = nodes.new("ShaderNodeTexImage")
        n.image = _img(path, data)
        links.new(uv_socket, n.inputs["Vector"])
        return n

    col = tex_node(mp["color"], False).outputs["Color"]
    if spec.get("grey"):                                       # recolour: keep the texture's shading only
        bw = nodes.new("ShaderNodeRGBToBW")
        links.new(col, bw.inputs[0])
        col = bw.outputs[0]
    if spec.get("tint"):
        mx = nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        next(s for s in mx.inputs if s.identifier == "Factor_Float").default_value = 1.0
        links.new(col, next(s for s in mx.inputs if s.identifier == "A_Color"))
        next(s for s in mx.inputs if s.identifier == "B_Color").default_value = tuple(spec["tint"]) + (1,)
        col = next(s for s in mx.outputs if s.identifier == "Result_Color")
    links.new(col, bsdf.inputs["Base Color"])

    nm = nodes.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = spec.get("nstr", 1.0)
    links.new(tex_node(mp["normal"], True).outputs["Color"], nm.inputs["Color"])
    links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

    sep = nodes.new("ShaderNodeSeparateColor")
    links.new(tex_node(mp["arm"], True).outputs["Color"], sep.inputs[0])
    links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    if spec.get("metal"):
        links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    m.diffuse_color = tuple(spec.get("tint", spec.get("c1", (0.5, 0.5, 0.5)))) + (1,)
