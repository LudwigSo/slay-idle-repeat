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
  "valueScale": null,
  "trigger": { "kind": "ALWAYS" },
  "condition": null,
  "target": "SELF",
  "duration": null,
  "stacking": { "mode": "ADDITIVE", "maxStacks": 1 },
  "tags": ["offense"]
}
```

Every effect is the same eight-part shape: **op · trigger · condition · target · value · valueScale · duration · stacking**. Everything in the game is a combination of those. *(`valueScale` added by ruling in `16` A7.)*

🔒 **`tags` is an open author-label set in which `drawback` is reserved.** `05` §4.1's ward **bypass list** (b) — *"self-inflicted costs (cursed-perk drawbacks such as `CP_BLOOD_PRICE` / `CP_TIMEBOUND`)"* — names no marker, and §7.5 authors `CP_BLOOD_PRICE` with `"tags": ["drawback"]`; the marker is defined by that one example, so it is reserved here. Any other tag is an author label with no engine meaning. ⚠️ This is **not** the same vocabulary as `REMOVE_STATUS`'s `statusTag` (§2.3), which labels a *status*.

### 1.1 `valueScale` — state-scaled values 🔒

`valueScale` multiplies the effect's `value` by a whole number of *steps* read from live state:

```
effectiveValue = value × steps
steps          = min( floor( fn / per ), cap )        // cap: null ⇒ uncapped
```

| Field | Meaning |
|---|---|
| `fn` | Any condition function from §4 (`SELF_MISSING_HP_PCT`, `GOLD_HELD`, `PET_COUNT`, `STATUS_STACKS`, `PERK_COUNT`, `DISTINCT_PERK_CATEGORIES`, `BATTLES_WON_THIS_RUN`, …), evaluated against current state and rounded to 4 dp **before** the division |
| `per` | State units per step |
| `cap` | Maximum number of steps; `null` = uncapped |
| `statusId` | The status `HAS_STATUS` and `STATUS_STACKS` read — §4's *"by status id"*. Required by both, meaningless to the rest |
| `category` | The perk category `PERK_COUNT` restricts to — §4's *"optionally by category"*. Genuinely optional; its absence counts every perk |

🔴 **Erratum, closed by M2-06 via §10's route.** The argument rows were missing. This table offered `fn` *"any condition function from §4"* and named `STATUS_STACKS` in its own worked list — but it takes an argument (it counts one status, the same one §4 types `HAS_STATUS` *"by status id"* — §4's own `STATUS_STACKS` row states no argument at all, which is this gap one section over) and there was no field to carry it, so **a scale driven by it was unexpressible** and the function was offered for something the vocabulary could not do. The keys are **not new vocabulary**: they are the same keys a §4 condition term already carries, with the same names, types and meanings, so a function reads an argument the same way from a scale as from a condition. A scale over `STATUS_STACKS` that names no status is **refused**, not read as "every status" or as zero. ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). *The list used to name `DIE_FACE_COUNT` alongside `STATUS_STACKS`, and `faceKind` was the third argument key.*

```json
{ "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.05,
  "trigger": {"kind":"ALWAYS"}, "target": "CURRENT_TARGET",
  "valueScale": { "fn": "STATUS_STACKS", "per": 1, "cap": 5, "statusId": "SUNDER" } }
```

`valueScale` is re-evaluated exactly when conditions are (§4): at every resolution pass for `ALWAYS` effects, at fire time for triggered ones. `valueScale: null` (the default) means `effectiveValue = value`.

`PK_BERSERK` Tier I — *"+1% ATK per 1% missing HP, up to +45%"*:

```json
{ "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
  "trigger": {"kind":"ALWAYS"}, "target": "SELF",
  "valueScale": { "fn": "SELF_MISSING_HP_PCT", "per": 0.01, "cap": 45 } }
```

`PK_HOARD` — *"+1% ATK per 100 Gold currently held"* (uncapped, per its `06` text):

```json
{ "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
  "trigger": {"kind":"ALWAYS"}, "target": "SELF",
  "valueScale": { "fn": "GOLD_HELD", "per": 100, "cap": null } }
