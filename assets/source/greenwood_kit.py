"""Procedural source for the Greenwood Vale cast -- chapter 1's nine enemies.

Six standard enemies, two elites and the chapter boss, all modelled in Python so
every form is rebuildable rather than sculpted. The cast is fixed by the game
data, not invented here: `game-data/content/chapters/CH_01_GREENWOOD_VALE.json`
names the enemy pool, the two `miniBossIds` and `BOSS_THORNMAW`, and
`game-data/assets/asset_manifest_art.json` already carries a one-line subject for
each of them. Those subjects are the brief; this module is the execution.

    grunt        Thistlekin Scrapper   a small chibi warrior with a crude weapon
    swarm        Acornling             a tiny round scuttler, one of a swarm
    brute        Barkbelly             a round-bellied brute with enormous fists
    skirmisher   Bramble Cutter        a lean lunging thing with twin daggers
    warden       Toadstool Bulwark     a squat guardian behind a tower shield
    caster       Sporecaller           a hooded caster with a floating orb
    thorn_sentinel  a towering bramble knight in rose-thorn armour   (elite)
    mossback_alpha  a giant moss-covered boar with glowing tusks     (elite)
    thornmaw        a colossal carnivorous flower, rooted in stone   (boss)

Conventions, all shared with `hero_kit`
---------------------------------------
* +X is the creature's left, -Y is the direction it faces, +Z is up, feet at 0.
* Every object a creature owns lives in a collection named after it, so a
  rebuild clears exactly that creature and nothing else.
* NOTHING IS METALLIC AND NOTHING MAY BE. The build lights the scene with one
  directional key, one fill and flat ambient -- no probe, no sky -- so a true
  metal has nothing to reflect and renders black. Tusks, thorn plate and the
  boss's pollen are bright albedo at low roughness; the gloss is the light's.
* Silhouette is the quality bar. Every creature carries one shape that steps off
  its body outline -- a raised club, a cap brim, a shelf fungus, a scarf -- so it
  survives being filled black at 64 px, which is where these are actually seen.

Scale is set by the hero: `chr_hero_rogue.glb` stands 2.22 units tall with its
feet on zero, so a grunt at 1.70 reads as chest-high to the player and Thornmaw
at 4.20 reads as twice their height.
"""

import math

import bpy
from mathutils import Matrix, Vector

import hero_kit as hk

# The biome palette, verbatim from asset_manifest_art.json's `greenwood` row.
PALETTE = {
    "base": "#5FBF5F",
    "shadow": "#2F7A3F",
    "accent": "#F2D06B",
    "glow": "#FFF3A8",
    "prop": "#8B5E3C",
    "sky": "#9FE0F0",
}


def srgb(hex_or_rgb, mul=1.0):
    """Linear colour from a palette hex. Blender's inputs are linear and the
    manifest's hexes are sRGB, so the transfer function has to be undone or
    every green in the biome lands a shade too bright."""
    if isinstance(hex_or_rgb, str):
        h = hex_or_rgb.lstrip("#")
        rgb = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    else:
        rgb = list(hex_or_rgb)
    out = []
    for c in rgb:
        c = min(1.0, max(0.0, c * mul))
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


# --------------------------------------------------------------------------- #
# materials
# --------------------------------------------------------------------------- #

MATERIAL_SPEC = {
    # bark: the biome's prop brown, split light/dark along the grain. The poles
    # are pushed apart and warmed off the flat hex -- a bark that sits on
    # `prop` exactly comes out mud, and the style bible asks for saturated warm.
    "gw_bark": (srgb("#6E4322"), srgb("#C98F4C"), 0.78,
                dict(spread=0.20, big=9.0, grain=("noise", 200.0), bump=0.42)),
    "gw_bark_dk": (srgb("#32200F"), srgb("#6F4620"), 0.82,
                   dict(spread=0.22, big=7.0, grain=("noise", 170.0), bump=0.38)),
    # moss: base green against the shadow green, fine and velvety
    "gw_moss": (srgb(PALETTE["shadow"]), srgb(PALETTE["base"]), 0.88,
                dict(spread=0.16, big=12.0, grain=("voronoi", 260.0), bump=0.55)),
    "gw_moss_dk": (srgb(PALETTE["shadow"], 0.62), srgb(PALETTE["shadow"], 1.10), 0.90,
                   dict(spread=0.18, big=10.0, grain=("voronoi", 220.0), bump=0.50)),
    # bramble: dead-vine olive, deliberately desaturated. A knight woven out of
    # the biome's live green reads as a frog; the wood a bramble is actually
    # made of is nearly grey.
    "gw_bramble": (srgb("#2A2E1C"), srgb("#6A6A3C"), 0.86,
                   dict(spread=0.20, big=8.0, grain=("noise", 190.0), bump=0.46)),
    "gw_bramble_dk": (srgb("#171A0E"), srgb("#41421F"), 0.88,
                      dict(spread=0.22, big=7.0, grain=("noise", 160.0), bump=0.44)),
    # leaf: brighter and waxier than moss, so foliage separates from a body
    "gw_leaf": (srgb(PALETTE["shadow"], 1.25), srgb(PALETTE["base"], 1.12), 0.42,
                dict(spread=0.24, big=6.0, grain=("noise", 90.0), bump=0.16)),
    "gw_leaf_dry": (srgb(PALETTE["accent"], 0.72), srgb(PALETTE["base"], 0.80), 0.62,
                    dict(spread=0.22, big=6.5, grain=("noise", 110.0), bump=0.20)),
    # toadstool: the one warm red in the biome, and the reason a warden or a
    # sporecaller can be picked out of a green field at a glance
    "gw_cap": ((0.32, 0.020, 0.012), (0.62, 0.075, 0.030), 0.30,
               dict(spread=0.20, big=8.0, grain=("noise", 70.0), bump=0.08)),
    "gw_cap_pale": (srgb("#E8DCC0", 0.80), srgb("#FFF6E0"), 0.36,
                    dict(spread=0.18, big=9.0, grain=("noise", 80.0), bump=0.10)),
    "gw_stem": (srgb("#E4D8B4", 0.78), srgb("#FFF4D8"), 0.55,
                dict(spread=0.18, big=10.0, grain=("noise", 130.0), bump=0.18)),
    # thorn / horn / tusk: hard, pale, low roughness for a wet-looking tip
    # thorn is deliberately NOT another brown: a bramble spike cut from the
    # same tan as the bark it grows on disappears the instant it is baked
    "gw_thorn": (srgb("#2B1420"), srgb("#8C4436"), 0.30,
                 dict(spread=0.26, big=14.0, grain=("noise", 150.0), bump=0.14)),
    "gw_tusk": (srgb("#D8E8C0"), srgb("#F4FFE0"), 0.22,
                dict(spread=0.20, big=16.0, grain=("noise", 120.0), bump=0.08)),
    # glow: the accent and the glow hex, bright albedo standing in for emission,
    # because a DIFFUSE bake carries colour and not light
    "gw_glow": (srgb(PALETTE["accent"]), srgb(PALETTE["glow"]), 0.14,
                dict(spread=0.30, big=18.0, grain=("noise", 60.0), bump=0.04)),
    # the spore green has to be SATURATED. Mixed between two pale poles it bakes
    # out as the same off-white as the gold glow, and the caster loses the one
    # colour that separates it from the toadstool it is wearing.
    "gw_glow_gn": (srgb("#3FA80E"), srgb("#B6FF52"), 0.14,
                   dict(spread=0.30, big=18.0, grain=("noise", 60.0), bump=0.04)),
    # cloth, hide, stone -- the non-plant surfaces the cast still needs
    "gw_cloth": (srgb("#6B7A4A", 0.70), srgb("#93A868"), 0.86,
                 dict(spread=0.20, big=7.0, grain=("noise", 180.0), bump=0.34)),
    "gw_hide": (srgb("#4A3A2A", 0.70), srgb("#6E5A40"), 0.80,
                dict(spread=0.22, big=8.0, grain=("noise", 210.0), bump=0.44)),
    "gw_stone": (srgb("#5A6455", 0.72), srgb("#8C9884"), 0.84,
                 dict(spread=0.18, big=6.0, grain=("voronoi", 90.0), bump=0.40)),
    "gw_petal": ((0.44, 0.045, 0.055), (0.78, 0.14, 0.05), 0.28,
                 dict(spread=0.22, big=5.0, grain=("noise", 75.0), bump=0.10)),
    # the eye set: white sclera, near-black iris. Eyes are geometry rather than
    # texture because these read at 64 px, and a painted pupil is the first
    # thing a decimator throws away.
    "gw_eye": ((0.95, 0.94, 0.90), (1.0, 1.0, 0.98), 0.16,
               dict(spread=0.08, big=20.0, grain=("noise", 40.0), bump=0.0)),
    "gw_pupil": ((0.012, 0.010, 0.014), (0.045, 0.040, 0.055), 0.20,
                 dict(spread=0.10, big=20.0, grain=("noise", 40.0), bump=0.0)),
    "gw_mouth": ((0.10, 0.020, 0.022), (0.22, 0.045, 0.045), 0.52,
                 dict(spread=0.16, big=12.0, grain=("noise", 90.0), bump=0.06)),
    # the boss's throat, darker than any other mouth in the cast: depth is the
    # only thing telling a player this one is a hole and not a painted disc
    "gw_gullet": ((0.030, 0.004, 0.008), (0.105, 0.018, 0.020), 0.62,
                  dict(spread=0.18, big=10.0, grain=("noise", 80.0), bump=0.10)),
}


def materials(force=False):
    """Build the Greenwood palette as painterly two-tone shaders.

    `hero_kit.painterly` gives each material two colour poles broken up by
    object-space noise plus a fine grain driving bump and roughness; the pair of
    poles is what keeps a flat cartoon colour from reading as plastic once it is
    baked. Grain scale separates the materials that share a hue: bark is coarse
    and streaky, moss is fine and dense, a mushroom cap is almost smooth.
    """
    if force:
        # `painterly` returns an existing material untouched, which is what
        # makes it cheap to call anywhere -- and also what makes a palette edit
        # invisible until the old block is gone. Dropping it re-links every
        # user to the rebuilt one under the same name.
        for name in MATERIAL_SPEC:
            old = bpy.data.materials.get(name)
            if old is not None:
                bpy.data.materials.remove(old)
    return {name: hk.painterly(name, a, b, rough, **kw)
            for name, (a, b, rough, kw) in MATERIAL_SPEC.items()}


def M(name):
    """A palette material by name, built on first use."""
    m = bpy.data.materials.get(name)
    if m is None:
        a, b, rough, kw = MATERIAL_SPEC[name]
        m = hk.painterly(name, a, b, rough, **kw)
    return m


# --------------------------------------------------------------------------- #
# shape grammar
# --------------------------------------------------------------------------- #

def coll_clear(name):
    """Delete a creature's collection contents. Every builder starts here, so a
    rebuild replaces exactly one creature and leaves the rest of the scene."""
    c = bpy.data.collections.get(name)
    if c is not None:
        for ob in list(c.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
    return c


def body(name, profile, mat, coll, phase=0.0, bins=16, smooth=True):
    """A mass lofted through horizontal sections.

    `profile` is a sequence of (z, rx, ry) or (z, rx, ry, cx, cy): the height of
    a section, its two radii, and where its centre sits. Almost every mass in
    this cast -- a belly, a skull, a mushroom stem, a boar's barrel -- is that
    list and nothing more, which is why it is worth having as one function.
    """
    rings = []
    for row in profile:
        z, rx, ry = row[0], row[1], row[2]
        cx, cy = (row[3], row[4]) if len(row) > 4 else (0.0, 0.0)
        rings.append(hk.ring(bins, max(rx, 1e-4), max(ry, 1e-4), z, cx, cy, phase))
    return hk.loft(name, rings, mat, coll=coll, smooth=smooth)


def body_y(name, profile, mat, coll, bins=16, smooth=True):
    """`body`, but the sections stack along -Y instead of up.

    A quadruped is a barrel lying down, and rebuilding it out of horizontal
    slices means describing a shape nobody thinks in. `profile` rows are
    (y, rx, rz) or (y, rx, rz, cx, cz).
    """
    rings = []
    for row in profile:
        y, rx, rz = row[0], row[1], row[2]
        cx, cz = (row[3], row[4]) if len(row) > 4 else (0.0, 0.0)
        rings.append([Vector((cx + max(rx, 1e-4) * math.cos(2 * math.pi * i / bins),
                              y,
                              cz + max(rz, 1e-4) * math.sin(2 * math.pi * i / bins)))
                      for i in range(bins)])
    return hk.loft(name, rings, mat, coll=coll, smooth=smooth)


def ball(name, centre, radius, mat, coll, squash=(1.0, 1.0, 1.0), steps=8, bins=16):
    """A sphere, optionally squashed per axis. The chibi silhouette is mostly
    made of these, so they carry their own proportions in the geometry rather
    than in a scale the export would have to apply."""
    cx, cy, cz = centre
    sx, sy, sz = squash
    prof = []
    for i in range(steps + 1):
        a = math.pi * i / steps
        prof.append((cz - radius * sz * math.cos(a),
                     max(radius * sx * math.sin(a), 1e-4),
                     max(radius * sy * math.sin(a), 1e-4), cx, cy))
    return body(name, prof, mat, coll, bins=bins)


def patch(name, centre, radius, mat, coll, squash=(1.0, 0.34, 1.0), rot=(0, 0, 0),
          steps=6, bins=12):
    """A flattened ball, rotated: a bark plate, a moss patch, a scale, a scab.

    Detail that has to survive a texture bake and a decimate has to be geometry,
    and the cheapest geometry that still catches the key light is a squashed
    sphere pushed halfway into the surface it is decorating.
    """
    ob = ball(name, (0.0, 0.0, 0.0), radius, mat, coll, squash=squash,
              steps=steps, bins=bins)
    ob.rotation_euler = rot
    ob.location = centre
    return ob


def limb(name, a, b, r0, r1, mat, coll, bins=10, bow=None, steps=6, cap=True):
    """A tapered tube from `a` to `b`, optionally bowed off the straight line.

    Chunky proportions live or die on limbs that curve: a straight cylinder arm
    reads as a robot, and the same arm bowed a tenth of its length outward reads
    as a cartoon. `bow` is a world-space vector added at mid-length.
    """
    a, b = Vector(a), Vector(b)
    axis = b - a
    up = Vector((0.0, 0.0, 1.0))
    if abs(axis.normalized().dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    nx = axis.cross(up).normalized()
    ny = axis.cross(nx).normalized()
    rings = []
    for i in range(steps + 1):
        t = i / steps
        c = a + axis * t
        if bow is not None:
            c = c + Vector(bow) * math.sin(math.pi * t)
        r = r0 + (r1 - r0) * t
        rings.append([c + nx * (r * math.cos(2 * math.pi * j / bins))
                      + ny * (r * math.sin(2 * math.pi * j / bins)) for j in range(bins)])
    verts = [p for rg in rings for p in rg]
    return hk.mesh(name, verts, hk.stitch(rings, False, cap, cap), mat, coll)


def spike(name, base, direction, length, radius, mat, coll, bins=8, curve=0.0,
          steps=5):
    """A thorn: a cone that may hook. Every thorn, tusk, claw, fang and horn in
    the biome is one of these, and between them they are most of the cast's
    read at silhouette size."""
    base = Vector(base)
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    side = d.cross(up).normalized()
    hook = d.cross(side).normalized()
    rings = []
    for i in range(steps + 1):
        t = i / steps
        c = base + d * (length * t) + hook * (curve * length * t * t)
        r = max(radius * (1.0 - t) ** 0.7, 1e-4)
        rings.append([c + side * (r * math.cos(2 * math.pi * j / bins))
                      + hook * (r * math.sin(2 * math.pi * j / bins)) for j in range(bins)])
    verts = [p for rg in rings for p in rg]
    return hk.mesh(name, verts, hk.stitch(rings, False, True, True), mat, coll)


def blade(name, base, direction, length, width, thick, mat, coll, curve=0.0,
          belly=0.62, steps=7, up=None):
    """A flat, double-edged blade with a raised spine, tapering to a point.

    A thorn dagger drawn as a cone is a horn, and this cast already has plenty
    of horns. What makes an edged weapon read is the flat: wide across, thin
    through, and widest short of halfway so the tip looks earned.

    `up` rolls the blade about its own axis. It matters more than it sounds:
    the camera is fixed at a 3/4 low angle, so a blade that happens to present
    its edge to that camera is a sliver, and the same blade rolled 90 degrees
    is a weapon. Pass the axis the flat should turn AWAY from.
    """
    base = Vector(base)
    d = Vector(direction).normalized()
    up = Vector(up) if up is not None else Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up.normalized())) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    flat = d.cross(up).normalized()
    nrm = flat.cross(d).normalized()
    rings = []
    for i in range(steps + 1):
        t = i / steps
        c = base + d * (length * t) + nrm * (curve * length * t * t)
        w = max(width * math.sin(math.pi * min((t + 0.06) * belly * 1.6, 1.0)) ** 0.5
                * (1.0 - t) ** 0.45, 1e-4)
        th = max(thick * (1.0 - t) ** 0.6, 1e-4)
        rings.append([c + flat * w, c + nrm * th, c - flat * w, c - nrm * th])
    rings.append([base + d * length] * 4)
    verts = [p for rg in rings for p in rg]
    return hk.mesh(name, verts, hk.stitch(rings, False, True, False), mat, coll)


