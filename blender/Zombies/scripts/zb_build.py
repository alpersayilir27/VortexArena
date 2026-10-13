"""Builds the zombie character FBXs for Unity from the Mixamo sources in ../source/characters.

Per character: import -> join meshes -> decimate to TARGET_TRIS -> pack every material's UVs into one
atlas -> bake base colour to ATLAS_PX -> single material -> export skinned FBX (mixamorig skeleton kept,
no animation) + albedo PNG into Assets/Modes/Zombie/Models/. Run inside Blender:
    exec(open(r"<repo>/blender/Zombies/scripts/zb_build.py").read())
"""
import bpy, os, math

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__) if "__file__" in dir() else
                                    r"C:\UnityProjects\VortexArena\blender\Zombies\scripts", "..", "..", ".."))
SRC = os.path.join(REPO, "blender", "Zombies", "source", "characters")
OUT = os.path.join(REPO, "Assets", "Modes", "Zombie", "Models")
TARGET_TRIS = 8000          # Quest budget: ~30 zombies on screen
ATLAS_PX = 1024
BAKE_MARGIN = 4

# source file -> output name (ASCII, Unity-friendly)
CHARACTERS = {
    "Ch10_nonPBR.fbx": "ZB_Ghoul",
    "copzombie_l_actisdato.fbx": "ZB_Cop",
    "Warzombie F Pedroso.fbx": "ZB_Soldier",
    "Whiteclown N Hallin.fbx": "ZB_Clown",
    "Zombiegirl W Kurniawan.fbx": "ZB_Girl",
}


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures, bpy.data.actions):
        for b in list(block):
            if b.users == 0:
                block.remove(b)


def tri_count(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def join_meshes(meshes):
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def decimate(obj):
    tris = tri_count(obj.data)
    if tris <= TARGET_TRIS:
        return tris
    mod = obj.modifiers.new("Decimate", 'DECIMATE')
    mod.ratio = TARGET_TRIS / tris
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = obj
    with bpy.context.temp_override(object=obj, active_object=obj):
        # first in the stack: decimate must run on the undeformed mesh, before the armature
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return tri_count(obj.data)


def pack_atlas_uv(obj):
    """New UV layer 'Atlas' = the artist's islands repacked into one 0-1 square.

    Repacking keeps each island's relative size, so the face keeps its higher texel density; islands
    of different materials (all overlapping 0-1 in the source) no longer collide."""
    me = obj.data
    # ⚠️ names only: adding a layer or switching mode invalidates layer references (dangling pointer)
    src_name = me.uv_layers.active.name
    me.uv_layers.new(name="Atlas")              # copy of the active layer
    me.uv_layers.active = me.uv_layers["Atlas"]
    names = (src_name, "Atlas")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    return names


def route_base_color_to_emission(nt):
    """Feeds the BSDF's base colour straight into an Emission output for an EMIT bake.

    ⚠️ Not a DIFFUSE colour bake: Cycles scales that pass by (1 - metallic) and specular, and the
    Mixamo materials carry both, so the atlas came out up to 3x darker than the source texture."""
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    output = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if bsdf is None or output is None:
        return
    emit = nt.nodes.new("ShaderNodeEmission")
    base = bsdf.inputs[0]
    if base.is_linked:
        nt.links.new(base.links[0].from_socket, emit.inputs["Color"])
    else:
        emit.inputs["Color"].default_value = base.default_value
    nt.links.new(emit.outputs[0], output.inputs["Surface"])


def bake_albedo(obj, name, src_uv, atlas_uv):
    img = bpy.data.images.new(name + "_Albedo", ATLAS_PX, ATLAS_PX, alpha=False)
    # bake target node in EVERY material, reading the atlas UV
    for mat in obj.data.materials:
        nt = mat.node_tree
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.name = "ZB_BakeTarget"
        uvn = nt.nodes.new("ShaderNodeUVMap")
        uvn.uv_map = atlas_uv
        nt.links.new(uvn.outputs[0], tex.inputs[0])
        nt.nodes.active = tex
        # source image textures keep sampling the original UV explicitly
        for n in nt.nodes:
            if n.type == 'TEX_IMAGE' and n is not tex and not n.inputs[0].is_linked:
                s = nt.nodes.new("ShaderNodeUVMap")
                s.uv_map = src_uv
                nt.links.new(s.outputs[0], n.inputs[0])
        route_base_color_to_emission(nt)
    obj.data.uv_layers[atlas_uv].active = True
    scn = bpy.context.scene
    try:
        scn.render.engine = 'CYCLES'
    except TypeError:
        pass
    scn.cycles.samples = 8
    scn.render.bake.margin = BAKE_MARGIN
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type='EMIT')
    path = os.path.join(OUT, name + "_Albedo.png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    return img


def single_material(obj, name, img, atlas_uv):
    me = obj.data
    mat = bpy.data.materials.new("M_" + name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs[0], bsdf.inputs[0])
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    # only the atlas UV ships
    for uv in [u for u in me.uv_layers if u.name != atlas_uv]:
        me.uv_layers.remove(uv)
    me.uv_layers[0].name = "UVMap"


def export(arm, mesh, name):
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    mesh.name = name
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
        apply_scale_options='FBX_SCALE_UNITS', path_mode='STRIP', mesh_smooth_type='FACE')


def build(src_file, name):
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, src_file))
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    obj = join_meshes(meshes)
    before = tri_count(obj.data)
    after = decimate(obj)
    src_uv, atlas_uv = pack_atlas_uv(obj)
    img = bake_albedo(obj, name, src_uv, atlas_uv)
    single_material(obj, name, img, atlas_uv)
    export(arm, obj, name)
    print(f"[zb] {name}: {before} -> {after} tris, {len(meshes)} mesh(es)")


os.makedirs(OUT, exist_ok=True)
ONLY = globals().get("ZB_ONLY")     # set ZB_ONLY = "ZB_Girl" before exec to build one character
for src_file, name in CHARACTERS.items():
    if ONLY and name != ONLY:
        continue
    build(src_file, name)
print("[zb] done")
