# 06 — Perks (In-Run Draft Pool)

The perk draft is the game's primary decision. Everything else — dice, gear, talents — sets the stage; the draft is where the player actually plays.

🔒 Perks are **run-scoped**. They vanish when the run ends. This is what keeps every run fresh despite a heavily linear meta.

---

## 1. Draft mechanics

| Property | Value |
|---|---|
| Trigger | After winning any `TILE_ENEMY`, `TILE_ELITE` or `TILE_BOSS` battle |
| Options shown | 3 |
| Free rerolls | 1 per stage, plus the Draft Token consumable (+1 immediately on purchase, greyed out at the cap — `03` §7.1, ruled in `16` A7). Accumulates up to 3 |
| Ad reroll | `AD_REROLL_PERK`, 2 per run |
| Ad 4th option | `AD_EXTRA_PERK_CHOICE`, 1 per run — shows a 4th option drawn from a rarity-upgraded pool |
| Skip | Allowed. Skipping grants 60 Gold and +1 free reroll. |
| Max perks per run | Unlimited (a full run yields ~18–22) |

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

## 2. Perk categories (6)

| Category | Colour | Count in v1 | Role |
|---|---|---|---|
| **Offense** | red | 22 | Raw damage, crit, attack speed |
| **Defense** | blue | 18 | HP, armor, dodge, block, shields |
| **Sustain** | green | 12 | Lifesteal, regen, heals, revive-adjacent |
| **Dice & Board** | gold | 12 | Die faces, rerolls, movement, tile outcomes |
| **Economy** | purple | 10 | Gold, drops, shop discounts, treasure |
| **Trigger / Synergy** | orange | 16 | Conditional effects that reward specific builds |

**Total: 90 standard.** 📐 TUNABLE

Plus a **7th hidden category: Cursed Perks (bonus, not counted in 90).** These offer an oversized benefit with a real drawback and only appear from `TILE_CURSE` and certain events, never in the standard draft.

---

## 3. Perk pool — v1 catalogue

Format: `ID | Name | Rarity | Tier I effect`

