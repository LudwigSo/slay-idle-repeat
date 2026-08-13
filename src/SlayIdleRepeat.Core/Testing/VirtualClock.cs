using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Testing;

/// <summary>
/// 🔒 `30` §6 — the harness's clock. <em>"Time is <c>VirtualClock</c> — advanced explicitly. Nothing
/// ever waits."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>It never asks what time it is.</b> There is no <c>DateTimeOffset.UtcNow</c> here, not even
/// as a constructor default — <c>DomainPurityTests.Domain_has_no_ambient_time_or_randomness</c>
/// forbids it assembly-wide, and `30` §3's whole argument is that a rule which calls a clock is not
/// pure. The start instant is an argument, and every subsequent instant is the sum of the advances
/// a caller asked for. That is what makes <em>"energy regenerates by RULE, not by waiting"</em>
/// (`30` §6) mechanically true rather than a claim about how fast the test suite runs.
/// </para>
/// <para>
/// 🔒 <b>It only goes forwards, and that is a decision rather than an omission.</b> M1-11 wrote it
/// as a refusal to settle carried-forward item 20 — <c>GameRules.AdvanceTime</c> <b>clamped</b> a
/// backwards clock on the energy path for `30` §2.1's <b>P3</b> while <c>Player.MarkApplied</c>
/// <b>threw</b> on the same input, two halves of one ruling disagreeing — on the reasoning that a
/// rewindable harness would be the only producer of that state in the repository, so whichever test
/// was written first would settle a ruling nobody had made.
/// </para>
/// <para>
/// 🔒 <b>M1-12 settled it, on P3's side, and this paragraph is corrected rather than left to
/// rot</b> (steering <b>S4</b>'s known limit — the reason this type gave for being forward-only was
/// still formally valid and had stopped being the reason). <c>GameRules.MarkApplied</c> now floors
/// the instant it hands both aggregates; the aggregates go on refusing a backwards anchor, because
/// inside the model that is a persistence defect rather than skew. Skew therefore returns a result.
/// </para>
/// <para>
/// ⚠️ <b>Forward-only stays, on its own remaining merit.</b> It is no longer about an unsettled
/// ruling: `30` §6 writes the harness as <c>Advance(...)</c> rather than a setter, and a clock that
/// could rewind would let a test assert on a state the <em>harness</em> manufactured rather than one
/// a composition root produces. Skew is real and reaches <c>Apply</c> through composition roots that
/// are not this type — and it is now driven deliberately, by
/// <c>GameRulesBackwardsClockTests</c>, which builds the state through `30` §11.3's
/// <c>Rehydrate</c> instead of by rewinding a clock.
/// </para>
/// <para>
/// 🔒 <b>It cannot be constructed into <c>default(DateTimeOffset)</c> by accident, and that is a
/// defect closed rather than documented.</b> M1-08 left this signposted by name:
/// <c>default(DateTimeOffset)</c> is <c>0001-01-01T00:00:00+00:00</c>, it <em>passes</em>
/// <c>GameContext</c>'s zero-offset guard, and a clock or fixture that forgot to set <c>NowUtc</c>
/// lands exactly there. <see cref="GameCalendar.FirstGameDay"/> floors the calendar's arithmetic so
/// that state is survivable; this type makes it <b>unreachable</b> instead — there is no
/// parameterless constructor, and a start before the first game day is refused with a message that
/// names <c>default(DateTimeOffset)</c> out loud, because that is what the caller almost certainly
/// passed.
/// </para>
/// <para>
/// ⚠️ A class rather than a record: it is mutable by design (that is what <see cref="Advance"/> is),
/// and value equality over a mutable instant would be a trap. It also means no synthesized
/// <c>PrintMembers</c> renders a <see cref="DateTimeOffset"/> through the ambient culture — the
/// blind spot `14` §8.2 and <c>AmbientApiTests</c> are about.
/// </para>
/// </remarks>
public sealed class VirtualClock
{
    private DateTimeOffset _nowUtc;

    /// <summary>
    /// A clock stopped at <paramref name="startUtc"/>.
    /// </summary>
    /// <param name="startUtc">
    /// 🔒 The instant the simulation begins, in UTC. Required, with no default — see the type's
    /// remarks. Must carry a zero offset and must not precede
    /// <see cref="GameCalendar.FirstGameDay"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="startUtc"/> carries a non-zero offset, or precedes the first game day.
    /// </exception>
    public VirtualClock(DateTimeOffset startUtc) => _nowUtc = RequireStart(startUtc);

