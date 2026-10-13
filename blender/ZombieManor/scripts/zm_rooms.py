"""Shared interior building blocks: wall lining, north-wall window, boarded side door, chimney breast with
fireplace, cage lift around a floor portal. Heights are relative to the floor `fh`; layout in venue coordinates."""
import math
import random

from mathutils import Matrix, Vector

from zm_common import bl, west_x, east_x, NW, NE, CEIL, DOOR_H, WIN_Z

SINK = 0.02


def north_z(x):
    """North boundary (inner face of the north wall) at venue x."""
    return NW[1] + (NE[1] - NW[1]) * (x - NW[0]) / (NE[0] - NW[0])


def s_x(a, b, x):
    """Distance along a->b to the point whose venue x is `x` (segments that run mostly along x)."""
    a, b = Vector(a), Vector(b)
    return (x - a.x) / (b.x - a.x) * (b - a).length


def s_z(a, b, z):
    a, b = Vector(a), Vector(b)
    return (z - a.y) / (b.y - a.y) * (b - a).length


def lining(p, a, b, center_off, face_sign, wall_t, gaps=(), trim=(False, False), crown=True, ext=(None, None), fh=0.0):
    """Wainscot (panels + chair rail + baseboard) and crown molding on one face of a wall.
    center_off: wall centre offset to the right of a->b; face_sign +1 = right face, -1 = left face.
    gaps: (s0, s1, kind) spans with no lining below door height; kind "door" = a casing covers the ends
    (layers run k cm under it), "hole" = bare opening (layers stop k cm short of the reveal).
    trim: shorten an end by the element's own depth where it meets a perpendicular lining; untrimmed ends
    run k cm into the neighbouring wall. k differs per layer, so no two layers ever end on one plane.
    ext: per end, f(depth, embed) -> metres to extend (negative = shorten); overrides trim (outside corners)."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    n = Vector((d.y, -d.x))
    L = (b - a).length
    face = center_off + face_sign * wall_t / 2
    # (front depth, back embed into the wall, h0, h1): every element's back sits at its own depth inside
    # the wall, and the panel floats 5 mm off the floor behind the baseboard, so no two layers share a plane
    elements = [(0.02, 0.004, 0.005, 0.895, "Wainscot"), (0.035, 0.008, 0.88, 0.94, "Trim"),
                (0.03, 0.012, -SINK, 0.15, "Trim")]
    if crown:
        elements.append((0.06, 0.016, CEIL - 0.12, CEIL, "Trim"))
    for k, (depth, embed, h0, h1, mat) in enumerate(elements, 1):
        st = 0.01 * k
        # +4 mm: the venue outline is skewed ~1.5 deg, a square-cut end would graze the slanted neighbour
        s0 = depth + 0.004 if trim[0] else -st
        s1 = L - depth - 0.004 if trim[1] else L + st
        if ext[0]:
            s0 = -ext[0](depth, embed)
        if ext[1]:
            s1 = L + ext[1](depth, embed)
        ops = []
        for g0, g1, kind in gaps:
            if h0 >= DOOR_H:
                continue
            g0, g1 = (g0 + st, g1 - st) if kind == "door" else (g0 - st, g1 + st)
            if g1 > s0 and g0 < s1:
                ops.append((g0 - s0, g1 - s0, h1 - h0))
        t = depth + embed
        off = face + face_sign * (depth - embed) / 2
        p.wall(tuple(a + d * s0 + n * off), tuple(a + d * s1 + n * off), fh + h0, fh + h1, mat, t=t, openings=ops)


def north_window(p, x0, x1, fh, win_z=WIN_Z):
    """Casing, sill, dark glass and muntins for an opening in the north wall (room at z < boundary)."""
    xm = (x0 + x1) / 2
    zi = north_z(xm)
    h0, h1 = fh + win_z[0], fh + win_z[1]
    p.box(x0, zi + 0.069, x1, zi + 0.081, h0, h1, "WindowView")      # night sky behind the glass
    for xa, xb in ((x0 - 0.1, x0 + 0.01), (x1 - 0.01, x1 + 0.1)):
        p.box(xa, zi - 0.04, xb, zi + 0.02, h0 - 0.08, h1 + 0.02, "Trim")
    p.box(x0 - 0.14, zi - 0.05, x1 + 0.14, zi + 0.02, h1 + 0.02, h1 + 0.14, "Trim")
    p.box(x0 - 0.12, zi - 0.09, x1 + 0.12, zi + 0.025, h0 - 0.04, h0 + 0.012, "Trim")
    p.box(xm - 0.02, zi + 0.035, xm + 0.02, zi + 0.06, h0, h1, "Trim")
    hm = (h0 + h1) / 2 + 0.0
    p.box(x0, zi + 0.03, x1, zi + 0.065, hm - 0.02, hm + 0.02, "Trim")


def fake_door(p, x_face, inward, z0, z1, fh=0.0):
    """Boarded-up panelled door flat on a side wall (x_face = wall inner face, inward = +1 if the room is at +x)."""
    X = lambda off: x_face + inward * off
    xs = lambda o0, o1: (min(X(o0), X(o1)), max(X(o0), X(o1)))
    # backs 2 cm deep in the wall: deeper than every wainscot layer's back
    for za, zb in ((z0 - 0.12, z0 + 0.0), (z1 - 0.0, z1 + 0.12)):                  # jambs
        xa, xb = xs(-0.02, 0.045)
        p.box(xa, za, xb, zb, fh - 0.03, fh + DOOR_H + 0.1, "Trim")
    xa, xb = xs(-0.02, 0.05)
    p.box(xa, z0 - 0.16, xb, z1 + 0.16, fh + DOOR_H + 0.1, fh + DOOR_H + 0.26, "Trim")   # head
    # panelled leaf: centre plane 2.5 mm out (back 2 cm in the wall), bottom clear of the wall's bottom
    panel_door(p, (X(0.0025), z0, fh + 0.004), (0, 1, 0), (0, 0, 1), (inward, 0, 0), z1 - z0, DOOR_H + 0.096,
               knobs=(1,))
    for hh, ang in ((0.7, 12), (1.35, -9), (1.95, 6)):                              # boards, resting on the leaf
        p.obox((X(0.0375), (z0 + z1) / 2, fh + hh), (0.025, z1 - z0 + 0.3, 0.15), "DoorWood", rot=(ang, 0, 0))


MANTEL_TOP = 1.21           # shelf top above the floor (candelabras stand on it)
MANTEL_HALF = 0.34          # pilaster outer edge from the opening's edge


def fireplace(p, fh, bx0, fz0, fz1, fire_h, fire_d, hearth_d=0.5, mirror=True):
    """Victorian fireplace on a west-facing breast face (x = bx0): cast-iron arched insert with brass bead
    and hood, grate with glowing coals, majolica tile cheeks, carved mantel (pilasters, corbels, frieze,
    shelf), marble hearth with tile inlay and brass fender, optional arched overmantel mirror."""
    tc = (fz0 + fz1) / 2
    B = lambda o0, o1, t0, t1, h0, h1, mat: p.box(bx0 - o1, t0, bx0 - o0, t1, fh + h0, fh + h1, mat)
    L = lambda t, o, h: bl(bx0 - o, t, fh + h)               # o = metres out of the breast face (into the room)
    N = 12

    def arch_strips(o0, o1, hw, hs, top, mat):
        """Fills the area between an arch (half-width hw, springing hs) and `top` with convex slabs."""
        arc = [(tc - hw * math.cos(math.pi * i / N), hs + hw * math.sin(math.pi * i / N)) for i in range(N + 1)]
        for (ta, ha), (tb, hb) in zip(arc, arc[1:]):
            p.slab([L(ta, o1, ha), L(tb, o1, hb), L(tb, o1, top), L(ta, o1, top)], Vector(L(0, o0, 0)) - Vector(L(0, o1, 0)), mat)

    # cast-iron insert: plate 2.5 cm proud with an arched opening, 6 cm over the breast face each side
    aw, hs, ptop = 0.27, 0.42, fire_h + 0.06
    B(0.0, 0.025, fz0 - 0.06, tc - aw, 0.025, ptop, "CastIron")
    B(0.0, 0.025, tc + aw, fz1 + 0.06, 0.025, ptop, "CastIron")
    arch_strips(0.0, 0.025, aw, hs, ptop, "CastIron")
    bead = [L(tc - aw - 0.012, 0.03, 0.02), L(tc - aw - 0.012, 0.03, hs)]
    bead += [L(tc - (aw + 0.012) * math.cos(math.pi * i / N), 0.03, hs + (aw + 0.012) * math.sin(math.pi * i / N)) for i in range(1, N)]
    bead += [L(tc + aw + 0.012, 0.03, hs), L(tc + aw + 0.012, 0.03, 0.02)]
    p.pipe(bead, [0.014] * len(bead), "Gold", segs=6)                                    # brass bead around the opening
    hood = [L(tc - (aw + 0.04) * math.cos(a), o, hs + (aw + 0.04) * math.sin(a) - dh)
            for a in [math.radians(d) for d in range(25, 160, 15)] for o, dh in ((0.024, 0.0), (0.1, 0.05))]
    p.hull(hood, "CastIron")                                                             # canopy over the arch
    for sg in (-1, 1):                                                                   # rosettes in the spandrels
        p.sphere(L(tc + sg * (aw + 0.02), 0.03, ptop - 0.08), 0.035, "Gold", segs=10, rings=6, scale=(0.5, 1, 1))

    # firebox: grate basket on legs, coals glowing in it, two logs on top
    gx0, gx1 = 0.03, fire_d - 0.08                                                       # depth inside the opening
    for t_ in [tc - 0.2 + 0.05 * k for k in range(9)]:
        p.pipe([L(t_, -gx0, 0.09), L(t_, -gx0, 0.27)], [0.008, 0.008], "CastIron", segs=5)
    B(-gx1, -gx0 + 0.01, tc - 0.22, tc + 0.22, 0.07, 0.1, "CastIron")
    for t_ in (tc - 0.2, tc + 0.2):
        B(-gx0 - 0.03, -gx0 + 0.005, t_ - 0.015, t_ + 0.015, 0.025, 0.07, "CastIron")
    B(-gx1 + 0.02, -gx0 - 0.01, tc - 0.19, tc + 0.19, 0.1, 0.16, "LampGlow")              # coal bed
    # lumps of coal, some lit: two staggered rows that never touch (identical unrotated spheres that overlap
    # end up with parallel facets in one plane)
    for k in range(14):
        row, col = k % 2, k // 2
        p.sphere(L(tc - 0.165 + col * 0.055 + row * 0.0275, -(gx0 + 0.05 if row == 0 else gx1 - 0.05), 0.17 + 0.003 * k),
                 0.022 + 0.0003 * k, "LampGlow" if k % 3 == 0 else "CastIron", segs=6, rings=4, scale=(1, 1, 0.7))
    for dt, dh in ((-0.1, 0.22), (0.08, 0.25)):
        p.pipe([L(tc + dt - 0.2, -gx0 - 0.04, dh), L(tc + dt + 0.2, -gx1 + 0.04, dh + 0.02)], [0.045, 0.04], "Bark", segs=7)

    # majolica tile cheeks between the insert and the pilasters
    # every mantel piece's back sits at its own depth (>= 2 mm apart where pieces overlap, and off the
    # wainscot's 4/8/12 mm); bottoms differ from the baseboard's -2 cm and the breast's -2.5 cm
    for t0, t1 in ((fz0 - 0.21, fz0 - 0.06), (fz1 + 0.06, fz1 + 0.21)):
        B(-0.005, 0.012, t0, t1, 0.025, ptop, "FireTile")

    # mantel (dark wood): pilasters with plinth, panel and capital; corbels; frieze with an oval boss; shelf
    for t0, t1 in ((fz0 - MANTEL_HALF, fz0 - 0.21), (fz1 + 0.21, fz1 + MANTEL_HALF)):
        # plinth/capital overhang the pilaster on both sides; on the south side only 5 mm (the breast ends 1 cm out)
        e0, e1 = (0.005, 0.01) if t1 < fz0 else (0.01, 0.01)
        B(0.0, 0.07, t0, t1, -0.022, 1.0, "Trim")
        B(-0.015, 0.09, t0 - e0, t1 + e1, -0.028, 0.18, "Trim")                          # plinth
        B(0.068, 0.08, t0 + 0.03, t1 - 0.03, 0.26, 0.9, "Trim")                          # raised panel
        B(-0.018, 0.09, t0 - e0, t1 + e1, 0.98, 1.085, "Trim")                           # capital
        p.hull([L(t, o, h) for t in (t0 + 0.015, t1 - 0.015) for o, h in ((-0.023, 1.075), (0.09, 1.075), (-0.023, 1.165), (0.19, 1.165))],
               "Trim")                                                                   # corbel
        p.pipe([L(t0 + 0.012, 0.15, 1.115), L(t1 - 0.012, 0.15, 1.115)], [0.028, 0.028], "Trim", segs=8)   # its scroll
    B(-0.009, 0.03, fz0 - 0.21, fz1 + 0.21, ptop, 1.135, "Trim")                         # frieze
    p.sphere(L(tc, 0.03, (ptop + 1.135) / 2), 0.06, "Trim", segs=12, rings=8, scale=(0.35, 1.6, 0.9))   # oval boss
    for sg in (-1, 1):                                                                   # swags from the boss
        p.pipe([L(tc + sg * 0.09, 0.032, 1.06), L(tc + sg * 0.2, 0.032, 0.99), L(tc + sg * 0.33, 0.032, 1.06)],
               [0.014, 0.018, 0.014], "Trim", segs=6)
    B(-0.012, 0.23, fz0 - MANTEL_HALF - 0.05, fz1 + MANTEL_HALF + 0.05, 1.13, 1.16, "Trim")   # cornice under the shelf
    B(-0.014, 0.26, fz0 - MANTEL_HALF - 0.08, fz1 + MANTEL_HALF + 0.08, 1.16, MANTEL_TOP, "Trim")   # shelf

    # hearth: marble slab, tile inlay 5 mm proud, brass fender with returns
    ht0, ht1 = fz0 - MANTEL_HALF - 0.02, fz1 + MANTEL_HALF + 0.02
    B(-0.03, hearth_d, ht0, ht1, -0.03, 0.03, "Stone")
    B(0.1, hearth_d - 0.06, fz0 - 0.15, fz1 + 0.15, 0.03, 0.035, "FireTile")
    fo = hearth_d - 0.03
    fender = [L(fz0 - 0.25, 0.1, 0.13), L(fz0 - 0.25, fo, 0.13), L(fz1 + 0.25, fo, 0.13), L(fz1 + 0.25, 0.1, 0.13)]
    p.pipe(fender, [0.016] * 4, "Gold", segs=6)
    for t_, o_ in ((fz0 - 0.25, fo), (fz1 + 0.25, fo), (tc, fo)):
        p.pipe([L(t_, o_, 0.025), L(t_, o_, 0.13)], [0.012, 0.012], "Gold", segs=6)

    if mirror:                                                       # arched overmantel mirror standing on the shelf
        mw, mh0, mh1 = 0.45, MANTEL_TOP + 0.005, 2.05

        def outline(w, o, h0):
            pts = [L(tc - w, o, h0), L(tc - w, o, mh1)]
            pts += [L(tc - w * math.cos(math.pi * i / N), o, mh1 + w * math.sin(math.pi * i / N)) for i in range(1, N)]
            return pts + [L(tc + w, o, mh1), L(tc + w, o, h0)]

        frame = outline(mw, 0.04, mh0 + 0.04)
        p.pipe(frame, [0.04] * len(frame), "Trim", segs=8)
        bead = outline(mw - 0.05, 0.03, mh0 + 0.075)                   # gilt bead inside the frame, over the glass
        p.pipe(bead, [0.012] * len(bead), "Gold", segs=6)
        B(-0.018, 0.07, tc - mw - 0.045, tc + mw + 0.045, mh0, mh0 + 0.07, "Trim")    # bottom rail
        glass = [L(tc - mw + 0.01, 0.012, mh0 + 0.03)] + \
                [L(tc - (mw - 0.01) * math.cos(math.pi * i / N), 0.012, mh1 + (mw - 0.01) * math.sin(math.pi * i / N)) for i in range(N + 1)] + \
                [L(tc + mw - 0.01, 0.012, mh0 + 0.03)]
        p.slab(glass, Vector(L(0, 0.003, 0)) - Vector(L(0, 0.012, 0)), "Mirror")
        p.sphere(L(tc, 0.05, mh1 + mw + 0.06), 0.05, "Trim", segs=10, rings=6, scale=(0.5, 1.2, 1))   # crest


def chimney_breast(p, fh, bx, bz, fire_z, fire_h=0.85, fire_d=0.35, paper="Wallpaper", hearth_d=0.5, mirror=True):
    """Chimney breast whose back block holds a physical column, fireplace opening west at x = bx[0],
    north end buried in the north wall. Includes its wainscot and crown; `paper` = the room's wallpaper."""
    bx0, bx1 = bx
    bz0, bz1 = bz
    fz0, fz1 = fire_z
    # blocks start below the floor and end inside the slab above: no face shared with walls, crowns or ceiling
    lo, top = fh - 0.025, fh + CEIL + 0.03
    p.box(bx0 + fire_d, bz0, bx1, bz1, lo, top, paper)                              # back block (holds the column)
    p.box(bx0, bz0, bx0 + fire_d, fz0, lo, fh + fire_h, paper)                      # jambs
    p.box(bx0, fz1, bx0 + fire_d, bz1, lo, fh + fire_h, paper)
    p.box(bx0, bz0, bx0 + fire_d, bz1, fh + fire_h, top, paper)                     # over the opening
    p.box(bx0 + fire_d - 0.03, fz0 + 0.005, bx0 + fire_d - 0.005, fz1 - 0.005, fh, fh + fire_h - 0.005, "Brick")
    fireplace(p, fh, bx0, fz0, fz1, fire_h, fire_d, hearth_d, mirror)
    # crowns: sides abut the front crown's back and stop at the north wall crown's front
    for x_face, sgn in ((bx0, -1), (bx1, 1)):
        p.box(min(x_face, x_face + sgn * 0.06), bz0 + 0.005, max(x_face, x_face + sgn * 0.06), north_z(x_face) - 0.065,
              fh + CEIL - 0.12, fh + CEIL + 0.02, "Trim")
    p.box(bx0 - 0.06, bz0 - 0.06, bx1 + 0.06, bz0 + 0.005, fh + CEIL - 0.12, fh + CEIL + 0.02, "Trim")
    # wainscot: the south face wraps both outside corners, the side faces start behind its back and stop at
    # the north wall's wainscot; the west face has wainscot only north of the mantel (it ends under the
    # pilaster), the south pilaster covers the rest up to the wrapped corner
    wrap = lambda depth, embed: depth
    after = lambda depth, embed: -(embed + 0.002)
    to_north = lambda depth, embed: -(depth + 0.004)
    lining(p, (bx0, bz0), (bx1, bz0), 0.0, 1, 0.0, crown=False, ext=(wrap, wrap), fh=fh)
    lining(p, (bx0, north_z(bx0)), (bx0, fz1 + MANTEL_HALF - 0.08), 0.0, 1, 0.0, crown=False, ext=(to_north, None), fh=fh)
    lining(p, (bx1, bz0), (bx1, north_z(bx1)), 0.0, 1, 0.0, crown=False, ext=(after, to_north), fh=fh)


