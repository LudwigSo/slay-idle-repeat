using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// An <see cref="IClockPort"/> whose readings move by a fixed step per read, and which remembers
/// how far it moved.
/// </summary>
/// <remarks>
/// <para>
/// A boot presenter reports how long the boot took, and the number that reaches a latency budget
/// has to come from the injected clock rather than from the ambient one. Driving that with a
/// scripted clock makes the expected value a fact of the fixture instead of a range: the span the
/// clock moved between the presenter's first and last reading is exactly what
/// <c>BootPresenter.Elapsed</c> must report.
/// </para>
/// <para>
/// 🔒 <see cref="Reads"/> is exposed because the assertion is otherwise satisfiable by never
/// reading at all — a presenter that leaves <c>Elapsed</c> at zero agrees with a clock that was
/// never asked the time. Non-decreasing across reads, as the port requires;
/// <see cref="Frozen"/> is the legitimate zero-step case, not a violation of it.
/// </para>
/// </remarks>
internal sealed class SteppingClock : IClockPort
{
    private readonly DateTimeOffset _start;
    private readonly TimeSpan _step;
    private int _reads;

    private SteppingClock(DateTimeOffset start, TimeSpan step)
    {
        _start = start;
        _step = step;
    }

    /// <summary>A clock that reports the same instant however often it is read.</summary>
    internal static SteppingClock Frozen(DateTimeOffset at) => new(at, TimeSpan.Zero);

    /// <summary>A clock that advances by <paramref name="step"/> on every reading.</summary>
    internal static SteppingClock Advancing(DateTimeOffset from, TimeSpan step) => new(from, step);

    /// <summary>How many times the clock has been read.</summary>
    internal int Reads => _reads;

    /// <summary>The span between the first reading taken and the most recent one.</summary>
    internal TimeSpan Moved => _reads <= 1 ? TimeSpan.Zero : _step * (_reads - 1);

    /// <inheritdoc/>
    public DateTimeOffset UtcNow
    {
        get
        {
            var now = _start + (_step * _reads);
            _reads++;
            return now;
        }
    }
}
