# 16 — Decision Log & Remaining Open Items

Replaces the original `16_OPEN_QUESTIONS.md`. That document listed 38 open items; **all 38 have been closed** — 23 by product decision, 15 by authoring. What remains is a smaller, sharper list of genuine unknowns and accepted risks.

---

# PART A — Decision Log

Every decision, its rationale, and what it changed. Decisions are permanent unless explicitly revisited; if one is reopened, add a dated amendment rather than editing the row.

## A1. Foundational (from the first design pass)

| ID | Decision | Rationale | Primary docs |
|---|---|---|---|
| **D1** | Client is **Godot 4 with C#** | Free, capable 2D engine; C# lets the rules library be shared with the server | `14` |
| **D2** | **Run-based only**, no idle income | Keeps every session an active choice rather than a collection chore; halves the systems surface | `02`, `10` |
| **D3** | **Full auto-battle + perk drafting** | Idle-friendly, easy to balance, and it concentrates all agency in the draft — the game's best decision | `05`, `06` |
| **D4** | **Single hero + pets/mounts** | Tightest power fantasy, lowest asset count, no roster gacha temptation | `07` |
| **D5** | **Chibi cartoon fantasy**, bold outlines | Most readable at phone size and the most consistent style for image models | `15` |
| **D6** | Gear merging + talent tree + pets/mounts; **no ascension in v1** | Three deep systems beat four shallow ones; ascension is the natural first update | `08`, `09`, `07` |
| **D7** | **Procedural boards inside authored chapters** | Best mix of replay variety and authored difficulty control | `03` |
| **D8** | **Async Ghost Duel** PvP, one mode | Fits automatic combat perfectly, needs no netcode, fair by construction | `11` |
| **D10** | **Energy per run**, regenerating | Natural daily cadence and the game's biggest ad sink, without being a wall | `10` |

## A2. Business model

| ID | Decision | Rationale | Consequences |
|---|---|---|---|
| **D9** | **Slay Plus — €4.99/month subscription.** Removes ads and auto-grants every rewarded-ad benefit. No other paid product. | Recurring revenue with far higher LTV per converter than a one-time unlock. Break-even against a €5.99 purchase is ~1.5 months. | Rewrote `12` entirely. Forces every ad reward to be hard-capped (§1 fairness contract). Introduces churn management and lapse handling — see `12` §2.2. **Carries real reputational risk; see R1.** |
| **D15** | Ad network: **AppLovin MAX** | Genre-standard mediation with the best rewarded fill and eCPM. **AppLovin ships an official MIT-licensed Godot 4 plugin** (github.com/AppLovin/AppLovin-MAX-Godot), which removes the main integration risk. | Plugin is **GDScript-only**, so a GDScript↔C# shim is needed: **3–5 days, revised down from 2–3 weeks.** Android/iOS require the custom export-template path (Gradle + Java 17; CocoaPods) — CI must use it from day one. S2S reward callback exposure is unverified — see **O14**. |
| **D16** | **No banner ads in v1** | Banner revenue is small; a persistent banner in a portrait board game costs real screen space and perceived quality | One less format to integrate. Revisit only with data. |
| **D14** | **No cosmetics at all** | Cuts 97 art assets and a whole UI surface from v1 | Rank and Plus are text labels. **Removes the only non-power reward the game had; see R3.** |

## A3. Architecture

| ID | Decision | Rationale | Consequences |
|---|---|---|---|
| **D11** | **Fully server-authoritative PvE** | Unspoofable progression, a perfectly fair ladder, instant cross-device play, no save security work, complete analytics, remote balance tuning | **Roughly doubles engineering scope.** The game requires a connection and can never be played offline. Both accepted deliberately — see `14` §15. |
| **D12** | **Containerised ASP.NET Core**, Azure App Service first, **no vendor lock-in** | Portability across hyperscalers and self-hosting; the whole stack boots from `docker compose` | No serverless, no vendor SDKs, no proprietary services. Postgres + Redis + S3-compatible storage only. CI enforces it. |
| **D13** | **Pause and reconnect gracefully** on disconnect | The alternative — a blocking error — is a 1-star review generator on commutes | Requires idempotent commands, 48-hour server-side run state, and a chaos test that kills the connection at every command boundary |
| **Determinism** | **Rounded doubles + cross-platform CI hash test** | Almost certainly sufficient, and far cheaper than fixed-point | Round to 4 dp at every accumulation point. Escalate to Q32.32 only if CI actually fails. |
| **D21** | **Self-hostable OSS observability** — Sentry, PostHog, OpenTelemetry, own JSON remote config | Consistent with the no-lock-in stance | Analytics events are emitted **server-side**, so they are complete and unspoofable by construction |
| **D23** | **Title: Slay. Idle. Repeat.** Code namespace `SlayIdleRepeat`; unpunctuated "Slay Idle Repeat" in running prose; stylised form on store and marketing. Subscription becomes **Slay Plus**. | Names the loop in three beats, each mapping to a real system. Memorable and self-describing from a single screenshot. | Renamed across all 24 documents. Adds a **wordmark** asset (§E21, art total 975). Two consequences flagged: the subscription name (**O16**) and the "Idle" expectation gap against D2 (**O17**, `01` §1.1). |
| **D22** | **Ports and adapters (hexagonal), project-wide.** Every external dependency is a C# interface owned by `SlayIdleRepeat.Application` and implemented by an adapter in its own project — ads, billing, push, telemetry, analytics, database, cache, object store, store server APIs, remote config, the clock, and **Godot itself**. | Makes D12's no-lock-in rule *enforceable* rather than aspirational; lets the economy simulator and balance harness run the real application headless; keeps the messy Godot SDK edges quarantined one project each; and turns the Plus "no ads, same rewards" promise into an adapter swap with no branch in the game. | ~20–30 interfaces and ~20 small projects; **1–2 weeks additional up-front work** plus a permanent small tax on each new external capability. Enforced by `SlayIdleRepeat.Architecture.Tests`, which fails the build. Full spec: **`23_PORTS_AND_ADAPTERS.md`**. |

