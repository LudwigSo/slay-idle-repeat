using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Primitives;

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
/// <param name="SynergyPerkIds">Owned perks this option interacts with. Empty when there are none.</param>
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
/// something, and the three separate absences that sit around them.
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
/// 🔴 <b>Three separate absences share this screen and must never share a sentence.</b> The ad
/// reroll is an ad reward whose command is deferred to a later milestone. The fourth-option ad card
/// is a different affordance waiting on the same milestone, in a different place, with its own
/// layout slot kept so it can be filled rather than re-laid-out. And the free-reroll allowance the
/// design describes — one per stage, accumulating, plus one granted by a skip — <b>is not
/// implemented at all</b>: see <see cref="TheFreeRerollAllowanceDoesNotExist"/>. The reroll on offer
/// is Gold-priced and uncapped, so the control shows its price and the player's balance, and no
/// remaining-free count is invented to sit beside it.
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
    /// ⚠️ Deliberately not built, and named so it can be found. The design authors a free-reroll
    /// economy this build does not have, and a screen that showed a remaining-free count would be
    /// showing a number that exists nowhere.
    /// </summary>
    private const string TheFreeRerollAllowanceDoesNotExist =
        "The design set describes one free draft reroll per stage, accumulating up to three, plus " +
        "one more granted by skipping a draft. None of it is in the code: REROLL_DRAFT charges " +
        "Gold on every call with no cap and no counter, SKIP_DRAFT pays Gold and grants no charge, " +
        "and no persisted field anywhere counts draft rerolls. So there is no remaining-free number " +
        "to show, no cap to draw a meter against, and nothing this screen could read if it wanted " +
        "one. It shows the authored Gold price instead, and names the absence in its own sentence " +
        "— distinct from the ad reroll's, which is a real command deferred to a later milestone " +
        "rather than a mechanic that was never written.";

    /// <summary>
    /// ⚠️ Deliberately not granted, and named so it can be found. Both ad affordances on this screen
    /// resolve to one deferred command, and neither may be given a placement id, a cap or a reward
    /// here.
    /// </summary>
    private const string TheAdRewardCommandIsDeferred =
        "CLAIM_AD_REWARD is registered as deferred to a later milestone, so no screen in this build " +
        "grants an ad reward. The two ad affordances here keep their layout slots — the quieter " +
        "reroll beside the priced one, and the dashed fourth card below the three — so the " +
        "milestone that lands them fills a slot rather than re-laying out the screen. Each carries " +
        "its OWN sentence: they are two different offers, and a player told the fourth card is " +
        "unavailable when it was the reroll they pressed learns nothing.";

    private const string TitleNameKey = "loc.perk_draft.title.name";
    private const string AdFourthOptionNameKey = "loc.perk_draft.ad_fourth_option.name";
    private const string SynergyLabelKey = "loc.perk_draft.synergy.label";
    private const string RerollCostLabelKey = "loc.perk_draft.reroll_cost.label";
    private const string SkipRewardLabelKey = "loc.perk_draft.skip_reward.label";
    private const string UpgradeBadgeKey = "loc.perk_draft.upgrade.badge";
    private const string RerollActionKey = "loc.perk_draft.reroll.action";
    private const string AdRerollActionKey = "loc.perk_draft.ad_reroll.action";
    private const string SkipActionKey = "loc.perk_draft.skip.action";
    private const string AdRerollBlockKey = "loc.perk_draft.ad_reroll_deferred.block";
    private const string AdFourthOptionBlockKey = "loc.perk_draft.ad_fourth_option_deferred.block";
    private const string FreeRerollBlockKey = "loc.perk_draft.free_reroll_unbuilt.block";
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


    /// <summary>Whether the ad reroll may be taken. 🔒 Never — see <see cref="TheAdRewardCommandIsDeferred"/>.</summary>
    public bool AdRerollAvailable => false;

    /// <summary>Whether the fourth ad card may be taken. 🔒 Never — same reason, different affordance.</summary>
    public bool AdFourthOptionAvailable => false;

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The dashed fourth card's own caption, resolved.</summary>
    public string AdFourthOptionName => _strings.Resolve(AdFourthOptionNameKey);

    /// <summary>The lead-in a synergy hint is written after, resolved.</summary>
    public string SynergyLabel => _strings.Resolve(SynergyLabelKey);

    /// <summary>The caption the reroll's Gold price is drawn beside, resolved.</summary>
    public string RerollCostLabel => _strings.Resolve(RerollCostLabelKey);

    /// <summary>The caption the skip's Gold reward is drawn beside, resolved.</summary>
    public string SkipRewardLabel => _strings.Resolve(SkipRewardLabelKey);

    /// <summary>The reroll control's caption, resolved.</summary>
    public string RerollText => _strings.Resolve(RerollActionKey);

    /// <summary>The quieter ad reroll's caption, resolved.</summary>
    public string AdRerollText => _strings.Resolve(AdRerollActionKey);

    /// <summary>The skip control's caption, resolved.</summary>
    public string SkipText => _strings.Resolve(SkipActionKey);

    /// <summary>Why the ad reroll cannot be taken, resolved.</summary>
    public string AdRerollBlockText => _strings.Resolve(AdRerollBlockKey);

    /// <summary>Why the fourth ad card cannot be taken, resolved.</summary>
    public string AdFourthOptionBlockText => _strings.Resolve(AdFourthOptionBlockKey);

    /// <summary>
    /// Why there is no free reroll to show, resolved — a different absence from either ad slot's.
    /// </summary>
    public string FreeRerollBlockText => _strings.Resolve(FreeRerollBlockKey);

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
    public string TierBadge(PerkDraftCard card) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

    /// <summary>
    /// The effect line one card shows: its rendered sentence, or the named line saying the numbers
    /// are unavailable. 🔒 Never a sentence with a token still in it.
    /// </summary>
    /// <param name="card">The card to describe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="card"/> is null.</exception>
    public string EffectLine(PerkDraftCard card) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

    /// <summary>Reads the run this screen is about and projects the draft it has open.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

    /// <summary>Submits <c>PICK_PERK</c> for one of the cards on offer.</summary>
    /// <param name="optionIndex">The chosen card's index, as <see cref="Cards"/> lists it.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<PerkDraftSubmission> PickAsync(int optionIndex, CancellationToken ct) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

    /// <summary>Submits <c>REROLL_DRAFT</c>, which costs the authored Gold and has no free tier.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task<PerkDraftSubmission> RerollAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

    /// <summary>Submits <c>SKIP_DRAFT</c>, which pays the authored Gold and grants nothing else.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task<PerkDraftSubmission> SkipAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: written against the failing cases in PerkDraftPresenterTests " +
            "and filled in by the implementation phase.");

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
    private Task<PerkDraftSubmission> SubmitAsync(GameCommand command, CancellationToken ct) =>
        throw new NotImplementedException(
            "M7-07 phase 1 skeleton: the single submission funnel, written against the failing " +
            "cases in PerkDraftPresenterTests and filled in by the implementation phase.");
}
