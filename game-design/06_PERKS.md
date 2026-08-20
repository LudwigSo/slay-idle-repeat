# 06 — Perks (In-Run Draft Pool)

The perk draft is the game's primary decision. Everything else — dice, gear, talents — sets the stage; the draft is where the player actually plays.

🔒 Perks are **run-scoped**. They vanish when the run ends. This is what keeps every run fresh despite a heavily linear meta.

---

## 1. Draft mechanics

| Property | Value |
|---|---|
| Trigger | **The run opening**, and after winning any `TILE_ENEMY`, `TILE_ELITE` or `TILE_BOSS` battle |
| Options shown | 3 |
| Free rerolls | 1 per stage, plus the Draft Token consumable (+1 immediately on purchase, greyed out at the cap — `03` §7.1, ruled in `16` A7). Accumulates up to 3 |
| Ad reroll | `AD_REROLL_PERK`, 2 per run |
| Ad 4th option | `AD_EXTRA_PERK_CHOICE`, 1 per run — shows a 4th option drawn from a rarity-upgraded pool |
| Skip | Allowed. Skipping grants 60 Gold and +1 free reroll. |
| Max perks per run | Unlimited (a full run yields ~18–22) |

🔒 **The run opens on a draft.** Before the first roll, the player is offered three options on exactly the draft above — same pool, same guarantees, same skip and reroll economy — and no other command is legal until they answer. It is the same draft and not a second mechanic, because a second kind of perk choice would be a second set of rules to learn.

It draws against **stage 1** and names no battle tile: `Rules.Perks.CurrentDraft` asks the tile kind only whether it is Elite or Boss, and the opening draft is neither, so it draws the ordinary stage-1 band. A run at the trailhead has fought nothing, and reading the draft as anything above stage 1 would hand out the strongest offer first.

⚠️ What it offers follows from §2's category gate rather than from any rule of its own: a run owning nothing satisfies no perk's prerequisites, so the draftable pool is exactly the nine bases. **The opening draft is the choice of which element the run is about.**

⚠️ The draft blocks `ABANDON_RUN` as it blocks everything else — a player who opens a run must answer its draft (skipping is one of the three answers) before they can leave it. That is the pre-existing behaviour of every draft, not a rule the opening one adds.

### 1.1 Perk tiers within a run

Every perk has **3 internal tiers**. Drafting a perk you already own upgrades it rather than duplicating it.

```
Tier I   → base effect
Tier II  → effect × 1.8
Tier III → effect × 2.8, plus a small bonus clause
```

Owned-perk upgrades appear in the draft with a distinct gold border and "UPGRADE" tag. This creates the core draft tension: **go tall** (upgrade what you have) or **go wide** (new synergies).

Once a perk is at Tier III it is removed from that run's draft pool.

---

## 2. Perk categories (9)

| Category | Colour | Count | Role |
|---|---|---|---|
| **Lightning** | violet | 7 | Chains and repeat strikes. The one element with no ailment of its own — its damage is hit COUNT. |
| **Cold** | ice cyan | 7 | `CHILL` degrades what an enemy deals; `FREEZE` stops it acting. |
| **Fire** | ember red | 9 | `BURN` — damage over time on a capped stack that expires. |
| **Poison** | toxic green | 8 | `POISON` — damage over time that stacks without a cap and never expires. |
| **Bleed** | crimson | 9 | `BLEED` — a capped, expiring stack that scales on missing health and that a perk can SPEND. |
| **Defense** | steel teal | 7 | Damage reduction, dodge, health, counters and shields. |
| **Offense** | magenta | 6 | Raw damage, attack speed and extra swings. |
| **Crit** | gold-green | 6 | Crit chance, crit damage, and what a crit sets off. |
| **Sustain** | rose | 8 | Healing, lifesteal and staying up. |

**Total: 67 standard.** 📐 TUNABLE

