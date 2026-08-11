---
name: unit-testing
description: Conventions for xUnit unit tests against SlayIdleRepeat.Core and SlayIdleRepeat.Application (and plain-C# Godot client presenters) in tests/SlayIdleRepeat.Core.Tests, tests/SlayIdleRepeat.Application.Tests, and tests/SlayIdleRepeat.Client.Tests. Use whenever writing or reviewing a unit test, or when a TDD phase touches game rules, use cases, or presenter logic. This project's workflow has no integration or end-to-end test tier — unit tests are the centerpiece.
---

This skill codifies how to write xUnit unit tests for **Slay Idle Repeat** (Godot 4 / C# client + ASP.NET Core server, sharing one rules library). It applies the same overarching principles as [tdd-write-tests](tdd-write-tests.md) — observable behaviour over implementation details, one behaviour per test, AAA structure, descriptive names, edge cases first-class, no test logic. Read that skill first; this one only covers what's specific to this project's unit tests.

Source of truth for everything below: `game-design/14_TECHNICAL_ARCHITECTURE.md`, `game-design/23_PORTS_AND_ADAPTERS.md`, and `game-design/30_DOMAIN_MODEL.md` (the authority for `Core`'s internal shape). **Check `game-design/16_DECISION_LOG.md` §A7 first: its gap-review rulings are authoritative over contradicting text in the other design docs until the amendments land** — the RNG model, `IClockPort`'s reach, and the idempotency window all changed there. This repo has no separate `CONVENTIONS.md`/`ARCHITECTURE.md` yet — these docs are it.

## Scope & placement — this is the whole test surface for this workflow

This project's full CI also has `SlayIdleRepeat.Integration.Tests` (a real ASP.NET host against a Docker Compose Postgres/Redis stack) and `SlayIdleRepeat.Contract.Tests` (each port's shared suite run against every real implementation). **Neither is part of this workflow.** Never write to, extend, or run them here — see [tdd-write-tests](tdd-write-tests.md)'s routing table. This skill covers only the three tiers that are genuinely unit-level: no engine, no database, no network, no vendor SDK.

| Feature touches | Suite | Notes |
|---|---|---|
| Game rules: combat, dice, board generation, effect DSL, talents, gear/merging, economy math, progression, luck protection/pity — and any command behaviour decided inside `GameRules.Apply` (which is most behaviour) | `tests/SlayIdleRepeat.Core.Tests/` | Zero dependencies — `SlayIdleRepeat.Core` references nothing but the .NET BCL, not even a clock, and is fully synchronous (no `Task`/`async`/`CancellationToken` — 30 §2.1). Mirror `SlayIdleRepeat.Core`'s folder structure (30 §11: `Primitives/`, `Content/`, `Rng/`, `Model/`, `Rules/`, `Commands/`, `Events/`, `Handlers/`). |
| Use-case *orchestration* (`RollDice`, `PickPerk`, `MergeGear`, `StartDuel`, …): load slice → `GameRules.Apply` → persist → dispatch, idempotency handling | `tests/SlayIdleRepeat.Application.Tests/` | Exercise entirely against `SlayIdleRepeat.Adapters.InMemory` — every port has an in-memory fake by design (23 §5, rule A5). No real Postgres, Redis, S3, ad SDK, store API, or Godot. Use cases contain no game rules (23 §2.0a) — assert the choreography, not rule outcomes; those belong in `Core.Tests` through `Apply` (30 §10). |
| Presenters under `res://game/presenters/` (plain C# classes, ports injected by the composition root) | `tests/SlayIdleRepeat.Client.Tests/` | Presenters are deliberately designed to be "tested without booting the engine" (16 Part D) — they take ports as constructor arguments like any other class. If this test project doesn't exist yet, create it following the same convention as the other `tests/SlayIdleRepeat.*.Tests` projects. |

Godot scenes and nodes under `res://game/scenes/` hold no rules and no port references (Rule A10 — Godot is a *driving adapter*, not a foundation); they render and forward input to a presenter. There is nothing behavioural in a scene to unit-test, so none exists — a scene's correctness is a visual/structural concern, covered by the review-ui-quality skill reading the `.tscn`/theme files, not by an automated test tier.

## The Core seam: `GameRules.Apply`, not fakes

`GameRules.Apply(WorldSlice, GameCommand, GameContext)` is the only public mutation in the game (30 §2, §11 — backstopped by the `Apply_is_the_only_public_mutation` architecture test). Rule tests are written as: **construct a state, call `Apply`, assert on the returned `CommandResult` and the emitted `DomainEvent`s** (`DiceRolled`, `TileResolved`, `CurrencyChanged`, `PityCounterAdvanced`, `GearGranted` with its `FromPity` flag, …). Use `Core/Testing/InMemoryGame` (+ `VirtualClock`) — it references `Core` only: no ports, no fakes, no Application (30 §6).

- **An illegal move is data, never an exception** (30 §2.1): assert `Accepted == false` and the specific `RejectionReason` — an `Assert.Throws` on a rejectable command is itself a test-quality finding.
- Handlers and rule calculators are `internal`; only `CombatSimulator` and `PowerCalculator` are public. `SlayIdleRepeat.Core.Tests` is the one project granted `InternalsVisibleTo` (30 §11.3), so driving an internal calculator (the effect resolver, `LuckService`, board generation) directly is legitimate there — and only there. From any other test project, go through `Apply`.
- Test setup builds state via `Apply` or `Player.Rehydrate(PlayerSnapshot, ContentSnapshot)` — aggregates have public getters and internal constructors; outside code reads everything and constructs nothing (30 §11).

## Naming convention

Follow: `<Method>_<expected outcome>_<condition>` in lower snake case after the method name, e.g. `Simulate_produces_identical_LogHash_for_same_seed_and_snapshot`, `PickPerk_upgrades_perk_to_tier_two_when_already_drafted`, `WatchAd_grants_reward_and_consumes_cap_slot_on_no_fill` (that *is* the specified no-fill behaviour — 12 §4.3: never punish a player for a network problem).

The name should make the failing test output self-explanatory without opening the file.

## Use fakes over mocks

- `SlayIdleRepeat.Application` tests exercise use cases against `SlayIdleRepeat.Adapters.InMemory`'s hand-written fake for the port under test (e.g. an in-memory `IPlayerRepository`, `IRunStateStore`, `IRewardedAdPort`). Do not introduce a mocking library — every port already has a fake built for exactly this purpose (23 §5, rule A5), so a mock would duplicate infrastructure that already exists and would drift from what the real adapters actually do.
- **Time** — inside `Core`, time is a value: `GameContext.NowUtc`, set explicitly per test (via `InMemoryGame`'s `VirtualClock`). `IClockPort` exists only at the Application layer — it must never appear in `Core` at all (30 §3, architecture-tested); in an Application test, inject its in-memory fake and set it explicitly. Never rely on the real clock (and never let `Core`/`Application` production code touch `DateTime.Now`/`UtcNow` — see "What you must never do" below).
- **Randomness** in game rules is counter-based (16 §A7 ruling 13, which supersedes 14 §8.1's xoshiro256\*\*/`SaveState()` text): every in-run draw is `Hash64(runSeed, streamName, drawIndex)` (xxHash64) over the named streams (`board`, `dice`, `draft`, `drops`, `combat:{battleIndex}`, `events`, `minigame:{index}`), the persisted stream position *is* the draw counter, and out-of-run meta commands draw from the server-issued `GameContext.CommandSeed`. It is deliberately *not* a port — it's part of the rules, not an external dependency. Fix the seed(s) on the state/context explicitly and assert the exact outcome at a pinned draw index; never assert on an unseeded roll, and never model randomness as a stateful generator you seed and step.
- Ids come from `IIdGeneratorPort` (a shared/determinism-critical port) — use its fake rather than letting a test depend on `Guid.NewGuid()` output.

## Determinism is a first-class test concern, not an edge case

Combat, board generation, and drafting are specified as byte-identical given the same `(seed, snapshot)` — this is load-bearing for PvP fairness and client/server parity (14 §8.2). When testing anything that touches the deterministic draw streams or the combat simulator:

- Fix the seed explicitly and assert the exact expected sequence/outcome, not just "it produced *a* result."
- Internal values are `double`, rounded to 4 decimal places at every accumulation point (`Math.Round(x, 4)`) — assert against the rounded value, not raw floating-point equality, and never assert with a wider tolerance than the spec's own rounding rule (that would hide a real rounding-order bug).
- For a full simulated fight, prefer asserting on `SimulationResult.LogHash` (or a specific `CombatEvent` in `Log`) over hand-verifying every tick — that mirrors how the project's own determinism CI test and PvP anti-cheat verification work.
- Every randomised grant routes through `LuckService` (24 §11). A pity guarantee needs an explicit unit test that it fires at exactly `N` **and** a property test that it never fires later than `N` across 100,000 seeded sequences (24 §11) — assert via the `PityCounterAdvanced` event and `GearGranted.FromPity`. Ads never advance pity, and dungeons advance no counter at all — good testable negatives.

## Data-driven cases

No `if`, `for`, `switch` inside a test body. Use `[Theory]` / `[InlineData]` / `[MemberData]` for data-driven cases — e.g. stat-cap boundaries, the stat-resolution stages (18 §8: flat → percent → convert → multiplicative → set → cap → round), or a table of `(chapter, tier)` pairs for board generation constraints.

## Effect DSL tests — drive the resolver generically

Every perk, talent, gear affix, pet aura, mount bonus, status effect, event outcome, shrine buff, curse, and boss mechanic is expressed through the **one** generic effect resolver in `SlayIdleRepeat.Core/Effects` (18 §1) — never per-entity code. When testing a new perk/talent/boss mechanic:

- Test it as **data** flowing through `EffectResolver`, the same way every other effect is tested — construct the `EffectDefinition` (op/trigger/condition/target/value/duration/stacking) and assert the resolver's output, not a hand-written special case.
- If the request needs a new DSL operation, trigger, or condition that doesn't exist yet, that is itself the thing under test: add the op to `Effects/Ops`, the JSON schema, `game-design/18_EFFECT_DSL.md` itself, and the client/server parity test, with a unit test for the op in isolation — all in the same change (18 §10) — and never write `if (perkId == "PK_X")` (or a `CP_`/`BOSS_`-prefixed equivalent) anywhere as a substitute. One sanctioned exception exists: `MODIFY_DIE_FACE`'s combat-context special case (16 §A7 ruling 9).
- The resolution order (18 §8: flat → percent → convert → multiplicative → set → cap → round; ascending lexicographic effect-ID order governs the convert/multiplicative/set stages — flat and percent are order-independent sums) is exact and load-bearing for client/server parity. A test asserting only a final stat value without pinning intermediate steps can pass while the resolver applies stages in the wrong order — prefer a test that would catch an order swap when the feature's behaviour depends on order (e.g. two stacking percent buffs plus one multiplicative one).

## What you must never do

- Introduce a mocking library when the project's own in-memory fake will do.
- Rely on the real system clock, `System.Random`, `Random.Shared`, `GD.Randi()`, or `Environment.TickCount` — these are CI-grepped and banned inside `SlayIdleRepeat.Core`/`SlayIdleRepeat.Application` (14 §8.1); a test that needs one of these to compile is a sign production code reached for the wrong source.
- Reach into private members via reflection, or assert on internal *state* a caller can't observe. (`InternalsVisibleTo` itself is sanctioned for `SlayIdleRepeat.Core.Tests` only — 30 §11.3 — because handlers and rule calculators are `internal` by design; any other test project using it is a finding.)
- Reference a concrete adapter type, a vendor SDK type (Npgsql, StackExchange.Redis, the AppLovin plugin, etc.), or a Godot type (`Node`, `GD.*`) from a `SlayIdleRepeat.Core.Tests` or `SlayIdleRepeat.Application.Tests` test — those tiers exercise pure rules and use cases against ports/fakes only, mirroring the production dependency rule (`Adapters → Application → Core → nothing`).
- Write a test in — or that requires — `SlayIdleRepeat.Integration.Tests`, `SlayIdleRepeat.Contract.Tests`, or anything that boots Docker/Postgres/Redis/a real ad SDK/the Godot runtime. That tier exists in this project's own CI but is explicitly out of scope for this workflow.
- Hand-verify a value that the Effect DSL resolver, stat-aggregation pipeline, or `DeterministicRng` should be producing — assert on the pipeline's output instead of re-deriving the math inline in the test.
