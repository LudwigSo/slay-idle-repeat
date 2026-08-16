using System.Diagnostics;
using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// 🔒 <b>The M4 exit criterion, executable.</b> <em>"A simulated player runs, banks gear, merges and
/// enhances it, levels the hero and carries the loadout into the next run entirely in memory."</em>
/// Every state change below is produced by <c>GameRules.Apply</c> answering a command sent through
/// <see cref="InMemoryGame"/> — there is no call to an aggregate's mutator anywhere in this file.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>THE CRITERION IS MET IN PART, AND THE PART THAT IS NOT MET IS ASSERTED RATHER THAN OMITTED.</b>
/// Four of the six clauses are driven end to end here. Two are unreachable through the command
/// vocabulary as it stands, and each is pinned by a case below that <b>fails on the commit that
/// closes it</b> — so this file stops overstating the milestone the moment the gap is filled, rather
/// than quietly continuing to skip the hard half.
/// </para>
/// <list type="table">
///   <item><term>a simulated player runs</term><description>✅ driven — <see cref="A_simulated_player_plays_a_whole_run_through_commands_alone"/>.</description></item>
///   <item><term>banks gear</term><description>❌ <b>unreachable.</b> No production caller anywhere hands an item to the inventory, so a run cannot add one. Pinned by <see cref="A_run_banks_no_gear_because_no_production_caller_stocks_the_inventory"/>.</description></item>
///   <item><term>merges and enhances it</term><description>✅ driven, over a <em>seeded</em> stock rather than a banked one, and funded by currency the run itself paid — <see cref="The_forge_half_of_the_loop_runs_on_what_the_run_paid_for_it"/>.</description></item>
///   <item><term>levels the hero</term><description>⚠️ <b>the path is live, the rung is not reachable.</b> The run's payout does move Legend XP through <c>Apply</c>, and the level reconciliation runs on every accepted command; the reachable board cannot bank enough to cross the first rung. Pinned by <see cref="The_run_pays_Legend_XP_but_no_reachable_run_reaches_the_first_rung"/>.</description></item>
///   <item><term>carries the loadout into the next run</term><description>❌ <b>unreachable, twice over.</b> There is no next run, and the loadout is always empty. Pinned by <see cref="No_second_run_can_be_started_so_nothing_is_carried_into_one"/> and <see cref="The_loadout_carried_into_a_run_is_the_players_own_but_nothing_can_fill_it"/>.</description></item>
/// </list>
/// <para>
/// 🔒 <b>Why the content is the shipped set.</b> The harness takes a pre-built
/// <see cref="ContentSnapshot"/> and never loads one, so this file hands it
/// <c>ShippedHarness.Content</c> — the same composition <c>BossCatalogueTests</c> already drives, off
/// the real <c>game-data/</c>. A hermetic fixture cannot serve: a run reads a chapter's board, its
/// enemy weights, the dice bag, the stage gates, the run payout, the forge ladder, the gear tables
/// and the pity registry, and the fixtures that carry those hold two mutually exclusive
/// <c>currencies.json</c> bodies between them.
/// </para>
/// <para>
/// ⚠️ This is a unit test in the domain suite, driving the in-memory harness in-process. There is no
/// integration tier in this repository and this is not one.
/// </para>
/// </remarks>
public sealed class MetaLoopTests
{
    /// <summary>The chapter the loop is driven on — the only one whose board and enemies are authored beside chapter 2.</summary>
    private const int Chapter = 1;

    /// <summary>The seed. Chosen for the tile mix it produces, and named so a failure re-runs.</summary>
    private const ulong Seed = 0xA11CE_0000_1111UL;

    /// <summary>How many identical items the player starts holding. Three is a fusion; the rest fund it.</summary>
    private const int StartingStock = 12;

    /// <summary>`08` §4.1 — a fusion takes three inputs of one item at one band and one enhance level.</summary>
    private const int FusionInputs = 3;

    /// <summary>
    /// `08` §4.1's cheapest fusion price, in Crowns — a C-band trio landing on B. The floor the run
    /// that funds <see cref="The_forge_half_of_the_loop_runs_on_what_the_run_paid_for_it"/> has to
    /// clear.
    /// </summary>
    private const long FusionCrownFloor = 120L;

