# 21 — Economy Simulator: Implementation Spec

🔒 Decision: **spec it in detail now, build it in C# alongside the real code, sharing the actual data files.** 🔒 **Decision D31: the simulator is a v1 deliverable and must pass before the live service opens, not after.**

This is the highest-leverage engineering task in the project that is not the game itself. Every number marked 📐 TUNABLE across this documentation set is an educated guess until this tool exists and has been run.

Its companion is **`29_POWER_MODEL.md`**, which defines `PlayerPower` and the three authored expectation tables this tool grades the game against. Read that first.

---

## 1. Purpose

Answer seven questions, quantitatively:

1. **Is the player as strong as I expected them to be, on the day I expected it?** — the headline question, graded against `29` §6.
2. **How long does it take to reach each chapter?** (validates `01` §7 and `10` §8)
3. **What is the bottleneck at each stage?** (Crowns? Enhance Stones? Soul Shards? Legend XP? Energy?)
4. **How far behind is a player who watches no ads?** 🔒 Never more than 45% behind a full-ad-watcher at day 30 (`12` §1).
5. **Does any currency inflate or starve?**
6. **How unlucky can an unlucky player be?** — the p10 question, which pity (`24`) exists to bound.
7. **Where does the curve break?**

🔒 **The tool is a tuning loop, not a report.** Everything about it — the data layout, the CLI, the runtime budget, the output format — is designed so that the cycle *change one number → re-run → read one table* takes under two minutes.

---

## 2. Placement and shape

```
tools/EconomySim/
├── Program.cs                     # CLI entry point (§10)
├── Simulation/
│   ├── SimulatedPlayer.cs         # the agent
│   ├── PlayerProfile.cs           # behaviour parameters (§5)
│   ├── FeatureEngagement.cs       # per-system participation rates (§5.2)
│   ├── AdBehaviour.cs             # per-placement watch rates (§5.3)
│   ├── DayLoop.cs                 # one simulated day (§6)
│   ├── RunModel.cs                # one simulated run — calls the real use cases
│   ├── DungeonModel.cs            # 25
│   ├── EventModel.cs              # 26
│   ├── GuildModel.cs              # 27
│   └── DecisionPolicy.cs          # how the agent spends (§7)
├── Power/
│   ├── PowerCalculator.cs         # closed-form PlayerPower, from 29 §2
│   ├── EmpiricalPower.cs          # measured power, from 29 §1
│   └── PowerDecomposition.cs      # attribution by source (§8.2)
├── Tuning/
│   ├── OverrideLoader.cs          # §9.2 — layered data overrides
│   └── SweepRunner.cs             # §9.3 — parameter sweeps
├── Reporting/
│   ├── ExpectationReport.cs       # §8.1 — the headline table
│   ├── CsvWriter.cs
│   └── Assertions.cs              # the CI gates (§11)
└── EconomySim.csproj              # 🔒 references SlayIdleRepeat.Core ONLY
```

🔒 **The simulator is a thin wrapper over `InMemoryGame`** (`30` §6). It references **`SlayIdleRepeat.Core` and nothing else** — no `Application`, no ports, no adapters, not even the in-memory fakes.

That is possible because the domain model is a pure, synchronous state machine (`30` §2): time is a value it advances, randomness is a seed it supplies, and content is a snapshot it loads once. There is **no database, no Redis, no Godot, no network, no ad SDK, and nothing to wire up.**

Critically, there is also **no reimplementation**. Board generation, drop tables, `LuckService` (`24` §11), merge math, talent math, energy math and the combat simulator are all executed by the same `GameRules.Apply` that serves real players. A simulator that reimplements the economy tests the reimplementation, not the game.

⚠️ **This is a simplification from the previous version of this spec**, which wired the simulator to `Application` plus eleven in-memory adapters. That worked, but it left a real hazard: the simulator and the live game could diverge through their *adapter set* rather than through their rules, and the divergence would be invisible. Depending on `Core` alone removes the possibility.

Ad behaviour (§5.3) is therefore modelled as **domain commands** — the simulator issues `ClaimAdReward(placement)` at the profile's rate — not as a fake ad SDK. The domain already owns cap enforcement and Plus auto-grant equivalence (`12` §4.3), so this is both simpler and more faithful.

---