## A4. Content and production

| ID | Decision | Rationale |
|---|---|---|
| **D17** | Merge Pet Food + Mount Feed into **Beast Feed** (8 currencies) | Removes a currency a player would touch a dozen times in their account's whole life |
| **D18** | **Flavour text only** — no plot, no cutscenes, no narrator | The genre does not need it, it keeps localisation cheap, and a lone narrator would set a story expectation nothing else meets |
| **D19** | **AI-generated audio** (12 music, 94 SFX) | Consistent with the art pipeline; fast and cheap. Licence terms must be confirmed per tool — see R5. |
| **D20** | **EN + DE at launch** | Ship lean and verifiable. No machine translation reaches players in a language the team cannot read. |
| **D5b** | Image model: **Midjourney** with `--sref` | Strongest style-consistency control available, which is the dominant risk across 975 assets. Costs a background-removal step on every asset. |
| **Fonts** | **Baloo 2 + Nunito Sans**, both SIL OFL | Free, embeddable, right character, broad coverage |
| **Revive** | Works **everywhere including bosses** | Highest-value ad moment and the place players most want a safety net |
| **PvP perks** | **Ban non-combat categories.** 68 eligible, **5 slots, 10 points** | Legible metagame, tractable balance. Accepts that a fifth of the Codex is PvE-only — fine in a PvE-first game. |
| **Ladder** | **Global only. Every player ranked and findable.** Top 50 initial view, own position always pinned. | A player at #48,201 who climbs to #44,000 has visibly achieved something. "Not in the top 100" tells them nothing. |
| **Ascension** | **First major post-launch update** | Designed now so nothing blocks it; shipped when the most engaged players hit the wall (~2–3 months in) |
| **Economy sim** | **Specced now, built in C# later** | Sharing the real `Core` and real data makes it accurate; a throwaway Python model would test the model, not the game |

## A4b. Fairness, retention and social (the second design pass)

Added after a gap review against *Rogue Legend — Roguelike RPG* and the wider genre. The review found the design clean on purchasable power but weak in three places: **randomness had no floor**, **materials could not be farmed deliberately**, and **there was almost no live-ops or social surface** in a genre where those carry retention from day 30 onward.