    /// <summary>
    /// The chapter the forge case runs, and it is <b>not</b> <see cref="Chapter"/> — see
    /// <see cref="ForgeStart"/> for why a second board is needed rather than wanted.
    /// </summary>
    private const int ForgeChapter = 2;

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 🔴 The forge case's own start instant, and the reason it exists is a finding rather than a
    /// preference.
    /// </summary>
    /// <remarks>
    /// A run's board is derived from its own seed, which folds in the instant the run started — so
    /// the clock, not <see cref="Seed"/>, is what selects a board. The board
    /// <see cref="A_simulated_player_plays_a_whole_run_through_commands_alone"/> drives pays
    /// <b>80</b> Crowns before the stall parks it, and the cheapest fusion costs
    /// <see cref="FusionCrownFloor"/>. Swept across both authored chapters and two thousand start
    /// instants each while writing this, no board paid a fusion's price <em>and</em> ended in a
    /// death; the ones that pay it end by being abandoned. This is the one that funds a fusion, and
    /// it is a chapter 2 board because chapter 1's do not. That a run barely covers a single C-band
    /// fusion is itself a consequence of the stall
    /// (<see cref="A_run_stalls_on_its_stage_boundary_and_can_never_reach_the_boss"/>): four tiles
    /// is what a stalled run gets to resolve.
    /// </remarks>
    private static readonly DateTimeOffset ForgeStart = Start.AddHours(12);

    // ═════════════════════════════════════════════════════════ the run

    /// <summary>
    /// 🔒 <b>Clause 1 — "a simulated player runs".</b> A run starts, moves across the board, resolves
    /// the tiles it lands on, fights, opens a draft, and ends — every step of it a command.
    /// </summary>
    /// <remarks>
    /// The assertions are about the run having gone <em>somewhere</em> and done several
    /// <em>different</em> things. A count of accepted commands would be satisfied by a hundred
    /// refused rolls at the trailhead, and "the run ended" is satisfied by <c>ABANDON_RUN</c> on the
    /// first command.
    /// </remarks>
    [Fact]
    public void A_simulated_player_plays_a_whole_run_through_commands_alone()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
        var run = game.State(player).Run;

        run.ShouldNotBeNull(Trace(driver));

        run!.Phase.ShouldBe(
            RunPhase.Ended,
            "the run did not end. " + driver.Ending + Trace(driver));

        driver.Visited.Count.ShouldBeGreaterThan(
            2,
            "a run that stood on at most two board positions did not travel — the trailhead and one " +
            "node is what a single roll produces, and every clause below rests on this one having " +
            "actually moved." + Trace(driver));

        driver.Tiles.Distinct().Count().ShouldBeGreaterThan(
            1,
            "every tile the run resolved was the same kind, so the resolution path is exercised in " +
            "one shape only." + Trace(driver));

        driver.BattlesFought.ShouldBeGreaterThan(
            0, "no battle was fought, so START_BATTLE and CONFIRM_BATTLE_RESULT are not on this " +
            "path at all." + Trace(driver));

        driver.BattlesWon.ShouldBeGreaterThan(
            0, "every battle was lost. A won battle is what banks XP and opens a draft, so a loop " +
            "with none of them proves neither." + Trace(driver));

        driver.DraftsSkipped.ShouldBeGreaterThan(
            0, "no draft was ever opened, so 24 §4.7's DRAFT guarantees — one of the four live grant " +
            "classes — are not on this path." + Trace(driver));

