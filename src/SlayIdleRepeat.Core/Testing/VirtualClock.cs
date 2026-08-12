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
/// 🔒 <b>It only goes forwards, and that is a decision rather than an omission</b> — carried-forward
/// item 20, which this type deliberately declines to settle. Two halves of one ruling disagree
/// today: <c>GameRules.AdvanceTime</c> <b>clamps</b> a backwards clock on the energy path
/// (<em>"a backwards clock costs the player nothing and grants them nothing"</em>, kept that way for
/// `30` §2.1's <b>P3</b>), while <c>Player.MarkApplied</c> <b>throws</b>
/// <see cref="ArgumentOutOfRangeException"/> on a <c>NowUtc</c> earlier than
/// <c>LastAppliedAtUtc</c> — an exception out of <c>Apply</c>, which is the P3 violation the clamp
/// exists to avoid, reached through the other door.
/// </para>
/// <para>
/// A clock that could go backwards would make <b>this harness the only producer</b> of that
/// disagreement in the repository: every test that moved time backwards would be asserting on an
/// unresolved ruling, and the first one written would silently become the ruling. So
/// <see cref="Advance"/> refuses a negative span and there is no setter. ⚠️ <b>This does not close
/// the question and must not be read as closing it.</b> Host clock skew is real and reaches
/// <c>Apply</c> in production through a composition root that is not this type; the disagreement
/// stays carried forward, owned by whichever milestone rules on M1-05's guard against M1-08's
/// clamp. What is decided here is only that the <em>test harness</em> does not manufacture it.
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
                "setter, and the reason it is enforced rather than assumed is carried-forward item " +
                "20: GameRules.AdvanceTime CLAMPS a backwards clock on the energy path (30 §2.1's " +
                "P3 — an exception out of Apply is a defect, not a refusal) while " +
                "Player.MarkApplied THROWS on a NowUtc earlier than LastAppliedAtUtc. Those two are " +
                "halves of one ruling and they disagree. A harness that could rewind would be the " +
                "only producer of that state in the repository, and whichever test was written " +
                "first would settle a ruling nobody made. Skew is still real in production and " +
                "still reaches Apply through a composition root that is not this type; it is not " +
                "settled here, it is refused here.");
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
