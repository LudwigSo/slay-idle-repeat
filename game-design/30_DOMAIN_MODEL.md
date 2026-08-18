# 30 — The Domain Model

🔒 **Decision D32: the domain model is a pure, synchronous, dependency-free state machine, and the entire game is playable in memory against it alone.**

> *"Having only this should allow playing the game in memory."*

That is the correct instinct and it is already what D22 (`23_PORTS_AND_ADAPTERS.md`) was bought for. But it is not currently **specified** anywhere: `14` §2.3 lists 30 command types, `02` §1.1 draws the run state machine as ASCII art, and `23` §3 mentions a `UseCases/` folder — and no document names an aggregate, defines a transition function, or says what the domain is allowed to know.

Without that, the natural implementation is thirty use-case classes that each load from a repository, mutate, and save. That *works*, and it is still testable — but the rules end up smeared across the Application layer, every test needs the full port set wired, and "play the game in memory" quietly becomes "play the game in memory, once you have constructed eleven fakes."

This document closes that gap.

---

## 1. The assessment: what you had right, and the one correction

| Your claim | Verdict |
|---|---|
| The domain model should be the centrepiece | ✅ Yes, and D22 exists to make it so |
| It should be playable in memory with no external services | ✅ Yes — and `21` §2 already depends on exactly this |
| No storage, no UI, no network needed for verification | ✅ Yes |
| It is **server-side** | ⚠️ **Not quite — it is *shared*, and the server is merely *authoritative*.** See §1.1. |
| Having **only** the domain model is enough | ⚠️ **Not as currently structured.** The rules live in `Core`; the *sequencing* lives in `Application` next to the ports. See §1.2. |

### 1.1 The domain model is shared, not server-side 🔒

`14` §2.4 has the client running the **identical** combat simulation locally from a server-issued seed, and `14` §13 mandates a client/server parity test asserting that 1,000 command sequences produce identical state hashes on both. `11` §6 has the client running the whole PvP duel and the server re-running it to verify.

So the domain model is not a server component. It is a **shared library that both sides execute**, where the server's copy is the one that counts. That distinction is load-bearing, because it forbids the domain from ever touching anything server-only — no repository, no connection, no request context, no server clock. Which is convenient, because §3 forbids all of that anyway.

Calling it "server-side" would, over eighteen months, licence exactly one `IPlayerRepository` parameter to sneak into a rule, and that is the end of the property you are trying to buy.

### 1.2 The correction: the state machine belongs in `Core`, not `Application`

`23` §3 currently splits things like this:

```
Core         combat · board · dice · stats · effects · economy formulas    ← rules
Application  UseCases/ (RollDice, PickPerk, MergeGear…) + ALL ports       ← sequencing + I/O
```

`Core` can compute *"what damage does this attack do"* but cannot answer *"what happens when the player rolls the die"*, because that requires knowing where they are on the board, what tile they land on, whether a fork is pending, and how to advance the run. That knowledge is sequencing, and it currently sits in the same project as the ports — which are async, `Task`-shaped, and I/O-flavoured.

**The fix is small and is a refinement of D22, not a reversal:**

```
Core/Domain/       aggregates + the transition function     ← NEW: the sequencing moves here
Core/Rules/        combat · board · dice · stats · effects  ← unchanged
Application/       ports + orchestration + persistence choreography
```

After this move, **`SlayIdleRepeat.Core` alone is the playable-in-memory unit** — which is the property you asked for, stated as an architecture test rather than an aspiration (§9).

---

## 2. The transition function 🔒

The whole domain reduces to one signature. Everything else in this document is detail about its arguments.

```csharp
public static class GameRules
{
    public static CommandResult Apply(
        WorldSlice   state,     // the aggregates this command may read or write
        GameCommand  command,   // what the player intends
        GameContext  context);  // everything ambient, passed as DATA
}

public readonly record struct CommandResult(
    bool             Accepted,
    RejectionReason? Rejection,     // legal-move failures are VALUES, not exceptions
    WorldSlice       NewState,      // the complete resulting state
    IReadOnlyList<DomainEvent> Events);
```

`RejectionReason` is catalogued normatively in `14` §16.2 (ruled in `16` A7). Two tiers produce it: the **transport tier** — malformed envelopes, sequence and idempotency conflicts, rate limits, protocol/content version — rejects before the domain is ever invoked, so those values never appear in a `CommandResult`; `Apply` returns only the **domain-tier** values (`ILLEGAL_STATE`, `INSUFFICIENT_ENERGY`, `INSUFFICIENT_FUNDS`, `CAP_REACHED`, `COOLDOWN_ACTIVE`, `NOT_OWNED`, `NOT_ENTITLED`, `INVENTORY_FULL`, `RUN_EXPIRED`, `RUN_ALREADY_ENDED`). One enum, one wire field, two producers.

### 2.1 The five properties that make it work 🔒

| # | Property | Why it matters |
|---|---|---|
| **P1** | **Pure.** No I/O, no clock, no ambient randomness, no logging, no statics. Same inputs → same outputs, forever, on every platform. | This *is* the determinism requirement (`14` §8) restated as a type signature. It is what makes `LogHash` comparison, the reconnect chaos test and the parity test possible. |
| **P2** | **Synchronous.** No `Task`, no `async`, no `CancellationToken` anywhere in `Core`. | Async in the domain is always a symptom of hidden I/O. Banning the keyword makes the leak a compile error. |
| **P3** | **Total.** Every command on every state returns a result. Illegal moves return `Rejection`, they do not throw. | Exceptions for expected conditions make the simulator slow and the server's error handling ambiguous. An illegal move is data. |
| **P4** | **Immutable.** `WorldSlice` in, new `WorldSlice` out. No in-place mutation of the input. | Makes replay, rollback, speculation and the client's optimistic prediction (`14` §2.4) trivial rather than dangerous. |
| **P5** | **Complete.** Every rule in every document in this set is reachable from `Apply`. There is no game logic anywhere else. | The moment a second place can change a currency balance, the domain model has stopped being the centrepiece and the economy simulator has stopped being true. |

### 2.2 Why a single entry point rather than thirty use cases

Thirty use-case classes and one `Apply` with a thirty-case switch contain the same logic. The difference is what they make *easy*:

- One entry point means **one place to record a domain event**, so the economy log (`14` §7.1) and the analytics stream (`14` §10.1) fall out of the return value instead of being sprinkled by hand at thirty call sites — where they will eventually be forgotten at one of them.
- One entry point means **one place to enforce a cross-cutting invariant** — pity counters (`24`), caps (`12` §4.3), entitlement (`12` §2.1).
- One entry point means the in-memory harness (§6) is twenty lines rather than a fake for every use case.

Internally `Apply` dispatches to per-command handlers. It is a façade, not a god function.

### 2.3 The day cycle: `BEGIN_SESSION` and lazy catch-up 🔒 *(ruled in `16` A7)*

A pure `Apply` with no scheduled per-player jobs still has to handle two kinds of time: **login-anchored** grants (the calendar advances "on login"; the daily free refill pays on "first login of the day") and **clock-anchored** resets (quest expiry, the wheel's free spin, ad caps and dungeon entries all reset at 05:00 UTC whether or not anyone logs in). One command and one rule cover both.

**`BEGIN_SESSION`** — a meta command (`14` §2.3) the client sends (a) as its first command whenever it establishes a server session, and (b) when it observes the 05:00 UTC game-day boundary while a session is live. Its handler, inside `Apply`:

1. runs the same lazy catch-up as every other command (below);
2. if this is the first `BEGIN_SESSION` of the game day: advances the login calendar (at most once per game day, and only if the open day has been claimed — otherwise the calendar stays paused, per `19` G's *"nothing is skipped or lost"*), grants the daily free Energy refill (`10` §3), draws the day's three quests under the draw rule in `19` B, **and draws the Daily shop tab's 6-offer block under `10` §5.1** — both draws from this command's `CommandSeed`, **the day's draw seed**;
3. otherwise: succeeds as a no-op. Its daily effects are idempotent per game day.

**Lazy catch-up** — the first step of **every** command handler is `AdvanceTime(state, context.NowUtc)`: an internal pure function that rolls the aggregate forward across every reset boundary crossed since `state.LastAppliedAtUtc` — Energy regeneration accrual, the 05:00 UTC daily resets (quest expiry, wheel free-spin, ad caps, dungeon entries, daily shop stock **expiry** — the redraw waits for the day's `BEGIN_SESSION` seed), weekly boundaries, Plus expiry, event-window state. No job, no timer, no clock call: `state + NowUtc → state`. This is what lets `InMemoryGame` (§6) cross 180 day boundaries by advancing `VirtualClock` and sending the next command.

Two boundary notes:

- **Correctness never depends on `BEGIN_SESSION` arriving.** Resets are lazy, so any command triggers them. `BEGIN_SESSION` exists because the login-anchored *grants* need an anchor and the daily draws need a **seed** — the quest slate and the Daily shop tab (`10` §5.1) are the only daily actions that consume randomness, both derive from this one seed, and everything else in catch-up is deterministic arithmetic.
- **What `BEGIN_SESSION` does not do:** claims stay explicit commands (`CLAIM_CALENDAR`, `CLAIM_INBOX`, `CLAIM_QUEST`); inbox expiry auto-grants remain the nightly hosted job (`28` A6); guild settlement remains the scheduled pure function (§5).

---

## 3. `GameContext` — everything ambient, as data 🔒

This is the part that most often gets missed, and it is where "playable in memory" is actually won or lost.

```csharp
public sealed record GameContext(
    DateTimeOffset  NowUtc,           // NOT IClockPort — a value
    ulong?          CommandSeed,      // server-issued; META COMMANDS ONLY — null on run commands (14 §8.1)
    ContentSnapshot Content,          // the loaded, validated, version-stamped data files
    Entitlements    Entitlements,     // { HasPlus, ExpiresAtUtc } — read-only to the domain
    FeatureFlags    Flags);           // remote config, resolved to a plain record
```

| Input | Currently | Must become |
|---|---|---|
| **Time** | `IClockPort` in `Application` (`23` §4.3) | ⚠️ **A value on `GameContext`.** Energy regeneration, daily resets at 05:00 UTC, event windows (`26` §4), guild weeks (`27` §4), PvP seasons (`11` §5.3) and subscription expiry (`12` §2.2) are *all* time-dependent rules. A rule that calls a clock is not pure. `IClockPort` remains — the **composition root** calls it and puts the answer in the context. |
| **Randomness** | `DeterministicRng` in `Core`, seeded externally (`23` §4.3) | ⚠️ **Two regimes** (ruled in `16` A7). **In-run draws never touch the context:** they come from the `Run` aggregate's committed `runSeed` and its persisted per-stream draw counters (`02` §2, `14` §8) — state, not ambience. `CommandSeed` is **reserved for meta commands** — wheel spins, container opens, the `BEGIN_SESSION` quest draw — whose draws are `Hash64(CommandSeed, stream, i)` from `i = 0` (`14` §8.1). *(The earlier wording cited `14` §8.1 in support of a per-command-seed model; that was a mis-citation — §8.1 specifies the run-stream model.)* The invariant that survives, restated accurately: **the domain never invents entropy.** Every draw is a pure function of committed state or a server-supplied context value — `runSeed` itself is derived deterministically inside `Apply` on `START_RUN` from `(playerId, chapter, tier, NowUtc, runCounter)` (`02` §2). |
| **Content** | `game-data`, "embedded in both" (`14` §6) | ⚠️ **An immutable, version-stamped `ContentSnapshot` on the context.** Loading JSON is I/O and belongs in an adapter; *reading* content is a rule. The version stamp is what makes a replayed command reproduce its original outcome after a balance patch. |
| **Entitlement** | Server session payload (`12` §2.1) | ✅ A read-only value. 🔒 **The domain may read `HasPlus` only at the two sites enumerated below — never to alter a stat, a rate or a drop.** An architecture test asserts `Entitlements` is unreachable from the power computation (`29` §3) and from every rule in `Core/Rules/`. |
| **Feature flags** | `IRemoteConfigPort` | ⚠️ Resolved at the composition root into a plain record. The domain must not call a config service mid-rule. |

🔒 **Amendment (M4-10): the entitlement row's enumerated readers are TWO, not one.** It said *"only to
resolve ad-reward auto-grant caps"* and contradicted `14` §16.2, which classifies `NOT_ENTITLED` as a
**domain**-tier rejection — meaning `GameRules.Apply` is the only thing allowed to return it — and
whose sole worked example is *"a Plus-gated operation without Plus (e.g. preset slot 4+, `09` §2.1)"*.
A domain-tier value the domain is forbidden to compute cannot both be true. The licensed sites are:

| Site | Licensed for | Authority |
|---|---|---|
| `Rules/…/AdGrantCapRule` (M15-03) | the ad-reward auto-grant cap | `30` §3, `12` §2 |
| `Handlers/SavePreset` (M4-10) | the preset slot allowance, and nothing else | `14` §16.2, `12` §2 |

The list is closed and enumerated in `IsolationTests.EntitlementReaders`, matched by **exact type
name and exact file path**, with a written licence per entry, and a rule fails when a listed site
stops reading the entitlement — so an exemption cannot outlive what it excused. `APPLY_PRESET`
deliberately does *not* read the entitlement: `12` §2.2 keeps presets beyond the free allowance
**read-only, not deleted**, so only the write path asks about Plus. A third reader is a decision that
belongs in a diff and in this table, not in a widened predicate.

⚠️ **The alternative that would need no exemption, recorded because it was considered and not taken.**
The allowance could be resolved *outside* the domain — a number on the session, composed at the
composition root from `ads.json#/plus/freePresets` for a free player and unbounded for a subscriber —
and `SAVE_PRESET` would then compare a slot against a number without ever naming `HasPlus`. That obeys
this section's own generalisation below more literally. It was not taken because it puts a tuning read
in a composition root that does not exist yet, and because "unbounded" has no representation on the
context that does not collide with this repository's `null`-means-unauthorised convention. **Owner: the
M5 kickoff**, which authors the composition root and can decide it with that root in front of it.

🔒 **The rule that generalises all of this: if a rule needs to know something about the outside world, that something is an argument, not a call.**

---

## 4. Aggregates

"The domain model" is not one object. Naming the boundaries matters, because it determines what can be tested in isolation and — more importantly — what cannot.

| Aggregate | Root | Contains | Concurrency |
|---|---|---|---|
| **`Player`** | `PlayerId` | Profile, Legend Level, all 8 currencies, inventory, **unopened containers** (`24` §4.0), gear instances, pets, mounts, talents, presets, unlocks, FTUE progress (`19` Part D7), Energy + Reserve (`28` C), **all pity counters** (`24`), Feats and Renown (`28` D), daily/weekly counters, ad caps, entitlement | Single writer. One player, one command at a time. |
| **`Run`** | `RunId` | Board, position, HP, run Gold, drafted perks, held consumables and the armed Escape Rope flag (`03` §7.1), pending fork choice (mid-move junction pause, `03` §1.1), RNG stream positions, per-run ad uses, curses | Single writer, owned by exactly one `Player`. Modelled as a **child of `Player`**, not a peer — every run mutation also touches player state (rewards, XP, pity), and splitting them would create a two-phase commit on the hottest path in the game. |
| **`Guild`** | `GuildId` | Roster, roles, guild level, quest counters, boss HP pool and damage ledger, phrase board, log | ⚠️ **Up to 30 concurrent writers.** The only genuinely contended aggregate in the game. See §5. |
| **`Ladder`** | season | Ratings, ranks | Global, append-mostly, eventually consistent. Not a real aggregate — a projection. |
| **`EventInstance`** | `EventId` | The event package, per-player track progress and shop stock | Package is immutable content; per-player progress lives on `Player`. |

🔒 **The persistence counterpart of the single-writer rule** (ruled in `16` A7): one accepted command commits as **one Postgres transaction** — the `Player` (and `Run`) snapshots, the idempotency outcome and the appended domain events together — with Redis as a rebuildable cache. `14` §16.4 is normative. This is what the child-not-peer modelling of `Run` above buys: the "two-phase commit on the hottest path" this table warns about cannot arise, because there is exactly one transactional store and both aggregates commit in it together.

### 4.1 `WorldSlice`

`Apply` never receives "the world". It receives exactly the aggregates a given command may touch:

```csharp
public sealed record WorldSlice(
    Player        Player,
    Run?          Run,          // null outside a run
    GuildView?    Guild,        // a READ-ONLY projection — see §5
    GhostSnapshot? Opponent);   // duels only
```

Loading the right slice is the Application layer's job. Deciding what happens to it is the domain's. This split is what keeps the domain free of repositories without pretending persistence does not exist.

---

## 5. The one place the model genuinely does not hold: guilds ⚠️

Everything above works cleanly for a single player. **Guilds break it**, and this is the honest limit of "play the game in memory."

`27` §11 already identifies guild quest counters and Guild Boss damage as the game's only contended writes — 30 players may submit simultaneously, and the spec calls for atomic increments rather than read-modify-write. A pure `Apply(state, command) → newState` over a `Guild` aggregate would require loading and rewriting the whole guild on every contribution, which is both a lock convoy and a lost-update hazard.

**The resolution:**

| Rule | Specification |
|---|---|
| The domain never returns a mutated `Guild` | `GuildView` in `WorldSlice` is **read-only** |
| Guild effects are expressed as **intents**, not states | `Apply` returns a `GuildContribution { guildId, counterId, delta }` domain event |
| The Application layer applies them as **atomic increments** | One `UPDATE … SET counter = counter + n` per contribution, per `27` §11 |
| Guild **rewards** are computed by a separate, scheduled pure function | `GuildRules.SettleWeek(guildState, memberLedger, context) → grants[]`, run once by a hosted service at week end. Pure, deterministic, and testable in memory like everything else — just not inside a player command. |

So the accurate statement is: **one player's entire game is playable in memory. Guild settlement is a second pure function, invoked on a schedule rather than by a command.** That is a small caveat and it is worth writing down, because discovering it during implementation would otherwise produce either a lock convoy or a quiet lost-update bug in the one system where 30 people are watching the number.

---

## 6. `InMemoryGame` — the harness 🔒

The concrete artefact that makes the claim testable. It is the *only* thing tests and the economy simulator need to construct.

```csharp
var game = new InMemoryGame(
    content: ContentSnapshot.LoadFromDisk("game-data"),
    seed:    12345,
    clock:   new VirtualClock(start: "2026-08-11T05:00:00Z"));

var player = game.CreatePlayer();

game.Send(player, new StartRun(chapter: 3, tier: Tier.Normal));
game.Send(player, new RollDice());
game.Send(player, new PickPerk(optionIndex: 1));
game.Clock.Advance(TimeSpan.FromHours(8));       // energy regenerates by RULE, not by waiting

game.State(player).Energy.Should().Be(200);
game.Events.OfType<GearGranted>().Should().HaveCount(3);
```

| Property | Value |
|---|---|
| Dependencies | **`SlayIdleRepeat.Core` only.** No Application, no ports, no fakes, no adapters. |
| Storage | A `Dictionary<PlayerId, Player>` |
| Time | `VirtualClock` — advanced explicitly. Nothing ever waits. |
| Randomness | A fixed seed. Reproducible byte-for-byte. |
| Speed | Target: **a full 180-day simulated player in < 200 ms.** |
| Events | Every `DomainEvent` accumulated and queryable — this is the assertion surface |

🔒 **The economy simulator (`21`) and the balance harness (`05` §9) are both thin wrappers over `InMemoryGame`.** `21` §2 currently wires the simulator to `Application` + eleven in-memory adapters; after this document it needs only `Core`. That is a material simplification of the highest-leverage tool in the project, and it removes the risk that the simulator and the game diverge through their adapter set rather than through their rules.

---

## 7. Domain events

`Apply` returns events. They are not decoration — four separate features in this documentation set are downstream of them and currently have no specified source.

| Consumer | Doc | What it needs |
|---|---|---|
| **Analytics** | `14` §10.1 — 30 named server-emitted events | Emit from the returned list. Complete by construction, because the domain cannot change state without producing one. |
| **Economy event log** | `14` §7.1 — append-only in Postgres | The same list, persisted. |
| **Feats** | `28` D — evaluated server-side from the event stream | Counters increment off the event list rather than off bespoke hooks. |
| **Client replay** | `14` §2.4 — the client animates what it is told | The event list *is* the animation script. |

```csharp
public abstract record DomainEvent(int Sequence);

public sealed record DiceRolled(int Sequence, DieFace Face)                  : DomainEvent(Sequence);
public sealed record TileResolved(int Sequence, TileType Type, NodeId Node)  : DomainEvent(Sequence);
public sealed record GearGranted(int Sequence, GearInstance Item,
                                 SourceClass Source, bool FromPity)          : DomainEvent(Sequence);
public sealed record CurrencyChanged(int Sequence, CurrencyId Id,
                                     long Delta, string Reason)              : DomainEvent(Sequence);
public sealed record PityCounterAdvanced(int Sequence, string Key, int Value): DomainEvent(Sequence);
public sealed record GuildContribution(int Sequence, GuildId Guild,
                                       string CounterId, long Delta)         : DomainEvent(Sequence);
```

🔒 **Every currency movement in the game emits `CurrencyChanged` with a reason.** That single rule is what makes `21` §8.3's `income_attribution.csv` — the report that answers risk **R10**, the compounding of dungeon, event and guild income — a query over events rather than thirty pieces of hand-written bookkeeping that will disagree with each other.

⚠️ **This is not event sourcing.** The aggregates are the source of truth and are stored as state; events are an *output* used for analytics, logging, replay and Feats. Adopting event sourcing as the persistence model is a much larger decision and is explicitly **not** taken here.

---

## 8. What stays outside the domain

| Concern | Where | Why |
|---|---|---|
| Persistence, transactions, unit of work | `Application` + adapters | I/O |
| Idempotency, command sequencing, replay of stored outcomes (`14` §3.2) | `Application` | A transport concern. The domain sees each command exactly once. |
| Showing an ad | `IRewardedAdPort` | The domain handles the *grant* (`ClaimAdReward`), never the impression |
| Store receipts, entitlement verification | `Application` + adapters | The domain receives the resolved answer |
| Push, telemetry sinks, analytics transport | Adapters | The domain emits events; who consumes them is not its business |
| Matchmaking candidate **selection** | `Application` (`IGhostRepository`) | A query. The *duel* is domain. |
| Leaderboard ranking | Projection | A window function over a table |
| Rendering, animation, audio, input | Client adapters | — |
| Rate limiting, auth, HTTP | `Server` | — |

**The test to apply:** *would this behave differently on a plane?* If yes, it is not domain.

---

## 9. Enforcement 🔒

`SlayIdleRepeat.Architecture.Tests` (`23` §6) gains six rules, each failing the build:

```csharp
[Fact] public void Domain_is_synchronous() =>
    // no Task, ValueTask, async, CancellationToken, or IAsyncEnumerable
    // in any public or private signature in SlayIdleRepeat.Core

[Fact] public void Domain_has_no_ambient_time_or_randomness() =>
    // already required by 14 §8.1; extended to ban IClockPort itself appearing in Core

[Fact] public void Domain_references_no_port_interface() =>
    // Core must not name any type from Application/Ports/

[Fact] public void Every_command_type_is_handled_by_Apply() =>
    // reflection over GameCommand subtypes vs the dispatch table — no silently unhandled command

[Fact] public void Every_currency_mutation_emits_CurrencyChanged() =>
    // IL scan: no write to a currency field outside the event-emitting helper

[Fact] public void The_whole_game_is_playable_from_Core_alone() =>
    // InMemoryGame's assembly closure is exactly { SlayIdleRepeat.Core, System.* }
```

Plus four more from §11, guarding the accessibility boundary:

```csharp
[Fact] public void Apply_is_the_only_public_mutation() =>
    // no public setter, public mutating method or public constructor on any Model/ type

[Fact] public void Handlers_and_Rules_are_internal() =>
    // except the two documented exceptions: CombatSimulator, PowerCalculator

[Fact] public void Core_internal_layering_holds() =>
    // Handlers → Rules → Model → Content → Primitives; Model never references Rules

[Fact] public void InternalsVisibleTo_names_only_the_Core_test_assembly()
```

The last one is your requirement, expressed as a test. **It is the single most valuable line in this document**, because it is the only thing that will still be true in eighteen months.

---

## 10. What this unlocks

| Capability | Before | After |
|---|---|---|
| Economy simulator (`21`) | `Core` + `Application` + 11 in-memory adapters | **`Core` alone** |
| Balance harness (`05` §9) | Wired to Application | `Core` alone |
| A 180-day player | Minutes, with fakes | **< 200 ms, one object** |
| Rule unit tests | Construct fakes, await, assert on repository writes | Construct a state, call `Apply`, assert on events |
| Reconnect chaos test (`14` §13) | Requires the whole stack | Replay a command sequence against a pure function |
| Client/server parity (`14` §13) | Compare two stacks | Compare one function's output on two platforms |
| A new designer answering *"what happens if I change this number"* | Read four documents | Run `InMemoryGame` for 180 days in a unit test |

That last row is the point. **The economy is the project's highest identified risk (`16` R10), and the fastest possible feedback loop on it is a domain model you can run in a test.**

---

## 11. The anatomy of `SlayIdleRepeat.Core` 🔒

### 11.1 The seam is I/O, not "use case"

The word *use case* covers two different things, and separating them is what decides this question.

| | Kind | Example | Needs a port? | Where |
|---|---|---|---|---|
| **A** | **Decision** | *"The player rolled a 4. Where do they land, what tile is it, what does it pay, which pity counters advance?"* | ❌ No | 🔒 **`Core`** |
| **B** | **Choreography** | *"Load the player from Postgres, check the idempotency key, call A, persist, publish events, return a profile delta"* | ✅ Yes | `Application` |

🔒 **Everything of kind A lives in `Core`, including the services that steer the aggregates.** If the deciding logic lived in `Application`, `InMemoryGame` would need `Application`, and §9's `The_whole_game_is_playable_from_Core_alone` — the property this entire document exists to buy — would be unachievable.

**So: one project, not two.** `Application` is already the second layer; it is just drawn along the I/O seam rather than the conventional "entities vs use cases" one. The conventional split would put kind A in a second assembly and gain nothing but a mapping layer between two layers that share a vocabulary — the exact "mapping fatigue" failure mode recorded in `23` §9.

### 11.2 Internal accessibility — yes, and it is the main prize 🔒

Because the deciding services sit in the same assembly as the aggregates, C#'s `internal` becomes a **compiler-enforced** boundary rather than a convention:

> **The only public way to change state in this game is `GameRules.Apply`.** Everything else the outside world can see is a getter.

| Type | Accessibility | Why |
|---|---|---|
| `GameCommand` + subtypes | **public** | The input vocabulary — also the wire protocol (`14` §2.3) |
| `DomainEvent` + subtypes | **public** | The output. Analytics, the economy log, Feats and client replay all read them (§7) |
| `GameContext`, `ContentSnapshot` | **public** | Inputs the composition root must build (§3) |
| **Aggregates** (`Player`, `Run`) | **public type · public getters · `internal` constructors · `internal` mutators** | Outside code reads everything and constructs nothing |
| `*Snapshot` persistence DTOs | **public**, with `ToSnapshot()` / `Rehydrate()` | See §11.3 |
| `GameRules.Apply` | **public static** | The single mutation entry point |
| **Command handlers** | 🔒 **`internal`** | The services that steer the domain. Nobody outside calls them directly. |
| **Rules / calculators** | 🔒 **`internal` by default** | Exceptions below |

**The `Rules` types that are public are enumerated here**, each with a documented external consumer. 🔴 **The list said "the only two" through M7 while it had grown to five, and neither widening amended it** — the widenings were recorded only in the architecture suite's own comments, which is the second-list failure §11.4's erratum describes. The authority is `Domain.PublicRuleTypes` plus the surface rules stated over it (`PublicRuleTypeFloorTests`' floor **and** cap, `BoardViewSurfaceRuleTests`, `HeroBattleSurfaceRuleTests`); this list is the *decision record* for why each name is on it, and adding one without a row here is the drift:

- `CombatSimulator` — the client simulates battles locally from a server-issued seed (`14` §2.4), and the balance harness calls it directly (`05` §9)
- `PowerCalculator` — the Hero screen displays `PlayerPower` (`29` §1)
- `BoardView` (M7-05b) — the Board screen renders the tile track (`03` §1.1). A read-only projection: it generates no board and hands out no draw stream
- `HeroBuild` (M7-06b) — the Hero and Inventory screens show the stat block a fight is actually run on, and their side-by-side delta *is* the gear derivation (`05` §1.1, `08` §3). A `class` rather than a `record` so its aggregate stays `internal`: a positional record's parameters are public properties, and carrying `AggregatedStats` as one would export the attack pipeline's heal ceiling with it
- `RunBattle` (M7-06b) — the Application layer's `SimulatePendingBattleUseCase` turns a run standing in `BattlePending` into the fight it is standing in, which is what lets the run leave that phase at all. It also owns the *question* — `HasOpenBattle` — so no layer above decides what "standing in a battle" means

⚠️ **Each of the five publishes an entry point, never the machinery behind it.** `StatAggregation`, `HeroBaseCurve`, `GearStatDerivation`, `EncounterFight`, `BossFight`, `BattlePlan`, `ActorPlan`, `BoardGenerator` and their peers are `internal`, and the surface rules above are what hold them there. A sixth name is a kickoff decision, not a keyword.

Every other calculator — board generation, drop tables, `LuckService`, the effect DSL interpreter, merge and enhance math, energy math — is `internal`. They are reachable only through `Apply`, which is the guarantee that no second code path can grant a currency or fire a pity counter.

⚠️ **This is encapsulation, not anti-cheat.** Anti-cheat is server authority (`14` §9); the client's copy of the state is explicitly untrusted and tampering with it changes nothing (`14` §7.2). `internal` exists to stop *our own code*, eighteen months from now, from taking the shortcut. Do not over-invest in it as a security boundary.

### 11.3 Rehydration — the one hole `internal` would otherwise leave

If constructors are `internal`, the Postgres adapter cannot rebuild a `Player` from a row. Resolved by a single public, validating factory pair rather than by `InternalsVisibleTo`:

```csharp
public sealed record PlayerSnapshot(int SchemaVersion, /* flat, serialisable fields */);

public sealed class Player {
    public PlayerSnapshot ToSnapshot();
    public static Result<Player> Rehydrate(PlayerSnapshot s, ContentSnapshot content);
}
```

Two benefits beyond keeping the boundary intact:

1. **One validated entry point** for every persisted state in the game — a corrupt row fails loudly at the seam rather than silently three rules later.
2. **The persistence shape is decoupled from the domain shape.** Aggregates can be refactored without a database migration, and `SchemaVersion` gives snapshot upgrades a home.

🔒 `InternalsVisibleTo` is permitted for **`SlayIdleRepeat.Core.Tests` only**, and for nothing else. An architecture test asserts that.

### 11.4 Structure

```
SlayIdleRepeat.Core/
├── Primitives/          # ids, Result<T>, RejectionReason, value objects
├── Content/             # ContentSnapshot + every definition type. Immutable, version-stamped.
├── Rng/                 # DeterministicRng, seed streams
├── Model/               # THE AGGREGATES. Public getters, internal ctors, invariants only.
│   ├── Player/ Run/ Guild/
│   ├── Gear/            #   08 §7 — GearInstance, Inventory, ItemAvailability (M4-03, M4-05)
│   └── Snapshots/       #   public persistence DTOs + Rehydrate (§11.3)
├── Rules/               # internal, static, stateless calculators (🔴 see the note below)
│   ├── Combat/          #   05, 17 — CombatSimulator is public; per-battle state, enumerated
│   ├── Stats/           #   05 §1.1, 29 — PowerCalculator is public
│   ├── Board/ Dice/     #   03, 04
│   ├── Effects/         #   18 — the DSL interpreter
│   ├── Luck/            #   24 — LuckService
│   ├── Perks/           #   06 §1–2, §4, 24 §4.7 — the 3-option draft and its composition rules
│   ├── Gear/            #   08 §2–3 — item power, quality, affix rolls, set bonuses
│   ├── Forge/           #   08 §4 — merge, enhance, salvage, the auto-salvage filter
│   ├── Inventory/       #   08 §5, 10 §4 — availability, sorting, side-by-side comparison
│   ├── Hero/            #   07 §1, §4 — Legend curve, unlock gates, hero name, loadout rules
│   ├── Feats/           #   28 D, §12.7 — the event → lifetime-counter projection
│   └── Economy/         #   10, 03 §7 — energy, currency math, shop pricing, run rewards
├── Commands/            # public GameCommand hierarchy
├── Events/              # public DomainEvent hierarchy
├── Handlers/            # internal. One per command. The services that steer the model.
├── GameRules.cs         # public static Apply — the only public mutation (§2)
└── Testing/
    └── InMemoryGame.cs  # public harness (§6)
```

🔴 **Corrected in the M4 review — the tree was four `Rules/` namespaces and one `Model/` namespace short, and one row pointed at the wrong one.** `Rules/` has **13** subdirectories on disk; this tree named **8**. `Forge/` (M4-04), `Hero/` (M4-10), `Inventory/` (M4-05), `Feats/` (M4-13) and `Perks/` (M3-06) were missing, as was `Model/Gear/` (M4-03, M4-05). Worse than absent: M4-03 amended the `Economy/` row to read *"`08` §4, 10 — merge, enhance…"* and M4-04 then created a **new** `Rules/Forge/` for exactly those two operations, so the row named a namespace they had left. `Economy/` is re-scoped above to what it actually holds. `ARCHITECTURE.md`'s copy of the same list was five short and is corrected in the same change. ⚠️ Two lists of one directory tree is what let this drift; neither is mechanically checked, so both are re-read at each milestone review rather than trusted.

**Dependency direction inside `Core`**, enforced by namespace-level architecture tests:

```
Testing ──▶ Handlers ──▶ Rules ──▶ Model ──▶ Content ──▶ Primitives
                                                 ▲
                    Commands ─────────────────────┘ (Content, Primitives, Rng — never Model)
                    Events   ─────────────────────┘ (Content, Primitives, Rng, and Model
                                                      under the value-record rule below)
```

`Rules` never references `Handlers`. `Model` never references `Rules`. `Rng` is pure arithmetic (`14` §8.1) and sits beside `Content`, beneath `Model`. Nothing beneath the `SlayIdleRepeat.Core` root reaches up into it.

🔒 **Amended by the M4 kickoff (2026-08-16), closing M1 carry-forward 8.** The chain above used to be written `Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives` and named **five** of the ten namespaces this section's own tree enumerates. `Rng`, `Commands`, `Events`, `Testing` and the `SlayIdleRepeat.Core` root had no place in it at all, so a type under any of them was matched by no rule in either direction — three separate milestones each found one of those regions ungoverned with every architecture rule green. The two positions that were genuinely undecided are settled here:

| Namespace | May name | May **not** name |
|---|---|---|
| **`Testing`** | the root, `Handlers`' peers below it — `Model`, `Commands`, `Events`, `Content`, `Rng`, `Primitives` | `Rules`, `Handlers`. `30` §6's harness drives the domain through `GameRules.Apply` and nothing else; nothing beneath it, the root included, names the harness |
| **`Handlers`** | `Rules`, `Model`, `Commands`, `Events`, `Content`, `Rng`, `Primitives`, the root | `Testing` |
| **`Rules`** | `Model`, `Content`, `Rng`, `Primitives` | `Handlers`, `Testing` |
| **`Model`** | `Content`, `Rng`, `Primitives` | `Rules`, `Handlers`, `Testing`, the root |
| **`Commands`** *(peer leaf)* | `Content`, `Primitives`, `Rng` | **`Model`**, `Rules`, `Handlers`, `Testing`, the root |
| **`Events`** *(peer leaf)* | `Content`, `Primitives`, `Rng`, **`Model`** — under the restriction below | `Rules`, `Handlers`, `Testing`, the root |
| **`Content`**, **`Rng`** | `Primitives` (and each other) | everything above them, and the root |
| **`Primitives`** | nothing | everything |

**`Commands` and `Events` are peer leaves, not a rung of the chain.** Neither sits above or below the other: a command is an input to `Apply` and an event is its output, and nothing may name either from below.

🔒 **`Commands` may not name `Model`.** A command carries **ids**, not aggregates — `14` §2.3's payload columns are ids and indices throughout, a merge names gear *instance ids* rather than `GearInstance`s, and `30` §11.6's one-vocabulary rule makes a command a wire value, which an aggregate is not. A command carrying a `WorldSlice` would additionally smuggle the aggregates past the clone §2.1's P4 depends on.

🔒 **`Events` may name `Model`, and only under this restriction:**

> An event may name a `Model/` type **only** when that type is an **immutable, fully-serialisable value record with no mutators** — snapshot-shaped. It may **never** name an aggregate **root** (`Player`, `Run`), nor any `Model/` type that carries an `internal` mutator.

The permission is forced by §7: `GearGranted(int Sequence, GearInstance Item, SourceClass Source, bool FromPity)` carries the item itself, and all four consumers of the event list — analytics, the economy log, Feats and the client's replay — **serialise** it, so an event carrying an id instead would send every one of them back to an aggregate whose state has since moved on. `08` §7's `GearInstance` is exactly the shape the restriction describes: a flat, serialisable record whose computed stats are never stored.

The restriction is what keeps the permission from being an open door. An event naming `Player` would put an aggregate root — with its `internal` mutators — into a list that leaves the domain, handing the outside world a mutation path around the single public one §11.2 exists to be. A value record has no such path: there is nothing on it to call.

⚠️ **It is enforced as a rule of its own, not as a row in the layering table**, and that is forced rather than stylistic: the table matches namespace *pairs*, while the permitted reference and the forbidden one here go to the same namespace and differ only in the **shape** of the type reached. `AccessibilityBoundaryTests.An_event_names_a_Model_type_only_when_it_is_an_immutable_value_record` carries it, beside `Core_internal_layering_holds` rather than inside it.

🔴 **Erratum, recorded by M2-09 — `Rules/` is not entirely stateless, and the exceptions are enumerated.** `05` §3's simulator is a **fixed-tick loop**: 1800 iterations that accumulate HP, cooldowns, an event log, `18` §2.4's charges and `05` §4.1's ward segments. A stateless function would have to take and return the whole battle on every call. So a handful of types under `Rules/Combat/` hold per-battle or per-actor state, each owned by exactly one caller, never shared and never `static`, so none carries the properties this annotation exists to protect. The list is **closed and mechanical**: `StatefulRuleTypeRuleTests.Stateful` is the authority, it fails the build on a type that is not on it, and equally on a listed type that has stopped holding state. Adding one is allowed and is a deliberate edit with its reason in the diff, which is the point. Everything else under `Rules/` — including every type in `Rules/Combat/` not on that list, `AttackPipeline` among them — is still the static, stateless calculator this line describes. ⚠️ The rule is scoped to `Rules/Combat/`; whether the same enumeration should cover `Rules/Effects/`'s trigger and stacking state is a milestone-review question, not M2-09's.

⚠️ **The list is not restated here on purpose, and this paragraph replaces one that was.** M2-09 wrote the erratum above naming its five entries — `CombatLog`, `BattleSimulation`, `BattleActor`, `CombatFlowState`, `WardPool` — and by the end of M2 the rule enumerated **nine**: M2-10 added `Status.ActorStatuses`, `Status.StatusTimeline` and `Status.StunWindow`, and M2-12 added `Bosses.BossPhaseController` (`05` §3.1's *"while `currentPhase < PhaseFor(hp)`"* and *"phases never revert"* are both stated over what has already happened, which no function of present HP can recover). Three milestones running, the document's copy of the list was the one nobody updated. A second copy of a closed list is a second list; the rule file is the one that fails the build, so it is the one that holds the names.

### 11.5 Why `Model` does not reference `Rules` — and the trade this accepts ⚠️

Aggregates hold **state and invariants**; they do not compute. `PowerCalculator.Compute(player, content)`, not `player.Power`.

That is deliberately **not** a rich-DDD-entity design, and it is worth being honest about why rather than discovering the tension later:

- The rules here are **data-driven by design**. `06` §5 is explicit: *"Do not write per-perk code"* — 98 perks are interpreted by one effect DSL (`18`). Logic that is an interpreter over content does not naturally become methods on an entity.
- The heavy calculations are **shared with tools that never load an aggregate** — the balance harness runs `CombatSimulator` against synthetic stat blocks (`05` §9), and `PowerCalculator` runs against a reference opponent (`29` §2.2).
- Keeping `Model` at the bottom of the internal dependency graph is what keeps `Rehydrate` cheap and the aggregates trivially serialisable.

**What lives on the aggregate, then?** Invariants only: *Energy never exceeds max + reserve · a currency never goes negative · a talent rank never exceeds 5 · an equipped pet is owned · a run's position is a valid node.* Those belong to the state and travel with it.

### 11.6 Consequence for `SlayIdleRepeat.Contracts`

`23` §3 lists a `Contracts` project for DTOs shared client↔server. With `GameCommand`, `DomainEvent` and the `*Snapshot` types public in `Core`, it **shrinks to wire envelopes only**: request/response wrappers, `commandId`, `sequence`, `stateHash`, error shapes.

🔒 **`Contracts` must never re-declare a command, an event or a domain type.** Per `14` §2.3, there is one vocabulary. A parallel DTO hierarchy is where mapping fatigue starts.

---

## 12. CQRS 🔒

**Decision D33: yes, and most of it is already here.** What is adopted is the read/write separation. What is **not** adopted is event sourcing, a second database, a mediator, or eventual consistency on the player's own state.

### 12.1 What already exists without the name

| CQRS concept | Where it already is |
|---|---|
| First-class commands | `GameCommand` + `GameRules.Apply` (§2). One dispatch point, already built. |
| Domain events as output | §7 — feeding analytics, the economy log, Feats and client replay |
| A read model | `Ladder` is already described in §4 as *"not a real aggregate — a projection"*, backed by a window function cached for 10 minutes (`11` §5.2a) |
| A client-side read model | `StateMirror` (`14` §5) — a denormalised local copy for display, explicitly never authoritative |
| Query-only repositories | `IGhostRepository.FindOpponentsAsync`, `ILeaderboardRepository` (`23` §4.2) |

So the question is not whether to adopt CQRS but **where to draw the line**, and the answer is not the conventional one.

### 12.2 The hard constraint: read-your-own-writes must stay strongly consistent 🔒

This is the one place strict CQRS would break an existing decision, and it is worth being precise about why.

`14` §2.3 has every command return `profileDelta` and `stateHash` **synchronously**, `14` §2.5 budgets p95 at 150 ms, and `14` §3.2 makes a duplicate command replay its *stored outcome*. The entire reconnect design (`14` §3) depends on the client being able to ask *"what is the authoritative state at sequence N"* and get a definitive answer immediately.

A player rolls the die roughly **30 times per run**, and each roll must resolve inside its 0.8 s animation. If their own profile were served from an eventually-consistent read model, the reward that just landed might not be there when the HUD reads it. That breaks the run loop, `stateHash` verification, and the optimistic prediction model in `14` §2.4.

🔒 **The player's own `Player` and `Run` are read through the write model. `Apply` returns the new state; there is no projection in between, and no staleness is tolerated.**

### 12.3 The rule: split by aggregate ownership, not by layer

| Data | Read path | Consistency |
|---|---|---|
| **Your own `Player` and `Run`** — currencies, inventory, talents, pity counters, Energy, Feats, inbox | Through the write model. `Apply` returns it. | 🔒 **Strong. Read-your-own-writes.** |
| **Everyone else's data, and cross-player rollups** — ladder, ghost candidates, guild rollups, event leaderboards, guild browser | Dedicated read models | Eventual, with a **stated staleness budget per view** |

That line maps exactly onto the aggregate table in §4, which is not a coincidence: the aggregates were drawn along the same seam.

### 12.4 The read models

| View | Source | Staleness | Notes |
|---|---|---|---|
| `LadderView` | player ratings | **10 min** | Already specified (`11` §5.2a) and already surfaced to the player as *"refreshed every 10 minutes"* |
| `GhostCandidates` | ghost table, rating band | 10 min | Must satisfy the B3 fairness rule (`24` §4.10) at selection time |
| `GuildRosterView` | guild membership + contribution ledger | 1 min | |
| `GuildBossView` | the damage ledger | **30 s** | Reading a slightly stale shared HP total is harmless; the *writes* are atomic increments (§5) |
| `GuildBrowserView` | guild activity rollup | 5 min | |
| `EventLeaderboardView` | event scores | 10 min | `26` §3.2 pays by percentile band, so exact live rank never matters |
| Analytics / PostHog | the event stream | fully async | `14` §10 |

### 12.5 Rules for the query side 🔒

| # | Rule |
|---|---|
| **Q1** | Queries never call `Apply` and never mutate. A query handler that writes is a command wearing a disguise. |
| **Q2** | **Queries contain no game rules.** If the answer to a query is a rule — *"what is my PlayerPower"*, *"can I afford this merge"*, *"would this build win"* — it comes from `Core` (`PowerCalculator`, `CombatSimulator`, both public per §11.2). A query handler that reimplements a rule is the failure mode this whole document exists to prevent. |
| **Q3** | **Every read model is rebuildable from the aggregates**, not only from the event stream. This is what keeps events an *output* rather than a source of truth, and it is the line that separates this from event sourcing. |
| **Q4** | Every read model declares its staleness budget in its port, and the UI states it wherever the player could otherwise be misled. |
| **Q5** | Query endpoints are `GET`, cacheable, carry no idempotency key, and may be served from a read replica. Commands are `POST`, idempotent by key, and always hit the primary. |
| **Q6** | The command vocabulary and the query vocabulary are **separate** — and that is fine. `14` §2.3's one-vocabulary rule binds commands only. |

### 12.6 What is deliberately not adopted

| Not adopted | Why |
|---|---|
| **Event sourcing** | Already ruled out in §7. Aggregates are stored as state; events are output. CQRS does not require ES, and adopting ES would be a far larger decision with real operational cost — replay, versioning, snapshotting, and a migration story for every rule change. |
| **A separate read database** | A read **replica** is the ceiling for v1. `14` §11 estimates ~2.4M requests/day at 10k DAU, trivially served by one Postgres. A second store buys consistency problems before it buys throughput. |
| **A mediator / command bus** (MediatR-style) | `GameRules.Apply` is already the single dispatch point (§2.2). Putting a second dispatcher in front of it is indirection with no gain — the same reasoning that rejects a DI container in the client (`16` O15). |
| **Eventual consistency on own-player state** | §12.2. |
| **Separate command and query *models* for the player** | There is one `Player` aggregate. Its snapshot is what the client renders. A parallel read DTO would be the mapping-fatigue failure mode from `23` §9. |

### 12.7 One clarification this forces ⚠️

`28` D2 describes Feats as *"evaluated server-side from the event stream"*. Under Q3 that is the wrong framing and should be read as follows:

🔒 **Feat counters are `Player` aggregate state, incremented inside `Apply`.** The event stream is how *analytics* observes them, not their source of truth. Otherwise Feats would be a projection that cannot be rebuilt without retaining every event forever — event sourcing through the back door, for one feature.

This also makes `28` D2's retroactivity requirement trivially true: Feats evaluate against **lifetime profile counters**, which are already on the aggregate, so existing progress counts the moment the feature ships.

### 12.8 Consequence for the API

`14` §2.3 specifies the command endpoint and nothing else. The query side needs its own surface and its own rules:

```
POST /run/{runId}/command       commands — idempotency key, primary, strongly consistent
POST /player/command            meta commands — same
GET  /ladder?around=me          queries — cacheable, replica-eligible, staleness stated
GET  /arena/candidates
GET  /guild/{id}/roster
GET  /guild/{id}/boss
GET  /guilds?search=…
GET  /event/{id}/leaderboard
```

`GET /run/{runId}/state?sinceSequence=N` (`14` §3.1) is **not** a query in this sense — it reads the player's own authoritative state and must hit the primary. It is a write-model read.

---

## 13. Amendments

| Doc | Change |
|---|---|
| `23` §3 | `Core` gains `Domain/` (aggregates + `Apply`) and `Rules/` (the existing pure calculators). `Application/UseCases/` shrinks to orchestration: load slice → call `Apply` → persist → dispatch events. The dependency rule is unchanged. |
| `23` §4.3 | `IClockPort` stays, but is called by the **composition root** and its answer passed as `GameContext.NowUtc`. It must never be injected into `Core`. |
| `23` §6 | Six new architecture tests (§9). |
| `14` §2.3 | The wire command list and the domain `GameCommand` hierarchy are **the same vocabulary**. 🔒 One name per command, no mapping layer — this is the direct guard against the "mapping fatigue" failure mode in `23` §9. |
| `14` §6 | Content becomes an immutable, version-stamped `ContentSnapshot` passed on the context, not a globally-reachable loaded blob. |
| `21` §2 | The simulator depends on `SlayIdleRepeat.Core` only. Remove the in-memory adapter set from its dependency list. |
| `27` §11 | Guild contributions are domain **events** applied as atomic increments; guild settlement is a separate scheduled pure function (§5). |
| `16` Part D | Build order step 1 becomes `Core` **including** `Domain/` and `InMemoryGame`. Step 2 (`Application` + ports) no longer blocks the economy simulator, which can now start at step 1. |
| `14` §2.3 | Gains a **query surface** alongside the command endpoint, with different rules: `GET`, cacheable, replica-eligible, stated staleness (§12.8). `GET /run/{runId}/state` is a write-model read and stays on the primary. |
| `28` D2 | Feat counters are **`Player` aggregate state incremented inside `Apply`**, not a projection over the event stream (§12.7). Retroactivity follows for free. |
| `23` §4.2 | Repository ports split by responsibility: write-side repositories return aggregates; **query ports return view models and declare a staleness budget** (§12.5 Q4). |