        run.BankedLegendXp.ShouldBeGreaterThan(
            0L, "the run banked no Legend XP, so the reward path the hero clause depends on never " +
            "ran." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>Clause 2 — "banks gear" — is NOT reachable, and this is the failing witness the register
    /// says is waiting for it.</b> The run above resolves treasure, a cache and a minigame and pays
    /// out at its end, and the player's stock is byte-for-byte what it started as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The cause is a missing caller, not a missing rule.</b> <c>Rules.Gear.GearGeneration</c>
    /// rolls a run drop and a session floor, <c>Model.Gear.Inventory.Place</c> takes a granted item,
    /// and <c>Events.GearGranted</c> reports one — and nothing in <c>SlayIdleRepeat.Core</c> calls
    /// any of the three. <c>GapRegister</c> writes this down with an owner (M4-02) and names this
    /// very test as the witness; that is what this case makes executable.
    /// </para>
    /// <para>
    /// 🔒 <b>It expires by itself (steering S4).</b> The day a run drop reaches the stock, the
    /// comparison below stops holding and this case goes red — which is the signal to delete it and
    /// drive the merge and enhance clauses off <em>banked</em> gear instead of the seeded stock they
    /// use today.
    /// </para>
    /// <para>
    /// Compared as canonical bytes rather than by count or by record equality (steering S17): an
    /// <c>InventorySnapshot</c>'s item list compares by reference under synthesized equality, and a
    /// count is satisfied by a run that swapped one item for another.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_banks_no_gear_because_no_production_caller_stocks_the_inventory()
    {
        var (game, player) = Loop();
        var before = StockBytes(game, player);

        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        game.State(player).Run!.Phase.ShouldBe(RunPhase.Ended, Trace(driver));

        StockBytes(game, player).ShouldBe(
            before,
            "the player's stock CHANGED across a whole run — so something now grants gear mid-run, " +
            "and the M4 exit criterion's 'banks gear' clause has become reachable. Delete this case " +
            "and drive the merge and enhance clauses off the banked item instead of the seeded " +
            "stock. Until that commit, the criterion is met in part and this is the part that is " +
            "not: no production caller in Core hands an item to Inventory.Place, so a run cannot " +
            "add one." + Trace(driver));

        game.Events.OfType<GearGranted>().ShouldBeEmpty(
            "a GearGranted event was emitted, so a grant path has been wired. Same consequence as " +
            "above — this case is now the stale half of the claim, not the true one.");
    }

    // ═════════════════════════════════════════════════════════ the forge

    /// <summary>
    /// 🔒 <b>Clause 3 — "merges and enhances it".</b> <c>SALVAGE</c>, <c>ENHANCE</c> and
    /// <c>MERGE</c>, in that order, each through <c>Apply</c>, and paid for with currency the run
    /// itself produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The funding is the point of the ordering.</b> A harness player's wallet starts at zero
    /// in every column and there is no seam to change that, so the forge is affordable only because
    /// the run paid: the Crowns come from the tiles the run resolved and the Enhance Stones from
    /// those plus the salvage. Asserting the balances rose <em>during the run</em> is what makes this
    /// a loop rather than three commands against a stacked wallet.
    /// </para>
    /// <para>
    /// ⚠️ The stock those three commands operate on is <b>seeded</b>, not banked — see
    /// <see cref="A_run_banks_no_gear_because_no_production_caller_stocks_the_inventory"/>. That is
    /// the one substitution this file makes for the criterion, and it is made through
    /// <c>CreatePlayer</c>'s starting row (which goes through <c>Player.Rehydrate</c>, the validated
    /// construction path) rather than by reaching past <c>Apply</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_forge_half_of_the_loop_runs_on_what_the_run_paid_for_it()
    {
        var (game, player) = Loop(ForgeStart);

        game.State(player).Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(
            0L, "a harness player starts every wallet column at zero, so anything spent below was " +
            "earned by a command.");

        var driver = MetaLoopDriver.Play(game, player, ForgeChapter, DifficultyTier.NORMAL);
        var afterRun = game.State(player).Player;

        afterRun.BalanceOf(CurrencyId.CROWNS).ShouldBeGreaterThanOrEqualTo(
            FusionCrownFloor,
            "the run paid " + afterRun.BalanceOf(CurrencyId.CROWNS) + " Crowns and 08 §4.1 prices " +
            "the cheapest fusion at " + FusionCrownFloor + ". A harness player's wallet starts at " +
            "zero in every column and there is no seam to change that, so a run that does not cover " +
            "the fusion means the merge below cannot be driven at all." + Trace(driver));

        afterRun.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBeGreaterThan(
            0L, "the run paid no Enhance Stones, so the enhancement below is unfunded." + Trace(driver));

        // ── SALVAGE: four of the stock become Merge Dust.
        var stock = Ids(game, player);
        var scrapped = stock.Skip(1 + FusionInputs).Take(4).ToArray();

        Accepted(driver.Send(new SalvageCommand(scrapped)), "SALVAGE", driver);

        var afterSalvage = game.State(player).Player;

        afterSalvage.BalanceOf(CurrencyId.MERGE_DUST).ShouldBeGreaterThan(
            0L, "salvaging four items paid no Merge Dust." + Trace(driver));
        afterSalvage.Inventory.Stored.Count.ShouldBe(
            StartingStock - scrapped.Length, "the salvaged items are gone." + Trace(driver));

        // ⚠️ No Enhance Stones are asserted here, and that is the rule rather than a gap: 08 §4.3
        // refunds the stones INVESTED in an item, and every item in this stock is unenhanced, so a
        // refund of zero is the correct answer rather than a missing one.

        // ── ENHANCE: an attempt is made and paid for, win or lose.
        var target = stock[0];
        var stonesBeforeAttempt = afterSalvage.BalanceOf(CurrencyId.ENHANCE_STONES);

        Accepted(driver.Send(new EnhanceCommand(target)), "ENHANCE", driver);

        var afterEnhance = game.State(player).Player;
        var enhanced = afterEnhance.Inventory.Find(target);

        enhanced.ShouldNotBeNull("the enhanced item is still in the stock." + Trace(driver));
        afterEnhance.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBeLessThan(
            stonesBeforeAttempt,
            "08 §4.2 charges the stones whether the attempt succeeds or fails, so a balance that did " +
            "not move means no attempt was made." + Trace(driver));

        (enhanced!.EnhanceLevel > 0 || enhanced.EnhanceFailures > 0).ShouldBeTrue(
            "the attempt neither raised the level nor recorded a failure, so nothing happened to the " +
            "item at all — 24 §4.6's mercy counts the failures, and one of the two has to move." +
            Trace(driver));

        // ── MERGE: three of the untouched items fuse into one at the next band up.
        var inputs = stock.Skip(1).Take(FusionInputs).ToArray();
        var inputBand = afterEnhance.Inventory.Find(inputs[0])!.Rarity;
        var crownsBefore = afterEnhance.BalanceOf(CurrencyId.CROWNS);
        var stockBefore = StockBytes(game, player);

        Accepted(driver.Send(new MergeCommand(inputs, dustSubstituted: false)), "MERGE", driver);

        var afterMerge = game.State(player).Player;

        afterMerge.BalanceOf(CurrencyId.CROWNS).ShouldBeLessThan(
            crownsBefore, "08 §4.1 prices a fusion in Crowns; a wallet that did not move means the " +
            "fusion was not paid for." + Trace(driver));

        afterMerge.Inventory.Stored.Count.ShouldBe(
            StartingStock - scrapped.Length - FusionInputs + 1,
            "a fusion consumes three items and leaves one." + Trace(driver));

        ((int)afterMerge.Inventory.Find(inputs[0])!.Rarity).ShouldBeGreaterThan(
            (int)inputBand,
            "the fusion's output is on the same rung it started on. 08 §4.1 fuses ONTO the next band, " +
            "and that decision is LuckService.MergeOutputBand's — an output at the input band means " +
            "the fusion did not go through it." + Trace(driver));

        StockBytes(game, player).ShouldNotBe(
            stockBefore, "the fusion left the stock byte-identical, so nothing was actually fused.");
    }

    // ═════════════════════════════════════════════════════════ the hero

    /// <summary>
    /// ⚠️ <b>Clause 4 — "levels the hero" — the path is live and the rung is out of reach.</b> The
    /// run's payout moves lifetime Legend XP through <c>Apply</c>, and <c>GameRules</c> reconciles
    /// the level on every accepted command; what no reachable run can do is bank enough to cross the
    /// first rung.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The arithmetic, so the claim is checkable rather than asserted.</b> The first rung costs
    /// <c>120</c> lifetime XP. A run that ends in a stage-1 death — the only ending a
    /// command-driven run can reach, see
    /// <see cref="A_run_stalls_on_its_stage_boundary_and_can_never_reach_the_boss"/> — pays a quarter
    /// of what it banked, so it needs <c>480</c> banked. Chapter 1 at NORMAL pays 25 a normal kill,
    /// and the reachable part of the board holds a handful of them. Swept across chapters 1–2, all
    /// three tiers and forty seeds while writing this, the best any run reached was <b>93</b> lifetime
    /// XP from <b>372</b> banked. There is no second run to accumulate across.
    /// </para>
    /// <para>
    /// 🔒 <b>It expires by itself (steering S4).</b> Both halves are asserted: the XP has to move
    /// (so a broken payout is caught), and the level has to still be the floor (so the day the boss
    /// becomes reachable — a victory pays the boss kill plus the victory bonus, far over the rung —
    /// this case goes red and asks for the level-up to be asserted properly).
    /// </para>
    /// </remarks>
    [Fact]
    public void The_run_pays_Legend_XP_but_no_reachable_run_reaches_the_first_rung()
    {
        var (game, player) = Loop();
        var floor = game.State(player).Player.LegendLevel;

        game.State(player).Player.LegendXp.ShouldBe(0L, "a harness player starts with no XP.");

        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
        var hero = game.State(player).Player;

        hero.LegendXp.ShouldBeGreaterThan(
            0L,
            "the run ended and paid no lifetime Legend XP at all, so END_RUN's payout never reached " +
            "the player — the half of this clause that IS reachable." + Trace(driver));

        hero.LegendXp.ShouldBeLessThan(
            RungOne,
            "the run banked enough to cross the first Legend rung, which no run this loop can drive " +
            "was able to do when this was written. That is good news and this case is now the stale " +
            "half of the claim: assert the level-up itself — LegendLevel, TalentPoints and the " +
            "legend_level_up Energy refill — and delete this bound." + Trace(driver));

        hero.LegendLevel.ShouldBe(
            floor,
            "the Legend Level moved while lifetime XP stayed under the first rung, which means the " +
            "reconciliation is deriving a level the curve does not authorise." + Trace(driver));

        hero.TalentPoints.ShouldBe(
            0L, "07 §1.1 grants Talent Points on the way up, and no level was gained." + Trace(driver));
    }

    // ═════════════════════════════════════════════════════════ the carry

    /// <summary>
    /// 🔒 <b>Clause 5a — there is no next run.</b> Once a run has ended, <c>START_RUN</c> is refused
    /// <c>RUN_ALREADY_ENDED</c> for good: it is a run command, the gate ahead of dispatch refuses
    /// every run command on an ended run, and nothing in <c>Apply</c> ever puts the slice's run back
    /// to absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This is the clause the exit criterion cannot reach, and it is structural.</b> "Carries
    /// the loadout into the next run" needs a next run; through <c>GameRules.Apply</c> a player gets
    /// exactly one, for the life of the slice. Clearing the ended run is something only a caller
    /// outside <c>Apply</c> could do — the Application layer loading a fresh slice — and the harness,
    /// which is the artefact that demonstrates the domain is playable from <c>Core</c> alone, has no
    /// seam for it and stores whatever <c>Apply</c> returned.
    /// </para>
    /// <para>
    /// ⚠️ <b>The refusal is asserted by identity, not by "it was refused" (steering S2).</b> Three
    /// other rules answer <c>ILLEGAL_STATE</c> to a <c>START_RUN</c> — a run already open, a chapter
    /// below one, an undeclared tier — so a case that only checked for a rejection would pass while
    /// the ended-run gate had gone.
    /// </para>
    /// <para>
    /// 🔒 <b>It expires by itself (steering S4)</b>: the day a second run can be started, this goes
    /// red and asks for the carry to be asserted across the boundary for real.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_second_run_can_be_started_so_nothing_is_carried_into_one()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        game.State(player).Run!.Phase.ShouldBe(RunPhase.Ended, Trace(driver));

        var second = driver.Send(new StartRunCommand(Chapter, DifficultyTier.NORMAL));

        second.Accepted.ShouldBeFalse(
            "a SECOND run started. The M4 exit criterion's last clause — 'carries the loadout into " +
            "the next run' — is reachable now: drive the carry across the boundary and compare the " +
            "second run's StartingLoadout with the loadout the player held between the two, by " +
            "canonical bytes." + Trace(driver));

        second.Rejection.ShouldBe(
            RejectionReason.RUN_ALREADY_ENDED,
            "START_RUN was refused for some OTHER reason than the ended run still sitting in the " +
            "slice, so this case is no longer about the thing it was written for." + Trace(driver));

        game.State(player).Player.RunsStarted.ShouldBe(
            1L, "a refused START_RUN must not spend the run counter." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>Clause 5b — the carry mechanism is wired, and nothing can put anything in it.</b> The
    /// run freezes the player's loadout at <c>START_RUN</c> and it is byte-identical to the one the
    /// player holds; that loadout is empty, and no command in the vocabulary can fill it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>An empty loadout would make the comparison vacuous, so the reason it is empty is
    /// asserted too.</b> <c>EQUIP</c> is a deferred dispatch row, so a player cannot put an item in a
    /// slot; <c>SAVE_PRESET</c> stores whatever the loadout currently is and <c>APPLY_PRESET</c>
    /// puts that back, so the two of them together cannot introduce a first item either. Both are
    /// driven below — they are the only wired loadout writers, and they round-trip.
    /// </para>
    /// <para>
    /// 🔒 The comparison is by canonical bytes (steering S17). <c>LoadoutSnapshot</c> holds an
    /// <c>IReadOnlyDictionary</c>, which a synthesized record <c>Equals</c> compares by reference, and
    /// <c>ToSnapshot</c> allocates a fresh map each call — so record equality would report two
    /// identical loadouts as different, and, worse, a comparison written against the empty-map
    /// singleton would report success by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_loadout_carried_into_a_run_is_the_players_own_but_nothing_can_fill_it()
    {
        var (game, player) = Loop();

        // The two wired loadout writers, before the run — APPLY_PRESET is refused mid-run.
        var driver = MetaLoopDriver.Idle(game, player);

        Accepted(driver.Send(new SavePresetCommand(1, "opener")), "SAVE_PRESET", null);
        Accepted(driver.Send(new ApplyPresetCommand(1)), "APPLY_PRESET", null);

        var equip = driver.Send(new EquipCommand(Ids(game, player)[0], GearSlot.WEAPON));

        equip.Accepted.ShouldBeFalse(
            "EQUIP was accepted, so a hero can wear an item now. The loadout clause of the M4 exit " +
            "criterion stops being vacuous on this commit: equip an item, then compare what the next " +
            "run froze against what the player holds.");
        equip.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "a deferred command answers ILLEGAL_STATE; some other reason means EQUIP is handled and " +
            "refusing for a reason of its own.");

        var play = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
        var slice = game.State(player);

        Canonical(slice.Run!.StartingLoadout.ToSnapshot()).ShouldBe(
            Canonical(slice.Player.Loadout.ToSnapshot()),
            "the loadout the run froze at START_RUN is not the one the player was holding. That " +
            "freeze IS the carry — a run reads the player's loadout once and keeps it for its whole " +
            "life — so a mismatch means the carry is broken, empty or not." + Trace(play));

        slice.Player.Loadout.Gear.ShouldBeEmpty(
            "the loadout is no longer empty, which means something filled it — so the comparison " +
            "above has become a real one and the EQUIP assertion above has already told you what to " +
            "write instead." + Trace(play));

        slice.Player.Presets.Count.ShouldBe(
            1, "SAVE_PRESET stored one preset and nothing removed it." + Trace(play));
    }

    // ═════════════════════════════════════════════════════════ the two blockers, named

    /// <summary>
    /// 🔴 <b>Why the run above cannot win.</b> A roll taken from the last node of a stage is accepted
    /// and moves the run nowhere, and every roll after it does the same — so a command-driven run can
    /// never leave stage 1, never reach the boss node, and never end in a victory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>03</c> §1.1 authors the stage-end clamp as <em>"if a die roll would move the player past
    /// the last node of a stage, the player stops on the last node and the Stage Gate fires"</em> — a
    /// one-time stop, after which the next roll carries on. The implementation applies the clamp
    /// whenever the <em>next</em> node belongs to another stage, which is also true when the run is
    /// already standing on the boundary node, so it returns the current node with the whole roll
    /// unspent. Stage 3 → boss is the one transition with an explicit exception.
    /// </para>
    /// <para>
    /// 🔒 <b>Recorded here rather than repaired.</b> The repair is a change to how every run in the
    /// game moves, and it belongs with the board rules and their own suite, not inside the milestone's
    /// exit-criterion test. What this case buys is that the criterion's "runs" clause stops being
    /// quietly satisfied by a run that never got anywhere: the day movement is fixed, this goes red
    /// and the loop above can be driven to a boss and a victory payout.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_stalls_on_its_stage_boundary_and_can_never_reach_the_boss()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        driver.StalledAt.ShouldNotBeNull(
            "no roll was accepted without moving the run, so the stage-boundary stall is gone. If " +
            "movement was repaired, this case has done its job: delete it, and drive the loop above " +
            "to the boss node so the run ends in a VICTORY and the hero clause becomes assertable." +
            Trace(driver));

