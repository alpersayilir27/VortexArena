# B1 yatai: wooden oden street-food stall for the Tokyo back alley. Front (customer side) faces -Y,
# pivot at the bottom center of the 2.0 x 1.0 footprint, z=0 floor, 2.05 m tall.
# Only the roof may pass the footprint (2.2 x 1.2, above head height). Unity side gets two box
# colliders (cart body + counter, roof slab); __main__ prints them in Blender coords.
import importlib, math, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\JapanProps\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import jp_common
importlib.reload(jp_common)

FOOT = (2.0, 1.0)
FRONT_Y = -0.5                  # footprint front edge; nothing but the roof passes it
BODY_Y, BODY_D = 0.07, 0.80     # cart body center / depth: pushed back so the deck can be proud
BODY_W, BODY_H = 1.86, 0.71     # narrower than the footprint: the wheels live in that 7 cm
DECK = (1.98, 0.86, 0.14)       # proud band over the body, under the counter
COUNTER = (2.00, 0.95, 0.05)
COUNTER_Z = 0.85
SKIRT_FRONT = -0.49             # front skirt plane; the menu decal stands 3.5 mm off it
PLANKS = 8
WHEEL_R, WHEEL_T, WHEEL_X, WHEEL_Z = 0.32, 0.06, 0.965, 0.34
POST, POST_TOP = 0.07, 1.95
POST_X, POST_YF, POST_YB = 0.93, -0.44, 0.40
EAVE_BOT, EAVE_TOP, RIDGE_BOT, RIDGE_TOP = 1.90, 1.94, 2.00, 2.04
ROOF_X, ROOF_Y = 1.10, 0.60     # roof half sizes: the allowed 2.2 x 1.2 overhang
BEAM_Z, BEAM_H = 1.74, 0.14
# 5 flaps = the 5 panels painted in the noren art, so the slits line up with the gaps
NOREN_W, NOREN_H, NOREN_FLAPS, NOREN_GAP = 1.4, 0.42, 5, 0.02
NOREN_Y = -0.47
# lanterns beside the narrower noren, front-most point stays inside the footprint (y -0.49)
LANT_R, LANT_H, LANT_X, LANT_Y, LANT_Z = 0.13, 0.36, 0.84, -0.36, 1.36
SPOKES = 6
MENU_WH = (0.36, 0.30)          # menu art is ~1.2:1
BEV = 0.008

# Lantern profile as fractions of (LANT_R, LANT_H): chochin belly, flat paper ends
LANT_FR = [(0.00, 0.00), (0.34, 0.00), (0.52, 0.06), (0.80, 0.20), (1.00, 0.50),
           (0.80, 0.80), (0.52, 0.94), (0.34, 1.00), (0.00, 1.00)]
# Bottle profile as fractions of (0.035, 0.26)
BOTTLE_FR = [(0.00, 0.00), (0.95, 0.00), (1.00, 0.06), (1.00, 0.55), (0.45, 0.70),
             (0.38, 0.95), (0.30, 1.00), (0.00, 1.00)]
LANT_KEYS = ("lantern_oden", "lantern_oden")


def cart(b):
    """Body, plank cladding, deck band, counter and the skirt that carries the menu decal."""
    b.box("Body", (BODY_W, BODY_D, BODY_H), (0, BODY_Y), 0.0, "wood_dark", BEV, 1, tile="Wood")
    b.box("Deck", DECK, (0, BODY_Y), BODY_H, "wood_dark", BEV, 1, tile="Wood")
    b.box("Counter", COUNTER, (0, FRONT_Y + COUNTER[1] / 2), COUNTER_Z, "wood_dark", BEV, 1, tile="Wood")

    # vertical planks on the customer side: 1.3 cm proud of the body face, clear of the deck front
    face_y = BODY_Y - BODY_D / 2
    pitch = BODY_W / PLANKS
    for i in range(PLANKS):
        b.box(f"Plank{i}", (pitch - 0.018, 0.018, 0.65), (-BODY_W / 2 + pitch * (i + 0.5), face_y - 0.004),
              0.03, "wood_dark", 0.004, 1, tile="Wood")

    # skirt hangs off the counter underside; its top face is back to back with the counter bottom
    b.box("Skirt", (1.90, 0.04, COUNTER_Z - 0.50), (0, SKIRT_FRONT + 0.02), 0.50,
          "wood_dark", 0.004, 1, tile="Wood")
    b.decal("MenuBoard", MENU_WH, (0.0, SKIRT_FRONT - 0.0035, 0.675), "menu_board")


