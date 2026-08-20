# SLAY. IDLE. REPEAT. — Game Design Documentation
### Master Index & Reading Order for the Implementing AI

**Title:** **Slay. Idle. Repeat.** 🔒
**Genre:** Board-movement dice roguelike RPG with auto-battle combat and deep linear meta-progression
**Platform:** **Android at v1** (portrait, one-handed). **Always online.** ⚠️ **iOS is descoped to post-launch** — ruled at the M0 review (2026-08-11); see `16` D34 and O23. The architecture stays iOS-ready (StoreKit adapter, Apple sign-in, `net8.0`), so this is a *shipping* decision, not an architectural one.
**Client:** Godot 4.3+ with C# (.NET 8)
**Server:** Containerised ASP.NET Core, portable across hyperscalers and self-hosting
**Business model:** Rewarded ads + **Slay Plus**, a €4.99/month subscription that removes ads and auto-grants every ad reward. **No pay-to-win. No purchasable power. No cosmetics.**

---

## 0a. Naming conventions 🔒

The title is stylised with full stops, which does not survive contact with running prose, file paths or C# identifiers. Three forms, used consistently:

| Context | Form | Example |
|---|---|---|
| **Store listing, app icon, key art, splash, marketing, document titles** | **Slay. Idle. Repeat.** | The App Store / Play Store name |
| **Running prose in these documents, and anywhere the trailing full stop would collide with sentence punctuation** | **Slay Idle Repeat** | *"Slay Idle Repeat requires a connection."* |
| **Code — namespaces, projects, solution, folders** | `SlayIdleRepeat` | `SlayIdleRepeat.Core`, `SlayIdleRepeat.Adapters.Ads.AppLovin` |

Other identifiers derived from the name:

| Thing | Value |
|---|---|
| Solution | `SlayIdleRepeat.sln` |
| Bundle / package ID | ⚠️ **NEEDS DETAIL** — needs a studio/organisation prefix. Placeholder: `com.<studio>.slayidlerepeat` |
| Subscription product ID | `slayidlerepeat.plus.monthly` |
| Subscription display name | **Slay Plus** ⚠️ *see `16` O16 — alternatives are "Repeat Plus" or simply "Plus"* |
| Save/cache namespace | `user://slayidlerepeat/` |

**Never abbreviate the title to "SIR" in anything player-facing.** It is fine as an internal shorthand in commit messages and channel names, nowhere else.

---

## 0. How to use this documentation

This documentation set is written to be consumed by an AI coding agent as the specification for a full implementation. It is deliberately **prescriptive**: where a number, formula, enum value or file path is given, treat it as the spec, not a suggestion.

Four markers appear throughout:

| Marker | Meaning |
|---|---|
| `🔒 LOCKED` | A decision made by the product owner. Do not redesign it. All 33 are listed in §1 and logged with rationale in `16_DECISION_LOG.md`. |
| `📐 TUNABLE` | A number that must live in a data file, not in code. It will be re-tuned after the economy simulator runs and after playtest. |
| `⚠️ NEEDS DETAIL` | Genuinely under-specified. Do **not** invent a final answer silently — implement the stated placeholder, surface the gap, and flag it. All are collected in `16_DECISION_LOG.md` Part B. |
| `✅` | A previously-open item that has since been resolved, with a pointer to where. |

### Reading order

**Core design**
1. `01_GAME_OVERVIEW.md` — pillars, fantasy, what the game *is*
2. `02_CORE_LOOP_AND_RUN.md` — the minute-to-minute and session-to-session loops
3. `03_BOARD_AND_TILES.md` — procedural board generation, all 14 tile types
4. `04_DICE_SYSTEM.md` — the signature mechanic
5. `05_COMBAT_SIMULATION.md` — the deterministic auto-battle core
6. `06_PERKS.md` — the 98-perk in-run draft pool
7. `17_BOSS_DESIGNS.md` — all 8 boss fights, three phases each

**Meta progression**

8. `07_HERO_PETS_MOUNTS.md` — the collection layer
9. `08_GEAR_AND_MERGING.md` — the loot chase
10. `09_TALENT_TREE.md` — the permanent power spine
11. `10_ECONOMY_AND_PROGRESSION.md` — 8 currencies, energy, curves, pacing

**Fairness, retention and social**

12. `24_LUCK_PROTECTION.md` — **every pity, mercy and floor in the game.** Read it before implementing any drop, chest, egg, crate or draft.
13. `25_RESOURCE_DUNGEONS.md` — three daily deterministic material dungeons
14. `26_LIVE_OPS_AND_EVENTS.md` — the limited-time-event framework and the launch event
15. `27_GUILDS.md` — 30-player guilds, guild quests, the weekly Guild Boss
16. `28_LIVE_SERVICE_ESSENTIALS.md` — the inbox, account linking, the Energy Reserve, Feats & Renown

