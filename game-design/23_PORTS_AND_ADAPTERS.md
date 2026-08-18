# 23 — Ports & Adapters (Hexagonal Architecture)

🔒 **D22 — LOCKED, PROJECT-WIDE.**

> **Every external dependency is accessed through a port — a C# interface owned by the application — and implemented by a concrete adapter in a separate project. The game never references a vendor SDK, a database driver, or an engine API directly.**

This applies to **all** external dependencies, on the client and the server: ad networks, billing, push, crash reporting, analytics, the HTTP API, the database, the cache, object storage, store server APIs, remote config, the filesystem, the clock — and Godot itself.

AppLovin was the example that prompted this rule. It is not the scope of it.

---

## 1. Why this rule earns its cost

Slay Idle Repeat has an unusually strong case for hexagonal architecture, and it is worth being explicit about why — so that nobody later "simplifies" it away.

| Reason | Concretely |
|---|---|
| **The rules library is shared between client and server** | `SlayIdleRepeat.Core` runs in Godot *and* in a Linux container. Anything it touched that was platform-specific would break one of the two. Ports are what make that sharing possible at all. |
| **No vendor lock-in is already a locked decision (D12)** | A rule that says "portable across hyperscalers and self-hosting" is unenforceable if `NpgsqlConnection` and an S3 client are scattered through the application. Ports are the mechanism that makes D12 true rather than aspirational. |
| **Every external dependency here is genuinely swappable** | Ad network, analytics, crash reporting, push transport, object store and even the database are all "current best choice", not permanent commitments. |
| **The economy simulator and balance harness need the whole game without any of the I/O** | Doc 21's simulator runs 180 days × 14 profiles headless. ✅ **After `30_DOMAIN_MODEL.md` it needs no adapters at all** — the whole game is playable from `SlayIdleRepeat.Core` alone via `InMemoryGame` (`30` §6). Ports remain what keeps everything *else* out of that assembly. |
| **Determinism is a hard requirement** | The clock and randomness are external dependencies too. Making them ports is what makes `LogHash` reproducible across x64 and ARM64 (`14` §8). |
| **Godot's SDK story is weak** | The MAX plugin is GDScript-only; billing needs a bridge; there is no first-party push. All of these are messy at the edge. Ports keep the mess in one small, isolated, individually-testable project each. |

---

## 2. The layering

```
                         ┌───────────────────────────────────────┐
   DRIVING ADAPTERS      │                                       │      DRIVEN ADAPTERS
   (call into the app)   │            APPLICATION                │   (the app calls out)
                         │   use cases + PORT INTERFACES         │
  HTTP endpoints ───────▶│                                       │──────▶ Postgres adapter
  WebSocket hub ────────▶│   ┌───────────────────────────────┐   │──────▶ Redis adapter
  Store webhooks ───────▶│   │            CORE               │   │──────▶ S3 adapter
  MAX S2S callback ─────▶│   │  pure rules: combat, board,   │   │──────▶ AppLovin adapter
  Godot presenters ─────▶│   │  dice, stats, effects, econ   │   │──────▶ Billing adapters
  CLI tools ────────────▶│   │  NO I/O, NO TIME, NO RANDOM   │   │──────▶ Sentry adapter
  Cron jobs ────────────▶│   └───────────────────────────────┘   │──────▶ PostHog adapter
  Economy simulator ────▶│                                       │──────▶ FCM/APNs adapter
                         └───────────────────────────────────────┘
```

### 2.0a Where the seam actually falls 🔒

`Application` is **not** "the use-case layer" in the conventional Clean Architecture sense. The seam here is **I/O, not use case** (`30` §11.1):

| Kind | Example | Where |
|---|---|---|
| **Decision** — *"the player rolled a 4; where do they land, what does it pay, which pity counters advance?"* | needs no port | 🔒 **`Core`** |
| **Choreography** — *"load from Postgres, check idempotency, call the decision, persist, publish, return a delta"* | needs ports | `Application` |

Drawing it the conventional way would put the deciding logic in a second assembly and gain nothing but a mapping layer between two layers that share a vocabulary — the "mapping fatigue" failure mode in §9. It would also make `30` §9's `The_whole_game_is_playable_from_Core_alone` unachievable, since `InMemoryGame` would then need `Application`.

