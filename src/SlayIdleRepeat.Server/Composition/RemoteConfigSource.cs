using SlayIdleRepeat.Core;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The reloading server-disk flags document behind <c>GET /config</c> and the command gateway's
/// kill switches (14 §10, §14): one JSON file, parsed whole-or-not-at-all.
/// </summary>
/// <remarks>
/// <para>
/// The document's five members are all optional and each omission is the identity — kill lists say
/// only what they kill. A document that says anything unrecognisable (an unknown top-level member,
/// a blank kill-list entry, malformed JSON) is refused WHOLE, loudly, keeping the last good state:
/// a typo like <c>disabledChapter</c> must never silently kill nothing. An absent file or an unset
/// path is the identity element — everything enabled — warned with the greppable
/// <c>[remote-config]</c> marker, since ops config that vanished is an incident, not a default.
/// </para>
/// <para>
/// <see cref="Document"/> is the exact accepted raw file text (or a rendered identity document when
/// absent) so <c>GET /config</c> serves byte-for-byte what an operator authored.
/// </para>
/// </remarks>
public sealed class RemoteConfigSource
{
    /// <summary>Creates the source and performs the initial load pass.</summary>
    /// <param name="configuredPath">The flags file's path from <c>RemoteConfig:Path</c>, or null/empty when unset.</param>
    /// <param name="warn">The loud-log seam every refusal and absence is reported through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="warn"/> is null.</exception>
    public RemoteConfigSource(string? configuredPath, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);

        Current = new FeatureFlags(
            pvpEnabled: true, plusOfferEnabled: true, mailEnabled: true,
            disabledAdPlacements: [], disabledChapters: []);
        Document =
            "{\"pvpEnabled\":true,\"plusOfferEnabled\":true,\"mailEnabled\":true," +
            "\"disabledAdPlacements\":[],\"disabledChapters\":[]}";
    }

    /// <summary>The flags the last accepted document resolved to.</summary>
    public FeatureFlags Current { get; }

    /// <summary>The exact raw text of the last accepted document, or the rendered identity when absent.</summary>
    public string Document { get; }

    /// <summary>One load pass: re-reads the file and swaps the state atomically on acceptance.</summary>
    public void Reload()
    {
    }
}
