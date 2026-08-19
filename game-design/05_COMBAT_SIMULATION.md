# 05 — Combat Simulation

🔒 LOCKED: Full auto-battle. The player has **zero inputs during a fight** other than speed control and (on death) the revive prompt. All agency is upstream: gear, talents, pets, mount, perks drafted this run.

This is the most important document for the implementer. The combat simulator must be:

1. **Deterministic** — `Simulate(seed, heroSnapshot, enemySnapshot)` returns an identical result every time, on any device, on the server.
2. **Engine-independent** — a pure C# library with no Godot references, so it can run headless for PvP validation and for balance sweeps.
3. **Fast** — a full 60-second fight must simulate in < 5 ms so PvP results can be computed instantly and balance sweeps can run thousands of fights.

The visual battle is a **replay of a pre-computed log**, not a live simulation. Compute first, then animate.

🔴 **Erratum, recorded by M2-09 — the shipped signature.** `Simulate(seed, heroSnapshot, enemySnapshot)` above is the *shape*, not the parameter list. The entry point is `CombatSimulator.Simulate(battleSeed, hero, heroLevel, enemies, enemyLevel, content)`. The two **levels** were added by M2-08 because §4 step 3's mitigation curve reads `attacker.Level` and a stat block does not carry one. The **`ContentSnapshot`** was added by M2-09 because §1.1's caps, §4's two mitigation dials and §4.1's `wardCapPct` are all tunables held in data (each declared at its own section below), and a `static` method can hold no document: it is handed one, and `CombatCaps.Read` resolves every pointer. Taking the constants as bare numbers instead was rejected — two adjacent `double`s that transpose silently, plus one more parameter per future dial. Nothing else about the signature is widened, and no new public *type* was introduced.

---

## 1. Stat definitions

| Stat | Symbol | Type | Notes |
|---|---|---|---|
| Max Health | `MaxHP` | float | |
| Attack | `ATK` | float | |
| Defense | `DEF` | float | Mitigation, not flat subtraction |
| Attack Speed | `ASPD` | float | attacks per second, base 1.0 |
| Crit Chance | `CRIT` | 0..1 | capped at 0.75 |
| Crit Damage | `CDMG` | float | multiplier bonus, base 0.50 (i.e. ×1.5) |
| Lifesteal | `LS` | 0..1 | capped at 0.40 |
| Dodge | `DODGE` | 0..1 | capped at 0.50 |
| Block | `BLOCK` | 0..1 | capped at 0.60; a block halves the hit |
| Armor Penetration | `PEN` | 0..1 | capped at 0.70 |
| Damage Bonus | `DMG%` | float | additive multiplier bucket |
| Damage Reduction | `DR%` | 0..1 | capped at 0.60 |
| Healing Received | `HEAL%` | float | |
| Thorns | `THORN` | float | % of damage taken reflected |

### 1.1 Stat aggregation order (must be implemented exactly)

```
Final(stat) = ( Base(stat)
              + Σ FlatAdd(stat) from gear, talents, pets, mount, perks, run buffs )
              × ( 1 + Σ PctAdd(stat) )
              × Π (1 + Multiplicative(stat))
```

- **Flat** adds first, **percent** buckets are additive with each other, **multiplicative** sources (rare, Legendary perks only) multiply last.
- Caps are applied **after** all aggregation.
- Rounding: all combat math uses `double`, **rounded to 4 decimal places (`Math.Round(x, 4)`) at every accumulation point** — after each damage calculation, each heal, and each stat aggregation step. This is the locked determinism rule (`14` §8.2, `18` §8 step 10, `16` A3). Display rounding is separate and cosmetic. *(Corrected in `16` A7 — this line previously said "round only for display", which contradicted `14` §8.2.)*

📐 TUNABLE: every cap above lives in `res://data/combat_caps.json`.

---

## 2. Hero base stats

```
MaxHP  = 250 + 45 * L
ATK    = 30  + 6  * L
DEF    = 15  + 3  * L
ASPD   = 1.00
CRIT   = 0.05
CDMG   = 0.50
LS     = 0.00
DODGE  = 0.02
BLOCK  = 0.00
PEN    = 0.00
DMG%   = 0.00
DR%    = 0.00
HEAL%  = 1.00      // multiplier on ALL healing received; base 1.0, so lifesteal and
                   // heals work with no modifiers. "+35% Healing Received" ⇒ ×1.35.
THORN  = 0.00

where L = Legend Level (1..200)
```

🔒 The last four rows were added by `16` A7: every actor — hero and enemy alike — carries a complete 14-stat block with these defaults. An unstated stat is a bug, not a zero.

Gear, talents, pets and mounts then multiply these. At Legend Level 60 with mid-tier gear the hero should sit near `ChapterPowerTarget(5)`.

📐 TUNABLE.

---

## 3. Simulation model

A **fixed-tick** simulation.

| Property | Value |
|---|---|
| Tick rate | 20 ticks/second (`TICK = 0.05 s`) |
| Max fight duration | 90 s = 1800 ticks. On timeout, the side with the higher **remaining HP fraction** wins. |
| Actors | Hero (1), Pets (0–3, ability modules — they never basic-attack, see §3.2), Mount (passive only, no actor), Enemies (1–5) |