def face_box(p, c, tan, n, s0, s1, o0, o1, h0, h1, mat):
    """Box on a wall face: c = venue point on the face, tan = unit direction along the face, n = outward unit
    normal; spans s0..s1 along tan, o0..o1 off the face (negative = into the wall), h0..h1 absolute."""
    c, tan, n = Vector(c), Vector(tan), Vector(n)
    mid = (o0 + o1) / 2
    p.wall(tuple(c + tan * s0 + n * mid), tuple(c + tan * s1 + n * mid), h0, h1, mat, t=o1 - o0)


def frame_box(p, O, U, V, N, u0, u1, v0, v1, n0, n1, mat):
    """Box in a local frame: O venue point (x, z, h), U/V/N venue unit vectors (x, z, h components)."""
    O, U, V, N = Vector(O), Vector(U), Vector(V), Vector(N)
    p.hull([bl(*(O + U * u + V * v + N * n)) for u in (u0, u1) for v in (v0, v1) for n in (n0, n1)], mat)


def panel_door(p, O, U, V, N, w, h, t=0.045, mat="Trim", knobs=(1, -1)):
    """Victorian four-panel door: stiles, bottom/lock/top rails, centre mullion, sunk boards with raised
    fields on both faces, brass backplate + knob (knobs: which faces, +1 = N side). O = hinge-side bottom
    corner on the door's centre plane; U along the width, V up, N through the thickness."""
    ht = t / 2
    B = lambda u0, u1, v0, v1, n0, n1, m=mat: frame_box(p, O, U, V, N, u0, u1, v0, v1, n0, n1, m)
    st, rb, rl0, rl1, rt = 0.11, 0.22, 0.93, 1.08, 0.11
    B(0, st, 0, h, -ht, ht)
    B(w - st, w, 0, h, -ht, ht)
    for v0, v1 in ((0, rb), (rl0, rl1), (h - rt, h)):
        B(st, w - st, v0, v1, -ht, ht)
    for v0, v1 in ((rb, rl0), (rl1, h - rt)):
        B(w / 2 - 0.045, w / 2 + 0.045, v0, v1, -ht, ht)                                 # mullion
        for u0, u1 in ((st, w / 2 - 0.045), (w / 2 + 0.045, w - st)):
            B(u0 - 0.012, u1 + 0.012, v0 - 0.012, v1 + 0.012, -0.1 * t, 0.1 * t)           # sunk board, edges in the frame
            # raised field with bevelled sides (a flat-faced field catches no light at this depth)
            O_, U_, V_, N_ = Vector(O), Vector(U), Vector(V), Vector(N)
            pts = []
            for n_, ins in ((0.09 * t, 0.03), (0.36 * t, 0.07)):
                for sg in (-1, 1):
                    pts += [O_ + U_ * u + V_ * v + N_ * sg * n_ for u in (u0 + ins, u1 - ins) for v in (v0 + ins, v1 - ins)]
            p.hull([bl(*q) for q in pts], mat)
    for sg in knobs:
        n0, n1 = sorted((sg * ht, sg * (ht + 0.006)))
        B(w - 0.09, w - 0.04, (rl0 + rl1) / 2 - 0.09, (rl0 + rl1) / 2 + 0.07, n0, n1, "Gold")
        c = Vector(O) + Vector(U) * (w - 0.065) + Vector(V) * ((rl0 + rl1) / 2) + Vector(N) * sg * (ht + 0.035)
        p.sphere(bl(*c), 0.026, "Gold", segs=12, rings=8)
        p.pipe([bl(*(c - Vector(N) * sg * 0.032)), bl(*c)], [0.009, 0.009], "Gold", segs=8)   # neck starts in the plate


