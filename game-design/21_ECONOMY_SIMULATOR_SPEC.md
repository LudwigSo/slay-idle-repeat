# 21 — Economy Simulator: Implementation Spec

Resolves open items P2 #19 and #20. 🔒 Decision: **spec it in detail now, build it in C# alongside the real code, sharing the actual data files.**

This is the highest-leverage engineering task in the project that is not the game itself. Every number marked 📐 TUNABLE across this documentation set is an educated guess until this tool exists and has been run.

---

## 1. Purpose

Answer five questions, quantitatively:

1. **How long does it take to reach each chapter?** (validates `01` §7 and `10` §8)
2. **What is the bottleneck at each stage of the game?** (Crowns? Enhance Stones? Soul Shards? Legend XP?)
3. **How far behind is a player who watches no ads?** 🔒 **Hard requirement: never more than 45% behind a full-ad-watcher at day 30.** This is the fairness contract from `12` §1 expressed as a testable assertion.
4. **Does any currency inflate or starve?** (a currency nobody spends is a design bug; a currency always at zero is a wall)
5. **Where does the curve break?** (the point at which progress-per-hour falls below a tolerable floor)

---

## 2. Placement and shape

```
tools/EconomySim/
├── Program.cs                 # CLI entry point
├── Simulation/
│   ├── SimulatedPlayer.cs     # the agent
│   ├── PlayerProfile.cs       # behaviour parameters
│   ├── DayLoop.cs             # one simulated day
│   ├── RunModel.cs            # one simulated run — calls the real use cases
│   └── DecisionPolicy.cs      # how the agent spends
├── Reporting/
│   ├── CsvWriter.cs
│   ├── ChartRenderer.cs       # optional; CSV is sufficient
│   └── Assertions.cs          # the CI gates
└── EconomySim.csproj          # references SlayIdleRepeat.Application, SlayIdleRepeat.Core,
                               #   SlayIdleRepeat.Data and SlayIdleRepeat.Adapters.InMemory
```

🔒 **The simulator is a driving adapter over the real application (D22).** It references `SlayIdleRepeat.Application` and satisfies every port with `SlayIdleRepeat.Adapters.InMemory` — a fake clock, a fake ID generator, in-memory repositories, an in-memory run-state store, and `FakeRewardedAdAdapter` scripted to the profile's ad-watching rate.

That means **no database, no Redis, no Godot, no network, no ad SDK** — and, critically, **no reimplementation**. Board generation, drop tables, merge math, talent math, energy math and the combat simulator are all executed by the same code that serves real players. A simulator that reimplements the economy tests the reimplementation, not the game.

Without D22 this tool would not be buildable at all; it is the clearest single payoff of the ports architecture (`23` §1).

---

## 3. Simulated player profiles

```csharp
public sealed record PlayerProfile {
    public string Name { get; init; }
    public double RunsPerDayTarget { get; init; }      // intent, capped by energy
    public double AdWatchRate { get; init; }           // 0.0 .. 1.0 of available placements
    public bool   HasPlus { get; init; }               // auto-grants all ad rewards
    public double SkillFactor { get; init; }           // 0.7 .. 1.3, multiplies effective power
    public int    DaysPerWeekActive { get; init; }
    public DecisionPolicy Policy { get; init; }
}
```

**Required profiles for every run of the tool:**

| Profile | Runs/day | Ad rate | Plus | Skill | Days/week |
|---|---|---|---|---|---|
| `NoAds_Casual` | 4 | 0.0 | no | 0.9 | 5 |
| `NoAds_Core` | 10 | 0.0 | no | 1.0 | 7 |
| `SomeAds_Core` | 10 | 0.5 | no | 1.0 | 7 |
| `AllAds_Core` | 10 | 1.0 | no | 1.0 | 7 |
| `Plus_Core` | 10 | n/a | **yes** | 1.0 | 7 |
| `AllAds_Hardcore` | 20 | 1.0 | no | 1.2 | 7 |
| `NoAds_Weekend` | 12 | 0.0 | no | 1.0 | 2 |

`AllAds_Core` and `Plus_Core` **must produce near-identical curves** — that is the fairness contract, and it is assertion A2 in §7.

---

## 4. The day loop

```
for day in 1..180:
    grantDailyLogin()
    grantDailyEnergyRefill()
    assignDailyQuests(3)

    while energy >= RUN_COST and runsToday < profile.RunsPerDayTarget:
        chapter, tier = Policy.ChooseChapter(state)
        result = RunModel.Simulate(state, chapter, tier, profile)
        applyRunRewards(result)
        runsToday++
        if adAvailable(AD_DOUBLE_RUN_REWARDS) and profile.WatchesAd(): applyDouble()
        if energy < RUN_COST and adAvailable(AD_ENERGY) and profile.WatchesAd(): grantEnergy(40)

    completeAvailableQuests()
    doPvpDuels(5 + adExtras)
    Policy.SpendCrowns(state)         // merge, enhance, level pets
    Policy.SpendSoulShards(state)     // eggs, chests, energy refills
    Policy.SpendTalentPoints(state)
    Policy.AutoSalvage(state)
    regenerateEnergy(hoursOffline)
    record(day, snapshot(state))
```

### 4.1 Run outcome model

`RunModel.Simulate` does **not** play a full board tile by tile — that would be slow and needlessly detailed. It:

