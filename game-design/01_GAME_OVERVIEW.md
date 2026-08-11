# 01 — Game Overview

## 1. One-paragraph pitch

**Slay. Idle. Repeat.** is a portrait-mode, always-online mobile roguelike RPG. You roll a die to move your chibi hero along a winding fantasy board. Landing on a tile triggers an event — an automatic battle, a shrine, a shop, a trap, a treasure chest, a minigame. Win a fight and you draft one of three perks, stacking a build that only lasts this run. Reach the end of the board and you fight the chapter boss. Win or die, you carry home gear, pets, currency and Legend XP, which feed a permanent progression spine of merged equipment, a branching talent tree, and a stable of pets and mounts. When you want to measure yourself against other people, you send your build into an asynchronous Ghost Duel and climb a seasonal ladder where every player has a visible rank. **No power in the game can be bought with money** — the only paid product is a subscription that removes ads and grants their rewards automatically, which anyone can match for free by watching.

### 1.1 What the title promises, and whether the game delivers it

The name is a three-beat description of the loop, and each beat maps to a real system:

| Beat | The system it names | Honest? |
|---|---|---|
| **Slay.** | Auto-battle combat — 18–22 fights per run, watched not played | ✅ Yes |
| **Idle.** | Combat resolves itself. The player never taps during a fight. | ⚠️ **Partly — see below** |
| **Repeat.** | Run-based structure with a permanent meta spine; the run resets, you don't | ✅ Yes, and it is the strongest beat |

⚠️ **A genuine tension worth deciding on deliberately.** "Idle" in this genre normally means **offline accrual** — the game earns while you are away. Locked decision D2 explicitly rules that out: there is no idle income, no AFK stage, no offline progression beyond Energy regeneration. What the game actually has is *auto-battle*, which is idle-**adjacent** and is commonly marketed under the idle banner, but it is not what a player who searches "idle RPG" expects to find.

Two ways this resolves, and they point in opposite directions:

1. **Accept it as intended.** The title is a joke about the loop, not a genre claim. Auto-battle plus a heavy meta spine is close enough to the idle audience's expectations that store-search traffic is a net win. The store copy leans on "auto-battle" so nobody is misled past install.
2. **Treat it as a store-listing risk.** Players arriving from an "idle" search install, discover an active 10-minute run loop with an Energy gate, and churn on day 1 — and D1 retention is where this whole business lives (`12` §9).

**Recommendation:** keep the name — it is memorable, it describes the loop truthfully to anyone who reads a screenshot, and "Repeat" is doing more work than "Idle" — but write the store's first line to set the expectation immediately. Something like *"Auto-battle roguelike. Roll, fight, loot, repeat."* Then measure D1 retention by install source; if idle-search installs churn markedly worse than the rest, the fix is listing copy and creative, not a rename.

Logged as **O17** in `16_DECISION_LOG.md`.

---

## 2. Player fantasy

> *"I am a small, determined adventurer whose luck gets better every week."*

The emotional arc the design chases, in order:

1. **Session start (10s):** "I'm a bit stronger than yesterday." — visible from equipped gear and Legend Level.
2. **Mid-run (3–6 min):** "This build is turning into something." — perk synergies clicking.
3. **Run climax (8–12 min):** "I might actually kill this boss." — a tense, unskippable auto-battle the player watches.
4. **Post-run (30s):** "I got the thing." or "I got closer to the thing." — a drop, a merge, a talent point.
5. **Weekly:** "I moved up the ladder." — season rank.

## 3. Target audience

| | |
|---|---|
| Primary | 22–45, plays idle/roguelike hybrids on commute and in bed. Familiar with *Legend of Slime*, *Archer Forest*, *Magic Survival*, *Top Heroes*. |
| Session length | 10–25 minutes, 2–4 sessions/day |
| Motivation profile | Completion + accumulation first, competition second, expression third |
| Key frustration we solve | These players like the genre but resent the paywall. Slay Idle Repeat is the same loop with the wallet removed. |
| Key friction we accept | **The game requires a connection.** This is stated plainly in the store listing and on first launch. |

**Store listing, first line** — sets the genre expectation immediately, per §1.1 / O17:
> *Auto-battle roguelike. Roll, fight, loot, repeat.*

**Positioning line (the paragraph underneath):**
> *All the grind you love. None of the paywall. Watch an ad if you want a boost — or subscribe, never see an ad again, and get every boost automatically. Nothing here can be bought that you cannot earn.*

**Requirement disclosure**, stated plainly and early rather than discovered:
> *Requires an internet connection.*

## 4. Reference games and what we take from each

| Game | What Slay Idle Repeat takes | What Slay Idle Repeat rejects |
|---|---|---|
| **Rogue Legend – Roguelike RPG** | The core identity: dice-driven board movement, tile events, auto-battles with a 1-of-3 perk draft, minigame tiles, shop tiles, layered meta progression (gear, talents, pets, mounts) | Its monetization: gear-acquisition acceleration and power scaling sold for money |
| **Legend of Slime: Idle RPG** | Chibi silhouette language, the "always one more upgrade" meta layering, generous-feeling reward cadence, readable phone-size UI | Idle/offline income, gacha spend loops |
| **Top Heroes: Kingdom Survival** | Saturated, glossy, high-contrast UI treatment; the sense of a big collection | Kingdom-building, PvP-driven spend pressure, roster gacha |
| **Magic Survival** | Build-defining stacking upgrades whose combinations matter more than raw stats | Real-time twitch control (our combat is auto) |
| **Archer Forest: Idle Defense** | Stat-transparent progression: the player can always read exactly why they got stronger | Idle defense structure |

