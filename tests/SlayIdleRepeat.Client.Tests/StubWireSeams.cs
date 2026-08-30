using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// A wire half that is composed over and never called.
/// </summary>
/// <remarks>
/// <para>
/// Every member refuses rather than answering, and that is the assertion rather than a shortcut:
/// composing a client must not talk to a server. If a root ever opened a session, read a run or
/// asked for content while wiring the graph, the cases over it fail by name instead of quietly
/// making a network call on the cold-start path.
/// </para>
/// <para>
/// Hand-written on the grounds every fake in this repository is: there is no mocking library here,
/// and the in-memory fakes live in adapter projects this suite deliberately does not reference.
/// </para>
/// </remarks>
internal static class StubWireSeams
{
    /// <summary>A wire half whose every call refuses.</summary>
    internal static ClientWireSeams Unreached() =>
        new(new StubGameApi(), new StubContentDistribution(), []);
}

/// <summary>A wire seam that is composed over and never called.</summary>
internal class StubGameApi : IGameApiPort
{
    private const string Unreached =
        "the composition root reached the network while building the graph. Composing is wiring: " +
        "it opens no session and reads no run, because the cold-start path must not wait on a " +
        "server that may not be there.";

    /// <inheritdoc/>
    public Task<WireDeviceRegistration> RegisterDeviceAsync(string? displayName, CancellationToken ct) =>
        throw new NotSupportedException(Unreached);

    /// <inheritdoc/>
    public Task<WireSession> AuthenticateAsync(WireCredentials credentials, CancellationToken ct) =>
        throw new NotSupportedException(Unreached);

    /// <inheritdoc/>
    public Task<WireCommandResult> SendCommandAsync(
        RunId? run, CommandEnvelope envelope, CancellationToken ct) =>
        throw new NotSupportedException(Unreached);

    /// <inheritdoc/>
    public Task<WireRunState> FetchRunStateAsync(RunId run, long sinceSequence, CancellationToken ct) =>
        throw new NotSupportedException(Unreached);
}

/// <summary>The content seam's counterpart of <see cref="StubGameApi"/>: composed, never called.</summary>
internal class StubContentDistribution : IContentDistributionClient
{
    private const string Unreached =
        "the composition root asked the server for content while building the graph. The sync is a " +
        "boot STAGE with a screen behind it, not part of wiring.";

    /// <inheritdoc/>
    public Task<string> FetchCurrentVersionAsync(CancellationToken ct) =>
        throw new NotSupportedException(Unreached);

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct) =>
        throw new NotSupportedException(Unreached);
}
