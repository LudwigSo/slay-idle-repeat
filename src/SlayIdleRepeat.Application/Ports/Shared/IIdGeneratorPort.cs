namespace SlayIdleRepeat.Application.Ports.Shared;

/// <summary>
/// Fresh identity. The only sanctioned source of a new identifier: the ambient generators are
/// banned outright, so an implementation of this interface is the single place one can be minted.
/// </summary>
/// <remarks>
/// Paired with the clock for the same reason — a test supplies a counting generator and gets a
/// reproducible sequence, where an ambient call would make every run of the same scenario differ in
/// its identifiers alone.
/// </remarks>
public interface IIdGeneratorPort
{
    /// <summary>
    /// A fresh <see cref="Guid"/>. Never <see cref="Guid.Empty"/> — the empty guid is the value a
    /// caller uses to mean "no identity", so returning it would make "unset" and "freshly minted"
    /// the same value.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Unique across separate instances of the generator, not merely within one.</b> That
    /// stronger claim is the one that matters: a generator that restarts its sequence on
    /// construction produces the same identifiers after a process restart as it did before it, and
    /// an idempotency key that repeats after a restart is not an idempotency key — a retried command
    /// would be matched against an unrelated earlier one and its outcome replayed.
    /// </remarks>
    Guid NewGuid();

    /// <summary>
    /// A fresh command identifier: a non-empty string containing no whitespace and no control
    /// character, so it survives a URL, a log line and a header unescaped and unambiguous.
    /// </summary>
    /// <remarks>
    /// Unique under ordinal comparison, and — as for <see cref="NewGuid"/>, and for the same
    /// idempotency reason — unique across separate instances of the generator too.
    /// </remarks>
    string NewCommandId();
}
