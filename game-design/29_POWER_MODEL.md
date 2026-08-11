# 29 — The Power Model & Expectation Curves

🔒 **Decision D30: `PlayerPower` is a first-class, canonical, data-defined scalar, and every progression expectation in the game is authored against it.**

`02` §4.4 introduced `PlayerPower` as a *display-only aggregate* used for chapter recommendations. That is no longer sufficient. The economy simulator (`21`) needs a single number it can compare against an authored expectation, the game needs it for gating and matchmaking, and the product owner needs it as the dial they actually reason in.

This document defines that number, what feeds it, what deliberately does not, and the three authored tables that express *what it should be* — by content, by level, and by day.

**Who owns what:** the tables in §4, §5 and §6 are **design artefacts, owned by the product owner and edited freely**. The formulas in §2 and §3 are engineering contracts and change rarely.

---

## 1. Two numbers, not one

| Number | What it is | Cost | Used by |
|---|---|---|---|
| **`PlayerPower`** | A closed-form scalar computed from the aggregated stat block against a fixed reference opponent | ~microseconds | UI, chapter gating, the "you may be too weak" warning, PvP candidate selection, the simulator's decisions |
| **`EmpiricalPower`** | The same quantity *measured* by running N real combat simulations against the standard dummy (§2.5.2) and solving for the power that produces the observed time-to-kill and time-to-die | ~2 ms for N=20 | The simulator's reporting, the balance harness, and the calibration check below |

🔒 **Calibration assertion (`21` A10):** across every build archetype and every chapter band, `PlayerPower` must track `EmpiricalPower` within **±12%**. If it drifts outside that, `PlayerPower` is lying to the player and to every system that consumes it, and the closed form in §2 must be corrected — not the tolerance.

This split exists because a number shown on the Hero screen must be instant, but a number the economy is tuned against must be *true*. Keeping both, and asserting they agree, gets you both properties and catches the class of bug where a stat is silently absent from the aggregate.

---

## 2. The `PlayerPower` formula

### 2.1 Why the previous formula is replaced ⚠️

`02` §4.4 specified:

```
PlayerPower = (EffectiveHP * 0.5) + (DPS * 10)          // SUPERSEDED
```

That form is **additive**, which means a build with enormous EffectiveHP and near-zero DPS scores highly. Under a 90-second fight cap (`05` §3) that build loses every fight it enters. A power number that says "you can clear Chapter 6" about a build that cannot kill anything is worse than no power number at all — and the simulator would act on it, choosing chapters the agent then fails, corrupting every downstream curve.

A fight is won when **time-to-kill the enemy is less than time-to-die**. Power must therefore be monotone in *both* survivability and damage, and must go to zero when either does. The geometric mean has exactly that shape:

```
PlayerPower = K_POWER * sqrt(EffectiveHP * DPS)
```

📐 `K_POWER` is a pure calibration constant with no design meaning. It is fixed once, by solving for `PlayerPower = 1000` on the **reference par build** — now an explicit authored statblock in §2.5 — and then left alone. Expected magnitude ≈ 5.3; the harness derives the exact value and writes it into `power_model.json`.

🔒 **Both forms ship.** `data/power_model.json` carries a `model` field (`"geometric"` | `"additive_legacy"`) so the change is reversible without a client patch, per `02` §4.4's own requirement that the formula live in data. `"geometric"` is the default and the one every table in this document is authored against.

### 2.2 The reference opponent 🔒

`EffectiveHP` and `DPS` are meaningless in the abstract, because mitigation depends on the attacker's level (`05` §4) and penetration depends on the target's DEF. `PlayerPower` is therefore always evaluated against a **fixed reference opponent**, defined once in data and never varied by chapter:

```json
{
  "referenceOpponent": {
    "level": 40,
    "atk": 2000,
    "def": 1500,
    "aspd": 1.0,
    "crit": 0.10,
    "critDamage": 0.50
  }
}
```

This makes `PlayerPower` an **absolute, comparable scalar**: the same build scores the same number in Chapter 1 and Chapter 8, on the Hero screen and in the simulator. Comparison against content happens by comparing it to `ParPower` (§4), not by re-evaluating power per chapter.

