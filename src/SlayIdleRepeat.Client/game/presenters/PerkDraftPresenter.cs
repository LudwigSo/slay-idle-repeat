using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Perks;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Perk Draft screen has got with the read everything it draws depends on.</summary>
public enum PerkDraftStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run was read and has a draft open.</summary>
    Ready = 2,

    /// <summary>
    /// The run was read and has no draft open. Named rather than folded into <see cref="RunMissing"/>:
    /// the run is fine, and this is also the state a finished draft leaves behind.
    /// </summary>
    NoDraft = 3,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,
}

/// <summary>What one submission from this screen did.</summary>
public enum PerkDraftSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering — a screen that
    /// collapsed them would tell a player their pick was illegal when the network dropped.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>
/// One <c>DRAFT</c> luck-protection counter as a row on this screen: the caption, the numbers, and
/// whether the guarantee is currently due at all.
/// </summary>
/// <remarks>
/// 🔒 Two fields, not four, and deliberately: the arithmetic that turns a counter and a rung into
/// a countdown belongs to <c>DraftView</c>, and a row carrying the raw numbers would invite a second
/// screen to subtract them differently. <c>24</c> §1.1's Authority rule is that the client displays
/// and never computes.
/// </remarks>
/// <param name="Label">The counter's caption, already resolved through the locale catalogue.</param>
/// <param name="Value">
/// The numbers beside it: <c>drafts-remaining/rung</c> while the guarantee is due, and the rung alone
/// while it is not. Numerals and a separator only — never a word, because words are the caption's.
/// </param>
/// <param name="Live">
/// Whether the guarantee can fire as the run stands. False rows still show their rung, and
/// <c>PerkDraftPresenter.GuaranteeNotDueBlockText</c> is the sentence that says why.
/// </param>
public sealed record PerkDraftGuaranteeRow(string Label, string Value, bool Live);

/// <summary>One card of the open draft, as this screen draws it.</summary>
/// <param name="OptionIndex">What <c>PICK_PERK</c> carries for this card.</param>
/// <param name="PerkId">The perk offered.</param>
/// <param name="Name">The perk's player-facing name, already resolved.</param>
/// <param name="Category">The perk's category, which the colour bar is drawn from.</param>
/// <param name="Rarity">The perk's own rarity band, which the gem is drawn from.</param>
/// <param name="IconId">The icon asset id.</param>
/// <param name="IsUpgrade">Whether taking this raises an already-owned copy rather than granting a fresh one.</param>
/// <param name="NewTier">The tier taking this option lands on.</param>
/// <param name="EffectText">
/// The perk's sentence with its real numbers substituted, or <c>null</c> when the authored data
/// cannot answer every token in it. Never a half-substituted string.
/// </param>
/// <param name="UnresolvedTokens">
/// The tokens that stopped the sentence, for the log. Empty on a card that rendered.
/// </param>
/// <param name="SynergyPerkIds">
/// Owned perks this option interacts with, as the projection carries them — <c>PK_*</c> IDS, for the
/// log. 🔒 Never drawn: <see cref="PerkDraftPresenter.SynergyLine"/> is what a card shows, and it
/// names each of these through the catalogue.
/// </param>
public sealed record PerkDraftCard(
    int OptionIndex,
    string PerkId,
    string Name,
    PerkCategory Category,
    PerkRarity Rarity,
    string IconId,
    bool IsUpgrade,
    int NewTier,
    string? EffectText,
    IReadOnlyList<string> UnresolvedTokens,
    IReadOnlyList<string> SynergyPerkIds);

