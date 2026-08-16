using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The pity registry read out of <c>tuning/luck.json</c>: ten source classes with their counter keys
/// and scopes, the five authored ladders, the one rarity-floor rule — and the four ways a data set
/// can fail to answer, told apart.
/// </summary>
/// <remarks>
/// No <c>const int ChestStandardA = 10</c> anywhere in <c>Core</c>. <c>24</c> §3's own 📐 puts class
/// membership in the data, and <c>CHEST_STANDARD</c> runs three counters at once — so a counter is
/// addressed by the authored key paired with the guarantee it protects, and this reader is the one
/// place that pairing is formed.
/// </remarks>
public sealed class LuckTuningTests
{
    // ---------------------------------------------------------------- the registry

    /// <summary>All ten classes of <c>24</c> §3, in the order the document lists them.</summary>
    /// <remarks>
    /// Order is asserted, not just membership: the registry is what maps an authored id back to the
    /// enum, and a reader that sorted the rows would still answer ten rows while pairing every class
    /// with someone else's counter key.
    /// </remarks>
    [Fact]
    public void The_reader_answers_the_ten_source_classes_the_document_lists_in_document_order()
    {
        LuckTuning.Read(LuckDocuments.Shipped).SourceClasses.Select(row => row.Id).ShouldBe(new[]
        {
            SourceClass.CHEST_STANDARD,
            SourceClass.CHEST_PREMIUM,
            SourceClass.CHEST_APEX,
            SourceClass.DROP_RUN,
            SourceClass.EGG_PET,
            SourceClass.CRATE_MOUNT,
            SourceClass.ENHANCE,
            SourceClass.DRAFT,
            SourceClass.WHEEL,
            SourceClass.MINIGAME,
        });
    }

    /// <summary>
    /// The reader answers the counter key the document authors — proved by moving the value, not by
    /// agreeing with the fixture's own constant.
    /// </summary>
    [Theory]
    [InlineData("chest.alpha")]
    [InlineData("zz.standard.chest")]
    public void The_reader_answers_the_counter_key_the_document_authors(string authored)
    {
        LuckTuning.Read(LuckDocuments.LuckOnly(chestStandardCounterKey: ContentValue.Text(authored)))
            .Row(SourceClass.CHEST_STANDARD)
            .CounterKey.ShouldBe(
                authored,
                $"the reader must resolve {LuckTuning.SourceClassesReference} and answer the key it " +
                "finds there — not a key compiled into Core. 24 §3's 📐: class membership lives in " +
                "the data file.");
    }

    /// <summary>
    /// Retuning one class's counter key moves that class's key and nothing else — the negative
    /// control on the registry lookup.
    /// </summary>
    /// <remarks>
    /// A reader that answered the first row for every class, or that keyed by array position rather
    /// than by the authored id, would pass every positive case above.
    /// </remarks>
    [Fact]
    public void Retuning_one_classes_counter_key_leaves_every_other_classes_key_where_it_was()
    {
        var tuning = LuckTuning.Read(LuckDocuments.LuckOnly(wheelCounterKey: ContentValue.Text("wheel.daily")));

        tuning.Row(SourceClass.WHEEL).CounterKey.ShouldBe("wheel.daily");
        tuning.Row(SourceClass.CHEST_STANDARD).CounterKey.ShouldBe(
            LuckDocuments.ShippedChestStandardCounterKey,
            "counters are per source class (24 §1.2). A registry that let one class's edit reach " +
            "another's key would be the pooling the anti-farming rule exists to forbid.");
        tuning.Row(SourceClass.MINIGAME).CounterKey.ShouldBe(LuckDocuments.ShippedMinigameCounterKey);
    }