## 5. Core loop at three time scales

```
┌─ MINUTE LOOP (20–60 s) ────────────────────────────────────┐
│  Roll die → move N tiles → resolve tile → (if battle) watch │
│  auto-battle → draft 1 of 3 perks → repeat                  │
└─────────────────────────────────────────────────────────────┘
              ↓ x25-35 tiles
┌─ SESSION LOOP (8–12 min per run) ──────────────────────────┐
│  Spend 20 Energy → Stage 1 → Stage 2 → Stage 3 → Boss →     │
│  Victory or Death → Reward screen → bank Crowns, Soul       │
│  Shards, Gear, Legend XP                                    │
└─────────────────────────────────────────────────────────────┘
              ↓ x5-14 runs/day
┌─ META LOOP (days/weeks) ───────────────────────────────────┐
│  Merge & enhance gear → spend Talent Points → level and     │
│  ascend pets → unlock the next Chapter → raise difficulty   │
│  tier → update your PvP Ghost → climb the season ladder     │
└─────────────────────────────────────────────────────────────┘
```

## 6. Content scope for v1 (launch)

| Content | v1 target |
|---|---|
| Chapters (biomes) | 8 |
| Difficulty tiers per chapter | 3 (Normal / Heroic / Mythic) |
| Tile types | 14 |
| Perks (run-scoped) | 98 (90 standard + 8 cursed) |
| Talent nodes | 60 (3 branches × 20) |
| Gear families | 4 per slot × 6 slots = 24 base items, × 5 rarities |
| Pets | 24 |
| Mounts | 12 |
| Enemy archetypes | 8 base, reskinned per biome (64 visual variants) |
| Elites | 16 |
| Bosses | 8 (one per chapter) |
| Minigames | 4 |
| PvP modes | 1 (Ghost Duel) |
| Resource Dungeons × tiers | 3 × 8 |
| Live event archetypes / launch events | 3 / 1 |
| Guilds | 30 players, 15 guild quests, 8-boss weekly rotation, 60 phrases |
| Rewarded ad placements | 29 |
| Paid products | 1 (Slay Plus subscription) |
| Cosmetics | 0 — cut from v1 |
| Event cards | 30 |
| Daily quests | 20 |
| Weekly challenge modifiers | 14 |
| Launch languages | 2 (EN, DE) |
| Art assets to generate | 975 |
| Audio assets to generate | 106 |

📐 TUNABLE: Content counts are the launch target. The systems must scale to 2–3× these numbers via data files without code changes.

## 7. Estimated progression pacing

Design target for a **free player who does not watch ads**:

| Milestone | Target time-to-reach |
|---|---|
| Clear Chapter 1 Normal | 15 minutes (first session) |
| Unlock PvP (Legend Level 10) | Day 1, ~45 min |
| Clear Chapter 4 Normal | Day 4–6 |
| First S-rarity weapon | Day 8–12 |
| Clear Chapter 8 Normal (content complete) | Day 25–35 |
| Clear Chapter 8 Mythic (mastery) | Day 90+ |

A player who watches most available ads should reach these ~40–50% faster. A Slay Plus subscriber reaches them at the same rate as a full-ad-watcher, without the ads. **The ad-watcher and the payer must land on the same curve — that is the fairness contract.** See `12_MONETIZATION_ADS.md` §1.

⚠️ **UNVALIDATED:** These pacing targets are estimates derived from genre norms, not from a simulated economy. The simulator that will validate them is fully specified in `21_ECONOMY_SIMULATOR_SPEC.md` and must be built and run before any of these numbers is treated as real. Expect several of them to move.

## 8. Success criteria (what "done" means for v1)

The implementation is complete when all of the following hold:

1. A player can install, play a full run of every chapter at every difficulty tier, and progress through all meta systems.
2. The combat simulator is deterministic: the same `(seed, buildSnapshot)` produces a byte-identical battle log on device and on server, verified by CI across x64, Android ARM64 and iOS ARM64.
3. Losing connection mid-run never loses progress: the run resumes exactly where it stopped, on any device, for up to 48 hours.
4. Every rewarded ad placement in `12_MONETIZATION_ADS.md` is implemented, capped, granted server-side, and correctly auto-granted for Slay Plus subscribers.
5. **No code path exists by which real money produces combat power**, and a lapsed subscriber loses nothing they had earned.
6. All game content lives in data files and can be changed server-side without an app store update.
7. A full run at 60 FPS on a 2021 mid-range Android device (Snapdragon 695 class) with < 400 MB RAM.
8. The whole backend stack boots from `docker compose` on a laptop with no cloud account.
9. The economy simulator (`21`) passes all 16 named assertions and 23 inherited requirements, including the 45% fairness gap (A3).
