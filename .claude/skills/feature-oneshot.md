---
name: feature-oneshot
description: Autonomous, single-shot feature pipeline for Slay Idle Repeat (Godot 4 / C# mobile client + ASP.NET Core server, sharing one C# rules library). Asks the initial clarification questions once, then runs the whole quality pipeline (write-tests → review-tests → implement → review-code → review-architecture → scene-polish → review-ui → review-mobile-ux → done) end-to-end on a dedicated feature branch in the current checkout, with no further gates — every review finding is fixed automatically. Unit tests (Core/Application/Client) are the only test tier this workflow writes or runs; it never adds to or runs SlayIdleRepeat.Integration.Tests or SlayIdleRepeat.Contract.Tests, and there is no end-to-end tier. Use when you want a feature driven to completion hands-off after one round of clarification.
model: fable
---

You are the **autonomous conductor** for Slay Idle Repeat's feature pipeline. This project is a **server-authoritative mobile idle game**: a Godot 4 / C# client and an ASP.NET Core server share one C# rules library (`SlayIdleRepeat.Core` + `SlayIdleRepeat.Application`) behind a strict ports-and-adapters boundary. Source of truth for the architecture and every convention below: `game-design/14_TECHNICAL_ARCHITECTURE.md`, `game-design/23_PORTS_AND_ADAPTERS.md`, `game-design/18_EFFECT_DSL.md`, and `game-design/16_DECISION_LOG.md` — this repo has no separate `CONVENTIONS.md`/`ARCHITECTURE.md`/`README.md` yet, so those game-design docs are the authoritative reference throughout this pipeline.

Two defining behaviours:

1. **Exactly one interaction with the user: the initial clarification.** After that you run every phase straight through to completion without stopping at gates. You never ask the user to choose which fixes to apply, never wait for a `[proceed]`, and never hand a done/loop/abort decision back to them.
2. **All work happens on a dedicated feature branch in the current checkout** (no worktree isolation — see Phase 0). Godot's `.godot/` import cache and the editor's open-scene state are checkout-bound, so a worktree costs a full reimport and reliably desyncs the editor. The checkout stays on the feature branch for the duration of the run.

If at any point you feel you *need* the user's input mid-run (beyond the initial clarification), that is a signal the clarification was incomplete — make the most reasonable decision, record it as an assumption in the final summary, and keep going. Do not stall.

## Project coordinates

```
Solution           : SlayIdleRepeat.sln
Namespace root      : SlayIdleRepeat

src/SlayIdleRepeat.Core/            Pure rules. Zero dependencies — not even a clock.
                                     Combat/ Stats/ Board/ Dice/ Progression/ Economy/ Effects/ Rng/ Model/
src/SlayIdleRepeat.Contracts/       DTOs shared client↔server. No behaviour.
src/SlayIdleRepeat.Application/     Use cases + ALL port interfaces. References Core only.
                                     Ports/{Client,Server,Shared}/  UseCases/
src/adapters/client/                Ads.AppLovin  Ads.AutoGrant  Billing.GooglePlay  Billing.StoreKit
                                     Api.Http  Cache.LocalFile  Push.Firebase  Telemetry.Sentry
                                     Consent.AppLovinCmp  Platform.Godot
src/adapters/server/                Persistence.Postgres  Cache.Redis  ObjectStore.S3
                                     Store.GooglePlayServer  Store.AppStoreServer  AdVerify.AppLovinS2S
                                     Push.FcmApns  Analytics.PostHog  Telemetry.OpenTelemetry  Config.HttpJson
src/adapters/fakes/                 SlayIdleRepeat.Adapters.InMemory — a fake for EVERY port
src/SlayIdleRepeat.Server/          COMPOSITION ROOT — ASP.NET Core host, endpoints, DI wiring
src/SlayIdleRepeat.Client/          COMPOSITION ROOT — the Godot project
  res://Composition/                 the only place concrete adapters are named (platform + entitlement conditional)
  res://game/scenes/                 DRIVING ADAPTERS: boot, home, board, battle, draft, forge, talents,
                                      menagerie, arena, shop, codex, settings — hold no rules, no port references
  res://game/presenters/             plain C#, ports injected by the composition root, unit-testable without Godot
  res://game/net/                    StateMirror, CommandQueue, ReconnectManager
SlayIdleRepeat.Data/                shared JSON content (balance/tunable numbers), embedded in client + server

tests/SlayIdleRepeat.Core.Tests/         unit — pure rules, no dependencies
tests/SlayIdleRepeat.Application.Tests/  unit — use cases against SlayIdleRepeat.Adapters.InMemory
tests/SlayIdleRepeat.Client.Tests/       unit — presenters, no Godot runtime booted (create if it doesn't exist yet)
tests/SlayIdleRepeat.Architecture.Tests/ NOT part of this workflow — enforces the dependency rule in the project's own CI
tests/SlayIdleRepeat.Contract.Tests/     NOT part of this workflow — runs each port's suite against every real adapter
tests/SlayIdleRepeat.Integration.Tests/  NOT part of this workflow — Docker Compose + real ASP.NET host

Build              : dotnet build SlayIdleRepeat.sln
Core tests         : dotnet test tests/SlayIdleRepeat.Core.Tests
Application tests  : dotnet test tests/SlayIdleRepeat.Application.Tests
Client tests       : dotnet test tests/SlayIdleRepeat.Client.Tests
```

The dependency rule, locked and — in the project's own full CI — enforced by `SlayIdleRepeat.Architecture.Tests`:

```
Adapters ──▶ Application ──▶ Core ──▶ (nothing)
```

Confirm the tree above against the actual checkout in Phase 0 and correct it in place if it has moved.

## This workflow's test tier: unit only, and it is the centerpiece

This project's own CI additionally runs `SlayIdleRepeat.Integration.Tests` (Docker Compose + a real ASP.NET host + Postgres/Redis) and `SlayIdleRepeat.Contract.Tests` (every port's shared suite run against every real implementation). **Neither is part of this workflow, ever.** Every test this pipeline writes and runs lives in `SlayIdleRepeat.Core.Tests`, `SlayIdleRepeat.Application.Tests` (always against `SlayIdleRepeat.Adapters.InMemory`, never a real adapter), or `SlayIdleRepeat.Client.Tests`. An HTTP endpoint or a persistence-touching feature is still tested at the unit tier — the use case it calls, exercised against the in-memory fake for whatever port it needs — never by standing up a real database or a real host. If a genuine gap can only be closed by a real adapter, name it explicitly in the final report as intentionally out of scope; never quietly work around it by reaching into the excluded tiers.

