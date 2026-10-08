# G shell: checker floor, subway-tile kitchen walls, red ceiling, skirting + cornice, and the diner
# behind the ORDER windows (x < -4.5, outside the play area, look only): painted walls with a teal
# wainscot and a low partition on the play-area edge. Placement: world origin, rotation Y 180 ->
# Blender (x, y) == world (x, z). The old shell keeps its colliders (renderers off).
import importlib, sys
SCRIPTS = r"C:\UnityProjects\VortexArena\blender\BurgerKitchen\scripts"
if SCRIPTS not in sys.path:
    sys.path.insert(0, SCRIPTS)
import bmesh
import bk_common
importlib.reload(bk_common)

KX, HY = 4.5, 4.0          # kitchen: x -4.5..4.5, z -4..4 (= play area)
DX = -7.5                  # diner back wall (world x)
CEIL = 3.2                 # visual ceiling; hood duct and lights follow it
SK, CORN = 0.15, 0.18      # skirting and cornice heights
TILE = 1 / 3               # floor checker square
WAIN = 1.25                # diner wainscot top
# pass wall between kitchen and diner, wall to wall along z: the ORDER frame (d1_order_window, posts
# at z -2.58 / 2.95, header top 2.44) and the service counters fill the middle, the wall closes
# both ends floor to ceiling and the band above the header. Players cannot reach the diner.
PASS_X0, PASS_X1 = -3.55, -3.43     # diner face, kitchen face
PASS_S, PASS_N = -2.64, 3.01        # end segments start just outside the end posts
PASS_TOP = 2.44                     # band above the ORDER header
# diner entrance: opening in the diner south wall (inside the D4 door jamb) + vestibule behind it
DOOR_X0, DOOR_X1, DOOR_H = -6.53, -5.47, 2.10
VEST_D, VEST_H = 1.4, 2.6


def floor(b):
    nx, ny = round((KX - DX) / TILE), round(2 * HY / TILE)
    for parity, color in ((0, "white"), (1, "charcoal")):
        bm = bmesh.new()
        for i in range(nx):
            for j in range(ny):
                if (i + j) % 2 != parity:
                    continue
                x0, y0 = KX - (i + 1) * TILE, -HY + j * TILE     # grid anchored at the kitchen's east wall
                bm.faces.new([bm.verts.new((x0, y0, 0)), bm.verts.new((x0 + TILE, y0, 0)),
                              bm.verts.new((x0 + TILE, y0 + TILE, 0)), bm.verts.new((x0, y0 + TILE, 0))])
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        for f in bm.faces:
            if f.normal.z < 0:
                f.normal_flip()
        b._finish(f"Floor_{color}", bm, color, 0.0, 1)


def wall(b, k, a, c, inward, diner):
    """Wall from a to c (x, y); inward = unit (nx, ny) toward the room."""
    (ax, ay), (cx2, cy2) = a, c
    nx, ny = inward
    along_x = ay == cy2
    length = abs(cx2 - ax) if along_x else abs(cy2 - ay)
    mx, my = (ax + cx2) / 2, (ay + cy2) / 2
    def strip(tag, depth, z_lo, z_hi, color, bevel=0.01):
        size = (length, depth, z_hi - z_lo) if along_x else (depth, length, z_hi - z_lo)
        b.box(f"{tag}_{k}", size, (mx + nx * depth / 2, my + ny * depth / 2), z_lo, color, bevel, 1)
    inside = (mx + nx, my + ny, 1.5)
    if diner:
        b.quad(f"Paint_{k}", [(ax, ay, WAIN + 0.06), (cx2, cy2, WAIN + 0.06), (cx2, cy2, CEIL - CORN), (ax, ay, CEIL - CORN)], "cream", inside)
        strip("Wainscot", 0.02, SK, WAIN, "teal", 0.006)
        strip("DinerStripe", 0.035, WAIN, WAIN + 0.06, "red", 0.008)
    else:
        b.quad(f"Tiles_{k}", [(ax, ay, SK), (cx2, cy2, SK), (cx2, cy2, CEIL - CORN), (ax, ay, CEIL - CORN)], "white", inside, tile=True)
    strip("Skirting", 0.03, 0.0, SK, "red_dark")
    strip("Cornice", 0.06, CEIL - CORN, CEIL, "red", 0.015)