Keeping both in `Core` also buys the real prize: **`internal` becomes a compiler-enforced boundary**, so `GameRules.Apply` is the only public way to change state anywhere in the codebase (`30` §11.2).

### 2.1 The dependency rule 🔒

**Dependencies point inward. Always.**

```
Adapters ──▶ Application ──▶ Core ──▶ (nothing)
```

- `Core` references nothing but the .NET BCL. No Godot, no ASP.NET, no drivers, no clock, no RNG source. Internally it layers `Handlers → Rules → Model → Content → Primitives` (`30` §11.4), enforced by namespace-level architecture tests.
- `Application` references `Core` and `Contracts`. It **defines** every port. It references no adapter, ever. It contains **no game rules** — its use cases load a slice, call `GameRules.Apply`, persist, and dispatch events (`30` §11.1).
- `Adapters.*` reference `Application` (to implement its ports) and whatever vendor package they wrap. **Adapters never reference each other.**
- **Composition roots** (`SlayIdleRepeat.Server`, `SlayIdleRepeat.Client`) are the only projects that reference concrete adapters. They exist to wire things up and do nothing else.

### 2.2 Where ports live

Ports are **owned by the application, not by the adapter**. `IRewardedAdPort` lives in `SlayIdleRepeat.Application/Ports/`, not in the AppLovin project. This is the whole point: the application states what it needs in its own language, and vendors conform to it.

---

## 3. Solution structure