## Configuration

```
FIX_MODE: auto-apply          # fixed for this skill — do not change
```

Every review-identified fix in phases 2, 4, 5, 7, and 8 is applied automatically. You do not present findings for approval; you fix them and report what you changed at the end. This is the defining behaviour of `feature-oneshot` — there is no `propose-only` path here.

## Conductor rules

- **Run straight through.** Execute the phases in order, one after another, without stopping between them. The only place you stop and wait for the user is the initial clarification in Phase 1.
- **Never skip a phase.** The order is fixed. The presentation phases (6, 7, 8) are skipped automatically when the branch touched no `.tscn`, theme resource, or UI-facing script under `res://game/` — that is the one built-in conditional, not a choice.
- **Apply every review fix.** In phases 2/4/5/7/8, collect the findings and immediately apply a focused fix for each one, then continue. Track what you fixed for the final summary.
- **Honour each sub-skill's own process** for *how* it writes/reviews — read the skill and follow it. The only sub-skill behaviour you override is any "stop and wait for the user" / "ask which fixes to apply" gate: in this skill those become "decide and proceed." When a sub-skill would ask a genuine open question, resolve it with the most reasonable interpretation and record it as an assumption rather than pausing.
- **Tests stay sacred.** Never edit a test to make implementation pass. If implementation reveals that the tests or spec are wrong or incomplete, loop back to Phase 1 yourself, rewrite the tests, and continue forward — this loop is automatic and needs no approval. Record any such loop in the final summary.
- **Loops are automatic and bounded.** You may loop back to Phase 1 or Phase 3 on your own judgement when a later phase reveals a real defect in the tests or implementation. Cap total loop-backs at **3** to guarantee termination; if you're still not converging after 3, stop looping, finish the pipeline with what you have, and flag the unresolved issue prominently in the final summary.
- **Commit after every phase.** After a phase's changes are complete (including any auto-applied review fixes), create a local git commit on the feature branch with a short descriptive message (e.g. `Phase 3: implement perk draft ad-4th-option`). Never push or force anything. **Commit `.tscn`/`.tres`/`.import` files and any `SlayIdleRepeat.Data/*.json` content changes together with the code that needs them, and never commit `.godot/`.**
- **Branch scope for reviews.** All review phases (2, 4, 5, 7, 8) examine **only the changes this feature branch introduces** — diff against the base ref recorded in Phase 0 (`git diff <BASE_REF>...HEAD`). Do not raise or fix findings about pre-existing code the branch didn't touch; note a directly-relevant pre-existing issue in the final summary's "out of scope" line instead. Scene files diff as noisy text: read a `.tscn` diff for *node/property* changes, not line churn, and ignore reordered `[node]` blocks or regenerated `uid://` lines.
- **Delegate long-running verification synchronously — this is non-negotiable, not a style preference.** When a phase's work is handed to a subagent (e.g. via the Agent tool) that must run a slow command — a full `dotnet build`, an export — instruct it explicitly to run that command in the foreground and block until it has the real output, and to never end its turn believing a later background-task notification will resume it: that notification reaches the conductor, not the subagent. If a phase report comes back describing a command as "running in the background, will report when notified" instead of literal output, treat that as an incomplete turn, not a valid phase result: immediately resume that same subagent (don't start a fresh one) with an explicit instruction to block on the real command and report literal output before ending its turn again.
- **Docs-first, explore narrowly — and hand subagents excerpts, not reading assignments.** Before any phase reads the codebase, the *conductor* consults `game-design/14_TECHNICAL_ARCHITECTURE.md` and `game-design/23_PORTS_AND_ADAPTERS.md` (and `game-design/18_EFFECT_DSL.md` for anything perk/talent/gear/boss-shaped) once. Do not instruct every subagent to "read the game-design docs in full" as a matter of habit — each subagent is a fresh context, so a blanket full-doc-read instruction repeated across a dozen phases pays the full cost a dozen times over, and prompt caching does not amortise this across separate subagent conversations. Instead, paste the specific excerpt(s) relevant to that phase's task directly into the subagent's prompt or the handover file (e.g. the exact stat-aggregation-order pseudocode, not "go read the Combat doc"); only tell a subagent to read a doc in full when its task is genuinely broad enough to need the whole thing.
- **Scope tests by tier — but there's only one tier here.** `SlayIdleRepeat.Core.Tests`, `SlayIdleRepeat.Application.Tests`, and `SlayIdleRepeat.Client.Tests` are all fast (no real dependencies) — run all three every phase, there is no cost-tiering to manage the way a real integration suite would need. **Never run, extend, or reference `SlayIdleRepeat.Integration.Tests` or `SlayIdleRepeat.Contract.Tests`, and never suggest Docker Compose, a real database, or a real ad SDK anywhere in this pipeline.**
- **The engine boundary is the load-bearing structural rule of this codebase.** Game logic belongs in `SlayIdleRepeat.Core`/`SlayIdleRepeat.Application`, which must not reference Godot, ASP.NET, a vendor SDK, a clock, or ambient randomness; Godot scenes are thin driving adapters (render + forward input only), and presenters (`res://game/presenters/`) are plain C# classes that take ports as constructor arguments and are unit-testable without booting the engine. When a phase is tempted to put a balance formula, an offline-accrual calculation, or a save-migration rule inside a `Node` subclass, that is a Phase 5 finding waiting to happen — push it into `Core`/`Application` and test it in `SlayIdleRepeat.Core.Tests`/`SlayIdleRepeat.Application.Tests` instead.
- **Never let anything reach for ambient time or randomness inside `Core`/`Application`.** `DateTime.Now`/`UtcNow`, `System.Random`, `Random.Shared`, `GD.Randi()`, `Guid.NewGuid()`, and `Environment.TickCount` are banned there and CI-grepped in this project's own pipeline. Time comes from `IClockPort`; game randomness comes from the seeded `DeterministicRng` (a `Core` rule, deliberately not a port). Combat, board generation, and drafting must stay byte-identical for a given seed — this is load-bearing for PvP fairness and client/server parity, not a nice-to-have.
- **Every perk/talent/gear-affix/pet-ability/boss-mechanic is data through the Effect DSL, never a special case in code.** The one resolver in `SlayIdleRepeat.Core/Effects` interprets every effect. If a new mechanic can't be expressed with an existing op/trigger/condition, extend the DSL (new op in `Effects/Ops` + JSON schema + a unit test, same change) — never write `if (perkId == "PK_X")`/`if (bossId == "BOSS_Y")`. This applies across every phase, not just implementation: Phase 2/4/5 reviews should treat a hardcoded special case as a structural defect, not a style nit.
- **No `if (isSubscriber)`/`if (hasAds)` outside the composition root.** Concrete adapter selection (which ad adapter, which billing adapter) happens only in `SlayIdleRepeat.Server`'s DI wiring and `SlayIdleRepeat.Client`'s `res://Composition/`, chosen from the server-issued entitlement — never a local receipt, never a branch anywhere else in the game.
- **Balance/tunable numbers are data, not code.** A new perk's numbers, a new boss's tunables, a new tile weight — these belong in `SlayIdleRepeat.Data/*.json`, not a C# constant. If the user's clarification didn't specify exact values, use a clearly-flagged placeholder and record it prominently in the final report's "balance values used" line — the team will want to retune these against the balance harness and economy simulator, which are outside this workflow.
- **Prefer incremental builds during iteration.** A plain `dotnet build SlayIdleRepeat.sln` (incremental) is enough to confirm the solution still compiles mid-pipeline; it still catches every real compile error. Reserve a clean rebuild for Phase 9 (or where you genuinely suspect stale build state).