/// <summary>
/// Drives the Perk Draft screen: the three cards on offer, the two ways off the screen that cost
/// something, and the guarantee counters that sit beneath them.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>This screen never advances itself.</b> Nothing here picks, skips or rerolls except the four
/// public methods below, each of which is a player's press. There is no timeout that takes a card,
/// no auto-pick for a single legal option, and no path from reading the run to submitting anything.
/// A draft that chose for the player would spend the one decision a run is actually made of — and it
/// would do it silently, because the run comes back already changed and the screen would look like
/// it had simply been slow.
/// </para>
/// <para>
/// 🔒 <b>This screen draws no ad reroll, no fourth ad card and no free-reroll count, because none of
/// those exist.</b> The command that grants an ad reward is not built, so nothing here may offer or
/// explain an ad. And the free-reroll allowance the design describes — one per stage, accumulating,
/// plus one granted by a skip — is not implemented at all: <c>REROLL_DRAFT</c> charges Gold on every
/// call with no cap and no counter, <c>SKIP_DRAFT</c> grants no charge, and nothing persisted counts
/// draft rerolls. So the reroll on offer shows its Gold price and the player's balance, and nothing is
/// drawn for a count or a control the game does not have.
/// </para>
/// <para>
/// 🔒 <b>The three <c>DRAFT</c> luck-protection counters are shown, always.</b> <c>24</c> §1.1's
/// Visibility rule is a 🔒 — <em>"a hidden pity system is indistinguishable from no pity system and
/// buys none of the goodwill it costs to build"</em> — and its Disclosure rule makes stating every
/// <c>N</c> in §4 on its class's own screen a store-policy requirement on both platforms. <c>DRAFT</c>
/// is that class and S07 is that screen. 🔒 <b>These are not the free-reroll count.</b> That number
/// does not exist anywhere, as the paragraph above says; these three do, they are on the run, they
/// drive real forced options, and <c>DraftView.Guarantees</c> is where they and their authored rungs
/// are read. Two of §4.7's five rules carry no counter and so get no row — the Sustain anti-brick is a
/// state predicate and the Codex bias is a weight.
/// </para>
/// <para>
/// 🔒 <b>The counter rows carry captions and numerals, never a composed sentence.</b> The locale
/// catalogue has no interpolation at all, so a magnitude can never be written into a translated
/// string — the row is the caption from loc beside the numbers drawn separately, which is the same
/// shape the reroll price already uses and the same reason.
/// </para>
/// <para>
/// 🔒 <b>A card whose numbers cannot be rendered says so.</b> Four of the shipped perks carry
/// description tokens that name no field in their own authored effect data; the renderer answers
/// with the offending tokens instead of a sentence, and this screen draws a named line in place of
/// a half-substituted claim about what a perk does.
/// </para>
/// </remarks>
public sealed class PerkDraftPresenter
{
    /// <summary>
    /// Separates a countdown from the rung it is counting towards, the way the scene half separates a
    /// price from the balance it is read against.
    /// </summary>
    private const char OverSeparator = '/';

    private const string TitleNameKey = "loc.perk_draft.title.name";
    private const string SynergyLabelKey = "loc.perk_draft.synergy.label";
    private const string RerollCostLabelKey = "loc.perk_draft.reroll_cost.label";
    private const string SkipRewardLabelKey = "loc.perk_draft.skip_reward.label";
    private const string UpgradeBadgeKey = "loc.perk_draft.upgrade.badge";
    private const string RerollActionKey = "loc.perk_draft.reroll.action";
    private const string SkipActionKey = "loc.perk_draft.skip.action";
    private const string LegendaryPityLabelKey = "loc.perk_draft.legendary_pity.label";
    private const string QualityFloorLabelKey = "loc.perk_draft.quality_floor.label";
    private const string UpgradeFamineLabelKey = "loc.perk_draft.upgrade_famine.label";
    private const string GuaranteeNotDueBlockKey = "loc.perk_draft.guarantee_not_due.block";
    private const string LoadingStatusKey = "loc.perk_draft.loading.status";
    private const string NoDraftStatusKey = "loc.perk_draft.no_draft.status";
    private const string RunMissingStatusKey = "loc.perk_draft.run_missing.status";
    private const string ReadUnavailableStatusKey = "loc.perk_draft.read_unavailable.status";
    private const string CardsUnavailableStatusKey = "loc.perk_draft.cards_unavailable.status";
    private const string EffectNumbersUnavailableStatusKey = "loc.perk_draft.effect_numbers_unavailable.status";
    private const string RefusedStatusKey = "loc.perk_draft.refused.status";
    private const string RerollUnaffordableStatusKey = "loc.perk_draft.reroll_unaffordable.status";
    private const string HostUnavailableStatusKey = "loc.perk_draft.host_unavailable.status";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Joins the owned perks one card interacts with, in the order the projection lists them.</summary>
    private const string SynergyJoin = " · ";

    /// <summary>
    /// The tier badge's numerals, indexed from tier one.
    /// </summary>
    /// <remarks>
    /// 🔒 Numerals rather than wording, which is why they live in code at all: a tier badge reading
    /// "II" is the same mark in every locale, and the one translated part of an upgrade badge is the
    /// word in front of it. The two are composed here rather than in the rules layer, because a
    /// composed English badge built beside the projection would be a user-facing string outside the
    /// loc system.
    /// </remarks>
    private static readonly string[] TierNumerals = ["I", "II", "III"];

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly RunId _run;

