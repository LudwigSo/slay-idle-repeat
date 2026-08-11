# 15 — Art Direction & AI Asset Manifest

**This is the asset-generation document.** Everything visual in Slay Idle Repeat is to be produced by AI image generation. This file defines the style, the generation method, the technical output spec, and the complete list of what must be produced.

🔒 LOCKED: **Chibi cartoon fantasy, bold outlines.**

---

# PART A — STYLE BIBLE

## A1. The look in one sentence

> Chunky chibi fantasy characters with thick dark outlines, saturated candy-jewel colours, soft cel shading, and a glossy mobile-game finish — readable as a silhouette at 64 px on a phone screen in daylight.

## A2. Reference vocabulary

Neighbouring visual territory (for direction only — never copy or name these in prompts): *Legend of Slime*, *Top Heroes*, *Archer Forest*, *Hero Wars* casual art, modern Disney-adjacent mobile RPG UI.

## A3. Non-negotiable style rules

| Rule | Specification |
|---|---|
| **Proportions** | Characters are 2.5 to 3 heads tall. Big head, small body, oversized hands and feet, tiny or no neck. |
| **Outline** | Every character and prop has a uniform dark outline. Colour `#231A2E` (never pure black). Weight: 3–4 px at 512 px canvas, scaled proportionally. |
| **Shading** | Two-tone cel shading: one base, one shadow at 85% value / +8% saturation. One soft rim light from the upper left. **No gradients across large areas, no airbrushing.** |
| **Highlight** | A single crisp specular highlight on metal, gems and eyes. |
| **Eyes** | Large, expressive, high-contrast. Two-tone iris with a white catchlight at upper-left. Enemies may have glowing eyes with no iris. |
| **Colour** | Saturated, jewel-like. Avoid muddy mid-tones. Each biome has a locked 6-colour palette (§A5). |
| **Lighting** | Consistent key light from the **upper left** in every single asset. |
| **Perspective** | Characters: straight-on 3/4 view, slight low angle so they read as heroic. Icons: flat straight-on. Tiles: top-down-ish 2.5D. |
| **Background** | All character/prop/icon assets: **fully transparent**. No shadow baked in — contact shadows are drawn by the engine. |
| **Detail budget** | Low. If a detail is not readable at 64 px, remove it. Chunky shapes beat fine ornament. |
| **Text** | **Never** render text inside a generated image. All text is engine-rendered. |

## A4. Silhouette test

Every character asset must pass this test before acceptance: fill it 100% black, scale to 64 px. If you cannot tell which character it is, regenerate it. Silhouette clarity is the single most important quality bar in a chibi mobile game.

## A5. Biome palettes

Every asset for a biome uses only these six hues plus neutrals. Lock these into the prompts.

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

---

# PART B — GENERATION METHOD

## B0. Tool 🔒

**Midjourney, using style reference (`--sref`) and character reference (`--cref`) for consistency.**

| Concern | Specification |
|---|---|
| Why | Best-in-class for stylised chibi game art, and `--sref` is the strongest style-consistency control available in any current tool — which is the dominant risk across 975 assets. |
| Commercial rights | Included on paid plans. ⚠️ Confirm the current terms in writing and keep provenance records (job ID, prompt, seed, `--sref` value, date) for **every** generated asset. |
| Key weakness | **No true transparency.** Every character, prop and icon must go through background removal (§B4 step 1). Budget for this — it is roughly 30 seconds of automated processing per asset plus manual cleanup on maybe 10%. |
| Parameters | Lock `--ar`, `--style raw`, `--s` (stylize) and the `--sref` value per category and record them. Do not vary parameters mid-batch. |
| Automation | Use the Midjourney API or a queued bot workflow for the ~700 repetitive assets; hand-drive the ~50 showcase pieces (bosses, key art, store). |

## B1. The master prompt scaffold

Every generated asset uses this scaffold. Only `{SUBJECT}` and `{PALETTE}` change.

**Positive prompt template:**

```
{SUBJECT}, chibi cartoon fantasy game art, 2.5 heads tall proportions,
oversized head and hands, thick uniform dark outline (#231A2E),
two-tone cel shading, single soft rim light from upper left,
saturated jewel-tone colour palette limited to {PALETTE},
glossy mobile game asset, clean chunky readable shapes, low detail density,
centered composition, straight-on three-quarter view, full body,
isolated on a plain transparent background, no shadow, no ground,
high contrast, crisp edges, sticker-like, professional mobile RPG icon art
```

