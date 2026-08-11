# Slay. Idle. Repeat. — Implementation Tracker

This is the single tracking document for turning the design set in [`game-design/`](game-design/00_README_INDEX.md) into a shipped game. It follows the build order authored in `16_DECISION_LOG.md` Part D and the architecture fixed by docs `14`, `23` and `30`.

## How this document is used

- **One interactive kickoff phase per milestone.** Before a milestone starts, the open questions in its *Kickoff decisions* block are resolved with the product owner, and the task list is adjusted. After kickoff, agents implement the tasks autonomously (via the `feature-oneshot` pipeline) and update the status column here.
- **Statuses:** `⬜ todo` · `🔄 in progress` · `🔍 in review` · `✅ done` · `⛔ blocked (note why)`
- **Spec references** (`14 §8.1` etc.) point into `game-design/`. Where docs disagree, the authority chain ruled in `16` A7 applies — implement against the referenced authority doc, not a summarising one.
- Tasks are sized to be one autonomous feature-pipeline run each (spec → tests → implementation → reviews). If a task turns out too large, split it here first, then implement.
- Milestones are ordered, but the **workstreams marked ∥ can run in parallel** with the mainline (noted per milestone).

## Milestone snapshot

| # | Milestone | Build-order steps (16 Part D) | Status |
|---|---|---|---|
| M0 | Foundations, CI & week-1 spikes | pre-1, 2 (partial), spikes O14/O23 | 🔄 |
| M1 | Core domain skeleton & `InMemoryGame` | 1 | ⬜ |
| M2 | Effect DSL & combat simulation | 1 | ⬜ |
| M3 | Board, dice & the run loop | 1 | ⬜ |
| M4 | Meta systems in Core (LuckService, gear, talents, beasts, economy, FTUE) | 1, 8 (Core half) | ⬜ |
| M5 | Application layer, server backbone & inbox | 2, 3, 4 | ⬜ |
| M6 | Power model & economy simulator | 5 (∥ from end of M4) | ⬜ |
| M7 | Godot client vertical slice | 6 | ⬜ |
| M8 | Art & audio pipeline + Chapter 1 assets | 7 (∥ workstream) | ⬜ |
| M9 | Meta screens & **First Playable** (Ch 1–3) | 8 | ⬜ |
| M10 | Resource Dungeons | 9 | ⬜ |
| M11 | Content fill: chapters 4–8, full catalogues & asset batches | 10 | ⬜ |
| M12 | PvP — Ghost Duel | 11 | ⬜ |
| M13 | Live-ops & events framework | 12 | ⬜ |
| M14 | Guilds *(designated schedule-relief valve)* | 13 | ⬜ |
| M15 | Ads & Slay Plus | 14 | ⬜ |
| M16 | Live-service essentials (linking, Reserve polish, Feats, push) | 15 | ⬜ |
| M17 | Audio, localisation & accessibility completion | 16 | ⬜ |
| M18 | Hardening, joint economy tuning & soft launch | 17 | ⬜ |

**Hard gates on the way:**
- 🔒 **First Playable** = end of M9: chapters 1–3 playable online, excluding PvP, dungeons, live-ops, guilds, ads/subscription and live-service extras.
- 🔒 **D31:** the economy simulator must pass (16 named + 23 inherited assertions) before the live service opens (M18).
- 🔒 **E19 ordering rule:** dungeons + events + guilds + Reforge/Retune sinks are tuned **together** in the simulator (M18), never individually.

---

## Overarching tasks (cross-milestone)

These live across the whole project; they start in M0 and grow with every milestone. An agent finishing a feature must leave these green.

| ID | Task | Spec | Status |
|---|---|---|---|
| X-01 | **Determinism gate** — `Hash64` reference vectors, 4-dp rounding at every accumulation point, cross-platform `LogHash` CI (Linux x64 / Android ARM64 / iOS ARM64) on every commit | 14 §8, 05 §1.1 | ⬜ |
| X-02 | **Architecture test suite** — dependency rule, port purity, banned ambient APIs, `Apply`-only mutation, playable-from-Core-alone, internals, entitlement/guild isolation; fails the build | 23 §6, 30 §9 | ⬜ |
| X-03 | **Content schema validation + tuning discipline** — build fails on unknown IDs/orphans/duplicates; every 📐 number lives in `SlayIdleRepeat.Data/tuning/` (14 files), enforced by the marker-vs-schema build check; experiments as sparse override patches only | 14 §6, 21 §3 | ⬜ |
| X-04 | **Localisation discipline** — every user-facing string is a loc key from day one (`en` + `de`); DE ~30 % longer tested in tight widgets; nothing ships machine-translated | 13 §10, 19, 24 §9 | ⬜ |
| X-05 | **Telemetry event set** — server-emitted analytics events wired as each feature lands; derived metrics (perk pick rate, abandonment by tile, disconnect rate, season rating drift) | 14 §10.1 | ⬜ |
| X-06 | **Contract tests per port** — one shared suite per port, run against every implementation including the in-memory fake | 23 §5 A8 | ⬜ |
| X-07 | **No-lock-in CI** — vendor `PackageReference` uniqueness check; full stack boots via `docker compose` with no cloud credentials on every commit | 14 §1.1 | ⬜ |
| X-08 | **Balance harness green** — guardrail tests (par clear rate 62–78 %, no perk > +12 pp, boss duration bounds, mitigation cap, stat relevance) run nightly once combat exists | 05 §9, 17 §11 | ⬜ |

---

## M0 — Foundations, CI & week-1 spikes

**Goal:** the solution skeleton, the deterministic primitives, and the two schedule-risk spikes — before any feature work.
**Exit:** CI builds everything, boots the compose stack, runs the architecture tests; both spikes have written findings.

**Kickoff decisions** — ✅ resolved 2026-08-11 (record: `.claude/.milestone-runs/M0/kickoff.md`)
1. **O18 — bundle/package identifier: `de.ludwigso.slayidlerepeat`.** Reverse-DNS of a controlled domain. Subscription product ID unchanged (`slayidlerepeat.plus.monthly`). → fold into `00` §0a and `16` Part B as **O18 closed**.
2. **Repo/branching/versioning:** base branch renamed `master` → `main`. Branches: `milestone/M<N>` ← `feature-M<N>-<nn>-<slug>`, review on `review/M<N>`. **Three independent version numbers**, each hand-bumped with a written rule and a CI pin: assembly SemVer `0.x.y` in one `Directory.Build.props` (→ `1.0.0` at soft launch), wire `PROTOCOL_VERSION` (int, from 1, `14` §16.1), snapshot `SchemaVersion` (int, from 1, `14` §16.6).
3. **Spikes signed off to run first, in parallel with scaffolding** — dispatched in wave 1. Scope bounded by the available hardware: **O23's Android leg is executed for real locally**; the **iOS leg is a written CI recipe + findings** (no Mac / Apple Developer account), split out as M0-05b. **O14 is a source-and-docs verdict** on the MAX Godot plugin (no AppLovin account needed to read an MIT repo).
4. **Runtime pin: `net8.0` for every project**, so `Core` stays loadable by the Godot client. Godot version pinned by the O23 spike to the newest 4.x stable that passes the full custom-export path.
5. **CI: GitHub Actions**, authored now under `.github/workflows/`. ⚠️ **No git remote exists** — nothing can actually run until the user creates the repo and pushes, so M0-02's deliverable is *authored and locally validated*, not *observed green*.
6. **Local observability (M0-03):** OTel Collector + Jaeger + Prometheus + Grafana in compose. Sentry/PostHog adapters wired but pointed at a local sink — no cloud account, no multi-GB self-hosted stacks.