def eyes(prefix, centre, radius, coll, spread=1.0, look=(0.0, -1.0, -0.12),
         tilt=0.0, pupil=0.52, brow=None, brow_mat="gw_bark_dk", squash=None):
    """The pair of large, high-contrast eyes the style bible asks for.

    A white ball set into the head, a dark ball pushed forward through it, and
    an optional brow ridge clipped over the top. `tilt` rotates the brows toward
    each other, which is the whole of an angry expression at this scale.
    """
    cx, cy, cz = centre
    look = Vector(look).normalized()
    sq = squash or (1.0, 1.0, 1.0)
    out = []
    for sgn in (1, -1):
        side = "L" if sgn > 0 else "R"
        x = cx + sgn * spread
        out.append(ball("{}_EyeW{}".format(prefix, side), (x, cy, cz), radius,
                        M("gw_eye"), coll, squash=sq, steps=7, bins=14))
        out.append(ball("{}_EyeP{}".format(prefix, side),
                        (x + look.x * radius * 0.60,
                         cy + look.y * radius * 0.60,
                         cz + look.z * radius * 0.60),
                        radius * pupil, M("gw_pupil"), coll, steps=6, bins=12))
        if brow is not None:
            # Through `patch`, whose mesh is built on the origin and then moved:
            # a brow built at its final coordinates and *then* rotated swings
            # about the world origin instead of about itself, and the pair fly
            # off the head as two ears.
            out.append(patch("{}_Brow{}".format(prefix, side),
                             (x, cy + radius * 0.06, cz + radius * brow),
                             radius * 1.22, M(brow_mat), coll,
                             squash=(1.0, 1.0, 0.40), rot=(0.0, sgn * tilt, 0.0),
                             steps=6, bins=14))
    return out


def mouth_slot(name, centre, width, height, depth, mat, coll, curve=0.0):
    """An open mouth as a shallow lens driven back into the head, with a curved
    upper lip line so a wide grin does not read as a letterbox."""
    cx, cy, cz = centre
    rings = []
    for dy, s in ((0.0, 1.0), (depth * 0.6, 0.80), (depth, 0.30)):
        pts = []
        n = 12
        for j in range(n):
            a = 2 * math.pi * j / n
            u, v = math.cos(a), math.sin(a)
            pts.append(Vector((cx + width * s * u,
                               cy + dy,
                               cz + height * s * v - curve * width * s * u * u)))
        rings.append(pts)
    return hk.loft(name, rings, mat, coll=coll, cap_start=True, cap_end=True)


def fangs(prefix, centre, width, count, length, radius, mat, coll, down=True,
          jitter=0.22, lean=-0.18):
    """A row of teeth around the front of a mouth. Uneven on purpose -- a
    perfectly regular row reads as a comb, not a bite."""
    cx, cy, cz = centre
    out = []
    for i in range(count):
        t = (i + 0.5) / count
        x = cx + width * (t * 2 - 1)
        scale = 1.0 - jitter * abs(math.sin(i * 2.4))
        d = (0.0, lean, -1.0 if down else 1.0)
        out.append(spike("{}_Fang{}".format(prefix, i), (x, cy, cz),
                         d, length * scale, radius * scale, mat, coll,
                         bins=6, steps=3))
    return out


def mushroom(prefix, base, height, cap_r, stem_r, cap_mat, stem_mat, coll,
             lean=(0.0, 0.0), cap_drop=0.78, gills=True, spots=0):
    """A toadstool: stem, domed cap, optional gills and optional pale spots.

    Every creature in this biome carries at least one. The manifest's subject
    line for the whole chapter ends in `mushroom cap details`, and a shared
    motif is what makes nine different silhouettes read as one faction.
    """
    bx, by, bz = base
    lx, ly = lean
    out = [body("{}_Stem".format(prefix),
                [(bz, stem_r * 1.30, stem_r * 1.30, bx, by),
                 (bz + height * 0.30, stem_r * 0.84, stem_r * 0.84,
                  bx + lx * 0.30, by + ly * 0.30),
                 (bz + height * 0.72, stem_r * 0.78, stem_r * 0.78,
                  bx + lx * 0.72, by + ly * 0.72),
                 (bz + height, stem_r * 0.98, stem_r * 0.98, bx + lx, by + ly)],
                stem_mat, coll, bins=12)]
    tx, ty, tz = bx + lx, by + ly, bz + height
    # The cap is a tall dome that turns back under at the rim. A shallow one is
    # the single thing that makes a toadstool read as a floating plate instead
    # of a mushroom, and at 64 px a plate reads as nothing at all.
    cap_h = cap_r * cap_drop
    prof, steps = [], 9
    for i in range(steps + 1):
        t = i / steps
        r = cap_r * math.sin(math.pi * 0.5 * t) ** 0.52
        z = tz + cap_h * math.cos(math.pi * 0.5 * t) ** 0.85
        prof.append((z, r, r, tx, ty))
    prof.append((tz - cap_h * 0.16, cap_r * 0.88, cap_r * 0.88, tx, ty))
    prof.sort(key=lambda row: row[0])
    out.append(body("{}_Cap".format(prefix), prof, cap_mat, coll, bins=16))
    if gills:
        out.append(body("{}_Gills".format(prefix),
                        [(tz - cap_r * 0.03, cap_r * 0.92, cap_r * 0.92, tx, ty),
                         (tz + cap_r * 0.12, cap_r * 0.28, cap_r * 0.28, tx, ty)],
                        M("gw_cap_pale"), coll, bins=16))
    for i in range(spots):
        a = 2 * math.pi * i / max(spots, 1) + 0.4
        r = cap_r * (0.30 + 0.34 * (((i * 7) % 3) / 2.0))
        z = tz + cap_r * cap_drop * (1.0 - (r / cap_r) ** 1.7) + cap_r * 0.02
        out.append(ball("{}_Spot{}".format(prefix, i),
                        (tx + r * math.cos(a), ty + r * math.sin(a), z),
                        cap_r * 0.14, M("gw_cap_pale"), coll,
                        squash=(1.0, 1.0, 0.30), steps=4, bins=10))
    return out


def leaf(name, base, direction, length, width, mat, coll, curl=0.30, twist=0.0):
    """A single leaf blade: a flat lens swept along a curving spine."""
    base = Vector(base)
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    side = d.cross(up).normalized()
    nrm = side.cross(d).normalized()
    rings, steps = [], 7
    for i in range(steps + 1):
        t = i / steps
        c = base + d * (length * t) + nrm * (curl * length * math.sin(math.pi * t) * 0.5)
        w = max(width * math.sin(math.pi * min(t * 1.12, 1.0)) ** 0.75, 1e-4)
        th = max(width * 0.13 * (1 - t * 0.6), 1e-4)
        s = side * math.cos(twist * t) + nrm * math.sin(twist * t)
        n2 = nrm * math.cos(twist * t) - side * math.sin(twist * t)
        rings.append([c + s * w, c + n2 * th, c - s * w, c - n2 * th])
    verts = [p for rg in rings for p in rg]
    return hk.mesh(name, verts, hk.stitch(rings, False, True, True), mat, coll)


def vine(name, a, b, r0, r1, thickness, mat, coll, turns=2.0, sides=7, steps=48,
         phase=0.0):
    """A creeper spiralling from `a` to `b` at a radius that tapers r0 to r1.

    The elite and the boss are both made of woven bramble, and weave is the one
    thing a stack of lofted masses cannot fake: it needs a line that actually
    goes round. Cheap at 7 sides, and it is what stops a vine body reading as a
    tree trunk with stripes painted on.
    """
    a, b = Vector(a), Vector(b)
    axis = (b - a)
    up = Vector((0.0, 0.0, 1.0))
    if abs(axis.normalized().dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    nx = axis.cross(up).normalized()
    ny = axis.normalized().cross(nx).normalized()
    path = []
    for i in range(steps):
        t = i / (steps - 1.0)
        ang = phase + turns * 2 * math.pi * t
        r = r0 + (r1 - r0) * t
        path.append(a + axis * t + nx * (r * math.cos(ang)) + ny * (r * math.sin(ang)))
    return hk.sweep(name, path, thickness, sides=sides, mat=mat, coll=coll,
                    closed=False)


def petal(name, base, direction, length, width, mat, coll, cup=0.55, twist=0.0):
    """A cupped petal: wider and rounder than a leaf, and curled along its
    length so a whorl of them holds an opening rather than a starburst."""
    base = Vector(base)
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    side = d.cross(up).normalized()
    nrm = side.cross(d).normalized()
    rings, steps = [], 8
    for i in range(steps + 1):
        t = i / steps
        c = base + d * (length * t) - nrm * (cup * length * (t ** 1.6) * 0.5)
        w = max(width * math.sin(math.pi * min(0.16 + t * 0.95, 1.0)) ** 0.42,
                1e-4)
        th = max(width * 0.10 * (1 - t * 0.5), 1e-4)
        s = side * math.cos(twist * t) + nrm * math.sin(twist * t)
        n2 = nrm * math.cos(twist * t) - side * math.sin(twist * t)
        # a cupped cross-section rather than a flat lens: five points, the outer
        # pair rolled back toward the flower's centre
        rings.append([c + s * w + n2 * (th * 0.9) - n2 * (w * 0.22),
                      c + n2 * th,
                      c - s * w + n2 * (th * 0.9) - n2 * (w * 0.22),
                      c - s * w * 0.92 - n2 * th,
                      c + s * w * 0.92 - n2 * th])
    verts = [p for rg in rings for p in rg]
    return hk.mesh(name, verts, hk.stitch(rings, False, True, True), mat, coll)


def rose(prefix, centre, direction, radius, mat, coll, whorls=3, per=6,
         open_angle=1.05):
    """A rose bloom: nested whorls of petals, each tighter and more upright
    than the one outside it. The chapter's elite wears one as a crest and the
    boss is one, so it is worth having the shape once rather than twice."""
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    ax = d.cross(up).normalized()
    ay = d.cross(ax).normalized()
    out = []
    for w in range(whorls):
        t = w / max(whorls - 1, 1)
        tilt = open_angle * (1.0 - t) + 0.10
        r = radius * (1.0 - 0.26 * t)
        n = max(per - w, 3)
        for k in range(n):
            a = 2 * math.pi * k / n + w * 0.55
            radial = ax * math.cos(a) + ay * math.sin(a)
            out.append(petal("{}_P{}_{}".format(prefix, w, k),
                             tuple(Vector(centre) + radial * (radius * 0.10)
                                   + d * (radius * 0.10 * w)),
                             tuple((radial * math.sin(tilt)
                                    + d * math.cos(tilt)).normalized()),
                             r, r * 0.46, mat, coll,
                             cup=0.50 + 0.25 * t, twist=0.30))
    return out


def frond(prefix, base, direction, length, width, mat, coll, count=5,
          spread=0.55, curl=0.30):
    """A spray of leaves off one point -- a crest, a tail, a shoulder tuft."""
    d = Vector(direction).normalized()
    up = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0.0, 1.0, 0.0))
    side = d.cross(up).normalized()
    out = []
    for i in range(count):
        t = (i / max(count - 1, 1)) * 2 - 1
        dd = (d + side * (spread * t)).normalized()
        out.append(leaf("{}_Leaf{}".format(prefix, i), base, dd,
                        length * (1.0 - 0.24 * abs(t)), width, mat, coll,
                        curl=curl, twist=0.5 * t))
    return out


