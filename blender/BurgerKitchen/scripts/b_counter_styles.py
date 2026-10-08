# Counter style study (Cook'd Up look): three 1.8 m variants side by side for a decision.
# Same collider contract as b1_counter: top surface at TOP (1.033 local), front YF / back YB.
# A: thick rounded light-grey top, plain dark wood cabinet (palette only)
# B: thick top + textured dark wood frame-and-panel doors (cabinet wood material)
# C: B with a steel top and a red diner stripe under the top edge
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

YF, YB, TOP = -0.369, 0.403, 1.033
TOP_T = 0.12
OVER = 0.035


def counter(name, length, style):
    b = bk_common.Builder(name)
    L = length
    top_col = "steel" if style == "C" else "cream"
    b.box("Top", (L + 2 * OVER, YB - YF + OVER, TOP_T), (0, (YF - OVER + YB) / 2), TOP - TOP_T, top_col, 0.045, 4)
    bf, bb = YF + 0.03, YB - 0.01
    z_top = TOP - TOP_T
    body = b.box("Body", (L - 0.02, bb - bf, z_top - 0.10), (0, (bf + bb) / 2), 0.10, "wood_dark", 0.015, 1)
    b.box("Plinth", (L - 0.10, bb - bf - 0.08, 0.10), (0, (bf + bb) / 2 + 0.04), 0.0, "charcoal", 0.012, 1)
    if style == "C":
        b.box("Stripe", (L + 0.002, 0.012, 0.05), (0, bf - 0.004), z_top - 0.06, "red", 0.006, 1)
    # frame-and-panel doors
    n = max(2, round(L / 0.6))
    gap = 0.03
    dw = (L - 0.06 - (n - 1) * gap) / n
    dz0, dh = 0.13, z_top - 0.13 - (0.10 if style == "C" else 0.04)
    for i in range(n):
        x = -L / 2 + 0.03 + dw / 2 + i * (dw + gap)
        if style == "A":
            b.box(f"Door_{i}", (dw, 0.03, dh), (x, bf - 0.015), dz0, "wood_dark", 0.02, 2)
            b.box(f"Panel_{i}", (dw - 0.12, 0.012, dh - 0.12), (x, bf - 0.035), dz0 + 0.06, "wood", 0.01, 1)
        else:
            frame = b.box(f"Door_{i}", (dw, 0.035, dh), (x, bf - 0.0175), dz0, "wood_dark", 0.02, 2)
            frame["bk_wood"] = True
            # recessed panel: a slightly inset darker plank field inside a raised frame
            panel = b.box(f"Panel_{i}", (dw - 0.13, 0.012, dh - 0.13), (x, bf - 0.033), dz0 + 0.065, "wood_dark", 0.008, 1)
            panel["bk_wood"] = True
            for k, (px, pz, pw_, ph_) in enumerate([(x, dz0 + dh - 0.04, dw - 0.06, 0.03), (x, dz0 + 0.01, dw - 0.06, 0.03)]):
                rail = b.box(f"Rail_{i}{k}", (pw_, 0.02, ph_), (px, bf - 0.045), pz, "wood_dark", 0.008, 1)
                rail["bk_wood"] = True
        hx = x + (dw / 2 - 0.07) * (1 if i % 2 == 0 else -1)
        b.box(f"Knob_{i}", (0.035, 0.03, 0.035), (hx, bf - 0.06), dz0 + dh - 0.16, "steel", 0.012, 2)
    if style != "A":
        body["bk_wood"] = True
    return b.bake()


if __name__ == "__main__":
    bk_common.build_cabinet_wood()
    for i, s in enumerate("ABC"):
        o, t = counter(f"CounterStyle_{s}", 1.8, s)
        o.location.x = (i - 1) * 2.3
        print(s, t)
