# Architecture

Slay Idle Repeat is a Godot 4.7 / C# (.NET 8) mobile idle-roguelike (Android + iOS). The
codebase follows **hexagonal architecture (ports & adapters)**: a pure, engine-free domain
core, application use cases behind interfaces, and swappable adapters for everything that
touches the outside world (Godot, databases, ad networks, billing, push, telemetry).

What the game *is* — pillars, loops, systems — is summarised in
[`game-design.md`](game-design.md). This file is just a map for finding the right folder
quickly.

Three constraints from the design shape every folder below, so they are worth stating here:
the domain is a **pure, synchronous state machine with one entry point** and the whole game
must be playable in memory from `Core` alone; **every external dependency is a port** the
application owns and an adapter implements; and the simulation is **deterministic** — the
same seed and build produce a byte-identical battle log on device and on server. All three
are enforced by tests, not convention.

## Layout

```
src/SlayIdleRepeat.Core/          pure domain — no Godot, no I/O, deterministic
src/SlayIdleRepeat.Application/   use cases + port interfaces (Ports/Client|Server|Shared)
src/SlayIdleRepeat.Contracts/     wire/DTO contracts shared between client and server
src/SlayIdleRepeat.Client/        Godot client app (scenes, presenters, composition root)
src/SlayIdleRepeat.Server/        server host
src/adapters/{client,server,shared,fakes}/   one project per external integration
tests/                            xUnit + Shouldly + NetArchTest, mirrors src/ layout
docs/game-design.md               one-page summary of the design
docs/3d-resources.md              CC0 model, texture, HDRI and audio sources
game-data/                        tuning/content data, schemas, tuning experiments
.claude/                          the agent workflow (skills, commands, steering, retros)
```

## Core (`src/SlayIdleRepeat.Core/`)

Pure, deterministic game logic — must stay playable and testable without Godot loaded.

- `Commands/` + `Handlers/` — one command → one handler (e.g. `RollDice.cs`, `ResolveTile.cs`,
  `StartRun.cs`, `ChooseFork.cs`, `ShopBuy.cs`, `PickPerk.cs`), dispatched via `CommandDispatch.cs`.
- `Rules/` — game systems, subfoldered: `Board`, `Combat`, `Dice`, `Economy`, `Effects`, `Feats`,
  `Forge`, `Gear`, `Hero`, `Inventory`, `Luck`, `Perks`, `Stats`. Tile-kind resolution (Portal,
  Minigame, Shop, …) lives here, driven from `Handlers/ResolveTile.cs`. ⚠️ The Dice Forge is a
  placeholder that clears itself; its resolver went with the die's configurable faces.
  `Economy` is energy, currency math, shop pricing and run rewards; **merge, enhance and salvage
  are `Forge`**, not `Economy`.
- `Model/` — `Player`, `Run`, `Guild`, `Gear`, `Snapshots` (versioned state via `SchemaVersion`).
- `Events/` — domain events (`DiceRolled.cs`, `CurrencyChanged.cs`, …).
- `Rng/` — deterministic RNG (seeded, reproducible across client/server).

**Adding or changing a game mechanic → start here.**

## Application & Contracts

- `src/SlayIdleRepeat.Application/Ports/{Client,Server,Shared}` — interfaces the adapters
  implement (persistence, billing, ads, push, telemetry, content loading, …).
- `src/SlayIdleRepeat.Application/UseCases/` and `Services/` — orchestration above Core.
- `src/SlayIdleRepeat.Contracts/` — DTOs shared between client and server over the wire.

**Adding a new integration point (new port) or cross-cutting use case → here.**

## Adapters (`src/adapters/`)

One project per external system, split by `client/server/shared/fakes`: ads (AppLovin),
billing (GooglePlay/StoreKit), persistence (Postgres), cache (Redis), push
(Firebase/FcmApns), telemetry (Sentry/OpenTelemetry), content (LocalFile), and an
`Adapters.InMemory` fake set for tests.

**Wiring up a real third-party service (payments, ads, push, DB) → here.** Implement the
matching `Ports` interface; don't leak adapter-specific types into Core/Application.

## Client (`src/SlayIdleRepeat.Client/`)