### 2.3 The components

```
EffectiveHP = MaxHP
            / (1 - MitigationVsReference)
            / (1 - DR)
            / (1 - Dodge)
            / (1 - Block * 0.5)
            * (1 + Lifesteal * LS_WEIGHT)
            * (1 + Regen_per_sec * REGEN_WEIGHT)

  where MitigationVsReference = effDef / (effDef + 120 + 20 * 40)
        effDef                = DEF * (1 - referenceOpponent.pen)     // pen = 0

DPS = ATK
    * ASPD
    * (1 + CRIT * CDMG)
    * (1 + DMG%)
    * (1 - MitigationOfReference)
    * (1 + PetDpsShare)

  where MitigationOfReference uses the hero's PEN against referenceOpponent.def
```

📐 `LS_WEIGHT` (default `2.0`), `REGEN_WEIGHT` (default `8.0`) and `PetDpsShare` weighting all live in `power_model.json`.

**Caps are applied before this evaluation**, per `05` §1.1. A player at the 0.75 crit cap gains nothing from more crit, and `PlayerPower` must reflect that or it will recommend gear that does nothing.

### 2.4 Pets and mounts

Pet **passive auras** are stat modifiers and are already inside the aggregated stat block, so they need no special handling.

Pet **active abilities** are not, and they are a material share of real damage. They enter as `PetDpsShare`:

```
PetDpsShare = Σ over equipped pets of ( AbilityDamagePerCast / EffectiveCooldown ) / HeroDps
```

📐 Computed from the same pet definitions the game uses. Pets with non-damage actives (shields, heals, `FREEZE`) contribute to `EffectiveHP` through a per-ability weight table in `power_model.json` rather than being ignored — a Aegis Owl build is genuinely tankier and the number must say so.

Mount **stat blocks** are in the aggregate. Mount **run perks** (`07` §3.2) are board-layer effects and are excluded — see §3.

### 2.5 Calibration artefacts 🔒 *(ruled in `16` A7 — single source of truth; `05` §9 points here)*

The three inputs the grading stack could not run without: the reference par build (fixes `K_POWER`), the standard dummy (makes `EmpiricalPower` measurable), and the five build-archetype loadouts `05` §9 sweeps. All live in one file:

```
game-data/tuning/calibration_builds.json
```

📐 Every number below. The harness **loads** these; it never synthesises its own.

#### 2.5.1 The reference par build

A Legend-Level-10 hero in solid blue-quality Chapter-1 gear — deliberately *meta-only*: no run perks, no shrine buffs (`PlayerPower` excludes them, §3). Pet auras are already folded into the stat block; the pet's active enters through `PetDpsShare` (§2.4).

```json
{
  "referenceParBuild": {
    "comment": "Defines K_POWER: PlayerPower(this) := 1000 (Chapter 1 Normal par).",
    "level": 10,
    "stats": {
      "maxHp": 1120, "atk": 145, "def": 74, "aspd": 1.05,
      "crit": 0.08, "critDamage": 0.55, "lifesteal": 0.02, "dodge": 0.03,
      "block": 0.0, "pen": 0.02, "dmgPct": 0.05, "drPct": 0.03,
      "healPct": 1.0, "thorns": 0.0
    },
    "pets": ["PET_SPARKLING"],
    "mount": null
  }
}
```

#### 2.5.2 The standard dummy

The dummy **is** the §2.2 reference opponent, promoted to a live actor — same level, ATK, DEF, ASPD, crit. It deals damage, so time-to-die is measurable; the `INVULNERABLE` flag means its HP pool never empties (damage dealt to it is still accumulated for measurement). It is **not** status-immune: control, DoT and debuff builds must measure as what they are.

```json
{
  "standardDummy": {
    "level": 40, "maxHp": 100000, "atk": 2000, "def": 1500, "aspd": 1.0,
    "crit": 0.10, "critDamage": 0.50,
    "lifesteal": 0, "dodge": 0, "block": 0, "pen": 0,
    "dmgPct": 0, "drPct": 0, "healPct": 1.0, "thorns": 0,
    "flags": ["INVULNERABLE"]
  }
}
```