```
SlayIdleRepeat.sln
│
├── src/
│   ├── SlayIdleRepeat.Core/                      # THE WHOLE GAME. Zero dependencies.
│   │   │                                         #   Full anatomy: 30_DOMAIN_MODEL.md §11.
│   │   ├── Primitives/ Content/ Rng/        #   ids, ContentSnapshot, DeterministicRng
│   │   ├── Model/                           #   AGGREGATES — public getters, internal ctors
│   │   │   └── Snapshots/                   #     public persistence DTOs + Rehydrate()
│   │   ├── Rules/                           #   INTERNAL calculators: combat, board, dice,
│   │   │                                    #     stats, effects (18), luck (24), economy.
│   │   │                                    #     Public only: CombatSimulator, PowerCalculator
│   │   ├── Commands/ Events/                #   public — the input and output vocabulary
│   │   ├── Handlers/                        #   INTERNAL — the services that steer the model
│   │   ├── GameRules.cs                     #   🔒 public Apply() — the ONLY public mutation
│   │   └── Testing/InMemoryGame.cs          #   🔒 the game, playable with NO other assembly
│   ├── SlayIdleRepeat.Contracts/                 # WIRE ENVELOPES ONLY — commandId, sequence,
│   │                                             #   stateHash, error shapes. 🔒 Never re-declares
│   │                                             #   a command, event or domain type (30 §11.6).
│   ├── SlayIdleRepeat.Application/               # I/O choreography + ALL port interfaces
│   │   ├── Ports/
│   │   │   ├── Client/                      #   IRewardedAdPort, IBillingPort, …
│   │   │   ├── Server/                      #   IPlayerRepository, IRunStateStore, …
│   │   │   └── Shared/                      #   IClockPort, IIdGeneratorPort, …
│   │   ├── UseCases/                        #   orchestration ONLY: load slice → GameRules.Apply()
│   │   │                                    #   → persist → dispatch events. No game rules. (30 §11)
│   │   └── Services/                        #   choreography over ports
│   │
│   ├── adapters/
│   │   ├── client/
│   │   │   ├── SlayIdleRepeat.Adapters.Ads.AppLovin/        # the GDScript shim + IRewardedAdPort
│   │   │   ├── SlayIdleRepeat.Adapters.Ads.AutoGrant/       # Plus subscribers — no ad, instant grant
│   │   │   ├── SlayIdleRepeat.Adapters.Billing.GooglePlay/
│   │   │   ├── SlayIdleRepeat.Adapters.Billing.StoreKit/
│   │   │   ├── SlayIdleRepeat.Adapters.Api.Http/            # IGameApiPort over HTTPS + WebSocket
│   │   │   ├── SlayIdleRepeat.Adapters.Cache.LocalFile/
│   │   │   ├── SlayIdleRepeat.Adapters.Push.Firebase/
│   │   │   ├── SlayIdleRepeat.Adapters.Telemetry.Sentry/
│   │   │   ├── SlayIdleRepeat.Adapters.Consent.AppLovinCmp/
│   │   │   ├── SlayIdleRepeat.Adapters.Platform.Godot/      # ENGINE CAPABILITIES — audio, haptics,
│   │   │   │                                                #   engine paths. Implements NO port (§7.2a)
│   │   │   └── SlayIdleRepeat.Adapters.Platform.Host/       # the plain-C# sibling that DOES:
│   │   │                                                    #   IPlatformInfoPort over the BCL
│   │   │
│   │   ├── server/
│   │   │   ├── SlayIdleRepeat.Adapters.Persistence.Postgres/
│   │   │   ├── SlayIdleRepeat.Adapters.Cache.Redis/
│   │   │   ├── SlayIdleRepeat.Adapters.ObjectStore.S3/
│   │   │   ├── SlayIdleRepeat.Adapters.Store.GooglePlayServer/
│   │   │   ├── SlayIdleRepeat.Adapters.Store.AppStoreServer/
│   │   │   ├── SlayIdleRepeat.Adapters.AdVerify.AppLovinS2S/
│   │   │   ├── SlayIdleRepeat.Adapters.Push.FcmApns/
│   │   │   ├── SlayIdleRepeat.Adapters.Analytics.PostHog/
│   │   │   ├── SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry/
│   │   │   └── SlayIdleRepeat.Adapters.Config.HttpJson/
│   │   │
│   │   └── fakes/
│   │       └── SlayIdleRepeat.Adapters.InMemory/            # a fake for EVERY port. Ships in tests only.
│   │
│   ├── SlayIdleRepeat.Server/                    # COMPOSITION ROOT — ASP.NET Core host
│   │   ├── Endpoints/                       #   driving adapters: REST, WS, webhooks
│   │   ├── Composition/                     #   the only DI registration in the codebase
│   │   └── Program.cs
│   │
│   └── SlayIdleRepeat.Client/                    # COMPOSITION ROOT — the Godot project
│       ├── game/scenes/                     #   driving adapters: Godot scenes + presenters
│       ├── Composition/                     #   platform-conditional adapter wiring
│       └── addons/                          #   vendor GDScript plugins live here, wrapped
│
├── game-data/                          # shared JSON content, embedded in client and server
│
├── tools/
│   ├── BalanceHarness/                      # mass battle simulation over Core
│   └── EconomySim/                          # doc 21 — a thin wrapper over InMemoryGame;
│                                            #   references SlayIdleRepeat.Core ONLY (30 §6)
│
└── tests/
    ├── SlayIdleRepeat.Core.Tests/
    ├── SlayIdleRepeat.Application.Tests/         # uses InMemory adapters exclusively
    ├── SlayIdleRepeat.Architecture.Tests/        # enforces §6. Fails the build on violation.
    └── SlayIdleRepeat.Contract.Tests/            # one suite per port, run against EVERY adapter
```

🔒 **There is no integration or end-to-end test tier, and none is to be added.** Every suite
above is fast and dependency-free: no container, no live database, no real ASP.NET host, no
Godot runtime. A behaviour that seems to need one is tested at the unit tier — the use case
against the in-memory fake for whatever port it needs. If that genuinely cannot express it,
the gap is named and left open; it is never closed by standing up infrastructure.

---

## 4. Port catalogue

Every port below is an interface in `SlayIdleRepeat.Application/Ports/`. Signatures are indicative but the **shape** — domain language, no vendor types, async where I/O happens — is mandatory.

### 4.1 Client ports