The Godot game app: `Composition/` is the composition root that wires adapters into ports
at startup; `game/{net,presenters,scenes}` holds networking, presenters (UI-facing view
logic), and Godot scenes. `project.godot` is hand-written and stays that way — the
editor rewrites it on every save, so each setting carries the reason it exists next to
it. The Android export is *not* configured there; that lives in the export spikes.

**UI, scenes, presentation logic, client-side wiring → here.**

Every screen scene roots at `Node3D` and has the same two halves: a `World` holding that
screen's 3D content (its own `Camera3D`, and a backdrop plane on every screen but the
board), and a `Ui` `CanvasLayer` holding the whole interface as an overlay above it. The
board is the first screen whose camera MOVES, and it moves a rig rather than the camera:
`%Camera` stays a bare `Camera3D` with its scene-unique name, because that name is the
whole of `ScreenStage`'s handover contract. It is also the screen with no backdrop plane —
a plane at a fixed distance is left behind by a camera that travels, so what is behind the
board is `AppRoot`'s environment background. `AppRoot` owns the one thing there can
only be one of — the `WorldEnvironment` — plus the app-wide key light. Screens are siblings
under the root and are shown and hidden through `ScreenStage`, never through `Visible`:
engine visibility does not cross the `Node3D`/`CanvasLayer` seam, so hiding a screen is two
writes plus a camera claim, and every handover in `game/scenes/` goes through that one helper.

## Server (`src/SlayIdleRepeat.Server/`)

Server host process for multiplayer/guild/PvP features.

## Tests (`tests/`)

Six projects, mirroring `src/` — `Core.Tests`, `Application.Tests`, `Client.Tests`,
`Server.Tests`, `Contract.Tests` (the shared per-port suite run against every real
adapter implementation), and:

- `SlayIdleRepeat.Architecture.Tests` — NetArchTest/IL rules enforcing the dependency
  direction (Core must not depend on Godot/adapters); **this fails the build if the
  layering is violated**, so re-run it after moving code between layers. It is one file,
  `DependencyRuleTests.cs`, and it is deliberately narrow: it guards the project
  references and the port catalogue, nothing else. Every other structural rule in this
  document is upheld by review, not mechanically.

Mutation testing is scoped to Core and Application — one config per project,
`stryker-config.json` and `stryker-config.application.json` (`StrykerOutput/` for
results). Commands, including the diff-only run, are in [`../README.md`](../README.md).

## Game data (`game-data/`)

Schema-validated tuning/content data (not code) — `content/`, `schema/`, `tuning/`
(including `tuning/experiments/`), `loc/`, `assets/`. **Balance/tuning changes → here**,
validated against `schema/` by the content loader in
`src/SlayIdleRepeat.Application/Services/Content/`.

## Dev process (`.claude/`)

Features are built one at a time by an agent pipeline. Key references:

- `.claude/retros/STEERING.md` — binding rules distilled from past retrospectives;
  **read before implementing anything non-trivial**. The rule that matters most: a test
  that cannot fail is a defect, so every guard is proven to fail before it is trusted.
- `.claude/skills/` — the pipeline itself. `feature-oneshot` is the conductor and runs
  the rest end-to-end: `tdd-write-tests` → `tdd-implement` → `review-code-quality`,
  `review-architecture-quality`, `review-test-quality`, `review-ui-quality`,
  `review-ux-quality`, with `unit-testing` and `ui-design` as the shared conventions
  those phases are judged against.
- `.claude/commands/` — the slash commands that invoke them.
- `.claude/retros/M0.md`…`M5.md` — retrospectives from the milestone-based phase the
  project ran on earlier. Historical: the numbered milestones and their kickoff/handover
  docs are gone, and only the lessons in `STEERING.md` are still binding.
- `.claude/.feature-runs/<slug>/handover.md` — per-run state for a feature in flight
  (gitignored, along with `.claude/.milestone-runs/` and `.claude/worktrees/`).

## CI / build

`.github/workflows/ci.yml`; `Directory.Build.props` /
`Directory.Packages.props` for central package versioning; `global.json` pins the .NET SDK
to `net8.0` (kept in sync with what the Godot client can load); `docker-compose.yml` for
the local stack (Postgres, Redis, OTel/Jaeger/Prometheus/Grafana — no cloud creds needed).
Export spikes for platform packaging live under `spikes/godot-android-export/` and
`spikes/godot-ios-export/`.