## 3. The tuning surface 🔒

The user-facing requirement is that the economy be **easy to tweak**. That is a data-layout problem before it is a tooling problem, and it is solved by three rules.

### 3.1 Rule 1 — every tunable number lives in one directory

```
SlayIdleRepeat.Data/tuning/
├── power_model.json            # 29 §2-3 — formula weights, reference opponent
├── par_power.json              # 29 §4-5 — content par, level-expectation factor curves
├── expected_progression.json   # 29 §6 — the product owner's intent, per profile per day
├── currencies.json             # income and sink rates per source
├── progression.json            # Legend XP curve, energy, catch-up/frontier curve
├── drops.json                  # rarity tables per chapter band, affix pools, quality range
├── luck.json                   # 24 — every pity N, soft-pity slope, source-class map
├── forge.json                  # merge costs, enhance rates and costs, reforge/retune costs
├── beasts.json                 # pet/mount level costs, egg and crate odds
├── dungeons.json               # 25 — tier yields, entry caps, energy cost
├── events.json                 # 26 — earn rates, track thresholds, shop prices
├── guilds.json                 # 27 — quest targets, boss HP, perk values
├── ads.json                    # 12 — placement caps, bundle scaling
└── sim_thresholds.json         # §11 — the assertion thresholds themselves
```

🔒 **A 📐 TUNABLE number that is not in this directory is a bug.** A build-time check enumerates every `📐` marker in the documentation set against the schema keys and fails on a mismatch. That check is what stops the tuning surface eroding over eighteen months.

### 3.2 Rule 2 — nothing that is tunable is also code

Already required by `14` §6. Restated because the simulator makes it enforceable: if a number is in code, the simulator cannot sweep it, and it will therefore never be tuned.

### 3.3 Rule 3 — overrides never edit the canonical files

```
--overrides tuning/experiments/cheaper_merges.json
```

An override file is a **sparse JSON patch** applied on top of the canonical data at load time. Sweeps, experiments and what-ifs all run as overrides, so the canonical data is only ever edited when a change is *adopted*. This keeps `git diff` on `SlayIdleRepeat.Data` a record of decisions rather than a record of attempts.

```json
{
  "forge.json": { "mergeCrownCost": { "S": 4200, "SS": 22000 } },
  "progression.json": { "legendXpExponent": 1.09 }
}
```

---

## 4. Power, and how the simulator uses it

The agent's every decision runs through `PlayerPower` as defined in `29` §2.

| Use | Detail |
|---|---|
| **Chapter choice** | The policy picks the highest `(chapter, tier)` where `PlayerPower ≥ ParPower × policyThreshold` (`29` §4) |
| **Clear probability** | Blended from the power ratio and `profile.SkillFactor`, then **validated** against real combat sims (§6.1) |
| **Expectation grading** | Daily `PlayerPower` is diffed against `expected_progression.json` (`29` §6) — this is the headline output |
| **Decomposition** | Every daily snapshot records power attributed to level, gear, talents, pets, mount and set bonus (§8.2) |
| **Calibration** | Every 10 simulated days, `EmpiricalPower` is measured and compared to `PlayerPower` — assertion **A10** |

🔒 **The simulator must never compute power itself.** It calls `PowerCalculator`, which calls the same `SlayIdleRepeat.Core` stat aggregation the game uses. A second power implementation would be the exact failure mode this whole architecture exists to prevent.

---

## 5. Simulated player profiles

The previous profile model had five fields and could not express *"plays daily, does dungeons, ignores events, never joined a guild, watches the double-rewards ad but not the energy ad"* — which is most real players. This is the replacement.

```csharp
public sealed record PlayerProfile {
    public string Name                  { get; init; }

    // --- Cadence ---------------------------------------------------------
    public double SessionsPerDay        { get; init; }   // 1..6
    public double MinutesPerSession     { get; init; }   // caps runs more honestly than a run count
    public int    DaysPerWeekActive     { get; init; }   // 1..7
    public double LapseProbability      { get; init; }   // chance/day of skipping regardless
    public int    MaxLapseDays          { get; init; }   // feeds the Energy Reserve model (28 C)

    // --- Competence ------------------------------------------------------
    public double SkillFactor           { get; init; }   // 0.7 .. 1.3 on effective power in combat
    public double SystemLiteracy        { get; init; }   // 0..1 — how close to optimal they spend
    public IDecisionPolicy Policy       { get; init; }

    // --- Participation ---------------------------------------------------
    public FeatureEngagement Features   { get; init; }   // §5.2
    public AdBehaviour       Ads        { get; init; }   // §5.3
    public bool   HasPlus               { get; init; }
    public int    PlusStartDay          { get; init; }   // -1 = never
    public int    PlusEndDay            { get; init; }   // models lapse (12 §2.2)
}
```

