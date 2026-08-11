# 26 — Live-Ops & Limited-Time Events

🔒 **Decision D25: a data-driven limited-time-event framework ships in v1, with one launch event live on day 1.**

The game as previously specified had exactly one recurring beat outside the daily loop: the Weekly Chapter Challenge. That is a thin live-ops surface for a genre whose retention is built on a rolling event calendar, and it is the main reason the day-30-to-day-60 window looked empty.

This document specifies the **framework** first and the **content** second, deliberately. The framework is the deliverable; events are data.

---

## 1. Constraints

| # | Constraint |
|---|---|
| **C1** | **No app update to run an event.** An event is a JSON package plus existing art. If shipping an event requires a client build, the framework has failed. |
| **C2** | **Nothing is permanently unobtainable.** 🔒 Inherited from `11` §5.3. Every reward in an event shop must also exist in a permanent source. Event *timing* is exclusive; event *content* never is. |
| **C3** | **No purchasable event anything.** No event currency for money, no event pass, no paid tier, no bundle. (`12` §8) |
| **C4** | **Event currency never strands.** Unspent event currency auto-converts to Crowns at event end, at a published rate. A player who misses the last day loses nothing. |
| **C5** | **No FOMO pressure surfaces.** No countdown popups, no "last chance" modals, no push spam. One quiet banner and one badge. This is the same rule that governs the Plus offer (`12` §2.4) and it applies with equal force here. |
| **C6** | **Events reuse existing content.** New biomes, enemies, bosses and music are out of scope for an event. Palette shifts, modifier stacks and reward tracks are in scope. |
| **C7** | **Luck protection applies.** Every randomised event grant belongs to a source class and obeys `24_LUCK_PROTECTION.md`. |
| **C8** | **Killable.** Every event is behind a remote-config flag and can be terminated mid-flight without a client patch, with automatic reward reconciliation. |

---

## 2. The event package

An event is one JSON document validated against `SlayIdleRepeat.Data/schema/event.schema.json` and served from the content endpoint (`14` §6).

```json
{
  "id": "EVT_EMBERFALL_2026_10",
  "type": "EVENT_CHAPTER",
  "displayName": "loc.event.emberfall.name",
  "windowUtc": { "start": "2026-10-06T05:00:00Z", "end": "2026-10-20T05:00:00Z" },
  "unlock": { "legendLevel": 12 },

  "board": {
    "baseChapter": 4,
    "paletteId": "pal_ember_event",
    "stageLengths": [12, 14, 16],
    "tileWeightOverrides": [ { "TILE_ELITE": 8, "TILE_CURSE": 10 }, {}, {} ],
    "modifiers": ["MOD_DOUBLE_ELITES", "MOD_RICH"],
    "powerScalar": 1.0,
    "tierLadder": "MATCH_PLAYER_HIGHEST"
  },

  "currency": { "id": "EVC_EMBER", "displayName": "loc.event.emberfall.currency",
                "conversionAtEnd": { "to": "CROWNS", "rate": 25 } },

  "earn": [
    { "source": "EVENT_RUN_CLEAR",   "amount": 300 },
    { "source": "EVENT_ELITE_KILL",  "amount": 20  },
    { "source": "EVENT_BOSS_KILL",   "amount": 150 },
    { "source": "DAILY_FIRST_CLEAR", "amount": 500, "cap": "1/day" }
  ],

  "track": [
    { "points": 500,   "reward": { "crowns": 3000 } },
    { "points": 1500,  "reward": { "chest": "CHEST_STANDARD" } },
    { "points": 3000,  "reward": { "enhanceStones": 300 } },
    { "points": 6000,  "reward": { "chest": "CHEST_PREMIUM" } },
    { "points": 10000, "reward": { "soulShards": 900 } },
    { "points": 15000, "reward": { "talentPoints": 2 } },
    { "points": 22000, "reward": { "chest": "CHEST_APEX" } },
    { "points": 30000, "reward": { "setTokens": 4 } }
  ],

  "shop": [
    { "item": "PET_EGG",        "cost": 4000, "stock": 3 },
    { "item": "MOUNT_CRATE",    "cost": 9000, "stock": 1 },
    { "item": "BEAST_FEED_500", "cost": 800,  "stock": 10 },
    { "item": "SET_TOKEN",      "cost": 3000, "stock": 4 }
  ],

  "leaderboard": null,
  "featureFlag": "event.emberfall"
}
```

### 2.1 Field rules

