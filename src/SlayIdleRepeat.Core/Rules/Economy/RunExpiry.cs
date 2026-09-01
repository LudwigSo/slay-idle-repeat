using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
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
/// <para>
/// 🔓 <b>Public, and narrowly so, because a client that cannot ask this question strands the
/// player.</b> <c>GameRules</c> refuses every run command on a lapsed run with <c>RUN_EXPIRED</c>
/// and — deliberately — settles it only on the next command the player is ALLOWED to make, which is
/// every meta command and <c>START_RUN</c>. A client that cannot tell a lapsed run from a live one
/// therefore offers to resume it, opens a screen whose every control is a run command, and leaves
/// the player with nothing that works and no way back. That happened: `16` D70.
/// </para>
/// <para>
/// 🔒 <b>One predicate, two shapes of input.</b> The rules ask it of the aggregate mid-command; a
/// client asks it of the snapshot it was handed. Both go through <see cref="HasLapsed(RunPhase,
/// DateTimeOffset, DateTimeOffset, ContentSnapshot)"/>, because a second copy of "how long is too
/// long" is a second thing to keep true — and the copy that drifted would be the one deciding
/// whether the player is shown a door or a wall.
/// </para>
/// </remarks>
public static class RunExpiry
{
    /// <summary>Whether this run's window has passed.</summary>
    /// <param name="run">The run, or <c>null</c> when the slice carries none.</param>
    /// <param name="nowUtc">The instant the command is applied at.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    internal static bool HasLapsed(Run? run, DateTimeOffset nowUtc, ContentSnapshot content) =>
        run is not null && HasLapsed(run.Phase, run.LastAppliedAtUtc, nowUtc, content);

    /// <summary>
    /// Whether the run a client is holding has been left alone past its window — so every run
    /// command addressed to it will be refused, and the player must be offered a new run instead.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The answer is a fact about an instant, not a subscription.</b> A run live when this is
    /// asked lapses while the screen is up if the player leaves it open long enough, and nothing
    /// here says so. A caller that keeps a decision on screen for hours re-reads it or accepts that
    /// the refusal is what tells the player.
    /// </remarks>
    /// <param name="run">The run the client was handed, or <c>null</c> when it holds none.</param>
    /// <param name="nowUtc">Now, from the one sanctioned clock — never an ambient reading.</param>
    /// <param name="content">The version-stamped content snapshot the client loaded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static bool HasLapsed(RunSnapshot? run, DateTimeOffset nowUtc, ContentSnapshot content) =>
        run is not null && HasLapsed(run.Phase, run.LastAppliedAtUtc, nowUtc, content);

    /// <summary>The predicate itself, over the two facts either shape of input carries.</summary>
    private static bool HasLapsed(
        RunPhase phase, DateTimeOffset lastAppliedAtUtc, DateTimeOffset nowUtc, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return phase != RunPhase.Ended &&
               nowUtc - lastAppliedAtUtc >= RunLifetimeTuning.Read(content).Window;
    }
}
