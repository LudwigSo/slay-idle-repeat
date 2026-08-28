using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>Whether a run has been left alone for longer than its authored window.</summary>
/// <remarks>
/// <para>
/// The window slides from <c>Run.LastAppliedAtUtc</c>, which only an accepted run command stamps —
/// so a shop visit or a daily claim mid-run cannot keep a run nobody is playing alive. The span is
/// authored content rather than a constant here, and the server's run-row lifetime mirrors it.
/// </para>
/// <para>
/// An already-ended run is never expired: it was closed and paid out, and answering "expired" for
/// it would invite a client to wait for a settlement that already happened.
/// </para>
/// </remarks>
internal static class RunExpiry
{
    /// <summary>Whether this run's window has passed.</summary>
    /// <param name="run">The run, or <c>null</c> when the slice carries none.</param>
    /// <param name="nowUtc">The instant the command is applied at.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    internal static bool HasLapsed(Run? run, DateTimeOffset nowUtc, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return run is { Phase: not RunPhase.Ended } &&
               nowUtc - run.LastAppliedAtUtc >= RunLifetimeTuning.Read(content).Window;
    }
}
