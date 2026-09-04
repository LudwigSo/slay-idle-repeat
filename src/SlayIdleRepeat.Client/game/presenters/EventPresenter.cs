using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Event screen has got with the card the tile draws.</summary>
public enum EventStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is on an Event tile with no card drawn, and the draw is this screen's own.</summary>
    Drawing = 2,

    /// <summary>A card is drawn and the choice is the player's.</summary>
    Choosing = 3,

    /// <summary>A choice was accepted and the tile is cleared. What happened is on the screen.</summary>
    Resolved = 4,

    /// <summary>The run was read and is standing on some other tile. The screen is open on the wrong one.</summary>
    NotAtAnEvent = 5,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 6,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 7,

    /// <summary>A card is drawn and the content set cannot describe it. A sentence, not a crash.</summary>
    CardUnavailable = 8,
}

/// <summary>What one submission from this screen did.</summary>
public enum EventSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>One option of the drawn card as the screen draws it.</summary>
/// <param name="ChoiceIndex">What <c>EVENT_CHOOSE</c> carries for it — the option's authored position.</param>
/// <param name="Label">The option's caption, verbatim English as the card authors it.</param>
/// <param name="CostText">
/// The price, already resolved and formatted, or empty for a free option. The currency's caption
/// comes from <c>tuning/currencies.json</c>'s own <c>loc.currency.&lt;snake&gt;.name</c> key.
/// </param>
/// <param name="Available">Whether this run can pay for it at all.</param>
/// <param name="BlockText">
/// Why it cannot, already resolved, or empty for an option that can be taken. 🔒 Shown BEFORE the
/// press: <c>EVENT_CHOOSE</c> refuses an unaffordable option with a wire value four other things
/// share, so a round trip would replace the sentence naming the price with one naming nothing.
/// </param>
public sealed record EventOptionRow(
    int ChoiceIndex, string Label, string CostText, bool Available, string BlockText);

/// <summary>One line of what a resolved choice actually moved.</summary>
/// <param name="Label">The row's caption, already resolved.</param>
/// <param name="Delta">
/// 🔒 Signed, and shown signed: a card that takes 40 Gold and one that gives 40 read identically
/// without the sign, and both are outcomes the same option can have.
/// </param>
public readonly record struct EventResultLine(string Label, long Delta);

/// <summary>
/// ⚠️ <b>Phase 1 stub — the public surface only.</b> Phase 3b writes the real body.
/// </summary>
/// <remarks>
/// <para>
/// Drives the Event screen: the tile draws a card, the card offers two or three options, and
/// <c>EVENT_CHOOSE</c> spends one and clears the tile whatever the outcome was.
/// </para>
/// <para>
/// 🔒 <b>The screen owns its draw.</b> <c>StartAsync</c> submits <c>RESOLVE_TILE</c> itself when the
/// tile is an Event with no card on it. The board's Continue is not on this path, and on resume with
/// a card already drawn nothing is submitted at all — <c>DecisionFor</c> routes a re-entered Event
/// tile back here, so that branch is real rather than theoretical, and a second draw would replace
/// the card the player was reading.
/// </para>
/// <para>
/// 🔴 <b>Event card prose is shown verbatim, in English.</b>
/// <c>schema/board_events.schema.json</c> types a card's <c>title</c> and <c>body</c> and an
/// option's <c>label</c> as free English strings rather than <c>loc.*</c> keys, so
/// <c>ContentInvariants</c>' locale check never sees them and a German player reads the card in
/// English. The screen's own chrome is fully localised. Re-authoring the cards against loc keys is a
/// content pass nobody has done; it is stated here rather than hidden.
/// </para>
/// <para>
/// 🔴 <b>Most shipped outcomes are <c>UNSUPPORTED</c>, so "nothing happened" is the COMMON
/// result.</b> It is shown honestly through the screen's own sentence rather than dressed as
/// flavour.
/// </para>
/// <para>
/// 🔒 <b>The pre-submit snapshot is kept.</b> A refused command comes back carrying an empty state
/// slice, so a screen that read its rows back off a refused outcome would blank the card it is still
/// showing. The result lines are diffed against the snapshot taken before the command went out.
/// </para>
/// </remarks>
public sealed class EventPresenter
{
    /// <summary>The tile kind an event is, as the run reports it.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed: the numbering is public, so a
    /// kind inserted above this one renumbers the constant with it rather than leaving a literal
    /// pointing at whatever moved into its slot.
    /// </remarks>
    public const int EventTileKind = (int)TileKind.Event;

    /// <summary>Builds the screen over the host, the strings, the content set and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the drawn card is projected against.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public EventPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        RunId run) =>
        throw new NotImplementedException(
            "Phase 1 stub. Phase 3b builds this screen; the tests that name these members are the " +
            "specification it is written against.");

    /// <summary>How far the screen has got with the card.</summary>
    public EventStage Stage { get; } = EventStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; }

    /// <summary>Whether the tile has cleared, so Continue may return to the board.</summary>
    /// <remarks>
    /// 🔒 False until the run reports no pending tile. The board's decision latch logs "halted" if
    /// the same decision re-opens with the tile still pending, so handing back early is a loop.
    /// </remarks>
    public bool CanLeave { get; }

    /// <summary>The drawn card's id, for the log. Empty until one is drawn.</summary>
    public string CardId { get; } = "";

    /// <summary>The drawn card's heading — authored English, not a resolved key.</summary>
    public string CardTitle { get; } = "";

    /// <summary>The drawn card's prose — authored English, not a resolved key.</summary>
    public string CardBody { get; } = "";

    /// <summary>The card's options, in authored order. Empty until a card is drawn.</summary>
    public IReadOnlyList<EventOptionRow> Options { get; } = [];

    /// <summary>What the resolved choice moved, signed. Empty when nothing observable happened.</summary>
    public IReadOnlyList<EventResultLine> ResultLines { get; } = [];

    /// <summary>The screen's heading, resolved.</summary>
    public string Title { get; } = "";

    /// <summary>The lead-in an option's price is shown under, resolved.</summary>
    public string CostLabel { get; } = "";

    /// <summary>The lead-in the result lines are listed under, resolved.</summary>
    public string ResultLabel { get; } = "";

    /// <summary>The one action that leaves this screen, resolved.</summary>
    public string ContinueText { get; } = "";

    /// <summary>The line saying what the screen is doing while it has something to say, resolved.</summary>
    public string StatusText { get; } = "";

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    public string RejectionText { get; } = "";

    /// <summary>Reads the run, and draws the card itself when the tile has none.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "Phase 1 stub. Phase 3b reads the run and submits the draw here.");

    /// <summary>Submits <c>EVENT_CHOOSE</c> for one of the card's options.</summary>
    /// <param name="choiceIndex">The option's authored position, which is what the command carries.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<EventSubmission> ChooseAsync(int choiceIndex, CancellationToken ct) =>
        throw new NotImplementedException(
            "Phase 1 stub. Phase 3b submits the choice here.");
}