### 5.1 `MinutesPerSession`, not `RunsPerDay` 🔒

The old model asked for a run target. But a run is 8–12 minutes, a dungeon is 2–4, a duel is 40 seconds and a guild boss attempt is 2 — and a player's real constraint is **time**, not appetite. Budgeting minutes and letting `FeatureEngagement` allocate them across activities is the only way to model "this player does dungeons *instead of* a third run", which is exactly the trade-off dungeons (`25`) introduce.

Energy remains a second, independent constraint. A player is blocked by whichever binds first, and **which one binds is itself a reportable output** (§8.4).

### 5.2 `FeatureEngagement`

```csharp
public sealed record FeatureEngagement {
    public double Runs            { get; init; }  // share of session minutes → chapter runs
    public double Dungeons        { get; init; }  // 25 — 0.0 means never opens them
    public double Events          { get; init; }  // 26 — share of runs routed into the live event
    public double Pvp             { get; init; }  // 11 — 0..1 of daily duel attempts used
    public double GuildActivity   { get; init; }  // 27 — 0 = guildless; 1 = full quests + 3 boss attempts
    public double DailyQuests     { get; init; }  // 0..1 of the 3 completed
    public double LuckyWheel      { get; init; }  // 0..1 of available spins
    public double LoginCalendar   { get; init; }  // 0..1 — claimed or not
    public double ForgeDiligence  { get; init; }  // 0..1 — how promptly merges/enhances happen
    public double FocusUsage      { get; init; }  // 24 §5 — 0 = never sets a Focus
    public double AutoSalvage     { get; init; }  // 0..1 — configured or not
}
```

`Runs`, `Dungeons`, `Events` and `Pvp` are **normalised to sum to 1.0** and allocate the session-minute budget. The rest are independent participation rates.

🔒 This block is where "how often they use certain features" lives, and it is the reason the tool can answer *"is the guildless player fine?"* (E17) and *"is a player who ignores events fine?"* (E13) rather than assuming everyone does everything.

### 5.3 `AdBehaviour` — per placement, not a single rate

```csharp
public sealed record AdBehaviour {
    public double DefaultWatchRate                          { get; init; }
    public IReadOnlyDictionary<AdPlacementId, double> PerPlacement { get; init; }
    public double InterstitialTolerance                     { get; init; } // 0..1
}
```

A single `AdWatchRate` cannot express real behaviour: almost everyone takes `AD_DOUBLE_RUN_REWARDS` (one ad, doubles the run) and far fewer take `AD_SHOP_REFRESH` (one ad, marginal). Per-placement rates matter because **the placements differ enormously in power-per-ad**, and the 45% fairness gap (`12` §1) is entirely a function of which ones players actually watch.

🔒 **Plus is modelled by setting `GameContext.Entitlements.HasPlus` and issuing the same `ClaimAdReward` commands** (`30` §3). The domain resolves the grant, enforces the identical daily cap, and neither knows nor cares whether an ad was watched — which is exactly the fairness contract (`12` §1) expressed as a code path.

There is therefore no `if (hasPlus)` in the simulator, and none in the game either: on the client the *impression* is an adapter swap (`23` §7.2), but the *grant* has always been one domain rule. That is why `Plus_Core` and `AllAds_Core` producing identical curves (A2) is a meaningful test rather than a tautology — the simulator is exercising the real grant logic, not a mock of it.

`PlusStartDay` / `PlusEndDay` model **subscription lapse**, so assertion A15 can check that a lapsed subscriber's curve flattens to the free-player slope and **never regresses** (`12` §2.2).

### 5.4 The required profile set (14)

Every run of the tool simulates all of these.

