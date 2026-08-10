# 05 — Combat Simulation

🔒 LOCKED: Full auto-battle. The player has **zero inputs during a fight** other than speed control and (on death) the revive prompt. All agency is upstream: gear, talents, pets, mount, perks drafted this run.

This is the most important document for the implementer. The combat simulator must be:

1. **Deterministic** — `Simulate(seed, heroSnapshot, enemySnapshot)` returns an identical result every time, on any device, on the server.
2. **Engine-independent** — a pure C# library with no Godot references, so it can run headless for PvP validation and for balance sweeps.
3. **Fast** — a full 60-second fight must simulate in < 5 ms so PvP results can be computed instantly and balance sweeps can run thousands of fights.

The visual battle is a **replay of a pre-computed log**, not a live simulation. Compute first, then animate.

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
- Rounding: keep everything as `double` internally; round only for display.

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

where L = Legend Level (1..200)
```

Gear, talents, pets and mounts then multiply these. At Legend Level 60 with mid-tier gear the hero should sit near `ChapterPowerTarget(5)`.

📐 TUNABLE.

---

## 3. Simulation model

A **fixed-tick** simulation.

| Property | Value |
|---|---|
| Tick rate | 20 ticks/second (`TICK = 0.05 s`) |
| Max fight duration | 90 s = 1800 ticks. On timeout, the side with the higher **remaining HP fraction** wins. |
| Actors | Hero (1), Pets (0–3), Mount (passive only, no actor), Enemies (1–5) |

### 3.1 Tick order (strict)

```
for tick in 0..maxTicks:
    1. Advance all status effect timers; apply DoT/HoT ticks
    2. Resolve any expiring buffs/debuffs
    3. For each actor in initiative order (Hero, Pets by slot, Enemies by index):
         a. if actor.attackCooldown <= 0 and actor.alive:
              - select target (see 3.2)
              - resolve attack (see 4)
              - fire on-hit / on-crit / on-kill triggers
              - actor.attackCooldown = 1.0 / actor.ASPD
         b. actor.attackCooldown -= TICK
    4. Resolve pet ability cooldowns and fire ready abilities
    5. Check death conditions; remove dead actors; fire on-death triggers
    6. Append a CombatEvent to the log for every state change
    7. if hero dead OR all enemies dead: break
```

**Initiative is fixed, not randomised.** This removes a whole class of nondeterminism.

### 3.2 Targeting

- Hero targets the enemy with the highest `targetPriority`, breaking ties by **lowest current HP**. `targetPriority` defaults to `0`; a value of `-1` makes an enemy deprioritised (used by Sporequeen Vell's sporelings — `17` §8), `+1` forces focus.
- Pets target the enemy with the **highest current HP** (so pets chip the tanky one while the hero cleans up).
- Enemies always target the Hero. **Pets cannot be targeted or killed.** They are stat/effect modules with visual presence, not units to protect. 🔒 This keeps the single-hero fantasy intact and removes a large balancing surface.

---

## 4. Damage resolution

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

    // 6. Flat damage reduction
    dmg *= (1 - defender.DRPct)

    // 7. Floor: never less than 10% of raw
    dmg = Max(dmg, raw * 0.10)

    // 8. Apply
    defender.HP -= dmg
    if attacker.LS > 0: attacker.Heal(dmg * attacker.LS * attacker.HEALPct)
    if defender.THORN > 0: attacker.HP -= dmg * defender.THORN

    log(HIT, attacker, defender, dmg, isCrit)
```

**Mitigation curve sanity check:** with `DEF = 120` and `attackerLevel = 1`, mitigation = `120/(120+140)` = 0.46. With `DEF = 600`, mitigation = `600/(600+140)` = 0.81. The `20 * attackerLevel` term means defense must keep growing to stay relevant — this is intentional and is what makes gear upgrades feel necessary rather than optional.

📐 TUNABLE: the `120` and `20` constants are the two most important balance dials in the game. Expose them in data.

---

## 5. Status effects

| ID | Type | Effect |
|---|---|---|
| `BURN` | DoT | `X%` of attacker ATK per second for `D` s. Stacks to 5. |
| `POISON` | DoT | `X%` of target Max HP per second, `D` s. Stacks to 3. Ignores DEF. |
| `BLEED` | DoT | Flat damage per second, amplified by target's missing HP. |
| `FREEZE` | Debuff | −50% ASPD for `D` s |
| `STUN` | Debuff | Cannot act for `D` s. Max 1.5 s per application, with a 3 s immunity window after. |
| `WEAKEN` | Debuff | −X% ATK |
| `SUNDER` | Debuff | −X% DEF, stacks to 5 |
| `SPORE` | Debuff | −X% healing received, stacks to 4 (Chapter 7 signature) |
| `RAGE` | Buff | +X% ATK, decays over `D` s |
| `WARD` | Buff | Absorb shield, flat HP amount |
| `HASTE` | Buff | +X% ASPD |
| `REGEN` | HoT | Heal X% Max HP per second |