    /// <summary>
    /// A class whose counter is not player-scoped authors no key at all, and the reader carries the
    /// scope rather than reading the missing key as "no counter".
    /// </summary>
    [Fact]
    public void A_class_whose_counter_is_not_player_scoped_authors_no_key_but_still_states_its_scope()
    {
        var tuning = LuckTuning.Read(LuckDocuments.Shipped);

        tuning.Row(SourceClass.ENHANCE).CounterKey.ShouldBeNull(
            "24 §3 authors this counter as 'per gear instance'. It has no id in the player counter " +
            "map, and an invented one would be a counter the player profile cannot store.");
        tuning.Row(SourceClass.ENHANCE).Scope.ShouldBe(CounterScope.GEAR_INSTANCE);

        tuning.Row(SourceClass.DRAFT).CounterKey.ShouldBeNull("24 §3 authors this counter as 'per run'");
        tuning.Row(SourceClass.DRAFT).Scope.ShouldBe(CounterScope.RUN);
    }

    /// <summary>Every player-scoped class states a key; the two non-player-scoped ones do not.</summary>
    [Fact]
    public void Exactly_the_player_scoped_classes_carry_a_counter_key()
    {
        var rows = LuckTuning.Read(LuckDocuments.Shipped).SourceClasses;

        rows.Where(row => row.Scope == CounterScope.PLAYER)
            .Select(row => row.CounterKey)
            .ShouldAllBe(key => key != null);

        rows.Count(row => row.Scope != CounterScope.PLAYER).ShouldBe(
            2,
            "24 §3 scopes ENHANCE per gear instance and DRAFT per run; the other eight are columns " +
            "on the player profile (24 §11).");
    }

    // ---------------------------------------------------------------- counter-key formation

    /// <summary>
    /// The counter id is the authored key paired with the guarantee it protects, and this reader is
    /// the only place it is formed.
    /// </summary>
    /// <remarks>
    /// <c>CHEST_STANDARD</c> runs three ladders simultaneously (<c>24</c> §4.1), so the class alone
    /// does not address a counter — which is why the key, and not a <c>SourceClass</c>, is what
    /// <c>PityCounterAdvanced</c> carries.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.A, "chest.standard:A")]
    [InlineData(Rarity.S, "chest.standard:S")]
    [InlineData(Rarity.SS, "chest.standard:SS")]
    public void CounterKey_pairs_the_authored_key_with_the_guarantee_it_protects(
        Rarity guarantee, string expected)
    {
        LuckTuning.Read(LuckDocuments.Shipped)
            .CounterKey(SourceClass.CHEST_STANDARD, guarantee)
            .ShouldBe(expected);
    }

    /// <summary>The key follows the document, separator included — it is not a compiled-in string.</summary>
    [Fact]
    public void CounterKey_follows_the_authored_key_rather_than_a_compiled_in_one()
    {
        LuckTuning.Read(LuckDocuments.LuckOnly(chestStandardCounterKey: ContentValue.Text("chest.alpha")))
            .CounterKey(SourceClass.CHEST_STANDARD, Rarity.S)
            .ShouldBe(
                "chest.alpha" + LuckTuning.CounterKeySeparator + nameof(Rarity.S),
                "the stored key and the authored key cannot be allowed to drift: a retune that " +
                "renamed chest.standard would silently start a fresh counter for every player.");
    }

    /// <summary>Two classes never form the same counter id, and one class never collapses two rungs into one.</summary>
    [Fact]
    public void Every_class_and_rung_pairing_forms_a_distinct_counter_id()
    {
        var tuning = LuckTuning.Read(LuckDocuments.Shipped);

        new[]
        {
            tuning.CounterKey(SourceClass.CHEST_STANDARD, Rarity.A),
            tuning.CounterKey(SourceClass.CHEST_STANDARD, Rarity.S),
            tuning.CounterKey(SourceClass.CHEST_STANDARD, Rarity.SS),
            tuning.CounterKey(SourceClass.CHEST_PREMIUM, Rarity.S),
            tuning.CounterKey(SourceClass.CHEST_PREMIUM, Rarity.SS),
            tuning.CounterKey(SourceClass.CHEST_APEX, Rarity.SS),
            tuning.CounterKey(SourceClass.EGG_PET, Rarity.S),
            tuning.CounterKey(SourceClass.CRATE_MOUNT, Rarity.S),
        }.ShouldBeUnique(
            "24 §1.2: counters never pool across classes. Two classes sharing an id would pool them " +
            "exactly, and the cheap class would advance the expensive class's guarantee.");
    }