def subsurf(ob, levels=1):
    m = ob.modifiers.new("Subdivision", "SUBSURF")
    m.levels = m.render_levels = levels
    return m


def stage():
    """The authoring light rig: one directional key, one fill, flat ambient.

    This is the build's lighting contract stated as objects, so what is judged
    in the viewport is what the game will show. It is also the reason nothing
    here may be metallic -- there is no probe and no sky, so a true metal has
    nothing to reflect and comes out black.
    """
    for name in ("GW_Key", "GW_Fill"):
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
    scene = bpy.context.scene
    world = bpy.data.worlds.get("GW_World") or bpy.data.worlds.new("GW_World")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.42, 0.52, 0.46, 1.0)
    bg.inputs[1].default_value = 0.55
    scene.world = world

    key = bpy.data.lights.new("GW_KeyData", "SUN")
    key.energy, key.angle = 3.6, 0.34
    key.color = (1.0, 0.96, 0.86)
    ob = bpy.data.objects.new("GW_Key", key)
    ob.rotation_euler = (math.radians(56.0), 0.0, math.radians(38.0))
    bpy.context.scene.collection.objects.link(ob)

    fill = bpy.data.lights.new("GW_FillData", "SUN")
    fill.energy, fill.angle = 1.1, 1.2
    fill.color = (0.72, 0.84, 0.96)
    ob2 = bpy.data.objects.new("GW_Fill", fill)
    ob2.rotation_euler = (math.radians(72.0), 0.0, math.radians(-140.0))
    bpy.context.scene.collection.objects.link(ob2)
    return ob, ob2


def smooth_all(coll_name, levels=1, skip=()):
    """Subdivide every mesh a creature owns. Authoring at one level and pinning
    to one on export is what keeps the triangle count honest -- see
    `hero_export._scratch`, which learned that from the hero being authored at
    two and exporting four times the geometry anyone asked for."""
    for ob in bpy.data.collections[coll_name].objects:
        if ob.type != "MESH" or ob.name in skip:
            continue
        if not any(m.type == "SUBSURF" for m in ob.modifiers):
            subsurf(ob, levels)


# --------------------------------------------------------------------------- #
# looking at the work
# --------------------------------------------------------------------------- #

def isolate(coll_name):
    """Show one creature and hide the rest. The cast all stands on the origin --
    a preview offset would be a transform the export would then have to undo.

    Both flags, and they are not the same flag: `LayerCollection.hide_viewport`
    is the eye icon and stops at the viewport, while `Collection.hide_render` is
    the camera icon. Setting only the first gives a viewport with one creature
    in it and a render with all nine standing inside each other.
    """
    for lc in bpy.context.view_layer.layer_collection.children:
        hidden = lc.name != coll_name
        lc.hide_viewport = hidden
        lc.collection.hide_render = hidden


def show_all():
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.hide_viewport = False
        lc.collection.hide_render = False


def look(target=(0.0, 0.0, 0.9), dist=4.4, azim=32.0, elev=8.0, shading="RENDERED"):
    """Point every 3D viewport at a creature from the fixed 3/4 low angle the
    style bible puts the game camera at, so what is judged here is the read the
    player gets rather than a flattering turntable."""
    import mathutils
    rot = (mathutils.Euler((math.radians(90.0 - elev), 0.0, math.radians(azim)), "XYZ")
           .to_quaternion())
    for area in bpy.context.screen.areas:
        if area.type != "VIEW_3D":
            continue
        space = area.spaces.active
        r3d = space.region_3d
        r3d.view_perspective = "PERSP"
        r3d.view_rotation = rot
        r3d.view_location = mathutils.Vector(target)
        r3d.view_distance = dist
        space.shading.type = shading
        space.overlay.show_overlays = False
    return True


def silhouette(on=True):
    """Flat-black solid shading against a pale ground: the 64 px test the style
    bible calls the quality bar, run as a viewport mode rather than as a render,
    so it can be checked between edits instead of at the end."""
    for area in bpy.context.screen.areas:
        if area.type != "VIEW_3D":
            continue
        s = area.spaces.active.shading
        if on:
            s.type = "SOLID"
            s.light = "FLAT"
            s.color_type = "SINGLE"
            s.single_color = (0.0, 0.0, 0.0)
            s.background_type = "VIEWPORT"
            s.background_color = (0.92, 0.94, 0.92)
        else:
            s.type = "RENDERED"
    return on


# --------------------------------------------------------------------------- #
# 1 / GRUNT -- Thistlekin Scrapper
# --------------------------------------------------------------------------- #

GRUNT = "chr_enemy_greenwood_grunt"


def grunt():
    """A small chibi warrior with a crude weapon and angry squinting eyes.

    The commonest thing in the chapter -- 40 of the pool's 100 draw weight -- so
    it is the shape the player learns the biome from. Everything else in the
    cast is read against this: a bark body, a moss beard, a thistle crest and
    one toadstool. The silhouette hook is the club held high off the shoulder,
    which is also the only part of the pose that says `attack` while the model
    is still a single static mesh.
    """
    C = GRUNT
    coll_clear(C)
    bark, dk = M("gw_bark"), M("gw_bark_dk")
    moss, leafm = M("gw_moss"), M("gw_leaf")

    # legs: short, bowed outward, ending in root clumps twice the ankle's width
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("GR_Leg" + s, (sgn * 0.155, 0.0, 0.50), (sgn * 0.195, -0.02, 0.15),
             0.125, 0.098, dk, C, bow=(sgn * 0.030, 0.0, 0.0))
        ball("GR_Foot" + s, (sgn * 0.195, -0.075, 0.085), 0.185, dk, C,
             squash=(0.92, 1.30, 0.52))
        for k in range(3):
            spike("GR_Toe{}{}".format(s, k),
                  (sgn * 0.195 + (k - 1) * 0.085, -0.20, 0.055),
                  (0.16 * (k - 1), -1.0, 0.14), 0.115, 0.045, dk, C, bins=6, steps=3)

    # torso: a barrel wider at the belly than at the shoulders, so the chunky
    # read comes from the mass and not from the armour
    body("GR_Torso",
         [(0.40, 0.215, 0.185),
          (0.56, 0.272, 0.232),
          (0.76, 0.300, 0.252),
          (0.94, 0.288, 0.238),
          (1.06, 0.238, 0.196),
          (1.13, 0.150, 0.130)], bark, C)
    # a moss bib pushed through the chest, which is where the biome colour goes
    ball("GR_Bib", (0.0, -0.135, 0.72), 0.235, moss, C, squash=(1.0, 0.62, 1.08))
    # bark plates: two ridges wrapping the belly, the cheap way to say `bark`
    for z, r in ((0.60, 0.286), (0.83, 0.302)):
        body("GR_Ridge{}".format(int(z * 100)),
             [(z - 0.030, r * 0.97, r * 0.84), (z, r * 1.06, r * 0.92),
              (z + 0.030, r * 0.97, r * 0.84)], dk, C)
    # curling bark plates up the back and flanks, so the body is not one smooth
    # brown egg from every angle the board camera can swing to
    for sgn in (1, -1):
        for k, (z, sc) in enumerate(((0.58, 1.00), (0.78, 0.92), (0.95, 0.78))):
            patch("GR_Plate{}{}".format("L" if sgn > 0 else "R", k),
                  (sgn * 0.235 * sc, 0.150 + 0.03 * k, z), 0.135 * sc, dk, C,
                  squash=(1.0, 0.30, 0.62), rot=(0.0, sgn * 0.45, sgn * 0.55))
    for sgn in (1, -1):
        patch("GR_Knee" + ("L" if sgn > 0 else "R"),
              (sgn * 0.175, -0.105, 0.360), 0.095, moss, C,
              squash=(1.0, 0.44, 0.86), rot=(0.30, 0.0, 0.0))

    # arms: the left hangs, the right is up with the club. Both bow outward, and
    # the raised one is pushed well clear of the head -- an arm that crosses the
    # skull disappears the moment the silhouette is filled black.
    limb("GR_ArmL", (0.255, -0.01, 0.98), (0.415, -0.10, 0.58), 0.108, 0.084,
         bark, C, bow=(0.085, -0.02, 0.0))
    ball("GR_HandL", (0.440, -0.135, 0.500), 0.150, dk, C, squash=(1.0, 1.05, 0.94))
    limb("GR_ArmR", (-0.255, -0.01, 0.98), (-0.560, -0.075, 1.310), 0.108, 0.086,
         bark, C, bow=(-0.110, -0.03, -0.02))
    ball("GR_HandR", (-0.590, -0.100, 1.360), 0.155, dk, C, squash=(1.0, 1.02, 0.96))
    # moss caps on both shoulders: the biome colour, and the joint the arm needs
    for sgn in (1, -1):
        ball("GR_Pad" + ("L" if sgn > 0 else "R"), (sgn * 0.250, -0.020, 0.985),
             0.150, moss, C, squash=(1.0, 0.98, 0.80))

    # head: oversized, set slightly forward, with a heavy bark brow shelf
    ball("GR_Head", (0.0, -0.035, 1.30), 0.345, bark, C, squash=(1.0, 0.95, 0.93))
    ball("GR_Brow", (0.0, -0.185, 1.425), 0.315, dk, C, squash=(1.06, 0.72, 0.34))
    ball("GR_Jaw", (0.0, -0.175, 1.135), 0.255, dk, C, squash=(1.02, 0.80, 0.52))
    eyes("GR", (0.0, -0.275, 1.330), 0.116, C, spread=0.152, tilt=math.radians(27.0),
         brow=0.72, brow_mat="gw_bark_dk", squash=(1.0, 0.94, 0.86))
    ball("GR_Snout", (0.0, -0.320, 1.235), 0.088, bark, C, squash=(0.9, 1.15, 0.8))
    # A wide jagged grin, not a slot: at 64 px the mouth is one of three marks
    # on the face and has to be as big as the eyes to survive. The front ring
    # sits just OUTSIDE the skull -- the first pass put it a centimetre inside
    # and the whole mouth vanished into the head.
    mouth_slot("GR_Mouth", (0.0, -0.318, 1.108), 0.205, 0.072, 0.130,
               M("gw_mouth"), C, curve=0.30)
    fangs("GR", (0.0, -0.322, 1.150), 0.172, 5, 0.080, 0.033, M("gw_tusk"), C)
    fangs("GRlo", (0.0, -0.318, 1.058), 0.150, 4, 0.064, 0.029, M("gw_tusk"), C,
          down=False, lean=-0.14)
    # a moss beard, low enough to leave the grin clear -- the first pass put it
    # over the mouth and cost the face one of its three marks
    ball("GR_Beard", (0.0, -0.125, 0.950), 0.190, moss, C, squash=(1.0, 0.86, 0.52))
    # knots and a split plate on the skull: bark, stated as geometry
    ball("GR_Knot", (0.175, -0.245, 1.395), 0.062, dk, C, squash=(1.0, 0.7, 0.9))
    # Plates on the BACK of the skull only. An earlier pass ran them round to
    # the widest point of the head, where a flat disc on a sphere stops reading
    # as bark and starts reading as an ear.
    for k, (ang, z, sc) in enumerate(((2.35, 1.40, 1.0), (math.pi, 1.30, 0.9),
                                      (-2.35, 1.40, 1.0))):
        patch("GR_Skull{}".format(k),
              (0.255 * math.sin(ang), -0.035 + 0.265 * math.cos(ang) * 0.92, z),
              0.100 * sc, dk, C, squash=(1.0, 0.22, 0.66), rot=(0.0, 0.0, -ang))

    # crest: the thistle the name is for, and the top third of the silhouette
    frond("GR_Crest", (0.0, 0.010, 1.555), (0.0, 0.20, 1.0), 0.480, 0.105,
          leafm, C, count=5, spread=0.72, curl=0.34)
    frond("GR_Crest2", (0.0, 0.075, 1.520), (0.0, 0.62, 0.90), 0.330, 0.085,
          M("gw_leaf_dry"), C, count=3, spread=0.85, curl=0.42)
    # toadstools behind the shoulder -- the motif every creature here carries.
    # Behind, not beside: on the shoulder line a cap reads as an ear.
    mushroom("GR_Shroom", (0.185, 0.155, 0.960), 0.115, 0.098, 0.036,
             M("gw_cap"), M("gw_stem"), C, lean=(0.045, 0.040), spots=3)
    mushroom("GR_Shroom2", (0.070, 0.195, 0.925), 0.078, 0.066, 0.026,
             M("gw_cap"), M("gw_stem"), C, lean=(0.016, 0.030), spots=0)

    # the club: a broken branch with a rock lashed into the fork. Held out from
    # the body rather than over it, so the club and the head are two shapes.
    limb("GR_Club", (-0.560, -0.060, 1.200), (-0.700, -0.235, 1.960), 0.086, 0.100,
         dk, C, bow=(-0.030, 0.0, 0.0))
    ball("GR_ClubRock", (-0.712, -0.262, 2.010), 0.185, M("gw_stone"), C,
         squash=(0.86, 0.80, 1.06), steps=6, bins=10)
    for k in range(3):
        a = 2.1 * k + 0.5
        spike("GR_ClubStub{}".format(k),
              (-0.700 + 0.06 * math.cos(a), -0.235 + 0.06 * math.sin(a), 1.930),
              (math.cos(a), math.sin(a), 0.30), 0.140, 0.038, dk, C, bins=6, steps=3)
    for k in range(2):
        z = 1.845 + k * 0.062
        body("GR_ClubLash{}".format(k),
             [(z - 0.016, 0.080, 0.080, -0.688, -0.222),
              (z, 0.092, 0.092, -0.688, -0.222),
              (z + 0.016, 0.080, 0.080, -0.688, -0.222)],
             M("gw_cloth"), C, bins=10)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 2 / SWARM -- Acornling
# --------------------------------------------------------------------------- #

SWARM = "chr_enemy_greenwood_swarm"


