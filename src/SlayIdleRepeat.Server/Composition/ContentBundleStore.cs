using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The file-backed shelf of published content bundles behind <c>GET /content/{version}</c>: one
/// gzip per version, named for the stamp it carries.
/// </summary>
/// <remarks>
/// <para>
/// A composition-root type with no port, on the <see cref="RemoteConfigSource"/> precedent. The
/// application never asks for "a bundle store"; it asks for the bytes of one version, and this
/// process happens to keep them on its own disk. A port would name an infrastructure boundary that
/// is not there and put a directory of files in the catalogue every adapter is checked against.
/// </para>
/// <para>
/// The file name IS the stamp — <c>&lt;64 hex&gt;.bundle.gz</c> — so a directory listing is the
/// inventory and no index file can disagree with what is actually on the shelf. A name that is not
/// 64 lowercase hex is not a bundle this store owns.
/// </para>
/// <para>
/// 🔴 A null, empty or unwritable root DISABLES retention entirely: only the current version can be
/// served, so a client pinned to an older one has nothing to fetch and must re-sync. That is a
/// degraded mode, not a default, and it is announced exactly once through the warn sink in a line
/// carrying the greppable <c>[content-bundles]</c> marker — absent, greppable, loud. A silent
/// fallback here would look identical to a healthy server right up until an old pin needed a bundle
/// that was never written.
/// </para>
/// </remarks>
public sealed class ContentBundleStore
{
    private const string Marker = "[content-bundles]";

    private const string BundleSuffix = ".bundle.gz";

    /// <summary>Creates the store over the configured bundle root.</summary>
    /// <param name="bundleRoot">The directory bundles are written to, or null/empty when unset.</param>
    /// <param name="current">The snapshot this process is serving, which <see cref="Publish"/> writes out.</param>
    /// <param name="warn">The loud-log seam a disabled shelf is announced through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> or <paramref name="warn"/> is null.</exception>
    public ContentBundleStore(string? bundleRoot, ContentSnapshot current, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(warn);

        BundleRoot = bundleRoot;
        Current = current;
        Warn = warn;
    }

    private string? BundleRoot { get; }

    private ContentSnapshot Current { get; }

    private Action<string> Warn { get; }

    private readonly object _shelfGate = new();

    private byte[]? _currentBundle;

    private bool _announced;

    /// <summary>Writes the current snapshot's bundle to the shelf, if there is a shelf.</summary>
    /// <remarks>Idempotent: republishing a version already on the shelf leaves its bytes alone.</remarks>
    public void Publish()
    {
        if (Shelf() is not { } root)
        {
            return;
        }

        var destination = Path.Combine(root, Current.Version.Value + BundleSuffix);
        if (File.Exists(destination))
        {
            // Not "the bytes match": the file's write time is what a sweep reads, so rewriting an
            // identical bundle would silently restart every version's retention clock on restart.
            return;
        }

        // Written aside and moved: a reader is served a file name it can only find complete, never
        // a half-written stream whose hash would not check out.
        var staging = destination + ".partial";

        try
        {
            File.WriteAllBytes(staging, CurrentBundle());
            File.Move(staging, destination, overwrite: true);
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 🔒 The same degradation Shelf() applies, for the same stated reason. Shelf() only
            // covers CreateDirectory: a root that exists and is creatable but is full, read-only at
            // the file level, or being written by a second instance faults HERE — and this runs
            // inside the backbone's construction, so an unguarded throw makes every request on the
            // process answer 500 until somebody fixes the disk. That is precisely what this type
            // says a misconfigured volume must not be able to do.
            Announce(
                "the current bundle could not be published to '" + BundleRoot + "' (" + fault.Message +
                "). The current version is still servable from memory; nothing is retained.");
        }
    }

    /// <summary>The bytes of one retained version, or <c>null</c> when the shelf does not hold it.</summary>
    /// <param name="version">The requested stamp.</param>
    /// <returns>The gzip bundle, or <c>null</c> — absence is an answer here, never an exception.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is null.</exception>
    public ReadOnlyMemory<byte>? TryRead(ContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        // The current version is rendered from the loaded snapshot, so it is servable on a host
        // with no shelf at all — which is what keeps a misconfigured deployment merely unable to
        // serve HISTORY rather than unable to serve content.
        if (version.Equals(Current.Version))
        {
            return CurrentBundle();
        }

        if (Shelf() is not { } root)
        {
            return null;
        }

        var file = Path.Combine(root, version.Value + BundleSuffix);

        // The cast is load-bearing. Without it the conditional's natural type is byte[], and a null
        // byte[] converts to an EMPTY ReadOnlyMemory rather than to no value at all — so "the shelf
        // does not hold this" would arrive at the endpoint as a zero-byte bundle served with a 200.
        try
        {
            return File.Exists(file) ? File.ReadAllBytes(file) : (ReadOnlyMemory<byte>?)null;
        }
        catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
        {
            // Sweep deletes on one thread while requests read on others, so the file can go between
            // the Exists and the read. Absence is this method's documented answer, and the two
            // causes of it are meant to be indistinguishable 404s — a race that answered 500 would
            // make them distinguishable, and by timing rather than by anything the caller did.
            return null;
        }
    }

