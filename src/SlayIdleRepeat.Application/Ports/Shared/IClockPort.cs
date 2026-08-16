namespace SlayIdleRepeat.Application.Ports.Shared;

/// <summary>
/// Wall-clock time. The only sanctioned way anything in this application learns what time it is:
/// the ambient clocks are banned outright, so an implementation of this interface is the single
/// place a reading can enter.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Never injected into <c>SlayIdleRepeat.Core</c>.</b> A rule that calls a clock is not a
/// function of its inputs, and a great many rules here are time-dependent — energy regeneration,
/// the daily and weekly boundaries, event windows, subscription expiry. The composition root reads
/// <see cref="UtcNow"/> once, at the edge, and passes the answer down as a plain value on the game
/// context. That is what makes a run replayable: the same command sequence with the same recorded
/// instant produces the same state, with no clock to re-wind.
/// </para>
/// <para>
/// Consequently a use case that needs "now" takes it as an argument. Reaching for this port from
/// inside a computation, rather than at the boundary, defeats the whole arrangement.
/// </para>
/// </remarks>
public interface IClockPort
{
    /// <summary>
    /// The current instant, <b>always in UTC</b> — <c>Offset</c> is <see cref="TimeSpan.Zero"/> on
    /// every reading from every implementation, whatever the host machine's local zone is. A caller
    /// may compare two readings, or a reading against a stored instant, without normalising first.
    /// </summary>
    /// <remarks>
    /// Non-decreasing across successive reads of one instance: a later read never reports an earlier
    /// instant. It is deliberately <i>not</i> required to be strictly increasing — two reads inside
    /// one timer tick legitimately return the same instant, and an implementation that manufactured
    /// a difference to avoid that would be inventing precision it does not have.
    /// </remarks>
    DateTimeOffset UtcNow { get; }
}