| Profile | Sessions/day | Min/session | Days/wk | Skill | Literacy | Ads | Plus | Notable engagement |
|---|---|---|---|---|---|---|---|---|
| `NoAds_Casual` | 1 | 20 | 5 | 0.9 | 0.5 | 0.0 | — | runs only, no dungeons, no guild |
| `NoAds_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.0 | — | everything except ads |
| `SomeAds_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | mixed per-placement |
| `AllAds_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 1.0 | — | the reference curve |
| `Plus_Core` | 3 | 40 | 7 | 1.0 | 0.8 | n/a | ✅ | must match `AllAds_Core` (A2) |
| `Plus_Lapsed` | 3 | 40 | 7 | 1.0 | 0.8 | 0.3 | d1–d60 | tests lapse handling (A15) |
| `AllAds_Hardcore` | 5 | 60 | 7 | 1.2 | 1.0 | 1.0 | — | ceiling case |
| `NoAds_Weekend` | 2 | 90 | 2 | 1.0 | 0.7 | 0.0 | — | burst player |
| `Lapsed_Returner` | 3 | 40 | 3 | 1.0 | 0.7 | 0.5 | — | 48h+ gaps; **the Energy Reserve test (E20)** |
| `Guildless_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | `GuildActivity = 0` (E17) |
| `Guilded_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | `GuildActivity = 0.7` (E17) |
| `EventSkipper_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | `Events = 0` (E13) |
| `DungeonOnly` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | `Runs = 0.05` (E8) |
| `Unlucky_Core` | 3 | 40 | 7 | 1.0 | 0.8 | 0.5 | — | seeded to the **p10** RNG band (E1) |

📐 The whole table lives in `sim_profiles.json` and profiles can be added freely. The 14 above are the minimum set required for the assertions in §11.

---

## 6. The day loop

```
for day in 1..180:
    if lapsed(day): regenerateEnergyAndReserve(24h); continue     // 28 Part C

    grantDailyLogin()            if Features.LoginCalendar
    grantDailyEnergyRefill()
    assignDailyQuests(3)
    spinLuckyWheel()             if Features.LuckyWheel           // 24 §4.8
    claimInbox()                                                  // 28 Part A — excluded from fairness (E23)

    minutes = SessionsPerDay * MinutesPerSession
    allocate minutes across { Runs, Dungeons, Events, Pvp } per FeatureEngagement

    while minutesRemaining(Runs) and energyAvailable(20):
        chapter, tier = Policy.ChooseChapter(state)               // uses ParPower — 29 §4
        result = RunModel.Simulate(state, chapter, tier, profile)
        applyRunRewards(result)
        offerAd(AD_DOUBLE_RUN_REWARDS); offerAd(AD_ENERGY) if low

    while minutesRemaining(Dungeons) and entriesLeft and energyAvailable(10):
        DungeonModel.Run(state, bestUnlockedTier)                 // 25

    while minutesRemaining(Events) and eventLive:
        EventModel.Run(state, activeEvent)                        // 26

    doPvpDuels(attemptsAllowed * Features.Pvp)
    GuildModel.Day(state, Features.GuildActivity)                 // 27 — quests, boss attempts

    completeAvailableQuests(Features.DailyQuests)
    Policy.SpendCrowns(state); Policy.SpendSoulShards(state)
    Policy.SpendTalentPoints(state); Policy.AutoSalvage(state)
    Policy.SetFocus(state)                                        // 24 §5
    evaluateFeats(state)                                          // 28 Part D
    regenerateEnergyAndReserve(hoursOffline)
    record(day, snapshot(state))                                  // §8
```

### 6.1 Run outcome model

`RunModel.Simulate` does **not** play a full board tile by tile — that would be slow and needlessly detailed. It:

1. Computes `PlayerPower` from the real stat aggregation (`29` §2).
2. Computes `clearProbability` by running **20 real combat simulations** (`SlayIdleRepeat.Core.Combat`) against representative enemies from that chapter, blended with `profile.SkillFactor`.
3. Rolls the outcome: victory, or death at a stage weighted by the power ratio.
4. Draws rewards from the **real drop tables via `LuckService`** (`24` §11), so every pity counter advances exactly as it would in play.
5. Models the in-run perk draft as a flat power uplift drawn from the real perk pool, since perks are run-scoped (`29` §3).
6. Applies the completion multiplier from `02` §5.2.