def shutter(p, O, U, V, N, w, h, mat="ShutterPaint"):
    """Louvred shutter lying on a wall: frame (stiles, three rails) with slats tilted 40 deg out of the wall.
    O = bottom corner on the wall plane; the shutter is 3 cm deep along N."""
    B = lambda u0, u1, v0, v1, n0, n1: frame_box(p, O, U, V, N, u0, u1, v0, v1, n0, n1, mat)
    B(0, 0.05, 0, h, 0.0, 0.03)
    B(w - 0.05, w, 0, h, 0.0, 0.03)
    hm = h / 2
    for v0, v1 in ((0, 0.07), (hm - 0.03, hm + 0.03), (h - 0.07, h)):
        B(0.05, w - 0.05, v0, v1, 0.002, 0.028)
    a = math.radians(40)
    dv, dn = math.cos(a) * 0.022, math.sin(a) * 0.022             # half the slat's width in (up, out)
    pv, pn = -math.sin(a) * 0.003, math.cos(a) * 0.003           # half its thickness
    O_, U_, V_, N_ = Vector(O), Vector(U), Vector(V), Vector(N)
    for v0, v1 in ((0.07, hm - 0.03), (hm + 0.03, h - 0.07)):
        n_sl = max(1, int((v1 - v0) / 0.05))
        for k in range(n_sl):
            vc = v0 + (v1 - v0) * (k + 0.5) / n_sl
            pts = [O_ + U_ * u + V_ * (vc + s1 * dv + s2 * pv) + N_ * (0.015 + s1 * dn + s2 * pn)
                   for u in (0.045, w - 0.045) for s1 in (-1, 1) for s2 in (-1, 1)]
            p.hull([bl(*q) for q in pts], mat)