| ID | Decision | Rationale | Consequences |
|---|---|---|---|
| **D27** | **Luck protection is a first-class system.** Every random source has a counted, server-owned, **player-visible** guarantee. Three primitives only — hard pity, soft pity, mercy accrual. Plus a **Focus** system for targeted acquisition, **Set Tokens** for the SS set chase, and **Reforge / Retune** so an item is improvable rather than a lottery result. | A game with no wallet shortcut must not have a luck shortcut either. Randomness was the only remaining way a player could fall behind through no fault of their own — which is precisely the feeling this product exists to remove. | New doc **`24`**. One `LuckService` in `Core` through which every gear, pet, mount and draft grant must route, enforced by an architecture test. Two new non-wallet counters. Two new Forge operations. Amends `03` C7, `05` §6.2, `06` §4, `07` §2.2 and §3.1, `08` §4.2 and §6, `11` §4.2, `19` Part F. **The economy simulator must now report p10, not just p50** — see E1–E5. |
| **D24** | **Three daily Resource Dungeons** (Enhance Stones, Beast Feed, Crowns), 8 tiers each, fixed deterministic payouts, 3 entries/day each, 10 Energy. | Every material was farmed by playing the same board and hoping. A player who needed Stones had no way to go and get Stones. This is the deterministic counterweight to D27. | New doc **`25`**. 2 new screens, 1 new tile type, 1 new ad placement (`AD_EXTRA_DUNGEON`). **Net new art cost: 4 assets.** Deliberately poor Legend XP so it never becomes the optimal levelling route. |
| **D25** | **Data-driven limited-time-event framework in v1**, with one launch event (`EVT_EMBERFALL`) and a rolling calendar. Three archetypes: Chapter, Score Rush, Collection. | The only recurring beat outside the daily loop was the Weekly Challenge. That is a thin live-ops surface for this genre, and it is why the day-30-to-day-60 window looked empty. | New doc **`26`**. 3 new screens. The Weekly Chapter Challenge becomes an `EVENT_SCORE_RUSH` package rather than a bespoke system — which **closes O3 structurally** (scoring is now a data field, correctable weekly). No new art per event: palette shifts and modifier stacks only. |
| **D26** | **30-player guilds ship in v1.** Guild Quests, a weekly Guild Boss, guild levels. 🔒 **Non-combat perks only. No resource transfer between players. No free-text chat.** | Guilds are the strongest retention system in the reference set and the one thing a solo design cannot replicate: a reason to open the app that is about somebody else. | New doc **`27`**. **Reverses the "no guilds/social" exclusion** in `00` §3. 4 new screens, ~6 new tables, the game's only contended-write path, and a permanent moderation queue. **Budget 3–4 engineering weeks plus ongoing ops.** The three locked constraints exist to contain three specific failure modes: combat perks would break the PvP ladder, transfers would become an alt-account farm within a week, and chat would be an unaffordable 24/7 obligation. See **R9**, **R10**. |
| **D28** | **Accept the content cliff for v1.** No endless mode. Ascension stays as the first post-launch update, as originally planned in D6. | Deliberate scope discipline. Events (D25) and guilds (D26) add real runway to the day-30-to-day-90 window, which was the cliff's most urgent part; the true end-of-content wall at Chapter 8 Mythic (~day 90) is left for ascension to solve. | **R8** records this as an accepted risk rather than a solved problem, because it is one. |
| **Plus / PvP** | **Battle-log replay history becomes free for everyone.** | It was a Plus grant, and duel replays are a **PvP scouting advantage**. The fairness contract bounded *power* and said nothing about *information*. | `12` §2.5. Establishes the general rule: **Plus may grant time and convenience; never power, never information.** |
| **D29** | **Live-service essentials ship in v1: inbox, account linking, Energy Reserve, Feats & Renown.** | Three of the four are only ever noticed at the worst possible moment — after an outage with no way to apologise, after a lost phone with no way to recover, after a weekend away with two days of Energy discarded. The fourth answers D28: a player past Chapter 8 Mythic needs a list, and Feats are a list. | New doc **`28`**. Closes **O19–O22**. 2 new screens (S37, S38). ⚠️ **Amends `09` §2** — Talent Point max 294 → 324, and the `09` §8 guardrail must be re-derived (**E22**). Amends `10` §3 (the Reserve resolves a self-contradiction in that section), `13` §1, `14` §7.3 and §12. Simulator gains E20–E23 and a twelfth profile. **The inbox moves to build step 4**, not 15. |
| **D33** | **CQRS, split by aggregate ownership rather than by layer.** Commands and domain events are already first-class (D32); this formalises the read side. 🔒 **The player's own `Player` and `Run` are read through the write model with strong read-your-own-writes consistency.** Cross-player data — ladder, ghost candidates, guild rollups, event leaderboards — is served from read models with a stated staleness budget. | Most of it already existed unnamed: the ladder is already a cached projection (`11` §5.2a), `StateMirror` is already a client read model (`14` §5), and `IGhostRepository` is already a query port. The one place strict CQRS *would* have broken something: a player rolls ~30 times per run and each roll must resolve inside its 0.8 s animation. Eventual consistency on their own profile would break the run loop, `stateHash` verification and the prediction model. | `30` §12. Adds a query API surface to `14` §2.3a with different rules (GET, cacheable, replica-eligible) from commands. Query ports return view models and declare staleness (`23` §4.2). ⚠️ **Clarifies `28` D2:** Feat counters are aggregate state incremented inside `Apply`, **not** a projection over the event stream — otherwise Feats would be event sourcing through the back door, for one feature. **Explicitly not adopted:** event sourcing (already ruled out, `30` §7), a separate read database (a replica is the ceiling), a mediator/command bus (`Apply` is already the dispatcher), and eventual consistency on own-player state. |
| **D32** | **The domain model is a pure, synchronous, dependency-free state machine, and the whole game is playable in memory from `SlayIdleRepeat.Core` alone.** One entry point: `GameRules.Apply(state, command, context) → (newState, events)`. | This was already the intent behind D22, but no document named an aggregate, defined a transition function, or said what the domain may know. Without that, the natural implementation is thirty use-case classes that each load, mutate and save — which works, but smears the rules across the Application layer and makes every test wire eleven fakes. | New doc **`30`**. **Refines D22 rather than reversing it:** the state machine moves from `Application/UseCases` into `Core/Domain`, and `UseCases` shrinks to load-slice → `Apply` → persist → dispatch. Time, content, entitlement and feature flags become **values on `GameContext`**, not services — `IClockPort` may no longer appear in `Core` at all. **The simulator (`21`) and balance harness now depend on `Core` only**, removing the risk that they diverge from the game through their adapter set. ⚠️ Guilds are the one place the model does not hold (30 concurrent writers) — contributions are events applied as atomic increments, and weekly settlement is a separate scheduled pure function. 6 new architecture tests, of which `The_whole_game_is_playable_from_Core_alone` is the load-bearing one. |
| **D30** | **`PlayerPower` becomes a first-class, canonical, data-defined scalar**, and every progression expectation is authored against it: `ParPower` per content, `ExpectedPower` per Legend Level, and `ExpectedProgression` per profile per day. | The economy could not be graded without a single number to grade. More importantly, the previous formula was **additive and therefore wrong** — it scored a zero-DPS build highly, and both the game and the simulator would have acted on that. | New doc **`29`**. ⚠️ **Amends `02` §4.4** (formula replaced with `K·√(EHP·DPS)`; the additive form is retained in data as `additive_legacy` and is reversible without a client patch). Amends `05` §9.1 (the 70% clear-at-par guardrail becomes the *definition* of `ParPower`, with a 62–78% band as A11) and `09` §8 (the guardrail table becomes `TalentFactor(L)`). Adds a second measured number, `EmpiricalPower`, and asserts the two agree within ±12% (**A10**). |
| **D31** | **The economy simulator is a v1 deliverable that must pass before the live service opens**, with a rich behavioural profile model and a tuning surface built for iteration. | The economy was identified as the project's highest remaining risk (**R10**), and three income streams were sized in isolation. A simulator that is correct but painful to use gets run three times and abandoned. | `21` rewritten. Profiles gain **per-feature engagement rates** and **per-placement ad rates**; the cadence model switches from runs/day to **minutes/day**, because dungeons made time the binding constraint rather than appetite. Plus is modelled as an **adapter swap**, exactly as in the game, so A2 is a real test rather than a tautology. Adds overrides, sweeps, a sensitivity report and a `--fast` mode. **14 profiles, 16 named assertions + 23 inherited. Effort 1.5 weeks → ~3 weeks**, almost all of it tuning ergonomics. |
| **Ad gap** | **The 45% day-30 ad-fairness gap stands unchanged.** | Watching ~20–25 minutes of ads a day is itself a form of effort. A payer is buying back time, not buying power, and the reward set is already hard-capped. | No change to `12` §1. Revisit only if review sentiment says otherwise. |