```csharp
// ---- Ads -------------------------------------------------------------
public interface IRewardedAdPort {
    bool IsReady(AdPlacementId placement);
    Task<AdOutcome> ShowAsync(AdPlacementId placement, CancellationToken ct);
    Task PreloadAsync(AdPlacementId placement, CancellationToken ct);
}
public interface IInterstitialAdPort {
    bool IsReady();
    Task ShowAsync(CancellationToken ct);
}
public readonly record struct AdOutcome(AdResultKind Kind, string? VerificationToken);
public enum AdResultKind { Completed, Dismissed, NoFill, Error }

// ---- Billing / entitlement -------------------------------------------
public interface IBillingPort {
    Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken ct);
    Task<PurchaseAttempt> PurchaseAsync(ProductId id, CancellationToken ct);
    Task<IReadOnlyList<PurchaseReceipt>> RestoreAsync(CancellationToken ct);
    Task OpenManageSubscriptionsAsync();
}

// ---- Backend ----------------------------------------------------------
public interface IGameApiPort {
    Task<SessionSnapshot> AuthenticateAsync(AuthRequest req, CancellationToken ct);
    Task<CommandOutcome> SendCommandAsync(GameCommand cmd, CancellationToken ct);
    Task<RunState> FetchRunStateAsync(RunId id, int sinceSequence, CancellationToken ct);
}
public interface IRealtimeChannelPort {
    ConnectionState State { get; }
    event Action<ConnectionState> StateChanged;
    event Action<ServerEvent> EventReceived;
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();
}

// ---- Local storage ----------------------------------------------------
public interface ILocalCachePort {
    Task<T?> ReadAsync<T>(string key, CancellationToken ct);
    Task WriteAsync<T>(string key, T value, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

// ---- Platform ---------------------------------------------------------
public interface IPlatformInfoPort {
    string DeviceModel { get; }
    string OsVersion { get; }
    string AppVersion { get; }
    CultureInfo Locale { get; }
    bool IsLowEndDevice { get; }
}
public interface IHapticsPort { void Play(HapticPattern pattern); }
public interface IAudioPort {
    void PlaySfx(SfxId id, float volume = 1f);
    void PlayMusic(MusicId id, float fadeSeconds = 1f);
    void SetBusVolume(AudioBus bus, float linear);
    void DuckForExternalAudio(bool ducked);   // mandatory around ads — see 20 §5
}
public interface IConsentPort {
    Task<ConsentState> RequestAsync(CancellationToken ct);
    ConsentState Current { get; }
}
public interface IPushRegistrationPort {
    Task<PushToken?> RegisterAsync(CancellationToken ct);
    Task UnregisterAsync(CancellationToken ct);
}
```

### 4.2 Server ports

```csharp
// ---- Persistence ------------------------------------------------------
public interface IPlayerRepository {
    Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct);
    Task SaveAsync(PlayerProfile profile, CancellationToken ct);
    Task<PlayerId> CreateAnonymousAsync(DeviceFingerprint fp, CancellationToken ct);
}
public interface IRunStateStore {                     // cache over the Postgres-authoritative row — 14 §16.4
    Task<RunState?> GetAsync(RunId id, CancellationToken ct);
    Task SaveAsync(RunState state, TimeSpan ttl, CancellationToken ct);
    Task DeleteAsync(RunId id, CancellationToken ct);
}
public interface IIdempotencyStore {
    Task<CommandOutcome?> GetRecordedOutcomeAsync(CommandId id, CancellationToken ct);
    Task RecordAsync(CommandId id, CommandOutcome outcome, TimeSpan ttl, CancellationToken ct);
}
public interface IMessageRepository {                 // the inbox — 28 Part A (ruled in 16 A7)
    Task<IReadOnlyList<PlayerMessage>> GetActiveAsync(PlayerId id, CancellationToken ct);
    Task AppendAsync(PlayerMessage message, CancellationToken ct);
    Task MarkClaimedAsync(PlayerId id, IReadOnlyList<MessageId> ids, CancellationToken ct);
    Task<IReadOnlyList<PlayerMessage>> DequeueExpiringAsync(DateTimeOffset asOfUtc, int limit,
                                                            CancellationToken ct);   // nightly auto-grant job, 28 A6
}
public interface IGhostRepository {
    Task UpsertAsync(GhostSnapshot ghost, CancellationToken ct);
    Task<IReadOnlyList<GhostSnapshot>> FindOpponentsAsync(int rating, int count, CancellationToken ct);
}
// ---- Query ports (read models) ----------------------------------------
// 🔒 Per the CQRS split in 30 §12: query ports return VIEW MODELS, never aggregates,
//    never mutate, contain no game rules (30 §12.5 Q2), and each declares its
//    staleness budget. The player's OWN state is never served through one.
public interface ILeaderboardRepository {
    Task<LeaderboardPage> GetTopAsync(int count, CancellationToken ct);
    Task<LeaderboardPage> GetAroundAsync(PlayerId id, int radius, CancellationToken ct);
    Task<int> GetRankAsync(PlayerId id, CancellationToken ct);
    Task<LeaderboardPage> SearchByNameAsync(string query, CancellationToken ct);
}
public interface IBattleLogStore {                    // S3-compatible
    Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> compressed, CancellationToken ct);
    Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct);
}
public interface IUnitOfWork { Task CommitAsync(CancellationToken ct); }
```

