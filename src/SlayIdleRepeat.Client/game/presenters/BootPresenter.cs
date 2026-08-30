using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// How far the boot has got. Every value is a state the player can be looking at.
/// </summary>
public enum BootStage
{
    /// <summary>The first drawn frame, before any work has started.</summary>
    Splash = 1,

    /// <summary>Checking that there is a content set to play against.</summary>
    Content = 2,

    /// <summary>Asking the server whether it serves a newer content set, and taking it if so.</summary>
    ContentSync = 3,

    /// <summary>Opening the account session the wire runs on.</summary>
    Session = 4,

    /// <summary>Opening the local profile.</summary>
    Profile = 5,

    /// <summary>Reading whatever atlas metadata this installation has.</summary>
    Atlas = 6,

    /// <summary>Everything the boot needed is up, and the next screen can take over.</summary>
    Ready = 7,

    /// <summary>The boot stopped. Where it stopped is carried by <see cref="BootFailure.Stage"/>.</summary>
    Failed = 8,
}

/// <summary>
/// Why a boot did not finish — one name per cause, because the four are fixed by different people.
/// </summary>
public enum BootFailureKind
{
    /// <summary>There is no content set. A broken export, fixed by rebuilding.</summary>
    ContentUnavailable = 1,

    /// <summary>The local profile would not open. One device's cache, cleared by a reinstall.</summary>
    ProfileUnavailable = 2,

    /// <summary>The app was shut down mid-start. A player action, not a defect.</summary>
    Cancelled = 3,

    /// <summary>Something nobody anticipated. Carried rather than swallowed.</summary>
    Unexpected = 4,

    /// <summary>
    /// The server understood the sign-in and said no. Fatal, unlike a server that could not be
    /// reached at all: a refusal repeated unchanged is refused again, so there is nothing to wait for.
    /// </summary>
    SessionRefused = 5,
}

/// <summary>
/// One boot failure, identified: where it happened, what kind it was, and what actually went wrong.
/// </summary>
public sealed class BootFailure
{
    /// <summary>Records a failure against the stage it happened in.</summary>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is null, empty or whitespace.</exception>
    public BootFailure(BootStage stage, BootFailureKind kind, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Stage = stage;
        Kind = kind;
        Detail = detail;
    }

    /// <summary>The stage the boot was in when it stopped.</summary>
    public BootStage Stage { get; }

    /// <summary>Which of the four causes this was.</summary>
    public BootFailureKind Kind { get; }

    /// <summary>What went wrong, in words that name this failure rather than failure in general.</summary>
    public string Detail { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} in {Stage} — {Detail}";
}

/// <summary>
/// Drives the boot screen: content, profile, atlas, then Ready — or a named failure.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole boot sequence runs under
/// a test runner with no engine anywhere near it. The scene above renders <see cref="Stage"/> and
/// <see cref="StatusText"/> and forwards nothing; the composition root below decides what the
/// collaborators actually are.
/// </para>
/// <para>
/// 🔒 A failure is a state, never an escape. A boot that threw would leave a window that never
/// draws, which is the one outcome worse than saying what broke.
/// </para>
/// <para>
/// ⚠️ Auth, the server session and the content-hash check are deliberately absent — they arrive with
/// the task that builds them, and a stage here that pretended to do them would make an unbuilt flow
/// look shipped. So would a first-run beat: the tutorial belongs to the milestone that owns it.
/// </para>
/// </remarks>
public sealed class BootPresenter
{
    /// <summary>The application title a player reads, as opposed to the window title.</summary>
    private const string TitleKey = "loc.boot.title.name";

    private const string SplashStatusKey = "loc.boot.splash.status";
    private const string ContentStatusKey = "loc.boot.content.status";
    private const string ContentSyncStatusKey = "loc.boot.content_sync.status";
    private const string SessionStatusKey = "loc.boot.session.status";
    private const string ProfileStatusKey = "loc.boot.profile.status";
    private const string AtlasStatusKey = "loc.boot.atlas.status";
    private const string ReadyStatusKey = "loc.boot.ready.status";
    private const string FailureStatusKey = "loc.boot.failure.status";

    /// <summary>What an empty content set means, said in terms of what a player can be told to do.</summary>
    private const string EmptyContentSetDetail =
        "the content set holds no documents at all, so neither the game's rules nor any of its text " +
        "can be resolved — the build did not ship its data";

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly IBootAtlasCatalogue _atlas;
    private readonly IClockPort _clock;

    private DateTimeOffset _startedAt;