**Negative prompt (use on every generation):**

```
text, letters, watermark, signature, logo, ui, frame, border, background scenery,
photo, photorealistic, 3d render, realistic proportions, adult body proportions,
gradient background, drop shadow, ground plane, blurry, noisy, grainy, sketchy,
rough lines, painterly, oil painting, anime screenshot, multiple characters,
cropped, cut off, extra limbs, deformed hands, muted colours, desaturated,
dark scene, low contrast, busy details, fine ornament, small text
```

## B2. Consistency workflow — do this in order

Consistency across ~1,000 assets is the hard part. Follow this pipeline; do not generate assets ad hoc.

1. **Generate the Style Anchor Sheet first.** One image containing 6 characters in the target style (hero, a grunt, a brute, a pet, a mount, a boss silhouette). Iterate on this single image until it is exactly right. Everything downstream references it.
2. **Lock a seed family.** Record the seed, sampler, CFG and model version that produced the anchor. Reuse the same settings for every asset in a category.
3. **Upload the anchor sheet and use it as `--sref` on every subsequent generation.** This is what actually holds the style together — prompt text alone will not. Record the `--sref` URL/ID and never change it mid-project.
4. **Generate in category batches**, not one asset at a time. All 24 pets in one session, all 64 enemies in one session. Style drifts between sessions.
5. **Character sheets before variants.** For the hero and each boss, generate a 4-pose sheet in a single image (idle, attack, hurt, victory), then cut it. Poses generated separately will not match.
6. **Post-process every asset** through the standard pipeline (§B4).
7. **Silhouette-test** every character asset (§A4). Reject and regenerate failures.
8. **Palette-quantise** each biome batch to its locked 6-colour palette + neutrals so nothing drifts off-palette.

## B3. Per-category prompt modifiers

Append these to the scaffold:

| Category | Modifier |
|---|---|
| Hero & gear overlays | `heroic pose, confident stance, hooded adventurer, layered equipment clearly visible` |
| Enemies | `menacing but cute, exaggerated expression, simple readable silhouette, game enemy sprite` |
| Bosses | `imposing, large scale, dramatic pose, elaborate but chunky design, boss monster` |
| Pets | `adorable, round, bouncy, small companion creature, friendly expression` |
| Mounts | `rideable creature, side profile, saddle visible, sturdy stance` |
| Tile icons | `flat game icon, single object centered, thick outline, no perspective, icon design` |
| Gear icons | `single item on transparent background, item icon, 3/4 view, glossy, rpg loot icon` |
| Perk / talent icons | `circular emblem icon, single bold symbol, minimal, high contrast, magical rune style` |
| UI panels | `game ui panel, wooden and gold frame, 9-slice friendly, ornate corners, seamless edges` |
| VFX | `sprite sheet, bright energy effect, additive glow, transparent background, frame sequence` |
| Backgrounds | `wide parallax layer, no characters, no foreground objects, seamless horizontal tiling` |

## B4. Post-processing pipeline (mandatory for every asset)

```
1. Background removal  → true alpha, no halo (matte decontamination on)
2. Trim to content     → then pad to the target canvas with the subject centered
3. Palette quantise    → to the biome palette + neutrals (biome assets only)
4. Outline repair      → ensure the outline is continuous and uniform width
5. Resize              → to the spec size in the manifest (Lanczos, then sharpen 0.4)
6. Export              → PNG-32, then compress with pngquant (quality 80-95)
7. Atlas pack          → into the category atlas (see §D2)
```

---

# PART C — TECHNICAL OUTPUT SPEC

| Property | Value |
|---|---|
| Generation resolution | 1024×1024 (upscale to 2048 for bosses and backgrounds) |
| Delivery format | PNG-32 with straight (non-premultiplied) alpha |
| Colour space | sRGB |
| Compression in engine | ETC2 (Android), ASTC 6×6 (iOS) |
| Naming | `snake_case`, prefix by category — see §D1 |
| Pivot | Characters: bottom-center. Icons: center. Declared in the atlas metadata. |
| Max single texture | 2048×2048 |

### Delivery sizes by category

