# 22 — Icon Prompt Tables (138 icons)

Resolves open items P1 #6 (perk icons) and P1 #7 (talent icons). ⚠️ **Talent icons are 40, not 60** (`16` D54 removed the FORTUNE branch). 🔴 The perk count is unreconciled: this document's Part A is organised on a seven-category taxonomy the perk rework replaced with nine (`06` §2), so Part A is owed a re-section before its rows are generated.

Without these, generated icons come out interchangeable and the player cannot tell their build apart at a glance. Every row below gives the `{SYMBOL}` string that slots into the icon prompt scaffold in `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` §E12/§E13.

**Prompt assembly:**
```
{SYMBOL}, {FRAME} game ability icon, single bold centered symbol,
{DISC_COLOUR} radial background disc, thick dark outline (#231A2E),
minimal detail, high contrast, glossy magical emblem,
chibi cartoon fantasy game art style, no text
```
`{FRAME}` = `circular` for perks, `hexagonal` for talents, `star-shaped` for keystones.

**Silhouette rule:** within a category, no two symbols may share a primary shape. The tables below were written with that constraint applied — check it again after generation.

---

# PART A — Perk Icons (98)

## A1. Offense — red disc `#D9453C` (22)

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_SHARP_EDGE` | Sharp Edge | a single upward sword blade with a glinting edge |
| `PK_QUICK_HANDS` | Quick Hands | two crossed daggers with motion streaks |
| `PK_KEEN_EYE` | Keen Eye | a stylised eye inside a crosshair ring |
| `PK_HEAVY_SWING` | Heavy Swing | a warhammer head mid-arc with an impact crack |
| `PK_PIERCING` | Piercing Strikes | an arrowhead punching through a cracked plate |
| `PK_BRUTALITY` | Brutality | a clenched armoured fist with knuckle spikes |
| `PK_EXECUTIONER` | Executioner | a headsman's axe over a thin red line |
| `PK_FLURRY` | Flurry | five short slash marks fanned outward |
| `PK_OVERPOWER` | Overpower | a huge greatsword with a small weight chained to it |
| `PK_CRIT_CASCADE` | Crit Cascade | three stacked lightning-shaped crit sparks ascending |
| `PK_RUPTURE` | Rupture | a torn wound shape dripping three droplets |
| `PK_IGNITE` | Ignite | a flame bursting from a struck flint |
| `PK_GIANT_SLAYER` | Giant Slayer | a tiny sword standing on a huge fallen boot |
| `PK_MOMENTUM_ATK` | Warpath | a boot print trail rising into a raised blade |
| `PK_CLEAVE` | Cleave | a single wide horizontal slash cutting three silhouettes |
| `PK_DEATHMARK` | Deathmark | a floating skull-shaped target rune |
| `PK_BERSERK` | Berserker's Pact | a cracked heart with an axe embedded in it |
| `PK_TWIN_STRIKE` | Twin Strike | two identical mirrored blades striking the same point |
| `PK_SUNDERING` | Sundering Blows | a shield being split down the middle |
| `PK_APEX` | Apex Predator | a fanged jaw silhouette crowned with a small spark |
| `PK_ANNIHILATE` | Annihilation | a blade of pure white light erasing a dark shape |
| `PK_CHAIN_DEATH` | Chain of Ruin | three skulls linked by a taut chain |

## A2. Defense — blue disc `#3B82F6` (18)

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_TOUGH_HIDE` | Tough Hide | a thick layered leather pauldron |
| `PK_IRON_SKIN` | Iron Skin | an armoured forearm with riveted plates |
| `PK_NIMBLE` | Nimble | a feather beside a dashed dodge arc |
| `PK_BULWARK` | Bulwark | a wide tower shield planted in the ground |
| `PK_STOIC` | Stoic | a stone face carved in a flat plinth |
| `PK_THORNS` | Thornmail | a round shield ringed with outward spikes |
| `PK_SECOND_SKIN` | Second Skin | two nested shield outlines, one inside the other |
| `PK_WARDED` | Warded | a glowing bubble over a small shield |
| `PK_EVASIVE` | Evasive Step | an afterimage boot leaving a blur trail |
| `PK_STALWART` | Stalwart | a small shield braced against a huge shadowed fist |
| `PK_ANCHOR` | Anchor | a heavy iron anchor driven into stone |
| `PK_REACTIVE` | Reactive Plating | armour plates snapping outward in a burst |
| `PK_IMMOVABLE` | Immovable | a mountain silhouette with a broken chain across it |
| `PK_AEGIS` | Aegis | a radiant hexagonal energy shield |
| `PK_LAST_STAND` | Last Stand | a cracked shield with a single defiant flame behind it |
| `PK_MIRROR` | Mirror Ward | a polished mirror shield reflecting an arrow back |
| `PK_UNBREAKABLE` | Unbreakable | a shield with a crack that is visibly healing shut |
| `PK_FORTRESS` | Living Fortress | a small castle keep with arms and legs |

## A3. Sustain — green disc `#4CAF50` (12)

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_LEECH` | Leeching Strikes | a dagger with a red droplet travelling up the blade |
| `PK_REGEN` | Slow Regeneration | a sprouting leaf on a heart-shaped seed |
| `PK_VITAL_SURGE` | Vital Surge | a heart with an upward energy pulse line |
| `PK_BLOODLETTER` | Bloodletter | a curved blade over a filling chalice |
| `PK_FEAST` | Feast | a fork and knife crossed over a skull |
| `PK_HEALERS_TOUCH` | Healer's Touch | an open palm radiating soft light |
| `PK_SANGUINE` | Sanguine Pact | a crit spark turning into a droplet mid-fall |
| `PK_RESTORATION` | Restoration | a stone basin overflowing with glowing water |
| `PK_UNDYING` | Undying Will | a heart with a single unbroken thread around it |
| `PK_TRANSFUSION` | Transfusion | a heart pouring overflow into a shield bubble |
| `PK_PHOENIX` | Phoenix Heart | a small phoenix rising from a heart-shaped ember |
| `PK_ETERNAL` | Eternal Spring | a fountain with two streams, one green one red |

## A4. Dice & Board — gold disc `#F5A623` (12)

