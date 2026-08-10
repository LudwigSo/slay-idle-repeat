# 11 — PvP: Ghost Duel & Ranking

🔒 LOCKED: **One PvP mode.** Asynchronous Ghost Duel — your build auto-battles a stored snapshot of another player's build. No real-time matchmaking, no netcode, no live opponents.

PvE is the main objective (`00_README_INDEX.md`, pillar P1). PvP exists to give accumulated PvE progress a competitive expression — it is a **scoreboard for the grind**, not a parallel progression track.

---

## 1. Why Ghost Duel is the right fit

| Requirement | How Ghost Duel satisfies it |
|---|---|
| Combat is fully automatic | No live inputs are needed, so async loses nothing |
| No pay-to-win | No power is purchasable, so the ladder measures play, not spend |
| Backend already exists | PvE is server-authoritative anyway (`14` §2), so ghosts, seeds and the ladder are incremental rather than a new system |
| Must not cannibalise PvE | Duels take 40 s and are capped daily; PvP rewards feed PvE |
| Must be fair across time zones | Async: no scheduled windows, no ping advantage |

---

## 2. The Ghost

A **Ghost** is an immutable snapshot of a player's PvP loadout, uploaded to the server.

```json
{
  "ghostId": "g_9f2a...",
  "playerId": "p_44c1...",
  "displayName": "Wanderer",
  "rating": 1487,
  "legendLevel": 74,
  "snapshotVersion": 3,
  "createdAtUtc": "2026-08-08T09:14:00Z",
  "build": {
    "heroBase": { "legendLevel": 74 },
    "gear": [ /* 6 gear instance payloads, fully resolved */ ],
    "pets": [ {"id":"PET_STORMFANG","level":42,"stars":3}, ... ],
    "mount": {"id":"MNT_STARHOOF","level":21},
    "talents": [ {"nodeId":"MT_WHETSTONE","rank":5}, ... ],
    "pvpPerks": ["PK_SYMBIOSIS","PK_LAST_STAND","PK_EXECUTIONER","PK_KEEN_EYE","PK_TOUGH_HIDE"],
    "pvpPerkPointsSpent": 10,
    "tier": "DIAMOND",
    "plus": true
  },
  "checksum": "fnv1a:..."
}
```

- The Ghost is **regenerated server-side** whenever the player changes their PvP loadout, and at minimum once per day on login. Since the server owns the profile, the client never submits stats — it submits *choices*, and the server builds the snapshot.
- Ghosts are **never** a live query against another player. They are static rows. This means a player can be duelled while offline, uninstalled, or asleep.
- A player's Ghost is used as an *opponent* for others; the player is notified of incoming defence results in a **Duel Log** but incoming defences never cost them anything. 🔒 **Losing as a defender must never reduce your rating.** Defence-loss penalties punish players for being popular and drive rank-hiding. Defence results are informational only.

---

## 3. The PvP loadout and Perk Budget

This is the mode's core strategic layer and the reason it isn't a pure stat check.

In PvE, perks are drafted randomly during a run. In PvP there is no run, so the player **assembles a fixed perk set in advance** from perks they have discovered in their Codex.

| Rule | Value |
|---|---|
| Perk slots | **5** |
| Budget | **10 Perk Points** 📐 |
| Cost by rarity | Common 1 · Rare 2 · Epic 3 · Legendary 5 |
| Eligibility | Any **combat** perk discovered in the Codex (see `06_PERKS.md` §6) |
| Tier | All PvP perks are locked to **Tier II** |
| Banned 🔒 | The **Economy** (10) and **Dice & Board** (12) categories are ineligible. Eligible pool: **68 perks.** |

🔒 **Decision:** the ban stands rather than converting non-combat perks into PvP equivalents. The metagame stays legible, balance stays tractable, and PvE remains the main objective — a fifth of the Codex being PvE-only is acceptable in a PvE-first game. Recorded in `16_DECISION_LOG.md` §A4 ("PvP perks").

A 10-point budget over 5 slots means the player cannot take five Legendaries — they must mix. Typical shapes: `2 Legendary` (10, only 2 slots used), `3 Epic + 1 Common` (10), `1 Legendary + 2 Rare + 1 Common` (10), `2 Epic + 2 Rare` (10). Filling all 5 slots always forces cheap picks, so a wide build and a tall build are genuinely different strategies. This makes the PvP metagame a genuine deckbuilding puzzle on top of accumulated power.

