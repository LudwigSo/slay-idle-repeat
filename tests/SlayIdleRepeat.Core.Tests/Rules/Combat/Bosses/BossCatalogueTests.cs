using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 <c>content/bosses/bosses.json</c> as the boss engine reads it — `17` §1.2 and §2-9.
/// </summary>
/// <remarks>
/// The structure is <c>EnemyCatalogueTests</c>'; what differs is where the bytes come from. This reader
/// is exercised against the <b>shipped</b> document off disk, because a fixture cannot make that claim.
/// Every negative case mutates one anchor of the shipped text in memory, never an edit to
/// <c>game-data/</c>.
/// <para>
/// 🔴 Each refusal is probed <b>twice, in different shapes</b>, with a negative control that must stay
/// green: a reader that threw on everything would satisfy every refusal and read nothing at all.
/// </para>
/// </remarks>
public sealed class BossCatalogueTests
{
    private const string Thornmaw = "BOSS_THORNMAW";
    private const string Gulgrot = "BOSS_GULGROT";
    private const string Sporequeen = "BOSS_SPOREQUEEN_VELL";
    private const string Ftue = "BOSS_FTUE";

    /// <summary>`17` §1.2 — nine rows: the eight campaign bosses and the FTUE mini-boss.</summary>
    private const int AuthoredScriptCount = 9;

    /// <summary>
    /// The pointers this reader names, spelled out. The precedent is
    /// <c>EnemyCatalogueTests.The_document_is_the_one_05_section_6_names</c>: a pointer that drifted
    /// would move every read in the file at once, and every case below would still pass on whatever
    /// the new pointers happened to resolve to.
    /// </summary>
    [Fact]
    public void The_document_is_the_one_17_section_1_2_names()
    {
        BossCatalogue.Document.ShouldBe("content/bosses/bosses.json");

        BossCatalogue.CritPointer.ShouldBe("content/bosses/bosses.json#/secondaryStats/crit");
        BossCatalogue.CritDamagePointer.ShouldBe("content/bosses/bosses.json#/secondaryStats/critDamage");
        BossCatalogue.DodgePointer.ShouldBe("content/bosses/bosses.json#/secondaryStats/dodge");
        BossCatalogue.LifestealPointer.ShouldBe("content/bosses/bosses.json#/secondaryStats/lifesteal");
        BossCatalogue.ScriptsPointer.ShouldBe("content/bosses/bosses.json#/scripts");

        BossCatalogue.ScriptPointer(3).ShouldBe("content/bosses/bosses.json#/scripts/3");
        BossCatalogue.CoefficientsPointer(3).ShouldBe("content/bosses/bosses.json#/scripts/3/coefficients");
        BossCatalogue.EffectsPointer(3).ShouldBe("content/bosses/bosses.json#/scripts/3/effects");
        BossCatalogue.EffectPointer(3, 5).ShouldBe("content/bosses/bosses.json#/scripts/3/effects/5");
        BossCatalogue.PhasesPointer(3).ShouldBe("content/bosses/bosses.json#/scripts/3/phases");
        BossCatalogue.PhasePointer(3, 1).ShouldBe("content/bosses/bosses.json#/scripts/3/phases/1");
        BossCatalogue.MechanicsPointer(3, 1)
            .ShouldBe("content/bosses/bosses.json#/scripts/3/phases/1/mechanics");
        BossCatalogue.MechanicPointer(3, 1, 2)
            .ShouldBe("content/bosses/bosses.json#/scripts/3/phases/1/mechanics/2");
        BossCatalogue.OutcomePointer(7, 0, 1)
            .ShouldBe("content/bosses/bosses.json#/scripts/7/effects/0/outcomes/1");

        // 🔒 The document really is in the tree the harness loads. A pointer constant that named a
        // file nobody ships would make every case in this class a statement about a typo.
        GameDataLoader.Load().DocumentPaths.ShouldContain(BossCatalogue.Document);
    }

