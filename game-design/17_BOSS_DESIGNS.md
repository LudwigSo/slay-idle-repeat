# 17 — Boss Fight Designs (all 8)

Resolves open item P0 #3 — previously the largest authoring gap in the design.

---

## 1. Design rules that apply to every boss

| Rule | Specification |
|---|---|
| **Phases** | Exactly 3, triggered at 100%, 66% and 33% Max HP |
| **Power** | `EnemyPower(i)` from `02` §4.3, which already includes `StageMult.Boss = 2.20`. Do **not** multiply again. The power-to-stats split — per-boss hp/atk/def/aspd coefficient rows — is §1.2. |
| **Duration** | 35–60 s at par power. Balance guardrail: never below 12 s, never above 70 s (`05` §9) |
| **Telegraph** | Every damaging mechanic has a visible 1.0–1.5 s wind-up: a coloured floor marker, a charge glow, or an on-screen band. The player cannot act on it — combat is automatic — but they **must be able to read what is happening**, or the fight feels arbitrary. |
| **Counterplay** | Each boss must be beatable by at least three of the five build archetypes (crit, tank/thorns, DoT, lifesteal, pet-focused). No boss may hard-require one stat. |
| **Adds** | If a boss summons, adds use standard archetypes from `05` §6.1 at 25–35% of boss power, capped at 3 alive at once. |
| **Stun** | Bosses are immune to `STUN` and `FREEZE` in phase 3. All bosses respect the 3 s stun-immunity window in phases 1–2. |
| **Enrage** | Every boss gains a hard enrage at 70 s: +8% ATK per second, compounding. This guarantees termination without a draw. |
| **First-clear** | The first time a player fights a boss, phase 1 lasts 20% longer, to let them read the fight. |

### 1.1 Mechanic vocabulary

All boss mechanics are expressed in the effect DSL (`18_EFFECT_DSL.md`). No boss requires bespoke code.

| Mechanic type | Meaning |
|---|---|
| `PERIODIC` | Fires every N seconds from phase entry |
| `ON_PHASE_ENTER` | Fires once when the phase begins |
| `ON_HP_THRESHOLD` | Fires once when boss HP crosses a value |
| `AURA` | Continuous passive while the phase is active |
| `ON_HIT_TAKEN` | Reactive, with an internal cooldown |

### 1.2 Boss statblock coefficients 🔒 *(ruled in `16` A7 — single source of truth; `05` §6.3 points here)*

Bosses use the same `EnemyStats(power, archetype)` derivation as everything else (`05` §6), with `power = EnemyPower(bossNode)` (the 2.20 boss multiplier already inside it) and a per-boss coefficient row instead of a shared archetype. `Level = EnemyLevel(chapter, tier)` (`05` §6.0). 📐 All rows; they live in `data/bosses.json`.

| Boss | Ch | hpCoef | atkCoef | defCoef | aspdCoef | Shape rationale |
|---|---|---|---|---|---|---|
| `BOSS_THORNMAW` | 1 | 2.40 | 0.80 | 0.80 | 0.70 | Teaching boss: long, slow, forgiving. Its §2 "ASPD 0.7" is this base coefficient, not a phase aura. |
| `BOSS_GULGROT` | 2 | 2.50 | 0.85 | 0.70 | 0.75 | Poison does the killing; direct hits stay soft. |
| `BOSS_OSSUARY_KING` | 3 | 2.10 | 0.90 | 1.00 | 0.80 | Rise Again's 25% refill makes effective HP ≈ 2.63 — the visible bar is deliberately shorter. |
| `BOSS_CINDERMAW` | 4 | 2.40 | 1.05 | 1.10 | 0.75 | The first real DPS check. |
| `BOSS_RIMEHOLD` | 5 | 2.80 | 0.95 | 1.30 | 0.60 | Slowest, hardest shell; the Core ×1.6 window is the answer. |
| `BOSS_COGITATOR_PRIME` | 6 | 2.20 | 0.90 | 0.85 | 1.00 | Starts modest — Escalation compounds it. |
| `BOSS_SPOREQUEEN_VELL` | 7 | 2.50 | 0.90 | 0.75 | 0.90 | The Rot drain and SPORE stacks are the pressure, not ATK. |
| `BOSS_DICELORD` | 8 | 2.60 | 1.00 | 0.95 | 0.90 | The rounded finale: no soft stat. |
| `BOSS_FTUE` | — | 2.40 | 0.80 | 0.80 | 0.70 | The FTUE beat-7 mini-boss (phase 1 only). **Fixed authored inputs: `power = 900`, `Level = 1`** 📐 — it does not use `EnemyPower(i)`. Coefficients mirror Thornmaw's row, matching the ⚠️ **O36** default skin (Thornmaw phase 1 — `16` B4). **This row is the single statblock authority**, referenced by `ftue.json` (`19` Part D §D4.1); the binding arbiter for its tuning is `19` D8's T3 band, not the §1 duration rule. |

