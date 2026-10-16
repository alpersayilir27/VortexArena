# A1 beer crates: waist-high cover prop for the Tokyo back alley (stacked plastic beer crates
# + two kegs). Front faces -Y, pivot at bottom center of the whole footprint, z=0 floor.
# Approx 1.58 x 0.78 x 0.99 m. Unity side gets three box colliders (lower crate block, top crate,
# kegs); __main__ prints their centers/sizes in Blender coords.
import importlib, math, os, random, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

CRATE = (0.47, 0.35, 0.33)      # outer size of one crate
GAP = 0.036                     # >= 2 x (PROUD + yaw corner swing): proud rails of neighbours must not meet
YAW_JITTER = 2.0                # degrees; 2 deg swings a crate corner ~1 cm
OUT_JITTER = 0.010              # crates only drift away from the block center, never into a neighbour
T = 0.02                        # wall thickness
BEV = 0.006                     # plastic edge; only on the parts that are actually seen (tri budget)
PROUD = 0.008                   # rails/rims stand this far off the wall plane (>= ZF_GAP x 4)
POST_PROUD = 0.004              # posts stand off the wall plane too: never coplanar with it
KEG_R, KEG_H = 0.20, 0.58

# Keg profile as fractions of (KEG_R, KEG_H): straight steel body (a belly reads as a wine barrel),
# rolling rings are separate hoops, recessed top.
KEG_PROFILE_FR = [
    (0.00, 0.012), (0.86, 0.012),   # bottom disk sits above the floor; the chime ring caps the base
    (0.90, 0.050), (0.90, 0.950),
    (0.86, 1.000),
    (0.55, 1.000),                  # top deck
    (0.45, 0.945), (0.20, 0.930), (0.00, 0.955),   # recessed dome, below the rim
]

JP_FONTS = [r"C:\Windows\Fonts\YuGothB.ttc", r"C:\Windows\Fonts\meiryob.ttc", r"C:\Windows\Fonts\msgothic.ttc"]


def jp_font():
    for p in JP_FONTS:
        if os.path.exists(p):
            return p
    return None


