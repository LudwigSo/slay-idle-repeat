using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §11.5 / `14` §2.3 / `14` §16.3 — the run's three plain pieces of state and the mutators
/// that write them: the hero's hit points, the board position, and the instant the run last accepted
/// a command.
/// </summary>
public sealed class RunVitalsTests
{
    private static Run Fresh(int position = 0, int currentHp = 100, int maxHp = 100) =>
        Run.Rehydrate(RunSnapshots.With(position: position, currentHp: currentHp, maxHp: maxHp)).Value;

    // ------------------------------------------------------------------ hit points

    /// <summary>
    /// 🔒 <c>SetHitPoints</c> writes <b>both</b> halves in one call, which is the invariant rather
    /// than an ergonomic choice.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <c>Player.AccrueEnergy</c> taking both halves of one accrual: a caller
    /// that raised the maximum and forgot the current — or the reverse — would leave the pair in a
    /// state neither individual write is illegal in, so no aggregate-level invariant could catch it
    /// afterwards. `03` §7a.5's <c>SHR_HP</c> shrine raises the maximum <em>and</em> heals, which is
    /// exactly one fact with two components.
    /// </remarks>
    [Fact]
    public void SetHitPoints_writes_the_current_and_the_maximum_together()
    {
        var run = Fresh(currentHp: 61, maxHp: 100);

        run.SetHitPoints(140, 140);

        run.CurrentHp.ShouldBe(140);
        run.MaxHp.ShouldBe(140);
    }

    /// <summary>Damage is the same seam with a lower current and an unchanged maximum.</summary>
    [Fact]
    public void SetHitPoints_records_damage_without_touching_the_maximum()
    {
        var run = Fresh(currentHp: 100, maxHp: 100);

        run.SetHitPoints(37, 100);

        run.CurrentHp.ShouldBe(37);
        run.MaxHp.ShouldBe(100);
    }

    /// <summary>A maximum below 1 is refused: a hero with no hit points at all is not a run state.</summary>
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

    /// <summary>A negative current is refused; zero is not — a downed hero awaiting a revive is at 0.</summary>
    [Fact]
    public void SetHitPoints_refuses_a_negative_current_and_accepts_zero()
    {
        var run = Fresh();

        Should.Throw<ArgumentOutOfRangeException>(() => run.SetHitPoints(-1, 100))
              .Message.ShouldMatchWildcard("*negative*");

        run.SetHitPoints(0, 100);

        run.CurrentHp.ShouldBe(0, "02 §6's revive acts on a hero at zero, so zero is a state the run has");
    }

    /// <summary>
    /// 🔒 Overheal is refused, not clamped. `30` §11.5 keeps the arithmetic in the rule that computes
    /// it; a silent clamp here would make a healing rule that over-delivered look correct.
    /// </summary>
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

    /// <summary>Full health is legal: the boundary is not off by one.</summary>
    [Fact]
    public void SetHitPoints_accepts_a_current_equal_to_the_maximum()
    {
        var run = Fresh(currentHp: 12, maxHp: 100);

        run.SetHitPoints(100, 100);

        run.CurrentHp.ShouldBe(100);
    }

    // -------------------------------------------------------------------- position

    /// <summary>`14` §2.3 — <c>MoveTo</c> records the new linear node index.</summary>
    [Fact]
    public void MoveTo_records_the_new_position()
    {
        var run = Fresh(position: 12);

        run.MoveTo(19);

        run.Position.ShouldBe(19);
    }

    /// <summary>
    /// ⚠️ A move <b>backwards</b> is legal, and this case is why <c>MoveTo</c> has no monotonicity
    /// guard: `03` §1.1's Portal jumps and the board's back-edges move a run in both directions, so a
    /// forwards-only rule would refuse legal play.
    /// </summary>
    [Fact]
    public void MoveTo_accepts_a_move_backwards_because_the_board_has_portals()
    {
        var run = Fresh(position: 19);

        run.MoveTo(4);

        run.Position.ShouldBe(4);
    }

    /// <summary>A negative position is refused — and that is the whole check.</summary>
    [Fact]
    public void MoveTo_refuses_a_negative_position()
    {
        var run = Fresh(position: 7);

        var act = () => run.MoveTo(-1);

        Should.Throw<ArgumentOutOfRangeException>(act);

        run.Position.ShouldBe(7);
    }

