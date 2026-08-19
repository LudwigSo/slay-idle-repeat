using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <c>ftueProgress</c> on the <c>Player</c> aggregate. Tested on the aggregate because no shipped
/// command advances the tutorial yet — <c>Apply</c> cannot reach these mutators.
/// </summary>
public sealed class PlayerFtueTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player At(FtueBeat beat, DateTimeOffset? completed = null) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(ftueBeatId: beat, ftueCompletedAtUtc: completed), Content)
            .Value;

    /// <summary>The enum has no zero member, so an uninitialised column cannot read as a beat.</summary>
    [Fact]
    public void A_default_FtueBeatId_is_refused_at_the_seam()
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(ftueBeatId: default(FtueBeat)), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("FtueBeatId is 0", Case.Sensitive);
    }

    /// <summary>
    /// The wire numbers are 1..12 in script order — <c>CanonicalStateWriter</c> writes the number,
    /// so renumbering rewrites every <c>stateHash</c> that has ever carried a player.
    /// </summary>
    [Fact]
    public void The_beat_wire_numbers_are_pinned_in_script_order()
    {
        Enum.GetValues<FtueBeat>().Select(beat => (int)beat)
            .ShouldBe(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

        // B6b sits between B6 and B7 in the script.
        ((int)FtueBeat.B6B).ShouldBe((int)FtueBeat.B6 + 1);
        ((int)FtueBeat.B7).ShouldBe((int)FtueBeat.B6B + 1);
    }

    [Fact]
    public void A_beat_advances_forwards()
    {
        var player = At(FtueBeat.B0);

        player.AdvanceFtue(FtueBeat.B1);
        player.AdvanceFtue(FtueBeat.B2);

        player.FtueBeat.ShouldBe(FtueBeat.B2);
        player.IsFtueComplete.ShouldBeFalse();
    }

    /// <summary>A skip jumps straight to beat 9, so the advance may skip beats — it just may not go backwards.</summary>
    [Fact]
    public void A_skip_may_jump_several_beats_forwards()
    {
        var player = At(FtueBeat.B2);

        player.AdvanceFtue(FtueBeat.B9);

        player.FtueBeat.ShouldBe(FtueBeat.B9);
    }

    /// <summary>A beat that does not move forwards is a replayed command — the resume table only re-presents the current beat.</summary>
    [Theory]
    [InlineData(FtueBeat.B5)]
    [InlineData(FtueBeat.B1)]
    public void A_beat_that_does_not_move_forwards_is_refused(FtueBeat beat)
    {
        var player = At(FtueBeat.B5);

        Should.Throw<ArgumentOutOfRangeException>(() => player.AdvanceFtue(beat))
              .Message.ShouldMatchWildcard("*is not later*replayed command*");

        player.FtueBeat.ShouldBe(FtueBeat.B5);
    }

    [Fact]
    public void An_undefined_beat_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => At(FtueBeat.B0).AdvanceFtue((FtueBeat)99))
              .Message.ShouldMatchWildcard("*B0..B10 plus B6b*");
    }

    [Fact]
    public void The_tutorial_completes_at_beat_ten()
    {
        var player = At(FtueBeat.B10);

        player.CompleteFtue(PlayerSnapshots.Midmorning);

        player.IsFtueComplete.ShouldBeTrue();
        player.FtueCompletedAtUtc.ShouldBe(PlayerSnapshots.Midmorning);
    }

    /// <summary>Completing early would skip the forced Talent Point spend beat 10 exists for.</summary>
    [Theory]
    [InlineData(FtueBeat.B0)]
    [InlineData(FtueBeat.B9)]
    public void Completing_before_beat_ten_is_refused(FtueBeat beat)
    {
        var player = At(beat);

        Should.Throw<InvalidOperationException>(() => player.CompleteFtue(PlayerSnapshots.Midmorning))
              .Message.ShouldMatchWildcard("*not B10*forced Talent Point spend*");

        player.IsFtueComplete.ShouldBeFalse();
    }

    /// <summary>The payout grants once; a second completion would grant it twice.</summary>
    [Fact]
    public void Completing_twice_is_refused()
    {
        var player = At(FtueBeat.B10, PlayerSnapshots.Midmorning);

        Should.Throw<InvalidOperationException>(
                  () => player.CompleteFtue(PlayerSnapshots.Midmorning.AddDays(1)))
              .Message.ShouldMatchWildcard("*already completed*granted once*");
    }

    [Fact]
    public void No_beat_advances_after_completion()
    {
        var player = At(FtueBeat.B10, PlayerSnapshots.Midmorning);

        Should.Throw<InvalidOperationException>(() => player.AdvanceFtue(FtueBeat.B10))
              .Message.ShouldMatchWildcard("*no FTUE surface ever appears again*");
    }

    [Fact]
    public void The_completion_instant_must_be_UTC()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => At(FtueBeat.B10).CompleteFtue(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.FromHours(2))))
              .Message.ShouldMatchWildcard("*Unix milliseconds*");
    }

    /// <summary>A completed tutorial at any beat but B10 claims a payout banked at a beat that never reached it.</summary>
    [Theory]
    [InlineData(FtueBeat.B0)]
    [InlineData(FtueBeat.B8)]
    [InlineData(FtueBeat.B9)]
    public void A_completed_tutorial_at_any_beat_but_B10_is_refused(FtueBeat beat)
    {
        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(ftueBeatId: beat, ftueCompletedAtUtc: PlayerSnapshots.Midmorning),
            Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("FtueCompletedAtUtc is set while", Case.Sensitive);
    }

    /// <summary>The rule is "completed implies B10", not "B10 implies completed".</summary>
    [Fact]
    public void Beat_ten_loads_both_before_and_after_the_spend_commits()
    {
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(ftueBeatId: FtueBeat.B10), Content)
            .Value.IsFtueComplete.ShouldBeFalse();

        Core.Model.Player
            .Rehydrate(
                PlayerSnapshots.With(ftueBeatId: FtueBeat.B10, ftueCompletedAtUtc: PlayerSnapshots.Midmorning),
                Content)
            .Value.IsFtueComplete.ShouldBeTrue();
    }
}
