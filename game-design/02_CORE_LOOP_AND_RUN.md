# 02 — Core Loop & Run Structure

🔒 LOCKED: Run-based only. There is **no** idle income, **no** offline accrual, **no** AFK stage. If the app is closed, nothing accumulates except Energy regeneration (see `10_ECONOMY_AND_PROGRESSION.md` §3).

---

## 1. Run definition

A **Run** is one attempt at one `(Chapter, DifficultyTier)` pair.

| Property | Value |
|---|---|
| Energy cost | 20 📐 TUNABLE |
| Target duration | 8–12 min (Normal), 10–15 min (Heroic/Mythic) |
| Board length | 3 stages: 12 / 14 / 16 tiles + 1 boss tile = **43 tiles** 📐 TUNABLE |
| Expected battles per run | 18–22 (enemy + elite + boss) |
| Expected perk drafts per run | equal to battles won |
| End conditions | Boss defeated (**Victory**) · Hero HP reaches 0 (**Death**) · Player forfeits (**Abandon**) |
| Persistence | Run state lives on the server for 48 h. Closing the app, losing connection or switching device resumes the run exactly. See `14` §3. |

### 1.1 Run state machine

```
        ┌──────────────┐
        │  RUN_SETUP   │  choose chapter + tier, confirm energy, load loadout
        └──────┬───────┘
               ↓
        ┌──────────────┐
   ┌───▶│  AWAIT_ROLL  │◀──────────────────────────┐
   │    └──────┬───────┘                           │
   │           ↓ player taps ROLL                  │
   │    ┌──────────────┐                           │
   │    │   ROLLING    │  die animation, ~0.8 s    │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ REROLL_PROMPT│  optional, if charges > 0 │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │    MOVING    │  hop N tiles, ~0.25 s each│
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ RESOLVE_TILE │  branch by TileType       │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   │    │ (BATTLE) →   │  see 05_COMBAT_SIMULATION │
   │    │ (PERK_DRAFT) │  see 06_PERKS             │
   │    │ (SHOP/EVENT/ │                           │
   │    │  MINIGAME…)  │                           │
   │    └──────┬───────┘                           │
   │           ↓                                   │
   │    ┌──────────────┐                           │
   └────┤ CHECK_STATE  │ alive & tiles remain? ────┘
        └──────┬───────┘
               ↓ boss dead OR hp<=0
        ┌──────────────┐
        │ DEATH_PROMPT │  offer ad-revive (once/run) → back to AWAIT_ROLL
        └──────┬───────┘
               ↓
        ┌──────────────┐
        │ RUN_RESULTS  │  reward tally, ad-double offer, bank to save
        └──────────────┘
```

### 1.2 Stage transitions

Reaching the last tile of a stage triggers a **Stage Gate**:

- Full-screen banner: *"STAGE 2 — The Ashen Mire"*
- Hero restores **15%** of Max HP 📐 TUNABLE
- Reroll charges refresh to base value
- Perk draft rarity weights shift upward (see `06_PERKS.md` §4)
- Enemy power multiplier steps up (see §4 below)
- Autosave checkpoint written

Stage gates are the pacing heartbeat of the run. They are also the natural place for a non-intrusive interstitial ad for non-payers — see `12_MONETIZATION_ADS.md` §6.

---

## 2. Run setup

On the Chapter Select screen the player picks:

1. **Chapter** (1–8, unlocked linearly by clearing the previous chapter on Normal)
2. **Difficulty Tier** (Normal always; Heroic unlocked by clearing Normal; Mythic by clearing Heroic **and** reaching Legend Level 60 — see `10` §7)
3. Confirms their **Loadout**: equipped gear (6 slots), equipped pets (up to 3), equipped mount (1). Loadout is changed on the Hero screen, not here.

The run then generates a **Run Seed**:

```
runSeed = Hash64(playerId, chapterId, tierId, utcUnixSeconds, runCounter)
```

The seed is stored in run state. All board generation, drop rolls, draft rolls and battle simulation derive from child streams of this seed (see `14_TECHNICAL_ARCHITECTURE.md` §8.1). This makes runs reproducible for bug reports and makes anti-cheat validation possible.

**Anti-reroll rule:** the seed is committed *before* the board is shown. The player cannot abandon and re-enter to fish for a better board without paying the Energy cost again.

---

## 3. Turn structure in detail

One "turn" = one die roll and its full resolution.

