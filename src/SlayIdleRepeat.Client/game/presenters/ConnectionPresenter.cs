using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Client.Game.Net;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The connection, as the five things a player may ever be shown about it.
/// </summary>
/// <remarks>
/// <para>
/// One presenter for the whole build rather than one per screen: what it describes is global, and a
/// per-screen copy would draw a second pill on a handover and none at all on a screen that forgot to
/// build one.
/// </para>
/// <para>
/// 🔒 <b>Connected draws NOTHING.</b> Not a tick, not a badge, not a green dot. A connection that is
/// working is the ordinary case and the specification says so; a build that marks it is a build that
/// spends screen furniture on the state a player is in almost always.
/// </para>
/// <para>
/// 🔒 <b>There is no blocking error, ever.</b> <see cref="BlockingErrorVisible"/> is a constant
/// <c>false</c> with a test whose name says so, because that is the one hard prohibition in the
/// specification this presenter implements: a run is never stopped by the network. A future change
/// that wants a full-screen connection error has to delete a named assertion to get one.
/// </para>
/// <para>
/// The durations and the opacity below are spec-locked presentation constants, not balance dials:
/// they are not authored in <c>game-data/tuning/</c> and must not be moved there. Retuning them is a
/// design decision about the interface, taken in the specification, not a number a live-ops pass may
/// turn.
/// </para>
/// </remarks>
public sealed class ConnectionPresenter
{
    /// <summary>The pill's own words.</summary>
    private const string ReconnectingStatusKey = "loc.net.reconnecting.status";

    /// <summary>What a tap on an action the server has to answer is told.</summary>
    private const string WaitingForConnectionStatusKey = "loc.net.waiting_for_connection.status";

    /// <summary>What a resync that actually moved the state is announced with.</summary>
    private const string CaughtUpStatusKey = "loc.net.caught_up.status";

    /// <summary>The resume card's sentence, with two runtime parameters this presenter substitutes.</summary>
    private const string RunResumedStatusKey = "loc.net.run_resumed.status";

    /// <summary>The chapter placeholder in <see cref="RunResumedStatusKey"/>'s sentence.</summary>
    private const string ChapterParameter = "{chapter}";

    /// <summary>The stage placeholder in <see cref="RunResumedStatusKey"/>'s sentence.</summary>
    private const string StageParameter = "{stage}";

    /// <summary>A view state with nothing to say says nothing, rather than saying a placeholder.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>
    /// How long the resync announcement — the green flash and the line of text together — stays up.
    /// 🔒 Spec-locked presentation, not a tunable.
    /// </summary>
    /// <remarks>
    /// The flash and the toast are one announcement of one event, so they share one window. Giving
    /// the text its own longer life would leave a sentence about a moment that has visibly passed.
    /// </remarks>
    private static readonly TimeSpan ResyncAnnouncementDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// How long the card naming the run a player is being dropped back into stays up.
    /// 🔒 Spec-locked presentation, not a tunable.
    /// </summary>
    private static readonly TimeSpan RunResumedCardDuration = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// What a control the server would have to answer is drawn at while it cannot be used.
    /// 🔒 Spec-locked presentation, not a tunable.
    /// </summary>
    /// <remarks>
    /// Dimmed rather than removed, and dimmed rather than disabled-looking: the control is still
    /// where the player left it, so the screen does not reflow underneath them when the connection
    /// comes and goes.
    /// </remarks>
    public const float UnavailableActionOpacity = 0.40f;

    /// <summary>The stages a run's board is divided into, and the only values worth naming as one.</summary>
    /// <remarks>
    /// ⚠️ The boss node belongs to no stage and is carried as stage zero, which is also the value a
    /// run with no pending tile carries. Neither is a stage a sentence can name, so the range is
    /// stated rather than assumed — the same reading <c>BoardPresenter.StageNumber</c> takes.
    /// </remarks>
    private const int FirstNamedStage = 1;

    /// <summary>The last stage a chapter's board is divided into.</summary>
    private const int LastNamedStage = 3;