These IDs are shared with `04_DICE_SYSTEM.md` §5.

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_LOADED_DIE` | Loaded Die | a die with a small lead weight visible inside |
| `PK_SECOND_THOUGHT` | Second Thought | a die with a circular refresh arrow around it |
| `PK_MOMENTUM_DIE` | Momentum | four dice in a row, the last one glowing gold |
⚠️ **The three rows here were `PK_FORTUNES_FAVOUR`, `PK_CHAINBREAKER` and `PK_TWIN_FATES`.** ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). Their perks are gone, so the icons are not owed.
| `PK_WEIGHTED_FATE` | Weighted Fate | a die on a tilted balance scale |
| `PK_DICELORD_GIFT` | The Dicelord's Gift | a golden die floating inside a masked figure's open palm |
| `PK_PATHFINDER` | Pathfinder | a winding dotted path with a small flag at the end |
| `PK_SCOUT` | Scout's Instinct | a spyglass over a forked road sign |
| `PK_LEAPFROG` | Leapfrog | a boot leaping over two flagstones |
| `PK_CARTOGRAPHER` | Cartographer | a rolled map with a glowing X and a compass rose |

## A5. Economy — purple disc `#8B5CF6` (10)

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_GREED` | Greed | a coin pile with a grasping hand above it |
| `PK_HAGGLER` | Haggler | a price tag with a downward arrow through it |
| `PK_SCAVENGER` | Scavenger | a crow perched on an open sack |
| `PK_LUCKY_FIND` | Lucky Find | a glowing gem half-buried in loose earth |
| `PK_PROSPECTOR` | Prospector | a pickaxe striking a rune stone with sparks |
| `PK_MERCHANT_FRIEND` | Merchant's Friend | two hands shaking over a market awning |
| `PK_BOUNTY` | Bounty Hunter | a wanted poster with a coin nailed to it |
| `PK_ALCHEMY` | Alchemy | a flask turning coins into crowns |
| `PK_MIDAS` | Midas Touch | a fingertip turning a pebble gold |
| `PK_HOARD` | Dragon's Hoard | a small dragon curled asleep on a coin mound |

## A6. Trigger / Synergy — orange disc `#F97316` (16)

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `PK_GLASS` | Glass Cannon | a cannon made of cracked glass |
| `PK_TURTLE` | Turtle Doctrine | a shield with a sword blade growing out of its edge |
| `PK_JUGGERNAUT` | Juggernaut | a heart with a bicep flexing out of it |
| `PK_DUELIST` | Duelist | two crossed rapiers with a single spark between the tips |
| `PK_SWARMBANE` | Swarmbane | one blade sweeping through five small dots |
| `PK_OPENER` | Opening Gambit | a chess pawn advancing with a sword shadow |
| `PK_CLOSER` | Closing Argument | an hourglass nearly empty with a blade through it |
| `PK_PACK_LEADER` | Pack Leader | a paw print with a small crown above it |
| `PK_SYMBIOSIS` | Symbiosis | two interlocking crescents forming a circle |
| `PK_ECHO` | Echo Strike | a slash mark with a fainter duplicate behind it |
| `PK_MOMENTUM_CH` | Snowball | a snowball rolling downhill, growing, with a spark core |
| `PK_GAMBLER` | Gambler's Ruin | a coin frozen mid-flip, half gold half black |
| `PK_ARSENAL` | Arsenal | a fanned rack of five different weapon tips |
| `PK_PERFECTIONIST` | Perfectionist | a flawless faceted gem with a single highlight |
| `PK_AVATAR` | Avatar of the Die | a humanoid silhouette whose head is a glowing die |
| `PK_SINGULARITY` | Singularity | many small symbols spiralling into one bright point |

## A7. Cursed — black-violet disc `#4C1D6B` (8)

All cursed icons carry a **cracked** motif and a downward-hanging chain fragment, so the category reads instantly as "this has a cost".

