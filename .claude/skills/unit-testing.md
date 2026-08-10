---
name: unit-testing
description: Conventions for xUnit unit tests against SlayIdleRepeat.Core and SlayIdleRepeat.Application (and plain-C# Godot client presenters) in tests/SlayIdleRepeat.Core.Tests, tests/SlayIdleRepeat.Application.Tests, and tests/SlayIdleRepeat.Client.Tests. Use whenever writing or reviewing a unit test, or when a TDD phase touches game rules, use cases, or presenter logic. This project's workflow has no integration or end-to-end test tier — unit tests are the centerpiece.
---

This skill codifies how to write xUnit unit tests for **Slay Idle Repeat** (Godot 4 / C# client + ASP.NET Core server, sharing one rules library). It applies the same overarching principles as [tdd-write-tests](tdd-write-tests.md) — observable behaviour over implementation details, one behaviour per test, AAA structure, descriptive names, edge cases first-class, no test logic. Read that skill first; this one only covers what's specific to this project's unit tests.

Source of truth for everything below: `game-design/14_TECHNICAL_ARCHITECTURE.md` and `game-design/23_PORTS_AND_ADAPTERS.md` (this repo has no separate `CONVENTIONS.md`/`ARCHITECTURE.md` yet — those game-design docs are it).

## Scope & placement — this is the whole test surface for this workflow

This project's full CI also has `SlayIdleRepeat.Integration.Tests` (a real ASP.NET host against a Docker Compose Postgres/Redis stack) and `SlayIdleRepeat.Contract.Tests` (each port's shared suite run against every real implementation). **Neither is part of this workflow.** Never write to, extend, or run them here — see [tdd-write-tests](tdd-write-tests.md)'s routing table. This skill covers only the three tiers that are genuinely unit-level: no engine, no database, no network, no vendor SDK.

| Feature touches | Suite | Notes |
|---|---|---|
| Game rules: combat, dice, board generation, effect DSL, talents, gear/merging, economy math, progression | `tests/SlayIdleRepeat.Core.Tests/` | Zero dependencies — `SlayIdleRepeat.Core` references nothing but the .NET BCL, not even a clock. Mirror `SlayIdleRepeat.Core`'s folder structure (`Combat/`, `Stats/`, `Board/`, `Dice/`, `Progression/`, `Economy/`, `Effects/`, `Rng/`). |
| Use cases (`RollDice`, `PickPerk`, `MergeGear`, `StartDuel`, …) | `tests/SlayIdleRepeat.Application.Tests/` | Exercise entirely against `SlayIdleRepeat.Adapters.InMemory` — every port has an in-memory fake by design (23 §5, rule A8). No real Postgres, Redis, S3, ad SDK, store API, or Godot. |
| Presenters under `res://game/presenters/` (plain C# classes, ports injected by the composition root) | `tests/SlayIdleRepeat.Client.Tests/` | Presenters are deliberately designed to be "tested without booting the engine" (16 Part D) — they take ports as constructor arguments like any other class. If this test project doesn't exist yet, create it following the same convention as the other `tests/SlayIdleRepeat.*.Tests` projects. |

Godot scenes and nodes under `res://game/scenes/` hold no rules and no port references (Rule A10 — Godot is a *driving adapter*, not a foundation); they render and forward input to a presenter. There is nothing behavioural in a scene to unit-test, so none exists — a scene's correctness is a visual/structural concern, covered by the review-ui-quality skill reading the `.tscn`/theme files, not by an automated test tier.

## Naming convention

Follow: `<Method>_<expected outcome>_<condition>` in lower snake case after the method name, e.g. `Simulate_produces_identical_LogHash_for_same_seed_and_snapshot`, `PickPerk_upgrades_perk_to_tier_two_when_already_drafted`, `RollDice_returns_NoFill_result_untouched_when_ad_adapter_errors`.

The name should make the failing test output self-explanatory without opening the file.

## Use fakes over mocks

- `SlayIdleRepeat.Application` tests exercise use cases against `SlayIdleRepeat.Adapters.InMemory`'s hand-written fake for the port under test (e.g. an in-memory `IPlayerRepository`, `IRunStateStore`, `IRewardedAdPort`). Do not introduce a mocking library — every port already has a fake built for exactly this purpose (23 §5, A8), so a mock would duplicate infrastructure that already exists and would drift from what the real adapters actually do.
- **Time** comes from `IClockPort` — inject its in-memory fake and set it explicitly; never rely on the real clock in a test (and never let `Core`/`Application` production code touch `DateTime.Now`/`UtcNow` — see "What you must never do" below).
- **Randomness** in game rules comes from `DeterministicRng` (xoshiro256\*\*), seeded from one of the named child streams (`board`, `dice`, `draft`, `drops`, `combat:{battleIndex}`, `events`, `minigame:{index}`) — it is deliberately *not* a port (14 §8.1: it's part of the rules, not an external dependency). Seed it explicitly with a fixed value in every test that touches randomness; never assert on an unseeded roll.
- Ids come from `IIdGeneratorPort` (a shared/determinism-critical port) — use its fake rather than letting a test depend on `Guid.NewGuid()` output.

## Determinism is a first-class test concern, not an edge case

Combat, board generation, and drafting are specified as byte-identical given the same `(seed, snapshot)` — this is load-bearing for PvP fairness and client/server parity (14 §8.2). When testing anything that touches `DeterministicRng` or the combat simulator:

- Fix the seed explicitly and assert the exact expected sequence/outcome, not just "it produced *a* result."
- Internal values are `double`, rounded to 4 decimal places at every accumulation point (`Math.Round(x, 4)`) — assert against the rounded value, not raw floating-point equality, and never assert with a wider tolerance than the spec's own rounding rule (that would hide a real rounding-order bug).
- For a full simulated fight, prefer asserting on `SimulationResult.LogHash` (or a specific `CombatEvent` in `Log`) over hand-verifying every tick — that mirrors how the project's own determinism CI test and PvP anti-cheat verification work.

## Data-driven cases

No `if`, `for`, `switch` inside a test body. Use `[Theory]` / `[InlineData]` / `[MemberData]` for data-driven cases — e.g. stat-cap boundaries, the five stat-aggregation stages (flat → percent → multiplicative → cap → round), or a table of `(chapter, tier)` pairs for board generation constraints.

## Effect DSL tests — drive the resolver generically

Every perk, talent, gear affix, pet aura, mount bonus, status effect, event outcome, shrine buff, curse, and boss mechanic is expressed through the **one** generic effect resolver in `SlayIdleRepeat.Core/Effects` (18 §1) — never per-entity code. When testing a new perk/talent/boss mechanic:

- Test it as **data** flowing through `EffectResolver`, the same way every other effect is tested — construct the `EffectDefinition` (op/trigger/condition/target/value/duration/stacking) and assert the resolver's output, not a hand-written special case.
- If the request needs a new DSL operation, trigger, or condition that doesn't exist yet, that is itself the thing under test: add the op to `Effects/Ops` + the JSON schema, write a unit test for the op in isolation, and — per the doc's own extension rule (18 §10) — never write `if (perkId == "PK_X")` anywhere as a substitute.
- The resolution order (18 §8: flat → percent → convert → multiplicative → set → cap → round, effects applied in ascending lexicographic effect-ID order) is exact and load-bearing for client/server parity. A test asserting only a final stat value without pinning intermediate steps can pass while the resolver applies stages in the wrong order — prefer a test that would catch an order swap when the feature's behaviour depends on order (e.g. two stacking percent buffs plus one multiplicative one).

## What you must never do

- Introduce a mocking library when the project's own in-memory fake will do.
- Rely on the real system clock, `System.Random`, `Random.Shared`, `GD.Randi()`, or `Environment.TickCount` — these are CI-grepped and banned inside `SlayIdleRepeat.Core`/`SlayIdleRepeat.Application` (14 §8.1); a test that needs one of these to compile is a sign production code reached for the wrong source.
- Reach into private or internal members via reflection or `InternalsVisibleTo` to assert on state a caller can't observe.
- Reference a concrete adapter type, a vendor SDK type (Npgsql, StackExchange.Redis, the AppLovin plugin, etc.), or a Godot type (`Node`, `GD.*`) from a `SlayIdleRepeat.Core.Tests` or `SlayIdleRepeat.Application.Tests` test — those tiers exercise pure rules and use cases against ports/fakes only, mirroring the production dependency rule (`Adapters → Application → Core → nothing`).
- Write a test in — or that requires — `SlayIdleRepeat.Integration.Tests`, `SlayIdleRepeat.Contract.Tests`, or anything that boots Docker/Postgres/Redis/a real ad SDK/the Godot runtime. That tier exists in this project's own CI but is explicitly out of scope for this workflow.
- Hand-verify a value that the Effect DSL resolver, stat-aggregation pipeline, or `DeterministicRng` should be producing — assert on the pipeline's output instead of re-deriving the math inline in the test.