**Measurement protocol (`EmpiricalPower`, §1)** — two passes, `N = 20` seeds each (streams `harness:{n}`), median over runs, all 📐:

| Pass | Setup | Window | Measures |
|---|---|---|---|
| Offense | Dummy ATK set to 0 | 30 s | `DPS_emp` = damage dealt to the dummy per second (attacks, DoTs, pet abilities — everything) |
| Defense | Dummy at full ATK | Until hero death, clamped at 300 s | `TTD` = time of death (300 s if outlived — sustain builds legitimately saturate) |

```
EmpiricalPower = K_POWER * sqrt( (TTD * 2100) * DPS_emp )
```

`2100` = the dummy's pre-mitigation output per second (`2000 × 1.0 × (1 + 0.10 × 0.50)`), so `TTD × 2100` is raw damage absorbed before death — the measured analogue of §2.3's `EffectiveHP`, exactly as `DPS_emp` is the measured analogue of §2.3's `DPS`. A10 (±12%) compares this against the closed form.

#### 2.5.3 The five build-archetype loadouts

The archetypes named in `17` §1 and `11` §8.1 — crit, tank/thorns, DoT, lifesteal, pet-focused — as concrete, harness-reproducible loadouts. Each is: a **stat block** (the shape, authored at the par-1000 scale like §2.5.1, auras folded in), a **pet trio** (actives at catalogue base values, ★0), a **frozen perk set** (12 perks, fixed tiers — used for single-fight sweeps and `EmpiricalPower` per archetype), and a **draft priority list** (used by full-run simulations: the run agent always drafts the first available perk on the list, at the highest offered tier).

**Scaling rule 🔒:** to place a loadout at power `P`, multiply `maxHp`, `atk`, `def` by a single scalar `s` (ratio stats unchanged), solving `PlayerPower = P` by bisection to within 0.1%; round `s` to 4 dp. When targeting content `(c, t)`, the hero's `Level` := `EnemyLevel(c, t)` (`05` §6.0 — the level a par player typically has there); otherwise 40.

| | `ARCH_CRIT` | `ARCH_TANK_THORNS` | `ARCH_DOT` | `ARCH_LIFESTEAL` | `ARCH_PET` |
|---|---|---|---|---|---|
| maxHp | 900 | 1600 | 1050 | 1150 | 1100 |
| atk | 150 | 95 | 130 | 135 | 125 |
| def | 60 | 120 | 70 | 70 | 75 |
| aspd | 1.10 | 0.95 | 1.25 | 1.10 | 1.05 |
| crit | 0.35 | 0.05 | 0.10 | 0.08 | 0.10 |
| critDamage | 1.20 | 0.50 | 0.60 | 0.55 | 0.60 |
| lifesteal | 0 | 0 | 0 | 0.25 | 0.02 |
| dodge | 0.03 | 0.02 | 0.03 | 0.03 | 0.03 |
| block | 0 | 0.20 | 0 | 0 | 0 |
| pen | 0.10 | 0 | 0.15 | 0.05 | 0.05 |
| dmgPct | 0.05 | 0 | 0.10 | 0.05 | 0.05 |
| drPct | 0 | 0.15 | 0.03 | 0.05 | 0.05 |
| healPct | 1.0 | 1.0 | 1.0 | 1.20 | 1.0 |
| thorns | 0 | 0.30 | 0 | 0 | 0 |
| pets | NIPPER, VOIDKITTEN, WISP | SNAILGUARD, THORNBUD, AEGISOWL | EMBERCUB, SPOREMOTHER, WISP | LEECHLING, TOADKING, MOSSLING | STORMFANG, VOIDKITTEN, CLOCKHOUND |

*(Pet IDs carry the `PET_` prefix in data.)*

**Frozen perk sets** (12 each; Roman numeral = tier):