    /// <summary>
    /// 🔒 S3 — the floor under every case in this namespace: nine rows, a named one among them, and
    /// `17` §1.2's four baseline secondaries.
    /// </summary>
    /// <remarks>
    /// A reader that returned an empty catalogue would take
    /// <c>AuthoredBossScriptTests</c>' 872 lines green with it, over nothing. The named row is what
    /// makes "nine" a statement about `17` §1.2's table rather than about nine rows of some other
    /// document.
    /// </remarks>
    [Fact]
    public void Reading_the_document_produces_every_row_17_section_1_2_puts_in_data()
    {
        var catalogue = ShippedBosses.Catalogue;

        catalogue.Scripts.Count.ShouldBe(AuthoredScriptCount, "17 §1.2's table has nine rows");
        catalogue.Scripts.Select(s => s.Script.Id).ShouldContain(Sporequeen);

        catalogue.Crit.ShouldBe(0.05);
        catalogue.CritDamage.ShouldBe(0.50);
        catalogue.Dodge.ShouldBe(0.0);
        catalogue.Lifesteal.ShouldBe(0.0);

        var thornmaw = catalogue.Of(Thornmaw);

        thornmaw.Chapter.ShouldBe(1);
        thornmaw.Script.Coefficients.Hp.ShouldBe(2.40);
        thornmaw.Script.Phases.Count.ShouldBe(BossScript.PhaseCount, "17 §1 — exactly three blocks");
        thornmaw.Effects.Count.ShouldBeGreaterThanOrEqualTo(4, "17 §2's Root, Bloom, Regrowth and Rage");
        thornmaw.TelegraphSeconds.Count.ShouldBe(1, "17 §2 authors one wind-up, on the Root");
    }

    /// <summary>Asking for a boss the document does not declare fails rather than answering null.</summary>
    [Fact]
    public void Reading_a_row_the_document_does_not_declare_fails()
    {
        var thrown = Should.Throw<KeyNotFoundException>(() => ShippedBosses.Catalogue.Of("BOSS_NOBODY"));

        thrown.Message.ShouldContain("BOSS_NOBODY", Case.Sensitive);
        thrown.Message.ShouldContain(BossCatalogue.Document, Case.Sensitive, "which document");
    }

    // ─────────────────────────────────────────────────────── absent, and null, are different faults

    /// <summary>
    /// 🔒 An <b>absent</b> required pointer is refused rather than defaulted —
    /// <see cref="MissingContentException"/>, naming the pointer.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two shapes, at two depths.</b> A missing coefficient on a script row and a missing
    /// <c>op</c> on an embedded effect travel through different block readers; a rule that only
    /// covered the row would let an effect silently lose its operation.
    /// </remarks>
    [Theory]
    [InlineData(
        "\"coefficients\": { \"hp\": 2.8, \"atk\": 0.95, \"def\": 1.3, \"aspd\": 0.6 }",
        "\"coefficients\": { \"hp\": 2.8, \"atk\": 0.95, \"def\": 1.3 }",
        "content/bosses/bosses.json#/scripts/4/coefficients/aspd",
        "a coefficient of a 17 §1.2 row")]
    [InlineData(
        "\"op\": \"REFLECT\",",
        "",
        "content/bosses/bosses.json#/scripts/4/effects/2/op",
        "the operation of an embedded 18 §1 effect")]
    public void An_absent_required_pointer_is_refused_rather_than_defaulted(
        string find, string replaceWith, string pointer, string what)
    {
        var thrown = Should.Throw<MissingContentException>(() => Read(find, replaceWith));

        thrown.Reference.ShouldBe(pointer, what);
    }

    /// <summary>
    /// 🔒 An authored <b>null</b> at a required pointer is a different fault and earns a different
    /// exception — <see cref="UnauthorisedTunableException"/>, <em>"the design docs do not authorise a
    /// value here"</em>.
    /// </summary>
    /// <remarks>
    /// 🔴 The test-side reader it replaced handed the JSON <c>null</c> back, so a numeric read threw a
    /// serialisation error about a token type — a message about JSON rather than content, at a pointer
    /// nobody could grep for. Two shapes, at two depths: a top-level baseline stat and a coefficient
    /// inside the scripts array.
    /// </remarks>
    [Theory]
    [InlineData(
        "\"crit\": 0.05,",
        "\"crit\": null,",
        "content/bosses/bosses.json#/secondaryStats/crit",
        "17 §1.2's shared baseline")]
    [InlineData(
        "\"coefficients\": { \"hp\": 2.5, \"atk\": 0.85, \"def\": 0.7, \"aspd\": 0.75 }",
        "\"coefficients\": { \"hp\": 2.5, \"atk\": 0.85, \"def\": 0.7, \"aspd\": null }",
        "content/bosses/bosses.json#/scripts/1/coefficients/aspd",
        "a coefficient of a 17 §1.2 row")]
    public void An_authored_null_at_a_required_pointer_is_refused_rather_than_defaulted(
        string find, string replaceWith, string pointer, string what)
    {
        var thrown = Should.Throw<UnauthorisedTunableException>(() => Read(find, replaceWith));

        thrown.Reference.ShouldBe(pointer, what);
    }

