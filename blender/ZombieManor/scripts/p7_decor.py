"""Part 7: decor kit - one object per prop, built at the world origin (venue coordinates) with Unity's convention:
front = +z (venue), pivot where the prop meets its support (floor props: bottom, back edge centre or centre;
wall props: back centre on the wall; ceiling props: top). Most props are CC0 models (zm_assets), the rest are
hand-made. The kit is hidden; ZM_P7_Layout holds linked copies placed in the rooms as the first layout (Unity
owns the final placement); the street lamps and the covered car stand outside the boundary."""
import importlib
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(r"C:\UnityProjects\VortexArena", "blender", "ZombieManor", "scripts"))
import zm_common as zm
importlib.reload(zm)
import zm_rooms as zr
importlib.reload(zr)
import zm_assets as za
importlib.reload(za)
from zm_common import Part, bl, west_x, east_x, collection, clear_collection, FLOORS, ZONE_Z, CEIL
from zm_rooms import north_z, MANTEL_TOP

V = bl
ZA, ZB = ZONE_Z
F1 = FLOORS[1]
rnd = random.Random(77)

kit = collection("ZM_P7_Kit")
clear_collection(kit)
lay = collection("ZM_P7_Layout")
clear_collection(lay)
TO_FRONT = Vector((0, -1, 0))        # venue +z in Blender space (the props' front)


# ---------------------------------------------------------------- CC0 props (Poly Haven, decimated to the Quest budget)
def chandelier():
    o = za.load("Chandelier_01", "ZM_D_Chandelier", kit, 6000, pivot="ceiling")
    # the six lampshades (loose cones ~0.21 m wide, 0.15 m tall) become the self-lit Shade
    za.restyle(o, lambda s, c: s.x > 0.18 and s.y > 0.18 and 0.12 < s.z < 0.19, zm.material("Shade"))
    return o


def candelabra():
    """Five-arm brass candelabra (the tallest of the set)."""
    o = za.load("brass_candleholders", "ZM_D_Candelabra", kit, 3000, pivot="floor_c",
                pick=lambda ms: [m for m in ms if m.name.startswith("brass_candleholder_03")])
    za.glow(o, za.slot_of(o, "flame"), "flame")
    return o


def oil_lamp():
    o = za.load("vintage_oil_lamp", "ZM_D_OilLamp", kit, 2500, pivot="floor_c")
    za.glow(o, za.slot_of(o, "flame"), "flame")
    return o


def portrait():
    """Oval portrait; its cover glass is dropped (it read as a black mirror over the painting)."""
    o = za.load("hanging_picture_frame_03", "ZM_D_FramePortrait", kit, 3000, pivot="wall", scale=1.4)
    za.drop(o, "glass")
    return o


def street_lamp():
    o = za.load("street_lamp_01", "ZM_D_StreetLamp", kit, 3000, pivot="floor_c")
    za.glow(o, za.slot_of(o, "bulb"), "lamp", 6.0)
    return o