## A5. Rulings made during authoring

Small design questions that were resolved in the course of writing the new documents.

| Ruling | Where |
|---|---|
| `CP_GLASS_HEART`: shields/dodge/block/thorns work, healing does not, `MAX_HP` set after multipliers, shields re-based off pre-perk Max HP | `18` §9.1 |
| `PET_DICEBEAST`: fires `ON_BATTLE_END`, not on a combat cooldown | `18` §9.2 |
| Rimehold's "Core": a damage-amplification state flag, not a second target | `17` §6 |
| Sporequeen's sporelings: new `targetPriority` field, default 0, sporelings −1 | `17` §8, `05` §3.2 |
| The Dicelord phase 2: outcome table becomes `1–4 boss / 5–6 both` | `17` §9 |
| Ad fill failure: grant the reward **and** consume the cap slot; enforced server-side | `12` §4.3 |
| Plus enhance-luck equivalence: exactly 3 charges of +15% per day, auto-applied to the first 3 attempts | `08` §4.2 |
| Talent/loadout presets: **3 free slots**, unlimited with Plus, no ad placement attached | `09` §2.1 |
| No dismount animation — cut straight to battle | `07` §6 |
| No tutorial narrator — diegetic board captions instead | `19` Part D |
| SS sets: **4 sets, one per family axis**, so set membership is implied by family | `08` §3.2 |
| Curse catalogue: **12 curses**, never stacking, always paired with a reward when inflicted by a tile | `19` Part E |
| Lucky Wheel: 8 always-positive segments, server-rolled, 1 free + 2 ad spins daily, jackpot pity at 60 | `19` Part F |
| Talent node IDs: `{MT\|WD\|FT}_{NAME}`, keystones `{BRANCH}_KS_{NAME}` | `09` §2.2 |
| PvP fight cap is **60 s**, a documented override of the simulator's 90 s default | `11` §4.3 |
| Die face `Tier` scales the face's non-movement effect only — it is mechanical, not cosmetic | `04` §1 |
| The `Core` / `Application` seam is **I/O, not "use case"**: deciding logic and the services that steer the aggregates both live in `Core`; only choreography needing a port lives in `Application` | `30` §11.1 |
| Command handlers and rule calculators are **`internal`**; `GameRules.Apply` is the only public mutation. Public exceptions: `CombatSimulator`, `PowerCalculator` — and only those | `30` §11.2 |
| Aggregates are rehydrated through a public `ToSnapshot()` / `Rehydrate()` pair, **not** `InternalsVisibleTo` (which is permitted for `Core.Tests` alone) | `30` §11.3 |
| `Model` never references `Rules` — aggregates hold state and invariants, calculators compute over them. A deliberate departure from rich-DDD entities, because the rules are data-driven (`06` §5, `18`) | `30` §11.5 |
| `Contracts` shrinks to wire envelopes only and never re-declares a command, event or domain type | `30` §11.6 |

## A6. Rulings from the implementation-readiness review (2026-08-11)

A pre-implementation gap review found spec holes the earlier passes missed. All were resolved by product-owner decision on 2026-08-11.

