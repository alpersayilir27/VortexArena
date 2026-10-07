# Builds the mole-game leather glove meshes (MoleGlove_R / MoleGlove_L) on Meta's OpenXR hands (Blender 5.x).
# Run inside MoleGlove.blend (Scripting > Run Script = build_all()); usage: README.md next to this file.
# Hand space: fingers +Y, dorsal +Z, thumb -X on the right hand, metres. Textures come from mole_tex.py.
# Geometry code is shared with ../ChefGlove/chef_builder.py (imported, not copied); only the profile lives here.
import bpy, os, sys, importlib

def _here():
    f = globals().get("__file__")
    if f and os.path.isfile(f): return os.path.dirname(os.path.abspath(f))
    return os.path.dirname(bpy.data.filepath)  # Text Editor run: the .blend sits next to this script

_CHEF = os.path.normpath(os.path.join(_here(), "..", "ChefGlove"))
if _CHEF not in sys.path: sys.path.insert(0, _CHEF)
import chef_builder as cb
importlib.reload(cb)

GLOVE, CUFF, STRAP, INNER = 0, 1, 3, 5   # GLOVE / INNER must match chef_builder; STRAP = Unity submesh 1 (team tint)
INFLATE = 0.0028                         # leather thickness over the hand (m)
# Cuff profile from the wrist opening: (dy below its lowest point, scale around its centre, zone, tag).
# A band between two rings takes the zone of its lower ring. Tags are the anchors mole_tex.py paints from.
CUFF_RINGS = [
    (-0.0040, 1.02, GLOVE, None), (-0.0100, 1.04, GLOVE, None),                     # leather down to the strap
    (-0.0104, 1.10, STRAP, "s_front"), (-0.0118, 1.135, STRAP, "s_top"),           # strap's upper edge
    (-0.0370, 1.135, STRAP, "s_bot"), (-0.0384, 1.10, STRAP, None), (-0.0388, 1.05, STRAP, "s_end"),
    (-0.0400, 1.045, CUFF, None), (-0.0470, 1.055, CUFF, "rim"), (-0.0482, 1.035, CUFF, "lip"),  # leather hem
    (-0.0470, 0.99, INNER, None), (-0.0300, 0.95, INNER, None),                     # inner wall
]

def build_all():
    for side in ("Right", "Left"):
        cb.build(f"{side}Hand", f"MoleGlove_{side[0]}", rings=CUFF_RINGS, inflate=INFLATE, material="M_MoleGlove")

if __name__ == "__main__":
    build_all()