**Secondary stats:** every boss uses the baseline — CRIT 0.05, CDMG 0.50, DODGE 0, LS 0, BLOCK 0, PEN 0, DMG%/DR%/THORN 0, HEAL% 1.0. Anything else (Gulgrot's 30% lifesteal, Dicelord's every-6th auto-crit) is a **phase mechanic in the fight scripts below**, never a base stat — so the pre-battle power readout stays honest.

**Tuning authority:** the §1 duration rule (35–60 s at par) and `05` §9's A11 band are the arbiters — the harness re-tunes these rows, the doc records intent.

---

## 2. Chapter 1 — **Thornmaw**
*A colossal carnivorous flower rooted in mossy stone.*

**Role:** the teaching boss. Must be beatable by a player with a bad build and no gear.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Basking** | 100–66% | Basic attacks only, `ASPD 0.7`. Nothing else. Teaches the rhythm and lets the player watch their own build work. |
| **2 — Snapping** | 66–33% | `PERIODIC 8s` — **Root**: hero `ASPD −40%` for 3 s. Telegraph: vines coil around the hero's feet 1.2 s before. |
| **3 — Bloom** | 33–0% | `ON_PHASE_ENTER` — summons 2 `SWARM` adds. `PERIODIC 12s` — summons 2 more (max 3 alive). `AURA` — boss gains `RAGE +30% ATK`. |

**Counterplay:** any AoE or cleave trivialises phase 3; single-target builds simply out-damage the boss instead. Root is annoying, never lethal.
**Failure mode to avoid:** Root stacking with itself. It must refresh, not stack.

---

## 3. Chapter 2 — **Gulgrot**
*A bloated toad shaman in a bone crown.*

**Role:** teaches that sustain matters. This is the first boss that punishes a player with zero healing.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Croak** | 100–66% | Basic attacks apply `POISON` (1 stack, 2% Max HP/s, 4 s, max 3 stacks). |
| **2 — Miasma** | 66–33% | `AURA` — **Bog Air**: hero `HEAL% −35%` while in this phase. `PERIODIC 10s` — **Belch**: applies 2 `POISON` stacks instantly. Telegraph: a green cloud swells around the boss for 1.2 s. |
| **3 — Gorge** | 33–0% | `AURA` — boss gains **30% Lifesteal**. `ON_HIT_TAKEN (cd 6s)` — **Spit**: 120% ATK burst and 1 `POISON` stack. |

**Counterplay:** poison ignores DEF, so tanks must bring lifesteal or regen; DoT builds race it; crit builds burst phase 3 before lifesteal matters. The `PK_HEALERS_TOUCH` and `Mending` talent both directly answer Bog Air.
**Tuning note:** total poison DPS at par power must stay under 6% Max HP/s or the fight becomes a pure sustain check.

---

