# D4 diner swinging double door on the diner's south wall, in front of the entrance vestibule
# (g_shell cuts the opening). Three models so the leaves can swing (BurgerDinerDoor):
#   D4_DoorFrame  steel jamb, pivot = floor centre of the opening
#   D4_DoorLeafE  leaf on the east side seen in Unity, pivot = its hinge
#   D4_DoorLeafW  leaf on the west side, pivot = its hinge
# Placement: frame at world (-6.0, 0, -3.99), rotation 0, -Y (Blender) faces into the room.
# Export flips x: Blender s = -1 ends up on Unity +x (east).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

LW, LH, T = 0.48, 2.04, 0.05   # leaf width, height, thickness
Z0 = 0.06
HINGE = LW + 0.006             # |x| of each hinge from the opening centre


def frame(name):
    b = bk_common.Builder(name)
    b.box("Jamb_L", (0.07, 0.08, Z0 + LH + 0.07), (-(LW + 0.045), 0.0), 0.0, "steel", 0.015, 1)
    b.box("Jamb_R", (0.07, 0.08, Z0 + LH + 0.07), ((LW + 0.045), 0.0), 0.0, "steel", 0.015, 1)
    b.box("Jamb_T", (2 * LW + 0.16, 0.08, 0.07), (0, 0.0), Z0 + LH, "steel", 0.015, 1)
    return b.bake()


def leaf(name, s):
    """One leaf, built relative to its hinge (x' = x - s * HINGE)."""
    b = bk_common.Builder(name)
    h = s * HINGE
    x = s * (LW / 2 + 0.006) - h
    b.box("Leaf", (LW, T, LH), (x, 0.0), Z0, "red", 0.04, 3)
    for side, dy in (("F", -1), ("B", 1)):   # porthole and plates on both faces
        b.cyl(f"PortRing{side}", 0.115, 0.03, (x, dy * (T / 2 + 0.01), Z0 + 1.50), "Y", "steel", 24, 0.01, 2)
        b.cyl(f"PortGlass{side}", 0.085, 0.032, (x, dy * (T / 2 + 0.016), Z0 + 1.50), "Y", "cream", 24, 0.008, 1)
        b.box(f"PushPlate{side}", (0.07, 0.015, 0.17), (s * 0.075 - h, dy * (T / 2 + 0.006)), Z0 + 0.96, "steel", 0.006, 1)
        b.box(f"KickPlate{side}", (LW - 0.06, 0.012, 0.30), (x, dy * (T / 2 + 0.004)), Z0 + 0.06, "steel", 0.006, 1)
    for j, z in enumerate([0.30, 1.75]):
        b.box(f"Hinge_{j}", (0.03, 0.04, 0.12), (s * 0.006, -0.01), Z0 + z, "steel_dark", 0.006, 1)
    return b.bake()


BUILDS = [("D4_DoorFrame", frame), ("D4_DoorLeafE", lambda n: leaf(n, -1)), ("D4_DoorLeafW", lambda n: leaf(n, 1))]

if __name__ == "__main__":
    for n, fn in BUILDS:
        obj, tris = fn(n)
        print(n, "tris", tris, "->", bk_common.export_fbx(obj, rf"Models\{n}.fbx"))
