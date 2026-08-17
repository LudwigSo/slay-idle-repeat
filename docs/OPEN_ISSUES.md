# Open issues — as of the M4 + M7 implementation runs (2026-08-17)

Written at the end of `kickoff-milestone M4` (complete) and partway through `kickoff-milestone M7`
(in progress). **Neither milestone has had its `milestone-review` pass yet**, so nothing here has
been through the cross-task consistency check — these are findings from the implementation runs
themselves, recorded as they were made.

Every claim below was verified in source at the time it was written, not taken from an agent's
report. Where a figure came from a report and was later re-measured, the re-measured value is used.

---

## 1. 🔴 Blockers — these stop the game being playable end to end

### 1.1 A battle cannot be fought · `M7-06b`

**The hero's `ActorStats` cannot be built by anyone, and not because pieces are `internal` — the
code does not exist at any accessibility.**

- There is no `AffixId → StatId`/`EffectOp` mapping anywhere in code or content.
  `drops.json#/affixPool/affixes` carries only `{id, displayName, min, max, slots}`.
- There is no gear/talent/pet `IEffectSource`. The only implementation, `ListEffectSource`, says in
  its own remarks that it *"carries no notion of gear, a perk or a draft"*.
- Behind that: `StatAggregation.Aggregate`, `HeroBaseCurve.At`, `GearStatDerivation`,
  `GearCatalogue` and `LoadoutRules` are all `internal`.
- Two production doc comments written **after** M4 landed gear say so outright
  (`StartBattle.cs`, `ConfirmBattleResult.cs`).

**Consequence:** a run that enters a battle never leaves `RunPhase.BattlePending`. The replay screen
renders, times, skips and confirms correctly against a real combat log — nothing can *feed* it one.
So *"roll → move → fight → draft → results"* stops at the third verb.

**Shape of the fix:** larger than `M7-05b` was. That one only had to *expose* something that already
existed; this must first **author the gear→stat derivation**, then decide its surface. It is a
domain-surface decision, not a client fix.

⚠️ **M3-05's tracker note is imprecise** and has been corrected: not *"no `ActorStats` buildable"*
but *"no **hero** `ActorStats` buildable"*. A client *can* build **an** `ActorStats` today —
`ActorStats.From` is public, `CombatSimulator.Simulate` is callable, and `tools/BalanceHarness`
proves it from a separate assembly with no `InternalsVisibleTo`.

### 1.2 A battle result is never verified · `M7-06c`

`ConfirmBattleResult` shape-checks the client's `LogHash` with `NumberStyles.None` and **never
recomputes it**. Any well-formed `ulong` with `Won: true` buys a full kill payout.

`14` §9 makes the server authoritative and the client's copy explicitly untrusted, so this
contradicts the security model rather than merely missing a nicety.

⚠️ **Scope:** the in-process host *is* the server today, so this is not exploitable by a third party
yet. It becomes so the moment **M5-15** swaps in HTTPS — which is exactly when it stops being cheap
to fix.

*Found by M7-06 while deciding what it was **not** allowed to fabricate. This is the concrete payoff
of the "never fill a hole with a plausible value" rule: inventing a stat block would not merely have
been dishonest, it would have walked straight through this hole.*

---

## 2. 🔴 The frozen command vocabulary is now short by at least two commands

The command vocabulary was frozen at **49** in M1, with the explicit term that *"additions afterwards
are logged decisions in `16`"*. Three separate implementation runs hit that wall and each correctly
left it alone rather than inventing a 50th wire name. **These are one product decision, not four.**

| Missing | Consequence |
|---|---|
| `EXPAND_INVENTORY` | `10` §4's ten-rung Crown ladder and the 400-Soul-Shard alternative are fully authored and **entirely unspendable**. Capacity can be modelled, priced and tested; no player action can move it. |
| `UNEQUIP` | A loadout slot can be filled and **never emptied**, except by overwriting it. `Player.Unequip(GearSlot)` and `Loadout.Without(slot)` exist and no command reaches them. |
| *(resolved)* Shop / Dice Forge clearing | Was the same shape; **closed** by `M7-00e` routing both through the existing `RESOLVE_TILE` rather than adding a command. |

