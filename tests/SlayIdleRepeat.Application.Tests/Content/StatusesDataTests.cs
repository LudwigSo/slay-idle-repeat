using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests the shipped <c>content/statuses.json</c> values against the numbers the design docs authorise.</summary>
/// <remarks>
/// <c>SlayIdleRepeat.Core.Tests</c> references only <c>SlayIdleRepeat.Core</c> and has no JSON
/// reader, so it can't see this document; this suite is where the numbers get checked. Every value
/// is read with a <see cref="ContentSnapshot"/> reader, which throws on null, so a quietly nulled
/// constant fails here rather than reading as zero. The one deliberate null is asserted through
/// <see cref="ContentSnapshot.IsAuthorised"/>.
/// </remarks>
public sealed class StatusesDataTests
{
    private const string Document = "content/statuses.json";

    /// <summary>The twelve statuses, in the order the design doc's table prints them.</summary>
    private static readonly string[] StatusOrder =
    {
        "BURN", "POISON", "BLEED", "FREEZE", "STUN", "WEAKEN",
        "SUNDER", "SPORE", "RAGE", "WARD", "HASTE", "REGEN",
    };

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>The status catalogue is a content/ document, not a seventeenth tuning/ file.</summary>
    /// <remarks>These are combat balance constants, not economy dials the economy simulator sweeps — the same ruling combat_caps.json and enemies.json already record.</remarks>
    [Fact]
    public void The_document_is_in_the_snapshot_and_under_content_rather_than_tuning()
    {
        var snapshot = Data();

        snapshot.DocumentPaths.ShouldContain(Document);
        snapshot.DocumentPaths.ShouldNotContain("tuning/statuses.json");
    }

    /// <summary>Fixes twelve statuses, by these ids, in this order.</summary>
    /// <remarks>
    /// The order is part of the assertion: a status's <c>dataId</c> is its row position in this
    /// table, and that ordinal is inside <c>LogHash</c> — reordering would silently renumber every
    /// status event in every committed reference log.
    /// </remarks>
    [Fact]
    public void The_file_carries_05_section_5s_twelve_statuses_in_the_sections_order()
    {
        var statuses = Data().GetDocument(Document).Root;

        statuses.TryGetMember("statuses", out var rows).ShouldBeTrue();
        rows!.Items.Count.ShouldBe(12);

        rows.Items.Select(r =>
        {
            r.TryGetMember("id", out var id);
            return id!.AsText();
        }).ShouldBe(StatusOrder);
    }

    /// <summary>The status vocabulary is enclosed twice — this catalogue and the effect schema's own enum — and the two copies must agree.</summary>
    /// <remarks>A status added to one and not the other is authorable-but-unimplemented, or implemented-but-unauthorable, and neither fails anywhere else.</remarks>
    [Fact]
    public void The_catalogues_ids_are_exactly_the_effect_schemas_status_vocabulary()
    {
        // Both directions, over the PARSED enum — a raw-text substring scan caught nothing in the
        // other direction (a thirteenth id added to $defs/statusId would pass, and an id only in a
        // description would satisfy the scan).
        //
        // Read from the raw document set rather than the snapshot: a ContentSnapshot holds the data
        // documents a build ships, and schemas govern them from outside it.
        using var schema = JsonDocument.Parse(RepoData.Documents["schema/effect.schema.json"]);

        var enclosed = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("statusId")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();

        var catalogue = Data().GetDocument(Document).Root;
        catalogue.TryGetMember("statuses", out var rows).ShouldBeTrue();

        var shipped = rows!.Items.Select(r =>
        {
            r.TryGetMember("id", out var id);
            return id!.AsText();
        }).ToArray();

        // Set equality both ways, between the two files themselves — neither side is this test's
        // constant, so an id added to either alone is a failure.
        enclosed.OrderBy(s => s, StringComparer.Ordinal)
            .ShouldBe(shipped.OrderBy(s => s, StringComparer.Ordinal));

        shipped.Length.ShouldBe(12, "05 §5 fixes twelve");
    }

    /// <summary>BLEED's missing-HP scaling term.</summary>
    /// <remarks>The tick is "that amount × (1 + target's missing-HP fraction)", so the authored coefficient is the multiplier on the missing-HP fraction and 1.0 is that expression written out.</remarks>
    [Fact]
    public void BLEEDs_missing_HP_scaling_term_is_the_one_05_section_5_states()
    {
        Data().ReadDouble(Document + "#/bleedMissingHpScaling").ShouldBe(1.0);
    }

    /// <summary>STUN's cap and immunity window: max 1.5 s per application, with a 3 s immunity window after.</summary>
    [Fact]
    public void STUNs_cap_and_immunity_window_are_the_two_numbers_05_section_5_states()
    {
        var data = Data();

        data.ReadDouble(Document + "#/stun/maxSecondsPerApplication").ShouldBe(1.5);
        data.ReadDouble(Document + "#/stun/immunityWindowSeconds").ShouldBe(3.0);
    }