🔒 **The commit rule** (`14` §16.4, ruled in `16` A7): one accepted command = one Postgres transaction containing the aggregate snapshot(s), the idempotency outcome and the appended domain events. `IUnitOfWork` spans exactly that transaction. `IRunStateStore` and the Redis side of `IIdempotencyStore` are rebuildable caches behind it — never a second system of record.

```csharp
// ---- External services ------------------------------------------------
public interface IStoreSubscriptionPort {             // Google Play + App Store server APIs
    Task<SubscriptionStatus> VerifyAsync(StoreReceipt receipt, CancellationToken ct);
    Task<SubscriptionStatus> RefreshAsync(SubscriptionRef sub, CancellationToken ct);
}
public interface IAdRewardVerificationPort {          // MAX S2S callback validation
    bool TryValidate(AdCallbackPayload payload, out AdRewardGrant grant);
}
public interface IPushSenderPort {
    Task SendAsync(PushToken token, PushMessage message, CancellationToken ct);
}
public interface IAnalyticsSinkPort {
    void Track(PlayerId player, AnalyticsEvent evt);   // fire-and-forget, buffered
}
public interface ITelemetryPort {
    void RecordException(Exception ex, IReadOnlyDictionary<string, string>? context = null);
    IDisposable BeginSpan(string name);
    void RecordMetric(string name, double value, params (string Key, string Value)[] tags);
}
public interface IRemoteConfigPort {
    T Get<T>(string key, T fallback);
    bool IsFeatureEnabled(FeatureFlag flag);
    Task RefreshAsync(CancellationToken ct);
}
```

### 4.3 Shared ports (both sides) — the determinism-critical ones

```csharp
public interface IClockPort {
    DateTimeOffset UtcNow { get; }
}
public interface IIdGeneratorPort {
    Guid NewGuid();
    string NewCommandId();
}
```

🔒 **These two are the reason `Core` can be deterministic.** `DateTime.Now`, `Guid.NewGuid()` and `Random` are **banned outright** in `Core` and `Application` (`14` §8.1); the clock and ID generation are injected. Tests supply a frozen clock and a counting ID generator, which is what makes the reconnect chaos test and the economy simulator reproducible.

⚠️ **`IClockPort` is never injected into `Core`.** The **composition root** calls it and passes the answer as `GameContext.NowUtc` (`30` §3). A rule that calls a clock is not pure, and a great many rules here are time-dependent — Energy regeneration, 05:00 UTC resets, event windows (`26` §4), guild weeks (`27` §4), PvP seasons, subscription expiry. An architecture test asserts `IClockPort` does not appear in `Core` at all.

Note that game randomness is **not** a port — `DeterministicRng` lives in `Core` and is pure arithmetic: every seed it consumes is either committed run state (`02` §2) or a server-issued `CommandSeed` on meta commands (`30` §3), because it is part of the rules, not an external dependency.

---

## 5. Adapter rules 🔒

