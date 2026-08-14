using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `19` D7 — the <c>Player</c> aggregate carries <c>ftueProgress { completedAtUtc | null, beatId }</c>,
/// advanced server-side as each beat's interaction completes.
/// </summary>
/// <remarks>
/// ⚠️ The seven tutorial-only rule flags of `19` D4.3 are deliberately <b>not</b> on this aggregate:
/// they describe the tutorial, not the player, and are M4-12's <c>ftue.json</c> content package.
/// Putting them in SchemaVersion 1's pinned field list would make M4-12's first real decision about
/// them a serialisation change.
/// </remarks>
public sealed class PlayerFtueTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static Core.Model.Player At(FtueBeat beat, DateTimeOffset? completed = null) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(ftueBeatId: beat, ftueCompletedAtUtc: completed), Content)
            .Value;

    /// <summary>🔒 `19` D7's beat vocabulary is exactly B0…B10 plus B6b — twelve values, in script order.</summary>
    [Fact]
    public void The_beat_vocabulary_is_the_twelve_beats_19_D7_names()
    {
        Enum.GetNames<FtueBeat>().ShouldBe(new[]
        {
            "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B6B", "B7", "B8", "B9", "B10",
        });
    }

    /// <summary>
    /// 🔒 The enum has no zero member, so an uninitialised column cannot read as "at beat 0, about
    /// to enter their name" — the same rule <see cref="CurrencyId"/> follows.
    /// </summary>
    [Fact]
    public void The_beat_vocabulary_has_no_zero_member()
    {
        Enum.GetValues<FtueBeat>().ShouldNotContain((FtueBeat)0);

        var result = Core.Model.Player.Rehydrate(
            PlayerSnapshots.With(ftueBeatId: default(FtueBeat)), Content);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("FtueBeatId is 0", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The wire numbers are 1..12 in script order. <c>CanonicalStateWriter</c> writes the
    /// number, never the name, so renumbering rewrites every <c>stateHash</c> that has ever carried
    /// a player.
    /// </summary>
    [Fact]
    public void The_beat_wire_numbers_are_pinned_in_script_order()
    {
        Enum.GetValues<FtueBeat>().Select(beat => (int)beat)
            .ShouldBe(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

        // B6b sits between B6 and B7, which is where 19 D3's script puts it.
        ((int)FtueBeat.B6B).ShouldBe((int)FtueBeat.B6 + 1);
        ((int)FtueBeat.B7).ShouldBe((int)FtueBeat.B6B + 1);
    }

    /// <summary>A beat advances forwards, one interaction at a time.</summary>
    [Fact]
    public void A_beat_advances_forwards()
    {
        var player = At(FtueBeat.B0);

        player.AdvanceFtue(FtueBeat.B1);
        player.AdvanceFtue(FtueBeat.B2);

        player.FtueBeat.ShouldBe(FtueBeat.B2);
        player.IsFtueComplete.ShouldBeFalse();
    }

    /// <summary>
    /// `19` D6 — a skip jumps straight to beat 9, so the advance may skip beats. It just may not
    /// go backwards.
    /// </summary>
    [Fact]
    public void A_skip_may_jump_several_beats_forwards()
    {
        var player = At(FtueBeat.B2);

        player.AdvanceFtue(FtueBeat.B9);

        player.FtueBeat.ShouldBe(FtueBeat.B9);
    }

    /// <summary>
    /// A beat that does not move forwards is a replayed command — `19` D7's resume table only ever
    /// re-presents the current beat.
    /// </summary>
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

    /// <summary>
    /// 🔒 `19` D7 — completion is beat 10's spend committing: <c>completedAtUtc</c> is set, and
    /// from then on the tutorial is over.
    /// </summary>
    [Fact]
    public void The_tutorial_completes_at_beat_ten()
    {
        var player = At(FtueBeat.B10);

        player.CompleteFtue(PlayerSnapshots.Midmorning);

        player.IsFtueComplete.ShouldBeTrue();
        player.FtueCompletedAtUtc.ShouldBe(PlayerSnapshots.Midmorning);
    }

    /// <summary>
    /// Completing before beat 10 is refused: it would skip the forced Talent Point spend that beat
    /// 10 exists for (`19` D7, `19` D5's level-up lock).
    /// </summary>
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

    /// <summary>
    /// Completing twice is refused. `19` D7 grants the D5 payout once, keyed on the run; a second
    /// completion would grant it twice.
    /// </summary>
    [Fact]
    public void Completing_twice_is_refused()
    {
        var player = At(FtueBeat.B10, PlayerSnapshots.Midmorning);

        Should.Throw<InvalidOperationException>(
                  () => player.CompleteFtue(PlayerSnapshots.Midmorning.AddDays(1)))
              .Message.ShouldMatchWildcard("*already completed*granted once*");
    }

    /// <summary>
    /// `19` D6 — <em>"never re-offered after completion"</em>: no beat moves once the tutorial is
    /// done.
    /// </summary>
    [Fact]
    public void No_beat_advances_after_completion()
    {
        var player = At(FtueBeat.B10, PlayerSnapshots.Midmorning);

        Should.Throw<InvalidOperationException>(() => player.AdvanceFtue(FtueBeat.B10))
              .Message.ShouldMatchWildcard("*no FTUE surface ever appears again*");
    }

    /// <summary>The completion instant must be UTC, like every other instant on the aggregate.</summary>
    [Fact]
    public void The_completion_instant_must_be_UTC()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                  () => At(FtueBeat.B10).CompleteFtue(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.FromHours(2))))
              .Message.ShouldMatchWildcard("*Unix milliseconds*");
    }

    /// <summary>
    /// 🔒 A persisted row claiming a completed tutorial at any beat but B10 is refused: it claims a
    /// payout was banked at a beat that never reached it.
    /// </summary>
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

    /// <summary>
    /// …and both legitimate states at beat 10 load: reached but not yet committed, and completed.
    /// The rule is "completed implies B10", not "B10 implies completed".
    /// </summary>
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