🔒 **Every category is entered through exactly one base perk.** Each category authors one perk with no prerequisites — its base — and every other perk in that category requires it; a hybrid requires the base of every category it draws on. A run that has not taken `PK_IGNITE` is never offered a Burn upgrade, because an offer a player can take and feel nothing from is worse than no offer at all. The draft enforces it by narrowing the draftable pool, the same way every §4 composition rule is enforced, so an unsatisfiable narrowing falls through instead of emptying a draft. The bases draw in the **Common** band: §4's `RarityWeights` are keyed on four bands, and a fifth would be a weight table nobody wrote.

🔒 **The five elements are five stacking rules, not five flavours.** That is what keeps them apart and what a build commits to. Ailment damage is a percentage of the applier's ATK across all three damage-over-time statuses (`05` §5), so a poison build gains from the same items an attack build does.

The five elements carry 40 of the 67 rows and the four generic categories 27, deliberately: defence, offence, crit and sustain are what every build wants, so a generic pool as wide as an elemental one would crowd out the identity the draft exists to build towards.

Plus a **hidden category: Cursed Perks (bonus, not counted above).** These offer an oversized benefit with a real drawback and only appear from `TILE_CURSE` and certain events, never in the standard draft.

---

## 3. Perk pool — the shipped catalogue

Format: `ID | Name | Rarity | Requires | Tier I effect`. This table is a reading of `game-data/content/perks/perks.json`, which is the catalogue itself — every row there authors three tiers of Effect DSL and there is no per-perk code. **Requires** is the gate: `—` marks the category's base.

### 3.1 Lightning (7)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_STATIC_CHARGE` | Static Charge | Common | — | Attacks arc to another enemy for {value}% of ATK. |
| `PK_FORKED_BOLT` | Forked Bolt | Rare | `PK_STATIC_CHARGE` | The arc forks: every enemy but your target takes {value}% of ATK. |
| `PK_CONDUCTION` | Conduction | Rare | `PK_STATIC_CHARGE` | The bolt loops back: your target is struck a second time for {value}% of ATK. |
| `PK_RECOIL_ARC` | Recoil Arc | Epic | `PK_STATIC_CHARGE` | Every {everyNth}th attack detonates the charge for {value}% of ATK to every enemy. |
| `PK_THUNDERCLAP` | Thunderclap | Epic | `PK_STATIC_CHARGE` | +{value}% damage while three or more enemies stand. |
| `PK_STORM_HERALD` | Storm Herald | Legendary | `PK_STATIC_CHARGE` | Every attack arcs to every enemy for {value}% of ATK, and each enemy present feeds the storm. |
| `PK_ARC_OVERLOAD` | Arc Overload | Epic | `PK_STATIC_CHARGE`, `PK_KEEN_EYE` | A crit overloads the arc: {value}% of ATK to every enemy. |

### 3.2 Cold (7)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_FROSTBITE` | Frostbite | Common | — | Attacks Chill: −{value}% enemy ATK for {duration}s. |
| `PK_DEEP_CHILL` | Deep Chill | Rare | `PK_FROSTBITE` | A second, deeper Chill: −{value}% enemy ATK for {duration}s. |
| `PK_BRITTLE` | Brittle | Rare | `PK_FROSTBITE` | Cold makes them fragile: enemies you strike take +{value}% damage for {duration}s. |
| `PK_FLASH_FREEZE` | Flash Freeze | Epic | `PK_FROSTBITE` | Every {everyNth}th attack Freezes the target for {duration}s. |
| `PK_SHATTER` | Shatter | Epic | `PK_FROSTBITE` | A frozen corpse bursts: killing an enemy deals {value}% of its Max HP to every other. |
| `PK_ABSOLUTE_ZERO` | Absolute Zero | Legendary | `PK_FROSTBITE` | The whole field freezes over: −{value}% ATK to every enemy for {duration}s, and cold cannot touch you. |
| `PK_STORM_FROST` | Storm Frost | Epic | `PK_STATIC_CHARGE`, `PK_FROSTBITE` | The arc carries the cold: every enemy is Chilled for −{value}% ATK over {duration}s. |

