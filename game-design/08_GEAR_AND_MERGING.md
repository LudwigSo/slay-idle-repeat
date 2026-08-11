# 08 — Gear, Merging & the Forge

Gear is the loot chase — the reason a run that ends in death still felt worth doing. It must drop often, feel legible, and never require money.

---

## 1. Slots and families

**6 equipment slots**, each with **4 item families**:

| Slot | Families | Primary stat | Visible on hero |
|---|---|---|---|
| **Weapon** | Blade, Axe, Staff, Bow | ATK | ✅ |
| **Helmet** | Hood, Helm, Circlet, Mask | DEF / crit | ✅ |
| **Armor** | Leathers, Plate, Robe, Scalemail | Max HP / DEF | ✅ |
| **Boots** | Treads, Greaves, Slippers, Sandals | ASPD / dodge | ❌ |
| **Ring** | Band, Signet, Loop, Seal | crit / pen | ❌ |
| **Amulet** | Pendant, Talisman, Charm, Idol | mixed / lifesteal | ❌ |

**24 base items**, each existing at **5 rarities** = 120 distinct gear entries.

### 1.1 Family identity

Families are not cosmetic — each biases a build:

| Family axis (weapon / helmet / armor / boots / ring / amulet) | Bias | SS set |
|---|---|---|
| Blade / Hood / Leathers / Treads / Band / Pendant | Balanced, crit-leaning | **Bloodmoon** |
| Axe / Helm / Plate / Greaves / Signet / Talisman | Heavy: ATK + Max HP, lower ASPD | **Ironvow** |
| Staff / Circlet / Robe / Slippers / Loop / Charm | Caster: DoT power, ASPD, low DEF | **Fateweave** |
| Bow / Mask / Scalemail / Sandals / Seal / Idol | Agile: ASPD, dodge, penetration | **Stormcall** |

---

## 2. Rarity

| Rarity | Code | Colour | Stat multiplier | Affix count | Drop share (Ch. 1) |
|---|---|---|---|---|---|
| Common | **C** | grey | ×1.00 | 0 | 60% |
| Rare | **B** | green | ×1.55 | 1 | 27% |
| Epic | **A** | blue | ×2.40 | 2 | 10% |
| Legendary | **S** | gold | ×3.80 | 3 | 2.7% |
| Mythic | **SS** | red-violet | ×6.00 | 4 + set bonus | 0.3% |

Drop shares shift by chapter: later chapters remove C entirely and lift the S/SS shares.

```
DropShare(chapter):
  ch1-2: C60 B27 A10 S2.7 SS0.3
  ch3-4: C40 B36 A18 S5.4 SS0.6
  ch5-6: C15 B40 A32 S11  SS2
  ch7-8: C0  B28 A44 S23  SS5
```

📐 TUNABLE.

---

## 3. Item stat generation

```
ItemPower(chapter, rarity) = ChapterPowerTarget(chapter) * 0.10 * RarityMult(rarity)

q ~ Uniform(0, 1)          // ONE quality scalar per item instance, rolled at generation 📐

PrimaryStat   = ItemPower * SlotPrimaryCoef   * (0.90 + 0.20 * q)
SecondaryStat = ItemPower * SlotSecondaryCoef * (0.85 + 0.30 * q)
```

Quality is **one scalar** `q ∈ [0, 1]`, stored on the instance (§7) — there are no independent per-stat rolls (ruled in `16` A7). The UI quality bar displays `q` directly as 0–100%; Reforge (`24` §6.1) re-rolls `q`, keep-best. Two S-rarity Blades are not identical, and the player can see why — a cheap, high-value source of loot excitement, now computable from one number. The ranges are deliberately asymmetric: the primary spans ×0.90–1.10, the secondary ×0.85–1.15, so high-`q` items lean hardest into their secondary stat.

### 3.0a Slot coefficients 🔒

Previously unspecified. Two kinds of stat: **flat stats** scale with `ItemPower` (and therefore with chapter); **percent stats** scale with rarity only, because they feed capped percentages (`05` §1.1) and must not inflate across chapters.

| Slot | Primary stat | `SlotPrimaryCoef` | Secondary stat | `SlotSecondaryCoef` |
|---|---|---|---|---|
| Weapon | ATK (flat) | 0.20 | Crit Chance (%) | see % table |
| Helmet | DEF (flat) | 0.18 | Max HP (flat) | 0.55 |
| Armor | Max HP (flat) | 1.10 | DEF (flat) | 0.12 |
| Boots | Attack Speed (%) | see % table | Dodge (%) | see % table |
| Ring | Crit Chance (%) | see % table | Armor Pen (%) | see % table |
| Amulet | Max HP (flat) | 0.45 | Lifesteal (%) | see % table |

Percent-stat values by rarity. Quality still applies, via the §3 multipliers: a percent stat in the **primary** position takes ×(0.90 + 0.20 q), in the **secondary** position ×(0.85 + 0.30 q):

