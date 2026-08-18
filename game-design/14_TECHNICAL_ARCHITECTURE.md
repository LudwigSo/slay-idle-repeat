# 14 — Technical Architecture

🔒 LOCKED DECISIONS
1. **Client: Godot 4.7.1-stable with C#** (`net8.0` / "Godot .NET" export templates — engine pinned by the M0-05a spike). No web export. ⚠️ **Android at v1; iOS post-launch** (`16` D34). The iOS export path is specified and CI-ready but unverified — `docs/spikes/O23-godot-ios-export.md`.
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
  "protocolVersion": 1,         // envelope version — see §16.1
  "commandId": "c_8f3a...",     // client-generated UUID, idempotency key
  "sequence": 47,               // monotonically increasing per run (per player for meta commands, §16.3)
  "type": "ROLL_DICE",
  "payload": {}
}

→ 200
{
  "protocolVersion": 1,
  "sequence": 47,
  "outcome": {
    "dieFace": { "kind": "Pip", "value": 4 },
    "newPosition": 19,
    "tile": { "type": "TILE_ELITE", "enemyId": "EL_RIMEFANG_WARDEN", "modifier": "ARMORED" },
    "battleSeed": "0x9f2a4c...",
    "rngStreamStates": { "dice": 12, "board": 8 }    // draw counters, literally — see §8.1
  },
  "profileDelta": { ... },
  "stateHash": "fnv1a:a91f..."
}
```

A command the server says no to returns a **rejection envelope**, also on HTTP 200 — the full contract (rejection reasons, HTTP mapping, sequencing, idempotency scope) is the normative appendix, **§16**.

🔒 **The wire command list and the domain `GameCommand` hierarchy are the same vocabulary** (`30` §11). One name per command, no mapping layer between transport and domain. This is the direct guard against the "mapping fatigue" failure mode recorded in `23` §9.

#### The canonical command registry 🔒

This table is the **complete** command vocabulary — wire protocol and domain `GameCommand` hierarchy alike, per the one-vocabulary rule above. 🔒 **It is exhaustive: a command not listed here does not exist.** Adding one is a decision, recorded in `16`, and lands here first. Payload sketches show shape and intent; the field-level source of truth is the public `GameCommand` subtypes in `Core/Commands` (`30` §11.2) — `Contracts` wraps them and never re-declares them (`30` §11.6). Commands marked *(A7)* were added by the `16` A7 registry ruling.

**Run commands (19)** — `POST /run/{runId}/command`, sequence per run. *(Exception: `START_RUN` is submitted on the player endpoint, since no `runId` exists yet; the server allocates the `RunId` and the run's sequence starts at 1.)*

| Command | Payload sketch | Notes |
|---|---|---|
| `START_RUN` | `{ chapterId, tier }` | Commits `runSeed` before the board is shown (`02` §2) |
| `ROLL_DICE` | `{}` | Server answers with the face, movement and landing outcome |
| `USE_REROLL` | `{}` | Spends one reroll charge on the just-shown face (`02` §3) |
| `CHOOSE_FORK` | `{ branchIndex }` | Always required at a junction (`02` §3, A7 movement ruling) |
| `RESOLVE_TILE` | `{}` | Acknowledges / advances the pending tile resolution |
| `PICK_PERK` | `{ optionIndex }` | |
| `REROLL_DRAFT` | `{}` | |
| `SKIP_DRAFT` | `{}` | |
| `SHOP_BUY` | `{ shopSlotIndex }` | In-run shop, run-local Gold (`03` §7). Distinct from the meta `SHOP_PURCHASE` |
| `SHOP_REFRESH` | `{}` | |
| `EVENT_CHOOSE` | `{ choiceIndex }` | |
| `MINIGAME_SUBMIT` | `{ minigameId, result }` | Client-asserted, legality-validated only (`03` §6.2, §9) |
| `CAMPFIRE_CHOOSE` | `{ choiceIndex }` | |
| `START_BATTLE` | `{}` | Server answers with `battleSeed` + the build snapshot (§2.4) |
| `CONFIRM_BATTLE_RESULT` | `{ logHash }` | |
| `REVIVE` | `{}` | Once per run (`02` §6) |
| `USE_CONSUMABLE` | `{ consumableId }` | *(A7)* Board-only, never during combat (`03` §7, `04` §3) |
| `END_RUN` | `{}` | |
| `ABANDON_RUN` | `{}` | |

**Meta commands (33)** — `POST /player/command`, sequence per player (§16.3). Commands whose outcome needs randomness are marked **⚄** and draw from the command's server-issued seed (`30` §3, §8.1). *(M4-04 corrected the count — the table had held thirty rows since the A7 additions — and marked `MERGE` and `ENHANCE` ⚄: both draw, and neither was marked. Eleven rows carry the die.)*

🔒 **The M4 retro's product-owner ruling of 2026-08-17 added three rows: `UNEQUIP`, `LOCK_ITEM` and `SET_AUTO_SALVAGE_RULES`.** The vocabulary goes **49 → 52** (19 run + 33 meta); the run table is untouched. None of the three draws, so none carries **⚄** — the die count stays eleven. Each closes a mechanism that shipped with no way to reach it: a gear slot could be filled but never emptied, `Inventory.SetLock` had no production caller so `LOCKED` was a state no real player could be in, and `Player.AutoSalvageRules` had no writer so `Rules/Forge/AutoSalvageFilter` was unreachable code. ⚠️ The same ruling **explicitly did not add `EXPAND_INVENTORY`** — capacity is a flat 1000 instead (`08` §5), and `10` §4's ladder stays authored and unspendable.

*(Counted off the table below rather than carried forward: 30 rows before this edit, verified line by line, plus three.)*

| Command | Payload sketch | Notes |
|---|---|---|
| `BEGIN_SESSION` ⚄ | `{ clientVersion, contentHash }` | *(A7)* Server-acknowledged first contact of a session **and** of each game day. Carries the calendar advance, the daily free Energy refill (`10` §3) and the day's random draws — the quest slate (`19` B) and the Daily shop block (`10` §5.1) — its command seed is the day's draw seed. Semantics: `30` §2.3 |
| `SKIP_FTUE` | `{}` | *(A7)* Valid only while `ftueProgress` is between beats 2 and 8; grants the full scripted payout and jumps to beat 9 (`19` D6). Idempotent — a resend after completion is a no-op |
| `EQUIP` | `{ itemId, gearSlot }` | Gear only; pets and mounts have their own commands below |
| `UNEQUIP` | `{ gearSlot }` | *(M4 review 2026-08-17)* Empties one gear slot. Names the **slot**, not the item — a slot holds at most one identity, and `EQUIP` could only ever overwrite. Refused mid-run on `EQUIP`'s rule (`07` §4), and refused on a slot that is already empty |
| `MERGE` ⚄ | `{ inputItemIds[2–3], dustSubstituted }` | *(M4-04)* `08` §4.1 fuses **three** items, and the earlier two-id sketch could not express that at all — two ids plus a flag is only complete when `dustSubstituted` is true. The list carries the real inputs: **three** ids with `dustSubstituted: false`, **two** with `dustSubstituted: true`, and no other combination is legal. The affix re-roll at the new rarity draws from this command's seed |
| `ENHANCE` ⚄ | `{ itemId }` | *(M4-04)* The success roll draws from this command's seed |
| `SALVAGE` | `{ itemIds[] }` | |
| `LOCK_ITEM` | `{ itemId, locked }` | *(M4 review 2026-08-17)* Sets or clears `08` §5's lock, which excludes an item from auto-salvage and merge selection. An **explicit boolean, not a toggle** — a toggle is not idempotent, and a client retrying under a fresh `commandId` after a timeout would unprotect the item it had just protected. A locked item is accepted (that is how it is unlocked); an item waiting in overflow is not |
| `SET_AUTO_SALVAGE_RULES` | `{ rules: [{ rarity, belowEnhanceLevel }] }` | *(M4 review 2026-08-17)* Replaces `08` §4.3's auto-salvage filter wholesale; an empty list sweeps nothing. 🔴 **The payload is derived, not authored**: this table sketched none, so the shape is the shipped `Core/Primitives/AutoSalvageRule` row — itself read literally off `08` §4.3's own example, *"salvage all C and B below +3"* — and nothing else. One row per band, at most as many rows as the rarity ladder has bands, and `belowEnhanceLevel` inside `08` §4.2's authored `minLevel..maxLevel+1`. ⚠️ Setting the rows only: nothing applies the filter at run end yet, and the screen that edits them is M9-01's |
| `RESPEC` | `{}` | |
| `LEVEL_PET` | `{ beastId }` | Pets **and mounts** — mounts mirror pets (`07` §3.1) |
| `ASCEND_PET` | `{ beastId }` | Same scope as `LEVEL_PET` |
| `EQUIP_PET` | `{ slotIndex: 0–2, petId? }` | *(A7)* Null `petId` unequips the slot |
| `EQUIP_MOUNT` | `{ mountId? }` | *(A7)* Null unequips |
| `CLAIM_QUEST` | `{ questSlot }` | |
| `REROLL_QUEST` ⚄ | `{ questSlot }` | *(A7)* 1 free per day (`19` B); the replacement is drawn from this command's seed |
| `CLAIM_AD_REWARD` | `{ placementId }` | Granted against the server-side S2S callback record (`12` §3.3) |
| `CLAIM_CALENDAR` | `{}` | *(A7)* Claims the currently open calendar day (`19` G) |
| `CLAIM_INBOX` | `{ messageIds[]? }` | *(A7)* Omitted/empty = claim everything claimable (`28` A4) |
| `SPIN_WHEEL` ⚄ | `{}` | *(A7)* Consumes the oldest available spin charge, free before ad-granted (`19` F) |
| `SET_FOCUS` | `{ gearSlot?, family? }` | *(A7)* Null clears; a change starts the 12 h cooldown (`24` §5) |
| `REFORGE_ITEM` ⚄ | `{ itemId }` | *(A7)* Quality re-roll, keep-best (`24` §6.1) |
| `RETUNE_ITEM` ⚄ | `{ itemId, lockedAffixIds[≤3], wishlistAffixIds[≤3] }` | *(A7)* The wishlist rides the command and persists on the item; the M2 mercy counter resets when it changes (`24` §6.2) |
| `SAVE_PRESET` | `{ presetSlot, name }` | *(A7)* Snapshots the current talents + loadout (`09` §2.1) |
| `APPLY_PRESET` | `{ presetSlot }` | *(A7)* Never mid-run |
| `SHOP_PURCHASE` | `{ offerId, quantity }` | *(A7)* The meta shop (`10` §5). Purchased containers arrive **unopened** on the shelf (`24` §4) |
| `OPEN_CHEST` ⚄ | `{ containerId }` | *(A7)* Pity and Focus are read **at open** (`24` §4) |
| `OPEN_EGG` ⚄ | `{ containerId }` | *(A7)* Same |
| `OPEN_CRATE` ⚄ | `{ containerId }` | *(A7)* Same |
| `UPLOAD_GHOST` | `{}` | |
| `START_DUEL` ⚄ | `{ ghostId }` | The server issues `duelSeed` from this command's seed (`11` §4.3) |
| `SUBMIT_DUEL` | `{ duelId, logHash }` | |

### 2.3a The query surface 🔒

Commands are not the whole API. Reads split into two kinds with **different rules**, per the CQRS split in `30` §12:

| | Own authoritative state | Cross-player read models |
|---|---|---|
| Endpoints | `POST /run/{runId}/command`, `POST /player/command`, `GET /run/{runId}/state?sinceSequence=N` | `GET /ladder`, `/arena/candidates`, `/guild/{id}/roster`, `/guild/{id}/boss`, `/guilds?search=`, `/event/{id}/leaderboard` |
| Consistency | 🔒 **Strong. Read-your-own-writes.** | Eventual, with a stated staleness budget per view (`30` §12.4) |
| Routing | Always the primary | Cacheable, replica-eligible |
| Idempotency key | Required on commands | None — `GET` is idempotent by nature |

🔒 **The player's own profile and run are never served from a projection.** A player rolls ~30 times per run and each roll must resolve inside its 0.8 s animation; a stale read would break the run loop, `stateHash` verification and the prediction model in §2.4. `GET /run/{runId}/state` looks like a query but is a **write-model read** and stays on the primary.

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

If p95 command latency exceeds 400 ms for a region, deploy an additional regional instance. The API is stateless; hot run state is cached in Redis over the Postgres-authoritative row (§16.4), so horizontal scaling is trivial.

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

Every command carries a client-generated `commandId`. The server stores each processed command's outcome for **the lifetime of the run state itself — the same 48-hour TTL** — and **replays the stored outcome** for a duplicate rather than re-executing. *(The earlier 24 h window contradicted the 48 h resumable-run rule and was superseded — ruled in `16` A7; a resumable run must be able to replay any of its outcomes, so the two lifetimes are one.)* Scope, sequencing and the conflict values are normative in §16.3. This makes "did my roll go through before the connection dropped?" a non-question.

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
├── game-data/              # shared JSON content, embedded in both
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
├── data/                       # mirror of game-data, for prediction + display
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

Every number marked 📐 TUNABLE lives in `game-data/*.json`, never in code.

🔒 **Every economy-affecting tunable lives specifically in `game-data/tuning/`** — a flat directory of 14 files, catalogued in `21` §3.1. A 📐 number outside that directory is a bug, and a build-time check enumerates every 📐 marker in the documentation set against the schema keys and **fails on a mismatch**. That check is what stops the tuning surface eroding over eighteen months, and it is what makes the economy simulator (`21`) and its parameter sweeps possible at all — a number in code can never be swept, and will therefore never be tuned.

Experiments and what-ifs run as **sparse override patches** layered on top of the canonical files (`21` §3.3), never as edits to them. This keeps `git diff` on `game-data` a record of decisions rather than a record of attempts.

🔒 Content is loaded once into an **immutable, version-stamped `ContentSnapshot`** and passed to the domain on `GameContext` (`30` §3). Loading JSON is I/O and belongs in an adapter; *reading* content is a rule. The version stamp is what lets a replayed command reproduce its original outcome after a balance patch — without it, replay and the reconnect chaos test silently diverge whenever content changes.

- The **server** is the source of truth for content. The client ships a copy for prediction and display, and validates its content hash against the server at session start. A mismatch triggers a content download before play — this allows balance changes without an app store update.
- JSON is validated at build time against schemas in `game-data/schema/`. The build fails on unknown IDs, missing icons, out-of-range values, orphaned references or duplicate IDs.
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
  "enemyPool": { "GRUNT": 15, "SWARM": 10, "BRUTE": 20, "SKIRMISHER": 15,
                 "WARDEN": 20, "CASTER": 10, "LEECH": 5, "REAVER": 5 },
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
| **PostgreSQL** | Player profiles, inventory, gear instances, pets, mounts, talents, presets, **active run snapshots**, player messages (`28` A), PvP ghosts, ratings, ladder, seasons, entitlements, idempotency outcomes, an append-only economy event log | 🔒 **The system of record for everything, per command** — snapshot + idempotency outcome + events commit in one transaction (§16.4). Row-level JSONB for the flexible parts (inventory, talents), typed columns for the queryable parts (rating, level, chapter). Reached only via `IPlayerRepository`, `IRunStateStore`, `IIdempotencyStore`, `IMessageRepository`, `IGhostRepository`, `ILeaderboardRepository`. |
| **Redis** | Hot run state, sessions, idempotency hot-cache, matchmaking candidate cache, rate limits | 🔒 **Rebuildable cache only, never a system of record** (ruled in `16` A7 — supersedes "written through on stage gates"). Populated after/through the Postgres commit (§16.4); on loss or inconsistency, rebuilt from Postgres. Flushing Redis costs latency, never progress. Run-state cache TTL 48 h. Reached only via `IRunStateStore` and `IIdempotencyStore`. |
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
| Upgrade to a real account | Sign in with Google / Apple, linking the anonymous account. 🔒 **Prompted at Legend Level 10 and 30, then a dismissible fortnightly banner — never blocking.** Full spec, including the one-time link reward and provider-uniqueness enforcement: `28_LIVE_SERVICE_ESSENTIALS.md` Part B. |
| **Sign-in conflict** 🔒 | If the signed-in identity already owns an account and this device's anonymous account has meaningful progress, the client **must** show a side-by-side comparison and require an explicit, typed confirmation. Never resolve silently in either direction. The discarded account is soft-deleted with a 30-day support-recoverable window. `28` B4. |
| Multiple providers | One account may link both Google and Apple. This is the cross-platform path. |
| Unlinking | ❌ Not offered — it exists only to enable account selling and unanswerable support tickets. Deletion is the exit. |
| Abandoned anonymous accounts | Purged after 180 days with no session. ⚠️ They cannot be warned; no contact channel exists. That is exactly the problem the prompts above solve. |
| Cross-device | Automatic and instant — the profile has always lived on the server. No cloud-save provider is needed. ✅ This closes the old open question about save sync. |
| Deletion | GDPR-compliant account deletion endpoint, hard delete within 30 days |

---

## 8. Determinism and RNG

🔒 **One RNG model, project-wide: counter-based streams over a committed seed** (ruled in `16` A7). Every random draw the game ever makes is a *pure function* of `(seed, streamName, drawIndex)`. There is no stateful generator — nothing evolves, so there is no PRNG state to persist, snapshot or restore. The earlier xoshiro256\*\* generator and its `SaveState()`/`RestoreState()` pair are **removed**: 256 bits of generator state could never round-trip through the wire's 64-bit stream positions, and random access is precisely what replay wants anyway.

### 8.0 `Hash64` — the one pinned hash 🔒

`Hash64` is **xxHash64** (XXH64, hash-seed parameter `0`) over the canonical byte encoding of its arguments, concatenated in argument order:

| Argument type | Encoding |
|---|---|
| `ulong` / `long` / `int` / enum (widened to 64 bits; ints sign-extended) | 8 bytes, little-endian |
| `string` | 4-byte little-endian byte count, then the UTF-8 bytes |

One implementation, in `Core/Rng/`, used for **both** seed derivation (`runSeed` — `02` §2; `battleSeed` — §8.1) and every draw. The determinism CI (§8.2) pins **reference vectors** for it: a fixed table of `(inputs → hash)` rows, generated once at first implementation, committed to the repository, and asserted byte-identical on every target platform.

```csharp
public sealed class DeterministicRng {
    // Draw i of stream s over seed r  =  Hash64(r, s, i). Stateless but for the counter.
    public DeterministicRng(ulong seed, string streamName, ulong position = 0);

    public ulong  Position { get; }   // the NEXT draw index. This is the entire persistable state.

    public uint   NextUInt();         // top 32 bits of the draw
    public double NextDouble();       // [0,1): (draw >> 11) * 2^-53 — the standard 53-bit construction
    public int    Range(int minInclusive, int maxExclusive);
                                      // min + (int)(draw % (ulong)(max - min));
                                      // modulo bias < range / 2^64 — accepted
    public T      WeightedPick<T>(IReadOnlyList<(T item, double weight)> table);
                                      // ONE draw: x = unit interval × Σ weights; walk the table
                                      // in order; first item whose cumulative weight exceeds x
}
```

🔒 **Every call consumes exactly one draw index.** `WeightedPick` is one draw, not two. This is what makes the persisted counter meaningful and auditable: `Position` equals the number of calls ever made on that stream.

### 8.1 Seed streams

One run seed spawns independent named streams so consuming randomness in one system never shifts another. 🔒 **The persisted stream position IS the draw counter.** The `"rngStreamStates": { "dice": 12, "board": 8 }` in §2.3's wire example literally means *"12 draws consumed from `dice`, 8 from `board`"*, and rehydrating a stream is nothing more than `new DeterministicRng(runSeed, name, position)`. There is nothing else to restore.

| Stream | Used by |
|---|---|
| `board` | Board layout generation; Portal jump draws (`03` §1.1) |
| `dice` | Die rolls and the fair-bag weights |
| `draft` | Perk draft options |
| `drops` | Gear, currency and material drops |
| `treasure` | Treasure-tile payout profile draws (`03` §7a.3) |
| `shrine` | Shrine option draws (`03` §7a.5) |
| `combat` | Per battle, **re-rooted at `battleSeed`** — see the Battles rule below, which is the sole derivation |
| `events` | Event card outcomes |
| `forge` | *(M4-04)* A fusion's affix re-roll and an enhancement attempt (`08` §4.1, §4.2). Drawn only in the meta regime, so it never appears in a run's `rngStreamStates` |
| `minigame:{index}` | Minigame randomisation |

This table is the complete stream registry — a system that needs randomness draws from one of these streams or gets a new row here.

Three rules complete the model:

| Rule | Specification |
|---|---|
| **Run draws** | Draw `i` of stream `s` is `Hash64(runSeed, s, i)`. The `Run` aggregate holds `runSeed` plus the per-stream counters; both are authoritative run state, and the counters are echoed to the client with every outcome (§2.3). Counter-based draws are randomly accessible: a revive replay, a resync or a bug-report reproduction re-derives any draw without replaying the ones before it. |
| **Battles** | `battleSeed = Hash64(runSeed, "combat", battleIndex)`, and combat draw `i` of that battle is `Hash64(battleSeed, "combat", i)`. The client receives `battleSeed` and simulates the identical fight (§2.4) **without ever holding `runSeed`** — which never leaves the server (`02` §2). A revived battle restarts from draw 0 of the same battle stream: reproducible by construction. |
| **Meta commands** | Out-of-run draws — wheel spins, container opens, the `BEGIN_SESSION` quest draw — use the server-issued per-command seed: draw `i` is `Hash64(CommandSeed, s, i)` with `i` starting at 0 for each command and **no persisted counter**. The command is atomic, and idempotency (§16.3) replays its stored outcome, so a meta draw can never be re-rolled by resubmission. `CommandSeed` lives on `GameContext` and is generated by the server host, never by the domain (`30` §3). |

🔒 **Never use `System.Random`, `GD.Randi()`, `Random.Shared`, `DateTime.Now`, `Guid.NewGuid()` or `Environment.TickCount` anywhere in `SlayIdleRepeat.Core` or `SlayIdleRepeat.Application`.** CI greps for these and fails.

Time and identity are **ports**, not ambient state: `IClockPort` and `IIdGeneratorPort` (`23` §4.3). Tests supply a frozen clock and a counting ID generator, which is what makes the reconnect chaos test (§13) and the economy simulator (doc 21) reproducible.

Game randomness is deliberately **not** a port — the stream algebra lives in `Core/Rng` and is pure arithmetic; every seed it consumes is either committed run state (`02` §2) or a server-issued `CommandSeed` (`30` §3). xxHash64 is not cryptographic and does not need to be: unpredictability comes from the server keeping `runSeed`, not from the hash.

### 8.2 Floating point 🔒

**Rounded doubles plus a cross-platform CI hash test.**

- All combat math uses `double`.
- Avoid `Math.Pow` in hot paths; use explicit multiplication.
- **Round to 4 decimal places (`Math.Round(x, 4)`) at every accumulation point** — after each damage calculation, each heal, each stat aggregation step.
- CI runs a determinism test on every commit: simulate 10,000 fixed `(seed, build, enemy)` triples on **Linux x64 (server) and Android ARM64**, compare `LogHash` values, fail on any divergence. ⚠️ The **iOS ARM64** leg is authored and gated off with iOS itself (`16` D34); it is the first thing to re-enable if iOS returns, because NativeAOT is a *different runtime* from the Mono/CoreCLR path the other two legs exercise.
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
| Skill minigames 🔒 | **Documented exception:** `MG_TIMING_BAR` and `MG_MEMORY_RUNE` outcomes are client-asserted (`03` §6.2). The server validates legality only (valid tier, one submission per tile, rate limits). Accepted because rewards are small, capped and run-local. |

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

The two most important derived metrics: **perk pick rate by perk id** (balance) and **run abandonment point by tile index** (pacing). A third is now available for free: **disconnect rate by region and screen**, which tells you whether the online requirement is hurting you. A fourth is mandatory per risk R13: **season rating drift** — median and p90 ladder rating per season, tracking the inflation inherent in one-sided Elo (`11` §5.1).

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

Server techniques: hot run state cached in Redis (Postgres-authoritative per command, §16.4) so the API is stateless; battle logs written asynchronously to object storage; content cached in memory; connection pooling to Postgres.

---

## 12. Third-party integration

| Concern | Approach |
|---|---|
| Ads | **AppLovin MAX** via the **official MIT-licensed Godot 4 plugin** (<https://github.com/AppLovin/AppLovin-MAX-Godot>). The plugin is GDScript-only, so a GDScript autoload shim plus `AppLovinRewardedAdAdapter : IRewardedAdPort` is needed — **3–5 engineering days**, not the 2–3 weeks a native bridge would have cost. Everything AppLovin-specific lives in `Adapters.Ads.AppLovin`; the game sees only the port. See `12` §3 and `23` §8. **Note:** the Android and iOS builds require the custom export-template path (Gradle + Java 17; CocoaPods + Xcode), so CI must build ads through that path from day one. |
| Subscription | Google Play Billing and StoreKit 2 behind `IBillingPort`, implemented by `Adapters.Billing.GooglePlay` and `Adapters.Billing.StoreKit`, selected per platform at the composition root. **Server-to-server notifications** are the authoritative entitlement source, consumed via `IStoreSubscriptionPort` on the server. |
| Push notifications | Energy-full, season-ending, event start (`26` §8), guild boss expiry (`27` §11) and material compensation (`28` A5) only. Opt-in, max 1/day. ⚠️ **NEEDS DETAIL:** provider not chosen — recommend FCM + APNs directly rather than a wrapper service, to stay lock-in-free. |
| Auth | Anonymous device accounts, upgradeable via Sign in with Google / Apple. Tokens are our own JWTs; the identity providers are only used for the initial assertion. 🔒 The full scheme — keystore-held device secret, access/refresh JWT lifetimes, silent mid-run renewal, WebSocket auth — is normative in §16.5 (ruled in `16` A7). |

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
- **Server release:** container image to a registry, rolling deploy, database migrations run as a pre-deploy job. The server must tolerate one version of client skew in both directions — enforced on the wire by the `protocolVersion` field (§16.1).
- **Client release:** **Android AAB**, staged rollout starting at 5%. ⚠️ **iOS IPA is post-launch** (`16` D34) — the `ios-export` CI job stays authored and gated off so a reopen is a one-line change, not a rebuild.
- **Kill switches:** remote config flags for PvP, each ad placement, the Plus offer, and each chapter — so a bad content change is a config edit, not a client patch.

---

## 15. Cost and complexity honesty

⚠️ **Worth stating plainly:** choosing full server authority for PvE roughly **doubles the engineering scope** of this project versus an offline-first client with a thin PvP service. You get, in exchange: unspoofable progression, a completely fair ladder, instant cross-device play, no save-file security work, complete server-side analytics, remote balance tuning without app updates, and airtight ad-reward and subscription integrity.

Those are real benefits and they line up well with a game whose entire pitch is fairness. But the trade is a permanent operational burden — you now run a service, not just ship an app — and the game does not work on a plane. Both consequences should be accepted deliberately, not discovered later.

---

## 16. Wire & lifecycle appendix (normative) 🔒

*(Authored per the `16` A7 wire/lifecycle ruling, required before build step 4. This appendix is the single source of truth for the envelope and rejection contract, sequencing and idempotency scope, the commit rule, auth, and the `stateHash` serialisation contract. `30` §2/§4 and `23` §4 point here and must not restate it.)*

### 16.1 The envelope and protocol version

Every command request and response carries `protocolVersion` — an integer, currently **1**. It versions the *envelope and lifecycle semantics* of this appendix, nothing else: content changes ride the content hash (§6) and never bump it; command additions ride the registry (§2.3) and never bump it.

🔒 **Skew rule:** the server accepts its own version `N` and `N−1` — the wire enforcement of §14's "one version of client skew in both directions". Anything outside that window is rejected with `PROTOCOL_VERSION_UNSUPPORTED`, and the client shows the forced-update flow (`16` O32).

### 16.2 Rejection — the `RejectionReason` enum and the HTTP mapping

🔒 **A rejection is a successful protocol exchange whose answer is no. It rides HTTP 200 in a rejection envelope. HTTP status codes are reserved for transport failures.** The distinction is load-bearing: a 200-rejection means the server understood, decided, and recorded; a transport failure means the conversation itself broke and the client should retry the same `commandId`.

```
→ 200
{
  "protocolVersion": 1,
  "sequence": 47,
  "rejected": true,
  "reason": "INSUFFICIENT_ENERGY",
  "detail": { "required": 20, "available": 12 },   // reason-specific, optional
  "stateHash": "fnv1a:a91f..."                     // hash of the UNCHANGED state
}
```

`stateHash` is present even on a rejection — it hashes the untouched state, so a client whose mirror disagrees resyncs even on a "no".

Two tiers produce the enum. **Transport-tier** values are produced by the server host / Application layer and never reach `GameRules.Apply` (`30` §8: the domain sees each command exactly once, and only well-formed ones). **Domain-tier** values are returned by `Apply` as `CommandResult.Rejection` (`30` §2).

| Value | Tier | Meaning |
|---|---|---|
| `MALFORMED_COMMAND` | transport | Envelope parsed, but the command failed schema validation |
| `UNKNOWN_COMMAND_TYPE` | transport | `type` is not in the §2.3 registry |
| `PROTOCOL_VERSION_UNSUPPORTED` | transport | Outside the `{N, N−1}` window (§16.1) |
| `CONTENT_VERSION_MISMATCH` | transport | Client content hash does not match the `ContentSnapshot` version pinned for this run/session (§6) |
| `SEQUENCE_GAP` | transport | `sequence` is ahead of expected — the client must resync (`GET /run/{id}/state`) |
| `SEQUENCE_STALE` | transport | `sequence` already processed, under a different `commandId` |
| `IDEMPOTENCY_CONFLICT` | transport | Known `commandId` arriving with a different payload or sequence |
| `RATE_LIMITED` | transport | A per-player application-level limit (infrastructure limits use HTTP 429 instead) |
| `FEATURE_DISABLED` | transport | The command's feature is kill-switched (§14) |
| `RUN_NOT_FOUND` | transport | No run state exists for `runId` — never expressed as HTTP 404 |
| `RUN_EXPIRED` | domain | The 48 h run TTL has passed (§16.3) |
| `RUN_ALREADY_ENDED` | domain | A run command on a finished run |
| `ILLEGAL_STATE` | domain | The action is not legal at this point of the state machine — wrong phase, revive already used, illegal merge inputs, fork choice with no fork pending, … |
| `INSUFFICIENT_ENERGY` | domain | |
| `INSUFFICIENT_FUNDS` | domain | Any currency or material shortfall; `detail.currencyId` names it |
| `CAP_REACHED` | domain | A daily/weekly/per-run cap or attempt limit (`12` §4.3) |
| `COOLDOWN_ACTIVE` | domain | e.g. the 12 h Focus cooldown (`24` §5); `detail.availableAtUtc` |
| `NOT_OWNED` | domain | The referenced item / pet / mount / container / message does not exist on this account |
| `NOT_ENTITLED` | domain | A Plus-gated operation without Plus (e.g. preset slot 4+, `09` §2.1) |
| `INVENTORY_FULL` | domain | A grant would exceed capacity and cannot be held (`08` §5; contrast the inbox hold rule, `28` A4) |
| `PREREQUISITE_NOT_CLEARED` | domain | The clear the chapter/tier unlock ladder demands has not happened (`10` §7) — `START_RUN` only |
| `LEGEND_LEVEL_TOO_LOW` | domain | The Legend Level that ladder demands has not been reached (`10` §7, Mythic only) — `START_RUN` only |
| `BATTLE_IN_PROGRESS` | domain | A battle is open and this command would change the hero the server is about to recompute it with (§9) — `EQUIP` / `UNEQUIP` / `MERGE` / `ENHANCE` / `SALVAGE` only |

🔒 **Forward compatibility:** values may be appended, never renamed or reused. A client receiving an unknown value treats it as a generic rejection and resyncs.

**The HTTP mapping:**

| HTTP | Meaning | Client behaviour |
|---|---|---|
| 200 | Accepted outcome, or a rejection envelope | Apply / surface. Never blind-retry a rejection. |
| 400 | Body is not a parseable envelope at all | A client bug; report via telemetry |
| 401 | Missing / expired / invalid access token | Silent token refresh, then retry the **same** `commandId` (§16.5) |
| 403 | Account locked or sanctioned | Show the account-state screen |
| 429 | Infrastructure rate limit (per-IP) | Back off per `Retry-After` |
| 500 | Server fault | Retry with backoff, **same** `commandId` — idempotency makes this safe |
| 503 | Maintenance mode | Maintenance screen; poll the status endpoint (`16` O32) |

🔒 **The retry rule:** transport failures — timeouts, 5xx, 429, a 401 cured by refresh — are retried with the **same** `commandId`, which is exactly what the idempotency store exists for. A 200-rejection is final for that command. `SEQUENCE_GAP`, `SEQUENCE_STALE` and `IDEMPOTENCY_CONFLICT` mean the client's model of the conversation is wrong: resync state, then continue with fresh commands.

### 16.3 Sequence and idempotency scope 🔒

| | Run commands | Meta commands |
|---|---|---|
| Endpoint | `POST /run/{runId}/command` | `POST /player/command` |
| `sequence` | Monotone per run, starting at 1 | Monotone per player — a lifetime counter |
| Idempotency key | `commandId`, scoped to the run | `commandId`, scoped to the player |
| Record TTL | 🔒 **The run-state TTL itself: 48 hours.** A resumable run must be able to replay any of its outcomes, so the two lifetimes are the same by definition — this supersedes the 24 h window formerly stated in §3.2 (ruled in `16` A7). | 48 hours from the command |

Rules:

- The expected `sequence` is exactly `last + 1`. A duplicate `(commandId, sequence, payload)` replays the stored outcome byte-identically. A lower sequence with an unknown `commandId` → `SEQUENCE_STALE`. Higher than expected → `SEQUENCE_GAP`.
- The 48 h run TTL is **sliding**: measured from the last accepted command, so an active run never expires under the player. On expiry the run is closed server-side and settled as a **Death at the stage reached** — banked rewards pay the `02` §5.2 death multiplier for that stage, and gear picked up during the run is kept. *(Default authored with this appendix; friendlier than Abandon's 0.10 and consistent with "death must still pay", `02` §5.2.)*
- `commandId` is a client-generated UUID from `IIdGeneratorPort` (`23` §4.3).

### 16.4 The commit rule — Postgres-authoritative 🔒

One accepted command commits as **one Postgres transaction**, containing all three of:

1. the new aggregate snapshot(s) — `PlayerSnapshot`, plus `RunSnapshot` for run commands (`30` §11.3);
2. the idempotency outcome record — the full response envelope;
3. the appended domain-event rows — the economy event log (§7.1) and the analytics feed (`30` §7).

**The transaction commit is the moment the command happened.** There is no cross-store atomicity problem, because there is only one store that counts.

**Redis is a rebuildable cache, never a system of record:** hot run state for the latency budget (§2.5), sessions, an idempotency hot-cache, rate limiting, matchmaking candidates. It is populated after (or through) the Postgres commit; on any inconsistency or loss it is rebuilt from Postgres. Flushing Redis loses latency, never progress.

This resolves the two-phase-commit concern in `30` §4 by construction: `Run` is a child of `Player`, both snapshots commit in the same transaction, and the idempotency record can never disagree with the state it describes.

Throughput sanity: ~30 commands/run × ~8 runs/DAU × 10k DAU ≈ 2.4M transactions/day ≈ **28/s average** — far inside a single small Postgres (§11). Do not "optimise" this back into Redis-authoritative writes; that trade was examined and refused (ruled in `16` A7).

### 16.5 Auth

| Element | Specification |
|---|---|
| Device credential | On first launch the server issues `{ deviceId, deviceSecret }` — the secret is 256 bits of server-generated randomness. It is stored **only** in the platform keystore (Android Keystore / iOS Keychain): never in `user://`, never in the settings file, never logged. This is the anonymous account's root credential (§7.3). |
| Session issuance | `POST /auth/session { deviceId, deviceSecret }` over TLS → an **access JWT** (lifetime 60 min 📐) and a **refresh token** (opaque, single-use rotating, lifetime 30 days 📐). Reuse of an already-rotated refresh token revokes the whole token family and falls back to device-secret re-auth. |
| Sign-in providers | Google / Apple assertions link an identity to the account (`28` B); session issuance is identical afterwards. Tokens remain our own JWTs (§12) — the providers are only used for the initial assertion. |
| Transport | The access JWT rides `Authorization: Bearer` on every HTTP request. WebSocket connections authenticate **once, at upgrade**, via the same header; when the server requires fresh auth it closes with code `4401` and the client silently reconnects — reconnection is already free (§3). |
| Silent renewal 🔒 | The client renews in the background at ~80% 📐 of the access-token lifetime. On a 401: one refresh, then retry the **same** `commandId`. If the refresh fails: re-auth with the device secret. Only if that also fails does the player see anything — the reconnecting pill, then the account-recovery path (`28` B). **A mid-run token expiry must never be player-visible and can never lose progress:** the run has a 48 h TTL and every command is idempotent (§16.3). |
| Config home | Token lifetimes are server-operations numbers: environment configuration (§1.1), with the defaults above. They are deliberately **not** in `game-data/tuning/` — they are not economy tunables and must never ride a content push. |

### 16.6 The `stateHash` serialisation contract 🔒

`stateHash` — and the battle `LogHash` (§8.2, §9), and the parity test's hashes (§13) — are produced by **one** canonical writer, `CanonicalStateWriter`, in `Core`. A second serialiser producing "almost the same bytes" is how parity tests rot; there is exactly one.

| Rule | Specification |
|---|---|
| Input | The public snapshot DTOs (`30` §11.3). Run commands hash `PlayerSnapshot` then `RunSnapshot`, concatenated; meta commands hash `PlayerSnapshot` alone. |
| Algorithm | **FNV-1a, 64-bit**, over the canonical bytes. Wire form: `"fnv1a:"` + 16 lowercase hex characters. The prefix names the algorithm so it can only ever be rotated deliberately and visibly. |
| Field order | Declaration order of the snapshot record, depth-first. 🔒 Adding, removing or reordering a field is a serialisation change: it bumps `SchemaVersion` and is handled as a versioned migration, never silently. A CI test pins the field list per `SchemaVersion`. |
| Collections | Lists in stored order. Every dictionary/map in ascending key order — ordinal for strings, numeric for numeric ids. No unordered container is ever hashed as-is. |
| Scalars | Integers: 8 bytes little-endian (widened). Booleans: 1 byte. Enums: their numeric value, 8 bytes. Strings: 4-byte little-endian byte count + UTF-8 bytes. Timestamps: Unix milliseconds UTC, 8 bytes. Optionals: a presence byte `0x00`/`0x01`, then the value. |
| Doubles | Per the determinism rules (§8.2), every double in persisted state is already rounded to 4 dp at its accumulation point. The writer encodes the IEEE-754 bit pattern (8 bytes, little-endian) of that rounded value, and asserts `Math.Round(x, 4) == x` in debug builds. NaN and infinities are forbidden in state; CI fails on either. |
| Consumers | The per-command `stateHash` (§2.3, §16.2), client-mirror verification (§2.4), the parity and reconnect chaos tests (§13), the determinism CI (§8.2). |
