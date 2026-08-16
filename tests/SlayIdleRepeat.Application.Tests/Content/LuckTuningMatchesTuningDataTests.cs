using System.Globalization;
using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks that <c>tuning/luck.json</c>, Core's <c>SourceClass</c>/<c>Rarity</c> vocabularies and the
/// hermetic fixture <c>Core.Tests</c> mirrors it with cannot drift apart.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic and transcribes the pity registry into a fixture; the reader is
/// internal to Core, so this suite cannot call it and reads the real file through
/// <see cref="RepoData"/> instead. Neither half is sufficient alone: the Core cases prove the rules
/// are right about the numbers, these prove those are the numbers we ship.
/// <para>
/// Every pointer is written out here as a literal rather than shared with the reader or the fixture
/// (neither of which this assembly can see), so a rename in one place without the other fails this
/// suite instead of silently pointing both halves at the same wrong leaf.
/// </para>
/// <para>
/// The <c>N</c> table below is the one that makes <c>24</c> §11's <em>"every rule in §4 gets an
/// explicit unit test asserting the guarantee fires at exactly N"</em> checkable: <c>HardPityTests</c>
/// runs a case per row of its own copy of this list, and this suite is what stops that copy from
/// quietly disagreeing with the shipped file.
/// </para>
/// </remarks>
public sealed class LuckTuningMatchesTuningDataTests
{
    private const string LuckDocument = "tuning/luck.json";

    private const string SourceClassesPointer = LuckDocument + "#/sourceClasses";
    private const string RarityFloorPointer = LuckDocument + "#/rarityFloor";

    /// <summary>
    /// Every <c>N</c> authored anywhere in <c>luck.json</c>, at the pointer that authors it.
    /// </summary>
    public static TheoryData<string, int> EveryAuthoredN() => new()
    {
        { "#/chestStandard/hardPity/0/everyNth", 10 },
        { "#/chestStandard/hardPity/1/everyNth", 40 },
        { "#/chestStandard/hardPity/2/everyNth", 160 },
        { "#/chestPremium/hardPity/0/everyNth", 5 },
        { "#/chestPremium/hardPity/1/everyNth", 25 },
        { "#/chestApex/hardPity/0/everyNth", 3 },
        { "#/dropRun/eliteMercy/consecutiveMissesBeforeForce", 6 },
        { "#/dropRun/bossMercy/consecutiveMissesBeforeForce", 4 },
        { "#/eggPet/hardPity/0/everyNth", 30 },
        { "#/eggPet/hardPity/1/everyNth", 150 },
        { "#/crateMount/hardPity/0/everyNth", 8 },
        { "#/crateMount/hardPity/1/everyNth", 30 },
        { "#/wheel/jackpotHardPitySpins", 60 },
        { "#/minigame/chestPick/guaranteeAfterConsecutiveMisses", 4 },
        { "#/draft/legendaryPityDraftNumber", 15 },
        { "#/draft/qualityFloor/consecutiveDraftsWithoutAboveCommon", 3 },
        { "#/draft/upgradeFamine/consecutiveDraftsWithoutOwnedUpgrade", 5 },
    };

    // ---------------------------------------------------------------- the guarantee Ns

    /// <summary>Every <c>N</c> the exact-<c>N</c> theory covers is the <c>N</c> the file authors.</summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredN))]
    public void Every_authored_N_is_the_number_the_exact_N_cases_are_written_against(
        string pointer, int expected)
    {
        Leaf(pointer).GetInt32().ShouldBe(
            expected,
            $"{LuckDocument}{pointer} is the one place this guarantee is a number. " +
            "SlayIdleRepeat.Core.Tests.Content.LuckDocuments.EveryAuthoredHardPityN carries the same " +
            "row and drives HardPityTests from it; a retune here that left the fixture behind would " +
            "leave the hermetic case green while the game guaranteed something else.");
    }