| Category | Delivered size | Notes |
|---|---|---|
| Hero body & gear overlays | 512×512 | layered, aligned to a shared skeleton |
| Standard enemies | 512×512 | |
| Elites | 640×640 | |
| Bosses | 1024×1024 | |
| Pets | 256×256 | |
| Mounts | 512×384 | wider than tall |
| Tile icons | 192×192 | |
| Board path & decor | 256×256 | |
| Gear icons | 192×192 | |
| Perk / talent / status icons | 128×128 | |
| Currency icons | 96×96 | |
| UI panels & frames | variable, 9-slice | corners must be square |
| Battle backdrops | 1080×1440 per layer | 3 parallax layers per biome |
| VFX sheets | 1024×1024 (4×4 grid of 256 px frames) | |

| Die faces | 256×256 | |

---

# PART D — CONVENTIONS

## D1. Naming convention

```
{category}_{subcategory}_{id}[_{variant}][_{state}].png

Examples:
  chr_hero_body_idle.png
  chr_hero_weapon_blade_s.png
  chr_enemy_frost_brute_attack.png
  chr_boss_rimehold_phase3.png
  pet_stormfang_idle.png
  mnt_starhoof_move.png
  tile_icon_treasure.png
  board_frost_path_curve_l.png
  gear_weapon_staff_ss.png
  icon_perk_executioner.png
  icon_talent_might_whetstone.png
  icon_status_burn.png
  ui_panel_main_9slice.png
  bg_frost_layer2.png
  vfx_crit_burst_sheet.png
  die_face_star_default.png
```

## D2. Atlas grouping

| Atlas | Contents |
|---|---|
| `atlas_hero` | Hero body + all gear overlays |
| `atlas_biome_{n}` | That biome's enemies, elites, boss, tiles, board pieces, decor |
| `atlas_pets` | All 24 pets |
| `atlas_mounts` | All 12 mounts |
| `atlas_icons_gear` | All 120 gear icons |
| `atlas_icons_perks` | All perk + talent + status icons |
| `atlas_ui` | Panels, buttons, frames, currency icons |
| `atlas_vfx` | All VFX sheets |
| `atlas_dice` | The die body and all 11 face artworks |

Backgrounds are **not** atlased (they are full-screen and streamed per biome).

---

# PART E — THE ASSET MANIFEST

## E1. Summary table

| § | Category | Asset count |
|---|---|---|
| E2 | Hero & gear overlays | 64 |
| E3 | Enemies (standard) | 128 |
| E4 | Elites | 32 |
| E5 | Bosses | 32 |
| E6 | Pets | 48 |
| E7 | Mounts | 24 |
| E8 | Tile icons | 14 |
| E9 | Board paths & decor | 112 |
| E10 | Battle backdrops & scene backgrounds | 28 |
| E11 | Gear icons | 120 |
| E12 | Perk icons | 98 |
| E13 | Talent node icons | 60 |
| E14 | Status effect icons | 12 |
| E15 | Currency & resource icons | 9 |
| E16 | Dice faces | 11 |
| E17 | UI panels, buttons, frames | 86 |
| E18 | ~~Profile frames & cosmetics~~ | **0 — cut** |
| E19 | VFX sprite sheets | 32 |
| E20 | Misc UI icons | 50 |
| E21 | Store & marketing | 15 |
| | **TOTAL** | **975** |

🔒 **Cosmetics are cut entirely** (decision D14). No die skins, no profile frames, no borders, no badges. Rank and Plus status are displayed as **text labels**. This removed 97 assets from the manifest.

---

## E2. Hero & gear overlays (64)

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

## E6. Pets (48)

**24 pets × 2 assets** (idle, ability-cast). 256×256. All pets are round, bouncy and unambiguously cute — they are the collection reward and must be desirable at thumbnail size.

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
| `PET_DICEBEAST` | Dicebeast | SS | *a small creature whose whole body is a glowing golden six-sided die, with tiny legs and big eyes* |
| `PET_SOLARION` | Solarion | SS | *a radiant miniature sun lion with a mane of golden flame and white-hot eyes* |
| `PET_NYXWEAVER` | Nyxweaver | SS | *a small elegant spider of woven night sky with silver constellation markings* |
| `PET_ARCHIVIST` | The Archivist | SS | *a floating hooded book-creature with glowing pages for a face and quill-tipped arms* |

