using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The settable fake for <see cref="IClockPort"/>: it reads <see cref="Start"/> until a test moves
/// it, and then reads exactly where it was moved to.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a time-dependent scenario a function of its inputs — energy regeneration
/// across a day boundary, an event window opening, a subscription expiring — without a test ever
/// waiting for a real second to pass.
/// </para>
/// <para>
/// 🔒 Neither knob can move it backwards. The port's non-decreasing clause is a property of every
/// clock, not a default a fake may switch off, and a fake that could rewind would let a use case
/// pass here on an elapsed span the real clock can never produce.
/// </para>
/// </remarks>
public sealed class AdjustableClock : IClockPort
{
    /// <summary>
    /// The instant a freshly constructed clock reads. Fixed and stated so a scenario can assert
    /// against it, and comfortably after the epoch every reading has to clear.
    /// </summary>
    public static readonly DateTimeOffset Start = new(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = Start;

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _now;

    /// <summary>Moves the clock to <paramref name="instant"/>, normalised to UTC.</summary>
    /// <param name="instant">Where the clock should read next. Never earlier than it reads now.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="instant"/> is earlier than the current reading.
    /// </exception>
    public void Set(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();

        if (utc < _now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instant),
                instant,
                $"a clock reads {_now:O} and cannot be set back to {utc:O}. Every implementation of " +
                "IClockPort is non-decreasing, so an elapsed span is never negative — and a cooldown " +
                "computed against a rewound reading never ends.");
        }

        _now = utc;
    }

    /// <summary>Moves the clock forward by <paramref name="by"/>.</summary>
    /// <param name="by">How far forward. Never negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="by"/> is negative.</exception>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(by),
                by,
                "a clock only moves forward. Set the instant directly if a scenario needs to start " +
                "somewhere else.");
        }

        _now += by;
    }
}
