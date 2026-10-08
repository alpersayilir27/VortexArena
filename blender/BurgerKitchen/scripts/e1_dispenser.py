# E1 ingredient dispenser machine (Cook'd Up style, look only): low colored base, steel-rimmed
# cream pad where the ingredient samples sit, back tower with a tilted icon display.
# One FBX per ingredient (base color + icon differ). Pivot = counter top under the dispenser root
# (root-local y -0.265); -Y faces the room. The take mechanic (squeeze in the grip socket) is
# unchanged; scene placement lifts Sample_* and GripSocket by PAD_TOP onto the pad.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

# (dispenser suffix, model name, base color, decal key)
INGREDIENTS = [
    ("patty", "Patty", "red", "icon_patty"),
    ("bun_whole", "Bun", "wood", "icon_bun"),
    ("cheese", "Cheese", "yellow", "icon_cheese"),
    ("tomato", "Tomato", "red_dark", "icon_tomato"),
    ("lettuce", "Lettuce", "green", "icon_lettuce"),
    ("onion", "Onion", "purple", "icon_onion"),
    ("pickle", "Pickle", "green_dark", "icon_pickle"),
    ("bacon", "Bacon", "wood_dark", "icon_bacon"),
]
BASE_H = 0.06
PAD_TOP = 0.074      # samples and grip socket are lifted by this much in the scene
TILT = 15


def build(name, color, icon_key):
    b = bk_common.Builder(name)
    b.box("Base", (0.38, 0.43, BASE_H), (0, 0.025), 0.0, color, 0.025, 2)
    for i, x in enumerate([-0.15, 0.15]):
        b.cyl(f"Bolt_{i}", 0.018, 0.012, (x, -0.16, BASE_H + 0.006), "Z", "steel", 10, 0.004)
    b.lathe("PadRim", [(0, BASE_H), (0.125, BASE_H), (0.125, BASE_H + 0.012), (0.105, BASE_H + 0.012), (0, BASE_H + 0.012)], (0, 0), "steel", 28)
    b.lathe("Pad", [(0, BASE_H + 0.004), (0.105, BASE_H + 0.004), (0.104, PAD_TOP), (0, PAD_TOP)], (0, 0), "cream", 28)
    b.box("Tower", (0.38, 0.10, 0.26), (0, 0.19), BASE_H, color, 0.02, 2)
    b.box("Display", (0.30, 0.035, 0.24), (0, 0.15), 0.28, "steel", 0.015, 2, rot_x=TILT)
    if any(k == icon_key for k, _, _ in bk_common.DECALS):
        b.decal("Icon", (0.21, 0.21), (0, 0.40), 0.1305, icon_key, 0.12, tilt=TILT)
    else:  # icon not in the atlas yet: blank cream screen
        b.box("IconBlank", (0.25, 0.006, 0.20), (0, 0.1305), 0.30, "cream", 0.01, 1, rot_x=TILT)
    return b.bake()


if __name__ == "__main__":
    for suffix, model, color, icon in INGREDIENTS:
        obj, tris = build(f"E1_Dispenser{model}", color, icon)
        print(f"E1_Dispenser{model} tris", tris, "->", bk_common.export_fbx(obj, rf"Models\E1_Dispenser{model}.fbx"))
