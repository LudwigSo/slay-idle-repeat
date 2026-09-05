using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Minigame screen's string keys, its authored timing-bar numbers, and the content sets its
/// cases run against.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The timing-bar numbers here are NOT the shipped ones.</b> A fixture that mirrored
/// <c>game-data</c> would let a game with the numbers hard-coded pass every case in this suite, and
/// the whole point of reading them is that a designer can retune them. The window is deliberately
/// wide and the sweep deliberately slow, so the boundary cases land on figures no shipped value
/// produces.
/// </para>
/// <para>
/// 🔒 <b>The reward tables and the pity registry are the SHIPPED documents.</b> Those two are read by
/// Core's own projection, which validates them whole — a hand-authored stand-in would be a second
/// <c>game-data</c> that drifts, and the reward numbers are not what these cases are about.
/// </para>
/// <para>
/// Fixture string values are the key with a marker in front, so every string is unique, obviously
/// not the shipped copy, and impossible for a hard-coded literal to match by accident.
/// </para>
/// </remarks>
internal static class MinigameContent
{
    internal const string TitleNameKey = "loc.minigame.title.name";
    internal const string ChestPickNameKey = "loc.minigame.chest_pick.name";
    internal const string TimingBarNameKey = "loc.minigame.timing_bar.name";
    internal const string DiceDuelNameKey = "loc.minigame.dice_duel.name";
    internal const string MemoryRuneNameKey = "loc.minigame.memory_rune.name";

    internal const string ChestPickRuleKey = "loc.minigame.chest_pick.rule";
    internal const string TimingBarRuleKey = "loc.minigame.timing_bar.rule";
    internal const string DiceDuelRuleKey = "loc.minigame.dice_duel.rule";
    internal const string MemoryRuneRuleKey = "loc.minigame.memory_rune.rule";

    internal const string RewardsLabelKey = "loc.minigame.rewards.label";
    internal const string HitsLabelKey = "loc.minigame.hits.label";
    internal const string StrikesLeftLabelKey = "loc.minigame.strikes_left.label";
    internal const string GuaranteeLabelKey = "loc.minigame.guarantee.label";
    internal const string ResultLabelKey = "loc.minigame.result.label";

    /// <summary>The reward ladder's fixed-die column, the one column captioned from this screen.</summary>
    /// <remarks>
    /// A die is not a currency, so unlike Gold, Crowns, Beast Feed and Enhance Stones it has no
    /// <c>loc.currency.&lt;snake&gt;.name</c> key for the ladder to borrow.
    /// </remarks>
    internal const string FixedDiceLabelKey = "loc.minigame.fixed_dice.label";

    internal const string StrikeActionKey = "loc.minigame.strike.action";
    internal const string StepActionKey = "loc.minigame.step.action";
    internal const string PickChestActionKey = "loc.minigame.pick_chest.action";
    internal const string RollActionKey = "loc.minigame.roll.action";
    internal const string ContinueActionKey = "loc.minigame.continue.action";

    internal const string LoadingStatusKey = "loc.minigame.loading.status";
    internal const string PlayingStatusKey = "loc.minigame.playing.status";
    internal const string RunMissingStatusKey = "loc.minigame.run_missing.status";
    internal const string NotAtAMinigameStatusKey = "loc.minigame.not_at_a_minigame.status";
    internal const string ReadUnavailableStatusKey = "loc.minigame.read_unavailable.status";
    internal const string RulesUnavailableStatusKey = "loc.minigame.rules_unavailable.status";
    internal const string AlreadyResolvedStatusKey = "loc.minigame.already_resolved.status";
    internal const string RefusedStatusKey = "loc.minigame.refused.status";
    internal const string HostUnavailableStatusKey = "loc.minigame.host_unavailable.status";

    /// <summary>The prefix an outcome caption key is derived under, and its suffix.</summary>
    /// <remarks>
    /// 🔒 Derived from the authored token rather than mapped: a fourteenth reward row is then a
    /// content edit rather than a code edit. <c>MinigameOutcomeKeyAgreementTests</c> pins the
    /// derivation against the shipped tables from both ends.
    /// </remarks>
    internal const string OutcomeKeyPrefix = "loc.minigame.";

    internal const string OutcomeKeySuffix = ".outcome";