| Archetype | Frozen set |
|---|---|
| `ARCH_CRIT` | KEEN_EYE III, HEAVY_SWING II, CRIT_CASCADE II, TWIN_STRIKE II, SHARP_EDGE II, QUICK_HANDS II, SANGUINE II, EXECUTIONER II, OPENER I, APEX I, TOUGH_HIDE I, DUELIST I |
| `ARCH_TANK_THORNS` | THORNS II, MIRROR II, IRON_SKIN II, TOUGH_HIDE II, BULWARK II, STOIC II, SECOND_SKIN II, REACTIVE II, WARDED II, LAST_STAND I, AEGIS I, ANCHOR I |
| `ARCH_DOT` | RUPTURE II, IGNITE II, SUNDERING II, QUICK_HANDS II, PIERCING II, BRUTALITY II, KEEN_EYE II, SHARP_EDGE II, DEATHMARK I, GIANT_SLAYER I, TOUGH_HIDE I, REGEN I |
| `ARCH_LIFESTEAL` | LEECH II, BLOODLETTER II, FEAST II, SANGUINE II, HEALERS_TOUCH II, SHARP_EDGE II, QUICK_HANDS II, TOUGH_HIDE II, VITAL_SURGE I, TRANSFUSION I, UNDYING I, ETERNAL I |
| `ARCH_PET` | PACK_LEADER II, SYMBIOSIS II, SHARP_EDGE II, TOUGH_HIDE II, KEEN_EYE II, QUICK_HANDS II, SECOND_SKIN II, ECHO I, ARSENAL I, REGEN I, STOIC I, HEAVY_SWING I |

**Draft priority lists** (15 each, first-available wins; all IDs carry the `PK_` prefix in data):

| Archetype | Priority order |
|---|---|
| `ARCH_CRIT` | KEEN_EYE, HEAVY_SWING, CRIT_CASCADE, TWIN_STRIKE, SANGUINE, SHARP_EDGE, QUICK_HANDS, EXECUTIONER, APEX, OPENER, DUELIST, TOUGH_HIDE, PERFECTIONIST, ANNIHILATE, GIANT_SLAYER |
| `ARCH_TANK_THORNS` | THORNS, MIRROR, IRON_SKIN, TOUGH_HIDE, WARDED, SECOND_SKIN, STOIC, BULWARK, REACTIVE, AEGIS, LAST_STAND, ANCHOR, FORTRESS, IMMOVABLE, REGEN |
| `ARCH_DOT` | RUPTURE, IGNITE, SUNDERING, QUICK_HANDS, KEEN_EYE, PIERCING, BRUTALITY, DEATHMARK, SHARP_EDGE, GIANT_SLAYER, TOUGH_HIDE, REGEN, CLEAVE, TWIN_STRIKE, ANNIHILATE |
| `ARCH_LIFESTEAL` | LEECH, BLOODLETTER, FEAST, SANGUINE, HEALERS_TOUCH, SHARP_EDGE, QUICK_HANDS, TOUGH_HIDE, VITAL_SURGE, TRANSFUSION, UNDYING, ETERNAL, PHOENIX, SECOND_SKIN, KEEN_EYE |
| `ARCH_PET` | PACK_LEADER, SYMBIOSIS, ECHO, ARSENAL, SHARP_EDGE, KEEN_EYE, TOUGH_HIDE, QUICK_HANDS, SECOND_SKIN, REGEN, STOIC, HEAVY_SWING, APEX, PERFECTIONIST, FLURRY |

The absolute magnitudes of the stat blocks only anchor the *shape* — the scaling rule always solves for the target power, so editing a shape never silently moves a par. The five loadouts intentionally mirror `11` §8.1's PvP bots (same archetype identities, deeper PvE perk sets), so PvE sweeps, PvP bots and `17` §1's counterplay rule all reason about the same five builds.

---

## 3. What `PlayerPower` deliberately excludes 🔒

`PlayerPower` measures **combat power against a reference opponent**. It is not a measure of account progress, and conflating the two would make it useless for its actual jobs.