def flat_window(p, c, tan, n, w, h0, h1, shutters=True, trim="TrimPaint"):
    """Window laid flat on an exterior wall face (nothing behind it is walkable): dark glass, casing, sill,
    cross muntins and optional open shutters. Layer depths are distinct so no two faces share a plane."""
    hw = w / 2
    face_box(p, c, tan, n, -hw, hw, -0.01, 0.012, h0, h1, "Glass")
    face_box(p, c, tan, n, -hw - 0.1, -hw + 0.01, -0.02, 0.045, h0 - 0.05, h1 + 0.03, trim)
    face_box(p, c, tan, n, hw - 0.01, hw + 0.1, -0.02, 0.045, h0 - 0.05, h1 + 0.03, trim)
    face_box(p, c, tan, n, -hw - 0.14, hw + 0.14, -0.03, 0.05, h1 + 0.03, h1 + 0.15, trim)
    face_box(p, c, tan, n, -hw - 0.12, hw + 0.12, -0.025, 0.085, h0 - 0.09, h0 - 0.03, trim)
    face_box(p, c, tan, n, -0.02, 0.02, 0.008, 0.032, h0 + 0.005, h1 - 0.005, trim)
    hm = (h0 + h1) / 2
    face_box(p, c, tan, n, -hw + 0.01, hw - 0.01, 0.012, 0.04, hm - 0.02, hm + 0.02, trim)
    if shutters:
        sw = min(0.5, w / 2)
        c3, t3, n3 = Vector((c[0], c[1], 0)), Vector((tan[0], tan[1], 0)), Vector((n[0], n[1], 0))
        for s0 in (-hw - 0.12 - sw, hw + 0.12):
            shutter(p, c3 + t3 * s0 + Vector((0, 0, h0 - 0.02)), t3, (0, 0, 1), n3, sw, h1 - h0 + 0.02)


