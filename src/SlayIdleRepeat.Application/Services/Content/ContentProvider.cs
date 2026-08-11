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
    /// <summary>Loads the initial snapshot. Throws <see cref="ContentLoadException"/> if invalid.</summary>
    public ContentProvider(IContentSourcePort source, ContentLoadOptions options, ContentReloadPolicy reloadPolicy) =>
        throw new NotImplementedException();

    /// <summary>The live snapshot. Never null, never mutated.</summary>
    public ContentSnapshot Current => throw new NotImplementedException();

    /// <summary>Whether this host may reload.</summary>
    public ContentReloadPolicy ReloadPolicy => throw new NotImplementedException();

    /// <summary>The source revision <see cref="Current"/> was built from.</summary>
    public string LoadedRevision => throw new NotImplementedException();

    /// <summary>Rebuilds from the source and swaps in the new snapshot, which it returns.</summary>
    public ContentSnapshot Reload() => throw new NotImplementedException();

    /// <summary>Reloads only when the source revision moved. False means nothing changed.</summary>
    public bool TryReloadIfChanged(out ContentSnapshot snapshot) => throw new NotImplementedException();
}