```

---

## 2. Operations (`op`)

### 2.1 Stat operations

| Op | Meaning |
|---|---|
| `STAT_ADD_FLAT` | Add a flat amount to a stat, before percent aggregation |
| `STAT_ADD_PCT` | Add to the additive percent bucket for a stat |
| `STAT_MULT` | Multiply the stat after all additive aggregation (Legendary-tier only) |
| `STAT_SET` | Force a stat to a value (`CP_GLASS_HEART` only) |
| `STAT_CONVERT` | Convert `value` × the **source** stat (`stat`) into the **destination** stat (`toStat`) — `PK_TURTLE` (20% of DEF into ATK), `PK_JUGGERNAUT` (8% of Max HP into ATK). Two signed deltas, applied at §8 step 6 |
| `STAT_CAP_OVERRIDE` | Raise or redirect a cap, per `capKind`: `STAT_MAX` replaces `05` §1's ceiling on `stat` with `value`; `REDIRECT_EXCESS` multiplies the amount by which `stat` overshot its ceiling by `value` and adds it to `toStat` (`Perfect Strike`); `HEAL_CEILING` bounds `Heal()` (`05` §4.3) at `value` × Max HP and touches no stat cap (`Avatar of War`) |

Valid `stat` values: `MAX_HP · ATK · DEF · ASPD · CRIT · CDMG · LIFESTEAL · DODGE · BLOCK · PEN · DMG_PCT · DR_PCT · HEAL_PCT · THORNS`
Plus the non-combat stats: `GOLD_PCT · CROWNS_PCT · DROP_CHANCE · RARITY_SHIFT · ENERGY_REGEN_PCT · PET_AURA_PCT · TILE_PREVIEW · SHOP_PRICE_PCT · XP_PCT · BEAST_FEED_PCT · STONE_PCT`

⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). *`REROLL_CHARGES` was a twelfth non-combat stat; nothing can grant a reroll charge, so no effect, affix or set bonus may name it. Its wire number stays retired rather than reused.*

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

`valueMode`: `ATK_MULT` (default) · `FLAT` · `SELF_MAXHP_PCT` · `TARGET_MAXHP_PCT` · `TARGET_MISSING_HP_PCT` · `DAMAGE_DEALT_PCT` · `HEAL_AMOUNT` · `OVERHEAL_AMOUNT`

The last two exist only inside `ON_HEAL` contexts (`05` §4.3): `HEAL_AMOUNT` is the full amount actually healed, `OVERHEAL_AMOUNT` the clipped excess. A `SHIELD` op may carry `sourceCapPct`: the total unbroken ward contributed by that effect instance is clamped at `sourceCapPct × Max HP`. `PK_TRANSFUSION` (*"overheal converts into a shield, up to 20% Max HP"*):

```json
{ "op": "SHIELD", "valueMode": "OVERHEAL_AMOUNT", "value": 1.0, "sourceCapPct": 0.20,
  "trigger": {"kind":"ON_HEAL"}, "target": "SELF" }