## 4. Chapter 3 — **Ossuary King**
*A crowned skeleton on a throne of skulls.*

**Role:** teaches burst windows. The chapter's signature ("enemies resurrect once at 20% HP") appears here as a boss mechanic.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Court** | 100–66% | `ON_PHASE_ENTER` — summons 2 `GRUNT` skeletons. `PERIODIC 15s` — resummons any dead ones. |
| **2 — Bone Shield** | 66–33% | `PERIODIC 14s` — **Ossify**: boss gains a `WARD` equal to 20% of its Max HP and `DR% +30%` until the ward breaks or 6 s pass. Telegraph: ribcage plates snap shut with a hard sound cue. |
| **3 — Second Death** | 33–0% | `ON_HP_THRESHOLD 1%` — **Rise Again** (once): boss returns to 25% HP, `ATK +40%`, `ASPD +25%`, and all adds are cleared permanently. |

**Counterplay:** the Ward rewards sustained DPS over burst in phase 2, and Rise Again rewards keeping cooldowns and lifesteal available rather than dumping everything. `PK_EXECUTIONER` is a trap here unless the player accounts for the second health bar.
**Player-facing clarity:** Rise Again must be unmistakable — full-screen flash, the HP bar visibly refilling, and the boss nameplate changing to "Ossuary King, Risen".

---

## 5. Chapter 4 — **Cindermaw**
*A magma drake with obsidian plating.*

**Role:** the first real DPS check, and the introduction of a mechanic that punishes pure-defence builds.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Smoulder** | 100–66% | Basic attacks apply `BURN` (8% boss ATK/s, 3 s, stacks to 5). |
| **2 — Eruption** | 66–33% | `PERIODIC 9s` — **Magma Vent**: 180% ATK to the hero and refreshes all `BURN` stacks. Telegraph: the floor under the hero glows orange for 1.3 s. `AURA` — boss `DR% +20%`. |
| **3 — Molten Core** | 33–0% | `AURA` — **Overheat**: boss `ATK +60%`, `DEF −40%`. The boss becomes a glass cannon; the fight becomes a race. `PERIODIC 7s` — Magma Vent continues. |

**Counterplay:** phase 3's `DEF −40%` is a deliberate gift — it means a player who survives to 33% almost always wins, so the fight's tension is front-loaded rather than a slow loss. Burn is answered by Max HP, `Steadfast` (debuff duration), or simply killing faster.
**Failure mode to avoid:** a pure turtle build that survives everything but cannot out-damage the 70 s enrage. Verify with the balance harness that a max-defence, min-offence build still clears at par power.

---

## 6. Chapter 5 — **Rimehold**
*A glacier-slab ice golem with a glowing core.*

**Role:** attack-speed disruption. Tests whether the player's damage survives losing tempo.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Frostbite** | 100–66% | `PERIODIC 12s` — **Chill**: hero `ASPD −25%` for 4 s. |
| **2 — Glacial Armour** | 66–33% | `AURA` — boss `DEF +60%`. `ON_HIT_TAKEN (cd 10s)` — **Shatterback**: reflects 25% of the hit and applies `FREEZE` 1.5 s. `ON_PHASE_ENTER` — the boss's exposed **Core** becomes targetable: hits deal ×1.6 damage but the armour aura persists. |
| **3 — Avalanche** | 33–0% | `PERIODIC 10s` — **Collapse**: 220% ATK, and 2 `SWARM` ice shards spawn. `AURA` — boss `ASPD +40%`. |

**Counterplay:** the Core in phase 2 rewards armor penetration and crit; Shatterback punishes very fast attackers, so `Steadfast` and high Max HP shine. Attack-speed builds struggle most, which is intentional — this is the chapter where the player learns that one stat cannot carry everything.
⚠️ **NEEDS DETAIL:** "Core becomes targetable" implies a second target in a game where targeting is automatic (`05` §3.2). **Ruling:** the Core is not a separate actor. It is a state flag on the boss that multiplies incoming damage by 1.6 and is visually indicated by a glowing chest. No targeting logic changes.

