# Greenwood Vale — the chapter 1 cast

Nine 3D characters: six standard enemies, two elites and the chapter boss, built for
`CH_01_GREENWOOD_VALE`. This is the design record for them — what each one is, why it looks
the way it does, and which line of game data it answers to.

Source: [`assets/source/greenwood_kit.py`](source/greenwood_kit.py) (modelling) and
[`assets/source/greenwood_export.py`](source/greenwood_export.py) (bake and glTF), against
[`assets/source/greenwood_enemies.blend`](source/greenwood_enemies.blend). Shipped to
`src/SlayIdleRepeat.Client/game/art/`.

## The cast was not invented here

Chapter 1's roster is already fixed by data, and the models answer to it rather than the other
way round:

- `game-data/content/chapters/CH_01_GREENWOOD_VALE.json` sets the `enemyPool` draw weights, names
  `miniBossIds` as `EL_THORN_SENTINEL` and `EL_MOSSBACK_ALPHA`, and `bossId` as `BOSS_THORNMAW`.
- `game-data/content/enemies/enemies.json` gives each archetype its stat coefficients and each
  elite its `baseArchetype`.
- `game-data/assets/asset_manifest_art.json` already carried a one-line subject for all eleven
  greenwood rows (§E3/§E4/§E5) plus the biome palette. Those subjects are the brief.

Six of the pool's eight archetypes are modelled: the six with the highest chapter-1 draw weight.
`LEECH` (weight 5) and `REAVER` (weight 0, i.e. never drawn in this chapter) are not.

| # | Archetype / id | Name | Draw weight | Fights like |
|---|---|---|---|---|
| 1 | `GRUNT` | Thistlekin Scrapper | 40 | the baseline — every coefficient 1.0 |
| 2 | `SWARM` | Acornling | 20 | three per draw, fast, fragile |
| 3 | `BRUTE` | Barkbelly | 15 | slow, huge HP, heavy hits |
| 4 | `SKIRMISHER` | Bramble Cutter | 10 | fast, dodgy, low HP |
| 5 | `WARDEN` | Toadstool Bulwark | 5 | armoured, slow, high DEF |
| 6 | `CASTER` | Sporecaller | 5 | ranged, applies the biome status |
| 7 | `EL_THORN_SENTINEL` | Thorn Sentinel | elite (WARDEN base) | mini-boss |
| 8 | `EL_MOSSBACK_ALPHA` | Mossback Alpha | elite (BRUTE base) | mini-boss |
| 9 | `BOSS_THORNMAW` | Thornmaw | boss | 3 phases, root then bloom |

## The look

Everything in `game-design.md`'s art section, applied:

**Chunky, oversized-head proportions.** Heads run a third to a quarter of total height; hands and
feet are deliberately too big for the arms and legs they hang off. Nothing in the cast is
naturalistically proportioned and nothing is meant to be.

**Painterly, semi-realistic surfaces.** Every material is a two-tone painterly shader — object-space
noise breaks the base colour between two poles, and a fine grain drives both bump and roughness, so
bark is coarse and streaky, moss is fine and velvety, and a mushroom cap is nearly smooth. All of it
is baked down to one texture set per creature.

**One locked biome palette.** Taken verbatim from the manifest's `greenwood` row —
base `#5FBF5F`, shadow `#2F7A3F`, accent `#F2D06B`, glow `#FFF3A8`, prop `#8B5E3C`. Bark is warmed
and pushed off the flat `prop` hex, because a bark sitting exactly on it bakes out as mud. The one
warm red in the biome is the toadstool cap, and it is spent deliberately: it is what lets a warden,
a sporecaller or the boss be picked out of a green field at a glance.

**One shared motif.** Every creature carries at least one toadstool. The manifest's subject line for
the whole chapter ends in `mushroom cap details`, and a shared motif is what makes nine different
silhouettes read as one faction.

**Nothing is metallic and nothing may be.** The build lights 3D with one directional key, one fill
and flat ambient — no reflection probe, no sky — so a true metal has nothing to reflect and renders
black. Tusks, thorn plate and pollen are bright albedo at low roughness; the gloss is the light's.
`GreenwoodCastTests` asserts this over every shipped file, the same way `HeroWeaponKitTests` does
for the hero kit.

