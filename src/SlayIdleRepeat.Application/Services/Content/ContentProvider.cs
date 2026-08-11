using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>Whether this host may rebuild its content without restarting.</summary>
/// <remarks>
/// `14` §6: <em>"In editor/dev builds, content hot-reloads without restarting."</em> Editor and
/// dev builds only — a shipped client or server reloads content by being handed a new package and
/// restarting, never by watching a directory.
/// </remarks>
public enum ContentReloadPolicy
{
    /// <summary>Shipping default: the snapshot this host booted with is the snapshot it dies with.</summary>
    Disabled = 0,

    /// <summary>Editor/dev builds: <see cref="ContentProvider.Reload"/> is permitted.</summary>
    Enabled = 1,
}

/// <summary>Raised when a host with <see cref="ContentReloadPolicy.Disabled"/> is asked to reload.</summary>
public sealed class ContentReloadNotPermittedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ContentReloadNotPermittedException()
        : base("Content hot-reload is an editor/dev-build affordance only (14 §6). This host was " +
               "constructed with ContentReloadPolicy.Disabled.")
    {
    }
}

/// <summary>
/// Holds the one live <c>ContentSnapshot</c> and, in editor/dev builds, replaces it.
/// </summary>
/// <remarks>
/// 🔒 <b>Reload never mutates.</b> <see cref="Reload"/> builds a whole new snapshot from the
/// source and swaps the reference; the previous snapshot is untouched and stays valid for anyone
/// still holding it. That is what keeps `14` §6's immutability promise true <em>through</em> a
/// hot-reload rather than only until the first one — a command mid-flight against the old
/// snapshot still replays against the content it actually ran on.
/// </remarks>
public sealed class ContentProvider
{
    private readonly IContentSourcePort _source;
    private readonly ContentLoadOptions _options;
    private readonly object _reloadGate = new();

    /// <summary>
    /// The snapshot and the source revision it was built from, published as one value.
    /// </summary>
    /// <remarks>
    /// 🔒 Two separate fields would be two separate publications: a reader could see the new
    /// snapshot beside the old revision — in which case <see cref="TryReloadIfChanged"/> reports
    /// "nothing changed" while <see cref="Current"/> is stale, and the dev never sees their edit.
    /// </remarks>
    private sealed record Loaded(ContentSnapshot Snapshot, string Revision);

    private Loaded _state;

    /// <summary>Loads the initial snapshot. Throws <see cref="ContentLoadException"/> if invalid.</summary>
    public ContentProvider(IContentSourcePort source, ContentLoadOptions options, ContentReloadPolicy reloadPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        _source = source;
        _options = options;
        ReloadPolicy = reloadPolicy;

        var revision = source.Revision;
        _state = new Loaded(ContentLoader.Load(source, options).Require(), revision);
    }

    /// <summary>The live snapshot. Never null, never mutated.</summary>
    public ContentSnapshot Current => Volatile.Read(ref _state).Snapshot;

    /// <summary>Whether this host may reload.</summary>
    public ContentReloadPolicy ReloadPolicy { get; }

    /// <summary>The source revision <see cref="Current"/> was built from.</summary>
    public string LoadedRevision => Volatile.Read(ref _state).Revision;

    /// <summary>Rebuilds from the source and swaps in the new snapshot, which it returns.</summary>
    /// <remarks>
    /// 🔒 The rebuild happens first and completely. If the edited content is invalid the exception
    /// leaves <see cref="Current"/> exactly as it was — a dev who saves a half-typed JSON file gets
    /// an error, not a game running on nothing.
    /// <para>
    /// Serialised: a file watcher and a manual reload can otherwise interleave so that the
    /// <em>older</em> rebuild wins the last write and the newer edit is silently discarded.
    /// </para>
    /// </remarks>
    public ContentSnapshot Reload()
    {
        if (ReloadPolicy != ContentReloadPolicy.Enabled)
        {
            throw new ContentReloadNotPermittedException();
        }

        lock (_reloadGate)
        {
            var revision = _source.Revision;
            var rebuilt = ContentLoader.Load(_source, _options).Require();

            Volatile.Write(ref _state, new Loaded(rebuilt, revision));
            return rebuilt;
        }
    }

    /// <summary>Reloads only when the source revision moved. False means nothing changed.</summary>
    /// <remarks>
    /// 🔒 Never throws on a host that may not reload. This is the method a watcher polls, and a
    /// <c>Try*</c> that throws on a shipping build is a crash waiting for the first content
    /// change — it reports "nothing to do", which is the truth for that host.
    /// </remarks>
    public bool TryReloadIfChanged(out ContentSnapshot snapshot)
    {
        if (ReloadPolicy != ContentReloadPolicy.Enabled)
        {
            snapshot = Current;
            return false;
        }

        lock (_reloadGate)
        {
            // Inside the gate: reading the revision and acting on it must be one decision, or two
            // callers both see "changed" and both rebuild.
            if (string.Equals(_source.Revision, LoadedRevision, StringComparison.Ordinal))
            {
                snapshot = Current;
                return false;
            }

            snapshot = Reload();
            return true;
        }
    }
}