    /// <summary>The thirteen outcome tokens the four shipped reward tables author.</summary>
    internal static IReadOnlyList<string> OutcomeTokens { get; } =
    [
        "BRONZE", "SILVER", "GOLD",
        "HITS_0", "HITS_1", "HITS_2", "HITS_3",
        "LOSS", "WIN_2_1", "WIN_2_0",
        "FAIL_ROUND_1", "ROUND_1_ONLY", "BOTH_ROUNDS",
    ];

    /// <summary>The currency captions the reward rows borrow rather than authoring their own.</summary>
    internal static IReadOnlyList<string> CurrencyNameKeys { get; } =
    [
        "loc.currency.gold.name",
        "loc.currency.crowns.name",
        "loc.currency.beast_feed.name",
        "loc.currency.enhance_stones.name",
    ];

    /// <summary>The three arms this client draws, as the rules layer spells them.</summary>
    /// <remarks>
    /// 🔒 Transcribed here because <c>MinigameCatalogue</c> is internal to Core and this project
    /// cannot see it. <c>MinigamePresenterTests</c> holds these three against
    /// <see cref="MinigameArms.Built"/>, which is derived from the catalogue — so a rename in Core
    /// reddens that one case rather than quietly re-pointing every fixture in this suite.
    /// </remarks>
    internal const string ChestPick = "MG_CHEST_PICK";

    /// <inheritdoc cref="ChestPick"/>
    internal const string TimingBar = "MG_TIMING_BAR";

    /// <inheritdoc cref="ChestPick"/>
    internal const string DiceDuel = "MG_DICE_DUEL";

    /// <summary>Where the timing bar's authored numbers live.</summary>
    internal const string MinigamesDocument = "tuning/minigames.json";

    /// <summary>Where the reward tables and the pity registry live.</summary>
    internal const string CurrenciesDocument = "tuning/currencies.json";

    internal const string LuckDocument = "tuning/luck.json";

    // ------------------------------------------------------------------------------------------
    // The fixture timing bar. Deliberately unlike the shipped numbers.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Three strikes, which is the one number that cannot be varied: the tier is the hit count and
    /// the shipped <c>MG_TIMING_BAR</c> table authors four rows.
    /// </summary>
    internal const int Strikes = 3;

    /// <summary>A two-second there-and-back sweep, so the cursor is at 1 exactly one second in.</summary>
    internal const double SweepSeconds = 2.0;

    /// <summary>
    /// A quarter-bar window, so the scoring range is exactly <c>[0.25, 0.75]</c> and a boundary case
    /// lands on a figure with no floating-point argument about it.
    /// </summary>
    internal const double HitWindowHalfWidth = 0.25;