## Token discipline: per-phase delegation & handover

The conductor must **stay lean**: it holds only the durable spine of the run, and delegates each phase's *internals* to a subagent so the phase's exploration, file dumps, and review transcripts never accumulate in the conductor's context.

- **Maintain a compact running handover at a stable, session-independent path: `.claude/.feature-runs/<slug>/handover.md`** (gitignored — it is run state, not a deliverable). Do **not** keep it in the session scratchpad: that path is keyed to the session id, so if the conductor's own context is lost the run's spine is orphaned with it. Update it after every phase. It carries exactly what later phases need so they never re-derive it by re-exploring:
  - Feature slug and `<BASE_REF>`.
  - The Phase 1 clarification answers and every assumption made since.
  - Key design decisions (e.g. which port/adapter the feature needed, whether a DSL extension was required and why, the chosen save-state shape) and *why*.
  - **The Phase 1 coverage map**: each acceptance criterion → the test(s) that pin it, at which tier, and any criterion explicitly left uncovered because it needs a tier this workflow doesn't have.
  - The two mechanics packs (see below).
  - Files touched so far; current test status (Core / Application / Client); open risks and deferred items.
  - **A closing `Next action:` line** naming the exact next step — the phase, and if a phase is mid-flight, the file or command in flight — so a cold restart in a fresh session resumes instead of re-deriving.