### 3.1 Offense (22)

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_SHARP_EDGE` | Sharp Edge | Common | +12% ATK |
| `PK_QUICK_HANDS` | Quick Hands | Common | +10% Attack Speed |
| `PK_KEEN_EYE` | Keen Eye | Common | +6% Crit Chance |
| `PK_HEAVY_SWING` | Heavy Swing | Common | +20% Crit Damage |
| `PK_PIERCING` | Piercing Strikes | Common | +10% Armor Penetration |
| `PK_BRUTALITY` | Brutality | Common | +8% damage vs enemies above 70% HP |
| `PK_EXECUTIONER` | Executioner | Rare | +25% damage vs enemies below 30% HP |
| `PK_FLURRY` | Flurry | Rare | Every 5th attack hits twice |
| `PK_OVERPOWER` | Overpower | Rare | +18% ATK, −8% Attack Speed |
| `PK_CRIT_CASCADE` | Crit Cascade | Rare | Crits grant +8% Crit Chance for 4 s, stacks 3× |
| `PK_RUPTURE` | Rupture | Rare | Attacks apply `BLEED` (4% ATK/s, 4 s) |
| `PK_IGNITE` | Ignite | Rare | Crits apply `BURN` (8% ATK/s, 3 s) |
| `PK_GIANT_SLAYER` | Giant Slayer | Rare | +30% damage vs Elites and Bosses |
| `PK_MOMENTUM_ATK` | Warpath | Rare | +4% ATK per enemy killed this battle (resets each battle) |
| `PK_CLEAVE` | Cleave | Epic | Attacks hit all enemies for 40% damage |
| `PK_DEATHMARK` | Deathmark | Epic | Every 8 s, mark an enemy: it takes +30% damage |
| `PK_BERSERK` | Berserker's Pact | Epic | +1% ATK per 1% missing HP, up to +45% |
| `PK_TWIN_STRIKE` | Twin Strike | Epic | 25% chance to attack twice |
| `PK_SUNDERING` | Sundering Blows | Epic | Attacks apply `SUNDER` (−6% DEF, stacks 5) |
| `PK_APEX` | Apex Predator | Legendary | Crits reduce all cooldowns by 0.5 s and refresh `RAGE` |
| `PK_ANNIHILATE` | Annihilation | Legendary | ×1.35 multiplicative damage. Cannot be upgraded past Tier II. |
| `PK_CHAIN_DEATH` | Chain of Ruin | Legendary | Killing an enemy deals 20% of its Max HP to all others |

### 3.2 Defense (18)

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_TOUGH_HIDE` | Tough Hide | Common | +15% Max HP |
| `PK_IRON_SKIN` | Iron Skin | Common | +18% DEF |
| `PK_NIMBLE` | Nimble | Common | +5% Dodge |
| `PK_BULWARK` | Bulwark | Common | +8% Block |
| `PK_STOIC` | Stoic | Common | +6% Damage Reduction |
| `PK_THORNS` | Thornmail | Common | Reflect 10% of damage taken |
| `PK_SECOND_SKIN` | Second Skin | Rare | +22% Max HP, +10% DEF |
| `PK_WARDED` | Warded | Rare | Start each battle with a shield = 12% Max HP |
| `PK_EVASIVE` | Evasive Step | Rare | Dodging grants +25% ASPD for 2 s |
| `PK_STALWART` | Stalwart | Rare | −20% damage taken from Elites and Bosses |
| `PK_ANCHOR` | Anchor | Rare | +30% DEF, −10% Attack Speed |
| `PK_REACTIVE` | Reactive Plating | Rare | Being hit grants +4% DEF for 5 s, stacks 6× |
| `PK_IMMOVABLE` | Immovable | Epic | Immune to `STUN` and `FREEZE` |
| `PK_AEGIS` | Aegis | Epic | Every 12 s gain a shield = 18% Max HP |
| `PK_LAST_STAND` | Last Stand | Epic | Below 25% HP: +40% DEF and +25% Damage Reduction |
| `PK_MIRROR` | Mirror Ward | Epic | Reflect 30% of damage taken; +10% Max HP |
| `PK_UNBREAKABLE` | Unbreakable | Legendary | Once per battle, survive a lethal hit at 1 HP and gain a shield = 25% Max HP |
| `PK_FORTRESS` | Living Fortress | Legendary | ×1.30 multiplicative Max HP and DEF; −15% Attack Speed |

### 3.3 Sustain (12)

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_LEECH` | Leeching Strikes | Common | +6% Lifesteal |
| `PK_REGEN` | Slow Regeneration | Common | Heal 1% Max HP/s in battle |
| `PK_VITAL_SURGE` | Vital Surge | Common | Heal 8% Max HP after each battle |
| `PK_BLOODLETTER` | Bloodletter | Rare | +10% Lifesteal, +8% ATK |
| `PK_FEAST` | Feast | Rare | Killing an enemy heals 6% Max HP |
| `PK_HEALERS_TOUCH` | Healer's Touch | Rare | +35% Healing Received |
| `PK_SANGUINE` | Sanguine Pact | Rare | Crits heal for 12% of damage dealt |
| `PK_RESTORATION` | Restoration | Rare | Shrines and Campfires heal +50% more |
| `PK_UNDYING` | Undying Will | Epic | At 0 HP, revive once per battle at 30% HP (stacks with the ad revive) |
| `PK_TRANSFUSION` | Transfusion | Epic | Overheal converts into a shield, up to 20% Max HP |
| `PK_PHOENIX` | Phoenix Heart | Legendary | Reviving (any source) also grants 8 s of `RAGE` +50% ATK |
| `PK_ETERNAL` | Eternal Spring | Legendary | Heal 3% Max HP/s; healing also damages the lowest-HP enemy for the same amount |

### 3.4 Dice & Board (12)

See `04_DICE_SYSTEM.md` §5 for the 8 core dice perks. Additional 4:

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_PATHFINDER` | Pathfinder | Common | See the contents of the next 10 tiles instead of 6 |
| `PK_SCOUT` | Scout's Instinct | Rare | Fork previews reveal exact tile contents |
| `PK_LEAPFROG` | Leapfrog | Rare | Landing on `TILE_EMPTY` grants a free extra roll |
| `PK_CARTOGRAPHER` | Cartographer | Epic | Once per stage, choose any tile in the next 6 and move there |

