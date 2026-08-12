using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 The shipped <c>content/statuses.json</c> against `05` §5 — the transcription, asserted where
/// the real file can actually be read.
/// </summary>
/// <remarks>
/// <para>
/// <c>SlayIdleRepeat.Core.Tests</c> owns the <em>behaviour</em> of the cadence, the stun window and
/// the catalogue reader; it references <c>SlayIdleRepeat.Core</c> and nothing else, so it has no JSON
/// reader and cannot see this document. This suite is where the document's numbers meet the design
/// section that authorises them — <c>game-data/README.md</c>: <em>"a number nobody can trace to a
/// section is a number nobody will defend."</em>
/// </para>
/// <para>
/// 🔒 Every value is read with a <see cref="ContentSnapshot"/> reader, which throws on a
/// <c>null</c> — so a constant that was quietly nulled fails here rather than reading as zero. The
/// one deliberate <c>null</c> is asserted through <see cref="ContentSnapshot.IsAuthorised"/>, which
/// is the only way to observe it without throwing.
/// </para>
/// </remarks>
public sealed class StatusesDataTests
{
    private const string Document = "content/statuses.json";

    /// <summary>`05` §5's twelve, in the order the section's table prints them.</summary>
    private static readonly string[] StatusOrder =
    {
        "BURN", "POISON", "BLEED", "FREEZE", "STUN", "WEAKEN",
        "SUNDER", "SPORE", "RAGE", "WARD", "HASTE", "REGEN",
    };

    private static ContentSnapshot Data() => ContentLoader.Load(RepoData.Source()).Require();

    /// <summary>
    /// 🔒 `05` §5's catalogue is a <c>content/</c> document, not a seventeenth <c>tuning/</c> file.
    /// </summary>
    /// <remarks>
    /// Doc 21's catalogue names exactly sixteen tuning files and a seventeenth is a bug; these are
    /// combat balance constants rather than economy dials the economy simulator sweeps. The same
    /// ruling <c>combat_caps.json</c> and <c>enemies.json</c> already record.
    /// </remarks>
    [Fact]
    public void The_document_is_in_the_snapshot_and_under_content_rather_than_tuning()
    {
        var snapshot = Data();

        snapshot.DocumentPaths.ShouldContain(Document);
        snapshot.DocumentPaths.ShouldNotContain("tuning/statuses.json");
    }

    /// <summary>
    /// 🔒 `05` §5 fixes <b>twelve</b> statuses, by these ids, in this order.
    /// </summary>
    /// <remarks>
    /// The order is part of the assertion because `05` §7's status <c>dataId</c> is the row's position
    /// in this table and the ordinal is inside <c>LogHash</c> — reordering the rows would silently
    /// renumber every status event in every committed reference log.
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

    /// <summary>
    /// 🔒 The status vocabulary is enclosed twice and the two copies are equal — this catalogue and
    /// the effect schema's own enum.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two closed sets of twelve that must agree, with something making them.</b> `18` §2.3
    /// refers to the twelve rather than restating them, and the effect schema encloses them so an
    /// authored op cannot name a thirteenth; this file is the catalogue those ids are drawn from. A
    /// status added to one and not the other is authorable-but-unimplemented, or
    /// implemented-but-unauthorable, and neither fails anywhere else.
    /// </remarks>
    [Fact]
    public void The_catalogues_ids_are_exactly_the_effect_schemas_status_vocabulary()
    {
        // Read from the raw document set rather than the snapshot: a ContentSnapshot holds the DATA
        // documents a build ships, and schemas govern them from outside it.
        var text = RepoData.Documents["schema/effect.schema.json"];

        // The enum sits under $defs/statusId, whose members are the only place in that file where
        // all twelve appear together.
        foreach (var id in StatusOrder)
        {
            text.ShouldContain('"' + id + '"', Case.Sensitive);
        }

        // And the shipped catalogue is the same set — asserted in the direction that catches an id
        // added to the schema and not here, which the loop above cannot see.
        var catalogue = Data().GetDocument(Document).Root;
        catalogue.TryGetMember("statuses", out var rows).ShouldBeTrue();

        rows!.Items.Count.ShouldBe(12, "05 §5 fixes twelve and the effect schema encloses twelve");
    }

