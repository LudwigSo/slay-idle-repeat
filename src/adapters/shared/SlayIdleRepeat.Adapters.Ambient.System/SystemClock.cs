using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.Ambient.System;

/// <summary>
/// The real <see cref="IClockPort"/>: the host machine's UTC clock, read straight from the BCL.
/// </summary>
/// <remarks>
/// Stateless and one line long, which is the point. Everything the clock could otherwise be asked
/// to decide — what "today" means, when a window opens, whether a subscription has expired — is a
/// computation over an instant that already entered as a value, and belongs above this seam.
/// </remarks>
public sealed class SystemClock : IClockPort
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
