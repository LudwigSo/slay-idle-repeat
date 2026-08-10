# 14 — Technical Architecture

🔒 LOCKED DECISIONS
1. **Client: Godot 4.3+ with C#** (.NET 8 / "Godot .NET" export templates). No web export — Android and iOS only.
2. **Backend: fully server-authoritative, for PvE as well as PvP.**
3. **Backend is a containerised ASP.NET Core application**, deployed initially to **Azure App Service for Containers**, but with **no hard vendor lock-in** — it must run unchanged on AWS ECS/Fargate, Google Cloud Run, Kubernetes, or a single self-hosted Docker host.
4. **Disconnection is handled by pausing and reconnecting gracefully**, never by kicking the player out.
5. Observability is **self-hostable open source**, containerised alongside the backend.
6. Determinism strategy: **rounded doubles plus a cross-platform CI hash test.**
7. Ad network: **AppLovin MAX**, via its official MIT-licensed Godot 4 plugin, wrapped in an adapter.
8. **Ports and adapters (D22), project-wide.** Every external dependency is a C# interface owned by `SlayIdleRepeat.Application` and implemented by an adapter in its own project — see **`23_PORTS_AND_ADAPTERS.md`**.

---

## 1. System overview

```
┌──────────────────────────────┐        ┌──────────────────────────────────────┐
│  CLIENT (Godot 4 / C#)       │        │  BACKEND (ASP.NET Core, container)   │
│                              │        │                                      │
│  • Rendering, input, audio   │◀──────▶│  • AUTHORITATIVE game state          │
│  • Local mirror of state     │  HTTPS │  • Command validation & execution    │
│  • Local combat simulator    │   +    │  • Identical combat simulator        │
│    (prediction & replay)     │  WS    │  • RNG seed authority                │
│  • Reconnect & resync        │        │  • Economy, drops, energy            │
│                              │        │  • PvP: ghosts, matchmaking, ladder  │
└──────────────────────────────┘        │  • Ad reward grants (MAX callbacks)  │
                                        │  • Subscription entitlement          │
                                        └───────────────┬──────────────────────┘
                                                        │
                              ┌─────────────────────────┼─────────────────────────┐
                              │                         │                         │
                     ┌────────▼────────┐      ┌─────────▼────────┐     ┌──────────▼────────┐
                     │  PostgreSQL     │      │  Redis           │     │  Object storage   │
                     │  profiles,      │      │  sessions, run   │     │  (S3-compatible)  │
                     │  ladder, events │      │  state, caches   │     │  battle logs      │
                     └─────────────────┘      └──────────────────┘     └───────────────────┘
```

Every component is open-source and portable. Postgres, Redis and S3-compatible storage exist as managed services on every hyperscaler **and** as containers you can run yourself. There is no proprietary service anywhere in the critical path.

### 1.1 The no-lock-in rule 🔒

| Rule | Enforcement |
|---|---|
| **No driver or vendor SDK anywhere outside an adapter** 🔒 | `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` reference **no** data-access or cloud package at all. Npgsql lives only in `Adapters.Persistence.Postgres`, StackExchange.Redis only in `Adapters.Cache.Redis`, and the S3 client only in `Adapters.ObjectStore.S3` (AWS SDK or MinIO client — both speak S3, and MinIO is self-hostable). Enforced by architecture tests (`23` §6) plus a CI check that fails if a vendor `PackageReference` appears in more than one `.csproj`. |
| No vendor-specific compute model | A plain ASP.NET Core web app in a Docker container. **No** Azure Functions, no Lambda, no Durable Functions, no vendor triggers. Background work runs as hosted services inside the same container or as a second container. |
| Configuration | Environment variables only (12-factor). No vendor config service. |
| Secrets | Injected as environment variables; the platform's secret store is an implementation detail of deployment, not of the app. |
| Infrastructure as code | **Terraform or OpenTofu**, with the Azure implementation first and the module boundaries drawn so AWS/GCP/self-host variants are swappable. |
| Local development | `docker compose up` brings the entire stack — API, Postgres, Redis, MinIO, observability — up on a laptop with no cloud account. |
| Portability test | CI builds and boots the full stack in Docker Compose on every commit. If it cannot run on a laptop, it is locked in. |

⚠️ **NEEDS DETAIL:** managed vs self-hosted Postgres for production (Azure Database for PostgreSQL vs a Postgres container with volume backups) is a cost/ops trade-off, not an architectural one. Either satisfies the rule. Decide at deployment time.