| Ruling | Where |
|---|---|
| **Enemy level** is a chapter-based table (Ch1=10 … Ch8=80, +10 Heroic, +20 Mythic) — the damage formula's `attacker.Level` term was uncomputable for enemies | `05` §6.0 |
| **Pets never basic-attack.** They are aura + active-ability modules only; abilities scale off the hero's ATK. Resolves the contradiction between `05` §3 and `07` | `05` §3.2 |
| **PvP duel simulation semantics**: heroes target only the opposing hero, attacker's side acts first each tick, `ON_KILL` never fires, target conditions read the opposing hero | `05` §3.3 |
| **Skill minigame outcomes are client-asserted**, validated for legality only — a documented exception to server authority, accepted because rewards are small, capped and run-local | `03` §6.2, `14` §9 |
| **Legend XP income table** authored (`BaseXp(c) = 25 × 1.55^(c-1)`, ×3 elite, ×15 boss, ×10 victory); the FTUE run pays a scripted 300 XP so beat 10's forced talent spend always has a point to spend | `02` §5.1a, `19` Part D |
| **Gear slot coefficients** authored — flat stats scale with ItemPower, percent stats scale with rarity only | `08` §3.0a |
| **Chapter signatures ruled**: Ch3 enemies+Elites revive once at 20% (DoTs persist, boss excluded); Ch4 Burning tiles (~20% of tiles, 4% Max HP on landing); Ch6 Clockwork Pressure (+5% enemy power per roll in the stage, cap +50%, resets at Stage Gate) | `03` §4.1 |
| **Matchmaking sparse-population rules**: ±150 band widening to ±600, bot-Ghost backfill when B3 is unsatisfiable, lowest-rated player exempt from B3 | `11` §4.4 |
| **The five bot Ghosts and the tutorial Ghost are authored builds**, doubling as the sparse-population backfill | `11` §8.1 |
| **28-day login calendar authored** (day-14/28 S-tier chests reclassed to `CHEST_PREMIUM`) | `19` Part G, `24` §3 |
| **15 guild quests, 60 phrases, 40 description phrases authored** | `27` §3.1, §6.1a, §6.1b |
| **All 140 Feats authored** with tier thresholds; Renown total ≈12,900 vs the ~12,000 target — accepted | `28` D2.2 |
| **Codex mastery completed**: five sections, +17.2% all stats at full completion (active from Legend 100), 10 TP at 25/50/75/90/100% completion | `06` §6.1 |
| **Minigame reward tables authored**, chapter-scaled like the ad bundles | `03` §6.1 |
| **Ladder inflation from one-sided Elo accepted and monitored**, not fixed | R13 below, `11` §5.1, `14` §10.1 |

---

# PART B — Remaining Open Items (19)

Everything still genuinely unresolved, prioritised. Nothing here blocks starting implementation.

## B1. Must resolve before launch (10)

| # | Item | Doc | Note |
|---|---|---|---|
| **O1** | **Frontier-bonus / catch-up curve.** Without it Chapter 8 takes ~70 hours; the curve that compresses it to ~40 is sketched, not specified. | `10` §8 | Cannot be specified by hand — it is the economy simulator's first job. |
| **O2** | **Event outcome weights and value scalars** for events 11–30. Several (26, 28, 30) can swing a run's rewards by >50%. | `19` Part A | Same dependency: run them through the simulator. |
| **O3** | ✅ **Closed structurally by D25.** The Weekly Challenge is now an `EVENT_SCORE_RUSH` package and its scoring formula is a data field (`leaderboard.formula`), retunable weekly on live evidence. The underlying concern — that the formula may reward the strongest account rather than the best run — still stands, but it is no longer a launch blocker, because fixing it is a JSON edit rather than an app release. | `19` Part C, `26` §3.2 | Ship the default formula; watch the correlation between rank and PlayerPower in week 1 and correct. |
| **O4** | **Server cost model at scale.** ~2.4M requests/day at 10k DAU is trivially servable, but no actual estimate exists. | `14` §11 | Do this before committing to a hosting tier. |
| **O5** | **Push notification transport.** FCM + APNs directly is recommended over a wrapper, to stay lock-in-free. | `14` §12 | Small, but unowned. |
| **O6** | **Managed vs self-hosted Postgres** in production. | `14` §1.1 | A cost/ops choice, not an architectural one. Either satisfies the no-lock-in rule — and with D22, swapping is a one-adapter change. |
| **O16** | **Subscription display name.** Currently **Slay Plus**, derived from the title. Alternatives: "Repeat Plus" (fits the loop framing better), or plain "Plus" (shortest, and the store already shows the game name above it). | `12` §2, `00` §0a | Low stakes, but it appears on the store page, the Plus tab and the ladder tag — pick before store assets are produced. |
| **O17** | **"Idle" in the title vs D2 (no idle income).** The game is auto-battle, not idle-accrual. Store-search traffic from "idle RPG" may install and churn on discovering an active, Energy-gated 10-minute run loop. | `01` §1.1 | Keep the name; set expectations in the store's first line (*"Auto-battle roguelike. Roll, fight, loot, repeat."*). **Measure D1 retention by install source.** If idle-search installs churn markedly worse, fix listing copy and creative — not the name. |
| **O18** | **Bundle / package identifier** needs a studio or organisation prefix. Placeholder `com.<studio>.slayidlerepeat`. | `00` §0a | Blocking for the first store upload, trivial before then. |
| **O15** | **DI container vs hand-rolled composition root in the Godot client.** Godot's node lifecycle resists constructor injection into scenes. | `23` §9 | Recommendation: hand-rolled root with explicit factories. Scenes talk only to presenters; presenters receive ports from the root. Avoids a dependency and the engine's lifecycle sharp edges. |

