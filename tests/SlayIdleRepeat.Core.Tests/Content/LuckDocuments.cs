using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/luck.json</c> fixtures — the <c>sourceClasses</c> registry, the
/// <c>rarityFloor</c> rule and the five blocks that author a hard/soft pity ladder.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped file rather than reading it;
/// <c>LuckTuningMatchesTuningDataTests</c> in <c>Application.Tests</c> pins the two halves together
/// by resolving the same JSON pointers in the real file. Neither is sufficient alone: this proves
/// the rules are right about the numbers, that one proves those are the numbers we ship.
/// <para>
/// Only what a reader actually reads is transcribed as a document. <c>LuckTuning</c> takes the
/// registry, the floor rule and the five ladder blocks; <c>DropRunTuning</c> takes the
/// <c>dropRun</c> block, and the draft, chest-pick and enhancement guarantees are read by the façade
/// members that serve those three classes — which is why all four <em>are</em> authored here while
/// the jackpot spin count still is
/// not — nothing reads it, and authoring it would imply something does. Its <c>N</c> still
/// appear in <see cref="EveryAuthoredHardPityN"/>, because <c>24</c> §11 asks for an exact-<c>N</c>
/// test per rule and that list is what makes the coverage claim checkable.
/// </para>
/// </remarks>
internal static class LuckDocuments
{
    /// <summary>Where the pity registry lives.</summary>
    internal const string DocumentPath = "tuning/luck.json";

    // ---------------------------------------------------------------- source-class registry

    /// <summary>The standard-chest counter key, as shipped.</summary>
    internal const string ShippedChestStandardCounterKey = "chest.standard";

    /// <summary>The premium-chest counter key, as shipped.</summary>
    internal const string ShippedChestPremiumCounterKey = "chest.premium";

    /// <summary>The apex-chest counter key, as shipped.</summary>
    internal const string ShippedChestApexCounterKey = "chest.apex";

    /// <summary>The in-run drop counter key, as shipped.</summary>
    internal const string ShippedDropRunCounterKey = "drop.run";

    /// <summary>The Pet Egg counter key, as shipped.</summary>
    internal const string ShippedEggPetCounterKey = "egg.pet";

    /// <summary>The Mount Crate counter key, as shipped.</summary>
    internal const string ShippedCrateMountCounterKey = "crate.mount";

    /// <summary>The Lucky Wheel counter key, as shipped.</summary>
    internal const string ShippedWheelCounterKey = "wheel";

    /// <summary>The chest-pick minigame counter key, as shipped.</summary>
    internal const string ShippedMinigameCounterKey = "minigame.chestpick";

    // ---------------------------------------------------------------- the rarity-floor rule

    /// <summary>The one renormalisation rule, as shipped.</summary>
    internal const string ShippedRenormalisation = "PROPORTIONAL";

    /// <summary>Whether a floored draw still advances and resets counters, as shipped.</summary>
    internal const bool ShippedCountersAdvanceNormally = true;

    // ---------------------------------------------------------------- the five authored ladders

    /// <summary><c>24</c> §4.1 — A-or-better every 10th standard chest.</summary>
    internal const int ShippedChestStandardARung = 10;

    /// <summary><c>24</c> §4.1 — S-or-better every 40th standard chest.</summary>
    internal const int ShippedChestStandardSRung = 40;

    /// <summary><c>24</c> §4.1 — SS every 160th standard chest.</summary>
    internal const int ShippedChestStandardSsRung = 160;

    /// <summary><c>24</c> §4.1 — the SS soft-pity ramp starts past 100 misses.</summary>
    internal const int ShippedChestStandardSoftPityThreshold = 100;

    /// <summary><c>24</c> §4.1 — the SS soft-pity slope.</summary>
    internal const double ShippedChestStandardSoftPitySlope = 0.05;

    /// <summary><c>24</c> §4.2 — S-or-better every 5th premium chest.</summary>
    internal const int ShippedChestPremiumSRung = 5;

    /// <summary><c>24</c> §4.2 — SS every 25th premium chest.</summary>
    internal const int ShippedChestPremiumSsRung = 25;

    /// <summary><c>24</c> §4.2 — the premium SS ramp starts past 15 misses.</summary>
    internal const int ShippedChestPremiumSoftPityThreshold = 15;