    /// <summary>
    /// 📐 `05` §5 — <c>BLEED</c>'s missing-HP scaling term, the one 📐 TUNABLE the section carries.
    /// </summary>
    /// <remarks>
    /// `05` §5 states the tick as <em>"that amount × (1 + target's missing-HP fraction)"</em>, so the
    /// authored coefficient is the multiplier on the missing-HP fraction and <c>1.0</c> is that
    /// expression written out. It is the key whose citation closes this section's 📐 baseline entry.
    /// </remarks>
    [Fact]
    public void BLEEDs_missing_HP_scaling_term_is_the_one_05_section_5_states()
    {
        Data().ReadDouble(Document + "#/bleedMissingHpScaling").ShouldBe(1.0);
    }

    /// <summary>
    /// 🔒 `05` §5's <c>STUN</c> literals — <em>"Max 1.5 s per application, with a 3 s immunity window
    /// after"</em>.
    /// </summary>
    [Fact]
    public void STUNs_cap_and_immunity_window_are_the_two_numbers_05_section_5_states()
    {
        var data = Data();

        data.ReadDouble(Document + "#/stun/maxSecondsPerApplication").ShouldBe(1.5);
        data.ReadDouble(Document + "#/stun/immunityWindowSeconds").ShouldBe(3.0);
    }

    /// <summary>
    /// 🔒 `05` §5's stack ceilings — stated for five of the twelve and for no others.
    /// </summary>
    /// <remarks>
    /// The absences are asserted as well as the numbers. `05` §5 fixes no stacking for the other
    /// seven, so `18` §6's per-effect block governs them and a ceiling appearing here would be this
    /// file deciding on the section's behalf.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `05` §5 — <c>BLEED</c> <em>"does not stack; reapplication refreshes"</em>, which is `18`
    /// §6's <c>NONE</c> plus <c>refreshOnReapply</c>.
    /// </summary>
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

    /// <summary>
    /// 🔒 `05` §5 states <c>FREEZE</c>'s potency as a literal <em>−50% ASPD</em>, and it is the only
    /// row that carries one.
    /// </summary>
    [Fact]
    public void FREEZE_is_the_only_row_whose_potency_the_section_states_as_a_literal()
    {
        Data().ReadDouble(Document + "#/statuses/3/fixedPotency").ShouldBe(-0.5);

        var withLiterals = StatusOrder
            .Where(id => Row(id).TryGetMember("fixedPotency", out _))
            .ToArray();

        withLiterals.ShouldBe(new[] { "FREEZE" });
    }

    /// <summary>
    /// 🔒 `05` §5 says <c>RAGE</c> <em>"decays over D s"</em> and states no curve — the one
    /// unauthorised value in this file, and it stays <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Steering S6 / `16` R6: a plausible linear ramp here would be a balance decision invented by
    /// the implementer and invisible afterwards. <c>RAGE</c> is the only row carrying the key, and it
    /// carries it so the hole is greppable rather than absent. Counted by
    /// <c>RealDataNegativeCaseTests</c>'s population guard; filling it changes that number in the
    /// same commit.
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

    /// <summary>
    /// 🔒 The six statuses `05` §5 states as a percentage of a stat name that stat, and the other six
    /// name none.
    /// </summary>
    /// <remarks>
    /// `05` §5's <em>"−X% healing received"</em> is <c>HEAL_PCT</c>, which `05` §2 bases at 1.0 as a
    /// multiplier on all healing received — so a <c>SPORE</c> pointed at any other stat would be a
    /// different debuff wearing the same name.
    /// </remarks>
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
