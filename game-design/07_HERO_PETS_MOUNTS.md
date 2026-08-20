# 07 — Hero, Pets & Mounts

🔒 LOCKED: One hero. No roster, no team building, no hero gacha. Pets and mounts are the collection layer.

---

## 1. The Hero

There is exactly one hero: **the Rogue**. The player's identity is expressed through gear, talents, pets, mount and die — not through character selection.

| Property | Detail |
|---|---|
| Name | Player-chosen at first launch (12 chars, profanity-filtered), default "Wanderer" |
| Visual | Chibi hooded adventurer. Equipped weapon, helmet and armor are **visible on the sprite** (see §1.2). |
| Base stats | See `05_COMBAT_SIMULATION.md` §2 |
| Progression | Legend Level 1 → 200 |

🔒 **The name filter is `27` §1's, applied here (M4 kickoff ruling).** This table says only "12 chars,
profanity-filtered" — no language list, no rule about edits, no behaviour on a match. `27` §1 is the
design set's only authored profanity specification (*"Name (16 chars) and a 4-character tag, both
profanity-filtered in EN and DE at creation and on every edit"*), and the hero name is held to that
same standard: **EN and DE, at creation and on every edit.** The hero name keeps its own **12**-character
limit; `27`'s sixteen is a different field's number and is deliberately not borrowed with the rest.
Running two different filters on two name fields in one product is the worse outcome.

The **word lists are content**, not a mechanic: `game-data/content/profanity/{en,de}.json`. What ships
in M4-10 is a deliberately conservative **seed** of twelve terms per language, and **M17** owns the
curated lists — the deferral is held by `ContentCurationRegister` and expires when the files stop
declaring themselves seeds. The **mechanism** (case folding, ligature expansion, diacritic stripping,
substitution folding, substring matching) is code.

### 1.1 Legend Level

The meta level. Gained from **Legend XP**, earned in runs.

```
LegendXpForLevel(L) = 120 * L^1.05      // XP needed to go from level L to L+1
CumulativeXp(L)     = Σ(i=1..L-1) LegendXpForLevel(i)
```

| Level | Cumulative XP | Unlocks |
|---|---|---|
| 1 | 0 | Start |
| 5 | ~1.3k | Pet slot 1, Menagerie screen |
| 8 | ~3.6k | Forge (merge + enhance) |
| 10 | ~5.9k | **PvP Ghost Duel** |
| 15 | ~14.1k | Pet slot 2 |
| 20 | ~25.8k | Mount slot |
| 30 | ~60.3k | Pet slot 3 |
| 60 | ~254.2k | Mythic difficulty tier |
| 100 | ~729.4k | Codex mastery bonuses |
| 200 | ~3.04M | Level cap |

**Each Legend Level grants:** +1 Talent Point, and the base stat increase from the formula in `05` §2. Reaching the level 200 cap requires **199 level-ups** and ~3.04M total Legend XP.

> **Errata (M4-10).** This sentence read *"~3.07M"* and contradicted the table's own level-200 row of
> *"~3.04M"*. The table is right. 3.07M is what you get by summing `LegendXpForLevel(i)` over
> `i = 1..200` — one term too many, because `LegendXpForLevel(200)` is the price of going from 200 to
> 201 and the cap makes that level-up unreachable. Summing the 199 level-ups a player actually makes
> gives 3,036,044, which is the table's figure. **The formula is authoritative and both totals are
> illustrative:** neither is transcribed in code, and `LegendLevelCurveTests` reproduces the
> discrepancy from the formula rather than pinning either number.

📐 TUNABLE: the exponent 1.05 is the single dial controlling long-term pacing. Raising it to 1.15 roughly doubles the total; lowering it to 0.95 roughly halves it.

### 1.2 Gear visualisation

Three gear slots change the hero sprite: **Weapon**, **Helmet**, **Armor**. Each has 4 families × 5 rarities, but the sprite only swaps by **family + rarity colourway** (20 variants per slot), not per individual item. This keeps the art budget sane while preserving the "I look different now" payoff.

Boots, Ring and Amulet are stat-only, no visual.

