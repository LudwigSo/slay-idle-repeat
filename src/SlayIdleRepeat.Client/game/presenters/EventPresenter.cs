using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
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
/// <param name="FromWallet">
/// Whether this row is a wallet currency rather than one of the run's own three readouts. The
/// screen groups the wallet rows under their own heading, because "From your wallet" is the only
/// thing on the panel that says which purse a number came out of — Gold, hit points and fixed dice
/// are the run's and are captioned by this screen, while a wallet row is captioned by
/// <c>tuning/currencies.json</c>'s key and would otherwise hang under nothing.
/// </param>
public readonly record struct EventResultLine(string Label, long Delta, bool FromWallet);

/// <summary>
/// Drives the Event screen: the tile draws a card, the card offers two or three options, and
/// <c>EVENT_CHOOSE</c> spends one and clears the tile whatever the outcome was.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The screen owns its draw.</b> <see cref="StartAsync"/> submits <c>RESOLVE_TILE</c> itself
/// when the tile is an Event with no card on it. The board's Continue is not on this path, and on
/// resume with a card already drawn nothing is submitted at all — <c>DecisionFor</c> routes a
/// re-entered Event tile back here, so that branch is real rather than theoretical, and a second draw
/// would replace the card the player was reading. It is also why the draw is conditional on the tile
/// really being an Event: <c>RESOLVE_TILE</c> on an Enemy tile is ACCEPTED and opens a fight, so a
/// draw submitted because this screen happened to be open would start a battle nobody chose.
/// </para>
/// <para>
/// 🔒 <b>Affordability is answered here, before the press, and it mirrors the handler exactly.</b>
/// Gold is charged to the RUN and every other currency to the PLAYER's wallet, and an option is
/// available at <c>balance &gt;= cost</c> — a run holding exactly the price can pay it, which is the
/// commonest way to meet a priced option at all. A screen reading one balance for both purses would
/// price a wallet option off Gold the handler never looks at.
/// </para>
/// <para>
/// 🔴 <b>Event card prose is shown verbatim, in English.</b>
/// <c>schema/board_events.schema.json</c> types a card's <c>title</c> and <c>body</c> and an
/// option's <c>label</c> as free English strings rather than <c>loc.*</c> keys, so
/// <c>ContentInvariants</c>' locale check never sees them and a German player reads the card in
/// English until a content pass re-authors the thirty cards against loc keys. That pass is nobody's
/// yet. The screen's own chrome — every heading, price, block sentence and status line — is fully
/// localised, and a screen that pushed the card's prose through the catalogue as well would render
/// every card as its own untranslated key. It is stated here rather than hidden.
/// </para>
/// <para>
/// 🔴 <b>Most shipped outcomes are <c>UNSUPPORTED</c>, so "nothing happened" is the COMMON
/// result.</b> It is shown honestly through the screen's own sentence rather than dressed as
/// flavour: a cleared tile under an empty result panel reads as a screen that failed rather than as
/// a card that did nothing.
/// </para>
/// <para>
/// 🔒 <b>The pre-submit snapshot is kept.</b> A refused command comes back carrying an empty state
/// slice — the player's own row and no run — so a screen that read its rows back off a refused
/// outcome would blank the card it is still showing and leave the player looking at nothing on a
/// tile that is still pending. The result lines are diffed against the snapshot taken before the
/// command went out.
/// </para>
/// <para>
/// 🔒 <b>Hit points are DIFFED, not read off an event.</b> An HP movement emits no domain event at
/// all, so an events-only reading of an outcome would show a card that cost the hero ten points as
/// one that did nothing to him. The wallet rows are the other way round: the profile row a refusal
/// hands back is not the one that moved, so those come off the outcome's own
/// <see cref="CurrencyChanged"/> events.
/// </para>
/// </remarks>
public sealed class EventPresenter
{
    private const string TitleNameKey = "loc.event_screen.title.name";
    private const string CostLabelKey = "loc.event_screen.cost.label";
    private const string ResultLabelKey = "loc.event_screen.result.label";
    private const string GoldLabelKey = "loc.event_screen.gold.label";
    private const string HpLabelKey = "loc.event_screen.hp.label";
    private const string FixedDiceLabelKey = "loc.event_screen.fixed_dice.label";
    private const string WalletLabelKey = "loc.event_screen.wallet.label";
    private const string ContinueActionKey = "loc.event_screen.continue.action";
    private const string UnaffordableBlockKey = "loc.event_screen.unaffordable.block";
    private const string LoadingStatusKey = "loc.event_screen.loading.status";
    private const string DrawingStatusKey = "loc.event_screen.drawing.status";
    private const string RunMissingStatusKey = "loc.event_screen.run_missing.status";
    private const string NotAtAnEventStatusKey = "loc.event_screen.not_at_an_event.status";
    private const string ReadUnavailableStatusKey = "loc.event_screen.read_unavailable.status";
    private const string CardUnavailableStatusKey = "loc.event_screen.card_unavailable.status";
    private const string NothingHappenedStatusKey = "loc.event_screen.nothing_happened.status";
    private const string RefusedStatusKey = "loc.event_screen.refused.status";
    private const string HostUnavailableStatusKey = "loc.event_screen.host_unavailable.status";

