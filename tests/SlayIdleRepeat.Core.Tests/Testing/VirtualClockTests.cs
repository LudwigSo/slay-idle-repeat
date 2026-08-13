using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §6 — <c>VirtualClock</c>: <em>"advanced explicitly. Nothing ever waits."</em>
/// </summary>
public sealed class VirtualClockTests
{
    /// <summary>A clock reads exactly the instant it was started at, and nothing else.</summary>
    [Fact]
    public void A_clock_reads_the_instant_it_was_started_at()
    {
        new VirtualClock(Harnesses.Start).NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>
    /// 🔒 An advance moves the clock by <b>exactly</b> the span asked for, and advances accumulate.
    /// </summary>
    /// <remarks>
    /// The second half is the one worth having: a clock that <em>set</em> rather than added would
    /// pass the first assertion and fail this one, and "energy regenerates by RULE" rests on the
    /// clock's arithmetic being ordinary addition.
    /// </remarks>
    [Fact]
    public void Advancing_moves_the_clock_by_exactly_the_span_and_advances_accumulate()
    {
        var clock = new VirtualClock(Harnesses.Start);

        clock.Advance(TimeSpan.FromHours(8));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(8));

        clock.Advance(TimeSpan.FromMinutes(90));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30));

        clock.Advance(TimeSpan.FromTicks(1));
        clock.NowUtc.ShouldBe(Harnesses.Start.AddHours(9).AddMinutes(30).AddTicks(1));
    }

    /// <summary>
    /// A zero advance is a legal no-op — a loop that computes its own step must not have to branch.
    /// </summary>
    [Fact]
    public void A_zero_advance_is_a_no_op()
    {
        var clock = new VirtualClock(Harnesses.Start);

        clock.Advance(TimeSpan.Zero);

        clock.NowUtc.ShouldBe(Harnesses.Start);
    }

    /// <summary>
    /// 🔒 The clock only goes forwards, and the refusal names the reason it still does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message fragment is pinned rather than only the exception type (steering <b>S2</b>).
    /// <see cref="ArgumentOutOfRangeException"/> is what several other guards on this type throw —
    /// a non-zero offset, a start before the first game day, an overflow — so a test that asserted
    /// only the type would pass while the wrong guard fired, and each of those four has a different
    /// fix.
    /// </para>
    /// <para>
    /// 🔒 <b>M1-12 is the milestone this remark was written for, and this is that deliberate
    /// edit.</b> The paragraph used to read: <em>"It pins the ruling's identity too. If a later
    /// milestone settles the disagreement between <c>Player.MarkApplied</c>'s throw and
    /// <c>GameRules.AdvanceTime</c>'s clamp and decides a harness may rewind, this test is what has
    /// to be deleted deliberately — with that ruling in its commit message — rather than quietly
    /// relaxed."</em> Carried-forward item 20 is settled, on `30` §2.1 <b>P3</b>'s side:
    /// <c>GameRules.MarkApplied</c> floors the instant it hands both aggregates, so a backwards
    /// clock returns a result. The old fragment (<c>"carried-forward item"</c>) is therefore gone
    /// from the message and from this assertion.
    /// </para>
    /// <para>
    /// ⚠️ <b>The rule itself did not change and the test is not relaxed</b> — only the reason it
    /// gives. Forward-only stands on `30` §6 writing the harness as <c>Advance(...)</c> rather than
    /// a setter: skew is a thing composition roots produce, not a thing a harness manufactures. So
    /// the second fragment now pins <c>Rehydrate</c> — the `30` §11.3 path a test must use to build
    /// skewed state instead — which is what a caller who hit this guard actually needs to be told.
    /// <c>GameRulesBackwardsClockTests</c> is where that is driven.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_negative_advance_is_refused_and_names_the_ruling_it_declines_to_make()
    {
        var clock = new VirtualClock(Harnesses.Start);

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => clock.Advance(TimeSpan.FromTicks(-1)));

        refusal.ParamName.ShouldBe("by");
        refusal.Message.ShouldContain("only moves FORWARDS", Case.Sensitive);
        refusal.Message.ShouldContain(
            "Rehydrate",
            Case.Sensitive,
            "the refusal must tell the caller what to do INSTEAD — build skewed state through " +
            "30 §11.3's Rehydrate — not merely that it will not rewind. This fragment replaced " +
            "'carried-forward item' when M1-12 settled item 20; see the remarks.");

        clock.NowUtc.ShouldBe(
            Harnesses.Start,
            "a refused advance leaves the clock where it was — a guard that threw after writing " +
            "would leave the simulation in a state no caller asked for.");
    }

    /// <summary>
    /// 🔒 <c>default(DateTimeOffset)</c> cannot be used as a start — the trap M1-08 signposted for
    /// this type <b>by name</b>, closed rather than documented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M1-08's finding: <c>default(DateTimeOffset)</c> is <c>0001-01-01T00:00:00+00:00</c> and
    /// <b>passes</b> <c>GameContext</c>'s zero-offset guard, so a clock or a fixture that forgot to
    /// set <c>NowUtc</c> lands there silently. <c>GameCalendar</c> floors its arithmetic at
    /// <see cref="GameCalendar.FirstGameDay"/> so the state is survivable; this makes it
    /// unreachable.
    /// </para>
    /// <para>
    /// 🔒 The first assertion is the one that makes the rest meaningful: it re-establishes M1-08's
    /// premise from the framework rather than trusting this test's own memory of it. If
    /// <c>default(DateTimeOffset)</c> ever stopped carrying a zero offset, the guard below would be
    /// catching a different thing than the one it was written for.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_default_DateTimeOffset_cannot_be_used_as_a_start()
    {
        default(DateTimeOffset).Offset.ShouldBe(
            TimeSpan.Zero,
            "M1-08's premise: default(DateTimeOffset) PASSES GameContext's zero-offset guard, which " +
            "is why it needs a guard of its own here rather than being caught downstream.");

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(default));

        refusal.ParamName.ShouldBe("startUtc");
        refusal.Message.ShouldContain("default(DateTimeOffset)", Case.Sensitive);
        refusal.Message.ShouldContain("A7", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The negative half: the first game day itself is accepted, so the guard above is a floor
    /// rather than a refusal of everything.
    /// </summary>
    /// <remarks>
    /// Without this, a guard that threw for every start would look like a stricter rule instead of a
    /// broken one — the shape steering <b>S1</b> is about, pointed the other way.
    /// </remarks>
    [Fact]
    public void The_first_game_day_itself_is_an_acceptable_start()
    {
        new VirtualClock(GameCalendar.FirstGameDay).NowUtc.ShouldBe(GameCalendar.FirstGameDay);

        Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(GameCalendar.FirstGameDay.AddTicks(-1)),
            "one tick earlier is one tick before the first game day the calendar can answer.");
    }

    /// <summary>🔒 A start stated in anything but UTC is refused where it was written.</summary>
    /// <remarks>
    /// <c>GameContext</c> refuses it too, and that is not a reason to leave it out: the failure a
    /// caller gets from there names the first command they sent, not the line that built the clock,
    /// and 05:00+02:00 against 05:00+00:00 is two different game days naming instants two hours
    /// apart. The message fragment pins which of this type's four guards fired (S2).
    /// </remarks>
    [Fact]
    public void A_start_with_a_non_zero_offset_is_refused()
    {
        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => new VirtualClock(new DateTimeOffset(2026, 8, 12, 5, 0, 0, TimeSpan.FromHours(2))));

        refusal.ParamName.ShouldBe("startUtc");
        refusal.Message.ShouldContain("offset", Case.Insensitive);
        refusal.Message.ShouldNotContain(
            "default(DateTimeOffset)",
            Case.Sensitive,
            "an offset failure and a before-the-first-game-day failure have different fixes; a " +
            "single message would send whoever hit it to the wrong line (S2).");
    }

    /// <summary>An advance past the end of representable time is refused rather than overflowing.</summary>
    [Fact]
    public void An_advance_past_the_end_of_time_is_refused()
    {
        var clock = new VirtualClock(Harnesses.Start);

        var refusal = Should.Throw<ArgumentOutOfRangeException>(
            () => clock.Advance(DateTimeOffset.MaxValue - Harnesses.Start + TimeSpan.FromTicks(1)));

        refusal.ParamName.ShouldBe("by");

        // 🔒 The fragment, like the other three guards on this type: both overflow and the negative
        // advance throw ArgumentOutOfRangeException(nameof(by)), and a positive span cannot reach
        // the negative guard today — but "cannot today" is a property of the arithmetic, not of the
        // assertion, and this file's policy is that every guard says which one fired (S2).
        refusal.Message.ShouldContain("end of representable time", Case.Sensitive);

        clock.NowUtc.ShouldBe(Harnesses.Start);

        // …and the boundary itself is reachable, so the guard is not off by one in the direction
        // that would quietly cost a caller the last representable tick.
        clock.Advance(DateTimeOffset.MaxValue - Harnesses.Start);
        clock.NowUtc.ShouldBe(DateTimeOffset.MaxValue);
    }

    /// <summary>Two clocks are independent: advancing one does not move the other.</summary>
    /// <remarks>
    /// `21` §9 sweeps 14 profiles, each its own simulation, and a clock that was shared static state
    /// would make every one of those runs depend on the order the others ran in — which is the exact
    /// failure "reproducible byte-for-byte" (`30` §6) rules out.
    /// </remarks>
    [Fact]
    public void Two_clocks_are_independent()
    {
        var first = new VirtualClock(Harnesses.Start);
        var second = new VirtualClock(Harnesses.Start);

        first.Advance(TimeSpan.FromDays(180));

        second.NowUtc.ShouldBe(Harnesses.Start);
        first.NowUtc.ShouldBe(Harnesses.Start.AddDays(180));
    }
}