---

## 2. Pets

### 2.1 Overview

| Property | Value |
|---|---|
| Total pets in v1 | 23 |
| Equipped simultaneously | up to 3 (unlocked at Legend Level 5 / 15 / 30) |
| Roles | Each pet has a **passive aura** (always on) and an **active ability** (on a cooldown during battle) |
| Targetable | ❌ Pets cannot be attacked or killed (see `05` §3.2) |
| Rarity | B / A / S / SS (no C-tier pets) |
| Levels | 1 → 60 |
| Stars | ★1 → ★5 via duplicates |

### 2.2 Pet progression

**Levelling** — each level costs **Beast Feed** (from `TILE_CACHE`, daily quests, ad rewards) and **Crowns** (both in `data/tuning/beasts.json`, ruled in `16` A7):
```
BeastFeedCost(level) = 8 * level^1.3
CrownCost(level)     = 50 * level^1.075     // 📐 50 at level 1→2, ≈4,000 at 59→60 — the `10` §4 endpoints
```
Maxing a pet (level 60) costs ≈ 41,000 Beast Feed and ≈ 114,000 Crowns in total.
Pet level scales its passive aura linearly: `AuraValue = Base * (1 + 0.035 * (level-1))`.

**Ascension (Stars)** — costs duplicates of the same pet.
```
★1 → ★2 : 2 duplicates
★2 → ★3 : 4 duplicates
★3 → ★4 : 8 duplicates
★4 → ★5 : 16 duplicates
```
Each star: `+20%` to the passive aura and `−8%` to the active ability cooldown. ★5 unlocks a bonus clause on the active.

**Acquisition:** Pet Eggs from bosses, `TILE_CACHE`, daily login, PvP season rewards, and the earned-currency shop. **Never purchasable with money.** Egg rarity odds are disclosed in-game.

```
Egg rarity odds:  B 62% · A 28% · S 9% · SS 1%
Pity: guaranteed S or better every 30 eggs; guaranteed SS every 150 eggs.
```

🔒 **`24_LUCK_PROTECTION.md` §4.4 is the authority** for the `EGG_PET` source class and adds three rules on top of the above: **duplicate protection on every pity-forced egg**, the **Beast Mark** mercy-accrual exchange that lets a player buy a *specific* pet outright, and a **newcomer floor** forcing a player's first three eggs to be three distinct pets.

### 2.3 Pet catalogue (24)