```

### 2.3 Status operations

| Op | Meaning |
|---|---|
| `APPLY_STATUS` | Apply one of the 12 statuses from `05` §5 |
| `REMOVE_STATUS` | Clear one status (`statusId`) **or** every status carrying a tag (`statusTag`) — exactly one of the two. 🔒 `statusTag` labels a **status**; it is not the effect's own `tags` array (§1), which labels the effect and whose `drawback` member is reserved for `05` §4.1's ward-bypass list |
| `EXTEND_STATUS` | Add duration to an existing status |
| `IMMUNE_STATUS` | Grant immunity to a status for a duration |
| `STATUS_POWER_PCT` | Scale the potency of statuses this actor applies |
| `STATUS_DURATION_PCT` | Scale duration of statuses applied to this actor |

### 2.4 Combat-flow operations

| Op | Meaning |
|---|---|
| `EXTRA_ATTACK` | Perform an additional attack immediately |
| `ATTACK_MULT_NEXT` | Multiply the damage of the next `charges` attacks by `value` (`PK_OPENER`'s ×3 first attack — `05` §4) |
| `FORCE_CRIT_NEXT` | The next `charges` attacks always crit. Carries no `value` |
| `REDUCE_COOLDOWN` | Reduce pet/boss ability cooldowns |
| `SURVIVE_LETHAL` | Survive an otherwise-fatal hit at the HP `value`/`valueMode` name — `PK_UNBREAKABLE` is `{"value": 1, "valueMode": "FLAT"}` = **1 HP**, per `06`. `valueMode` defaults to `SELF_MAXHP_PCT` here, not `ATK_MULT` |
| `REVIVE` | Return from 0 HP at a given HP fraction |
| `SUMMON` | Spawn N enemies of an archetype (boss use) |
| `SET_TARGET_PRIORITY` | Adjust targeting weight (added for Sporequeen — `17` §8) |
| `DAMAGE_TAKEN_MULT` | Multiply incoming damage (Rimehold's Core — `17` §6) |
| `CLEAR_SUMMONS` | Despawn all living summons owned by the target (default `SELF`). Despawned ≠ killed: no `ON_DEATH`, no `ON_KILL`, no on-death explosions, no rewards (Ossuary King's Rise Again — `17` §4) |
| `STAT_COPY` | Copy `value` × the copy-source's **final resolved** stat onto the holder as a percent-bucket add for `duration`. Reads the start-of-tick snapshot, so mutual copies cannot recurse. `stat` may be a stat name or `HIGHEST_PCT_BONUS` (Cogitator's Recalibrate — `17` §7; `PK_PACK_LEADER` copying the hero's CRIT to pets) |
| `RANDOM_OUTCOME` | Draw **one** value from the battle's combat stream over the `outcomes` weight table and fire the single effect that row names — the **mutually exclusive** choice §10.1 E6 adds for the Dicelord's *Roll of Fate* (`17` §9). Each row is `{"effectId": …, "weight": …}` and names a **sibling** effect id — one declared by the **same owning content** as the `RANDOM_OUTCOME` itself — never an embedded effect object. An effect is embedded in the content that owns it rather than living in a registry, so the reference resolves inside that one owner and needs nothing global; the boss encounter builder holds the script’s own effect set at the moment it has to answer and refuses a row naming an id the script does not declare. It is a reference rather than a nested object because (a) the outcome rows are ordinary phase mechanics that must **also** sit on the actor plan to be registered, telegraphable and index-resolvable, so inlining would author each one twice, and (b) no other op nests an effect inside an effect and `effect.schema.json`’s `oneOf` is a closed op-to-key partition with no shape for one; weights are relative, and a `0` weight disables that row. Carries **no** `value`: its own number is the 1-based index of the row that won. 🔒 Exactly one draw index per roll — three `chance`-gated effects would be three *independent* draws, which is neither mutual exclusion nor one d6 |

### 2.5 Run and board operations

These are resolved by the run controller, never by the combat simulator.

🔒 **Combat-context exception** *(ruled in `16` A7)*: a **combat trigger may emit a run/board op** — e.g. a boss firing `MOVE_NODES` from a `PERIODIC` trigger. ⚠️ The sanctioned case used to be the Dicelord's Scramble firing `MODIFY_DIE_FACE` (`17` §9); that op is gone (`04` §5) and so is the Scramble. The simulator still never resolves it: it appends a `RunEffectQueued` event to the combat log (`05` §7) and the run controller applies the queued ops **in log order when the battle resolves** — after the outcome is fixed, before `ON_BATTLE_END` effects are granted. In a PvP duel the queue is discarded, consistent with `IS_PVP` skipping (§9.3). This keeps the simulator pure while letting combat mark consequences for the run.

| Op | Meaning |
|---|---|
| `GRANT_CURRENCY` | Gold, Crowns, Soul Shards, Beast Feed, Enhance Stones, Merge Dust, Honor, Energy |
| `GRANT_ITEM` | A gear item at a given rarity band |
| `GRANT_PERK` | A perk, random or specified |
| `UPGRADE_PERK` | Raise an owned perk one tier |
| ~~`MODIFY_DIE_FACE`~~ | ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). Ordinal 35 stays retired. |
| ~~`GRANT_REROLL`~~ | ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). Ordinal 36 stays retired. |
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
| `ON_ATTACK` | Each attack made | `everyNth`, `chance` |
| `ON_HIT` | Each successful hit landed | `chance` |
| `ON_CRIT` | Each critical hit | `chance` |
| `ON_HIT_TAKEN` | Each hit received | `chance`, `cooldown` |
| `ON_DODGE` / `ON_BLOCK` | On a successful avoid | `cooldown` |
| `ON_KILL` | The owner kills an enemy | `everyNth` |
| `ON_DEATH` | The owning actor dies — fires in tick slot 6, before removal (`05` §3.1). The Volatile elite's explosion; Sporequeen's sporeling-death heal | — |
| `ON_REVIVE` | The owning actor returns from 0 HP — the `REVIVE` op or the ad revive. `SURVIVE_LETHAL` does **not** count (the actor never died). `PK_PHOENIX` | — |
| `ON_LOW_HP` | Self HP crosses a threshold downward | `threshold`, `once` |
| `ON_LETHAL` | Would take fatal damage | `once` |
| `ON_HEAL` | Healing is received | — |
| `PERIODIC` | Every N seconds of battle time | `interval`, `startDelay` |
| `ON_PHASE_ENTER` | Boss phase begins | `phase` |
| `ON_TILE_RESOLVED` | A board tile resolves | `tileType` |
| `ON_ROLL` | A die roll completes | *(none)* ⚠️ it took `faceKind`; the die has no face kinds (`04` §1), so the trigger fires on every roll |
| `ON_PERK_TAKEN` | A perk is drafted | `category` |
| `ON_STAGE_GATE` | A stage boundary is crossed | — |
| `ON_RUN_START` / `ON_RUN_END` | Run boundaries | — |

`everyNth` counters live on the effect instance: `ON_ATTACK` counters reset at battle start; `ON_KILL` counters **persist across battles for the run** (`PK_MIDAS`'s "every 6th enemy killed"). Never fires in PvP (`05` §3.3) — and a duel kill does not advance the counter either, because the counter belongs to the run and a duel is not part of one.

### 3.1 Rulings on the trigger table 🔒

Three rulings, recorded here so the trigger vocabulary has one statement rather than three.

**R8 — a `PERIODIC`'s clock starts when its owning effect becomes active, and `startDelay` is measured from that anchor.** `17` §1.1 defines the boss mechanic as *"fires every N seconds from phase entry"*; §3 above defines the DSL trigger over battle time. They agree under one rule and no new parameter: an effect inside a boss phase block becomes active at `ON_PHASE_ENTER` and therefore anchors at **phase entry**; a perk periodic (`PK_AEGIS`, `PK_DEATHMARK`) becomes active at battle start and anchors **there**; `SYS_ENRAGE` is a `BATTLE`-scope built-in rather than a phase-scoped one, so its `startDelay: 70.0` is measured from **battle start** — which is what "a hard enrage at 70 s" means. The anchor is set **once**: `05` §3.1's phase check can enter two phases inside one tick and a live effect never re-anchors. Erratum on `17` §1.1's row.

An **absent `startDelay` is one interval**, so the schedule is `anchor + startDelay + k × interval` for `k = 0, 1, 2, …`. `17` §1.1 reads *"every N seconds **from** phase entry"*, whose first firing is at `X + N`; and `17` §1 requires a 1.0–1.5 s telegraph before every damaging mechanic, which a periodic firing on the anchor tick has nowhere to put.

**R9 — `ON_HP_THRESHOLD` *is* `ON_LOW_HP`; there is no 24th trigger.** `17` §1.1's mechanic vocabulary is boss-design shorthand, not the DSL. `ON_LOW_HP` — *"self HP crosses a threshold downward"*, params `threshold`, `once` — is the same sentence. The Ossuary King's `ON_HP_THRESHOLD 1%` Rise Again (`17` §4) is `{"kind":"ON_LOW_HP","threshold":0.01,"once":true}`. The count stays **23**. Erratum on `17` §1.1.

Because `once` exists, `ON_LOW_HP` must be able to fire more than once — which means the threshold **re-arms** when HP goes back above it. Rise Again writes `once: true` precisely because it must not.

**R11 — `ON_ATTACK` gains `chance`**, per §10's procedure (code, schema, this document and a test, in one commit). `06` authors per-attack random perks and `ON_HIT` and `ON_CRIT` already carry `chance`, so this is a uniformity fix rather than a new concept. ⚠️ `ON_KILL` does **not** gain it — §10 extends the DSL by the smallest step that expresses the design, and no design asks for a chancy kill trigger.

**R2 — `once` is a boolean**, as §3 and §7.4 both write it. `05` §3.1's *"at most their authored `once` count"* is loose prose for "the authored limit". Erratum on `05` §3.1's wording.

**Parameters that are constitutive rather than narrowing.** A parameter that narrows the row's own sentence means "not narrowed" when it is absent — `chance`, `everyNth`, `cooldown`, `onlyIfWon`, `tileType`, `category`. Three cannot be read that way and are **refused** when absent rather than defaulted: `PERIODIC` without `interval` has no period, `ON_LOW_HP` without `threshold` names no crossing, and `ON_PHASE_ENTER` without `phase` cannot say which entry it means.

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
| ~~`DIE_FACE_COUNT`~~ | ⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41). |
| `GOLD_HELD` | int |
| `BATTLES_WON_THIS_RUN` | int |
| `STAGE_INDEX` | 1..3 |
| `CHAPTER` / `TIER` | int / enum |
| `IS_PVP` | bool — **the hook that lets a perk behave differently in a duel** |
| `ATTACKER_IS_ELITE` / `ATTACKER_IS_BOSS` / `ATTACKER_IS_SUMMON` | bool — valid only in contexts with an attacker (`ON_HIT_TAKEN`, `ON_DODGE`/`ON_BLOCK`, and `DAMAGE_TAKEN_MULT` evaluation inside `05` §4 step 6); `false` elsewhere. `PK_STALWART` |

Comparators: `eq · neq · lt · lte · gt · gte · between`. Combinators: `all · any · not`.

---

## 5. Targets

`SELF · CURRENT_TARGET · OTHER_ENEMIES · ALL_ENEMIES · LOWEST_HP_ENEMY · HIGHEST_HP_ENEMY · RANDOM_ENEMY · ALL_PETS · ATTACKER · OWNER · RUN` (the run itself, for board ops)

- `OTHER_ENEMIES`: all enemies **except the attack's primary target** — `PK_CLEAVE`'s splash no longer double-hits its primary. Valid only inside an attack context; elsewhere it degrades to `ALL_ENEMIES`.
- `OWNER`: the summoner of the source actor (a sporeling's owner is Sporequeen). On an actor that is not a summon, the effect is skipped.

---

## 6. Duration and stacking

```json
"duration": { "seconds": 4.0, "scope": "BATTLE" }
```
`scope`: `INSTANT · BATTLE · PHASE · STAGE · RUN · PERMANENT`

- `PHASE`: ends when the boss **exits the phase in which the effect was applied** (`05` §3.1's phase check fires the exits; `17` §1.1's `AURA` mechanics are `PHASE`-scoped by definition — Gulgrot's Bog Air). Outside a boss fight it behaves as `BATTLE`.
- A duration may also carry an early terminator: `"until": "WARD_BROKEN"` ends the effect the moment the owner's ward pool breaks (`05` §4.1) — Ossify's DR buff. `until` fields fire whichever comes first, terminator or timer.

```json
"stacking": { "mode": "ADDITIVE", "maxStacks": 5, "refreshOnReapply": true }
```
`mode`: `ADDITIVE · MULTIPLICATIVE · REPLACE · HIGHEST_WINS · NONE`

🔴 **Erratum, recorded by M2-06 — this section names the five modes and describes none of them.** The implementation reads each mode as its name: `ADDITIVE` sums the applications, `MULTIPLICATIVE` multiplies them, `REPLACE` keeps the newest, `HIGHEST_WINS` keeps the strongest, `NONE` keeps the first and ignores the rest. Three are pinned to authored behaviour and are not inferences: `MULTIPLICATIVE` is `05` §3.1's `SYS_ENRAGE` (*"multiplicative stacking, uncapped"*), `ADDITIVE` is `05` §5's `SUNDER`/`BURN` (*"stacks to 5"*), `NONE` is `05` §5's `BLEED` (*"does not stack; reapplication refreshes"*). `REPLACE` and `HIGHEST_WINS` have no authored user yet.

🔴 **`HIGHEST_WINS` compares the two values *literally*, not by magnitude** — it keeps the larger number. The consequence, stated so nobody has to rediscover it: a **negative**-valued debuff authored with `HIGHEST_WINS` keeps the **least** negative application, i.e. the *weakest* one. No authored content does this today. Taking the larger number is the only reading that invents nothing; the day a design needs "largest magnitude", this section is what has to say so.

- `maxStacks`: the stack ceiling; absent or `null` is **uncapped** (`SYS_ENRAGE`), never one. A surplus application past the ceiling is **dropped** — §6 states the ceiling and authors no eviction, so nothing is evicted — and it still refreshes the duration if `refreshOnReapply` asks, because `05` §3.1 keeps the two questions apart (*"reapplication adds stacks / refreshes duration"*).
- `refreshOnReapply`: an independent key, honoured for **every** mode including `NONE` (`BLEED` is exactly `NONE` + `refreshOnReapply`). Absent is not `true`. It restarts the effect's **duration** only — `05` §3.1 is explicit that reapplication *"never re-anchors the cadence"*.

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
  { "op":"SURVIVE_LETHAL", "value":1, "valueMode":"FLAT",
    "trigger":{"kind":"ON_LETHAL","once":true} },
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

🔴 **Erratum (conductor ruling R3): the `RAGE` block below is a pre-`PHASE` artifact. Every boss `AURA` mechanic is `"duration": {"scope": "PHASE"}`.** §6 says `AURA` mechanics are `PHASE`-scoped *by definition* and §7.10 authors Gulgrot's Bog Air that way; `PHASE` itself was added by the `16` A7 batch (§11: *"6 duration scopes = 5 + `PHASE`"*), so this example simply predates it. The two forms are equivalent **for Thornmaw only**, because phase 3 is never exited — which is why the artifact survived review. Applied in any earlier phase they differ: the `PHASE` form ends at the exit and `{999, BATTLE}` does not. `M2-13` authors the boss data with `scope: PHASE`; the `{"seconds": 999}` idiom is not to be used anywhere.

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

### 7.10 The A7 batch, worked — `PK_STALWART`, `PK_CLEAVE`, Volatile, Ossify, Bog Air

`PK_STALWART` Tier I — *"−20% damage taken from Elites and Bosses"*:

```json
{ "op": "DAMAGE_TAKEN_MULT", "value": 0.80, "trigger": {"kind":"ALWAYS"},
  "target": "SELF",
  "condition": { "any": [
      { "fn": "ATTACKER_IS_ELITE", "op": "eq", "value": true },
      { "fn": "ATTACKER_IS_BOSS",  "op": "eq", "value": true } ] } }
