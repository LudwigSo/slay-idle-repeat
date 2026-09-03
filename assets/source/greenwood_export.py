"""Bake and export the Greenwood Vale cast to the `.glb` files the client ships.

Nine creatures, each collapsed to one mesh with one baked material -- one
primitive, one draw call -- exactly the way `hero_export` does it for the hero.
That is not a coincidence and not copied code: this module calls
`hero_export.bake_asset` directly, so the two assets cannot drift apart in
recipe. If the hero's bake changes, these change with it.

    import greenwood_export
    greenwood_export.export_all(r"...\\src\\SlayIdleRepeat.Client\\game\\art")

What is different is the BUDGET. The hero is one actor on screen and gets 22k
triangles with a 1024 base colour. A chapter-one fight puts up to five enemies
out at once, so a standard enemy is held near 10k with 512 maps; the two elites
appear alone and get roughly 16k with 1024; the boss gets 24k with 1024. Those
are the numbers `BUDGET` states, and `plan()` turns them into decimate ratios by
measuring what each creature actually builds rather than by guessing.

Texture names: the Godot importer extracts embedded images to
`<glb stem>_<image name>.png`, so every asset here bakes into images called
plainly `basecolor` / `orm` / `normal`. The stem already says which creature it
is; a prefix would only produce `chr_boss_thornmaw_thornmaw_basecolor.png`.
`hero_export.bake_asset` deletes the images once the file is written, so the
next creature in the loop can reuse the same three names.
"""

import os

import bpy

import greenwood_kit as gw
import hero_export as hx

# (base colour size, ORM/normal size, target triangles, bake a normal map)
BUDGET = {
    "standard": (512, 512, 10000, True),
    "elite": (1024, 512, 16000, True),
    "boss": (1024, 1024, 24000, True),
}

# Decimate collapse is not linear in visible quality: below about a fifth the
# collapse starts eating silhouette rather than interior, and a thorn tip is
# all silhouette. Anything the plan wants to push under this is left here and
# reported instead, so an over-budget asset is a number in the log rather than
# a creature quietly turned to mush.
MIN_RATIO = 0.20


def plan(built=None):
    """Per-creature export settings, with the decimate ratio derived from the
    triangle count the creature actually builds at subdivision level 1."""
    rows = []
    for name, _fn, role in gw.CAST:
        base, maps, target, normal = BUDGET[role]
        tris = gw.measure(name)["tris"]
        # `bake_asset` pins subdivision to level 1 on the way through, which is
        # also what `measure` reports, so this ratio is against the right count.
        ratio = min(1.0, target / float(tris))
        clamped = max(ratio, MIN_RATIO)
        rows.append({
            "stem": name, "role": role, "source_tris": tris,
            "base_size": base, "map_size": maps, "want_normal": normal,
            "decimate": round(clamped, 4),
            "target": target,
            "expected_tris": int(tris * clamped),
            "clamped": clamped > ratio + 1e-9,
        })
    return rows


def export_all(art_dir, only=None):
    """Build, settle and bake every creature. Returns one row per file."""
    gw.build_all()
    out = []
    for row in plan():
        if only and row["stem"] not in only:
            continue
        parts = [o for o in bpy.data.collections[row["stem"]].objects
                 if o.type == "MESH"]
        res = hx.bake_asset(
            parts, os.path.join(art_dir, row["stem"] + ".glb"), "",
            base_size=row["base_size"], map_size=row["map_size"],
            decimate=row["decimate"], want_normal=row["want_normal"],
            island_margin=0.006)
        res.update({"stem": row["stem"], "role": row["role"],
                    "decimate": row["decimate"], "source_tris": row["source_tris"],
                    "target": row["target"], "clamped": row["clamped"]})
        out.append(res)
        print("{:38s} {:6d} -> {:6d} tris (target {}, ratio {}){}".format(
            row["stem"], row["source_tris"], res["tris"], row["target"],
            row["decimate"], "  CLAMPED" if row["clamped"] else ""))
    return out