This keeps a 180-day × 14-profile × 200-seed simulation under **~4 minutes** while staying grounded in the real combat and loot math. 🔒 **If the runtime ever exceeds 5 minutes, cut fidelity — not iterations.** A tool nobody re-runs is a tool that stops being true.

📐 Budget reference from `30` §6: a single 180-day player against `InMemoryGame` should complete in **under 200 ms**. If one player takes materially longer than that, the `RunModel` fidelity in this section is the first thing to reduce.

---

## 7. Decision policies

The agent must spend roughly the way a competent player would, or the output is meaningless.

```csharp
public interface IDecisionPolicy {
    (int chapter, Tier tier) ChooseChapter(PlayerState s);
    void SpendCrowns(PlayerState s);
    void SpendSoulShards(PlayerState s);
    void SpendTalentPoints(PlayerState s);
    void AutoSalvage(PlayerState s);
    void SetFocus(PlayerState s);
    void SpendEventCurrency(PlayerState s);
}
```

**`GreedyPowerPolicy` (default):**

| Decision | Rule |
|---|---|
| Chapter | Highest `(c,t)` where `PlayerPower ≥ 0.85 × ParPower(c,t)`, preferring the highest tier that still clears |
| Crowns | Merge whenever 3 mergeable items exist, cheapest first; then enhance the weapon to the highest level with mercy-adjusted EV ≥ 1 (`24` §4.6); then level pets |
| Soul Shards | Energy refill if it enables ≥2 more runs; else Pet Eggs until 3 equipped; else S Gear Chests |
| Talent Points | Along the branch with the highest marginal `PlayerPower` per point, computed by actually evaluating the aggregation |
| Focus | The slot with the largest gap to `ExpectedPower` contribution; re-evaluated weekly (respecting the 12h cooldown) |
| Salvage | Anything below the equipped item's power that is not merge fodder; SS always salvaged for Set Tokens once the set is complete |

`SystemLiteracy` degrades the policy toward random by that fraction, so a literacy of 0.5 makes half the decisions suboptimal. **This is what stops the tool assuming every player is an expert.**

Also implement **`BalancedPolicy`** and **`HoarderPolicy`** as sensitivity checks. 🔒 **If time-to-chapter differs by more than ~25% across policies, the economy is too sensitive to player knowledge and should be simplified** — that is assertion A16, and it is a design finding, not a tuning one.

---

## 8. Outputs

Written to `out/economy/{timestamp}/`.

### 8.1 `expectation.md` — read this first 🔒

The headline artefact, specified in `29` §6.1: expected vs actual `PlayerPower` per profile per checkpoint, with the tolerance band and a pass/fail. **Everything else is diagnosis for a failure in this table.**

### 8.2 `power_decomposition.csv`

Per profile, per day, `PlayerPower` attributed to: base level · gear · talents · pets · mount · set bonus · **Utility Index** separately (`29` §3.1).

Without this, "power is 30% under expectation" is unactionable. With it, "gear contribution is 45% under, everything else is on target" points straight at the drop tables.

### 8.3 The rest

| File | Contents |
|---|---|
| `timeline.csv` | One row per profile per day: every currency, Legend Level, highest chapter, `PlayerPower`, `EmpiricalPower`, runs, dungeons, duels, ads watched, minutes used |
| `milestones.csv` | Day each profile first cleared each `(chapter, tier)`, plus p10/p50/p90 across seeds |
| `bottlenecks.csv` | Per profile per week: which currency sat at zero most, which purchase was blocked most, **and whether time or Energy was the binding constraint** (§5.1) |
| `fairness.csv` | Daily `NoAds_Core` : `AllAds_Core` : `Plus_Core` power ratios |
| `luck.csv` | Per source class: pity fire rate, p10/p50/p90 draws to each guarantee, time-to-6-piece-SS with and without Focus (`24` §10) |
| `income_attribution.csv` | Share of each material sourced from runs / dungeons / events / guild / ads / quests — **the R10 report** |
| `summary.md` | Assertion results |

### 8.4 Percentiles, not just means 🔒

Every profile is simulated across **200 seeds**, and every reported figure carries **p10 / p50 / p90**. Pity systems (`24`) change distributions far more than means; a design built to protect the unluckiest player cannot be validated by a median.

---

