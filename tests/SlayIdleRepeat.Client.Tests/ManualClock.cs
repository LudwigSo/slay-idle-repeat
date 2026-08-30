using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>An <see cref="IClockPort"/> that moves only when a case moves it.</summary>
/// <remarks>
/// 🔒 Distinct from <see cref="SteppingClock"/> rather than a variant of it. That one advances on
/// every READ, which is what a latency measurement needs and what a schedule cannot use: the
/// reconnect ladder is asserted on "0.5 s elapsed between two polls", and a clock that also moved
/// whenever the code under test happened to read it would make the elapsed span a fact about the
/// implementation's read count instead of about the case.
/// </remarks>
internal sealed class ManualClock : IClockPort
{
    private DateTimeOffset _now;

    /// <summary>A clock reading the given instant until it is told otherwise.</summary>
    internal ManualClock(DateTimeOffset now) => _now = now;

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="span">By how much. Never negative — the port requires readings not to go back.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is negative.</exception>
    internal void Advance(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                span,
                "IClockPort is non-decreasing across reads, so a fixture that rewound would be " +
                "proving code against a clock no implementation could be.");
        }

        _now += span;
    }
}
