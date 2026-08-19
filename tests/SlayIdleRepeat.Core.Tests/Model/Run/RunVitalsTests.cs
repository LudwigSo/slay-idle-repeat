using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The guards on the run's hit points, board position and TTL anchor. The legal movements are
/// covered at the <c>Apply</c> seam (<c>RollDiceTests</c>, <c>ConfirmBattleResultTests</c>); these
/// inputs are ones no handler produces, so <c>Apply</c> cannot express them.
/// </summary>
public sealed class RunVitalsTests
{
    private static Run Fresh(int position = 0, int currentHp = 100, int maxHp = 100) =>
        Run.Rehydrate(RunSnapshots.With(position: position, currentHp: currentHp, maxHp: maxHp)).Value;

    /// <summary>A hero with no hit points at all is not a run state.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetHitPoints_refuses_a_maximum_below_one(int max)
    {
        var run = Fresh();

        var act = () => run.SetHitPoints(0, max);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*maximum*at least 1*");

        run.MaxHp.ShouldBe(100, "a refused write changes nothing");
        run.CurrentHp.ShouldBe(100);
    }

    [Fact]
    public void SetHitPoints_refuses_a_negative_current_and_accepts_zero()
    {
        var run = Fresh();

        Should.Throw<ArgumentOutOfRangeException>(() => run.SetHitPoints(-1, 100))
              .Message.ShouldMatchWildcard("*negative*");

        run.CurrentHp.ShouldBe(100, "a refused write changes nothing — least of all a partial one");
        run.MaxHp.ShouldBe(100);

        run.SetHitPoints(0, 100);

        run.CurrentHp.ShouldBe(0, "02 §6's revive acts on a hero at zero, so zero is a state the run has");
    }

    /// <summary>Refused, not clamped: a silent clamp would make a healing rule that over-delivered look correct.</summary>
    [Fact]
    public void SetHitPoints_refuses_a_current_above_the_maximum_rather_than_clamping()
    {
        var run = Fresh(currentHp: 50, maxHp: 100);

        var act = () => run.SetHitPoints(101, 100);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*101*exceeds*100*");

        run.CurrentHp.ShouldBe(50, "a refused write changes nothing — least of all a partial one");
        run.MaxHp.ShouldBe(100);
    }

    [Fact]
    public void SetHitPoints_accepts_a_current_equal_to_the_maximum()
    {
        var run = Fresh(currentHp: 12, maxHp: 100);

        run.SetHitPoints(100, 100);

        run.CurrentHp.ShouldBe(100);
    }

    /// <summary>
    /// Pins the ABSENCE of a monotonicity guard: which index may follow which is the board
    /// graph's question, and the aggregate holds no graph.
    /// </summary>
    [Fact]
    public void MoveTo_accepts_a_lower_index_because_movement_legality_is_M3_01s_not_the_aggregates()
    {
        var run = Fresh(position: 19);

        run.MoveTo(4);

        run.Position.ShouldBe(4);
    }

    /// <summary>The case a floor of 0 would get wrong: a run abandoned before its first roll persists at −1.</summary>
    [Fact]
    public void MoveTo_accepts_the_trailhead_because_that_is_where_every_run_starts()
    {
        var run = Fresh(position: 7);

        run.MoveTo(-1);

        run.Position.ShouldBe(-1);
    }

    [Fact]
    public void MoveTo_refuses_a_position_below_the_trailhead()
    {
        var run = Fresh(position: 7);

        var act = () => run.MoveTo(-2);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .ParamName.ShouldBe(
                  "position",
                  "naming the parameter is what says WHICH refusal this is — the exception type " +
                  "alone is the same one SetHitPoints and MarkApplied raise.");

        run.Position.ShouldBe(7, "a refused write changes nothing");
    }

    /// <summary>"A run's position is a valid node" is deferred to the board rules, not approximated here.</summary>
    [Fact]
    public void MoveTo_accepts_a_position_no_board_could_contain_because_node_identity_is_M3_01s()
    {
        var run = Fresh();

        run.MoveTo(int.MaxValue);

        run.Position.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void MarkApplied_advances_the_run_TTL_anchor()
    {
        var run = Fresh();
        var later = RunSnapshots.Midmorning.AddMinutes(11);

        run.MarkApplied(later);

        run.LastAppliedAtUtc.ShouldBe(later);
    }

    /// <summary>Equal is allowed: a client can send two commands inside the same millisecond.</summary>
    [Fact]
    public void MarkApplied_allows_the_same_instant_twice()
    {
        var run = Fresh();

        run.MarkApplied(RunSnapshots.Midmorning);

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary>The run TTL is measured FROM this instant, so moving it backwards would extend a run past its expiry.</summary>
    [Fact]
    public void MarkApplied_refuses_an_instant_that_goes_backwards()
    {
        var run = Fresh();

        var act = () => run.MarkApplied(RunSnapshots.Midmorning.AddSeconds(-1));

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*16.3*");

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary><c>CanonicalStateWriter</c> encodes an instant as Unix milliseconds, so two offsets naming one instant would hash identically.</summary>
    [Fact]
    public void MarkApplied_refuses_an_instant_carrying_a_non_zero_offset()
    {
        var run = Fresh();
        var offset = new DateTimeOffset(2026, 8, 12, 13, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentOutOfRangeException>(() => run.MarkApplied(offset))
              .Message.ShouldMatchWildcard("*offset*");

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }
}
