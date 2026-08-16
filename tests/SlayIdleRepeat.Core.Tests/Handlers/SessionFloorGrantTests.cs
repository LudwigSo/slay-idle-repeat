using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// The session floor at <c>END_RUN</c>: what a qualifying run that produced nothing worth keeping is
/// owed, and the three reasons it is owed nothing.
/// </summary>
/// <remarks>
/// The floor's band, count, daily allowance and qualification rule are all read out of
/// <c>tuning/luck.json#/dropRun/sessionFloor</c> rather than spelled here — a case written against the
/// literal would keep passing after the document moved and the payout did not.
/// </remarks>
public sealed class SessionFloorGrantTests
{
    /// <summary>Any well-formed <c>LogHash</c>.</summary>
    private const string BattleLog = "1";

    /// <summary>The stage a death has to reach for the floor to apply at all.</summary>
    private const int QualifyingStage = 3;

    /// <summary>A stage a death does NOT qualify at.</summary>
    private const int UnqualifyingStage = 1;

    private static SessionFloor Floor => GearGrantWorlds.DropRun.SessionFloor;

    /// <summary>A qualifying run that produced nothing at the floor's band is paid the authored grant.</summary>
    [Fact]
    public void A_qualifying_run_that_produced_nothing_at_the_floors_band_is_paid_the_authored_grant()
    {
        var result = End(GearGrantWorlds.DeadAtStage(NewPlayer(), QualifyingStage, "RUN_PLAYER_TEST_1"));

        var granted = Granted(result);

        granted.Count.ShouldBe(
            Floor.GrantCount,
            "luck.json#/dropRun/sessionFloor grants " + Floor.GrantCount + " item(s) to a run that " +
            "qualified and produced nothing at or above " + Floor.GrantRarity + ", and this run was " +
            "paid " + granted.Count + ". The floor is the promise that a session is never entirely " +
            "wasted; unwired, a player can grind a whole evening for nothing.");

        granted[0].Item.Rarity.ShouldBe(
            Floor.GrantRarity,
            "the floor grants at the band the document authors, not at the chapter's drop table — it " +
            "is a payout, not a draw.");

        granted[0].Source.ShouldBe(
            SourceClass.DROP_RUN, "the floor is the in-run drop class's own protection.");

        granted[0].FromPity.ShouldBeTrue(
            "the floor firing IS a protection firing, and a grant that reported itself as an " +
            "ordinary drop would make the protection unauditable from the event log.");

        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            Floor.GrantCount, "and the item the event reports actually reached the stock.");
    }

    /// <summary>A qualifying run that already produced an item at the floor's band is paid nothing.</summary>
    /// <remarks>
    /// Driven through a real drop rather than a hand-set tally: the run's Elite dry streak is one kill
    /// short of the forced one, so the kill below banks a guaranteed item at or above the floor's
    /// band, and the floor has to notice it.
    /// </remarks>
    [Fact]
    public void A_qualifying_run_that_already_produced_an_item_at_the_floors_band_is_paid_nothing()
    {
        var breaker = GearGrantWorlds.DropRun.EliteMercy;
        var kill = SlayIdleRepeat.Core.GameRules.Apply(
            GearGrantWorlds.OnKill(
                TileKind.Elite,
                pity: GearGrantWorlds.EliteMercyAt(breaker.ForceOnNthKill - 1),
                stage: QualifyingStage),
            new ConfirmBattleResultCommand(BattleLog, Won: true),
            GearGrantWorlds.Context);

        kill.Accepted.ShouldBeTrue("the kill was refused " + kill.Rejection + ".");

        var banked = kill.NewState.Player.Inventory.Stored;

        banked.Count.ShouldBe(1, "the premise: the run produced one item.");
        (banked[0].Rarity >= Floor.GrantRarity).ShouldBeTrue(
            "the premise: the forced Elite drop landed on " + banked[0].Rarity + ", which has to be " +
            "at or above the floor's " + Floor.GrantRarity + " for this case to be about a run that " +
            "already earned its floor.");

        var result = End(GearGrantWorlds.DyingAtStage(kill.NewState, QualifyingStage));

        Granted(result).ShouldBeEmpty(
            "the run already produced an item at or above " + Floor.GrantRarity + " and the floor " +
            "paid anyway. The floor is a floor, not a bonus — paying it on top of a run that met it " +
            "hands every successful run a free extra item.");

        result.NewState.Player.Inventory.Stored.Count.ShouldBe(
            1, "and the stock still holds only what the run itself dropped.");
    }

    /// <summary>Once the day's authored allowance of floor grants is spent, a further qualifying run is paid nothing.</summary>
    /// <remarks>
    /// 🔴 The allowance is <c>maxPerDay</c>, which the shipped document authors above one — so the
    /// case drives exactly that many qualifying runs before the one it is really about, rather than
    /// assuming the second run is the refused one.
    /// </remarks>
    [Fact]
    public void The_days_authored_allowance_of_floor_grants_is_spent_and_the_next_qualifying_run_is_paid_nothing()
    {
        var (player, grants) = SpendTheAllowance();

        grants.ShouldBe(
            Floor.MaxPerDay,
            "the first " + Floor.MaxPerDay + " qualifying runs of the day were paid " + grants +
            " floor grant(s) between them. luck.json authors maxPerDay as " + Floor.MaxPerDay +
            ", so every one of them is inside the allowance and this case has nothing to say until " +
            "they all pay.");

        var beyond = End(GearGrantWorlds.DeadAtStage(player, QualifyingStage, "RUN_PLAYER_TEST_BEYOND"));

        Granted(beyond).ShouldBeEmpty(
            "the day's allowance of " + Floor.MaxPerDay + " floor grants was already spent and " +
            "another run was paid one anyway — so a player who re-runs all evening is handed an " +
            "unbounded stream of guaranteed " + Floor.GrantRarity + " items.");
    }

    /// <summary>A death short of the qualifying stage is paid nothing.</summary>
    /// <remarks>
    /// The same world as the paying case in every respect but the stage the hero died on, so the
    /// qualification rule is the only thing that can account for the difference.
    /// </remarks>
    [Fact]
    public void A_death_short_of_the_qualifying_stage_is_paid_nothing()
    {
        Floor.RequiresVictoryOrStage3Death.ShouldBeTrue(
            "the premise: luck.json still requires a victory or a stage-3 death. Turned off, this " +
            "case is asserting a rule the document no longer states.");

        var result = End(
            GearGrantWorlds.DeadAtStage(NewPlayer(), UnqualifyingStage, "RUN_PLAYER_TEST_1"));

        result.NewState.Run!.Phase.ShouldBe(
            RunPhase.Ended, "the premise: the run ended, so the floor had its chance to fire.");

        Granted(result).ShouldBeEmpty(
            "a stage-" + UnqualifyingStage + " death was paid the session floor. The floor rewards a " +
            "session that was genuinely played out; paid on any death, it turns 'die on the first " +
            "tile and restart' into the fastest way to farm " + Floor.GrantRarity + " gear.");

        result.NewState.Player.Inventory.Stored.ShouldBeEmpty("and nothing reached the stock.");
    }

    // ═══════════════════════════════════════════════════════════ the drives

    /// <summary>The day's whole allowance of qualifying runs, and how many grants they were paid.</summary>
    /// <remarks>
    /// Each run gets an identity of its own, because a floor grant's instance id is minted off the run
    /// that owed it — two runs sharing one identity would collide in the stock rather than reach it.
    /// </remarks>
    private static (Player Player, int Grants) SpendTheAllowance()
    {
        var player = NewPlayer();
        var grants = 0;

        for (var run = 0; run < Floor.MaxPerDay; run++)
        {
            var result = End(GearGrantWorlds.DeadAtStage(
                player, QualifyingStage, "RUN_PLAYER_TEST_" + (run + 1)));

            grants += Granted(result).Count;
            player = result.NewState.Player;
        }

        return (player, grants);
    }

    private static CommandResult End(WorldSlice world)
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new EndRunCommand(), GearGrantWorlds.Context);

        result.Accepted.ShouldBeTrue(
            "END_RUN was refused " + result.Rejection + ", so nothing below is about a run-end " +
            "payout at all.");

        return result;
    }

    private static Player NewPlayer() => Worlds.Rehydrated(PlayerSnapshots.Valid);

    private static IReadOnlyList<GearGranted> Granted(CommandResult result) =>
        result.Events.OfType<GearGranted>().ToArray();
}