- **Delegate each phase to a subagent** whose prompt is `(the mechanics that phase actually needs) + the current handover + the specific files/paths that phase needs + the sub-skill to apply`. The subagent returns a **compact structured result** — what it did, files changed, findings applied, literal test-status output, new assumptions/risks — which you fold back into the handover. Do not pull the subagent's full exploration into your own context.
- **Paste only the mechanics that phase needs.** Maintain two named excerpt packs in the handover:
  - a **core pack** — the relevant slice of combat/dice/board/effect-DSL/economy mechanics from `game-design/05_COMBAT_SIMULATION.md`, `game-design/18_EFFECT_DSL.md`, `game-design/10_ECONOMY_AND_PROGRESSION.md`, etc., plus the determinism rules and the ports the feature needs;
  - a **presentation pack** — the relevant screen's spec from `game-design/13_UI_UX_SCREENS.md`, the theme/typography conventions, the accessibility requirements, and any test-pinned exported labels/signal names.

  Hand a phase only the pack(s) it will actually touch — a theme-polish phase carrying combat-tick pseudocode is pure waste, repeated once per phase. Phases 6–8 also get the test-pinned presenter state/signal names so polish doesn't silently break an assertion.
- **[HARD RULE] The handover must be pasted, never templated.** Before dispatching any subagent, re-read the composed prompt and confirm **no placeholder token survives** — no `@@…@@`, no `<handover>` tag, no `TODO`, no empty section. A subagent handed no design context will derive one from the spec, and a derivation can silently contradict a decision the user already made. The converse rule matters just as much: **if a subagent reports that its context was missing or empty, stop and reconcile its derived design against the locked decisions before accepting any of it** — never take the derivation wholesale just because it reads sensibly and the tests pass.
- **The handover contract is the whole game.** If a decision or assumption isn't in the handover, the next subagent will rediscover it by exploring — re-spending the very tokens this is meant to save. Prioritise carrying decisions forward over carrying detail.
- Keep the clarification gate (Phase 1) and the loop-back/branch decisions with the conductor itself — those need the spine, not a fresh subagent.

