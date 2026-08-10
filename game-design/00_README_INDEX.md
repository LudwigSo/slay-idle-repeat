# SLAY. IDLE. REPEAT. — Game Design Documentation
### Master Index & Reading Order for the Implementing AI

**Title:** **Slay. Idle. Repeat.** 🔒
**Genre:** Board-movement dice roguelike RPG with auto-battle combat and deep linear meta-progression
**Platform:** Android + iOS (portrait, one-handed). **Always online.**
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
| `🔒 LOCKED` | A decision made by the product owner. Do not redesign it. All 23 are listed in §1 and logged with rationale in `16_DECISION_LOG.md`. |
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

**Competition and business**

12. `11_PVP_GHOST_DUEL.md` — the single PvP mode and its global ladder
13. `12_MONETIZATION_ADS.md` — 28 ad placements and the Plus subscription

**Production**

14. `13_UI_UX_SCREENS.md` — 27 screens, flows, fonts, accessibility, connection states
15. `14_TECHNICAL_ARCHITECTURE.md` — Godot client, server-authoritative backend, determinism, observability
16. `23_PORTS_AND_ADAPTERS.md` — **the architectural constraint that shapes the whole codebase**: port catalogue, adapter rules, enforcement
17. `18_EFFECT_DSL.md` — the one language every game effect is written in
18. `19_CONTENT_TABLES.md` — 30 events, 20 quests, 14 modifiers, FTUE, curses, the Lucky Wheel
19. `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` — **975 art assets**
20. `22_ICON_PROMPT_TABLES.md` — per-icon prompts for all 158 perk and talent icons
21. `20_AUDIO_MANIFEST.md` — **106 audio assets**
22. `21_ECONOMY_SIMULATOR_SPEC.md` — the tool that validates every economy number

**Governance**

23. `16_DECISION_LOG.md` — every decision made, and everything still open

---

## 1. The 23 locked decisions

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

Plus these standing rules, decided alongside: revive works on bosses; PvP bans non-combat perk categories (68 eligible, 5 slots, 10 points); typefaces are Baloo 2 + Nunito Sans; determinism uses rounded doubles with a cross-platform CI hash test; ascension ships as the first post-launch update; the economy simulator is specced now and built in C# alongside the real code.

---

## 2. Design pillars

**P1 — The grind is the game, and the grind is honest.**
PvE progression is the main objective. The player should always see the next power step and reach it by playing. There is no wallet shortcut. Power comes from time, decisions and drop luck only.

**P2 — Every run is a short, complete story.**
8–12 minutes. Roll, move, fight, draft, get greedy, survive or die, bank the loot. A run must be legible in the first 30 seconds and satisfying even when it ends in death.

**P3 — Randomness the player can bend.**
Dice, drops and drafts are random, but the player accumulates tools to bend them: rerolls, upgraded die faces, draft rerolls, drop-rate talents. Luck is an input you level up.

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
- ❌ Guilds, chat, alliances, friend lists, social graph
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
| Rewarded ad placements | 28 |
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
| **Die Face** | One of the six configurable faces of the player's die. |
| **Core** | `SlayIdleRepeat.Core` — the pure C# rules library shared by client and server. |
| **Plus** | Slay Plus, the €4.99/month subscription. |
| **Beast Feed** | The single currency used to level both pets and mounts. |