### 3.3 Fire (9)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_IGNITE` | Ignite | Common | — | Attacks Burn for {value}% ATK per second over {duration}s. |
| `PK_KINDLING` | Kindling | Rare | `PK_IGNITE` | The fire takes deeper hold: Burn stacks to {value} instead of 5. |
| `PK_FAN_THE_FLAMES` | Fan the Flames | Rare | `PK_IGNITE` | +{value}% potency on every ailment you inflict. |
| `PK_COMBUSTION` | Combustion | Epic | `PK_IGNITE` | Every {everyNth}th attack detonates the fire for {value}x ATK and relights it. |
| `PK_WILDFIRE` | Wildfire | Epic | `PK_IGNITE` | Fire spreads: every enemy Burns for {value}% ATK per second over {duration}s. |
| `PK_INFERNAL_CORE` | Infernal Core | Legendary | `PK_IGNITE` | The fire never goes out: Burn for {value}% ATK per second, stacking to {cap}, and it lasts the battle. |
| `PK_THERMAL_SHOCK` | Thermal Shock | Epic | `PK_IGNITE`, `PK_FROSTBITE` | Fire into frost cracks them open: {value}x ATK, ignoring armour, every {everyNth}th attack. |
| `PK_FLAME_TEMPO` | Flame Tempo | Epic | `PK_IGNITE`, `PK_MIGHT` | The fire keeps your rhythm: +{value}% Attack Speed and Burn on every swing. |
| `PK_ELEMENTALIST` | Elementalist | Legendary | `PK_STATIC_CHARGE`, `PK_FROSTBITE`, `PK_IGNITE` | Three elements answer at once: +{value}% ailment potency, and every enemy Burns, Chills and rots. |

### 3.4 Poison (8)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_VENOM` | Venom | Common | — | Attacks Poison for {value}% ATK per second. It stacks without limit and never wears off. |
| `PK_VIRULENCE` | Virulence | Rare | `PK_VENOM` | A second dose on every swing: {value}% ATK per second more Poison. |
| `PK_TOXIC_BUILDUP` | Toxic Buildup | Rare | `PK_VENOM` | Every {everyNth}th attack floods them: {value}% ATK per second of extra Poison. |
| `PK_CORROSION` | Corrosion | Rare | `PK_VENOM` | The toxin eats armour: −{value}% enemy DEF for {duration}s, stacking. |
| `PK_PLAGUE` | Plague | Epic | `PK_VENOM` | A death spreads the blight: every other enemy takes {value}% ATK per second of Poison. |
| `PK_LINGERING_MIASMA` | Lingering Miasma | Epic | `PK_VENOM` | The air itself is poison: every {interval}s all enemies take {value}% ATK per second more. |
| `PK_ENDLESS_BLIGHT` | Endless Blight | Legendary | `PK_VENOM` | The blight compounds: {value}% ATK per second of Poison per battle already won this run. |
| `PK_NAPALM_BLOOM` | Napalm Bloom | Epic | `PK_IGNITE`, `PK_VENOM` | What the fire leaves behind festers: {value}% ATK per second of Poison on every burning enemy. |

### 3.5 Bleed (9)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_LACERATE` | Lacerate | Common | — | Attacks Bleed for {value}% ATK per second over {duration}s, worse the more hurt they are. |
| `PK_GASH` | Gash | Rare | `PK_LACERATE` | The wounds pile up: Bleed stacks to {value} instead of 5. |
| `PK_HEMORRHAGE` | Hemorrhage | Rare | `PK_LACERATE` | Blood loss compounds: {value}% ATK per second more Bleed on every strike. |
| `PK_REND` | Rend | Rare | `PK_LACERATE` | Every {everyNth}th attack tears the wound open for {value}x ATK and spends the Bleed. |
| `PK_BLOOD_FRENZY` | Blood Frenzy | Epic | `PK_LACERATE` | The smell of blood quickens you: +{value}% Attack Speed for {duration}s, stacking. |
| `PK_CRIMSON_HARVEST` | Crimson Harvest | Epic | `PK_LACERATE` | A kill throws the wound onto the next: {value}% ATK per second of Bleed on every other enemy. |
| `PK_BLOOD_DEBT` | Blood Debt | Legendary | `PK_LACERATE` | The wound deepens as they weaken: {value}% ATK per second of Bleed for every 10% health they have lost. |
| `PK_ENVENOMED_WOUNDS` | Envenomed Wounds | Epic | `PK_VENOM`, `PK_LACERATE` | The blade is dirty: every strike also Poisons for {value}% ATK per second. |
| `PK_BUTCHER` | Butcher | Epic | `PK_LACERATE`, `PK_KEEN_EYE` | A crit opens them up: {value}% ATK per second of Bleed over {duration}s. |