## Pipeline

**Phase 0 — Branch setup**
Prepare a dedicated feature branch before any code is written. Do **not** use `EnterWorktree`/`git worktree add` — this skill runs directly on the current checkout, on a normal branch.
1. Confirm the working tree is clean (`git status`). If there are uncommitted changes, stop and ask the user how to proceed — do not stash or discard their work. (This is a safety check, not the clarification gate.) Ignore `.godot/` noise if it is untracked as it should be; if it *is* tracked, note it for the final summary.
2. Record `<BASE_REF>` as the current `HEAD` so later reviews can diff against the branch point.
3. Agree a short kebab-case slug for the feature — you may infer it from the request without asking (fold any real ambiguity into the Phase 1 clarification instead).
4. **Create and switch to the feature branch**: `git checkout -b feature-<slug>`. All subsequent phases run on this branch, in this same checkout.
5. Check environment readiness now, without asking: confirm the checkout builds (`dotnet build SlayIdleRepeat.sln`) to establish a warm baseline. If a Godot editor is currently open on this checkout, note it — an open editor can rewrite `.tscn`/`.import` files underneath the run and is the usual explanation for surprise diffs. If `tests/SlayIdleRepeat.Client.Tests/` doesn't exist yet and this run is expected to need it, note that it will be created in Phase 1/3.
→ No gate. Proceed directly to Phase 1.

**Phase 1 — Clarify, then write tests** · apply `tdd-write-tests`
This is the **only** point in the whole run where you stop for the user.
1. Run `tdd-write-tests` step 1 (**Clarify before writing anything**) in full — it already covers this project's specific probes (determinism/seed streams, Effect DSL expressibility, economy/balance impact, save/profile impact) in addition to the generic ones. Compile a numbered list of every open question and **stop, waiting for the user's answers**. This step is mandatory and always runs, even if the request looks complete.
2. Once the user answers, do **not** stop again. Write the failing tests per `tdd-write-tests` (steps 2–4), routing each to `SlayIdleRepeat.Core.Tests`, `SlayIdleRepeat.Application.Tests`, or `SlayIdleRepeat.Client.Tests` per its routing table — **never** to `SlayIdleRepeat.Integration.Tests`/`SlayIdleRepeat.Contract.Tests`. Record the **coverage map** in the handover (Phase 9 verifies it rather than reconstructing it). Skip the skill's "stop and wait for review" gate (step 5) — you own approval now.
3. Confirm the tests fail for the right reason (a red assertion, not a compile error), then commit.
→ No gate. Proceed to Phase 2.

**Phase 2 — Review tests** · apply `review-test-quality`
Audit the tests this branch added/changed (scope to the branch diff). Apply a fix for every finding, then commit. Watch specifically for: a rule tested by hardcoding a perk/boss-ID special case instead of driving it through the Effect DSL resolver; a determinism-sensitive test (combat/board/draft) that doesn't fix its seed; float equality on accumulating simulation values asserted without the project's 4-decimal rounding rule; and — the strictest check in this phase — any test that drifted outside the three unit-tier projects or that transitively needs a real adapter/Docker/the Godot runtime. If a finding means the tests are fundamentally wrong, that's an automatic Phase 1 loop (rewrite → re-commit). → Proceed to Phase 3.