        game.State(player).Run!.BossDefeated.ShouldBeFalse(
            "the run reached and beat the boss, which the stall makes impossible — so the stall is " +
            "gone and this case is stale." + Trace(driver));

        driver.Ending.StartsWith("END_RUN", StringComparison.Ordinal).ShouldBeTrue(
            "the run ended as '" + driver.Ending + "' rather than by a death after the stall. That " +
            "is the only ending a stalled run has, so a different one means the shape of this loop " +
            "has changed." + Trace(driver));
    }

    // ═════════════════════════════════════════════════════════ the budget

    /// <summary>
    /// The whole loop belongs to the unit tier, re-measured on this branch rather than quoted from
    /// an earlier one.
    /// </summary>
    /// <remarks>
    /// Measured best-of-three on a Debug build of this checkout, warm, with the figure written into
    /// the message so a failure reports what it actually cost. The bound is an order-of-magnitude
    /// regression detector on `30` §6's 200 ms budget, exactly as
    /// <c>InMemoryGamePerformanceTests</c> frames its own — not a budget check.
    /// </remarks>
    [Fact]
    public void The_whole_meta_loop_stays_inside_the_unit_tier_budget()
    {
        _ = Play();

        var best = double.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            var driver = Play();
            watch.Stop();

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);

            driver.Visited.Count.ShouldBeGreaterThan(
                2, "the measurement is only about the loop if the loop ran (steering S3).");
        }

        best.ShouldBeLessThan(
            BudgetMs * RegressionMultiple,
            "the whole meta loop took " + best.ToString("F1", CultureInfo.InvariantCulture) +
            " ms. 30 §6 budgets 200 ms for a command drive and this asserts 2,000 — ten times, on " +
            "InMemoryGamePerformanceTests' precedent. If it fires, look for a content read moved " +
            "into the command loop; do not raise it.");
    }

    /// <summary>`30` §6's budget, in milliseconds.</summary>
    private const double BudgetMs = 200;

    /// <summary>The multiple of it the assertion uses — a regression detector, not a budget check.</summary>
    private const double RegressionMultiple = 10;

    /// <summary>
    /// `07` §1.1's first Legend rung, in lifetime XP: the coefficient times the minimum level raised
    /// to the exponent, which at level 1 is the coefficient itself.
    /// </summary>
    private const long RungOne = 120L;

    // ═════════════════════════════════════════════════════════ fixtures

    private static MetaLoopDriver Play()
    {
        var (game, player) = Loop();

        return MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
    }

    /// <summary>
    /// A harness over the shipped content, and a player holding a stock the run cannot give them.
    /// </summary>
    private static (InMemoryGame Game, PlayerId Player) Loop(DateTimeOffset? start = null)
    {
        var game = new InMemoryGame(ShippedHarness.Content, Seed, new VirtualClock(start ?? Start));
        var player = game.CreatePlayer(inventory: Inventories.Stock(StartingStock));

        game.Send(player, new BeginSessionCommand("1.0.0", "content"));

        return (game, player);
    }

    private static IReadOnlyList<GearInstanceId> Ids(InMemoryGame game, PlayerId player) =>
        game.State(player).Player.Inventory.Stored.Select(item => item.InstanceId).ToArray();

    private static byte[] StockBytes(InMemoryGame game, PlayerId player) =>
        CanonicalStateWriter.CanonicalBytes(game.State(player).Player.Inventory.ToSnapshot());

    private static byte[] Canonical(object snapshot) =>
        CanonicalStateWriter.CanonicalBytes(snapshot);

    private static void Accepted(CommandResult result, string name, MetaLoopDriver? driver)
    {
        result.Accepted.ShouldBeTrue(
            name + " was refused " + result.Rejection + ". The loop cannot go on without it." +
            (driver is null ? "" : Trace(driver)));
    }

    private static string Trace(MetaLoopDriver driver) =>
        Environment.NewLine + "commands: " + string.Join(Environment.NewLine + "  ", driver.Log);
}
