# Handover — M2 carry-forwards R1–R4

**For:** an agent implementing all four of M2's carried-forward findings.
**Written by:** the M2 `milestone-review` conductor, 2026-08-14.
**Status of M2:** 17/17 tasks merged and reviewed; the milestone is **🔄, not signed off**, precisely
because of these four. Fixing R2 + R3 is what makes exit criterion ③ verifiable.

This document is **self-contained**. You should not need to read any prior conversation. Every claim
below was verified against the repository at `review/M2` — but verify anything you rely on, because
during M2 **eight of ten wrong statements came from the conductor's own prompts, and every one was a
claim about the repo rather than about a design document** (see steering S17). If this document
disagrees with the code or the docs, **they win** — and say so in your report.

---

## 0. Where you are

**Branch:** `review/M2` (the reviewed milestone branch; `milestone/M2` is its unreviewed parent).
**Base of the milestone:** `c7359cf`. Diff range for context: `c7359cf..review/M2`.

**Verified state, run by the conductor immediately before writing this:**

```
0 Warnung(en) / 0 Fehler
Core.Tests          3015
Application.Tests    555
Contract.Tests        24
Architecture.Tests    79
content validation   OK   (📐 baseline 38)
```

🔒 **`milestone/M2` and `review/M2` merge to `main` only after M1 is merged and verified.** M2 was
built as a parallel milestone session alongside an unfinished M1. Do not merge either branch.

**What M2 is:** the `18` effect DSL and the `05` combat simulator — 44 ops, 23 triggers, 23
conditions, 11 targets, a fixed-tick engine, damage and wards, 12 statuses, enemy derivation, a boss
engine, nine boss scripts authored as data, PvP duels, a combat log with `LogHash`, a determinism
baseline, and a balance harness.

**Suggested order: R3 → R2 → R1 → R4.** R3 is the only one blocking an exit criterion, R2 is small and
independent, R1 is the largest and touches what R3 and R2 sit on, R4 is a design decision you may be
told to leave.

---

## 1. R3 — 4 of 9 bosses fault before phase 3 🔴 *(do this first)*

**Why it matters:** M2's exit criterion ③ is *"all 8 bosses run from DSL data with zero bespoke
code."* The zero-bespoke-code half is **proved** (an IL scan asserts no `ldstr` beginning `BOSS_`
anywhere under `Core.Rules`). The *run* half is not: **4 of the 9 authored bosses fault.** There is
also **no test that runs the authored bosses to completion** — a `[Theory]` over the nine would go red
on four rows today, which is why nobody added one.

There are two independent causes.

### 1a. `CURRENT_TARGET` in a boss `PERIODIC` cannot resolve

**Exactly eight effects, in four bosses.** Verified by reading
`game-data/content/bosses/bosses.json`:

| Boss | Effect id | Op | Trigger |
|---|---|---|---|
| `BOSS_CINDERMAW` | `BOSS_CINDERMAW_P2_MAGMA_VENT` | `DAMAGE` | `PERIODIC` |
| `BOSS_CINDERMAW` | `BOSS_CINDERMAW_P2_VENT_REFRESH` | `EXTEND_STATUS` | `PERIODIC` |
| `BOSS_CINDERMAW` | `BOSS_CINDERMAW_P3_MAGMA_VENT` | `DAMAGE` | `PERIODIC` |
| `BOSS_CINDERMAW` | `BOSS_CINDERMAW_P3_VENT_REFRESH` | `EXTEND_STATUS` | `PERIODIC` |
| `BOSS_RIMEHOLD` | `BOSS_RIMEHOLD_P3_COLLAPSE` | `DAMAGE` | `PERIODIC` |
| `BOSS_COGITATOR_PRIME` | `BOSS_COGITATOR_PRIME_P2_RECALIBRATE` | `STAT_COPY` | `PERIODIC` |
| `BOSS_COGITATOR_PRIME` | `BOSS_COGITATOR_PRIME_P3_PISTON_SLAM` | `DAMAGE` | `PERIODIC` |
| `BOSS_DICELORD` | `BOSS_DICELORD_P3_ALL_IN` | `DAMAGE` | `PERIODIC` |