```

`PK_CLEAVE` Tier I — *"attacks hit all enemies for 40% damage"* (primary takes the normal hit; the splash never double-hits it):

```json
{ "op": "DAMAGE", "value": 0.40, "trigger": {"kind":"ON_HIT"},
  "target": "OTHER_ENEMIES" }
```

The `Volatile` elite modifier — *"explodes on death for 15% of hero Max HP"*:

```json
{ "op": "DAMAGE_MAXHP_PCT", "value": 0.15, "valueMode": "TARGET_MAXHP_PCT",
  "trigger": {"kind":"ON_DEATH"}, "target": "ALL_ENEMIES" }
```

Ossify — Ossuary King phase 2 (`17` §4): ward plus a DR buff that dies with the ward:

```json
[
  { "op": "SHIELD", "value": 0.20, "valueMode": "SELF_MAXHP_PCT",
    "trigger": {"kind":"PERIODIC","interval":14.0}, "target": "SELF" },
  { "op": "STAT_ADD_PCT", "stat": "DR_PCT", "value": 0.30,
    "trigger": {"kind":"PERIODIC","interval":14.0}, "target": "SELF",
    "duration": { "seconds": 6.0, "scope": "BATTLE", "until": "WARD_BROKEN" } }
]
```

Bog Air — Gulgrot phase 2 (`17` §3): an aura that ends on phase exit:

```json
{ "op": "STAT_ADD_PCT", "stat": "HEAL_PCT", "value": -0.35,
  "trigger": {"kind":"ON_PHASE_ENTER","phase":2}, "target": "ALL_ENEMIES",
  "duration": { "scope": "PHASE" } }
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

