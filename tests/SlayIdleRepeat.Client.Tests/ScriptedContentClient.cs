using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The two-call content seam, scripted — and recording what the sync was showing when it was called.
/// </summary>
/// <remarks>
/// Hand-written on the grounds every fake in this repository is, and shared rather than nested so
/// the boot cases and the sync cases drive one mechanism: two would drift, and the boot's whole
/// claim is that it runs the same sync those cases pin.
/// </remarks>
internal sealed class ScriptedContentClient : IContentDistributionClient
{
    private readonly Func<string> _version;
    private readonly Func<string, ReadOnlyMemory<byte>> _bundle;

    /// <summary>Answers the pointer and the bundle from the two functions given.</summary>
    internal ScriptedContentClient(Func<string> version, Func<string, ReadOnlyMemory<byte>> bundle)
    {
        _version = version;
        _bundle = bundle;
    }

    /// <summary>Reads the sync's state at call time, so the sequence of states is observable.</summary>
    internal Func<SyncState>? Observe { get; set; }

    /// <summary>The states observed at each call, in order.</summary>
    internal List<SyncState> StatesSeen { get; } = [];

    /// <summary>Every stamp a bundle was asked for, in order.</summary>
    internal List<string> BundlesAskedFor { get; } = [];

    /// <inheritdoc/>
    public Task<string> FetchCurrentVersionAsync(CancellationToken ct)
    {
        Record();

        return Task.FromResult(_version());
    }

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct)
    {
        Record();
        BundlesAskedFor.Add(version);

        return Task.FromResult(_bundle(version));
    }

    private void Record()
    {
        if (Observe is { } observe)
        {
            StatesSeen.Add(observe());
        }
    }
}