    /// <summary>
    /// 🔴 The negative control for the two cases above, and the discriminator: the SAME anchors,
    /// edited to a legal value, read back as that value.
    /// </summary>
    /// <remarks>
    /// Without this the refusals are consistent with a reader that refuses every edited document —
    /// or with one that never reads the file at all. It is also the proof that the re-pointed suites
    /// really are reading the shipped bytes: the numbers below are not the authored ones.
    /// </remarks>
    [Fact]
    public void The_same_anchors_edited_to_a_legal_value_are_read_as_that_value()
    {
        Read("\"crit\": 0.05,", "\"crit\": 0.07,").Crit.ShouldBe(0.07);

        Read(
                "\"coefficients\": { \"hp\": 2.5, \"atk\": 0.85, \"def\": 0.7, \"aspd\": 0.75 }",
                "\"coefficients\": { \"hp\": 2.5, \"atk\": 0.85, \"def\": 0.7, \"aspd\": 0.99 }")
            .Of(Gulgrot).Script.Coefficients.Aspd.ShouldBe(0.99);

        // And the shipped tree is still the shipped tree — 21 §3.3's "overrides never edit the
        // canonical files", asserted rather than assumed.
        ShippedBosses.Catalogue.Crit.ShouldBe(0.05);
        ShippedBosses.Catalogue.Of(Gulgrot).Script.Coefficients.Aspd.ShouldBe(0.75);
    }

    /// <summary>
    /// 🔒 An <b>optional</b> key is the one place a <c>null</c> is not a fault — it is the hole
    /// itself, and it stays <c>null</c> rather than becoming a zero.
    /// </summary>
    /// <remarks>
    /// 🔴 Paired with the row that authors the same key, because "null" alone is what a reader that
    /// read nothing would also answer.
    /// </remarks>
    [Fact]
    public void An_optional_key_that_is_absent_or_null_stays_null_and_never_becomes_a_zero()
    {
        var shipped = ShippedBosses.Catalogue;

        shipped.Of(Gulgrot).Script.AddsPowerFraction.ShouldBeNull(
            "17 §3 authors no SUMMON, so the key is absent — and an adds fraction of 0.0 would be a " +
            "fight whose adds have no power at all");
        shipped.Of(Thornmaw).Script.AddsPowerFraction.ShouldBe(
            0.30, "the discriminator — the reader does pick the key up where it is authored");

        // An authored null reads exactly as the absent key does: the documents authorise no value.
        // 🔒 17 §1.2's fixedPower, which only the FTUE row carries — an optional key with a value,
        // so the same anchor serves as its own discriminator on the line below.
        Read("\"fixedPower\": 900,", "\"fixedPower\": null,").Of(Ftue).FixedPower.ShouldBeNull(
            "an authored null is the documents authorising no value, and 900.0 would be a number " +
            "nobody wrote");

        Read("\"fixedPower\": 900,", "\"fixedPower\": 950,").Of(Ftue).FixedPower.ShouldBe(
            950.0, "the discriminator — the same pointer, authored, is read as what it says");

        shipped.Of(Ftue).FixedPower.ShouldBe(900.0, "17 §1.2's fixed authored input, as shipped");
    }

    // ─────────────────────────────────────────────────────── the reader refuses what it cannot map