def swarm():
    """A tiny round scuttler with oversized eyes, drawn three at a time.

    The SWARM archetype puts three of these on the field per draw, so the model
    is solved for the smallest it will ever be seen at: one circle, one stalk,
    two enormous eyes and six little legs. Nothing else would survive. It stands
    0.78 against the hero's 2.22 -- barely knee-high, which is the joke.
    """
    C = SWARM
    coll_clear(C)
    bark, dk = M("gw_bark"), M("gw_bark_dk")

    # the nut: a fat acorn, slightly wider than tall so it reads as a bead
    ball("SW_Nut", (0.0, 0.0, 0.335), 0.300, bark, C, squash=(1.0, 0.94, 1.04))
    # the cap: a knurled dome over the top half, in the darker bark
    body("SW_Cap",
         [(0.335, 0.322, 0.302),
          (0.395, 0.328, 0.308),
          (0.470, 0.300, 0.282),
          (0.545, 0.228, 0.214),
          (0.600, 0.120, 0.112),
          (0.620, 0.045, 0.042)], dk, C)
    for r_i, (z, rr, n) in enumerate(((0.372, 0.330, 12), (0.448, 0.305, 10),
                                      (0.520, 0.242, 8))):
        for k in range(n):
            a = 2 * math.pi * k / n + r_i * 0.4
            ball("SW_Knurl{}_{}".format(r_i, k),
                 (rr * math.sin(a), rr * math.cos(a) * 0.94, z), 0.036, dk, C,
                 squash=(1.0, 1.0, 0.8), steps=4, bins=8)
    # the stalk and its leaf: the whole of the silhouette above the ball
    limb("SW_Stalk", (0.0, 0.02, 0.600), (0.0, 0.055, 0.735), 0.036, 0.022, dk, C)
    frond("SW_Sprig", (0.0, 0.055, 0.720), (0.0, 0.35, 1.0), 0.150, 0.052,
          M("gw_leaf"), C, count=3, spread=0.80, curl=0.40)

    # eyes taking up most of the front of the nut, because at swarm size the
    # eyes ARE the character
    eyes("SW", (0.0, -0.215, 0.360), 0.128, C, spread=0.132,
         tilt=math.radians(16.0), pupil=0.46, brow=0.74, brow_mat="gw_bark_dk",
         squash=(1.0, 0.92, 0.96))
    mouth_slot("SW_Mouth", (0.0, -0.285, 0.215), 0.110, 0.042, 0.075,
               M("gw_mouth"), C, curve=0.42)
    fangs("SW", (0.0, -0.290, 0.242), 0.078, 3, 0.046, 0.021, M("gw_tusk"), C)

    # six limbs: four scuttling legs and two little arms it waves
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        for k, (hy, ty, hz) in enumerate(((-0.115, -0.225, 0.150),
                                          (0.075, 0.185, 0.150))):
            limb("SW_Leg{}{}".format(s, k), (sgn * 0.185, hy, hz),
                 (sgn * 0.255, ty, 0.030), 0.048, 0.032, dk, C,
                 bow=(sgn * 0.030, 0.0, 0.030))
            spike("SW_Toe{}{}".format(s, k), (sgn * 0.255, ty, 0.032),
                  (sgn * 0.35, -1.0 if k == 0 else 1.0, -0.30), 0.085, 0.030,
                  dk, C, bins=6, steps=3)
        limb("SW_Arm" + s, (sgn * 0.245, -0.055, 0.400), (sgn * 0.360, -0.140, 0.300),
             0.045, 0.034, bark, C, bow=(sgn * 0.035, 0.0, 0.020))
        ball("SW_Hand" + s, (sgn * 0.378, -0.160, 0.282), 0.062, dk, C)

    # one toadstool, the faction motif, riding the back of the cap
    mushroom("SW_Shroom", (0.105, 0.145, 0.470), 0.075, 0.062, 0.022,
             M("gw_cap"), M("gw_stem"), C, lean=(0.030, 0.030), gills=False)
    # moss creeping up the underside, so the nut is not one flat brown at the
    # size this thing is actually drawn
    ball("SW_Moss", (0.0, 0.030, 0.185), 0.265, M("gw_moss"), C,
         squash=(1.02, 1.00, 0.66))
    for k, (ax, ay) in enumerate(((-0.20, -0.14), (0.22, -0.10), (0.0, 0.24))):
        patch("SW_MossTuft{}".format(k), (ax, ay, 0.310), 0.115, M("gw_moss"), C,
              squash=(1.0, 1.0, 0.30), rot=(-0.5 + ay, 0.0, ax * 2.0))

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 3 / BRUTE -- Barkbelly
# --------------------------------------------------------------------------- #

BRUTE = "chr_enemy_greenwood_brute"


def brute():
    """A huge round-bellied brute with tiny legs and enormous fists.

    Built as one enormous barrel with everything else hung off it: the head is
    sunk into the shoulders, the legs are stumps, and the arms are long enough
    to put the knuckles on the ground. The silhouette hook is the row of shelf
    fungus stepping off the shoulder line, which is what tells a player at a
    glance that the big one has arrived. 2.35 tall against the hero's 2.22.
    """
    C = BRUTE
    coll_clear(C)
    bark, dk, moss = M("gw_bark"), M("gw_bark_dk"), M("gw_moss")

    # the barrel: the whole character, really
    body("BR_Belly",
         [(0.42, 0.480, 0.400),
          (0.62, 0.630, 0.520),
          (0.90, 0.720, 0.590),
          (1.20, 0.700, 0.575),
          (1.45, 0.610, 0.510),
          (1.62, 0.505, 0.430),
          (1.72, 0.400, 0.350)], bark, C, bins=20)
    # growth rings around the stump-body, and a moss cap over the shoulders
    for z, r in ((0.66, 0.640), (1.02, 0.726), (1.38, 0.632)):
        body("BR_Ring{}".format(int(z * 100)),
             [(z - 0.045, r * 0.98, r * 0.82), (z, r * 1.05, r * 0.88),
              (z + 0.045, r * 0.98, r * 0.82)], dk, C, bins=20)
    ball("BR_Shoulders", (0.0, 0.075, 1.630), 0.520, moss, C,
         squash=(1.0, 0.82, 0.42), bins=20)
    # split bark staves up the barrel, so the growth rings stop reading as the
    # hoops of an actual barrel and start reading as a stump
    for k in range(9):
        a = 2 * math.pi * k / 9 + 0.2
        patch("BR_Stave{}".format(k),
              (0.700 * math.sin(a), 0.575 * math.cos(a), 0.95 + 0.06 * math.sin(k)),
              0.290, dk, C, squash=(0.40, 1.0, 1.34),
              rot=(0.0, 0.0, -a), steps=6, bins=10)

    # tiny legs, wide feet: the mass has to look like it is barely carried
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("BR_Leg" + s, (sgn * 0.290, 0.0, 0.520), (sgn * 0.345, -0.030, 0.180),
             0.235, 0.205, dk, C, bow=(sgn * 0.040, 0.0, 0.0))
        ball("BR_Foot" + s, (sgn * 0.350, -0.115, 0.110), 0.300, dk, C,
             squash=(0.95, 1.25, 0.42))
        for k in range(3):
            spike("BR_Toe{}{}".format(s, k),
                  (sgn * 0.350 + (k - 1) * 0.140, -0.310, 0.075),
                  (0.20 * (k - 1), -1.0, 0.10), 0.175, 0.070, dk, C, bins=6, steps=3)

    # Arms to the floor, ending in fists twice the size of the head. They swing
    # WIDE of the barrel: an arm tucked against a body this fat is an arm the
    # silhouette never sees.
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("BR_Arm" + s, (sgn * 0.600, 0.040, 1.545), (sgn * 1.020, -0.250, 0.560),
             0.220, 0.185, bark, C, bow=(sgn * 0.260, -0.060, 0.0))
        ball("BR_Fist" + s, (sgn * 1.060, -0.310, 0.375), 0.345, dk, C,
             squash=(1.0, 1.05, 0.94))
        for k in range(3):
            a = -0.5 + k * 0.5
            spike("BR_Knuck{}{}".format(s, k),
                  (sgn * 1.060 + 0.155 * math.sin(a), -0.310 - 0.235 * math.cos(a),
                   0.475),
                  (sgn * 0.30 * math.sin(a), -math.cos(a), 0.55), 0.150, 0.064,
                  M("gw_thorn"), C, bins=6, steps=3)
        patch("BR_ArmMoss" + s, (sgn * 0.780, -0.060, 1.180), 0.230, moss, C,
              squash=(1.0, 1.0, 0.34), rot=(0.4, -sgn * 0.5, sgn * 0.35))

    # Head: oversized in the chibi way but sunk low between the shoulders, so
    # the brute reads as neckless. All brow and underbite.
    ball("BR_Head", (0.0, -0.185, 1.900), 0.430, bark, C, squash=(1.0, 0.96, 0.88))
    ball("BR_Jaw", (0.0, -0.360, 1.720), 0.340, dk, C, squash=(1.05, 0.88, 0.52))
    eyes("BR", (0.0, -0.470, 1.960), 0.118, C, spread=0.178,
         tilt=math.radians(31.0), pupil=0.50, brow=0.76, brow_mat="gw_bark_dk",
         squash=(1.0, 0.90, 0.74))
    # The mouth ring sits clear of the JAW, not of the skull -- an underbite
    # puts the chin further forward than the brow, and a mouth measured off the
    # brow lands inside it.
    mouth_slot("BR_Mouth", (0.0, -0.670, 1.735), 0.245, 0.072, 0.150,
               M("gw_mouth"), C, curve=0.26)
    # the underbite: two tusks up out of the lower jaw, wide of the eyes so the
    # face reads angry rather than weeping
    for sgn in (1, -1):
        spike("BR_Tusk" + ("L" if sgn > 0 else "R"),
              (sgn * 0.285, -0.560, 1.630), (sgn * 0.36, -0.22, 1.0), 0.360, 0.084,
              M("gw_tusk"), C, bins=8, curve=-0.20)
    fangs("BRlo", (0.0, -0.660, 1.700), 0.170, 4, 0.076, 0.033, M("gw_tusk"), C,
          down=False, lean=-0.10)

    # Shelf fungus rising off the shoulders like pauldrons. Two earlier passes
    # put these flat on the flank, where they read as wings, and then behind the
    # arms, where they did not read at all -- angled UP off the shoulder is the
    # placement that both clears the arm and steps off the outline.
    for k, (sgn, x, y, z, r, tilt) in enumerate(
            ((1, 0.500, 0.300, 1.560, 0.345, 0.62),
             (-1, 0.520, 0.360, 1.620, 0.300, 0.70),
             (1, 0.640, 0.400, 1.080, 0.250, 0.45),
             (-1, 0.665, 0.380, 1.140, 0.215, 0.50))):
        for suffix, mat, dz, sc, thin in (("", M("gw_cap"), 0.0, 1.0, 0.26),
                                          ("U", M("gw_cap_pale"), -r * 0.11,
                                           0.86, 0.20)):
            patch("BR_Shelf{}{}".format(suffix, k),
                  (sgn * x, y, z + dz), r * sc, mat, C,
                  squash=(1.0, 1.0, thin),
                  rot=(0.60, -sgn * tilt, sgn * 0.95), steps=6, bins=14)
    mushroom("BR_Shroom", (0.215, 0.380, 1.585), 0.185, 0.150, 0.052,
             M("gw_cap"), M("gw_stem"), C, lean=(0.065, 0.060), spots=4)
    # moss on the crown and the knuckles, so the green does not stop at the neck
    ball("BR_Crown", (0.0, -0.100, 2.150), 0.340, moss, C, squash=(1.0, 1.0, 0.58))
    frond("BR_Crest", (0.0, -0.030, 2.250), (0.0, 0.30, 1.0), 0.400, 0.110,
          M("gw_leaf"), C, count=5, spread=0.72, curl=0.36)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 4 / SKIRMISHER -- Bramble Cutter
# --------------------------------------------------------------------------- #

SKIRMISHER = "chr_enemy_greenwood_skirmisher"