# ---------------------------------------------------------------- wall: fleshy growth (pivot = floor, on the wall plane)
def roots():
    """A fleshy mass high on the wall, veins crawling out of it over the paper, thin tendrils hanging to the floor."""
    p = Part("ZM_D_Roots", kit)
    p.smooth = True
    MH, XS = 2.1, 0.6                                                # mass centre height, half-width it may spread
    pts, edges, rad = [], [], []

    def add(x, h, r, parent, bulge=0.6):
        pts.append(V(x, r * bulge, h))                                # hugs the wall: under half the girth buried
        rad.append((r, r * 0.85))
        if parent is not None:
            edges.append((parent, len(pts) - 1))
        return len(pts) - 1

    core = add(0.0, MH, 0.1, None, bulge=0.7)
    inside = lambda x, h: (x / XS) ** 2 + ((h - MH) / 0.5) ** 2 < 1.0     # bare paper between chair rail and crown

    def vein(x, h, a, L, r, depth):
        """Thin root crawling over the wall (a tapered tube, half buried): wiggles, turns back at the patch
        edge, forks."""
        lift = rnd.uniform(0.0, 0.006)              # own offset: forked tubes never share facet planes
        path, rads, n = [V(x, r * 0.6 + lift, h)], [r], 7
        for k in range(n):
            a += rnd.uniform(-0.45, 0.45)
            nx, nh = x + math.sin(a) * L / n, h + math.cos(a) * L / n
            if not inside(nx, nh):                                    # steer back toward the mass
                a = math.atan2(-x, MH - h) + rnd.uniform(-0.6, 0.6)
                nx, nh = x + math.sin(a) * L / n * 0.5, h + math.cos(a) * L / n * 0.5
            x, h, r = nx, nh, max(r * 0.84, 0.0035)
            path.append(V(x, r * 0.6 + lift, h))
            rads.append(r)
            if depth < 2 and k in (2, 4) and rnd.random() < 0.6:
                vein(x, h, a + rnd.choice((-1, 1)) * rnd.uniform(0.5, 1.1), L * 0.5, r * 0.8, depth + 1)
        p.pipe(path, rads, "Roots", segs=6)        # even sides: a corner, never a facet, faces the wall

    for k in range(13):
        a = -math.pi + 2 * math.pi * (k + rnd.uniform(0.1, 0.9)) / 13
        vein(0.0, MH, a, rnd.uniform(0.25, 0.55), 0.03, 0)
    for k in range(8):                                                # lumps knot the mass together
        a = 2 * math.pi * k / 8 + rnd.uniform(-0.3, 0.3)
        d = rnd.uniform(0.05, 0.11)
        add(math.cos(a) * d, MH + math.sin(a) * d * 0.8, rnd.uniform(0.045, 0.075), core, bulge=0.8)
    p.skin(pts, edges, rad, "Roots", levels=1)                         # the mass only
    # tendrils hanging from under the mass close to the wall (6-12 cm out: clear of the 3.5 cm chair rail);
    # half reach the floor and curl there, half end in mid-air like dangling root tips
    for k in range(10):
        x0 = rnd.uniform(-0.1, 0.1)
        out = rnd.uniform(0.06, 0.12)
        end = 0.012 if k % 2 == 0 else rnd.uniform(0.45, 1.3)
        o0 = 0.035 + 0.004 * k                                        # own start depth: no two coincide
        path, rad_ = [V(x0, o0, MH - 0.05)], [0.012]
        for t in (0.25, 0.5, 0.75, 1.0):
            path.append(V(x0 + rnd.uniform(-0.06, 0.06) * t, o0 + out * min(1.0, t * 1.6), MH - 0.05 - (MH - 0.05 - end) * t))
            rad_.append(0.012 * (1 - 0.6 * t) if end < 0.1 else 0.012 * (1 - 0.85 * t))
        if end < 0.1:
            path.append(V(-path[-1].x + rnd.uniform(-0.12, 0.12), o0 + out + rnd.uniform(0.05, 0.15), 0.012))
            rad_.append(0.003)
        p.pipe(path, rad_, "Roots", segs=6)
    p.smooth = False
    return p.finish()


# ---------------------------------------------------------------- floor props (pivot = bottom, back-edge centre unless noted)
def typewriter():
    """Pivot: bottom centre (sits on the desk top)."""
    p = Part("ZM_D_Typewriter", kit)
    # black enamel body: low front, key bed rising to the back, open-sided frame under the carriage
    p.hull([V(x, z, h) for x in (-0.165, 0.165) for z, h in ((0.16, 0.0), (0.16, 0.03), (-0.13, 0.0), (-0.13, 0.12),
                                                              (-0.02, 0.135), (0.05, 0.06))], "CastIron")
    for sx in (-1, 1):
        p.hull([V(sx * x, z, h) for x in (0.15, 0.175) for z, h in ((-0.15, 0.003), (-0.15, 0.17), (0.0, 0.15), (0.06, 0.003))],
               "CastIron")                                                                 # side frames
    p.box(-0.045, -0.165, 0.045, -0.163, 0.04, 0.075, "Gold")                              # maker's plate on the front
    # keys: 4 staggered rows on stems, round ivory caps with a brass rim, space bar in front
    slope = lambda z: 0.055 + (0.14 - z) / 0.16 * 0.07
    for row in range(4):
        z = 0.12 - row * 0.034
        for k in range(10 - (row == 3)):
            x = -0.15 + k * 0.032 + (row % 2) * 0.012
            p.pipe([V(x, z + 0.012, slope(z) - 0.02), V(x, z, slope(z))], [0.003, 0.003], "Steel", segs=4)
            p.cyl(x, z, slope(z), slope(z) + 0.004, 0.0115, "Gold", segs=10)
            p.cyl(x, z, slope(z) + 0.004, slope(z) + 0.009, 0.0095, "Bone", segs=10)
    p.box(-0.08, 0.15, 0.08, 0.162, 0.04, 0.05, "CastIron")                               # space bar
    # type-bar basket: a fan of thin bars converging on the printing point
    for k in range(17):
        a = math.pi * (0.1 + 0.8 * k / 16)
        p.pipe([V(-math.cos(a) * 0.1, -0.03 - math.sin(a) * 0.04, 0.12), V(0, -0.075, 0.16)], [0.002, 0.002], "Steel", segs=3)
    # carriage: platen roller with knobs, paper sheet rising out of it, ribbon spools, return lever
    p.pipe([V(-0.2, -0.09, 0.19), V(0.2, -0.09, 0.19)], [0.028, 0.028], "CastIron", segs=12)
    for sx in (-1, 1):
        p.pipe([V(sx * 0.2, -0.09, 0.19), V(sx * 0.235, -0.09, 0.19)], [0.022, 0.022], "Bone", segs=10)
        p.cyl(sx * 0.1, -0.03, 0.135, 0.15, 0.032, "CastIron", segs=14)                   # ribbon spool
        p.cyl(sx * 0.1, -0.03, 0.15, 0.155, 0.02, "Gold", segs=10)
    p.obox((0, -0.115, 0.29), (0.21, 0.003, 0.2), "Paper", rot=(-14, 0, 0))
    p.pipe([V(-0.2, -0.06, 0.205), V(-0.26, -0.03, 0.22), V(-0.29, 0.03, 0.225)], [0.005] * 3, "Steel", segs=5)
    p.sphere(V(-0.292, 0.035, 0.225), 0.011, "CastIron", segs=8, rings=6)
    return p.finish()


