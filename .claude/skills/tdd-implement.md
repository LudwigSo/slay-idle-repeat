---
name: tdd-implement
description: TDD implementation for Slay Idle Repeat — write clean, well-structured production code that makes all failing unit tests pass, respecting the Core → Application → Adapters → Composition-root dependency rule. Never modifies tests. Never writes integration/E2E tests (this repository has no such tier) and never starts Docker or any infrastructure.
model: sonnet
---

You are making the failing tests pass with production code that is **correct and well-structured on the first pass**. Get the design right as you write it — there is no separate refactor phase.

## The contract you must never break

1. **Tests are the specification.** Read them as requirements. Do not change, remove, or skip any test.
2. **Implement only what the tests require — but implement it well.** Do not add public methods, properties, or classes that no test exercises, yet make the code you do write clean, clearly named, and free of duplication.
3. **Do not gold-plate.** No speculative abstraction layers, no design patterns applied "just in case", no extension points for hypothetical future requirements.
4. **Tests stay green.** After writing the implementation, run the full unit test suites (`SlayIdleRepeat.Core.Tests`, `SlayIdleRepeat.Application.Tests`, `SlayIdleRepeat.Client.Tests`). **Never run or add to `SlayIdleRepeat.Contract.Tests`** — it exists in this project's own CI but is out of scope for this workflow. **There is no integration or end-to-end tier in this repository and you must not create one**, under any name, nor start Docker or any other infrastructure.

## The dependency rule you must never break

```
Adapters ──▶ Application ──▶ Core ──▶ (nothing)
```

- `SlayIdleRepeat.Core` references nothing but the .NET BCL, and is **fully synchronous — no `Task`, `async`, or `CancellationToken` anywhere in it** (30 §2.1, architecture-tested). **No Godot, no ASP.NET, no vendor SDK, no clock (`DateTime.Now`/`UtcNow`), no ambient randomness (`System.Random`, `Random.Shared`, `GD.Randi()`, `Guid.NewGuid()`, `Environment.TickCount`).** These specific APIs are CI-grepped and banned inside `Core` and `Application` (14 §8.1) — if you reach for one, stop: time enters `Core` as `GameContext.NowUtc` (`IClockPort` is called by the composition root and must never appear in `Core` itself — 30 §3), and randomness is the counter-based deterministic draw `Hash64(runSeed, streamName, drawIndex)` (16 §A7 ruling 13).
- `SlayIdleRepeat.Application` defines every port (driving and driven) and references only `Core` and `Contracts` (23 §2.1). It never references an adapter, and it contains **no game rules** — a use case loads a slice, calls `GameRules.Apply`, persists, and dispatches events (23 §2.0a).
- Each `SlayIdleRepeat.Adapters.*` project implements the ports it needs, references `Application` plus its own vendor package, and never references another adapter project.
- Only `SlayIdleRepeat.Server` and `SlayIdleRepeat.Client` (the two composition roots) may reference `Adapters.*`. Godot itself is an adapter (`SlayIdleRepeat.Adapters.Platform.Godot`), not a foundation — a Godot scene/node holds no rules and no port reference; it renders and forwards input to a presenter.
- Balance/tunable numbers live in `game-data/tuning/*.json` specifically (21 §3.1 — a 📐-marked tunable outside `tuning/` is a bug, and a build check enumerates 📐 markers against schema keys), never hardcoded in `Core`/`Application` — a new perk/talent/boss value belongs in a content file, validated by schema at build time, not a constant in code.

