using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>Where a run's and a session's content pins live.</summary>
/// <remarks>
/// <para>
/// A dumb store on purpose: read a pin, write a pin, list what is retained. The rules — when a pin
/// is taken, which snapshot an unresolvable pin falls back to, and how long a version is kept —
/// live in <see cref="ContentPinning"/> and <see cref="ContentRetention"/> and nowhere else, so the
/// durable store that replaces this seam inherits them instead of re-deciding them.
/// </para>
/// <para>
/// A run pin is taken once, when the run opens, and never moves: a run played half against one
/// content set and half against another is a run whose replay proves nothing. A session pin is the
/// player's current answer between runs, and it does move — on the next session begin.
/// </para>
/// <para>
/// 🔒 <b>A seam, not a port, and deliberately not under <c>Ports/</c>.</b> A port names a capability
/// this application needs from the outside world and cannot supply itself — a clock, a database, a
/// store front. This is the opposite: pins are the application's own bookkeeping, and the interface
/// exists so the gateway can be driven against an in-memory double rather than because some vendor
/// is on the other side. Promoting it to a port would put it in the catalogue every adapter is
/// checked against and imply an infrastructure boundary that is not there — the same reasoning that
/// keeps <c>ICommandLedgerStore</c> in <c>Wire/</c>. Where expiry is genuinely storage semantics,
/// the durable implementation reaches its real backing through the ports that already exist.
/// </para>
/// </remarks>
public interface IContentPinStore
{
    /// <summary>The version a run is pinned to, or <c>null</c> when the run has no pin.</summary>
    /// <param name="run">The run.</param>
    /// <param name="ct">Cancellation.</param>
    Task<ContentVersion?> ReadRunPinAsync(RunId run, CancellationToken ct);

    /// <summary>Pins a run to a version, recording when the pin was last touched.</summary>
    /// <param name="run">The run.</param>
    /// <param name="version">The version the run is played against for its whole life.</param>
    /// <param name="atUtc">The moment of the write, supplied by the caller — this assembly reads no clock.</param>
    /// <param name="ct">Cancellation.</param>
    Task WriteRunPinAsync(RunId run, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct);

    /// <summary>The version a player's current session is pinned to, or <c>null</c>.</summary>
    /// <param name="player">The player.</param>
    /// <param name="ct">Cancellation.</param>
    Task<ContentVersion?> ReadSessionPinAsync(PlayerId player, CancellationToken ct);

    /// <summary>Pins a player's session to a version, replacing whatever the previous session held.</summary>
    /// <param name="player">The player.</param>
    /// <param name="version">The version this session was begun against.</param>
    /// <param name="atUtc">The moment of the write, supplied by the caller.</param>
    /// <param name="ct">Cancellation.</param>
    Task WriteSessionPinAsync(PlayerId player, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct);

    /// <summary>Every version any pin still names, with the last moment it was named.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>What a sweep is handed. A version absent from this list is one nothing is pinned to.</remarks>
    Task<IReadOnlyList<RetainedVersion>> ListRetainedAsync(CancellationToken ct);
}

/// <summary>
/// The content a command is judged against: the current snapshot, the older pinned ones, and the
/// store that remembers who is pinned to what.
/// </summary>
/// <remarks>
/// <para>
/// One constructor parameter rather than four, because the four are one decision. A gateway handed
/// a store, a snapshot, a resolver and a log sink separately would let a composition root supply
/// three of them and forget the fourth, and the failure would be a run silently judged against the
/// wrong content.
/// </para>
/// <para>
/// 🔒 An unresolvable pin falls back to <see cref="Current"/> AND warns, never silently. A pin that
/// no longer resolves means a bundle was swept while something was still pinned to it — the run is
/// now being played against different numbers than it was opened with, which is a retention defect
/// worth a line in the log rather than a fallback nobody can see happening.
/// </para>
/// </remarks>
public sealed class ContentPinning
{
    /// <summary>Wires the pin store, the current snapshot, the resolver for older ones, and the warn sink.</summary>
    /// <param name="store">Where pins are read and written.</param>
    /// <param name="current">The snapshot the server is serving now.</param>
    /// <param name="resolveSnapshot">Resolves a pinned version to its snapshot, or <c>null</c> when it is no longer retained.</param>
    /// <param name="warn">The loud-log seam an unresolvable pin is reported through.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ContentPinning(
        IContentPinStore store,
        ContentSnapshot current,
        Func<ContentVersion, ContentSnapshot?> resolveSnapshot,
        Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(resolveSnapshot);
        ArgumentNullException.ThrowIfNull(warn);

        Store = store;
        Current = current;
        ResolveSnapshot = resolveSnapshot;
        Warn = warn;
    }

    /// <summary>Where pins are read and written.</summary>
    public IContentPinStore Store { get; }

    /// <summary>The snapshot the server is serving now — the answer for anything unpinned.</summary>
    public ContentSnapshot Current { get; }

    private Func<ContentVersion, ContentSnapshot?> ResolveSnapshot { get; }

    private Action<string> Warn { get; }

    /// <summary>The snapshot a pinned version resolves to, falling back — loudly — to <see cref="Current"/>.</summary>
    /// <param name="pinned">The pinned version, or <c>null</c> when there is no pin.</param>
    /// <returns>The pinned snapshot, or <see cref="Current"/> when there is no pin or the pin no longer resolves.</returns>
    public ContentSnapshot SnapshotFor(ContentVersion? pinned) => throw new NotImplementedException();
}
