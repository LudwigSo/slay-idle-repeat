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
game-design.md                    one-page summary of the design
game-data/                        tuning/content data, schemas, tuning experiments
.claude/                          milestone-based agent workflow (skills, retros, steering, handovers)
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
logic), and Godot scenes. **Note:** `project.godot` here is currently an M0 placeholder
stub, not the real editor project yet (tracker notes it's replaced in M7).

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

Mirrors `src/` layout project-for-project (`SlayIdleRepeat.Core.Tests`,
`Application.Tests`, `Contract.Tests` for shared adapter-port contract suites) plus:

- `SlayIdleRepeat.Architecture.Tests` — NetArchTest rules enforcing the dependency
  direction (Core must not depend on Godot/adapters); **this fails the build if the
  layering is violated**, so re-run it after moving code between layers.
- `AssetManifest.Tests`, `AssetPipeline.Tests`, `AssetPlaceholders.Tests`,
  `AssetProvenance.Tests` — asset pipeline correctness.

Mutation testing is scoped to Core and Application — one config per project,
`stryker-config.json` and `stryker-config.application.json` (`StrykerOutput/` for
results). Commands, including the diff-only run, are in `README.md`.

## Game data (`game-data/`)

Schema-validated tuning/content data (not code) — `content/`, `schema/`, `tuning/`
(including `tuning/experiments/`), `loc/`, `assets/`. **Balance/tuning changes → here**,
validated against `schema/` by the content loader in
`src/SlayIdleRepeat.Application/Services/Content/`.

## Dev process (`.claude/`)

Work happened milestone-by-milestone (M0–M18). Key references:

- `.claude/retros/STEERING.md` — binding rules distilled from past milestone retros;
  **read before implementing anything non-trivial**.
- `.claude/retros/M0.md`…`M3.md` — per-milestone retrospectives.
- `.claude/skills/` — the agent pipeline (`feature-oneshot`, `kickoff-milestone`,
  `milestone-review`, `tdd-write-tests`, `tdd-implement`, `review-*`).
- `.claude/handovers/` and `.claude/.milestone-runs/M<N>/kickoff.md` — kickoff decisions
  and handover notes per milestone.

## CI / build

`.github/workflows/ci.yml`; `Directory.Build.props` /
`Directory.Packages.props` for central package versioning; `global.json` pins the .NET SDK
to `net8.0` (kept in sync with what the Godot client can load); `docker-compose.yml` for
the local stack (Postgres, Redis, OTel/Jaeger/Prometheus/Grafana — no cloud creds needed).
Export spikes for platform packaging live under `spikes/godot-android-export/` and
`spikes/godot-ios-export/`.
