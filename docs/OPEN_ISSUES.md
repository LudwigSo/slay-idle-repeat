# Open issues — as of the M4 + M7 implementation runs (2026-08-17)

Written at the end of `kickoff-milestone M4` (complete) and partway through `kickoff-milestone M7`
(in progress). **Neither milestone has had its `milestone-review` pass yet**, so nothing here has
been through the cross-task consistency check — these are findings from the implementation runs
themselves, recorded as they were made.

Every claim below was verified in source at the time it was written, not taken from an agent's
report. Where a figure came from a report and was later re-measured, the re-measured value is used.

---

## 1. 🔴 Blockers — these stop the game being playable end to end

### 1.1 A battle cannot be fought · `M7-06b` — ✅ **CLOSED**, and it closed twice

**Closed in the domain by `M7-06b`** (2026-08-18): the gear→stat derivation was authored, and
`HeroBuild` plus `RunBattle` were made public for the client to compose a hero through. The four
bullets that used to stand here — no `AffixId → StatId` mapping, no gear `IEffectSource`, everything
behind them `internal`, two production doc comments saying so — are all false as of that task.

🔴 **And it stayed shipped-broken for another day, which is the part worth keeping.** Nothing came
back to *call* any of it. `LocalBattleSimulation` went on returning
`BattleReadiness.HeroStatsUnavailable` unconditionally, quoting the reasoning above in a named
constant, and the client's own suite pinned that refusal in four cases whose justification quoted it
again. So a run reached a fight and could not leave `RunPhase.BattlePending` — which refuses every
other command, `ABANDON_RUN` included, so the profile could not start another run either. A player
met it as a status line saying *"the part of the game that works out your hero's power is not
finished… there is nothing to fix"*, about a feature that had shipped.

**Closed in the client on 2026-08-19** (`fix/battle-local-simulation`): the prediction calls
`RunBattle.Simulate` through the same door `CONFIRM_BATTLE_RESULT` recomputes the fight through, the
presenter hands it the profile row it had been reading and dropping, and the readiness state and its
EN/DE sentence are retired with their schema entry. Verified against the product owner's own stuck
save rather than off the unit tier: `phase=BattlePending → readiness=Ready`, a 49-tick fight,
`Submitted`, `phase=InProgress`.

⚠️ **Two lessons, both already in this repository's own record.** The overstatement is the one the
tracker logs against M3's exit criterion and again against M7's: *"the rules can fight"* was read as
*"a player can reach a fight."* And it is `S25` exactly — a seam whose only caller is deferred is
untested by construction — except the caller here was not deferred, it was **already written and
pointing at the old answer**.

⚠️ **M3-05's tracker note is imprecise** and has been corrected: not *"no `ActorStats` buildable"*
but *"no **hero** `ActorStats` buildable"*. A client *can* build **an** `ActorStats` today —
`ActorStats.From` is public, `CombatSimulator.Simulate` is callable, and `tools/BalanceHarness`
proves it from a separate assembly with no `InternalsVisibleTo`.

### 1.2 A battle result is never verified · `M7-06c` — ✅ **CLOSED**