### 3.6 Defense (7)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_IRONHIDE` | Ironhide | Common | — | −{value}% damage taken. |
| `PK_EVASION` | Evasion | Rare | `PK_IRONHIDE` | +{value}% dodge chance. |
| `PK_VITALITY` | Vitality | Rare | `PK_IRONHIDE` | +{value}% Max HP. |
| `PK_RIPOSTE` | Riposte | Epic | `PK_IRONHIDE` | Being hit answers back: {value}% of the blow returned, and the attacker is stunned. |
| `PK_AEGIS` | Aegis | Epic | `PK_IRONHIDE` | A shield of {value}% Max HP every {interval}s. |
| `PK_BULWARK` | Bulwark | Legendary | `PK_IRONHIDE` | Braced behind the shield: −{value}% damage taken, and a dodge renews the guard. |
| `PK_FROZEN_ARMOUR` | Frozen Armour | Epic | `PK_FROSTBITE`, `PK_IRONHIDE` | Striking you costs them: −{value}% attacker ATK for {duration}s. |

### 3.7 Offense (6)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_MIGHT` | Might | Common | — | +{value}% ATK. |
| `PK_SWIFTNESS` | Swiftness | Rare | `PK_MIGHT` | +{value}% Attack Speed. |
| `PK_MOMENTUM` | Momentum | Rare | `PK_MIGHT` | The swing builds: +{value}% ATK for {duration}s on every landed hit, stacking. |
| `PK_DOUBLE_STRIKE` | Double Strike | Epic | `PK_MIGHT` | Every {everyNth}th attack strikes twice. |
| `PK_ONSLAUGHT` | Onslaught | Legendary | `PK_MIGHT` | Speed becomes force: {value}% of your Attack Speed is converted into ATK. |
| `PK_TURTLE_DOCTRINE` | Turtle Doctrine | Epic | `PK_IRONHIDE`, `PK_MIGHT` | The wall swings back: {value}% of your DEF is converted into ATK. |

### 3.8 Crit (6)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_KEEN_EYE` | Keen Eye | Common | — | +{value}% crit chance. |
| `PK_DEADLY_PRECISION` | Deadly Precision | Rare | `PK_KEEN_EYE` | +{value}% crit damage. |
| `PK_KILLER_INSTINCT` | Killer Instinct | Rare | `PK_KEEN_EYE` | A crit quickens you: +{value}% Attack Speed for {duration}s, stacking. |
| `PK_OVERWHELM` | Overwhelm | Epic | `PK_KEEN_EYE` | +{value}% armour penetration — crits land on bare skin. |
| `PK_CASCADE` | Cascade | Epic | `PK_KEEN_EYE` | A crit guarantees the next {value} attacks crit as well. |
| `PK_PERFECT_STRIKE` | Perfect Strike | Legendary | `PK_KEEN_EYE` | Precision beyond the cap becomes force: {value}% of your crit chance is converted into crit damage. |

### 3.9 Sustain (8)