| ID | Name | Rarity | Passive aura | Active ability (cooldown) |
|---|---|---|---|---|
| `PET_SPARKLING` | Sparkling | B | +6% ATK | Zap: 80% ATK to one enemy (6 s) |
| `PET_MOSSLING` | Mossling | B | +8% Max HP | Bloom: heal 5% Max HP (10 s) |
| `PET_PEBBLE` | Pebble | B | +10% DEF | Harden: +25% DEF for 4 s (12 s) |
| `PET_WISP` | Wisp | B | +5% Attack Speed | Haste: +30% ASPD for 3 s (10 s) |
| `PET_NIPPER` | Nipper | B | +4% Crit Chance | Snap: guaranteed crit next attack (8 s) |
| `PET_SNAILGUARD` | Snailguard | B | +5% Damage Reduction | Shell: shield 10% Max HP (14 s) |
| `PET_EMBERCUB` | Embercub | A | +9% ATK, applies `BURN` on your crits | Firebreath: 120% ATK to all (9 s) |
| `PET_FROSTKIT` | Frostkit | A | +7% DEF, 10% chance to `FREEZE` on hit | Rime: `FREEZE` all enemies 2 s (14 s) |
| `PET_THORNBUD` | Thornbud | A | +12% Thorns | Lash: reflect ×3 for 5 s (12 s) |
| `PET_GILDBEAK` | Gildbeak | A | +18% Gold | Peck for Coin: next kill drops Treasure (20 s) |
| `PET_SHADEPAW` | Shadepaw | A | +6% Dodge | Vanish: 100% dodge for 2 s (16 s) |
| `PET_LEECHLING` | Leechling | A | +7% Lifesteal | Drain: 100% ATK, heals full amount (10 s) |
| `PET_RUNEMOTH` | Runemoth | A | +5% all stats | Flutter: reset all pet cooldowns (25 s) |
| `PET_TOADKING` | Toadking | A | +14% Max HP, +6% Healing Received | Croak: `WEAKEN` all enemies −20% ATK, 5 s (15 s) |
| `PET_STORMFANG` | Stormfang | S | +14% ATK, +6% Attack Speed | Thunderclap: 200% ATK to all, `STUN` 1 s (12 s) |
| `PET_AEGISOWL` | Aegis Owl | S | +12% DEF, +10% Max HP | Sanctum: shield 25% Max HP + `REGEN` 5 s (16 s) |
| `PET_VOIDKITTEN` | Void Kitten | S | +10% Crit Chance, +25% Crit Damage | Rift: 350% ATK to the lowest-HP enemy (14 s) |
| `PET_GOLDWYRM` | Goldwyrm | S | +30% Gold, +12% gear drop chance | Hoard: instantly gain 250 Gold (20 s) |
| `PET_SPOREMOTHER` | Sporemother | S | Your DoTs deal +40% | Bloomburst: apply `POISON` ×3 to all (12 s) |
| `PET_CLOCKHOUND` | Clockhound | S | −12% all pet cooldowns, +8% ASPD | Rewind: restore 15% Max HP and clear all debuffs (22 s) |
| `PET_SOLARION` | Solarion | SS | +18% ATK, +18% Max HP | Solar Lance: 500% ATK to one enemy, ignores 50% DEF (15 s) |
| `PET_NYXWEAVER` | Nyxweaver | SS | +15% all stats while below 50% HP | Web of Night: enemies take +35% damage for 6 s (18 s) |
| `PET_ARCHIVIST` | The Archivist | SS | Your active perks gain +1 effective tier (max III) | Recall: re-trigger every perk's on-battle-start effect (once per battle) |

📐 TUNABLE: all values.

🔒 **`PET_DICEBEAST` is removed** (`16` D55). Its passive and its *Loaded Fate* active were both dice mechanics, and a flat +8%-all-stats pet at SS rarity is a price tag with no identity behind it. The **catalogue is 23**, not 24, and the id is retired rather than reused. `18` §9.2's ruling on its active retires with it.

---

## 3. Mounts

### 3.1 Overview

| Property | Value |
|---|---|
| Total mounts in v1 | 11 |
| Equipped | 1 (slot unlocked at Legend Level 20) |
| Effect shape | A **stat block** + **one run-level perk** (not a combat ability) |
| Rarity | A / S / SS |
| Levels | 1 → 30, via **Beast Feed** — cost formula and level effect in §3.1a |
| No stars | Mounts do not ascend; duplicates convert to Beast Feed |
| Crate odds | A 70% · S 26% · SS 4% 📐 |
| Pity | Guaranteed S+ every **8** crates, SS every **30**, with duplicate protection — `24_LUCK_PROTECTION.md` §4.5 |

#### 3.1a Mount levelling 🔒 (ruled in `16` A7)

Mounts mirror pets. A mount level scales the mount's **stat block**:

```
StatBlockValue(level) = Base × (1 + 0.035 × (level − 1))     // 🔒 same shape as the pet aura curve
MountFeedCost(level)  = 24 × level^1.3                       // = the pet Beast Feed formula × 3.0 📐
                                                             //   (mountFeedScalar, beasts.json)
```

- The **run perk never scales** with level — it is a fixed effect; only the stat block grows. 📐
- Mount levelling costs **Beast Feed only** — no Crown component. Pets are the double-currency sink; mounts are the pure Feed sink. 📐
- Totals: maxing a mount (level 30, ×2.02 stat block) ≈ **24,000 Beast Feed**; maxing a pet (level 60, ×3.07 aura) ≈ 41,000. A mount is the cheaper, shallower investment by design.

⚠️ Mounts were previously the **only unprotected chase in the game** — 12 mounts, 2,500 Soul Shards per crate, and three SS mounts with run-defining effects. `24` §4.5 closes that.