**Silhouette is the quality bar.** Each creature carries one shape that steps off its body outline,
and `greenwood_kit.contact_sheet` renders a flat-black pass of all nine so the claim can be checked
rather than asserted.

## The nine

Heights are in world units against the hero's 2.22. Every model stands with its feet on exactly
`z = 0` and exports with an identity root node.

### 1 · Thistlekin Scrapper — `chr_enemy_greenwood_grunt` · 1.62 (2.22 with club)

A small bark humanoid with a split-log head, knot-hole eyes under a heavy brow, a moss beard and a
thistle crest. Forty percent of the chapter's draw weight, so it is the shape the player learns the
biome from and everything else is read against it.

**Silhouette hook:** the crude club — a broken branch with a rock lashed into the fork — held high
and well clear of the head. Held over the head it disappeared the moment the outline was filled in.

### 2 · Acornling — `chr_enemy_greenwood_swarm` · 0.85

A fat acorn with a knurled cap, six little legs and two enormous eyes. The `SWARM` archetype puts
three on the field per draw, so it is solved for the smallest it will ever be seen at: one circle,
one stalk, two eyes. Barely knee-high to the hero, which is the joke.

**Silhouette hook:** the sprouting stalk and its leaf.

### 3 · Barkbelly — `chr_enemy_greenwood_brute` · 2.64

One enormous stump of a barrel with everything else hung off it — a head sunk between the shoulders,
stump legs, and arms long enough to put the knuckles on the ground. Split bark staves keep the
growth rings from reading as the hoops of an actual barrel.

**Silhouette hook:** shelf fungus rising off the shoulders like pauldrons. Flat on the flank they
read as wings; behind the arms they did not read at all.

### 4 · Bramble Cutter — `chr_enemy_greenwood_skirmisher` · 1.80

The only diagonal in the cast, and thin where the rest are fat. Caught mid-lunge, one leg thrown
back to push, twin flat thorn daggers swept wide of the body — one low and forward, one cocked high
behind — with a ragged leaf scarf streaming off the collar.

**Silhouette hook:** the X of the two blades, held wide. Thrust straight down the facing axis the
fixed camera saw them end-on and both vanished.

### 5 · Toadstool Bulwark — `chr_enemy_greenwood_warden` · 1.94

A squat guardian behind a tower shield cut from a giant toadstool cap: a red-and-white disc almost
as tall as the creature carrying it, rimmed, thorn-studded and bossed, with a helmeted head peeking
over the top corner. The shield sits on one flank rather than centred so the body is not entirely
eclipsed at the fixed 3/4 camera angle.

**Silhouette hook:** the shield. This is the one enemy in the chapter a player should read as
*hit this from the side* before the fight starts.

### 6 · Sporecaller — `chr_enemy_greenwood_caster` · 2.41

A hood that is a drooping toadstool, flopping forward over a face that is not there — a dark hollow
with two green lights in it. The robe reaches the floor in three leaf tiers, so the creature has no
legs and reads as sliding rather than walking, which is the cheapest way to make one enemy in a line
of walkers look wrong.

**Silhouette hook:** the floating spore orb held off the raised hand, and the flop of the cap.

### 7 · Thorn Sentinel — `chr_elite_thorn_sentinel` · 3.41

`enemies.json` gives this elite the `WARDEN` base archetype, so it had to read as heavy without
repeating the standard warden's tower shield. It carries an ivory bramble greatsword planted
point-down instead: the same slow, rooted, unmovable idea told with a different outline. The armour
is genuinely woven — helical vines run the length of every limb with the thorn plate on top of them
— and the helm is a slit visor with two lights behind it under a rose-bloom crest.

**Silhouette hook:** the planted greatsword, and the rake of the pauldron spurs. The blade is ivory
on purpose: cut from the same dark maroon as the armour it was a dark shape inside a dark shape.

### 8 · Mossback Alpha — `chr_elite_mossback_alpha` · 2.49 tall, 4.16 long

The only quadruped in the chapter, which does most of the work of telling it apart before any detail
resolves: a long low wedge where everything else is an upright stack. The mass is thrown forward
onto a mossy hump sprouting toadstools, the head is nearly as wide as the shoulders, and the
hindquarters are small enough to look like an afterthought.