| Excluded | Why | Tracked instead as |
|---|---|---|
| Run-scoped **perks** (`06`) | They do not exist outside a run. Including them would make the number unstable and unusable for gating. | Modelled inside `RunModel` (`21` §6) |
| **Die faces**, reroll charges, Nudge (`04`) | Board-layer. They change how much content you reach, not whether you win a fight. | **Utility Index** (§3.1) |
| **Economy** perks and talents — Gold, Crowns, drop rates, Focus (`24` §5) | Income, not power | **Utility Index** |
| **Guild perks** (`27` §5) | Non-combat by locked rule R1. If a guild perk ever showed up in `PlayerPower`, R1 has been violated and an architecture test should already have failed. | **Utility Index** |
| **Energy**, Energy Reserve (`28` C) | Throughput, not power | **Utility Index** |
| **Renown** (`28` D) | Its Talent Points are already counted via the talent aggregate; Renown itself is a score | — |
| **Slay Plus** | Grants no stat, ever (`12` §2). 🔒 **An architecture test must assert that no entitlement field is reachable from the power computation.** | — |

### 3.1 The Utility Index

A second, deliberately cruder scalar covering everything above, so that progression in non-combat systems is still visible and still tunable:

```
UtilityIndex = 100
             * (1 + DieFaceValue)      // sum of per-face weights, Star=1.0 … Pip1=0.05
             * (1 + IncomeMultiplier)  // Crowns/Gold/drop-rate bonuses from all sources
             * (1 + BoardControl)      // rerolls, Nudge, tile preview, portals, Focus
             * (1 + Throughput)        // energy cap, regen, reserve, dungeon entries
```

📐 Entirely in `power_model.json`. The Utility Index is **not shown to the player** — it exists so the simulator can report that a Fortune-branch player is progressing even when their `PlayerPower` is flat, and so §6's expectation curves can have a second dimension.

---

## 4. Table 1 — `ParPower`: expected power to clear content 🔒

**This is the table the user asked for: for each level of content, the internal expected power level for completion.** It is authored, it lives in data, and both the game and the simulator read it.

`ParPower(chapter, tier)` is defined by its **meaning**, not by a formula:

> 🔒 **A player at exactly `ParPower(c, t)` clears `(c, t)` approximately 70% of the time.**

That is the balance guardrail already stated in `05` §9.1, promoted here into an authored, testable table.

### 4.1 The table

```
ParPower(c, t) = ChapterPowerTarget(c) * TierMult(t)

ChapterPowerTarget(c) = 1000 * 2.0^(c-1)
TierMult:  Normal 1.0 · Heroic 4.0 · Mythic 16.0
```

| Chapter | Normal | Heroic | Mythic |
|---|---|---|---|
| 1 | 1,000 | 4,000 | 16,000 |
| 2 | 2,000 | 8,000 | 32,000 |
| 3 | 4,000 | 16,000 | 64,000 |
| 4 | 8,000 | 32,000 | 128,000 |
| 5 | 16,000 | 64,000 | 256,000 |
| 6 | 32,000 | 128,000 | 512,000 |
| 7 | 64,000 | 256,000 | 1,024,000 |
| 8 | 128,000 | 512,000 | **2,048,000** |

Plus the two modes added since:

| Content | Par |
|---|---|
| Resource Dungeon tier `t` (`25` §4.1) | `ChapterPowerTarget(t) * 1.15` |
| Guild Boss tier `t` (`27` §4) | `ChapterPowerTarget(t) * 1.30` — no clear condition, so par means "the median bracket" |

📐 **Every cell is independently editable.** The formula above is the *default fill*, not a constraint. If playtest says Chapter 6 Heroic is a wall, edit that one cell — the file is a table of 24 numbers, not an expression.

### 4.2 How the game uses it

| Consumer | Use |
|---|---|
| Chapter Select (S04) | Soft warning below `0.7 × ParPower` (`02` §4.4) — never a block |
| Difficulty recommendation | Highest tier where `PlayerPower ≥ ParPower` |
| Simulator chapter policy | `21` §7 — the agent picks the highest content it can clear |
| Balance harness | `05` §9.1 — assert the 70% clear rate at exactly par |

### 4.3 The assertion that keeps the table honest

🔒 **`21` A11:** for every `(chapter, tier)`, a build at exactly `ParPower(c,t)` clears between **62% and 78%** of the time in the balance harness. A cell outside that band is either mis-authored or the content behind it has drifted. This is what stops `ParPower` decaying into a number nobody maintains.

---

## 5. Table 2 — `ExpectedPower(L)`: the Legend Level spine

`ParPower` says what content *demands*. This table says what a player at Legend Level `L` should *have* — and the gap between them is the entire pacing design.