    private const string GoldCurrencyNameKey = "loc.currency.gold.name";
    private const string CrownsCurrencyNameKey = "loc.currency.crowns.name";
    private const string SoulShardsCurrencyNameKey = "loc.currency.soul_shards.name";
    private const string EnergyCurrencyNameKey = "loc.currency.energy.name";
    private const string EnhanceStonesCurrencyNameKey = "loc.currency.enhance_stones.name";
    private const string MergeDustCurrencyNameKey = "loc.currency.merge_dust.name";
    private const string BeastFeedCurrencyNameKey = "loc.currency.beast_feed.name";
    private const string HonorCurrencyNameKey = "loc.currency.honor.name";

    /// <summary>What a caption key for a currency this screen does not know is built out of.</summary>
    private const string CurrencyNameKeyPrefix = "loc.currency.";

    private const string CurrencyNameKeySuffix = ".name";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>What separates a price's amount from the currency it is charged in.</summary>
    private const string AmountAndCurrency = " ";

    /// <summary>What a signed result row puts in front of a movement that added something.</summary>
    private const string Gained = "+";

    /// <summary>The tile kind an event is, as the run reports it.</summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed: the numbering is public, so a
    /// kind inserted above this one renumbers the constant with it rather than leaving a literal
    /// pointing at whatever moved into its slot.
    /// </remarks>
    public const int EventTileKind = (int)TileKind.Event;

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly RunId _run;

    /// <summary>The run as the last answer left it — and, during a submission, as it was before it.</summary>
    private RunSnapshot? _snapshot;

    /// <summary>The profile the read found, which is the purse every non-Gold price is read against.</summary>
    private PlayerSnapshot? _wallet;