def build(name):
    b = bk_common.Builder(name)
    floor(b)
    b.quad("Ceiling", [(DX, -HY, CEIL), (KX, -HY, CEIL), (KX, HY, CEIL), (DX, HY, CEIL)], "red", (0, 0, 0))
    wall(b, "N", (-KX, HY), (KX, HY), (0, -1), False)
    wall(b, "S", (-KX, -HY), (KX, -HY), (0, 1), False)
    wall(b, "E", (KX, HY), (KX, -HY), (-1, 0), False)
    wall(b, "DN", (DX, HY), (-KX, HY), (0, -1), True)
    # diner south wall with the entrance opening, and the vestibule behind it
    wall(b, "DS1", (DX, -HY), (DOOR_X0, -HY), (0, 1), True)
    wall(b, "DS2", (DOOR_X1, -HY), (-KX, -HY), (0, 1), True)
    lintel(b)
    vestibule(b)
    wall(b, "DW", (DX, -HY), (DX, HY), (1, 0), True)
    pass_wall(b)
    return b.bake()


def lintel(b):
    """Wall above the door opening (paint + cornice), seen from the diner."""
    y = -HY
    b.quad("Lintel", [(DOOR_X0, y, DOOR_H), (DOOR_X1, y, DOOR_H), (DOOR_X1, y, CEIL - CORN), (DOOR_X0, y, CEIL - CORN)],
           "cream", ((DOOR_X0 + DOOR_X1) / 2, 0, 2.5))
    b.box("LintelCornice", (DOOR_X1 - DOOR_X0, 0.06, CORN), ((DOOR_X0 + DOOR_X1) / 2, y + 0.03), CEIL - CORN, "red", 0.015, 1)