    /// <summary>Every version on the shelf, stamped with the file's last write time.</summary>
    /// <returns>The inventory a sweep is handed, ordinal-sorted by stamp.</returns>
    public IReadOnlyList<RetainedVersion> ListStored()
    {
        if (Shelf() is not { } root)
        {
            return [];
        }

        var found = new List<RetainedVersion>();

        foreach (var file in Directory.EnumerateFiles(root, "*" + BundleSuffix))
        {
            var name = Path.GetFileName(file);
            var stamp = name[..^BundleSuffix.Length];

            // The file name IS the stamp, so anything that does not parse as one is not a bundle
            // this store owns — an operator's note, a half-written .partial, a foreign artefact.
            if (ContentVersion.TryFromHex(stamp, out var version))
            {
                found.Add(new RetainedVersion(version!, new DateTimeOffset(File.GetLastWriteTimeUtc(file))));
            }
        }

        return found.OrderBy(r => r.Version.Value, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Deletes the bundles <see cref="ContentRetention.Sweep"/> says are past the window.</summary>
    /// <param name="nowUtc">The moment the sweep runs, supplied by the caller.</param>
    /// <param name="liveReferences">Versions something is still pinned to. Never deleted, whatever their age.</param>
    /// <exception cref="ArgumentNullException"><paramref name="liveReferences"/> is null.</exception>
    public void Sweep(DateTimeOffset nowUtc, IReadOnlyCollection<ContentVersion> liveReferences)
    {
        ArgumentNullException.ThrowIfNull(liveReferences);

        if (Shelf() is not { } root)
        {
            return;
        }

        // A live reference is restamped to now before the policy runs, so "still pinned" and
        // "recently written" reach the rule as the same fact and the rule stays a pure function of
        // its inventory.
        var live = liveReferences.Select(version => new RetainedVersion(version, nowUtc));

        var inventory = ListStored()
            .Where(stored => !liveReferences.Any(reference => reference.Equals(stored.Version)))
            .Concat(live)
            .ToArray();

        foreach (var version in ContentRetention.Sweep(
                     nowUtc, Current.Version, inventory, ContentRetention.WindowAlignedToRunTtl))
        {
            try
            {
                File.Delete(Path.Combine(root, version.Value + BundleSuffix));
            }
            catch (Exception fault) when (fault is IOException or UnauthorizedAccessException)
            {
                // Retention is bookkeeping. A file that will not delete — held open by a concurrent
                // read, or on a volume that has gone read-only — must leave the rest of the sweep
                // running and the server serving, not abort the pass on its first refusal.
                Announce(
                    "version " + version.Short + " could not be swept from '" + BundleRoot + "' (" +
                    fault.Message + "). It stays on the shelf and remains servable.");
            }
        }
    }

    /// <summary>The current snapshot's bundle, packed once and kept.</summary>
    private byte[] CurrentBundle()
    {
        lock (_shelfGate)
        {
            return _currentBundle ??=
                ContentBundle.Pack(Current.DocumentPaths.Select(Current.GetDocument));
        }
    }

    /// <summary>The shelf directory, or <c>null</c> when there is none — announced exactly once.</summary>
    /// <remarks>
    /// An unusable root degrades exactly like an unset one rather than faulting. A misconfigured or
    /// unmounted volume must not be able to take the whole API down: content the process already
    /// holds is still servable, and losing HISTORY is a smaller failure than losing the server.
    /// </remarks>
    private string? Shelf()
    {
        if (!string.IsNullOrWhiteSpace(BundleRoot))
        {
            try
            {
                Directory.CreateDirectory(BundleRoot);
                return BundleRoot;
            }
            catch (Exception fault) when (fault is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Announce(
                    "the bundle root '" + BundleRoot + "' cannot be used (" + fault.Message + "), so " +
                    "no content version but the current one can be served and nothing is retained. " +
                    "Point Content__BundleRoot at a writable directory.");
                return null;
            }
        }

        Announce(
            "no bundle root is configured, so no content version but the current one can be served " +
            "and nothing is retained across a deploy. A client pinned to an older version has " +
            "nothing to fetch and must re-sync onto current. Set Content__BundleRoot to a writable " +
            "directory to turn retention on.");

        return null;
    }

    /// <summary>Says the shelf is unusable, once for the life of this store.</summary>
    /// <remarks>Once, not per call: a warning on every request is a log flood, which is a signal nobody reads.</remarks>
    private void Announce(string why)
    {
        lock (_shelfGate)
        {
            if (_announced)
            {
                return;
            }

            _announced = true;
        }

        Warn(Marker + " " + why);
    }
}