⚠️ **Twelve boss effects use `CURRENT_TARGET` in total. The other four are on `ON_HIT` /
`ON_HIT_TAKEN` and are correct** — those contexts genuinely carry a current target. **Do not change
them.** Only the eight above, whose trigger has no attack context, fault.

**The engine is behaving exactly as specified.** M2-05 established the uniform rule for `18` §5's
tokens, and it is the repo's convention:

> A token whose **subject** is absent from the context throws `EffectContextException`. A token whose
> subject is present but whose **set** is empty resolves to the empty set.

That rule exists because steering **S6** forbids coercing a hole to a default at read time, and
because a silent skip is byte-identical to a legitimately empty target set. `18` §5 authors a
degradation for only two of eleven tokens (`OTHER_ENEMIES` → `ALL_ENEMIES` outside an attack context;
`OWNER` on a non-summon → skipped) and says nothing about `CURRENT_TARGET`.

**🔴 This needs a ruling, and there are two defensible options. Take one, state which, and say why.**

**Option A — fix the data (8 edits, no engine change).**
`17` describes every one of these as hitting *"the hero"*. Under conductor ruling **R10** — *every
target token resolves relative to the effect's **holder*** — a boss's hero-side tokens are
`ALL_ENEMIES`, `LOWEST_HP_ENEMY`, `HIGHEST_HP_ENEMY`, `RANDOM_ENEMY`. Since `05` §3.2 makes pets
untargetable and unkillable, a boss's `ALL_ENEMIES` resolves to the hero alone.
⚠️ **`BOSS_COGITATOR_PRIME_P2_RECALIBRATE` needs care**: ruling **R13** says `STAT_COPY`'s `target`
names the **copy source** and writes onto the **holder** — an inversion `18` never flags. It needs a
token that names exactly one actor, not a set.

**Option B — give `CURRENT_TARGET` a meaning for a non-hero actor (1 engine change).**
`05` §3.2: *"Enemies always target the Hero."* So an enemy actor's current target is never ambiguous,
in or out of an attack context. Resolving `CURRENT_TARGET` on an enemy holder to the hero is arguably
what `18` §5 always meant, and it fixes all eight at once plus anything M3+ authors the same way.
⚠️ It weakens M2-05's uniform rule, and that rule is load-bearing — argue explicitly why this token is
the exception if you take this route.

### 1b. A value-less `APPLY_STATUS` is refused before the status can supply its own potency

**Exactly one effect:** `BOSS_RIMEHOLD_P2_SHATTERBACK_FREEZE` (`statusId: FREEZE`, no `value`).

**This is an engine gap, not a data gap.** M2-13 authored it correctly against all three sources:
`05` §5 states `FREEZE`'s potency as a literal **−50 % ASPD**; `game-data/content/statuses.json`
encodes `FREEZE.fixedPotency = -0.5`; and `StatusTimeline` (around line 356) already reads
`definition.FixedPotency ?? …`, with a comment stating *"the number is the status's and not the
applying effect's."*

The refusal happens **earlier**, in `ValueScaleEvaluator.Value`
(`src/SlayIdleRepeat.Core/Rules/Effects/Values/ValueScaleEvaluator.cs`, ~line 95):

```csharp
private static double Value(EffectDefinition effect) =>
    effect.Value ?? throw new EffectContextException(
        effect.Id, $"it is a {effect.Op} with no value", …);
```

🔒 **That guard is correct and must not simply be deleted.** Its remark is right: for `18` §2.1's stat
ops an absent value is *"a hole, not a zero"*, and reading it as 0 makes the effect a silent no-op
that is still visible in the data (steering S6). **The bug is that it never excepts the case where the
status itself supplies the number.** Narrow the exception to exactly that case — an `APPLY_STATUS`
whose `statusId` resolves to a definition carrying a `FixedPotency` — and leave every other op
refusing as it does now. **Prove the guard still bites** for a value-less stat op.

### R3 acceptance

1. All nine authored bosses run to completion. **Add the `[Theory]` over the nine** that nobody could
   add before — it is the regression test for this whole item.
2. The `05` §5 `FREEZE` potency actually applied (−50 % ASPD), asserted numerically.
3. `ValueScaleEvaluator`'s guard still refuses a value-less **stat** op — proved by mutation.
4. Exit criterion ③ re-verified and reported.
5. **Re-run the balance harness** (`tools/BalanceHarness`, `sweep` and `fast`). Nothing has ever
   reached boss phase 2, so guardrails 3 and 4 have never had data. Report whether the picture moves.