    /// <summary>
    /// 🔒 ⚠️ …and a position no board could contain is <b>accepted</b>. `30` §11.5 names <em>"a run's
    /// position is a valid node"</em> as an invariant of this aggregate, and it is <b>deferred</b>,
    /// not approximated.
    /// </summary>
    /// <remarks>
    /// There is no board and no node identity until M3-01, so a range check invented here would be a
    /// partial invariant wearing the real one's name and would be trusted as such by everything
    /// downstream. The real validation is registered as the <c>Board</c> entry in
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>, keyed on <c>NodeId</c>, which fails the
    /// build on the day node identity arrives. This case is the domain-side statement of the same
    /// deferral, so a later invented bound turns red here and points at the register.
    /// </remarks>
    [Fact]
    public void MoveTo_accepts_a_position_no_board_could_contain_because_node_identity_is_M3_01s()
    {
        var run = Fresh();

        run.MoveTo(int.MaxValue);

        run.Position.ShouldBe(int.MaxValue);
    }

    // ------------------------------------------------------------------- MarkApplied

    /// <summary>`14` §16.3 — the sliding TTL's anchor advances to the instant the command was applied.</summary>
    [Fact]
    public void MarkApplied_advances_the_run_TTL_anchor()
    {
        var run = Fresh();
        var later = RunSnapshots.Midmorning.AddMinutes(11);

        run.MarkApplied(later);

        run.LastAppliedAtUtc.ShouldBe(later);
    }

    /// <summary>
    /// ⚠️ Equal is allowed: the server stamps <c>NowUtc</c> once per command and a client can send
    /// two inside the same millisecond.
    /// </summary>
    [Fact]
    public void MarkApplied_allows_the_same_instant_twice()
    {
        var run = Fresh();

        run.MarkApplied(RunSnapshots.Midmorning);

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary>
    /// 🔒 Strictly-earlier is refused. `14` §16.3 measures the 48-hour run TTL <b>from</b> this
    /// instant, so moving it backwards would extend a run past the point it expires.
    /// </summary>
    [Fact]
    public void MarkApplied_refuses_an_instant_that_goes_backwards()
    {
        var run = Fresh();

        var act = () => run.MarkApplied(RunSnapshots.Midmorning.AddSeconds(-1));

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*16.3*");

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary>
    /// A non-zero offset is refused: <c>CanonicalStateWriter</c> encodes a <see cref="DateTimeOffset"/>
    /// as Unix milliseconds, so two offsets naming one instant hash identically while record equality
    /// calls them different.
    /// </summary>
    [Fact]
    public void MarkApplied_refuses_an_instant_carrying_a_non_zero_offset()
    {
        var run = Fresh();
        var offset = new DateTimeOffset(2026, 8, 12, 13, 0, 0, TimeSpan.FromHours(2));

        Should.Throw<ArgumentOutOfRangeException>(() => run.MarkApplied(offset))
              .Message.ShouldMatchWildcard("*offset*");

        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning);
    }

    /// <summary>
    /// 🔒 The run's anchor is its <b>own</b>: it is not <c>Player.LastAppliedAtUtc</c> under another
    /// name, and it must not be.
    /// </summary>
    /// <remarks>
    /// `14` §16.3 makes the run TTL sliding and measured from the last command accepted <em>by the
    /// run</em>. The player's anchor advances on meta commands too, so a run whose expiry were slid
    /// off it would stay alive because its owner opened the shop. Stated as a type-shape assertion
    /// because there is nothing else in <c>Core</c> that could notice the two being merged.
    /// </remarks>
    [Fact]
    public void The_runs_TTL_anchor_is_the_runs_own_and_not_the_players()
    {
        var run = Fresh();
        var player = Core.Model.Player
            .Rehydrate(PlayerSnapshots.Valid, ProgressionDocuments.Shipped).Value;

        run.MarkApplied(RunSnapshots.Midmorning.AddHours(2));

        player.LastAppliedAtUtc.ShouldBe(
            PlayerSnapshots.Midmorning,
            "advancing a run's anchor must not touch the player's — they are two fields answering " +
            "two different expiry questions (14 §16.3).");
        run.LastAppliedAtUtc.ShouldBe(RunSnapshots.Midmorning.AddHours(2));
    }
}