def crate(b, prefix, center_xy, z0, color, yaw=0.0, filled_top=False, label=None,
          label_color="label_red", label_font=None, label_size=0.10):
    """One Japanese plastic beer crate, built from frame parts (no booleans). Open between mid rail
    and rim; on the +-x sides that window under the rim reads as the hand-hold."""
    sx, sy, sz = CRATE
    cx, cy = center_xy
    hx, hy = sx / 2, sy / 2

    # inset: plate sides flush with the panels' outer faces would z-fight
    b.box(f"{prefix}_Plate", (sx - 0.006, sy - 0.006, T), (cx, cy), z0, color, 0)
    # posts embedded in the plate: a post bottom flush with the plate bottom would z-fight
    for i, (qx, qy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        # top ends inside the rim band: a post top flush with the rim top would z-fight
        b.box(f"{prefix}_Post{i}", (0.04, 0.04, sz - 0.034),
              (cx + qx * (hx + POST_PROUD - 0.02), cy + qy * (hy + POST_PROUD - 0.02)),
              z0 + 0.004, color, 0)

    # lower solid walls, outer face flush; they start inside the plate for the same reason
    wz0, wz1 = z0 + 0.012, z0 + 0.15
    for i, qy in enumerate((-1, 1)):
        b.box(f"{prefix}_PanelY{i}", (sx, T, wz1 - wz0), (cx, cy + qy * (hy - T / 2)), wz0, color, BEV, 1)
    for i, qx in enumerate((-1, 1)):
        b.box(f"{prefix}_PanelX{i}", (T, sy - 2 * T, wz1 - wz0), (cx + qx * (hx - T / 2), cy), wz0, color, BEV, 1)

    # mid rail and top rim, proud of the wall; +-x pieces are shortened so no two of them share a plane
    rail_d = T + PROUD
    for i, qy in enumerate((-1, 1)):
        b.box(f"{prefix}_RailY{i}", (sx, rail_d, 0.03), (cx, cy + qy * (hy + PROUD - rail_d / 2)),
              z0 + 0.15, color, 0)
    for i, qx in enumerate((-1, 1)):
        b.box(f"{prefix}_RailX{i}", (rail_d, sy - 2 * T, 0.03), (cx + qx * (hx + PROUD - rail_d / 2), cy),
              z0 + 0.15, color, 0)
    rim_d = 0.034
    rim_in = rim_d - PROUD          # how far the rim reaches inboard of the wall plane
    for i, qy in enumerate((-1, 1)):
        b.box(f"{prefix}_RimY{i}", (sx, rim_d, 0.04), (cx, cy + qy * (hy + PROUD - rim_d / 2)),
              z0 + sz - 0.04, color, BEV, 1)
    for i, qx in enumerate((-1, 1)):
        b.box(f"{prefix}_RimX{i}", (rim_d, sy - 2 * rim_in, 0.04), (cx + qx * (hx + PROUD - rim_d / 2), cy),
              z0 + sz - 0.04, color, BEV, 1)

    if filled_top:
        # 4 x 5 bottle cells; the two divider sets differ in height so their crossings are not coplanar
        ix, iy = sx - 2 * T, sy - 2 * T
        for i in range(1, 5):
            b.box(f"{prefix}_DivX{i}", (0.012, iy, 0.208), (cx - ix / 2 + i * ix / 5, cy),
                  z0 + 0.012, "crate_inner", 0)
        for i in range(1, 4):
            b.box(f"{prefix}_DivY{i}", (ix, 0.012, 0.208), (cx, cy - iy / 2 + i * iy / 4),
                  z0 + 0.008, "crate_inner", 0)

    if label:
        if label_font is not None and not os.path.exists(label_font):
            print(f"A1_BeerCrates: font yok, {prefix} etiketi atlandi ({label_font})")
        else:
            # letters sit 3 mm proud of the panel and 5 mm into it: no coplanar face with the wall
            b.text(f"{prefix}_Label", label, label_size, (cx, cy - hy + 0.001, z0 + 0.085),
                   label_color, 0.004, label_font or r"C:\Windows\Fonts\ariblk.ttf", 1)

    if yaw:
        b.transform_parts(prefix, yaw, (0, 0, 0), center_xy)


def keg(b, prefix, center_xy, z0):
    """Aluminium keg: lathed body, two dark rolling hoops, dark chime bands, spear valve on top."""
    cx, cy = center_xy
    prof = [(KEG_R * fr, z0 + KEG_H * fz) for fr, fz in KEG_PROFILE_FR]
    b.lathe(f"{prefix}_Body", prof, center_xy, "keg_steel", 24)
    for i, fz in enumerate((0.25, 0.75)):
        b.cyl(f"{prefix}_Hoop{i}", KEG_R * 0.96, 0.035, (cx, cy, z0 + KEG_H * fz), "Z", "keg_steel_dark", 24)
    b.cyl(f"{prefix}_ChimeLow", KEG_R * 0.95, 0.050, (cx, cy, z0 + 0.025), "Z", "keg_steel_dark", 24)
    b.cyl(f"{prefix}_ChimeTop", KEG_R * 0.95, 0.040, (cx, cy, z0 + KEG_H - 0.035), "Z", "keg_steel_dark", 24)
    b.cyl(f"{prefix}_ValveCollar", 0.055, 0.016, (cx, cy, z0 + KEG_H - 0.044), "Z", "keg_steel_dark", 20)
    b.cyl(f"{prefix}_Valve", 0.035, 0.040, (cx, cy, z0 + KEG_H - 0.014), "Z", "keg_black", 16)


# Layer colors: the two front crates of layer 2 are the labelled yellow ones.
LAYER1 = ["crate_orange", "crate_red", "crate_red", "crate_orange"]
LAYER2 = ["crate_yellow", "crate_yellow", "crate_red", "crate_yellow"]
CELLS = [(-(CRATE[0] + GAP) / 2, -(CRATE[1] + GAP) / 2), ((CRATE[0] + GAP) / 2, -(CRATE[1] + GAP) / 2),
         (-(CRATE[0] + GAP) / 2, (CRATE[1] + GAP) / 2), ((CRATE[0] + GAP) / 2, (CRATE[1] + GAP) / 2)]

BLOCK_HALF_X = CRATE[0] + GAP / 2
BLOCK_HALF_Y = CRATE[1] + GAP / 2
KEG_CX = BLOCK_HALF_X + 0.02 + KEG_R
KEG_CY = 0.21   # not 0.19: with r = 0.20 the two kegs would intersect each other
# pivot = bottom center of the footprint: crates reach -BLOCK_HALF_X, the kegs reach KEG_CX + KEG_R
SHIFT_X = -(-BLOCK_HALF_X + KEG_CX + KEG_R) / 2


def build(name):
    b = jp_common.Builder(name)
    rnd = random.Random(7)
    jpf = jp_font()
    if jpf is None:
        print("A1_BeerCrates: japonca font bulunamadi, ust sandik etiketi atlandi")

    for li, colors in enumerate((LAYER1, LAYER2)):
        z0 = li * CRATE[2]
        for ci, (cx, cy) in enumerate(CELLS):
            col = colors[ci]
            yaw = math.radians(rnd.uniform(-YAW_JITTER, YAW_JITTER))
            ox = math.copysign(rnd.uniform(0, OUT_JITTER), cx)
            oy = math.copysign(rnd.uniform(0, OUT_JITTER), cy)
            lbl = "BEER" if col == "crate_yellow" else None
            crate(b, f"C{li + 1}_{ci}", (cx + ox, cy + oy), z0, col, yaw,
                  filled_top=(li == 1), label=lbl, label_color="label_red")

    # top crate: interior visible, Japanese label
    crate(b, "C3_0", (0.0, 0.0), 2 * CRATE[2], "crate_red", math.radians(9), filled_top=True,
          label="ビール" if jpf else None, label_color="label_white", label_font=jpf, label_size=0.09)

    for i, qy in enumerate((-1, 1)):
        keg(b, f"Keg{i}", (KEG_CX, qy * KEG_CY), 0.0)

    b.transform_parts("", 0.0, (SHIFT_X, 0.0, 0.0))
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("A1_BeerCrates")
    print("A1_BeerCrates tris", tris, "->", jp_common.export_fbx(obj, r"Models\A1_BeerCrates.fbx"))
    # Unity box colliders, Blender coords (center / size); yawed crates get a small margin
    blk_cx = SHIFT_X
    print("COLLIDER block  center (%.3f, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (blk_cx, CRATE[2], 2 * BLOCK_HALF_X + 0.02, 2 * BLOCK_HALF_Y + 0.02, 2 * CRATE[2]))
    print("COLLIDER top    center (%.3f, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (blk_cx, 2 * CRATE[2] + CRATE[2] / 2, CRATE[0] + 0.03, CRATE[1] + 0.04, CRATE[2]))
    print("COLLIDER kegs   center (%.3f, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % (KEG_CX + SHIFT_X, KEG_H / 2, 2 * KEG_R, 2 * (KEG_CY + KEG_R), KEG_H))
