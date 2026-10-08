# Cook'd Up style cabinetry shared by every counter (style B): thick rounded cream top with overhang,
# textured dark wood body, frame-and-panel doors with top/bottom rails and steel knobs, dark plinth.
# The top SURFACE height is the collider contract (CounterSurface boxes); thickness grows downward.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

TOP_T = 0.12        # counter top thickness
OVER = 0.035        # top overhang past the cabinet face
PLINTH_H = 0.10
DOOR_Z0 = 0.13


def wood(o):
    o["bk_wood"] = True
    return o


def top(b, name, size_xy, center_xy, top_z, color="cream"):
    sx, sy = size_xy
    return b.box(name, (sx, sy, TOP_T), center_xy, top_z - TOP_T, color, 0.045, 4)


def doors_y(b, tag, x_from, x_to, face_y, s, top_z, gap=0.03, target=0.6):
    """Door row on a face perpendicular to Y; s = -1 for a -Y face, +1 for a +Y face."""
    L = x_to - x_from
    n = max(1, round((L - 0.06) / target))
    dw = (L - 0.06 - (n - 1) * gap) / n
    dh = top_z - TOP_T - DOOR_Z0 - 0.04
    for i in range(n):
        x = x_from + 0.03 + dw / 2 + i * (dw + gap)
        wood(b.box(f"Door_{tag}{i}", (dw, 0.035, dh), (x, face_y + s * 0.0175), DOOR_Z0, "wood_dark", 0.02, 2))
        wood(b.box(f"Panel_{tag}{i}", (dw - 0.13, 0.012, dh - 0.13), (x, face_y + s * 0.033), DOOR_Z0 + 0.065, "wood_dark", 0.008, 1))
        for k, pz in enumerate((DOOR_Z0 + dh - 0.04, DOOR_Z0 + 0.01)):
            wood(b.box(f"Rail_{tag}{i}{k}", (dw - 0.06, 0.02, 0.03), (x, face_y + s * 0.045), pz, "wood_dark", 0.008, 1))
        hx = x + (dw / 2 - 0.07) * (1 if i % 2 == 0 else -1)
        b.box(f"Knob_{tag}{i}", (0.035, 0.03, 0.035), (hx, face_y + s * 0.06), DOOR_Z0 + dh - 0.16, "steel", 0.012, 2)


def doors_x(b, tag, y_from, y_to, face_x, s, top_z, gap=0.03, target=0.6):
    """Door row on a face perpendicular to X; s = -1 for a -X face, +1 for a +X face."""
    L = y_to - y_from
    n = max(1, round((L - 0.06) / target))
    dw = (L - 0.06 - (n - 1) * gap) / n
    dh = top_z - TOP_T - DOOR_Z0 - 0.04
    for i in range(n):
        y = y_from + 0.03 + dw / 2 + i * (dw + gap)
        wood(b.box(f"Door_{tag}{i}", (0.035, dw, dh), (face_x + s * 0.0175, y), DOOR_Z0, "wood_dark", 0.02, 2))
        wood(b.box(f"Panel_{tag}{i}", (0.012, dw - 0.13, dh - 0.13), (face_x + s * 0.033, y), DOOR_Z0 + 0.065, "wood_dark", 0.008, 1))
        for k, pz in enumerate((DOOR_Z0 + dh - 0.04, DOOR_Z0 + 0.01)):
            wood(b.box(f"Rail_{tag}{i}{k}", (0.02, dw - 0.06, 0.03), (face_x + s * 0.045, y), pz, "wood_dark", 0.008, 1))
        hy = y + (dw / 2 - 0.07) * (1 if i % 2 == 0 else -1)
        b.box(f"Knob_{tag}{i}", (0.03, 0.035, 0.035), (face_x + s * 0.06, hy), DOOR_Z0 + dh - 0.16, "steel", 0.012, 2)


def body(b, name, size_xy, center_xy, top_z):
    sx, sy = size_xy
    wood(b.box(name, (sx, sy, top_z - TOP_T - PLINTH_H), center_xy, PLINTH_H, "wood_dark", 0.015, 1))
    b.box(name + "Plinth", (sx - 0.08, sy - 0.08, PLINTH_H), center_xy, 0.0, "charcoal", 0.012, 1)