### B1a. Live-service gaps found in the second design pass — ✅ all four closed by D29

| # | Item | Resolution |
|---|---|---|
| **O19** | No mail / inbox — no channel for outage compensation, announcements, event reconciliation (`26` §8) or moderation outcomes (`27` §6.3) | ✅ **`28` Part A.** Server-to-player only, authored templates rather than free text, attachments auto-granted on expiry rather than destroyed, no marketing ever. Build it at step 4 alongside the server skeleton, not at step 15 — the first time it is needed will be an incident. |
| **O20** | Account loss is silent and permanent — an anonymous device account with no local save and nothing that ever prompts a link | ✅ **`28` Part B.** Prompts at Legend Level 10 and 30 plus a dismissible fortnightly banner, never blocking. One-time link reward (500 Soul Shards + a chest), unfarmable via provider-subject uniqueness. **The sign-in conflict case is specified explicitly** and must never resolve silently; the discarded account is soft-deleted with a 30-day recovery window. Unlinking is not offered. |
| **O21** | Energy overflow discarded — `10` §3 contradicted its own stated intent that a returning player should always find a full tank | ✅ **`28` Part C.** An Energy Reserve holding 1× Max Energy, overflow-fed only, spent automatically, never purchasable. Does not add a second exception to D2: nothing new accrues, less is discarded. |
| **O22** | No record of what the player has *done* — the Codex covers collection only, and D28 leaves the post-content player with no list | ✅ **`28` Part D.** 140 Feats in 9 categories, 3 tiers each, fully retroactive, none missable and none luck-dependent, feeding a **Renown** total. ⚠️ **Changes `09` §2:** the v1 Talent Point maximum rises 294 → 324, and the `09` §8 power guardrail must be re-derived (assertion **E22**). |

## B2. Verify or resolve during production (5)

| # | Item | Doc | Note |
|---|---|---|---|
| **O7** | **Layered gear rigging.** AI-generated overlays will not naturally align to a shared skeleton. | `15` §E2 | Mitigation is specified (generate over a ghosted body, remove in post). Budget manual alignment for all 60 overlays; fallback is 20 composited looks with less mix-and-match. |
| **O8** | **VFX generation method.** Image models produce incoherent frame sequences. | `15` §G | Strong recommendation to abandon generated sprite sheets and animate procedurally in-engine. Not yet formally accepted. |
| **O9** | **Tutorial elite scripting.** Beat 6 needs the player left at ~25% HP regardless of build. | `19` Part D | Must be a tutorial-only enemy definition, never a hack in the combat loop. |
| **O14** | **Does the MAX Godot plugin expose server-side rewarded callbacks / `setUserId`?** Not mentioned in its public docs. Our whole ad-reward model grants server-side, because the client is not trusted with progression. | `12` §3.3 | **Verify on a spike build before writing the shim.** If absent: patch or fork the MIT-licensed plugin (the native layer is available), or fall back to client-asserted completion with a signed nonce plus server-side caps. The fallback is acceptable here only because every ad reward is hard-capped and nothing is purchasable. |
| **O13** | **Curse chapter gating.** The 12-curse catalogue is authored, but which curses appear in which chapters is only suggested. | `19` Part E | Suggested: 4 basic curses from Ch. 1, the rest from Ch. 3, `CUR_HUNTED` from Ch. 5. |
| **O23** | **Godot 4.x C# (.NET) mobile export maturity — especially iOS.** The whole project rests on D1, and this was never flagged as a risk. | `14` §1 | **Verify on a week-1 spike, alongside O14:** export a trivial C# app to Android and iOS through the full custom export-template CI path (Gradle + Java 17; CocoaPods + Xcode). If it fails, the fallback discussion (engine version pin, GDScript UI shell, waiting for a point release) happens in week 1, not month 4. |

## B3. Scheduled reviews (3)

| # | Item | When |
|---|---|---|
| **O10** | **Currency count review.** Eight is still at the upper edge. Consider merging Merge Dust and Enhance Stones into one "Forge Material" — same screen, same items, same moment. Would bring it to seven. | After first playtest. **A reminder has been scheduled.** |
| **O11** | **Preset slot count.** 3 free slots is a guess. Raise the allowance if telemetry shows players capped; never gate it further. | After first playtest |
| **O12** | **Revenue model validation.** `12` §9 gives the shape (rewarded ARPDAU $0.02–0.09, sub conversion 0.5–2% of MAU, 3–6 month median lifetime) but none of it is validated for this specific product. | Before launch, ideally with a store-page test |

---

# PART C — Accepted Risks

Not problems to solve — consequences of decisions, recorded so nobody is surprised later.