    /// <summary>
    /// The file authors seventeen <c>N</c>s and no more — so a new rule cannot arrive without a case.
    /// </summary>
    /// <remarks>
    /// Counted structurally rather than from the list above, which would agree with itself. The five
    /// ladder blocks are counted from their own arrays and the other five from the closed set of
    /// blocks <c>24</c> §4 gives them, so an added rung, an added mercy or an added draft rule all
    /// move this number.
    /// </remarks>
    [Fact]
    public void The_file_authors_exactly_the_seventeen_Ns_the_exact_N_cases_cover()
    {
        var rungs = new[] { "chestStandard", "chestPremium", "chestApex", "eggPet", "crateMount" }
            .Sum(block => Leaf($"#/{block}/hardPity").GetArrayLength());

        rungs.ShouldBe(
            10,
            "24 §4.1/§4.2/§4.4/§4.5: 3 + 2 + 1 + 2 + 2 rungs. An added rung is a guarantee with no " +
            "exact-N case and no place in the ladder the reader builds.");

        EveryAuthoredN().Count().ShouldBe(
            17,
            "the ten ladder rungs plus dropRun's two mercies, the wheel's jackpot, the minigame's " +
            "chest pick and the draft's three. 24 §11 wants a case per rule, so the list has to be " +
            "the whole list.");
    }

    /// <summary>Every ladder rung guarantees a rarity Core can parse.</summary>
    /// <remarks>
    /// A guarantee naming a token outside the ladder — <c>LEGENDARY</c> from the perk bands, say — is
    /// a rung the reader refuses at load, which takes the whole game down rather than one chest.
    /// </remarks>
    [Theory]
    [InlineData("chestStandard", "A", "S", "SS")]
    [InlineData("chestPremium", "S", "SS")]
    [InlineData("chestApex", "SS")]
    [InlineData("eggPet", "S", "SS")]
    [InlineData("crateMount", "S", "SS")]
    public void Every_ladder_rung_guarantees_a_rarity_Core_declares(string block, params string[] expected)
    {
        var guarantees = Leaf($"#/{block}/hardPity")
            .EnumerateArray()
            .Select(rung => rung.GetProperty("guaranteeRarityAtLeast").GetString()!)
            .ToArray();

        guarantees.ShouldBe(expected);
        guarantees.Except(Enum.GetNames<Rarity>(), StringComparer.Ordinal).ShouldBeEmpty(
            $"{LuckDocument}#/{block}/hardPity names a rarity Rarity does not declare");
    }

    /// <summary>The rungs of a ladder ascend, in <c>N</c> and in guarantee alike.</summary>
    /// <remarks>
    /// An invariant no schema can express and the ladder's whole meaning: a 40-chest rung guaranteeing
    /// less than the 10-chest rung would be satisfied by every draw the shorter one forced, and would
    /// never fire on its own.
    /// </remarks>
    [Theory]
    [InlineData("chestStandard")]
    [InlineData("chestPremium")]
    [InlineData("eggPet")]
    [InlineData("crateMount")]
    public void A_ladders_rungs_ascend_in_both_N_and_guarantee(string block)
    {
        var rungs = Leaf($"#/{block}/hardPity")
            .EnumerateArray()
            .Select(rung => (
                N: rung.GetProperty("everyNth").GetInt32(),
                Rarity: Enum.Parse<Rarity>(rung.GetProperty("guaranteeRarityAtLeast").GetString()!)))
            .ToArray();

        rungs.Select(rung => rung.N).ShouldBe(rungs.Select(rung => rung.N).OrderBy(n => n));
        rungs.Select(rung => rung.Rarity).ShouldBe(
            rungs.Select(rung => rung.Rarity).OrderBy(rarity => rarity),
            $"{LuckDocument}#/{block}/hardPity must get rarer as it gets longer, or the longer rung " +
            "is satisfied by every draw the shorter one already forced.");
    }

    // ---------------------------------------------------------------- the source-class registry

    /// <summary>The registry's ids are exactly <see cref="SourceClass"/>, in the same order.</summary>
    /// <remarks>
    /// <c>24</c> §3: <em>"adding a new grant source must assign it a class"</em>. A row Core cannot
    /// parse is a grant source with no class, and a member with no row is a class whose counter key
    /// and scope nothing authors.
    /// </remarks>
    [Fact]
    public void The_source_class_ids_are_exactly_the_SourceClass_members_in_order()
    {
        Ids().ShouldBe(
            Enum.GetNames<SourceClass>(),
            $"{SourceClassesPointer} and SourceClass are the two halves of 24 §3's table, and the " +
            "reader maps a row to a member by name and keeps the document's order.");
    }