M3's review had already carried Shop and Dice Forge to `M3-08b`/`M3-11` — **which sit in M11**, five
milestones away. That is why M7 had to take the minimum itself.

---

## 3. Design-document contradictions with no owner

Each of these was found by an agent reading the specs closely, and each needs a ruling from the
product owner rather than a guess.

| # | Contradiction | Where it bites |
|---|---|---|
| 3.1 | **`23` §7.2 registers `GodotPlatformInfoAdapter`/`GodotAudioAdapter`/`GodotHapticsAdapter` against ports — which this repo's test tier cannot support.** A Godot class implementing a port makes `Every_implementation_of_a_port_has_a_contract_fixture` demand a fixture, and that fixture is a fatal `AccessViolationException` (§5.3 below). One of the two has to give. | `M7-01b` declared 1 of 3 ports; the other two are deferred |
| 3.2 | **`runSeed` "never leaves the server"** — asserted in `SeedDerivation.cs` and `RunRngScope.cs` — while `RunSnapshot.RunSeed` is handed to the client verbatim with no narrowing projection. | `M7-02` / `M5-15` |
| 3.3 | **`03` §1.1 says the tile resolves and *then* the Stage Gate fires**; the code fires it at the movement landing, preserving M3-05's architecture. Deferring it needs a persisted "gate owed" flag and a rework of every follow-up-command tile. | `M7-00f` implemented the landing order |
| 3.4 | **The chapter clear gate is authored twice.** `content/chapters/*.json` carry a required `unlockCondition` that **nothing reads at runtime**, and `chapter.schema.json` permits shapes the generic ladder cannot express — such a chapter would be **opened anyway**. An Application test now pins that the two sources agree, but whether `unlockCondition` replaces or adds to the rung is undecided. | `M7-04` |
| 3.5 | **Chapter/tier gating in `START_RUN` is presentation-only and unowned.** `StartRun.Handle` checks only that the chapter id is ≥ 1 and the tier is defined. The ladder is enforced on screen and nowhere else. | `M7-04` |
| 3.6 | **`USE_REROLL` cannot re-roll the face it is shown beside.** `ROLL_DICE` answers face, movement *and* landing in one command, so the run has already moved; `UseReroll` instead burns a dice-stream draw so the *next* roll differs. `04` §3's authored UX promises an undo the shipped command cannot give. | `M7-05` |
| 3.7 | **`13` §3 is the Board screen (S05), not Home.** Home is `13` §2 and **Chapter Select has no dedicated `13` section at all** — its real sources are `02` §2 and `10` §7. *(Tracker spec ref already corrected.)* | `M7-04` |
| 3.8 | **`07` §1.1's cumulative-XP column contradicts its own prose** — `~3.04M` in the table, `~3.07M` two lines later. **Resolved by arithmetic**: the formula gives 3,036,044, so the table is right and the prose sums one term too many. Errata landed. | `M4-10` |

---

## 4. Things that are authored but unreachable

| What | Why |
|---|---|
| **The `Star` die face** | `RollDiceCommand` carries no payload, so a face needing a player choice is refused `ILLEGAL_STATE`. Confirmed still true at M7-05. |
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
| **M7** | In progress. Merged: `M7-00a/b/d/e/f/g`, `M5-01`, `M5-02`, `M7-09`, `M7-01`, `M7-01b`, `M7-03`, `M7-04`, `M7-05`, `M7-05b`, `M7-06`. Remaining: `M7-07`, `M7-08`, `M7-11`, `M7-10`, plus queued `M7-00h`, `M7-06b`, `M7-06c`. |

**M7's exit criterion will land partially met**, in the same shape as M4's:

- ✅ The APK builds through the custom export template and can be asserted to contain managed
  assemblies — the toolchain is present locally.
- ⛔ **A human playing two chapters on a physical handset** — no agent can attach to or tap a device.
  A product-owner acceptance step.
- ⛔ **CI building the APK on a runner** — no git remote.
- 🔴 **And a full run cannot yet be completed at all**, because of §1.1.

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