def wheel(b, prefix, sx):
    """Spoked cart wheel at an x end: segmented wooden felloe with an iron tyre, spokes and hub."""
    x = sx * WHEEL_X
    seg = 12
    rim_in, rim_out = WHEEL_R - 0.05, WHEEL_R
    chord = 2 * rim_out * math.sin(math.pi / seg) + 0.012   # overlap so the ring reads closed
    for k in range(seg):
        a = 2 * math.pi * (k + 0.5) / seg
        r = (rim_in + rim_out) / 2
        cy, cz = BODY_Y + r * math.cos(a), WHEEL_Z + r * math.sin(a)
        # felloe block: tangent along the ring, radial depth 5 cm, rotated about X
        # neighbours overlap at the joints: alternate thickness so their side faces never share a plane
        t = WHEEL_T if k % 2 == 0 else WHEEL_T - 0.006
        b.box(f"{prefix}_Felloe{k}", (t, chord, rim_out - rim_in), (x, cy), cz - (rim_out - rim_in) / 2,
              "wood_dark", 0.004, 1, rot_x=-math.degrees(a) + 90.0, tile="Wood")
        b.box(f"{prefix}_Tyre{k}", (WHEEL_T - 0.016 - (k % 2) * 0.006, chord + 0.004, 0.012), (x, BODY_Y + (rim_out + 0.006) * math.cos(a)),
              WHEEL_Z + (rim_out + 0.006) * math.sin(a) - 0.006, "keg_black", 0, 1, rot_x=-math.degrees(a) + 90.0)
    for k in range(SPOKES):
        a = 2 * math.pi * k / SPOKES
        r = (0.06 + rim_in + 0.01) / 2
        b.box(f"{prefix}_Spoke{k}", (WHEEL_T - 0.022, 0.035, rim_in + 0.01 - 0.06),
              (x, BODY_Y + r * math.cos(a)), WHEEL_Z + r * math.sin(a) - (rim_in - 0.05) / 2,
              "wood_dark", 0.003, 1, rot_x=-math.degrees(a) + 90.0, tile="Wood")
    b.cyl(f"{prefix}_Hub", 0.07, WHEEL_T + 0.01, (x, BODY_Y, WHEEL_Z), "X", "keg_steel_dark", 16, 0.006, 1)


def roof(b):
    """Gabled tin roof: two hull slabs meeting at the ridge, plus the ridge cap and the front beam."""
    for i, sy in enumerate((-1, 1)):
        pts = []
        for sx in (-1, 1):
            pts += [(sx * ROOF_X, sy * ROOF_Y, EAVE_BOT), (sx * ROOF_X, sy * ROOF_Y, EAVE_TOP),
                    (sx * ROOF_X, 0.0, RIDGE_BOT), (sx * ROOF_X, 0.0, RIDGE_TOP)]
        b.hull(f"Roof{i}", pts, "tin_dark", 0.004, 1, tile="RoofTin")
    # cap is 1 cm narrower than the slabs: flush x faces would z-fight along the ridge
    b.box("RidgeCap", (2 * ROOF_X - 0.02, 0.16, 0.07), (0, 0.0), 1.98, "wood_dark", BEV, 1, tile="Wood")

    for i, (px, py) in enumerate(((-POST_X, POST_YF), (POST_X, POST_YF),
                                  (-POST_X, POST_YB), (POST_X, POST_YB))):
        b.box(f"Post{i}", (POST, POST, POST_TOP - 0.90), (px, py), 0.90, "wood_dark", 0.004, 1, tile="Wood")
    b.box("Beam", (1.98, 0.08, BEAM_H), (0, FRONT_Y + 0.045), BEAM_Z, "wood_dark", BEV, 1, tile="Wood")