---

## 2. Server-authoritative PvE 🔒

This is the biggest architectural decision in the project and it shapes everything below.

### 2.1 What the server owns

**Everything that matters.** The server is the single source of truth for:

- The player profile: Legend Level, XP, all currencies, inventory, gear instances, pets, mounts, talents, presets, unlocks
- Energy: balance, regeneration, caps, refills
- Run state: the board, the current position, HP, gold, drafted perks, RNG stream positions, remaining ad uses
- Every random outcome: board layout, die rolls, perk draft options, drops, event results, minigame results
- Every battle result
- All PvP: ghosts, matchmaking, duel seeds, ratings, ladder
- Ad reward grants and subscription entitlement

### 2.2 What the client owns

**Presentation and prediction only.** The client:

- Renders, animates, plays audio, reads input
- Holds a **mirror** of server state, never the truth
- Runs the **identical** combat simulator locally so battles animate instantly instead of waiting on a round trip
- Predicts optimistically where it is safe (see §2.4) and reconciles when the server answers

### 2.3 Command / event protocol

The client sends **intents**; the server returns **authoritative outcomes**.

```
POST /run/{runId}/command
{
  "commandId": "c_8f3a...",     // client-generated UUID, idempotency key
  "sequence": 47,               // monotonically increasing per run
  "type": "ROLL_DICE",
  "payload": {}
}

→ 200
{
  "sequence": 47,
  "outcome": {
    "dieFace": { "kind": "Pip", "value": 4 },
    "newPosition": 19,
    "tile": { "type": "TILE_ELITE", "enemyId": "EL_RIMEFANG_WARDEN", "modifier": "ARMORED" },
    "battleSeed": "0x9f2a4c...",
    "rngStreamStates": { "dice": 12, "board": 8 }
  },
  "profileDelta": { ... },
  "stateHash": "fnv1a:a91f..."
}
```

**Command types:** `START_RUN`, `ROLL_DICE`, `USE_REROLL`, `CHOOSE_FORK`, `RESOLVE_TILE`, `PICK_PERK`, `REROLL_DRAFT`, `SKIP_DRAFT`, `SHOP_BUY`, `SHOP_REFRESH`, `EVENT_CHOOSE`, `MINIGAME_SUBMIT`, `CAMPFIRE_CHOOSE`, `START_BATTLE`, `CONFIRM_BATTLE_RESULT`, `REVIVE`, `END_RUN`, `ABANDON_RUN`, plus the meta commands (`EQUIP`, `MERGE`, `ENHANCE`, `SALVAGE`, `SPEND_TALENT`, `RESPEC`, `LEVEL_PET`, `ASCEND_PET`, `CLAIM_QUEST`, `CLAIM_AD_REWARD`, `UPLOAD_GHOST`, `START_DUEL`, `SUBMIT_DUEL`).

### 2.4 Where prediction is allowed

| Action | Prediction |
|---|---|
| Die roll | ❌ Never predicted — the server issues the face. The roll animation covers the round trip (0.8 s of animation vs ~80 ms of latency). |
| Movement after a known roll | ✅ Predicted — the outcome is already known |
| Battle | ✅ **Fully simulated locally** from the server-issued `battleSeed` and the server-issued build snapshot. The client animates immediately; the server simulates in parallel and the result is confirmed by hash. Divergence → the server's result wins and the client resyncs. |
| Perk draft options | ❌ Server-issued |
| Drops and rewards | ❌ Server-issued |
| UI navigation, inventory sorting, comparisons | ✅ Purely local |

**This is the key insight that makes server authority feel good:** the expensive, slow, dramatic thing (a 40-second battle) is *computed locally from a server-issued seed*, so it costs one round trip for the seed and zero for the fight. Only the cheap, instantaneous things (a roll, a card pick) pay latency, and they are all covered by existing animation time.

### 2.5 Latency budget

| Operation | Target p95 |
|---|---|
| Command round trip | < 150 ms |
| Run start (full state load) | < 800 ms |
| Battle start (seed + snapshot) | < 150 ms, hidden behind the 0.6 s intro banner |
| App launch to Home | < 4 s including auth and profile fetch |

If p95 command latency exceeds 400 ms for a region, deploy an additional regional instance. The API is stateless; run state lives in Redis, so horizontal scaling is trivial.

---

## 3. Connection handling 🔒