| # | Rule | Rationale |
|---|---|---|
| **A1** | **One adapter project per external dependency.** Never a shared "Infrastructure" project. | A grab-bag project silently re-couples everything and defeats the whole exercise. |
| **A2** | **No vendor type crosses a port boundary.** Not in a parameter, a return type, a generic argument, an exception, or an event payload. | A single leaked `MaxAd` or `NpgsqlException` makes the port decorative. |
| **A3** | **Adapters translate errors.** Vendor exceptions are caught at the adapter edge and mapped to domain results (`AdResultKind.NoFill`) or domain exceptions. | The application must never catch `Npgsql.PostgresException`. |
| **A4** | **Ports are named in domain language, not vendor language.** `IRewardedAdPort`, never `IAppLovinService`. `IBattleLogStore`, never `IS3Client`. | If the port is named after the vendor, it will be shaped after the vendor. |
| **A5** | **Every port has at least two implementations** — the real adapter and an in-memory fake. | Two implementations is the cheapest possible proof that the abstraction is real. A port with one implementation is usually a wrapper pretending to be an abstraction. |
| **A6** | **Adapters contain no business rules.** They map, call, and translate. If an adapter has an `if` about game logic, that logic belongs in `Application`. | |
| **A7** | **Adapters never reference other adapters.** Composition happens only at the root. | |
| **A8** | **Every port is exercised by a shared contract-test suite** run against every one of its implementations, including the fake. | This is what stops the fake and the real adapter drifting apart — the failure mode that makes teams stop trusting their tests. |
| **A9** | **Vendor packages are referenced by exactly one project.** If two `.csproj` files reference the AppLovin plugin, one of them is wrong. | Trivially checkable in CI. |
| **A10** | **Godot is an adapter, not a foundation.** Game logic lives in plain C# classes; scenes and nodes are driving adapters that call use cases and render results. | Also what makes the client logic testable without booting the engine. |

---

## 6. Enforcement 🔒

Rules that are not enforced are suggestions. `SlayIdleRepeat.Architecture.Tests` runs on every commit and **fails the build**.

```csharp
[Fact] public void Core_depends_on_nothing_but_the_BCL() =>
    Types.InAssembly(CoreAssembly)
         .ShouldNot().HaveDependencyOnAny("Godot", "Microsoft.AspNetCore",
                                          "Npgsql", "StackExchange", "Amazon", "Sentry")
         .GetResult().IsSuccessful.Should().BeTrue();

[Fact] public void Application_never_references_an_adapter() =>
    Types.InAssembly(ApplicationAssembly)
         .ShouldNot().HaveDependencyOn("SlayIdleRepeat.Adapters")
         .GetResult().IsSuccessful.Should().BeTrue();

[Fact] public void Adapters_never_reference_each_other() { /* pairwise check */ }

[Fact] public void Every_port_has_at_least_two_implementations() { /* reflection over Ports/ */ }

[Fact] public void No_port_signature_exposes_a_vendor_type() { /* assembly-scope check on
                                                                 parameter and return types */ }

[Fact] public void Core_and_Application_contain_no_ambient_time_or_randomness() =>
    // greps for DateTime.Now/UtcNow, Guid.NewGuid, new Random(), Random.Shared, Environment.TickCount
```

**Six more, from `30_DOMAIN_MODEL.md` §9** — the rules that keep the domain playable in memory:

```csharp
[Fact] public void Domain_is_synchronous()                      // no Task/async/CancellationToken in Core
[Fact] public void Domain_references_no_port_interface()        // Core names nothing from Ports/
[Fact] public void Domain_has_no_clock()                        // IClockPort absent from Core entirely
[Fact] public void Every_command_type_is_handled_by_Apply()     // no silently unhandled GameCommand
[Fact] public void Every_currency_mutation_emits_CurrencyChanged()
[Fact] public void The_whole_game_is_playable_from_Core_alone() //  InMemoryGame's assembly closure
                                                                //  is exactly { Core, System.* }
```

🔒 **The last one is the load-bearing test in the entire codebase.** It is the only thing that will still be enforcing "the domain model is the centrepiece" in eighteen months, when someone is under deadline pressure and a repository reference in a rule would solve their problem in five minutes.

Additional CI checks:
- **Vendor package uniqueness (A9):** parse every `.csproj`; fail if a vendor `PackageReference` appears in more than one project.
- **Composition-root isolation:** only `SlayIdleRepeat.Server` and `SlayIdleRepeat.Client` may reference `SlayIdleRepeat.Adapters.*`.

---

## 7. Composition roots

The only place concrete types are named.

### 7.1 Server

```csharp
// SlayIdleRepeat.Server/Composition/ServiceRegistration.cs
services.AddSingleton<IClockPort, SystemClockAdapter>();
services.AddSingleton<IIdGeneratorPort, GuidIdGeneratorAdapter>();
services.AddScoped<IPlayerRepository, PostgresPlayerRepository>();
services.AddScoped<IRunStateStore, RedisRunStateStore>();
services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
services.AddScoped<IGhostRepository, PostgresGhostRepository>();
services.AddScoped<ILeaderboardRepository, PostgresLeaderboardRepository>();
services.AddSingleton<IBattleLogStore, S3BattleLogStore>();
services.AddSingleton<IStoreSubscriptionPort, CompositeStoreAdapter>();   // Play + App Store
services.AddSingleton<IAdRewardVerificationPort, AppLovinS2SAdapter>();
services.AddSingleton<IPushSenderPort, FcmApnsPushAdapter>();
services.AddSingleton<IAnalyticsSinkPort, PostHogAnalyticsAdapter>();
services.AddSingleton<ITelemetryPort, OpenTelemetryAdapter>();
services.AddSingleton<IRemoteConfigPort, HttpJsonRemoteConfigAdapter>();
```

