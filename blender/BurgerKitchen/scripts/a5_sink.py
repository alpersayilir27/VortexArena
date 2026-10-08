# A5 sink cabinet. Front faces -Y, pivot = old cabinet's root. The basin is a real container in
# game (CounterSurface boxes: floor top 0.788, rim 1.030, walls + 0.09 m back splash up to 1.36):
# the visual basin follows those boxes; the floor sits a few mm below the floor box.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bk_common
importlib.reload(bk_common)

W = 0.60
YF, YB, YW = -0.365, 0.31, 0.40       # cabinet front, cabinet back, wall side of the splash
BX, BYF, BYB = 0.2215, -0.303, 0.273  # basin interior half-width, front, back
FLOOR, RING0, RIM, SPLASH = 0.783, 0.77, 1.030, 1.36
TOP0 = 0.985                          # countertop underside


def build(name):
    b = bk_common.Builder(name)
    yc = (YF + YB) / 2
    b.box("Plinth", (W - 0.04, YB - YF - 0.06, 0.08), (0, yc + 0.02), 0.0, "charcoal", 0.02, 1)
    b.box("Cabinet", (W, YB - YF, RING0 - 0.06), (0, yc), 0.06, "wood", 0.03, 2)

    # wooden ring around the basin up to the countertop
    b.box("Ring_F", (W, BYF - YF, TOP0 - RING0), (0, (YF + BYF) / 2), RING0, "wood", 0.012, 1)
    b.box("Ring_B", (W, YB - BYB, TOP0 - RING0), (0, (BYB + YB) / 2), RING0, "wood", 0.006, 1)
    for i, s in enumerate([-1, 1]):
        b.box(f"Ring_S{i}", (W / 2 - BX, BYB - BYF, TOP0 - RING0), (s * (BX + W / 4 - BX / 2), (BYF + BYB) / 2), RING0, "wood", 0.006, 1)
    b.box("DrawerGroove", (W - 0.06, 0.006, 0.014), (0, YF - 0.002), RING0 - 0.012, "wood_dark", 0)

    # countertop ring, slight overhang at the front and sides
    o = 0.015
    b.box("Top_F", (W + 2 * o, BYF - YF + o, RIM - TOP0), (0, (YF - o + BYF) / 2), TOP0, "steel", 0.012, 2)
    b.box("Top_B", (W + 2 * o, YB - BYB, RIM - TOP0), (0, (BYB + YB) / 2), TOP0, "steel", 0.006, 1)
    for i, s in enumerate([-1, 1]):
        b.box(f"Top_S{i}", (W / 2 + o - BX, BYB - BYF, RIM - TOP0), (s * (BX + (W / 2 + o - BX) / 2), (BYF + BYB) / 2), TOP0, "steel", 0.008, 1)

    # basin liner
    b.box("BasinFloor", (2 * BX, BYB - BYF, 0.012), (0, (BYF + BYB) / 2), FLOOR - 0.012, "steel_dark", 0)
    t = 0.008
    b.box("BasinWall_F", (2 * BX, t, TOP0 - FLOOR), (0, BYF + t / 2), FLOOR, "steel", 0)
    b.box("BasinWall_B", (2 * BX, t, TOP0 - FLOOR), (0, BYB - t / 2), FLOOR, "steel", 0)
    for i, s in enumerate([-1, 1]):
        b.box(f"BasinWall_S{i}", (t, BYB - BYF, TOP0 - FLOOR), (s * (BX - t / 2), (BYF + BYB) / 2), FLOOR, "steel", 0)
    b.cyl("Drain", 0.03, 0.006, (0, (BYF + BYB) / 2, FLOOR + 0.003), "Z", "charcoal", 12)

    # back splash + goose-neck faucet over the basin
    b.box("Splash", (W, YW - YB, SPLASH - TOP0), (0, (YB + YW) / 2), TOP0, "steel", 0.02, 2)
    b.cyl("FaucetBase", 0.035, 0.03, (0, YB - 0.012, 1.12), "Y", "steel_dark", 14, 0.006)
    b.pipe("Faucet", [(0, YB, 1.12), (0, 0.255, 1.12), (0, 0.235, 1.15), (0, 0.232, 1.29), (0, 0.215, 1.355),
                      (0, 0.17, 1.38), (0, 0.125, 1.355), (0, 0.108, 1.30), (0, 0.105, 1.235)], 0.025, "steel_dark", 12)
    b.pipe("Lever", [(0, 0.27, 1.12), (0.05, 0.26, 1.16), (0.09, 0.255, 1.18)], 0.014, "red", 8)

    # doors with raised panels and bar handles
    for i, s in enumerate([-1, 1]):
        x = s * 0.1425
        b.box(f"Door_{i}", (0.27, 0.025, 0.60), (x, YF - 0.0125), 0.13, "wood", 0.02, 2)
        b.box(f"DoorPanel_{i}", (0.19, 0.012, 0.48), (x, YF - 0.03), 0.19, "wood_dark", 0.008, 1)
        b.cyl(f"Handle_{i}", 0.013, 0.15, (s * 0.035, YF - 0.06, 0.52), "Z", "steel", 10, 0.005)
        for j, z in enumerate([0.465, 0.575]):
            b.cyl(f"HandleStem_{i}_{j}", 0.01, 0.03, (s * 0.035, YF - 0.04, z), "Y", "steel", 8)
    for p in b.parts:   # same textured cabinet wood as the counters
        if p["bk_color"] in ("wood", "wood_dark"):
            p["bk_wood"] = True
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("A5_Sink")
    print("A5_Sink tris", tris, "->", bk_common.export_fbx(obj, r"Models\A5_Sink.fbx"))
