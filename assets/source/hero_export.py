"""Bake and export the hero kit to the `.glb` files the client ships.

The models are authored as many small objects carrying procedural shaders, and
neither of those things is what a phone wants to draw. This module collapses an
asset to one mesh with one baked material -- one primitive, one draw call --
and writes it out as glTF.

It works on *copies*. Nothing here modifies the authored objects, so it can be
run repeatedly against the same `.blend` without the file slowly degrading into
its own export.

    import hero_export
    hero_export.export_all(r"...\\src\\SlayIdleRepeat.Client\\game\\art")

Texture names matter: the Godot importer is set to extract embedded images to
sibling PNGs named `<glb stem>_<image name>.png`, so the hero images are called
`hero_basecolor` / `hero_orm` / `hero_normal` to keep landing on the filenames
already committed next to `chr_hero_rogue.glb`.
"""

import os

import bpy
import numpy as np

import hero_kit as kit

BAKE_COLL = "_bake"


# --------------------------------------------------------------------------- #
# scaffolding
# --------------------------------------------------------------------------- #

def _reveal():
    """Baking drives operators, and operators refuse to touch hidden objects."""
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.hide_viewport = lc.exclude = False


def _select(objects, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = active or (objects[0] if objects else None)


def _scratch(objects, subsurf_levels=1):
    """Copies of `objects`, in their own collection, with modifiers applied.

    Subdivision is pinned on the way through. The hero is authored at two levels
    so it reads while being modelled, and two levels is 248k triangles -- four
    times what the decimator was tuned against, and four times what a phone
    should be asked to carry."""
    old = bpy.data.collections.get(BAKE_COLL)
    if old is not None:
        for ob in list(old.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
    copies = []
    for src in objects:
        cp = src.copy()
        cp.data = src.data.copy()
        cp.name = "_bake_" + src.name
        for m in cp.modifiers:
            if m.type == "SUBSURF":
                m.levels = m.render_levels = subsurf_levels
        kit.link(cp, BAKE_COLL)
        copies.append(cp)
    _select(copies)
    bpy.ops.object.convert(target="MESH")
    return copies


def _purge():
    """Drop the orphaned meshes, materials and images a previous export left
    behind. Without this the names creep -- `chr_hero_rogue.001`, then `.002` --
    because Blender will not reuse a name an unsaved orphan is still holding,
    and the export stops being reproducible."""
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for block in list(collection):
            if block.users == 0 and not getattr(block, "use_fake_user", False):
                collection.remove(block)


def _image(name, size, non_color=False):
    old = bpy.data.images.get(name)
    if old is not None:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, size, size, alpha=False,
                              float_buffer=False, is_data=non_color)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def _bake_target(obj, image):
    """Point every material on `obj` at one image, which is how Cycles is told
    where a bake should land."""
    for slot in obj.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get("BAKE_TARGET")
        if node is None:
            node = nt.nodes.new("ShaderNodeTexImage")
            node.name = node.label = "BAKE_TARGET"
            node.location = (-1500, -600)
        node.image = image
        for n in nt.nodes:
            n.select = False
        node.select = True
        nt.nodes.active = node


def _drop_bake_targets(obj):
    for slot in obj.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get("BAKE_TARGET")
        if node is not None:
            nt.nodes.remove(node)


def _baked_material(name, basecolor, orm, normal):
    """One material reading the baked set. Metallic is wired to a constant zero
    rather than to the ORM blue channel, because it is zero by rule and a
    texture that can only ever say zero is a texture fetch for nothing."""
    m = bpy.data.materials.get(name)
    if m is not None:
        bpy.data.materials.remove(m)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    bsdf = N["Principled BSDF"]
    bsdf.inputs["Metallic"].default_value = 0.0

    tex_bc = N.new("ShaderNodeTexImage")
    tex_bc.image = basecolor
    tex_bc.location = (-700, 300)
    L.new(tex_bc.outputs["Color"], bsdf.inputs["Base Color"])

    tex_orm = N.new("ShaderNodeTexImage")
    tex_orm.image = orm
    tex_orm.location = (-700, 0)
    sep = N.new("ShaderNodeSeparateColor")
    sep.location = (-420, 0)
    L.new(tex_orm.outputs["Color"], sep.inputs["Color"])
    L.new(sep.outputs["Green"], bsdf.inputs["Roughness"])

    if normal is not None:
        tex_n = N.new("ShaderNodeTexImage")
        tex_n.image = normal
        tex_n.location = (-700, -320)
        nm = N.new("ShaderNodeNormalMap")
        nm.location = (-420, -320)
        L.new(tex_n.outputs["Color"], nm.inputs["Color"])
        L.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return m


# --------------------------------------------------------------------------- #
# the recipe
# --------------------------------------------------------------------------- #

def bake_asset(objects, out_glb, prefix, base_size=1024, map_size=512,
               decimate=None, want_normal=True, extras=(), island_margin=0.006):
    """Collapse `objects` into one baked mesh and write `out_glb`.

    `extras` are exported alongside the mesh but never joined into it -- that is
    how the socket empties survive into the file as nodes the engine can parent
    a weapon to.

    `prefix` names the images, and the Godot importer extracts them to
    `<glb stem>_<image name>.png`, so it is the empty string for anything whose
    stem already says what the asset is. The images are deleted again once the
    file is written: two assets both wanting to call an image `basecolor` is
    only a collision if the first one is still holding the name.
    """
    _reveal()
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    _purge()
    copies = _scratch(objects)
    _select(copies, active=copies[0])
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active

    if decimate:
        d = ob.modifiers.new("Decimate", "DECIMATE")
        d.decimate_type, d.ratio = "COLLAPSE", decimate
        _select([ob], ob)
        bpy.ops.object.convert(target="MESH")
        ob = bpy.context.view_layer.objects.active

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15192, island_margin=island_margin)
    bpy.ops.object.mode_set(mode="OBJECT")

    scene = bpy.context.scene
    engine, samples = scene.render.engine, getattr(scene.cycles, "samples", 128)
    scene.render.engine = "CYCLES"
    # Every pass baked here is data, not light transport, so one sample is not
    # an approximation -- it is the answer.
    scene.cycles.samples = 1
    scene.render.bake.margin = 8
    scene.render.bake.use_selected_to_active = False

    basecolor = _image(prefix + "basecolor", base_size)
    rough = _image(prefix + "rough_tmp", map_size, non_color=True)
    orm = _image(prefix + "orm", map_size, non_color=True)
    normal = _image(prefix + "normal", map_size, non_color=True) if want_normal else None

    _select([ob], ob)
    _bake_target(ob, basecolor)
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True)

    _bake_target(ob, rough)
    bpy.ops.object.bake(type="ROUGHNESS", use_clear=True)

    if want_normal:
        _bake_target(ob, normal)
        bpy.ops.object.bake(type="NORMAL", use_clear=True)

    # Pack occlusion / roughness / metallic the way glTF wants them. Occlusion
    # is flat white: this build has no baked AO to put there.
    n = map_size * map_size * 4
    buf = np.empty(n, dtype=np.float32)
    rough.pixels.foreach_get(buf)
    packed = np.empty(n, dtype=np.float32)
    packed[0::4] = 1.0
    packed[1::4] = buf[0::4]
    packed[2::4] = 0.0
    packed[3::4] = 1.0
    orm.pixels.foreach_set(packed)
    orm.update()
    bpy.data.images.remove(rough)

    # The join keeps whichever name was active, so the engine would otherwise
    # meet a node called `WPN_SwordBlade.002`. Name it after the asset instead.
    stem = os.path.splitext(os.path.basename(out_glb))[0]
    ob.name = ob.data.name = stem

    _drop_bake_targets(ob)
    ob.data.materials.clear()
    ob.data.materials.append(_baked_material(stem + "_baked", basecolor, orm, normal))

    scene.render.engine, scene.cycles.samples = engine, samples

    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    _select(list(extras) + [ob], ob)
    os.makedirs(os.path.dirname(out_glb), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=out_glb, export_format="GLB", use_selection=True,
        export_yup=True, export_apply=False, export_materials="EXPORT",
        export_image_format="AUTO", export_cameras=False, export_lights=False,
        export_extras=False, export_animations=False,
    )

    bpy.data.objects.remove(ob, do_unlink=True)
    names = [i.name for i in (basecolor, orm, normal) if i]
    for img in (basecolor, orm, normal):
        if img is not None:
            bpy.data.images.remove(img)
    return {"glb": out_glb, "tris": tris, "images": names}


def export_all(art_dir):
    """Every deliverable of the kit, in one call."""
    _reveal()
    _purge()
    kit.preview_clear()
    sockets = [bpy.data.objects[n] for n in kit.SOCKET_POSE]
    body = [o for o in bpy.data.collections[kit.HERO_COLL].objects
            if o.type == "MESH" and not o.name.startswith(kit.PREVIEW_TAG)]

    out = [bake_asset(body, os.path.join(art_dir, "chr_hero_rogue.glb"), "hero_",
                      base_size=1024, map_size=512, decimate=0.30,
                      want_normal=True, extras=sockets)]
    # A weapon covers a fraction of the pixels the hero does, so it gets a
    # fraction of the texture and no normal map at all: at this size a baked
    # normal is bytes nobody can see.
    for stem in ("wpn_sword", "wpn_dagger"):
        parts = [o for o in bpy.data.collections[stem].objects if o.type == "MESH"]
        out.append(bake_asset(parts, os.path.join(art_dir, stem + ".glb"), "",
                              base_size=256, map_size=256, decimate=None,
                              want_normal=False, island_margin=0.012))
    return out