```
ExpectedPower(L) = BasePower(L) * GearFactor(L) * TalentFactor(L) * BeastFactor(L)
```

| L | Base | × Gear | × Talents | × Beasts | **Expected** | Nearest par |
|---|---|---|---|---|---|---|
| 1 | 210 | 1.0 | 1.00 | 1.00 | **210** | — |
| 10 | 640 | 1.6 | 1.15 | 1.05 | **1,236** | Ch1 N |
| 20 | 1,180 | 2.4 | 1.35 | 1.15 | **4,395** | Ch1 H / Ch3 N |
| 40 | 2,400 | 4.2 | 1.80 | 1.35 | **24,494** | Ch5 N |
| 60 | 3,700 | 6.5 | 2.30 | 1.55 | **85,746** | Ch7 N |
| 100 | 6,500 | 11.0 | 3.20 | 1.90 | **434,720** | Ch8 H |
| 150 | 10,200 | 16.0 | 4.30 | 2.20 | **1,544,000** | — |
| 200 | 14,000 | 20.0 | 5.50 | 2.50 | **3,850,000** | Ch8 M ×1.9 |

📐 **Every factor column is a hand-authorable curve.** `BasePower(L)` derives from `05` §2 and is not free; the other three are the design's statement about how much of a player's power should come from each system, and they are the most useful dials in the whole game.

⚠️ **All values above are shaped, not derived.** They express intent — *"gear should be the dominant source, talents a steady multiplier, pets and mounts a modest one"* — and they are the first thing the simulator will contradict.

### 5.1 The two properties this table must hold

| # | Property | Assertion |
|---|---|---|
| **P1** | `ExpectedPower(L)` at the Legend Level a player typically reaches when unlocking chapter `c` should sit at **1.0–1.3 ×** `ParPower(c, Normal)`. Below 1.0, players hit a wall on schedule; above 1.3, content is trivial on arrival. | `21` **A12** |
| **P2** | No single factor column may exceed **60%** of the total multiplier at any level. If gear is 80% of power, talents and pets are decoration. | `21` **A13** |

P2 is the one to watch. The current shape has gear at roughly 55–60% of the multiplicative contribution at level 100, which is right at the ceiling and is deliberate — gear *should* be the loot chase — but it leaves no room for the gear curve to grow.

---

## 6. Table 3 — `ExpectedProgression`: power by day, per profile 🔒

**This is the expectation the simulator is graded against.** Tables 1 and 2 are about the game; this one is about the player's *time*.

The product owner authors, for each simulated profile (`21` §5), the `PlayerPower` a player of that profile should have on given days, with a tolerance band.

```json
{
  "profile": "AllAds_Core",
  "checkpoints": [
    { "day": 1,   "expectedPower": 900,       "tolerance": 0.40 },
    { "day": 3,   "expectedPower": 3200,      "tolerance": 0.35 },
    { "day": 7,   "expectedPower": 11000,     "tolerance": 0.30 },
    { "day": 14,  "expectedPower": 38000,     "tolerance": 0.25 },
    { "day": 30,  "expectedPower": 140000,    "tolerance": 0.25 },
    { "day": 60,  "expectedPower": 460000,    "tolerance": 0.30 },
    { "day": 90,  "expectedPower": 950000,    "tolerance": 0.30 },
    { "day": 180, "expectedPower": 2400000,   "tolerance": 0.35 }
  ]
}
```

| Rule | Specification |
|---|---|
| File | `game-data/tuning/expected_progression.json` — one entry per profile |
| Checkpoints | 8 per profile: days 1, 3, 7, 14, 30, 60, 90, 180. Add more freely; the simulator interpolates between them for charting. |
| Tolerance | A fractional band. `0.25` means actual must land within ±25% of expected. Bands widen with time because variance compounds and because late-game behaviour is less predictable. |
| Derivation | **`01` §7's milestone table is the player-facing summary of these checkpoints** (ruled in `16` A7) — if day-7 expected power sits near `ParPower(4, Normal)`, then `01` §7's "Chapter 4 Normal, day 5–7" row follows. Changes are made here first; `01` §7 is rewritten to match, never the reverse. |
| Authority | 🔒 **This file is the product owner's, not engineering's.** It is the statement of intent that everything else is measured against. Changing it is a design decision and should appear in a commit of its own. |