    /// <summary>The ids are the upper-case tokens the enum spells, case-sensitively.</summary>
    /// <remarks>
    /// Written out as literals rather than derived from <c>Enum.GetNames</c>, which is the only way
    /// this case can bite: a list taken from the enum would agree with it by construction.
    /// </remarks>
    [Fact]
    public void The_source_class_ids_are_the_upper_case_tokens_the_enum_spells()
    {
        var ids = Ids();

        ids.ShouldContain("CHEST_STANDARD");
        ids.ShouldContain("DROP_RUN");
        ids.ShouldContain("MINIGAME");
        ids.ShouldNotContain("Chest_Standard");
        ids.ShouldNotContain("CHEST_MYTHIC");
    }

    /// <summary>Every counter key and scope is the one the hermetic fixture mirrors.</summary>
    [Theory]
    [InlineData("CHEST_STANDARD", "chest.standard", "PLAYER")]
    [InlineData("CHEST_PREMIUM", "chest.premium", "PLAYER")]
    [InlineData("CHEST_APEX", "chest.apex", "PLAYER")]
    [InlineData("DROP_RUN", "drop.run", "PLAYER")]
    [InlineData("EGG_PET", "egg.pet", "PLAYER")]
    [InlineData("CRATE_MOUNT", "crate.mount", "PLAYER")]
    [InlineData("ENHANCE", null, "GEAR_INSTANCE")]
    [InlineData("DRAFT", null, "RUN")]
    [InlineData("WHEEL", "wheel", "PLAYER")]
    [InlineData("MINIGAME", "minigame.chestpick", "PLAYER")]
    public void Every_source_class_carries_the_counter_key_and_scope_the_fixture_mirrors(
        string id, string? counterKey, string scope)
    {
        var row = Rows().Single(candidate =>
            candidate.GetProperty("id").GetString()!.Equals(id, StringComparison.Ordinal));

        var authored = row.GetProperty("counterKey");
        (authored.ValueKind == JsonValueKind.Null ? null : authored.GetString()).ShouldBe(
            counterKey,
            $"{SourceClassesPointer} is where 24 §3's Counter key column lives, and LuckTuning forms " +
            "'<counterKey>:<rarity>' from it. A rename here starts every player on a fresh counter " +
            "and 24 §1.1's persistence rule — 'counters never reset' — is broken silently.");

        row.GetProperty("counterScope").GetString().ShouldBe(
            scope,
            "ENHANCE's counter lives on the gear instance and DRAFT's on the run (24 §3, §4.6). " +
            "Moving either to PLAYER would make it farmable across cheap items — exactly what 24 " +
            "§1.2's anti-farming rule forbids.");
    }

    /// <summary>Exactly the two non-player-scoped classes author no counter key.</summary>
    /// <remarks>
    /// The two facts are authored independently — a row could name a key and a non-player scope, or
    /// neither — and the reader treats a null key as "not in the player counter map". A player-scoped
    /// class with a null key would be a counter with nowhere to live.
    /// </remarks>
    [Fact]
    public void Exactly_the_non_player_scoped_classes_author_no_counter_key()
    {
        Rows()
            .Where(row => row.GetProperty("counterKey").ValueKind == JsonValueKind.Null)
            .Select(row => row.GetProperty("counterScope").GetString()!)
            .ShouldBe(new[] { "GEAR_INSTANCE", "RUN" });

        var playerScoped = Rows()
            .Where(row => row.GetProperty("counterScope").GetString() == "PLAYER")
            .ToArray();

        playerScoped.Length.ShouldBe(
            8,
            "the floor under the assertion below: ShouldAllBe over an empty sequence passes, so a " +
            "registry that renamed the scope token would satisfy it while quantifying over nothing.");
        playerScoped
            .Select(row => row.GetProperty("counterKey").ValueKind)
            .ShouldAllBe(kind => kind == JsonValueKind.String);
    }

