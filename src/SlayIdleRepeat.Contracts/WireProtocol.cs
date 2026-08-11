namespace SlayIdleRepeat.Contracts;

/// <summary>
/// The wire protocol version carried by every command request and response envelope
/// (<c>14</c> §16.1).
/// </summary>
/// <remarks>
/// One of the three independent version numbers in this codebase:
/// <list type="bullet">
///   <item>the assembly SemVer lives in <c>Directory.Build.props</c> and moves on releases;</item>
///   <item><see cref="PROTOCOL_VERSION"/> versions the envelope and lifecycle semantics;</item>
///   <item><c>SlayIdleRepeat.Core.Model.Snapshots.SnapshotSchema.SchemaVersion</c> versions
///         snapshot serialisation.</item>
/// </list>
/// They are never bumped together for tidiness.
/// </remarks>
public static class WireProtocol
{
    /// <summary>
    /// The protocol version this build speaks.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Bump rule (<c>14</c> §16.1):</b> this integer versions the <i>envelope and lifecycle
    /// semantics</i> of <c>14</c> §16 and nothing else. Bump it only on a wire-contract change.
    /// Content changes ride the content hash (<c>14</c> §6) and never bump it; command additions
    /// ride the command registry (<c>14</c> §2.3) and never bump it either.
    /// <para>🔒 <b>Skew rule:</b> the server accepts its own version <c>N</c> and <c>N−1</c>.
    /// Anything outside that window is rejected with <c>PROTOCOL_VERSION_UNSUPPORTED</c> and the
    /// client shows the forced-update flow.</para>
    /// </remarks>
    public const int PROTOCOL_VERSION = 1;
}
