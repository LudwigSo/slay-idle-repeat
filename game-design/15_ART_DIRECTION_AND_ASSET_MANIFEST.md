# 15 — Art Direction & 3D Asset Manifest

**This is the asset-production document.** Everything visual in Slay Idle Repeat is produced as **real-time 3D**: toon-shaded meshes rendered in the client, plus a set of 2D images *rendered from* those meshes, plus a residue of genuinely flat 2D that 3D would only make worse. This file defines the style, the production method, the technical output spec, and the complete list of what must be produced.

🔒 LOCKED: **Chibi cartoon fantasy, bold outlines.** The *look* is unchanged and stays locked (`16` D5).

🔓 **UNLOCKED: the medium and the tools.** This document was previously a 2D sprite manifest generated with Midjourney, and both the medium and the vendor were locked. Ruling **D60** (2026-08-23) replaces the medium with real-time 3D and, deliberately, **locks no tool at all** — see §B0.

> ## ⚠️ STATUS: WORK IN PROGRESS — NOT FOR PUBLICATION
>
> Everything produced under this document is **work in progress and does not ship to players**. That is a deliberate standing condition, not a phase this document is waiting to exit.
>
> Two consequences, both intentional:
>
> * **Licence terms are not a gate.** Ruling D60 suspends the `15` §G / `20` §6 licence precondition *for unpublished work*. The risks are real and are catalogued in **`31_ASSET_LICENCE_RISKS.md`** — that document is a **register, not a gate**. Nothing in it blocks production, and nothing in it may be treated as blocking without a new ruling. It becomes a gate again the day publication is on the table.
> * **Numbers marked ⚠️ PROVISIONAL are not authorised.** Every performance budget in Part C is an engineering *estimate written down so it can be measured and argued with*, not a product decision. None has been measured on a real device. Steering rule S6 forbids treating a plausible number as a decided one: a ⚠️ PROVISIONAL value may be built against, and must **not** be cited as authority, hardcoded as a named constant, or copied into `assets/pipeline/thresholds.json`.

---

# PART A — STYLE BIBLE

## A1. The look in one sentence

> Chunky chibi fantasy characters as toon-shaded 3D meshes with a uniform dark inverted-hull outline, saturated candy-jewel colours, two-band cel shading, and a glossy mobile-game finish — readable as a silhouette at 64 px on a phone screen in daylight.

The sentence is deliberately almost identical to the 2D one it replaces. **The target image did not change; the way it is produced did.** If a 3D asset does not look like the 2D concept art it descends from, the 3D asset is wrong.

## A2. Reference vocabulary

Neighbouring visual territory (for direction only — never copy or name these in prompts): *Legend of Slime*, *Top Heroes*, *Archer Forest*, *Hero Wars* casual art, modern Disney-adjacent mobile RPG UI.

For the 3D execution specifically the target is **stylised toon 3D that reads as 2D art**, not stylised realism. If a viewer can tell it is 3D from a still frame, the shading is doing too much.

## A3. Non-negotiable style rules

| Rule | Specification |
|---|---|
| **Proportions** | Characters are 2.5 to 3 heads tall. Big head, small body, oversized hands and feet, tiny or no neck. Enforced by the shared base mesh (§B2), not by eye. |
| **Outline** | Every character and prop has a uniform dark outline, produced by an **inverted-hull shell** — a backface-rendered, normal-extruded duplicate — *not* a post-process edge filter, which cannot hold a uniform width against a depth buffer on the Mobile renderer. Colour `#231A2E` (never pure black). Width is **screen-space constant** so it does not thin with distance or scale: 3–4 px at the actor viewport's render height (§C6). |
| **Shading** | **Two-band cel ramp, not PBR.** One base, one shadow at 85% value / +8% saturation, hard terminator. One soft rim light from the upper left. No metallic-roughness response, no gradients across large areas, no airbrushing, and **no ambient occlusion in the shader** — AO is baked into the albedo (§B4) where it is wanted and nowhere else. |
| **Highlight** | A single crisp specular highlight on metal, gems and eyes, from a **stepped** specular term — hard-edged, one band. Never a smooth Blinn-Phong falloff. |
| **Eyes** | Large, expressive, high-contrast. **Texture-driven, never geometry** — an eye modelled as a sphere reads as a doll's eye and puts an outline where none belongs. Two-tone iris with a white catchlight at upper-left. Enemies may have glowing eyes with no iris, via the emissive channel. |
| **Colour** | Saturated, jewel-like. Avoid muddy mid-tones. Each biome has a locked 6-colour palette (§A5), enforced by albedo quantisation at bake time (§B4 step 6) — **not** left to the shader. |
| **Lighting** | Consistent key light from the **upper left** in every single asset. In 3D this is a property of the **actor viewport's own fixed light rig** (§C6), identical for every actor, and **never** inherited from a scene or biome. A biome tints the backdrop; it does not touch the key light. |
| **Perspective** | Actors: a **fixed 3/4 camera at a slight low angle** so they read as heroic — the camera is part of the spec, not a per-scene choice (§C6). Rendered icons: **orthographic, straight-on**, no perspective convergence. |
| **Background** | Actor viewports render with a **transparent background**. No shadow baked into the mesh or the albedo — contact shadows are a separate engine-drawn decal. |
| **Detail budget** | Low, and now doubly so: geometric detail costs vertices *and* outline noise. **If a detail is not readable at 64 px it must not exist as geometry** — bake it to the albedo or delete it. Chunky shapes beat fine ornament. |
| **Text** | **Never** in a mesh, a texture, or a UV layout. All text is engine-rendered. |
| **Silhouette-first** | The block-out is approved on silhouette alone, before any detail pass (§B2). A model whose silhouette fails cannot be rescued by texturing. |

## A4. Silhouette test

Every character asset must pass this test before acceptance: render it from the canonical §C6 camera, fill it 100% black, scale to 64 px. If you cannot tell which character it is, remodel it. Silhouette clarity is the single most important quality bar in a chibi mobile game, and 3D makes it **easier to get wrong** — a shape that reads from one angle can collapse from the canonical one.

Run the test on the **block-out**, not the finished asset. Discovering a silhouette failure after rigging and texturing wastes the whole downstream pipeline.

## A5. Biome palettes

Every asset for a biome uses only these six hues plus neutrals. Locked into the albedo at bake time (§B4 step 6).

| Chapter | Biome | Palette (base · shadow · accent · glow · prop · sky) |
|---|---|---|
| 1 | Greenwood Vale | `#5FBF5F` `#2F7A3F` `#F2D06B` `#FFF3A8` `#8B5E3C` `#9FE0F0` |
| 2 | Ashen Mire | `#6B5A8E` `#3B2E52` `#8FBF5F` `#C7F26B` `#4A3B2E` `#8E7BA8` |
| 3 | Sunken Crypt | `#4A6E7A` `#233A45` `#E8E3C8` `#6BF2D6` `#5A5148` `#1E2A33` |
| 4 | Emberpeak | `#C4462A` `#6E1E14` `#F2A03C` `#FFD86B` `#3A2A28` `#2A1A1E` |
| 5 | Frostbound Reach | `#7EC8E8` `#3E6E96` `#E8F6FF` `#A8E8FF` `#5A6E8E` `#2E4A6E` |
| 6 | Clockwork Vaults | `#C89A4A` `#7A5A28` `#4AC8B4` `#8FF2E0` `#5A4A3A` `#2E2A28` |
| 7 | Bloom of Decay | `#D46BA8` `#7A2E5A` `#8FE86B` `#D8FF8F` `#5A3A4A` `#3A2A38` |
| 8 | Astral Spire | `#7A5AD8` `#3A2A7A` `#F2C86B` `#C8A8FF` `#2E2A4A` `#141028` |

**Rarity colours** (used on frames, gems, glows — identical across all biomes):
`C #9AA5B1` · `B #4CAF50` · `A #3B82F6` · `S #F5A623` · `SS #C13BE8`

## A6. What 3D does *not* change 🔒

The game remains a **portrait, one-handed, 2D-UI mobile game**. 3D is confined to actor viewports and to the offline render rig. Specifically:

* **The UI stays 2D.** No perspective UI, no 3D panels, no world-space menus, no camera move on a screen transition. `13`'s 37 screens are unaffected as layouts.
* **The design canvas stays 1080×1920 `canvas_items`, and the stretch mode does not change.** 🔓 **What changed is which way round the two are composited — see D61.** This bullet used to read "3D content is composited *into* that canvas through a `SubViewport` at the canvas's scale". It is the other way round: **every screen scene roots at `Node3D`, its 3D world renders to the main viewport, and the whole interface sits above it on a `CanvasLayer`.** Under `canvas_items` the root viewport stays at the window's own size and only the canvas transform is scaled, so the UI is still laid out in 1080×1920 units — the 2D half of this bullet is untouched. The 3D half is not: it renders at the device's real resolution rather than at the canvas's scale.
* **Godot's Mobile renderer stays.** ⚠️ This is the load-bearing constraint behind every budget in Part C, and the reason the shading model is toon rather than PBR.
* **Determinism is untouched.** Nothing in this document enters `Core`, the rules, or the replay hash. Rendering is a client concern and always was.