    /// <summary><c>24</c> §4.2 — the premium SS slope.</summary>
    internal const double ShippedChestPremiumSoftPitySlope = 0.08;

    /// <summary><c>24</c> §4.2 — SS every 3rd apex chest.</summary>
    internal const int ShippedChestApexSsRung = 3;

    /// <summary><c>24</c> §4.4 — S-or-better every 30th Pet Egg.</summary>
    internal const int ShippedEggPetSRung = 30;

    /// <summary><c>24</c> §4.4 — SS every 150th Pet Egg.</summary>
    internal const int ShippedEggPetSsRung = 150;

    /// <summary><c>24</c> §4.5 — S-or-better every 8th Mount Crate.</summary>
    internal const int ShippedCrateMountSRung = 8;

    /// <summary><c>24</c> §4.5 — SS every 30th Mount Crate.</summary>
    internal const int ShippedCrateMountSsRung = 30;

    /// <summary><c>24</c> §4.5 — the crate SS ramp starts past 20 misses.</summary>
    internal const int ShippedCrateMountSoftPityThreshold = 20;

    /// <summary><c>24</c> §4.5 — the crate SS slope.</summary>
    internal const double ShippedCrateMountSoftPitySlope = 0.1;

    /// <summary>The rarity every authored soft-pity curve on a chest or crate targets.</summary>
    internal const string ShippedSoftPityTarget = "SS";

    /// <summary><c>24</c> §4.6 — the per-consecutive-failure addition to the enhancement rate.</summary>
    /// <remarks>
    /// Declared here so the rate-mercy cases state the shipped slope rather than a literal, and
    /// pinned against the file in <c>Application.Tests</c>.
    /// </remarks>
    internal const double ShippedEnhanceMercySlope = 0.08;

    /// <summary><c>24</c> §4.6 — the ceiling on the effective enhancement rate.</summary>
    /// <inheritdoc cref="ShippedEnhanceMercySlope"/>
    internal const double ShippedEnhanceRateCap = 1.0;

    /// <summary><c>24</c> §4.6 — the rewarded ad's bonus adds to the raised chance rather than multiplying into it.</summary>
    /// <inheritdoc cref="ShippedEnhanceMercySlope"/>
    internal const bool ShippedEnhanceAdStacksAdditively = true;

    /// <summary><c>24</c> §4.6 — an attempt carrying the ad's bonus neither advances nor consumes the mercy counter.</summary>
    /// <inheritdoc cref="ShippedEnhanceMercySlope"/>
    internal const bool ShippedEnhanceAdAdvancesCounter = false;

    // ------------------------------------------------- the Ns the other five classes author

    /// <summary><c>24</c> §4.3 D1 — force A-or-better on the 6th below-A Elite drop.</summary>
    internal const int ShippedDropRunEliteMercyN = 6;

    /// <summary><c>24</c> §4.3 D2 — force S-or-better on the 4th below-S boss drop.</summary>
    internal const int ShippedDropRunBossMercyN = 4;

    /// <summary><c>24</c> §4.8 — jackpot hard pity at 60 spins.</summary>
    internal const int ShippedWheelJackpotN = 60;

    /// <summary><c>24</c> §4.9 — the gold-tier chest is guaranteed on the 4th consecutive miss.</summary>
    internal const int ShippedMinigameChestPickN = 4;

    /// <summary><c>24</c> §4.7 — Legendary pity on draft #15.</summary>
    internal const int ShippedDraftLegendaryPityN = 15;

    /// <summary><c>24</c> §4.7 F1 — a Rare-or-better option after 3 drafts with nothing above Common.</summary>
    internal const int ShippedDraftQualityFloorN = 3;

    /// <summary><c>24</c> §4.7 F3 — force an owned-perk upgrade after 5 drafts without one.</summary>
    internal const int ShippedDraftUpgradeFamineN = 5;

    /// <summary><c>24</c> §4.7 — the anti-brick guarantee stands, as shipped.</summary>
    internal const bool ShippedDraftSustainAntiBrickEnabled = true;

    /// <summary><c>24</c> §4.7 — the category the anti-brick forces, as shipped.</summary>
    internal const string ShippedDraftSustainForceCategory = "SUSTAIN";