    /// <summary>
    /// The perk catalogue, read once and kept, because a synergy hint is a lookup per card per
    /// redraw and this screen redraws twice for every press.
    /// </summary>
    /// <remarks>
    /// Safe to keep for exactly as long as this screen lives: the snapshot it is read out of is the
    /// one this presenter was built against and never changes underneath it. Null while nothing has
    /// asked, and null again after a read that could not answer — see <see cref="SynergyLine"/>.
    /// </remarks>
    private PerkCatalogue? _catalogue;

    private bool _submissionInFlight;

    /// <summary>Builds the screen over the host, the strings, the content set and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the draft is projected against.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public PerkDraftPresenter(
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

    /// <summary>How far the read this screen depends on has got.</summary>
    public PerkDraftStage Stage { get; private set; } = PerkDraftStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The three cards on offer, in the order <c>PICK_PERK</c> indexes them.</summary>
    /// <remarks>
    /// Empty while no draft is open, and also empty when the projection could not be built at all —
    /// <see cref="CardsAvailable"/> is what tells those apart, because a draft with no cards is a
    /// screen that has to say why rather than one that has nothing to say.
    /// </remarks>
    public IReadOnlyList<PerkDraftCard> Cards { get; private set; } = [];

    /// <summary>Whether the cards could be projected at all.</summary>
    /// <remarks>
    /// 🔒 False rather than a crash. The projection reads the perk catalogue and the draft economy
    /// out of the content set, and a content set missing either is a real failure mode — one this
    /// screen reports in words instead of taking the client down on the tile a run's whole build is
    /// chosen on.
    /// <para>
    /// 🔒 <b>What is caught is the content set failing to answer, and only that</b> — the exceptions
    /// a <c>ContentSnapshot</c> read raises. A blanket catch would turn a programming error inside
    /// the projection into the same quiet sentence, and the screen would report a missing document
    /// while the real fault was a defect in the derivation the player is about to commit a run to.
    /// </para>
    /// </remarks>
    public bool CardsAvailable { get; private set; }

    /// <summary>The Gold a reroll costs, as authored. Zero while the cards are unavailable.</summary>
    public long RerollGoldCost { get; private set; }

    /// <summary>The Gold a skip pays, as authored. Zero while the cards are unavailable.</summary>
    public long SkipGoldReward { get; private set; }

    /// <summary>The run's Gold balance, which is what the reroll's price is read against.</summary>
    public long Gold { get; private set; }

    /// <summary>
    /// The three <c>DRAFT</c> counter rows, in the order the guarantees fire. Empty only while there
    /// is no draft to read at all — never trimmed because a counter stands at zero.
    /// </summary>
    /// <remarks>
    /// 🔒 Every number in a row comes off <c>DraftView.Guarantees</c>. Nothing here subtracts,
    /// clamps or counts: <c>24</c> §1.1 makes the client a display for these counters and says so in
    /// the same breath as its Authority rule — <em>"the client displays; it never computes, never
    /// predicts, never resets"</em>.
    /// </remarks>
    public IReadOnlyList<PerkDraftGuaranteeRow> Guarantees { get; private set; } = [];

    /// <summary>
    /// Why a row is showing its rung without a countdown, resolved — empty while all three are due.
    /// </summary>
    /// <remarks>
    /// 🔒 The one absence on this screen that gets a sentence, because it is an absence in a mechanic
    /// that is built: the upgrade famine is live, and simply cannot be owed by a run that holds no perk
    /// below its top tier. A rung with no countdown and no reason would read as a counter that has
    /// stopped working.
    /// </remarks>
    public string GuaranteeNotDueBlockText =>
        Guarantees.Any(g => !g.Live) ? _strings.Resolve(GuaranteeNotDueBlockKey) : NothingLeftToSay;

    /// <summary>
    /// Whether the three numbers below are showing their exact values rather than their shortened
    /// ones.
    /// </summary>
    /// <remarks>
    /// 🔒 The state lives here rather than in the scene, because "which form is on the page" is the
    /// half of this rule that can be proven. The gesture that sets it is the scene's — a long press
    /// is an engine event — but what a long press MEANS to the numbers is decided where a test can
    /// read it, and a screen that shortened a number with no way back to the exact one would be
    /// rounding a price a player is about to pay.
    /// </remarks>
    public bool FullValuesRevealed { get; private set; }

    /// <summary>The Gold a reroll costs, as a player reads it.</summary>
    public string RerollGoldCostText => Readout(RerollGoldCost);

