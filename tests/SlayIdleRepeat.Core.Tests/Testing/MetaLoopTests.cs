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
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;
using SlayIdleRepeat.Core.Tests.Rules.Combat;

namespace SlayIdleRepeat.Core.Tests.Testing;

/// <summary>
/// 🔒 <b>The M4 exit criterion, executable.</b> <em>"A simulated player runs, banks gear, merges and
/// enhances it, levels the hero and carries the loadout into the next run entirely in memory."</em>
/// Every state change below is produced by <c>GameRules.Apply</c> answering a command sent through
/// <see cref="InMemoryGame"/> — there is no call to an aggregate's mutator anywhere in this file.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>EVERY CLAUSE IS NOW DRIVEN END TO END.</b> The last two closed together: a run can leave a
/// Shop and a Dice Forge, so it walks the whole board and ends in a <em>victory</em> — and a victory
/// pays enough to carry the hero over the first Legend rung, which is what made "levels the hero"
/// assertable at last. Nothing below is skipped or substituted except the forge's stock, which is
/// seeded rather than banked and says so.
/// </para>
/// <list type="table">
///   <item><term>a simulated player runs</term><description>✅ driven — <see cref="A_simulated_player_plays_a_whole_run_through_commands_alone"/>.</description></item>
///   <item><term>banks gear</term><description>✅ driven — <see cref="A_run_banks_gear_into_the_players_own_stock"/>.</description></item>
///   <item><term>merges and enhances it</term><description>✅ driven, over a <em>seeded</em> stock rather than a banked one, and funded by currency the run itself paid — <see cref="The_forge_half_of_the_loop_runs_on_what_the_run_paid_for_it"/>.</description></item>
///   <item><term>levels the hero</term><description>✅ <b>driven, and the level-up itself is asserted rather than its absence.</b> A run that reaches a victory banks lifetime Legend XP well over the first rung, and the reconciliation that runs on every accepted command raises the Legend Level, grants Talent Points and refills Energy — <see cref="A_run_that_wins_levels_the_hero_past_the_first_Legend_rung"/>.</description></item>
///   <item><term>carries the loadout into the next run</term><description>✅ <b>both halves closed, by two different tasks that could not see each other.</b> M7-00b made a second run startable and M7-00d made the loadout fillable, so the carry is now compared across a real boundary with a non-empty loadout — <see cref="A_second_run_starts_after_the_first_ends_and_carries_the_players_loadout"/> and <see cref="The_loadout_carried_into_a_run_is_the_one_EQUIP_filled"/>. Until both landed, each half made the other's assertion vacuous.</description></item>
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

    /// <summary>
    /// `07` §1.1's first Legend rung, in lifetime XP: the coefficient times the minimum level raised
    /// to the exponent, which at level 1 is the coefficient itself.
    /// </summary>
    private const long RungOne = 120L;

    /// <summary>
    /// The wall-clock bound on a whole loop, in milliseconds — ~25× the ~4 ms measured when this
    /// landed. See <see cref="The_whole_meta_loop_stays_inside_the_unit_tier_budget"/>.
    /// </summary>
    private const double LoopBudgetMs = 750;

    /// <summary>How many Stage Gates a whole run crosses: out of stage 1 and out of stage 2, never a third.</summary>
    private const int ExpectedStageGates = 2;

    /// <summary><c>MetaLoopDriver</c>'s own wording for a run that ended by beating the boss.</summary>
    private const string VictoryEnding = "END_RUN after a victory";

    /// <summary>The attribution token <c>GameRules</c> logs a Legend level-up's Energy refill under.</summary>
    private const string LegendLevelUpReason = "legend_level_up";

    private static readonly DateTimeOffset Start = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 🔴 The forge case's own start instant, and the reason it exists is a finding rather than a
    /// preference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run's board is derived from its own seed, which folds in the instant the run started — so
    /// the clock, not <see cref="Seed"/>, is what selects a board. The board
    /// <see cref="A_simulated_player_plays_a_whole_run_through_commands_alone"/> drives did not
    /// cover <see cref="FusionCrownFloor"/>, the cheapest fusion's price. Swept across both authored
    /// chapters and two thousand start instants each while this was written, no board paid a
    /// fusion's price <em>and</em> ended in a death; the ones that pay it end by being abandoned.
    /// This is the one that funds a fusion, and it is a chapter 2 board because chapter 1's do not.
    /// </para>
    /// <para>
    /// ⚠️ That sweep was measured before X-10 was repaired, when a run resolved four tiles and then
    /// parked. A repaired run travels much further and pays more, so this instant is now a
    /// sufficient choice rather than a uniquely necessary one — the assertions below still hold, and
    /// re-sweeping for a cheaper board would only be tidying.
    /// </para>
    /// </remarks>
    private static readonly DateTimeOffset ForgeStart = Start.AddHours(12);

    /// <summary>
    /// 🔴 The gear-banking case's own start instant, and it exists for the same reason
    /// <see cref="ForgeStart"/> does: the board is selected by the clock.
    /// </summary>
    /// <remarks>
    /// The board <see cref="Start"/> produces resolves two Treasure tiles and one ordinary enemy, and
    /// an ordinary kill drops at the authored per-kill chance — under a tenth — so a run on it banks
    /// gear only by luck. This board resolves an <b>Elite</b>, which is the kill kind the acquisition
    /// rates guarantee a drop from, so the clause is driven rather than hoped for. Swept across both
    /// authored chapters and a day of start instants while writing this.
    /// </remarks>
    private static readonly DateTimeOffset DropStart = Start.AddHours(3);

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
            "the run did not end. " + driver.Ending + Environment.NewLine +
            "⚠️ If the command budget was exhausted, the likeliest cause is a tile the driver has no " +
            "command for and does not yet name — it will re-send RESOLVE_TILE at one position until " +
            "the budget runs out. MetaLoopDriver.StuckOn concludes that from the commands and ends " +
            "the run; teach the driver the kind's own command rather than widening the budget. " +
            "add the kind there rather than widening the budget." +
            Trace(driver));

        driver.Visited.Distinct().Count().ShouldBeGreaterThan(
            2,
            "a run that stood on at most two DISTINCT board nodes did not travel, and every clause " +
            "below rests on this one having actually moved. Distinct rather than the raw count: the " +
            "list collapses only consecutive repeats, so a movement regression that bounced a run " +
            "between two nodes would otherwise read as four." + Trace(driver));

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
    /// 🔒 <b>Clause 2 — "banks gear".</b> A run resolves its tiles, fights and pays out, and the
    /// player's stock is larger at the end than it was at the start — every item of the difference
    /// reported by a <c>GearGranted</c> the run itself emitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The board is chosen so the clause is driven rather than hoped for</b> — see
    /// <see cref="DropStart"/>. An ordinary kill drops at the authored per-kill chance, which is
    /// under a tenth; an Elite kill drops the authored count every time, so the run has to reach one
    /// for "banks gear" to be a claim about the wiring rather than about a coin.
    /// </para>
    /// <para>
    /// The stock is compared by the identities that appeared in it rather than by canonical bytes,
    /// because the interesting quantity here is <em>which</em> items arrived: the same comparison has
    /// to be able to say that every one of them is an item a <c>GearGranted</c> named, and a byte
    /// difference cannot.
    /// </para>
    /// <para>
    /// 🔴 <b>The exit criterion says "banks gear, merges and enhances <em>it</em>", and until now the
    /// <em>it</em> was never driven.</b> The forge case operates on a <c>Inventories.Stock(12)</c>
    /// seeded through <c>CreatePlayer</c>, on a different board from this one, so the two halves of
    /// one clause never met: gear was banked here and something else was enhanced there, and a
    /// banked item that no forge command would accept — a wrong slot, an unrehydratable roll, an
    /// identity the stock could not find again — passed both. The <c>ENHANCE</c> below closes the
    /// chain on the item this run actually handed the player.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_banks_gear_into_the_players_own_stock()
    {
        var (game, player) = Loop(DropStart);
        var before = Owned(game, player);

        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        game.State(player).Run!.Phase.ShouldBe(RunPhase.Ended, Trace(driver));

        // 🔒 The premise, asserted rather than narrated: "the stock grew across a run" is a claim
        // about a run that actually fought something. A run that refused every command would make
        // the assertion below fail for a reason that has nothing to do with the grant path.
        // KillsWon, not Tiles: an Elite the run merely ARRIVED at owes nothing — the drop is owed by
        // the kill — so a board that offered one and a hero that lost to it would leave the
        // assertion below a coin toss again.
        driver.KillsWon.ShouldContain(
            TileKind.Elite,
            "this run won no Elite battle, so no kill it made is guaranteed to drop. Pick a start " +
            "instant whose reachable tiles include an Elite the hero beats — see DropStart." +
            Trace(driver));

        var banked = Owned(game, player).Except(before).ToArray();

        banked.ShouldNotBeEmpty(
            "the run won " + driver.BattlesWon + " battle(s), at least one of them an Elite, and " +
            "banked nothing at all. The M4 exit criterion's 'banks gear' clause is exactly this: a " +
            "kill hands an item to the stock." + Trace(driver));

        game.Events.OfType<GearGranted>().Select(granted => granted.Item.InstanceId).ShouldBe(
            banked,
            ignoreOrder: true,
            "the items that appeared in the stock and the items GearGranted reported are not the " +
            "same set, so either an item arrived unannounced or an announcement named an item the " +
            "player never received." + Trace(driver));

        // ── …and the exit criterion's "merges and enhances IT": the forge acts on the item the run
        //    just banked, not on a seeded stand-in.
        var target = banked[0];
        var beforeAttempt = game.State(player).Player.Inventory.Find(target);

        beforeAttempt.ShouldNotBeNull(
            "the banked item is in the STORED stock, which is where a forge command looks for it. An " +
            "item that only ever reached overflow cannot be enhanced, and the clause would be " +
            "unreachable through the front door." + Trace(driver));

        var stonesBefore = game.State(player).Player.BalanceOf(CurrencyId.ENHANCE_STONES);

        Accepted(driver.Send(new EnhanceCommand(target)), "ENHANCE on the banked item", driver);

        var enhanced = game.State(player).Player.Inventory.Find(target);

        enhanced.ShouldNotBeNull("the enhanced item is still in the stock." + Trace(driver));

        game.State(player).Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBeLessThan(
            stonesBefore,
            "08 §4.2 charges the stones whether the attempt lands or misses, so a balance that did " +
            "not move means no attempt was made on the banked item at all." + Trace(driver));

        (enhanced!.EnhanceLevel > beforeAttempt!.EnhanceLevel ||
         enhanced.EnhanceFailures > beforeAttempt.EnhanceFailures).ShouldBeTrue(
            "the attempt on the BANKED item neither raised its level nor recorded a failure, so " +
            "nothing happened to it — 24 §4.6's mercy counts the failures, and one of the two has to " +
            "move. This is the assertion that makes the exit criterion's 'enhances IT' a chain " +
            "rather than two unrelated boards." + Trace(driver));
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
    /// ⚠️ <b>The salvage asserts dust and not a stone refund</b>, and that is `08` §4.3 rather than a
    /// gap: the refund returns the stones <em>invested</em> in an item, and every item in this
    /// seeded stock is unenhanced, so zero is the right answer. Asserting a refund here would be
    /// asserting a bug.
    /// </para>
    /// <para>
    /// ⚠️ The stock these three commands operate on is <b>seeded</b>, not banked, and it stays that
    /// way on purpose: it is a chapter-2 board chosen for what it <em>pays</em>
    /// (see <see cref="ForgeStart"/>), not for what it drops, so a fusion needs three matching items
    /// the run has no reason to have produced. The banked-gear half of the same clause is driven on
    /// its own board by <see cref="A_run_banks_gear_into_the_players_own_stock"/>, which enhances the
    /// item the run actually handed the player. The seeding is made through <c>CreatePlayer</c>'s
    /// starting row — which goes through <c>Player.Rehydrate</c>, the validated construction path —
    /// rather than by reaching past <c>Apply</c>.
    /// <para>
    /// 🔴 This used to point at <c>A_run_banks_no_gear_because_no_production_caller_stocks_the_inventory</c>,
    /// a member deleted when the grant path landed. The dangling <c>cref</c> was silent because
    /// <c>GenerateDocumentationFile</c> is off in this project.
    /// </para>
    /// </para>
    /// </remarks>
    [Fact]
    public void The_forge_half_of_the_loop_runs_on_what_the_run_paid_for_it()
    {
        var (game, player) = Loop(ForgeStart);

        game.State(player).Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(
            0L, "a harness player starts every wallet column at zero, so anything spent below was " +
            "earned by a command.");

        // 10 §7 gates chapter 2 Normal on a chapter 1 Normal clear, and START_RUN enforces it. The
        // chapter stays 2 for the reason ForgeChapter records — this case needs the richer payout.
        Harnesses.HasCleared(game, player, 1, DifficultyTier.NORMAL);

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

        // Counted off what the stock holds NOW rather than off StartingStock: the run itself may
        // have banked gear into it, and the claim here is about what the salvage removed.
        var stockedAfterRun = afterRun.Inventory.Stored.Count;

        Accepted(driver.Send(new SalvageCommand(scrapped)), "SALVAGE", driver);

        var afterSalvage = game.State(player).Player;

        afterSalvage.BalanceOf(CurrencyId.MERGE_DUST).ShouldBeGreaterThan(
            0L, "salvaging four items paid no Merge Dust." + Trace(driver));
        afterSalvage.Inventory.Stored.Count.ShouldBe(
            stockedAfterRun - scrapped.Length, "the salvaged items are gone." + Trace(driver));

        // ⚠️ No stone refund is asserted, and that is the rule rather than a gap — see this case's
        // remarks.

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
            stockedAfterRun - scrapped.Length - FusionInputs + 1,
            "a fusion consumes three items and leaves one." + Trace(driver));

        var fused = afterMerge.Inventory.Find(inputs[0]);

        fused.ShouldNotBeNull(
            "08 §4.1's fusion leaves its output under the FIRST input's identity, and no item is " +
            "there. If a fusion now mints a fresh id, find the output by set difference instead — " +
            "the band assertion below is the point, not the lookup." + Trace(driver));

        ((int)fused!.Rarity).ShouldBeGreaterThan(
            (int)inputBand,
            "the fusion's output is on the same rung it started on. 08 §4.1 fuses ONTO the next band, " +
            "and that decision is LuckService.MergeOutputBand's — an output at the input band means " +
            "the fusion did not go through it." + Trace(driver));

        StockBytes(game, player).ShouldNotBe(
            stockBefore, "the fusion left the stock byte-identical, so nothing was actually fused.");
    }

    // ═════════════════════════════════════════════════════════ the hero

    /// <summary>
    /// 🔒 <b>Clause 4 — "levels the hero".</b> A run that ends in a victory banks lifetime Legend XP
    /// past the first rung, and the reconciliation <c>GameRules</c> runs on every accepted command
    /// turns that into a level, Talent Points and an Energy refill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Measured on this checkout rather than predicted.</b> A victory pays the boss kill and
    /// the completion bonus, and this board banks <b>750</b> lifetime XP against the rung's
    /// <see cref="RungOne"/> — six times over, and comfortably over on every board swept while this
    /// was written (700 the lowest, 1666 the highest). The assertion is a floor at the rung rather
    /// than that number: what the clause needs is that a reachable run crosses it, not that it
    /// crosses it by exactly this much.
    /// </para>
    /// <para>
    /// ⚠️ <b>The level-up is asserted through all three of its effects</b> (steering S2). Lifetime XP
    /// over the rung is what a broken reconciliation would leave standing on its own; the level, the
    /// Talent Point and the <c>legend_level_up</c> Energy row are what say the curve was actually
    /// applied.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_that_wins_levels_the_hero_past_the_first_Legend_rung()
    {
        var (game, player) = Loop();
        var floor = game.State(player).Player.LegendLevel;

        // Both floors are READ rather than assumed to be zero: an assertion against a literal 0 would
        // stop discriminating the day a harness player started holding either.
        var talentFloor = game.State(player).Player.TalentPoints;

        game.State(player).Player.LegendXp.ShouldBe(0L, "a harness player starts with no XP.");

        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
        var hero = game.State(player).Player;

        game.State(player).Run!.BossDefeated.ShouldBeTrue(
            "the premise: only a victory pays enough to cross the rung, so a run that ended any " +
            "other way makes every assertion below a claim about the wrong ending." + Trace(driver));

        hero.LegendXp.ShouldBeGreaterThanOrEqualTo(
            RungOne,
            "the run banked " + hero.LegendXp + " lifetime Legend XP against the first rung's " +
            RungOne + ". A victory pays the boss kill plus the completion bonus, so a payout this " +
            "small means END_RUN did not pay a victory out." + Trace(driver));

        hero.LegendLevel.ShouldBeGreaterThan(
            floor,
            "lifetime XP crossed the first rung and the Legend Level stayed at " + floor + ", so the " +
            "reconciliation on the accepted command never derived the level the curve authorises." +
            Trace(driver));

        hero.TalentPoints.ShouldBeGreaterThan(
            talentFloor,
            "a level was gained and the hero holds the " + talentFloor + " Talent Points they started " +
            "with, so the level-up granted none — the curve grants them on the way up." + Trace(driver));

        game.Events.OfType<CurrencyChanged>().ShouldContain(
            row => row.Reason == LegendLevelUpReason,
            "no Energy row is attributed to the level-up, so the refill the rung owes the player was " +
            "never paid — or was paid unattributed." + Trace(driver));
    }

    // ═════════════════════════════════════════════════════════ the carry

    /// <summary>
    /// 🔒 <b>Clause 5a — there IS a next run, and the loadout crosses into it.</b> Once a run has
    /// ended, <c>START_RUN</c> opens a fresh one: it is the single dispatch row marked
    /// <c>OpensRun</c>, and the phase gate lets that row through an ended run rather than answering
    /// <c>RUN_ALREADY_ENDED</c> to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The acceptance is asserted by identity, not by "it went through" (steering S2).</b> Two
    /// different failures refuse this command and they mean opposite things:
    /// <c>RUN_ALREADY_ENDED</c> is the gate never opening at all, and <c>ILLEGAL_STATE</c> is the
    /// gate opening while the ended run stayed in the working slice, so <c>StartRun.Handle</c>'s
    /// already-active-run guard fired instead. The message prints which one arrived.
    /// </para>
    /// <para>
    /// ⚠️ <b>The byte comparison is vacuous today and that is recorded rather than dressed up.</b>
    /// The loadout is empty on both sides — <c>EQUIP</c> is still a deferred row, so nothing can put
    /// an item in a slot — and an empty loadout has exactly one canonical encoding. It is written in
    /// the shape that will discriminate the day a slot can be filled: canonical bytes per steering
    /// S17, since <c>LoadoutSnapshot</c> holds an <c>IReadOnlyDictionary</c> that record equality
    /// compares by reference. What carries the weight here is the second run existing at all, and
    /// being a genuinely different run from the first.
    /// </para>
    /// <para>
    /// 🔒 <b>The draw counters are the sharpest half of "genuinely different", and this is the one
    /// file where they bite hardest.</b> The first run here actually played — it rolled, fought and
    /// drafted — so its counter map is non-empty, and a second run that came back holding those
    /// counters is the ended run wearing a new phase. The sibling cases seed that map by fixture
    /// (<c>GameRulesRunPhaseGateTests</c>) or cannot produce one at all
    /// (<c>InMemoryGameRunLifecycleTests</c>, whose two-command run draws nothing).
    /// </para>
    /// </remarks>
    [Fact]
    public void A_second_run_starts_after_the_first_ends_and_carries_the_players_loadout()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        game.State(player).Run!.Phase.ShouldBe(RunPhase.Ended, Trace(driver));

        var ended = game.State(player).Run!;
        var firstId = ended.Id;
        var firstSeed = ended.RunSeed;
        var firstDraws = ended.RngStreamPositions;

        // 🔒 The premise under the counter assertion below, asserted rather than narrated: against a
        // first run that had drawn nothing, "the second run has drawn nothing" would be equally true
        // of the first one re-phased, and the sharpest assertion here would be worth nothing.
        firstDraws.ShouldNotBeEmpty(
            "the run that just ended drew from no stream at all, which the loop above makes " +
            "impossible — it rolls, fights and drafts. Something stopped the run travelling." +
            Trace(driver));

        // Read between the two runs, which is the only moment the carry can be compared against.
        var heldBetweenRuns = Canonical(game.State(player).Player.Loadout.ToSnapshot());

        var second = driver.Send(new StartRunCommand(Chapter, DifficultyTier.NORMAL));

        second.Accepted.ShouldBeTrue(
            "START_RUN after an ended run was refused " + second.Rejection + ", so the M4 exit " +
            "criterion's last clause is unreachable again. RUN_ALREADY_ENDED means the phase gate " +
            "refuses every run command on an ended run, START_RUN included — the gate never opened. " +
            "ILLEGAL_STATE means the gate DID let it through but the ended run was never cleared off " +
            "the working slice, so StartRun.Handle's already-active-run guard refused it instead." +
            Trace(driver));

        var opened = game.State(player).Run;

        opened.ShouldNotBeNull("START_RUN was accepted and attached no run." + Trace(driver));

        opened!.Phase.ShouldBe(
            RunPhase.InProgress, "the second run came back unplayable." + Trace(driver));

        opened.Id.ShouldNotBe(
            firstId,
            "the second run carries the FIRST run's identity, so the ended run was re-phased rather " +
            "than replaced." + Trace(driver));

        opened.RunSeed.ShouldNotBe(
            firstSeed,
            "the second run committed the FIRST run's seed, so it would replay the board the player " +
            "has already walked." + Trace(driver));

        opened.RngStreamPositions.ShouldBeEmpty(
            "the second run opened holding the draw counters the first run left behind (" +
            string.Join(", ", opened.RngStreamPositions.Select(row => row.Key + "=" + row.Value)) +
            "), which a run that has drawn nothing cannot have — so the ended run was never cleared " +
            "off the working slice and what came back is it, re-phased." + Trace(driver));

        game.State(player).Player.RunsStarted.ShouldBe(
            2L,
            "two runs were opened and the lifetime counter says otherwise. That counter is what both " +
            "run seeds are derived from, so a second run that did not spend it replays the first " +
            "run's board." + Trace(driver));

        // ⚠️ Vacuous while the loadout is empty — see this case's remarks.
        Canonical(opened.StartingLoadout.ToSnapshot()).ShouldBe(
            heldBetweenRuns,
            "the loadout the second run froze at START_RUN is not the one the player was holding " +
            "between the two runs." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>Clause 5b — the loadout a run carries is the one the player filled.</b> <c>EQUIP</c>
    /// puts an item in a slot, <c>START_RUN</c> freezes what the hero is wearing, and the two are
    /// byte-identical.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The byte comparison only means something over a NON-EMPTY loadout, so filling one is
    /// half the case.</b> An empty loadout has exactly one canonical encoding, so a
    /// <c>START_RUN</c> that froze <c>Loadout.Empty</c> instead of the player's own would leave the
    /// carry provably broken and the comparison green. The <c>EQUIP</c> above is what makes the
    /// comparison below discriminate, which is why the slot is asserted filled before the run starts.
    /// </para>
    /// <para>
    /// Canonical bytes rather than record equality (steering S17): <c>LoadoutSnapshot</c> holds an
    /// <c>IReadOnlyDictionary</c>, which a record's synthesized equality compares by reference.
    /// </para>
    /// <para>
    /// <c>SAVE_PRESET</c> is driven alongside because it reads the live loadout: a preset saved after
    /// the equip has to record the item, which is the other half of "the loadout is the player's own".
    /// </para>
    /// </remarks>
    [Fact]
    public void The_loadout_carried_into_a_run_is_the_one_EQUIP_filled()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Idle(game, player);
        var item = Ids(game, player)[0];

        Accepted(driver.Send(new EquipCommand(item, GearSlot.WEAPON)), "EQUIP", null);
        Accepted(driver.Send(new SavePresetCommand(1, "opener")), "SAVE_PRESET", null);

        game.State(player).Player.Loadout.TryGet(GearSlot.WEAPON, out var worn).ShouldBeTrue(
            "EQUIP was accepted and the weapon slot is still empty, so nothing was actually worn — " +
            "and the comparison below would be back to the vacuous empty-versus-empty one.");
        worn.ShouldBe(item, "the slot names some other item than the one EQUIP was given.");

        var play = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
        var slice = game.State(player);

        slice.Run!.StartingLoadout.Gear.ShouldNotBeEmpty(
            "the run froze an EMPTY loadout over a hero who was wearing something, so it is fighting " +
            "naked and the comparison below is comparing two empties." + Trace(play));

        Canonical(slice.Run!.StartingLoadout.ToSnapshot()).ShouldBe(
            Canonical(slice.Player.Loadout.ToSnapshot()),
            "the loadout the run froze at START_RUN is not the one the player was holding." +
            Trace(play));

        slice.Player.TryGetPreset(1, out var preset).ShouldBeTrue(
            "SAVE_PRESET stored one preset and nothing removed it." + Trace(play));
        preset!.Loadout.TryGet(GearSlot.WEAPON, out var saved).ShouldBeTrue(
            "the preset recorded an empty weapon slot over a hero who was wearing one, so it is not " +
            "reading the live loadout at all." + Trace(play));
        saved.ShouldBe(item, "and the preset names the item the hero was actually wearing." + Trace(play));
    }

    // ═════════════════════════════════════════════════════════ the whole board

    /// <summary>
    /// 🔒 <b>The run crosses both stage boundaries, meets no tile it cannot leave, and beats the
    /// boss.</b> It is the claim every clause above rests on, and it is what makes the victory
    /// ending — and therefore the hero clause — reachable at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This case has now asserted the repair of two defects in turn.</b> It began as X-10's:
    /// <c>StalledAt</c> non-null, a roll accepted from a stage's last node that moved the run
    /// nowhere for ever. It then required <c>StuckOn</c> non-null, because <c>RESOLVE_TILE</c>
    /// acknowledged <c>TILE_SHOP</c> and <c>TILE_DICE_FORGE</c> and left them pending for a command
    /// that did not exist. Both are now clearing commands' work, and the requirements below are
    /// those same claims turned the right way up.
    /// </para>
    /// <para>
    /// ⚠️ <b>Four requirements rather than one, because "the run reached the boss" is satisfied by
    /// several different wrong runs</b> (steering S2): a run that stalls, a run that stops on a tile
    /// it cannot clear, and a run that never leaves stage 1 each fail a different one of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_run_crosses_both_stage_boundaries_and_ends_in_a_victory()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        driver.StalledAt.ShouldBeNull(
            "a ROLL_DICE was accepted, moved the run nowhere and opened no fork. That is X-10 back: " +
            "03 §1.1's stage-end clamp re-firing on a run already standing on the stage's last node." +
            Trace(driver));

        driver.StuckOn.ShouldBeNull(
            "the run met a " + driver.StuckOn + " tile that RESOLVE_TILE accepted and left pending, " +
            "and no second command clears — so it is held there and a pending tile refuses every " +
            "roll." + Trace(driver));

        driver.Stages.ShouldContain(
            2, "the run never resolved a tile in stage 2, so it did not cross the first boundary." +
            Trace(driver));

        driver.Stages.ShouldContain(
            3, "the run never resolved a tile in stage 3, so it did not cross the second boundary." +
            Trace(driver));

        game.State(player).Run!.BossDefeated.ShouldBeTrue(
            "the run travelled the whole board and did not beat the boss." + Trace(driver));

        driver.Ending.ShouldBe(
            VictoryEnding,
            "the run ended some other way than by winning, so the ending the meta half is paid out " +
            "of is not a victory's." + Trace(driver));
    }

    /// <summary>
    /// 🔒 <b>A run crosses exactly two Stage Gates</b> — one out of stage 1 and one out of stage 2.
    /// Stage 3's last node leads to the boss, which belongs to no stage, so it gates nothing.
    /// </summary>
    /// <remarks>
    /// An exact count rather than a floor: the interstitial ad cadence is authored against a run
    /// having two, so a third would be a real balance change and a first-only would be the trigger
    /// having narrowed back to the overshoot clamp.
    /// </remarks>
    [Fact]
    public void A_whole_run_crosses_exactly_two_stage_gates()
    {
        var (game, player) = Loop();
        var driver = MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);

        game.State(player).Run!.BossDefeated.ShouldBeTrue(
            "the premise: a run that stopped short of the boss was never in a position to cross two " +
            "boundaries." + Trace(driver));

        driver.StageGatesCrossed.ShouldBe(
            ExpectedStageGates,
            "the run crossed " + driver.StageGatesCrossed + " Stage Gates. One means the gate still " +
            "only fires on an overshoot clamp; three means stage 3's last node gated on its way to " +
            "the boss." + Trace(driver));
    }

    // ═════════════════════════════════════════════════════════ the budget

    /// <summary>
    /// The whole loop belongs to the unit tier, re-measured on this branch rather than quoted from
    /// an earlier one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured best-of-three on a Debug build of this checkout, warm, with the figure written into
    /// the message so a failure reports what it actually cost.
    /// </para>
    /// <para>
    /// 🔴 <b>The bound is anchored to the measurement, not to `30` §6's command budget, and the
    /// difference matters.</b> Borrowing <c>InMemoryGamePerformanceTests</c>' 200 ms × 10 would leave a
    /// bound far above the real cost, under which a hundredfold regression passes in silence. The
    /// figure is written into the message so a failure reports what it actually cost, and the next
    /// person to move this number owes the same attribution.
    /// </para>
    /// <para>
    /// 🔴 <b>4 ms → 100 ms → 750 ms, and the middle number's own instruction said "do not raise it".
    /// Raising it anyway is a premise change, not an exception.</b> That bound was measured against a
    /// loop in which <c>CONFIRM_BATTLE_RESULT</c> <em>simulated nothing</em>: it shape-checked the
    /// client's <c>LogHash</c> and trusted the reported result. M7-06c made the server recompute every
    /// fight, because <c>14</c> §9 requires it — so the loop now pays for real battles, and the old
    /// bound was measuring a loop that no longer exists.
    /// </para>
    /// <para>
    /// <b>Measured on this branch, and attributed rather than asserted.</b> The loop fights
    /// <b>4 battles</b>; best-of-three is <b>~47 ms</b> run alone and <b>~226 ms</b> under a full-suite
    /// run, i.e. roughly <b>12 ms per battle</b> isolated. That is the same order as the authored fight
    /// cost — <c>05</c>'s budget is under 5 ms and M2-08 measured a 5.4 ms median for worst-case
    /// synthetic fights, 8.2 ms at 1800 ticks — so the increase is the simulation arriving, not a
    /// defect. 750 is ~3× the contended figure: still loose enough for a CI container, still an order
    /// of magnitude below what a content read moved inside the command loop would cost.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this bound no longer catches on its own:</b> a per-command regression is now hidden
    /// behind four battles' worth of simulation. If that becomes the thing to guard, the measurement to
    /// take is milliseconds per COMMAND with the battles subtracted, not a tighter whole-loop number —
    /// <c>driver.BattlesFought</c> is on the driver for exactly that.
    /// </para>
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

            driver.Visited.Distinct().Count().ShouldBeGreaterThan(
                2, "the measurement is only about the loop if the loop ran (steering S3).");
        }

        best.ShouldBeLessThan(
            LoopBudgetMs,
            "the whole meta loop took " + best.ToString("F1", CultureInfo.InvariantCulture) +
            " ms against a bound of " + LoopBudgetMs.ToString("F0", CultureInfo.InvariantCulture) +
            " — about three times the ~226 ms measured under a full-suite run when M7-06c made the " +
            "server recompute every fight. If it fires, FIRST divide by driver.BattlesFought and " +
            "compare against 05's per-fight budget: a rise in cost per battle is a simulation " +
            "regression, and a rise with the per-battle figure flat is a content read moved inside " +
            "the command loop. Do not raise this number without that attribution.");
    }

    // ═════════════════════════════════════════════════════════ fixtures

    private static MetaLoopDriver Play()
    {
        var (game, player) = Loop();

        return MetaLoopDriver.Play(game, player, Chapter, DifficultyTier.NORMAL);
    }

    /// <summary>
    /// A harness over the shipped content, and a player holding a stock the run cannot give them —
    /// <b>wearing six of it</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The hero is equipped, and every run this file drives depends on it.</b> Since
    /// <c>CONFIRM_BATTLE_RESULT</c> recomputes the fight (<c>14</c> §9), the server decides whether a
    /// battle was won — and <c>05</c> §2 is explicit that the base curve is not the whole hero:
    /// <em>"Gear, talents, pets and mounts then multiply these"</em>, with <c>EnemyPowerFormula</c>
    /// scaled against a geared one. A bare-handed hero loses, so the run never opens a draft, never
    /// crosses a stage gate and never reaches the boss — and every claim in this file about the loop
    /// becomes a claim about a run that died on its first fight.
    /// </para>
    /// <para>
    /// 🔒 <b>Equipped through <c>EQUIP</c>, not by handing the harness a loadout.</b>
    /// <c>InMemoryGame.CreatePlayer</c> builds a STARTING player, and starting players own no gear —
    /// so the honest way to a geared hero is the command a real player uses, which this file's own
    /// headnote insists on: there is no call to an aggregate's mutator anywhere in it. It also means
    /// the equip path is exercised on the way to every assertion here rather than only in
    /// <c>EquipTests</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Each result is asserted, because a refused <c>EQUIP</c> is silent.</b> The run would still
    /// start, the hero would still be bare, and every case would fail somewhere far away with a
    /// message about stage gates. The loadout is frozen at <c>START_RUN</c> (<c>07</c> §4), so these
    /// six have to land before the driver sends it.
    /// </para>
    /// </remarks>
    private static (InMemoryGame Game, PlayerId Player) Loop(DateTimeOffset? start = null)
    {
        var game = new InMemoryGame(ShippedHarness.Content, Seed, new VirtualClock(start ?? Start));
        var player = game.CreatePlayer(inventory: WearableStock());

        game.Send(player, new BeginSessionCommand("1.0.0", "content"));

        foreach (var item in RunBattleWorlds.FarAbovePar)
        {
            var equipped = game.Send(player, new EquipCommand(item.InstanceId, item.Slot));

            equipped.Accepted.ShouldBeTrue(
                "EQUIP of the fixture's " + item.Slot + " was refused (" + equipped.Rejection +
                "), so this run's hero fights bare-handed and loses every battle. Nothing below would " +
                "name that as the cause — the failures land on stage gates and drafts instead.");
        }

        return (game, player);
    }

    /// <summary>
    /// The starting stock, plus one item per slot for the hero to wear.
    /// </summary>
    /// <remarks>
    /// The filler twelve are kept at their own count: this file's stock assertions read what the
    /// inventory holds NOW rather than off <see cref="StartingStock"/>, so the worn six are additional
    /// rather than a replacement, and the salvage and fusion cases still have their twelve to work on.
    /// The worn items are <c>RunBattleWorlds</c>' rather than a seventh loadout built here, so every
    /// suite that needs a geared hero composes the same one.
    /// </remarks>
    private static InventorySnapshot WearableStock() =>
        new(
            0,
            Inventories.Fill(StartingStock)
                .Concat(RunBattleWorlds.FarAbovePar)
                .Select(Inventories.Persist)
                .ToArray(),
            []);

    private static IReadOnlyList<GearInstanceId> Ids(InMemoryGame game, PlayerId player) =>
        game.State(player).Player.Inventory.Stored.Select(item => item.InstanceId).ToArray();

    /// <summary>Every identity the player owns, stored or held — a drop at a full stock lands in the latter.</summary>
    private static IReadOnlyList<GearInstanceId> Owned(InMemoryGame game, PlayerId player)
    {
        var inventory = game.State(player).Player.Inventory;

        return inventory.Stored.Concat(inventory.Held).Select(item => item.InstanceId).ToArray();
    }

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