def drape(name, build_body, size, res, drop_h, frames=90, tilt=(0.0, 0.0, 0.0)):
    """Dust sheet by cloth simulation: a size x size cloth (res x res quads) falls from drop_h onto a hidden
    body (build_body(Part) adds its colliders) and the floor; the last frame becomes the prop. Pivot: bottom
    centre. Single surface: its material (Sheet) is two-sided."""
    import bmesh
    sc = bpy.context.scene
    work = sc.collection                                   # simulated objects must be visible
    body = Part(name + "_body", work)
    build_body(body)
    body.box(-3, -3, 3, 3, -0.1, 0.0, "Fabric")            # floor
    body = body.finish()
    body.modifiers.new("col", "COLLISION")
    body.collision.thickness_outer = 0.012
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=res, y_segments=res, size=size / 2)
    me = bpy.data.meshes.new(name + "_cloth")
    bm.to_mesh(me)
    bm.free()
    cl = bpy.data.objects.new(name + "_cloth", me)
    work.objects.link(cl)
    cl.location = (0, 0, drop_h)
    cl.rotation_euler = [math.radians(a) for a in tilt]
    mod = cl.modifiers.new("cloth", "CLOTH")
    st = mod.settings
    st.quality, st.mass = 8, 0.25
    st.tension_stiffness = st.compression_stiffness = st.shear_stiffness = 12.0
    st.bending_stiffness = 0.3
    st.air_damping = 3.0                                    # settles instead of flapping
    st.tension_damping = st.compression_damping = st.shear_damping = 10.0
    cs = mod.collision_settings
    cs.distance_min = 0.008
    cs.use_self_collision = True
    cs.self_distance_min = 0.006
    mod.point_cache.frame_start, mod.point_cache.frame_end = 1, frames
    f0 = sc.frame_current
    for f in range(1, frames + 1):
        sc.frame_set(f)
    dg = bpy.context.evaluated_depsgraph_get()
    res_me = bpy.data.meshes.new_from_object(cl.evaluated_get(dg))
    res_me.transform(cl.matrix_world)
    sc.frame_set(f0)
    for o in (cl, body):
        bpy.data.objects.remove(o, do_unlink=True)
    p = Part(name, kit)
    vs = [p.bm.verts.new(v.co) for v in res_me.vertices]
    faces = [p.bm.faces.new([vs[i] for i in poly.vertices]) for poly in res_me.polygons]
    p.bm.edges.index_update()
    rim = [v for v in vs if any(e.is_boundary for e in v.link_edges)]
    for _ in range(4):                                      # the free hem crumples into teeth: relax it
        bmesh.ops.smooth_vert(p.bm, verts=rim, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bpy.data.meshes.remove(res_me)
    bpy.data.meshes.remove(me)
    p.smooth = True
    p._set(faces, "Sheet")
    p.smooth = False
    return p.finish()


def sheet_chair():
    """Dust sheet over a wing armchair (front = +z)."""
    def body(b):
        b.hull([V(x, z, h) for x in (-0.34, 0.34) for z, h in ((-0.32, 0.0), (0.34, 0.0), (0.34, 0.4), (-0.32, 0.46))],
               "Fabric")                                                          # seat, sloping to the front
        b.hull([V(x, z, h) for x in (-0.3, 0.3) for z, h in ((-0.36, 0.44), (-0.2, 0.44), (-0.3, 1.12), (-0.4, 1.12))],
               "Fabric")                                                          # raked back
        b.pipe([V(-0.28, -0.36, 1.16), V(0.0, -0.37, 1.2), V(0.28, -0.36, 1.16)], [0.07, 0.08, 0.07], "Fabric", segs=10)
        for sg in (-1, 1):
            b.pipe([V(sg * 0.33, -0.3, 0.66), V(sg * 0.35, 0.1, 0.66), V(sg * 0.37, 0.33, 0.62)], [0.065, 0.07, 0.075],
                   "Fabric", segs=10)                                             # rolled arms
            b.hull([V(sg * x, z, h) for x in (0.27, 0.4) for z in (-0.38, -0.2) for h in (0.62, 1.08)], "Fabric")   # wings
    return drape("ZM_D_SheetChair", body, 2.3, 42, 1.5, tilt=(4, -3, 12))


def sheet_ghost():
    """A sheet over a standing figure with its arms reaching forward (front = +z)."""
    def body(b):
        b.cyl(0, 0, 0.0, 1.32, 0.19, "Fabric", segs=16, r1=0.21)
        b.pipe([V(-0.22, 0, 1.38), V(0.22, 0, 1.38)], [0.08, 0.08], "Fabric", segs=10)       # shoulders
        b.sphere(V(0, 0.02, 1.58), 0.12, "Fabric", segs=14, rings=10)                     # head
        for sg in (-1, 1):
            b.pipe([V(sg * 0.24, 0, 1.36), V(sg * 0.22, 0.25, 1.3), V(sg * 0.17, 0.5, 1.26)], [0.05, 0.045, 0.04], "Fabric", segs=8)
    # 3.2 m cloth: the hem reaches the floor and pools (a shorter sheet hovered above the floor)
    return drape("ZM_D_SheetGhost", body, 3.2, 50, 1.95, tilt=(2, 3, 30))


def armor():
    """Pivot: bottom centre. Gothic plate armour on a display plinth, halberd in the right gauntlet (front = +z).
    Plates are separate surfaces of revolution / hulls stacked edge to edge (no shared planes), gilt edges and
    rivets pick out the lames."""
    p = Part("ZM_D_Armor", kit)
    p.box(-0.24, -0.2, 0.24, 0.2, 0.0, 0.06, "Trim")                                               # plinth
    p.box(-0.26, -0.22, 0.26, 0.22, -0.02, 0.025, "Trim")                                          # plinth foot
    p.pipe([V(0, -0.16, 0.06), V(0, -0.16, 1.2)], [0.012, 0.012], "CastIron", segs=6)               # display stand
    ring = lambda cx, cz, h, rx, rz, r, mat, n=20: p.pipe(
        [V(cx + rx * math.cos(2 * math.pi * k / n), cz + rz * math.sin(2 * math.pi * k / n), h) for k in range(n + 1)],
        [r] * (n + 1), mat, segs=5, cap=False)
    p.smooth = True
    for sg in (-1, 1):                                                                          # legs
        cx = sg * 0.105
        p.hull([V(cx + dx, z, h) for dx, z, h in ((-0.045, -0.06, 0.062), (0.045, -0.06, 0.062), (-0.04, -0.06, 0.13),
                                                  (0.04, -0.06, 0.13), (0.0, 0.19, 0.067), (-0.026, 0.12, 0.062),
                                                  (0.026, 0.12, 0.062), (0.0, 0.1, 0.12))], "Steel")   # sabaton
        for k, zz in enumerate((0.0, 0.045, 0.09)):                                             # its lames
            p.pipe([V(cx - 0.042 + 0.008 * k, zz, 0.07), V(cx, zz + 0.01, 0.128 - 0.012 * k), V(cx + 0.042 - 0.008 * k, zz, 0.07)],
                   [0.005] * 3, "Steel", segs=5)
        p.lathe(cx, 0.0, [(0.1, 0.05), (0.18, 0.056), (0.3, 0.066), (0.42, 0.057), (0.49, 0.053)], "Steel", segs=16, squash=0.9)
        p.pipe([V(cx, 0.047, 0.12), V(cx, 0.06, 0.3), V(cx, 0.052, 0.48)], [0.006] * 3, "Steel", segs=5)   # shin ridge
        p.lathe(cx, 0.008, [(0.49, 0.06), (0.53, 0.071), (0.58, 0.062)], "Steel", segs=16, squash=0.95)     # knee cop
        for side in (-1, 1):                                                                    # its fans
            p.hull([V(cx + side * dx, z, h) for dx, z, h in ((0.045, 0.03, 0.49), (0.045, 0.03, 0.57), (0.095, -0.005, 0.53),
                                                             (0.07, -0.02, 0.53))], "Steel")
        p.sphere(V(cx, 0.08, 0.535), 0.012, "Gold", segs=8, rings=6)                            # boss
        p.lathe(cx, 0.0, [(0.58, 0.066), (0.66, 0.07)], "Steel", segs=16, squash=0.9)            # cuisse lames
        p.lathe(cx, 0.0, [(0.66, 0.072), (0.9, 0.088)], "Steel", segs=16, squash=0.9)
    for k, (h0, r0) in enumerate(((0.86, 0.208), (0.905, 0.204), (0.95, 0.2), (0.995, 0.196))):    # fauld lames, each
        # stepping 7 mm out over the one below (closer radii put the two surfaces within the z-fight gap)
        p.lathe(0, 0, [(h0, r0), (h0 + 0.045, r0 - 0.011)], "Steel", segs=24, squash=0.75)
        for a in (-0.5, 0.0, 0.5):                                                              # rivets
            p.sphere(V(math.sin(a) * (r0 - 0.003), math.cos(a) * (r0 - 0.003) * 0.75, h0 + 0.012), 0.0065, "Gold", segs=6, rings=4)
    for sg in (-1, 1):                                                                          # tassets over the thighs
        for k in range(3):
            hh = 0.84 - 0.06 * k
            p.hull([V(sg * x, z, h) for x, z, h in ((0.03, 0.12, hh), (0.17, 0.11, hh), (0.035, 0.115 + 0.01 * k, hh - 0.075),
                                                    (0.16, 0.1 + 0.01 * k, hh - 0.075), (0.1, 0.135 + 0.01 * k, hh - 0.04),
                                                    (0.1, 0.12, hh - 0.04))], "Steel")
    p.lathe(0, 0, [(1.04, 0.168), (1.08, 0.155), (1.18, 0.178), (1.3, 0.197), (1.38, 0.182), (1.44, 0.13)], "Steel",
            segs=24, squash=0.72)                                                               # breastplate, waisted
    p.hull([V(x, z, h) for x, z, h in ((-0.13, 0.08, 1.04), (0.13, 0.08, 1.04), (-0.11, 0.1, 1.17), (0.11, 0.1, 1.17),
                                       (0.0, 0.135, 1.2), (0.0, 0.135, 1.05))], "Steel")          # plackart
    p.pipe([V(0, 0.136, 1.2), V(0, 0.142, 1.3), V(0, 0.125, 1.42)], [0.007] * 3, "Steel", segs=6)   # ridge
    ring(0, 0, 1.44, 0.132, 0.132 * 0.72, 0.008, "Gold")                                       # gilt neckline
    ring(0, 0, 1.035, 0.17, 0.17 * 0.72, 0.013, "Hide")                                       # belt
    for k, (h0, r0) in enumerate(((1.44, 0.112), (1.475, 0.104), (1.51, 0.094))):               # gorget lames
        p.lathe(0, 0, [(h0, r0), (h0 + 0.035, r0 - 0.008)], "Steel", segs=18, squash=0.9)
    for sg in (-1, 1):                                                                          # pauldrons: 3 lames
        cx = sg * 0.235
        p.lathe(cx, 0, [(1.36, 0.118), (1.42, 0.108), (1.47, 0.07), (1.495, 0.0)], "Steel", segs=18)
        p.lathe(cx, 0, [(1.31, 0.126), (1.36, 0.12)], "Steel", segs=18)
        p.lathe(cx, 0, [(1.26, 0.118), (1.31, 0.114)], "Steel", segs=18)
        ring(cx, 0, 1.262, 0.118, 0.118, 0.006, "Gold", 18)
        p.lathe(sg * 0.17, 0.11, [(1.28, 0.0), (1.285, 0.045), (1.3, 0.04), (1.31, 0.0)], "Steel", segs=14)   # rondel
    # arms: left hangs, right grips the halberd; elbow cops with fans, flared gauntlet cuffs, fingers
    hx, hz = 0.25, 0.13
    for sg, el, wr in ((-1, (-0.27, 0.02, 1.07), (-0.255, 0.12, 0.92)), (1, (0.28, 0.04, 1.11), (hx, hz - 0.01, 0.99))):
        sh = V(sg * 0.25, 0, 1.28)
        p.pipe([sh, V(*el)], [0.05, 0.044], "Steel", segs=12)                                       # rerebrace
        p.sphere(V(*el), 0.056, "Steel", segs=14, rings=8, scale=(1, 1, 0.9))                      # couter
        p.hull([Vector(V(*el)) + Vector(q) for q in ((0.0, 0.0, 0.05), (0.0, 0.0, -0.05), (sg * -0.07, 0.03, 0.0),
                                                     (sg * -0.05, -0.02, 0.0))], "Steel")          # its fan
        e, w_ = Vector(V(*el)), Vector(V(*wr))
        d = (w_ - e).normalized()
        p.pipe([e, w_ - d * 0.03], [0.044, 0.037], "Steel", segs=12)                                 # vambrace
        p.pipe([w_ - d * 0.04, w_ + d * 0.03], [0.04, 0.058], "Steel", segs=12)                      # flared cuff
        hand = w_ + d * 0.075
        p.hull([hand + Vector((x, y, z)) for x in (-0.028, 0.028) for y in (-0.03, 0.03) for z in (-0.04, 0.04)], "Steel")
        for f in range(4):                                                                      # fingers
            fz = -0.03 + 0.02 * f
            base = hand + Vector((0, 0, fz)) + d * 0.035
            p.pipe([base, base + d * 0.03 + Vector((-sg * 0.02, 0, 0)), base + Vector((-sg * 0.035, 0.0, -0.01))],
                   [0.011, 0.01, 0.009], "Steel", segs=6)
    # armet: skull with comb; sparrow-beak visor split around a dark eye slit; chin bevor; red plume
    p.lathe(0, -0.005, [(1.53, 0.09), (1.58, 0.11), (1.65, 0.12), (1.72, 0.121), (1.79, 0.105), (1.84, 0.065), (1.86, 0.0)],
            "Steel", segs=24, squash=1.12)
    p.pipe([V(0, 0.115, 1.8), V(0, 0.0, 1.885), V(0, -0.125, 1.81)], [0.011, 0.014, 0.011], "Steel", segs=6)   # comb
    p.hull([V(x, z, h) for x, z, h in ((-0.105, 0.06, 1.715), (0.105, 0.06, 1.715), (-0.1, 0.06, 1.79), (0.1, 0.06, 1.79),
                                       (0.0, 0.16, 1.73), (-0.06, 0.125, 1.75), (0.06, 0.125, 1.75))], "Steel")   # upper visor
    p.hull([V(x, z, h) for x, z, h in ((-0.105, 0.06, 1.6), (0.105, 0.06, 1.6), (-0.105, 0.06, 1.7), (0.105, 0.06, 1.7),
                                       (0.0, 0.215, 1.67), (-0.07, 0.16, 1.62), (0.07, 0.16, 1.62), (-0.06, 0.17, 1.695),
                                       (0.06, 0.17, 1.695))], "Steel")                               # beak, 1.5 cm under it
    p.hull([V(x, z, h) for x in (-0.09, 0.09) for z, h in ((0.05, 1.7), (0.11, 1.7), (0.05, 1.716), (0.1, 1.716))],
           "CastIron")                                                                          # the dark slit behind
    for k in range(5):                                                                          # breaths
        p.sphere(V(0.04 + 0.012 * k, 0.16 - 0.012 * k, 1.66 - 0.008 * k), 0.0045, "CastIron", segs=5, rings=4)
    p.hull([V(x, z, h) for x, z, h in ((-0.11, 0.0, 1.522), (0.11, 0.0, 1.522), (-0.1, 0.07, 1.6), (0.1, 0.07, 1.6),
                                       (0.0, 0.14, 1.565), (0.0, 0.1, 1.522))], "Steel")          # bevor (below the skull's rim)
    for k, (dx, ang) in enumerate(((-0.012, -0.15), (0.0, 0.0), (0.012, 0.15))):                # plume feathers
        p.hull([V(x, z, h) for x, z, h in ((dx - 0.008, -0.04, 1.86), (dx + 0.008, -0.04, 1.86),
                                           (dx + math.sin(ang) * 0.1, -0.27, 2.04 - 0.03 * k), (dx * 3, -0.38, 1.8 - 0.02 * k),
                                           (dx, -0.15, 1.9))], "Velvet")
    p.smooth = False
    # halberd: oak pole, steel langets, axe blade, back spike, top spike
    p.pipe([V(hx, hz, 0.065), V(hx, hz, 2.2)], [0.017, 0.017], "Trim", segs=8)
    p.pipe([V(hx, hz, 1.88), V(hx, hz, 2.06)], [0.02, 0.02], "Steel", segs=8)
    p.hull([V(hx + dx, z, h) for dx in (-0.004, 0.004) for z, h in ((hz, 2.17), (hz + 0.16, 2.22), (hz + 0.26, 2.14),
                                                                      (hz + 0.27, 2.03), (hz + 0.18, 1.97), (hz, 2.0))], "Steel")
    p.hull([V(hx + dx, z, h) for dx in (-0.005, 0.005) for z, h in ((hz, 2.14), (hz, 2.06), (hz - 0.18, 2.12))], "Steel")
    p.pipe([V(hx, hz, 2.2), V(hx, hz, 2.44)], [0.02, 0.002], "Steel", segs=6)
    return p.finish()


def rug():
    """Pivot: centre, on the floor. Pattern = generated Rug image over the whole top (MATS T = rug size)."""
    p = Part("ZM_D_Rug", kit)
    p.box(-1.2, -0.8, 1.2, 0.8, 0.0, 0.012, "Rug")
    for k in range(24):
        z = -0.75 + 1.5 * (k + 0.5) / 24
        for sx in (-1, 1):                                                 # fringe tassels at both short ends
            xa, xb = sorted((sx * 1.2, sx * 1.26))
            p.box(xa, z - 0.008, xb, z + 0.008, 0.001, 0.006, "Fabric")
    return p.finish()


K = {
    "chandelier": chandelier(), "candelabra": candelabra(), "oillamp": oil_lamp(), "streetlamp": street_lamp(),
    # frames have no fixed real size: scaled to the wall
    "portrait": portrait(),
    "landscape": za.load("fancy_picture_frame_01", "ZM_D_FrameLandscape", kit, 3000, pivot="wall", scale=1.6),
    "small": za.load("fancy_picture_frame_02", "ZM_D_FrameSmall", kit, 3000, pivot="wall"),
    "clock": za.load("vintage_grandfather_clock_01", "ZM_D_Clock", kit, 6000, fmt="fbx"),   # its glTF breaks the importer
    "bed": za.load("GothicBed_01", "ZM_D_Bed", kit, 8000),
    "desk": za.load("ClassicConsole_01", "ZM_D_Desk", kit, 5000),                       # top at 0.949 m
    "commode": za.load("GothicCommode_01", "ZM_D_Commode", kit, 4000),                  # top at 1.212 m
    "bust": za.load("marble_bust_01", "ZM_D_Bust", kit, 3000, pivot="floor_c"),
    "armchair": za.load("ArmChair_01", "ZM_D_ArmChair", kit, 4000),
    "rocker": za.load("Rockingchair_01", "ZM_D_RockingChair", kit, 4000),
    "car": za.load("covered_car", "ZM_D_CoveredCar", kit, 5000, pivot="floor_c"),
    "mirror": za.load("ornate_mirror_01", "ZM_D_Mirror", kit, 4000, pivot="wall"),
    # hand-made
    "roots": roots(), "typewriter": typewriter(), "chair": sheet_chair(), "ghost": sheet_ghost(),
    "armor": armor(), "rug": rug(),
}
kit.hide_viewport = True
kit.hide_render = True


def place(key, x, z, h, face=(0.0, 1.0), scale=1.0):
    """Linked copy of a kit prop at venue (x, z, h), its front turned toward venue direction `face`."""
    src = K[key]
    o = bpy.data.objects.new(f"{src.name}_L", src.data)
    lay.objects.link(o)
    o.location = bl(x, z, h)
    o.rotation_euler = (0, 0, math.atan2(-face[0], face[1]))
    o.scale = (scale,) * 3
    o["zm_prefab"] = src.name                                   # kit object = FBX / prefab name
    return o


s2 = 0.7071
WALL_GAP = 0.003                 # wall props hang 3 mm off the paper
RAIL = 0.04                      # floor props stand clear of the chair rail
ON = 0.002                       # small props sit 2 mm above the top they stand on
DESK_TOP, COMMODE_TOP = 0.949, 1.212
CANDLE_Z = (6.68, 7.92)          # mantel shelf ends, clear of the overmantel mirror (breast face at x 1.95)
# ---- Kat0
for x, z in ((0.8, 1.5), (0.3, 5.4), (4.7, 5.4)):
    place("chandelier", x, z, CEIL - 0.06)
place("clock", west_x(2.4) + RAIL, 2.4, 0.0, (1, 0))
place("commode", west_x(1.0) + RAIL, 1.0, 0.0, (1, 0))                                    # foyer, beside the front door
place("bust", west_x(0.9) + RAIL + 0.29, 0.9, COMMODE_TOP + ON, (1, 0))
place("oillamp", west_x(1.42) + RAIL + 0.27, 1.42, COMMODE_TOP + ON)
place("portrait", east_x(1.4) - WALL_GAP, 1.4, 1.75, (-1, 0))
place("small", -1.2, ZB - 0.075 - WALL_GAP, 1.7, (0, -1))
place("landscape", 3.1, ZB + 0.075 + WALL_GAP, 1.75, (0, 1))
place("roots", 1.6, ZB + 0.075, 0.0, (0, 1))
place("mirror", 3.8, north_z(3.8) - WALL_GAP, 1.7, (0, -1))
for zc in CANDLE_Z:
    place("candelabra", 1.95 - 0.14, zc, MANTEL_TOP + ON, (-1, 0), scale=0.75)
place("armchair", 0.45, 7.3, 0.0, (1, 0))                                                  # by the salon fire
place("chair", 5.6, 8.1, 0.0, (-0.6, -0.8))
place("ghost", 5.75, 0.25, 0.0, (-0.96, 0.29))
place("armor", -1.5, 3.05, 0.0, (1, 0))
place("armor", east_x(6.3) - 0.3, 6.3, 0.0, (-1, 0))
place("rug", 0.0, 5.3, 0.0)
# ---- Kat1
for x, z in ((0.4, 5.2), (4.4, 5.6)):
    place("chandelier", x, z, F1 + CEIL - 0.06)
# bed: headboard on the north wall between the P1 cage and the chimney breast; the room south of it stays open
place("bed", 0.83, north_z(0.83) - 0.1, F1, (0, -1))            # headboard clear of the window sill (9 cm proud)
DZ = 6.27                                                       # desk on the study's east wall, clear of the P2 cage
place("desk", east_x(DZ) - RAIL, DZ, F1, (-1, 0))
place("typewriter", east_x(DZ) - RAIL - 0.3, DZ - 0.15, F1 + DESK_TOP + ON, (-1, 0))
place("oillamp", east_x(DZ + 0.55) - RAIL - 0.3, DZ + 0.55, F1 + DESK_TOP + ON)
place("portrait", 3.8, north_z(3.8) - WALL_GAP, F1 + 1.75, (0, -1))
place("portrait", west_x(6.3) + WALL_GAP, 6.3, F1 + 1.75, (1, 0))
place("landscape", 2.64 - 0.075 - WALL_GAP, 6.05, F1 + 1.75, (-1, 0), scale=0.85)   # clear of the door casing (5.64)
place("small", 2.64 + 0.075 + WALL_GAP, 6.0, F1 + 1.7, (1, 0))
for zc in CANDLE_Z:
    place("candelabra", 1.95 - 0.14, zc, F1 + MANTEL_TOP + ON, (-1, 0), scale=0.75)
place("ghost", 5.75, 4.0, F1, (-s2, s2))                       # hem stays clear of the east wainscot
place("chair", 3.35, 6.1, F1, (0.6, -0.8))
place("rocker", 5.2, 2.6, F1, (-0.5, -0.87))                   # balcony corner, by the wall lantern
# ---- street (outside the boundary)
place("streetlamp", -0.7, -5.75, 0.0)
place("streetlamp", 4.9, -5.75, 0.0)
place("car", 4.6, -9.2, -0.15, (math.cos(math.radians(9)), math.sin(math.radians(9))))

for o in K.values():
    print(f"{o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")
print("layout copies:", len(lay.objects))