---

## 7. Chapter 6 — **Cogitator Prime**
*A brass spider automaton with a single glass eye.*

**Role:** a timer fight. The chapter's signature (enemies gain stats over time) becomes the boss's identity.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Calibrating** | 100–66% | `AURA` — **Escalation**: boss gains `+2% ATK and +2% ASPD every 5 s`, compounding, and never resets for the whole fight. |
| **2 — Countermeasures** | 66–33% | `ON_PHASE_ENTER` — summons 2 `WARDEN` drones that apply `SUNDER` to the hero. `PERIODIC 16s` — **Recalibrate**: the boss copies the hero's **highest** current stat percentage bonus and gains half of it for 10 s. Telegraph: the eye lens scans the hero with a visible beam. |
| **3 — Overclock** | 33–0% | `AURA` — Escalation rate doubles to `+4% per 5 s`. `PERIODIC 8s` — **Piston Slam**: 200% ATK, ignores 40% DEF. |

**Counterplay:** this is the game's clearest "your build must actually be strong enough" check — stalling loses. Recalibrate makes hyper-specialised builds slightly self-defeating, nudging players toward breadth. Escalation means the enrage timer effectively arrives early, so a slow tank build must bring real damage.
**Tuning note:** at par power the fight should end around 45 s, i.e. before Escalation exceeds +36% ATK. If the harness shows median duration above 60 s, the boss is over-tuned.

---

## 8. Chapter 7 — **Sporequeen Vell**
*A regal fungal queen with a glowing mushroom crown.*

**Role:** the anti-healing and debuff-stacking fight. Punishes players who rely on a single sustain source.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — Pollination** | 100–66% | `PERIODIC 6s` — applies 1 `SPORE` stack (hero `HEAL% −12%` each, max 4). Stacks persist through the whole fight and never expire. |
| **2 — Bloom Court** | 66–33% | `ON_PHASE_ENTER` — summons 2 `CASTER` sporelings that also apply `SPORE`. `AURA` — the boss heals **4% of its Max HP** each time a sporeling dies. `PERIODIC 12s` — **Burst Cap**: 150% ATK AoE, applies `POISON` ×2. |
| **3 — Rot** | 33–0% | `AURA` — hero takes `1.5% Max HP` true damage per second, unmitigable. `PERIODIC 10s` — resummons 1 sporeling. |

**Counterplay:** the adds are a trap — killing them heals the boss. A player who ignores them and focuses the queen wins faster, which is a genuinely interesting decision in an auto-battler where targeting is automatic.
⚠️ **NEEDS DETAIL / IMPORTANT:** the hero targets the **lowest-HP enemy** (`05` §3.2), so the player *cannot* choose to ignore the adds. **Ruling required.** Two options: (a) sporelings spawn with higher HP than the boss's current HP so targeting naturally prefers the boss, or (b) add a `TAUNT_IMMUNE` / `LOW_PRIORITY` flag to the targeting rules for this encounter. **Recommendation: (b)** — add a `targetPriority` integer to enemy definitions, defaulting to 0, and give sporelings −1. This is a small, general addition to the targeting system and it makes several future designs possible.
**Phase 3 note:** the unmitigable drain sets a hard soft-timer of roughly 66 s, on top of the 70 s enrage. Verify these do not interact confusingly.

---

## 9. Chapter 8 — **The Dicelord**
*A masked cosmic figure in a starfield cloak, holding a giant golden die.*

**Role:** the finale. The only boss whose mechanics touch the dice system, closing the loop on the game's signature.