| # | Risk | Source | Why it was accepted |
|---|---|---|---|
| **R1** | **Subscription reputation risk.** A recurring charge to remove ads is a harder sell than a one-time unlock, and this genre's audience is vocal about it. Reviews will mention it. | D9 | Mitigated by: genuinely light interstitial load, nothing lost on lapse, honest store copy stating the ad-minutes saved, no dark patterns. **Watch review sentiment in week 1 and be prepared to add a lifetime option.** A €24.99 lifetime unlock alongside the sub is the obvious hedge and remains available. |
| **R2** | **The game does not work offline.** Discovering this on a plane is a 1-star review. | D11 | Stated plainly in the store listing and on first launch. Reconnect handling is designed to make brief drops invisible. |
| **R3** | **No visual trophies anywhere.** A Legend-tier player's only marker is a text label, and every season pays identical rewards. Ladder retention past two seasons is untested. | D14 | Cheapest future fix is die skins — ~11 assets per skin, no other system changes. The door is deliberately left open. |
| **R4** | **Engineering scope roughly doubled** by server authority. This is now a service, not just an app. | D11 | Bought: unspoofable progression, a fair ladder, cross-device play, remote tuning, airtight ad and subscription integrity. All of which serve the game's core pitch of fairness. |
| **R5** | **AI licensing exposure.** Commercial terms for both the image model and the audio tools must be confirmed in writing, with provenance records for every one of the ~1,080 generated assets. | D5b, D19 | **Legal prerequisite, not a formality.** Do this before the first production batch, not after. |
| **R7** | **Ports-and-adapters has its own failure modes** — anaemic ports that mirror a vendor API, mapping fatigue, fakes drifting from real adapters, and over-abstracting things that are not actually external. | D22 | Each is guarded by a specific rule in `23` §5 and §9: design ports from the use case backwards (A4), keep `Contracts` thin, run the shared contract-test suite against every implementation including the fake (A8), and port only what crosses a process, network, device or vendor boundary. |
| **R8** | **The content cliff is real and unsolved.** Chapter 8 Mythic completes at ~day 90, the talent tree stops mattering at ~324 of 633 points, Legend Level caps at 200, and after that the only progression is PvP rating — whose own retention is already flagged as untested (R3). | D28 | Accepted deliberately for scope. Events (D25) and guilds (D26) add real day-30-to-day-90 runway, which was the most urgent part of the problem. **Ascension remains the first post-launch update and is now load-bearing rather than optional** — it should be designed during v1 production, not after. The cheapest interim fix if the wall arrives early is an endless-scaling mode reusing board generation, combat and the existing 8 bosses with no new art. |
| **R9** | **Guilds bring a permanent operational obligation.** Even without free-text chat there is a report queue (guild names, tags, player names), a documented response SLA, an inactivity-and-orphaned-leader policy, and GDPR handling for membership and contribution data. | D26 | Contained as far as it can be: no free text, no transfers, no global guild ladder, no guild-vs-guild. **The residual obligation must be owned by a named person before launch.** If the schedule slips, guilds are the correct feature to move to the first post-launch update — they are additive and nothing depends on them. |
| **R10** | **Three new material income streams were sized independently and compound multiplicatively.** Dungeons (D24), events (D25) and guild perks (D26) all pay Crowns, Enhance Stones and Beast Feed, and guild perks multiply the other two. Together they may double a mid-game player's income and collapse the merge bottleneck that `10` §4 identifies as the game's most exciting sink. | D24, D25, D26 | **Assertion E19 — the highest-risk item in the simulator spec.** Run the simulator with all three enabled *plus* the new Reforge/Retune sinks before tuning any of them individually. Expect `MergeCrownCost`, the `+11 → +15` stone costs and `BeastFeedCost` all to rise. |
| **R12** | **The expectation curves are guesses about guesses.** `29` §5 and §6 are hand-shaped: the gear/talent/pet factor split and the day-by-day power checkpoints express intent, not evidence. Assertion A14 grades the game against them, so if they are wrong the tool will confidently report a healthy economy as broken, or the reverse. | D30 | Accepted, and explicitly designed for: `29` §6.2 lists *"the expectation curve is wrong, not the game"* as a legitimate outcome of a failed run. The curves live in a file the product owner owns and are meant to be revised. **The discipline that makes this safe is committing a change to `expected_progression.json` on its own**, so that moving the goalposts is always visible as a decision. |
| **R11** | **Pity systems change the distribution, not the mean — and the simulator currently reports means.** A design whose whole purpose is to protect the unluckiest player cannot be validated by a median. | D27 | `24` §10 adds p10 reporting and assertion E2 (p10 within 1.35× p50). If p10 does not move after this work, the work failed and the numbers are wrong. |
| **R13** | **The ladder inflates.** Only the attacker's rating changes (`11` §5.1, locked), attackers can always pick the weakest of three candidates, and defender losses cost nothing — one-sided Elo is not zero-sum, so ratings drift upward, and ladder health is explicitly outside the simulator's scope (`21` §13). | `11` §5.1 | Accepted deliberately: the defender protection is worth more than rating purity. The soft season reset absorbs drift; a **season rating drift metric** (median and p90 rating per season, `14` §10.1) measures it. Revisit tier thresholds only if the data shows material inflation. |
| **R6** | **Every economy number is unvalidated.** The pacing targets, drop rates, costs and curves throughout these documents are genre-informed estimates that have never been tested against each other. | — | The simulator (`21`) exists precisely to fix this. Until it has run, treat all 📐 TUNABLE numbers as placeholders that look precise. |