### 6.0 The authored table — all 14 profiles 🔒 (ruled in `16` A7)

This *is* the content of `expected_progression.json`, in the checkpoint shape above. 🔒 The `AllAds_Core` row is the **canonical curve** (`21` A1: Chapter 8 Normal — par 128,000 — falls to it around day 25–45); every other row is derived from it by this section's method: scale by the profile's minutes, ads, skill and engagement, then anchor against the `01` §7 milestone bands and the fairness assertions (A2, A3, A15, E2, E8, E13, E17). All non-canonical rows 📐 — they are the product owner's intent, expected to be revised on simulator evidence (`16` R12).

`ExpectedPower` by checkpoint day:

| Profile | d1 | d3 | d7 | d14 | d30 | d60 | d90 | d180 |
|---|---|---|---|---|---|---|---|---|
| `AllAds_Core` 🔒 | 900 | 3,200 | 11,000 | 38,000 | 140,000 | 460,000 | 950,000 | 2,400,000 |
| `Plus_Core` (= AllAds, A2) | 900 | 3,200 | 11,000 | 38,000 | 140,000 | 460,000 | 950,000 | 2,400,000 |
| `Plus_Lapsed` (lapses d60) | 900 | 3,200 | 11,000 | 38,000 | 140,000 | 460,000 | 800,000 | 1,900,000 |
| `SomeAds_Core` | 800 | 2,700 | 9,000 | 30,000 | 112,000 | 375,000 | 790,000 | 2,050,000 |
| `NoAds_Core` | 700 | 2,200 | 7,000 | 22,500 | 84,000 | 290,000 | 640,000 | 1,750,000 |
| `NoAds_Casual` | 400 | 1,100 | 3,200 | 9,000 | 30,000 | 95,000 | 190,000 | 520,000 |
| `AllAds_Hardcore` | 1,300 | 5,000 | 18,000 | 62,000 | 225,000 | 700,000 | 1,400,000 | 3,300,000 |
| `NoAds_Weekend` | 500 | 1,300 | 4,200 | 12,500 | 42,000 | 140,000 | 300,000 | 850,000 |
| `Lapsed_Returner` | 800 | 2,000 | 5,500 | 16,000 | 58,000 | 200,000 | 430,000 | 1,150,000 |
| `Guildless_Core` | 800 | 2,600 | 8,600 | 28,500 | 105,000 | 350,000 | 730,000 | 1,900,000 |
| `Guilded_Core` | 800 | 2,700 | 9,200 | 31,000 | 117,000 | 395,000 | 830,000 | 2,150,000 |
| `EventSkipper_Core` | 800 | 2,600 | 8,500 | 27,500 | 100,000 | 330,000 | 700,000 | 1,800,000 |
| `DungeonOnly` | 600 | 1,600 | 4,500 | 13,000 | 45,000 | 150,000 | 320,000 | 900,000 |
| `Unlucky_Core` | 700 | 2,200 | 7,200 | 24,000 | 90,000 | 300,000 | 630,000 | 1,700,000 |

**Tolerance ladders** 📐 (per checkpoint d1 → d180):

- **Standard** — `AllAds_Core`, `Plus_Core`, `SomeAds_Core`, `NoAds_Core`, `AllAds_Hardcore`, `Guildless_Core`, `Guilded_Core`, `EventSkipper_Core`: `0.40 / 0.35 / 0.30 / 0.25 / 0.25 / 0.30 / 0.30 / 0.35`
- **Wide** (volatile cadence or seeded tails) — `NoAds_Casual`, `NoAds_Weekend`, `Lapsed_Returner`, `DungeonOnly`, `Unlucky_Core`: `0.50 / 0.45 / 0.40 / 0.35 / 0.35 / 0.40 / 0.40 / 0.45`
- `Plus_Lapsed`: standard through d60, then `0.35 / 0.40` — the lapse makes the tail less predictable.

