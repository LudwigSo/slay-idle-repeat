using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The boot-time content sync: compare, download, re-stamp, swap — or stop on a named failure.
/// </summary>
/// <remarks>
/// The verification case is the one that matters. A stamp that arrived from the same place as the
/// bytes proves nothing about them, so the presenter recomputes it over the unpacked documents; a
/// bundle that does not re-stamp is refused and the installed content is kept.
/// </remarks>
public sealed class ContentSyncPresenterTests
{
    private static ContentSnapshot Snapshot(decimal value)
    {
        var documents = new[]
        {
            new ContentDocument(
                "tuning/a.json",
                ContentValue.Object([new KeyValuePair<string, ContentValue>("x", ContentValue.Number(value))])),
        };

        return new ContentSnapshot(ContentHashing.Compute(documents), documents);
    }

    private static byte[] Bundle(ContentSnapshot snapshot) =>
        ContentBundle.Pack(snapshot.DocumentPaths.Select(snapshot.GetDocument));

    private static (ContentSyncPresenter Presenter, ScriptedContentClient Client) Wire(
        ContentSnapshot installed,
        Func<string> version,
        Func<string, ReadOnlyMemory<byte>>? bundle = null)
    {
        var client = new ScriptedContentClient(
            version,
            bundle ?? (_ => throw new InvalidOperationException("no bundle was scripted for this case")));

        var presenter = new ContentSyncPresenter(client, installed);
        client.Observe = () => presenter.State;

        return (presenter, client);
    }

    // ------------------------------------------------------------------ up to date

    [Fact]
    public async Task Content_that_already_matches_the_server_downloads_nothing()
    {
        var installed = Snapshot(1m);
        var (presenter, client) = Wire(installed, () => installed.Version.Value);

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.UpToDate);
        presenter.Failure.ShouldBeNull();
        presenter.Downloaded.ShouldBeNull("nothing was fetched, so there is nothing to swap onto");
        client.BundlesAskedFor.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_check_runs_before_anything_else_is_decided()
    {
        var installed = Snapshot(1m);
        var (presenter, client) = Wire(installed, () => installed.Version.Value);

        await presenter.RunAsync(CancellationToken.None);

        client.StatesSeen.ShouldBe([SyncState.Checking]);
    }

    // ------------------------------------------------------------------ download and verify

