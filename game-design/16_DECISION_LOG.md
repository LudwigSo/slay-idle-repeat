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

---

# PART B — Remaining Open Items (18)

Everything still genuinely unresolved, prioritised. Nothing here blocks starting implementation.

## B1. Must resolve before launch (10)

| # | Item | Doc | Note |
|---|---|---|---|
| **O1** | **Frontier-bonus / catch-up curve.** Without it Chapter 8 takes ~70 hours; the curve that compresses it to ~40 is sketched, not specified. | `10` §8 | Cannot be specified by hand — it is the economy simulator's first job. |
| **O2** | **Event outcome weights and value scalars** for events 11–30. Several (26, 28, 30) can swing a run's rewards by >50%. | `19` Part A | Same dependency: run them through the simulator. |
| **O3** | **Weekly Challenge scoring formula.** Suggested `tiles×100 + kills×25 + boss×2000 − seconds`, but it may simply reward the strongest account. | `19` Part C | Needs a design pass so the leaderboard measures the run, not the profile. |
| **O4** | **Server cost model at scale.** ~2.4M requests/day at 10k DAU is trivially servable, but no actual estimate exists. | `14` §11 | Do this before committing to a hosting tier. |
| **O5** | **Push notification transport.** FCM + APNs directly is recommended over a wrapper, to stay lock-in-free. | `14` §12 | Small, but unowned. |
| **O6** | **Managed vs self-hosted Postgres** in production. | `14` §1.1 | A cost/ops choice, not an architectural one. Either satisfies the no-lock-in rule — and with D22, swapping is a one-adapter change. |
| **O16** | **Subscription display name.** Currently **Slay Plus**, derived from the title. Alternatives: "Repeat Plus" (fits the loop framing better), or plain "Plus" (shortest, and the store already shows the game name above it). | `12` §2, `00` §0a | Low stakes, but it appears on the store page, the Plus tab and the ladder tag — pick before store assets are produced. |
| **O17** | **"Idle" in the title vs D2 (no idle income).** The game is auto-battle, not idle-accrual. Store-search traffic from "idle RPG" may install and churn on discovering an active, Energy-gated 10-minute run loop. | `01` §1.1 | Keep the name; set expectations in the store's first line (*"Auto-battle roguelike. Roll, fight, loot, repeat."*). **Measure D1 retention by install source.** If idle-search installs churn markedly worse, fix listing copy and creative — not the name. |
| **O18** | **Bundle / package identifier** needs a studio or organisation prefix. Placeholder `com.<studio>.slayidlerepeat`. | `00` §0a | Blocking for the first store upload, trivial before then. |
| **O15** | **DI container vs hand-rolled composition root in the Godot client.** Godot's node lifecycle resists constructor injection into scenes. | `23` §9 | Recommendation: hand-rolled root with explicit factories. Scenes talk only to presenters; presenters receive ports from the root. Avoids a dependency and the engine's lifecycle sharp edges. |

## B2. Verify or resolve during production (5)

| # | Item | Doc | Note |
|---|---|---|---|
| **O7** | **Layered gear rigging.** AI-generated overlays will not naturally align to a shared skeleton. | `15` §E2 | Mitigation is specified (generate over a ghosted body, remove in post). Budget manual alignment for all 60 overlays; fallback is 20 composited looks with less mix-and-match. |
| **O8** | **VFX generation method.** Image models produce incoherent frame sequences. | `15` §G | Strong recommendation to abandon generated sprite sheets and animate procedurally in-engine. Not yet formally accepted. |
| **O9** | **Tutorial elite scripting.** Beat 6 needs the player left at ~25% HP regardless of build. | `19` Part D | Must be a tutorial-only enemy definition, never a hack in the combat loop. |
| **O14** | **Does the MAX Godot plugin expose server-side rewarded callbacks / `setUserId`?** Not mentioned in its public docs. Our whole ad-reward model grants server-side, because the client is not trusted with progression. | `12` §3.3 | **Verify on a spike build before writing the shim.** If absent: patch or fork the MIT-licensed plugin (the native layer is available), or fall back to client-asserted completion with a signed nonce plus server-side caps. The fallback is acceptable here only because every ad reward is hard-capped and nothing is purchasable. |
| **O13** | **Curse chapter gating.** The 12-curse catalogue is authored, but which curses appear in which chapters is only suggested. | `19` Part E | Suggested: 4 basic curses from Ch. 1, the rest from Ch. 3, `CUR_HUNTED` from Ch. 5. |

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
| **R6** | **Every economy number is unvalidated.** The pacing targets, drop rates, costs and curves throughout these documents are genre-informed estimates that have never been tested against each other. | — | The simulator (`21`) exists precisely to fix this. Until it has run, treat all 📐 TUNABLE numbers as placeholders that look precise. |

---

# PART D — Suggested Build Order

Derived from the dependency graph across all 24 documents.

1. **`SlayIdleRepeat.Core`** — RNG, stats, effect DSL (`18`), combat sim (`05`), board generation (`03`). Pure C#, unit-tested, no engine, no clock.
2. **`SlayIdleRepeat.Application` + the port catalogue** (`23` §4) and the **in-memory fake for every port**. Stand up `SlayIdleRepeat.Architecture.Tests` at the same time — the rules are far cheaper to enforce from commit one than to retrofit.
3. **Determinism CI test** across x64/ARM64. Everything downstream depends on this being true.
4. **Server skeleton** — containerised ASP.NET Core composition root, Postgres/Redis/MinIO adapters, `docker compose`, command protocol (`14` §2.3), idempotency, auth.
5. **Economy simulator** (`21`) — as soon as `Application` runs on in-memory adapters. Re-tune every 📐 number before content production begins.
6. **Godot client vertical slice** — one chapter, one boss, board + battle + draft + results, real network layer including reconnect. Presenters tested without booting the engine.
7. **Art anchor sheet + UI kit + Chapter 1 assets** (`15` §H order) — unblocks a genuinely playable slice.
8. **Meta systems** — gear/forge, talents, menagerie.
9. **Content fill** — chapters 2–8, all perks, events, quests as data.
10. **PvP** — ghosts, matchmaking, ladder, seasons.
11. **Ads + subscription adapters** — install the official MAX Godot plugin inside `Adapters.Ads.AppLovin`, write the GDScript↔C# shim, add `AutoGrantAdAdapter`, stand up the custom export-template build in CI, and wire the store adapters with server-side grants. Resolve **O14** (S2S callbacks) first. Note that everything before this step runs on `FakeRewardedAdAdapter` — the game is fully playable and testable with no ad SDK present.
12. **Audio** (`20`), localisation (EN+DE), accessibility.
13. **Balance harness, chaos tests, soft launch.**