| Phase | HP band | Mechanics |
|---|---|---|
| **1 — The Wager** | 100–66% | `PERIODIC 10s` — **Roll of Fate**: the boss rolls a d6, visibly, on screen. 1–2: boss gains `ATK +25%` for 8 s. 3–4: hero gains `ATK +25%` for 8 s. 5–6: both gain `ASPD +30%` for 8 s. Genuinely fair, and it makes the player watch a die roll during the climax of the game. |
| **2 — Loaded Dice** | 66–33% | `AURA` — Roll of Fate now rolls **only 1–3** (never in the hero's favour on 3–4... see ruling below). 🔴 **Scramble is removed** (⚠️ Removed with the die's special faces and the reroll (`04` §5, `16` D41).): a `PERIODIC 14s` replacement of one of the hero's six die faces with `Void`, persisting into the run and making a fast kill genuinely valuable. It was also `18` §2.5's sanctioned combat-context exception. **Phase 2 is now the aura alone**, which is a materially weaker phase, and the Dicelord — a boss named for die manipulation — is owed a redesign. |
| **3 — House Always Wins** | 33–0% | `ON_PHASE_ENTER` — the boss gains a `WARD` equal to 25% Max HP and `Thorns 30%`. `PERIODIC 8s` — **All In**: 300% ATK single hit, telegraphed 1.5 s. `AURA` — every 6th attack the boss makes is an automatic critical hit for ×3. |

⚠️ **NEEDS DETAIL:** phase 2's "rolls only 1–3" is ambiguous against phase 1's table, where 3 favours the hero. **Ruling:** in phase 2 the outcome table changes to `1–4: boss buff, 5–6: both buff` — the hero-favourable outcome is simply removed. Cleaner to read and to implement.

**Counterplay:** phase 3's Thorns punishes attack-speed builds, All In punishes low Max HP, and the guaranteed crit punishes low DR. It is deliberately the one fight that demands a rounded build rather than a spike. Scramble creates a real incentive to bring burst damage even for tank players.
**Presentation:** this fight gets the game's only bespoke set-piece treatment — the arena is the Astral Spire platform, the boss's die is rendered at 3× the size of the player's, and the victory screen is the only one with a unique background.

---

## 10. Boss reward table

| Chapter | Soul Shards (Normal) | Gear drops | First-clear bonus |
|---|---|---|---|
| 1 | 15 | 2 items, B-capped | 100 Soul Shards, 1 A item, +2 Talent Points, unlock Ch. 2 |
| 2 | 30 | 2 items, B-capped | 150, 1 A item, +2 TP, unlock Ch. 3 |
| 3 | 45 | 2 items, A-capped | 220, 1 A item, +2 TP, unlock Ch. 4 |
| 4 | 60 | 3 items, A-capped | 320, 1 S item, +2 TP, unlock Ch. 5 |
| 5 | 75 | 3 items, A-capped | 450, 1 S item, +2 TP, unlock Ch. 6 |
| 6 | 90 | 3 items, S-capped | 580, 1 S item, +2 TP, unlock Ch. 7 |
| 7 | 105 | 3 items, S-capped | 700, 1 S item, +2 TP, unlock Ch. 8 |
| 8 | 120 | 3 items, SS-capped | 800, 1 SS item, +2 TP, **campaign complete** (no further chapter to unlock) |

Heroic pays ×2.7 Soul Shards and +3 Talent Points on first clear; Mythic ×6.7 and +5.

📐 TUNABLE.

---

## 11. Implementation checklist

- [ ] `targetPriority` field added to enemy definitions (required by Sporequeen — §8)
- [ ] `Core` state flag for damage-amplification states (required by Rimehold — §6)
- [ ] Phase transition events emitted into the combat log for the UI band
- [ ] Telegraph events emitted 1.0–1.5 s ahead of every damaging mechanic
- [ ] Universal 70 s enrage implemented once, applied to all bosses
- [ ] First-clear phase-1 extension flag
- [ ] All 8 bosses expressed purely in the effect DSL — **zero bespoke boss code**
- [ ] Balance harness run per boss per tier: clear rate ~70% at par power, median duration 35–60 s
