using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// `16` D45: a run is an attrition. HP flows out of one fight, through the campfire's rest and the
/// revive, and into the next fight — every step through the production dispatch table.
/// </summary>
/// <remarks>
/// <para>
/// Every case fights bare-handed at run Max HP 1150 (the Legend-20 base curve: 250 + 45 × 20), so
/// the fights take real damage and the figures below are literal composed values, not ratios. A
/// geared loadout ends these fights at or above the run ceiling, where a carried deficit is
/// invisible.
/// </para>
/// <para>
/// ⚠️ <b>Chapter 2, not chapter 1.</b> Chapter 1's par is authored at 175 for a hero with NO
/// gear at all — it is the chapter a level-1 player enters — so a bare Legend-20 hero leaves every
/// chapter-1 fight at full HP and the attrition these cases are about stops being observable there.
/// Chapter 2 still sits on the default fill's ladder at 2000, which is where a bare Legend-20 hero
/// takes real damage.
/// </para>
/// <para>
/// Each pin carries its own control: the identical slice — same seed, same tile, same streams —
/// with only the run's current HP re-authored. Two different endings from two different openings is
/// the whole claim; a fight that ignored <c>run.CurrentHp</c> makes every pair below collapse to
/// one number.
/// </para>
/// </remarks>
public sealed class RunAttritionTests
{
    /// <summary>The bare Legend-20 hero's Max HP: 250 + 45 × 20.</summary>
    private const int MaxHp = 1150;

    /// <summary>The chapter these fights are in — the type's remarks say why it is not 1.</summary>
    private const int Chapter = 2;