def post_lantern(p, c, h):
    """Lantern standing on a post cap: Blender-space centre `c`, base height `h` (absolute)."""
    base = Vector((c.x, c.y, h))
    p.hull([base + Vector((x, y, z)) for x in (-0.1, 0.1) for y in (-0.1, 0.1) for z in (0.0, 0.04)], "Iron")
    p.hull([base + Vector((x, y, z)) for x in (-0.075, 0.075) for y in (-0.075, 0.075) for z in (0.04, 0.3)], "LampGlow")
    for x in (-0.085, 0.085):
        for y in (-0.085, 0.085):
            p.hull([base + Vector((x + a, y + b, z)) for a in (-0.012, 0.012) for b in (-0.012, 0.012) for z in (0.035, 0.305)], "Iron")
    p.hull([base + Vector((x, y, 0.3)) for x in (-0.12, 0.12) for y in (-0.12, 0.12)] + [base + Vector((0, 0, 0.44))], "Iron")


def balustrade(p, pts, fh, rail_h=1.0, every=2.4, ends=(True, True), lamps=(), mat="TrimPaint"):
    """Turned balustrade along a venue polyline at floor `fh`: newels at the vertices and every ~`every` m.
    ends=False leaves out that end's newel (the rails then run 2 cm into whatever they meet).
    lamps: venue points; a newel standing there carries a lantern instead of a ball finial."""
    pts = [Vector(q) for q in pts]
    lamps = [Vector(q) for q in lamps]
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        d = (b - a).normalized()
        L = (b - a).length
        n = max(1, round(L / every))
        st = [L * j / n for j in range(n + 1)]
        has = [True] * (n + 1)
        if i == 0 and not ends[0]:
            has[0] = False
        if i == len(pts) - 2 and not ends[1]:
            has[-1] = False
        for j, s in enumerate(st):
            if j == 0 and i > 0:
                continue                                   # shared vertex: drawn by the previous segment
            if has[j]:
                c = a + d * s
                p.wall(tuple(c - d * 0.08), tuple(c + d * 0.08), fh - 0.03, fh + rail_h + 0.1, mat, t=0.16)
                p.wall(tuple(c - d * 0.1), tuple(c + d * 0.1), fh + rail_h + 0.1, fh + rail_h + 0.15, mat, t=0.2)
                if any((c - l).length < 0.05 for l in lamps):
                    post_lantern(p, bl(c.x, c.y), fh + rail_h + 0.15)
                else:
                    p.sphere(bl(c.x, c.y, fh + rail_h + 0.21), 0.07, mat, segs=10, rings=6)
        for j in range(n):
            # rails end 8 mm inside a newel (deeper and two rails meeting in a corner newel overlap) / 2 cm in a neighbour
            s0 = st[j] + (0.072 if has[j] else -0.02)
            s1 = st[j + 1] - (0.072 if has[j + 1] else -0.02)
            p0, p1 = tuple(a + d * s0), tuple(a + d * s1)
            p.wall(p0, p1, fh + rail_h - 0.07, fh + rail_h, mat, t=0.14)
            p.wall(p0, p1, fh + 0.06, fh + 0.12, mat, t=0.1)
            m = max(1, round((s1 - s0) / 0.15))
            hs = [0.12, 0.2, 0.3, 0.42, 0.55, 0.72, 0.85, rail_h - 0.07]
            rs = [0.028, 0.045, 0.03, 0.022, 0.03, 0.05, 0.03, 0.028]
            for q in range(m):
                c = a + d * (s0 + (s1 - s0) * (q + 0.5) / m)
                p.pipe([bl(c.x, c.y, fh + h_) for h_ in hs], rs, mat, segs=6)