    [Fact]
    public async Task A_server_serving_different_content_is_downloaded_verified_and_handed_over()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, client) = Wire(
            installed, () => served.Version.Value, _ => Bundle(served));

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Updated);
        presenter.Failure.ShouldBeNull();
        presenter.Downloaded.ShouldNotBeNull();
        presenter.Downloaded.Version.ShouldBe(served.Version);
        client.BundlesAskedFor.ShouldBe([served.Version.Value]);
    }

    [Fact]
    public async Task The_handed_over_snapshot_carries_the_documents_the_bundle_did()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, _) = Wire(installed, () => served.Version.Value, _ => Bundle(served));

        await presenter.RunAsync(CancellationToken.None);

        var downloaded = presenter.Downloaded.ShouldNotBeNull();

        // The stamp alone could be carried by an empty snapshot; the documents are what a run is
        // played against.
        downloaded.DocumentPaths.ShouldBe(served.DocumentPaths);
        ContentHashing.Compute(downloaded.DocumentPaths.Select(downloaded.GetDocument))
            .ShouldBe(served.Version);
    }

    [Fact]
    public async Task The_download_is_shown_as_downloading_and_the_re_stamp_as_verifying()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, client) = Wire(installed, () => served.Version.Value, _ => Bundle(served));

        await presenter.RunAsync(CancellationToken.None);

        client.StatesSeen.ShouldBe([SyncState.Checking, SyncState.Downloading]);
        presenter.State.ShouldBe(SyncState.Updated, "Verifying is passed through, never rested in");
    }

    // ------------------------------------------------------------------ named failures

    [Fact]
    public async Task A_server_that_cannot_be_reached_for_the_pointer_fails_as_unreachable()
    {
        var installed = Snapshot(1m);

        var (presenter, _) = Wire(
            installed, () => throw new HttpRequestException("the socket said no"));

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Failed);
        presenter.Failure.ShouldNotBeNull().Kind.ShouldBe(ContentSyncFailureKind.Unreachable);
        presenter.Downloaded.ShouldBeNull();
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_for_the_bundle_fails_as_unreachable()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, _) = Wire(
            installed,
            () => served.Version.Value,
            _ => throw new HttpRequestException("the socket said no"));

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Failed);
        presenter.Failure.ShouldNotBeNull().Kind.ShouldBe(
            ContentSyncFailureKind.Unreachable,
            "a download that never arrived is not a rejected bundle — the two are fixed by "
            + "different people");
    }

    [Theory]
    [InlineData("")]
    [InlineData("deadbeef")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_pointer_that_names_something_that_is_not_a_stamp_fails_as_malformed(string named)
    {
        var installed = Snapshot(1m);
        var (presenter, client) = Wire(installed, () => named);

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Failed);
        presenter.Failure.ShouldNotBeNull().Kind.ShouldBe(ContentSyncFailureKind.VersionMalformed);
        presenter.Failure.Detail.ShouldContain(
            named.Length == 0 ? "empty" : named,
            Case.Sensitive,
            "the failure names what the server actually said, not that a failure occurred");
        client.BundlesAskedFor.ShouldBeEmpty("text that is not a stamp is never turned into a request");
    }

    [Fact]
    public async Task A_bundle_that_does_not_re_stamp_to_the_version_it_was_served_under_is_refused()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);
        var impostor = Snapshot(3m);

        // The server names one stamp and hands over another content set's bytes — the exact shape a
        // cache poisoning or a truncated CDN response takes.
        var (presenter, _) = Wire(
            installed, () => served.Version.Value, _ => Bundle(impostor));

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Failed);
        presenter.Failure.ShouldNotBeNull().Kind.ShouldBe(ContentSyncFailureKind.BundleRejected);
        presenter.Downloaded.ShouldBeNull("a snapshot that failed verification is never handed over");
    }

    [Fact]
    public async Task Bytes_that_are_not_a_bundle_at_all_are_refused_as_a_rejected_bundle()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, _) = Wire(
            installed, () => served.Version.Value, _ => new byte[] { 1, 2, 3, 4 });

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Failed);
        presenter.Failure.ShouldNotBeNull().Kind.ShouldBe(ContentSyncFailureKind.BundleRejected);
    }

    /// <summary>
    /// A failure is a state, never an escape: this runs at boot, where a thrown exception leaves a
    /// screen that never resolves and no earlier screen to fall back to.
    /// </summary>
    [Fact]
    public async Task No_failure_path_throws_out_of_the_sync()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var cases = new Func<Task<ContentSyncPresenter>>[]
        {
            async () =>
            {
                var (p, _) = Wire(installed, () => throw new HttpRequestException("down"));
                await p.RunAsync(CancellationToken.None);
                return p;
            },
            async () =>
            {
                var (p, _) = Wire(installed, () => "not a stamp");
                await p.RunAsync(CancellationToken.None);
                return p;
            },
            async () =>
            {
                var (p, _) = Wire(installed, () => served.Version.Value, _ => new byte[] { 9 });
                await p.RunAsync(CancellationToken.None);
                return p;
            },
        };

        // A floor over the arms, so a refactor that collapsed two failure kinds into one cannot
        // leave this rule quantifying over a shorter list while still reporting success.
        cases.Length.ShouldBe(3);

        foreach (var arm in cases)
        {
            var presenter = await arm();
            presenter.State.ShouldBe(SyncState.Failed);
            presenter.Failure.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// The negative control for every failure case above: the same wiring, a healthy server. Without
    /// it, a presenter that failed unconditionally would satisfy the whole failure block.
    /// </summary>
    [Fact]
    public async Task The_healthy_path_reaches_Updated_with_no_failure_recorded()
    {
        var installed = Snapshot(1m);
        var served = Snapshot(2m);

        var (presenter, _) = Wire(installed, () => served.Version.Value, _ => Bundle(served));

        await presenter.RunAsync(CancellationToken.None);

        presenter.State.ShouldBe(SyncState.Updated);
        presenter.Failure.ShouldBeNull();
    }

    [Fact]
    public void A_sync_that_has_not_run_is_checking_and_has_nothing_to_report()
    {
        var installed = Snapshot(1m);
        var (presenter, _) = Wire(installed, () => installed.Version.Value);

        presenter.State.ShouldBe(SyncState.Checking);
        presenter.Failure.ShouldBeNull();
        presenter.Downloaded.ShouldBeNull();
    }
}