**Competition and business**

17. `11_PVP_GHOST_DUEL.md` — the single PvP mode and its global ladder
18. `12_MONETIZATION_ADS.md` — 29 ad placements and the Plus subscription

**Production**

19. `13_UI_UX_SCREENS.md` — 39 screens, flows, fonts, accessibility, connection states
20. `14_TECHNICAL_ARCHITECTURE.md` — Godot client, server-authoritative backend, determinism, observability
21. `30_DOMAIN_MODEL.md` — **the centrepiece**: aggregates, one pure transition function, and the rule that the whole game is playable in memory from `Core` alone
22. `23_PORTS_AND_ADAPTERS.md` — **the architectural constraint that shapes the whole codebase**: port catalogue, adapter rules, enforcement
23. `18_EFFECT_DSL.md` — the one language every game effect is written in
24. `19_CONTENT_TABLES.md` — 30 events, 20 quests, 14 modifiers, FTUE, curses, the Lucky Wheel, the 28-day login calendar
25. `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` — **975 art assets**
26. `22_ICON_PROMPT_TABLES.md` — per-icon prompts for all 158 perk and talent icons
27. `20_AUDIO_MANIFEST.md` — **106 audio assets**
28. `29_POWER_MODEL.md` — **`PlayerPower`, and the three authored tables that say what it should be.** The product owner's primary dial.
29. `21_ECONOMY_SIMULATOR_SPEC.md` — the tool that grades the game against `29`: **14 player profiles, 16 assertions + 23 inherited requirements**

**Governance**

30. `16_DECISION_LOG.md` — every decision made, and everything still open

---

## 1. The 33 locked decisions

| # | Decision | Choice |
|---|---|---|
| D1 | Client tech | 🔒 **Godot 4 with C#** |
| D2 | Core session shape | 🔒 **Run-based only** — no idle income, no AFK stage. *Energy regeneration is the single exception: it accrues server-side while the player is away.* |
| D3 | Combat interactivity | 🔒 **Full auto-battle + perk drafting.** No taps during combat, ever. |
| D4 | Party | 🔒 **Single hero + pet/mount companions.** No roster. |
| D5 | Art direction | 🔒 **Chibi cartoon fantasy, bold outlines**, generated with **Midjourney** |
| D6 | Meta systems | 🔒 Gear + merging, talent tree, pets + mounts. **No ascension in v1.** |
| D7 | Content generation | 🔒 **Procedural boards inside authored chapters** |
| D8 | PvP | 🔒 **Async Ghost Duel**, one mode, global ladder with every player ranked |
| D9 | Paid product | 🔒 **Slay Plus, €4.99/month subscription.** Removes ads, auto-grants every ad reward. |
| D10 | Run gating | 🔒 **Energy per run, regenerating over time** |
| D11 | Backend authority | 🔒 **Fully server-authoritative, PvE included.** The game requires a connection. |
| D12 | Backend hosting | 🔒 **Containerised ASP.NET Core**, Azure App Service first, **no vendor lock-in** |
| D13 | Disconnection | 🔒 **Pause and reconnect gracefully.** Never kick the player out. |
| D14 | Cosmetics | 🔒 **None.** No die skins, no frames, no badges. Rank is a text label. |
| D15 | Ad network | 🔒 **AppLovin MAX**, via its official MIT-licensed Godot 4 plugin + a GDScript↔C# shim |
| D16 | Banners | 🔒 **No banner ads in v1** |
| D17 | Currencies | 🔒 **8** — Pet Food and Mount Feed merged into **Beast Feed** |
| D18 | Narrative | 🔒 **Flavour text only.** No plot, no cutscenes, no narrator. |
| D19 | Audio | 🔒 **AI-generated**, mirroring the art pipeline |
| D20 | Localisation | 🔒 **EN + DE only at launch** |
| D21 | Observability | 🔒 **Self-hostable OSS** — Sentry, PostHog, OpenTelemetry, own remote config |
| D22 | External dependencies | 🔒 **Ports and adapters (hexagonal), project-wide.** Every external dependency — ads, billing, push, telemetry, database, cache, object store, store APIs, remote config, clock, and Godot itself — is a C# interface owned by the application, implemented by an adapter in its own project. Enforced by architecture tests. |
| D23 | Title | 🔒 **Slay. Idle. Repeat.** Code namespace `SlayIdleRepeat`; see §0a for the three naming forms. |
| D24 | Resource Dungeons | 🔒 **Three daily deterministic material dungeons** — Stones, Beast Feed, Crowns. `25` |
| D25 | Live-ops | 🔒 **Data-driven limited-time-event framework in v1**, with one launch event and a rolling calendar. The Weekly Chapter Challenge becomes an event package. `26` |
| D26 | Guilds | 🔒 **30-player guilds ship in v1** — guild quests, a weekly Guild Boss, non-combat perks only, **no resource transfers, no free-text chat.** Reverses the previous no-social exclusion. `27` |
| D27 | Luck protection | 🔒 **Every random source in the game has a deterministic floor**, and every counter is shown to the player. `24` |
| D29 | Live-service essentials | 🔒 **Inbox, account linking, Energy Reserve, Feats & Renown all ship in v1.** `28` |
| D33 | CQRS | 🔒 **Read/write split by aggregate ownership.** Your own `Player`/`Run` read through the write model, strongly consistent. Cross-player data (ladder, ghosts, guild rollups) from read models with a stated staleness budget. **No event sourcing, no second database, no mediator.** `30` §12 |
| D32 | Domain model | 🔒 **The domain is a pure, synchronous state machine with one entry point**, `GameRules.Apply(state, command, context)`. **The whole game is playable in memory from `SlayIdleRepeat.Core` alone** — enforced by an architecture test. Time, content and entitlement are values, not services. `30` |
| D30 | Power model | 🔒 **`PlayerPower = K·√(EffectiveHP × DPS)`**, evaluated against a fixed reference opponent, with authored `ParPower`, `ExpectedPower(L)` and `ExpectedProgression` tables. Supersedes the additive formula in `02` §4.4. `29` |
| D31 | Economy validation | 🔒 **The economy simulator must pass before the live service opens.** 14 behavioural profiles, per-feature and per-ad-placement engagement, graded against the owner's authored expectation curve. `21` |
| D28 | Endgame | 🔒 **Accept the content cliff for v1.** No endless mode; ascension stays as the first post-launch update. Events and guilds carry the day-30-to-day-90 window instead. See R8. |

