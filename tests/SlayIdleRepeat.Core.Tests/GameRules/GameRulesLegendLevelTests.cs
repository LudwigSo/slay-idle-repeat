using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// The Legend Level reconciliation <c>GameRules.Apply</c> runs on every accepted command: the level
/// a lifetime XP total buys, the Talent Points it grants, and `10` §3.1's Energy refill.
/// </summary>
/// <remarks>
/// Driven through <c>Apply</c> rather than through the rule, because the claim is about the SEAM:
/// the reconciliation is folded into the command loop precisely so that every path that banks Legend
/// XP levels the player up, including the ones no milestone has written yet.
/// </remarks>
public sealed class GameRulesLegendLevelTests
{
    private static readonly LegendCurveTuning Curve =
        LegendCurveTuning.Read(TuningDocuments.Shipped);

    private static readonly LegendTuning Range = LegendTuning.Read(TuningDocuments.Shipped);

    private static readonly EnergyTuning Energy = EnergyTuning.Read(TuningDocuments.Shipped);

    /// <summary>A player whose banked XP has outgrown their level is levelled by the next command.</summary>
    /// <remarks>
    /// A row like this is what a persisted player looks like the moment the XP grant and the level
    /// stop agreeing — a resumed session, a replayed payout, or any grant path a later milestone
    /// adds without knowing the curve exists.
    /// </remarks>
    [Fact]
    public void A_command_reconciles_a_level_the_banked_XP_has_already_bought()
    {
        var result = Apply(PlayerSnapshots.With(legendLevel: 1, legendXp: Needed(4)));

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.LegendLevel.ShouldBe(4);
    }

    [Fact]
    public void The_level_ups_grant_a_Talent_Point_each()
    {
        Apply(PlayerSnapshots.With(legendLevel: 1, legendXp: Needed(4)))
            .NewState.Player.TalentPoints.ShouldBe(3L);
    }

    /// <summary>
    /// 🔒 `10` §3.1 — a Legend Level-up refills Energy to full, and the event says why.
    /// </summary>
    /// <remarks>
    /// The reason token is asserted rather than only the balance: the catch-up publishes its own
    /// <c>CurrencyChanged</c> for <c>ENERGY</c> on the same command, so a test that only counted
    /// energy movements could not tell a refill from a regeneration tick.
    /// </remarks>
    [Fact]
    public void A_level_up_refills_Energy_to_full_and_attributes_it()
    {
        var result = Apply(PlayerSnapshots.With(legendLevel: 1, legendXp: Needed(4)));

        result.NewState.Player.Energy.Energy.ShouldBe(Energy.MaxEnergyAt(4));

        result.Events
            .OfType<CurrencyChanged>()
            .ShouldContain(change => change.Reason == "legend_level_up");
    }

    /// <summary>A player already standing where their XP puts them gains nothing and pays nothing.</summary>
    /// <remarks>
    /// The negative control the whole suite needs: without it, a reconciliation that levelled on
    /// every command would satisfy every case above.
    /// </remarks>
    [Fact]
    public void A_command_that_levels_nobody_publishes_no_level_up()
    {
        var result = Apply(PlayerSnapshots.With(legendLevel: 1, legendXp: 0L));

        result.NewState.Player.LegendLevel.ShouldBe(1);
        result.NewState.Player.TalentPoints.ShouldBe(0L);
        result.Events.OfType<CurrencyChanged>()
            .ShouldNotContain(change => change.Reason == "legend_level_up");
    }

    /// <summary>Applying twice grants once: the reconciliation is idempotent over a lifetime total.</summary>
    [Fact]
    public void A_second_command_over_the_same_total_grants_nothing_more()
    {
        var first = Apply(PlayerSnapshots.With(legendLevel: 1, legendXp: Needed(4)));

        var second = SlayIdleRepeat.Core.GameRules.Apply(
            first.NewState, new SavePresetCommand(1, "Build"), Worlds.Context);

        second.NewState.Player.LegendLevel.ShouldBe(4);
        second.NewState.Player.TalentPoints.ShouldBe(3L, "the same total buys the same level, once.");
    }

    /// <summary>
    /// 🔒 A refused command levels nobody, even when the player's banked XP would have.
    /// </summary>
    /// <remarks>
    /// The reconciliation runs on the working copy, which a rejection discards. Without that, a
    /// player could be handed Talent Points by a command the game refused — and the refusal would
    /// still report that nothing changed.
    /// </remarks>
    [Fact]
    public void A_refused_command_levels_nobody()
    {
        var world = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(legendLevel: 1, legendXp: Needed(4))), null);

        // Slot 0 is below the first: an ILLEGAL_STATE the handler refuses before it writes anything.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            world, new SavePresetCommand(0, "Build"), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.NewState.Player.LegendLevel.ShouldBe(1);
        result.NewState.Player.TalentPoints.ShouldBe(0L);
    }

    /// <summary>
    /// A player one level below the cap, with more XP than any ladder could spend, lands exactly on
    /// the cap and is granted exactly one Talent Point.
    /// </summary>
    /// <remarks>
    /// The boundary that can actually fail. Starting a player AT the cap tests nothing: the walk's
    /// own loop condition and the never-lower floor make "the level stayed at the cap" true by
    /// construction, so only an outright throw could fail it. One level below is where a clamp
    /// written <c>&lt;=</c> instead of <c>&lt;</c> — or a grant that counted the levels the XP would
    /// have bought rather than the levels actually gained — goes red.
    /// </remarks>
    [Fact]
    public void A_player_one_level_below_the_cap_lands_exactly_on_it()
    {
        var result = Apply(PlayerSnapshots.With(
            legendLevel: Range.Maximum - 1, legendXp: long.MaxValue / 2));

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.LegendLevel.ShouldBe(Range.Maximum);
        result.NewState.Player.TalentPoints.ShouldBe(1L, "one level gained is one point, not 199.");
    }

    /// <summary>A player already at the cap gains nothing, however much XP they bank.</summary>
    [Fact]
    public void The_reconciliation_stops_at_the_cap()
    {
        var result = Apply(PlayerSnapshots.With(
            legendLevel: Range.Maximum, legendXp: long.MaxValue / 2));

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.LegendLevel.ShouldBe(Range.Maximum);
        result.NewState.Player.TalentPoints.ShouldBe(0L);
    }

    /// <summary>One accepted meta command over a player row, through <c>Apply</c> and nothing else.</summary>
    private static CommandResult Apply(PlayerSnapshot player) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            new WorldSlice(Worlds.Rehydrated(player), null),
            new SavePresetCommand(1, "Build"),
            Worlds.Context);

    /// <summary>The lifetime total a level costs, as a whole number of banked XP.</summary>
    private static long Needed(int level) =>
        (long)Math.Ceiling(LegendLevelCurve.CumulativeXpTo(level, Curve, Range));
}