### 10.1 Extensions taken under this procedure

Every row below adds a **key or a token, never a number** — the numbers stay in authored content
(`16` R6). Each landed with its op, its schema branch and its row in §2, in one commit.

| # | Op(s) | What was missing | Key added | Why it was unimplementable |
|---|---|---|---|---|
| E1 | `STAT_CONVERT` | the destination stat | `toStat` | §2.1 says "stat A into stat B" and the shape carries one `stat`. `06`'s `PK_TURTLE` and `PK_JUGGERNAUT` share a destination and differ in source, so `stat` is the source |
| E2 | `STAT_CAP_OVERRIDE` | a token for either half of "raise or redirect" | `capKind: STAT_MAX`, `capKind: REDIRECT_EXCESS`, `toStat` | the one authored `capKind`, `HEAL_CEILING`, is a ceiling on `Heal()` (`05` §4.3) and **not** one of `05` §1's six stat caps; the only stated redirect (`09` §4's *Perfect Strike*) has no JSON anywhere |
| E3 | `ATTACK_MULT_NEXT`, `FORCE_CRIT_NEXT` | the N of "the next N attacks" | `charges` | both need a count, and `05` §4 already spends the one `value` on the multiplier |
| E4 | `SURVIVE_LETHAL` | which unit `value` is in | `valueMode` | §2.4 says "HP fraction", §7.4 writes `"value": 1` (a *full-HP* fraction) and `06` says "at 1 HP". Reuses §2.2's existing modes rather than picking a reading |
| E5 | `REMOVE_STATUS` | the tag group | `statusTag` | §2.3 offers "a status or a tag group" and named a key only for the first |
| E6 | `RANDOM_OUTCOME` (new op) | a **mutually exclusive** weighted choice | `outcomes` (`[{effectId, weight}]`) | `17` §9's *Roll of Fate* is one visible d6 with three results. §4's conditions are "pure functions of current state" and a draw is **not** state, so three `chance`-gated effects are three *independent* draws — all three can fire, or none — and they spend **three** draw indices where `14` §8.0's `WeightedPick` spends **one**, desynchronising every later draw of the battle |

