using SlayIdleRepeat.Application.Services.Content;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The shelf of published bundles: what it publishes, what it hands back, what it sweeps, and what
/// it does when there is no shelf at all.
/// </summary>
/// <remarks>
/// The disabled-shelf cases matter as much as the healthy ones. A store that quietly served only
/// the current version would look identical to a working one until the first client pinned to an
/// older stamp asked for a bundle that was never written — which is exactly the failure retention
/// exists to prevent.
/// </remarks>
public sealed class ContentBundleStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "sir-bundles-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _warnings = [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ContentSnapshot Snapshot(string path, decimal value)
    {
        var documents = new[]
        {
            new ContentDocument(
                path,
                ContentValue.Object([new KeyValuePair<string, ContentValue>("x", ContentValue.Number(value))])),
        };

        return new ContentSnapshot(ContentHashing.Compute(documents), documents);
    }

    private static ContentSnapshot Current => Snapshot("tuning/a.json", 1m);

    private static ContentSnapshot Older => Snapshot("tuning/a.json", 2m);

    private ContentBundleStore Store(ContentSnapshot current, string? root) =>
        new(root, current, _warnings.Add);

    /// <summary>Puts a version on the shelf by hand, stamped with the write time a sweep will read.</summary>
    private void Shelve(ContentSnapshot snapshot, DateTimeOffset writtenAtUtc)
    {
        Directory.CreateDirectory(_root);

        var file = Path.Combine(_root, snapshot.Version.Value + ".bundle.gz");
        File.WriteAllBytes(
            file, ContentBundle.Pack(snapshot.DocumentPaths.Select(snapshot.GetDocument)));
        File.SetLastWriteTimeUtc(file, writtenAtUtc.UtcDateTime);
    }

    // ------------------------------------------------------------------ C7

    [Fact]
    public void Publish_writes_the_current_bundle_under_its_own_stamp()
    {
        var current = Current;

        Store(current, _root).Publish();

        var written = Path.Combine(_root, current.Version.Value + ".bundle.gz");

        File.Exists(written).ShouldBeTrue(
            "the file name IS the stamp, so a directory listing is the inventory");
        ContentBundle.Open(File.ReadAllBytes(written), current.Version).Version.ShouldBe(current.Version);
    }

    [Fact]
    public void Publish_leaves_a_bundle_that_is_already_on_the_shelf_exactly_as_it_was()
    {
        var current = Current;
        var store = Store(current, _root);

        store.Publish();

        var written = Path.Combine(_root, current.Version.Value + ".bundle.gz");
        var stampedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(written, stampedAt.UtcDateTime);

        store.Publish();

        // Not just "the bytes are the same": the write time is what a sweep reads, so a republish
        // that rewrote the file would silently refresh every bundle's retention clock on restart.
        File.GetLastWriteTimeUtc(written).ShouldBe(stampedAt.UtcDateTime);
    }

    [Fact]
    public void A_published_version_reads_back_as_the_bundle_it_published()
    {
        var current = Current;
        var store = Store(current, _root);
        store.Publish();

        var read = store.TryRead(current.Version);

        read.ShouldNotBeNull();
        ContentBundle.Open(read!.Value, current.Version).Version.ShouldBe(current.Version);
    }

    [Fact]
    public void An_older_version_the_shelf_holds_reads_back_even_though_it_is_not_current()
    {
        var current = Current;
        var older = Older;
        Shelve(older, DateTimeOffset.UnixEpoch);

        var read = Store(current, _root).TryRead(older.Version);

        read.ShouldNotBeNull();
        ContentBundle.Open(read!.Value, older.Version).Version.ShouldBe(older.Version);
    }

    [Fact]
    public void A_version_the_shelf_does_not_hold_answers_absent_rather_than_throwing()
    {
        Store(Current, _root).Publish();

        Store(Current, _root).TryRead(Older.Version).ShouldBeNull(
            "absence is an answer here — the endpoint turns it into a 404");
    }

    [Fact]
    public void The_inventory_names_every_shelved_stamp_once_ordinal_sorted()
    {
        var current = Current;
        var older = Older;
        Shelve(older, DateTimeOffset.UnixEpoch);

        var store = Store(current, _root);
        store.Publish();

        var stamps = store.ListStored().Select(r => r.Version.Value).ToArray();

        stamps.ShouldBe(
            new[] { current.Version.Value, older.Version.Value }.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_inventory_carries_each_bundles_own_write_time()
    {
        var older = Older;
        var writtenAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        Shelve(older, writtenAt);

        var found = Store(Current, _root).ListStored().Single(r => r.Version.Equals(older.Version));

        found.LastReferencedAtUtc.UtcDateTime.ShouldBe(writtenAt.UtcDateTime);
    }

    [Fact]
    public void A_file_in_the_root_that_is_not_a_bundle_is_not_in_the_inventory()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "README.md"), "not a bundle");
        File.WriteAllText(Path.Combine(_root, "deadbeef.bundle.gz"), "stamp too short");
        File.WriteAllText(
            Path.Combine(_root, new string('A', ContentVersion.HexLength) + ".bundle.gz"), "upper case");

        var store = Store(Current, _root);
        store.Publish();

        store.ListStored().Select(r => r.Version.Value).ShouldBe([Current.Version.Value]);
    }

    // ------------------------------------------------------------------ C7 · sweep

    [Fact]
    public void A_sweep_deletes_a_bundle_whose_last_reference_is_past_the_window()
    {
        var older = Older;
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Shelve(older, now - ContentRetention.WindowAlignedToRunTtl - TimeSpan.FromTicks(1));

        var store = Store(Current, _root);
        store.Publish();
        store.Sweep(now, []);

        store.TryRead(older.Version).ShouldBeNull();
    }

    [Fact]
    public void A_sweep_keeps_a_bundle_inside_the_window()
    {
        var older = Older;
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Shelve(older, now - TimeSpan.FromHours(1));

        var store = Store(Current, _root);
        store.Publish();
        store.Sweep(now, []);

        store.TryRead(older.Version).ShouldNotBeNull();
    }

    [Fact]
    public void A_sweep_keeps_a_bundle_something_is_still_pinned_to_however_old_it_is()
    {
        var older = Older;
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Shelve(older, DateTimeOffset.UnixEpoch);

        var store = Store(Current, _root);
        store.Publish();
        store.Sweep(now, [older.Version]);

        store.TryRead(older.Version).ShouldNotBeNull(
            "a live run pinned to this stamp must still be able to fetch what it was played against");
    }

    [Fact]
    public void A_sweep_never_deletes_the_version_this_process_is_serving()
    {
        var current = Current;
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var store = Store(current, _root);
        store.Publish();
        File.SetLastWriteTimeUtc(
            Path.Combine(_root, current.Version.Value + ".bundle.gz"), DateTimeOffset.UnixEpoch.UtcDateTime);

        store.Sweep(now, []);

        File.Exists(Path.Combine(_root, current.Version.Value + ".bundle.gz")).ShouldBeTrue();
    }

    // ------------------------------------------------------------------ C8 · no shelf

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_root_that_was_never_configured_disables_retention_out_loud(string? root)
    {
        var current = Current;
        var store = Store(current, root);

        store.Publish();

        _warnings.Count.ShouldBe(1, "the degraded mode is announced, and announced once");
        _warnings[0].ShouldContain(
            "[content-bundles]",
            Case.Sensitive,
            "the marker is what makes this greppable in a container's log stream");
    }

    [Fact]
    public void A_disabled_shelf_says_so_once_however_often_it_is_used()
    {
        var store = Store(Current, root: null);

        store.Publish();
        store.Publish();
        store.TryRead(Older.Version);
        store.ListStored();
        store.Sweep(DateTimeOffset.UnixEpoch, []);

        _warnings.Count.ShouldBe(1, "a per-request warning is a log flood, not a signal");
    }

    [Fact]
    public void A_disabled_shelf_still_serves_the_version_this_process_is_serving()
    {
        var current = Current;
        var store = Store(current, root: null);

        var read = store.TryRead(current.Version);

        read.ShouldNotBeNull(
            "the current bundle is rendered from the loaded snapshot and needs no disk at all");
        ContentBundle.Open(read!.Value, current.Version).Version.ShouldBe(current.Version);
    }

    [Fact]
    public void A_disabled_shelf_holds_nothing_older_and_sweeps_nothing()
    {
        var store = Store(Current, root: null);

        store.TryRead(Older.Version).ShouldBeNull();
        store.ListStored().ShouldBeEmpty();

        // Not "does not throw" — the assertion is that a disabled shelf has nothing to lose, which
        // is what makes calling Sweep on every host safe.
        Should.NotThrow(() => store.Sweep(DateTimeOffset.UnixEpoch, []));
    }

    /// <summary>
    /// The negative control for the whole disabled block: a configured root warns about nothing.
    /// Without it, a store that warned unconditionally would satisfy every case above.
    /// </summary>
    [Fact]
    public void A_configured_root_announces_nothing()
    {
        var store = Store(Current, _root);

        store.Publish();
        store.TryRead(Current.Version);
        store.ListStored();
        store.Sweep(DateTimeOffset.UnixEpoch, []);

        _warnings.ShouldBeEmpty();
    }

    /// <summary>
    /// An unusable root degrades exactly like an unset one. A misconfigured or unmounted volume must
    /// not be able to take the API down with it — losing retained history is a far smaller failure
    /// than losing the server, and the current version needs no disk at all.
    /// </summary>
    [Fact]
    public void A_root_that_cannot_be_used_degrades_instead_of_faulting()
    {
        // A file where a directory is wanted: Directory.CreateDirectory refuses it on every
        // platform, and no privilege or platform-specific ACL is needed to set it up.
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "occupied");
        File.WriteAllText(blocker, "not a directory");

        var current = Current;
        var store = Store(current, Path.Combine(blocker, "bundles"));

        Should.NotThrow(() => store.Publish());

        store.TryRead(current.Version).ShouldNotBeNull("the current bundle never needed the shelf");
        store.TryRead(Older.Version).ShouldBeNull();
        store.ListStored().ShouldBeEmpty();

        _warnings.Count.ShouldBe(1);
        _warnings[0].ShouldContain("[content-bundles]", Case.Sensitive);
        _warnings[0].ShouldContain(
            blocker, Case.Sensitive, "the line names the root that could not be used, not just that one could not");
    }

    [Fact]
    public void A_configured_root_that_does_not_exist_yet_is_created_rather_than_refused()
    {
        var nested = Path.Combine(_root, "deep", "deeper");

        Store(Current, nested).Publish();

        File.Exists(Path.Combine(nested, Current.Version.Value + ".bundle.gz")).ShouldBeTrue();
        _warnings.ShouldBeEmpty("a first boot on an empty volume is the normal case, not a fault");
    }
}
