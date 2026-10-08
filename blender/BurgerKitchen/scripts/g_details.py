# G details as separate pieces, so each lamp and each wall picture is its own scene object that can be
# selected and moved by hand: one ceiling lamp model (shared by every lamp) and one model per picture.
# Pivots: lamp = ceiling contact centre (hangs down); picture = wall contact at the picture centre,
# front facing -Y (Unity +Z). The SCENE owns the positions; LAYOUT below is only the first placement.
# Lamps only glow (unlit material); the light itself comes from the baked spots.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

FRAME_T = 0.025

# (decal key, width, height, frame colour)
PICTURES = [
    ("menu_board", 1.20, 0.80, "wood_dark"),
    ("poster", 0.62, 0.93, "red"),
    ("chef_mascot", 0.60, 0.90, "red"),
    ("veggies", 0.56, 0.84, "red"),
    ("hotdog", 0.52, 0.78, "red"),
    ("grill", 0.96, 0.64, "red"),
    ("cola", 0.52, 0.78, "red"),
    ("diner_night", 1.20, 0.80, "steel"),
    ("fries", 0.56, 0.84, "steel"),
    ("milkshake", 0.60, 0.90, "steel"),
    ("open_sign", 0.96, 0.64, "steel"),
]

# first placement, Unity world space: (key, x, y, z, yaw) — yaw turns the picture's front (+Z)
# toward the room: 0 = +z (south walls), 90 = +x (diner back wall, pass wall), 180 = -z, 270 = -x
LAYOUT = [
    ("menu_board", 0.90, 2.15, -4.0, 0), ("grill", 3.30, 2.15, -4.0, 0), ("cola", -1.20, 2.15, -4.0, 0),
    ("poster", 4.5, 1.95, -2.00, 270), ("veggies", 4.5, 2.05, -0.05, 270), ("hotdog", 4.5, 1.95, -3.40, 270),
    ("chef_mascot", -3.43, 1.95, -3.30, 90),
    ("diner_night", -7.5, 1.90, 1.80, 90), ("fries", -7.5, 1.90, -0.30, 90),
    ("milkshake", -6.40, 1.95, 4.0, 180), ("open_sign", -4.55, 1.95, -4.0, 0),
]
CEIL = 3.2
LAMPS = [(x, z) for x in (-3.38, -1.13, 1.13, 3.38) for z in (2.67, -0.08, -2.82)] + \
        [(-6.6, 2.85), (-6.6, 0.75), (-6.6, -1.35)]


def lamp(name):
    b = bk_common.Builder(name)
    b.lathe("Rim", [(0, -0.035), (0.20, -0.035), (0.20, 0.002), (0, 0.002)], (0, 0), "steel", 24)
    b.lamp("Glow", [(0, -0.047), (0.155, -0.047), (0.155, -0.03), (0, -0.03)], (0, 0), 24)
    return b.bake()


def picture(name, key, w, h, frame):
    b = bk_common.Builder(name)
    fw, fh = w + 0.07, h + 0.07
    b.box("Frame", (fw, FRAME_T, fh), (0, -FRAME_T / 2), -fh / 2, frame, 0.008, 1)
    b.decal("Art", (w, h), (0, 0), -FRAME_T - 0.002, key, 0.02, 2)
    return b.bake()


def model_name(key):
    return "G_Pic_" + "".join(p.capitalize() for p in key.split("_"))


if __name__ == "__main__":
    obj, tris = lamp("G_CeilingLamp")
    print("G_CeilingLamp tris", tris, "->", bk_common.export_fbx(obj, r"Models\G_CeilingLamp.fbx"))
    for key, w, h, frame in PICTURES:
        n = model_name(key)
        obj, tris = picture(n, key, w, h, frame)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