**The five design pillars for randomness, in one line:** no wallet shortcut *(D9)*, no luck shortcut *(D27)*, no social shortcut to combat power *(D26)*, no information advantage for payers *(`12` §2.5)*, and every floor visible to the player *(`24` §1.1)*.

Plus these standing rules, decided alongside: revive works on bosses; PvP bans non-combat perk categories (68 eligible, 5 slots, 10 points); typefaces are Baloo 2 + Nunito Sans; determinism uses rounded doubles with a cross-platform CI hash test; ascension ships as the first post-launch update; the economy simulator is specced now and built in C# alongside the real code.

---

## 2. Design pillars

**P1 — The grind is the game, and the grind is honest.**
PvE progression is the main objective. The player should always see the next power step and reach it by playing. There is no wallet shortcut. Power comes from time, decisions and drop luck only.

**P2 — Every run is a short, complete story.**
8–12 minutes. Roll, move, fight, draft, get greedy, survive or die, bank the loot. A run must be legible in the first 30 seconds and satisfying even when it ends in death.

**P3 — Randomness the player can bend, and randomness that cannot bury them.**
Dice, drops and drafts are random, but the player accumulates tools to bend them: rerolls, upgraded die faces, draft rerolls, drop-rate talents, a chosen loot **Focus**. Luck is an input you level up. And underneath all of it, **every random source has a visible floor** — a counted guarantee that no streak can outlast. A game with no wallet shortcut must not have a luck shortcut either. See `24_LUCK_PROTECTION.md`.

**P4 — Competition without an arms race.**
One PvP mode, asynchronous, decided by build quality and accumulated progress. Because nothing is purchasable, the ladder is a pure measure of play — and everyone can see exactly where they stand on it.

---

## 3. What this game is NOT

Explicitly out of scope. Do not implement these even if genre convention suggests them.

- ❌ Idle / AFK resource generation (Energy regeneration is the one exception — see `10` §3)
- ❌ Hero roster, team composition, party of 3–5
- ❌ Real-money currency, IAP bundles, battle pass, VIP levels, first-purchase bonuses, lifetime unlock
- ❌ Loot boxes purchased with money
- ❌ Timers that can be skipped with money
- ❌ **Cosmetics of any kind** — no skins, frames, badges, trails
- ❌ ~~Guilds~~ — **amended by D26.** Guilds ship in v1. Still out: **free-text chat**, alliances, friend lists, direct messages, cross-guild social, guild-vs-guild PvP, and any resource transfer between players (`27` §1, §8)
- ❌ Real-time multiplayer / netcode
- ❌ Prestige / ascension reset loop (deferred to the first post-launch update)
- ❌ Active skill buttons during combat
- ❌ Banner ads
- ❌ Offline play