    /// <summary>A class with no authored key has no counter id, and asking for one is refused.</summary>
    [Theory]
    [InlineData(SourceClass.ENHANCE)]
    [InlineData(SourceClass.DRAFT)]
    public void CounterKey_refuses_a_class_whose_counter_is_not_player_scoped(SourceClass source)
    {
        Should.Throw<InvalidTunableException>(
                () => LuckTuning.Read(LuckDocuments.Shipped).CounterKey(source, Rarity.S))
            .Message.ShouldContain(source.ToString(), Case.Sensitive,
                "several reads in this type throw InvalidTunableException; the message has to say " +
                "which class had no key, or the caller cannot tell this refusal from a bad ladder.");
    }

    // ---------------------------------------------------------------- the ladders

    /// <summary>The standard-chest ladder is <c>24</c> §4.1's three rungs, in order.</summary>
    [Fact]
    public void The_reader_answers_the_ladder_rungs_the_document_authors()
    {
        LuckTuning.Read(LuckDocuments.Shipped).Ladder(SourceClass.CHEST_STANDARD).HardPity.ShouldBe(new[]
        {
            new HardPityStep(LuckDocuments.ShippedChestStandardARung, Rarity.A),
            new HardPityStep(LuckDocuments.ShippedChestStandardSRung, Rarity.S),
            new HardPityStep(LuckDocuments.ShippedChestStandardSsRung, Rarity.SS),
        });
    }

    /// <summary>A retuned rung is read from the document, not from a number compiled into Core.</summary>
    [Theory]
    [InlineData(7, "B")]
    [InlineData(11, "S")]
    public void The_reader_answers_the_retuned_rung_the_document_authors(int everyNth, string guarantee)
    {
        var ladder = LuckTuning.Read(LuckDocuments.LuckOnly(
                chestStandardFirstRungEveryNth: ContentValue.Number(everyNth),
                chestStandardFirstRungGuarantee: ContentValue.Text(guarantee)))
            .Ladder(SourceClass.CHEST_STANDARD);

        ladder.HardPity[0].EveryNth.ShouldBe(everyNth);
        ladder.HardPity[0].GuaranteeRarityAtLeast.ShouldBe(Enum.Parse<Rarity>(guarantee));

        ladder.HardPity[1].EveryNth.ShouldBe(
            LuckDocuments.ShippedChestStandardSRung,
            "retuning one rung must leave its neighbours where the document put them");
    }

    /// <summary>Each of the five ladder classes carries the rungs its own block authors.</summary>
    [Theory]
    [InlineData(SourceClass.CHEST_STANDARD, 3)]
    [InlineData(SourceClass.CHEST_PREMIUM, 2)]
    [InlineData(SourceClass.CHEST_APEX, 1)]
    [InlineData(SourceClass.EGG_PET, 2)]
    [InlineData(SourceClass.CRATE_MOUNT, 2)]
    public void Every_class_that_authors_a_ladder_gets_its_own_rungs(SourceClass source, int rungs)
    {
        var ladder = LuckTuning.Read(LuckDocuments.Shipped).Ladder(source);

        ladder.Source.ShouldBe(source);
        ladder.HardPity.Count.ShouldBe(rungs);
    }

    /// <summary>The soft-pity curve is read as authored — target, threshold and slope.</summary>
    [Fact]
    public void The_reader_answers_the_soft_pity_curve_the_document_authors()
    {
        LuckTuning.Read(LuckDocuments.Shipped).Ladder(SourceClass.CHEST_STANDARD).SoftPity.ShouldBe(
            new SoftPityCurve(
                LuckDocuments.ShippedSoftPityTarget,
                LuckDocuments.ShippedChestStandardSoftPityThreshold,
                LuckDocuments.ShippedChestStandardSoftPitySlope));
    }