The player also chooses a separate **PvP talent preset**, **PvP gear loadout** and **PvP pet/mount set**, all stored independently from PvE.

Gear affixes with no combat meaning (`+X% Gold Gain`) are **skipped** in duels rather than converted. The `IS_PVP` condition in the effect DSL (`18` §4) is the mechanism.

---

## 4. Duel flow

```
ARENA screen
   ↓ tap DUEL
Matchmaking: server returns 3 candidate Ghosts within the rating band
   ↓ player picks one of the 3 (rating, name, Legend Level and tier label are shown)
   ↓ tap FIGHT
Client runs the deterministic sim (05_COMBAT_SIMULATION.md) locally
   ↓ <5 ms of computation, presented as a watchable battle up to 60 s (skippable)
RESULT: Victory / Defeat
   ↓ rating delta, Honor payout, duel log entry
   ↓ optional AD_DOUBLE_HONOR
back to ARENA
```

### 4.1 Attempts

| Property | Value |
|---|---|
| Free duels per day | 5 |
| Ad duels (`AD_EXTRA_DUEL`) | +2/day |
| Slay Plus subscribers | 7/day, auto-granted (identical to a full ad-watcher) |
| Attempt refresh | 05:00 UTC 📐 |
| Cost | Free — duels never cost Energy 🔒 |

Duels not costing Energy is important: PvP must never compete with PvE for the same resource, or players will feel taxed for engaging with it.

### 4.2 Choosing from 3 opponents

Offering three candidates instead of forcing one gives the player agency and lets them dodge a counter-build. It also makes the perk-budget metagame legible: you can see roughly what you're walking into (opponent rating, Legend Level, tier) and pick your fight.

### 4.3 The duel itself

- Both sides are simulated by `Simulate(duelSeed, attackerSnapshot, defenderSnapshot)`.
- `duelSeed` is issued by the server, not the client. This prevents seed-shopping.
- Both fighters are rendered side by side with their name, Legend Level and tier label.
- Duration cap: **60 s of simulated time** — a PvP-specific override of the simulator's 90 s default (`05` §3), set as `pvpMaxFightSeconds` in `data/combat_caps.json`. Duels are watched end to end far more often than PvE fights, so they are kept shorter. On timeout, the side with the higher remaining HP fraction wins. On an exact tie, the **lower-rated** player wins (a small underdog bias that prevents stagnation at the top).

---

## 5. Rating and ranks

### 5.1 Rating formula

A simplified Elo.

```
Expected(A)  = 1 / (1 + 10^((RatingB - RatingA) / 400))
Delta(A)     = K * (Result - Expected(A))     Result = 1 win, 0 loss

K = 40  if rating < 1400
    28  if 1400 <= rating < 1900
    20  if rating >= 1900

Rating floor: 800. Rating never drops below the floor of your current tier's entry.
```

🔒 **Only the attacker's rating changes.** The defender's Ghost result is recorded and shown to them, but their rating is untouched.

### 5.2 Tiers

| Tier | Rating band | Reward at season end |
|---|---|---|
| Bronze | 800–1099 | Soul Shards, Honor |
| Silver | 1100–1349 | ↑ scaling |
| Gold | 1350–1599 | ↑ + a gear chest |
| Platinum | 1600–1849 | ↑ + 1 Talent Point |
| Diamond | 1850–2099 | ↑ + 2 Talent Points |
| Master | 2100–2399 | ↑ + 3 Talent Points, Mount Crate |
| **Legend** | 2400+ | ↑ + 4 Talent Points, SS gear chest |

🔒 **No cosmetic rewards.** Tier is displayed as a text label beside the player's name, using the tier colour. There are no frames, badges, borders or die skins anywhere in v1 (decision D14).

### 5.2a The leaderboard 🔒

**Global, and everyone is on it.**

| Property | Value |
|---|---|
| Scope | One global ladder. No regional, country or friend boards. |
| Coverage | **Every ranked player appears**, from rank 1 to rank N. No cut-off. |
| Initial view | **Top 50**, refreshed every 10 minutes |
| Own position | **Always visible**, pinned as a sticky row at the bottom of the view, showing exact rank ("#48,201 of 312,904") |
| Navigation | Infinite scroll in both directions, a "jump to me" button, and a **search by player name** |
| Row contents | Rank, name, tier label, rating, Legend Level, Plus tag if applicable |
| Backing | Postgres with a rating index; rank computed by window function and cached for 10 minutes. At any realistic scale this is a single indexed query. |