| Percent stat | C | B | A | S | SS |
|---|---|---|---|---|---|
| Crit Chance | 1.5% | 2.5% | 4% | 6% | 8% |
| Attack Speed | 2% | 3% | 4.5% | 7% | 10% |
| Dodge | 1% | 1.5% | 2.5% | 4% | 5.5% |
| Armor Pen | 2% | 3.5% | 5% | 8% | 11% |
| Lifesteal | 1.5% | 2.5% | 4% | 6% | 8% |

📐 TUNABLE — all of it, in `data/tuning/drops.json`. Assertion **A13** (`29` §5.1 P2, gear ≤60% of the power multiplier) is what validates these; expect the flat coefficients to move.

### 3.1 Affixes

Rarity B and above roll random affixes from a pool. Affix pools are slot-restricted so nothing nonsensical appears.

| Affix | Range | Slots |
|---|---|---|
| `+X% Crit Chance` | 2–8% | Weapon, Ring, Helmet |
| `+X% Crit Damage` | 10–35% | Weapon, Ring |
| `+X% Attack Speed` | 3–12% | Weapon, Boots |
| `+X% Armor Penetration` | 4–15% | Weapon, Ring |
| `+X% Max HP` | 5–20% | Armor, Helmet, Amulet |
| `+X% DEF` | 5–22% | Armor, Helmet, Boots |
| `+X% Dodge` | 2–8% | Boots, Amulet |
| `+X% Block` | 3–12% | Armor, Helmet |
| `+X% Lifesteal` | 2–9% | Amulet, Weapon |
| `+X% Damage Reduction` | 2–8% | Armor, Amulet |
| `+X% Gold Gain` | 8–30% | Ring, Amulet |
| `+X% Pet Aura Power` | 5–20% | Amulet, Ring |
| `+X Reroll Charge` | 1 | Ring, Amulet (rare, S+ only) |
| `+X% Damage vs Elites` | 8–25% | Weapon, Ring |

### 3.2 Set bonuses (SS only)

**There are exactly 4 sets, one per family axis** (§1.1). Every SS item's set is therefore determined by its family — no separate mapping table is needed, and 4 sets × 6 slots = **24 SS items**, matching the 24 base items exactly.

Wearing 2 / 4 / 6 pieces of a set grants escalating bonuses.

| Set | Family axis | 2-piece | 4-piece | 6-piece |
|---|---|---|---|---|
| **Bloodmoon** | Balanced | +10% Lifesteal | Kills heal 8% Max HP | Lifesteal also applies to pet damage |
| **Ironvow** | Heavy | +15% DEF | −15% damage taken from Elites/Bosses | Once per battle, negate a lethal hit |
| **Fateweave** | Caster | +1 Reroll Charge | `Star` faces grant a free perk draft | One die face of your choice becomes `Star` |
| **Stormcall** | Agile | +10% ASPD | Every 5th attack chains to all enemies | Attack speed also scales pet ability cooldowns |

Because a set maps to a family axis, a full 6-piece set is also a full commitment to one build archetype. That is intentional: mixing axes is the flexible, safe play; committing to one is the high-ceiling play.

---

## 4. The Forge

**Five** operations, all free of real money. Merge, Enhance and Salvage are specified below; **Reforge** (quality re-roll, better-of-two) and **Retune** (affix re-roll with lockable affixes and a wishlist) are specified in `24_LUCK_PROTECTION.md` §6 and are what turn an SS item from a lottery ticket into a platform the player improves.

### 4.1 Merge (fusion)

```
3 × [same item, same rarity, same enhancement level]  →  1 × [same item, next rarity]
```

- The output's quality is the **highest `q` of its inputs** (§3). A Merge-Dust-substituted slot does not count — the max is taken over the real item inputs.
- The output's `chapterOrigin` is the **max `chapterOrigin` of its inputs** (real inputs only), and the output **keeps the shared enhance level** the input rule above already requires. (Ruled in `16` A7 — without the max rule, `ItemPower`'s per-chapter doubling let identical-looking merges legally differ by up to ×16.)
- The output re-rolls affixes at the new rarity's affix count.
- Merging is available from Common → Mythic.
- **Merge Dust** (from salvage) can substitute for **one** of the three inputs at a cost that scales with rarity.

```
DustSubstituteCost(rarity):  C=50, B=200, A=800, S=3200, SS=n/a
```

Cost per merge: Crowns, scaling with output rarity.
```
MergeCrownCost(outRarity):  B=120, A=600, S=3000, SS=15000
```

### 4.2 Enhancement (+levels)

Every item can be enhanced from **+0 to +15**.

```
StatBonusPerLevel = +7% of base item stats (additive)
+15 item = base × 2.05
```