def skirmisher():
    """A lean crouching thing with twin thorn daggers, caught mid-lunge.

    The only diagonal in the cast. Everything else here is built on a vertical
    stack of round masses; this one is a line running from the trailing back
    foot to the leading blade, with a ragged leaf scarf streaming off the
    shoulder to hold the top of that diagonal open. It is thin where the rest
    are fat, which is most of what separates it at a glance.
    """
    C = SKIRMISHER
    coll_clear(C)
    bark, dk = M("gw_bark"), M("gw_bark_dk")
    bram, thorn = M("gw_moss_dk"), M("gw_thorn")

    # hips over the leading foot, chest pitched out ahead of them
    body("SK_Torso",
         [(0.700, 0.235, 0.205, 0.030, 0.190),
          (0.860, 0.275, 0.240, 0.015, 0.075),
          (1.020, 0.290, 0.250, -0.005, -0.045),
          (1.170, 0.255, 0.220, -0.020, -0.150),
          (1.260, 0.180, 0.160, -0.025, -0.215)], bark, C)
    for k, (z, y, r) in enumerate(((0.840, 0.090, 0.288), (0.980, -0.020, 0.300),
                                   (1.120, -0.125, 0.268))):
        body("SK_Rib{}".format(k),
             [(z - 0.030, r * 0.96, r * 0.80, 0.0, y),
              (z, r * 1.06, r * 0.88, 0.0, y),
              (z + 0.030, r * 0.96, r * 0.80, 0.0, y)], bram, C)

    # legs: the right folded under the lunge, the left thrown back to push. Both
    # are thicker than a lean creature strictly needs -- chunky is the house
    # rule, and lean here means lean RELATIVE to the brute standing next to it.
    limb("SK_LegR", (-0.165, -0.020, 0.720), (-0.290, -0.400, 0.395),
         0.145, 0.108, dk, C, bow=(-0.055, -0.040, 0.030))
    limb("SK_ShinR", (-0.290, -0.400, 0.395), (-0.280, -0.520, 0.105),
         0.108, 0.078, dk, C, bow=(0.0, 0.060, 0.0))
    ball("SK_FootR", (-0.280, -0.600, 0.080), 0.170, dk, C,
         squash=(0.88, 1.34, 0.46))
    limb("SK_LegL", (0.200, 0.100, 0.730), (0.330, 0.480, 0.360),
         0.145, 0.104, dk, C, bow=(0.060, 0.020, 0.055))
    limb("SK_ShinL", (0.330, 0.480, 0.360), (0.355, 0.660, 0.090),
         0.104, 0.074, dk, C, bow=(0.0, 0.065, 0.0))
    ball("SK_FootL", (0.360, 0.720, 0.080), 0.170, dk, C,
         squash=(0.88, 1.34, 0.46))
    for sgn, fy, fx in ((1, 0.810, 0.360), (-1, -0.690, -0.280)):
        s = "L" if sgn > 0 else "R"
        for k in range(3):
            spike("SK_Toe{}{}".format(s, k),
                  (fx + (k - 1) * 0.080, fy, 0.062),
                  (0.20 * (k - 1), sgn * 1.0, 0.05), 0.115, 0.042, dk, C,
                  bins=6, steps=3)

    # Arms: the lead blade sweeps low and WIDE of the body, the trailing one is
    # cocked high behind. Both were thrust straight down the facing axis in an
    # earlier pass, where the fixed camera saw them end-on and they vanished.
    limb("SK_ArmR", (-0.265, -0.075, 1.135), (-0.640, -0.470, 0.860),
         0.108, 0.084, bark, C, bow=(-0.110, 0.0, 0.060))
    ball("SK_HandR", (-0.672, -0.520, 0.838), 0.125, dk, C)
    limb("SK_ArmL", (0.265, -0.030, 1.140), (0.545, 0.300, 1.330),
         0.108, 0.084, bark, C, bow=(0.110, 0.0, 0.090))
    ball("SK_HandL", (0.572, 0.348, 1.360), 0.125, dk, C)

    # twin thorn daggers: flat, edged and leaf-shaped, rolled so the fixed 3/4
    # camera meets the flat rather than the edge
    blade("SK_BladeR", (-0.700, -0.560, 0.820), (-0.42, -1.0, -0.30), 0.560,
          0.170, 0.048, thorn, C, curve=0.08, up=(1.0, 0.0, 0.0))
    blade("SK_BladeL", (0.600, 0.385, 1.395), (0.38, 0.72, 1.0), 0.520,
          0.155, 0.044, thorn, C, curve=-0.10, up=(1.0, 0.0, 0.0))
    for nm, at, ax in (("R", (-0.690, -0.545, 0.826), (-0.42, -1.0, -0.30)),
                       ("L", (0.592, 0.375, 1.385), (0.38, 0.72, 1.0))):
        a = Vector(ax).normalized()
        side = a.cross(Vector((1.0, 0.0, 0.0))).normalized()
        for sgn in (1, -1):
            spike("SK_Quillon{}{}".format(nm, 1 if sgn > 0 else 0),
                  at, tuple(side * sgn + a * 0.30), 0.135, 0.038, bram, C,
                  bins=6, steps=3, curve=0.25)

    # head: big in the chibi way, thrust out past the chest on a hidden neck
    ball("SK_Head", (-0.030, -0.395, 1.400), 0.330, bark, C,
         squash=(0.94, 1.06, 0.94))
    # A smaller muzzle and narrowed eyes. Built round and wide the first time,
    # the face came out a puppy -- which is the wrong joke for the one enemy in
    # the chapter that is supposed to look like it means it.
    ball("SK_Muzzle", (-0.030, -0.605, 1.300), 0.145, dk, C,
         squash=(0.92, 1.26, 0.70))
    eyes("SK", (-0.030, -0.575, 1.455), 0.128, C, spread=0.152,
         tilt=math.radians(36.0), pupil=0.42, brow=0.66, brow_mat="gw_bark_dk",
         squash=(1.0, 0.86, 0.58))
    # a leaf band pulled across the bridge of the muzzle: a bandit's wrap, and
    # the cheapest thing that says `this one robs people`
    patch("SK_Mask", (-0.030, -0.545, 1.338), 0.205, M("gw_leaf"), C,
          squash=(1.0, 0.86, 0.30), rot=(0.24, 0.0, 0.0), steps=6, bins=14)
    mouth_slot("SK_Mouth", (-0.030, -0.775, 1.272), 0.140, 0.050, 0.115,
               M("gw_mouth"), C, curve=0.30)
    fangs("SK", (-0.030, -0.770, 1.300), 0.112, 4, 0.062, 0.026, M("gw_tusk"), C)
    # two small backswept thorns, kept short so they do not compete with the
    # daggers for the same read
    for sgn in (1, -1):
        spike("SK_Horn" + ("L" if sgn > 0 else "R"),
              (sgn * 0.195, -0.300, 1.545), (sgn * 0.34, 1.0, 0.70), 0.260, 0.048,
              thorn, C, bins=6, curve=0.16)

    # the ragged scarf, streaming back and up off the collar: it holds the top
    # of the diagonal open and is the reason the pose reads as motion
    ball("SK_Collar", (0.0, -0.185, 1.235), 0.265, M("gw_leaf"), C,
         squash=(1.0, 0.96, 0.54))
    for k, (dx, dy, dz, ln, wd) in enumerate(((0.42, 1.0, 0.70, 1.05, 0.215),
                                              (0.06, 1.0, 0.34, 1.24, 0.195),
                                              (-0.38, 1.0, 0.58, 0.98, 0.170),
                                              (0.20, 1.0, -0.10, 0.86, 0.150),
                                              (-0.14, 1.0, 0.92, 0.80, 0.145))):
        leaf("SK_Scarf{}".format(k),
             (0.06 - 0.05 * k, -0.055, 1.245 + 0.02 * k), (dx, dy, dz),
             ln, wd, M("gw_leaf"), C, curl=0.26 + 0.08 * k, twist=0.6 - 0.35 * k)
    mushroom("SK_Shroom", (0.185, 0.115, 1.100), 0.135, 0.105, 0.034,
             M("gw_cap"), M("gw_stem"), C, lean=(0.050, 0.040), spots=2)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 5 / WARDEN -- Toadstool Bulwark
# --------------------------------------------------------------------------- #

WARDEN = "chr_enemy_greenwood_warden"


def warden():
    """A squat guardian behind a tower shield cut from a giant toadstool cap.

    One shape does the work: a red-and-white disc almost as tall as the creature
    carrying it, with a helmeted head peeking over the top corner. That is the
    whole silhouette, and it is the one enemy in the chapter a player has to
    read as `hit this from the side` before the fight starts. The shield is set
    off to one flank rather than centred, so the body is not entirely eclipsed
    at the fixed 3/4 camera angle.
    """
    C = WARDEN
    coll_clear(C)
    bark, dk, moss = M("gw_bark"), M("gw_bark_dk"), M("gw_moss")
    cap, pale = M("gw_cap"), M("gw_cap_pale")

    # body: short, wide, and mostly shoulders
    body("WA_Torso",
         [(0.400, 0.330, 0.290),
          (0.560, 0.395, 0.335),
          (0.780, 0.430, 0.360),
          (1.020, 0.435, 0.365),
          (1.220, 0.395, 0.330),
          (1.330, 0.290, 0.250)], bark, C, bins=18)
    ball("WA_Yoke", (0.0, 0.020, 1.265), 0.470, moss, C, squash=(1.0, 0.86, 0.42),
         bins=18)
    for z, r in ((0.640, 0.402), (0.900, 0.436), (1.140, 0.418)):
        body("WA_Ring{}".format(int(z * 100)),
             [(z - 0.034, r * 0.97, r * 0.82), (z, r * 1.05, r * 0.89),
              (z + 0.034, r * 0.97, r * 0.82)], dk, C, bins=18)

    # legs: barely there, planted wide, braced against the shield
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("WA_Leg" + s, (sgn * 0.235, 0.020, 0.470), (sgn * 0.305, -0.030, 0.145),
             0.185, 0.160, dk, C, bow=(sgn * 0.035, 0.0, 0.0))
        ball("WA_Foot" + s, (sgn * 0.310, -0.115, 0.090), 0.245, dk, C,
             squash=(0.95, 1.30, 0.44))
        for k in range(3):
            spike("WA_Toe{}{}".format(s, k),
                  (sgn * 0.310 + (k - 1) * 0.115, -0.280, 0.062),
                  (0.18 * (k - 1), -1.0, 0.10), 0.140, 0.056, dk, C, bins=6, steps=3)

    # head: helmeted in a small toadstool, set high and toward the free flank so
    # it clears the shield rim
    ball("WA_Head", (0.115, -0.155, 1.475), 0.330, bark, C, squash=(1.0, 0.96, 0.90))
    eyes("WA", (0.115, -0.395, 1.505), 0.104, C, spread=0.140,
         tilt=math.radians(24.0), pupil=0.50, brow=0.80, brow_mat="gw_bark_dk",
         squash=(1.0, 0.90, 0.80))
    mouth_slot("WA_Mouth", (0.115, -0.420, 1.320), 0.150, 0.048, 0.100,
               M("gw_mouth"), C, curve=0.28)
    fangs("WA", (0.115, -0.415, 1.348), 0.118, 4, 0.056, 0.024, M("gw_tusk"), C)
    ball("WA_Beard", (0.115, -0.230, 1.230), 0.230, moss, C, squash=(1.0, 0.90, 0.50))
    # the helm sits ON the skull -- measured off the head's crown, not off a
    # stem, or it hovers above the head like a halo
    mushroom("WA_Helm", (0.115, -0.020, 1.430), 0.145, 0.430, 0.115,
             cap, M("gw_stem"), C, cap_drop=0.66, spots=4)

    # the shield arm hugs the boss of the shield; the free arm makes a fist
    limb("WA_ArmR", (-0.330, -0.070, 1.185), (-0.470, -0.420, 0.930),
         0.150, 0.125, bark, C, bow=(-0.075, 0.0, 0.0))
    ball("WA_HandR", (-0.485, -0.470, 0.900), 0.170, dk, C)
    limb("WA_ArmL", (0.360, -0.040, 1.180), (0.545, -0.180, 0.640),
         0.150, 0.125, bark, C, bow=(0.095, -0.020, 0.0))
    ball("WA_HandL", (0.560, -0.215, 0.575), 0.185, dk, C)
    for k in range(3):
        a = -0.5 + k * 0.5
        spike("WA_Knuck{}".format(k),
              (0.560 + 0.080 * math.sin(a), -0.215 - 0.135 * math.cos(a), 0.660),
              (0.25 * math.sin(a), -math.cos(a), 0.60), 0.090, 0.036,
              M("gw_thorn"), C, bins=6, steps=3)

    # THE SHIELD: one giant cap, face out, gills in, rimmed and bossed
    sx, sy, sz, sr = -0.300, -0.640, 0.830, 0.640
    patch("WA_Shield", (sx, sy, sz), sr, cap, C,
          squash=(1.0, 0.30, 1.36), rot=(0.10, 0.0, 0.14), steps=9, bins=20)
    patch("WA_ShieldBack", (sx + 0.030, sy + 0.150, sz), sr * 0.94, pale, C,
          squash=(1.0, 0.20, 1.30), rot=(0.10, 0.0, 0.14), steps=8, bins=20)
    # gill ribs fanning off the boss on the inside face
    for k in range(11):
        a = math.pi * (k / 10.0) - math.pi / 2
        patch("WA_Gill{}".format(k),
              (sx + 0.030 + sr * 0.52 * math.sin(a), sy + 0.190,
               sz + sr * 1.20 * 0.52 * math.cos(a)),
              sr * 0.50, pale, C, squash=(0.10, 0.16, 1.0),
              rot=(0.0, -a, 0.0), steps=5, bins=8)
    # the rim: a thorn-studded hoop swept round the edge of the cap
    rim = [Vector((sx + sr * 0.985 * math.cos(t * 2 * math.pi / 40),
                   sy + 0.055,
                   sz + sr * 1.34 * math.sin(t * 2 * math.pi / 40)))
           for t in range(40)]
    hk.sweep("WA_Rim", rim, 0.062, sides=8, mat=dk, coll=C, closed=True)
    for k in range(9):
        a = 2 * math.pi * k / 9 + 0.3
        px = sx + sr * 0.985 * math.cos(a)
        pz = sz + sr * 1.34 * math.sin(a)
        spike("WA_Stud{}".format(k), (px, sy + 0.020, pz),
              (math.cos(a) * 0.55, -1.0, math.sin(a) * 0.55), 0.145, 0.052,
              M("gw_thorn"), C, bins=6, steps=3)
    # pale spots on the face of the cap, because a plain red oval is a stop sign
    for k, (fx, fz, rr) in enumerate(((-0.16, 0.38, 0.135), (0.24, 0.10, 0.115),
                                      (-0.06, -0.30, 0.150), (0.30, -0.52, 0.100),
                                      (-0.34, -0.08, 0.105))):
        # squash already lays these flat against the FACE of the cap; the first
        # pass also rotated them a quarter turn and they came out edge-on, five
        # white pills stuck to a red oval
        patch("WA_Spot{}".format(k),
              (sx + fx * sr * 1.2, sy - 0.150, sz + fz * sr * 1.30),
              rr, pale, C, squash=(1.0, 0.34, 1.0), steps=5, bins=10)
    ball("WA_Boss", (sx, sy - 0.185, sz), 0.185, dk, C, squash=(1.0, 0.72, 1.0))

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 6 / CASTER -- Sporecaller
# --------------------------------------------------------------------------- #

CASTER = "chr_enemy_greenwood_caster"