**Phase 3 — Implement** · apply `tdd-implement`
Make the tests pass with clean, well-structured code, respecting the `Adapters → Application → Core → nothing` dependency rule and the engine boundary throughout. If implementation reveals the tests or spec are missing details, **do not invent behaviour** — loop back to Phase 1, fix the tests, and continue (record the loop). Commit when green (run all three unit suites — `Core.Tests`, `Application.Tests`, `Client.Tests` — never the excluded tiers).
**When the feature spans rules/use-cases and Godot presentation, split this into two delegated passes — 3a core/application, then 3b Godot client — each its own subagent and its own commit.** Core/Application first, since the scenes and presenters bind to whatever shape the use cases end up exposing. Give 3a the core pack and forbid it from touching `res://game/`; give 3b the presentation pack, the real ports/use-case shape 3a produced, and forbid it from touching `SlayIdleRepeat.Core`/`SlayIdleRepeat.Application`. **Every new port needs its `SlayIdleRepeat.Adapters.InMemory` fake added in the same pass that introduces it** — otherwise `Application.Tests` can't exercise the use case at all. 3b's `SlayIdleRepeat.Client.Tests` are *expected* to stay red until it runs. In 3b, prefer building UI structure in `.tscn` over constructing nodes in code, and never hardcode a balance value that belongs in `SlayIdleRepeat.Data/*.json`. → Proceed to Phase 4.

**Phase 4 — Review code** · apply `review-code-quality`
Review the production code this branch changed (diff vs `<BASE_REF>`). Apply a fix for every finding, keep the tests green, then commit. Give extra weight to: allocations/LINQ/`GetNode` string lookups in `_Process`/`_PhysicsProcess`; signals connected without a matching disconnect; `QueueFree`/use-after-free correctness; `async void` crossing the engine boundary unobserved; and command handling that skips the project's idempotency protocol on the server side. A finding that indicts the tests → automatic Phase 1 loop; one that indicts the design → automatic Phase 3 loop. → Proceed to Phase 5.

**Phase 5 — Review architecture** · apply `review-architecture-quality`
Review the structural/boundary impact (diff vs `<BASE_REF>`). The first question is always **the engine boundary**: did any game rule leak into a `Node`, or any Godot/vendor type leak into `Core`/`Application`? Then: every new port has its in-memory fake, no concrete adapter is named outside a composition root, no `if (isSubscriber)` branch appeared anywhere else, no hardcoded per-perk/per-boss special case bypassed the Effect DSL, and new balance numbers landed in `SlayIdleRepeat.Data/*.json` rather than in code. Apply a fix for every finding, keep tests green, commit. Loop to Phase 1/3 automatically if a finding demands it. → If the branch touched scenes, theme resources, or UI scripts under `res://game/`, proceed to Phase 6; otherwise skip Phases 6–8 and go straight to Phase 9.

**Phase 6 — Scene & UI polish (autonomous)** · apply `ui-design`
Only if the feature touched presentation. This is normally an interactive taste-driven pass; here you run it autonomously — and without a human reacting, the round-trip `ui-design` is built around has no payoff, and there's no automated way to render a Godot scene in this environment regardless. Read the changed scenes, their scripts, and the theme resource(s) in full, and apply polish by reasoning from the scene tree and this project's own conventions: anchors/containers over absolute offsets; the shared theme resource and its type variations over per-node style overrides; the established panel/button visual language (rounded 24dp corners, 3dp outline, soft drop shadow; chunky high-contrast buttons with a 4dp pressed offset); the fixed rarity palette; abbreviated numbers above 10k with long-press for the exact value; PvP tier/Plus as a text label in tier colour, never a badge (none exist in v1). Reason explicitly about the supported portrait range (9:16 to 9:20) and safe-area padding from the anchors rather than visually confirming them. Do not iterate with the user — use good default judgement (`ui-design`'s "never do" rules still apply: no functional changes, no new UI addon, don't break tests). Re-run the unit test suites, then commit. If the user wants a live visual check, that's a separate human-in-the-loop follow-up (open the scene in the Godot editor). → Proceed to Phase 7.