Source of truth for all of the above: `game-design/14_TECHNICAL_ARCHITECTURE.md`, `game-design/23_PORTS_AND_ADAPTERS.md`, and `game-design/30_DOMAIN_MODEL.md` (authoritative for `Core`'s internal shape: `GameRules.Apply` as the only public mutation, internal handlers/rules, the `Handlers → Rules → Model → Content → Primitives` layering). `game-design/16_DECISION_LOG.md` §A7's rulings override contradicting text in the other docs until amendments land. This repo has no separate `CONVENTIONS.md`/`ARCHITECTURE.md` yet.

## Workflow

### Step 1 — Read the tests
Read all test files relevant to the feature. Identify:
- Which types need to exist (classes, interfaces, enums, effect DSL ops).
- Which public members are required (constructors, properties, methods).
- What the observable postconditions are (return values, `CommandResult`s and emitted `DomainEvent`s, `RejectionReason`s — domain rules reject, they never throw (30 §2.1) — `SimulationResult`/`LogHash`, state visible through an in-memory fake).

Do **not** infer internal design from test names or test double configuration. The tests define the public contract; you decide the internals.

### Step 2 — Identify what already exists
First read the relevant sections of `game-design/14_TECHNICAL_ARCHITECTURE.md` (dependency rule, layer responsibilities, testing/CI rules) and `game-design/23_PORTS_AND_ADAPTERS.md` (the ports catalogue and adapter rules A1–A10) — paste only the excerpt the feature actually needs, not the whole doc, if you're a delegated subagent. If the feature touches the Effect DSL, also check `game-design/18_EFFECT_DSL.md` for whether an existing op/trigger/condition already covers it. Then check `src/` and `res://game/` for existing code that partially or fully satisfies the tests. **Docs-first, then read the files you'll touch and their direct collaborators; explore wider only when the docs are silent.** Prefer extending existing code over creating new files.

### Step 3 — Write the implementation

Place production code in the correct layer, mirroring the namespace used in the tests:

- Game rules (combat, dice, board, effect DSL, talents, gear/merging, economy math, progression, luck protection, deterministic draws) → `src/SlayIdleRepeat.Core/` (references nothing but the BCL). New mutations are `internal` command handlers behind `GameRules.Apply` — the only public mutation in the game (30 §11; only `CombatSimulator` and `PowerCalculator` are public rules). Every currency mutation must emit a `CurrencyChanged` event (IL-scanned architecture test), and any code path granting a randomised item must route through `LuckService` (24 §11 — a drop table consulted directly is a bug).
- DTOs shared client↔server, no behaviour → `src/SlayIdleRepeat.Contracts/`.
- Use cases and every port interface (driving and driven) → `src/SlayIdleRepeat.Application/` (references `Core` only). Group ports under `Ports/{Client,Server,Shared}/` and use cases under `UseCases/`, matching the existing layout.
- A driven-port implementation → the relevant `src/adapters/{client,server}/<Category>.<Vendor>/` project (e.g. `Adapters.Ads.AppLovin`, `Adapters.Persistence.Postgres`). One adapter project per external dependency — never add a second concrete adapter to an existing vendor's project, and never share an "Infrastructure" grab-bag project.
- **Every new port needs an in-memory fake in `src/adapters/fakes/` (`SlayIdleRepeat.Adapters.InMemory`) in the same change** — this is what `SlayIdleRepeat.Application.Tests` will run against, and it's the project's own rule that every port has at least two implementations (23 §5, rule A5).
- Concrete adapter selection is named **only** in the composition roots: `SlayIdleRepeat.Server` (DI registration, e.g. a `services.AddSingleton<IClockPort, SystemClockAdapter>()`-style call) and `res://Composition/` in `SlayIdleRepeat.Client` (platform-conditional `#if ANDROID`/`#if IOS`, and entitlement-conditional — e.g. a Plus subscriber gets `AutoGrantAdAdapter` instead of `AppLovinRewardedAdAdapter`, chosen from the server-issued entitlement, never a local receipt or an `if (isSubscriber)` branch anywhere else in the game).
- Godot-facing code: scenes under `res://game/scenes/` render and forward input only — no rules, no port references. Presenters under `res://game/presenters/` are plain C# classes receiving ports as constructor arguments from the composition root; put orchestration logic here, not in a `Node` subclass, and never in `_Process`/`_PhysicsProcess`.
- Content/balance changes (a new perk's numbers, a new boss's tunables) → `game-data/tuning/*.json`, matching the existing schema for that content type. If the change needs a new Effect DSL op/trigger/condition, add it to `Core/Effects/Ops` **and** the JSON schema **and** `game-design/18_EFFECT_DSL.md` **and** the client/server parity test **and** a unit test, in the same change (18 §10) — never special-case a perk/talent/boss ID in code (18 preamble, §10). One sanctioned exception exists and must not be "fixed": `MODIFY_DIE_FACE`'s combat-context special case (16 §A7 ruling 9).

Respect the dependency direction: dependencies point inward and toward composition roots only, never the reverse.

Write the code well the first time, applying these quality bars as you go — not afterwards:

- **No duplication.** If the same logic or object-construction appears twice, extract a private method, factory, or value object before moving on.
- **Intent-revealing names.** Name classes, methods, and variables for what they mean in the game's own vocabulary — reuse the project's proper nouns (`Chapter`, `Stage`, `Tile`, `Perk`, `Talent`, `LegendLevel`, `Ghost`, stat names, effect op/trigger/condition keywords) rather than inventing generic synonyms. Never borrow names from test files or use single-letter names outside tight loops.
- **Right-sized methods.** Keep methods focused on one thing; split a method that grows past ~40 lines or does more than one job.
- **Simplest structure that works.** Pick the data structure that fits the access pattern. Prefer early-return guard clauses over nested conditionals.
- **Group values that travel together.** Model a snapshot/aggregate (a stat block, a loadout, a `CombatEvent`) as a `record`/value object instead of loose parameters.

Apply these only to code the tests require — they are about doing the required work cleanly, not about adding new surface area.

### Step 4 — Run the tests

Run tests in two stages so build errors don't hide test failures:

```
dotnet build SlayIdleRepeat.sln 2>&1
```

An incremental build is enough here — it still catches every real compile error, and repeating this step often (once per fix iteration) is exactly where a `--no-incremental` rebuild adds up. Reserve `--no-incremental` for a final, once-per-feature sanity check (or when you genuinely suspect stale incremental state).

If the build fails, fix the compilation errors before continuing — do not proceed to `dotnet test`.

Once the build succeeds, run the unit suites:

```
dotnet test tests/SlayIdleRepeat.Core.Tests --no-build --logger "console;verbosity=detailed" 2>&1
dotnet test tests/SlayIdleRepeat.Application.Tests --no-build --logger "console;verbosity=detailed" 2>&1
dotnet test tests/SlayIdleRepeat.Client.Tests --no-build --logger "console;verbosity=detailed" 2>&1
```

All three are fast (no real dependencies — `Application.Tests` runs against `Adapters.InMemory`, not a real adapter) — run all three every time, there is no cost tier to manage here the way there is for a real integration suite.

**Never run `SlayIdleRepeat.Contract.Tests`, never create an integration/E2E suite, and never start Docker Compose, a real database, or a real ad SDK as part of this workflow.** If a test genuinely can't be expressed at the unit tier (it needs a real Postgres instance, a real AppLovin callback, etc.), say so explicitly in the coverage map rather than reaching for infrastructure — that gap is intentionally out of scope here, not something to quietly work around.

From the output, collect every failing test in this format:
- **Test name** (the fully-qualified method name)
- **Failure message** (the assertion or exception message)
- **Expected vs Actual** if present

List all failures before making any code changes.

### Step 5 — Fix failures iteratively

For each failing test:
1. Re-read the test body to understand what it asserts.
2. Update the production code to satisfy that assertion without changing the test.
3. After all edits, re-run the relevant test command.
4. Repeat until the output contains no failures.

If the output exceeds what you can read at once, run tests for a single namespace or class:
```
dotnet test tests/SlayIdleRepeat.Core.Tests --no-build --filter "FullyQualifiedName~<Namespace>" --logger "console;verbosity=detailed" 2>&1
```

### Step 6 — Fix regressions
If any previously-passing test now fails, fix the implementation — never the test. Explain what caused the regression.

## Determinism checklist before declaring done

If the feature touches the deterministic draw streams, the combat simulator, or board generation:
- Confirm no new code path reaches for `System.Random`/`Random.Shared`/`GD.Randi()`/`DateTime.Now`/`Guid.NewGuid()`/`Environment.TickCount` inside `Core` or `Application`.
- Confirm randomness is a counter-based draw from a named stream with the draw counter persisted as state — never a stateful generator with save/restore semantics (16 §A7 ruling 13).
- Confirm accumulating `double` values are rounded to 4 decimal places at each accumulation point, matching the project's own rule (14 §8.2).
- If the feature added a new effect, confirm it composes correctly with the resolution order (18 §8: flat → percent → convert → multiplicative → set → cap → round; effect-ID ascending order for the convert/multiplicative/set stages) rather than being applied out of band.

## Hardcoding detection

If your first-pass implementation hardcodes a return value to pass a single test, flag it explicitly:

> **Note:** This is a stub that passes the current test via a hardcoded value. It will need to be generalised when more tests are added.

This applies with extra weight to the Effect DSL and content data: a hardcoded perk/talent/boss-ID branch that happens to satisfy today's test is exactly the "special case" the DSL exists to prevent — flag it loudly, don't just note it in passing.

## What you must never do

- Modify, delete, comment out, or skip any test.
- Add `// tested by:` comments or other annotations linking production code to specific tests.
- Pre-emptively add interfaces, base classes, or abstractions that no existing test requires.
- Change method signatures to avoid implementing logic.
- Write `if (perkId == "PK_X")`/`if (bossId == "BOSS_Y")`-style special casing anywhere the Effect DSL should express the behaviour instead.
- Write `if (isSubscriber)`/`if (hasAds)` anywhere outside the composition root's adapter selection.
- Touch `SlayIdleRepeat.Contract.Tests`, create an integration/E2E suite, start Docker or any other infrastructure, or reach for a real adapter/vendor SDK as part of making unit tests pass.

## When all tests pass

Report: **"All tests pass. Implementation complete."**

Summarize the implementation briefly (max 2 sentences).
If required offer me a prompt for another iteration on the feature (another call to /tdd-write-tests).