| Field | Rule |
|---|---|
| `type` | One of the three archetypes in §3. Determines which other fields are required. |
| `board.baseChapter` | **Must** reference an existing chapter. C6 — no new biome. |
| `board.tierLadder` | `MATCH_PLAYER_HIGHEST` scales event enemy power to the player's own highest cleared chapter, so the event is equally relevant on day 12 and day 120. This is the single most important field in the package. |
| `board.modifiers` | Drawn from the 14 weekly modifiers in `19` Part C, obeying the same combination rules. Events do not invent modifiers. |
| `currency.conversionAtEnd` | **Required.** C4. |
| `track` | Strictly ascending, ≤ 12 milestones. The final milestone must be reachable by a player of average engagement in ~70% of the window — validated by the simulator (§7). |
| `shop.stock` | Per-player, not global. Global stock creates a race, which is a FOMO surface (C5). |
| `shop.item` | Every entry must resolve to an item ID that exists in a permanent source. Build-time validation enforces C2. |
| `leaderboard` | `null` or a scoring config. Optional by design — most events should not have one. |
| `featureFlag` | **Required.** C8. |

---

## 3. The three event archetypes

Three is the whole taxonomy. A fourth archetype is a new feature, not a new event.

### 3.1 `EVENT_CHAPTER` — the workhorse

A re-skinned existing chapter with a modifier stack, its own currency, its own track and its own shop. Runs cost normal Energy and count for daily quests.

- **Cadence:** the default. Roughly 3 of every 4 events.
- **Player promise:** *"a familiar board, twisted, that pays a currency you spend on a track."*
- **Why it works:** the modifier stack changes the optimal build, so a player's existing account is re-contextualised rather than re-ground.

### 3.2 `EVENT_SCORE_RUSH` — the competitive one

A single fixed `(chapter, tier, seed, modifier set)` shared by every player, scored on a leaderboard. This is the Weekly Chapter Challenge (`19` Part C) **promoted into the event framework** — the Weekly Challenge stops being a bespoke system and becomes an `EVENT_SCORE_RUSH` package on a 7-day window.

✅ This resolves open item **O3** structurally: the scoring formula becomes a field on the event package (`leaderboard.formula`), tunable per event and per week, rather than one hardcoded formula that must work for every case forever.

- **Cadence:** weekly, always running.
- **Scoring default:** `tilesCleared × 100 + enemiesKilled × 25 + bossKilled × 2000 − secondsElapsed`, with the caveat from `19` Part C still standing: this may simply reward the strongest account. The framework's answer is that scoring is now data, so it can be corrected weekly on live evidence instead of guessed once.
- **Rewards:** by percentile band, not by absolute rank, so the reward experience is identical at 10,000 players and at 1,000,000.

### 3.3 `EVENT_COLLECTION` — the low-pressure one

Tokens drop from ordinary play across **all** modes (chapter runs, dungeons, duels). Collect N to fill a card album; completed rows pay out.

- **Cadence:** roughly 1 in 4, deliberately overlapping an `EVENT_CHAPTER` so there is always something for a player who does not want to change what they are doing.
- **Player promise:** *"keep playing exactly as you were, and a second progress bar fills."*
- **Why it matters:** it is the only event type that requires zero behaviour change, which makes it the right thing to run over holidays and during content droughts.
- **Luck protection:** token→card assignment uses `LuckService` with duplicate protection — an unowned card is always favoured while any unowned card remains (`24` §1, M1).

---

## 4. The calendar

🔒 **Exactly one `EVENT_CHAPTER` or `EVENT_COLLECTION` is live at any time, plus the always-on weekly `EVENT_SCORE_RUSH`.** Two simultaneous major events split attention and make both feel like chores.

```
Week:   1    2    3    4    5    6    7    8
CHAPTER [=========]         [=========]
COLLECT           [=========]         [====
SCORE   [==][==][==][==][==][==][==][==]
PvP SEASON  [==============][==============]
```

| Rule | Value |
|---|---|
| Major event length | **14 days** 📐 |
| Gap between major events | **0 days** — the next begins as the previous ends, at 05:00 UTC |
| Offset from PvP seasons | **7 days.** 🔒 Seasons and events must never end on the same day, or the reward-claim moment collapses into one overwhelming screen and the two systems compete for the same evening. |
| Score Rush | 7 days, rolling, always on |
| Announcement | The next event appears as a dated card on the Events screen **3 days** before it starts. No push, no popup. |

---

## 5. The launch event

**`EVT_EMBERFALL`** — an `EVENT_CHAPTER`, live from day 1, 14 days.