⚠️ **Not taken, and recorded so nobody assumes it was.** `REVIVE` has E4's problem word for word —
§2.4 gives it "at a given HP fraction" — and is deliberately left fraction-only: no authored content
needs a flat revive, and a key nobody asked for is still a key nobody agreed.

🔴 **The one entry that moved.** This paragraph used to record `17` §9's Dicelord *Roll of Fate* as
having no DSL construct and deliberately not being given one, on the grounds that "a 44th op would
break §11's pinned count, and the ruling belongs with the boss engine". **M2-12 is the boss engine,
and it has ruled**: the construct is E6 above, the count in §11 moves from 43 to 44 with it, and the
reasoning is the one E6's own row states — independent `chance` draws are not mutual exclusion, and
three draws are not `WeightedPick`'s one. Adding the op is the cheaper of the two failures the
pinned count exists to catch: a count that moves in a reviewed commit, against a boss mechanic that
no amount of authored content could express.

---

## 11. Implementation checklist

- [ ] `EffectDefinition` record and JSON schema
- [ ] `EffectResolver` implementing §8's order exactly
- [ ] All 42 ops implemented with unit tests
- [ ] All 23 trigger kinds wired into the combat and run loops
- [ ] All 22 condition functions
- [ ] All 98 perks, 60 talents, 14 affixes, 4 set bonuses, 24 pet definitions, 12 mount definitions, 12 statuses and 8 boss scripts authored as data — **zero hardcoded content**
- [ ] Schema validation in the build, failing on unknown ops or ids
- [ ] Parity test: client and server resolvers agree on 10,000 random build permutations