---

## 2. R2 — `ON_LETHAL` is never fired

**What is true today.** `TriggerKind.ON_LETHAL = 14` is declared
(`src/SlayIdleRepeat.Core/Content/Effects/TriggerKind.cs`) and registered in
`TriggerCatalogue` (~line 369) as `TriggerLayer.COMBAT` with parameter `ONCE`. **Nothing raises it.**

The death-save *mechanism* is now wired — M2's cross-task review found
`CombatFlowState.ConsumeDeathSave` had no production caller at all and fixed it, so
`AttackPipeline` (~line 487) and `BattleSimulation` (~line 791) now consume saves. **But the trigger
moment is still missing**: a save is consumed without `ON_LETHAL` ever firing.

**Consequences, both real:**
- `18` §7.4's `PK_UNBREAKABLE` — the document's own worked example — **cannot be authored in its
  documented shape.** `once` is admitted only on `ON_LETHAL` and `ON_LOW_HP`.
- `05` §3.1's anti-loop rule — *"`SURVIVE_LETHAL` / `REVIVE` effects fire at most their authored
  `once` count per battle"* — is unobservable end to end.

**What `18` §3 gives you:** `ON_LETHAL` — *"Would take fatal damage"*, parameter `once`.

**The one judgement call:** where in `05` §4's step order the moment sits. Step 9 is
`dmg = defender.Wards.Absorb(dmg); defender.HP -= dmg`. The trigger must fire when the hit *would* be
fatal — i.e. after ward absorption (a fully absorbed hit is not lethal) and before HP is written.
State your placement and cite the step.

⚠️ `05` §3.1's anti-loop rules are in force: a `SURVIVE_LETHAL`/`REVIVE` fires **at most its authored
`once` count per battle**, and reflected (thorns) damage never triggers the victim's thorns.

**Acceptance:** `PK_UNBREAKABLE` authorable in `18` §7.4's exact shape and proved to work end to end;
`once` proved to bound firing; the anti-loop rule proved by a case that would otherwise recurse.

---

## 3. R1 — `SYS_ENRAGE` raises the boss's ATK by nothing 🔴 *(the largest)*

**This is the most consequential of the four and the least visible.** The universal 70 s enrage fires
on schedule, anchors correctly (conductor ruling R8, proved by test), and **has no effect on the
boss's ATK.**

**Why.** `SYS_ENRAGE` is a *triggered* `PERIODIC` → `STAT_MULT ATK ×1.08`. `BattleSimulation.RefreshStats`
(~line 1353) aggregates **untriggered standing effects only**. Its own remark states the boundary and
the gap, verbatim from the code:

> 🔒 UNTRIGGERED effects only, and this is the `18` §8 step 1 boundary rather than an omission. §1.1
> splits the two: an untriggered effect is a standing modifier re-evaluated "at every resolution
> pass", while a triggered one applies "at fire time" and lives for its `18` §6 duration afterwards.
> Aggregating a triggered effect here would apply SYS_ENRAGE's ×1.08 from tick 0 — 70 seconds early,
> and exactly once instead of once a second.
>
> ⚠️ THE OTHER HALF IS M2-02'S AND IS NOT WIRED HERE. `18` §8 step 1 is "collect all ACTIVE effects",
> which includes a triggered effect that has fired and whose duration has not ended — that set is
> `EffectResolver`'s (M2-02) over M2-06's `EffectStackSet` … A fired stat op therefore reaches
> `EffectOpResolver` and changes nothing that outlives the call.

**So this is not only about the enrage.** *Every* fired stat op is inert: every `STAT_ADD_PCT`,
`STAT_MULT` or `STAT_SET` applied by a trigger — boss auras that grant `RAGE`, Cindermaw's Overheat,
Cogitator's Escalation, Rimehold's Glacial Armour, every future perk that buffs on hit.

