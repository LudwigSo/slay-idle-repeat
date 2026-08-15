using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Testing;

/// <summary>The harness's clock: advanced explicitly. Nothing ever waits.</summary>
/// <remarks>
/// <para>
/// It never asks what time it is — no <c>DateTimeOffset.UtcNow</c>, not even as a constructor
/// default. The start instant is an argument, and every later instant is the sum of the advances a
/// caller asked for, which is what makes "energy regenerates by rule, not by waiting" mechanically
/// true rather than a claim about test-suite speed.
/// </para>
/// <para>
/// Forward-only, deliberately: the harness models a clock the player experiences, and a clock that
/// could rewind would let a test assert on a state the harness manufactured rather than one a
/// composition root produces. Backwards host clock skew is real and is exercised elsewhere, by
/// building state through <c>Rehydrate</c> at an earlier instant rather than by rewinding a clock.
/// </para>
/// <para>
/// Cannot be constructed into <c>default(DateTimeOffset)</c> by accident: that value passes
/// <c>GameContext</c>'s zero-offset guard silently, so a start before the first game day is refused
/// with a message naming <c>default(DateTimeOffset)</c> explicitly, since that's what a forgotten
/// assignment reads as.
/// </para>
/// <para>A class rather than a record: mutable by design, so value equality over a mutable instant would be a trap.</para>
/// </remarks>
public sealed class VirtualClock
{
    private DateTimeOffset _nowUtc;

    /// <summary>A clock stopped at <paramref name="startUtc"/>.</summary>
    /// <param name="startUtc">
    /// The instant the simulation begins, in UTC. Must carry a zero offset and must not precede
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

    /// <summary>Moves the clock forward. <c>Advance(TimeSpan.FromHours(8))</c> is eight hours of regeneration, paid the next time a command is applied.</summary>
    /// <param name="by">How far forward. Never negative. <see cref="TimeSpan.Zero"/> is permitted and is a no-op.</param>
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

    /// <summary>The guard that makes the <c>default(DateTimeOffset)</c> trap unreachable. See the type's remarks.</summary>
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

    /// <summary>Renders an instant or a span with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same everywhere.</summary>
    private static string Text(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(DateTimeOffset)"/>
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);
}