**Silhouette hook:** two great glowing green sabre tusks out of the lower jaw — the one feature the
manifest names for this elite — over the hump and its mushrooms.

### 9 · Thornmaw — `chr_boss_thornmaw` · 4.91

`bosses.json` calls it the teaching boss: phase 1 is basic attacks and nothing else, phase 2 roots
the party, phase 3 blooms and keeps summoning `SWARM` adds. The model makes those three readable
without a line of bespoke boss code — the thorned vine arms are the phase 2 root, the open bloom
full of pollen is the phase 3 bloom, and the acorn-like seed pods clustered at its feet are visibly
where the adds come from.

It does not face the player with a head on a body. It faces them with an open throat: the bloom is
turned almost straight down the facing axis, so the fixed 3/4 camera looks *into* the mouth — a ring
of fangs converging across a dark gullet, a lolling tongue, a crown of glowing yellow pollen on
arching stamens, and the petals splayed flat behind it as a ruff.

**Silhouette hook:** the open maw ringed in fangs, over a stalk rooted in mossy stone.

## Budgets

The hero is one actor on screen and carries 22k triangles with a 1024 base colour. A chapter-1 fight
puts up to five enemies out at once, so the budget is spent by role rather than evenly:

| Role | Triangles | Base colour | ORM / normal |
|---|---|---|---|
| standard (×6) | 10,000 | 512 | 512 |
| elite (×2) | 16,000 | 1024 | 512 |
| boss | 24,000 | 1024 | 1024 |

`greenwood_export.plan()` derives each decimate ratio by *measuring* what the creature actually
builds at subdivision level 1, rather than by guessing a ratio. One asset is over budget and says so
in the log: the Thorn Sentinel builds 87k triangles and would need a 0.18 collapse to reach 16k,
below the 0.20 floor where decimation starts eating silhouette rather than interior — so it ships at
17.4k and the overage is reported instead of quietly turning a creature to mush.

## The recipe

Identical to the hero's, because `greenwood_export` calls `hero_export.bake_asset` directly rather
than restating it. Purge orphans → pin every subsurf to level 1 → convert → join → decimate →
Smart UV → bake DIFFUSE(colour) / ROUGHNESS / NORMAL in Cycles at 1 sample (these are data passes,
so one sample is exact, not an approximation) → pack roughness into the ORM green → one material →
GLB, +Y up. Every file is one mesh, one primitive, one draw call.

Two things the greenwood build adds on top, both in `greenwood_kit.build_all`:

- **`settle`** drops each creature so its lowest *evaluated* point sits on exactly `z = 0`. Every
  builder aims for feet-on-the-floor by hand and every builder misses by a centimetre, because what
  ends up lowest is usually a toe spike added after the legs were measured.
- **`flatten`** bakes every object transform into its mesh. The export joins into whichever object
  is active and the result keeps *that* object's transform, so without this the glTF root node comes
  out carrying a stray translation.

### Rebuilding

Blender must be running with the MCP server (the addon refuses to open its socket in `-b`). Then:

```python
import sys; sys.path.insert(0, r"...\assets\source")
import greenwood_export
greenwood_export.export_all(r"...\src\SlayIdleRepeat.Client\game\art")
```

`greenwood_kit.contact_sheet(out_dir)` renders the nine beauty shots and the nine flat-black
silhouette passes used to check the 64 px test.

## Known gaps

- **No Godot scenes.** The `.glb` files ship, but nothing instances them yet; there is no
  `Enemy.tscn` equivalent to `Hero.tscn`.
- **No sockets and no rig.** These are single static meshes. The hero's swappable-weapon socket
  trick is not applied here — no enemy carries a detachable weapon, and nothing is animated.
- **`asset_manifest_art.json` is untouched.** Its greenwood §E3/§E4/§E5 rows still describe 512×512
  two-pose *sprites* with an atlas, which is the pre-D60 2D medium. Editing the manifest moves the
  `ContentSnapshot` hash, so reconciling it with real 3D assets is its own task.
- **The boss ships one mesh, not three.** §E5 lists Thornmaw at `idle` / `phase2` / `phase3`. The
  model is built so the three phases are legible in one silhouette rather than authored as three.