    /// <summary><c>24</c> §4.7 F1 — the band the quality floor forces, as shipped.</summary>
    internal const string ShippedDraftQualityFloorRarity = "RARE";

    /// <summary><c>24</c> §4.7 F2 — the never-drafted weight multiplier, as shipped.</summary>
    internal const double ShippedDraftCodexBiasMultiplier = 1.35;

    /// <summary><c>24</c> §4.7 F2 — at most one option per draft may be bias-selected.</summary>
    internal const int ShippedDraftMaxBiasSelectedOptions = 1;

    /// <summary><c>06</c> §4 — the per-option owned-upgrade bias, authored inside the famine block.</summary>
    internal const double ShippedDraftOwnedUpgradeBias = 0.3;

    /// <summary><c>24</c> §4.9 — the chest pick offers three chests.</summary>
    internal const int ShippedMinigameChestCount = 3;

    /// <summary><c>24</c> §4.9 — exactly one of them is the gold tier.</summary>
    internal const int ShippedMinigameGoldTierChests = 1;

    /// <summary>
    /// The authored top-tier outcome token for <c>MG_CHEST_PICK</c> — the guarantee this class's
    /// counter is keyed by. Authored in <c>currencies.json</c>, restated here so a fixture can form
    /// the counter id the way the reader does.
    /// </summary>
    internal const string ShippedChestPickGuaranteeToken = "GOLD";

    // ---------------------------------------------------------------- the in-run drop block

    /// <summary>A drop counts against the elite streak when it lands strictly below this band.</summary>
    internal const string ShippedEliteMercyBelowRarity = "A";

    /// <summary>The band the forced elite drop must reach or beat.</summary>
    internal const string ShippedEliteMercyForceRarity = "A";

    /// <summary>A drop counts against the boss streak when it lands strictly below this band.</summary>
    internal const string ShippedBossMercyBelowRarity = "S";

    /// <summary>The band the forced boss drop must reach or beat.</summary>
    internal const string ShippedBossMercyForceRarity = "S";

    /// <summary>The band each session-floor grant lands on.</summary>
    internal const string ShippedSessionFloorGrantRarity = "B";

    /// <summary>How many items one qualifying session's floor grants.</summary>
    internal const int ShippedSessionFloorGrantCount = 1;

    /// <summary>How many floor grants a player may take in one game day.</summary>
    internal const int ShippedSessionFloorMaxPerDay = 2;

    /// <summary>Whether the run must have ended in a victory or a stage-3 death to qualify.</summary>
    internal const bool ShippedSessionFloorRequiresVictoryOrStage3Death = true;

    /// <summary>
    /// Every <c>N</c> authored anywhere in <c>luck.json</c>, with the class it protects and the
    /// pointer it is authored at.
    /// </summary>
    /// <remarks>
    /// <c>24</c> §11: <em>"every rule in §4 gets an explicit unit test asserting the guarantee fires
    /// at exactly N"</em>. Driving the exact-<c>N</c> theory from this list rather than from a hand
    /// of <c>[InlineData]</c> literals is what lets <c>Application.Tests</c> resolve every pointer in
    /// the shipped file and fail when a rule is added, renamed or retuned without its case.
    /// </remarks>
    internal static IReadOnlyList<AuthoredGuarantee> EveryAuthoredHardPityN { get; } =
    [
        new("CHEST_STANDARD", "#/chestStandard/hardPity/0/everyNth", ShippedChestStandardARung),
        new("CHEST_STANDARD", "#/chestStandard/hardPity/1/everyNth", ShippedChestStandardSRung),
        new("CHEST_STANDARD", "#/chestStandard/hardPity/2/everyNth", ShippedChestStandardSsRung),
        new("CHEST_PREMIUM", "#/chestPremium/hardPity/0/everyNth", ShippedChestPremiumSRung),
        new("CHEST_PREMIUM", "#/chestPremium/hardPity/1/everyNth", ShippedChestPremiumSsRung),
        new("CHEST_APEX", "#/chestApex/hardPity/0/everyNth", ShippedChestApexSsRung),
        new("DROP_RUN", "#/dropRun/eliteMercy/forceOnNthKill", ShippedDropRunEliteMercyN),
        new("DROP_RUN", "#/dropRun/bossMercy/forceOnNthKill", ShippedDropRunBossMercyN),
        new("EGG_PET", "#/eggPet/hardPity/0/everyNth", ShippedEggPetSRung),
        new("EGG_PET", "#/eggPet/hardPity/1/everyNth", ShippedEggPetSsRung),
        new("CRATE_MOUNT", "#/crateMount/hardPity/0/everyNth", ShippedCrateMountSRung),
        new("CRATE_MOUNT", "#/crateMount/hardPity/1/everyNth", ShippedCrateMountSsRung),
        new("WHEEL", "#/wheel/jackpotHardPitySpins", ShippedWheelJackpotN),
        new("MINIGAME", "#/minigame/chestPick/guaranteeOnNthPick", ShippedMinigameChestPickN),
        new("DRAFT", "#/draft/legendaryPityDraftNumber", ShippedDraftLegendaryPityN),
        new("DRAFT", "#/draft/qualityFloor/consecutiveDraftsWithoutAboveCommon", ShippedDraftQualityFloorN),
        new("DRAFT", "#/draft/upgradeFamine/consecutiveDraftsWithoutOwnedUpgrade", ShippedDraftUpgradeFamineN),
    ];

