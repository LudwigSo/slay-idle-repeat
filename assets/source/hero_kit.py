"""Procedural source for `chr_hero_rogue` and its swappable weapon kit.

The hero is modelled in Python rather than sculpted, so every form here is
rebuildable. Run this against `assets/source/hero_rogue.blend`, which already
holds the body, the head and the painterly materials this pass reuses.

Three passes, each idempotent -- a rerun deletes what it made and remakes it:

    detail()   extra trim on the existing body: hood brim, collar, pauldron
               rims, rivets, boot cuffs and soles, knee wraps, belt furniture
    sockets()  removes the daggers that were welded into the hero and leaves
               two empties behind in their place, so weapons become swappable
    weapons()  builds `wpn_sword` and `wpn_dagger` in a canonical grip frame

Conventions
-----------
* +X is the hero left, -Y is the direction they face, +Z is up.
* Object names carry a pass prefix (`DTL_`, `Socket_`, `WPN_`) so a pass can find
  and clear its own output without touching anything else.
* NOTHING IS METALLIC AND NOTHING MAY BE. The build lights 3D with one
  directional key, one fill and flat ambient -- no reflection probe, no sky --
  so a true metal has nothing to reflect and renders black. Brass and blades
  are bright albedo at low roughness instead; the gloss comes from the light.
"""

import math

import bmesh
import bpy
from mathutils import Vector

HERO_COLL = "Hero"
WEAPON_COLL = "Weapons"
DETAIL_TAG = "DTL_"
SOCKET_TAG = "Socket_"
WEAPON_TAG = "WPN_"


# --------------------------------------------------------------------------- #
# geometry helpers
# --------------------------------------------------------------------------- #

def link(ob, coll_name):
    coll = bpy.data.collections.get(coll_name) or bpy.data.collections.new(coll_name)
    if coll.name not in {c.name for c in bpy.context.scene.collection.children}:
        bpy.context.scene.collection.children.link(coll)
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    coll.objects.link(ob)
    return ob


def mesh(name, verts, faces, mat=None, coll=HERO_COLL, smooth=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    me.validate()
    me.update()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    if mat is not None:
        ob.data.materials.append(mat if hasattr(mat, "node_tree") else bpy.data.materials[mat])
    return link(ob, coll)


def stitch(rings, closed_path=False, cap_start=False, cap_end=False):
    """Quads joining equal-length rings of points, plus optional end caps."""
    n = len(rings[0])
    faces = []
    last = len(rings) if closed_path else len(rings) - 1
    for i in range(last):
        a, b = (i % len(rings)) * n, ((i + 1) % len(rings)) * n
        for j in range(n):
            k = (j + 1) % n
            faces.append((a + j, a + k, b + k, b + j))
    if cap_start:
        faces.append(tuple(range(n - 1, -1, -1)))
    if cap_end:
        o = (len(rings) - 1) * n
        faces.append(tuple(range(o, o + n)))
    return faces


def loft(name, rings, mat=None, cap_start=True, cap_end=True, coll=HERO_COLL,
         closed_path=False, smooth=True):
    verts = [p for r in rings for p in r]
    return mesh(name, verts, stitch(rings, closed_path, cap_start, cap_end),
                mat, coll, smooth)


def ring(n, rx, ry, z, cx=0.0, cy=0.0, phase=0.0):
    return [Vector((cx + rx * math.cos(phase + 2 * math.pi * i / n),
                    cy + ry * math.sin(phase + 2 * math.pi * i / n), z)) for i in range(n)]


def sweep(name, path, radius, sides=10, mat=None, coll=HERO_COLL, closed=True):
    """Run a circular profile along a path, parallel-transporting the frame so
    the tube does not corkscrew. `radius` may be a float or f(t) for a taper."""
    n = len(path)
    tangents = []
    for i in range(n):
        a = path[(i - 1) % n] if closed else path[max(i - 1, 0)]
        b = path[(i + 1) % n] if closed else path[min(i + 1, n - 1)]
        tangents.append((b - a).normalized())
    ref = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(ref)) > 0.9:
        ref = Vector((0.0, 1.0, 0.0))
    nrm = (ref - tangents[0] * ref.dot(tangents[0])).normalized()
    rings = []
    for i, t in enumerate(tangents):
        nrm = (nrm - t * nrm.dot(t)).normalized()
        binorm = t.cross(nrm)
        r = radius(i / n) if callable(radius) else radius
        rings.append([path[i] + nrm * (r * math.cos(2 * math.pi * j / sides))
                      + binorm * (r * math.sin(2 * math.pi * j / sides)) for j in range(sides)])
    verts = [p for rg in rings for p in rg]
    return mesh(name, verts, stitch(rings, closed_path=closed,
                                    cap_start=not closed, cap_end=not closed), mat, coll)


