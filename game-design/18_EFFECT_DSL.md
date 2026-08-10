# 18 — The Effect DSL

Resolves open item P0 #4.

Every perk, talent, gear affix, pet aura, mount bonus, status effect, event outcome, shrine buff, curse and boss mechanic in Slay Idle Repeat is expressed in **one** declarative effect language, interpreted by **one** resolver in `SlayIdleRepeat.Core/Effects`.

🔒 **There is no per-perk, per-talent or per-boss code.** If a design cannot be expressed in this DSL, the DSL is extended — the design is never special-cased. This is what makes 98 perks, 60 talents, 24 pets, 12 mounts and 8 bosses maintainable, and what lets balance be changed by editing JSON rather than shipping a client build.

---

## 1. Anatomy of an effect

```json
{
  "op": "STAT_ADD_PCT",
  "stat": "ATK",
  "value": 0.12,
  "trigger": { "kind": "ALWAYS" },
  "condition": null,
  "target": "SELF",
  "duration": null,
  "stacking": { "mode": "ADDITIVE", "maxStacks": 1 },
  "tags": ["offense"]
}
```

Every effect is the same seven-part shape: **op · trigger · condition · target · value · duration · stacking**. Everything in the game is a combination of those.

---

## 2. Operations (`op`)

### 2.1 Stat operations

| Op | Meaning |
|---|---|
| `STAT_ADD_FLAT` | Add a flat amount to a stat, before percent aggregation |
| `STAT_ADD_PCT` | Add to the additive percent bucket for a stat |
| `STAT_MULT` | Multiply the stat after all additive aggregation (Legendary-tier only) |
| `STAT_SET` | Force a stat to a value (`CP_GLASS_HEART` only) |
| `STAT_CONVERT` | Convert a percentage of stat A into stat B (`PK_TURTLE`, `PK_JUGGERNAUT`) |
| `STAT_CAP_OVERRIDE` | Raise or redirect a stat cap (`Perfect Strike` keystone) |

Valid `stat` values: `MAX_HP · ATK · DEF · ASPD · CRIT · CDMG · LIFESTEAL · DODGE · BLOCK · PEN · DMG_PCT · DR_PCT · HEAL_PCT · THORNS`
Plus the non-combat stats: `GOLD_PCT · CROWNS_PCT · DROP_CHANCE · RARITY_SHIFT · ENERGY_REGEN_PCT · PET_AURA_PCT · REROLL_CHARGES · TILE_PREVIEW · SHOP_PRICE_PCT · XP_PCT · BEAST_FEED_PCT · STONE_PCT`

### 2.2 Damage and healing operations

| Op | Meaning |
|---|---|
| `DAMAGE` | Deal damage. `value` is a multiple of the source's ATK unless `valueMode` says otherwise |
| `DAMAGE_TRUE` | Damage ignoring DEF, DR and mitigation entirely |
| `DAMAGE_MAXHP_PCT` | Damage as a percentage of the target's Max HP |
| `HEAL` | Heal a flat amount or a % of Max HP |
| `HEAL_LEECH` | Heal a % of damage just dealt |
| `SHIELD` | Grant a `WARD` absorb of a given size |
| `REFLECT` | Return a % of incoming damage |

`valueMode`: `ATK_MULT` (default) · `FLAT` · `SELF_MAXHP_PCT` · `TARGET_MAXHP_PCT` · `TARGET_MISSING_HP_PCT` · `DAMAGE_DEALT_PCT`

### 2.3 Status operations

| Op | Meaning |
|---|---|
| `APPLY_STATUS` | Apply one of the 12 statuses from `05` §5 |
| `REMOVE_STATUS` | Clear a status or a tag group |
| `EXTEND_STATUS` | Add duration to an existing status |
| `IMMUNE_STATUS` | Grant immunity to a status for a duration |
| `STATUS_POWER_PCT` | Scale the potency of statuses this actor applies |
| `STATUS_DURATION_PCT` | Scale duration of statuses applied to this actor |

### 2.4 Combat-flow operations