def wall_decay(p, a, b, center_off, face_sign, wall_t, spots, fh=0.0, paper="Wallpaper", seed=7):
    """Bare plaster where the paper fell off, optionally with a strip peeling off the patch top and curling
    into the room. spots: (s along a->b, h centre, width, height, curl). Keep them between chair rail and crown."""
    import random
    import bmesh
    rnd = random.Random(seed)
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    nr = Vector((d.y, -d.x))
    base = a + nr * (center_off + face_sign * wall_t / 2)
    P = lambda s, h, o: bl(*(base + d * s + nr * (face_sign * o)), fh + h)    # o = metres out of the face
    bm = p.bm
    for s, h, w, ht, curl in spots:
        # torn outline: star-shaped ngon (concave allowed), 3.5 mm proud = more than ZF_GAP off the paper
        ph1, ph2 = rnd.uniform(0, 6.3), rnd.uniform(0, 6.3)
        ring = []
        for k in range(28):
            t_ = 2 * math.pi * k / 28
            r_ = max(0.45, 1 + 0.18 * math.sin(2 * t_ + ph1) + 0.12 * math.sin(3 * t_ + ph2) + rnd.uniform(-0.14, 0.06))
            ring.append((s + w / 2 * r_ * math.cos(t_), h + ht / 2 * r_ * math.sin(t_)))
        lo = [bm.verts.new(P(u, v, -0.003)) for u, v in ring]
        hi = [bm.verts.new(P(u, v, 0.0035)) for u, v in ring]
        faces = [bm.faces.new(lo), bm.faces.new(hi)]
        faces += [bm.faces.new((lo[i - 1], lo[i], hi[i], hi[i - 1])) for i in range(len(ring))]
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        p._set(faces, "Plaster")
        if not curl:
            continue
        # strip: glued 6 cm over the patch, bends ~30 deg off the wall, hangs, then rolls up at its end
        top, ws, th = h + ht * 0.4, w * 0.7, 0.0015
        Ls = min(0.55, ht * 0.9)
        La, Lb, Lc, K = 0.06, 0.08, 0.13, 26
        du = Ls / K
        sc = s + rnd.uniform(-0.12, 0.12) * w
        t_, out, down, st = 0.0, 0.0055, 0.0, []
        for k in range(K + 1):
            u = du * k
            half = ws / 2 * (1 - 0.4 * k / K) + (rnd.uniform(-0.015, 0.015) if k > K - 4 else 0.0)
            c = sc + (rnd.uniform(-0.008, 0.008) if k else 0.0)
            st.append([bm.verts.new(P(sv, top - down + sg * math.sin(t_) * th / 2, out + sg * math.cos(t_) * th / 2))
                       for sv, sg in ((c - half, 1), (c + half, 1), (c + half, -1), (c - half, -1))])
            kappa = 0.5 / Lb if La <= u < La + Lb else (2.4 / Lc if u >= Ls - Lc else 0.0)
            t_ += kappa * du
            out += math.sin(t_) * du
            down += math.cos(t_) * du
        front, back = [], []
        for q0, q1 in zip(st, st[1:]):
            front += [bm.faces.new((q0[0], q0[1], q1[1], q1[0])), bm.faces.new((q0[1], q0[2], q1[2], q1[1])),
                      bm.faces.new((q0[3], q0[0], q1[0], q1[3]))]
            back.append(bm.faces.new((q0[3], q0[2], q1[2], q1[3])))
        front += [bm.faces.new(st[0]), bm.faces.new(list(reversed(st[-1])))]
        bmesh.ops.recalc_face_normals(bm, faces=front + back)
        p.smooth = True
        p._set(front, paper)
        p._set(back, "Paper", new_part=False)                 # plain paper back shows where it rolls over
        p.smooth = False


def text_mesh(p, s, M, size, depth, mat):
    """Extruded text (Blender's built-in font) added to part p; M places local x right, y up, z out."""
    import bpy
    import bmesh
    cu = bpy.data.curves.new("zm_txt", "FONT")
    cu.body = s
    cu.size = size
    cu.extrude = depth / 2
    cu.align_x = "CENTER"
    ob = bpy.data.objects.new("zm_txt", cu)
    bpy.context.scene.collection.objects.link(ob)          # evaluated only when visible
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    me.transform(M @ Matrix.Translation((0, 0, depth / 2)))   # back face on the placement plane
    vs = [p.bm.verts.new(v.co) for v in me.vertices]
    faces = [p.bm.faces.new([vs[i] for i in poly.vertices]) for poly in me.polygons]
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    bpy.data.meshes.remove(me)
    bmesh.ops.recalc_face_normals(p.bm, faces=faces)
    return p._set(faces, mat)