    /// <summary>The whole shipped tuning set the luck rules read, plus a second document beside it.</summary>
    /// <remarks>
    /// A second document is present on purpose: a reader that resolved the wrong document path would
    /// otherwise be indistinguishable from one that resolved the right one in a snapshot of size one.
    /// </remarks>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The shipped set with individual luck leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> for a deliberate <c>null</c> hole, or omit to keep the
    /// shipped value.
    /// </summary>
    /// <param name="sourceClasses">The whole <c>sourceClasses</c> array.</param>
    /// <param name="chestStandardCounterKey">The standard chest's authored counter key.</param>
    /// <param name="wheelCounterKey">The wheel's authored counter key — the negative control.</param>
    /// <param name="enhanceCounterKey">The enhancement row's counter key. An authored null as shipped.</param>
    /// <param name="renormalisation">How a floored table is rescaled.</param>
    /// <param name="countersAdvanceNormally">Whether a floored draw still moves counters.</param>
    /// <param name="chestStandardHardPity">The whole standard-chest rung array.</param>
    /// <param name="chestStandardFirstRungEveryNth">The first standard-chest rung's <c>N</c>.</param>
    /// <param name="chestStandardFirstRungGuarantee">The first standard-chest rung's guarantee.</param>
    /// <param name="chestStandardSoftPity">The whole standard-chest soft-pity block.</param>
    /// <param name="chestStandardSoftPityThreshold">The standard-chest ramp's miss threshold.</param>
    /// <param name="chestStandardSoftPitySlope">The standard-chest ramp's slope.</param>
    /// <param name="chestApexSoftPity">The apex block's soft pity. An authored null as shipped.</param>
    /// <param name="dropRun">
    /// The whole <c>dropRun</c> block — the two dry-streak breakers and the session floor. Built with
    /// <see cref="DropRun"/>, <see cref="Breaker"/> and <see cref="Floor"/> rather than by a leaf
    /// apiece: <c>DropRunTuning</c> refuses a <em>pairing</em> (a forced band below the miss band) as
    /// well as individual leaves, so a case has to be able to move two at once.
    /// </param>
    /// <param name="draftLegendaryPityNumber">The draft ordinal the Legendary pity forces.</param>
    /// <param name="draftSustainForceCategory">The category the anti-brick forces.</param>
    /// <param name="minigameGuaranteeOnNthPick">The chest pick that is forced onto the top tier.</param>
    internal static ContentSnapshot With(
        ContentValue? sourceClasses = null,
        ContentValue? chestStandardCounterKey = null,
        ContentValue? wheelCounterKey = null,
        ContentValue? enhanceCounterKey = null,
        ContentValue? renormalisation = null,
        ContentValue? countersAdvanceNormally = null,
        ContentValue? chestStandardHardPity = null,
        ContentValue? chestStandardFirstRungEveryNth = null,
        ContentValue? chestStandardFirstRungGuarantee = null,
        ContentValue? chestStandardSoftPity = null,
        ContentValue? chestStandardSoftPityThreshold = null,
        ContentValue? chestStandardSoftPitySlope = null,
        ContentValue? chestApexSoftPity = null,
        ContentValue? dropRun = null,
        ContentValue? draftLegendaryPityNumber = null,
        ContentValue? draftSustainForceCategory = null,
        ContentValue? minigameGuaranteeOnNthPick = null,
        ContentValue? enhanceMercySlope = null,
        ContentValue? enhanceRateCap = null,
        ContentValue? enhanceAdAdvancesCounter = null,
        ContentValue? draftSustainAntiBrickEnabled = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                Luck(
                    sourceClasses,
                    chestStandardCounterKey,
                    wheelCounterKey,
                    enhanceCounterKey,
                    renormalisation,
                    countersAdvanceNormally,
                    chestStandardHardPity,
                    chestStandardFirstRungEveryNth,
                    chestStandardFirstRungGuarantee,
                    chestStandardSoftPity,
                    chestStandardSoftPityThreshold,
                    chestStandardSoftPitySlope,
                    chestApexSoftPity,
                    dropRun,
                    draftLegendaryPityNumber,
                    draftSustainForceCategory,
                    minigameGuaranteeOnNthPick,
                    enhanceMercySlope,
                    enhanceRateCap,
                    enhanceAdAdvancesCounter,
                    draftSustainAntiBrickEnabled),
                ProgressionDocuments.Shipped.GetDocument(ProgressionDocuments.DocumentPath),
            ]);

    /// <summary>A content set holding <b>only</b> <c>tuning/luck.json</c>.</summary>
    /// <remarks>
    /// For <c>LuckTuning</c>'s own tests, which are about that reader and must not be able to pass
    /// because some other document happened to be present.
    /// </remarks>
    /// <inheritdoc cref="With" path="/param"/>
    internal static ContentSnapshot LuckOnly(
        ContentValue? sourceClasses = null,
        ContentValue? chestStandardCounterKey = null,
        ContentValue? wheelCounterKey = null,
        ContentValue? enhanceCounterKey = null,
        ContentValue? renormalisation = null,
        ContentValue? countersAdvanceNormally = null,
        ContentValue? chestStandardHardPity = null,
        ContentValue? chestStandardFirstRungEveryNth = null,
        ContentValue? chestStandardFirstRungGuarantee = null,
        ContentValue? chestStandardSoftPity = null,
        ContentValue? chestStandardSoftPityThreshold = null,
        ContentValue? chestStandardSoftPitySlope = null,
        ContentValue? chestApexSoftPity = null,
        ContentValue? dropRun = null,
        ContentValue? draftLegendaryPityNumber = null,
        ContentValue? draftSustainForceCategory = null,
        ContentValue? minigameGuaranteeOnNthPick = null,
        ContentValue? enhanceMercySlope = null,
        ContentValue? enhanceRateCap = null,
        ContentValue? enhanceAdAdvancesCounter = null,
        ContentValue? draftSustainAntiBrickEnabled = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                Luck(
                    sourceClasses,
                    chestStandardCounterKey,
                    wheelCounterKey,
                    enhanceCounterKey,
                    renormalisation,
                    countersAdvanceNormally,
                    chestStandardHardPity,
                    chestStandardFirstRungEveryNth,
                    chestStandardFirstRungGuarantee,
                    chestStandardSoftPity,
                    chestStandardSoftPityThreshold,
                    chestStandardSoftPitySlope,
                    chestApexSoftPity,
                    dropRun,
                    draftLegendaryPityNumber,
                    draftSustainForceCategory,
                    minigameGuaranteeOnNthPick,
                    enhanceMercySlope,
                    enhanceRateCap,
                    enhanceAdAdvancesCounter,
                    draftSustainAntiBrickEnabled),
            ]);

    /// <summary>A content set with <b>no</b> <c>tuning/luck.json</c> at all.</summary>
    /// <remarks>
    /// The "missing document" case, which is a different failure from "the pointer holds null", from
    /// "the leaf is the wrong kind" and from "the value is authorised but unusable" — four doors into
    /// <c>LuckTuning.Read</c>, and the whole point of typed readers is that they are told apart.
    /// </remarks>
    internal static ContentSnapshot WithoutLuck() => ProgressionDocuments.Shipped;

    /// <summary>One dry-streak breaker, as <c>DropRunTuning</c> reads it.</summary>
    /// <param name="ordinal">The ordinal of the forced kill within a streak.</param>
    /// <param name="belowRarity">The band a drop must land under to count as a miss.</param>
    /// <param name="forceRarityAtLeast">The band the forced drop must reach or beat.</param>
    internal static ContentValue Breaker(
        ContentValue ordinal, ContentValue belowRarity, ContentValue forceRarityAtLeast) =>
        Members(
            ("forceOnNthKill", ordinal),
            ("belowRarity", belowRarity),
            ("forceRarityAtLeast", forceRarityAtLeast));

    /// <summary>The session floor, as <c>DropRunTuning</c> reads it.</summary>
    /// <param name="grantRarity">The band each floor grant lands on.</param>
    /// <param name="grantCount">How many items one qualifying session grants.</param>
    /// <param name="maxPerDay">How many floor grants a player may take in a game day.</param>
    /// <param name="requiresVictoryOrStage3Death">Whether the run must have ended a qualifying way.</param>
    internal static ContentValue Floor(
        ContentValue grantRarity,
        ContentValue grantCount,
        ContentValue maxPerDay,
        ContentValue requiresVictoryOrStage3Death) =>
        Members(
            ("grantRarity", grantRarity),
            ("grantCount", grantCount),
            ("maxPerDay", maxPerDay),
            ("requiresVictoryOrStage3Death", requiresVictoryOrStage3Death));

    /// <summary>The whole <c>dropRun</c> block, with any of its three rules replaced.</summary>
    /// <param name="eliteMercy">The elite dry-streak breaker.</param>
    /// <param name="bossMercy">The boss dry-streak breaker.</param>
    /// <param name="sessionFloor">The per-session gear floor.</param>
    internal static ContentValue DropRun(
        ContentValue? eliteMercy = null,
        ContentValue? bossMercy = null,
        ContentValue? sessionFloor = null) =>
        Members(
            ("eliteMercy", eliteMercy ?? Breaker(
                ContentValue.Number(ShippedDropRunEliteMercyN),
                ContentValue.Text(ShippedEliteMercyBelowRarity),
                ContentValue.Text(ShippedEliteMercyForceRarity))),
            ("bossMercy", bossMercy ?? Breaker(
                ContentValue.Number(ShippedDropRunBossMercyN),
                ContentValue.Text(ShippedBossMercyBelowRarity),
                ContentValue.Text(ShippedBossMercyForceRarity))),
            ("sessionFloor", sessionFloor ?? Floor(
                ContentValue.Text(ShippedSessionFloorGrantRarity),
                ContentValue.Number(ShippedSessionFloorGrantCount),
                ContentValue.Number(ShippedSessionFloorMaxPerDay),
                ContentValue.Boolean(ShippedSessionFloorRequiresVictoryOrStage3Death))));

    private static ContentDocument Luck(
        ContentValue? sourceClasses,
        ContentValue? chestStandardCounterKey,
        ContentValue? wheelCounterKey,
        ContentValue? enhanceCounterKey,
        ContentValue? renormalisation,
        ContentValue? countersAdvanceNormally,
        ContentValue? chestStandardHardPity,
        ContentValue? chestStandardFirstRungEveryNth,
        ContentValue? chestStandardFirstRungGuarantee,
        ContentValue? chestStandardSoftPity,
        ContentValue? chestStandardSoftPityThreshold,
        ContentValue? chestStandardSoftPitySlope,
        ContentValue? chestApexSoftPity,
        ContentValue? dropRun,
        ContentValue? draftLegendaryPityNumber,
        ContentValue? draftSustainForceCategory,
        ContentValue? minigameGuaranteeOnNthPick,
        ContentValue? enhanceMercySlope = null,
        ContentValue? enhanceRateCap = null,
        ContentValue? enhanceAdAdvancesCounter = null,
        ContentValue? draftSustainAntiBrickEnabled = null) =>
        new(
            DocumentPath,
            Members(
                ("sourceClasses", sourceClasses ?? ContentValue.Array(
                [
                    SourceClassRow("CHEST_STANDARD", chestStandardCounterKey ?? ContentValue.Text(ShippedChestStandardCounterKey), "PLAYER"),
                    SourceClassRow("CHEST_PREMIUM", ContentValue.Text(ShippedChestPremiumCounterKey), "PLAYER"),
                    SourceClassRow("CHEST_APEX", ContentValue.Text(ShippedChestApexCounterKey), "PLAYER"),
                    SourceClassRow("DROP_RUN", ContentValue.Text(ShippedDropRunCounterKey), "PLAYER"),
                    SourceClassRow("EGG_PET", ContentValue.Text(ShippedEggPetCounterKey), "PLAYER"),
                    SourceClassRow("CRATE_MOUNT", ContentValue.Text(ShippedCrateMountCounterKey), "PLAYER"),
                    SourceClassRow("ENHANCE", enhanceCounterKey ?? ContentValue.Unauthorised, "GEAR_INSTANCE"),
                    SourceClassRow("DRAFT", ContentValue.Unauthorised, "RUN"),
                    SourceClassRow("WHEEL", wheelCounterKey ?? ContentValue.Text(ShippedWheelCounterKey), "PLAYER"),
                    SourceClassRow("MINIGAME", ContentValue.Text(ShippedMinigameCounterKey), "PLAYER"),
                ])),
                ("rarityFloor", Members(
                    ("renormalisation", renormalisation ?? ContentValue.Text(ShippedRenormalisation)),
                    ("countersAdvanceNormally",
                        countersAdvanceNormally ?? ContentValue.Boolean(ShippedCountersAdvanceNormally)))),
                ("chestStandard", Members(
                    ("hardPity", chestStandardHardPity ?? ContentValue.Array(
                    [
                        Rung(
                            chestStandardFirstRungEveryNth ?? ContentValue.Number(ShippedChestStandardARung),
                            chestStandardFirstRungGuarantee ?? ContentValue.Text("A")),
                        Rung(ContentValue.Number(ShippedChestStandardSRung), ContentValue.Text("S")),
                        Rung(ContentValue.Number(ShippedChestStandardSsRung), ContentValue.Text("SS")),
                    ])),
                    ("softPity", chestStandardSoftPity ?? Curve(
                        ShippedSoftPityTarget,
                        chestStandardSoftPityThreshold ?? ContentValue.Number(ShippedChestStandardSoftPityThreshold),
                        chestStandardSoftPitySlope ?? ContentValue.Number((decimal)ShippedChestStandardSoftPitySlope))))),
                ("chestPremium", Members(
                    ("hardPity", ContentValue.Array(
                    [
                        Rung(ContentValue.Number(ShippedChestPremiumSRung), ContentValue.Text("S")),
                        Rung(ContentValue.Number(ShippedChestPremiumSsRung), ContentValue.Text("SS")),
                    ])),
                    ("softPity", Curve(
                        ShippedSoftPityTarget,
                        ContentValue.Number(ShippedChestPremiumSoftPityThreshold),
                        ContentValue.Number((decimal)ShippedChestPremiumSoftPitySlope))))),
                ("chestApex", Members(
                    ("hardPity", ContentValue.Array(
                    [
                        Rung(ContentValue.Number(ShippedChestApexSsRung), ContentValue.Text("SS")),
                    ])),
                    ("softPity", chestApexSoftPity ?? ContentValue.Unauthorised))),
                ("eggPet", Members(
                    ("hardPity", ContentValue.Array(
                    [
                        Rung(ContentValue.Number(ShippedEggPetSRung), ContentValue.Text("S")),
                        Rung(ContentValue.Number(ShippedEggPetSsRung), ContentValue.Text("SS")),
                    ])),
                    ("softPity", ContentValue.Unauthorised))),
                ("crateMount", Members(
                    ("hardPity", ContentValue.Array(
                    [
                        Rung(ContentValue.Number(ShippedCrateMountSRung), ContentValue.Text("S")),
                        Rung(ContentValue.Number(ShippedCrateMountSsRung), ContentValue.Text("SS")),
                    ])),
                    ("softPity", Curve(
                        ShippedSoftPityTarget,
                        ContentValue.Number(ShippedCrateMountSoftPityThreshold),
                        ContentValue.Number((decimal)ShippedCrateMountSoftPitySlope))))),
                ("dropRun", dropRun ?? DropRun()),
                ("enhance", Members(
                    ("mercySlopePerConsecutiveFailure",
                        enhanceMercySlope ?? ContentValue.Number((decimal)ShippedEnhanceMercySlope)),
                    ("effectiveRateCap",
                        enhanceRateCap ?? ContentValue.Number((decimal)ShippedEnhanceRateCap)),
                    ("adEnhanceLuckStacksAdditively",
                        ContentValue.Boolean(ShippedEnhanceAdStacksAdditively)),
                    ("adEnhanceLuckAdvancesCounter",
                        enhanceAdAdvancesCounter
                            ?? ContentValue.Boolean(ShippedEnhanceAdAdvancesCounter)))),
                ("draft", Members(
                    ("legendaryPityDraftNumber", draftLegendaryPityNumber ?? ContentValue.Number(ShippedDraftLegendaryPityN)),
                    ("sustainAntiBrick", Members(
                        ("enabled",
                            draftSustainAntiBrickEnabled
                                ?? ContentValue.Boolean(ShippedDraftSustainAntiBrickEnabled)),
                        ("forceCategory", draftSustainForceCategory ?? ContentValue.Text(ShippedDraftSustainForceCategory)))),
                    ("qualityFloor", Members(
                        ("consecutiveDraftsWithoutAboveCommon", ContentValue.Number(ShippedDraftQualityFloorN)),
                        ("forceRarityAtLeast", ContentValue.Text(ShippedDraftQualityFloorRarity)))),
                    ("codexBias", Members(
                        ("neverDraftedWeightMultiplier", ContentValue.Number((decimal)ShippedDraftCodexBiasMultiplier)),
                        ("maxBiasSelectedOptions", ContentValue.Number(ShippedDraftMaxBiasSelectedOptions)))),
                    ("upgradeFamine", Members(
                        ("ownedUpgradeBias", ContentValue.Number((decimal)ShippedDraftOwnedUpgradeBias)),
                        ("consecutiveDraftsWithoutOwnedUpgrade", ContentValue.Number(ShippedDraftUpgradeFamineN)))))),
                ("minigame", Members(
                    ("chestPick", Members(
                        ("chestCount", ContentValue.Number(ShippedMinigameChestCount)),
                        ("goldTierChests", ContentValue.Number(ShippedMinigameGoldTierChests)),
                        ("guaranteeOnNthPick",
                            minigameGuaranteeOnNthPick ?? ContentValue.Number(ShippedMinigameChestPickN))))))));

    private static ContentValue SourceClassRow(string id, ContentValue counterKey, string scope) =>
        Members(
            ("id", ContentValue.Text(id)),
            ("counterKey", counterKey),
            ("counterScope", ContentValue.Text(scope)));

    private static ContentValue Rung(ContentValue everyNth, ContentValue guarantee) =>
        Members(("everyNth", everyNth), ("guaranteeRarityAtLeast", guarantee));

    private static ContentValue Curve(string target, ContentValue missThreshold, ContentValue slope) =>
        Members(
            ("target", ContentValue.Text(target)),
            ("missThreshold", missThreshold),
            ("slope", slope));

    private static ContentValue Members(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(member =>
            new KeyValuePair<string, ContentValue>(member.Name, member.Value)));
}

/// <summary>One authored hard-pity <c>N</c>, and where <c>luck.json</c> states it.</summary>
/// <param name="Source">The source class the rule protects, as <c>24</c> §3 names it.</param>
/// <param name="Reference">The JSON pointer, relative to <c>tuning/luck.json</c>.</param>
/// <param name="EveryNth">
/// The number the document authors at <paramref name="Reference"/>. ⚠️ <b>It is the rung's own
/// <c>N</c> for every row but two.</b> <c>DRAFT</c>'s quality floor and upgrade famine author the
/// drafts that pass <em>before</em> the next one is floored, so their rung is this number plus one;
/// every other row — the Legendary pity and the chest pick included — authors the forced draw's own
/// ordinal. This list exists to prove the coverage claim over the authored numbers, so it carries
/// them verbatim; <c>DraftGuaranteeTests</c> is where each of the three readings is pinned to the
/// draft it actually floors.
/// </param>
internal readonly record struct AuthoredGuarantee(string Source, string Reference, int EveryNth);