    /// <summary>A retuned curve is read from the document, both leaves independently.</summary>
    [Theory]
    [InlineData(40, 0.02)]
    [InlineData(120, 0.5)]
    public void The_reader_answers_the_retuned_soft_pity_curve(int threshold, double slope)
    {
        var curve = LuckTuning.Read(LuckDocuments.LuckOnly(
                chestStandardSoftPityThreshold: ContentValue.Number(threshold),
                chestStandardSoftPitySlope: ContentValue.Number((decimal)slope)))
            .Ladder(SourceClass.CHEST_STANDARD).SoftPity;

        curve!.Value.MissThreshold.ShouldBe(threshold);
        curve.Value.Slope.ShouldBe(slope);
    }

    /// <summary>Two of the five classes are dense enough to author an explicit <c>null</c> curve.</summary>
    /// <remarks>
    /// An authored <c>null</c> here means "no curve", which is a different statement from an
    /// unauthorised hole elsewhere: the schema types <c>softPity</c> as <c>["object", "null"]</c>
    /// precisely so a class can say it has none.
    /// </remarks>
    [Theory]
    [InlineData(SourceClass.CHEST_APEX)]
    [InlineData(SourceClass.EGG_PET)]
    public void A_class_that_needs_no_soft_pity_reads_back_as_having_none(SourceClass source)
    {
        LuckTuning.Read(LuckDocuments.Shipped).Ladder(source).SoftPity.ShouldBeNull(
            "24 §4.2: 'no soft pity needed at that density'. Substituting a flat curve would make " +
            "the absence indistinguishable from a slope of zero somebody meant to author.");
    }