    /// <summary>The Gold a skip pays, as a player reads it.</summary>
    public string SkipGoldRewardText => Readout(SkipGoldReward);

    /// <summary>The run's Gold balance, as a player reads it.</summary>
    public string GoldText => Readout(Gold);

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The lead-in a synergy hint is written after, resolved.</summary>
    public string SynergyLabel => _strings.Resolve(SynergyLabelKey);

    /// <summary>The caption the reroll's Gold price is drawn beside, resolved.</summary>
    public string RerollCostLabel => _strings.Resolve(RerollCostLabelKey);

    /// <summary>The caption the skip's Gold reward is drawn beside, resolved.</summary>
    public string SkipRewardLabel => _strings.Resolve(SkipRewardLabelKey);

    /// <summary>The reroll control's caption, resolved.</summary>
    public string RerollText => _strings.Resolve(RerollActionKey);

    /// <summary>The skip control's caption, resolved.</summary>
    public string SkipText => _strings.Resolve(SkipActionKey);

    /// <summary>The line saying what the screen is doing while its cards are not an answer, resolved.</summary>
    public string StatusText => Stage switch
    {
        PerkDraftStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        PerkDraftStage.Ready => CardsAvailable
            ? NothingLeftToSay
            : _strings.Resolve(CardsUnavailableStatusKey),
        PerkDraftStage.NoDraft => _strings.Resolve(NoDraftStatusKey),
        PerkDraftStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// 🔒 Three outcomes, three sentences. A fault is read first because a faulted submission
    /// carries no rejection at all. A shortfall gets its own sentence because it is the one refusal
    /// here a player can act on — they can go and earn the Gold — and telling them the reroll was
    /// simply illegal would send them away from a control that works. Every other refusal shares the
    /// generic sentence and carries its identity to the log on <see cref="RulesRejection"/>.
    /// <para>
    /// 🔒 The shortfall is PAIRED with the reroll, on the board's own precedent for its exhausted
    /// reroll. Unpaired it would be correct today — nothing else this screen submits spends anything
    /// — and would stop being correct the day a second spender lands here, printing the reroll's
    /// price under another command's refusal with nothing going red.
    /// </para>
    /// </remarks>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection switch
        {
            null => NothingLeftToSay,
            RejectionReason.INSUFFICIENT_FUNDS when RerollWasTheLastSubmission =>
                _strings.Resolve(RerollUnaffordableStatusKey),
            _ => _strings.Resolve(RefusedStatusKey),
        };

    /// <summary>Whether the last command that reached the host was the reroll.</summary>
    /// <remarks>
    /// Set by the submission funnel before the command goes out, and cleared there for every other
    /// command — the same shape and the same reason as the board's exhausted-reroll latch. It is the
    /// pairing <see cref="RejectionText"/> reads; it reports nothing on its own and is deliberately
    /// not public.
    /// </remarks>
    private bool RerollWasTheLastSubmission { get; set; }

    /// <summary>The badge one card carries — the numeral, or the upgrade wording and the numeral.</summary>
    /// <param name="card">The card to badge.</param>
    /// <exception cref="ArgumentNullException"><paramref name="card"/> is null.</exception>
    public string TierBadge(PerkDraftCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.IsUpgrade
            ? _strings.Resolve(UpgradeBadgeKey) + Numeral(card.NewTier)
            : Numeral(card.NewTier);
    }

    /// <summary>
    /// The effect line one card shows: its rendered sentence, or the named line saying the numbers
    /// are unavailable. 🔒 Never a sentence with a token still in it.
    /// </summary>
    /// <param name="card">The card to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="card"/> is null.</exception>
    public string EffectLine(PerkDraftCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.EffectText ?? _strings.Resolve(EffectNumbersUnavailableStatusKey);
    }

    /// <summary>
    /// The owned perks one card interacts with, <b>by name</b>, or empty when it interacts with none.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Named here because only here can be.</b> The projection carries <c>PK_*</c> ids — they
    /// are what a run stores and what a log needs — and a hint reading "Works with PK_APEX" names a
    /// database row at somebody playing a game. Turning one into the perk's own authored name is a
    /// catalogue lookup against the loaded content set, which this side of the boundary holds and a
    /// scene does not.
    /// <para>
    /// An id the catalogue cannot name is dropped rather than printed: it can only appear on a run
    /// that outlived the content version which authored it, the projection already excludes those
    /// from a hint, and an id shown raw is the exact thing this member exists to prevent. Empty for
    /// a content set with no catalogue at all — a state in which no card exists to hint about, since
    /// <see cref="CardsAvailable"/> is false for the same read failure.
    /// </para>
    /// </remarks>
    /// <param name="card">The card to hint for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="card"/> is null.</exception>
    public string SynergyLine(PerkDraftCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (card.SynergyPerkIds.Count == 0 || Catalogue() is not { } catalogue)
        {
            return NothingLeftToSay;
        }

        var names = new List<string>(card.SynergyPerkIds.Count);

        foreach (var perkId in card.SynergyPerkIds)
        {
            if (catalogue.Contains(perkId))
            {
                names.Add(catalogue.Find(perkId).Name);
            }
        }

        return string.Join(SynergyJoin, names);
    }

    /// <summary>Shows the exact value of every number on this screen — a long press is holding it.</summary>
    public void RevealFullValues() => FullValuesRevealed = true;

    /// <summary>And puts the shortened form back, which is what the press ending means.</summary>
    public void ConcealFullValues() => FullValuesRevealed = false;

    /// <summary>Reads the run this screen is about and projects the draft it has open.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception)
        {
            Stage = PerkDraftStage.ReadUnavailable;
        }
    }

