# A1 griddle. Front faces -Y, pivot at bottom center. Rib tops sit at 0.95 m = the grill's
# CounterSurface top in the station; moving them makes patties float or sink.
import math, importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W = 1.10
RIB_TOP = 0.95

b = bk_common.Builder("A1_Griddle")

for i, (x, y) in enumerate([(-0.44, -0.24), (0.44, -0.24), (-0.44, 0.24), (0.44, 0.24)]):
    b.cyl(f"Leg_{i}", 0.05, 0.08, (x, y, 0.04), "Z", "steel", 16, 0.012)
b.box("Body", (1.04, 0.66, 0.77), (0, 0), 0.06, "red", 0.07, 3)

# knob row
b.box("KnobPanel", (0.92, 0.03, 0.15), (0, -0.335), 0.62, "red_dark", 0.012, 2)
for i, x in enumerate([-0.33, -0.11, 0.11, 0.33]):
    b.cyl(f"KnobRing_{i}", 0.062, 0.02, (x, -0.355, 0.695), "Y", "steel", 20, 0.006)
    b.cyl(f"Knob_{i}", 0.05, 0.05, (x, -0.375, 0.695), "Y", "charcoal", 20, 0.015, 2)
    b.box(f"KnobMark_{i}", (0.011, 0.004, 0.036), (x, -0.401), 0.694, "white", 0)

# doors with raised inner panel and chunky C handles
for i, x in enumerate([-0.23, 0.23]):
    b.box(f"Door_{i}", (0.43, 0.025, 0.42), (x, -0.335), 0.15, "red", 0.015, 2)
    b.box(f"DoorInset_{i}", (0.33, 0.02, 0.32), (x, -0.352), 0.20, "red_dark", 0.012, 2)
for i, x in enumerate([-0.07, 0.07]):
    b.cyl(f"HandleBar_{i}", 0.028, 0.20, (x, -0.42, 0.36), "Z", "steel", 16, 0.013, 2)
    for j, z in enumerate([0.28, 0.44]):
        b.cyl(f"HandleStem_{i}_{j}", 0.022, 0.08, (x, -0.385, z), "Y", "steel", 12, 0.006)

# cooking plate + raised ribs + front grease trough
plate_top = RIB_TOP - 0.018
b.box("Plate", (W, 0.70, plate_top - 0.83), (0, -0.01), 0.83, "plate", 0.03, 2)
n = 14
for i in range(n):
    x = -0.47 + 0.94 * i / (n - 1)
    b.box(f"Rib_{i}", (0.032, 0.56, 0.018), (x, -0.03), plate_top, "rib", 0.01, 1)
b.box("Trough", (0.96, 0.07, 0.035), (0, -0.375), 0.795, "steel", 0.012, 1)

# splash guard: back panel + sides rounding down toward the front
gz0, gz1, gt = plate_top - 0.005, plate_top + 0.155, 0.045
b.box("Guard_Back", (W, gt, gz1 - gz0), (0, 0.32), gz0, "steel", 0.015, 2)
R, yb, yf = 0.13, 0.345, -0.12
pts = [(yb, gz0), (yb, gz1), (yf + R, gz1)]
for k in range(1, 8):
    a = math.radians(90 * k / 8)
    pts.append((yf + R - R * math.sin(a), gz0 + (gz1 - gz0) * math.cos(a)))
pts.append((yf, gz0))
for i, x0 in enumerate([-W / 2, W / 2 - gt]):
    b.profile_x(f"Guard_Side_{i}", pts, x0, gt, "steel", 0.012, 2)

obj, tris = b.bake()
print("A1_Griddle tris", tris, "->", bk_common.export_fbx(obj, r"Models\A1_Griddle.fbx"))