    /// <summary>A class that authors a curve where the shipped file has none reads it back.</summary>
    /// <remarks>The negative control on the case above: <c>null</c> must be read, not assumed.</remarks>
    [Fact]
    public void A_class_that_gains_a_curve_reads_it_back_rather_than_staying_null()
    {
        var curve = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["target"] = ContentValue.Text("SS"),
            ["missThreshold"] = ContentValue.Number(2),
            ["slope"] = ContentValue.Number(0.25m),
        });

        LuckTuning.Read(LuckDocuments.LuckOnly(chestApexSoftPity: curve))
            .Ladder(SourceClass.CHEST_APEX).SoftPity.ShouldBe(new SoftPityCurve("SS", 2, 0.25));
    }

    // ---------------------------------------------------------------- the classes with no ladder

    /// <summary>The five classes that author a rarity ladder answer one.</summary>
    [Theory]
    [InlineData(SourceClass.CHEST_STANDARD)]
    [InlineData(SourceClass.CHEST_PREMIUM)]
    [InlineData(SourceClass.CHEST_APEX)]
    [InlineData(SourceClass.EGG_PET)]
    [InlineData(SourceClass.CRATE_MOUNT)]
    public void TryGetLadder_answers_true_for_a_class_that_authors_a_rarity_ladder(SourceClass source)
    {
        LuckTuning.Read(LuckDocuments.Shipped).TryGetLadder(source, out var ladder).ShouldBeTrue();
        ladder.HardPity.ShouldNotBeEmpty("24 §1: M1 is mandatory everywhere");
    }

    /// <summary>
    /// The other five state their protection in another shape entirely, and asking for a ladder they
    /// do not have answers false rather than synthesising one.
    /// </summary>
    [Theory]
    [InlineData(SourceClass.DROP_RUN)]
    [InlineData(SourceClass.ENHANCE)]
    [InlineData(SourceClass.DRAFT)]
    [InlineData(SourceClass.WHEEL)]
    [InlineData(SourceClass.MINIGAME)]
    public void TryGetLadder_answers_false_for_a_class_that_states_its_rule_in_another_shape(
        SourceClass source)
    {
        LuckTuning.Read(LuckDocuments.Shipped).TryGetLadder(source, out _).ShouldBeFalse(
            "24 §4.3/§4.6-§4.9 state a dry-streak breaker, a failure-rate mercy, a draft composition " +
            "rule, a jackpot spin count and a chest-pick guarantee. Answering a synthesised ladder " +
            "would draw these classes against a table nobody authored.");
    }

    /// <summary>Asking for the ladder outright is refused by name, with the task that wires it.</summary>
    [Theory]
    [InlineData(SourceClass.DROP_RUN, "dropRun")]
    [InlineData(SourceClass.ENHANCE, "enhance")]
    [InlineData(SourceClass.DRAFT, "draft")]
    [InlineData(SourceClass.WHEEL, "wheel")]
    [InlineData(SourceClass.MINIGAME, "minigame")]
    public void Ladder_refuses_a_class_that_states_its_rule_in_another_shape(
        SourceClass source, string block)
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => LuckTuning.Read(LuckDocuments.Shipped).Ladder(source));

        thrown.Message.ShouldContain(source.ToString(), Case.Sensitive);
        thrown.Message.ShouldContain(
            block,
            Case.Sensitive,
            "the message has to name the block that holds the class's real rule — a bare 'no ladder' " +
            "leaves the caller unable to tell a missing block from an unserved shape.");
    }

    // ---------------------------------------------------------------- the rarity-floor rule

    /// <summary>The one floor rule, read as authored.</summary>
    [Fact]
    public void The_reader_answers_the_one_rarity_floor_rule_the_document_authors()
    {
        var floor = LuckTuning.Read(LuckDocuments.Shipped).RarityFloor;

        floor.Renormalisation.ShouldBe(
            RarityFloorRenormalisation.PROPORTIONAL,
            "24 §4.0a rule 3: a floored source 'draws its class table renormalised at/above the " +
            "floor'. One rule for every class — a per-class variant would make an A guarantee mean a " +
            "different distribution in a chest than in a crate.");
        floor.CountersAdvanceNormally.ShouldBeTrue(
            "24 §4.0a rule 3: 'the draw advances and resets counters normally — a floored A still " +
            "resets the A-counter'. This is what makes forcing a guarantee expressible as flooring.");
    }

    /// <summary>The floor flag is read, not assumed — an authored <c>false</c> reads back as false.</summary>
    /// <remarks>
    /// The negative control on the case above: a reader that answered a compiled-in <c>true</c> would
    /// pass it and would quietly ignore a data set that turned the rule off.
    /// </remarks>
    [Fact]
    public void The_reader_answers_a_retuned_floor_flag_rather_than_a_compiled_in_one()
    {
        LuckTuning.Read(LuckDocuments.LuckOnly(countersAdvanceNormally: ContentValue.False))
            .RarityFloor.CountersAdvanceNormally.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- the four ways data can fail

    /// <summary>A missing document is a <c>MissingContentException</c>, not an empty registry.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(() => LuckTuning.Read(LuckDocuments.WithoutLuck()));
    }

    /// <summary>A deliberate <c>null</c> is an <c>UnauthorisedTunableException</c> — the hole stays a hole.</summary>
    /// <remarks>
    /// Reading an unauthorised renormalisation as <c>PROPORTIONAL</c> would produce a distribution,
    /// the economy simulator would grade it, and nobody would learn that a rule nobody authored had
    /// been chosen for them.
    /// </remarks>
    [Fact]
    public void An_unauthorised_null_throws_rather_than_defaulting()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => LuckTuning.Read(LuckDocuments.LuckOnly(renormalisation: ContentValue.Unauthorised)));
    }

    /// <summary>A leaf of the wrong kind is a <c>ContentTypeMismatchException</c>.</summary>
    [Fact]
    public void A_leaf_of_the_wrong_kind_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(
            () => LuckTuning.Read(LuckDocuments.LuckOnly(renormalisation: ContentValue.Number(1))));
    }

    /// <summary>A fractional <c>N</c> is a type mismatch: a rung counts whole draws.</summary>
    [Fact]
    public void A_fractional_rung_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(
            () => LuckTuning.Read(LuckDocuments.LuckOnly(
                chestStandardFirstRungEveryNth: ContentValue.Number(10.5m))));
    }

    /// <summary>An unknown renormalisation is authorised but unusable.</summary>
    /// <remarks>
    /// The schema types it as a one-member enum so widening it is a deliberate edit in both places;
    /// the reader refusing an unknown token is the half of that the schema cannot enforce at runtime.
    /// </remarks>
    [Theory]
    [InlineData("EQUAL")]
    [InlineData("proportional")]
    public void An_unknown_renormalisation_is_refused(string authored)
    {
        Should.Throw<InvalidTunableException>(
                () => LuckTuning.Read(LuckDocuments.LuckOnly(renormalisation: ContentValue.Text(authored))))
            .Message.ShouldContain(authored, Case.Sensitive);
    }

    /// <summary>A rung below 1 is authorised but unusable: there is no zeroth draw to force.</summary>
    /// <remarks>
    /// Refused at the read, where the data set is still nameable, rather than at the draw, where it
    /// would be one player's problem — and where <c>N = 0</c> would force every single chest.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void A_rung_below_one_is_refused(int everyNth)
    {
        Should.Throw<InvalidTunableException>(
            () => LuckTuning.Read(LuckDocuments.LuckOnly(
                chestStandardFirstRungEveryNth: ContentValue.Number(everyNth))));
    }

    /// <summary>An unknown guarantee rarity is authorised but unusable.</summary>
    [Fact]
    public void An_unknown_guarantee_rarity_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => LuckTuning.Read(LuckDocuments.LuckOnly(
                    chestStandardFirstRungGuarantee: ContentValue.Text("LEGENDARY"))))
            .Message.ShouldContain(
                "LEGENDARY",
                Case.Sensitive,
                "COMMON/RARE/EPIC/LEGENDARY is the perk band ladder, not the gear rarity ladder. The " +
                "two vocabularies are deliberately kept apart, so a token from one must not resolve " +
                "in the other.");
    }

    /// <summary>An unknown source-class id is authorised but unusable.</summary>
    [Fact]
    public void An_unknown_source_class_id_is_refused()
    {
        var rows = ContentValue.Array(
        [
            ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["id"] = ContentValue.Text("CHEST_MYTHIC"),
                ["counterKey"] = ContentValue.Text("chest.mythic"),
                ["counterScope"] = ContentValue.Text("PLAYER"),
            }),
        ]);

        Should.Throw<InvalidTunableException>(
                () => LuckTuning.Read(LuckDocuments.LuckOnly(sourceClasses: rows)))
            .Message.ShouldContain(
                "CHEST_MYTHIC",
                Case.Sensitive,
                "24 §3: 'adding a new grant source must assign it a class'. A row naming a class Core " +
                "does not declare is a source with no class, and silently skipping it is exactly the " +
                "unprotected grant the founding rule forbids.");
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => LuckTuning.Read(null!));
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>
    /// Numbers render identically under every culture. A slope of <c>0.05</c> under <c>de-DE</c>
    /// renders as <c>0,05</c>, which would put a comma inside a failure message and inside any
    /// counter string built from one.
    /// </summary>
    [Fact]
    public void Render_produces_the_same_text_under_any_culture()
    {
        var german = new CultureInfo("de-DE");

        LuckDocuments.ShippedChestStandardSoftPitySlope.ToString(german).ShouldNotBe(
            LuckDocuments.ShippedChestStandardSoftPitySlope.ToString(CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a German culture. Under " +
            "globalization-invariant mode new CultureInfo(\"de-DE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

        Render(() => LuckTuning.Render(LuckDocuments.ShippedChestStandardSoftPitySlope), german)
            .ShouldBe("0.05");
        Render(() => LuckTuning.Render(LuckDocuments.ShippedChestStandardSsRung), german).ShouldBe("160");
    }

    private static string Render(Func<string> render, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