---

# PART B — PRODUCTION METHOD

## B0. Tools 🔓 — deliberately not locked

**No tool is locked. No tool is endorsed. Any tool may be replaced at any time without amending this document.**

That is the substantive change in D60, and it is a change of *kind*, not of vendor. The previous §B0 locked Midjourney and, in doing so, wrote a vendor into the design spec — which meant a tool change was a spec change. This section is instead defined by **stages and their declared outputs**. A tool belongs in the pipeline for exactly as long as it produces its stage's output to spec.

**The contract is the output, not the vendor.** A stage's output is defined in §B4 and §C. Any tool — current, future, commercial, open-source, or hand-modelling with no generator at all — satisfies a stage if its output meets that spec.

| Stage | Declared output | Tools in use today *(illustrative, not binding)* |
|---|---|---|
| Concept / style reference | 2D concept art per character, for the modeller and for silhouette approval | Any image generator; Midjourney is one option and is **no longer the delivery tool** |
| Base mesh generation | An untextured or roughly-textured mesh of correct silhouette and proportion | Meshy (text-to-3D, image-to-3D); Hunyuan3D and Rodin/Hyper3D via the Blender MCP; or modelled by hand |
| Authoring, retopology, UV, rigging, baking, LOD, export | Everything in §B4 steps 2–10, and the §C output contract | **Blender.** In practice the one fixed point — see the note below |
| Icon and marketing rendering | The PNG deliverables of asset kind **R** (§E0) | Blender's render pipeline, driven headlessly |

**On Blender specifically.** Blender is not locked either, but it is the *stable* element for a reason worth stating: it is free, scriptable, self-hosted, and produces a `.glb` that owes nothing to a vendor's continued goodwill — which is the same no-lock-in argument as `16` D12, applied to the art pipeline. A generator that vanishes costs a stage; an authoring tool that vanishes costs the whole back catalogue. Prefer keeping the *authoring* step under our own control and treating *generation* as replaceable.

⚠️ **Generators are the weakest link in output quality, not in licensing.** Licensing is `31`'s subject and is explicitly not a gate here (see the status banner). Quality is this document's subject: current text-to-3D output is good at silhouette and bad at topology, UVs, and anything that must deform. §B4 exists because of that, and **steps 2–10 are not optional however good the generator looks**.

## B1. Prompt scaffold for generated base meshes

Generation is one stage of ten, and its only job is a **correct silhouette at the correct proportions**. Do not prompt for detail the bake will replace or the budget will delete.

**Positive prompt template:**

```
{SUBJECT}, chibi cartoon fantasy game character, 2.5 heads tall proportions,
oversized head and hands, chunky simplified forms, smooth clean surfaces,
symmetrical A-pose, arms out and away from the body, legs apart,
neutral face, closed mouth, single connected watertight mesh,
low detail density, no fine ornament, no loose hanging parts,
game-ready stylised 3D character, front-facing, centered, full body
```

**Negative prompt (use on every generation):**

```
realistic, photorealistic, PBR, high detail, intricate ornament, fine filigree,
realistic proportions, adult body proportions, thin limbs, long neck,
T-pose, dynamic pose, action pose, crossed arms, hands touching body,
separate floating parts, disconnected geometry, holes, non-manifold,
text, letters, watermark, signature, logo, base, plinth, pedestal, ground plane,
scenery, background, multiple characters, cropped, cut off, extra limbs, deformed hands,
muted colours, desaturated, dark, low contrast
```

Three of those negatives are worth their own line, because they are the failures that cost the most downstream:

* **`T-pose` / `dynamic pose`.** An A-pose with clear space under the arms is the only pose that rigs and weights cleanly at chibi proportions. A T-pose pinches the shoulder; a dynamic pose is unusable.
* **`separate floating parts` / `non-manifold`.** The inverted-hull outline (§A3) renders *every* surface, including interior ones. A mesh with hidden internal geometry grows outlines inside itself, which is invisible in a grey viewport and glaring in the game.
* **`base` / `plinth` / `pedestal`.** Generators add them constantly, and they land exactly where the origin must be (§C1).

## B2. Consistency workflow — do this in order

Consistency across ~950 assets is the hard part, and 3D moves *where* it is won. In 2D it was won by a style reference on every generation. In 3D it is won by **shared assets**: one base mesh, one skeleton, one material, one light rig. A generator cannot drift what it is not allowed to author.

1. **Author the Style Anchor Set first, and treat it as a hard gate.** Not an image — four things: (a) 2D concept art for six characters (hero, grunt, brute, pet, mount, boss); (b) the **canonical base mesh** at locked chibi proportions; (c) the **shared humanoid skeleton** (§C4); (d) the **toon material and light rig** (§A3, §C6). Iterate until exactly right. Everything downstream is derived from these four and may not fork them.
2. **Model the hero from the base mesh.** The hero is the proportion reference every other humanoid is judged against. Approve it fully — silhouette, rig, deformation, all clips — before any second character exists.
3. **Generate base meshes per category batch**, not per asset. A category shares its generation settings; record them (§B5).
4. **Silhouette-gate every block-out** (§A4) *before* retopology. This is the cheapest rejection point in the pipeline and the only one that costs nothing to act on.
5. **Retopologise onto the shared topology** wherever the subject allows it. Humanoids share the base mesh's topology, which makes rigging, weighting and clip retargeting near-free. A bespoke topology per character is the single most expensive mistake available here.
6. **Bake and quantise per biome batch** so a biome is internally consistent (§B4 steps 5–6).
7. **Retarget the shared clip set** rather than authoring per-character animation (§C5). Author bespoke clips only for bosses.
8. **Style-drift check per batch**: render the new batch and three previously-approved assets from the same category through the same §C6 rig, side by side, at in-game size. Drift is far less likely than in 2D — and correspondingly easier to miss, because nobody is looking for it.

## B3. Per-category modelling notes

Replaces the 2D prompt-modifier table. These are modelling and budget directions, not prompt text; where a generator is used, fold the relevant phrase into `{SUBJECT}`.

| Category | Direction |
|---|---|
| Hero | The proportion reference. Full clip set. Gear attaches to sockets (§E2 note) — the body is modelled *assuming* gear will cover it, and unclothed regions still need clean albedo. |
| Gear | Modelled as **attachable meshes on the shared skeleton's sockets**, not as body variants. Must read at icon size *and* at actor size, because the same mesh produces both (§E11). |
| Enemies | Menacing but cute; exaggerated expression; a simple readable silhouette that survives the 64 px test. Reuse the humanoid base and skeleton wherever the body plan allows. |
| Elites | An enemy silhouette plus one **large** readable addition — scale, a horn cluster, a weapon. Never a busy detail pass. |
| Bosses | The showcase tier and the only category with a bespoke budget, bespoke topology and bespoke clips. Imposing scale, dramatic proportion, elaborate but chunky. |
| Pets | Adorable, round, bouncy. Quadruped or blob skeleton (§C4). Small on screen — silhouette is nearly all a player perceives. |
| Mounts | Rideable, sturdy, saddle geometry present and weighted. Must carry the hero mesh without intersection at every clip frame. |
| Board pieces & decor | Props, not characters. No skeleton, no clips. Highest count per biome, so the tightest per-asset budget. |
| Rendered icons (kind **R**) | Modelled once, rendered orthographically. Readability at 96–192 px is the whole spec; a detail invisible at that size is waste in both the mesh and the render. |
| Dice | A real die (§E16). Six faces, chamfered edges, face artwork in the albedo. The 2D "3D-look" fake is retired. |

## B4. The production pipeline (mandatory for every asset)

Replaces the 2D post-processing pipeline. **Steps 2–10 apply however good the generated input looks.**

```
 1. Base mesh          → generated or hand-modelled; silhouette-gated at block-out (§A4)
 2. Repair             → manifold, no interior faces, consistent normals, no degenerate tris
 3. Transform          → Y-up, metres, origin at feet, facing +Z, scale per §C1
 4. Retopology         → to the §C2 poly budget, onto the shared topology where possible
 5. UV unwrap          → single UV set, no overlap, texel density per §C3
 6. Bake + quantise    → high-to-low bake of albedo/AO/normal; albedo quantised to the biome palette
 7. Rig + weight       → to the shared skeleton (§C4); deformation checked at clip extremes
 8. Animate            → retarget the shared clip set; bespoke clips for bosses only (§C5)
 9. LOD chain          → generate and verify silhouette holds at every level (§C2)
10. Export             → .glb per §C1; validate; assign toon material + outline shell in Godot
```

Two properties of this pipeline are worth stating because they are the payoff for the whole medium change:

* **There is no background removal, no matte decontamination, no halo, no outline repair.** Those four steps consumed roughly 30 seconds of processing plus manual cleanup on ~10% of every 2D asset, and they were the previous §B0's stated key weakness. Alpha is now exact by construction. The seventeen null thresholds in `assets/pipeline/thresholds.json` were overwhelmingly calibrating *these* steps — see `16` D60's consequences.
* **The outline is a shader, not an asset.** In 2D, outline uniformity was a per-asset QA property that could drift 949 ways. It is now one shader, correct once.

## B5. What to record per asset

Independent of licensing (`31`), production needs to be able to reproduce an asset. Record, per asset: the generator and version if any, the prompt and seed, the base-mesh hash, the retopology target, the bake settings, the skeleton version, the clip set version, and the exporting Blender version.

⚠️ This is a **production** record and is not the same thing as a provenance record. `assets/provenance/` has exactly three record kinds — `midjourney`, `procedural`, `cc0` — and none of them fits a 3D asset. See `16` D60's consequences and `31` §4; that mismatch is **not** a blocker while output is unpublished, because unpublished output is never delivered into `assets/`.

---

# PART C — TECHNICAL OUTPUT SPEC

## C1. The export contract

| Property | Value |
|---|---|
| Delivery format | **glTF 2.0 binary (`.glb`)**, one file per asset, textures embedded |
| Up axis / handedness | **Y-up**, right-handed (glTF native; Blender's exporter converts from Z-up) |
| Units | **Metres.** 1 unit = 1 m |
| Origin | **At the feet**, centred in X and Z. Props: at the base contact point |
| Facing | **+Z** |
| Hero reference height | **1.4 m** — chibi proportions at roughly adult scale, so mounts and props share one world scale |
| Material | **One material per asset**, toon (§A3). Albedo + optional emissive. **No metallic, roughness, or normal map at v1** — the bake writes shading intent into the albedo |
| Texture format | PNG-32 source; **ETC2 (Android)** in engine, per `project.godot` |
| Colour space | sRGB albedo |
| Max single texture | **2048×2048** (unchanged from the 2D spec) |
| Naming | `snake_case`, prefixed by category — see §D1 |
| Validation | Every `.glb` passes a glTF validator with **zero errors** before it is accepted |

## C2. Geometry budgets ⚠️ PROVISIONAL

**None of these numbers has been measured on a device.** They are a starting point sized against Godot's Mobile renderer on a mid-range Android handset, and they exist so that a real measurement has something to contradict. See the status banner: do not hardcode them, and do not copy them into `thresholds.json`.

| Category | Tris (LOD0) | LOD chain | Skeleton |
|---|---|---|---|
| Hero (body) | 4,000 | LOD0/1/2 | humanoid |
| Gear piece (each) | 800 | LOD0/1 | humanoid sockets |
| Standard enemy | 2,500 | LOD0/1/2 | humanoid or quadruped |
| Elite | 4,000 | LOD0/1/2 | humanoid or quadruped |
| Boss | 12,000 | LOD0/1/2 | bespoke |
| Pet | 1,500 | LOD0/1 | quadruped or blob |
| Mount | 5,000 | LOD0/1/2 | quadruped |
| Board piece / decor | 400 | LOD0 only | none |
| Die | 300 | LOD0 only | none |
| Icon source model (kind **R**) | *unbudgeted* | n/a | n/a |

**Icon source models are deliberately unbudgeted.** They are rendered offline to PNG and never shipped, so a triangle costs render seconds and nothing else. This is the one place in the pipeline where detail is free — and the reason kind **R** exists at all (§E0).

⚠️ **The budget that actually matters is per-frame, not per-asset**, and it is unwritten: on-screen triangle total, draw calls, skinned mesh count, and texture memory for a worst-case battle frame. Those depend on how many actors `05`'s combat puts on screen at once, and on the outline shell **doubling every draw call it applies to**. This is the single largest open risk in this document — see §G.

## C3. Texture budgets ⚠️ PROVISIONAL

| Category | Albedo | Notes |
|---|---|---|
| Hero + gear set | 1024×1024 | one shared sheet for body and all equipped gear |
| Standard enemy | 512×512 | one per enemy |
| Elite | 512×512 | |
| Boss | 1024×1024 | the showcase tier |
| Pet / mount | 512×512 | |
| Board pieces & decor | 512×512 | **shared per biome**, not per asset |
| Die | 256×256 | all six faces on one sheet |

Texel density target: **~256 px/m** on actors, so a 512 sheet covers a 2 m² surface budget. Density must be *uniform within an asset* — a face at 512 and a boot at 64 reads as a texturing error even when neither is individually wrong.

## C4. Skeletons

Three skeletons, shared. A fourth is a design change, not an art decision.

| Skeleton | Used by | Bones ⚠️ PROVISIONAL |
|---|---|---|
| `skel_humanoid` | hero, humanoid enemies, elites, humanoid bosses | ≤ 32 |
| `skel_quadruped` | pets, mounts, beast enemies | ≤ 28 |
| `skel_blob` | slimes, orbs, amorphous enemies | ≤ 12 |

Rules: **no per-character skeleton** outside bosses. Gear attaches at named sockets on `skel_humanoid` and is never skinned to a bespoke rig. Bone count caps exist because skinned-mesh bone counts hit a real uniform limit on the Mobile renderer.

## C5. Animation clips

The shared clip set is retargeted, not re-authored (§B2 step 7).

| Clip | Actors | Notes |
|---|---|---|
| `idle` | all | loops; the default state |
| `attack` | hero, enemies, elites, bosses | one per attack type the actor has |
| `hurt` | all combat actors | non-looping |
| `death` | enemies, elites, bosses | non-looping |
| `victory` | hero | non-looping |
| `move` | mounts, pets | loops |

Bosses additionally get **bespoke ability clips**, one per ability in `17`. That is the only category permitted to author outside the shared set.

⚠️ The 2D spec's 4-pose character sheets (`idle`, `attack`, `hurt`, `victory` cut from one image) are retired. Real clips replace them, which is a straight quality gain and a schedule cost — animation is new work with no 2D equivalent. See §G.

## C6. The actor viewport 🔒

This is what makes §A3's lighting and perspective rules enforceable rather than aspirational, and it is the same rig for the client and for the offline icon renderer.

| Property | Value |
|---|---|
| Host 🔓 | **The screen's own `Node3D` world, rendered to the main viewport** (D61). A `SubViewport` is still the host for the **offline icon renderer**, which needs a fixed render size and a transparent background — see the 🔴 rulings below |
| Background | **Transparent** |
| Camera — actors | Perspective, **fixed 3/4 yaw, slight upward pitch** so the actor reads heroic. Identical for every actor |
| Camera — rendered icons | **Orthographic**, straight-on, no convergence |
| Light rig | Fixed: one key from **upper left**, one soft rim, no scene contribution, **no shadow casters** |
| Contact shadow | A separate engine-drawn decal, never lighting-derived |
| Render size ⚠️ PROVISIONAL | Sized to its 1080×1920-canvas footprint at 1×; the outline's 3–4 px width (§A3) is defined against *this* height, so it is fixed per viewport class, not per device |
| MSAA ⚠️ PROVISIONAL | 2× — the inverted-hull outline aliases badly without it, and this is the cheapest place to spend on perceived quality |

🔴 **D61 reopened three rulings in this table, and they are left open rather than guessed.** Per S6 a hole is written down absent and greppable, not filled with a plausible answer. Each needs a product decision before actor production starts:

1. **The outline's reference height.** §A3 fixes the inverted-hull outline at 3–4 px "at the actor viewport's render height", and the Render size row calls that height "fixed per viewport class, not per device". With actors in the screen's own world that reference is gone — the 3D now renders at the device's resolution, so a constant pixel width is no longer constant across handsets. Either the width becomes a function of viewport height, or actors keep a `SubViewport` of their own inside the 3D world, or the width stops being screen-space.
2. **Scene light versus the fixed rig.** The client currently has ONE `WorldEnvironment` and one app-wide key light on `AppRoot`, which is scene lighting — the thing the paragraph below calls non-negotiable to avoid. A per-actor fixed rig in a shared world needs either per-actor light culling, an unshaded toon material that ignores scene light, or actor `SubViewport`s.
3. **Backdrops.** §E9 and the biome sections say backdrops stay 2D parallax layers composited behind the actors. Behind a root-level 3D world they are either a skybox, a 3D backdrop plane, or a `CanvasLayer` *below* the 3D — which `canvas_items` does not give for free.

⚠️ Until these are ruled, the client's 3D basis is a **stage with no actors on it**: the conversion put every screen on `Node3D` with a camera and a backdrop plane, and no asset is loaded into any of them.

⚠️ **The light rig ignoring the scene is deliberate and non-negotiable** (§A3). A biome tints its backdrop. If biome light reached the key, 949 assets would each need to look right under eight lighting conditions, and the whole consistency argument in §B2 would collapse.

---

# PART D — CONVENTIONS

## D1. Naming convention

```
Models:      {category}_{subcategory}_{id}[_{variant}].glb
Textures:    {same stem}_albedo.png   |   _emissive.png
LODs:        carried inside the .glb as LOD levels, not as separate files
Clips:       carried inside the .glb, named exactly per §C5
Rendered 2D: {category}_{subcategory}_{id}[_{variant}].png     (kind R — unchanged from the 2D spec)

Examples:
  chr_hero_body.glb
  chr_hero_body_albedo.png
  chr_hero_weapon_blade_s.glb
  chr_enemy_frost_brute.glb
  chr_boss_rimehold.glb
  pet_stormfang.glb
  mnt_starhoof.glb
  board_frost_path_curve_l.glb
  die_body.glb
  gear_weapon_staff_ss.png          ← rendered icon, kind R
  tile_icon_treasure.png            ← rendered icon, kind R
  icon_perk_executioner.png         ← flat 2D, kind F
  ui_panel_main_9slice.png          ← flat 2D, kind F
  bg_frost_layer2.png               ← flat 2D, kind F
```

The category prefixes are unchanged from the 2D spec. **Only the extension tells you the kind**, which is intentional: `13`'s screens and `19`'s content tables reference assets by id, and those references stay valid across the medium change.

## D2. Grouping and packing

The 2D atlas scheme applies only to what is still 2D. 3D assets group by **material**, which is the thing that costs draw calls.

| Group | Contents | Mechanism |
|---|---|---|
| `mat_hero` | Hero body + every gear piece | one shared 1024 albedo (§C3) |
| `mat_biome_{n}` | That biome's board pieces and decor | one shared 512 albedo per biome |
| per-asset | Enemies, elites, bosses, pets, mounts | one material each; too distinct to share |
| `atlas_icons_gear` | The 120 **rendered** gear icons | 2D atlas, unchanged |
| `atlas_icons_perks` | Perk, talent and status icons | 2D atlas, unchanged |
| `atlas_ui` | Panels, buttons, frames, currency icons | 2D atlas, unchanged |

Battle backdrops are **not** atlased (full-screen, streamed per biome) — unchanged.

⚠️ **The outline shell doubles the draw call for every mesh it applies to.** Material grouping is therefore worth roughly twice what it would be otherwise, and it is the first lever to pull if §C2's unwritten per-frame budget turns out to be tight.

---

# PART E — THE ASSET MANIFEST

## E0. The three asset kinds

The medium change splits the manifest three ways. Every section in Part E carries a kind, and the kind decides which parts of this document apply to it.

| Kind | Meaning | Ships as | Governed by |
|---|---|---|---|
| **M** | **Model.** A real-time 3D asset rendered in the client. | `.glb` + textures | All of Parts A–D |
| **R** | **Rendered.** A 2D image rendered offline from a 3D model. The model is a production asset and is never shipped. | `.png` | Parts A, B, §C1/§C3/§C6, §D1 — **not** the §C2 geometry budgets |
| **F** | **Flat.** Authored 2D. No 3D involved at any stage. | `.png` | §A5 palettes, §D1 naming, §F where applicable |

**Why kind R exists.** A gear icon and an equipped gear mesh are the same object seen two ways. Modelling it once and rendering the icon from it makes the icon *automatically* consistent with the thing the player equips — which the 2D pipeline could only achieve by hand, and mostly didn't. It also makes icons re-renderable at any size forever, which retires a whole class of "regenerate at 2×" work.

**Why kind F survives.** 3D is worse than 2D at flat symbolic design. A perk emblem is a *sign*, not an object: it wants one bold readable symbol, and a lit three-dimensional rendering of a symbol is less legible than the symbol. Likewise 9-slice UI panels, whose corners must stay square under arbitrary stretch. Forcing these through 3D would cost quality to buy consistency that nobody perceives.

## E1. Summary table

| § | Category | Asset count | Kind |
|---|---|---|---|
| E2 | Hero & gear overlays | 64 | **M** |
| E3 | Enemies (standard) | 128 | **M** |
| E4 | Elites | 32 | **M** |
| E5 | Bosses | 32 | **M** |
| E6 | Pets | 46 | **M** |
| E7 | Mounts | 22 | **M** |
| E8 | Tile icons | 14 | **R** |
| E9 | Board paths & decor | 112 | 🔴 **undecided** — see below |
| E10 | Battle backdrops & scene backgrounds | 28 | **F** |
| E11 | Gear icons | 120 | **R** |
| E12 | Perk icons | 98 | **F** |
| E13 | Talent node icons | 40 | **F** |
| E14 | Status effect icons | 12 | **F** |
| E15 | Currency & resource icons | 9 | **R** |
| E16 | Dice faces | 6 | **M** |
| E17 | UI panels, buttons, frames | 89 | **F** |
| E18 | ~~Profile frames & cosmetics~~ | **0 — cut** | — |
| E19 | VFX sprite sheets | 32 | **F** |
| E20 | Misc UI icons | 50 | **F** |
| E21 | Store & marketing | 15 | **R** |
| | **TOTAL** | **949** | |

**By kind:** **M** 330 · **R** 158 · **F** 349 · undecided 112 · **total 949**.

🔒 **Cosmetics remain cut entirely** (decision D14). No die skins, no profile frames, no borders, no badges. Rank and Plus status are displayed as **text labels**.

⚠️ **The total is unchanged at 949.** D60 changes the medium of the assets, not which assets exist. The four rulings that moved 975 → 949 (−5 dice faces per `04` §3, −20 talent node icons per `16` D54, −2 pet and −2 mount assets per `16` D55, +3 perk-category card frames per `06` §2) all stand. 🔴 **E12's 98 perk icons are still not re-counted here**: the perk rework leaves 67 standard + 8 cursed, and reconciling that with 98 remains a content question this manifest cannot answer on its own. D60 does not touch it.

⚠️ **`game-data/assets/asset_manifest_art.json` is now stale.** Its `technical` block mirrors the *2D* §C, and it carries no `kind` field. It was **not** updated by D60 — that file is enumerated into the `ContentSnapshot` and therefore into `ContentHashing.Compute`, so editing it moves the content version stamp that `14` §6 makes load-bearing for replay and `CONTENT_VERSION_MISMATCH`. Re-authoring it is its own task with its own migration. See `16` D60's consequences.

### 🔴 E9's kind is undecided, deliberately

Board paths and decor are 112 assets — the largest single section after enemies — and whether they are **M** or **R** depends on something this document does not own: whether the board screen becomes a 3D scene or stays a 2D track with rendered pieces on it. That is a `03` (board) and `13` (screens) question. `TrackNode.tscn` is 2D today.

Per steering rule S6 the hole is left open and greppable rather than filled with a plausible answer. **Both readings are viable**, and they differ by roughly 45,000 triangles per board and by whether §C2's board budget matters at all. Do not begin E9 production until it is ruled.

---

## E2. Hero & gear overlays (64)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

⚠️ **"Overlays" is now a misnomer.** Gear was 2D sprite layers hand-aligned over a body sprite. Gear is now **attachable meshes on named sockets** of `skel_humanoid` (§B3, §C4), so alignment is a transform rather than an art problem — this retires the *Layered gear misalignment* risk outright (§G). The 64 assets and their descriptors below are unchanged; only how they attach is.

The hero is **layered**, not baked. A shared skeleton drives four layers: body, armor, helmet, weapon.

| Asset | Count | ID pattern | Subject descriptor for the prompt |
|---|---|---|---|
| Hero body poses | 4 | `chr_hero_body_{idle\|attack\|hurt\|victory}` | *A young chibi adventurer in a simple grey tunic and boots, hood down, determined expression, brown hair* |
| Weapon overlays | 20 | `chr_hero_weapon_{blade\|axe\|staff\|bow}_{c\|b\|a\|s\|ss}` | see below |
| Helmet overlays | 20 | `chr_hero_helmet_{hood\|helm\|circlet\|mask}_{c\|b\|a\|s\|ss}` | see below |
| Armor overlays | 20 | `chr_hero_armor_{leathers\|plate\|robe\|scalemail}_{c\|b\|a\|s\|ss}` | see below |

### Rarity visual escalation (applies to all three overlay types)

| Rarity | Visual treatment |
|---|---|
| **C** | Plain, worn, grey-brown, no ornament |
| **B** | Clean, green leather / bronze trim, a small emblem |
| **A** | Blue steel with silver filigree, a glowing blue gem |
| **S** | Gold and deep orange, ornate engraving, glowing runes, small floating particles |
| **SS** | Violet and black with a magenta glow, dramatic silhouette additions (spikes, trailing cloth, floating shards), an aura |

### Family descriptors

| Family | Descriptor |
|---|---|
| Blade | *a short curved sword with a wide guard* |
| Axe | *a heavy single-bit battleaxe with a chunky head* |
| Staff | *a gnarled wooden staff topped with a floating crystal* |
| Bow | *a compact recurve bow with a taut string* |
| Hood | *a soft cloth hood with a wide brim* |
| Helm | *a full-face plate helm with a T-slit visor* |
| Circlet | *a thin metal circlet with a central gem* |
| Mask | *a carved animal-face mask covering the lower face* |
| Leathers | *a fitted studded leather jerkin* |
| Plate | *bulky rounded shoulder-plate armor* |
| Robe | *a long flowing hooded robe with wide sleeves* |
| Scalemail | *layered scale armor with a short cape* |

> ⚠️ **NEEDS DETAIL:** Layered paper-doll rigging requires the AI-generated overlays to align to a common skeleton, which image models do not guarantee. **Mitigation (must be planned for):** generate each overlay *on a faint ghosted copy of the hero body* so alignment is inherited, then remove the body in post. Budget manual alignment time for all 60 overlays. If this proves unreliable, fall back to 20 fully-composited hero sprites (one per slot-family-rarity look) and accept less mix-and-match.

---

## E3. Standard enemies (128)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

**8 archetypes × 8 biomes = 64 creatures × 2 poses (idle, attack) = 128 assets.**

Archetype descriptors (combine with the biome descriptor below):

| Archetype | Base descriptor |
|---|---|
| `GRUNT` | *a small chibi humanoid warrior with a crude weapon, angry squinting eyes* |
| `SWARM` | *a tiny round scuttling creature, one of a swarm, oversized eyes and tiny legs* |
| `BRUTE` | *a huge round-bellied brute with tiny legs and enormous fists* |
| `SKIRMISHER` | *a lean crouching creature with twin daggers, mid-lunge, ragged scarf* |
| `WARDEN` | *a squat armored guardian behind an oversized tower shield* |
| `CASTER` | *a hooded caster with a floating orb and glowing hands* |
| `LEECH` | *a bloated leech-like creature with a round sucking mouth* |
| `REAVER` | *a bladed predator with long curved claws and a fanged grin* |

Biome reskin descriptors:

| Biome | Reskin |
|---|---|
| Greenwood | *made of bark, moss and leaves, mushroom cap details* |
| Ashen Mire | *dripping with purple bog slime, tar-stained, swamp reeds* |
| Sunken Crypt | *skeletal, bone armor, teal ghost-flame eye sockets* |
| Emberpeak | *cracked obsidian skin with glowing magma veins* |
| Frostbound | *encased in pale blue ice shards, frost breath* |
| Clockwork | *brass and copper automaton with exposed gears and steam vents* |
| Bloom of Decay | *fungal growths, bioluminescent pink spores, rotting bark* |
| Astral Spire | *starfield-textured body, floating geometric shards, violet glow* |

ID pattern: `chr_enemy_{biome}_{archetype}_{idle|attack}`

---

## E4. Elites (32)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

**16 elites × 2 poses.** Two per biome. Elites are visually larger (640 px canvas), carry a coloured aura matching their modifier, and have one exaggerated distinguishing feature.

| # | ID | Biome | Descriptor |
|---|---|---|---|
| 1 | `EL_THORN_SENTINEL` | Greenwood | *a towering animated bramble knight with rose-thorn armor* |
| 2 | `EL_MOSSBACK_ALPHA` | Greenwood | *a giant moss-covered boar with glowing green tusks* |
| 3 | `EL_BOGFATHER` | Ashen Mire | *a bloated swamp shaman with a bone staff and hanging vines* |
| 4 | `EL_MIRESTALKER` | Ashen Mire | *a long-limbed marsh predator with a lantern lure* |
| 5 | `EL_BONE_CHOIR` | Sunken Crypt | *three skulls stacked into one floating singing entity* |
| 6 | `EL_GRAVE_TITAN` | Sunken Crypt | *a hulking skeleton wrapped in chains and burial cloth* |
| 7 | `EL_MAGMA_HERALD` | Emberpeak | *a lava-cored knight with a molten greatsword* |
| 8 | `EL_ASHWING` | Emberpeak | *a small fire drake with tattered burning wings* |
| 9 | `EL_RIMEFANG_WARDEN` | Frostbound | *an ice-armored guardian with a frozen tower shield* |
| 10 | `EL_GLACIER_MAW` | Frostbound | *a shaggy white beast with an oversized frozen jaw* |
| 11 | `EL_COGWRIGHT` | Clockwork | *a brass engineer automaton with six spider-like tool arms* |
| 12 | `EL_STEAMBREAKER` | Clockwork | *a piston-armed copper bruiser venting steam* |
| 13 | `EL_SPORELORD` | Bloom of Decay | *a mushroom-capped giant leaking glowing pink spores* |
| 14 | `EL_ROTVINE` | Bloom of Decay | *a tangled mass of decaying vines with a single eye* |
| 15 | `EL_STARSCRIBE` | Astral Spire | *a floating masked scholar surrounded by orbiting glyph tablets* |
| 16 | `EL_VOIDCALF` | Astral Spire | *a small cosmic beast whose body is a hole full of stars* |

🔒 Combat identity — each elite's base archetype and stat derivation — is authored in `05` §6.2 (ruled in `16` A7). This section owns only the art.

---

## E5. Bosses (32)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

The only category with a bespoke budget, bespoke topology and bespoke animation clips (§C2, §C5).

**8 bosses × 4 assets each** (idle, attack, phase-2 variant, phase-3 enraged variant). 1024×1024, the highest-quality assets in the game.

| Chapter | ID | Name | Descriptor |
|---|---|---|---|
| 1 | `BOSS_THORNMAW` | Thornmaw | *a colossal carnivorous flower with a fanged maw, thick thorned vines for arms, glowing yellow pollen, rooted in mossy stone* |
| 2 | `BOSS_GULGROT` | Gulgrot | *an enormously bloated toad shaman wearing a bone crown, purple bog slime dripping, warts glowing sickly green* |
| 3 | `BOSS_OSSUARY_KING` | Ossuary King | *a crowned skeleton king on a throne of stacked skulls, tattered teal robes, floating rib-cage shield* |
| 4 | `BOSS_CINDERMAW` | Cindermaw | *a chunky magma drake with obsidian plating, molten cracks, small wings, breathing fire* |
| 5 | `BOSS_RIMEHOLD` | Rimehold | *a massive ice golem with a glacier-slab body, glowing pale-blue core, jagged shoulder spires* |
| 6 | `BOSS_COGITATOR` | Cogitator Prime | *a brass spider automaton with eight articulated legs and a single glass eye lens, steam venting* |
| 7 | `BOSS_SPOREQUEEN` | Sporequeen Vell | *a regal fungal queen with a giant glowing mushroom crown, flowing spore-cloud gown, four slender arms* |
| 8 | `BOSS_DICELORD` | The Dicelord | *a tall masked cosmic figure in a starfield cloak, holding a giant floating golden die, six glowing dice orbiting, violet aura* |

Phase variants: phase 2 adds a visible damage/transformation cue; phase 3 adds an aggressive silhouette change plus a strong glow in the biome accent colour.

---

## E6. Pets (46)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

**23 pets × 2 assets** (idle, ability-cast) — `PET_DICEBEAST` is removed (`16` D55). 256×256. All pets are round, bouncy and unambiguously cute — they are the collection reward and must be desirable at thumbnail size.

| ID | Name | Rarity | Descriptor |
|---|---|---|---|
| `PET_SPARKLING` | Sparkling | B | *a tiny round yellow lightning sprite with two stubby arms and a zigzag tail* |
| `PET_MOSSLING` | Mossling | B | *a small round moss ball with leaf ears and sleepy eyes* |
| `PET_PEBBLE` | Pebble | B | *a chubby grey rock creature with mismatched eyes and stubby stone feet* |
| `PET_WISP` | Wisp | B | *a floating pale-blue flame with a small smiling face and a wispy tail* |
| `PET_NIPPER` | Nipper | B | *a small red crab-like creature with one oversized snapping claw* |
| `PET_SNAILGUARD` | Snailguard | B | *a round snail with a shield-shaped shell and a brave little face* |
| `PET_EMBERCUB` | Embercub | A | *a chubby orange fox cub with flame-tipped ears and tail* |
| `PET_FROSTKIT` | Frostkit | A | *a fluffy white kitten with ice-crystal whiskers and blue paws* |
| `PET_THORNBUD` | Thornbud | A | *a round green bud creature covered in short thorns with a flower crown* |
| `PET_GILDBEAK` | Gildbeak | A | *a plump golden bird with a coin-shaped beak and a tiny crown* |
| `PET_SHADEPAW` | Shadepaw | A | *a small dark-purple cat made of smoke with glowing violet eyes* |
| `PET_LEECHLING` | Leechling | A | *a round crimson leech creature with a cute round sucker mouth* |
| `PET_RUNEMOTH` | Runemoth | A | *a fuzzy pastel moth with glowing rune patterns on its wings* |
| `PET_TOADKING` | Toadking | A | *a fat green toad wearing a tiny lopsided golden crown* |
| `PET_STORMFANG` | Stormfang | S | *a small blue wolf cub wreathed in crackling lightning, storm cloud beneath* |
| `PET_AEGISOWL` | Aegis Owl | S | *a round white owl in miniature golden armor with a shield-shaped chest plate* |
| `PET_VOIDKITTEN` | Void Kitten | S | *a black kitten whose body is a window into a purple starfield, white star eyes* |
| `PET_GOLDWYRM` | Goldwyrm | S | *a tiny chubby golden dragon curled around a pile of coins* |
| `PET_SPOREMOTHER` | Sporemother | S | *a round pink fungal creature with a mushroom cap, glowing spores drifting off* |
| `PET_CLOCKHOUND` | Clockhound | S | *a brass mechanical puppy with a clock face on its chest and gear ears* |
| `PET_SOLARION` | Solarion | SS | *a radiant miniature sun lion with a mane of golden flame and white-hot eyes* |
| `PET_NYXWEAVER` | Nyxweaver | SS | *a small elegant spider of woven night sky with silver constellation markings* |
| `PET_ARCHIVIST` | The Archivist | SS | *a floating hooded book-creature with glowing pages for a face and quill-tipped arms* |

---

## E7. Mounts (22)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

⚠️ Mounts must carry the hero mesh with no intersection at every frame of every clip (§F).

**11 mounts × 2 assets** (idle, moving) — `MNT_VOIDSTEED` is removed (`16` D55). 512×384, side-profile 3/4 view with a visible saddle sized for a chibi rider.

| ID | Name | Rarity | Descriptor |
|---|---|---|---|
| `MNT_SADDLEBOAR` | Saddleboar | A | *a stocky brown boar with a leather saddle and tusk guards* |
| `MNT_DUSTRUNNER` | Dustrunner | A | *a lean tan desert ostrich-like bird with long legs and a feathered crest* |
| `MNT_PACKMULE` | Pack Mule | A | *a patient grey mule loaded with bulging coin sacks and bedrolls* |
| `MNT_GLIDEWING` | Glidewing | A | *a small green feathered glider lizard with membrane wings* |
| `MNT_STARHOOF` | Starhoof Stag | S | *a white stag with antlers of glowing golden stars and constellation fur markings* |
| `MNT_IRONSHELL` | Ironshell Tortoise | S | *a massive armored tortoise with an iron-plated shell and a howdah saddle* |
| `MNT_CINDERMANE` | Cindermane | S | *a black horse with a mane and tail of orange flame, molten hoof prints* |
| `MNT_TIDECALLER` | Tidecaller | S | *a teal sea serpent that glides above the ground with flowing water fins* |
| `MNT_COINWYRM` | Coinwyrm | S | *a golden serpentine dragon whose scales are stacked coins* |
| `MNT_FATESPINNER` | Fatespinner | SS | *a large elegant spider-like creature with a body of golden dice and silk threads of light* |
| `MNT_WORLDBEARER` | Worldbearer | SS | *an enormous stone tortoise-elephant carrying a tiny floating island with a tree on its back* |

---

## E8. Tile icons (14)

**Kind: R** — 2D PNG rendered offline from a 3D model (§E0). The source model is a production asset and never ships; the §C2 geometry budgets do not apply to it.

192×192, flat straight-on, thick outline, no perspective, engine-tinted per biome.

| ID | Descriptor |
|---|---|
| `tile_icon_enemy` | *two crossed swords* |
| `tile_icon_elite` | *a skull with two crossed swords behind it and a small crown* |
| `tile_icon_boss` | *a five-pointed star with a horned crown* |
| `tile_icon_shrine` | *a small glowing stone altar with a floating light orb* |
| `tile_icon_curse` | *a cracked purple skull leaking dark smoke* |
| `tile_icon_treasure` | *a closed wooden treasure chest with a golden lock* |
| `tile_icon_shop` | *a striped market awning over a small counter* |
| `tile_icon_campfire` | *a small crackling campfire with two logs* |
| `tile_icon_minigame` | *a target board with a dart in the bullseye* |
| `tile_icon_event` | *a large glowing question mark on a scroll* |
| `tile_icon_portal` | *a swirling blue-violet vortex ring* |
| `tile_icon_cache` | *a paw print on a small wooden crate* |
| `tile_icon_dice_forge` | *a golden die on a tiny anvil* |
| `tile_icon_empty` | *a plain round flagstone with a small carved dot* |

---

## E9. Board paths & decor (112)

🔴 **Kind: undecided — M or R.** This is the one open hole in the manifest and it is deliberate. Whether board pieces are real-time 3D or rendered 2D depends on whether the board screen becomes a 3D scene or stays a 2D track (`TrackNode.tscn` is 2D today) — a `03`/`13` question this document does not own. The two readings differ by roughly 45,000 triangles per board. Per steering rule S6 the hole stays open and greppable rather than filled with a plausible answer. **Do not begin E9 production until it is ruled.** See §E1.

Per biome: **6 path pieces + 8 decor props = 14 × 8 biomes = 112.**

| Path piece | ID suffix | Descriptor |
|---|---|---|
| Straight | `path_straight` | *a short straight cobbled path segment, top-down 2.5D, seamless ends* |
| Curve left | `path_curve_l` | *a cobbled path curving left, seamless ends* |
| Curve right | `path_curve_r` | *a cobbled path curving right, seamless ends* |
| Fork | `path_fork` | *a cobbled path splitting into two branches* |
| Junction | `path_join` | *two cobbled paths merging into one* |
| Boss approach | `path_boss` | *a wide ornate stone platform approach with braziers* |

Decor props (8 per biome, biome-flavoured): *a tree/pillar, a rock/ruin, a small plant cluster, a hanging or floating element, a broken structure, a light source, a ground detail patch, a large silhouette prop for the background.*

ID pattern: `board_{biome}_{piece}`

---

## E10. Battle backdrops & scene backgrounds (28)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

Backdrops remain 2D parallax layers, composited behind the actors in the §C6 viewport. A biome tints its backdrop and never touches the fixed key light (§A3).

| Asset | Count | Spec |
|---|---|---|
| Biome battle backdrops | 24 | 8 biomes × 3 parallax layers (far / mid / near), 1080×1440, seamless horizontal tiling |
| Home / Camp background | 1 | *a cosy nighttime camp clearing with a tent, campfire, weapon rack, and a starry sky* |
| Arena background | 1 | *a floating stone duelling platform above clouds with banners and torches* |
| Forge background | 1 | *a warm dwarven-style workshop interior with an anvil, glowing forge and hanging tools* |
| Menagerie background | 1 | *a bright stable-garden with hay bales, feeding bowls and small perches* |

---

## E11. Gear icons (120)

**Kind: R** — 2D PNG rendered offline from a 3D model (§E0). The source model is a production asset and never ships; the §C2 geometry budgets do not apply to it.

**The payoff case for kind R.** A gear icon and an equipped gear mesh are the same object seen two ways, so the icon is rendered from the mesh authored in §E2 and is *automatically* consistent with what the player equips — which the 2D pipeline could only achieve by hand, and mostly didn't. Nearly free once the render rig exists (§H step 8).

**24 base items × 5 rarities.** 192×192, 3/4 view, glossy, on transparent.

Base items = the 6 slots × 4 families listed in `08_GEAR_AND_MERGING.md` §1. Rarity escalation follows the table in §E2. The engine draws the rarity **frame** separately — the icon itself carries only the material/ornament escalation.

ID pattern: `gear_{slot}_{family}_{rarity}`

---

## E12. Perk icons (98)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

A perk emblem is a **sign, not an object**: one bold readable symbol. A lit three-dimensional rendering of a symbol is less legible than the symbol. `22`'s per-icon prompt tables remain valid and unamended.

**90 standard perks + 8 cursed perks.** 128×128, circular emblem, single bold symbol, category-coloured background disc.

Prompt template:
```
{SYMBOL}, circular game ability icon, single bold centered symbol,
{CATEGORY_COLOUR} radial background disc, thick dark outline, minimal detail,
high contrast, glossy magical emblem, chibi cartoon fantasy game art style
```

| Category | Disc colour | Symbol language |
|---|---|---|
| Offense | red `#D9453C` | blades, fangs, impacts, fire |
| Defense | blue `#3B82F6` | shields, walls, plates, barriers |
| Sustain | green `#4CAF50` | hearts, droplets, leaves, chalices |
| Dice & Board | gold `#F5A623` | dice, arrows, maps, compasses |
| Economy | purple `#8B5CF6` | coins, gems, chests, scales |
| Trigger / Synergy | orange `#F97316` | interlocking rings, chains, sparks |
| Cursed | black-violet `#4C1D6B` | cracked skulls, chains, inverted symbols |

> ✅ **All 98 symbol descriptors are authored in `22_ICON_PROMPT_TABLES.md` Part A.** Generate the symbol only; the circular frame and the category disc colour are applied in post so they are exact.

---

## E13. Talent node icons (40)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

`22`'s per-icon prompt tables remain valid and unamended.

**2 branches × 20 nodes** — the FORTUNE branch is removed (`16` D54). 128×128, same emblem language as perks but with a **hexagonal** frame instead of circular, so talents and perks are never confused.

| Branch | Disc colour | Symbol language |
|---|---|---|
| MIGHT | red `#D9453C` | weapons, fists, flames |
| WARD | blue `#3B82F6` | shields, armour, roots |

Keystones (6 of the 40) get a larger **star-shaped** frame and an animated glow overlay.

> ⚠️ **`22_ICON_PROMPT_TABLES.md` Part B authored 60 symbol descriptors**; the 20 FORTUNE rows retire with the branch, leaving **40**. The gold disc colour retires with them and is not reassigned.

---

## E14. Status effect icons (12)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

128×128, small, extremely readable at 32 px. `BURN`, `POISON`, `BLEED`, `FREEZE`, `STUN`, `WEAKEN`, `SUNDER`, `SPORE`, `RAGE`, `WARD`, `HASTE`, `REGEN`.

Descriptors: *a flame · a green skull bubble · a red droplet · a snowflake · orbiting stars · a downward broken arrow · a cracked shield · a spore cloud · a red upward arrow with fangs · a blue bubble shield · a winged boot · a green cross with leaves.*

---

## E15. Currency & resource icons (9)

**Kind: R** — 2D PNG rendered offline from a 3D model (§E0). The source model is a production asset and never ships; the §C2 geometry budgets do not apply to it.

96×96, glossy, instantly distinguishable by **shape**, not just colour.

| ID | Descriptor |
|---|---|
| `icon_cur_gold` | *a stack of three round gold coins* |
| `icon_cur_crown` | *a small jewelled golden crown* |
| `icon_cur_soulshard` | *a glowing violet crystal shard* |
| `icon_cur_energy` | *a yellow lightning bolt in a rounded droplet* |
| `icon_cur_enhance_stone` | *a blue-white polished rune stone* |
| `icon_cur_merge_dust` | *a small pouch spilling silver sparkling dust* |
| `icon_cur_beast_feed` | *a bone-shaped biscuit on a bundle of golden hay* |
| `icon_cur_honor` | *a crossed-swords medal with a red ribbon* |
| `icon_cur_talent_point` | *a glowing green six-pointed spark* |

---

## E16. Dice faces (6)

**Kind: M** — real-time 3D model, shipped as `.glb` (§E0). All of Parts A–D apply.

⚠️ **The die becomes a real die.** `04` §3 specifies "a chunky 3D-look 2D sprite rendered with a squash-and-stretch tumble". Under D60 the 3D-look fake is retired and the die is genuine geometry with the six face artworks in its albedo (§B3, §C3). The tumble becomes a real 3D animation. **This amends `04` §3** — see `16` D60's consequences.

**One die design only.** 🔒 Skins were cut with the rest of the cosmetics (D14).

**6** face artworks at 256×256, composited onto a 3D-look die body: `pip1`–`pip6`. The five special-face artworks retire with the die's face kinds (`04` §3).

| Face | ID | Descriptor |
|---|---|---|
| Pips 1–6 | `die_face_pip1` … `pip6` | *a cream bone die face with dark carved pips* |
| Star | `die_face_star` | *a golden five-pointed star with rays* |
| Surge | `die_face_surge` | *a cyan lightning bolt* |
| Fortune | `die_face_fortune` | *a green four-leaf clover over a coin* |
| Void | `die_face_void` | *a black hole with a violet rim* |
| Chain | `die_face_chain` | *three orange interlocking links* |

The die **body** is a single asset reused for every face, so an upgraded face reads instantly as a change to the same familiar object — which is arguably better for the dice-as-equipment fantasy than a full skin swap would have been.

---

## E17. UI panels, buttons & frames (86)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

9-slice corners must stay square under arbitrary stretch, which is exactly what 3D cannot promise. Stays flat 2D.

| Group | Count | Notes |
|---|---|---|
| 9-slice panels | 12 | main, dark, light, parchment, wood, stone, glass, tooltip, modal, banner, tab-active, tab-inactive |
| Buttons | 18 | primary/secondary/danger/ghost × normal/pressed/disabled, plus the large ROLL button (3 states), plus the ad button (3 states) |
| Item rarity frames | 10 | 5 rarities × (square item slot, round portrait) |
| Progress bars | 9 | HP, Energy, Legend XP — fill + track + cap for each |
| Tab bar & nav icons | 12 | Home, Hero, Forge, Talents, Menagerie, Arena, Shop, Codex, Settings, Back, Close, Info |
| Card backs & draft cards | 11 | **9** perk-category card frames (`06` §2) + owned-upgrade gold frame + the dashed ad-slot card |
| Decorative dividers, ribbons, banners | 10 | |
| Toast / notification chrome | 4 | |
| Loading elements | 3 | spinner die, progress track, tip card frame |

All panels must have **square, non-tapering corners** so 9-slice stretching does not distort ornament.

---

## E18. Profile frames & cosmetics — **CUT (0 assets)**

🔒 Decision D14 removed all cosmetic rewards from v1. PvP rank, Codex completion and Slay Plus are all shown as **coloured text labels**, rendered by the engine from the localisation strings. No art is required.

⚠️ **Flagged risk:** this leaves the game with no visual trophies at all, which weakens long-term ladder and mastery retention (see `11` §5.3 and `16` R3). If reinstated later, die skins are the cheapest and most thematic re-entry point — roughly 11 assets per skin, and no other system needs to change.

---

## E19. VFX sprite sheets (32)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

VFX became procedural in-engine work under the O8 ruling and stays so. Real-time 3D additionally makes `GPUParticles3D` available inside the actor viewport — a capability gain on top of the existing plan, not a change to it.

1024×1024, 4×4 grid of 256 px frames, additive-blend friendly, transparent.

`hit_normal`, `hit_crit`, `hit_block`, `miss_puff`, `heal_burst`, `shield_form`, `shield_break`, `burn_loop`, `poison_loop`, `bleed_loop`, `freeze_apply`, `stun_stars_loop`, `rage_aura_loop`, `regen_loop`, `levelup_burst`, `merge_success`, `enhance_success`, `enhance_fail`, `treasure_burst`, `coin_pickup`, `gem_pickup`, `die_land_dust`, `die_star_flare`, `die_surge_spark`, `die_fortune_sparkle`, `portal_swirl_loop`, `boss_phase_shockwave`, `victory_confetti`, `defeat_fade`, `perk_select_flash`, `pet_ability_generic`, `mount_dash_trail`.

---

## E20. Misc UI icons (50)

**Kind: F** — flat authored 2D, no 3D at any stage (§E0). 3D is worse than 2D at this job; see §E0.

Counted individually (50): sort · filter · lock · unlock · salvage · merge · enhance · equip · unequip · compare · star filled · star empty · plus · minus · check · cross · arrow up · arrow down · arrow left · arrow right · speed ×1 · speed ×2 · speed ×3 · skip · pause · sound on · sound off · music on · music off · haptics · language · account · privacy · help · bug report · share · calendar · clock · quest scroll · gift · wheel · leaderboard · medal · chest closed · chest open · key · timer · warning · info.

---

## E21. Store & marketing (15)

**Kind: R** — 2D PNG rendered offline from a 3D model (§E0). The source model is a production asset and never ships; the §C2 geometry budgets do not apply to it.

Rendered from the finished models (§H step 14). Key art and the wordmark may be authored flat where a render would not serve them.

| Asset | Spec |
|---|---|
| App icon | 1024×1024 — *the hero's face beside a large glowing golden die, bold, readable at 48 px*. **No text in the icon** (§A3) — the title renders beside it in the store. Test at 48 px against the icons of *Legend of Slime* and *Archer Forest* on a real home screen before accepting. |
| Feature graphic (Google Play) | 1024×500 |
| Screenshots | 8 (portrait 1080×1920) — board, battle, perk draft, forge merge, talent tree, menagerie, arena, run results |
| Promo art / key art | 1 wide — *the hero riding Starhoof Stag along a floating board path toward a giant die, all 8 biomes visible in the distance* |
| **Wordmark** | 1 — the title **Slay. Idle. Repeat.** set in Baloo 2 Heavy, three stacked lines, each full stop rendered as a small die pip. Delivered as SVG plus PNG at 512/1024/2048. **Engine-set type, not AI-generated** — image models cannot render text reliably (§A3). |
| Slay Plus store banner | 1 |
| Season promo template | 1 |
| Store listing icons | 1 set |

---

# PART F — QUALITY ASSURANCE CHECKLIST

Before an asset batch is accepted. Items are tagged by the kind (§E0) they apply to.

**Silhouette and readability** — *M, R*

- [ ] Silhouette test passed at 64 px from the §C6 camera, **on the block-out** (§A4)
- [ ] Silhouette still reads at every LOD level, including the last
- [ ] Readable at the smallest in-game display size
- [ ] Proportions match the Style Anchor Set (2.5–3 heads)

**Style** — *M, R, F*

- [ ] Outline continuous and uniform width, colour `#231A2E`, no interior outlines from hidden geometry
- [ ] Two-band cel ramp only — no PBR response, no smooth falloff, no shader AO
- [ ] Key light from upper left via the fixed §C6 rig; no scene light contribution
- [ ] Palette conforms to the biome's locked six colours + neutrals
- [ ] No text, watermark or signature in any mesh, texture or UV layout

**Geometry and topology** — *M*

- [ ] Manifold, watertight, consistent normals, **no interior faces**
- [ ] Within the §C2 triangle budget at LOD0, and LOD chain present per §C2
- [ ] Y-up, metres, origin at the feet, facing +Z, scale per §C1
- [ ] Retopologised onto the shared topology where the subject allowed it
- [ ] No plinth, base or ground plane geometry

**Texturing** — *M, R*

- [ ] Single non-overlapping UV set; texel density uniform within the asset (§C3)
- [ ] Within the §C3 texture budget; shares the group sheet where §D2 says it should
- [ ] Albedo quantised to the biome palette; AO baked where wanted and nowhere else

**Rig and animation** — *M, actors only*

- [ ] Bound to the correct shared skeleton (§C4); bone count within cap
- [ ] Deformation checked at the extremes of every clip — no pinching, no collapse
- [ ] Full shared clip set present and correctly named (§C5)
- [ ] Mounts: carry the hero mesh with no intersection at any clip frame

**Delivery** — *all*

- [ ] `.glb` passes a glTF validator with zero errors (*M, and R's source model*)
- [ ] Correct canvas size and pivot per the delivery table (*R, F*)
- [ ] Named per §D1 and grouped per §D2
- [ ] Production record written per §B5
- [ ] Side-by-side against 3 previously-approved assets in the same category through the same §C6 rig shows no style drift

⚠️ **This checklist has no performance item, and that is a gap, not a decision.** §C2's per-frame budget is unwritten, so there is nothing to check against. Until it exists, a batch can pass every item here and still be unshippable. See §G.

⚠️ **The mechanical QA layer implements the *old* Part F and is currently red.** `tools/AssetPipeline` carries nine `IQaCheck` implementations reconciled against Part F's previous eleven lines character for character (`Qa/Doc15PartF.cs`), so this re-authoring turns `QaChecklistTests` red — deliberately, per that file's 🔒 comment. Those checks measure **pixels** (alpha halo, outline conformance, palette quantisation, watermark, atlas packing); a 3D QA layer measures **meshes and glTF**. Re-authoring it is its own task — see `16` D60 consequences 5 and 5b, the second of which records a divergence the tripwire does **not** catch.

---

# PART G — KNOWN RISKS

Reordered by severity for the 3D pipeline. Four risks are new, three are inherited unchanged, and two are **retired by the medium change**.

| Risk | Severity | Mitigation |
|---|---|---|
| **Mobile performance is unmeasured, and the outline doubles draw calls** 🆕 | **Critical** | The largest risk in this document. Godot's Mobile renderer, a mid-range Android handset, skinned meshes, and an inverted-hull shell that **doubles the draw call of every mesh it touches** — with no per-frame budget written and no device measurement taken. **Mitigation: a device spike before any batch production**, measuring a worst-case `05` battle frame. Everything in §C2/§C3/§C6 marked ⚠️ PROVISIONAL is waiting on it. If it fails, the fallback is kind **R** for actors too — pre-rendered sprites off the same models, which is a delivery change and not a re-authoring |
| **Animation is new work with no 2D equivalent** 🆕 | **High** | The 2D spec cut four poses from one image. Real clips (§C5) are a straight quality gain and an unbudgeted schedule cost across ~330 **M** assets. **Mitigation: the shared-skeleton and retarget strategy (§B2, §C4) is the whole answer** — it is why per-character skeletons are banned outside bosses. If retargeting quality proves insufficient, cost scales with the number of bespoke rigs, so hold that line hard |
| **Generated meshes have unusable topology, UVs and weights** 🆕 | **High** | Current text-to-3D is good at silhouette, bad at everything that must deform. **Mitigation: §B4 steps 2–10 are mandatory regardless of how good the generator output looks**, and the silhouette gate at step 4 rejects before any of that cost is incurred. Treat generation as a blocking-out tool, never as a delivery tool |
| **E9's dimensionality is unruled** 🔴 | **High** | 112 assets — the second-largest section — cannot start. Owned by `03`/`13`, not by this document. See §E1 |
| **Style drift across ~950 assets** | Medium *(was High)* | **Substantially reduced by the medium change**: shared base mesh, shared skeleton, one material, one light rig (§B2). A generator cannot drift what it is not allowed to author. Residual risk moves to *modelling* drift, caught by the §B2 step 8 batch comparison — which is now easier to miss precisely because drift is rarer |
| **Texture memory across 8 biomes** 🆕 | Medium | Per-asset materials on enemies, elites and bosses (§D2) are the bulk of it, and 2048 is permitted. **Mitigation: biome-shared sheets for board and decor; the §D2 grouping is the lever.** Unquantified until the same device spike |
| **98 perk icons looking interchangeable** | Medium | Unchanged — kind **F**, so 3D neither helps nor hurts. Author the per-icon symbol CSV first (E12, `22`) and enforce distinct silhouettes |
| **9-slice panels with warped corners** | Medium | Unchanged — kind **F**. Generate flat, wide panels and cut corners manually; verify by stretching to 3× |
| **Seamless tiling for parallax backgrounds** | Medium | Unchanged — kind **F**. Generate wider than needed and blend the seam manually |
| **Rarity escalation not reading clearly** | Low | Unchanged. Test all 5 rarities of one item side by side before generating the other 23 — now easier, since rarity can be material and emissive rather than a re-model |
| ~~**Layered gear misalignment**~~ | **Retired** | **Retired by the medium change.** Gear was 2D overlays hand-aligned to a shared skeleton, and was a High risk. Gear is now attachable meshes on named sockets (§B3, §E2) — alignment is a transform, not an art problem |
| ~~**Sprite-sheet frame consistency for VFX**~~ | **Retired as stated** | VFX became procedural in-engine work under the O8 ruling and stays kind **F**. Real-time 3D additionally makes `GPUParticles3D` available in the actor viewport, which is a capability gain, not a new risk |
| ~~**No true transparency / background removal**~~ | **Retired** | The previous §B0's stated key weakness — ~30 s of processing per asset plus manual cleanup on ~10% of 949 — is gone. Alpha is exact by construction (§B4) |
| **Model / tool licence terms** | ⚠️ **Not a gate** | Deliberately **not** blocking, per D60 and the status banner. Output is work in progress and is not published. The risks are catalogued in **`31_ASSET_LICENCE_RISKS.md`**, which is a register. It becomes a gate again only when publication is on the table, and only by a new ruling |

---

# PART H — RECOMMENDED PRODUCTION ORDER

Reordered for 3D. The first two steps are new and both are gates: nothing downstream is worth starting until they pass.

1. **The device performance spike** (§G, risk 1). A worst-case `05` battle frame with placeholder meshes at §C2's provisional budgets, on a real mid-range Android handset, measuring triangles, draw calls, skinned meshes and texture memory. **This decides whether the whole document is viable as written**, and it needs no art at all — do it first.
2. **The Style Anchor Set** (§B2 step 1) — concept art, canonical base mesh, shared skeleton, toon material and light rig. Iterate until perfect; this gates everything.
3. **The hero, complete** — modelled, rigged, full clip set, one full gear set at all 5 rarities. Validates the socket system, the retarget strategy and the rarity language in one asset.
4. **UI panels, buttons, frames** (kind **F**) — unblocks all screen implementation, and is independent of every 3D unknown above. Can run in parallel from day one.
5. **Rule E9**, then Chapter 1 end to end: tile icons, board pieces, Chapter 1 enemies, Thornmaw. Unblocks a fully playable vertical slice.
6. **Currency, status, misc icons** — kinds **R** and **F**.
7. **The remaining 7 biomes, batched by biome** — enemies → elites → boss → board → backdrop together, so each biome is internally consistent.
8. **Gear icons (all 120)** — kind **R**, rendered from the gear meshes authored in step 3's system. Nearly free once the render rig exists.
9. **Perk + talent icons** — kind **F**, after the symbol CSV is authored (`22`).
10. **Pets, then mounts.**
11. **The die** — kind **M** now, a real die (§E16).
12. **VFX** — kind **F** plus in-engine particles.
13. ~~Profile frames and cosmetics~~ — cut (D14).
14. **Store and marketing art** — kind **R**, rendered from finished models.