### 3.1 Tick order (strict) 🔒

*(Replaced by ruling in `16` A7 — the previous 7-step list had no slot for battle-start effects, periodic effects, boss abilities, phase transitions, DoT cadence or the enrage.)*

**The battle-start pre-tick** — runs once, before tick 0:

```
PreTick:
    0a. Aggregate every actor's stats (`18` §8); attackCooldown = 0 for all
        battle-opening actors (the first basic attack lands on tick 0).
    0b. Fire every ON_BATTLE_START effect — hero side first (hero, then pets in
        slot order), then enemies by index; within one actor, ascending
        effect-id order. WARD grants (PK_WARDED, Shielded elites), opening buffs
        and ATTACK_MULT_NEXT charges (PK_OPENER) land here, before any attack.
    0c. The boss's phase 1 counts as entered: fire its ON_PHASE_ENTER(1) effects.
    0d. Emit BattleStart.
```

**The per-tick loop:**

```
for tick in 0..maxTicks-1:
    1. Status timers advance by TICK. Every DoT/HoT instance whose cadence
       boundary falls on this tick applies its tick (cadence rule below).
       Every DoT HP change runs ward absorption (§4 step 9, per the cadence
       rule below — no lifesteal, no thorns) and the phase check (below);
       HoT ticks route through Heal() (§4.3), not §4.
    2. Statuses whose duration reached 0 expire, in ascending effect-id order.
       A DoT expiring exactly on a cadence boundary deals that tick first,
       then expires. Emit StatusExpired.
    3. PERIODIC triggers whose interval elapses this tick fire — boss
       abilities, perk periodics (PK_AEGIS, PK_DEATHMARK), the built-in enrage
       (below) — in actor order (hero, pets in slot order, enemies by index),
       within one actor in ascending effect-id order.
    4. Basic attacks, in fixed initiative order (Hero, then enemies by index —
       pets never basic-attack, §3.2):
         a. if actor.attackCooldown <= 0 and actor.alive and not stunned:
              - select target (§3.2)
              - resolve the attack (§4); on-hit / on-crit / on-kill triggers
                resolve immediately, depth-first, in ascending effect-id
                order; the phase check runs after every HP change
              - actor.attackCooldown = 1.0 / actor.ASPD    (ASPD read at fire time)
         b. actor.attackCooldown -= TICK
    5. Pet ability cooldowns advance; ready abilities fire, pets in slot order.
    6. Deaths resolve: every actor at 0 HP, in actor-index order, fires its
       ON_DEATH effects (`18` §3) and is then removed. ON_KILL has already
       fired on the killing blow (step 4). An actor whose HP reaches 0 stops
       acting and being targetable at that moment — only its death
       *resolution* (ON_DEATH, removal) waits for this slot.
    7. CombatEvents are appended at the moment each state change occurs, not
       batched (the log is the replay).
    8. if hero dead OR all enemies dead: break
```

**Initiative is fixed, not randomised.** This removes a whole class of nondeterminism.

**DoT/HoT cadence 🔒** — one instance per `statusId` per target. The instance ticks once per second of battle time: on the 20th simulation tick after first application, and every 20 ticks thereafter. Reapplication adds stacks / refreshes duration per the status's stacking rule (`18` §6) but **never re-anchors the cadence**. Per-tick amount = per-second potency × current stack count, read at the moment the tick lands. DoT ticks are damage events, not attacks: no dodge, crit or block; DEF mitigation does not apply (potency was fixed at application — `POISON`'s "ignores DEF" is thereby true of every DoT); `DR%` and `DAMAGE_TAKEN_MULT` apply; wards absorb them (§4.1); the §4 damage floor does not apply; they trigger no lifesteal and no thorns.

**Phase check 🔒** — runs immediately after **every** boss HP decrease (attack, DoT tick, thorns, true damage), once ward absorption and the floor are settled: while `currentPhase < PhaseFor(hp)` (thresholds 66% / 33%, §6.3), enter the next phase **in order**, firing its ON_PHASE_ENTER effects in ascending effect-id order before evaluating further. A burst from 70% to 20% therefore fires phase 2's entry, then phase 3's. Phases never revert — healing back above a threshold does not re-enter an earlier phase — and `PHASE`-scoped effects (`18` §6) end at each exit.

**The 70 s enrage 🔒** — implemented exactly once, as a built-in effect `SYS_ENRAGE` present on every boss: `PERIODIC {interval: 1.0, startDelay: 70.0}` → `STAT_MULT ATK ×1.08`, multiplicative stacking, uncapped, `BATTLE` scope (`17` §1). It fires in tick slot 3. Bosses only; ordinary fights rely on the 90 s timeout.

**Summons** enter at the end of the enemy index list with a full attack cooldown (`1.0 / ASPD` — they never attack on their spawn tick) and become targetable at the next targeting evaluation.

**Trigger cascades** resolve depth-first in the order stated above, with two anti-loop rules: reflected (thorns) damage never triggers the victim's thorns, and `SURVIVE_LETHAL` / `REVIVE` effects fire at most their authored `once` count per battle.

