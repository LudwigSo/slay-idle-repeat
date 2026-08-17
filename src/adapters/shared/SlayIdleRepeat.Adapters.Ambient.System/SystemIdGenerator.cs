using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.Ambient.System;

/// <summary>
/// The real <see cref="IIdGeneratorPort"/>: fresh identity from the platform's guid generator.
/// </summary>
/// <remarks>
/// Holds no state at all, which is what satisfies the port's cross-instance clause without an
/// argument: there is no sequence to restart, so two generators — and two runs of the same process
/// — share nothing an idempotency key could collide on.
/// </remarks>
public sealed class SystemIdGenerator : IIdGeneratorPort
{
    /// <inheritdoc/>
    public Guid NewGuid() => Guid.NewGuid();

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 An <b>opaque token</b>: 22 characters of base64url over a fresh guid's sixteen bytes.
    /// Nothing may parse it, take it apart or read a time out of it — no wire contract has chosen a
    /// shape for a command id, and inventing one here would fix a format the protocol has not
    /// agreed to. The encoding is chosen for one reason only: a command id travels in a URL, a log
    /// line and a header unescaped, and base64url is the alphabet that survives all three.
    /// </remarks>
    public string NewCommandId() => ToBase64Url(Guid.NewGuid().ToByteArray());

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