---

## E7. Mounts (24)

**12 mounts × 2 assets** (idle, moving). 512×384, side-profile 3/4 view with a visible saddle sized for a chibi rider.

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
| `MNT_VOIDSTEED` | Voidsteed | SS | *a shadow horse made of purple starfield with white flame hooves and no eyes* |
| `MNT_FATESPINNER` | Fatespinner | SS | *a large elegant spider-like creature with a body of golden dice and silk threads of light* |
| `MNT_WORLDBEARER` | Worldbearer | SS | *an enormous stone tortoise-elephant carrying a tiny floating island with a tree on its back* |

---

## E8. Tile icons (14)

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

| Asset | Count | Spec |
|---|---|---|
| Biome battle backdrops | 24 | 8 biomes × 3 parallax layers (far / mid / near), 1080×1440, seamless horizontal tiling |
| Home / Camp background | 1 | *a cosy nighttime camp clearing with a tent, campfire, weapon rack, and a starry sky* |
| Arena background | 1 | *a floating stone duelling platform above clouds with banners and torches* |
| Forge background | 1 | *a warm dwarven-style workshop interior with an anvil, glowing forge and hanging tools* |
| Menagerie background | 1 | *a bright stable-garden with hay bales, feeding bowls and small perches* |

---

## E11. Gear icons (120)

**24 base items × 5 rarities.** 192×192, 3/4 view, glossy, on transparent.

Base items = the 6 slots × 4 families listed in `08_GEAR_AND_MERGING.md` §1. Rarity escalation follows the table in §E2. The engine draws the rarity **frame** separately — the icon itself carries only the material/ornament escalation.

ID pattern: `gear_{slot}_{family}_{rarity}`

---

## E12. Perk icons (98)

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

## E13. Talent node icons (60)

**3 branches × 20 nodes.** 128×128, same emblem language as perks but with a **hexagonal** frame instead of circular, so talents and perks are never confused.

| Branch | Disc colour | Symbol language |
|---|---|---|
| MIGHT | red `#D9453C` | weapons, fists, flames |
| WARD | blue `#3B82F6` | shields, armour, roots |
| FORTUNE | gold `#F5A623` | dice, clovers, coins, stars |

Keystones (9 of the 60) get a larger **star-shaped** frame and an animated glow overlay.

> ✅ **All 60 symbol descriptors are authored in `22_ICON_PROMPT_TABLES.md` Part B.**

---

## E14. Status effect icons (12)

128×128, small, extremely readable at 32 px. `BURN`, `POISON`, `BLEED`, `FREEZE`, `STUN`, `WEAKEN`, `SUNDER`, `SPORE`, `RAGE`, `WARD`, `HASTE`, `REGEN`.

Descriptors: *a flame · a green skull bubble · a red droplet · a snowflake · orbiting stars · a downward broken arrow · a cracked shield · a spore cloud · a red upward arrow with fangs · a blue bubble shield · a winged boot · a green cross with leaves.*

---

## E15. Currency & resource icons (9)

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

## E16. Dice faces (11)

**One die design only.** 🔒 Skins were cut with the rest of the cosmetics (D14).

11 face artworks at 256×256, composited onto a 3D-look die body: `pip1`–`pip6` plus the five special faces.

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

| Group | Count | Notes |
|---|---|---|
| 9-slice panels | 12 | main, dark, light, parchment, wood, stone, glass, tooltip, modal, banner, tab-active, tab-inactive |
| Buttons | 18 | primary/secondary/danger/ghost × normal/pressed/disabled, plus the large ROLL button (3 states), plus the ad button (3 states) |
| Item rarity frames | 10 | 5 rarities × (square item slot, round portrait) |
| Progress bars | 9 | HP, Energy, Legend XP — fill + track + cap for each |
| Tab bar & nav icons | 12 | Home, Hero, Forge, Talents, Menagerie, Arena, Shop, Codex, Settings, Back, Close, Info |
| Card backs & draft cards | 8 | 6 category card frames + owned-upgrade gold frame + the dashed ad-slot card |
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

1024×1024, 4×4 grid of 256 px frames, additive-blend friendly, transparent.