*(Counts after the `16` A7 batch extension, §10.1 E6 and D41's removals: 42 ops = 41 + `CLEAR_SUMMONS` + `STAT_COPY` + `RANDOM_OUTCOME` − `MODIFY_DIE_FACE` − `GRANT_REROLL`; 23 triggers = 21 + `ON_DEATH` + `ON_REVIVE`; 22 conditions = 20 + the three `ATTACKER_IS_*` − `DIE_FACE_COUNT`; 11 targets = 9 + `OTHER_ENEMIES` + `OWNER`; 6 duration scopes = 5 + `PHASE`.)*

### 11.1 Erratum on the last checklist line — the parity test (M2-17)

🔴 **"Client and server resolvers agree" has nothing to compare against, and will not until M7.**
There is **one** resolver, in one assembly, that both sides load; no client build exists yet. A test
that ran it twice and compared the answers could not fail.

**Ruling: the line is delivered as a committed-baseline determinism test.** 10 000 seeded random
build permutations are resolved through §8 and hashed with `CanonicalStateWriter`, against a
committed table — `tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/`. That is the same
shape M0-06 (`Hash64`) and M0-07 (`CanonicalStateWriter`) already use, and it catches what this line
exists to catch: an accidental order-dependence in §8.

⚠️ **It proves stability, not correctness.** M0-06's and M0-07's tables were validated against
*externally published* vectors; there is no published authority for *build permutation → hash*, so
this table is self-generated and says so in its own header. Correctness comes from M2-02…M2-07's
per-op and per-step unit tests.

⚠️ **Real two-runtime parity belongs to M5-12**, on Linux x64 and Android ARM64 — the iOS ARM64 leg
is authored but gated off with iOS itself (`16` D34).

🔴 **The succession is not yet booked, and this is the record of that.** M5-12's tracker row names
the cross-platform `LogHash` test and the command-sequence parity test; it does **not** name this
table, and `ci.yml`'s determinism job is `if: false` until M5-12 turns it on. So nothing today runs
these hashes on a second runtime, and nothing today obliges M5-12 to. M2-17 may not edit the tracker,
so the carry-forward went to the conductor instead. Until that row is amended, read every "M5-12
re-asserts this table" in the test tree as *what should happen*, not as *what is wired*.

🔒 **§10 step 4 now names something.** *"Add the op to the client/server parity test"* means: add the
token to the emission sets in
`tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/` (today `EffectVocabularyEmissionSets`),
whose emitted vocabulary is asserted against this document's catalogues **in both directions** — the
catalogue being the closed enum, never the emission set itself. All five §10.1 extensions, plus
§3.1's R11 `chance` and M2-06's three `valueScale` argument keys, are covered there. A **45th** op
cannot be added without either reaching the permutation corpus or turning a test red — as the 44th
demonstrated, below.

⚠️ Adding, removing or reordering a token **moves every hash in the committed table**, because the
generator anchors each axis by `list[index % list.Count]`. That is a documented regeneration, not a
determinism break, and the reviewer says which it was in the table's `review.why`.

🔴 **The forty-fourth op exercised that clause immediately.** §10.1's E6 `RANDOM_OUTCOME` (M2-12,
ruling R20) was added to the emission set and the table regenerated under the documented command;
`review.why` records it. No hash was regenerated to turn a red test green.