🔴 **Three seam documents justify their whole design around behaviour the assembled code does not
produce.** That includes conductor ruling **R1** (`STAT_MULT`'s value *is* the multiplier, not
`1 + value`), whose worked example was three seconds of `SYS_ENRAGE` reaching **125.9712**. R1 is
correct and is implemented correctly in `StatAggregation` — it is simply never reached by a fired op.

**What the work is.** `18` §8 step 1's other half: a fired triggered effect with a `18` §6 duration
joins the standing set until it expires, and `RefreshStats` aggregates that set too. The pieces exist
— M2-02's `EffectResolver` and M2-06's `EffectStackSet` — and were built by different tasks that never
met. **This is design-and-build, not a patch.**

**Constraints you must not break:**
- The performance note in `RefreshStats` is load-bearing: `18` §8 over nine actors × 1800 ticks is the
  whole of `05`'s **< 5 ms** budget. Re-aggregation is deliberately conditional on
  `StatsAreStale || StatsDependOnLiveState`. **Measure after.** The current real-content figure is
  median **2.881 ms**, p90 3.681 (`05` headnote budget is 5 ms).
- `SYS_ENRAGE` is `BATTLE` scope, multiplicative, **uncapped**, `maxStacks: null`, anchored at battle
  start with `startDelay: 70.0` (`05` §3.1). Three seconds of it must be **125.9712**, not 899.8912
  (that is R1) and not a single ×1.08.
- Phases never revert, and `18` §6's `PHASE` scope must still end a boss `AURA` on phase exit — that
  wiring was itself dead until M2-12 fixed it, so it has one milestone of history behind it.

**Acceptance:** three seconds of enrage = 125.9712 through the **real** loop, not a unit fixture;
a boss `AURA` granting `RAGE` measurably changes ATK; the < 5 ms budget re-measured and reported;
`05` §9's guardrails re-run, since enrage now does something and bosses may get *harder*.

---

## 4. R4 — no public entry point for a normal encounter or a duel

**What is true today.** The assembled milestone exposes a deterministic **boss-fight** simulator and
nothing else. `CombatRules`, `BattlePlan` and `EnemyCatalogue` are all `internal`, and the public
`Simulate` overload cannot attach an effect to any actor — so the `APPLY_STATUS` fix M2's architecture
review landed is unreachable through it.

Also in scope, and the same shape: **elite modifiers are drawn, validated, tested and mechanically
inert.** `EliteModifierDraw` has no production consumer, so **no fight in the repository ever contains
an Elite** — which also makes the elite-identity architecture rule hold *vacuously*. M2-11 warned that
the first consumer owes exactly one table.