def caster():
    """A hooded caster with a floating orb and glowing hands.

    The hood is a drooping toadstool whose point flops forward over a face that
    is not there -- a dark hollow with two lights in it. The robe reaches the
    ground in three leaf tiers, so the creature has no legs and reads as sliding
    rather than walking, which is the cheapest way to make one enemy in a line
    of walkers look wrong. The orb held off the raised hand is the second half
    of the silhouette and the only bright spot on the model.
    """
    C = CASTER
    coll_clear(C)
    dk, leafm = M("gw_bark_dk"), M("gw_leaf")
    glow, robe = M("gw_glow_gn"), M("gw_cloth")

    # robe: a bell to the floor, no feet
    body("CA_Robe",
         [(0.010, 0.520, 0.470),
          (0.180, 0.545, 0.490),
          (0.480, 0.480, 0.430),
          (0.820, 0.400, 0.360),
          (1.120, 0.352, 0.318),
          (1.340, 0.318, 0.288),
          (1.450, 0.262, 0.240)], robe, C, bins=18)
    # three tiers of leaf hem, which is what makes the robe read as vegetable
    for tier, (z, r, n, ln) in enumerate(((0.150, 0.545, 14, 0.310),
                                          (0.520, 0.478, 12, 0.270),
                                          (0.870, 0.398, 10, 0.230))):
        for k in range(n):
            a = 2 * math.pi * k / n + tier * 0.35
            leaf("CA_Hem{}_{}".format(tier, k),
                 (r * 0.86 * math.sin(a), r * 0.86 * math.cos(a) * 0.92, z),
                 (math.sin(a) * 0.55, math.cos(a) * 0.55, -1.0), ln, ln * 0.34,
                 leafm if tier % 2 == 0 else M("gw_leaf_dry"), C,
                 curl=0.30, twist=0.4)
    # a rope of braided vine at the waist
    waist = [Vector((0.415 * math.cos(t * 2 * math.pi / 32),
                     0.375 * math.sin(t * 2 * math.pi / 32), 1.080))
             for t in range(32)]
    hk.sweep("CA_Cord", waist, 0.048, sides=8, mat=M("gw_moss_dk"), coll=C,
             closed=True)

    # the hollow: a dark void where a face should be, with two lights in it
    ball("CA_Void", (0.0, -0.115, 1.630), 0.300, M("gw_pupil"), C,
         squash=(1.0, 0.90, 1.05))
    # the lights sit PROUD of the void, not inside it: two glows buried a
    # centimetre under the surface of a black sphere are two glows nobody sees
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        ball("CA_EyeGlow" + s, (sgn * 0.120, -0.372, 1.658), 0.130, glow, C,
             squash=(1.0, 0.30, 1.34))
        ball("CA_Eye" + s, (sgn * 0.120, -0.412, 1.658), 0.040, M("gw_glow"), C,
             squash=(1.0, 0.72, 1.35))

    # the hood: a big toadstool cap drooping forward over the void
    hood = mushroom("CA_Hood", (0.0, 0.140, 1.480), 0.330, 0.520, 0.150,
                    M("gw_cap"), M("gw_stem"), C, lean=(0.0, -0.190),
                    cap_drop=0.92, spots=5)
    # the point of the cap flops forward: a second, smaller lobe hung off the
    # front rim, which is the shape that reads as a hood rather than a hat
    body("CA_HoodTip",
         [(1.960, 0.320, 0.265, 0.0, -0.210),
          (1.905, 0.290, 0.238, 0.0, -0.330),
          (1.850, 0.225, 0.185, 0.0, -0.450),
          (1.795, 0.140, 0.118, 0.0, -0.530),
          (1.745, 0.050, 0.044, 0.0, -0.560)], M("gw_cap"), C, bins=14)
    for sgn in (1, -1):
        # the hood's shoulders, hanging in two folds
        patch("CA_Fold" + ("L" if sgn > 0 else "R"),
              (sgn * 0.310, 0.070, 1.410), 0.290, M("gw_cap"), C,
              squash=(1.0, 1.0, 0.42), rot=(0.30, -sgn * 1.15, sgn * 0.35),
              steps=6, bins=14)

    # arms: thin, sleeved, both hands lit. The right is up under the orb.
    limb("CA_ArmR", (-0.300, -0.075, 1.320), (-0.560, -0.330, 1.680),
         0.115, 0.078, robe, C, bow=(-0.090, -0.050, 0.0))
    ball("CA_HandR", (-0.585, -0.375, 1.735), 0.115, glow, C)
    limb("CA_ArmL", (0.300, -0.060, 1.310), (0.540, -0.330, 1.055),
         0.115, 0.078, robe, C, bow=(0.090, -0.060, 0.0))
    ball("CA_HandL", (0.560, -0.375, 1.010), 0.110, glow, C)
    for sgn, (hx, hy, hz) in ((1, (0.560, -0.375, 1.010)),
                              (-1, (-0.585, -0.375, 1.735))):
        for k in range(3):
            a = -0.6 + k * 0.6
            spike("CA_Finger{}{}".format(1 if sgn > 0 else 0, k),
                  (hx + 0.070 * math.sin(a) * sgn, hy - 0.070 * math.cos(a),
                   hz - 0.030),
                  (sgn * 0.35 * math.sin(a), -math.cos(a), -0.35), 0.105, 0.028,
                  glow, C, bins=6, steps=3)

    # the orb: a glowing spore pod held off the raised hand, ringed by motes
    ball("CA_Orb", (-0.630, -0.470, 2.010), 0.195, M("gw_glow"), C)
    ball("CA_OrbCore", (-0.630, -0.470, 2.010), 0.128, glow, C)
    for k in range(7):
        a = 2 * math.pi * k / 7
        ball("CA_Mote{}".format(k),
             (-0.630 + 0.330 * math.cos(a), -0.470 + 0.115 * math.sin(a),
              2.010 + 0.245 * math.sin(a + 1.1)),
             0.040 + 0.016 * ((k % 3) / 2.0), M("gw_glow"), C, steps=4, bins=8)
    # spore motes drifting off the low hand too, so the glow reads as a habit
    for k in range(4):
        ball("CA_Spore{}".format(k),
             (0.590 + 0.10 * math.cos(k * 1.9), -0.430 - 0.06 * k,
              1.120 + 0.115 * k), 0.034, M("gw_glow"), C, steps=4, bins=8)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 7 / ELITE -- EL_THORN_SENTINEL, the Thorn Sentinel
# --------------------------------------------------------------------------- #

THORN_SENTINEL = "chr_elite_thorn_sentinel"


def thorn_sentinel():
    """A towering animated bramble knight in rose-thorn armour.

    `CH_01_GREENWOOD_VALE.json` lists this as a mini-boss and `enemies.json`
    gives it the WARDEN base archetype, so it had to read as heavy without
    repeating the standard warden's tower shield. It carries a bramble
    greatsword planted point-down instead: the same slow, rooted, unmovable
    idea told with a different outline. 3.20 tall -- half again the hero.

    The armour is genuinely woven rather than plated-and-striped: helical vines
    run the length of every limb, and the plates sit on top of them.
    """
    C = THORN_SENTINEL
    coll_clear(C)
    bram, dkbram = M("gw_bramble"), M("gw_bramble_dk")
    thorn, petalm = M("gw_thorn"), M("gw_petal")
    ivory, glow = M("gw_tusk"), M("gw_glow_gn")

    # legs: short, thick columns of braided vine. Short on purpose -- the height
    # is spent on chest, pauldrons and crest, which is what makes a knight read
    # as heavy rather than as tall and thin.
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("TS_Thigh" + s, (sgn * 0.330, 0.040, 1.180), (sgn * 0.430, -0.070, 0.640),
             0.300, 0.250, bram, C, bow=(sgn * 0.070, 0.0, 0.0))
        limb("TS_Shin" + s, (sgn * 0.430, -0.070, 0.640), (sgn * 0.445, 0.030, 0.190),
             0.250, 0.200, bram, C, bow=(0.0, -0.080, 0.0))
        vine("TS_LegVine" + s, (sgn * 0.330, 0.040, 1.160), (sgn * 0.445, 0.030, 0.220),
             0.280, 0.205, 0.058, dkbram, C, turns=2.6, phase=sgn * 0.8)
        ball("TS_Foot" + s, (sgn * 0.450, -0.190, 0.130), 0.350, dkbram, C,
             squash=(0.92, 1.36, 0.44))
        for k in range(3):
            spike("TS_Claw{}{}".format(s, k),
                  (sgn * 0.450 + (k - 1) * 0.165, -0.420, 0.095),
                  (0.22 * (k - 1), -1.0, -0.10), 0.280, 0.085, thorn, C,
                  bins=6, steps=3, curve=0.20)
        patch("TS_Knee" + s, (sgn * 0.440, -0.265, 0.690), 0.265, thorn, C,
              squash=(1.0, 0.52, 1.0), rot=(0.20, 0.0, 0.0))
        spike("TS_KneeSpike" + s, (sgn * 0.440, -0.355, 0.710),
              (sgn * 0.15, -1.0, 0.55), 0.360, 0.090, thorn, C, bins=7, curve=0.22)

    # torso: a slab of a chest over a short waist
    body("TS_Body",
         [(1.020, 0.430, 0.350),
          (1.280, 0.470, 0.380),
          (1.620, 0.660, 0.480),
          (1.950, 0.740, 0.530),
          (2.180, 0.660, 0.480),
          (2.300, 0.470, 0.360)], bram, C, bins=20)
    for k in range(3):
        vine("TS_BodyVine{}".format(k), (0.0, 0.0, 1.040), (0.0, 0.0, 2.260),
             0.500, 0.510, 0.065, dkbram, C, turns=1.6, phase=k * 2.09)
    # a breastplate of overlapping thorn scales
    for row, (z, n, rr) in enumerate(((1.420, 7, 0.215), (1.700, 7, 0.240),
                                      (1.960, 6, 0.220))):
        for k in range(n):
            a = -1.15 + 2.30 * (k / (n - 1.0))
            patch("TS_Scale{}{}".format(row, k),
                  (0.640 * math.sin(a), -0.470 * math.cos(a), z),
                  rr, thorn, C, squash=(1.0, 0.42, 0.86),
                  rot=(0.0, 0.0, -a), steps=5, bins=10)

    # Pauldrons: domed caps sitting ON the shoulder with a rolled rim under
    # them, and their spurs raked UP rather than out. Pushed wide and squashed
    # flat they came out as two maroon slugs floating beside the chest.
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        ball("TS_Pauldron" + s, (sgn * 0.690, 0.000, 1.960), 0.545, thorn, C,
             squash=(1.0, 1.00, 0.82), bins=18)
        rim = [Vector((sgn * 0.690 + 0.520 * math.cos(t * 2 * math.pi / 28),
                       0.470 * math.sin(t * 2 * math.pi / 28), 1.905))
               for t in range(28)]
        hk.sweep("TS_PauldronRim" + s, rim, 0.070, sides=7, mat=dkbram, coll=C,
                 closed=True)
        ball("TS_PauldronIn" + s, (sgn * 0.640, 0.000, 1.855), 0.470, dkbram, C,
             squash=(1.0, 1.0, 0.70), bins=16)
        for k in range(5):
            a = -1.05 + k * 0.525
            spike("TS_Spur{}{}".format(s, k),
                  (sgn * (0.690 + 0.30 * math.cos(a)), 0.42 * math.sin(a), 2.290),
                  (sgn * (0.34 + 0.30 * math.cos(a)), 0.55 * math.sin(a), 1.0),
                  0.560 - 0.07 * abs(k - 2), 0.095, thorn, C, bins=7, curve=0.20)

    # arms: both down and forward, gauntlets stacked on the pommel
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("TS_Upper" + s, (sgn * 0.790, 0.020, 1.960), (sgn * 0.520, -0.520, 1.640),
             0.235, 0.195, bram, C, bow=(sgn * 0.160, 0.0, -0.040))
        limb("TS_Fore" + s, (sgn * 0.520, -0.520, 1.640), (sgn * 0.155, -0.930, 1.500),
             0.195, 0.160, bram, C, bow=(sgn * 0.050, -0.110, -0.020))
        vine("TS_ArmVine" + s, (sgn * 0.780, 0.020, 1.940), (sgn * 0.170, -0.920, 1.510),
             0.215, 0.155, 0.048, dkbram, C, turns=2.2, phase=sgn * 1.2)
        ball("TS_Gauntlet" + s, (sgn * 0.135, -0.960, 1.470), 0.245, dkbram, C,
             squash=(1.0, 1.06, 1.0))

    # The greatsword, planted point-down. It is IVORY, and that is not decoration:
    # a thorn blade cut from the same dark maroon as the armour is a dark shape
    # inside a dark shape, and the pose stops reading at any distance.
    blade("TS_Blade", (0.0, -1.020, 1.430), (0.0, -0.05, -1.0), 1.450,
          0.430, 0.110, ivory, C, up=(1.0, 0.0, 0.0), belly=0.50)
    for sgn in (1, -1):
        blade("TS_Edge" + ("L" if sgn > 0 else "R"),
              (sgn * 0.215, -1.030, 1.355), (sgn * 0.10, -0.04, -1.0), 1.250,
              0.098, 0.038, thorn, C, up=(1.0, 0.0, 0.0), belly=0.55)
    limb("TS_Grip", (0.0, -1.010, 1.400), (0.0, -0.975, 1.960), 0.090, 0.082,
         dkbram, C)
    ball("TS_Pommel", (0.0, -0.965, 1.995), 0.155, thorn, C, squash=(1.0, 1.0, 0.86))
    for sgn in (1, -1):
        spike("TS_Quillon" + ("L" if sgn > 0 else "R"), (0.0, -1.015, 1.420),
              (sgn * 1.0, -0.10, 0.32), 0.540, 0.078, thorn, C, bins=7, curve=0.30)
    rose("TS_GuardRose", (0.0, -1.100, 1.440), (0.0, -1.0, 0.10), 0.280,
         petalm, C, whorls=2, per=6, open_angle=0.95)

    # helm: a woven cage over a dark hollow, two lights, and a rose crest
    ball("TS_Helm", (0.0, -0.075, 2.600), 0.490, dkbram, C, squash=(1.0, 1.0, 1.06))
    # A SLIT, not two eyes on a ball. Round eyes on a round helm was a beetle;
    # a horizontal band of dark with two lights inside it is a knight.
    body("TS_Visor",
         [(2.520, 0.430, 0.400, 0.0, -0.085),
          (2.590, 0.470, 0.430, 0.0, -0.095),
          (2.660, 0.430, 0.400, 0.0, -0.085)], M("gw_pupil"), C, bins=18)
    ball("TS_Nasal", (0.0, -0.420, 2.560), 0.115, thorn, C, squash=(0.55, 1.0, 2.2))
    ball("TS_Brow", (0.0, -0.290, 2.760), 0.450, thorn, C, squash=(1.0, 0.80, 0.30))
    for k in range(5):
        a = -0.90 + k * 0.45
        vine("TS_HelmBar{}".format(k), (0.0, -0.075, 2.140), (0.0, -0.075, 3.060),
             0.500, 0.140, 0.044, thorn, C, turns=0.30, phase=a)
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        ball("TS_Eye" + s, (sgn * 0.190, -0.475, 2.596), 0.135, glow, C,
             squash=(1.0, 0.42, 0.52))
        ball("TS_EyeCore" + s, (sgn * 0.190, -0.500, 2.596), 0.070, M("gw_glow"), C,
             squash=(1.0, 0.62, 0.46))
        spike("TS_HelmHorn" + s, (sgn * 0.350, 0.010, 2.790),
              (sgn * 0.62, 0.40, 1.0), 0.680, 0.098, thorn, C, bins=7, curve=0.26)
    rose("TS_Crest", (0.0, 0.010, 2.930), (0.0, -0.34, 1.0), 0.520, petalm, C,
         whorls=3, per=7, open_angle=1.10)
    # loose thorns bristling off the back
    for k in range(7):
        a = 2 * math.pi * k / 7 + 0.4
        spike("TS_Bristle{}".format(k),
              (0.630 * math.sin(a), 0.440 * math.cos(a), 1.700 + 0.26 * math.cos(a)),
              (math.sin(a), math.cos(a), 0.55), 0.360, 0.070, thorn, C,
              bins=6, curve=0.28)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 8 / ELITE -- EL_MOSSBACK_ALPHA, the Mossback Alpha