    /// <summary>
    /// 🔒 A key this reader does not map is refused, not ignored. A silently dropped key would leave
    /// every case in this namespace asserting over a script that is not the one on disk.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two shapes.</b> A misspelled key on a mechanic — <c>telegraphTicks</c> where
    /// <c>telegraphSeconds</c> was meant, which is the mistake that deletes a wind-up in silence —
    /// and a new key at script level, which is the one a schema change would introduce.
    /// </remarks>
    [Theory]
    [InlineData(
        "{ \"effectId\": \"BOSS_THORNMAW_P2_ROOT\", \"telegraphSeconds\": 1.2 }",
        "{ \"effectId\": \"BOSS_THORNMAW_P2_ROOT\", \"telegraphTicks\": 24 }",
        "telegraphTicks",
        "content/bosses/bosses.json#/scripts/0/phases/1/mechanics/0")]
    [InlineData(
        "\"chapter\": 8,",
        "\"chapters\": 8,",
        "chapters",
        "content/bosses/bosses.json#/scripts/7")]
    public void A_key_this_reader_does_not_map_is_refused_rather_than_ignored(
        string find, string replaceWith, string key, string pointer)
    {
        var thrown = Should.Throw<ContentTypeMismatchException>(() => Read(find, replaceWith));

        thrown.Reference.ShouldBe(pointer, "which object carries it");
        thrown.Message.ShouldContain(key, Case.Sensitive, "which key");
    }

    /// <summary>
    /// 🔒 A token outside a closed vocabulary is refused rather than silently becoming the enum's
    /// zero member — which is a real operation, a real trigger and a real scope in every case.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Three shapes across three vocabularies</b>, because each is parsed by its own method and
    /// a rule proved over one says nothing about the other two.
    /// </remarks>
    [Theory]
    [InlineData("\"op\": \"REFLECT\",", "\"op\": \"DEFLECT\",",
        "content/bosses/bosses.json#/scripts/4/effects/2/op", "18 §2's operations")]
    [InlineData("\"kind\": \"PERIODIC\", \"interval\": 16.0 }",
        "\"kind\": \"PERIODIQUE\", \"interval\": 16.0 }",
        "content/bosses/bosses.json#/scripts/5/effects/5/trigger/kind", "18 §3's trigger kinds")]
    [InlineData("\"duration\": { \"seconds\": 6.0, \"scope\": \"BATTLE\", \"until\": \"WARD_BROKEN\" }",
        "\"duration\": { \"seconds\": 6.0, \"scope\": \"SIEGE\", \"until\": \"WARD_BROKEN\" }",
        "content/bosses/bosses.json#/scripts/2/effects/3/duration/scope", "18 §6's duration scopes")]
    public void A_token_outside_a_closed_vocabulary_is_refused_rather_than_defaulted(
        string find, string replaceWith, string pointer, string vocabulary)
    {
        var thrown = Should.Throw<ContentTypeMismatchException>(() => Read(find, replaceWith));

        thrown.Reference.ShouldBe(pointer, vocabulary);
        thrown.Actual.ShouldBe(ContentValueKind.Text);
    }

    // ─────────────────────────────────────────────────────── helpers

    /// <summary>
    /// The catalogue over the shipped document with one anchor rewritten — a single-edit mutation,
    /// in memory.
    /// </summary>
    /// <remarks>
    /// 🔒 The anchor must occur exactly once. <c>string.Replace</c> hits every occurrence, so an
    /// anchor that appears twice is a case that reads as one edit and is not — and the rule that
    /// fires may then not be the rule the case names. The precedent is
    /// <c>SlayIdleRepeat.Application.Tests</c>' <c>RepoData.SourceWithEdit</c>.
    /// </remarks>
    private static BossCatalogue Read(string find, string replaceWith)
    {
        var original = File.ReadAllText(
            Path.Combine(GameDataLoader.DataRoot, BossCatalogue.Document));

        var occurrences = Occurrences(original, find);

        if (occurrences != 1)
        {
            throw new InvalidOperationException(
                $"'{find}' occurs {occurrences} time(s) in {BossCatalogue.Document}, not once. A " +
                "mutation that is not the single edit it reads as proves nothing about the rule the " +
                "case names. The data moved — fix the anchor, do not delete the case.");
        }

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BossCatalogue.Document] = original.Replace(find, replaceWith, StringComparison.Ordinal),
        };

        return BossCatalogue.Read(GameDataLoader.LoadWith(GameDataLoader.DataRoot, replacements));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var at = text.IndexOf(value, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