### 3.5 Economy (10)

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_GREED` | Greed | Common | +25% Gold from all sources |
| `PK_HAGGLER` | Haggler | Common | −15% shop prices |
| `PK_SCAVENGER` | Scavenger | Common | +20% Crowns from Treasure tiles |
| `PK_LUCKY_FIND` | Lucky Find | Rare | +15% gear drop chance |
| `PK_PROSPECTOR` | Prospector | Rare | Treasure tiles also grant 2 Enhance Stones |
| `PK_MERCHANT_FRIEND` | Merchant's Friend | Rare | Shops show 5 slots instead of 4 |
| `PK_BOUNTY` | Bounty Hunter | Rare | Elites drop +1 gear item |
| `PK_ALCHEMY` | Alchemy | Epic | Convert Gold to Crowns at run end (10:1, max 500 Crowns) |
| `PK_MIDAS` | Midas Touch | Epic | Every 6th enemy killed drops a Treasure |
| `PK_HOARD` | Dragon's Hoard | Legendary | +1% ATK per 100 Gold currently held |

### 3.6 Trigger / Synergy (16)

These are the build-definers. They are deliberately conditional so they feel like discoveries.

| ID | Name | Rarity | Effect (Tier I) |
|---|---|---|---|
| `PK_GLASS` | Glass Cannon | Rare | +40% ATK, −25% Max HP |
| `PK_TURTLE` | Turtle Doctrine | Rare | Convert 20% of DEF into ATK |
| `PK_JUGGERNAUT` | Juggernaut | Rare | Convert 8% of Max HP into ATK |
| `PK_DUELIST` | Duelist | Rare | +35% damage when exactly one enemy remains |
| `PK_SWARMBANE` | Swarmbane | Rare | +30% damage when 3+ enemies are present |
| `PK_OPENER` | Opening Gambit | Rare | First attack of each battle deals ×3 damage |
| `PK_CLOSER` | Closing Argument | Rare | +50% ATK in the last 15 s of a battle |
| `PK_PACK_LEADER` | Pack Leader | Rare | +25% pet damage; pets gain your crit chance |
| `PK_SYMBIOSIS` | Symbiosis | Epic | Each equipped pet grants +7% to all your stats |
| `PK_ECHO` | Echo Strike | Epic | Every ability and pet ability triggers a second time at 40% power |
| `PK_MOMENTUM_CH` | Snowball | Epic | +2% permanent ATK per battle won this run (no cap) |
| `PK_GAMBLER` | Gambler's Ruin | Epic | 50% chance each attack deals ×2, 50% chance ×0.6 |
| `PK_ARSENAL` | Arsenal | Epic | +3% all stats per distinct perk category you own |
| `PK_PERFECTIONIST` | Perfectionist | Legendary | +60% all stats while at 100% HP |
| `PK_AVATAR` | Avatar of the Die | Legendary | Your current die face count of `Star`/`Surge`/`Fortune` × +12% all stats |
| `PK_SINGULARITY` | Singularity | Legendary | Halve your perk count (round up, you choose which to drop); triple the effect of all remaining perks |

### 3.7 Cursed Perks (8, non-draft)

| ID | Name | Benefit | Cost |
|---|---|---|---|
| `CP_BLOOD_PRICE` | Blood Price | +45% ATK | Lose 3% Max HP after every battle |
| `CP_BRITTLE` | Brittle Fury | +60% Crit Damage | −40% DEF |
| `CP_HUNGER` | Endless Hunger | +25% Lifesteal | −2% Max HP per tile moved |
| `CP_MYOPIA` | Myopia | +35% all stats | Cannot see upcoming tiles |
| `CP_LEADEN` | Leaden Die | +50% Max HP | All Pip faces −1 (min 1) |
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
- **Legendary pity:** if no Legendary has appeared in **14 consecutive drafts the player picked from**, force one into the 15th. The count restarts whenever a draft offers a Legendary, forced or not.
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

✅ **The full DSL — 44 operations, 23 triggers, 23 conditions (after the `16` A7 batch extension and `18` §10.1's E6 extension), resolution order and worked examples — is specified in `18_EFFECT_DSL.md`.** That document also resolves the two ambiguous perk designs previously flagged here: `CP_GLASS_HEART` (§9.1) and `PET_DICEBEAST` (§9.2).

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
