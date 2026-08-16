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
/// Only what <c>LuckTuning</c> reads is transcribed as a document. The other five classes state
/// their protection in another shape entirely (a dry-streak breaker, a failure-rate mercy, a draft
/// composition rule, a jackpot spin count, a chest-pick guarantee) and nothing reads them yet, so
/// authoring them here would imply something does. Their <c>N</c>s still appear in
/// <see cref="EveryAuthoredHardPityN"/>, because <c>24</c> §11 asks for an exact-<c>N</c> test per
/// rule and that list is what makes the coverage claim checkable.
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
    /// Authored in <c>luck.json</c>'s <c>enhance</c> block, which <c>LuckTuning</c> does not read:
    /// the enhancement command (M4-04b) owns that shape. Declared here so the rate-mercy cases state
    /// the shipped slope rather than a literal, and pinned against the file in
    /// <c>Application.Tests</c>.
    /// </remarks>
    internal const double ShippedEnhanceMercySlope = 0.08;

    /// <summary><c>24</c> §4.6 — the ceiling on the effective enhancement rate.</summary>
    /// <inheritdoc cref="ShippedEnhanceMercySlope"/>
    internal const double ShippedEnhanceRateCap = 1.0;

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
        new("DROP_RUN", "#/dropRun/eliteMercy/consecutiveMissesBeforeForce", ShippedDropRunEliteMercyN),
        new("DROP_RUN", "#/dropRun/bossMercy/consecutiveMissesBeforeForce", ShippedDropRunBossMercyN),
        new("EGG_PET", "#/eggPet/hardPity/0/everyNth", ShippedEggPetSRung),
        new("EGG_PET", "#/eggPet/hardPity/1/everyNth", ShippedEggPetSsRung),
        new("CRATE_MOUNT", "#/crateMount/hardPity/0/everyNth", ShippedCrateMountSRung),
        new("CRATE_MOUNT", "#/crateMount/hardPity/1/everyNth", ShippedCrateMountSsRung),
        new("WHEEL", "#/wheel/jackpotHardPitySpins", ShippedWheelJackpotN),
        new("MINIGAME", "#/minigame/chestPick/guaranteeAfterConsecutiveMisses", ShippedMinigameChestPickN),
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
        ContentValue? chestApexSoftPity = null) =>
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
                    chestApexSoftPity),
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
        ContentValue? chestApexSoftPity = null) =>
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
                    chestApexSoftPity),
            ]);

    /// <summary>A content set with <b>no</b> <c>tuning/luck.json</c> at all.</summary>
    /// <remarks>
    /// The "missing document" case, which is a different failure from "the pointer holds null", from
    /// "the leaf is the wrong kind" and from "the value is authorised but unusable" — four doors into
    /// <c>LuckTuning.Read</c>, and the whole point of typed readers is that they are told apart.
    /// </remarks>
    internal static ContentSnapshot WithoutLuck() => ProgressionDocuments.Shipped;

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
        ContentValue? chestApexSoftPity) =>
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
                        ContentValue.Number((decimal)ShippedCrateMountSoftPitySlope)))))));

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
/// <param name="EveryNth">The authored <c>N</c>.</param>
internal readonly record struct AuthoredGuarantee(string Source, string Reference, int EveryNth);