| Op | Meaning |
|---|---|
| `EXTRA_ATTACK` | Perform an additional attack immediately |
| `ATTACK_MULT_NEXT` | Multiply the damage of the next N attacks |
| `FORCE_CRIT_NEXT` | The next N attacks always crit |
| `REDUCE_COOLDOWN` | Reduce pet/boss ability cooldowns |
| `SURVIVE_LETHAL` | Survive an otherwise-fatal hit at a given HP fraction |
| `REVIVE` | Return from 0 HP at a given HP fraction |
| `SUMMON` | Spawn N enemies of an archetype (boss use) |
| `SET_TARGET_PRIORITY` | Adjust targeting weight (added for Sporequeen — `17` §8) |
| `DAMAGE_TAKEN_MULT` | Multiply incoming damage (Rimehold's Core — `17` §6) |

### 2.5 Run and board operations

These never appear in combat; they are resolved by the run controller.

| Op | Meaning |
|---|---|
| `GRANT_CURRENCY` | Gold, Crowns, Soul Shards, Beast Feed, Enhance Stones, Merge Dust, Honor, Energy |
| `GRANT_ITEM` | A gear item at a given rarity band |
| `GRANT_PERK` | A perk, random or specified |
| `UPGRADE_PERK` | Raise an owned perk one tier |
| `MODIFY_DIE_FACE` | Replace a die face (`Weighted Faces`, `The Sixth Star`, `Scramble`) |
| `GRANT_REROLL` | Add reroll charges |
| `MOVE_NODES` | Move the token forward/backward N nodes |
| `REVEAL_TILES` | Extend tile preview range |
| `RESOLVE_TILE_AGAIN` | Re-resolve the current tile at a multiplier |
| `MODIFY_SHOP` | Slot count, price multiplier, forced rarity |
| `MODIFY_DROP_TABLE` | Rarity shift or drop-count bonus |
| `APPLY_CURSE` / `CLEANSE_CURSE` | Run-scoped curse handling |

---

## 3. Triggers

| Trigger kind | Fires when | Parameters |
|---|---|---|
| `ALWAYS` | Passive, always active | — |
| `ON_BATTLE_START` | Once when a battle begins | — |
| `ON_BATTLE_END` | Once when a battle ends | `onlyIfWon` |
| `ON_ATTACK` | Each attack made | `everyNth` |
| `ON_HIT` | Each successful hit landed | `chance` |
| `ON_CRIT` | Each critical hit | `chance` |
| `ON_HIT_TAKEN` | Each hit received | `chance`, `cooldown` |
| `ON_DODGE` / `ON_BLOCK` | On a successful avoid | `cooldown` |
| `ON_KILL` | An enemy dies | — |
| `ON_LOW_HP` | Self HP crosses a threshold downward | `threshold`, `once` |
| `ON_LETHAL` | Would take fatal damage | `once` |
| `ON_HEAL` | Healing is received | — |
| `PERIODIC` | Every N seconds of battle time | `interval`, `startDelay` |
| `ON_PHASE_ENTER` | Boss phase begins | `phase` |
| `ON_TILE_RESOLVED` | A board tile resolves | `tileType` |
| `ON_ROLL` | A die roll completes | `faceKind` |
| `ON_PERK_TAKEN` | A perk is drafted | `category` |
| `ON_STAGE_GATE` | A stage boundary is crossed | — |
| `ON_RUN_START` / `ON_RUN_END` | Run boundaries | — |

---

## 4. Conditions

Conditions gate an effect without changing when it is evaluated. All are pure functions of current state.

```json
{ "all": [
    { "fn": "SELF_HP_PCT", "op": "gte", "value": 1.0 },
    { "fn": "ENEMY_COUNT",  "op": "eq",  "value": 1 }
]}
```

| Function | Returns |
|---|---|
| `SELF_HP_PCT` / `TARGET_HP_PCT` | 0..1 |
| `SELF_MISSING_HP_PCT` | 0..1 |
| `ENEMY_COUNT` | int |
| `TARGET_IS_ELITE` / `TARGET_IS_BOSS` | bool |
| `BATTLE_TIME` | seconds elapsed |
| `BATTLE_TIME_REMAINING_EST` | seconds to the 70 s enrage |
| `HAS_STATUS` | bool, by status id |
| `STATUS_STACKS` | int |
| `PERK_COUNT` | int, optionally by category |
| `DISTINCT_PERK_CATEGORIES` | int |
| `PET_COUNT` | int |
| `DIE_FACE_COUNT` | int, by face kind |
| `GOLD_HELD` | int |
| `BATTLES_WON_THIS_RUN` | int |
| `STAGE_INDEX` | 1..3 |
| `CHAPTER` / `TIER` | int / enum |
| `IS_PVP` | bool — **the hook that lets a perk behave differently in a duel** |

Comparators: `eq · neq · lt · lte · gt · gte · between`. Combinators: `all · any · not`.

---

## 5. Targets

`SELF · CURRENT_TARGET · ALL_ENEMIES · LOWEST_HP_ENEMY · HIGHEST_HP_ENEMY · RANDOM_ENEMY · ALL_PETS · ATTACKER · RUN` (the run itself, for board ops)

---

## 6. Duration and stacking

```json
"duration": { "seconds": 4.0, "scope": "BATTLE" }
```
`scope`: `INSTANT · BATTLE · STAGE · RUN · PERMANENT`

```json
"stacking": { "mode": "ADDITIVE", "maxStacks": 5, "refreshOnReapply": true }
```
`mode`: `ADDITIVE · MULTIPLICATIVE · REPLACE · HIGHEST_WINS · NONE`

---

## 7. Worked examples

### 7.1 A simple perk — `PK_SHARP_EDGE` Tier I
```json
{ "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12,
  "trigger": {"kind":"ALWAYS"}, "target": "SELF" }
```

### 7.2 A conditional perk — `PK_EXECUTIONER` Tier I
```json
{ "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.25,
  "trigger": {"kind":"ALWAYS"}, "target": "SELF",
  "condition": {"fn":"TARGET_HP_PCT","op":"lt","value":0.30} }
```

### 7.3 A triggered perk — `PK_FLURRY` Tier I
```json
{ "op": "EXTRA_ATTACK", "value": 1,
  "trigger": {"kind":"ON_ATTACK","everyNth":5}, "target": "CURRENT_TARGET" }
```

### 7.4 A survival perk — `PK_UNBREAKABLE` Tier I
```json
[
  { "op":"SURVIVE_LETHAL", "value":1, "trigger":{"kind":"ON_LETHAL","once":true} },
  { "op":"SHIELD", "value":0.25, "valueMode":"SELF_MAXHP_PCT",
    "trigger":{"kind":"ON_LETHAL","once":true}, "target":"SELF" }
]
```

### 7.5 A cursed perk with a drawback — `CP_BLOOD_PRICE`
```json
[
  { "op":"STAT_ADD_PCT", "stat":"ATK", "value":0.45, "trigger":{"kind":"ALWAYS"} },
  { "op":"DAMAGE_MAXHP_PCT", "value":0.03, "trigger":{"kind":"ON_BATTLE_END"},
    "target":"SELF", "tags":["drawback"] }
]
```

### 7.6 A talent keystone — `Avatar of War`
```json
[
  { "op":"STAT_MULT", "stat":"ATK", "value":1.20, "trigger":{"kind":"ALWAYS"} },
  { "op":"STAT_CAP_OVERRIDE", "stat":"MAX_HP", "value":0.80,
    "capKind":"HEAL_CEILING", "trigger":{"kind":"ALWAYS"} }
]
```

### 7.7 A pet — `PET_STORMFANG`
```json
{
  "aura": [
    {"op":"STAT_ADD_PCT","stat":"ATK","value":0.14,"trigger":{"kind":"ALWAYS"}},
    {"op":"STAT_ADD_PCT","stat":"ASPD","value":0.06,"trigger":{"kind":"ALWAYS"}}
  ],
  "active": {
    "cooldown": 12.0,
    "effects": [
      {"op":"DAMAGE","value":2.0,"target":"ALL_ENEMIES"},
      {"op":"APPLY_STATUS","statusId":"STUN",
       "duration":{"seconds":1.0,"scope":"BATTLE"},"target":"ALL_ENEMIES"}
    ]
  }
}
```

### 7.8 A boss mechanic — Thornmaw phase 3
```json
{
  "phase": 3,
  "effects": [
    {"op":"SUMMON","archetype":"SWARM","value":2,
     "trigger":{"kind":"ON_PHASE_ENTER","phase":3}},
    {"op":"SUMMON","archetype":"SWARM","value":2,"maxAlive":3,
     "trigger":{"kind":"PERIODIC","interval":12.0}},
    {"op":"APPLY_STATUS","statusId":"RAGE","value":0.30,
     "duration":{"seconds":999,"scope":"BATTLE"},
     "trigger":{"kind":"ON_PHASE_ENTER","phase":3},"target":"SELF"}
  ]
}
```

### 7.9 A board effect — `TILE_DICE_FORGE`
```json
{ "op":"MODIFY_DIE_FACE", "faceIndex":"PLAYER_CHOICE",
  "newFace":{"kind":"Pip","value":4},
  "duration":{"scope":"RUN"}, "trigger":{"kind":"ON_TILE_RESOLVED","tileType":"TILE_DICE_FORGE"} }
```

---

## 8. Resolution order 🔒

Must be implemented exactly, or builds will produce different numbers on client and server.

```
1. Collect all active effects from: gear → affixes → set bonuses → talents →
   pet auras → mount → run buffs → shrine buffs → curses → perks (in draft order)
2. Filter by condition, evaluated against current state
3. Group by (op, stat)
4. Apply STAT_ADD_FLAT      (sum)
5. Apply STAT_ADD_PCT       (sum, then multiply base once)
6. Apply STAT_CONVERT       (reads post-step-5 values, in effect-id order)
7. Apply STAT_MULT          (product, in effect-id order)
8. Apply STAT_SET           (last writer wins, in effect-id order)
9. Apply caps, honouring STAT_CAP_OVERRIDE
10. Round every resulting stat to 4 decimal places
```

**Effect-id order** means the ascending lexicographic order of effect IDs, not draft order. This removes the last source of order-dependence between client and server.

---

## 9. Rulings on ambiguous designs

These were flagged as open items and are resolved here.

### 9.1 `CP_GLASS_HEART` — ×2 all stats, Max HP set to 1 (was P2 #28)

**Ruling:** the perk is implemented as:
```json
[
  {"op":"STAT_MULT","stat":"ALL_COMBAT","value":2.0},
  {"op":"STAT_SET","stat":"MAX_HP","value":1.0,"valueMode":"FLAT"}
]
```
with these interactions fixed:
- `MAX_HP` is set **after** all multipliers (step 8), so ×2 never applies to it.
- **Shields (`WARD`) still function** and are the build's entire survival mechanism. Shield sizes computed as a % of Max HP become 1 HP — therefore any `SELF_MAXHP_PCT` shield is re-based to a flat value equal to 8% of the hero's *pre-perk* Max HP while this perk is active.
- **Dodge and Block still function normally.** A dodge build with Glass Heart is the intended fantasy.
- **Lifesteal and healing are useless** (capped at 1 HP). This is intended.
- **Thorns still work.**

**Risk accepted:** this is a deliberately extreme, low-appearance-rate perk. It appears only from `TILE_CURSE` and only from Chapter 5 onward. If the balance harness shows it exceeding a 12 pp swing in clear rate (`05` §9 guardrail 2), reduce it to ×1.6.

### 9.2 `PET_DICEBEAST` active (was P2 #29)

**Ruling:** Dicebeast's active is **not** on a combat cooldown. It fires `ON_BATTLE_END` (win only), granting `{"op":"MODIFY_DIE_FACE","scope":"NEXT_3_ROLLS","newFace":{"kind":"Star"}}`. This removes the category error of an out-of-combat effect on an in-combat timer, and it makes the pet feel like a board-layer reward, which fits its identity.

### 9.3 PvP behaviour of non-combat perks (was P2 #26)

🔒 **Decision: keep the ban.** Dice & Board (12 perks) and Economy (10 perks) are **ineligible in PvP**. The eligible pool is **68 perks**.

Consequently the PvP perk budget shrinks: **5 slots, 10 Perk Points** (was 6 slots, 12 points). See `11_PVP_GHOST_DUEL.md` §3.

The `IS_PVP` condition function exists in the DSL regardless, because gear affixes like `+X% Gold Gain` still need to be neutralised in duels — they are simply skipped rather than converted.

---

## 10. Extending the DSL

When a new design cannot be expressed:

1. Write the design as JSON using a **new op name** and let the schema validation fail.
2. Add the op to `SlayIdleRepeat.Core/Effects/Ops`, to the JSON schema, and to this document — all three, in the same commit.
3. Add a unit test that asserts the op's numeric behaviour.
4. Add the op to the client/server parity test.

**Never** add a `if (perkId == "PK_X")` branch. A single one of those is the beginning of the end for this system.

---

## 11. Implementation checklist

- [ ] `EffectDefinition` record and JSON schema
- [ ] `EffectResolver` implementing §8's order exactly
- [ ] All 41 ops implemented with unit tests
- [ ] All 21 trigger kinds wired into the combat and run loops
- [ ] All 20 condition functions
- [ ] All 98 perks, 60 talents, 14 affixes, 4 set bonuses, 24 pet definitions, 12 mount definitions, 12 statuses and 8 boss scripts authored as data — **zero hardcoded content**
- [ ] Schema validation in the build, failing on unknown ops or ids
- [ ] Parity test: client and server resolvers agree on 10,000 random build permutations