    /// <summary>Submits <c>PICK_PERK</c> for one of the cards on offer.</summary>
    /// <param name="optionIndex">The chosen card's index, as <see cref="Cards"/> lists it.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<PerkDraftSubmission> PickAsync(int optionIndex, CancellationToken ct)
    {
        // Checked against the cards actually on offer rather than against a bare range: an index the
        // screen is not showing would come back carrying a value four other things share, and the
        // sentence naming the real cause would be replaced by one naming nothing.
        if (Stage != PerkDraftStage.Ready || optionIndex < 0 || optionIndex >= Cards.Count)
        {
            return PerkDraftSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new PickPerkCommand(optionIndex), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>REROLL_DRAFT</c>, which costs the authored Gold and has no free tier.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<PerkDraftSubmission> RerollAsync(CancellationToken ct)
    {
        if (Stage != PerkDraftStage.Ready)
        {
            return PerkDraftSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new RerollDraftCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>SKIP_DRAFT</c>, which pays the authored Gold and grants nothing else.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<PerkDraftSubmission> SkipAsync(CancellationToken ct)
    {
        if (Stage != PerkDraftStage.Ready)
        {
            return PerkDraftSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new SkipDraftCommand(), ct).ConfigureAwait(false);
    }

    /// <remarks>
    /// 🔒 The latch is taken BEFORE the await, not after it. Taken afterwards, a second press
    /// arriving while the first is in flight finds it unset and submits again — which is how a
    /// double-tap takes two perks, and how it shipped once already on another screen in this
    /// milestone.
    /// <para>
    /// 🔒 <see cref="HostFaulted"/> and <see cref="RerollWasTheLastSubmission"/> are settled here
    /// before the command goes out, for the reason the board's funnel gives about its own latch:
    /// settled afterwards they survive every path that returns early — which is every refusal — and
    /// one command's sentence is printed under the next command's answer.
    /// </para>
    /// </remarks>
    private async Task<PerkDraftSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return PerkDraftSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;
        RerollWasTheLastSubmission = command is RerollDraftCommand;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                return PerkDraftSubmission.RefusedByRules;
            }

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a draft the command has closed.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                Carry(moved);
            }

            return PerkDraftSubmission.Submitted;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting
            // one would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return PerkDraftSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: the reroll and the skip are controls a player presses again
            // after one that never answered, and a latch left shut strands the run on the draft.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Run is not { } run)
        {
            Stage = PerkDraftStage.RunMissing;
            return;
        }

        Carry(run);
    }

    private void Carry(RunSnapshot run)
    {
        Gold = run.Gold;

        if (!run.DraftPending)
        {
            Cards = [];
            CardsAvailable = false;
            RerollGoldCost = 0;
            SkipGoldReward = 0;
            Guarantees = [];
            Stage = PerkDraftStage.NoDraft;

            return;
        }

        Stage = PerkDraftStage.Ready;

        Project(run);
    }

    /// <remarks>See <see cref="CardsAvailable"/> for why only a content read's failure is caught.</remarks>
    private void Project(RunSnapshot run)
    {
        try
        {
            var draft = DraftView.Project(run, _content);

            Cards = draft is null ? [] : Draw(draft);
            CardsAvailable = draft is not null;
            RerollGoldCost = draft?.RerollGoldCost ?? 0;
            SkipGoldReward = draft?.SkipGoldReward ?? 0;
            Guarantees = draft is null ? [] : Rows(draft);
        }
        catch (ContentException)
        {
            Cards = [];
            CardsAvailable = false;
            RerollGoldCost = 0;
            SkipGoldReward = 0;

            // Emptied with the cards, on the same argument: a content read that failed answered
            // nothing, and rows left standing from an earlier read would be a disclosure about a
            // draft this screen can no longer see.
            Guarantees = [];
        }
    }

    /// <summary>The projection's counters as rows, each with its caption already resolved.</summary>
    /// <remarks>
    /// 🔒 The countdown is drawn only while the guarantee is live. A row that is not due shows its
    /// authored rung alone, because <c>24</c> §1.1's Disclosure rule wants the <c>N</c> stated whatever
    /// the run is doing, while a countdown on a guarantee that cannot fire would be a promise about a
    /// draft that is not coming.
    /// </remarks>
    private IReadOnlyList<PerkDraftGuaranteeRow> Rows(DraftView draft)
    {
        var rows = new PerkDraftGuaranteeRow[draft.Guarantees.Count];

        for (var index = 0; index < rows.Length; index++)
        {
            var standing = draft.Guarantees[index];

            rows[index] = new PerkDraftGuaranteeRow(
                _strings.Resolve(CaptionKey(standing.Kind)),
                standing.Live
                    ? Count(standing.DraftsUntilForced) + OverSeparator + Count(standing.ForcedOnDraft)
                    : Count(standing.ForcedOnDraft),
                standing.Live);
        }

        return Array.AsReadOnly(rows);
    }

    /// <summary>The caption one counter's row is drawn with.</summary>
    /// <remarks>
    /// 🔒 Three keys rather than one with the guarantee's name substituted in: the catalogue has no
    /// interpolation, and a caption assembled from a stem plus a translated fragment is exactly the
    /// construction X-04 exists to forbid. A kind this build was never taught throws rather than
    /// borrowing a neighbour's caption — a wrong sentence beside a real number is worse than a crash
    /// in a screen whose whole job here is disclosure.
    /// </remarks>
    private static string CaptionKey(DraftGuaranteeKind kind) => kind switch
    {
        DraftGuaranteeKind.LegendaryPity => LegendaryPityLabelKey,
        DraftGuaranteeKind.QualityFloor => QualityFloorLabelKey,
        DraftGuaranteeKind.UpgradeFamine => UpgradeFamineLabelKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "No caption is authored for this DRAFT guarantee."),
    };