def dome(name, centre, normal, radius, height, mat, sides=10, coll=HERO_COLL):
    """A squashed dome sitting on a surface -- the shape every rivet and stud is."""
    nz = Vector(normal).normalized()
    ax = Vector((0.0, 0.0, 1.0))
    if abs(nz.dot(ax)) > 0.9:
        ax = Vector((1.0, 0.0, 0.0))
    nx = (ax - nz * ax.dot(nz)).normalized()
    ny = nz.cross(nx)
    rings, steps = [], 4
    for s in range(steps + 1):
        a = (math.pi / 2) * s / steps
        r, h = radius * math.cos(a), height * math.sin(a)
        rings.append([Vector(centre) + nx * (r * math.cos(2 * math.pi * j / sides))
                      + ny * (r * math.sin(2 * math.pi * j / sides)) + nz * h
                      for j in range(sides)])
    return loft(name, rings, mat, cap_start=True, cap_end=False, coll=coll)


def bevel(ob, width=0.01, segments=2):
    m = ob.modifiers.new("Bevel", "BEVEL")
    m.width, m.segments = width, segments
    m.limit_method = "ANGLE"
    return m


# --------------------------------------------------------------------------- #
# measuring the body we are decorating
# --------------------------------------------------------------------------- #

def evaluated_points(name):
    dg = bpy.context.evaluated_depsgraph_get()
    ob = bpy.data.objects[name]
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [ob.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return pts


def section(name, z, centre, bins=24, tol=0.035, grow=0.0, keep=None):
    """The outer silhouette of a mesh in a thin slab at height `z`, as a ring of
    `bins` points around `centre`. Empty bins borrow their nearest neighbour, so
    a sparse mesh still yields a closed ring."""
    cx, cy = centre
    pts = [p for p in evaluated_points(name) if abs(p.z - z) < tol]
    if keep:
        pts = [p for p in pts if keep(p)]
    best = {}
    for p in pts:
        a = math.atan2(p.y - cy, p.x - cx) % (2 * math.pi)
        b = int(a / (2 * math.pi) * bins) % bins
        r = math.hypot(p.x - cx, p.y - cy)
        if b not in best or r > best[b]:
            best[b] = r
    if not best:
        raise ValueError("no {} geometry within {} of z={}".format(name, tol, z))
    out = []
    for b in range(bins):
        if b in best:
            r = best[b]
        else:
            near = min(best, key=lambda k: min(abs(k - b), bins - abs(k - b)))
            r = best[near]
        a = 2 * math.pi * b / bins
        out.append(Vector((cx + (r + grow) * math.cos(a), cy + (r + grow) * math.sin(a), z)))
    return out


def band(name, target, z0, z1, centre, mat, grow=0.012, bins=24, bulge=0.0, keep=None):
    """A strap hugging `target` between two heights."""
    rings = []
    for t, z in ((0.0, z0), (0.5, (z0 + z1) / 2), (1.0, z1)):
        g = grow + (bulge if t == 0.5 else 0.0)
        rings.append(section(target, z, centre, bins=bins, grow=g, keep=keep))
    return loft(name, rings, mat, cap_start=True, cap_end=True)


def ordered_boundary(name, pick=None):
    """Ordered world-space boundary loops of the *base* mesh of an object."""
    ob = bpy.data.objects[name]
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    adj = {}
    for e in bm.edges:
        if len(e.link_faces) != 1:
            continue
        a, b = e.verts
        adj.setdefault(a.index, []).append(b.index)
        adj.setdefault(b.index, []).append(a.index)
    co = {v.index: ob.matrix_world @ v.co.copy() for v in bm.verts}
    bm.free()
    loops, seen = [], set()
    for start in adj:
        if start in seen:
            continue
        loop, prev, cur = [start], None, start
        seen.add(start)
        while True:
            nxt = next((k for k in adj[cur] if k != prev and k not in seen), None)
            if nxt is None:
                break
            loop.append(nxt)
            seen.add(nxt)
            prev, cur = cur, nxt
        loops.append([co[i] for i in loop])
    return loops if pick is None else max(loops, key=pick)


def surface(name, origin, direction, back_off=0.0):
    """Raycast an evaluated object; returns (point, normal) in world space.
    Placing trim by raycast beats solving the shapes of the body by hand."""
    src = bpy.data.objects[name]
    dg = bpy.context.evaluated_depsgraph_get()
    mw = src.matrix_world
    inv = mw.inverted()
    d = (inv.to_3x3() @ Vector(direction)).normalized()
    hit, loc, nor, _ = src.evaluated_get(dg).ray_cast(inv @ Vector(origin), d)
    if not hit:
        raise ValueError("ray missed {} from {}".format(name, tuple(origin)))
    p = mw @ loc
    n = (mw.to_3x3() @ nor).normalized()
    return p + n * back_off, n


def resample(path, count):
    """Even-arclength resampling of a closed polyline."""
    n = len(path)
    segs = [(path[i], path[(i + 1) % n]) for i in range(n)]
    lens = [(b - a).length for a, b in segs]
    total = sum(lens)
    out, acc, i = [], 0.0, 0
    for k in range(count):
        want = total * k / count
        while i < len(lens) - 1 and acc + lens[i] < want:
            acc += lens[i]
            i += 1
        a, b = segs[i]
        out.append(a.lerp(b, (want - acc) / lens[i] if lens[i] else 0.0))
    return out


# --------------------------------------------------------------------------- #
# materials
# --------------------------------------------------------------------------- #

def painterly(name, col_a, col_b, rough, spread=0.14, big=5.0, grain=("noise", 120.0),
              bump=0.10):
    """The two-tone painterly shader of the hero: object-space noise breaks the
    base colour between two poles, and a fine grain drives bump and roughness."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    bsdf = N["Principled BSDF"]
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = rough
    tc = N.new("ShaderNodeTexCoord"); tc.name = tc.label = "PT_tc"; tc.location = (-1100, 0)
    nb = N.new("ShaderNodeTexNoise"); nb.name = nb.label = "PT_big"; nb.location = (-900, 220)
    nb.inputs["Scale"].default_value = big
    nb.inputs["Detail"].default_value = 6.0
    nb.inputs["Roughness"].default_value = 0.62
    rp = N.new("ShaderNodeValToRGB"); rp.name = rp.label = "PT_ramp"; rp.location = (-700, 220)
    rp.color_ramp.elements[0].position = 0.33
    rp.color_ramp.elements[1].position = 0.68
    mx = N.new("ShaderNodeMix"); mx.name = mx.label = "PT_mix"; mx.location = (-400, 220)
    mx.data_type = "RGBA"
    rgba = [s for s in mx.inputs if s.type == "RGBA"]
    rgba[0].default_value = (col_a[0], col_a[1], col_a[2], 1.0)
    rgba[1].default_value = (col_b[0], col_b[1], col_b[2], 1.0)
    gtype, gscale = grain
    gn = N.new("ShaderNodeTexVoronoi" if gtype == "voronoi" else "ShaderNodeTexNoise")
    gn.name = gn.label = "PT_grain"; gn.location = (-900, -220)
    gn.inputs["Scale"].default_value = gscale
    if gtype == "voronoi":
        gn.feature = "F1"
        # Smoothness is only a socket on the smooth features, and which
        # features carry it has moved between Blender releases -- ask rather
        # than assume, or building a voronoi grain raises on a version that
        # hides it behind F1.
        if "Smoothness" in gn.inputs:
            gn.inputs["Smoothness"].default_value = 1.0
        gout = gn.outputs["Distance"]
    else:
        gn.inputs["Detail"].default_value = 4.0
        gout = gn.outputs["Fac"]
    bp = N.new("ShaderNodeBump"); bp.name = bp.label = "PT_bump"; bp.location = (-400, -320)
    bp.inputs["Strength"].default_value = bump
    bp.inputs["Distance"].default_value = 0.012
    mr = N.new("ShaderNodeMapRange"); mr.name = mr.label = "PT_rough"; mr.location = (-400, -60)
    mr.inputs["To Min"].default_value = rough * (1 - spread)
    mr.inputs["To Max"].default_value = rough * (1 + spread)
    L.new(tc.outputs["Object"], nb.inputs["Vector"])
    L.new(nb.outputs["Fac"], rp.inputs["Fac"])
    L.new(rp.outputs["Color"], mx.inputs["Factor"])
    L.new(mx.outputs["Result"], bsdf.inputs["Base Color"])
    L.new(tc.outputs["Object"], gn.inputs["Vector"])
    L.new(gout, bp.inputs["Height"])
    L.new(bp.outputs["Normal"], bsdf.inputs["Normal"])
    L.new(gout, mr.inputs["Value"])
    L.new(mr.outputs["Result"], bsdf.inputs["Roughness"])
    return m


def kit_materials():
    """The one material this kit adds. Everything else reuses the hero palette:
    brass, steel, leather, leather_dk, cloth_dk, hood, skin, white."""
    return {"gem": painterly("gem", (0.42, 0.03, 0.05), (0.72, 0.09, 0.10),
                             rough=0.12, spread=0.25, big=14.0,
                             grain=("noise", 60.0), bump=0.03)}


def clear(tag):
    for ob in [o for o in bpy.data.objects if o.name.startswith(tag)]:
        bpy.data.objects.remove(ob, do_unlink=True)


def strap(name, path, half_w, thick, mat, up=None, coll=HERO_COLL, taper=None):
    """A flat belt-like ribbon following a path. `half_w` and `thick` may each be
    a float or f(t). Also the honest way to build a blade."""
    up = Vector((0.0, 0.0, 1.0)) if up is None else Vector(up)
    n = len(path)
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        side = t.cross(up)
        if side.length < 1e-6:
            side = t.cross(Vector((0.0, 1.0, 0.0)))
        side.normalize()
        nrm = side.cross(t).normalized()
        u = i / (n - 1)
        hw = half_w(u) if callable(half_w) else half_w
        th = thick(u) if callable(thick) else thick
        rings.append([p + side * hw + nrm * th, p - side * hw + nrm * th,
                      p - side * hw - nrm * th, p + side * hw - nrm * th])
    return loft(name, rings, mat, cap_start=True, cap_end=True, coll=coll)


# --------------------------------------------------------------------------- #
# pass 1 -- detail on the existing body
# --------------------------------------------------------------------------- #

def hood_rim(bins=32, y_max=-0.28, centre_z=1.6445, outward=0.0):
    """The visible edge of the hood opening, read off the evaluated mesh: the
    innermost vertex per angular bin around the face. `outward` pushes the path
    radially away from the face, so a hem swept along it grows into the hood
    instead of closing over the eyes."""
    best = {}
    for p in evaluated_points("Hood"):
        if p.y >= y_max:
            continue
        a = math.atan2(p.z - centre_z, p.x) % (2 * math.pi)
        b = int(a / (2 * math.pi) * bins) % bins
        r = math.hypot(p.x, p.z - centre_z)
        if b not in best or r < best[b][0]:
            best[b] = (r, p)
    path = []
    for b in sorted(best):
        r, p = best[b]
        s = (r + outward) / r if r else 1.0
        path.append(Vector((p.x * s, p.y, centre_z + (p.z - centre_z) * s)))
    return path


def detail():
    """Trim the existing body. Everything here is additive -- no vertex of the
    original hero moves -- so the pass can be re-run, or dropped, at will.

    The brief is chunky and toy-like, so every addition is a solid form that
    catches the key light: rolled hems, piped edges, domed rivets. Nothing here
    is a surface pattern, because at the size this character ships at a pattern
    would dissolve and a form would not."""
    clear(DETAIL_TAG)
    M = bpy.data.materials
    hood, leather, leather_dk = M["hood"], M["leather"], M["leather_dk"]
    brass, cloth_dk = M["brass"], M["cloth_dk"]
    T = DETAIL_TAG
    made = []

    # A rolled hem around the face. Without it the hood reads as a smooth egg
    # with a hole cut in it. The path is pushed out by rather more than the tube
    # radius so the hem thickens the hood rather than narrowing the face.
    brim_path = resample(hood_rim(outward=0.030), 40)
    made.append(sweep(T + "HoodBrim", brim_path, 0.024, sides=8, mat=hood))

    # Piping along the whole opening of the cuirass, neck and shoulders both.
    collar = ordered_boundary("Cuirass", pick=lambda l: sum(p.z for p in l) / len(l))
    made.append(sweep(T + "Collar", resample(collar, 32), 0.024, sides=8, mat=leather_dk))

    # The cloak is a single skin and its free edge reads as paper without a hem.
    hem = max(ordered_boundary("Cloak"), key=len)
    made.append(sweep(T + "CloakHem", resample(hem, 72), 0.018, sides=6, mat=hood))

    for side, sx in (("L", 1.0), ("R", -1.0)):
        pauldron = "Pauldron" + side
        made.append(sweep(T + "PauldronRim" + side,
                          resample(max(ordered_boundary(pauldron), key=len), 28),
                          0.020, sides=8, mat=leather_dk))
        # Rivets over each shoulder cap. Two, not three, and low domes rather
        # than beads: brass is the loudest thing on this character and a dozen
        # bright dots read as measles at thumbnail size.
        cap = Vector((sx * 0.30, 0.005, 1.19))
        for i, d in enumerate(((0.46, -0.52, 0.72), (0.46, 0.52, 0.72))):
            aim = Vector((sx * d[0], d[1], d[2])).normalized()
            p, n = surface(pauldron, cap + aim * 1.0, -aim)
            made.append(dome(T + "RivetPauldron" + side + str(i), p, n, 0.020, 0.009, brass))
        # One stud on the outside of each bracer.
        p, n = surface("Bracer" + side, (sx * 0.35, -1.0, 0.690), (0.0, 1.0, 0.0))
        made.append(dome(T + "StudBracer" + side, p, n, 0.017, 0.008, brass))

        # A sole, so the boots read as footwear rather than as blunt cylinders.
        keep = (lambda p, s=sx: p.x * s > 0.005)
        cx, cy = sx * 0.137, 0.02
        bins = 20
        prof = section("Boot", 0.06, (cx, cy), bins=bins, keep=keep)
        radii = [math.hypot(p.x - cx, p.y - cy) for p in prof]

        def sole_ring(z, grow, _r=radii, _cx=cx, _cy=cy, _n=bins):
            return [Vector((_cx + (_r[i] + grow) * math.cos(2 * math.pi * i / _n),
                            _cy + (_r[i] + grow) * math.sin(2 * math.pi * i / _n), z))
                    for i in range(_n)]

        made.append(loft(T + "BootSole" + side,
                         [sole_ring(-0.004, 0.006), sole_ring(0.014, 0.022),
                          sole_ring(0.046, 0.022), sole_ring(0.066, 0.004)], leather_dk))
        made.append(band(T + "BootCuff" + side, "Boot", 0.245, 0.300, (cx, cy), leather_dk,
                         grow=0.016, bins=bins, keep=keep))
        made.append(band(T + "KneeWrap" + side, "Leg", 0.345, 0.415, (sx * 0.125, 0.0),
                         leather, grow=0.014, bins=16, keep=keep))

        # Eyelets punched around the belt.
        for i, ang in enumerate((34.0, 62.0)):
            a = math.radians(ang)
            d = Vector((sx * math.sin(a), -math.cos(a), 0.0))
            p, n = surface("Belt", Vector((0.0, 0.0, 0.746)) + d, -d)
            made.append(dome(T + "BeltEyelet" + side + str(i), p, n, 0.013, 0.006, brass))

        # A thumb, so an empty hand still reads as a fist once its weapon is off.
        base = Vector((sx * 0.330, -0.212, 0.556))
        tip = Vector((sx * 0.350, -0.232, 0.620))
        made.append(sweep(T + "Thumb" + side,
                          [base.lerp(tip, i / 4.0) for i in range(5)],
                          lambda t: 0.031 * (1.0 - 0.30 * t), sides=8, mat=cloth_dk,
                          closed=False))

    # The tail of the belt, hanging past the buckle over the tassets.
    made.append(strap(T + "BeltTail",
                      [Vector((0.078, -0.205, 0.792)), Vector((0.086, -0.222, 0.730)),
                       Vector((0.094, -0.243, 0.665)), Vector((0.092, -0.248, 0.612))],
                      0.030, 0.008, leather_dk))

    # A slider on the chest strap, echoing the belt buckle further up the body.
    p, n = surface("ChestStrap", (0.02, -1.0, 0.93), (0.0, 1.0, 0.0))
    made.append(dome(T + "StrapSlider", p, n, 0.030, 0.013, brass, sides=8))

    return made


# --------------------------------------------------------------------------- #
# pass 2 -- sockets, replacing the daggers that were welded into the hero
# --------------------------------------------------------------------------- #

# The daggers used to be part of the character mesh: a hero holding two daggers
# was the only hero there could be. These are the objects that pass built.
WELDED_WEAPONS = ("Dagger_L", "Blade_L", "Guard_L", "Grip_L", "Pommel_L",
                  "Dagger_R", "Blade_R", "Guard_R", "Grip_R", "Pommel_R")

# Where each fist is, and which way it is closed. A socket says only that much;
# how a given weapon sits in the fist is that weapon own business, so a sword
# can stand point-up in the same socket a dagger is reversed in.
#
# Neither socket leans sideways, and that is deliberate. A side lean that throws
# a point-up sword outwards throws a point-down dagger into the leg of the
# wearer, because turning the weapon over reverses which way the lean carries
# it. No single number is right for both, so the sockets lean back only, which
# reads the same whichever way up the weapon is held.
SOCKET_POSE = {
    "Socket_HandL": ((0.404, -0.158, 0.536), (-8.0, 0.0, 0.0)),
    "Socket_HandR": ((-0.404, -0.158, 0.536), (-8.0, 0.0, 0.0)),
}


def sockets():
    """Strip the welded daggers and leave an empty in each fist instead.

    glTF exports an empty as a plain node, so each socket survives into the
    `.glb` as a named child transform the engine can parent a weapon scene to."""
    for name in WELDED_WEAPONS:
        ob = bpy.data.objects.get(name)
        if ob is not None:
            bpy.data.objects.remove(ob, do_unlink=True)
    clear(SOCKET_TAG)
    made = []
    for name, (loc, rot) in SOCKET_POSE.items():
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.18
        e.location = loc
        e.rotation_euler = tuple(math.radians(a) for a in rot)
        made.append(link(e, HERO_COLL))
    return made


# --------------------------------------------------------------------------- #
# pass 3 -- the weapons
# --------------------------------------------------------------------------- #

def curve(pairs):
    """Piecewise-linear f(t) through (t, value) pairs, clamped at both ends."""
    def f(t):
        if t <= pairs[0][0]:
            return pairs[0][1]
        for (t0, v0), (t1, v1) in zip(pairs, pairs[1:]):
            if t <= t1:
                return v0 + (v1 - v0) * ((t - t0) / (t1 - t0) if t1 > t0 else 0.0)
        return pairs[-1][1]
    return f


def blade(name, z0, z1, half_w, thick, mat, ridge=1.0, segments=16, coll=WEAPON_COLL):
    """A blade as a lofted eight-point section: two edges, two flats and a
    centre line. `ridge` > 1 raises that centre into a spine, < 1 sinks it into
    a fuller -- one number, and the whole blade changes character."""
    rings = []
    for i in range(segments + 1):
        t = i / segments
        z = z0 + (z1 - z0) * t
        w, th = half_w(t), thick(t)
        pts = ((w, 0.0), (w * 0.55, th), (0.0, th * ridge), (-w * 0.55, th),
               (-w, 0.0), (-w * 0.55, -th), (0.0, -th * ridge), (w * 0.55, -th))
        rings.append([Vector((x, y, z)) for x, y in pts])
    return loft(name, rings, mat, cap_start=True, cap_end=True, coll=coll)


def wrapped_grip(name, z0, z1, radius, mat, turns=6.0, ripple=0.10, oval=0.84,
                 sides=10, segments=32, coll=WEAPON_COLL):
    """A grip whose radius ripples along its length, so it reads as cord or
    leather wound round a core rather than as a smooth dowel."""
    rings = []
    for i in range(segments + 1):
        t = i / segments
        r = radius * (1.0 + ripple * math.sin(t * turns * 2 * math.pi))
        r *= 1.0 - 0.12 * (2.0 * t - 1.0) ** 2
        rings.append(ring(sides, r, r * oval, z0 + (z1 - z0) * t))
    return loft(name, rings, mat, cap_start=True, cap_end=True, coll=coll)


def knob(name, z, radius, half_height, mat, sides=10, steps=6, oval=0.86,
         coll=WEAPON_COLL):
    """A squashed sphere: every pommel and every counterweight on the kit."""
    rings = []
    for s in range(steps + 1):
        a = -math.pi / 2 + math.pi * s / steps
        r = max(radius * math.cos(a), 0.004)
        rings.append(ring(sides, r, r * oval, z + half_height * math.sin(a)))
    return loft(name, rings, mat, cap_start=True, cap_end=True, coll=coll)


def crossguard(name, z, span, rise, depth, height, mat, segments=9, flare=0.40,
               coll=WEAPON_COLL):
    """A bar across the blade whose tips sweep towards the point and swell as
    they go, so the guard ends in two knuckles rather than two stumps."""
    path = []
    for i in range(segments):
        u = -1.0 + 2.0 * i / (segments - 1)
        path.append(Vector((span * u, 0.0, z + rise * u * u)))
    swell = lambda t: 1.0 + flare * (2.0 * t - 1.0) ** 2
    g = strap(name, path, lambda t: depth * swell(t), lambda t: height * swell(t),
              mat, up=(0.0, 0.0, 1.0), coll=coll)
    bevel(g, width=0.012, segments=2)
    return g


def weapons():
    """`wpn_sword` and `wpn_dagger`, each in the canonical grip frame: the origin
    sits in the middle of the fist, the blade runs up +Z, the edges face +/-X and
    the flats face +/-Y. A socket on the hero says only where a fist is, so a
    weapon that respects this frame drops into either hand.

    The two read as one kit -- the same brass, the same gem, the same domed
    pommel -- but the sword is broad, straight and fullered while the dagger is
    a short leaf blade with a raised spine, so they never read as one model
    scaled."""
    for name in (WEAPON_COLL, "wpn_sword", "wpn_dagger"):
        c = bpy.data.collections.get(name)
        if c is not None:
            for ob in list(c.objects):
                bpy.data.objects.remove(ob, do_unlink=True)
    clear(WEAPON_TAG)
    kit_materials()
    M = bpy.data.materials
    steel, brass, gem = M["steel"], M["brass"], M["gem"]
    leather_dk, cloth_dk = M["leather_dk"], M["cloth_dk"]
    out = {}

    # ---- sword: broad, straight, fullered -------------------------------- #
    c = "wpn_sword"
    # Straight and near parallel-sided for most of its length, so it never
    # reads as the dagger scaled up: the taper is saved for the last quarter.
    sword = [
        blade("WPN_SwordBlade", 0.150, 0.870,
              curve([(0.0, 0.090), (0.10, 0.094), (0.58, 0.088), (0.78, 0.072),
                     (0.93, 0.030), (1.0, 0.004)]),
              curve([(0.0, 0.026), (0.60, 0.020), (0.90, 0.010), (1.0, 0.003)]),
              steel, ridge=0.68, segments=18, coll=c),
        crossguard("WPN_SwordGuard", 0.128, 0.185, 0.052, 0.024, 0.028, brass, coll=c),
        wrapped_grip("WPN_SwordGrip", -0.108, 0.122, 0.038, leather_dk,
                     turns=7.0, ripple=0.09, coll=c),
        # A flattened, faceted disc, against the round ball on the dagger.
        knob("WPN_SwordPommel", -0.146, 0.070, 0.034, brass, sides=8, oval=0.62, coll=c),
    ]
    for i, sy in enumerate((-1.0, 1.0)):
        sword.append(dome("WPN_SwordGem" + str(i), (0.0, sy * 0.021, 0.135),
                          (0.0, sy, 0.0), 0.030, 0.016, gem, sides=8, coll=c))
    out["wpn_sword"] = sword

    # ---- dagger: short leaf blade with a raised spine --------------------- #
    c = "wpn_dagger"
    # A pronounced belly and a fast taper: the leaf silhouette is the whole
    # difference between reading as a dagger and reading as a small sword.
    dagger = [
        blade("WPN_DaggerBlade", 0.098, 0.470,
              curve([(0.0, 0.044), (0.30, 0.076), (0.55, 0.068), (0.82, 0.036),
                     (1.0, 0.004)]),
              curve([(0.0, 0.019), (0.55, 0.014), (0.88, 0.008), (1.0, 0.002)]),
              steel, ridge=1.30, segments=14, coll=c),
        crossguard("WPN_DaggerGuard", 0.082, 0.104, 0.030, 0.021, 0.022, brass, coll=c),
        wrapped_grip("WPN_DaggerGrip", -0.070, 0.078, 0.033, cloth_dk,
                     turns=6.0, ripple=0.13, coll=c),
        knob("WPN_DaggerPommel", -0.096, 0.046, 0.036, brass, coll=c),
    ]
    for i, sy in enumerate((-1.0, 1.0)):
        dagger.append(dome("WPN_DaggerGem" + str(i), (0.0, sy * 0.017, 0.086),
                           (0.0, sy, 0.0), 0.021, 0.011, gem, sides=8, coll=c))
    out["wpn_dagger"] = dagger
    return out


# --------------------------------------------------------------------------- #
# previewing a weapon in a socket
# --------------------------------------------------------------------------- #

PREVIEW_TAG = "PRV_"

# How each weapon sits in a fist, as euler degrees in the frame of the socket.
# This is the weapon own decision, not the socket one, which is what lets a
# sword stand point-up and a dagger hang reversed in the very same socket.
# These are the numbers the Godot weapon scenes carry on their root node.
# Only the X euler is used, and that is not an accident either: X is the one
# axis a grip can lean about and still mirror into the other hand unchanged.
# The sword tips forward so its blade clears the hood instead of hiding behind
# it; the dagger is simply turned over.
GRIP = {
    "wpn_sword": ((16.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
    "wpn_dagger": ((180.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
}


def preview(weapon, socket):
    """Hang a copy of a weapon off a socket, exactly as the engine will: the
    socket supplies the fist, `GRIP` supplies the way the weapon sits in it.
    Copies, so the originals keep their canonical frame."""
    rot, loc = GRIP[weapon]
    hub = bpy.data.objects.new(PREVIEW_TAG + weapon + "_" + socket, None)
    hub.empty_display_size = 0.05
    link(hub, HERO_COLL)
    hub.parent = bpy.data.objects[socket]
    hub.location = loc
    hub.rotation_euler = tuple(math.radians(a) for a in rot)
    for src in bpy.data.collections[weapon].objects:
        cp = src.copy()
        cp.data = src.data
        cp.name = PREVIEW_TAG + src.name + "_" + socket
        link(cp, HERO_COLL)
        cp.parent = hub
        cp.matrix_parent_inverse.identity()
        cp.location = (0.0, 0.0, 0.0)
    return hub


def preview_clear():
    clear(PREVIEW_TAG)