## 9. The tuning workflow

### 9.1 The loop

```
1. dotnet run --project tools/EconomySim
2. Read out/economy/{ts}/expectation.md          ← the one table
3. If a band is missed, read power_decomposition.csv to find WHICH source is off
4. Change ONE number, as an override:  --overrides tuning/experiments/try.json
5. Re-run (under 5 minutes)
6. When satisfied, promote the override into the canonical file and commit it alone
7. The CI assertion now protects it
```

### 9.2 Overrides

Per §3.3. Sparse patches, layered, never touching canonical data until adopted.

### 9.3 Sweeps

```bash
dotnet run --project tools/EconomySim -- sweep \
  --param progression.json:legendXpExponent \
  --range 0.95:1.20:0.01 \
  --metric expectation_deviation --profile AllAds_Core
```

Runs the parameter across the range and emits a curve of the chosen metric. **This is how the highest-suspicion numbers in §12 get resolved** — by looking at the shape of the response, not by guessing twice.

Two-parameter sweeps emit a heatmap. More than two is a design smell: if three numbers must move together, they are one number.

### 9.4 Sensitivity report

After any full run, `sensitivity.csv` perturbs each tunable by ±10% in isolation and reports the resulting change in day-30 and day-90 `PlayerPower`. **The top ten rows are the ten numbers that actually matter**, and they should be re-read whenever the economy changes shape — the list is rarely what anyone expects.

### 9.5 The ordering rule 🔒

**Run with dungeons, events and guilds all enabled together before tuning any of them individually.** Each was sized in isolation; all three pay Crowns, Enhance Stones and Beast Feed, and guild perks multiply the other two (`16` R10). Tuning them one at a time will converge on the wrong answer three times.

---

## 10. CLI

```
EconomySim [command] [options]

Commands:
  run       (default)  Full simulation, all profiles, all reports
  sweep                One- or two-parameter sweep (§9.3)
  compare   <a> <b>    Diff two output directories — what did my change actually do?
  assert               Assertions only, minimal output. This is the CI entry point.
  baseline             Write the current run as the reference for `compare`

Options:
  --days <n>            default 180
  --seeds <n>           default 200
  --profiles <list>     default all 14
  --overrides <files>   layered sparse patches (§3.3)
  --out <dir>
  --fast                20 seeds, 90 days — the inner-loop mode, ~20 seconds
```

🔒 `--fast` exists because the tuning loop is only useful if it is fast enough to stay in. Full fidelity is for the decision; `--fast` is for the search.

---

## 11. CI assertions 🔒

The tool exits non-zero — **failing the build** — if any of these fail. It runs on every change to `SlayIdleRepeat.Data`.

### 11.1 Core economy (A1–A9)

| ID | Assertion | Threshold |
|---|---|---|
| **A1** | `AllAds_Core` clears Chapter 8 Normal | day 25–45 |
| **A2** | `Plus_Core` vs `AllAds_Core` `PlayerPower` | within ±5% every day 🔒 the fairness contract |
| **A3** | `NoAds_Core` vs `AllAds_Core` at day 30 | ≥ 55% (no more than 45% behind) |
| **A4** | No currency grows unbounded | none exceeds 50× weekly income at day 180 |
| **A5** | No currency starved | none at zero >40% of days after day 14 |
| **A6** | Progress never stalls | weekly `PlayerPower` growth ≥ 3% through day 120 |
| **A7** | Energy is a soft gate | `NoAds_Core` energy-blocked on <25% of days |
| **A8** | Talents stay meaningful | `NoAds_Core` spends ≤60% of the 633-point tree by day 180 |
| **A9** | Chapter pacing monotonic | each chapter longer than the last, never >2.5× |

### 11.2 Power model (A10–A14) — new, from `29`

| ID | Assertion | Threshold |
|---|---|---|
| **A10** | `PlayerPower` tracks `EmpiricalPower` | within ±12%, every archetype, every chapter band (`29` §1) |
| **A11** | `ParPower` is honest | a build at exactly par clears 62–78% of the time (`29` §4.3) |
| **A12** | Level expectation aligns with content | `ExpectedPower(L)` at chapter-unlock level is 1.0–1.3× `ParPower(c, Normal)` (`29` §5.1 P1) |
| **A13** | No single power source dominates | no factor column >60% of the total multiplier at any level (`29` §5.1 P2) |
| **A14** | 🔒 **Actual matches intent** | **every profile inside its tolerance band at every checkpoint in `expected_progression.json`** (`29` §6) |

