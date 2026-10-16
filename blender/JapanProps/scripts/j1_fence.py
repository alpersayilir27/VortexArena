# J1 fence panel: Japanese construction-site panel (white corrugated sheet, blue frame bands and
# edge posts, rubber feet). Front faces -Y, pivot at the bottom center of the 2.0 x 0.5 foot
# footprint, z=0 floor, 1.86 m tall. Two box colliders (panel, feet); __main__ prints them.
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (2.0, 0.5)
PANEL = (2.0, 0.04, 1.72)
PANEL_Z = 0.14
PANEL_TOP = PANEL_Z + PANEL[2]
RIB_W, RIB_T, RIB_PROUD = 0.03, 0.013, 0.003
RIB_STEP, RIB_LAST = 0.10, 0.90     # last rib stays clear of the edge posts
RIB_Z0, RIB_Z1 = 0.26, 1.74
BAND = (1.98, 0.05, 0.10)           # blue frame bands: 5 mm proud of the sheet, ends buried in the posts
BAND_OUT = 0.004                    # bands pass the sheet top/bottom: shared end faces would z-fight
POST = (0.06, 0.066, PANEL[2] + 0.016)   # posts thicker and taller than the bands for the same reason
POST_X = 0.965                      # inboard of the panel edge: flush x faces would z-fight
SIGN = (1.4, 0.7)
SIGN_Y = -0.027                     # 4 mm clear of the rib faces, 7 mm of the sheet
FOOT_BLOCK = (0.3, 0.5, 0.12)
FOOT_X = 0.85
HAZARD_INSET = 0.006                # the yellow band is recessed: flush sides would z-fight
BEV = 0.006


def sheet(b):
    """Corrugated white sheet with ribs on both faces, blue frame bands and edge posts."""
    b.box("Panel", PANEL, (0, 0), PANEL_Z, "panel_white", 0.004, 1)
    n = int(round(2 * RIB_LAST / RIB_STEP)) + 1
    for i in range(n):
        x = -RIB_LAST + i * RIB_STEP
        for s, sy in enumerate((-1, 1)):
            # rib stands 3 mm off the sheet face and reaches 1 cm into it: never coplanar with it
            cy = sy * (PANEL[1] / 2 + RIB_PROUD - RIB_T / 2)
            b.box(f"Rib{s}_{i}", (RIB_W, RIB_T, RIB_Z1 - RIB_Z0), (x, cy), RIB_Z0,
                  "panel_white", 0.0015, 1)
    for i, z0 in enumerate((PANEL_Z - BAND_OUT, PANEL_TOP - BAND[2] + BAND_OUT)):
        b.box(f"Band{i}", BAND, (0, 0), z0, "panel_blue", BEV, 1)
    for i, sx in enumerate((-1, 1)):
        b.box(f"Post{i}", POST, (sx * POST_X, 0), PANEL_Z - 0.008, "panel_blue", BEV, 1)
    b.decal("Sign", SIGN, (0.0, SIGN_Y, (PANEL_Z + PANEL_TOP) / 2), "sign_kouji")


def feet(b):
    """Rubber foot blocks with a recessed hazard band, plus a bracket up to the panel."""
    sx_, sy_, sz_ = FOOT_BLOCK
    for i, sx in enumerate((-1, 1)):
        x = sx * FOOT_X
        b.box(f"Foot{i}_Low", (sx_, sy_, 0.045), (x, 0), 0.0, "rubber", BEV, 1)
        b.box(f"Foot{i}_Band", (sx_ - HAZARD_INSET, sy_ - HAZARD_INSET, 0.03), (x, 0), 0.045,
              "hazard_yellow", 0.004, 1)
        b.box(f"Foot{i}_Top", (sx_, sy_, sz_ - 0.075), (x, 0), 0.075, "rubber", BEV, 1)
        # bracket bridges the 2 cm between the foot top and the panel bottom
        b.box(f"Foot{i}_Bracket", (0.08, 0.14, 0.05), (x, 0), 0.10, "rubber", 0.004, 1)


def build(name):
    b = jp_common.Builder(name)
    sheet(b)
    feet(b)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("J1_FencePanel")
    print("J1_FencePanel tris", tris, "->", jp_common.export_fbx(obj, r"Models\J1_FencePanel.fbx"))
    print("COLLIDER panel center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % ((PANEL_Z + PANEL_TOP) / 2, PANEL[0], 0.08, PANEL[2]))
    print("COLLIDER feet  center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (FOOT_BLOCK[2] / 2, FOOT[0], FOOT[1], FOOT_BLOCK[2]))