| ID | Name | `{SYMBOL}` |
|---|---|---|
| `CP_BLOOD_PRICE` | Blood Price | a cracked coin dripping blood |
| `CP_BRITTLE` | Brittle Fury | a shattering crystal blade mid-break |
| `CP_HUNGER` | Endless Hunger | a cracked open mouth with a spiral void inside |
| `CP_MYOPIA` | Myopia | a cracked eye with the pupil fogged over |
| `CP_LEADEN` | Leaden Die | a cracked die sunk into thick tar |
| `CP_PAUPER` | Pauper's Bargain | a cracked upturned empty purse |
| `CP_GLASS_HEART` | Glass Heart | a cracked transparent glass heart with light inside |
| `CP_TIMEBOUND` | Timebound | a cracked hourglass leaking dark sand upward |

---

# PART B — Talent Node Icons (40)

Hexagonal frame. Keystones (9) use a star-shaped frame at 1.4× size with an animated glow overlay.

## B1. MIGHT — red disc `#D9453C` (20)

| Tier | Node | `{SYMBOL}` |
|---|---|---|
| 1 | Whetstone | a whetstone sharpening a blade edge |
| 1 | Precision | a narrow crosshair over a single point |
| 1 | Cruelty | a blade tip with three impact sparks |
| 1 | Swiftness | a winged blade |
| 1 | Piercer | a spike passing through a plate |
| 1 | Aggression | a forward-thrusting spear head |
| 2 | Bloodthirst | a fanged blade over a droplet |
| 2 | Executioner's Edge | a descending axe over a thin horizon line |
| 2 | Giantbane | a spear pinning an oversized shadow |
| 2 | Escalation | three ascending bars ending in a blade |
| 2 | Kindling | a small flame on a bundle of twigs |
| 2 | Opening Blow | a fist striking a starting bell |
| 3 | Overwhelm | a wave of blades cresting |
| 3 | Savage Rhythm | six notches, the sixth glowing |
| 3 | Bonecrusher | a mace over a splintering bone |
| 3 | Warlust | a full heart with a blade rising from it |
| 3 | Relentless | a stopwatch with a blade for a hand |
| 4★ | **Bloodmoon Ascendant** | a crimson eclipsed moon dripping into a chalice |
| 4★ | **Perfect Strike** | a single blade tip splitting an arrow in flight |
| 4★ | **Avatar of War** | a horned war helm wreathed in red flame |

## B2. WARD — blue disc `#3B82F6` (20)

| Tier | Node | `{SYMBOL}` |
|---|---|---|
| 1 | Constitution | a broad heart with a plate across it |
| 1 | Plating | three overlapping riveted plates |
| 1 | Footwork | two boot prints with a curved dodge line |
| 1 | Guard | a raised buckler |
| 1 | Resilience | a stone block absorbing an impact |
| 1 | Mending | a needle stitching a shield seam |
| 2 | Second Wind | a lung-shaped bellows exhaling light |
| 2 | Barbs | a ring of outward thorns |
| 2 | Bulwark Training | a shield on a training dummy |
| 2 | Steadfast | a boot planted on a broken chain |
| 2 | Warded Soul | a soul-flame inside a bubble |
| 2 | Slow Burn | a drip filling a heart, one droplet at a time |
| 3 | Fortified | a shield with a battlement crown |
| 3 | Grit | clenched teeth over a cracked plate |
| 3 | Counterpoise | a shield and blade balanced on a fulcrum |
| 3 | Iron Will | a helm with a broken stun-star across it |
| 3 | Vital Reserve | an overflowing heart spilling into a bubble |
| 4★ | **Unyielding** | a shield taking a killing blow and holding |
| 4★ | **Aegis Eternal** | a vast radiant shield eclipsing a small figure |
| 4★ | **Reversal** | an arrow bending back on itself into its archer |

⚠️ **B3 held the FORTUNE branch's 20 rows and is removed with the branch** (`16` D54). The gold disc `#F5A623` retires with it and is **not** reassigned to a future branch — a colour that once meant *fortune* on a talent node would read as a third branch that no longer exists. The talent icon set is **40 rows over two branches**.

---

# PART C — Generation notes

| Note | Detail |
|---|---|
| **Batch order** | Generate one full category per session (all 22 Offense, then all 18 Defense, …). Style drifts between sessions. |
| **Style reference** | Every icon generates with an image-to-image reference against the first 3 approved icons of its category, at 0.4 strength. |
| **Disc colour** | Do not rely on the prompt for the disc colour. Generate on a neutral disc, then recolour the disc programmatically in post so category colours are exact and consistent. |
| **Silhouette check** | After each category, lay all icons out at 48 px in a grid. Any two that read as the same shape must be regenerated. This check is the whole point of these tables. |
| **Frames** | Generate the symbol only. The circular / hexagonal / star frame is a separate UI asset composited in-engine, so a perk promoted to a talent (or vice versa) needs no regeneration. |
| **Total** | 98 perk + 60 talent = **158 icons**, matching `15` §E12 and §E13. |