---

# PART D — Suggested Build Order

Derived from the dependency graph across all 28 documents.

1. **`SlayIdleRepeat.Core`** — the **whole domain model** (`30`): aggregates, `GameRules.Apply`, `GameContext`, domain events, plus the rules they call (RNG, stats, effect DSL `18`, combat sim `05`, board generation `03`). Pure, synchronous C#; no engine, no clock, no ports. **Ship `InMemoryGame` (`30` §6) in this same step** — at the end of it the entire game is playable in a unit test, which is what every later step is measured against.
2. **`SlayIdleRepeat.Application` + the port catalogue** (`23` §4) and the **in-memory fake for every port**. Stand up `SlayIdleRepeat.Architecture.Tests` at the same time — the rules are far cheaper to enforce from commit one than to retrofit, and `The_whole_game_is_playable_from_Core_alone` is the one that must never go red.
3. **Determinism CI test** across x64/ARM64. Everything downstream depends on this being true.
4. **Server skeleton** — containerised ASP.NET Core composition root, Postgres/Redis/MinIO adapters, `docker compose`, command protocol (`14` §2.3), idempotency, auth. 🔒 **Build the inbox (`28` Part A) here.** It is small, it has no dependencies, and the first time it is genuinely needed will be an incident — at which point it is far too late to start.
5. **Power model (`29`) + economy simulator (`21`)** — ✅ **no longer blocked by step 2.** Since the simulator now depends on `SlayIdleRepeat.Core` alone (`30` §10), it can start the moment `InMemoryGame` exists in step 1, in parallel with the port catalogue. Build `PowerCalculator` and the `tuning/` data layout first; the simulator is meaningless without a trustworthy power number. Author `expected_progression.json` **before** the first run, so the first result is a comparison rather than a curiosity. Re-tune every 📐 number before content production begins. ⚠️ ~3 weeks, not 1.5 — see D31.
6. **Godot client vertical slice** — one chapter, one boss, board + battle + draft + results, real network layer including reconnect. Presenters tested without booting the engine.
7. **Art anchor sheet + UI kit + Chapter 1 assets** (`15` §H order) — unblocks a genuinely playable slice.
8. **Meta systems** — gear/forge, talents, menagerie. **Build `LuckService` (`24` §11) here, not later** — every grant in every later step must route through it, and retrofitting it means auditing every drop path in the game. Reforge and Retune (`24` §6) land with the Forge.
9. **Resource Dungeons** (`25`) — cheapest possible feature at this point: the board generator, combat and reward pipeline all already exist, and it needs 4 new art assets.

> 🔒 **Milestone: FIRST PLAYABLE — the end of step 8.** Since this project will be iterated on, steps 1–8 plus **Chapters 1–3 content** are formalised as the first version: the pure domain, ports and fakes, determinism CI, server skeleton (with inbox), power model + economy simulator, the client vertical slice with reconnect, Chapter 1 art, and the meta systems with `LuckService`. **Explicitly excluded from First Playable:** PvP, dungeons, live-ops, guilds, ads/subscription, live-service extras (all run on fakes or flags until their step). Everything after step 8 is iteration on a playable, testable game — and the already-documented cut lines (guilds first, per step 13) apply from here.
10. **Content fill** — chapters 2–8, all perks, events, quests as data.
11. **PvP** — ghosts, matchmaking, ladder, seasons.
12. **Live-ops framework** (`26`) — the event scheduler, package schema, track, shop and hub. The Weekly Challenge is migrated onto it rather than built separately. Author `EVT_EMBERFALL` as data.
13. **Guilds** (`27`) — last of the major systems, and the correct one to cut if the schedule slips. Nothing else depends on it. Stand up the report queue and the response SLA *before* the feature flag goes live, not after.
14. **Ads + subscription adapters** — install the official MAX Godot plugin inside `Adapters.Ads.AppLovin`, write the GDScript↔C# shim, add `AutoGrantAdAdapter`, stand up the custom export-template build in CI, and wire the store adapters with server-side grants. Resolve **O14** (S2S callbacks) first. Note that everything before this step runs on `FakeRewardedAdAdapter` — the game is fully playable and testable with no ad SDK present.
15. **Live-service essentials** (`28`) — account linking and the conflict flow (**O20**), Energy Reserve (**O21**), Feats & Renown (**O22**). The inbox (**O19**) was already built at step 4. Small individually; collectively the difference between an app and a service.
16. **Audio** (`20`), localisation (EN+DE), accessibility.
17. **Balance harness, chaos tests, soft launch.** Re-run the economy simulator with dungeons, events and guilds **all enabled together** (**R10 / E19**) before any tuning is treated as final.