    /// <summary>Stack ceilings, stated for five of the twelve statuses and for no others.</summary>
    /// <remarks>The absences are asserted as well as the numbers: the other seven have no stacking rule here, so a per-effect block governs them instead, and a ceiling appearing here would be this file overriding that.</remarks>
    [Theory]
    [InlineData("BURN", 5)]
    [InlineData("POISON", 3)]
    [InlineData("SUNDER", 5)]
    [InlineData("SPORE", 4)]
    [InlineData("BLEED", null)]
    [InlineData("FREEZE", null)]
    [InlineData("STUN", null)]
    [InlineData("WEAKEN", null)]
    [InlineData("RAGE", null)]
    [InlineData("WARD", null)]
    [InlineData("HASTE", null)]
    [InlineData("REGEN", null)]
    public void Each_status_carries_exactly_the_stack_ceiling_05_section_5_states(string id, int? ceiling)
    {
        var row = Row(id);

        if (!row.TryGetMember("stacking", out var stacking))
        {
            ceiling.ShouldBeNull("05 §5 states no stacking rule for " + id);
            return;
        }

        if (stacking!.TryGetMember("maxStacks", out var max))
        {
            max!.AsInt32().ShouldBe(ceiling!.Value);
            return;
        }

        ceiling.ShouldBeNull("05 §5 states no stack ceiling for " + id);
    }

    /// <summary>BLEED does not stack; reapplication refreshes — the NONE mode plus refreshOnReapply.</summary>
    [Fact]
    public void BLEED_is_the_authored_user_of_NONE_plus_refreshOnReapply()
    {
        var row = Row("BLEED");

        row.TryGetMember("stacking", out var stacking).ShouldBeTrue();
        stacking!.TryGetMember("mode", out var mode).ShouldBeTrue();
        mode!.AsText().ShouldBe("NONE");

        stacking.TryGetMember("refreshOnReapply", out var refresh).ShouldBeTrue();
        refresh!.AsBoolean().ShouldBeTrue();

        row.TryGetMember("scalesWithTargetMissingHp", out var scales).ShouldBeTrue();
        scales!.AsBoolean().ShouldBeTrue();
    }

    /// <summary>FREEZE's potency is a literal −50% ASPD, and it is the only row that carries one.</summary>
    [Fact]
    public void FREEZE_is_the_only_row_whose_potency_the_section_states_as_a_literal()
    {
        Data().ReadDouble(Document + "#/statuses/3/fixedPotency").ShouldBe(-0.5);

        var withLiterals = StatusOrder
            .Where(id => Row(id).TryGetMember("fixedPotency", out _))
            .ToArray();

        withLiterals.ShouldBe(new[] { "FREEZE" });
    }

    /// <summary>RAGE decays over an unstated shape — the one unauthorised value in this file, and it stays null.</summary>
    /// <remarks>
    /// A plausible linear ramp would be a balance decision invented by the implementer and
    /// invisible afterwards. RAGE is the only row carrying the key, so the hole is greppable rather
    /// than absent; filling it must change <c>RealDataNegativeCaseTests</c>'s population count in
    /// the same commit.
    /// </remarks>
    [Fact]
    public void RAGEs_decay_curve_is_null_and_stays_null()
    {
        var data = Data();
        var pointer = Document + "#/statuses/8/decayCurve";

        data.IsAuthorised(pointer).ShouldBeFalse(
            "05 §5 states that RAGE decays and states no shape for the decay");

        Should.Throw<UnauthorisedTunableException>(() => data.ReadText(pointer));

        // And it is the ONLY row that carries the key — a second status quietly acquiring a decay
        // would otherwise be invisible here.
        StatusOrder.Where(id => Row(id).TryGetMember("decayCurve", out _)).ShouldBe(new[] { "RAGE" });
    }

    /// <summary>The six statuses stated as a percentage of a stat name that stat, and the other six name none.</summary>
    /// <remarks>"−X% healing received" is HEAL_PCT, a multiplier on all healing received — so SPORE pointed at any other stat would be a different debuff wearing the same name.</remarks>
    [Theory]
    [InlineData("FREEZE", "ASPD")]
    [InlineData("HASTE", "ASPD")]
    [InlineData("WEAKEN", "ATK")]
    [InlineData("RAGE", "ATK")]
    [InlineData("SUNDER", "DEF")]
    [InlineData("SPORE", "HEAL_PCT")]
    [InlineData("BURN", null)]
    [InlineData("POISON", null)]
    [InlineData("BLEED", null)]
    [InlineData("STUN", null)]
    [InlineData("WARD", null)]
    [InlineData("REGEN", null)]
    public void Each_status_names_the_stat_05_section_5_gives_it_and_no_other(string id, string? stat)
    {
        var row = Row(id);

        if (row.TryGetMember("stat", out var named))
        {
            named!.AsText().ShouldBe(
                stat, "05 §5 gives " + id + " exactly this stat and no other");
            return;
        }

        stat.ShouldBeNull("05 §5 states no stat for " + id);
    }

    private static ContentValue Row(string id)
    {
        var statuses = Data().GetDocument(Document).Root;
        statuses.TryGetMember("statuses", out var rows);

        return rows!.Items.Single(r =>
        {
            r.TryGetMember("id", out var found);
            return found!.AsText().Equals(id, StringComparison.Ordinal);
        });
    }
}