def vestibule(b):
    """Closed entrance hall behind the diner door (outside the play area, look only): checker floor,
    painted walls, low ceiling with a lamp disc, and an outer glass door at the end so the open
    swinging door never shows empty space."""
    x0, x1, y0, y1, h = DOOR_X0, DOOR_X1, -HY - VEST_D, -HY, VEST_H
    xm = (x0 + x1) / 2
    # floor: checker continuing the diner grid (row j here is row -(j+1) of floor(), hence j + 1)
    for parity, color in ((0, "white"), (1, "charcoal")):
        bm = bmesh.new()
        i0, i1 = int((KX - x1) // TILE), int((KX - x0) // TILE) + 1
        for i in range(i0, i1):
            for j in range(int(VEST_D // TILE) + 1):
                if (i + j + 1) % 2 != parity:
                    continue
                ax, bx_ = max(KX - (i + 1) * TILE, x0), min(KX - i * TILE, x1)
                ay, by_ = max(-HY - (j + 1) * TILE, y0), -HY - j * TILE
                if bx_ <= ax or by_ <= ay:
                    continue
                bm.faces.new([bm.verts.new((ax, ay, 0)), bm.verts.new((bx_, ay, 0)), bm.verts.new((bx_, by_, 0)), bm.verts.new((ax, by_, 0))])
        for f in bm.faces:
            if f.normal.z < 0:
                f.normal_flip()
        if bm.faces:
            b._finish(f"VestFloor_{color}", bm, color, 0.0, 1)
        else:
            bm.free()
    inside = (xm, (y0 + y1) / 2, 1.2)
    b.quad("VestCeil", [(x0, y0, h), (x1, y0, h), (x1, y1, h), (x0, y1, h)], "red", (xm, (y0 + y1) / 2, 0))
    for tag, xs in (("W", x0), ("E", x1)):
        b.quad(f"VestWall{tag}", [(xs, y0, WAIN + 0.06), (xs, y1, WAIN + 0.06), (xs, y1, h), (xs, y0, h)], "cream", inside)
        sgn = 1 if tag == "W" else -1
        b.box(f"VestWain{tag}", (0.02, VEST_D, WAIN), (xs + sgn * 0.01, (y0 + y1) / 2), 0.0, "teal", 0.006, 1)
        b.box(f"VestStripe{tag}", (0.035, VEST_D, 0.06), (xs + sgn * 0.0175, (y0 + y1) / 2), WAIN, "red", 0.008, 1)
    # top of the opening seen from inside the hall
    b.quad("VestHead", [(x0, y1, DOOR_H), (x1, y1, DOOR_H), (x1, y1, h), (x0, y1, h)], "cream", (xm, y1 - 1, 2.0))
    b.lathe("VestLamp", [(0, h - 0.03), (0.14, h - 0.03), (0.14, h + 0.001), (0, h + 0.001)], (xm, (y0 + y1) / 2), "steel", 20)
    # outer double glass door closing the hall: steel frame, dark "night" glass, push bars
    w = x1 - x0
    b.box("OuterFrame", (w, 0.08, h), (xm, y0 + 0.04), 0.0, "steel", 0.02, 1)
    for k, gx in enumerate((xm - w / 4 + 0.01, xm + w / 4 - 0.01)):
        b.box(f"OuterGlass_{k}", (w / 2 - 0.1, 0.012, h - 0.5), (gx, y0 + 0.083), 0.25, "charcoal", 0.01, 1)
        b.box(f"OuterBar_{k}", (w / 2 - 0.2, 0.04, 0.04), (gx, y0 + 0.11), 1.0, "steel_dark", 0.01, 1)


def pass_wall(b):
    t, xc = PASS_X1 - PASS_X0, (PASS_X0 + PASS_X1) / 2
    kx, dxf = PASS_X1 + 0.002, PASS_X0 - 0.002            # kitchen / diner face planes
    pieces = [  # (name, z0, z1, y0, y1)
        ("S", -HY, PASS_S, 0.0, CEIL), ("N", PASS_N, HY, 0.0, CEIL), ("Top", -HY, HY, PASS_TOP, CEIL)]
    for n, z0, z1, y0, y1 in pieces:
        b.box(f"Pass_{n}", (t, z1 - z0, y1 - y0), (xc, (z0 + z1) / 2), y0, "cream", 0.0, 1)
        ylo = max(y0, SK)
        # kitchen face: tiles; diner face: paint above the wainscot
        b.quad(f"PassTiles_{n}", [(kx, z0, ylo), (kx, z1, ylo), (kx, z1, CEIL - CORN), (kx, z0, CEIL - CORN)], "white", (0, (z0 + z1) / 2, 1.5), tile=True)
        if y0 < WAIN:
            b.box(f"PassWain_{n}", (0.02, z1 - z0, WAIN - SK), (dxf - 0.01, (z0 + z1) / 2), SK, "teal", 0.006, 1)
            b.box(f"PassStripe_{n}", (0.035, z1 - z0, 0.06), (dxf - 0.0175, (z0 + z1) / 2), WAIN, "red", 0.008, 1)
            for side, x in (("K", kx + 0.015), ("D", dxf - 0.015)):
                b.box(f"PassSkirt_{n}{side}", (0.03, z1 - z0, SK), (x, (z0 + z1) / 2), 0.0, "red_dark", 0.01, 1)
    for side, x in (("K", kx + 0.03), ("D", dxf - 0.03)):
        b.box(f"PassCornice_{side}", (0.06, 2 * HY, CORN), (x, 0), CEIL - CORN, "red", 0.015, 1)


if __name__ == "__main__":
    obj, tris = build("G_Shell")
    print("G_Shell tris", tris, "->", bk_common.export_fbx(obj, r"Models\G_Shell.fbx"))