def noren(b):
    """Noren curtain under the front beam: one decal flap per slice, two sided so it reads from inside."""
    step = (NOREN_W - (NOREN_FLAPS - 1) * NOREN_GAP) / NOREN_FLAPS + NOREN_GAP
    fw = step - NOREN_GAP
    for i in range(NOREN_FLAPS):
        x0 = -NOREN_W / 2 + i * step
        b.decal(f"Noren{i}", (fw, NOREN_H), (x0 + fw / 2, NOREN_Y, BEAM_Z - NOREN_H / 2), "noren_oden",
                two_sided=True, sub=((x0 + NOREN_W / 2) / NOREN_W, 0.0, (x0 + fw + NOREN_W / 2) / NOREN_W, 1.0))


def lantern(b, prefix, sx, key):
    """Red chochin on a short cord under the eave: wrapped lathe body with black paper caps."""
    cx = sx * LANT_X
    prof = [(LANT_R * fr, LANT_Z + LANT_H * fz) for fr, fz in LANT_FR]
    b.lathe(f"{prefix}_Body", prof, (cx, LANT_Y), "label_red", 20, wrap_key=key)
    b.cyl(f"{prefix}_CapTop", 0.055, 0.03, (cx, LANT_Y, LANT_Z + LANT_H - 0.005), "Z", "keg_black", 16)
    b.cyl(f"{prefix}_CapBot", 0.055, 0.03, (cx, LANT_Y, LANT_Z + 0.01), "Z", "keg_black", 16)
    b.cyl(f"{prefix}_Cord", 0.008, 0.29, (cx, LANT_Y, LANT_Z + LANT_H + 0.155), "Z", "keg_black", 8)


def counter_items(b):
    """Oden pot with its lid and a few bottles standing on the counter."""
    top = COUNTER_Z + COUNTER[2]
    b.cyl("Pot", 0.22, 0.18, (-0.55, 0.05, top + 0.09), "Z", "keg_steel", 24, 0.01, 1)
    b.cyl("PotLid", 0.225, 0.03, (-0.55, 0.05, top + 0.195), "Z", "keg_steel_dark", 24, 0.006, 1)
    b.cyl("PotKnob", 0.03, 0.04, (-0.55, 0.05, top + 0.23), "Z", "keg_steel_dark", 12)
    for i, (bx, by) in enumerate(((0.52, 0.18), (0.66, 0.22), (0.79, 0.16))):
        prof = [(0.035 * fr, top + 0.26 * fz) for fr, fz in BOTTLE_FR]
        b.lathe(f"Bottle{i}", prof, (bx, by), "glass_green", 12)


def build(name):
    b = jp_common.Builder(name)
    cart(b)
    for i, sx in enumerate((-1, 1)):
        wheel(b, f"Wheel{i}", sx)
    roof(b)
    noren(b)
    for i, sx in enumerate((-1, 1)):
        lantern(b, f"Lantern{i}", sx, LANT_KEYS[i])
    counter_items(b)
    return b.bake()


if __name__ == "__main__":
    obj, tris = build("B1_Yatai")
    print("B1_Yatai tris", tris, "->", jp_common.export_fbx(obj, r"Models\B1_Yatai.fbx"))
    # Unity box colliders, Blender coords (center / size)
    print("COLLIDER body center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % ((COUNTER_Z + COUNTER[2]) / 2, FOOT[0], FOOT[1], COUNTER_Z + COUNTER[2]))
    print("COLLIDER roof center (0.000, 0.000, %.3f) size (%.3f, %.3f, %.3f)"
          % ((EAVE_BOT + RIDGE_TOP) / 2, 2 * ROOF_X, 2 * ROOF_Y, RIDGE_TOP - EAVE_BOT))