def lift_cage(p, px, pz, fh, sgn, needle_deg=0.0, roof=None, top=CEIL):
    """Cage lift around a floor portal disc against the north wall. sgn = +1: side wall at px + 0.9 (east),
    boundary west; sgn = -1: mirrored. The 1 m door faces south; needle_deg tilts the floor dial's needle.
    top: height the headers reach (the rooftop shed is lower than a room ceiling)."""
    X = lambda u: px + sgn * u
    xb = west_x(pz) if sgn > 0 else east_x(pz)
    u0 = sgn * (xb - px) + 0.005           # boundary side (negative u)
    u1 = 0.9                               # open side wall
    cz0 = pz - 0.7
    cz1 = min(north_z(X(u0)), north_z(X(u1))) - 0.005      # the north wall is skewed: stay clear along the whole cage
    door = (-0.5, 0.5)

    def ub(ua, za, ubb, zb, h0, h1, mat):
        xa, xb_ = sorted((X(ua), X(ubb)))
        p.box(xa, za, xb_, zb, fh + h0, fh + h1, mat)

    FRONT = Vector((0, 1, 0))                                  # the cage front faces venue -z = Blender +y
    # text plane on the front: local x -> viewer's right (venue +x = Blender -x), y -> up, z -> out
    M_front = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

    ub(u0 + 0.035, cz0 + 0.05, u1 - 0.05, cz1 - 0.035, 0.0, 0.012, "CastIron")                 # floor plate (clear of the wainscot)
    ub(door[0] + 0.04, cz0 - 0.06, door[1] - 0.04, cz0 + 0.05, 0.0, 0.008, "Gold")              # brass threshold
    posts = ((u1, cz0), (u0 + 0.045, cz0), (door[0], cz0), (door[1], cz0), (u1, cz1 - 0.045))
    for u_, z_ in posts:
        ub(u_ - 0.04, z_ - 0.04, u_ + 0.04, z_ + 0.04, -0.03, 2.40, "CastIron")                 # posts, tops in the beams
        ub(u_ - 0.05, z_ - 0.05, u_ + 0.05, z_ + 0.05, 0.01, 0.09, "Gold")    # brass foot collar (off the wainscot's 5 mm)
        ub(u_ - 0.047, z_ - 0.047, u_ + 0.047, z_ + 0.047, 2.22, 2.27, "Gold")                  # brass neck ring

    def kick(along, a0, a1, c):
        """Dark-wood kick panel (0-0.9 m) with raised panels on both faces, brass cap rail; along = 'u' or 'z'."""
        box = (lambda s0, s1, d0, d1, h0, h1, m: ub(s0, c + d0, s1, c + d1, h0, h1, m)) if along == "u" else \
              (lambda s0, s1, d0, d1, h0, h1, m: ub(c + d0, s0, c + d1, s1, h0, h1, m))
        box(a0, a1, -0.02, 0.02, 0.0, 0.9, "Trim")
        n = max(1, round((a1 - a0) / 0.32))
        for i in range(n):
            s0 = a0 + (a1 - a0) * i / n + 0.04
            s1 = a0 + (a1 - a0) * (i + 1) / n - 0.04
            for d0, d1 in ((-0.028, -0.019), (0.019, 0.028)):
                box(s0, s1, d0, d1, 0.14, 0.76, "Trim")
        box(a0 - 0.002, a1 + 0.002, -0.03, 0.03, 0.895, 0.93, "Gold")

    def grille(pts_at, n):
        """Cast-iron bars (0.93-2.28 m) with an ornamental band of rings between them (1.95-2.15 m)."""
        for i in range(n):
            p.pipe([pts_at(i + 0.5, 0.92), pts_at(i + 0.5, 2.29)], [0.01, 0.01], "CastIron", segs=6)
        for i in range(n - 1):
            c = Vector(pts_at(i + 1.0, 2.05))
            ring = [c + Vector(pts_at(i + 1.0 + 0.45 * math.cos(2 * math.pi * k / 10) * 0.9, 0.0)) - Vector(pts_at(i + 1.0, 0.0))
                    + Vector((0, 0, 0.045 * math.sin(2 * math.pi * k / 10))) for k in range(11)]
            p.pipe(ring, [0.006] * 11, "CastIron", segs=4, cap=False)

    # bars span post edge to post edge (a bar inside a post grazes its faces); band rails are thicker than
    # the bars so no bar facet lies in a rail face
    for (ua, ubb), (ga, gb) in (((u0 + 0.035, door[0] - 0.04), (u0 + 0.085, door[0] - 0.04)),
                                ((door[1] + 0.04, u1 - 0.04), (door[1] + 0.04, u1 - 0.04))):     # front bays
        kick("u", ua, ubb, cz0)
        n = max(1, round(abs(gb - ga) / 0.1))
        grille(lambda f, h, ga=ga, gb=gb, n=n: bl(X(ga + (gb - ga) * f / n), cz0, fh + h), n)
        for hh in (1.94, 2.16):                                                                  # band rails
            ub(ga, cz0 - 0.013, gb, cz0 + 0.013, hh - 0.012, hh + 0.012, "CastIron")
    za_, zb_ = cz0 + 0.04, cz1 - 0.085
    kick("z", za_, zb_, u1)                                                                     # side bay
    n = round((zb_ - za_) / 0.1)
    grille(lambda f, h: bl(X(u1), za_ + (zb_ - za_) * f / n, fh + h), n)
    for hh in (1.94, 2.16):
        ub(u1 - 0.013, za_, u1 + 0.013, zb_, hh - 0.012, hh + 0.012, "CastIron")

    ub(u0, cz0 - 0.03, u1 + 0.03, cz0 + 0.03, 2.3, 2.42, "CastIron")                             # top beams
    ub(u1 - 0.03, cz0 + 0.03, u1 + 0.03, cz1, 2.3, 2.42, "CastIron")
    ub(u0 + 0.01, cz0 - 0.036, u1 + 0.02, cz0 - 0.029, 2.33, 2.39, "Gold")                       # brass band on the beam
    ub(u0, cz0 - 0.02, u1 + 0.02, cz0 + 0.02, 2.42, top + 0.02, "Trim")                         # headers
    ub(u1 - 0.02, cz0 + 0.02, u1 + 0.02, cz1, 2.42, top + 0.02, "Trim")
    # cage ceiling + lamp go to `roof` (own object under ArenaRoof in Unity: it sits below the admin's
    # floor-band cut and would cover the portal in the top-down view)
    r = roof or p
    xa, xb_ = sorted((X(u0), X(u1 - 0.03)))
    r.box(xa, cz0 + 0.03, xb_, cz1, fh + 2.42, fh + 2.45, "CastIron")                            # cage ceiling
    r.cyl(px, pz, fh + 2.395, fh + 2.42, 0.09, "Gold", segs=16)                                  # ceiling lamp: brass rim
    r.sphere(bl(px, pz, fh + 2.395), 0.075, "LampGlow", segs=14, rings=8, scale=(1, 1, 0.6))     # + glowing glass dome

    # floor dial above the door: brass plate (back 5 mm inside the header), enamel face, ticks, numerals, needle
    dial_c = Vector(bl(px, cz0 - 0.015, fh + 2.5))
    arc = lambda r, k, n=16: Vector((-math.cos(math.pi * k / n) * r, 0.0, math.sin(math.pi * k / n) * r))
    p.hull([dial_c + arc(0.25, k) + FRONT * dy for k in range(17) for dy in (0.0, 0.017)], "Gold")
    p.hull([dial_c + arc(0.21, k) + Vector((0, 0, 0.025)) + FRONT * dy for k in range(17) for dy in (0.015, 0.02)], "Paper")
    for k in range(11):                                                          # ticks every 15 degrees
        th_ = math.radians(-75 + 15 * k)
        d = Vector((-math.sin(th_), 0, math.cos(th_)))
        p.pipe([dial_c + Vector((0, 0, 0.025)) + d * 0.17 + FRONT * 0.021, dial_c + Vector((0, 0, 0.025)) + d * 0.2 + FRONT * 0.021],
               [0.003, 0.003], "CastIron", segs=4)
    for fl, deg in (("0", -60), ("1", 0), ("2", 60)):
        th_ = math.radians(deg)
        c = dial_c + Vector((-math.sin(th_) * 0.125, 0, 0.025 + math.cos(th_) * 0.125 - 0.025)) + FRONT * 0.02
        text_mesh(p, fl, Matrix.Translation(c) @ M_front, 0.06, 0.003, "CastIron")
    th = math.radians(needle_deg)
    hub = dial_c + Vector((0, 0, 0.025)) + FRONT * 0.028
    p.pipe([hub, hub + Vector((-math.sin(th) * 0.19, 0, math.cos(th) * 0.19))], [0.008, 0.003], "Gold", segs=4)
    p.sphere(hub + FRONT * 0.004, 0.02, "Gold", segs=10, rings=6, scale=(1, 0.5, 1))

    # call panel on the jamb post's front: brass plate, three ivory buttons with floor numbers, lit indicator
    ub(door[1] - 0.038, cz0 - 0.052, door[1] + 0.038, cz0 - 0.035, 1.05, 1.42, "Gold")
    for i, hh in enumerate((1.14, 1.22, 1.3)):
        b0 = Vector(bl(X(door[1] + 0.012), cz0 - 0.05, fh + hh))
        p.pipe([b0, b0 + FRONT * 0.014], [0.014, 0.014], "Bone", segs=10)
        text_mesh(p, str(i), Matrix.Translation(Vector(bl(X(door[1] - 0.018), cz0 - 0.052, fh + hh - 0.012)) + FRONT * 0.001)
                  @ M_front, 0.026, 0.002, "CastIron")
    p.sphere(Vector(bl(X(door[1]), cz0 - 0.052, fh + 1.37)), 0.014, "LampGlow", segs=8, rings=6, scale=(1, 0.6, 1))

    # scissor gate folded against that jamb: hung from a brass track under the beam, standing on the threshold
    ub(door[0] + 0.04, cz0 + 0.025, door[1] - 0.04, cz0 + 0.045, 2.255, 2.3, "Gold")             # top track
    gz = cz0 + 0.035
    bars = [door[1] - 0.06 - 0.017 * k for k in range(8)]
    for ug in bars:
        ub(ug - 0.004, gz - 0.008, ug + 0.004, gz + 0.008, 0.012, 2.25, "CastIron")
    for a, b in zip(bars, bars[1:]):                                             # lazy-tong straps, crossing
        for k in range(9):
            h0 = 0.08 + 0.24 * k
            p.pipe([bl(X(a), gz, fh + h0), bl(X(b), gz, fh + h0 + 0.22)], [0.0035, 0.0035], "CastIron", segs=4)
            p.pipe([bl(X(b), gz + 0.003, fh + h0), bl(X(a), gz + 0.003, fh + h0 + 0.22)], [0.0035, 0.0035], "CastIron", segs=4)
    p.sphere(Vector(bl(X(bars[-1]), gz - 0.02, fh + 1.1)), 0.022, "Gold", segs=8, rings=6)          # pull knob