### 7.2 Client

Selection is **platform-conditional and entitlement-conditional**, and it is the only place that knows the difference:

```csharp
// SlayIdleRepeat.Client/Composition/ClientComposition.cs
container.Register<IGameApiPort, HttpGameApiAdapter>();
container.Register<ILocalCachePort, LocalFileCacheAdapter>();
container.Register<IPlatformInfoPort, HostPlatformInfo>();      // plain C#, NOT a Godot class — see 7.2a
container.Register<ITelemetryPort, SentryTelemetryAdapter>();

// The engine's own capabilities are named here as CONCRETE TYPES, behind no port (7.2a)
var audio   = new GodotAudioOutput();
var haptics = new GodotHaptics();
var paths   = new GodotUserPaths();

#if ANDROID
    container.Register<IBillingPort, GooglePlayBillingAdapter>();
#elif IOS
    container.Register<IBillingPort, StoreKitBillingAdapter>();
#endif

// Ad adapter chosen from the SERVER-ISSUED entitlement, never from a local receipt (12 §2.1)
container.Register<IRewardedAdPort>(_ => session.Entitlements.HasPlus
    ? new AutoGrantAdAdapter(session)          // instant claim, no ad, same daily caps
    : new AppLovinRewardedAdAdapter(maxBridge));
```

This is where the Plus subscription's "no ads, same rewards" promise is implemented — as **an adapter swap**, with no `if (isSubscriber)` anywhere in the game. It is the cleanest possible expression of `12` §1's fairness contract.

### 7.2a A Godot class may not implement a port 🔒

**This section used to register `GodotPlatformInfoAdapter`, `GodotAudioAdapter` and `GodotHapticsAdapter` against their ports. It cannot, and the reason is a measured physical fact rather than a preference.**

Every class in `SlayIdleRepeat.Adapters.Platform.Godot` reaches `GodotSharp`, whose managed API is a shim over native function pointers **the engine populates at startup**. Called from a test process, the first of them marshals a string through a null pointer and raises an `AccessViolationException` that no `catch` block can observe: the test host does not fail, it **dies**, taking every other case in the run with it. This was measured on this repository from a real fixture, not reasoned about.

That collides with §5 A8. `SlayIdleRepeat.Contract.Tests` project-references the engine adapter and demands a contract fixture for **every concrete implementation of a port it can see**, so the moment a class there implements one, the suite asks for the fixture that kills the run. There is no second tier to put such a fixture in: §3 is explicit that this repository has no integration or end-to-end tier and none is to be added. One of the two statements had to give, and **the document is the half that yields** — the engine's behaviour is not negotiable and the test tier is a locked decision.

So, three rules:

| | |
|---|---|
| **A class under `Adapters.Platform.Godot` implements no port.** It is a *capability*: a concrete type the composition root names directly, as above. | §2.1 already makes the composition roots "the only projects that reference concrete adapters". This is that permission used deliberately, rather than by omission. |
| **A port whose only plausible implementation is an engine call is DEFERRED, not implemented.** `IAudioPort` and `IHapticsPort` are both in that state, each with its own further blocker. | A no-op stand-in would satisfy §5 A5's two-implementations rule with two fakes, which is worse than an absent port because it looks built. |
| **Ports are implemented by plain-C# host adapters.** `SlayIdleRepeat.Adapters.Platform.Host` is the shipped precedent: it answers `IPlatformInfoPort` from the BCL, on the desktop *and* on the device, with a real contract fixture beside the in-memory fake. | An adapter that runs anywhere the BCL runs is an adapter the shared suite can actually exercise, which is the whole of §5 A8. |