| Step | Duration | Player input |
|---|---|---|
| 1. Roll | 0.8 s | Tap the die (or tap-and-hold to see face preview) |
| 2. Reroll decision | up to 4 s, skippable | Optional tap. Costs 1 Reroll Charge. Ad-reroll available (2/run). |
| 3. Movement | 0.25 s × pips | None (auto). Tap to speed up. |
| 4. Fork choice | up to 6 s | If the landed segment has a fork, choose branch. |
| 5. Tile resolution | 2 s – 40 s | Depends on tile type |
| 6. Perk draft | up to 15 s | Choose 1 of 3. Reroll available. |

**Speed controls:** the player can set battle speed to ×1 / ×2 / ×3 and toggle "Auto-advance" which auto-confirms non-choice screens. Auto-advance never auto-picks perks — the draft is the game's main decision and must always be manual.

📐 TUNABLE: All durations above.

---

## 4. Difficulty and power targets

Difficulty is expressed as a single scalar, **Power**, so that content authoring stays simple.

### 4.1 Chapter power targets

```
ChapterPowerTarget(c) = 1000 * 2.0^(c-1)      // c = 1..8
```

| Chapter | Power target |
|---|---|
| 1 | 1,000 |
| 2 | 2,000 |
| 3 | 4,000 |
| 4 | 8,000 |
| 5 | 16,000 |
| 6 | 32,000 |
| 7 | 64,000 |
| 8 | 128,000 |

### 4.2 Difficulty tier multipliers

| Tier | Enemy power × | Reward × | Unlock |
|---|---|---|---|
| Normal | 1.0 | 1.0 | default |
| Heroic | 4.0 | 2.5 | clear the chapter on Normal |
| Mythic | 16.0 | 6.0 | clear the chapter on Heroic **and** Legend Level 60 |

### 4.3 In-run ramp

Within a run, enemy power ramps with tile index `i` (0-based, across the whole board):

```
EnemyPower(i) = ChapterPowerTarget(c) * TierMult(t) * (1 + 0.035 * i) * StageMult(s)

StageMult:  Stage1 = 1.00, Stage2 = 1.15, Stage3 = 1.35, Boss = 2.20
```

Enemy stat derivation from Power is defined in `05_COMBAT_SIMULATION.md` §6.

### 4.4 Player Power

The player's own Power is a display-only aggregate used for matchmaking, chapter recommendations and the "you may be too weak" warning:

```
PlayerPower = (EffectiveHP * 0.5) + (DPS * 10)

EffectiveHP = MaxHP / (1 - AvgMitigation) * (1 + Dodge) * (1 + Lifesteal*2)
DPS         = ATK * ASPD * (1 + Crit * CritDmg)
```

📐 TUNABLE. The formula must live in data so it can be re-weighted without a client patch.

If `PlayerPower < 0.7 × ChapterPowerTarget × TierMult`, show a soft warning on the chapter confirm dialog. Never block the player — let them try.

---

## 5. Run rewards

### 5.1 Reward sources during a run

| Source | Drops |
|---|---|
| Normal enemy | Gold (run-local), small Legend XP |
| Elite enemy | Gold, Legend XP, **1 guaranteed gear item** |
| Boss | Gold, large Legend XP, **Soul Shards**, gear (rarity-weighted), chapter-first-clear bonus |
| Treasure tile | Crowns, Enhance Stones, Merge Dust |
| Minigame tile | Variable: Gold, Crowns, Beast Feed, or a perk |
| Event tile | Variable, sometimes a choice with a cost |

**Gold** is run-local and vanishes at run end. It exists only to be spent at Shop tiles. This keeps in-run economy decisions crisp and prevents "hoard gold, never spend" behaviour.

### 5.2 Run-end payout

```
FinalPayout = BankedRewards * CompletionMultiplier * AdDoubleMultiplier

CompletionMultiplier:
    Victory (boss killed)      = 1.00
    Death in Stage 3           = 0.60
    Death in Stage 2           = 0.40
    Death in Stage 1           = 0.25
    Abandon                    = 0.10   (and no gear drops are kept)

AdDoubleMultiplier = 2.0 if the player watches the run-end rewarded ad, else 1.0
                     (auto-applied for Slay Plus subscribers)
```

🔒 **Death must still pay.** A dead run that yields nothing is the single fastest way to make a grind feel hostile. Even a Stage-1 death returns 25% and always returns any gear already picked up during the run.

### 5.3 First-clear bonuses