    /// <summary>
    /// The instant this clock currently reads — what a composition root puts on
    /// <c>GameContext.NowUtc</c>.
    /// </summary>
    public DateTimeOffset NowUtc => _nowUtc;

    /// <summary>
    /// 🔒 `30` §6 — moves the clock forward. <c>Advance(TimeSpan.FromHours(8))</c> is eight hours of
    /// regeneration and every boundary in between, paid the next time a command is applied.
    /// </summary>
    /// <param name="by">
    /// How far forward. Never negative — see the type's remarks. <see cref="TimeSpan.Zero"/> is
    /// permitted and is a no-op: "no time passed" is a legitimate thing for a caller to express in a
    /// loop, and refusing it would push a branch into every caller that computes its step.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="by"/> is negative, or the advance would overflow
    /// <see cref="DateTimeOffset.MaxValue"/>.
    /// </exception>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(by),
                by,
                "A VirtualClock only moves FORWARDS. 30 §6 writes this as Advance(...), not as a " +
                "setter: the harness models a clock the player experiences, and a test that rewound " +
                "it would assert on a state the HARNESS manufactured rather than one a composition " +
                "root produces. Host clock skew is real, reaches Apply through composition roots " +
                "that are not this type, and has been 30 §2.1 P3-safe since M1-12 settled " +
                "carried-forward item 20 — GameRules.MarkApplied floors the instant it hands the " +
                "aggregates, so a backwards clock returns a result instead of throwing. To drive " +
                "skew, build the state through 30 §11.3's Rehydrate and apply a command at an " +
                "earlier NowUtc; do not rewind the harness.");
        }

        if (by > DateTimeOffset.MaxValue - _nowUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(by),
                by,
                "Advancing from " + Text(_nowUtc) + " by " + Text(by) + " runs past " +
                Text(DateTimeOffset.MaxValue) + ", the end of representable time. A span that large " +
                "in a simulation is an arithmetic defect upstream — a multiplication by a count " +
                "that was meant to be a divisor, most often — not a duration to roll a player " +
                "forward by, and the alternative to refusing it is an OverflowException from " +
                "inside the addition with nothing to say which caller asked for it.");
        }

        _nowUtc += by;
    }

    /// <summary>
    /// 🔒 The guard that makes the <c>default(DateTimeOffset)</c> trap unreachable. See the type's
    /// remarks.
    /// </summary>
    private static DateTimeOffset RequireStart(DateTimeOffset startUtc)
    {
        if (startUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startUtc),
                startUtc,
                Text(startUtc) + " carries a " + Text(startUtc.Offset) + " offset. Every instant " +
                "this clock hands to GameContext.NowUtc is read for its wall-clock components — " +
                "30 §2.3's 05:00 UTC game day above all — so 06:00+02:00 and 06:00+00:00 are two " +
                "different game days while naming instants two hours apart. GameContext refuses it " +
                "too; it is refused HERE as well so the failure names the line that built the " +
                "clock rather than the first command sent through it.");
        }

        if (startUtc < GameCalendar.FirstGameDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startUtc),
                startUtc,
                "A simulation cannot start at " + Text(startUtc) + ", which is before " +
                Text(GameCalendar.FirstGameDay) + " — the first 05:00 UTC game day the calendar " +
                "can answer (recorded assumption A7). ⚠️ IF YOU DID NOT PASS THAT INSTANT " +
                "DELIBERATELY, you passed default(DateTimeOffset): it is 0001-01-01T00:00:00+00:00, " +
                "it PASSES GameContext's zero-offset guard, and it is what a field that was never " +
                "assigned reads as. That is the exact state M1-08 signposted for this type, and " +
                "refusing it here is why it can no longer be reached by forgetting rather than by " +
                "choosing. Pass the instant the simulation starts at.");
        }

        return startUtc;
    }

    /// <summary>
    /// 🔒 Renders an instant or a span with <see cref="CultureInfo.InvariantCulture"/>, for the
    /// reason <c>Player</c>, <c>GameRules</c> and <c>EnergyTuning</c> each have one: `14` §8.2 wants
    /// <c>Core</c> reading identically everywhere, and
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> fails the
    /// build for a bare interpolation.
    /// </summary>
    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(DateTimeOffset)"/>
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