    /// <summary>An eighth of the bar per step, so four steps land exactly on the centre.</summary>
    internal const double ReducedMotionStepFraction = 0.125;

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('c', ContentVersion.HexLength));

    /// <summary>Every string key the Minigame screen renders.</summary>
    internal static IReadOnlyList<string> MinigameKeys { get; } =
    [
        TitleNameKey, ChestPickNameKey, TimingBarNameKey, DiceDuelNameKey, MemoryRuneNameKey,
        ChestPickRuleKey, TimingBarRuleKey, DiceDuelRuleKey, MemoryRuneRuleKey,
        RewardsLabelKey, HitsLabelKey, StrikesLeftLabelKey, GuaranteeLabelKey, ResultLabelKey,
        FixedDiceLabelKey,
        StrikeActionKey, StepActionKey, PickChestActionKey, RollActionKey, ContinueActionKey,
        LoadingStatusKey, PlayingStatusKey, RunMissingStatusKey, NotAtAMinigameStatusKey,
        ReadUnavailableStatusKey, RulesUnavailableStatusKey, AlreadyResolvedStatusKey,
        RefusedStatusKey, HostUnavailableStatusKey,
    ];

    /// <summary>The caption key one authored outcome token derives.</summary>
    internal static string OutcomeKeyFor(string token) =>
        OutcomeKeyPrefix + token.ToLowerInvariant() + OutcomeKeySuffix;

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) =>
        new(content, ScreenContent.English);

    /// <summary>
    /// The timing bar's authored numbers and the reward table its strike count is read against.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The reward table is here because the read needs it, not as scenery.</b>
    /// <c>TimingBarRules.Read</c> refuses a strike count the <c>MG_TIMING_BAR</c> table cannot pay,
    /// and a set holding the numbers alone leaves that comparison with nothing to compare against —
    /// every read would then throw for the missing document, which is a refusal that looks exactly
    /// like the one the strike-count cases are about while measuring nothing. The strings and the
    /// pity registry are still left out, so a read that reached for either would fail here.
    /// </remarks>
    internal static ContentSnapshot TimingBarOnly() =>
        new(FixtureStamp, [Minigames(), RewardTables()]);

    /// <summary>
    /// A content set with a timing bar whose numbers a case chose — for the refusals and the bounds.
    /// </summary>
    internal static ContentSnapshot TimingBarAuthoring(
        int strikes = Strikes,
        double sweepSeconds = SweepSeconds,
        double hitWindowHalfWidth = HitWindowHalfWidth,
        double reducedMotionStepFraction = ReducedMotionStepFraction) =>
        new(
            FixtureStamp,
            [
                Minigames(strikes, sweepSeconds, hitWindowHalfWidth, reducedMotionStepFraction),
                RewardTables(),
            ]);

    /// <summary>
    /// The authored numbers with no reward table at all — the shape a strikes-versus-rows check
    /// cannot be made over.
    /// </summary>
    /// <remarks>
    /// 🔒 Refused rather than read as "no disagreement". Without this, a read that never opened the
    /// reward table and simply believed the number it was given satisfies every other case in the
    /// suite: 3 is accepted, 2 and 4 are refused, and the refusal could be a hard-coded four.
    /// </remarks>
    internal static ContentSnapshot TimingBarWithoutTheRewardTable() =>
        new(FixtureStamp, [Minigames()]);

    /// <summary>Everything the Minigame screen reads: its strings, its numbers, the shipped tables.</summary>
    internal static ContentSnapshot Playable() =>
        new(FixtureStamp, [.. Locales(), Minigames(), .. ShippedTables()]);

    /// <summary>
    /// The same, without the timing bar's numbers — the shape a content set stripped of the document
    /// the screen depends on actually has, which the rules-unavailable arm has to be a sentence about.
    /// </summary>
    internal static ContentSnapshot WithoutTheAuthoredNumbers() =>
        new(FixtureStamp, [.. Locales(), .. ShippedTables()]);

    /// <summary>A content set carrying the strings and nothing else at all.</summary>
    internal static ContentSnapshot StringsOnly() => new(FixtureStamp, [.. Locales()]);

    /// <summary>The two shipped documents Core's own projection reads, borrowed rather than mimicked.</summary>
    private static IReadOnlyList<ContentDocument> ShippedTables() =>
    [
        RewardTables(),
        BootContent.Shipped.GetDocument(LuckDocument),
    ];

    /// <summary>The shipped reward tables, borrowed whole.</summary>
    private static ContentDocument RewardTables() =>
        BootContent.Shipped.GetDocument(CurrenciesDocument);

    private static ContentDocument Minigames(
        int strikes = Strikes,
        double sweepSeconds = SweepSeconds,
        double hitWindowHalfWidth = HitWindowHalfWidth,
        double reducedMotionStepFraction = ReducedMotionStepFraction) =>
        new(MinigamesDocument, Obj(
            ("timingBar", Obj(
                ("strikes", ContentValue.Number(strikes)),
                ("sweepSeconds", ContentValue.Number((decimal)sweepSeconds)),
                ("hitWindowHalfWidth", ContentValue.Number((decimal)hitWindowHalfWidth)),
                ("reducedMotionStepFraction",
                    ContentValue.Number((decimal)reducedMotionStepFraction))))));

    private static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m =>
            new KeyValuePair<string, ContentValue>(m.Name, m.Value)));

    private static IReadOnlyList<ContentDocument> Locales()
    {
        var keys = MinigameKeys
            .Concat(OutcomeTokens.Select(OutcomeKeyFor))
            .Concat(CurrencyNameKeys)
            .ToArray();

        return
        [
            Locale("loc/en.json", ScreenContent.English, keys, EnglishValueOf),
            Locale("loc/de.json", ScreenContent.German, keys, GermanValueOf),
        ];
    }

    private static ContentDocument Locale(
        string path, string localeTag, IReadOnlyList<string> keys, Func<string, string> valueOf) =>
        new(path, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("_locale", ContentValue.Text(localeTag)),
            new KeyValuePair<string, ContentValue>("strings", ContentValue.Object(
                keys.Select(k =>
                    new KeyValuePair<string, ContentValue>(k, ContentValue.Text(valueOf(k)))))),
        ]));
}
