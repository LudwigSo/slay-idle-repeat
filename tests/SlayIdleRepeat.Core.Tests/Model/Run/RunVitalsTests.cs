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

        run.CurrentHp.ShouldBe(100, "a refused write changes nothing — least of all a partial one");
        run.MaxHp.ShouldBe(100);

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
    /// ⚠️ A move to a <b>lower</b> index is accepted, and this case pins the <em>absence</em> of a
    /// monotonicity guard rather than a claim that the board goes backwards.
    /// </summary>
    /// <remarks>
    /// 🔒 The reason is the deferral, not the design of the board: `03` §1 says the opposite in so
    /// many words — <em>"movement is always forward. There is no backtracking"</em> — and §1.1 lists
    /// Portal jumps under <em>forward</em> movement, so a forwards-only rule would refuse nothing the
    /// design authorises today. It is still not written, because which index may follow which is a
    /// property of the board graph and the aggregate holds no graph; a direction rule here would be
    /// the same partial invariant wearing the real one's name that a range check would be (`30`
    /// §11.5's <em>"a run's position is a valid node"</em>, registered against M3-01). Movement
    /// legality is M3-01's and M3-02's. If a later milestone moves that rule into the aggregate, this
    /// case turns red and points at the register rather than at a guard nobody remembers omitting.
    /// </remarks>
    [Fact]
    public void MoveTo_accepts_a_lower_index_because_movement_legality_is_M3_01s_not_the_aggregates()
    {
        var run = Fresh(position: 19);

        run.MoveTo(4);

        run.Position.ShouldBe(4);
    }

    /// <summary>
    /// 🔒 `03` §1.1 — the <b>trailhead</b> at −1 is accepted: it is where every run stands before its
    /// first roll, not an invalid position.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the case a floor of 0 would have got wrong. §1.1 (ruled in `16` A7) puts the hero
    /// at <em>"a virtual trailhead one step before node 0 (position −1)"</em> and makes the movement
    /// arithmetic close from there — <em>"a first roll of <c>1</c> therefore lands on node 0"</em> —
    /// so a run created by <c>START_RUN</c> (M3-15) and abandoned before its first <c>ROLL_DICE</c>
    /// persists at −1. That is exactly the state `14` §16.3's sliding 48-hour TTL exists to keep
    /// alive, so refusing it would make the commonest resumable run unstorable.
    /// </remarks>
    [Fact]
    public void MoveTo_accepts_the_trailhead_because_that_is_where_every_run_starts()
    {
        var run = Fresh(position: 7);

        run.MoveTo(-1);

        run.Position.ShouldBe(-1);
    }

    /// <summary>A position below the trailhead is refused — and that is the whole check.</summary>
    [Fact]
    public void MoveTo_refuses_a_position_below_the_trailhead()
    {
        var run = Fresh(position: 7);

        var act = () => run.MoveTo(-2);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .ParamName.ShouldBe(
                  "position",
                  "naming the parameter is what says WHICH refusal this is (steering S2) — the " +
                  "exception type alone is the same one SetHitPoints and MarkApplied raise.");

        run.Position.ShouldBe(7, "a refused write changes nothing");
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
    /// off it would stay alive because its owner opened the shop. ⚠️ It is a <b>state</b> assertion
    /// over two live aggregates, not a type-shape one: what it catches is the two anchors being
    /// backed by one store — a static, a shared clock field, a <c>Run</c> that delegated to its
    /// parent — which is the only way in <c>Core</c> for advancing one to advance the other.
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