**Stun immunity** is mandatory. Without it, stun-locking becomes the only viable build.

---

## 6. Enemy stat derivation from Power

Enemies are not hand-statted. They are derived from the chapter `Power` value and an archetype coefficient set. This keeps 8 chapters × 3 tiers × 64 enemy variants authorable.

```
EnemyStats(power, archetype):
    MaxHP = power * 0.60 * archetype.hpCoef
    ATK   = power * 0.045 * archetype.atkCoef
    DEF   = power * 0.030 * archetype.defCoef
    ASPD  = 1.00 * archetype.aspdCoef
    CRIT  = archetype.crit
    ...
```

### 6.1 Enemy archetypes (8 base shapes, reskinned per biome)

| Archetype | hpCoef | atkCoef | defCoef | aspdCoef | Behaviour flavour |
|---|---|---|---|---|---|
| `GRUNT` | 1.00 | 1.00 | 1.00 | 1.00 | Baseline |
| `SWARM` (×3 units) | 0.35 | 0.55 | 0.60 | 1.30 | Three weak bodies; punishes single-target |
| `BRUTE` | 2.00 | 1.35 | 1.20 | 0.60 | Slow heavy hitter |
| `SKIRMISHER` | 0.70 | 0.85 | 0.70 | 1.70 | Fast, high dodge (0.15) |
| `WARDEN` | 1.60 | 0.70 | 2.20 | 0.85 | Tanky, applies `SUNDER` |
| `CASTER` | 0.75 | 1.50 | 0.55 | 0.70 | Applies a biome DoT on hit |
| `LEECH` | 1.10 | 0.95 | 0.90 | 1.10 | 25% lifesteal |
| `REAVER` | 0.90 | 1.20 | 0.80 | 1.00 | 30% crit, 1.2 crit damage |

### 6.2 Elites

`Elite = base archetype × 2.2 power` plus **one Elite Modifier** drawn from:

`Enraged` (+50% ATK below 40% HP) · `Armored` (+80% DEF, −20% ASPD) · `Vampiric` (35% LS) · `Volatile` (explodes on death for 15% of hero Max HP) · `Shielded` (starts with a `WARD` equal to 30% Max HP) · `Swift` (+60% ASPD) · `Cursed` (applies a run-scoped curse on victory unless killed within 20 s) · `Reflective` (25% thorns)

Elite modifiers are shown on the pre-battle banner. The player must be able to read the threat before it starts.

### 6.3 Bosses

Bosses use the `EnemyPower(i)` value from `02` §4.3, which **already** includes the `StageMult.Boss = 2.20` term. **Do not multiply by 2.20 again.** Bosses have **3 phases** at 100%/66%/33% HP, and each phase adds a mechanic. Boss mechanics are authored per boss, not derived.

Example — **Thornmaw** (Chapter 1):

| Phase | HP band | Mechanic |
|---|---|---|
| 1 | 100–66% | Basic attacks only. Teaches the fight rhythm. |
| 2 | 66–33% | Every 8 s: `Root` — hero ASPD −40% for 3 s |
| 3 | 33–0% | Summons 2 `SWARM` adds every 12 s; boss gains `RAGE` +30% ATK |

✅ **All 8 bosses are now fully designed in `17_BOSS_DESIGNS.md`** — three phases each, with mechanics, timings, telegraphs, counterplay by build archetype, and a universal 70-second enrage. Two small engine additions fall out of that document and are listed in its §11: a `targetPriority` field on enemy definitions, and a damage-amplification state flag.

---

## 7. The combat log (replay format)

```csharp
public enum CombatEventType {
    BattleStart, Attack, Hit, Crit, Miss, Block, Heal, Shield,
    StatusApplied, StatusExpired, StatusTick, PetAbility,
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

1. A player at exactly `ChapterPowerTarget(c)` should clear chapter `c` Normal **~70%** of the time.
2. No single perk may increase clear rate by more than 12 percentage points in isolation.
3. No build should be able to reduce a boss fight below 12 s at par power (prevents degenerate burst).
4. No build should require more than 70 s for a boss fight at par power (prevents unwinnable stall).
5. The `mitigation` term must never exceed 0.85 for any reachable DEF value at any chapter.
6. Every stat must be worth taking: for each stat, there must exist at least one build archetype where it is top-3 by marginal power.

Implement a **headless balance harness** that runs 10,000 simulated fights per `(chapter, tier, buildArchetype)` and reports clear rate, median duration and stat elasticities. This harness is a v1 deliverable, not a nice-to-have.