    private static CommandResult Apply(WorldSlice state, GameCommand command) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, command, TileWorlds.Context);

    private static CommandResult Confirm(WorldSlice state, bool won = true) =>
        Apply(state, new ConfirmBattleResultCommand("1", won));

    /// <summary>The same slice with only the run's current HP re-authored — seed, tile and streams untouched.</summary>
    private static WorldSlice WithHp(WorldSlice state, int currentHp)
    {
        var row = state.Run!.ToSnapshot() with { CurrentHp = currentHp };

        return state with { Run = SlayIdleRepeat.Core.Model.Run.Rehydrate(row).Value };
    }

    /// <summary>The confirmed run, stepped onto its next Enemy tile with everything else carried forward.</summary>
    private static WorldSlice SteppedOntoNextFight(WorldSlice state, int linearIndex)
    {
        var row = state.Run!.ToSnapshot() with
        {
            PendingTileKind = (int)TileKind.Enemy,
            PendingTileLinearIndex = linearIndex,
            PendingTileStage = 1,
        };

        return state with { Run = SlayIdleRepeat.Core.Model.Run.Rehydrate(row).Value };
    }

    /// <summary>Resolve the pending fight tile, open the battle, and confirm it.</summary>
    private static CommandResult FightOut(WorldSlice onTile)
    {
        var acknowledged = Apply(onTile, new ResolveTileCommand());
        acknowledged.Accepted.ShouldBeTrue("the fight tile must acknowledge, or nothing below fights");

        var opened = Apply(acknowledged.NewState, new StartBattleCommand());
        opened.Accepted.ShouldBeTrue("the battle must open, or nothing below fights");

        return Confirm(opened.NewState);
    }

    /// <summary>
    /// A won fight's remaining HP is what the next fight opens at: 1150 → 889 → 620 across two
    /// consecutive wins, with the second fight's ending only composable from the first's.
    /// </summary>
    [Fact]
    public void A_won_fights_remaining_hp_opens_the_next_fight()
    {
        var first = Confirm(TileWorlds.OnTile(
            TileKind.Enemy, chapterId: Chapter, currentHp: MaxHp, maxHp: MaxHp,
            phase: RunPhase.BattlePending, geared: false));

        first.Accepted.ShouldBeTrue();
        first.NewState.Run!.CurrentHp.ShouldBe(
            889, "the first fight opens at 1150 and its own ending HP is written back");

        var skipped = Apply(first.NewState, new SkipDraftCommand());
        skipped.Accepted.ShouldBeTrue("the win's draft must clear, or the run can take no next step");

        var stepped = SteppedOntoNextFight(skipped.NewState, linearIndex: 8);

        FightOut(stepped).NewState.Run!.CurrentHp.ShouldBe(
            620, "the second fight opened at 889 — its ending is composed from that deficit");

        FightOut(WithHp(stepped, MaxHp)).NewState.Run!.CurrentHp.ShouldBe(
            881, "the control: the identical second fight opened at full ends 261 higher, so the " +
            "620 above can only have come from the carried 889");
    }

    /// <summary>
    /// The campfire's rest is read by the next fight: 400 + 40% of 1150 = 860, and the fight that
    /// follows ends at a figure only composable from opening at 860.
    /// </summary>
    [Fact]
    public void A_campfire_rest_is_read_by_the_next_fight()
    {
        var rested = Apply(
            TileWorlds.OnTile(
                TileKind.Campfire, chapterId: Chapter, currentHp: 400, maxHp: MaxHp, geared: false),
            new CampfireChooseCommand(0));

        rested.Accepted.ShouldBeTrue();
        rested.NewState.Run!.CurrentHp.ShouldBe(860, "400 + 460, the 40% rest of Max HP 1150");

        FightOut(SteppedOntoNextFight(rested.NewState, linearIndex: 8))
            .NewState.Run!.CurrentHp.ShouldBe(
                591, "the next fight opened at the rested 860");

        FightOut(SteppedOntoNextFight(WithHp(rested.NewState, 400), linearIndex: 8))
            .NewState.Run!.CurrentHp.ShouldBe(
                131, "the control: the same fight without the rest ends 460 lower — the heal is " +
                "read by the fight, not just written to the run");
    }

    /// <summary>
    /// `16` D45: the revive re-enters the SAME battle — same seed — at 66% of Max HP, and that HP
    /// decides the outcome. At linear index 38 the bare hero survives from 1150 (ending at 171) and
    /// dies from 759, so re-entry HP is the only thing separating the two endings.
    /// </summary>
    /// <remarks>
    /// The pre-06e defect this pins dead: a revive whose re-fight opened at full HP would make the
    /// re-entry a guaranteed repeat of whatever full-HP produces — here a WIN at 171, when the
    /// design's 66% re-entry is a loss. One number moving (759 → 1150) flips the outcome.
    /// </remarks>
    [Fact]
    public void A_66_percent_revive_re_enters_the_same_battle_and_that_hp_decides_the_outcome()
    {
        var lost = Confirm(
            TileWorlds.OnTile(
                TileKind.Enemy, chapterId: Chapter, currentHp: 300, maxHp: MaxHp, linearIndex: 38,
                phase: RunPhase.BattlePending, geared: false),
            won: false);

        lost.Accepted.ShouldBeTrue();
        lost.NewState.Run!.CurrentHp.ShouldBe(0, "the recomputed fight from 300 is a genuine loss");
        lost.NewState.Run.HasPendingTile.ShouldBeTrue("the loss leaves the fight pending for REVIVE");

        var revived = Apply(lost.NewState, new ReviveCommand());
        revived.Accepted.ShouldBeTrue();
        revived.NewState.Run!.CurrentHp.ShouldBe(759, "66% of 1150 — D45's figure, not `02` §6's 50% (575)");
        revived.NewState.Run.Phase.ShouldBe(RunPhase.BattlePending);

        Confirm(revived.NewState, won: false).NewState.Run!.CurrentHp.ShouldBe(
            0, "the re-fight opens at the revive's 759 — same seed, and 759 is not enough here");

        Confirm(WithHp(revived.NewState, MaxHp)).NewState.Run!.CurrentHp.ShouldBe(
            171, "the control: the same battle from full HP is a WIN with 171 left, so the revive " +
            "HP is what separated death from survival");
    }
}