| Level band | Success rate | Cost (Enhance Stones) | Failure result |
|---|---|---|---|
| +0 → +5 | 100% | 2, 3, 4, 6, 8 | — |
| +6 → +10 | 85% → 65% | 12, 16, 22, 30, 40 | No level lost, stones consumed |
| +11 → +15 | 50% → 25% | 55, 75, 100, 140, 200 | No level lost, stones consumed |

🔒 **Enhancement never destroys or downgrades an item.** Destructive enhancement is a monetisation mechanic and has no place in a game with no monetisation. Failure costs materials only.

🔒 **Failure mercy** (`24_LUCK_PROTECTION.md` §4.6): each consecutive failure **on that gear instance** adds `+8` percentage points to the next attempt, resetting on success. The counter lives on the item (so it cannot be farmed on cheap items) and is inherited by merge outputs. `AD_ENHANCE_LUCK` stacks on top and neither advances nor consumes it. Displayed as *"Success 41% (+16% mercy)"*.

The rewarded ad `AD_ENHANCE_LUCK` grants **+15 percentage points** to the next enhancement attempt, **3 times per day**.

🔒 **Exact rule for Slay Plus subscribers:** they receive **3 charges of +15% per day**, auto-applied to their first 3 enhancement attempts of the day, with a visible counter (`Lucky attempts 2/3`). This is byte-for-byte identical to what a full ad-watcher gets — no more, no less. The charges do not accumulate across days.

### 4.3 Salvage

Any item can be salvaged into **Merge Dust** and a fraction of its Enhance Stones back.

```
DustValue(rarity, enhanceLevel) = BaseDust(rarity) * (1 + 0.15 * enhanceLevel)
BaseDust:  C=10, B=40, A=160, S=640, SS=2560
StoneRefund = 60% of stones invested
```

**Auto-salvage filter:** the player sets rules ("salvage all C and B below +3") and the game applies them at run-end. This is a quality-of-life feature, not a paid convenience — implement it in v1.

---

## 5. Inventory

| Property | Value |
|---|---|
| Base capacity | 120 items |
| Expansion | +20 per expansion, up to 400, purchased with **Crowns** (earned currency only) |
| Sorting | By slot, rarity, power, quality, newest |
| Comparison | Tapping an item always shows a side-by-side delta vs the currently equipped item in that slot, with green/red arrows per stat |
| Lock | Items can be locked to exclude them from auto-salvage and merge selection |

**Never** sell inventory space for money.

---

## 6. Gear acquisition sources

| Source | Rate |
|---|---|
| Elite kill | 1 guaranteed item |
| Boss kill | 2–3 items, rarity-boosted |
| Normal enemy | 8% chance 📐 |
| Treasure tile | 25% chance |
| Daily quests | ~2/day |
| Daily login | ~1/day, weekly milestones give S-tier chests |
| PvP season rewards | rarity scales with rank |
| Ad chest (`AD_FREE_GEAR_CHEST`) | 2/day |

Design target: a mid-game player should see **20–35 items per hour of play**, of which 2–5 are meaningful upgrades or merge fodder. High volume, aggressive auto-salvage, occasional real excitement.

🔒 **All gear acquisition routes through `LuckService`** and obeys `24_LUCK_PROTECTION.md`:
- Chests are **stored containers**, opened by explicit command with contents, pity and Focus resolved at open (§4.0–4.0a); they belong to `CHEST_STANDARD` / `CHEST_PREMIUM` / `CHEST_APEX` and carry the ten-chest rarity ladder (§4.1–4.2).
- In-run drops belong to `DROP_RUN` and carry Elite mercy, boss mercy and the session floor (§4.3).
- The **Focus** system (§5) biases *which item* a grant produces toward one player-chosen `(slot, family)`, at `×2.5` weight, free and unlimited.
- **Set Tokens** (§5.1) make the 6-piece SS set a deterministic chase: 12 tokens buy any SS item outright.

Without those, the SS set bonuses in §3.2 are a wall made entirely of luck — six natural SS drops in the right slots, or three *identical* S items per merge.

---

## 7. Data schema

```json
{
  "instanceId": "a7f3...",
  "defId": "GEAR_WEAPON_BLADE",
  "slot": "WEAPON",
  "family": "BLADE",
  "rarity": "S",
  "chapterOrigin": 5,
  "quality": 0.87,
  "enhanceLevel": 9,
  "affixes": [
    {"id":"AFX_CRIT_CHANCE","value":0.061},
    {"id":"AFX_CRIT_DAMAGE","value":0.28},
    {"id":"AFX_PEN","value":0.11}
  ],
  "setId": null,
  "locked": false
}
```

`quality` stores the scalar `q` of §3: the example's `0.87` reads as an 87% quality bar and multipliers ×1.074 primary / ×1.111 secondary. (Ruled in `16` A7 — earlier drafts rolled two independent per-stat ranges that this single stored scalar could not represent.)

Computed stats are **never** stored — they are derived from `defId + rarity + chapterOrigin + quality + enhanceLevel + affixes` at load time. This keeps saves small and lets balance patches re-tune existing items.