Showing every player their exact rank — rather than hiding everyone below 100 — is the whole point. A player at #48,201 who climbs to #44,000 has visibly achieved something; a player told only "you are not in the top 100" has not.

### 5.3 Seasons

| Property | Value |
|---|---|
| Length | 14 days |
| Reset | Soft: `newRating = 1000 + (oldRating - 1000) * 0.6` |
| Rewards | Paid at season end by **peak tier reached**: Soul Shards, Honor, gear chests, Mount Crates and 1–4 Talent Points (see §5.2) |
| Exclusivity | **None.** Nothing in the game is permanently unobtainable. Every season pays the same reward structure. |

🔒 Season rewards are entirely **PvE-useful** (Soul Shards, Talent Points, gear chests), so PvP feeds the main loop rather than living beside it.

⚠️ **RISK — flagged, not resolved:** with cosmetics removed, a Legend-tier player's only lasting marker is a text label, and season rewards are functionally identical every season. Ladder retention past the first two seasons is untested and may be weak. The cheapest future fix is a small cosmetic layer (die skins), which was deliberately cut in decision D14 and can be reinstated without touching any other system. See `16_DECISION_LOG.md` R3.

---

## 6. Anti-cheat

Because the sim runs on the client, results must be verifiable.

| Layer | Mechanism |
|---|---|
| **Seed authority** | The server issues `duelSeed`. The client cannot choose it. |
| **Snapshot authority** | Both build snapshots come from the server's stored Ghosts (including the attacker's own last-uploaded Ghost — the client does **not** submit its own stats at duel time). |
| **Log hash** | The client returns `LogHash` and the outcome. The server re-runs the identical deterministic sim and compares. Mismatch → result discarded, flag incremented. |
| **Server sim** | The same `SlayIdleRepeat.Core` assembly the client uses, running on the server. This is why engine-independence is mandatory (`05` §Intro, `14` §4.1). |
| **Rate limiting** | Max 7 duel results per player per day, matching the attempt cap. |
| **Progression validation** | ✅ **Largely obsolete.** Because PvE is server-authoritative (`14` §2), a player's power was accumulated under server supervision — there is no such thing as an implausible ghost, because there is no way to fabricate progress. The plausibility job remains as a cheap backstop for exploits in the game's own rules. |

Sanctions: shadow-exclusion from the leaderboard first, account action only on repeated, confirmed manipulation.

✅ Backend technology is decided: a **containerised ASP.NET Core application**, portable across hyperscalers and self-hosting. See `14_TECHNICAL_ARCHITECTURE.md` §1 and §4.

---

## 7. Honor and the Honor Shop

| Source | Honor |
|---|---|
| Duel win | 100 + (0.05 × opponent rating) |
| Duel loss | 30 |
| Successful defence (informational) | 15, capped at 10/day |
| Season rank reward | 2,000–25,000 |

| Honor Shop item | Cost |
|---|---|
| Pet Egg | 4,000 |
| Mount Crate | 9,000 |
| S Gear Chest | 6,500 |
| SS Gear Chest (weekly stock: 1) | 18,000 |
| Beast Feed ×500 | 1,200 |
| Enhance Stones ×200 | 1,500 |
| Merge Dust ×1,000 | 1,800 |

Honor buys **materials and collection progress only** — no power that cannot also be earned in PvE, and no cosmetics (there are none). The Honor Shop's role is to make duelling a legitimate alternative farming route for players who enjoy it, not a mandatory one.

---

## 8. Onboarding into PvP

- Unlocks at **Legend Level 10** (day 1 for most players).
- First entry runs a scripted duel against a fixed tutorial Ghost, tuned to be a guaranteed win, to teach the perk-budget screen.
- The first 5 duels of a new account are matched against bot Ghosts with authored builds at the player's power level, so a new player's first PvP experience is never a wall.
- A permanent tooltip on the Arena screen states plainly: **"No power in this game can be bought. Every opponent got here by playing."** This is a differentiator and should be said out loud.
- The candidate cards show rating, name, Legend Level and tier — enough to choose a fight, never enough to feel like a profile page.
