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
    private const string NothingScripted = "A silent seam answers nothing at all.";

    private readonly Func<string> _version;
    private readonly Func<string, ReadOnlyMemory<byte>> _bundle;
    private readonly bool _silent;

    /// <summary>Answers the pointer and the bundle from the two functions given.</summary>
    internal ScriptedContentClient(Func<string> version, Func<string, ReadOnlyMemory<byte>> bundle)
    {
        _version = version;
        _bundle = bundle;
    }

    private ScriptedContentClient()
    {
        _version = () => throw new NotSupportedException(NothingScripted);
        _bundle = _ => throw new NotSupportedException(NothingScripted);
        _silent = true;
    }

    /// <summary>A seam that accepts the call and never answers it.</summary>
    /// <remarks>
    /// 🔒 A refused connection answers instantly, and every other arrangement here is written
    /// against one. This is the black-holed network, where what ends the call is whatever bound the
    /// caller put on it — which is the only way to observe that there is one.
    /// </remarks>
    internal static ScriptedContentClient Silent() => new();

    /// <summary>Reads the sync's state at call time, so the sequence of states is observable.</summary>
    internal Func<SyncState>? Observe { get; set; }

    /// <summary>The states observed at each call, in order.</summary>
    internal List<SyncState> StatesSeen { get; } = [];

    /// <summary>Every stamp a bundle was asked for, in order.</summary>
    internal List<string> BundlesAskedFor { get; } = [];

    /// <summary>
    /// How many pointer reads reached this seam, answered or not — so a case can say the sync stage
    /// actually ran rather than infer it from an outcome a skipped stage would also produce.
    /// </summary>
    internal int PointerReads { get; private set; }

    /// <summary>How many calls ended because their own token was cancelled.</summary>
    internal int CancelledCalls { get; private set; }

    /// <inheritdoc/>
    public Task<string> FetchCurrentVersionAsync(CancellationToken ct)
    {
        PointerReads++;
        Record();

        return _silent ? Silence<string>(ct) : Task.FromResult(_version());
    }

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct)
    {
        Record();
        BundlesAskedFor.Add(version);

        return _silent ? Silence<ReadOnlyMemory<byte>>(ct) : Task.FromResult(_bundle(version));
    }

    /// <summary>A call that never answers, ending only when its token is cancelled.</summary>
    private Task<T> Silence<T>(CancellationToken ct)
    {
        var pending = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        ct.Register(
            () =>
            {
                CancelledCalls++;

                pending.TrySetCanceled(ct);
            });

        return pending.Task;
    }

    private void Record()
    {
        if (Observe is { } observe)
        {
            StatesSeen.Add(observe());
        }
    }
}