    /// <summary>Takes everything the boot needs, and refuses a graph that was not fully wired.</summary>
    /// <param name="gameHost">The host the profile is opened through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="atlas">The placeholder-atlas read.</param>
    /// <param name="clock">The only sanctioned source of time here.</param>
    /// <param name="contentSync">
    /// 🔒 The content sync to run, or <c>null</c> to say this build has no server to ask — stated,
    /// never defaulted. A null skips the stage entirely, which is how the local arm's boot stays
    /// byte-identical to what it was before a server existed.
    /// </param>
    /// <param name="session">The session to open, or <c>null</c> for the same reason.</param>
    /// <exception cref="ArgumentNullException">Any non-optional collaborator is null.</exception>
    public BootPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        IBootAtlasCatalogue atlas,
        IClockPort clock,
        ContentSyncPresenter? contentSync,
        SessionOpener? session)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(clock);

        _gameHost = gameHost;
        _strings = strings;
        _atlas = atlas;
        _clock = clock;
    }

    /// <summary>Where the boot is now. The stage the screen draws.</summary>
    public BootStage Stage { get; private set; } = BootStage.Splash;

    /// <summary>The profile the boot opened, or null while none is open.</summary>
    public PlayerId? PlayerId { get; private set; }

    /// <summary>
    /// The account the server's session belongs to, or null on an arm that opens none.
    /// </summary>
    /// <remarks>
    /// 🔴 Held BESIDE <see cref="PlayerId"/> rather than instead of it, because on the server arm
    /// the client genuinely holds two identities and nothing reconciles them — reconciling them is
    /// the presenter migration. Exposing both is what keeps the divergence visible.
    /// </remarks>
    public PlayerId? AccountPlayerId => throw new NotImplementedException();

    /// <summary>How far the content sync got, or null when this build ran none.</summary>
    public SyncState? ContentSync => throw new NotImplementedException();

    /// <summary>Why the content sync stopped, or null when it did not.</summary>
    public ContentSyncFailure? ContentSyncFailure => throw new NotImplementedException();

    /// <summary>What the atlas stage found, or null before it has run.</summary>
    public BootAtlasResult? Atlas { get; private set; }

    /// <summary>The failure that stopped the boot, or null when nothing did.</summary>
    public BootFailure? Failure { get; private set; }

    /// <summary>
    /// How long the boot took, measured on the injected clock between its first and last reading.
    /// </summary>
    /// <remarks>
    /// A span that ended, not a stopwatch still running: it stops moving when the boot does, so the
    /// number a cold-start report picks up does not depend on when the report was written.
    /// </remarks>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>The application title, resolved.</summary>
    public string Title => _strings.Resolve(TitleKey);

    /// <summary>The progress line for the current stage, resolved — never a raw key.</summary>
    public string StatusText => _strings.Resolve(KeyFor(Stage));

    /// <summary>
    /// Walks the boot: content, profile, atlas. Ends at <see cref="BootStage.Ready"/>, or at
    /// <see cref="BootStage.Failed"/> with a <see cref="Failure"/> that names where and why.
    /// </summary>
    /// <param name="ct">Cancellation — the app saying the window is closing.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        _startedAt = _clock.UtcNow;
        var reached = BootStage.Content;

        try
        {
            Stage = BootStage.Content;

            if (_strings.ContentSetIsEmpty)
            {
                Fail(BootStage.Content, BootFailureKind.ContentUnavailable, EmptyContentSetDetail);
                return;
            }

            Mark();
            reached = BootStage.Profile;
            Stage = BootStage.Profile;

            // Awaited inside the guard rather than merely called inside it: a real host's open is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            PlayerId = await _gameHost.OpenProfileAsync(ct).ConfigureAwait(false);

            Mark();
            reached = BootStage.Atlas;
            Stage = BootStage.Atlas;

            Atlas = _atlas.Load();

            Stage = BootStage.Ready;
        }
        catch (OperationCanceledException cancelled)
        {
            // Ahead of the general arm on purpose. Filed as Unexpected, every backgrounded launch on
            // every handset becomes the loudest error in the telemetry and buries the real ones.
            Fail(reached, BootFailureKind.Cancelled, Describe(cancelled));
        }
        catch (Exception failure)
        {
            Fail(reached, KindFor(reached), Describe(failure));
        }
        finally
        {
            Mark();
        }
    }

    /// <summary>The status key each stage is drawn from.</summary>
    private static string KeyFor(BootStage stage) => stage switch
    {
        BootStage.Splash => SplashStatusKey,
        BootStage.Content => ContentStatusKey,
        BootStage.ContentSync => ContentSyncStatusKey,
        BootStage.Session => SessionStatusKey,
        BootStage.Profile => ProfileStatusKey,
        BootStage.Atlas => AtlasStatusKey,
        BootStage.Ready => ReadyStatusKey,
        _ => FailureStatusKey,
    };

    /// <summary>Which cause a thrown failure in a given stage is.</summary>
    private static BootFailureKind KindFor(BootStage stage) => stage switch
    {
        BootStage.Content => BootFailureKind.ContentUnavailable,
        BootStage.Profile => BootFailureKind.ProfileUnavailable,
        _ => BootFailureKind.Unexpected,
    };

    private static string Describe(Exception failure) => $"{failure.GetType().Name}: {failure.Message}";

    private void Fail(BootStage stage, BootFailureKind kind, string detail)
    {
        Failure = new BootFailure(stage, kind, detail);
        Stage = BootStage.Failed;
    }

    private void Mark() => Elapsed = _clock.UtcNow - _startedAt;
}