    /// <summary>The lowest tile kind that is a real tile. Anything below it means nothing is pending.</summary>
    /// <remarks>
    /// The same reading <c>RunEndView</c> takes of the same field: the rules layer spells "no tile
    /// pending" as a negative kind, and the stage beside it is only meaningful while one is.
    /// </remarks>
    private const int FirstRealTileKind = 0;

    private readonly ReconnectManager _connection;
    private readonly StateMirror _mirror;
    private readonly LocaleStringCatalogue _strings;
    private readonly IClockPort _clock;

    private ConnectionState _lastObservedState = ConnectionState.Connected;
    private DateTimeOffset? _resyncAnnouncedAtUtc;
    private DateTimeOffset? _resumeCardRaisedAtUtc;
    private bool _resumeCardAlreadyShown;
    private bool _toastIsTheResyncAnnouncement;

    /// <summary>Builds the overlay's presenter over the connection, the mirror and the strings.</summary>
    /// <param name="connection">Where the connection fact and the resync outcome come from.</param>
    /// <param name="mirror">The local copy the resume card names a run out of.</param>
    /// <param name="strings">Key to display string. Nothing here writes a player-facing literal.</param>
    /// <param name="clock">The only sanctioned source of "now" — the two timed things are measured on it.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ConnectionPresenter(
        ReconnectManager connection,
        StateMirror mirror,
        LocaleStringCatalogue strings,
        IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(clock);