`ConfirmBattleResult` **recomputes the fight** through `RunBattle.Simulate` and the server's answer
wins; a mismatch raises no `RejectionReason`, emits no event and reaches no screen, and is tallied
for the review queue instead (`14` §9's three clauses, the third included). `GameRules` refuses a
stock change while a battle is pending, which is the other half of the same mechanism: without it,
an equip between opening and confirming would legitimately change the fight and be indistinguishable
from a forged log.

*Kept rather than deleted because of what it demonstrates: the refusal to fabricate a stat block in
`M7-06` is what stopped an invented block walking straight through this hole while it was open.*

---

## 2. 🔴 The frozen command vocabulary is now short by at least two commands

The command vocabulary was frozen at **49** in M1, with the explicit term that *"additions afterwards
are logged decisions in `16`"*. Three separate implementation runs hit that wall and each correctly
left it alone rather than inventing a 50th wire name. **These are one product decision, not four.**

| Missing | Consequence |
|---|---|
| `EXPAND_INVENTORY` | `10` §4's ten-rung Crown ladder and the 400-Soul-Shard alternative are fully authored and **entirely unspendable**. Capacity can be modelled, priced and tested; no player action can move it. |
| `UNEQUIP` | A loadout slot can be filled and **never emptied**, except by overwriting it. `Player.Unequip(GearSlot)` and `Loadout.Without(slot)` exist and no command reaches them. |
| *(resolved)* Shop / Dice Forge clearing | Was the same shape; **closed** by `M7-00e` routing both through the existing `RESOLVE_TILE` rather than adding a command. ⚠️ The Dice Forge half is now moot: the tile grants nothing and `RESOLVE_TILE` clears it in place (`16` D41). |

M3's review had already carried Shop and Dice Forge to `M3-08b`/`M3-11` — **which sit in M11**, five
milestones away. That is why M7 had to take the minimum itself.

---

## 3. Design-document contradictions with no owner

Each of these was found by an agent reading the specs closely, and each needs a ruling from the
product owner rather than a guess.

| # | Contradiction | Where it bites |
|---|---|---|
| 3.1 | ✅ **RESOLVED by `M7-01c` — the document yielded.** `23` §7.2 registered `GodotPlatformInfoAdapter`/`GodotAudioAdapter`/`GodotHapticsAdapter` against ports, which this repo's test tier cannot support: a class implementing a port makes `Every_implementation_of_a_port_has_a_contract_fixture` demand a fixture, and that fixture is a fatal `AccessViolationException` (§5.3 below). The new **`23` §7.2a** rules that a class able to reach the engine API implements no port; Godot classes are *capabilities* the composition root names directly, and ports are implemented by plain-C# host adapters. Held by `PortCatalogueTests.No_type_in_the_engine_adapter_implements_a_port` and its premise rule, so the ruling goes red rather than stale if the engine fact ever changes. | Resolved — no longer a contradiction. ⚠️ §7.2a is not yet reconciled with `23` §8's AppLovin worked example, which puts a GDScript bridge in the project that implements `IRewardedAdPort`: **`M15-01`** owns splitting it |
| 3.2 | **`runSeed` "never leaves the server"** — asserted in `SeedDerivation.cs` and `RunRngScope.cs` — while `RunSnapshot.RunSeed` is handed to the client verbatim with no narrowing projection. | `M7-02` / `M5-15` |
| 3.3 | **`03` §1.1 says the tile resolves and *then* the Stage Gate fires**; the code fires it at the movement landing, preserving M3-05's architecture. Deferring it needs a persisted "gate owed" flag and a rework of every follow-up-command tile. | `M7-00f` implemented the landing order |
| 3.4 | **The chapter clear gate is authored twice.** `content/chapters/*.json` carry a required `unlockCondition` that **nothing reads at runtime**, and `chapter.schema.json` permits shapes the generic ladder cannot express — such a chapter would be **opened anyway**. An Application test now pins that the two sources agree, but whether `unlockCondition` replaces or adds to the rung is undecided. | `M7-04` |
| 3.5 | **Chapter/tier gating in `START_RUN` is presentation-only and unowned.** `StartRun.Handle` checks only that the chapter id is ≥ 1 and the tier is defined. The ladder is enforced on screen and nowhere else. | `M7-04` |
| ~~3.6~~ | ✅ **CLOSED by removal, not by resolution** (`16` D41). `USE_REROLL` could not re-roll the face it was shown beside; the whole reroll is gone, so the divergence has no subject. |
| 3.7 | **`13` §3 is the Board screen (S05), not Home.** Home is `13` §2 and **Chapter Select has no dedicated `13` section at all** — its real sources are `02` §2 and `10` §7. *(Tracker spec ref already corrected.)* | `M7-04` |
| 3.8 | **`07` §1.1's cumulative-XP column contradicts its own prose** — `~3.04M` in the table, `~3.07M` two lines later. **Resolved by arithmetic**: the formula gives 3,036,044, so the table is right and the prose sums one term too many. Errata landed. | `M4-10` |

---

## 4. Things that are authored but unreachable

| What | Why |
|---|---|
| ~~**The `Star` die face**~~ | ✅ **CLOSED by removal** (`16` D41). The die has no face kinds at all, so there is no face needing a player choice. |
| **The 10-rung inventory Crown ladder** | No `EXPAND_INVENTORY` command (§2). |
| **Reforge, Retune, Focus, Set-Token redemption** | `24` §5–§6 fully specifies them and `luck.json` carries all their tuning; scheduled as `M4-04b` in **M9**. |
| **Chest / egg / crate / wheel grants** | `M4-02`/`07`/`08`/`09`, all in **M9**. Six of the ten `LuckService` source classes have no caller. |
| **`AD_ENHANCE_LUCK` and the Plus lucky charges** | Need the ad-reward path; `08` §4.2's ruling makes them byte-for-byte identical, so they must land together. |
| **The auto-salvage filter** | Persisted on `Player`, but no command sets it and none applies it at run end. |
| **`EffectOp.REVEAL_TILES`** | Queued-never-resolved. When it lands, the board preview-range gate belongs in `BoardView.Project`, not the presenter. |
| **All 106 audio ids** | Transcribed a milestone ago; **none of the assets exists** (M8-07, no tool licence). |

---

## 5. Test and infrastructure limits

### 5.1 No CI has ever run · `X-07`
**There is still no git remote.** Every workflow under `.github/workflows/` is authored and locally
validated; none has been observed on a runner. Unchanged since M0.

### 5.2 No scene tests are possible
Godot `Node` subclasses are not instantiable under xunit. Scene-code fixes across M7-04/05/06 are
proven by build + headless engine measurement + reasoning, **not** by a harness. This is why the
presenter/scene split matters: presenters are where the logic *is* testable, and that boundary is
enforced by 10 architecture rules with two complementary arms (IL and source text).

### 5.3 `Platform.Godot` cannot be exercised from the unit tier at all
Not "it throws" — **it kills the test host**. GodotSharp is a shim over native function pointers the
engine populates at startup; headless, the first call is an uncatchable `AccessViolationException`:

```
Der aktive Testlauf wurde abgebrochen. Grund: Der Testhostprozess ist abgestürzt.
: Fatal error. System.AccessViolationException
   at Godot.Input.VibrateHandheld(Int32)
```

### 5.4 A wall-clock-sensitive test flakes under load
One `Core.Tests` case failed **once out of 5,993** during a fully-loaded nine-assembly parallel run
and passed on both re-runs; the name was not captured.
`tests/SlayIdleRepeat.Core.Tests/BalanceHarness/` carries a `WallClockSensitive` collection, which is
the likely suspect. **A wall-clock-sensitive test that fails under load is a real defect class, not
noise.** Owner: whichever milestone review reaches it first.

### 5.5 `Test-VendorPackageUniqueness.ps1` cannot run from a worktree
It excludes `.claude/` paths by design, and this project's own dispatch skill *mandates* worktrees.
Recorded as M1 carry-forward 25 and still open.

### 5.6 Sub-agents cannot message a spawning peer
A harness limitation, not a code issue, but it cost real work this milestone: `M7-01b`'s verification
reviewers finished **into a void** and had to be relayed by hand. **Three of the four defects they
caught were introduced by the fix pass itself and were green.** Worth a steering rule.

---

## 6. Art, audio and localisation

- **No real art exists.** `M8-02` (style anchor) and `M8-03` (UI kit) are ⛔ **capability-blocked** —
  they need a human Midjourney session. Every theme override in the client is named as *M8-03 theme
  debt* ("re-check, don't re-apply"). **There is no theme resource in the checkout at all.**
- **There is no placeholder atlas either.** The generator emits loose per-asset PNGs and a rects-only
  JSON and **never rasterises an atlas page**; `artifacts/placeholders/` has never been generated on
  this machine; all 86 §E17 UI-kit rows are permanently skipped (`deliverySize` *and* `pivot` both
  null); and policy forbids placeholder output entering the Godot checkout. The client reports
  `atlas=absent` at boot and treats it as a named non-fatal outcome.
  *(An earlier M7 kickoff ruling overstated this and has been corrected in the tracker.)*
- **Localisation: 213 keys per locale, key sets identical, every DE value is `##TODO_DE##`-prefixed.**
  D20 forbids machine translation, so a human localiser is a hard prerequisite.
- 🔴 **No loc runtime exists repo-wide and no tracker row owns building one.** `LocaleStringCatalogue`
  is a single resolver with no plurals, no ICU, no interpolation, no font fallback and no runtime
  locale switch. Needs an owner.
- ⚠️ An unreferenced loc key is a **fatal** `OrphanedReference` — keys named only from C# will kill
  the client. Every screen therefore ships a `content/<screen>/*.json` that *names* its keys.

---

## 7. Smaller open items, with owners

| Item | Owner |
|---|---|
| `BoardGraph.FromLayout`'s dead-end guard is **positional, not tile-keyed** — a dangling `TileKind.Enemy` terminus still reports a boss encounter | `M7-00h` (queued) |
| `res://` inside a packed `.pck`/APK is **unreachable by `System.IO`** — works in the editor, fails in a packed build. Two consumers today | `M7-10x`, folded into `M7-10` |
| **Godot exits 0 even on a fatal in-game error** — the APK job must never gate on the exit code alone | `M7-10` |
| `ExportRelease` builds the 13 referenced plain-SDK projects **unoptimized**; confirmed *not* a parity risk (no `#if` in Core/Application) | `M7-10` |
| `IsLowEndDevice` omitted — nothing rules what "low-end" means; `M9-04` is the **nearest** row, not one that claims it | `M9-04` |
| `Adapters.Platform.Host` has no composition root yet, as `Cache.LocalFile` didn't before `M7-09` | `M7-01`/`M7-03` |
| Draft counters move on `PICK_PERK` only; if the intended reading is "every draft *offered*", `SKIP_DRAFT` needs a follow-up | M4 review |
| `PerkDraftEngine.GenerateOptions` now takes **nine parameters** — at the width where a parameter object earns its place | M4 review |
| `Model.FeatCounters` and `Model.DraftedPerks` are public records whose synthesized `Equals` compares their map component **by reference** — fix both or neither | M4 review |
| F2's Codex bias input is a genuine subset — no player-lifetime Codex exists (`M4-11`, M9) | `M4-11` |
| Re-simulating a **pre-M7-00f command log** against this build will diverge at the first landing the corrected Stage Gate trigger catches | Inherent; noted |

---

## 8. Milestone status

| | State |
|---|---|
| **M4** | Implementation complete, 8/8 tasks merged to `milestone/M4`. **Exit criterion met in part** — two clauses were blocked by X-09/X-10, both since fixed in M7. **No `milestone-review` has run.** |
| **M7** | In progress. Everything listed here as remaining has since merged to `main` (the M4+M7 completion run, 2026-08-18): `M7-07`, `M7-08`, `M7-11`, `M7-10`, `M7-00h`, `M7-01c`, `M7-04b`, `M7-05c`, `M7-06b/c/d/e`, plus `M7-10y` and `M7-10z1`. **Remaining: `M7-10z`** — nothing can drive the client headlessly, so the desktop-export half of the exit criterion is unproven. |

**M7's exit criterion will land partially met**, in the same shape as M4's:

- ✅ The APK builds through the custom export template and can be asserted to contain managed
  assemblies — the toolchain is present locally.
- ⛔ **A human playing two chapters on a physical handset** — no agent can attach to or tap a device.
  A product-owner acceptance step.
- ⛔ **CI building the APK on a runner** — no git remote.
- ✅ **A run can now roll, move, fight, draft and reach its results screen** — §1.1 is closed on both
  sides as of 2026-08-19, the client half verified against a real save rather than off the unit tier.
- 🔴 **Two tile kinds still dead-end a Chapter-1 run in the client**: `TILE_EVENT` (weight 8) and
  `TILE_MINIGAME` (weight 7) are accepted by `RESOLVE_TILE` and not cleared, and neither S09 nor S10
  exists as a screen, so the board submits neither `EVENT_CHOOSE` nor `MINIGAME_SUBMIT`. Same shape as
  the fight bug, and no tracker row owns either screen.

---

## 9. Suite state at the time of writing

On `milestone/M7`, from a clean build — **0 warnings, 0 errors**:

| Suite | Count |
|---|---|
| Core | 6 014 |
| Application | 1 060 |
| Architecture | 159 |
| Contract | 148 |
| Client | 348 |

Asset suites (unchanged by M4/M7): AssetProvenance 102 · AssetManifest 208 · AssetPipeline 209 ·
AssetPlaceholders 58.