**A14 is the assertion this entire tool exists to evaluate.** All others are diagnosis.

### 11.3 Behaviour and fairness (A15–A16)

| ID | Assertion | Threshold |
|---|---|---|
| **A15** | A lapsed subscriber never regresses | `Plus_Lapsed` power is monotonically non-decreasing across the lapse (`12` §2.2) |
| **A16** | Economy not over-sensitive to literacy | time-to-chapter differs <25% across the three policies (§7) |

### 11.4 Inherited requirements (E1–E23)

The 23 requirements added by docs `24`–`28` are unchanged and remain mandatory. Summarised:

| Group | IDs | Substance |
|---|---|---|
| **Luck** (`24` §10) | E1–E5 | p10 reporting; p10 within 1.35× p50; no guarantee fires >30% of the time; model Focus and Set Tokens; re-run all prior assertions against the new sinks |
| **Dungeons** (`25` §7) | E6–E10 | dungeons supply 40–65% of Stone/Feed income; a dungeon-only player levels ≤0.6× as fast; re-derive merge and enhance costs; re-check the Crown bottleneck |
| **Events** (`26` §7) | E11–E15 | rolling calendar over all 180 days; event income 15–30% of materials; an event-skipper within 1.25×; validate launch-event thresholds against the day-1 cohort; model end-of-event Crown conversion |
| **Guilds** (`27` §9) | E16–E19 | guildless vs guilded profiles; guildless within 1.20×; guild income ≤20% of any material; ⚠️ **E19 — the compounding of all three streams, the highest-risk item in this spec** |
| **Live service** (`28` §E2) | E20–E23 | Energy Reserve for the returner; Feats as a retroactive lump; re-derive the talent guardrail for 324 points; exclude inbox grants from fairness assertions |

📐 **Every threshold above lives in `sim_thresholds.json`**, so they are re-tuned deliberately rather than by editing code.

**Total: 16 named assertions + 23 inherited requirements, across 14 profiles.**

---

## 12. Expect the first honest run to fail

The numbers in `10_ECONOMY_AND_PROGRESSION.md` are genre-informed estimates that have never been tested against each other. Highest-suspicion candidates, in order:

1. **`LegendXpForLevel` exponent (1.05)** — controls everything downstream; sweep it first
2. **Combined Crown and Beast Feed income from dungeons + events + guild perks** (§9.5) — the most likely place the economy breaks outright
3. **Merge Crown costs** (120 / 600 / 3,000 / 15,000) — likely the real mid-game wall
4. **The catch-up / frontier-bonus curve** in `10` §8 — sketched, not specified. 🔒 **Deriving it is the simulator's first job.**
5. **Soul Shard income vs Pet Egg cost** (900) — ~1 egg/day, probably generous given the pity system
6. **`ExpectedPower` factor curves** (`29` §5) — shaped by hand and near-certain to be wrong; A13 will say which column

---

## 13. Deliberately out of scope

- **Retention and churn modelling.** This tool answers "how fast does a playing player progress", not "do they keep playing". `FeatureEngagement` is an *input*, not a prediction — the simulator cannot tell you what fraction of players join a guild, only what happens to those who do.
- **Revenue modelling.** `12` §9 covers the shape; ARPDAU needs live data.
- **PvP rating distribution.** Ladder health is its own simulation and a much smaller problem.

---

## 14. Effort estimate

| Component | Effort |
|---|---|
| Core loop, profiles, policies, reporting | 1–1.5 weeks |
| Power model + decomposition + calibration (`29`) | 3–4 days |
| Dungeon, event and guild models | 3–4 days |
| Tuning surface: overrides, sweeps, sensitivity, `compare` | 3–4 days |
| **Total** | **~3 weeks** |

Up from the 1–1.5 weeks originally estimated, and the increase is almost entirely §9 — the tuning ergonomics. That is the right place to spend it: a simulator that is correct but painful gets run three times and then abandoned, and every number in this documentation set stays a guess.

Build it immediately after the application runs on fakes, and **before** any economy number is treated as final.