### 3.2 Targeting

- Hero targets the enemy with the highest `targetPriority`, breaking ties by **lowest current HP**. `targetPriority` defaults to `0`; a value of `-1` makes an enemy deprioritised (used by Sporequeen Vell's sporelings — `17` §8), `+1` forces focus.
- 🔒 **Pets never perform basic attacks.** They are aura + active-ability modules only (`07` §2.1) and have no ATK/ASPD stats of their own — abilities express their damage as a percentage of the **hero's** ATK. They appear in the tick loop only at step 5 (ability cooldowns). This ruling resolves the previous ambiguity between this section and `07`; `29` §2.4's `PetDpsShare` (abilities only) is confirmed correct.
- A pet's *targeted ability* selects the enemy with the **highest current HP** (so pet abilities chip the tanky one while the hero cleans up), unless the ability specifies its own target (`18` §5).
- Enemies always target the Hero. **Pets cannot be targeted or killed.** They are stat/effect modules with visual presence, not units to protect. 🔒 This keeps the single-hero fantasy intact and removes a large balancing surface.

### 3.3 PvP duel simulation 🔒

The sections above describe hero-vs-enemies. A Ghost Duel (`11`) is hero-vs-hero, and the following rulings define it — the simulator is the same code path with two hero-shaped sides:

| Rule | Specification |
|---|---|
| Sides | Both sides are a full hero + pets snapshot. The mount contributes its stat block only, as in PvE. |
| Targeting | Each hero targets **only the opposing hero**. Pets are untargetable and unkillable on both sides, per §3.2. Pet abilities that target an enemy target the opposing hero. |
| Initiative | Within a tick: the **attacker's side acts first** (hero, then pet abilities), then the defender's side. Fixed and deterministic. The slight attacker edge is deliberate and consistent with only the attacker's rating being at stake (`11` §5.1). |
| `ON_KILL` triggers | **Never fire in duels.** The only death in a duel ends the fight. |
| Target-conditional effects | Conditions such as `TARGET_HP_BELOW_30` (`PK_EXECUTIONER`) read the **opposing hero**. `TARGET_IS_ELITE` / `TARGET_IS_BOSS` are always false. `ENEMY_COUNT` is always 1. |
| Non-combat effects | Gear affixes and perk clauses with no duel meaning are skipped via the `IS_PVP` condition (`18` §4), never converted. |
| Duration | 60 s cap (`pvpMaxFightSeconds`), timeout and tie rules exactly as `11` §4.3. |

---

## 4. Damage resolution

**`AttackMultiplier` 🔒** — a per-attack transient multiplier, **base 1.0** on every basic attack. It is modified only by:

- `ATTACK_MULT_NEXT` charges, consumed in ascending effect-id order (`PK_OPENER`'s ×3 first attack, granted at the pre-tick);
- per-attack multiplier effects (`PK_GAMBLER`'s ×2 / ×0.6 roll);
- a DSL `DAMAGE` op invoking this pipeline: the op's `value` **is** the AttackMultiplier for that resolved attack (a `DAMAGE` with `value: 2.0` resolves as an attack at ×2.0 — see §4.2).

Nothing else writes it, and it resets to 1.0 after every resolved attack.

```
ResolveAttack(attacker, defender):
    // 1. Dodge
    if Rng.NextDouble() < defender.DODGE:
        log(MISS); return

    // 2. Base damage
    raw = attacker.ATK * attacker.AttackMultiplier * (1 + attacker.DMGPct)

    // 3. Mitigation
    effDef     = defender.DEF * (1 - attacker.PEN)
    mitigation = effDef / (effDef + 120 + 20 * attacker.Level)
    dmg        = raw * (1 - mitigation)

    // 4. Crit
    isCrit = Rng.NextDouble() < attacker.CRIT
    if isCrit: dmg *= (1 + attacker.CDMG)

    // 5. Block
    if Rng.NextDouble() < defender.BLOCK: dmg *= 0.5

    // 6. Incoming-damage modifiers
    dmg *= (1 - defender.DRPct)
    dmg *= Π defender.DamageTakenMult      // all active DAMAGE_TAKEN_MULT effects
                                           // (PK_STALWART, Rimehold's Core),
                                           // product, ascending effect-id order

    // 7. Floor: never less than 10% of raw — applied BEFORE ward absorption
    dmg = Max(dmg, raw * 0.10)

    // 8. On-damage basis 🔒: lifesteal and thorns read THIS number — the full
    //    post-mitigation, post-floor hit, BEFORE ward absorption
    basis = dmg

    // 9. Ward absorption (§4.1); only the remainder reaches HP
    dmg = defender.Wards.Absorb(dmg)       // may emit WardBroken
    defender.HP -= dmg
    PhaseCheck(defender)                   // §3.1

    // 10. On-damage effects
    if attacker.LS > 0:    Heal(attacker, basis * attacker.LS)          // §4.3
    if defender.THORN > 0: ReflectDamage(attacker, basis * defender.THORN)

    log(HIT, attacker, defender, dmg, isCrit)
```

**Mitigation curve sanity check:** with `DEF = 120` and `attackerLevel = 1`, mitigation = `120/(120+140)` = 0.46. With `DEF = 600`, mitigation = `600/(600+140)` = 0.81. The `20 * attackerLevel` term means defense must keep growing to stay relevant — this is intentional and is what makes gear upgrades feel necessary rather than optional.

📐 TUNABLE: the `120` and `20` constants are the two most important balance dials in the game. Expose them in data.

**`ReflectDamage` (thorns)** is a non-attack damage event: no dodge, crit, block or floor; reduced by the receiver's `DR%` and `DAMAGE_TAKEN_MULT`; absorbed by the receiver's wards; triggers no lifesteal and — anti-loop rule — never triggers the receiver's thorns in turn.

### 4.1 Wards 🔒 *(ruled in `16` A7)*

`WARD` is one absorb pool per actor, made of **segments** `{amount, expiresAt?, sourceEffectId}`.

| Rule | Specification |
|---|---|
| **Stacking** | Every grant adds a segment (additive). Pool cap 📐 `wardCapPct = 1.0` × the actor's Max HP **as it stood after `18` §8 step 7** (post-multiplier, pre-`STAT_SET`) — which is what keeps `CP_GLASS_HEART`'s re-based shields functional (`18` §9.1). A grant that would exceed the cap is clipped. Lives in `combat_caps.json`. |
| **Absorption order** | Soonest-expiring segment first; ties broken by grant order (oldest first); non-expiring segments last. Deterministic, and expiring wards are used before they are wasted. |
| **Bypass list** | Only two things skip wards entirely and hit HP directly: **(a)** `DAMAGE_TRUE` and effects marked true damage (Sporequeen's Rot aura — `17` §8); **(b)** self-inflicted costs (cursed-perk drawbacks such as `CP_BLOOD_PRICE` / `CP_TIMEBOUND`, and board-layer costs like Emberpeak's Burning tiles) — wards must not silently delete perk drawbacks. Everything else — basic attacks, DSL `DAMAGE`, `DAMAGE_MAXHP_PCT`, DoT ticks, thorns reflect — is absorbed. |
| **On-damage basis** | **Pre-absorption** (§4 step 8): a lifesteal attacker still heals off a fully-warded hit, and thorns still reflect it. |
| **Floor ordering** | The §4 step-7 floor applies **before** absorption; there is no re-floor after. A fully absorbed hit deals 0 HP damage — the floor exists to defeat mitigation stacking, not shields. |
| **Events** | `Shield` on every grant. **`WardBroken`** the moment the pool reaches 0 **through damage** — the event Ossify's DR buff terminates on (`17` §4, `18` §6's `until`). Segment expiry silently removes its remainder (`StatusExpired`), and does **not** fire `WardBroken`. |

🔴 **Erratum, recorded by M2-09 — a DSL `SHIELD` cannot yet author an expiring segment.** The pool implements `expiresAt?` in full, but `IAttackPipeline.GrantWard` (declared by M2-03, and depended on by M2-10, M2-12 and M2-14) carries **no duration**, so `18` §2.2's `SHIELD` op drops `effect.duration` and the ward lasts the fight. The expiring form exists one layer up, as `BattleServices.GrantWard(…, expiresAtTick)`, which is also where `05` §3.1 slot 2's expiry sweep routes. No shipped content authors a `SHIELD` with a duration today and `ShieldDurationExpiryTests` fails the day one does; closing it needs `18` §6's duration bookkeeping wired to the pool, and belongs to whoever lands that.

🔴 **Erratum, recorded by M2-09 — two emission orders, not one.** §4.1's *absorption* order (soonest expiry, then grant order) is not §3.1 slot 2's *expiry* order (ascending effect-id). Two segments from different effects expiring on one tick are absorbed in the first order and logged in the second. The log is inside `LogHash`, which `11` §6 recomputes server-side.

### 4.2 How DSL damage ops route 🔒 *(ruled in `16` A7)*

| Op (`18` §2.2) | Route |
|---|---|
| `DAMAGE` | The **full `ResolveAttack` pipeline** — dodge, mitigation, crit, block, DR, floor, wards. `AttackMultiplier` = the op's value (`ATK_MULT` mode). The source's stats are used; a pet ability uses the hero's ATK (§3.2) but the **pet's** own CRIT (0 unless granted — `PK_PACK_LEADER`). On-hit-family triggers fire for the source actor of the attack. |
| `DAMAGE_TRUE` | **Bypasses everything**: no dodge, mitigation, crit, block, DR, `DAMAGE_TAKEN_MULT`, floor or wards. HP is reduced directly; the phase check still runs; no lifesteal or thorns. |
| `DAMAGE_MAXHP_PCT` | No dodge, crit, block, mitigation or floor; `DR%` and `DAMAGE_TAKEN_MULT` **do** apply (DR builds answer Volatile); wards absorb; no lifesteal or thorns. |
| `HEAL` / `HEAL_LEECH` | Through `Heal()` (§4.3) — `HEAL%` applies. |
| `SHIELD` | A ward grant (§4.1). |
| `REFLECT` | Adds to `THORN` for its duration. |

### 4.3 Healing

```
Heal(target, amount):
    healed   = min(amount * target.HEALPct, target.MaxHP - target.HP)
    overheal = amount * target.HEALPct - healed
    target.HP += healed
    log(Heal); fire ON_HEAL triggers (HEAL_AMOUNT / OVERHEAL_AMOUNT are
    available to them as valueModes — `18` §2.2)
```

`HEAL%` is the recipient's stat, base 1.0 (§2). Overheal is discarded unless an effect consumes it (`PK_TRANSFUSION`).

🔴 **Erratum, recorded by M2-09 — `amount × HEALPct` is floored at 0.** As written the formula has no lower bound, and §5's `SPORE` is *"−X% healing received, **stacks to 4**"*: past a cumulative −100% the multiplier goes negative and `min(negative, MaxHP − HP)` turns a **heal into an HP decrease** — one that emits no `Hit`, runs no phase check, and is observed by nothing. The scaled amount is therefore clamped at 0, so a fully-spored actor is healed for nothing rather than damaged, and the `overheal` a `PK_TRANSFUSION` reads is 0 rather than negative. `SPORE` past 100% is **floored, not reversed**.

---

## 5. Status effects

| ID | Type | Effect |
|---|---|---|
| `BURN` | DoT | `X%` of attacker ATK per second for `D` s. Stacks to 5. |
| `POISON` | DoT | `X%` of the applier's ATK per second, `D` s. Stacks without limit. Ignores DEF. |
| `BLEED` | DoT | Flat damage per second, set at application as `X%` of the applier's ATK; each tick deals that amount × (1 + target's missing-HP fraction) 📐. Stacks to 5; reapplication also refreshes. |
| `FREEZE` | Debuff | −50% ASPD for `D` s |
| `STUN` | Debuff | Cannot act for `D` s. Max 1.5 s per application, with a 3 s immunity window after. |
| `WEAKEN` | Debuff | −X% ATK |
| `SUNDER` | Debuff | −X% DEF, stacks to 5 |
| `SPORE` | Debuff | −X% healing received, stacks to 4 (Chapter 7 signature) |
| `RAGE` | Buff | +X% ATK, decays over `D` s |
| `WARD` | Buff | Absorb shield, flat HP amount. Full semantics — stacking, bypass, ordering, `WardBroken` — in §4.1. |
| `HASTE` | Buff | +X% ASPD |
| `REGEN` | HoT | Heal X% Max HP per second |
| `CHILL` | Debuff | −X% ATK, stacks to 5 |

**Ailment damage scales off ATK.** Every DoT's per-second potency is a percentage of the *applier's* ATK, fixed at application. `POISON` was originally stated against the target's Max HP; it is not, because an ailment whose damage does not read the attacker's ATK cannot be built into, and a poison build that gains nothing from an ATK item is a build the player cannot invest in. The "ignores DEF" property is unchanged and now follows from the cadence rule rather than from the basis: potency is fixed at application, so no DoT meets DEF.

**The five ailment identities are the stacking rules**, and they are what keep the elements apart: `BURN` stacks to a cap and expires; `POISON` stacks without a cap and never expires; `BLEED` stacks to a cap, expires, and is the one a perk can *consume* for an instant payout. `CHILL` and `FREEZE` are cold's pair — chill degrades what an enemy deals, freeze stops it acting, and chill stacking to its cap is what a perk turns into a freeze.

**`CHILL` is not `WEAKEN`**, although the two write the same stat. Cold's perks are gated on "is this enemy chilled", and keying that on `WEAKEN` would fire them on any enemy an unrelated source had weakened. It is the thirteenth row and it is **last**: a row position here is the wire ordinal `LogHash` pins, so inserting it beside `FREEZE` would renumber every status event in every committed reference log.

**Stun immunity** is mandatory. Without it, stun-locking becomes the only viable build.

---

## 6. Enemy stat derivation from Power

Enemies are not hand-statted. They are derived from the chapter `Power` value and an archetype coefficient set. This keeps 8 chapters × 3 tiers × 64 enemy variants authorable.

```
EnemyStats(power, archetype):
    MaxHP  = power * 0.60  * archetype.hpCoef
    ATK    = power * 0.045 * archetype.atkCoef
    DEF    = power * 0.030 * archetype.defCoef
    ASPD   = 1.00 * archetype.aspdCoef
    CRIT   = archetype.crit
    CDMG   = archetype.critDamage
    DODGE  = archetype.dodge
    LS     = archetype.lifesteal
    BLOCK  = 0.00
    PEN    = 0.00
    DMG%   = 0.00
    DR%    = 0.00
    HEAL%  = 1.00
    THORN  = 0.00
    Level  = EnemyLevel(chapter, tier)        // §6.0

    every term rounded to 4 dp (§1.1)
```

The formula is now total — every stat of every enemy is computable from `(power, archetype, chapter, tier)` with no free variables *(completed in `16` A7)*. Elite modifiers (§6.2) and boss mechanics (`17`) then modify these baselines through the effect DSL. Archetype rows live in 📐 `data/enemies.json`.

### 6.0 Enemy level 🔒

The damage formula (§4) needs an attacker/defender **Level** for enemies, which the Power derivation above does not produce. Enemy level is a **chapter-based table**, set to roughly the Legend Level a player typically has at that chapter (`29` §5):

```
EnemyLevel(c, t) = BaseEnemyLevel(c) + TierLevelBonus(t)

BaseEnemyLevel:   Ch1 10 · Ch2 15 · Ch3 20 · Ch4 30 · Ch5 40 · Ch6 50 · Ch7 60 · Ch8 80
TierLevelBonus:   Normal +0 · Heroic +10 · Mythic +20
```

All enemies, Elites, Guardians (`25` §3) and bosses in a `(chapter, tier)` share this level. It preserves the intended pressure from §4's mitigation note — defense must keep growing to stay relevant across chapters. 📐 TUNABLE, lives in `data/tuning/par_power.json` beside the par table.

### 6.1 Enemy archetypes (8 base shapes, reskinned per biome)

| Archetype | hpCoef | atkCoef | defCoef | aspdCoef | crit | critDamage | dodge | lifesteal | Behaviour flavour |
|---|---|---|---|---|---|---|---|---|---|
| `GRUNT` | 1.00 | 1.00 | 1.00 | 1.00 | 0.05 | 0.50 | 0.02 | 0 | Baseline |
| `SWARM` (×3 units) | 0.35 | 0.55 | 0.60 | 1.30 | 0.05 | 0.50 | 0.05 | 0 | Three weak bodies; punishes single-target |
| `BRUTE` | 2.00 | 1.35 | 1.20 | 0.60 | 0.05 | 0.75 | 0.00 | 0 | Slow heavy hitter |
| `SKIRMISHER` | 0.70 | 0.85 | 0.70 | 1.70 | 0.10 | 0.50 | 0.15 | 0 | Fast, high dodge |
| `WARDEN` | 1.60 | 0.70 | 2.20 | 0.85 | 0.03 | 0.50 | 0.02 | 0 | Tanky, applies `SUNDER` (§6.1a) |
| `CASTER` | 0.75 | 1.50 | 0.55 | 0.70 | 0.08 | 0.60 | 0.03 | 0 | Applies the biome status on hit (§6.1a) |
| `LEECH` | 1.10 | 0.95 | 0.90 | 1.10 | 0.05 | 0.50 | 0.03 | 0.25 | Sustain drain |
| `REAVER` | 0.90 | 1.20 | 0.80 | 1.00 | 0.30 | 1.20 | 0.05 | 0 | Crit spiker |

📐 All secondary columns authored per `16` A7. The three values previously stated in prose are preserved exactly: SKIRMISHER dodge 0.15, LEECH lifesteal 0.25, REAVER crit 0.30 / critDamage 1.20. All other secondaries are new and tunable. `BLOCK`/`PEN`/`DMG%`/`DR%`/`THORN` are 0 and `HEAL%` is 1.0 for every archetype (§6 formula).

### 6.1a On-hit status parameters *(ruled in `16` A7)*

**`WARDEN` — SUNDER.** Wherever a WARDEN appears (any chapter, including Cogitator Prime's drones — `17` §7), its on-hit debuff is one authored parameter set 📐:

| Parameter | Value |
|---|---|
| Proc chance per landed hit | 0.35 |
| Potency | −5% DEF per stack |
| Duration | 6 s, refresh on reapply |
| Max stacks | 5 (§5) |

**`CASTER` — the biome status.** Each chapter's CASTER applies that chapter's signature status on hit, aligned with the chapter signatures in `03` §4.1. One row per chapter 📐 (all rows: proc chance is per landed hit; stacking per §5):

| Ch | Biome | Status | Flavour name | Potency | Duration | Proc |
|---|---|---|---|---|---|---|
| 1 | Greenwood Vale | `BLEED` | Thorn Gash | 20% caster ATK/s | 3 s | 0.30 |
| 2 | Ashen Mire | `POISON` | Bog Rot | 20% caster ATK/s | 4 s, stacks 3 | 0.30 |
| 3 | Sunken Crypt | `BLEED` | Bone Splinter | 30% caster ATK/s | 4 s | 0.35 |
| 4 | Emberpeak | `BURN` | Magma Splash | 30% caster ATK/s | 3 s, stacks 5 | 0.35 |
| 5 | Frostbound Reach | `FREEZE` | Deep Chill | −50% ASPD (§5) | 2 s | 0.25 |
| 6 | Clockwork Vaults | `SUNDER` | Shear | −5% DEF per stack | 6 s, stacks 5 | 0.35 |
| 7 | Bloom of Decay | `SPORE` | Spore Cloud | −10% healing received per stack | 8 s, stacks 4 | 0.35 |
| 8 | Astral Spire | `BURN` | Starfire | 40% caster ATK/s | 3 s, stacks 5 | 0.35 |

Chapters whose signature is already a combat status (2, 5, 7 — `03` §4.1) use exactly that status; the remaining chapters (1, 3, 4, 6, 8), whose signatures live on the board or outside the status system, reuse the closest-fitting status under a biome skin — Emberpeak's BURN echoes its Burning tiles, and reskinning is the archetype system's normal mode. Sporequeen's own SPORE (−12%, never expires) is a boss-authored override (`17` §8), not this table. All rows live in 📐 `data/enemies.json`.

### 6.2 Elites

`Elite = base archetype × 2.2 power` plus **one Elite Modifier** drawn from:

`Enraged` (+50% ATK below 40% HP) · `Armored` (+80% DEF, −20% ASPD) · `Vampiric` (35% LS) · `Volatile` (explodes on death for 15% of hero Max HP) · `Shielded` (starts with a `WARD` equal to 30% Max HP) · `Swift` (+60% ASPD) · `Cursed` (applies a run-scoped curse on victory unless killed within 20 s) · `Reflective` (25% thorns)

Elite modifiers are shown on the pre-battle banner. The player must be able to read the threat before it starts.

🔒 **No Elite may draw the same modifier as the immediately preceding Elite in the same run** — redraw on collision. See `24_LUCK_PROTECTION.md` §4.10 B2.

**Elite identities → base archetypes 🔒** *(ruled in `16` A7 — this table is the single source of truth; `15` §E4 owns only the art identities).* Each chapter's `elitePool` is exactly its two biome elites. The modifier is still drawn per encounter, per the rules above.

| # | Elite | Biome (chapter) | Base archetype |
|---|---|---|---|
| 1 | `EL_THORN_SENTINEL` | Greenwood (1) | `WARDEN` |
| 2 | `EL_MOSSBACK_ALPHA` | Greenwood (1) | `BRUTE` |
| 3 | `EL_BOGFATHER` | Ashen Mire (2) | `CASTER` |
| 4 | `EL_MIRESTALKER` | Ashen Mire (2) | `SKIRMISHER` |
| 5 | `EL_BONE_CHOIR` | Sunken Crypt (3) | `CASTER` |
| 6 | `EL_GRAVE_TITAN` | Sunken Crypt (3) | `BRUTE` |
| 7 | `EL_MAGMA_HERALD` | Emberpeak (4) | `REAVER` |
| 8 | `EL_ASHWING` | Emberpeak (4) | `SKIRMISHER` |
| 9 | `EL_RIMEFANG_WARDEN` | Frostbound (5) | `WARDEN` |
| 10 | `EL_GLACIER_MAW` | Frostbound (5) | `BRUTE` |
| 11 | `EL_COGWRIGHT` | Clockwork (6) | `CASTER` |
| 12 | `EL_STEAMBREAKER` | Clockwork (6) | `BRUTE` |
| 13 | `EL_SPORELORD` | Bloom of Decay (7) | `CASTER` |
| 14 | `EL_ROTVINE` | Bloom of Decay (7) | `LEECH` |
| 15 | `EL_STARSCRIBE` | Astral Spire (8) | `CASTER` |
| 16 | `EL_VOIDCALF` | Astral Spire (8) | `LEECH` |

📐 Assignments authored per `16` A7; live in `data/enemies.json`. An elite is its base archetype's statline at ×2.2 power — the CASTER elites also apply their chapter's biome status (§6.1a), the WARDEN elites SUNDER, exactly as their base archetype does.

### 6.3 Bosses

Bosses use the `EnemyPower(i)` value from `02` §4.3, which **already** includes the `StageMult.Boss = 2.20` term. **Do not multiply by 2.20 again.** Bosses have **3 phases** at 100%/66%/33% HP, and each phase adds a mechanic. Boss mechanics are authored per boss, not derived.

Example — **Thornmaw** (Chapter 1):

| Phase | HP band | Mechanic |
|---|---|---|
| 1 | 100–66% | Basic attacks only. Teaches the fight rhythm. |
| 2 | 66–33% | Every 8 s: `Root` — hero ASPD −40% for 3 s |
| 3 | 33–0% | Summons 2 `SWARM` adds every 12 s; boss gains `RAGE` +30% ATK |

✅ **All 8 bosses are now fully designed in `17_BOSS_DESIGNS.md`** — three phases each, with mechanics, timings, telegraphs, counterplay by build archetype, and a universal 70-second enrage. Two small engine additions fall out of that document and are listed in its §11: a `targetPriority` field on enemy definitions, and a damage-amplification state flag.

🔒 **Per-boss statblock coefficients (hp/atk/def/aspd) are authored in `17` §1.2** — the single source of truth, including the FTUE phase-1-only variant (ruled in `16` A7).

### 6.4 Per-chapter enemy pools 🔒 *(ruled in `16` A7)*

The `enemyPool` field of each chapter's data file (`03` §4) is this weight table. A `TILE_ENEMY` battle draws **one** entry from the chapter's pool by weight (a `SWARM` draw spawns its 3 units); Elites come only from `elitePool` (§6.2). Weights per row sum to 100. 📐 All values.

| Ch | GRUNT | SWARM | BRUTE | SKIRMISHER | WARDEN | CASTER | LEECH | REAVER |
|---|---|---|---|---|---|---|---|---|
| 1 | 40 | 20 | 15 | 10 | 5 | 5 | 5 | 0 |
| 2 | 25 | 15 | 10 | 10 | 5 | 20 | 15 | 0 |
| 3 | 20 | 25 | 15 | 5 | 10 | 15 | 5 | 5 |
| 4 | 20 | 10 | 20 | 10 | 5 | 20 | 5 | 10 |
| 5 | 15 | 10 | 20 | 15 | 20 | 10 | 5 | 5 |
| 6 | 15 | 15 | 15 | 15 | 20 | 10 | 0 | 10 |
| 7 | 10 | 20 | 10 | 10 | 5 | 20 | 20 | 5 |
| 8 | 10 | 10 | 15 | 15 | 10 | 15 | 10 | 15 |

Shape intent: Chapter 1 is GRUNT-heavy with **no REAVER** (no 30%-crit spikes in the tutorial chapter); each chapter leans toward its signature (Ch2 CASTER/LEECH for the poison-and-sustain lesson, Ch3 SWARM for revive pressure, Ch5 WARDEN for the armor wall, Ch7 SWARM/LEECH fungal drain); Ch6 has **no LEECH** (machines do not drink); Chapter 8 is the hardest even mix. The same tiers reuse the same pool — tier difficulty comes from `TierMult` and `EnemyLevel`, not composition.

---

## 7. The combat log (replay format)

```csharp
public enum CombatEventType {
    BattleStart, Attack, Hit, Crit, Miss, Block, Heal, Shield,
    StatusApplied, StatusExpired, StatusTick, PetAbility,
    WardBroken,        // ward pool emptied through damage (§4.1)
    RunEffectQueued,   // a combat trigger emitted a run/board op (`18` §2.5)
    ActorDeath, PhaseChange, BattleEnd
}

public readonly struct CombatEvent {
    public int   Tick;
    public CombatEventType Type;
    public byte  SourceId;
    public byte  TargetId;
    public float Value;        // damage / heal / duration
    public ushort DataId;      // status id, ability id
}
```

`SimulationResult` = `{ bool HeroWon; int DurationTicks; float HeroHpRemaining; List<CombatEvent> Log; ulong LogHash; }`

`LogHash` is an FNV-1a hash over the serialised event list. The PvP backend compares client-reported and server-computed `LogHash` to detect tampering (`11_PVP_GHOST_DUEL.md` §6).

---

## 8. Visual presentation of a battle

- Side-view arena, hero on the left, enemies on the right, biome backdrop.
- Chibi sprites with a simple 4-frame attack animation and a hit flash + knockback nudge.
- Floating combat text: white for normal, yellow+larger for crit, green for heals, purple for DoT ticks.
- HP bars above every actor; the hero also has a large bar in the HUD.
- Pets orbit the hero and animate their ability on cast.
- Speed toggle `×1 / ×2 / ×3` persists across runs; the replay simply consumes the log faster.
- Battle intro: 0.6 s banner naming the enemy (and elite modifier), then fight.
- Victory: 0.8 s pose + reward pop, then the perk draft.

**Skip:** the player may skip to the end of a battle at any time; the outcome is already determined, so this is safe and must be offered. A skipped battle still shows the result screen.

---

## 9. Balance guardrails

The following must hold after tuning; write automated tests for them.

1. A player at exactly `ParPower(c, t)` clears `(c, t)` **62–78%** of the time — target 70%. 🔒 This is no longer just a guardrail: it is the **definition** of `ParPower` (`29_POWER_MODEL.md` §4), it is authored as a 24-cell table rather than a formula, and it is enforced as simulator assertion **A11**. A cell outside the band means either the table is mis-authored or the content behind it has drifted.
2. No single perk may increase clear rate by more than 12 percentage points in isolation.
3. No build should be able to reduce a boss fight below 12 s at par power (prevents degenerate burst).
4. No build should require more than 70 s for a boss fight at par power (prevents unwinnable stall).
5. The `mitigation` term must never exceed 0.85 for any reachable DEF value at any chapter.
6. Every stat must be worth taking: for each stat, there must exist at least one build archetype where it is top-3 by marginal power.

Implement a **headless balance harness** that runs 10,000 simulated fights per `(chapter, tier, buildArchetype)` and reports clear rate, median duration and stat elasticities. This harness is a v1 deliverable, not a nice-to-have.

🔒 **The harness's inputs are authored data, never harness-side inventions** *(ruled in `16` A7)*: the reference par build, the standard dummy statblock and the five build-archetype loadouts (crit, tank/thorns, DoT, lifesteal, pet-focused) live in `data/tuning/calibration_builds.json`, specified in `29` §2.5 — the single source of truth.

It also produces **`EmpiricalPower`** (`29` §1): the power value solved from observed time-to-kill and time-to-die against a standard dummy. Assertion **A10** requires the closed-form `PlayerPower` to track it within ±12% across every archetype and chapter band. That check is what catches the class of bug where a stat is silently missing from the aggregate — the harness measures what actually happens, the formula predicts it, and they must agree.