    private bool _submissionInFlight;

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
        RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
        _run = run;
    }

    /// <summary>How far the screen has got with the card.</summary>
    public EventStage Stage { get; private set; } = EventStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>Whether the tile has cleared, so Continue may return to the board.</summary>
    /// <remarks>
    /// 🔒 False until the run reports no pending tile. The board's decision latch logs "halted" if
    /// the same decision re-opens with the tile still pending, so handing back early is a loop.
    /// </remarks>
    public bool CanLeave { get; private set; }

    /// <summary>The drawn card's id, for the log. Empty until one is drawn.</summary>
    public string CardId { get; private set; } = "";

    /// <summary>The drawn card's heading — authored English, not a resolved key.</summary>
    public string CardTitle { get; private set; } = "";

    /// <summary>The drawn card's prose — authored English, not a resolved key.</summary>
    public string CardBody { get; private set; } = "";

    /// <summary>The card's options, in authored order. Empty until a card is drawn.</summary>
    public IReadOnlyList<EventOptionRow> Options { get; private set; } = [];

    /// <summary>What the resolved choice moved, signed. Empty when nothing observable happened.</summary>
    public IReadOnlyList<EventResultLine> ResultLines { get; private set; } = [];

    /// <summary>
    /// Whether the result rows are showing their exact values rather than their shortened ones.
    /// </summary>
    /// <remarks>
    /// 🔒 The state lives here rather than in the scene, because "which form is on the page" is the
    /// half of this rule that can be proven. The gesture that sets it is the scene's — a long press is
    /// an engine event — but what a long press MEANS to a number is decided where a test can read it,
    /// and a screen that shortened what a card paid with no way back to the exact figure would be
    /// rounding the only record of what the choice cost.
    /// </remarks>
    public bool FullValuesRevealed { get; private set; }

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The lead-in an option's price is shown under, resolved.</summary>
    public string CostLabel => _strings.Resolve(CostLabelKey);

    /// <summary>The lead-in the result lines are listed under, resolved.</summary>
    public string ResultLabel => _strings.Resolve(ResultLabelKey);

    /// <summary>The heading the wallet-currency result rows are grouped under, resolved.</summary>
    /// <remarks>
    /// 🔒 The result panel spends <see cref="ResultLabel"/> on its own heading and this screen's own
    /// captions on the run's three rows, so a per-currency row captioned only by
    /// <c>loc.currency.&lt;snake&gt;.name</c> would hang under nothing. This is the group's own line.
    /// </remarks>
    public string WalletLabel => _strings.Resolve(WalletLabelKey);

    /// <summary>The one action that leaves this screen, resolved.</summary>
    public string ContinueText => _strings.Resolve(ContinueActionKey);

    /// <summary>The line saying what the screen is doing while it has something to say, resolved.</summary>
    /// <remarks>
    /// 🔒 The resolved arm is the one that carries a sentence rather than silence, and it is the
    /// commonest state this screen ends in: most authored outcomes cannot be applied by this build,
    /// so a resolved card that moved nothing has to say that it moved nothing.
    /// </remarks>
    public string StatusText => Stage switch
    {
        EventStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        EventStage.Drawing => _strings.Resolve(DrawingStatusKey),
        EventStage.Choosing => NothingLeftToSay,
        EventStage.Resolved => ResultLines.Count == 0
            ? _strings.Resolve(NothingHappenedStatusKey)
            : NothingLeftToSay,
        EventStage.NotAtAnEvent => _strings.Resolve(NotAtAnEventStatusKey),
        EventStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        EventStage.CardUnavailable => _strings.Resolve(CardUnavailableStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// 🔒 The fault is read first, because a faulted submission carries no rejection at all and the
    /// two must not share a sentence. An unaffordable option does not appear here: it is refused by
    /// this screen without a round trip, and carries its own sentence on its own card.
    /// </remarks>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>One movement as a player reads it: signed, and shortened unless it is being held.</summary>
    /// <remarks>
    /// 🔒 The sign is the row's whole meaning: a card that takes forty Gold and one that gives forty
    /// read identically without it, and both are outcomes the same option can have. A negative number
    /// prints its own sign, so only the positive case needs one written.
    /// </remarks>
    /// <param name="delta">What the row moved.</param>
    public string DeltaText(long delta) =>
        delta > 0 ? Gained + Readout(delta) : Readout(delta);

    /// <summary>Shows the exact value of every result row — a long press is holding one.</summary>
    public void RevealFullValues() => FullValuesRevealed = true;

    /// <summary>And puts the shortened form back, which is what the press ending means.</summary>
    public void ConcealFullValues() => FullValuesRevealed = false;

    /// <summary>Reads the run, and draws the card itself when the tile has none.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        OwnStateResult state;

        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Stage = EventStage.ReadUnavailable;

            return;
        }

        Settle(state);

        if (Stage != EventStage.Drawing)
        {
            return;
        }

        _ = await SubmitAsync(new ResolveTileCommand(), aChoiceWasSpent: false, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Submits <c>EVENT_CHOOSE</c> for one of the card's options.</summary>
    /// <remarks>
    /// 🔒 <b>The index is checked against the card the screen is actually showing.</b> It arrives
    /// from the scene, where a row's position is bound once and the card behind it can change under
    /// it — a resume onto a shorter card, or a rebuild between the press and the handler. Unchecked,
    /// this screen would index its own row list and throw where a refusal was wanted, and the
    /// command would reach <c>EVENT_CHOOSE</c>, which refuses it as <c>ILLEGAL_STATE</c> — the same
    /// wire value a run standing on no event at all gets.
    /// </remarks>
    /// <param name="choiceIndex">The option's authored position, which is what the command carries.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<EventSubmission> ChooseAsync(int choiceIndex, CancellationToken ct)
    {
        if (Stage != EventStage.Choosing || choiceIndex < 0 || choiceIndex >= Options.Count)
        {
            return EventSubmission.RefusedNotAvailable;
        }

        var chosen = Options[choiceIndex];

        if (!chosen.Available)
        {
            return EventSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(
                new EventChooseCommand(chosen.ChoiceIndex), aChoiceWasSpent: true, ct)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// <para>
    /// 🔒 The latch is taken BEFORE the await, not after it. Taken afterwards, a second press
    /// arriving while the first is in flight finds it unset and submits again — and here the second
    /// press names a DIFFERENT option, so the run would pay two costs and draw two outcomes off a
    /// card that offers one choice.
    /// </para>
    /// <para>
    /// 🔒 The snapshot the result is diffed against is taken before the command goes out, and a
    /// refusal keeps it: the refused outcome's state slice carries no run at all.
    /// </para>
    /// </remarks>
    private async Task<EventSubmission> SubmitAsync(
        GameCommand command, bool aChoiceWasSpent, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return EventSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        var before = _snapshot;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                return EventSubmission.RefusedByRules;
            }

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a card the command has spent.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                ResultLines = aChoiceWasSpent && before is not null
                    ? WhatMoved(before, moved, outcome.Events)
                    : [];

                Carry(moved, aChoiceWasSpent);
            }

            return EventSubmission.Submitted;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting
            // one would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return EventSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a choice that never answered spent nothing and left the tile
            // pending, so the retry has to be able to reach the host.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View is not { Run: { } run } found)
        {
            Stage = EventStage.RunMissing;

            return;
        }

        _wallet = found.Player;

        Carry(run, aChoiceWasSpent: false);
    }

    /// <summary>Settles the screen on the run as it now stands.</summary>
    /// <param name="run">The row the read or the command answered with.</param>
    /// <param name="aChoiceWasSpent">
    /// Whether this run arrived as the answer to <c>EVENT_CHOOSE</c>. 🔒 It is what tells a cleared
    /// tile apart from a screen opened on the wrong one: the two rows are identical, and the handler
    /// clears the tile as its last step whatever the outcome was.
    /// </param>
    private void Carry(RunSnapshot run, bool aChoiceWasSpent)
    {
        _snapshot = run;
        CanLeave = run.PendingTileKind == BoardTileKinds.NoPendingTile;

        if (run.PendingTileKind == EventTileKind)
        {
            if (string.IsNullOrEmpty(run.PendingEventCardId))
            {
                Forget();
                Stage = EventStage.Drawing;

                return;
            }

            Project(run);

            return;
        }

        // Nothing is left to choose either way, and on the resolved arm the card's prose stays: it
        // is the question the player has just answered, and the result panel is the answer to it.
        Options = [];

        if (aChoiceWasSpent && CanLeave)
        {
            Stage = EventStage.Resolved;

            return;
        }

        Forget();
        Stage = EventStage.NotAtAnEvent;
    }

    /// <remarks>
    /// 🔒 Only a content read's failure is caught, and it is a sentence rather than a crash for the
    /// reason the stage member states: a content set stripped of the card catalogue, and a card id
    /// this content version has lost, are both the content set failing to describe a card the run
    /// really has drawn. A blanket catch would turn a programming error inside the projection into
    /// the same quiet line, and the screen would report a missing document while the options it drew
    /// disagreed with the ones the tile is about to charge for.
    /// </remarks>
    private void Project(RunSnapshot run)
    {
        try
        {
            if (EventCardView.Project(run, _content) is not { } card)
            {
                Forget();
                Stage = EventStage.CardUnavailable;

                return;
            }

            CardId = card.CardId;
            CardTitle = card.Title;
            CardBody = card.Body;
            Options = Draw(card, run);
            Stage = EventStage.Choosing;
        }
        catch (ContentException)
        {
            Forget();
            Stage = EventStage.CardUnavailable;
        }
        catch (ArgumentException)
        {
            // The catalogue's own throw for an id it does not carry — a content rollback across a
            // live run. Both arguments are non-null by construction, so this is that and nothing
            // else.
            Forget();
            Stage = EventStage.CardUnavailable;
        }
    }

    /// <summary>Whichever form of a number this screen is currently showing.</summary>
    private string Readout(long value) =>
        FullValuesRevealed ? PlayerNumber.Full(value) : PlayerNumber.Abbreviated(value);

    /// <summary>Drops the card, so no arm can draw prose or options belonging to another state.</summary>
    private void Forget()
    {
        CardId = "";
        CardTitle = "";
        CardBody = "";
        Options = [];
    }

    /// <summary>The card's options as the screen draws them, priced against this run and profile.</summary>
    private IReadOnlyList<EventOptionRow> Draw(EventCardView card, RunSnapshot run)
    {
        var rows = new EventOptionRow[card.Options.Count];

        for (var index = 0; index < rows.Length; index++)
        {
            var option = card.Options[index];

            if (option.CostCurrency is not { } currency || option.CostAmount is not { } amount)
            {
                rows[index] = new EventOptionRow(
                    option.ChoiceIndex,
                    option.Label,
                    NothingLeftToSay,
                    Available: true,
                    NothingLeftToSay);

                continue;
            }

            var affordable = BalanceOf(currency, run) >= amount;

            rows[index] = new EventOptionRow(
                option.ChoiceIndex,
                option.Label,
                PlayerNumber.Abbreviated(amount) + AmountAndCurrency +
                    _strings.Resolve(CurrencyNameKeyOf(currency)),
                affordable,
                affordable ? NothingLeftToSay : _strings.Resolve(UnaffordableBlockKey));
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>
    /// What the run or the profile holds of one currency — <c>EventChoose</c>'s own split.
    /// </summary>
    /// <remarks>
    /// 🔒 Gold is the RUN's one currency and every other cost currency lives on the PLAYER's wallet,
    /// which is exactly what the handler charges. A profile the read did not answer with holds
    /// nothing rather than everything: an option offered against a wallet nobody read would be
    /// refused after the press, for a reason the player never sees.
    /// </remarks>
    private long BalanceOf(CurrencyId currency, RunSnapshot run)
    {
        if (currency == CurrencyId.GOLD)
        {
            return run.Gold;
        }

        return _wallet?.Wallet is { } wallet && wallet.TryGetValue(currency, out var held) ? held : 0;
    }

    /// <summary>
    /// The signed difference one accepted choice made, as rows the screen can print.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Three readouts diffed, and the wallet read off the events.</b> Gold, hit points and the
    /// fixed dice held are all on the run's own row, and an HP movement emits no event at all — so
    /// those can only be diffed. A wallet movement is the other way round: the profile row an outcome
    /// hands back is not the one that moved, so the <see cref="CurrencyChanged"/> events are the only
    /// honest source for it.
    /// </para>
    /// <para>
    /// 🔒 Gold is skipped on the event side, because it is already the diffed row above and one
    /// movement drawn twice is a number a player cannot reconcile. Two movements of the SAME wallet
    /// currency — a card that charges Crowns and pays Crowns — are summed for the same reason: two
    /// rows under one caption read as two separate answers.
    /// </para>
    /// <para>
    /// A row that moved by nothing is not drawn. That is what leaves the panel empty on the
    /// commonest outcome this build has, which is the state <see cref="StatusText"/> then names.
    /// </para>
    /// </remarks>
    private IReadOnlyList<EventResultLine> WhatMoved(
        RunSnapshot before, RunSnapshot after, IReadOnlyList<DomainEvent> events)
    {
        var lines = new List<EventResultLine>(4);

        Add(lines, GoldLabelKey, after.Gold - before.Gold, fromWallet: false);
        Add(lines, HpLabelKey, after.CurrentHp - before.CurrentHp, fromWallet: false);
        Add(
            lines,
            FixedDiceLabelKey,
            DiceHeld(after.FixedDice) - DiceHeld(before.FixedDice),
            fromWallet: false);

        var order = new List<CurrencyId>(events.Count);
        var totals = new Dictionary<CurrencyId, long>();

        foreach (var change in events.OfType<CurrencyChanged>())
        {
            if (change.Id == CurrencyId.GOLD)
            {
                continue;
            }

            if (!totals.ContainsKey(change.Id))
            {
                order.Add(change.Id);
            }

            totals[change.Id] = totals.GetValueOrDefault(change.Id) + change.Delta;
        }

        foreach (var currency in order)
        {
            Add(lines, CurrencyNameKeyOf(currency), totals[currency], fromWallet: true);
        }

        return lines.AsReadOnly();
    }

    private void Add(List<EventResultLine> lines, string labelKey, long delta, bool fromWallet)
    {
        if (delta != 0)
        {
            lines.Add(new EventResultLine(_strings.Resolve(labelKey), delta, fromWallet));
        }
    }

    /// <summary>How many fixed dice a run holds, whatever numbers they are.</summary>
    /// <remarks>
    /// Summed rather than counted by number: the row says how many dice the card gave or took, and
    /// which faces they are is the die tray's answer rather than this panel's.
    /// </remarks>
    private static long DiceHeld(IReadOnlyDictionary<int, int>? dice)
    {
        if (dice is null)
        {
            return 0;
        }

        long held = 0;

        foreach (var count in dice.Values)
        {
            held += count;
        }

        return held;
    }

    /// <summary>The caption key for one currency, which <c>tuning/currencies.json</c> owns.</summary>
    /// <remarks>
    /// 🔒 Eight named arms rather than the enum member folded to lower case: the key is a string
    /// another document authors, so deriving it from a C# identifier would silently re-point every
    /// caption on this screen the day the member is renamed. The last arm keeps a currency this
    /// screen does not know visible — <see cref="LocaleStringCatalogue.Resolve"/> answers a key no
    /// locale carries with the key itself, and a dotted identifier on screen is a defect one search
    /// finds.
    /// </remarks>
    private static string CurrencyNameKeyOf(CurrencyId currency) => currency switch
    {
        CurrencyId.GOLD => GoldCurrencyNameKey,
        CurrencyId.CROWNS => CrownsCurrencyNameKey,
        CurrencyId.SOUL_SHARDS => SoulShardsCurrencyNameKey,
        CurrencyId.ENERGY => EnergyCurrencyNameKey,
        CurrencyId.ENHANCE_STONES => EnhanceStonesCurrencyNameKey,
        CurrencyId.MERGE_DUST => MergeDustCurrencyNameKey,
        CurrencyId.BEAST_FEED => BeastFeedCurrencyNameKey,
        CurrencyId.HONOR => HonorCurrencyNameKey,
        _ => CurrencyNameKeyPrefix + currency + CurrencyNameKeySuffix,
    };
}