# --------------------------------------------------------------------------- #

MOSSBACK_ALPHA = "chr_elite_mossback_alpha"


def mossback_alpha():
    """A giant moss-covered boar with glowing green tusks.

    The only quadruped in the chapter, which does most of the work of telling
    it apart before any detail resolves: a long low wedge where everything else
    is an upright stack. The mass is thrown forward onto a huge mossy hump and
    a head almost as wide as the shoulders, and the hindquarters are small
    enough to look like an afterthought -- boar proportions pushed until they
    are funny. 3.4 long, 2.2 at the hump. `enemies.json` gives it the BRUTE
    base archetype and it is one of chapter one's two `miniBossIds`.
    """
    C = MOSSBACK_ALPHA
    coll_clear(C)
    hide, dk = M("gw_hide"), M("gw_bark_dk")
    moss, leafm = M("gw_moss"), M("gw_leaf")
    tuskm, glow = M("gw_tusk"), M("gw_glow_gn")

    # the barrel, nose to tail: widest and tallest at the shoulder hump
    body_y("MA_Body",
           [(1.150, 0.300, 0.290, 0.0, 1.180),
            (0.860, 0.480, 0.470, 0.0, 1.150),
            (0.480, 0.610, 0.590, 0.0, 1.120),
            (0.050, 0.700, 0.680, 0.0, 1.150),
            (-0.420, 0.780, 0.790, 0.0, 1.240),
            (-0.760, 0.760, 0.780, 0.0, 1.280),
            (-1.020, 0.640, 0.640, 0.0, 1.270),
            (-1.180, 0.500, 0.480, 0.0, 1.230)], hide, C, bins=20)
    # the hump: a slab of moss over the shoulders, the elite's read from behind
    ball("MA_Hump", (0.0, -0.560, 1.760), 0.760, moss, C,
         squash=(0.94, 1.15, 0.62), bins=20)
    ball("MA_Hump2", (0.0, -0.080, 1.640), 0.640, moss, C,
         squash=(0.92, 1.20, 0.50), bins=18)
    # a ridge of coarse moss and leaves down the spine
    for k in range(9):
        t = k / 8.0
        y = -0.900 + 1.900 * t
        frond("MA_Mane{}".format(k), (0.0, y, 1.900 - 0.62 * t * t - 0.10 * t),
              (0.0, 0.30, 1.0), 0.420 - 0.20 * t, 0.115 - 0.045 * t,
              leafm if k % 2 else M("gw_leaf_dry"), C, count=3, spread=0.85,
              curl=0.34)

    # four short legs: fronts braced under the hump, backs tucked
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("MA_FrontUp" + s, (sgn * 0.470, -0.660, 1.230), (sgn * 0.560, -0.760, 0.660),
             0.290, 0.215, hide, C, bow=(sgn * 0.060, 0.0, 0.0))
        limb("MA_FrontLo" + s, (sgn * 0.560, -0.760, 0.660), (sgn * 0.575, -0.700, 0.200),
             0.215, 0.155, dk, C, bow=(0.0, -0.055, 0.0))
        limb("MA_HindUp" + s, (sgn * 0.430, 0.560, 1.140), (sgn * 0.500, 0.760, 0.620),
             0.275, 0.200, hide, C, bow=(sgn * 0.055, 0.040, 0.0))
        limb("MA_HindLo" + s, (sgn * 0.500, 0.760, 0.620), (sgn * 0.510, 0.660, 0.200),
             0.200, 0.145, dk, C, bow=(0.0, 0.060, 0.0))
        for nm, hy in (("F", -0.700), ("H", 0.655)):
            hx = sgn * (0.575 if nm == "F" else 0.510)
            ball("MA_Hoof{}{}".format(nm, s), (hx, hy - 0.060, 0.110), 0.215, dk, C,
                 squash=(0.86, 1.15, 0.56))
            # cloven, and the split is worth the two extra shapes: a rounded
            # stump on a beast this size reads as an elephant
            for j in (1, -1):
                spike("MA_Toe{}{}{}".format(nm, s, 1 if j > 0 else 0),
                      (hx + j * 0.085, hy - 0.150, 0.075),
                      (j * 0.20, -1.0, -0.15), 0.190, 0.070, tuskm, C,
                      bins=6, steps=3, curve=0.16)

    # head: broad, low, almost as wide as the shoulders. `body_y` sections run
    # along the facing axis, so the whole head is authored at negative y.
    body_y("MA_Head",
           [(-1.000, 0.470, 0.450, 0.0, 1.245),
            (-1.290, 0.680, 0.635, 0.0, 1.300),
            (-1.560, 0.700, 0.630, 0.0, 1.290),
            (-1.820, 0.575, 0.505, 0.0, 1.200),
            (-2.020, 0.420, 0.355, 0.0, 1.110),
            (-2.140, 0.345, 0.290, 0.0, 1.060)], hide, C, bins=18)
    # snout disc and nostrils, which is the one shape that says `boar`
    body_y("MA_Snout",
           [(-2.110, 0.355, 0.300, 0.0, 1.062),
            (-2.230, 0.390, 0.330, 0.0, 1.052),
            (-2.305, 0.345, 0.290, 0.0, 1.046)], dk, C, bins=16)
    for sgn in (1, -1):
        ball("MA_Nostril" + ("L" if sgn > 0 else "R"),
             (sgn * 0.135, -2.315, 1.078), 0.076, M("gw_pupil"), C,
             squash=(1.0, 0.55, 1.25), steps=5, bins=10)
    # the jaw, hung under the muzzle so the tusks have somewhere to come from
    body_y("MA_Jaw",
           [(-1.480, 0.430, 0.230, 0.0, 0.930),
            (-1.780, 0.400, 0.215, 0.0, 0.925),
            (-2.060, 0.330, 0.180, 0.0, 0.940),
            (-2.190, 0.265, 0.145, 0.0, 0.960)], dk, C, bins=14)

    eyes("MA", (0.0, -1.580, 1.640), 0.150, C, spread=0.470,
         tilt=math.radians(30.0), pupil=0.46, brow=0.72, brow_mat="gw_hide",
         squash=(1.0, 0.86, 0.80))
    mouth_slot("MA_Mouth", (0.0, -2.280, 0.960), 0.300, 0.072, 0.230,
               M("gw_mouth"), C, curve=0.20)
    fangs("MA", (0.0, -2.265, 1.008), 0.230, 5, 0.100, 0.038, tuskm, C)

    # THE TUSKS: two great glowing sabres out of the lower jaw, the single
    # feature the manifest names for this elite. The tusk itself is the spore
    # green and only the last third is ivory. Running it the other way round --
    # an ivory sheath over a lit core -- hides the whole point behind the bone.
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        spike("MA_Tusk" + s, (sgn * 0.300, -2.110, 0.930),
              (sgn * 0.30, -0.58, 1.0), 1.020, 0.128, glow, C, bins=9,
              curve=-0.40, steps=8)
        # No ivory cap on the big tusk. Placing one by eye at what looks like
        # the tip of a CURVED spike puts it three quarters of the way along
        # instead, and the pair reads as four tusks rather than two.
        spike("MA_TuskLo" + s, (sgn * 0.380, -1.920, 0.870),
              (sgn * 0.28, -0.48, 1.0), 0.520, 0.072, tuskm, C, bins=7,
              curve=-0.32)
        # ears, laid back flat along the skull: the angry read
        patch("MA_Ear" + s, (sgn * 0.520, -1.100, 1.680), 0.340, hide, C,
              squash=(1.0, 1.0, 0.24), rot=(0.55, -sgn * 0.85, sgn * 0.60))

    # mushrooms and moss clumps growing out of the hump
    for k, (mx, my, mz, hgt, rr) in enumerate(((0.330, -0.320, 1.960, 0.240, 0.230),
                                               (-0.280, -0.640, 1.980, 0.290, 0.265),
                                               (0.120, 0.180, 1.870, 0.200, 0.185),
                                               (-0.420, 0.020, 1.800, 0.175, 0.160))):
        mushroom("MA_Shroom{}".format(k), (mx, my, mz), hgt, rr, rr * 0.30,
                 M("gw_cap"), M("gw_stem"), C, lean=(mx * 0.16, my * 0.10),
                 spots=3 if k < 2 else 0)
    # tail, short and tufted
    limb("MA_Tail", (0.0, 1.150, 1.320), (0.0, 1.520, 1.480), 0.090, 0.055, dk, C,
         bow=(0.0, 0.0, 0.090))
    frond("MA_TailTuft", (0.0, 1.530, 1.500), (0.0, 0.60, 0.80), 0.330, 0.100,
          leafm, C, count=4, spread=0.90, curl=0.40)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# 9 / BOSS -- BOSS_THORNMAW
# --------------------------------------------------------------------------- #

THORNMAW = "chr_boss_thornmaw"