⚠️ **This is a limitation, not an architecture.** `A10` still holds — Godot is an adapter, not a foundation — and nothing here licenses game logic inside a `Node`. If an engine-capable test host or a sanctioned fixture exemption ever arrives, the honest change is to amend this subsection back, not to work around it: `PortCatalogueTests.No_type_in_the_engine_adapter_implements_a_port` is the rule that will be standing in the way, and it is standing there on purpose.

---

## 8. Worked example: the AppLovin adapter

The original question, answered fully.

```
SlayIdleRepeat.Application/Ports/Client/IRewardedAdPort.cs
    └── the game's entire vocabulary for ads: placement, ready, show, outcome

SlayIdleRepeat.Adapters.Ads.AppLovin/
    ├── AppLovinRewardedAdAdapter.cs   implements IRewardedAdPort
    ├── MaxBridge.cs                   C# wrapper over the GDScript autoload
    ├── addons/applovin_max/           the vendor plugin (GDScript, MIT)
    ├── max_bridge.gd                  autoload: wraps AppLovinMAX, re-emits signals
    └── PlacementMap.cs                AdPlacementId → MAX ad-unit ID

SlayIdleRepeat.Adapters.Ads.AutoGrant/
    └── AutoGrantAdAdapter.cs          implements IRewardedAdPort; returns Completed instantly

SlayIdleRepeat.Adapters.InMemory/
    └── FakeRewardedAdAdapter.cs       scriptable outcomes for tests: Completed/NoFill/Error
```

What this buys, concretely:

- **The game compiles and runs with zero ad SDK present.** The economy simulator, all application tests and CI headless runs use `FakeRewardedAdAdapter`. No Gradle, no CocoaPods, no fill.
- **The Plus behaviour is an adapter, not a branch.** `AutoGrantAdAdapter` is ~20 lines and cannot drift from the fairness contract, because it satisfies the identical interface under the identical caps.
- **The GDScript↔C# shim is quarantined.** It lives in exactly one project (`12` §3.2). If AppLovin ships a C# binding later, or the team switches to AdMob, one project changes and nothing else does.
- **Open item O14 shrinks.** If the plugin turns out not to expose S2S reward callbacks (`12` §3.3), the fallback is implemented behind the same `IRewardedAdPort` — `AdOutcome.VerificationToken` is already in the signature precisely so that either mechanism fits without an application change.

---

## 9. Cost and honest limits

**Cost:** roughly **20–30 extra interfaces and ~20 small projects**, plus the DTO mapping at each boundary. Realistically 1–2 weeks of additional up-front work across the project, and a permanent small tax on adding any new external capability.

**Where it pays back:** the economy simulator and balance harness get the real game for free; the client is testable without booting Godot; the server is testable without Docker; the no-lock-in decision becomes enforceable rather than aspirational; and every vendor swap is a one-project change.

**Where to be careful — the failure modes of this pattern:**

| Failure mode | Guard |
|---|---|
| **Anaemic ports** that mirror a vendor API 1:1 (`IAppLovinPort.LoadAd/ShowAd/OnAdRevenuePaid`) | Rule A4 + designing the port from the *use case* backwards, never from the SDK forwards |
| **Mapping fatigue** — DTO ↔ domain conversion everywhere becoming the dominant code | Keep `Contracts` thin; let adapters map directly to domain types rather than through an intermediate model |
| **Fakes drifting from reality**, so tests pass and production breaks | Rule A8: the shared contract-test suite runs against every implementation including the fake |
| **Over-abstraction** — porting things that are not actually external (the effect DSL, the RNG, board generation) | If it is a *rule*, it belongs in `Core`. Only things that cross a process, network, device or vendor boundary get a port. |
| **Logic leaking into `Application`** — a use case that decides rather than choreographs | `internal` handlers in `Core` (`30` §11.2) make the shortcut a compile error rather than a code-review argument. The test `Apply_is_the_only_public_mutation` is the backstop. |

⚠️ **NEEDS DETAIL:** whether to adopt a DI container in the Godot client (VContainer-style) or hand-roll a small service locator. Godot's node lifecycle does not cooperate well with constructor injection into scenes. Recommendation: a **hand-rolled composition root with explicit factory methods** — presenters receive their ports as constructor arguments from the root; scenes only ever talk to presenters. Avoids a dependency and Godot's lifecycle sharp edges.