**Pause and reconnect gracefully.** Never kick the player out.

### 3.1 Behaviour

```
Connection lost
  → client keeps rendering the current screen; input is accepted but queued
  → after 2 s of failed retry, a non-blocking "Reconnecting…" pill appears top-centre
  → exponential backoff: 0.5s, 1s, 2s, 4s, 8s, then every 10 s indefinitely
  → the player can browse their build, perks, inventory (read-only mirror) while offline
  → on reconnect:
        client sends GET /run/{runId}/state?sinceSequence=N
        server returns the authoritative state and any outcomes the client missed
        client reconciles, plays a brief "resynced" flash, resumes exactly where it was
  → run state has a 48-hour server-side TTL, so closing the app on the subway
    and reopening it that evening resumes the same run
```

### 3.2 Idempotency 🔒

Every command carries a client-generated `commandId`. The server stores processed command IDs per run for 24 hours and **replays the stored outcome** for a duplicate rather than re-executing. This makes "did my roll go through before the connection dropped?" a non-question.

### 3.3 What is playable offline

| Available offline | Not available offline |
|---|---|
| Viewing the current run state, build, perks | Rolling, resolving tiles, battling |
| Inventory browsing and comparison | Equipping, merging, enhancing, salvaging |
| Talent tree browsing and preview | Spending talent points |
| Codex, settings, stats | Everything in the Arena |

The client must make the distinction obvious: offline-unavailable buttons are visibly disabled with a "reconnecting" affordance, never silently broken.

### 3.4 Honest communication

The store listing and first-launch screen must state plainly that **Slay Idle Repeat requires an internet connection**. Discovering this on a plane is a 1-star review; being told upfront is a shrug.

---

## 4. Solution structure — ports and adapters 🔒