Mounts are the "run modifier" slot. Where pets shape combat, mounts shape the **run**: board movement, shop access, drop rates. ⚠️ *die faces* was the first item and is gone (`04` §5). This gives the two systems clearly separate identities.

### 3.2 Mount catalogue (12)

| ID | Name | Rarity | Stat block | Run perk |
|---|---|---|---|---|
| `MNT_SADDLEBOAR` | Saddleboar | A | +10% Max HP, +5% DEF | Start each run with a shield = 10% Max HP |
| `MNT_DUSTRUNNER` | Dustrunner | A | +8% ATK, +5% ASPD | +1 node of movement on Pip rolls of 1 or 2 |
| `MNT_PACKMULE` | Pack Mule | A | +6% Max HP | Start each run with 400 Gold |
| `MNT_GLIDEWING` | Glidewing | A | +6% Dodge | `TILE_PORTAL` jumps 2 extra nodes and heals 10% |
| `MNT_STARHOOF` | Starhoof Stag | S | +10% all stats | Your `3` face becomes a `Fortune` face |
| `MNT_IRONSHELL` | Ironshell Tortoise | S | +22% DEF, +15% Max HP, −5% ASPD | Curse tiles have no effect on you |
| `MNT_CINDERMANE` | Cindermane | S | +16% ATK, +10% Crit Damage | Elite tiles drop +1 gear item |
| `MNT_TIDECALLER` | Tidecaller | S | +12% Max HP, +10% Healing Received | Campfires and Shrines can be used twice |
| `MNT_COINWYRM` | Coinwyrm | S | +8% all stats | Shops always show one Epic perk offer |
| `MNT_FATESPINNER` | Fatespinner | SS | +12% all stats | Your `6` face becomes a `Star` face |
| `MNT_WORLDBEARER` | Worldbearer | SS | +20% Max HP, +20% ATK, +20% DEF | Board length +4 nodes (more content per run, more risk) |

📐 TUNABLE.

🔒 **`MNT_VOIDSTEED` is removed** (`16` D55). A mount is a stat block **plus** a run perk, and Voidsteed's whole run perk was reroll charges — leaving an SS mount that is only a stat block, which is the one shape this slot must never be. The **catalogue is 11**, not 12, and the id is retired rather than reused. **Two SS mounts remain** (Fatespinner, Worldbearer), and `24` §4.5's `CRATE_MOUNT` pity is owed a re-check against the narrower SS pool.

---

## 4. Loadout rules

- Equipping and unequipping is **free and unlimited** outside a run. Never gate loadout changes behind currency or timers.
- Loadout **cannot** be changed during a run. It is snapshotted at run start.
- The player may save **3 named loadout presets** (e.g. "Boss push", "Gold farm", "PvP"). Presets store gear + pets + mount + PvP perk set.
- A **PvP loadout** is stored separately from the PvE loadout, so a player never has to re-equip after a duel. See `11_PVP_GHOST_DUEL.md` §3.

---

## 5. Duplicate and disposal rules

Nothing in the collection should ever be dead weight.

| Item | Duplicate handling |
|---|---|
| Pet | Converts to ascension progress. Beyond ★5 → 200 Beast Feed each. |
| Mount | Converts to 300 Beast Feed. |
| Gear | Feeds the merge system (`08_GEAR_AND_MERGING.md`) or salvages to Merge Dust. |

---

## 6. Presentation notes

- **Menagerie screen:** grid of pet portraits, locked ones shown as silhouettes with their rarity frame visible (aspiration). Tapping a pet shows a large idle-animated sprite, its aura, its active, and the star track.
- Pets appear in battle orbiting the hero at a small scale, with a distinct 2-frame idle bob and a one-shot ability animation.
- Mount appears **on the board**, carrying the hero token between tiles. It does not appear in battle. This is a deliberate split: mounts are the board layer, pets are the combat layer, and the player can see that at a glance.

🔒 **The hero does not visually dismount.** The board cuts straight to the battle scene, where the mount is simply absent. Speed beats flourish, and a 0.4 s transition played 20 times per run costs eight seconds of the player's life for no information.