1. Computes `PlayerPower` from the real stat aggregation in `SlayIdleRepeat.Core`.
2. Computes `clearProbability` by running **20 real combat simulations** (via `SlayIdleRepeat.Core.Combat`) against representative enemies from that chapter, then blending with `profile.SkillFactor`.
3. Rolls the outcome: victory, or death at a stage weighted by the power ratio.
4. Draws rewards from the **real drop tables** for the tiles a run of that length would statistically contain, using the real seeded RNG.
5. Returns a `RunResult` with the completion multiplier from `02` §5.2 applied.

This keeps a 180-day simulation across 7 profiles under ~2 minutes while still being grounded in the real combat and loot math.

---

## 5. Decision policies

The agent must spend resources roughly the way a competent player would, or the output is meaningless.

```csharp
public interface IDecisionPolicy {
    (int chapter, Tier tier) ChooseChapter(PlayerState s);
    void SpendCrowns(PlayerState s);
    void SpendSoulShards(PlayerState s);
    void SpendTalentPoints(PlayerState s);
    void AutoSalvage(PlayerState s);
}
```

**`GreedyPowerPolicy` (the default):**

| Decision | Rule |
|---|---|
| Chapter | Highest chapter where `PlayerPower >= 0.85 × ChapterPowerTarget × TierMult`, preferring the highest tier that still clears |
| Crowns | Merge whenever 3 mergeable items exist, cheapest merge first; then enhance the weapon to the highest safe level; then level pets |
| Soul Shards | Buy an Energy refill if it enables ≥2 more runs today; else Pet Eggs until 3 pets are equipped; else S Gear Chests |
| Talent Points | Spend along the branch with the highest marginal `PlayerPower` per point, computed by actually evaluating the stat aggregation |
| Salvage | Salvage anything below the equipped item's power that is not merge fodder |

Also implement **`BalancedPolicy`** (spreads across systems) and **`HoarderPolicy`** (under-spends) as sensitivity checks. If the time-to-chapter curve differs by more than ~25% between policies, the economy is too sensitive to player knowledge and should be simplified.

---

## 6. Outputs

Written to `out/economy/{timestamp}/`:

| File | Contents |
|---|---|
| `timeline.csv` | One row per profile per day: every currency balance, Legend Level, highest chapter, PlayerPower, gear power, talent points spent, pets owned, runs completed, ads watched |
| `milestones.csv` | Day on which each profile first cleared each `(chapter, tier)` |
| `bottlenecks.csv` | Per profile per week: which currency was at zero the most often, and which desired purchase was blocked most |
| `fairness.csv` | Daily ratio of `NoAds_Core` PlayerPower to `AllAds_Core` PlayerPower, and `Plus_Core` to `AllAds_Core` |
| `summary.md` | Human-readable digest with the assertion results |

---

## 7. CI assertions 🔒

The tool exits non-zero — **failing the build** — if any of these fail. It runs on every change to `SlayIdleRepeat.Data`.

| ID | Assertion | Threshold |
|---|---|---|
| **A1** | `AllAds_Core` clears Chapter 8 Normal | between day 25 and day 45 |
| **A2** | `Plus_Core` PlayerPower vs `AllAds_Core` | within ±5% every day (the fairness contract) |
| **A3** | `NoAds_Core` PlayerPower vs `AllAds_Core` at day 30 | **≥ 55%** (i.e. no more than 45% behind) |
| **A4** | No currency balance grows unbounded | no currency exceeds 50× its weekly income at day 180 |
| **A5** | No currency is starved | no currency sits at zero for more than 40% of days after day 14 |
| **A6** | Progress never stalls | weekly `PlayerPower` growth stays ≥ 3% through day 120 |
| **A7** | Energy is a soft gate, not a wall | `NoAds_Core` is energy-blocked from its run target on fewer than 25% of days |
| **A8** | Talent points remain meaningful | `NoAds_Core` has spent ≤ 60% of the 633-point tree by day 180 |
| **A9** | Chapter pacing is monotonic | each chapter takes longer than the last, but never more than 2.5× the previous |

📐 All thresholds live in `economy_sim_thresholds.json` so they can be re-tuned deliberately rather than by editing code.

---

## 8. What to do with the output

The simulator is not a report — it is a **tuning loop**.

```
1. Run the sim
2. Read summary.md; find the failing assertion or the ugliest curve
3. Change ONE number in SlayIdleRepeat.Data (e.g. LegendXp exponent, a drop rate, a merge cost)
4. Re-run
5. Repeat until all 9 assertions pass and the curves look deliberate
6. Commit the data change; the CI assertion now protects it
```

Expect the first honest run to fail several assertions. The numbers in `10_ECONOMY_AND_PROGRESSION.md` are genre-informed estimates and have never been tested against each other.

**Highest-suspicion candidates, to check first:**
- `LegendXpForLevel` exponent (1.05) — controls everything downstream
- Merge Crown costs (120 / 600 / 3,000 / 15,000) — likely the real mid-game wall
- Soul Shard income vs Pet Egg cost (900) — currently ~1 egg/day, which may be too generous given the pity system
- The catch-up / frontier-bonus curve in `10` §8, which is currently sketched rather than specified — **the simulator's first job is to derive it**

---

## 9. Deliberately out of scope

- Retention and churn modelling. This tool answers "how fast does a playing player progress", not "do they keep playing". Those need real telemetry.
- Revenue modelling. `12` §9 covers the shape; actual ARPDAU needs live data.
- PvP rating distribution. Ladder health is its own simulation, and a much smaller problem.

---

## 10. Effort estimate

Roughly **1–1.5 engineering weeks** once `SlayIdleRepeat.Application` and the in-memory adapter set exist, because the hard parts (combat, drops, stat aggregation, board generation, spending use cases) are all reused rather than rebuilt. Build it immediately after the application runs on fakes and **before** any economy number is treated as final.