`hit_normal`, `hit_crit`, `hit_block`, `miss_puff`, `heal_burst`, `shield_form`, `shield_break`, `burn_loop`, `poison_loop`, `bleed_loop`, `freeze_apply`, `stun_stars_loop`, `rage_aura_loop`, `regen_loop`, `levelup_burst`, `merge_success`, `enhance_success`, `enhance_fail`, `treasure_burst`, `coin_pickup`, `gem_pickup`, `die_land_dust`, `die_star_flare`, `die_surge_spark`, `die_fortune_sparkle`, `portal_swirl_loop`, `boss_phase_shockwave`, `victory_confetti`, `defeat_fade`, `perk_select_flash`, `pet_ability_generic`, `mount_dash_trail`.

---

## E20. Misc UI icons (50)

Counted individually (50): sort · filter · lock · unlock · salvage · merge · enhance · equip · unequip · compare · star filled · star empty · plus · minus · check · cross · arrow up · arrow down · arrow left · arrow right · speed ×1 · speed ×2 · speed ×3 · skip · pause · sound on · sound off · music on · music off · haptics · language · account · privacy · help · bug report · share · calendar · clock · quest scroll · gift · wheel · leaderboard · medal · chest closed · chest open · key · timer · warning · info.

---

## E21. Store & marketing (15)

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

Before an asset batch is accepted:

- [ ] Silhouette test passed at 64 px (characters)
- [ ] Readable at the smallest in-game display size
- [ ] Outline continuous, uniform width, colour `#231A2E`
- [ ] Key light from upper left, consistent with the batch
- [ ] Palette conforms to the biome's locked six colours + neutrals
- [ ] Alpha is clean — no white/black halo, no semi-transparent fringe
- [ ] Correct canvas size and pivot per §C
- [ ] No text, watermark or signature anywhere in the image
- [ ] Proportions match the Style Anchor Sheet (2.5–3 heads)
- [ ] File named per §D1 and packed into the correct atlas
- [ ] Side-by-side comparison against 3 previously-approved assets in the same category shows no style drift

---

# PART G — KNOWN RISKS IN AI ASSET GENERATION

| Risk | Severity | Mitigation |
|---|---|---|
| **Style drift across ~1,000 assets** | High | Style Anchor Sheet + image-to-image reference on every generation + batch generation + the side-by-side drift check |
| **Layered gear misalignment** (E2) | High | Generate overlays on a ghosted body; budget manual alignment; fallback to composited sprites |
| **Sprite-sheet frame consistency for VFX** | High | Image models are poor at coherent frame sequences. **Recommendation: generate VFX as single hero frames and animate procedurally in-engine (scale, rotate, fade, particle systems) rather than as generated sheets.** This is likely to look better and cost less. |
| **9-slice panels with warped corners** | Medium | Generate flat, wide panels and cut corners manually; verify by stretching to 3× |
| **Seamless tiling for parallax backgrounds** | Medium | Generate wider than needed and blend the seam manually, or use a tiling-aware model/mode |
| **98 perk icons looking interchangeable** | Medium | Author the per-icon symbol CSV first (E12) and enforce distinct silhouettes |
| **Model/licence terms** | Medium | ✅ Tool decided: **Midjourney** (§B0). Confirm the current commercial terms in writing before the first batch and keep provenance records (job ID, prompt, seed, `--sref`, date) for every asset. Legal prerequisite, not a formality. |
| **Rarity escalation not reading clearly** | Low | Test all 5 rarities of one item side by side before generating the other 23 |

---

# PART H — RECOMMENDED GENERATION ORDER

1. Style Anchor Sheet (iterate until perfect — this gates everything)
2. Hero body poses + one full gear set at all 5 rarities (validates E2 and the rarity language)
3. UI panels, buttons, frames (unblocks all screen implementation)
4. Tile icons + Chapter 1 board pieces + Chapter 1 enemies + Thornmaw (unblocks a fully playable vertical slice)
5. Currency, status, misc icons
6. Remaining 7 biomes, batch by biome (enemies → elites → boss → board → backdrop together, so each biome is internally consistent)
7. Gear icons (all 120, one session)
8. Perk + talent icons (after the symbol CSV is authored)
9. Pets, then mounts
10. Dice faces
11. VFX
12. ~~Profile frames and cosmetics~~ — cut (D14)
13. Store and marketing art