Each `(Chapter, Tier)` pair pays a one-time First Clear bonus: a fixed Soul Shard grant, a guaranteed gear item one rarity above the normal chapter cap, and (for Normal tier) the unlock of the next chapter.

---

## 6. Death, revive and the ad contract

On reaching 0 HP:

1. Time freezes on the battle screen; a "DEFEATED" overlay slides in.
2. If the player has not yet revived this run, offer **Revive** (see `12_MONETIZATION_ADS.md`, placement `AD_REVIVE`):
   - Restores 50% Max HP 📐 TUNABLE
   - Grants 2 s of invulnerability on resume
   - The battle **restarts from the beginning of that battle**, not mid-fight — this keeps the deterministic sim clean.
   - Limit: **once per run**, hard.
   - Slay Plus subscribers get a "REVIVE" button that resolves instantly with no ad.
3. If declined or already used, go to `RUN_RESULTS` with the Death completion multiplier.

🔒 **Revive works everywhere, including bosses.** The boss is exactly where a player most wants the safety net, it is the highest-value rewarded-ad moment in the game, and dying at 5% boss HP with no recourse is the kind of moment that ends sessions. The cost is slightly deflated boss tension, which is accepted.

---

## 7. Session flow (outside a run)

```
APP LAUNCH
   ↓
[Splash → auth → server session + profile fetch → content hash check]
   ↓
HOME / CAMP  ────────────────────────────────────────────┐
   │  shows: hero portrait+gear, Energy bar, Legend Level │
   │  bar, daily quest strip, PvP rank chip, ad-offer     │
   │  buttons, chapter "Continue" CTA                     │
   ├─▶ HERO      (equip gear, pets, mount, view stats)    │
   ├─▶ FORGE     (merge, enhance, salvage gear)           │
   ├─▶ TALENTS   (spend Talent Points)                    │
   ├─▶ MENAGERIE (pets & mounts: level, ascend, equip)    │
   ├─▶ ARENA     (Ghost Duel, ladder, season rewards)     │
   ├─▶ SHOP      (earned currency only + Slay Plus)  │
   └─▶ CHAPTER SELECT ─▶ RUN ─▶ RUN RESULTS ─▶ back to HOME
```

A returning player's ideal first 20 seconds: open app → see Energy full and 2 daily quests claimable → tap Continue → be in a run. Never more than **two taps from launch to rolling a die**.

---

## 8. First-time user experience (FTUE)

The first run is a scripted, seeded tutorial board. It is short (12 tiles, no stage gates) and it teaches exactly four things, one at a time, with no text walls:

| Beat | Tile | Teaches |
|---|---|---|
| 1 | Roll #1 → Enemy | Roll to move; combat is automatic; you watch |
| 2 | After battle #1 | The perk draft — the game's core decision |
| 3 | Roll #3 → Treasure | Loot exists and is yours |
| 4 | Roll #5 → Shop | Gold is spent inside the run |
| 5 | Roll #7 → Elite (scripted near-death) | Reroll charges; the tension of the die |
| 6 | Final tile → mini-boss | The victory payoff and the reward screen |

After the tutorial run: force one gear equip, one talent point spend, then release the player. **Total FTUE ≤ 5 minutes.** No ads are shown during FTUE, and Slay Plus is never surfaced before Legend Level 8.

✅ **The full FTUE script is written in `19_CONTENT_TABLES.md` Part D.** Note one decision it contains: there is **no tutorial narrator character**. Instructions appear as short diegetic captions on the board itself. A talking guide would have been the only recurring character in a game with no story, setting an expectation nothing else meets.

---

## 9. Retention hooks (all non-monetary)

| Hook | Cadence | Reward |
|---|---|---|
| Daily Quests | 3/day, reroll 1 | Crowns, Enhance Stones, Energy |
| Daily Login Calendar | 28-day cycle | Escalating; day 7/14/21/28 give pets or S-tier gear chests |
| Lucky Wheel | daily | 1 free spin + 2 via ad. 8 always-positive segments — see `19` Part F |
| Weekly Chapter Challenge | weekly | A modifier-loaded run (e.g. "no shops, double drops") for Soul Shards |
| PvP Season | 14 days | Soul Shards, Honor, gear chests, Talent Points |
| Codex / Bestiary completion | ongoing | Permanent tiny stat bonuses for cataloguing enemies, perks, gear |

✅ **14 Weekly Challenge modifiers are authored in `19_CONTENT_TABLES.md` Part C**, with combination rules. The 20-quest daily pool is in Part B of the same document.
