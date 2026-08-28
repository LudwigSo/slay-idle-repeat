using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the content sync has got. Every value is a state the player can be looking at.</summary>
public enum SyncState
{
    /// <summary>Asking the server which content set it is serving.</summary>
    Checking = 1,

    /// <summary>The installed content is the server's. Nothing to download.</summary>
    UpToDate = 2,

    /// <summary>Pulling the bundle the server named.</summary>
    Downloading = 3,

    /// <summary>Re-stamping the downloaded bundle before believing a byte of it.</summary>
    Verifying = 4,

    /// <summary>A verified snapshot is in hand and the app can swap onto it.</summary>
    Updated = 5,

    /// <summary>The sync stopped. Why is carried by <see cref="ContentSyncFailure.Kind"/>.</summary>
    Failed = 6,
}

/// <summary>Why a sync did not finish — one name per cause, because the three are fixed by different people.</summary>
public enum ContentSyncFailureKind
{
    /// <summary>The server could not be reached, or would not answer. Ops or the player's network.</summary>
    Unreachable = 1,

    /// <summary>Bytes arrived and did not re-stamp to the version they were served under. Distribution.</summary>
    BundleRejected = 2,

    /// <summary>The pointer document named something that is not a stamp at all. A server-side defect.</summary>
    VersionMalformed = 3,
}

/// <summary>One sync failure, identified: what kind it was and what actually went wrong.</summary>
/// <param name="Kind">Which of the three causes this was.</param>
/// <param name="Detail">What went wrong, in words that name this failure rather than failure in general.</param>
public sealed record ContentSyncFailure(ContentSyncFailureKind Kind, string Detail)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Kind} — {Detail}";
}

/// <summary>
/// Drives the content sync: check, download, verify, then Updated — or a named failure.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole sequence runs under a
/// test runner with no engine anywhere near it. The scene above renders <see cref="State"/> and
/// forwards nothing; the composition root decides what the client actually is.
/// </para>
/// <para>
/// 🔒 A failure is a state, never an escape. A sync that threw would leave a screen that never
/// resolves, which is the one outcome worse than saying what broke — and this runs at boot, where
/// there is no earlier screen to fall back to.
/// </para>
/// <para>
/// 🔒 The downloaded bundle is re-stamped rather than trusted. A stamp that came from the same
/// place as the bytes proves nothing about them; only recomputing it over the unpacked documents
/// distinguishes "the content set the server named" from "whatever the network handed us".
/// </para>
/// </remarks>
public sealed class ContentSyncPresenter
{
    /// <summary>Wires the distribution client and the content this installation already carries.</summary>
    /// <param name="client">The two-call seam the sync speaks through.</param>
    /// <param name="installed">The snapshot the app booted on, whose version is compared with the server's.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public ContentSyncPresenter(IContentDistributionClient client, ContentSnapshot installed)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(installed);

        Client = client;
        Installed = installed;
        State = SyncState.Checking;
    }

    private IContentDistributionClient Client { get; }

    private ContentSnapshot Installed { get; }

    /// <summary>How far the sync has got.</summary>
    public SyncState State { get; private set; }

    /// <summary>Why it stopped, or <c>null</c> while it has not.</summary>
    public ContentSyncFailure? Failure { get; private set; }

    /// <summary>The verified snapshot the sync fetched, or <c>null</c> when nothing was downloaded.</summary>
    /// <remarks>Set only in <see cref="SyncState.Updated"/> — never alongside a failure, and never half-verified.</remarks>
    public ContentSnapshot? Downloaded { get; private set; }

    /// <summary>Runs the whole sequence, ending in <see cref="SyncState.UpToDate"/>, <see cref="SyncState.Updated"/> or <see cref="SyncState.Failed"/>.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task RunAsync(CancellationToken ct)
    {
        State = SyncState.Checking;

        string named;
        try
        {
            named = await Client.FetchCurrentVersionAsync(ct).ConfigureAwait(false);
        }
        catch (Exception fault) when (fault is not OperationCanceledException)
        {
            Stop(ContentSyncFailureKind.Unreachable, "the content pointer could not be read — " + fault.Message);
            return;
        }

        if (!ContentVersion.TryFromHex(named, out var served))
        {
            Stop(
                ContentSyncFailureKind.VersionMalformed,
                "the server named " + (named.Length == 0 ? "an empty stamp" : "'" + named + "'") +
                ", which is not the 64 lowercase hex characters a content stamp is");
            return;
        }

        if (served!.Equals(Installed.Version))
        {
            State = SyncState.UpToDate;
            return;
        }

        State = SyncState.Downloading;

        ReadOnlyMemory<byte> bundle;
        try
        {
            bundle = await Client.FetchBundleAsync(served.Value, ct).ConfigureAwait(false);
        }
        catch (Exception fault) when (fault is not OperationCanceledException)
        {
            // A download that never arrived is not a rejected bundle: one is ops or the player's
            // network, the other is the distribution pipeline, and they are fixed by different
            // people.
            Stop(ContentSyncFailureKind.Unreachable, "the bundle could not be fetched — " + fault.Message);
            return;
        }

        State = SyncState.Verifying;

        try
        {
            // Re-stamped, never trusted. The stamp arrived from the same place as the bytes, so
            // only recomputing it over the unpacked documents distinguishes the content set the
            // server named from whatever the network handed us.
            Downloaded = ContentBundle.Open(bundle, served);
        }
        catch (ContentBundleFormatException fault)
        {
            Stop(ContentSyncFailureKind.BundleRejected, fault.Message);
            return;
        }

        State = SyncState.Updated;
    }

    /// <summary>Ends the sync in a named failure, leaving nothing half-verified behind.</summary>
    private void Stop(ContentSyncFailureKind kind, string detail)
    {
        Downloaded = null;
        Failure = new ContentSyncFailure(kind, detail);
        State = SyncState.Failed;
    }
}