def thornmaw():
    """A colossal carnivorous flower with a fanged maw, thick thorned vines for
    arms and glowing yellow pollen, rooted in mossy stone.

    Chapter one's boss, and `bosses.json` calls it the teaching boss: phase 1 is
    basic attacks and nothing else, phase 2 roots the party, phase 3 blooms and
    keeps summoning SWARM adds. The model is built to make those three readable
    without a single line of bespoke boss code -- the vine arms are the phase 2
    root, the open bloom full of pollen is the phase 3 bloom, and the acorn-like
    seed pods clustered at the base are where the adds come from.

    It does not face the player with a head on a body. It faces them with an
    open throat: the bloom is turned almost straight down the facing axis so the
    fixed 3/4 camera looks INTO the mouth, which is the whole idea of the
    silhouette. 4.20 tall, near twice the hero.
    """
    C = THORNMAW
    coll_clear(C)
    stalk_m, dk = M("gw_moss_dk"), M("gw_bramble_dk")
    thorn, petalm = M("gw_thorn"), M("gw_petal")
    leafm, moss = M("gw_leaf"), M("gw_moss")
    glow, gullet = M("gw_glow"), M("gw_gullet")

    # --- rooted in mossy stone -------------------------------------------- #
    for k, (bx, by, bz, rr, sq) in enumerate(
            ((0.00, 0.150, 0.230, 0.980, (1.00, 0.90, 0.52)),
             (0.860, -0.220, 0.190, 0.560, (1.00, 0.86, 0.46)),
             (-0.760, -0.320, 0.175, 0.500, (1.00, 0.90, 0.42)),
             (-0.180, 0.880, 0.165, 0.520, (1.00, 0.92, 0.40)),
             (0.520, 0.640, 0.140, 0.380, (1.00, 0.88, 0.44)))):
        ball("TM_Stone{}".format(k), (bx, by, bz), rr, M("gw_stone"), C,
             squash=sq, bins=14)
        patch("TM_StoneMoss{}".format(k), (bx * 0.9, by * 0.9 - 0.06, bz + rr * sq[2]),
              rr * 0.78, moss, C, squash=(1.0, 0.92, 0.26),
              rot=(0.12, 0.0, 0.4 * k))
    # roots gripping the rock -- eight tapered tubes clawing outward
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.22
        limb("TM_Root{}".format(k), (0.0, 0.060, 0.760),
             (1.180 * math.sin(a), 0.060 + 1.020 * math.cos(a), 0.075),
             0.190, 0.070, dk, C, bow=(0.10 * math.sin(a), 0.10 * math.cos(a), 0.310),
             bins=8)
    # seed pods at the foot: phase 3 summons SWARM, and this is where from
    for k, (px, py, pz, pr) in enumerate(((0.740, -0.640, 0.400, 0.220),
                                          (-0.620, -0.700, 0.360, 0.195),
                                          (1.040, 0.120, 0.360, 0.175),
                                          (-0.980, 0.240, 0.340, 0.165))):
        ball("TM_Pod{}".format(k), (px, py, pz), pr, M("gw_bark"), C,
             squash=(1.0, 0.94, 1.22), bins=12)
        ball("TM_PodCap{}".format(k), (px, py, pz + pr * 0.78), pr * 0.86, dk, C,
             squash=(1.0, 1.0, 0.60), bins=12)
        limb("TM_PodStem{}".format(k), (px * 0.45, py * 0.45, 0.520),
             (px, py, pz - pr * 0.4), 0.090, 0.062, stalk_m, C, bins=8)

    # --- the stalk ---------------------------------------------------------- #
    body("TM_Stalk",
         [(0.520, 0.560, 0.560),
          (0.980, 0.470, 0.470, 0.0, 0.030),
          (1.560, 0.395, 0.400, 0.0, 0.010),
          (2.140, 0.360, 0.375, 0.0, -0.090),
          (2.640, 0.375, 0.400, 0.0, -0.230),
          (2.980, 0.430, 0.470, 0.0, -0.380)], stalk_m, C, bins=18)
    for k in range(3):
        vine("TM_StalkVine{}".format(k), (0.0, 0.040, 0.560), (0.0, -0.330, 2.960),
             0.470, 0.430, 0.070, dk, C, turns=1.4, phase=k * 2.09)
    for k in range(14):
        t = k / 13.0
        a = 2.6 * k
        r = 0.53 - 0.14 * t
        spike("TM_StalkThorn{}".format(k),
              (r * math.sin(a), 0.04 - 0.42 * t * t + r * math.cos(a) * 0.9,
               0.640 + 2.220 * t),
              (math.sin(a), math.cos(a), -0.34), 0.300 - 0.09 * t, 0.072,
              thorn, C, bins=6, curve=0.26)
    # a skirt of big leaves at the base of the stalk
    for k in range(7):
        a = 2 * math.pi * k / 7 + 0.35
        leaf("TM_Leaf{}".format(k),
             (0.480 * math.sin(a), 0.060 + 0.480 * math.cos(a), 0.720),
             (math.sin(a), math.cos(a), 0.55), 1.180, 0.360, leafm, C,
             curl=0.42, twist=0.35)
    # sepals cupping the bloom
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.5
        leaf("TM_Sepal{}".format(k),
             (0.330 * math.sin(a), -0.330 + 0.330 * math.cos(a) * 0.9, 2.940),
             (math.sin(a) * 0.85, math.cos(a) * 0.85 - 0.55, 0.62), 0.860, 0.290,
             M("gw_leaf_dry"), C, curl=0.34, twist=0.30)

    # --- the bloom, turned down the facing axis ----------------------------- #
    hx, hy, hz = 0.0, -0.520, 3.020
    # The petals go on FIRST and go BEHIND: a ruff splayed almost flat around
    # the back of the mouth. Whorled forward the way a garden rose is, they
    # closed over the maw and the boss became a flower with no face at all.
    rose("TM_Bloom", (hx, hy + 0.300, hz), (0.0, -1.0, 0.12), 1.480, petalm, C,
         whorls=2, per=9, open_angle=1.44)
    # the gullet: a throat receding into the head, not a painted disc
    body_y("TM_Gullet",
           [(hy - 0.060, 0.900, 0.900, hx, hz),
            (hy + 0.200, 0.780, 0.780, hx, hz - 0.030),
            (hy + 0.470, 0.520, 0.520, hx, hz - 0.050),
            (hy + 0.720, 0.280, 0.280, hx, hz - 0.060),
            (hy + 0.900, 0.085, 0.085, hx, hz - 0.060)], gullet, C, bins=18)
    # The lip is a swept HOOP, not a lofted disc. Lofting it closed the front
    # of the head over the throat: the boss came out as a red plate with teeth
    # round the edge and no mouth behind them.
    lip = [Vector((hx + 0.960 * math.cos(t * 2 * math.pi / 36), hy - 0.130,
                   hz + 0.960 * math.sin(t * 2 * math.pi / 36)))
           for t in range(36)]
    hk.sweep("TM_Lip", lip, 0.155, sides=8, mat=petalm, coll=C, closed=True)
    # One ring of fangs set into the lip and raked FORWARD at the player, with
    # the long ones top and bottom. Aimed back into the throat they came out as
    # a row of blades lying across the face.
    # The fangs converge ACROSS the opening, lamprey-fashion, and only lean
    # forward. Aimed mostly down the facing axis they pointed straight at the
    # camera and the whole ring of them came out as a scatter of pale dots.
    for k in range(14):
        a = 2 * math.pi * k / 14 + math.pi / 14
        px, pz = 0.900 * math.cos(a), 0.900 * math.sin(a)
        scale = 0.72 + 0.42 * abs(math.sin(a))
        spike("TM_Fang{}".format(k), (hx + px, hy - 0.150, hz + pz),
              (-px, -0.62, -pz), 0.620 * scale, 0.105 * scale,
              M("gw_tusk"), C, bins=7, steps=4, curve=0.14)
    # the tongue, lolling out over the lower lip
    limb("TM_Tongue", (hx, hy + 0.600, hz - 0.150), (hx, hy - 0.980, hz - 0.680),
         0.250, 0.140, M("gw_mouth"), C, bow=(0.0, 0.0, -0.190), bins=10)
    ball("TM_TongueTip", (hx, hy - 1.030, hz - 0.720), 0.155, M("gw_mouth"), C,
         squash=(1.0, 1.20, 0.62))
    # stamens carrying the glowing yellow pollen the manifest asks for: a crown
    # of them arching out of the throat and over the upper lip
    for k in range(7):
        a = math.pi * (0.12 + 0.76 * (k / 6.0))
        r = 0.560 + 0.120 * ((k * 5) % 3)
        tipx = hx + r * math.cos(a) * 1.72
        tipz = hz + r * math.sin(a) * 1.72
        limb("TM_Stamen{}".format(k),
             (hx + r * math.cos(a) * 0.40, hy + 0.300, hz + r * math.sin(a) * 0.40),
             (tipx, hy - 0.480, tipz), 0.062, 0.042, M("gw_stem"), C, bins=7,
             bow=(0.0, 0.0, 0.12))
        ball("TM_Pollen{}".format(k), (tipx, hy - 0.535, tipz), 0.150, glow, C,
             squash=(1.0, 1.20, 1.0), steps=6, bins=12)
    # loose pollen drifting out of the bloom
    for k in range(10):
        a = 2.9 * k
        rr = 1.30 + 0.30 * ((k * 7) % 3)
        ball("TM_Mote{}".format(k),
             (hx + rr * math.cos(a) * 0.95, hy - 1.180 - 0.16 * (k % 4),
              hz + rr * math.sin(a) * 0.80), 0.058 + 0.026 * ((k % 3) / 2.0),
             glow, C, steps=4, bins=8)

    # --- thorned vine arms -------------------------------------------------- #
    for sgn in (1, -1):
        s = "L" if sgn > 0 else "R"
        limb("TM_ArmA" + s, (sgn * 0.320, -0.080, 2.240), (sgn * 1.280, -0.820, 2.020),
             0.310, 0.250, stalk_m, C, bow=(sgn * 0.250, -0.140, 0.460), bins=10)
        limb("TM_ArmB" + s, (sgn * 1.280, -0.820, 2.020), (sgn * 1.640, -1.620, 1.180),
             0.250, 0.180, stalk_m, C, bow=(sgn * 0.290, -0.200, -0.240), bins=10)
        vine("TM_ArmVine" + s, (sgn * 0.340, -0.090, 2.230), (sgn * 1.630, -1.600, 1.200),
             0.280, 0.175, 0.062, dk, C, turns=2.4, phase=sgn * 1.1)
        ball("TM_Knuckle" + s, (sgn * 1.640, -1.640, 1.160), 0.260, dk, C,
             squash=(1.0, 1.05, 0.94))
        for k in range(3):
            a = -0.70 + k * 0.70
            spike("TM_Claw{}{}".format(s, k),
                  (sgn * 1.640, -1.700, 1.150),
                  (sgn * (0.30 + 0.55 * math.sin(a)), -1.0, -0.55 + 0.62 * math.cos(a)),
                  0.740, 0.120, thorn, C, bins=7, steps=5, curve=0.28)
        for k in range(6):
            t = (k + 0.5) / 6.0
            spike("TM_ArmThorn{}{}".format(s, k),
                  (sgn * (0.380 + 1.230 * t), -0.100 - 1.480 * t * t,
                   2.260 - 1.060 * t * t),
                  (sgn * 0.45, -0.35, 1.0), 0.330, 0.078, thorn, C,
                  bins=6, curve=0.24)
        leaf("TM_ArmLeaf" + s, (sgn * 0.920, -0.520, 2.220),
             (sgn * 0.55, -0.30, 1.0), 0.760, 0.260, leafm, C, curl=0.36,
             twist=0.4 * sgn)

    smooth_all(C, 1)
    return bpy.data.collections[C]


# --------------------------------------------------------------------------- #
# the cast
# --------------------------------------------------------------------------- #

CAST = (
    # (collection / glb stem, builder, role)
    (GRUNT, grunt, "standard"),
    (SWARM, swarm, "standard"),
    (BRUTE, brute, "standard"),
    (SKIRMISHER, skirmisher, "standard"),
    (WARDEN, warden, "standard"),
    (CASTER, caster, "standard"),
    (THORN_SENTINEL, thorn_sentinel, "elite"),
    (MOSSBACK_ALPHA, mossback_alpha, "elite"),
    (THORNMAW, thornmaw, "boss"),
)


def settle(name):
    """Drop a creature so its lowest evaluated point sits exactly on z = 0.

    Every builder aims for feet-on-the-floor by hand and every builder misses by
    a centimetre or two, because the thing that ends up lowest is usually a toe
    spike or a hem leaf added after the legs were measured. The engine puts
    these on a board tile at y = 0, so `about right` is a creature hovering or a
    creature with its ankles in the ground. Measured after modifiers: it is the
    subdivided surface that touches the tile, not the control cage.
    """
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    lowest = 1e9
    for ob in bpy.data.collections[name].objects:
        if ob.type != "MESH":
            continue
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        for v in me.vertices:
            lowest = min(lowest, (ob.matrix_basis @ v.co).z)
        ev.to_mesh_clear()
    if lowest > 1e8:
        return 0.0
    for ob in bpy.data.collections[name].objects:
        ob.location.z -= lowest
    return -lowest


def flatten(name):
    """Bake every object transform into its mesh, leaving all of them on the
    identity.

    Half this kit places geometry by moving an object -- `patch` builds on the
    origin and then rotates, `settle` shifts everything up by a centimetre. The
    export joins into whichever object is active, and the joined result keeps
    THAT object's transform, so the glTF root node comes out carrying a stray
    translation and the mesh inside it is authored off the floor. The hero has
    an identity root; so should these.
    """
    identity = Matrix.Identity(4)
    for ob in bpy.data.collections[name].objects:
        # `matrix_basis`, NOT `matrix_world`. Nothing here has a parent so they
        # hold the same value, but `matrix_world` is the depsgraph's cached copy
        # and it does not refresh until the scene is re-evaluated -- so it can
        # still read as the identity for an object `settle` moved a line ago,
        # and every one of those gets skipped.
        if ob.type != "MESH" or ob.matrix_basis == identity:
            continue
        ob.data.transform(ob.matrix_basis)
        ob.matrix_basis = identity.copy()
    return name


def build_all():
    """Every creature in the chapter, in one call. Idempotent."""
    materials()
    stage()
    out = []
    for name, fn, _role in CAST:
        out.append(fn())
        settle(name)
        flatten(name)
    show_all()
    return out


def contact_sheet(out_dir, width=560, height=760, silhouettes=True):
    """Render one PNG per creature from the fixed 3/4 low camera, and a matching
    black-on-white silhouette of each.

    The silhouette pass is the acceptance test, not a nicety: the style bible
    makes `identifiable filled black at 64 px` the quality bar, and the only way
    to answer that is to look at it filled black. Rendered with a flat emission
    override rather than by turning the lights off, because an unlit render is
    not black -- it is dark grey with the world colour behind it.
    """
    import os
    scene = bpy.context.scene
    prev = (scene.render.engine, scene.render.filepath,
            scene.render.resolution_x, scene.render.resolution_y,
            scene.render.film_transparent, scene.camera)
    # EEVEE was renamed BLENDER_EEVEE_NEXT for two releases and back again; ask
    # the enum which name this build uses rather than hardcoding either
    eevee = [i.identifier for i in
             bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items
             if "EEVEE" in i.identifier]
    scene.render.engine = eevee[0] if eevee else "CYCLES"
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.image_settings.file_format = "PNG"
    os.makedirs(out_dir, exist_ok=True)

    cam_data = bpy.data.cameras.get("GW_Cam") or bpy.data.cameras.new("GW_Cam")
    cam_data.lens = 50.0
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 36.0
    cam = bpy.data.objects.get("GW_Cam") or bpy.data.objects.new("GW_Cam", cam_data)
    if cam.name not in bpy.context.scene.collection.objects:
        bpy.context.scene.collection.objects.link(cam)
    scene.camera = cam

    flat = bpy.data.materials.get("GW_Silhouette")
    if flat is None:
        flat = bpy.data.materials.new("GW_Silhouette")
        flat.use_nodes = True
        nt = flat.node_tree
        for n in list(nt.nodes):
            if n.type != "OUTPUT_MATERIAL":
                nt.nodes.remove(n)
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
        nt.links.new(em.outputs[0], nt.nodes["Material Output"].inputs["Surface"])

    written = []
    for name, _fn, _role in CAST:
        m = measure(name)
        isolate(name)
        top = max(m["top"], 0.8)
        # frame from the creature's own extents rather than from a fudge factor,
        # because the cast runs from a 0.85 acorn to a 4.9 flower and one
        # distance cannot serve both
        tan_v = (cam_data.sensor_height * 0.5) / cam_data.lens
        tan_h = tan_v * (width / float(height))
        dist = max((top * 1.16 * 0.5) / tan_v,
                   (max(m["size"][0], m["size"][1]) * 1.16 * 0.5) / tan_h) * 1.08
        target = Vector((0.0, -0.08 * m["size"][1], top * 0.50))
        az, el = math.radians(26.0), math.radians(7.0)
        # -Y is the facing direction, so the camera goes at negative Y: in front
        # of the creature, swung round by `az` and lifted by `el`
        cam.location = target + Vector((math.sin(az) * math.cos(el),
                                        -math.cos(az) * math.cos(el),
                                        math.sin(el))) * dist
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

        scene.render.film_transparent = True
        scene.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        written.append(scene.render.filepath)

        if silhouettes:
            scene.view_layers[0].material_override = flat
            scene.render.film_transparent = True
            scene.render.filepath = os.path.join(out_dir, name + "_silhouette.png")
            bpy.ops.render.render(write_still=True)
            scene.view_layers[0].material_override = None
            written.append(scene.render.filepath)

    show_all()
    (scene.render.engine, scene.render.filepath, scene.render.resolution_x,
     scene.render.resolution_y, scene.render.film_transparent,
     scene.camera) = prev
    return written


def measure(name):
    """Evaluated triangle count and bounding box of one built creature.

    The count is what the modifier stack actually produces, not what the base
    meshes hold, because the subdivision is where nearly all of it comes from
    and the export decimates against this number.
    """
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    tris, lo, hi = 0, [1e9] * 3, [-1e9] * 3
    for ob in bpy.data.collections[name].objects:
        if ob.type != "MESH":
            continue
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
        for v in me.vertices:
            p = ob.matrix_basis @ v.co
            for i in range(3):
                lo[i], hi[i] = min(lo[i], p[i]), max(hi[i], p[i])
        ev.to_mesh_clear()
    return {"name": name, "tris": tris,
            "size": [round(hi[i] - lo[i], 3) for i in range(3)],
            "floor": round(lo[2], 3), "top": round(hi[2], 3),
            "objects": len(bpy.data.collections[name].objects)}