| ID | Name | Rarity | Requires | Effect (Tier I) |
|---|---|---|---|---|
| `PK_REGENERATION` | Regeneration | Common | — | Heal {value}% Max HP every {interval}s. |
| `PK_LIFESTEAL` | Lifesteal | Rare | `PK_REGENERATION` | Heal {value}% of the damage you deal. |
| `PK_SECOND_WIND` | Second Wind | Rare | `PK_REGENERATION` | The first time you fall below half health, heal {value}% Max HP. |
| `PK_OVERFLOW` | Overflow | Epic | `PK_REGENERATION` | Healing past full becomes a shield, up to {sourceCapPct}% Max HP. |
| `PK_UNDYING` | Undying | Legendary | `PK_REGENERATION` | Once a battle, survive a killing blow and come back with {value}% Max HP. |
| `PK_PARASITIC_TOXIN` | Parasitic Toxin | Epic | `PK_VENOM`, `PK_REGENERATION` | The toxin feeds you: heal {value}% Max HP every {interval}s while anything is poisoned. |
| `PK_SANGUINE_EDGE` | Sanguine Edge | Epic | `PK_KEEN_EYE`, `PK_REGENERATION` | Crits drink deep: heal {value}% of the damage a crit deals. |
| `PK_GALVANIC_WARD` | Galvanic Ward | Epic | `PK_STATIC_CHARGE`, `PK_REGENERATION` | Each arc leaves a charge behind: a shield of {value}% Max HP on every attack. |

### 3.10 Hybrids

A hybrid is any row whose **Requires** names more than one base; they are listed above under one of their categories rather than in a section of their own. The category key is single-valued because §4's diversity rule counts categories per draft, and a perk counted under two would satisfy a diversity narrowing by itself.
### 3.11 Cursed Perks (8, non-draft)

| ID | Name | Benefit | Cost |
|---|---|---|---|
| `CP_BLOOD_PRICE` | Blood Price | +45% ATK | Lose 3% Max HP after every battle |
| `CP_BRITTLE` | Brittle Fury | +60% Crit Damage | −40% DEF |
| `CP_HUNGER` | Endless Hunger | +25% Lifesteal | −2% Max HP per tile moved |
| `CP_MYOPIA` | Myopia | +35% all stats | Cannot see upcoming tiles |
| `CP_LEADEN` | Leaden Die | +50% Max HP | −1 to every roll (min 1) ⚠️ was *All Pip faces −1*; a plain die has no face kinds (`04` §1), and the effect is unchanged |
| `CP_PAUPER` | Pauper's Bargain | +30% all stats | Gold gain reduced to 0 |
| `CP_GLASS_HEART` | Glass Heart | ×2 all stats | Max HP set to 1 (dodge/block/shields still work) |
| `CP_TIMEBOUND` | Timebound | +80% ATK | Take 2% Max HP damage per second in battle |

✅ `CP_GLASS_HEART`'s interactions are ruled in `18_EFFECT_DSL.md` §9.1: shields, dodge, block and thorns all function; lifesteal and healing do not; `MAX_HP` is set after all multipliers; and shield sizes are re-based off the hero's pre-perk Max HP. It appears only from `TILE_CURSE`, Chapter 5 onward.

---

## 4. Draft pool weighting

```
RarityWeights(stage, isElite, isBoss):
    Stage 1 normal:  Common 62, Rare 30, Epic 7,  Legendary 1
    Stage 2 normal:  Common 48, Rare 36, Epic 13, Legendary 3
    Stage 3 normal:  Common 34, Rare 40, Epic 20, Legendary 6
    Elite battle:    shift one band upward (Commons halved, Legendary ×2)
    Boss battle:     Epic 55, Legendary 45 (no Commons/Rares)
```

Additional rules:
- **No duplicate options** within a single draft of 3.
- **Category diversity:** at least 2 distinct categories among the 3 options.
- **Owned-upgrade bias:** each option has a 30% chance of being drawn from the player's owned-but-not-maxed perks instead of the fresh pool. This makes "going tall" reachable without feeling forced.
- **Legendary pity:** if no Legendary has appeared in **14 consecutive drafts the player picked from**, force one into the 15th. The count restarts whenever such a draft offers a Legendary, forced or not.
- **Anti-brick:** if the player has no Sustain perk by the end of Stage 2, force one Sustain option into the next draft.
- **Quality floor:** 3 consecutive drafts **the player picked from** with no option above Common force a Rare-or-better option into the next draft.
- **Codex bias:** never-drafted perks carry a `×1.35` weight in the fresh-pool draw, capped at 1 bias-selected option per draft.
- **Upgrade famine:** 5 consecutive drafts **the player picked from** with no owned-perk upgrade among the 3 options — counted only while a non-maxed owned perk exists — force one.