**Assumptions recorded after the interactive window**
- `tuning/` holds **16 files** per `21` §3.1 (the catalogue `14` §6 points at). `14` §6's "14 files" is stale prose — implement 21, and flag the fix in `14`.
- Unit-test stack: **xUnit + FluentAssertions + NetArchTest**, per the code shapes in `23` §6.
- Architecture tests (M0-08) are written in full now and pass vacuously against the skeleton where their subject types don't exist yet — never `[Skip]`, so they bite the moment M1 code lands.
- `SlayIdleRepeat.Client` is a **non-Godot placeholder** in M0 (`.csproj` + `project.godot` stub, editor never opened). Only M0-05a runs a real Godot toolchain, on a throwaway spike project.

| ID | Task | Spec | Status |
|---|---|---|---|
| M0-01 | Solution skeleton per the authoritative layout: `Core`, `Contracts`, `Application`, `adapters/{client,server,fakes}`, `Server`, `Client` (placeholder), `SlayIdleRepeat.Data`, `tools/{BalanceHarness,EconomySim}` (empty), `tests/` (5 projects) | 23 §3 | 🔍 merged · `feature-M0-01-solution-skeleton` — 33 projects, all net8.0, clean build, dependency rule wired |
| M0-02 | CI pipeline: build + test + content validation + server container image + compose boot; placeholder job for the Android custom-export-template build (real in M7) | 14 §14 | 🔄 `feature-M0-02-ci-pipeline` |
| M0-03 | Local dev stack: `docker compose up` brings API stub, Postgres, Redis, MinIO and observability containers up with no cloud account | 14 §1.1 | ⬜ |
| M0-04 | **Spike O14:** AppLovin MAX Godot plugin — verify S2S rewarded callbacks and `setUserId` exist; write up the fallback (patch the MIT plugin vs signed-nonce client assertion) | 12 §3.3 | 🔍 merged · `feature-M0-04-spike-o14-applovin` → `docs/spikes/O14-applovin-max-s2s.md`. **Verdict: proceed as specified, no plugin patch** — `setUserId` is absent everywhere but `custom_data` is a better per-impression attribution channel. ⚠️ Two findings for M15: the no-fill grant is client-asserted *by construction*, and the plugin looks unmaintained (see kickoff record §6) |
| M0-05a | **Spike O23 (Android, executed):** Godot 4 C#/.NET Android export through the full custom export-template path (Gradle/Java 17), run for real locally; findings + the Godot version pin | 14 §1, 12 §3.2 | 🔄 `feature-M0-05a-spike-o23-android` |
| M0-05b | **Spike O23 (iOS, on paper):** the CocoaPods/Xcode export path as a written CI recipe + risk findings; macOS runner job authored but not executed (no Mac/Apple Developer account) | 14 §1, 12 §3.2 | ⬜ |
| M0-06 | `Core/Rng`: `Hash64` (xxHash64, pinned encoding) + committed reference-vector table; `DeterministicRng` counter-based streams (`WeightedPick` = one draw) | 14 §8.0–8.1 | ⬜ |
| M0-07 | `CanonicalStateWriter` — the single FNV-1a serialiser for `stateHash` / `LogHash` / parity hashes, with the field-order/SchemaVersion CI pin | 14 §16.6 | ⬜ |
| M0-08 | Seed `SlayIdleRepeat.Architecture.Tests` with the dependency rules + banned-API greps (`System.Random`, `DateTime.Now`, `Guid.NewGuid`, …) | 23 §6 | 🔍 merged · `feature-M0-08-architecture-tests` — **33 live rules, 0 skipped**, green on the integration branch. Six red-then-green demonstrations prove the suite bites, incl. a vacuous rule converting to a live one when a GameCommand hierarchy is added |
| M0-09 | Content pipeline base: immutable version-stamped `ContentSnapshot`, schema validation harness, dev hot-reload; 📐-marker-vs-schema build check | 14 §6, 30 §3 | ⬜ |
| M0-10 | `SlayIdleRepeat.Data` initial layout: `schema/`, `tuning/` (**the 16 files catalogued in `21` §3.1**, incl. `sim_thresholds.json` — `14` §6's "14 files" is stale prose and defers to 21), `loc/en.json` + `de.json`, `content/` | 21 §3.1 | 🔄 `feature-M0-10-data-layout` |

---

## M1 — Core domain skeleton & `InMemoryGame`

**Goal:** `GameRules.Apply` exists, the aggregates exist, and a full (rules-light) game session runs in memory from `Core` alone.
**Exit:** `InMemoryGame` drives a multi-day player through commands with the day cycle, energy and currencies working; all ten Core architecture tests green.

**Kickoff decisions**
1. Confirm the canonical command registry (19 run + 29 meta commands) is frozen as the vocabulary — additions afterwards are logged decisions in `16`.
2. Confirm snapshot `SchemaVersion` / migration policy from day one.

| ID | Task | Spec | Status |
|---|---|---|---|
| M1-01 | `Primitives/`: ids, `Result<T>`, value objects, the domain-tier `RejectionReason` values | 30 §11.4, 14 §16.2 | ⬜ |
| M1-02 | Public `GameCommand` hierarchy — all 48 commands (19 run, 29 meta) as the one wire+domain vocabulary | 14 §2.3 | ⬜ |
| M1-03 | Public `DomainEvent` hierarchy + the `CurrencyChanged`-with-reason invariant (IL-scan test) | 30 §7 | ⬜ |
| M1-04 | `Player` aggregate: profile, 8 currencies, inventory & unopened-container shelf, pity-counter storage, daily/weekly counters, FTUE progress, lifetime feat counters; public getters / internal ctors; `PlayerSnapshot` + `Rehydrate` | 30 §4, §11 | ⬜ |
| M1-05 | `Run` aggregate as child of `Player`: board, position, HP, run gold, perks, consumables, pending fork, RNG stream counters, per-run ad uses, curses; `RunSnapshot` | 30 §4 | ⬜ |
| M1-06 | `GameRules.Apply` façade: dispatch table, `CommandResult`, `WorldSlice`, total/pure/synchronous/immutable properties | 30 §2 | ⬜ |
| M1-07 | `GameContext` record (NowUtc, CommandSeed, ContentSnapshot, Entitlements, Flags); entitlement readable only for ad-grant caps (architecture test) | 30 §3 | ⬜ |
| M1-08 | `AdvanceTime` lazy catch-up: energy regen accrual, 05:00 UTC daily resets, weekly boundaries, Plus expiry, event-window state — first step of every handler | 30 §2.3 | ⬜ |
| M1-09 | `BEGIN_SESSION` handler: calendar advance, daily free refill, quest-slate draw, Daily-shop block draw — all from the command seed; idempotent per game day | 30 §2.3, 19 B/G | ⬜ |
| M1-10 | Energy + Energy Reserve math: max/regen/costs, overflow routing, automatic spend order | 10 §3, 28 C | ⬜ |
| M1-11 | `InMemoryGame` + `VirtualClock` harness; perf target: 180-day player < 200 ms | 30 §6 | ⬜ |
| M1-12 | The ten Core architecture tests (synchronous, no ports, no clock, every command handled, currency events, playable-from-Core-alone, Apply-only mutation, internal handlers, internal layering, `InternalsVisibleTo` pin) | 30 §9 | ⬜ |

---

## M2 — Effect DSL & combat simulation

**Goal:** the one interpreter every game effect compiles into, and the deterministic auto-battle core — "the most important document for the implementer" (05).
**Exit:** all 43 ops / 23 triggers / 23 conditions unit-tested; a full fight simulates in < 5 ms; all 8 bosses run from DSL data with zero bespoke code; DSL parity test (10 000 random builds) green.

**Kickoff decisions**
1. Confirm the four combat rulings recorded in `17`: `targetPriority` field (Sporequeen), the Rimehold damage-amp state flag, the Dicelord phase-2 outcome table, enrage expressed as `STAT_MULT ×1.08`/s (05 form is authoritative).
2. **O13 (17 §8):** verify/decide how Sporequeen's phase-3 drain interacts with the 70 s enrage soft-timer.
3. `CP_GLASS_HEART` stays at ×2 until the harness shows > 12 pp clear-rate swing (pre-agree the downgrade to ×1.6 as the automatic response).

| ID | Task | Spec | Status |
|---|---|---|---|
| M2-01 | `EffectDefinition` record + JSON schema (op·trigger·condition·target·value·valueScale·duration·stacking + extension fields) | 18 §1 | ⬜ |
| M2-02 | `EffectResolver` implementing the exact 10-step resolution order (collect → … → caps → 4-dp round) | 18 §8 | ⬜ |
| M2-03 | All 43 ops in 5 families, each with unit tests | 18 §2 | ⬜ |
| M2-04 | All 23 triggers wired into combat + run loops, incl. `everyNth` counter semantics | 18 §4 | ⬜ |
| M2-05 | All 23 condition functions + comparators/combinators; 11 targets with context-degradation rules | 18 §5–6 | ⬜ |
| M2-06 | Duration scopes (6) + early terminators + 5 stacking modes; `valueScale`/`valueMode` evaluators | 18 §3, §7 | ⬜ |
| M2-07 | 14-stat actor block, aggregation order, caps from `data/combat_caps.json`, hero base stat curve | 05 §1–2 | ⬜ |
| M2-08 | Fixed-tick engine: 20 ticks/s, pre-tick sequence, strict 8-step tick loop, initiative, targeting (`targetPriority` + lowest-HP tie-break), timeout rule | 05 §3 | ⬜ |
| M2-09 | `ResolveAttack` 10-step pipeline; ward pool (cap, absorption order, bypass list); `Heal()` + overheal; `ReflectDamage`; `AttackMultiplier` transient | 05 §4 | ⬜ |
| M2-10 | 12 status effects + the DoT/HoT cadence engine (anchoring, stacking, mitigation exemptions) | 05 §5 | ⬜ |
| M2-11 | Enemy stat derivation + `EnemyLevel(c,t)`; 8 archetypes with coefficient rows; WARDEN/CASTER on-hit tables; elite system (×2.2, 8 modifiers, no-repeat rule); `data/enemies.json` with 16 elite identities | 05 §6 | ⬜ |
| M2-12 | Boss engine: 3 phases at 100/66/33 %, `SYS_ENRAGE`, telegraph events, first-clear phase-1 extension, summon entry rule, damage-amp state flag, phase-change log events | 17 §1 | ⬜ |
| M2-13 | All 8 boss scripts + `BOSS_FTUE` authored as DSL data in `data/bosses.json` (coefficient rows + mechanics) | 17 §2–9 | ⬜ |
| M2-14 | PvP duel mode in the same code path: `IS_PVP` semantics, attacker-first initiative, no `ON_KILL`, 60 s cap, tie rules | 05 §3.3, 11 §6 | ⬜ |
| M2-15 | Combat log format (`CombatEvent`, `SimulationResult`) + `LogHash`; compute-then-animate contract; `RunEffectQueued` combat→run bridge | 05 §7, 18 §2.5 | ⬜ |
| M2-16 | Balance harness v1: 10 000 fights per (chapter, tier, archetype); the 6 guardrail tests | 05 §9 | ⬜ |
| M2-17 | Client/server DSL parity test over 10 000 random build permutations | 18 §11 | ⬜ |

---

## M3 — Board, dice & the run loop

**Goal:** a complete run — roll, move, resolve, fight, draft, die or win — playable through `InMemoryGame`.
**Exit:** full Chapter-1 run completes headless through commands only; board generator satisfies C1–C7 across seeds; FTUE authored board validates.

**Kickoff decisions**
1. **O35** — redesign `PK_DICELORD_GIFT` (current row is void under free fork choice; do not build against it).
2. **O21** — `PK_SINGULARITY` needs a mid-run perk-removal choice flow: specify or cut.
3. **O22** — `PK_CARTOGRAPHER` teleport vs traversal rules: movement ruling.
4. Dice Forge offer pool (faces/tiers, interaction with upgraded faces) — no table exists.
5. **O13 (decision log)** — curse chapter gating (suggested split in `19` E).

| ID | Task | Spec | Status |
|---|---|---|---|
| M3-01 | Board graph model (DAG, 3 stages, forks) + `GenerateBoard(chapter, tier, seed)` + constraint solver C1–C7 with redraw/injection fallbacks | 03 §1–3 | ⬜ |
| M3-02 | Movement engine: virtual trailhead, stepwise traversal, junction pause + `CHOOSE_FORK`, stage clamp, boss-exact rule, chain hops (cap 3→5), portal draws with campfire clamp | 03 §1.1 | ⬜ |
| M3-03 | The 14 tile resolvers + linear node index feeding `EnemyPower(i)` | 03 §2, 02 §4.3 | ⬜ |
| M3-04 | Dice system: `DieFace`/`DieFaceKind` + tiers, face effect resolvers (Pip/Star/Surge/Fortune/Void/Chain), run-start die composition pipeline, Fair-Dice weighted bag (server-side, stage-gate reset), reroll charge economy + Nudge | 04 | ⬜ |
| M3-05 | Run state machine (RUN_SETUP → … → RUN_RESULTS), stage gates (heal, refresh, rarity shift, power step, checkpoint, interstitial hook), timing/tunables | 02 §1–3 | ⬜ |
| M3-06 | Perk draft: post-battle trigger, 3 options, rarity weight tables by stage/elite/boss, tier upgrades (I/II/III + removal at III), composition rules, skip/reroll economy (LuckService hooks stubbed until M4-01) | 06 §1–2, §4 | ⬜ |
| M3-07 | The 98-perk catalogue (90 + 8 cursed) authored as DSL data — zero per-perk code | 06 §3, 18 | ⬜ |
| M3-08 | Shop tile (4 slots, refresh economy), pricing engine (`chapterPriceScalar`), run buffs, consumables incl. Escape Rope arming/skip semantics + `USE_CONSUMABLE` legality | 03 §7 | ⬜ |
| M3-09 | Event-card system (schema, weighted outcomes, costs/requirements) + the 30 authored cards in 3 chapter bands | 03 §5, 19 A | ⬜ |
| M3-10 | 4 minigames + reward tables + the server-authority split (2 server-rolled, 2 client-asserted legality-validated) | 03 §6 | ⬜ |
| M3-11 | Shrine (pool + cleanse rule), campfire, dice forge, curse tiles; the 12-curse catalogue + curse rules engine (no stacking, paired rewards, `AD_SKIP_CURSE` hook, mount immunity) | 03 §7a, 19 E | ⬜ |
| M3-12 | Chapter signatures: Ch3 revive, Ch4 burning tiles, Ch6 clockwork pressure, Ch8 die scramble | 03 §4 | ⬜ |
| M3-13 | Reward banking, in-run income tables, run-end payout (completion multipliers, ad-double), first-clear bonuses, death/revive (battle restarts; works on bosses), Legend XP income | 02 §5–6 | ⬜ |
| M3-14 | 8 chapter data files (weights, pools, targets, unlock conditions) + treasure/cache payout profiles | 14 §6, 03 §7a | ⬜ |
| M3-15 | `runSeed` derivation inside `Apply` on `START_RUN`; lifetime `runCounter`; stream-counter echo in every outcome | 02 §2, 14 §8.1 | ⬜ |

---

## M4 — Meta systems in Core

**Goal:** everything between runs — loot, forge, talents, beasts, wallet, dailies, FTUE — as pure Core rules. 🔒 `LuckService` lands **here**, before any later grant path exists.
**Exit:** a simulated 30-day player earns/spends/merges/drafts entirely in memory; every grant path routes through `LuckService` (architecture test).

**Kickoff decisions**
1. Rarity-floor renormalisation semantics per source (📐, flagged under-specified in 24 §4.0a).
2. **O36** — name the FTUE beat-7 mini-boss (default: Thornmaw phase 1).
3. Confirm draft-protection rule set for the `DRAFT` source class (doc count mismatch: "five rules" vs six listed — 06 §4 vs 24 §4.7).
4. Hoard-lever telemetry thresholds (shelf dwell, opens-after-Focus-change) — initial values.

| ID | Task | Spec | Status |
|---|---|---|---|
| M4-01 | **`LuckService`** — the single guarantee point: 3 primitives (hard pity, soft pity, mercy accrual), source-class registry (`data/luck.json`, 10 classes), counter rules (server-owned, visible, never reset), routing architecture test + 100 000-seed property tests | 24 §1–3, §11 | ⬜ |
| M4-02 | Container shelf + `OPEN_CHEST/EGG/CRATE` (+ OPEN ALL): contents/pity/Focus read at open from command seed; chest ladders (Standard 10/40/160, Premium 5/25, Apex 3), soft-pity slopes, egg P1–P3, crate protection, `DROP_RUN` D1–D3 | 24 §4 | ⬜ |
| M4-03 | Gear instance schema (`quality`, `chapterOrigin`, mercy counter, affixes, lock) + generation: `ItemPower`, slot coefficients, 14 affixes, chapter-banded drop shares, 4 SS set bonus engines; derived stats never stored | 08 §2–3 | ⬜ |
| M4-04 | Forge: merge (dust substitution, max-q/max-origin, crown costs), enhance (+0→+15, mercy inheritance, ad/Plus luck stacking), salvage (+Set Tokens), auto-salvage filters; Reforge (keep-best q) + Retune (locks + wishlist); Focus (×2.5, 12 h cooldown); Set Token redemption | 08 §4, 24 §5–6 | ⬜ |
| M4-05 | Inventory: capacity + expansion curve, sorting/compare/lock model, hold-not-lose on overflow | 08 §5 | ⬜ |
| M4-06 | Talent tree: 60-node catalogue as DSL data, tier gating, rank costs, spend/respec (free, instant), presets (3 free / Plus unlimited), die-face rewrites | 09 | ⬜ |
| M4-07 | Pets: 24 defs, levelling (dual cost), ascension ★1–5, aura aggregation, active-ability runtime, duplicates → Beast Marks, exchange tiers; `PET_DICEBEAST` + `PET_ARCHIVIST` special cases | 07 §2 | ⬜ |
| M4-08 | Mounts: 12 defs, levelling (Feed only), run-perk effect layer (board-side), crate odds, duplicate disposal | 07 §3 | ⬜ |
| M4-09 | Wallet + shop model (Daily draw rule + staples, Materials caps, Honor, Plus tab stub), daily quests (20-pool, draw + reroll rules, rewards, 3-of-3 chest), Lucky Wheel (`SPIN_WHEEL`, segment weights, W1/W2 pity), 28-day login calendar (`CLAIM_CALENDAR`, pause-not-skip) | 10 §1–5, 19 B/F/G | ⬜ |
| M4-10 | Hero: name entry + profanity filter, Legend Level curve + unlock-gate table, level-up grants, loadout snapshot-at-run-start, presets (`SAVE_PRESET`/`APPLY_PRESET`) | 07 §1, §4 | ⬜ |
| M4-11 | Codex model: 5 sections, per-entry stat bonuses (active at L100), TP milestones | 06 §6 | ⬜ |
| M4-12 | FTUE data package `ftue.json`: authored board, rigged die stream, tutorial-only defs and flags (7), fixed drafts/shop, scripted payout, per-beat resume, `SKIP_FTUE`; structural validator + T1–T5 harness acceptance tests | 19 D | ⬜ |
| M4-13 | Feat lifetime counters incremented inside `Apply` from now on (infrastructure only — Feats feature ships M16 but retroactivity requires counters to exist early) | 28 D, 30 §12.7 | ⬜ |
| M4-14 | Cross-system stacking rules: die-face rewrite resolution order (talents/mounts/set bonus/pet), reroll-charge stacking (5 sources), pet-cooldown reduction stacking (4 sources) | 07/08/09 | ⬜ |

---

## M5 — Application layer, server backbone & inbox

**Goal:** the ports, the ASP.NET Core host, persistence, auth and the wire contract — the game becomes a service. 🔒 The **inbox is built here** (step 4), not with the other live-service features.
**Exit:** a device plays a full run over HTTPS against the compose stack; reconnect chaos test passes; cross-platform determinism CI live.

**Kickoff decisions**
1. **O5** — push transport (recommendation: FCM + APNs directly). Decision needed before the port is shaped.
2. **O33** — simultaneous sessions on two devices (expected: newest wins) — confirm.
3. **O34** — player display-name lifecycle (uniqueness, rename, sanction path).
4. **O6** — managed vs self-hosted Postgres can stay open (deploy-time), confirm.

| ID | Task | Spec | Status |
|---|---|---|---|
| M5-01 | Full port catalogue in `Application/Ports/{Client,Server,Shared}` + in-memory fake for every port + shared contract-test suites | 23 §4–5 | ⬜ |
| M5-02 | Use-case orchestration: load slice → `Apply` → persist → dispatch events; no game rules in Application | 30 §11.1 | ⬜ |
| M5-03 | Command endpoints (`/run/{id}/command`, `/player/command`) with the normative envelope: protocol version + skew rule, rejection contract (200-rejections, full enum, HTTP mapping), sequencing + idempotency scopes, retry rules | 14 §16.1–16.3 | ⬜ |
| M5-04 | The commit rule: one accepted command = one Postgres transaction (snapshots + idempotency outcome + domain events); Redis strictly rebuildable cache | 14 §16.4 | ⬜ |
| M5-05 | Postgres adapter + schema + migrations (profiles JSONB/typed split, run snapshots, idempotency, economy event log, messages); Redis run-state/session/idempotency hot-cache; S3 battle-log store | 14 §7 | ⬜ |
| M5-06 | Auth: anonymous device accounts (keystore-held secret), our JWTs + rotating refresh, silent renewal, WebSocket upgrade auth, GDPR deletion endpoint | 14 §16.5, §7.3 | ⬜ |
| M5-07 | Query surface: `GET /run/{id}/state?sinceSequence=N` (write-model read) + read-model scaffolding with declared staleness budgets | 30 §12 | ⬜ |
| M5-08 | **Inbox (28 Part A):** message store + `IMessageRepository`, 6 categories, templated EN+DE messages, attachment grant engine (idempotent, overflow/hold rules), `CLAIM_INBOX`, nightly expiry auto-grant job, targeting + segment dry-run tooling + audit log | 28 A | ⬜ |
| M5-09 | Content distribution: endpoint, per-run/session version pinning, client hash check + download flow, retention (**O25** hardening) | 14 §6, 16 O25 | ⬜ |
| M5-10 | Remote config endpoint + feature flags resolved into `GameContext.Flags`; kill switches for PvP, placements, Plus offer, chapters | 14 §10, §14 | ⬜ |
| M5-11 | Observability: Serilog structured logs, OpenTelemetry metrics/traces, Sentry, PostHog server-side event sink | 14 §10 | ⬜ |
| M5-12 | Determinism CI cross-platform `LogHash` test (10 000 triples on x64/ARM64×2) + client/server parity test (1 000 command sequences) | 14 §8.2, §13 | ⬜ |
| M5-13 | Reconnect chaos test: kill the connection at every command boundary of a full run; no lost or duplicated outcomes | 14 §13 | ⬜ |
| M5-14 | Rate limiting (per-player, per-IP), plausibility-monitoring job skeleton + review queue + sanctions data model | 14 §9 | ⬜ |

---

## M6 — Power model & economy simulator ∥

**Goal:** the product owner's dial (`29`) and the tool that grades the game against it (`21`). Starts the moment `InMemoryGame` + meta systems exist (end of M4) — does **not** wait for M5.
**Exit:** `EconomySim assert` runs in CI on every `Data` change; first full 180-day × 14-profile run produced (expected to fail — that is its job); frontier curve derived.

**Kickoff decisions**
1. **O1** — intent for the frontier-bonus/catch-up curve (the simulator's first derivation target; −60 %/+50 % are placeholders).
2. Product owner reviews and commits `expected_progression.json` **before** the first run (changes to it are its own commits — R12).
3. Confirm tolerance ladders and which profiles use the Wide band.

| ID | Task | Spec | Status |
|---|---|---|---|
| M6-01 | `PowerCalculator` (public): geometric form + `additive_legacy` data switch, reference opponent, `EffectiveHP`/`DPS` terms, cap ordering, `PetDpsShare` + non-damage ability weights, mount handling | 29 §2 | ⬜ |
| M6-02 | `K_POWER` calibration + `EmpiricalPower` measurement harness (two passes, standard dummy) + assertion A10 (±12 %) | 29 §2.5 | ⬜ |
| M6-03 | `calibration_builds.json`: par build, dummy, 5 archetypes with frozen perk sets + draft priorities; archetype power-scaling rule (bisection) | 29 §2.5 | ⬜ |
| M6-04 | `par_power.json` (24 authored cells + dungeon ×1.15 / guild-boss ×1.30 pars) + `ExpectedPower(L)` factor spine + `expected_progression.json` (14 profiles × 8 checkpoints, tolerance ladders); consumers wired (chapter-select warning, tier recommendation) | 29 §4–6 | ⬜ |
| M6-05 | `UtilityIndex` (internal-only second scalar) | 29 §3.1 | ⬜ |
| M6-06 | `tools/EconomySim`: CLI (`run`/`sweep`/`compare`/`assert`/`baseline`, `--fast`), `SimulatedPlayer`/`DayLoop`/`RunModel`, 3 decision policies, override layering, references `SlayIdleRepeat.Core` **only** | 21 §2–7 | ⬜ |
| M6-07 | 14 behavioural profiles + per-placement `AdBehaviour` + `FeatureEngagement` tables as data entry (`sim_profiles.json`) | 21 §5 | ⬜ |
| M6-08 | Report artifacts (10 files: expectation, decomposition, timeline, milestones, bottlenecks, fairness, luck, income attribution, sensitivity, summary) with p10/p50/p90 | 21 §8 | ⬜ |
| M6-09 | Assertions A1–A16 + inherited E1–E23 wired as the CI gate on `SlayIdleRepeat.Data` changes; perf budget 180 d × 14 × 200 seeds < 4 min | 21 §9 | ⬜ |
| M6-10 | First calibration campaign: derive `K_POWER`, run the first full pass, derive the **O1** frontier curve, log the first sweep targets (XP exponent, dungeon/event/guild income, merge costs, Soul-Shard flow, factor curves) | 21 §12 | ⬜ |

---

## M7 — Godot client vertical slice

**Goal:** the game on a phone — one chapter, online, reconnectable, with placeholder art where M8 hasn't delivered yet.
**Exit:** full Chapter-1 run on an Android device against the compose stack: roll → move → fight (predicted locally from `battleSeed`) → draft → results; mid-run reconnect works; CI builds the APK through the custom export path.

**Kickoff decisions**
1. **O15** — confirm hand-rolled composition root + presenter pattern (recommendation in 23 §9) vs a DI container.
2. Placeholder-asset policy until M8 delivers (grey-box vs anchor-sheet drafts).

| ID | Task | Spec | Status |
|---|---|---|---|
| M7-01 | Godot project + composition root (platform-/entitlement-conditional wiring), `Platform.Godot` adapter (audio, haptics, locale, device info); scenes as driving adapters, presenters as plain C# | 14 §5, 23 §7.2 | ⬜ |
| M7-02 | `Api.Http` adapter + `StateMirror` + `CommandQueue` + `ReconnectManager`; the 5 connection states exactly as specified (pill, read-only offline, resync flash, resume card; never a blocking error mid-run) | 14 §3, 13 §11 | ⬜ |
| M7-03 | Boot S01: splash, auth, session, profile fetch, content-hash check + download; < 4 s cold-start budget | 02 §7, 13 | ⬜ |
| M7-04 | Home S03 (minimal: header, energy, CONTINUE) + Chapter Select S04 with par-power soft warning | 13 §3 | ⬜ |
| M7-05 | Board S05: tile track, roll button, reroll prompt (4 s ring), fork choice, consumable pouch, curse icons; Die Panel S12 | 13 §3, 04 §6 | ⬜ |
| M7-06 | Battle replay renderer S06: animates the pre-computed log; local prediction from server-issued `battleSeed`; speed ×1/×2/×3 + always-available skip; boss phase band | 05 §8, 14 §2.4 | ⬜ |
| M7-07 | Run decision screens: draft S07 (never auto-picked), shop S08, event S09, 4 minigames S10, campfire/shrine S11 | 13 §4 | ⬜ |
| M7-08 | Death/revive S13 + run results S14 (reward tally, mercy counters, session-floor line) | 13 §4 | ⬜ |
| M7-09 | `Cache.LocalFile` adapter: read-only mirror for cold start + offline browsing | 14 §7.2 | ⬜ |
| M7-10 | Client CI: Android debug APK through the custom export template (with the MAX plugin present but stubbed), iOS export smoke build | 14 §14 | 🔄 `feature-M0-02-ci-pipeline` |

---

## M8 — Art & audio pipeline + Chapter 1 assets ∥

**Goal:** the generation pipeline proven end-to-end and the assets that unblock screens and the vertical slice. Runs as a parallel workstream from M5 onward.
**Exit:** anchor sheet locked; UI kit + Ch1 biome + core SFX shipped through the full post-processing/QA pipeline.

**Kickoff decisions**
1. ⚠️ Confirm **commercial licence terms in writing** for Midjourney and every audio tool before generating anything; provenance record format.
2. **O7** — layered gear rigging spike result: shared-skeleton overlays vs the 20-composited-looks fallback.
3. **O8** — VFX method: procedural in-engine (recommended) vs generated sheets.
4. UI clicks: AI vs CC0 pack for the ~20 weakest sounds.

| ID | Task | Spec | Status |
|---|---|---|---|
| M8-01 | Licence confirmations + provenance tooling (job ID, prompt, seed, sref, date per asset) | 15 §B0, 20 §2.1 | ⬜ |
| M8-02 | **Style Anchor Sheet** (6 characters in one image) + locked seed family — gates all other art | 15 §B2 | ⬜ |
| M8-03 | UI kit E17 (12 panels, 18 buttons, frames, bars, tabs, card backs — square corners) — unblocks all screen implementation | 15 §E17 | ⬜ |
| M8-04 | Hero body poses + one full gear family at all 5 rarities — validates rigging (O7) and the rarity language | 15 §E2 | ⬜ |
| M8-05 | Chapter-1 batch: 14 tile icons, board pieces, Ch1 enemies (8×2), Thornmaw (4 poses), Greenwood backdrop layers | 15 Part H step 4 | ⬜ |
| M8-06 | 7-step post-processing pipeline tooling (bg removal → trim → quantise → outline repair → resize → export → atlas) + the 11-item QA checklist + silhouette gate | 15 §C–D, F | ⬜ |
| M8-07 | Audio: `mus_home` (defines the palette), core combat SFX, dice SFX, UI SFX; bus structure, ducking (incl. mandatory full duck around ads), polyphony caps | 20 §2, §4–5 | ⬜ |
| M8-08 | Currency, status and misc icon sets | 15 §E14–E15, E20 | ⬜ |

---

## M9 — Meta screens & **First Playable**

**Goal:** the complete out-of-run game on device; chapters 1–3; the FTUE. 🔒 End of this milestone is the **First Playable** gate.
**Exit:** a new player installs, plays the FTUE, progresses through chapters 1–3 across sessions with gear/talents/pets/quests — no PvP, dungeons, live-ops, guilds, ads or live-service extras.

**Kickoff decisions**
1. Review first-playable scope line-by-line (16 Part D step 8) — confirm nothing extra creeps in.
2. **O11** — preset slot count (3 free is a guess; raise, never gate further) — confirm for launch.

| ID | Task | Spec | Status |
|---|---|---|---|
| M9-01 | Hero S15, Inventory S16 (grid, filters, compare, lock, auto-salvage rules, Focus selector, container shelf with live counters), Forge S17 (Merge/Enhance/Salvage/Reforge/Retune tabs, Set Token counter, merge celebration) | 13 §5 | ⬜ |
| M9-02 | Talents S18 (3 branch tabs, rank deltas in real numbers, respec, preview) + Menagerie S19 (grids, detail views, Beast Mark exchange, egg/crate shelf) | 13 §6 | ⬜ |
| M9-03 | Shop S23 (4 tabs; every chest listing shows class + counter) + Dailies S25 (quests, 28-day calendar, Lucky Wheel spin presentation) + Codex S24 | 13 §7 | ⬜ |
| M9-04 | Settings S26 (audio, accessibility, account, privacy, **Odds & Guarantees page**) + Profile S27 | 13 §8 | ⬜ |
| M9-05 | FTUE playable end-to-end on device: 11 beats, per-beat resume, skip flow, scripted payout; T1–T5 harness assertions green | 19 D | ⬜ |
| M9-06 | Chapters 1–3 fully tuned first pass (harness clear-rates in band) + chapters 2–3 art batches | 17, 15 | ⬜ |
| M9-07 | Accessibility core set: reduced motion, text sizes with reflow, left-handed mode, colourblind palettes + shape-coded rarity | 13 §8 | ⬜ |
| M9-08 | **First Playable review**: full-loop playtest, perf pass on target devices (60 FPS / < 400 MB), punch-list | 16 Part D | ⬜ |

---

## M10 — Resource Dungeons

**Goal:** the three daily deterministic material dungeons. **O24 is resolved first** — the fixed-payout board must be reachable under the movement rules before anything else is built.

**Kickoff decisions**
1. **O24** — resolve the fixed-payout-vs-movement contradiction (node count, dice interaction, guaranteed full traversal).
2. Confirm entry economics (3/day, 10 Energy, ad +3, Plus 12) as initial values.

| ID | Task | Spec | Status |
|---|---|---|---|
| M10-01 | O24 resolution + dungeon board profile in `GenerateBoard` (8+1 nodes, fixed composition, shuffle constraint) + `TILE_CACHE_DUNGEON` + Guardian encounter (2.6× tier power) | 25 §3 | ⬜ |
| M10-02 | 3 dungeon definitions + 8-tier ladder (power/yield formulas, per-node payout split, partial-clear payout, 0.20× Legend XP), reward-exclusion + zero-pity enforcement, entry caps + ad/Plus grants, revive suppression | 25 §4–6 | ⬜ |
| M10-03 | S28 Dungeon Select + S29 Dungeon Board (S05 reskin) + entry points (Chapter Select 4th item, Home badge); palettes + Guardian shader + card art + SFX | 25 §8–9 | ⬜ |
| M10-04 | Simulator E6–E10 + ads-catalogue update (placement #29, caps 36/44) | 25 §7, 12 §4 | ⬜ |

---

## M11 — Content fill: chapters 4–8, full catalogues & assets

**Goal:** all remaining game content. **O30 is resolved first** — reconcile the art/audio manifests with docs 24–28 + A7 before batch generation.

**Kickoff decisions**
1. **O30** — manifest reconciliation (stale 975/106 totals, screen count).
2. Confirm biome batch order and which showcase pieces are hand-driven.

| ID | Task | Spec | Status |
|---|---|---|---|
| M11-01 | O30: reconcile asset manifests against the final feature set; regenerate counts | 15, 20 | ⬜ |
| M11-02 | Chapters 4–8: data files, signature mechanics live, elites + bosses tuned through the harness (per-boss clear-rate/duration bands; Cindermaw and Cogitator special checks) | 03 §4, 17 | ⬜ |
| M11-03 | Biome art batches 2–8, batched by biome (enemies → elites → boss → board → backdrop per session) | 15 Part H | ⬜ |
| M11-04 | Gear icons (120, one session), perk + talent icons (158, **after** the `22` symbol tables; neutral-disc + programmatic recolour; 48 px silhouette grid), pets + mounts art, dice faces, VFX per O8 decision | 15, 22 | ⬜ |
| M11-05 | Remaining content data entry: full gear/pet/mount catalogues, event weights first-pass, curse gating per M3 kickoff ruling; Codex completeness check | 07, 08, 19 | ⬜ |
| M11-06 | Biome music tracks (one session) + remaining SFX families; `mus_boss_final`, `mus_arena` | 20 §7 | ⬜ |

---

## M12 — PvP: Ghost Duel

**Goal:** the one PvP mode, its ladder and its economy — entirely on the shared combat core.

**Kickoff decisions**
1. **O31** — replay-history surface: which screen owns the last-50-runs/duels list (a locked free feature with no home).
2. **O34** — display-name lifecycle final (if not settled in M5).
3. Confirm matchmaking constants (±150 band, widen step, bot rating-delta halving) as initial 📐 values.

| ID | Task | Spec | Status |
|---|---|---|---|
| M12-01 | Ghost snapshot generation (server-side, resolved stats, checksum, versioning; regenerate on loadout change + daily) + static-row storage + `UPLOAD_GHOST` | 11 §2 | ⬜ |
| M12-02 | PvP loadout: separate talent/gear/beast presets + perk budget system (68 eligible, 5 slots, 10 points, Tier II locked) + `IS_PVP` affix neutralisation | 11 §3, 18 §9.3 | ⬜ |
| M12-03 | Matchmaking: candidate selection + band widening, B3 fairness rule, bot backfill + the 6 authored bot ghosts | 11 §4, 24 §4.10 | ⬜ |
| M12-04 | Duel flow: `START_DUEL`/`SUBMIT_DUEL`, server-issued `duelSeed`, Elo (attacker-only, K-bands, floors), defence recording + duel log, 7 tiers, 14-day seasons + soft reset + rewards via inbox | 11 §4–5 | ⬜ |
| M12-05 | Leaderboard read model (window-function rank, 10-min cache) + S22 (pinned own rank, infinite scroll, search) | 11 §5.2a | ⬜ |
| M12-06 | Arena S20 + PvP Loadout S21 + onboarding (scripted tutorial duel, 5-bot ramp); attempt system (5/day + ad + Plus) | 11 §7–8 | ⬜ |
| M12-07 | Honor economy + Honor Shop (7 items, weekly SS-chest stock) | 11 §9 | ⬜ |
| M12-08 | Duel anti-cheat: server re-simulation + `LogHash` compare, rate limits, shadow-exclusion ladder; season rating-drift metric (R13) | 11 §6, 14 §9 | ⬜ |

---

## M13 — Live-ops & events framework

**Goal:** the data-driven limited-time-event framework — 🔒 **the framework is the deliverable; events are data.** No app update may be required to run an event.

**Kickoff decisions**
1. **O27** — `EVENT_SCORE_RUSH` leaderboard schema + percentile band rewards (unauthored).
2. **O2** — event outcome weights/value scalars for events 11–30: run through the simulator, sign off.
3. Confirm the launch-calendar shape (14-day majors, 0-gap, 7-day PvP season offset).

| ID | Task | Spec | Status |
|---|---|---|---|
| M13-01 | Event package schema + build-time validator + content-endpoint delivery + server-side window/membership resolution + per-event feature flag & mid-flight kill (convert-and-close) + versioning/rollback | 26 §2, §4 | ⬜ |
| M13-02 | The 3 archetypes: `EVENT_CHAPTER` (incl. `MATCH_PLAYER_HIGHEST` tier ladder), `EVENT_SCORE_RUSH` (absorbs the Weekly Chapter Challenge as a rolling package; data-driven scoring formula), `EVENT_COLLECTION` (tokens via LuckService duplicate protection) | 26 §3, 19 C | ⬜ |
| M13-03 | Event currency (caps, `conversionAtEnd`), reward track (≤12 milestones), event shop (per-player stock; every item has a permanent source — build-validated) | 26 §3.3–3.5 | ⬜ |
| M13-04 | Calendar scheduler (one major live, offsets, 3-day announce) + weekly modifier reuse rules | 26 §4, 19 C | ⬜ |
| M13-05 | S30 Events Hub + S31 Event Track + S32 Event Shop + Home event card; event telemetry + single start push | 26 §6, §8 | ⬜ |
| M13-06 | `EVT_EMBERFALL` launch package authored + track thresholds validated in the simulator against the day-1 cohort | 26 §5 | ⬜ |
| M13-07 | Simulator E11–E15 (180-day rolling calendar modelling) | 26 §7 | ⬜ |

---

## M14 — Guilds

**Goal:** 30-player guilds, quests, the weekly Guild Boss. Largest single addition (3–4 eng weeks + ongoing ops). 🔒 If the schedule slips, **this is the milestone that moves to post-launch** — it is additive and nothing depends on it.

**Kickoff decisions**
1. ⚠️ Moderation: the human review queue and response SLA need a **named owner** before the flag goes live.
2. Go/no-go: still in v1, or exercised as the relief valve?
3. Confirm 📐 constants (creation cost, member cap, boss HP formula, perk values) as initial values.

| ID | Task | Spec | Status |
|---|---|---|---|
| M14-01 | Guild aggregate + Postgres schema + `IGuildRepository`; lifecycle (create/join/leave/kick/disband), join policies, roles + rate limits, 24 h cooldown, inactivity automation | 27 §2–3 | ⬜ |
| M14-02 | Identity (name/tag + EN+DE profanity filter), phrase-board (60 phrases, TTL, caps), server-generated guild log | 27 §6–7 | ⬜ |
| M14-03 | Guild quests: 15 authored defs, daily draw rules, active-member scaling, 15 % contribution cap, 🔒 atomic-increment write path (`GuildContribution` events; domain never mutates Guild), guild chest + streaks | 27 §4, 30 §5 | ⬜ |
| M14-04 | Guild Boss: weekly cycle, HP pool formula, fight variant (no enrage, 120 s cap), server-authoritative damage, `GuildRules.SettleWeek` scheduled pure function, tier-band leaderboard | 27 §5 | ⬜ |
| M14-05 | Guild levels 1–20 + 4 non-combat perks + architecture test: guild state unreachable from the combat path / ghost snapshot | 27 §8 | ⬜ |
| M14-06 | S33–S36 screens + Home guild card; unlock gate Legend 15 | 27 §10 | ⬜ |
| M14-07 | Moderation/report pipeline (shared queue with duel/name reports), GDPR membership handling, `guilds.enabled` kill switch, guild push (2) + telemetry | 27 §6.3, §11 | ⬜ |
| M14-08 | Simulator E16–E19 (E19 feeds the M18 joint tuning) | 27 §9 | ⬜ |

---

## M15 — Ads & Slay Plus

**Goal:** the entire business model — 29 rewarded placements, interstitials, and the subscription as an adapter swap. Direction depends on the **O14 spike result** from M0.

**Kickoff decisions**
1. Apply the O14 spike ruling: S2S callbacks available → planned path; absent → patched plugin or signed-nonce fallback.
2. **O28** — subscription↔account binding lifecycle (binding, uniqueness, rebinding conflict).
3. **O16** — subscription display name (blocks store assets).
4. **O12** — revenue-model validation approach (store-page test before launch); the €24.99 lifetime-unlock hedge stays open — reconfirm.

| ID | Task | Spec | Status |
|---|---|---|---|
| M15-01 | MAX GDScript shim (`max_bridge.gd` + `MaxBridge.cs` + `PlacementMap`) + `AppLovinRewardedAdAdapter` + preloading policy; mediation config | 12 §3 | ⬜ |
| M15-02 | `AutoGrantAdAdapter` (Plus) + `FakeRewardedAdAdapter`; composition-root entitlement swap — no `if (isSubscriber)` anywhere | 23 §7.2 | ⬜ |
| M15-03 | Server reward path: S2S callback verification, `CLAIM_AD_REWARD` against the callback record, 🔒 no-fill-still-grants rule, cap engine (29 placements, per-placement + 44/day soft cap + 20 s gap, all in `data/ads.json`), bundle chapter-scaling | 12 §3–5 | ⬜ |
| M15-04 | Interstitial system (stage-gate cadence across runs, exclusion list, session/day caps) + 72 h grace period | 12 §4.2 | ⬜ |
| M15-05 | Billing: GooglePlay + StoreKit adapters, store S2S notification webhooks → server-owned entitlement, lapse handling (nothing lost; grace windows; read-only presets), restore + O28 binding rules | 12 §2 | ⬜ |
| M15-06 | Plus presentation: Shop tab (one item), post-L8 Home banner, honest copy, 7-day trial, in-app renewal price/date/cancel link | 12 §2.4 | ⬜ |
| M15-07 | Consent stack: MAX CMP (GDPR), ATT post-FTUE, COPPA-safe config, no-personalised-ads path | 12 §7 | ⬜ |
| M15-08 | Replay history (last 50 runs + duels) — the locked free feature, on the screen chosen for O31 | 12 §2.5 | ⬜ |

---

## M16 — Live-service essentials

**Goal:** the remaining doc-28 features (inbox already shipped in M5): account linking, Reserve polish, Feats & Renown, push.

**Kickoff decisions**
1. **O29** — per-feat counter semantics (`counters.json`): must be right before ship — unfixable once live.
2. Confirm link reward and Renown/TP totals (raises TP max 294 → 324; the `09` §8 guardrail re-derivation is E22, checked in M18).

| ID | Task | Spec | Status |
|---|---|---|---|
| M16-01 | Account linking: Google/Apple flows, provider-subject uniqueness, prompt scheduler (L10/L30 + fortnightly banner, never blocking), conflict comparison + typed confirm + 30-day soft-delete, link reward, `ACCOUNT` inbox emitters, 180-day anonymous purge | 28 B | ⬜ |
| M16-02 | Energy Reserve UI (second bar segment, `138/200 (+200)` reading) + hoard/overflow telemetry (mechanics landed in M1) | 28 C | ⬜ |
| M16-03 | Feats & Renown: 140 feats as data (O29 semantics first), 3-tier evaluation + claims, retroactive grant at first launch (modelled by E21 first), Renown milestones (+TP), S38 screen + Codex/Profile surfacing | 28 D | ⬜ |
| M16-04 | Push: provider per O5, registration port + adapter, the 5 permitted sends (energy-full, season-end, event start, guild-boss expiry, compensation), opt-in, 1/day cap | 14 §12 | ⬜ |

---

## M17 — Audio, localisation & accessibility completion

**Goal:** everything the player hears and reads, finished; the a11y contract fully met.

**Kickoff decisions**
1. DE review vendor/process (nothing ships machine-translated).
2. Final store asset list (blocked on O16/O18 from earlier kickoffs).

| ID | Task | Spec | Status |
|---|---|---|---|
| M17-01 | Complete audio set (all 106 assets) + full-set mastering pass + 9-item audio QA checklist; < 40 MB budget | 20 | ⬜ |
| M17-02 | Complete DE translation + human review + DE-length pass over every tight widget at largest text size | 13 §10 | ⬜ |
| M17-03 | All 8 accessibility features complete, incl. no-timer mode and full reduced-motion coverage | 13 §8 | ⬜ |
| M17-04 | Store & marketing art (E21): app icon, screenshots, key art, wordmark (engine-set type); honest online-requirement copy | 15 §E21, 14 §3.4 | ⬜ |

---

## M18 — Hardening, joint economy tuning & soft launch

**Goal:** the launch gates. 🔒 The simulator must pass with **dungeons + events + guilds enabled together** before any tuning is treated as final and before the live service opens (D31/E19/R10).

**Kickoff decisions**
1. **O32** — first-boot failure, maintenance mode, forced-update flows + status endpoint (design sign-off).
2. **O17** — store-listing copy strategy for "Idle" + install-source attribution plan.
3. Deployment decisions now due: O6 (Postgres hosting), region strategy, rollout percentages.
4. Schedule the two post-playtest reviews: **O10** (merge Dust+Stones → 7 currencies?) and **O11** (preset slots).

| ID | Task | Spec | Status |
|---|---|---|---|
| M18-01 | O32: first-boot failure / maintenance / forced-update states + status endpoint + `PROTOCOL_VERSION_UNSUPPORTED` flow | 13 S01, 14 §16.1 | ⬜ |
| M18-02 | Full-stack hardening: integration suite green, reconnect chaos at scale, perf budgets verified on device matrix (60 FPS, < 400 MB, < 5 ms sim, < 4 s cold start, < 150 MB package) | 14 §11, §13 | ⬜ |
| M18-03 | **Joint economy tuning (E19):** all three income streams + Reforge/Retune sinks enabled together; re-derive merge Crown costs, +11→+15 stone costs, `BeastFeedCost`, energy budget; simulator fully green = the D31 gate | 10 §9a, 21 §9.5 | ⬜ |
| M18-04 | Balance harness nightly + all guardrails green across 8 chapters × 3 tiers × 8 bosses; FTUE T1–T5 re-verified; E22 guardrail re-derivation at 324 TP | 05 §9, 17 §11 | ⬜ |
| M18-05 | Anti-cheat live: plausibility jobs, review queue staffed, sanctions ladder verified end-to-end | 14 §9 | ⬜ |
| M18-06 | Infrastructure as code (Terraform/OpenTofu, Azure first with swappable modules), rolling deploy + pre-deploy migrations, kill switches verified, one-version client-skew tolerance tested | 14 §1.1, §14 | ⬜ |
| M18-07 | Store setup: listings (O17 copy), bundle IDs (O18), subscription product, staged rollout at 5 % | 12, 00 §0a | ⬜ |
| M18-08 | Soft launch: telemetry review cadence, week-1 watch list (Score Rush formula concern, rating drift, disconnect rates, hoard lever), go/no-go criteria for global launch | 16 B3 | ⬜ |

---

## Open-decision registry (where each lands)

Quick index of every open item from `16_DECISION_LOG.md` Part B to the kickoff that resolves it:

| Item | Summary | Resolved at |
|---|---|---|
| O1 | Frontier/catch-up curve | M6 kickoff (derived by the simulator) |
| O2 | Event weights 11–30 | M13 kickoff |
| O4 | Server cost model | M18 kickoff (estimate before launch) |
| O5 | Push transport | M5 kickoff |
| O6 | Postgres hosting | M18 (deploy time) |
| O7 | Gear overlay rigging | M8 kickoff |
| O8 | VFX method | M8 kickoff |
| O10 | 8 → 7 currencies? | Post-playtest review (scheduled in M18) |
| O11 | Preset slot count | M9 kickoff; re-review M18 |
| O12 | Revenue validation | M15 kickoff (store-page test) |
| O13 | Curse chapter gating | M3 kickoff |
| O14 | MAX S2S callbacks | **M0 spike**; ruling applied at M15 kickoff |
| O15 | Client DI approach | M7 kickoff |
| O16 | Subscription display name | M15 kickoff (blocks store assets) |
| O17 | "Idle" title vs store copy | M18 kickoff |
| O18 | Bundle ID prefix | ✅ **Closed at M0 kickoff (2026-08-11): `de.ludwigso.slayidlerepeat`** |
| O23 | Godot mobile export maturity | **M0 spike** |
| O24 | Dungeon payout vs movement | M10 kickoff |
| O25 | Content distribution hardening | M5 (task M5-09) |
| O26 | Recurring TP income vs 324 max | M18 (E22 re-derivation) |
| O27 | Score-Rush leaderboard schema | M13 kickoff |
| O28 | Subscription↔account binding | M15 kickoff |
| O29 | Feat counter semantics | M16 kickoff |
| O30 | Manifest reconciliation | M11 kickoff |
| O31 | Replay-history owning screen | M12 kickoff |
| O32 | Boot-failure/maintenance flows | M18 kickoff |
| O33 | Two-device sessions | M5 kickoff |
| O34 | Display-name lifecycle | M5 kickoff (final by M12) |
| O35 | `PK_DICELORD_GIFT` redesign | M3 kickoff |
| O36 | FTUE mini-boss identity | M4 kickoff |