| Property | Value |
|---|---|
| Base chapter | **4 — Emberpeak** (`03` §4). Chosen because its lava/obsidian palette recolours convincingly and its burn-floor signature mechanic reads clearly under a modifier stack. |
| Palette | `pal_ember_event` — a hotter, higher-contrast variant of `pal_ember`. **One new palette file, no new art.** |
| Modifiers | `MOD_DOUBLE_ELITES` + `MOD_RICH` — Risk/Reward paired with Risk/Reward, satisfying `19` Part C's combination rules and giving the event an identity ("greedy and dangerous") rather than just being harder |
| Currency | **Emberdust** (`EVC_EMBER`), converts to Crowns at 25:1 at close |
| Unlock | Legend Level **12** — after the Forge (8) and PvP (10), so a new player is not handed a fourth system in their first hour |
| Track | 8 milestones, terminating in 4 Set Tokens — one third of an SS piece (`24` §5.1), which is a genuinely exciting terminal reward that costs the economy nothing it does not already have |
| Leaderboard | **None.** A launch event should not also be a competition. |

⚠️ **NEEDS DETAIL:** the track's point thresholds are shaped, not validated. They must go through the simulator (§7) against the day-1-to-day-14 player, who is the only player who exists during the launch event.

---

## 6. Screens

| # | Screen | Purpose |
|---|---|---|
| **S30** | **Events Hub** | Live events as cards; each shows its remaining window as a plain date, its track progress bar, and an ENTER button. Upcoming events appear as dated grey cards. No countdown timers below 24 hours (C5). |
| **S31** | **Event Track** | The milestone ladder with claimed / claimable / locked states, the player's current point total, and a single line stating how the currency is earned. |
| **S32** | **Event Shop** | Stock list with per-player remaining counts, prices in event currency, and a permanent line: *"Everything here can also be earned in the Honor Shop or from Soul Shards."* — C2, said out loud. |

**Entry point:** a card on the Home screen between the daily-quest strip and the Continue CTA, present **only** while an event is live. When no event is live the row does not exist — no empty state, no teaser.

The Events Hub also absorbs the Weekly Chapter Challenge, which no longer needs its own surface.

---

## 7. Impact on the economy simulator

| # | Requirement |
|---|---|
| **E11** | Model a rolling event calendar over the full 180 simulated days, not a single event. Events are permanent income once the framework exists, and modelling one event underestimates their economic weight by an order of magnitude. |
| **E12** | New assertion: **event income supplies 15–30% of a mid-game player's total material income.** Below 15% events are decorative; above 30% the core loop is devalued and players will feel obliged to play events they do not enjoy. |
| **E13** | New assertion: **a player who ignores every event entirely still reaches all `01` §7 milestones within 1.25× the time of a player who plays every event.** Events are an accelerator and a change of pace, never a requirement. This is the event-framework equivalent of the 45% ad-fairness gap. |
| **E14** | Validate every launch-event track threshold against the day-1 cohort specifically. |
| **E15** | Model `conversionAtEnd` leakage — Crowns arriving in a lump at every event close is a real inflationary event that the Crown sink curve must absorb. |

---

## 8. Operational requirements

Events turn the backend from a service you run into a service you **operate**. That cost is real and is accepted here.

| Concern | Requirement |
|---|---|
| Authoring | Events are authored as JSON, validated by the same build-time schema validator as all other content (`14` §6), and reviewed like code. **No event ships without passing the validator.** |
| Deployment | Event packages are served from the content endpoint and picked up on the client's 6-hour config cache cycle, or immediately at session start. |
| Mid-flight kill | Setting `featureFlag` false ends the event at once. All banked event currency converts per `conversionAtEnd` in the same transaction. Track milestones already earned are **never** clawed back. |
| Timezone | All windows are UTC. Start and end at 05:00 UTC, matching every other daily reset in the game. |
| Clock skew | Event membership is decided **server-side** per request. The client's clock is never consulted, so device-clock manipulation does nothing. |
| Telemetry | New events: `event_viewed`, `event_entered`, `event_run_completed`, `event_currency_earned`, `event_milestone_claimed`, `event_shop_purchased`, `event_ended_unspent` (the leakage metric behind E15). |
| Push | At most **one** notification per event, at start, opt-in, respecting the existing 1/day cap (`14` §12). **None at event end** — an end-of-event push is a FOMO surface and violates C5. |
| Rollback | Event packages are versioned. A corrected package with the same ID supersedes the previous one; already-granted rewards are never revoked. |

---

## 9. What this framework does not do

Recorded so the pressure to add each of these has a documented "no" to point at.

- ❌ **No event battle pass**, free or paid. The track *is* the pass, and it is free.
- ❌ **No event-exclusive gear, pets, mounts or perks.** C2.
- ❌ **No login-streak events.** The daily login calendar (`02` §9) already occupies that slot; a second one is manipulation, not content.
- ❌ **No event that shortens or bypasses a pity counter.** `24` §2.
- ❌ **No new biome, boss, enemy or music per event.** C6. An event that needs new art is a content update, and should be shipped as a chapter.
- ❌ **No stacking major events.** §4.
- ❌ **No countdown timers under 24 hours anywhere in the UI.** C5.
