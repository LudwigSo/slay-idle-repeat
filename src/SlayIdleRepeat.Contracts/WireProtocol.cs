namespace SlayIdleRepeat.Contracts;

/// <summary>
/// The wire protocol version carried by every command request and response envelope.
/// </summary>
/// <remarks>
/// One of three independent version numbers in this codebase: the assembly SemVer in
/// <c>Directory.Build.props</c>, <see cref="PROTOCOL_VERSION"/> for envelope/lifecycle
/// semantics, and <c>SnapshotSchema.SchemaVersion</c> for snapshot serialisation. They
/// are never bumped together.
/// </remarks>
public static class WireProtocol
{
    /// <summary>
    /// The protocol version this build speaks.
    /// </summary>
    /// <remarks>
    /// Bump only on a wire-contract change to envelope/lifecycle semantics — content
    /// changes ride the content hash and command additions ride the command registry,
    /// neither bumps this. The server accepts its own version <c>N</c> and <c>N-1</c>;
    /// anything outside that window is rejected with <c>PROTOCOL_VERSION_UNSUPPORTED</c>
    /// and the client shows the forced-update flow.
    /// </remarks>
    public const int PROTOCOL_VERSION = 1;
}