    /// <summary>No two classes share a counter key.</summary>
    /// <remarks>
    /// <c>24</c> §1.2: counters never pool across classes. Two classes sharing an authored key would
    /// pool every one of their rungs, and the cheapest source in the game would fill the most
    /// expensive one's ladder.
    /// </remarks>
    [Fact]
    public void No_two_source_classes_share_a_counter_key()
    {
        var keys = Rows()
            .Select(row => row.GetProperty("counterKey"))
            .Where(key => key.ValueKind == JsonValueKind.String)
            .Select(key => key.GetString()!)
            .ToArray();

        keys.Length.ShouldBe(
            8,
            "the floor under the uniqueness claim: an empty set is trivially unique, so a registry " +
            "whose keys all became null would pass this case while pooling nothing at all.");
        keys.ShouldBeUnique();
    }

    // ---------------------------------------------------------------- the soft-pity curves

    /// <summary>Every authored curve is the one the hermetic fixture mirrors.</summary>
    [Theory]
    [InlineData("chestStandard", "SS", 100, 0.05)]
    [InlineData("chestPremium", "SS", 15, 0.08)]
    [InlineData("crateMount", "SS", 20, 0.1)]
    public void Every_authored_soft_pity_curve_is_the_one_the_fixture_mirrors(
        string block, string target, int threshold, double slope)
    {
        var curve = Leaf($"#/{block}/softPity");

        curve.GetProperty("target").GetString().ShouldBe(target);
        curve.GetProperty("missThreshold").GetInt32().ShouldBe(threshold);
        curve.GetProperty("slope").GetDouble().ShouldBe(
            slope,
            $"{LuckDocument}#/{block}/softPity/slope is the k in 24 §1 M2's 1 + k·(misses − T). " +
            "SoftPityTests pins 1 + 0.05 × (158 − 100) = 3.9 against 24 §4.1's own 'roughly 4× base " +
            "by chest 159'; a retune here that left the fixture behind would leave that green.");
    }

    /// <summary>The two classes that need no curve author an explicit <c>null</c>.</summary>
    /// <remarks>
    /// An absent member and an authored <c>null</c> are different states — the schema types
    /// <c>softPity</c> as <c>["object", "null"]</c> so a class can say it has none — and the reader
    /// answers "no curve" only for the second.
    /// </remarks>
    [Theory]
    [InlineData("chestApex")]
    [InlineData("eggPet")]
    public void A_class_that_needs_no_soft_pity_authors_an_explicit_null(string block)
    {
        Leaf($"#/{block}/softPity").ValueKind.ShouldBe(
            JsonValueKind.Null,
            "24 §4.2: 'no soft pity needed at that density'");
    }

    /// <summary>A curve's threshold sits below the hard rung it protects.</summary>
    /// <remarks>
    /// The invariant that makes a ramp a ramp: a threshold at or above its own <c>N</c> would never
    /// be passed, and the curve would be a number nobody could ever feel.
    /// </remarks>
    [Theory]
    [InlineData("chestStandard", 2)]
    [InlineData("chestPremium", 1)]
    [InlineData("crateMount", 1)]
    public void A_curves_threshold_sits_below_the_rung_it_protects(string block, int rungIndex)
    {
        Leaf($"#/{block}/softPity/missThreshold").GetInt32().ShouldBeLessThan(
            Leaf($"#/{block}/hardPity/{rungIndex.ToString(CultureInfo.InvariantCulture)}/everyNth").GetInt32(),
            $"{LuckDocument}#/{block}: the ramp has to start before the guarantee catches it, or 24 " +
            "§1 M2's 'makes the curve feel lucky rather than mechanical' buys nothing at all.");
    }

    // ---------------------------------------------------------------- the rarity-floor rule

    /// <summary>The one floor rule is the one the fixture mirrors, and the one Core declares.</summary>
    [Fact]
    public void The_rarity_floor_rule_is_the_one_the_fixture_mirrors()
    {
        Leaf("#/rarityFloor/renormalisation").GetString().ShouldBe(
            "PROPORTIONAL",
            $"{RarityFloorPointer}/renormalisation is a one-member enum in the schema, so widening it " +
            "is a deliberate edit in both places. 24 §4.0a rule 3: a floored source draws its class " +
            "table renormalised at/above the floor, preserving relative ratios.");

        Leaf("#/rarityFloor/countersAdvanceNormally").GetBoolean().ShouldBeTrue(
            "24 §4.0a rule 3: 'the draw advances and resets counters normally — a floored A still " +
            "resets the A-counter'. This is the sentence under which forcing a guarantee is " +
            "expressible as flooring the table, which is what keeps a resolution at one draw index.");
    }