Built-in consistency checks: `NoAds_Core` : `AllAds_Core` = 60% at day 30 (A3 floor 55% ✓); `Plus_Lapsed` is monotone and flattens to the free slope after d60 (A15 ✓); `Unlucky_Core` : `SomeAds_Core` ≈ 0.80 in power ≈ 1.2× in time (E2 bound 1.35× ✓); `Guildless` and `EventSkipper` sit within their 1.20×/1.25× bounds of the baseline (E17/E13 ✓); `DungeonOnly` reflects E8's deliberately poor dungeon XP.

### 6.1 The report

The simulator's headline output is a single table, and it is the thing to read first:

```
PROFILE          DAY   EXPECTED     ACTUAL       DELTA    BAND    STATUS
AllAds_Core        1        900        1,040     +15.6%   ±40%    ✅
AllAds_Core        7     11,000        7,900     -28.2%   ±30%    ⚠️  outside band
AllAds_Core       30    140,000      131,500      -6.1%   ±25%    ✅
AllAds_Core       90    950,000    1,880,000     +97.9%   ±30%    ❌  outside band
NoAds_Core        30     84,000       58,000     -31.0%   ±25%    ❌  outside band
```

🔒 **`21` A14: every profile must land inside its band at every checkpoint.** This is the assertion the whole tuning loop exists to satisfy, and it is the one the product owner should look at before any other.

### 6.2 Reading a failure

| Symptom | Usual cause | Dial to reach for first |
|---|---|---|
| Early days under, late days over | Onboarding too slow, endgame income compounds | Early drop rates up; late-game sinks up |
| Every day uniformly over | Income too high across the board | `MergeCrownCost`, `BeastFeedCost`, enhancement costs |
| Every day uniformly under | The expectation curve is wrong, not the game | Edit `expected_progression.json` — this is a legitimate outcome |
| One profile out, others fine | A feature-engagement rate is mis-modelled | The profile's `FeatureEngagement` block (`21` §5.2) |
| `NoAds_*` far under | Ad rewards carry too much power | Rebalance ad rewards toward convenience (`12` §1) |
| Day 90+ over on every profile | Dungeons + events + guilds compounding — **risk R10** | `21` §9.5, the ordering rule |

**The third row is important and often forgotten.** The simulator does not prove the game wrong; it shows where the game and the intent disagree. Sometimes the intent was the guess.

---

## 7. Data layout 🔒

Everything in this document is four files, and they are deliberately separated by *who edits them*:

```
game-data/tuning/
├── power_model.json            # §2, §3 — ENGINEERING. Formula weights, reference opponent,
│                               #   pet ability weights, Utility Index weights. Changes rarely.
├── calibration_builds.json     # §2.5 — ENGINEERING+DESIGN. Reference par build, standard
│                               #   dummy, five build-archetype loadouts. The harness's inputs.
├── par_power.json              # §4 — DESIGN. 24 chapter/tier cells + dungeon and guild-boss pars.
│                               #   A flat table of numbers. Edit any cell freely.
└── expected_progression.json   # §6 — PRODUCT OWNER. Per-profile day/power/tolerance checkpoints.
                                #   The statement of intent everything is graded against.
```

`expected_power_by_level.json` (§5) is generated from its four factor curves, which live in `par_power.json` alongside the par table since they are the same kind of decision.

All five are validated at build time against schemas (`14` §6) and are hot-reloadable in dev builds. None requires an app store update to change (`14` §6, `01` §8.6).

---

## 8. Amendments to other documents

| Doc | Change |
|---|---|
| `02` §4.4 | `PlayerPower` formula superseded by §2 here. The additive form is retained in data as `additive_legacy` and is not the default. The "too weak" warning threshold (`0.7 × ParPower`) is unchanged. |
| `05` §9.1 | The 70% clear-rate-at-par guardrail becomes the *definition* of `ParPower` (§4) rather than a separate target, and gains a tolerance band (62–78%) as assertion A11. |
| `09` §8 | The talent power guardrail table is now `TalentFactor(L)` in §5 and must be re-derived for 324 points (**E22**). |
| `11` §4 | PvP candidate selection may use `PlayerPower` for band construction; it must not use `UtilityIndex`. |
| `21` | Rewritten around this document — see `21` §4, §5, §8. |