🔒 **This is a design decision, not hardening, and it is governed by rulings R15/R16.** `30` §11.2 is
🔒 *"the only two `Rules` types that are public"*. M2 ruled that §11.2 enumerates public **entry
points**, whose parameter and return types are public **by consequence** (a C# requirement), and
widened `Domain.PublicRuleTypes` to an **explicitly enumerated closure** of six names —
`CombatSimulator`, `PowerCalculator`, `SimulationResult`, `CombatEvent`, `CombatEventType`,
`ActorStats`. **Enumerated, deliberately, so that a third public entry point stays a decision in a
diff.**

**Do not widen that list without an explicit ruling from the product owner.** If you are told to
proceed, the precedent to follow is M2-16a's: it added `CombatSimulator.SimulateBossFight` as a public
**method** whose signature closure was already public, leaving the enumerated list at six.

---

## 5. Rulings already in force — do not re-litigate these

Twenty-one conductor rulings were made during M2. The ones that touch this work:

| # | Ruling |
|---|---|
| **R1** | `STAT_MULT`'s `value` **is** the multiplier. `05` §1.1's `Π(1 + Multiplicative)` is an erratum; `18` §8 step 7 says "product". Three seconds of `SYS_ENRAGE` = **125.9712** |
| **R3** | Every boss `AURA` is `scope: PHASE`. `18` §7.8's `{999, BATTLE}` is a pre-`PHASE` artifact |
| **R8** | A `PERIODIC` anchors when its owning effect becomes **active** — a phase block's periodic at `ON_PHASE_ENTER`, a `BATTLE`-scope built-in at battle start |
| **R9** | `17` §1.1's `ON_HP_THRESHOLD` **is** `18` §3's `ON_LOW_HP`. There is no 24th trigger |
| **R10** | Every target token resolves relative to the effect's **holder**. Liveness filters *selection*, not *naming* — `SELF`/`CURRENT_TARGET`/`ATTACKER`/`OWNER` skip the `IsAlive` filter |
| **R12** | `"drawback"` is a reserved **author** tag marking `05` §4.1's ward-bypass class (b). A **status** tag is a different type |
| **R13** | `STAT_COPY`'s `target` names the copy **source** and writes onto the **holder** — an inversion `18` never flags |
| **R15/R16** | `30` §11.2 enumerates public **entry points**; their signature types are public by consequence. `Domain.PublicRuleTypes` is an enumerated closure of six |
| **R17** | Intra-`Rules` layering is `Rules.Combat → Rules.Stats → Rules.Effects`. `Rules.Effects` is the bottom. ⚠️ `Rules.Luck` has **no declared edges** — a known gap for the M4 kickoff |
| **R19′** | An effect is **always embedded** in the content that owns it; there is no registry and nothing is referenced by id. A content schema validates the effect array **structurally** (fewer than 3 op tokens — `No_other_schema_restates_the_effect_vocabulary`), and strict validation is a cross-file rule in `DeclaredRules.cs`, because `JsonSchemaValidator` resolves same-document pointers only |
| **R20** | `RANDOM_OUTCOME` is the **44th** op (the count is 44, not 43), added by `18` §10's procedure for the Dicelord's *Roll of Fate*. Its weighted table is the key `outcomes` |
| **R21** | `17` §9's *"every 6th attack … ×3"* = `ON_ATTACK {everyNth: 6}` → `FORCE_CRIT_NEXT {charges: 1}` + `ATTACK_MULT_NEXT {charges: 1, value: 2.0}`. Boss `CDMG` is 0.50, so the crit is ×1.5 and 1.5 × 2.0 = ×3.0 |

**Known holes — leave them absent (S6), do not fill them:**
- `RAGE` has **no authorised decay curve** (`05` §5 says it decays and states none). The harness
  measured the undecayed buff at **23–32 pp** of clear rate. ⚠️ Relevant to R1: once fired stat ops
  work, `RAGE` will start mattering.
- `FREEZE` has **no authorised stack count** anywhere.
- Three `17` mechanics are **not authorable** in the DSL and were recorded, not approximated:
  Cogitator's *"ignores 40 % DEF"* (no per-effect penetration key), Sporequeen's sporeling-death heal
  (a summon gets no `Effects`), the Dicelord's *"both gain"* (no `18` §5 target names both sides).

**Balance state — do not "fix" by retuning:** `05` §9's guardrails **1, 5 and 6 fail** and **3, 4 are
unmeasurable**, because no build at par has ever taken a boss below 66 % HP. Those are tuning
decisions on `game-data/` (`21` §3.2), not code. **Your work will change these numbers** — report the
new ones, do not chase them.

---

## 6. Standing rules

1. Work in your own worktree. `G:\Git\slay-idle-repeat` may belong to another milestone session —
   never `cd` into it, build in it, or commit from it.
2. Branch off **`review/M2`**. **Never merge**, never push.
3. Never edit `IMPLEMENTATION_TRACKER.md` (the conductor owns it), `game-data/tuning/` (exactly
   sixteen files; a seventeenth is a build failure), or `SnapshotFieldOrder.json`.
4. Never name a type `*Snapshot` — that pin is for persisted state, and nothing in M2 is.
5. `tests/SlayIdleRepeat.Architecture.Tests/SubjectSetFloorTests.cs` (`Pending`/`Live`) is the repo's
   **one** subject-set-floor and deferral register. Add entries there; never invent a second.
6. New architecture rules go in **new files**.
7. **Shouldly only.** *string* `ShouldContain`/`ShouldStartWith` default to **`Case.Insensitive`** —
   pass `Case.Sensitive` where case matters. **`ShouldAllBe` passes on an empty collection** — floor
   your collections first.
8. **All 4-dp rounding through `Primitives/DeterminismRounding`.** An IL rule fails the build on any
   `Math.Round(x, 4)`; it caught two agents at merge during M2.
9. Combat RNG is `new DeterministicRng(battleSeed, RngStreams.Combat)` with the seed **handed in**;
   `runSeed` never enters `Rules/`. A draw taken or skipped conditionally desynchronises client from
   server — `Position` is persisted stream state. `WeightedPick` is **one** draw (`14` §8.0).
10. **[HARD RULE] No infrastructure, ever — and no integration/E2E tier.** No Docker, no compose, no
    local Postgres/Redis/MinIO, no real server, at any phase, for any reason. No integration or
    end-to-end suite under any name, including a "slow" or "nightly" test project. Verify by
    inspection, unit tests and CI config review only.
11. **No AI-attribution trailers** in any commit message.
12. 🔒 **Dispatch any subagent BLOCKING (`run_in_background: false`) or not at all**, and never end
    your turn while one is running.
13. 🔒 **Never commit while a deliberate mutation is live.** The sweep is atomic: mutate → run →
    capture the literal output → revert → verify `git diff -- src/` is empty → **then** commit.

### Compiler traps that each bit someone during M2

- `Every_Core_type_lives_under_a_documented_namespace` rejects synthesized types Roslyn emits into the
  **global** namespace without `CompilerGeneratedAttribute`: `[x]` single-element collection
  expressions; `[value]` / `[.. xs, y]` targeting `IReadOnlyList<T>` (**`[]` is safe**); and array
  literals of several constants (`<PrivateImplementationDetails>/__StaticArrayInitTypeSize=N`). Use
  `new[] { … }` or `new List<T> { … }`.
- **Never use a `const` as an S1 probe** — the compiler folds it to a literal, so no IL reference
  exists and your proof passes for the wrong reason.

---

## 7. Steering rules — `.claude/retros/STEERING.md`, the ones that matter most here

**S1 · A test that cannot fail is a defect, not a weak test. [M0, amended M2]** Make it fail on
purpose, capture the literal output, revert, put that output in your report. 🔒 **One passing mutation
proves a rule is not *vacuous*. It does not prove it is correctly *scoped*.** Probe every rule with
**two different shapes plus a negative control**. M2 shipped **nine** cannot-fail tests and **every one
was caught by a second probe, never the first.** Choose probe values that can **discriminate** — one
M2 defect was invisible at 1.0 ASPD and only appeared at 2.0.

**S2 · Assert the identity, not the symptom.** Pin *which* rule fired — the pointer, location or
message fragment — not just the code. This repo's suites depend on messages naming the rule.

**S3 · Every reflection- or metadata-driven rule needs a floor on its subject set.** A rule whose
subject set can silently become empty passes forever.

**S4 · A declared exception must expire by itself** — including when it has been *satisfied*.

**S6 · Never fill a hole with a plausible value.** If the docs do not authorise a number, leave it
absent and greppable (`null`) and say so. Never coerce a hole to a default at read time; fail loudly.

**S7 · Add the `InMemory` fake *and* the shared contract suite in the same change as the port.**

**S9 · Never quote a number from one report into another prompt without verifying it.**

**S17 · Before ruling against an apparent gap, check what the repo has already decided about it.**
A ruling is pasted into dispatches as *authority*, so a wrong one propagates faster than any agent's
mistake. **Your claims about repo state are as unreliable as a number quoted from a report.**

---

## 8. What to return

A completion report containing:

- The branch name and a commit-by-commit summary.
- **Literal** final build + test output, all four suites, with deltas against the verified base in §0.
- **Which option you took for R3a, and why**, in the terms of §1a.
- Red-then-green proofs with **literal** captured output, at minimum:
  the `[Theory]` over all nine bosses; `FREEZE` at −50 % ASPD applied numerically;
  `ValueScaleEvaluator`'s guard still refusing a value-less **stat** op; `PK_UNBREAKABLE` in `18`
  §7.4's exact shape working end to end; **three seconds of `SYS_ENRAGE` = 125.9712 through the real
  loop**; a boss `AURA` granting `RAGE` measurably changing ATK.
- **The re-measured `< 5 ms` figure** (current real-content median is 2.881 ms) and the **re-run
  balance-harness numbers**, since bosses may now get materially harder. Report them; do not chase
  them.
- Whether **exit criterion ③** is now met, stated plainly.
- Every assumption recorded, and any `05` / `17` / `18` / `30` errata found — M2 collected seven and
  they are worth continuing.