    /// <summary>The block cites the section it transcribes, so the 📐 audit can find its authority.</summary>
    [Fact]
    public void The_rarity_floor_block_cites_the_section_it_transcribes()
    {
        Leaf("#/rarityFloor/_doc").GetString()!.ShouldContain(
            "24 §4.0a",
            Case.Sensitive,
            "the section carries a 📐 and is already cited by drops.schema.json; a bare '24 §4' " +
            "would be an unmarked citation the tunable-marker audit reports.");
    }

    // ---------------------------------------------------------------- the enhancement mercy

    /// <summary>
    /// <c>24</c> §4.6's rate mercy is the slope and cap <c>SoftPityTests</c> is written against.
    /// </summary>
    /// <remarks>
    /// The <c>enhance</c> block is not part of the pity registry <c>LuckTuning</c> reads — the
    /// enhancement command owns that shape — so this is the only place these two numbers are pinned
    /// against the shipped file at all.
    /// </remarks>
    [Fact]
    public void The_enhancement_mercy_slope_and_cap_are_the_ones_the_rate_cases_use()
    {
        Leaf("#/enhance/mercySlopePerConsecutiveFailure").GetDouble().ShouldBe(0.08);
        Leaf("#/enhance/effectiveRateCap").GetDouble().ShouldBe(1.0);
    }

    /// <summary>The ad boost stacks additively and advances no counter.</summary>
    /// <remarks>
    /// <c>24</c> §2: <em>"not an ad reward. No placement in <c>12</c> §4 may grant pity progress."</em>
    /// Authoring <c>true</c> here would make <c>AD_ENHANCE_LUCK</c> the cheapest way to advance a
    /// mercy counter, which is the monetisation of luck protection the founding rule forbids.
    /// </remarks>
    [Fact]
    public void The_ad_enhancement_boost_stacks_additively_and_advances_no_counter()
    {
        Leaf("#/enhance/adEnhanceLuckStacksAdditively").GetBoolean().ShouldBeTrue();
        Leaf("#/enhance/adEnhanceLuckAdvancesCounter").GetBoolean().ShouldBeFalse(
            "12 §4.3 and 24 §2: an ad is a one-shot boost to a single roll and never touches a counter");
    }

    // ---------------------------------------------------------------- helpers

    private static string[] Ids() =>
        Rows().Select(row => row.GetProperty("id").GetString()!).ToArray();

    private static JsonElement[] Rows() => Leaf("#/sourceClasses").EnumerateArray().ToArray();

    /// <summary>Resolves a JSON pointer inside <c>luck.json</c>, or fails naming the pointer.</summary>
    /// <remarks>
    /// <c>JsonElement.GetProperty</c> throws before any Shouldly message can print, so this walks the
    /// pointer itself and says which segment was missing.
    /// </remarks>
    private static JsonElement Leaf(string pointer)
    {
        using var document = JsonDocument.Parse(RepoData.Documents[LuckDocument]);

        var current = document.RootElement.Clone();
        foreach (var segment in pointer.TrimStart('#', '/').Split('/'))
        {
            current = Step(current, segment) ?? throw new InvalidOperationException(
                $"{LuckDocument}{pointer} resolves to nothing at '{segment}'. 24 is the single " +
                "authority for every pity N, and 21 §3.1 makes a 📐 TUNABLE outside game-data/tuning/ " +
                "a bug; Core.Content.LuckTuning reads this document and every container open would " +
                "throw. The document moved — fix the pointer, do not delete the case.");
        }

        return current;
    }

    private static JsonElement? Step(JsonElement current, string segment)
    {
        if (current.ValueKind == JsonValueKind.Object)
        {
            return current.TryGetProperty(segment, out var member) ? member.Clone() : null;
        }

        return current.ValueKind == JsonValueKind.Array &&
               int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
               index < current.GetArrayLength()
            ? current[index].Clone()
            : null;
    }
}