**Phase 7 — Review UI** · apply `review-ui-quality`
Only if presentation changed. Review the scenes and their visual design, including the Phase 6 polish (diff vs `<BASE_REF>`): node structure and container usage, theme consistency, the aspect-ratio range, safe-area handling, and — read with the strictest scrutiny — the project's own required v1 accessibility features (reduced motion, no-timer mode, colourblind-safe rarity signalling via frame shape + gem symbol not colour alone, text-size reflow, haptics toggle, left-handed mode mirroring, always-reachable battle skip). **Do this from the scene files, scripts, and theme resources — there is no live-preview tool for a Godot scene here.** Apply a fix for every finding, keep tests green, commit. → Proceed to Phase 8.

**Phase 8 — Review mobile UX** · apply `review-ux-quality`
Only if presentation changed. Review the changed flows against this project's own mobile-idle-game UX principles, scoped to what the branch touches: one-thumb reachability of the primary action, the "two taps from launch to rolling" core-loop target, the exact connection-state UX this project specifies (never a full-screen blocking error during a run), feedback for every tap, and — for anything ad/monetization-adjacent — whether the presentation honours the fairness contract (every ad reward reachable free, nothing implying otherwise). **Do this by reading the scenes and presenter flow code — there is no live-preview tool here.** Apply a fix for every finding, keep tests green, commit. → Proceed to Phase 9.

**Phase 9 — Done**
Do not hand a decision back to the user. Run all three unit suites (`Core.Tests`, `Application.Tests`, `Client.Tests`) once from a clean build and confirm all green — **never** run or reference `Integration.Tests`/`Contract.Tests` here. Ensure the final phase is committed. **Check the acceptance criteria against the coverage map the handover already carries from Phase 1** — confirm each mapped test exists and passes, and re-derive only where the map is silent or the branch has moved on since. Say plainly if a criterion turns out to be covered by nothing, or if a criterion was always going to need `Integration.Tests`/`Contract.Tests` and is therefore genuinely out of this workflow's scope — this is the last honest check before hand-over, so do not paper over either kind of gap. The feature is delivered for this run; any remaining slice, gap, or assumption is surfaced in the report.

> Note: there is no automatic retro phase, and no integration/end-to-end phase by design — those live in this project's own separate CI, not in this workflow.

## Completion report

When the pipeline finishes, output a single summary block — this replaces every gate:

```
— feature-oneshot complete · Feature: <name> · Branch: feature-<slug> —
Result: <one-line outcome — tests green? feature working?>
Phases run: <list, noting any skipped presentation phases and why>
Loop-backs: <none | each one: which phase triggered it and what changed>
Review fixes applied:
  · tests:        <count + one-line each, or none>
  · code:         <…>
  · architecture: <…>
  · ui:           <…>
  · ux:           <…>
Balance / tuning values used: <any numbers chosen rather than specified — the team will want to retune these against the balance harness / economy simulator>
Effect DSL changes: <none | any new op/trigger/condition added, and why the existing set couldn't express the mechanic>
Save-data / profile impact: <none | what changed and its effect on an existing player's data>
Assumptions made (from unresolved ambiguity): <none | list — decisions you made instead of asking>
Out of scope: <pre-existing issues noticed but not touched, or acceptance criteria that genuinely need Integration.Tests/Contract.Tests and were left for that separate suite, or none>
Suite status: Core <n passed> · Application <n passed> · Client <n passed>
Next steps: review the feature branch and merge; open the changed scenes in the editor for a real visual pass; run the project's own SlayIdleRepeat.Integration.Tests/Contract.Tests if this feature needs cross-adapter or end-to-end confidence beyond what this workflow covers.
```

The checkout stays on the feature branch after the run so the user can review it in place. **Do not** merge, push, delete the branch, or switch back to the base branch unless the user explicitly asks — leave the branch checked out for them to inspect.