    /// <summary>
    /// A draft count as a player reads it. Never abbreviated: a run holds a handful of drafts, so
    /// these are two digits and the thousands rule has nothing to act on.
    /// </summary>
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The projection's options as cards, indexed the way <c>PICK_PERK</c> indexes them.</summary>
    private static IReadOnlyList<PerkDraftCard> Draw(DraftView draft)
    {
        var cards = new PerkDraftCard[draft.Options.Count];

        for (var slot = 0; slot < cards.Length; slot++)
        {
            var option = draft.Options[slot];

            cards[slot] = new PerkDraftCard(
                slot,
                option.PerkId,
                option.Name,
                option.Category,
                option.Rarity,
                option.IconId,
                option.IsUpgrade,
                option.NewTier,
                option.EffectText.Text,
                option.EffectText.UnresolvedTokens,
                option.SynergyPerkIds);
        }

        return Array.AsReadOnly(cards);
    }

    /// <summary>Whichever form of a number this screen is currently showing.</summary>
    private string Readout(long value) =>
        FullValuesRevealed ? PlayerNumber.Full(value) : PlayerNumber.Abbreviated(value);

    /// <remarks>
    /// See <see cref="CardsAvailable"/> for why only a content read's failure is caught: a set with
    /// no catalogue is a screen that has already said so, and a hint is not the place to learn it a
    /// second time.
    /// </remarks>
    private PerkCatalogue? Catalogue()
    {
        if (_catalogue is not null)
        {
            return _catalogue;
        }

        try
        {
            _catalogue = PerkCatalogue.Read(_content);
        }
        catch (ContentException)
        {
            return null;
        }

        return _catalogue;
    }

    /// <remarks>
    /// Empty for a tier this build has no numeral for, rather than a fabricated one: the catalogue
    /// authors three tiers and a fourth would be a badge nobody has decided how to write.
    /// </remarks>
    private static string Numeral(int tier) =>
        tier >= 1 && tier <= TierNumerals.Length ? TierNumerals[tier - 1] : NothingLeftToSay;
}
