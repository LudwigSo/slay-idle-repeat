namespace SlayIdleRepeat.Adapters.Api.Http;

/// <summary>The deployment contract of <see cref="HttpGameApi"/>: which server, and how long to wait.</summary>
/// <remarks>
/// <para>
/// A plain settable class rather than a record, because this is what a configuration binder fills
/// in — the same shape every other adapter's options carry. Every member has a non-null default so
/// a composition root that binds nothing still constructs, and the defaults are the local
/// development server rather than anything a shipped build could reach by accident.
/// </para>
/// <para>
/// Nothing about the protocol lives here. The routes, the dialect and the renewal rule are the
/// adapter's, and a deployment that could move them would be a deployment that could speak a
/// different protocol than the server it points at.
/// </para>
/// </remarks>
public sealed class HttpGameApiOptions
{
    /// <summary>Where the server is. Every route is resolved relative to it.</summary>
    /// <remarks>
    /// Trailing slash included: a relative route resolved against a base without one drops the
    /// last path segment, which is the classic way a versioned prefix silently disappears.
    /// </remarks>
    public Uri BaseAddress { get; set; } = new("http://localhost:8080/");

    /// <summary>
    /// How long one request may take before the transport gives up and the caller is told the
    /// connection is lost.
    /// </summary>
    /// <remarks>
    /// Well below the BCL's 100 second default on purpose: this port sits behind a connection
    /// indicator with a 2 second budget before it says anything at all, and a request that hangs
    /// for a minute and a half is a client that looks frozen rather than disconnected.
    /// </remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
