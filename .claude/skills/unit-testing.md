---
name: unit-testing
description: Conventions for xUnit unit tests against SlayIdleRepeat.Core and SlayIdleRepeat.Application (and plain-C# Godot client presenters) in tests/SlayIdleRepeat.Core.Tests, tests/SlayIdleRepeat.Application.Tests, and tests/SlayIdleRepeat.Client.Tests. Use whenever writing or reviewing a unit test, or when a TDD phase touches game rules, use cases, or presenter logic. This repository has no integration or end-to-end test tier and none is to be created — unit tests are the centerpiece.
---

This skill codifies how to write xUnit unit tests for **Slay Idle Repeat** (Godot 4 / C# client + ASP.NET Core server, sharing one rules library). It applies the same overarching principles as [tdd-write-tests](tdd-write-tests.md) — observable behaviour over implementation details, one behaviour per test, AAA structure, descriptive names, edge cases first-class, no test logic. Read that skill first; this one only covers what's specific to this project's unit tests.

Source of truth for everything below: `docs/ARCHITECTURE.md` — the layout, the three opening constraints, the per-folder rules (including `Core`'s internal shape) and the `tests/` section. `README.md` documents the verification commands and the coverage, CRAP and mutation gates. This repo has no separate `CONVENTIONS.md` — those two are it.

## Scope & placement — this is the whole test surface for this workflow

🔒 **This repository has no integration or end-to-end tier.** `SlayIdleRepeat.Integration.Tests` (a real ASP.NET host against a Docker Compose stack) was deleted deliberately and must not be recreated under any name — see `$noIntegrationTier` in `build/ci/test-suites.json`. `SlayIdleRepeat.Contract.Tests` (each port's shared suite run against every real implementation) does exist, but **is not part of this workflow either** — never write to, extend, or run it here; see [tdd-write-tests](tdd-write-tests.md)'s routing table. This skill covers only the three tiers that are genuinely unit-level: no engine, no database, no network, no vendor SDK, and no container ever started.

| Feature touches | Suite | Notes |
|---|---|---|
| Game rules: combat, dice, board generation, effect DSL, talents, gear/merging, economy math, progression, luck protection/pity — and any command behaviour decided inside `GameRules.Apply` (which is most behaviour) | `tests/SlayIdleRepeat.Core.Tests/` | Zero dependencies — `SlayIdleRepeat.Core` references nothing but the .NET BCL, not even a clock, and is fully synchronous (no `Task`/`async`/`CancellationToken`). Mirror `SlayIdleRepeat.Core`'s folder structure (`Primitives/`, `Content/`, `Rng/`, `Model/`, `Rules/`, `Commands/`, `Events/`, `Handlers/`). |
| Use-case *orchestration* (`RollDice`, `PickPerk`, `MergeGear`, `StartDuel`, …): load slice → `GameRules.Apply` → persist → dispatch, idempotency handling | `tests/SlayIdleRepeat.Application.Tests/` | Exercise entirely against `SlayIdleRepeat.Adapters.InMemory` — every port has an in-memory fake by design (rule A5). No real Postgres, Redis, S3, ad SDK, store API, or Godot. Use cases contain no game rules — assert the choreography, not rule outcomes; those belong in `Core.Tests` through `Apply`. |
| Presenters under `res://game/presenters/` (plain C# classes, ports injected by the composition root) | `tests/SlayIdleRepeat.Client.Tests/` | Presenters are deliberately designed to be "tested without booting the engine" (16 Part D) — they take ports as constructor arguments like any other class. If this test project doesn't exist yet, create it following the same convention as the other `tests/SlayIdleRepeat.*.Tests` projects. |

Godot scenes and nodes under `res://game/scenes/` hold no rules and no port references (Rule A10 — Godot is a *driving adapter*, not a foundation); they render and forward input to a presenter. There is nothing behavioural in a scene to unit-test, so none exists — a scene's correctness is a visual/structural concern, covered by the review-ui-quality skill reading the `.tscn`/theme files, not by an automated test tier.

## The Core seam: `GameRules.Apply`, not fakes

`GameRules.Apply(WorldSlice, GameCommand, GameContext)` is the only public mutation in the game (backstopped by the `Apply_is_the_only_public_mutation` architecture test). Rule tests are written as: **construct a state, call `Apply`, assert on the returned `CommandResult` and the emitted `DomainEvent`s** (`DiceRolled`, `TileResolved`, `CurrencyChanged`, `PityCounterAdvanced`, `GearGranted` with its `FromPity` flag, …). Use `Core/Testing/InMemoryGame` (+ `VirtualClock`) — it references `Core` only: no ports, no fakes, no Application.

- **An illegal move is data, never an exception**: assert `Accepted == false` and the specific `RejectionReason` — an `Assert.Throws` on a rejectable command is itself a test-quality finding.
- Test setup builds state via `Apply` or `Player.Rehydrate(PlayerSnapshot, ContentSnapshot)` — aggregates have public getters and internal constructors; outside code reads everything and constructs nothing.

## 🔒 `InternalsVisibleTo` is a last resort, not a convenience

`SlayIdleRepeat.Core.Tests` is the one project granted `InternalsVisibleTo`. That grant exists so a handful of rules that have **no public route at all** can be tested — it is not a licence to test at whatever altitude is most convenient.

**Write every test at the outermost seam that can observe the behaviour.** In order of preference:

1. `GameRules.Apply` — for anything a command decides. Assert the `CommandResult` and the emitted `DomainEvent`s.
2. A public `CombatSimulator` entry point — `Simulate`, `SimulateEncounter`, `SimulateDuel`, `SimulateBossFight` — for anything a fight decides. Assert the emitted `CombatEvent`s in `SimulationResult.Log`, plus `HeroWon`/`DurationTicks`/`LogHash`. **`SimulateEncounter` and `SimulateDuel` both take `IReadOnlyList<EffectDefinition>`**, so effect-driven behaviour — stat aggregation, triggers, statuses, ops — is reachable publicly: attach the effect, run the fight, and assert the difference it makes against the identical fight without it. Constants that a fight reads (the `05` §1 caps, the `05` §4 mitigation dials, `wardCapPct`) are **content**, so vary them by passing a different `ContentSnapshot` rather than by reaching for the internal `StatCaps`/`MitigationConstants`.
3. `PowerCalculator` — for the power readout.
4. Only then, an internal type.

Before driving an internal calculator directly, you must be able to say **which** observable output would not move, and why. "The public entry point cannot express this input" and "the behaviour changes no event, hash, or outcome a caller can see" are the two answers that justify it. "Going through a fight would need more setup", "the internal call asserts the arithmetic more directly", and "the internal result type carries a field the log does not" are not — the last one especially, because a value no caller can observe is a value no test should pin.

When an internal test is genuinely the only option, **say so in the test's own remarks**: name the public seam you tried, and the specific reason it cannot reach the behaviour. A reviewer must be able to re-check that judgement without re-deriving it. Two worked examples currently in the suite:

- `DamageResolutionTests` — the `05` §4 arithmetic (mitigation, crit, block, the step-7 floor) runs through `CombatSimulator.SimulateDuel` and is asserted on `Hit`/`Crit`/`Block`/`Miss` events. What stays internal is only what the log cannot show: the draw-stream `Position` counts, `AttackResult.Basis` (a pre-absorption intermediate), and the ward/true-damage/`MAX_HP%` routes, which no public overload can invoke.
- `StatAggregationTests` — the `18` §8 resolution order is asserted through `SimulateDuel`'s `attackerEffects`, because an aggregated stat is observable as the damage it produces. What stays internal is the argument-validation and refusal surface, which throws before any fight exists.

From any test project **other** than `Core.Tests`, there is no escape hatch: go through `Apply` or a public entry point.

## 🔒 Before writing a test: name the defect only it can catch

The 2026-08 suite review deleted ~200 tests, and the single biggest class was not bad tests but **duplicates below the seam**: aggregate/model and internal-resolver tests whose behaviour was already pinned through `Apply` in a handler suite. So, before writing:

1. **Name the specific wrong implementation this test would go red on.** If every such implementation already fails an existing test, don't write it. A test that only re-asserts what a stronger case in the same file proves (a happy-path variant of a theory row, a weaker assert after an exact one) is a deletion at review, so don't author it.
2. **If the subject sits below `Apply` (a `Model` aggregate, a `Rules` calculator, a resolver), grep the outer-seam suites first** — `Handlers/`, `GameRules/`, the public-fight suites in `Rules/Combat`. Behaviour pinned there is done; write the below-seam test only for what the outer seam cannot express (unreachable state, `Rehydrate` fault arms, mid-command intermediates), and say so per the remark rule above.
3. **A mutator happy-path on an aggregate is almost never yours to test** — the command that drives it already asserts the observable outcome. Aggregate tests earn their place on: `Rehydrate` refusals, invariants no single command exposes, and round-trips (compare **canonical bytes**, never a hand-maintained field list — field lists rot silently as fields are added).

## Naming convention

Follow: `<Method>_<expected outcome>_<condition>` in lower snake case after the method name, e.g. `Simulate_produces_identical_LogHash_for_same_seed_and_snapshot`, `PickPerk_upgrades_perk_to_tier_two_when_already_drafted`, `WatchAd_grants_reward_and_consumes_cap_slot_on_no_fill` (that *is* the specified no-fill behaviour: never punish a player for a network problem).

The name should make the failing test output self-explanatory without opening the file.

## 🔒 Comments: short, or absent

**A test with no comment at all is fine, and is the default.** The name says what the case is; the code says how; the Shouldly `because` string says why the number is what it is. A comment is worth writing only when it carries something none of those three can.

Write one **only** for:

- **A spec citation** — the section the case enforces (`` `05` §4 step 3 ``). One line.
- **Why a literal is that literal** — the arithmetic behind `53.85`, or the sanity check it is transcribed from.
- **What a wrong implementation would produce** — the counterfactual that makes the case discriminating (`899.8912 is 100 × 2.08³`). This is the highest-value kind; keep it.
- **Why the case is shaped oddly** — a non-obvious fixture choice, a second tick, an authored ceiling. If a reader would otherwise ask "why not just…", answer it in one sentence.

**Delete on sight:**

- Anything restating the test name or the assertion. `/// <summary>Two contexts with the same values are equal.</summary>` above `Two_contexts_built_from_the_same_values_are_equal` is pure noise.
- The same reasoning written twice — once in `<remarks>` and once in the Shouldly `because`. **The `because` string wins**: it is what appears in the failure output, where a reader actually needs it. Cut the prose copy.
- Multi-paragraph `<remarks>` essays. If the reasoning genuinely needs more than about three lines, that is a signal the *test* is doing too much — split the case instead of explaining it.
- Commentary defending the test's own existence ("this reads as trivially true — it is a tripwire, not noise"). If a case needs an argument that it isn't noise, delete the case.
- Narration of project history — which task added it, which review found it, which branch it landed on. That is what `git log` is for.
- Section-divider banners made of box-drawing characters, unless the file is long enough that they genuinely aid navigation.

Fixture and bench files follow the same rule and are usually worse offenders: document the *contract* of a helper in a sentence, not the reasoning that led to it.

Prefer moving an explanation **into** the `because` string over leaving it above the method — same information, better placed.

## Use fakes over mocks

- `SlayIdleRepeat.Application` tests exercise use cases against `SlayIdleRepeat.Adapters.InMemory`'s hand-written fake for the port under test (e.g. an in-memory `IPlayerRepository`, `IRunStateStore`, `IRewardedAdPort`). Do not introduce a mocking library — every port already has a fake built for exactly this purpose (rule A5), so a mock would duplicate infrastructure that already exists and would drift from what the real adapters actually do.
- **Time** — inside `Core`, time is a value: `GameContext.NowUtc`, set explicitly per test (via `InMemoryGame`'s `VirtualClock`). `IClockPort` exists only at the Application layer — it must never appear in `Core` at all (architecture-tested); in an Application test, inject its in-memory fake and set it explicitly. Never rely on the real clock (and never let `Core`/`Application` production code touch `DateTime.Now`/`UtcNow` — see "What you must never do" below).
- **Randomness** in game rules is counter-based — not a stateful seeded generator with `SaveState()` semantics: every in-run draw is `Hash64(runSeed, streamName, drawIndex)` (xxHash64) over the nine named streams — `board`, `dice`, `draft`, `drops`, `treasure`, `shrine`, `combat`, `events`, `minigame:{index}`. 🔒 **The combat stream is named exactly `combat`, not `combat:{battleIndex}`** — per-battle separation comes from *re-rooting the seed*, not from decorating the stream name: `battleSeed = Hash64(runSeed, "combat", battleIndex)`, then combat draw `i` of that battle is `Hash64(battleSeed, "combat", i)`. `Core/Rng`'s stream validation rejects `combat:0`, and `SlayIdleRepeat.Core.Tests` pins that rejection. The persisted stream position *is* the draw counter, and out-of-run meta commands draw from the server-issued `GameContext.CommandSeed`. It is deliberately *not* a port — it's part of the rules, not an external dependency. Fix the seed(s) on the state/context explicitly and assert the exact outcome at a pinned draw index; never assert on an unseeded roll, and never model randomness as a stateful generator you seed and step.
- Ids come from `IIdGeneratorPort` (a shared/determinism-critical port) — use its fake rather than letting a test depend on `Guid.NewGuid()` output.

## Determinism is a first-class test concern, not an edge case

Combat, board generation, and drafting are specified as byte-identical given the same `(seed, snapshot)` — this is load-bearing for PvP fairness and client/server parity. When testing anything that touches the deterministic draw streams or the combat simulator:

- Fix the seed explicitly and assert the exact expected sequence/outcome, not just "it produced *a* result."
- Internal values are `double`, rounded to 4 decimal places at every accumulation point (`Math.Round(x, 4)`) — assert against the rounded value, not raw floating-point equality, and never assert with a wider tolerance than the spec's own rounding rule (that would hide a real rounding-order bug).
- For a full simulated fight, prefer asserting on `SimulationResult.LogHash` (or a specific `CombatEvent` in `Log`) over hand-verifying every tick — that mirrors how the project's own determinism CI test and PvP anti-cheat verification work.
- Every randomised grant routes through `LuckService`. A pity guarantee needs an explicit unit test that it fires at exactly `N` **and** a property test that it never fires later than `N` across 100,000 seeded sequences — assert via the `PityCounterAdvanced` event and `GearGranted.FromPity`. Ads never advance pity, and dungeons advance no counter at all — good testable negatives.

## Data-driven cases

No `if`, `for`, `switch` inside a test body. Use `[Theory]` / `[InlineData]` / `[MemberData]` for data-driven cases — e.g. stat-cap boundaries, the stat-resolution stages (flat → percent → convert → multiplicative → set → cap → round), or a table of `(chapter, tier)` pairs for board generation constraints.

## Effect DSL tests — drive the resolver generically

Every perk, talent, gear affix, pet aura, mount bonus, status effect, event outcome, shrine buff, curse, and boss mechanic is expressed through the **one** generic effect resolver in `SlayIdleRepeat.Core/Effects` — never per-entity code. When testing a new perk/talent/boss mechanic:

- Test it as **data** flowing through `EffectResolver`, the same way every other effect is tested — construct the `EffectDefinition` (op/trigger/condition/target/value/duration/stacking) and assert the resolver's output, not a hand-written special case.
- If the request needs a new DSL operation, trigger, or condition that doesn't exist yet, that is itself the thing under test: add the op to `src/SlayIdleRepeat.Core/Rules/Effects/Ops`, `game-data/schema/effect.schema.json`, and the client/server parity test, with a unit test for the op in isolation — all in the same change — and never write `if (perkId == "PK_X")` (or a `CP_`/`BOSS_`-prefixed equivalent) anywhere as a substitute. One sanctioned exception exists: `MODIFY_DIE_FACE`'s combat-context special case.
- The resolution order (flat → percent → convert → multiplicative → set → cap → round; ascending lexicographic effect-ID order governs the convert/multiplicative/set stages — flat and percent are order-independent sums) is exact and load-bearing for client/server parity. A test asserting only a final stat value without pinning intermediate steps can pass while the resolver applies stages in the wrong order — prefer a test that would catch an order swap when the feature's behaviour depends on order (e.g. two stacking percent buffs plus one multiplicative one).

## What you must never do

- Introduce a mocking library when the project's own in-memory fake will do.
- Rely on the real system clock, `System.Random`, `Random.Shared`, `GD.Randi()`, or `Environment.TickCount` — these are CI-grepped and banned inside `SlayIdleRepeat.Core`/`SlayIdleRepeat.Application`; a test that needs one of these to compile is a sign production code reached for the wrong source.
- Reach into private members via reflection, or assert on internal *state* a caller can't observe. (`InternalsVisibleTo` itself is sanctioned for `SlayIdleRepeat.Core.Tests` only, and even there only under the last-resort rule above; any other test project using it is a finding.)
- 🔒 **Assert on a type's *shape* rather than its behaviour.** `IsSealed`, "no public setter", "no public field", "exactly one public constructor", an exact `GetMembers()` name list — and their quieter cousins: an enum's **member count** (`ops == 44`), a type's **namespace location**, a synthesized-record **equality/`with`-copy** check, a **`ShouldBeSameAs` reference-identity** pin on a value a caller only ever reads — these are architecture rules, and `SlayIdleRepeat.Architecture.Tests` is where they belong — read its `DependencyRuleTests` before restating a shape rule here. Restating one in a unit test buys nothing and costs a second place to update. Reflection in `Core.Tests` is justified only when it drives *behaviour* — enumerating a vocabulary to prove every member round-trips, for instance — never when it merely describes a declaration.
- 🔒 **Write a test whose subject is the test's own fixture.** `The_reference_table_ids_are_unique`, `The_published_set_still_covers_every_input`, `The_check_recognises_a_real_name_and_refuses_an_invented_one`, `The_exact_N_theories_cover_every_rule_the_spec_states`, `Both_authored_tables_are_well_formed` — a malformed table already fails the test that reads it, so these assert nothing about the game.
- 🔒 **Write a null-guard test for an internal type no outside caller can hand null to** (`*_refuses_a_null_argument` on a resolver, view projection, or internal factory). The guard is one line of production code with no reachable failure path — a tautology in test form. Guards on the genuinely public surface (`Apply`, `Rehydrate`, simulator entry points) are behaviour and stay.
- 🔒 **Restate a shipped `game-data` value against a literal.** `The_campfire_heal_is_25` proves the JSON hasn't changed, not that the rule works — that is `tools/ContentValidator`'s and the Application mirror suites' job. Test the *reader/rule* against a **hermetic fixture**, and keep one *discriminating retune variant* (a different authored value producing a different outcome) so a reader ignoring the document fails loudly.
- Assert a wall-clock or performance budget in a unit test — it flakes on CI and pins no behaviour. (`Architecture.Tests` is the deliberate exception: its rules scan source and IL, where a silently-empty subject set really can pass vacuously, so its guard-the-guard cases earn their place. A `[Theory]` over a data table does not.)
- Reference a concrete adapter type, a vendor SDK type (Npgsql, StackExchange.Redis, the AppLovin plugin, etc.), or a Godot type (`Node`, `GD.*`) from a `SlayIdleRepeat.Core.Tests` or `SlayIdleRepeat.Application.Tests` test — those tiers exercise pure rules and use cases against ports/fakes only, mirroring the production dependency rule (`Adapters → Application → Core → nothing`).
- Write a test in — or that requires — `SlayIdleRepeat.Contract.Tests`, a newly created integration/E2E suite, or anything that boots Docker/Postgres/Redis/a real ad SDK/the Godot runtime. `Contract.Tests` exists in this project's own CI but is out of scope here; an integration/E2E tier does not exist at all and is not to be created.
- Hand-verify a value that the Effect DSL resolver, stat-aggregation pipeline, or `DeterministicRng` should be producing — assert on the pipeline's output instead of re-deriving the math inline in the test.
