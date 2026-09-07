using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Builds the boot screen's presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the boot scene never has to. A scene renders and forwards input; naming the
/// device locale source, a clock adapter or a filesystem-backed catalogue is the composition root's
/// job, and this is the part of it the boot screen needs.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the resolved paths all come out
/// of the graph the root owns — a second graph would mean a second cache over the same directory.
/// </para>
/// </remarks>
public static class BootComposition
{
    /// <summary>Wires the boot presenter over an already-composed client.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static BootPresenter CreateBootPresenter(ComposedGodotClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return new BootPresenter(
            composed.Client.GameHost,
            new LocaleStringCatalogue(
                composed.Client.Content.Current, composed.Capabilities.PlatformInfo.Locale),
            // 🔴 The disk content root WHEN THERE IS ONE, and the install directory otherwise.
            // It used to be the content root unconditionally, which threw in a packed build — so an
            // exported game died here, before drawing anything, even after it had a packed content
            // source. The atlas catalogue's own remarks predicted exactly this: it reads through
            // System.IO and "finds nothing once the game's resources are packed into an archive … the
            // packed-resource path belongs to the task that owns it". Finding nothing is its ordinary
            // answer and is handled; being handed a directory that does not exist was not.
            new PlaceholderAtlasCatalogue(
                composed.Capabilities.Paths.ContentDataRootOnDisk()
                ?? composed.Capabilities.Paths.ResolveInstallationRoot()),
            // The graph's clock, never a second one. The composed client owns exactly one so that a
            // screen measuring a soft timer measures against the instant source the host stamps its
            // commands with — and the cold-start budget this presenter reports is the one number
            // nothing else could substitute a clock into.
            composed.Client.Clock,

            // 🔴 Both stated rather than defaulted, and both null on the arm every shipped build
            // composes: an in-process host asks no server for content and opens no account session.
            // A null skips the stage entirely, which is what keeps that arm's boot as it was.
            contentSync: composed.Client.ContentSync,
            session: composed.Client.Session);
    }

    /// <summary>
    /// Wires the launch's game-day open — the one <c>BEGIN_SESSION</c> a launch sends.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Here rather than in the boot scene, for the reason every other collaborator on this
    /// screen is here.</b> A scene renders and forwards input; naming the host, the platform's build
    /// version and the loaded content set's hash is composition, and a scene that assembled its own
    /// collaborator out of the graph would be the one place in the client that did. Its sibling in
    /// <c>game/net/</c>, <c>SessionOpener</c>, is composed in <see cref="ClientComposition"/> for the
    /// same reason.
    /// </remarks>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static GameDayOpener CreateGameDayOpener(ComposedGodotClient composed)
    {
        ArgumentNullException.ThrowIfNull(composed);

        return new GameDayOpener(
            composed.Client.GameHost,
            composed.Capabilities.PlatformInfo.AppVersion,

            // The version of the content set this client actually LOADED, never the one it was
            // built against: BEGIN_SESSION is the one command carrying a content hash, and the
            // composition root is the only place that knows the real value.
            composed.Client.Content.Current.Version);
    }
}