**D22: every external dependency is accessed through a port (a C# interface owned by the application) and implemented by an adapter in a separate project.** This applies to ad networks, billing, push, telemetry, analytics, the database, the cache, object storage, store server APIs, remote config, the filesystem, the clock — and Godot itself.

**`23_PORTS_AND_ADAPTERS.md` is the authoritative document** for this: the full port catalogue with signatures, the ten adapter rules, the architecture tests that enforce them, and both composition roots. This section is the summary.

```
SlayIdleRepeat.sln
├── src/
│   ├── SlayIdleRepeat.Core/          # PURE rules. Zero dependencies — not even a clock.
│   │   ├── Combat/  Stats/  Board/  Dice/  Progression/  Economy/
│   │   ├── Effects/             #   the effect DSL interpreter (doc 18)
│   │   ├── Rng/                 #   DeterministicRng, SeedStreams
│   │   └── Model/
│   ├── SlayIdleRepeat.Contracts/     # DTOs shared client↔server. No behaviour.
│   ├── SlayIdleRepeat.Application/   # use cases + ALL port interfaces
│   │   ├── Ports/{Client,Server,Shared}/
│   │   └── UseCases/            #   RollDice, PickPerk, MergeGear, StartDuel, …
│   ├── adapters/
│   │   ├── client/              #   Ads.AppLovin · Ads.AutoGrant · Billing.GooglePlay ·
│   │   │                        #   Billing.StoreKit · Api.Http · Cache.LocalFile ·
│   │   │                        #   Push.Firebase · Telemetry.Sentry ·
│   │   │                        #   Consent.AppLovinCmp · Platform.Godot
│   │   ├── server/              #   Persistence.Postgres · Cache.Redis · ObjectStore.S3 ·
│   │   │                        #   Store.GooglePlayServer · Store.AppStoreServer ·
│   │   │                        #   AdVerify.AppLovinS2S · Push.FcmApns ·
│   │   │                        #   Analytics.PostHog · Telemetry.OpenTelemetry ·
│   │   │                        #   Config.HttpJson
│   │   └── fakes/               #   InMemory — a fake for EVERY port
│   ├── SlayIdleRepeat.Server/        # COMPOSITION ROOT: ASP.NET Core host, endpoints, DI wiring
│   └── SlayIdleRepeat.Client/        # COMPOSITION ROOT: the Godot project (see §5)
├── SlayIdleRepeat.Data/              # shared JSON content, embedded in both
├── tests/                       # Core · Application · Architecture · Contract · Integration
└── tools/
    ├── BalanceHarness/          # mass battle simulation
    └── EconomySim/              # doc 21 — runs the real app on in-memory adapters
```

### 4.1 The dependency rule 🔒

```
Adapters ──▶ Application ──▶ Core ──▶ (nothing)
```

- `Core` references nothing but the .NET BCL. No Godot, no ASP.NET, no drivers, **no clock, no randomness source.**
- `Application` defines every port and references no adapter, ever.
- Adapters implement ports and never reference each other.
- Only the two composition roots may reference `SlayIdleRepeat.Adapters.*`.

`SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` are referenced by **both** the server and the Godot client. **This is what makes the whole architecture work:** there is exactly one implementation of the rules and one set of use cases, and both sides run them.

Enforced by `SlayIdleRepeat.Architecture.Tests`, which fails the build on violation (`23` §6). Additionally, a vendor `PackageReference` appearing in more than one `.csproj` fails CI.

### 4.2 Why this is not gold-plating here

Three consequences make the pattern load-bearing rather than decorative:

1. **The economy simulator (doc 21) and balance harness run the real application** on in-memory adapters — no database, no engine, no network. Without ports, both tools would have to reimplement the game and would then be testing the reimplementation.
2. **The no-vendor-lock-in decision (D12) becomes enforceable.** A rule that says "must run on any hyperscaler" is unfalsifiable if `NpgsqlConnection` is scattered through the application; with ports it is a CI check.
3. **The Slay Plus promise becomes an adapter swap.** Subscribers get `AutoGrantAdAdapter` instead of `AppLovinRewardedAdAdapter` behind the same `IRewardedAdPort`, so there is no `if (isSubscriber)` branch anywhere in the game (`23` §7.2).

---

## 5. Client project layout (Godot)

```
res://
├── addons/                     # vendor GDScript plugins (AppLovin MAX, editor tools).
│                               #   Always wrapped by an adapter, never called from game code.
├── Composition/                # the ONLY place concrete adapters are named. Selection is
│                               #   platform-conditional (#if ANDROID / IOS) and
│                               #   entitlement-conditional (Plus → AutoGrantAdAdapter).
├── game/
│   ├── scenes/                 # DRIVING ADAPTERS: boot, home, board, battle, draft, forge,
│   │                           #   talents, menagerie, arena, shop, codex, settings
│   ├── presenters/             # plain C#, ports injected by the composition root
│   ├── net/                    # StateMirror, CommandQueue, ReconnectManager —
│   │                           #   built on IGameApiPort / IRealtimeChannelPort
│   └── vfx/
├── data/                       # mirror of SlayIdleRepeat.Data, for prediction + display
├── art/                        # see doc 15
├── audio/                      # see doc 20
└── tests/
```

**Architecture pattern:** Model–View–Presenter over a `StateMirror`, inside the hexagon.

- **Godot scenes are driving adapters.** They render and forward input; they hold no rules and no port references.
- **Presenters are plain C# classes** receiving their ports as constructor arguments from the composition root, so they are unit-testable **without booting Godot** — the practical payoff of adapter rule A10 (`23` §5).
- No game rules in `_Process`. Everything engine-specific (audio, haptics, locale, device info) goes through `SlayIdleRepeat.Adapters.Platform.Godot`.

⚠️ **NEEDS DETAIL:** DI container vs hand-rolled composition root in the client. Godot's node lifecycle resists constructor injection into scenes. Recommendation in `23` §9: a hand-rolled root with explicit factories — scenes talk only to presenters, presenters receive ports from the root.

---

## 6. Data-driven content 🔒

Every number marked 📐 TUNABLE lives in `SlayIdleRepeat.Data/*.json`, never in code.

- The **server** is the source of truth for content. The client ships a copy for prediction and display, and validates its content hash against the server at session start. A mismatch triggers a content download before play — this allows balance changes without an app store update.
- JSON is validated at build time against schemas in `SlayIdleRepeat.Data/schema/`. The build fails on unknown IDs, missing icons, out-of-range values, orphaned references or duplicate IDs.
- In editor/dev builds, content hot-reloads without restarting.

Example — chapter definition:

```json
{
  "id": 5,
  "displayName": "loc.chapter.5.name",
  "biomeArtSet": "biome_frost",
  "paletteId": "pal_frost",
  "musicId": "mus_frost",
  "powerTarget": 16000,
  "stageLengths": [12, 14, 16],
  "eliteCount": [1, 2, 2],
  "tileWeights": [ {"TILE_ENEMY": 32, "...": 0}, {}, {} ],
  "enemyPool": ["EN_FROST_GRUNT", "EN_FROST_BRUTE", "..."],
  "elitePool": ["EL_RIMEFANG_WARDEN", "EL_GLACIER_MAW"],
  "bossId": "BOSS_RIMEHOLD",
  "lootTable": "LT_CH5",
  "unlockCondition": {"clearChapter": 4, "tier": "NORMAL"}
}
```

---

## 7. Persistence

### 7.1 Server-side (authoritative)

| Store | Contents | Notes |
|---|---|---|
| **PostgreSQL** | Player profiles, inventory, gear instances, pets, mounts, talents, presets, PvP ghosts, ratings, ladder, seasons, entitlements, an append-only economy event log | Row-level JSONB for the flexible parts (inventory, talents), typed columns for the queryable parts (rating, level, chapter). Reached only via `IPlayerRepository`, `IGhostRepository`, `ILeaderboardRepository`. |
| **Redis** | Active run state, sessions, command idempotency keys, matchmaking candidate cache, rate limits | Run state TTL 48 h; written through to Postgres on stage gates and run end. Reached only via `IRunStateStore` and `IIdempotencyStore`. |
| **S3-compatible object storage** | Battle logs for replay, ghost snapshots over a size threshold | MinIO locally and self-hosted; any S3 service in production. Reached only via `IBattleLogStore`. |

🔒 **No application or use-case code names any of these technologies.** Swapping Redis for a Postgres table, or S3 for a local volume, is a one-adapter change with no application edit — which is what makes the no-lock-in decision (D12) real rather than aspirational.

### 7.2 Client-side (cache only)

| Property | Value |
|---|---|
| Contents | A read-only mirror of the last known profile and run state, plus settings |
| Format | JSON, gzipped, in `user://slayidlerepeat/cache/` |
| Purpose | Instant cold-start display and offline browsing. **Never** authoritative. |
| Security | None needed — tampering with the cache changes nothing, because the server ignores it. This is a direct benefit of server authority: no save encryption, no anti-tamper theatre. |

### 7.3 Account and cross-device

| Concern | Approach |
|---|---|
| Anonymous first launch | Server issues a device-bound account immediately; the player is playing within seconds, no sign-in wall |
| Upgrade to a real account | Sign in with Google / Apple, linking the anonymous account |
| Cross-device | Automatic and instant — the profile has always lived on the server. No cloud-save provider is needed. ✅ This closes the old open question about save sync. |
| Deletion | GDPR-compliant account deletion endpoint, hard delete within 30 days |

---

## 8. Determinism and RNG

```csharp
public sealed class DeterministicRng {
    // xoshiro256** — fast, high quality, portable, identical across platforms
    public DeterministicRng(ulong seed, string stream);
    public uint   NextUInt();
    public double NextDouble();
    public int    Range(int minInclusive, int maxExclusive);
    public T      WeightedPick<T>(IReadOnlyList<(T item, double weight)> table);
    public ulong  SaveState();
    public void   RestoreState(ulong s);
}
```

### 8.1 Seed streams

One run seed spawns independent child streams so consuming randomness in one system never shifts another. Stream positions are part of authoritative run state and are returned to the client with every outcome.

| Stream | Used by |
|---|---|
| `board` | Board layout generation |
| `dice` | Die rolls and the fair-bag weights |
| `draft` | Perk draft options |
| `drops` | Gear, currency and material drops |
| `combat:{battleIndex}` | One stream per battle, so a revive replay is reproducible |
| `events` | Event card outcomes |
| `minigame:{index}` | Minigame randomisation |

🔒 **Never use `System.Random`, `GD.Randi()`, `Random.Shared`, `DateTime.Now`, `Guid.NewGuid()` or `Environment.TickCount` anywhere in `SlayIdleRepeat.Core` or `SlayIdleRepeat.Application`.** CI greps for these and fails.

Time and identity are **ports**, not ambient state: `IClockPort` and `IIdGeneratorPort` (`23` §4.3). Tests supply a frozen clock and a counting ID generator, which is what makes the reconnect chaos test (§13) and the economy simulator (doc 21) reproducible.

Game randomness is deliberately **not** a port — `DeterministicRng` lives in `Core` and is seeded from server-issued values, because it is part of the rules rather than an external dependency.

### 8.2 Floating point 🔒

**Rounded doubles plus a cross-platform CI hash test.**

- All combat math uses `double`.
- Avoid `Math.Pow` in hot paths; use explicit multiplication.
- **Round to 4 decimal places (`Math.Round(x, 4)`) at every accumulation point** — after each damage calculation, each heal, each stat aggregation step.
- CI runs a determinism test on every commit: simulate 10,000 fixed `(seed, build, enemy)` triples on **Linux x64 (server), Android ARM64, and iOS ARM64**, compare `LogHash` values, fail on any divergence.
- If that test ever fails and cannot be fixed by additional rounding, escalate to fixed-point Q32.32 in `Core/Combat` only. Do not pre-emptively pay that cost.

---

## 9. Anti-cheat

Server authority does most of the work. What remains:

| Layer | Mechanism |
|---|---|
| No client-side state | The client cannot grant itself currency, drops, levels or wins. There is nothing to hack in the save. |
| Battle verification | The client reports `LogHash`; the server has already computed the same fight from the same seed. Mismatch → server result wins, counter incremented, no player-facing error. |
| Command validation | Every command is checked against the authoritative state: is it this player's run, is it the expected sequence, is the action legal now, are caps respected? |
| Rate limiting | Per-player and per-IP limits on all endpoints; duel submissions capped at the attempt limit. |
| Ad rewards | Granted server-side on AppLovin MAX server-side callbacks, never on client assertion. |
| Entitlement | Plus status comes from store server-to-server notifications, never from a client receipt. |
| Plausibility monitoring | Background job flags accounts whose power, currency or rating trajectory sits outside a statistical envelope. Flags go to a review queue, not to automatic bans. |

Sanctions ladder: shadow-exclusion from the ladder → rating reset → account action. Only on repeated, confirmed manipulation.

---

## 10. Observability 🔒

**Self-hostable open source, containerised alongside the backend.**

| Concern | Tool | Notes |
|---|---|---|
| Crash & error reporting | **Sentry** | Self-hosted or SaaS — identical SDK, so the choice is a deployment detail. Godot/C# and ASP.NET Core SDKs both supported. |
| Product analytics | **PostHog** | Self-hostable, event-based, good funnels and retention analysis. Events emitted from the **server**, not the client — since the server sees every run, drop and transaction, analytics are complete and unspoofable by construction. |
| Metrics & tracing | **OpenTelemetry → Prometheus + Grafana** | Vendor-neutral instrumentation. Swap the backend freely. |
| Log aggregation | **Loki** (or plain stdout + the platform's log store) | Structured JSON logs via Serilog |
| Remote config / feature flags | **A JSON endpoint on our own backend**, cached client-side for 6 hours | No third party. Flags gate PvP, individual ad placements, content versions and the Plus offer. |

### 10.1 Event set (server-emitted)

`run_start`, `run_end` (result, chapter, tier, duration, stage reached, rewards), `battle_end` (enemy, duration, hp remaining), `perk_drafted` (id, tier, options offered), `perk_skipped`, `die_rolled` (face), `tile_resolved` (type), `ad_offered / started / completed / failed` (placement), `plus_offer_viewed / trial_started / converted / renewed / cancelled / lapsed`, `gear_merged`, `gear_enhanced` (level, success), `talent_spent`, `pet_levelled`, `duel_start / duel_end`, `energy_empty`, `session_start / session_end`, `level_up`, `chapter_unlocked`, `tutorial_step`, `disconnect` (duration, screen), `resync`.

The two most important derived metrics: **perk pick rate by perk id** (balance) and **run abandonment point by tile index** (pacing). A third is now available for free: **disconnect rate by region and screen**, which tells you whether the online requirement is hurting you.

---

## 11. Performance

| Target | Value |
|---|---|
| Client frame rate | 60 FPS on Snapdragon 695 / A13 class |
| Client memory | < 400 MB RSS |
| Cold start to Home | < 4 s including auth and profile fetch |
| Battle simulation | < 5 ms for a 90 s fight, client and server |
| Server cost at 10k DAU | ⚠️ **NEEDS DETAIL** — estimate before launch. Rough shape: ~30 commands/run × ~8 runs/DAU = ~2.4M requests/day, trivially served by 2 small container instances plus a small Postgres. |
| APK/IPA download | < 150 MB |

Client techniques: texture atlases per biome and UI group; object pooling for combat text, particles, tile nodes and enemy sprites; board virtualisation (instantiate ~14 visible tiles, recycle on scroll); compute the whole battle up front then animate from the log; ETC2/ASTC compression.

Server techniques: run state in Redis so the API is stateless; battle logs written asynchronously to object storage; content cached in memory; connection pooling to Postgres.

---

## 12. Third-party integration

| Concern | Approach |
|---|---|
| Ads | **AppLovin MAX** via the **official MIT-licensed Godot 4 plugin** (<https://github.com/AppLovin/AppLovin-MAX-Godot>). The plugin is GDScript-only, so a GDScript autoload shim plus `AppLovinRewardedAdAdapter : IRewardedAdPort` is needed — **3–5 engineering days**, not the 2–3 weeks a native bridge would have cost. Everything AppLovin-specific lives in `Adapters.Ads.AppLovin`; the game sees only the port. See `12` §3 and `23` §8. **Note:** the Android and iOS builds require the custom export-template path (Gradle + Java 17; CocoaPods + Xcode), so CI must build ads through that path from day one. |
| Subscription | Google Play Billing and StoreKit 2 behind `IBillingPort`, implemented by `Adapters.Billing.GooglePlay` and `Adapters.Billing.StoreKit`, selected per platform at the composition root. **Server-to-server notifications** are the authoritative entitlement source, consumed via `IStoreSubscriptionPort` on the server. |
| Push notifications | Energy-full and season-ending only, opt-in, max 1/day. ⚠️ **NEEDS DETAIL:** provider not chosen — recommend FCM + APNs directly rather than a wrapper service, to stay lock-in-free. |
| Auth | Anonymous device accounts, upgradeable via Sign in with Google / Apple. Tokens are our own JWTs; the identity providers are only used for the initial assertion. |

---

## 13. Testing requirements

| Layer | Requirement |
|---|---|
| Unit | `SlayIdleRepeat.Core` at ≥ 80% line coverage. Damage formula, stat aggregation, merge math, talent math, energy math, effect DSL each have explicit cases. |
| **Architecture** | `SlayIdleRepeat.Architecture.Tests` enforces the dependency rule, port purity, adapter isolation and the two-implementations rule. **Fails the build on violation** — see `23` §6. |
| **Contract** | One shared suite per port, run against **every** implementation including the in-memory fake. This is what stops fakes drifting from real adapters. |
| **Application** | Use-case tests run entirely on in-memory adapters — no database, no engine, no network. |
| Determinism | The cross-platform `LogHash` test in §8.2, in CI on every commit. |
| Client/server parity | A test that runs the same 1,000 command sequences through the client's and the server's `Core` and asserts identical state hashes. |
| Integration | Full run played end-to-end against a Docker Compose stack in CI. |
| Reconnect | Automated chaos test: kill the connection at every command boundary of a full run and assert the run completes correctly with no duplicated or lost outcomes. |
| Balance | The headless harness run nightly; alert on clear-rate drift > 5 pp. |
| Economy | The simulator (doc 21) run on every economy data change; fail on the 45% fairness gap. |
| Content | Build-time schema validation of all JSON. |
| Portability | CI boots the whole stack via `docker compose` with no cloud credentials. |

---

## 14. Build and release

- **CI on every push:** build Core/Server/Client, run unit + determinism + parity tests, validate content, build the server container image, build an Android debug APK **through the custom export template with the MAX plugin included** (see `12` §3.2 — a plain export will not produce a working ad build), boot the Docker Compose stack.
- **Nightly:** balance harness + economy simulator.
- **Server release:** container image to a registry, rolling deploy, database migrations run as a pre-deploy job. The server must tolerate one version of client skew in both directions.
- **Client release:** Android AAB and iOS IPA, staged rollout starting at 5%.
- **Kill switches:** remote config flags for PvP, each ad placement, the Plus offer, and each chapter — so a bad content change is a config edit, not a client patch.

---

## 15. Cost and complexity honesty

⚠️ **Worth stating plainly:** choosing full server authority for PvE roughly **doubles the engineering scope** of this project versus an offline-first client with a thin PvP service. You get, in exchange: unspoofable progression, a completely fair ladder, instant cross-device play, no save-file security work, complete server-side analytics, remote balance tuning without app updates, and airtight ad-reward and subscription integrity.

Those are real benefits and they line up well with a game whose entire pitch is fairness. But the trade is a permanent operational burden — you now run a service, not just ship an app — and the game does not work on a plane. Both consequences should be accepted deliberately, not discovered later.