---

## 4. Content at a glance

| Content | v1 |
|---|---|
| Chapters × difficulty tiers | 8 × 3 |
| Tile types | 14 |
| Perks | 98 (90 standard + 8 cursed) |
| Talent nodes | 60 (3 branches × 20, 9 keystones) |
| Gear | 24 base items × 5 rarities = 120, plus 4 SS sets |
| Pets / mounts | 24 / 12 |
| Enemy archetypes / visual variants | 8 / 64 |
| Elites / bosses | 16 / 8 |
| Minigames | 4 |
| Event cards | 30 |
| Daily quests / weekly modifiers | 20 / 14 |
| Resource Dungeons × tiers | 3 × 8 |
| Live event archetypes / launch events | 3 / 1 |
| Guild quests / Guild Boss rotation | 15 / 8 |
| Guild phrase-board phrases | 60 |
| Feats × tiers | 140 × 3 |
| Screens | 39 |
| Rewarded ad placements | 29 |
| Art assets to generate | 975 |
| Audio assets to generate | 106 |

---

## 5. Glossary

| Term | Meaning |
|---|---|
| **Run** | One attempt at one Chapter board. Costs 20 Energy. Ends in victory, death or abandon. |
| **Chapter** | An authored biome whose board layout is generated procedurally per run. |
| **Stage** | One of three segments within a chapter board. Stage 3 ends in the boss. |
| **Tile / Node** | One space on the board track. |
| **Perk** | A run-scoped power-up drafted 1-of-3 after each battle. Lost at run end. |
| **Talent** | A permanent node in the meta talent tree. Never lost. |
| **Legend Level** | The player's meta level, 1–200. Grants Talent Points and base stats. |
| **Ghost** | A server-stored snapshot of a player's build, used as the opponent in async PvP. |
| ~~**Die Face**~~ | ⚠️ **Removed.** The die is an ordinary six-sided die showing 1..6; no face is configurable and no face has a kind (`04`). |
| **Core** | `SlayIdleRepeat.Core` — the pure C# domain model and rules library shared by client and server. **The whole game runs from this one assembly.** `30` |
| **Apply** | `GameRules.Apply(state, command, context) → (newState, events)` — the single entry point to every rule in the game. `30` §2 |
| **GameContext** | Everything ambient, passed as data: time, seed, content snapshot, entitlement, feature flags. Never a service call. `30` §3 |
| **InMemoryGame** | The harness that plays a full 180-day player in <200 ms with no storage, network or engine. `30` §6 |
| **Read model** | A projection serving cross-player data (ladder, ghosts, guild rollups) with a stated staleness budget. **Never used for your own state.** `30` §12 |
| **Plus** | Slay Plus, the €4.99/month subscription. |
| **Beast Feed** | The single currency used to level both pets and mounts. |
| **Pity** | A counted guarantee: after N misses the next draw is forced to succeed. Every one is server-owned and shown to the player. `24` |
| **Focus** | A player-chosen `(slot, family)` that gets `×2.5` weight in every gear grant. Free, unlimited, 12-hour change cooldown. `24` §5 |
| **Set Token** | Earned from SS salvage and SS duplicates; 12 buy any SS gear item outright. `24` §5.1 |
| **Beast Mark** | Earned from ★5-duplicate pets and duplicate mounts; buys a *chosen* pet. `24` §4.4 |
| **Dungeon** | A short, deterministic, fixed-payout board that farms one material. `25` |
| **Event** | A limited-time JSON content package: re-skinned board, own currency, own reward track, own shop. `26` |
| **Guild** | Up to 30 players sharing daily quests and a weekly boss. Non-combat benefits only, no transfers, no free text. `27` |
| **Energy Reserve** | A second Energy bank, overflow-fed only, 1× Max Energy, spent automatically. `28` Part C |
| **Feat** | One of 140 authored records of something the player has *done*, in 3 tiers, fully retroactive and never missable. `28` Part D |
| **Renown** | The point total accumulated from Feats. The number a post-content player can still watch go up. `28` Part D |
| **PlayerPower** | The canonical combat-power scalar, `K·√(EffectiveHP × DPS)` against a fixed reference opponent. `29` §2 |
| **EmpiricalPower** | The same quantity *measured* from real simulated fights. Must track `PlayerPower` within ±12%. `29` §1 |
| **ParPower** | The authored power at which a given `(chapter, tier)` clears ~70% of the time. A 24-cell table, not a formula. `29` §4 |
| **Utility Index** | The second scalar covering everything `PlayerPower` excludes — income, board control, throughput. ⚠️ Die faces were its first term and are gone (`04`). Internal only. `29` §3.1 |