        _connection = connection;
        _mirror = mirror;
        _strings = strings;
        _clock = clock;
    }

    /// <summary>The connection fact, as of the last <see cref="Poll"/>.</summary>
    public ConnectionState State { get; private set; } = ConnectionState.Connected;

    /// <summary>Whether the reconnecting pill is on screen.</summary>
    /// <remarks>
    /// 🔒 True in <see cref="ConnectionState.Reconnecting"/> and in no other state.
    /// <see cref="ConnectionState.Waiting"/> draws nothing either — it is the threshold that keeps a
    /// blink of failure off a player's screen, and drawing during it would defeat the whole of it.
    /// </remarks>
    public bool PillVisible => State == ConnectionState.Reconnecting;

    /// <summary>What the pill says.</summary>
    public string PillText => _strings.Resolve(ReconnectingStatusKey);

    /// <summary>Whether a control the server has to answer may be used.</summary>
    public bool ServerActionsAvailable => State == ConnectionState.Connected;

    /// <summary>Whether the cloud-slash glyph marking the dimmed controls is on screen.</summary>
    /// <remarks>
    /// The glyph is the part of the dim state a player who cannot separate a 40% control from a full
    /// one still reads, so it is present exactly when the dimming is, not only when the pill is.
    /// </remarks>
    public bool CloudSlashGlyphVisible => !ServerActionsAvailable;

    /// <summary>The inline toast's text, or null when none is up.</summary>
    /// <remarks>
    /// ⚠️ Inline and never a modal. Two things raise it: a tap on a control the connection cannot
    /// carry, and a resync that actually changed something. The resync one expires with the flash;
    /// the tap one has no authored duration and is therefore not given an invented one — it clears
    /// when the connection it is about returns, or when something replaces it.
    /// </remarks>
    public string? ToastText { get; private set; }

    /// <summary>Whether the green resync flash is up.</summary>
    public bool ResyncFlashActive { get; private set; }

    /// <summary>Whether the card naming the run being resumed is up.</summary>
    public bool ResumeCardVisible { get; private set; }

    /// <summary>The resume card's sentence, with the chapter and the stage substituted.</summary>
    /// <remarks>
    /// The catalogue is a lookup, not a localisation runtime — it does no interpolation — so the two
    /// runtime parameters are substituted here, in the one place that knows what they are.
    /// </remarks>
    public string ResumeCardText { get; private set; } = NothingLeftToSay;

    /// <summary>
    /// 🔒 Always <c>false</c>. There is no full-screen blocking connection error in this game, during
    /// a run or outside one.
    /// </summary>
    /// <remarks>
    /// It is a member rather than an absence so the prohibition is something a scene can be built
    /// against and a test can name. A connection problem is a pill, a dimmed control and a toast; a
    /// player is never stopped by one.
    /// </remarks>
    public bool BlockingErrorVisible => false;

    /// <summary>Answers a tap on a control the connection cannot carry.</summary>
    /// <remarks>
    /// 🔒 An inline toast and nothing else. Never a modal, never a dialogue with a retry button —
    /// the retry is already running on its own ladder, and a button offering to start it again would
    /// be offering the player something the client is doing anyway.
    /// </remarks>
    public void ReportUnavailableActionTapped()
    {
        if (ServerActionsAvailable)
        {
            return;
        }

        ToastText = _strings.Resolve(WaitingForConnectionStatusKey);
        _toastIsTheResyncAnnouncement = false;
    }

    /// <summary>
    /// Reads the connection, raises whatever has just become true, and expires whatever has run out.
    /// </summary>
    /// <remarks>
    /// Called every frame by the overlay scene. It reads the clock once, so the two timed things are
    /// measured against the same instant rather than against two readings a frame apart.
    /// </remarks>
    public void Poll()
    {
        var now = _clock.UtcNow;
        var previous = _lastObservedState;

        State = _connection.State;
        _lastObservedState = State;

        // The recovery EDGE, not the connected state: a resync happens once per reconnect, and a
        // presenter that keyed off "connected and something changed" would re-announce it on every
        // frame after it.
        if (previous != ConnectionState.Connected && State == ConnectionState.Connected &&
            _connection.LastResyncChangedState)
        {
            RaiseResyncAnnouncement(now);
        }

        RaiseResumeCardOnceARunIsKnown(now);

        Expire(now);
    }

    private void RaiseResyncAnnouncement(DateTimeOffset now)
    {
        ResyncFlashActive = true;
        ToastText = _strings.Resolve(CaughtUpStatusKey);
        _toastIsTheResyncAnnouncement = true;
        _resyncAnnouncedAtUtc = now;
    }

    /// <summary>
    /// Shows the resume card the first time this session finds a run already in progress.
    /// </summary>
    /// <remarks>
    /// "The app opened into a run" is observable here as the first moment the mirror holds one: the
    /// mirror starts empty and is filled by the opening read, so the first run it ever reports is
    /// the run the player was already in. Once per presenter, because a card that reappeared on
    /// every later state read would be announcing a resume that never happened.
    /// </remarks>
    private void RaiseResumeCardOnceARunIsKnown(DateTimeOffset now)
    {
        if (_resumeCardAlreadyShown || _mirror.Run is not { } run)
        {
            return;
        }

        _resumeCardAlreadyShown = true;

        if (run.PendingTileKind < FirstRealTileKind ||
            run.PendingTileStage is < FirstNamedStage or > LastNamedStage)
        {
            // ⚠️ The projection carries no stage this sentence could name — the run is between
            // tiles, or on the boss node, which belongs to no stage. The card names a chapter AND a
            // stage, and there is no second sentence authored that names only a chapter, so it is
            // not shown rather than shown with a number invented for it.
            return;
        }

        ResumeCardText = _strings.Resolve(RunResumedStatusKey)
                                 .Replace(ChapterParameter, PlayerNumber.Full(run.ChapterId), StringComparison.Ordinal)
                                 .Replace(StageParameter, PlayerNumber.Full(run.PendingTileStage), StringComparison.Ordinal);

        ResumeCardVisible = true;
        _resumeCardRaisedAtUtc = now;
    }

    private void Expire(DateTimeOffset now)
    {
        if (_resyncAnnouncedAtUtc is { } announced && now - announced >= ResyncAnnouncementDuration)
        {
            ResyncFlashActive = false;
            _resyncAnnouncedAtUtc = null;

            if (_toastIsTheResyncAnnouncement)
            {
                ToastText = null;
                _toastIsTheResyncAnnouncement = false;
            }
        }

        if (_resumeCardRaisedAtUtc is { } raised && now - raised >= RunResumedCardDuration)
        {
            ResumeCardVisible = false;
            ResumeCardText = NothingLeftToSay;
            _resumeCardRaisedAtUtc = null;
        }

        // The tap toast is about an action being unavailable, so it goes when the action comes back.
        if (ServerActionsAvailable && !_toastIsTheResyncAnnouncement)
        {
            ToastText = null;
        }
    }
}