🔒 The last five rules above are the `DRAFT` source class and are specified in full in **`24_LUCK_PROTECTION.md` §4.7**, which is the authority. They resolve through `LuckService`, not in the draft code.

🔒 **The unit those three counting rules count is a draft the player *picked from*.** Legendary pity, the quality floor and the upgrade famine advance only when a draft is closed by taking one of its options. A **skip** and a **reroll** (§1) both leave all three standing, and for different reasons: a skip takes no option, so there is no pick to count; a reroll is *bought*, and a counter a purchase could advance would put the guarantee itself up for sale — the shape `24_LUCK_PROTECTION.md` §1.2 exists to forbid. The accepted cost is that a player who skips meets each guarantee later; these rules protect the quality of the choices a player actually makes, not the number of battles they win. Ruled 2026-08-18.

📐 TUNABLE: all weights above.

---

## 5. Perk data schema

```json
{
  "id": "PK_EXECUTIONER",
  "name": "Executioner",
  "category": "OFFENSE",
  "rarity": "RARE",
  "iconId": "icon_perk_executioner",
  "description": "+{value}% damage to enemies below 30% health.",
  "tiers": [
    { "tier": 1, "effects": [{"stat":"CONDITIONAL_DMG_PCT","value":0.25,"condition":"TARGET_HP_BELOW_30"}] },
    { "tier": 2, "effects": [{"stat":"CONDITIONAL_DMG_PCT","value":0.45,"condition":"TARGET_HP_BELOW_30"}] },
    { "tier": 3, "effects": [{"stat":"CONDITIONAL_DMG_PCT","value":0.70,"condition":"TARGET_HP_BELOW_35"}] }
  ],
  "excludes": [],
  "requires": [],
  "poolTags": ["standard"]
}
```

The `effects` array is interpreted by a single generic effect resolver. **Do not write per-perk code.** Any perk that cannot be expressed in the effect DSL must extend the DSL, not bypass it. This is what makes 98 perks (and later 300) maintainable.

✅ **The full DSL — 42 operations, 23 triggers, 22 conditions (after the `16` A7 batch extension, `18` §10.1's E6 extension and D41's removals), resolution order and worked examples — is specified in `18_EFFECT_DSL.md`.** That document also resolves the two ambiguous perk designs previously flagged here: `CP_GLASS_HEART` (§9.1) and `PET_DICEBEAST` (§9.2).

---

## 6. Perk Codex

All perks the player has ever drafted are recorded in a Codex. Discovering a perk for the first time grants a small permanent bonus (+0.1% all stats per unique perk discovered, max +9.8% at full completion). This gives long-term value to variety and rewards experimentation over always taking the safe pick.

### 6.1 Codex mastery — all sections 🔒

Previously only the perk bonus was specified. The full Codex has five sections; each entry discovered grants a permanent all-stats bonus, and overall completion pays the 10 Talent Points referenced in `09` §2.

| Section | Entries | Bonus per entry | Section max |
|---|---|---|---|
| Perks | 98 | +0.10% | +9.8% |
| Enemies (bestiary — visual variants catalogued by defeating one) | 64 | +0.05% | +3.2% |
| Gear (base item × rarity discovered) | 120 | +0.02% | +2.4% |
| Pets | 24 | +0.05% | +1.2% |
| Mounts | 12 | +0.05% | +0.6% |

**Full-Codex total: +17.2% all stats.** 📐 TUNABLE. The passive stat bonuses **activate at Legend Level 100** (`07` §1.1 — "Codex mastery bonuses" is that unlock); entries and completion percentages are tracked and visible from the start.

**Talent Point milestones (the 10 TP in `09` §2):** +2 TP at **25% / 50% / 75% / 90% / 100%** overall Codex completion. Claimable at any level — only the passive stat bonuses are gated behind Legend 100.
