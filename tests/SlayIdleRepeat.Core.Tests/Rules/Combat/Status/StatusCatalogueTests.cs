using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>The twelve statuses, as the catalogue reads them.</summary>
public sealed class StatusCatalogueTests
{
    /// <summary>Exactly twelve statuses, by these exact ids, in this order.</summary>
    /// <remarks>
    /// The floor for every rule in this file and every <c>MemberData</c> that walks the catalogue:
    /// a reader that returned an empty or shortened list would make each of them report success over
    /// nothing. The order is asserted as well as the set, because <c>StatusLogId</c>'s ordinals are
    /// table positions and they are inside <c>LogHash</c>.
    /// </remarks>
    [Fact]
    public void The_catalogue_holds_05_section_5s_twelve_statuses_in_the_sections_order()
    {
        var catalogue = StatusFixtures.Catalogue();

        catalogue.Statuses.Count.ShouldBe(12);
        catalogue.Statuses.Select(s => s.Id).ShouldBe(new[]
        {
            "BURN", "POISON", "BLEED", "FREEZE", "STUN", "WEAKEN",
            "SUNDER", "SPORE", "RAGE", "WARD", "HASTE", "REGEN",
        });
    }

    /// <summary>Three DoTs, five Debuffs, three Buffs and one HoT.</summary>
    /// <remarks>
    /// Stated per status rather than as four counts: a count is satisfied by any permutation, and
    /// swapping <c>REGEN</c> and <c>BURN</c>'s kinds would keep every count right while routing a
    /// heal into the damage pipeline.
    /// </remarks>
    [Theory]
    [InlineData("BURN", nameof(StatusKind.DoT))]
    [InlineData("POISON", nameof(StatusKind.DoT))]
    [InlineData("BLEED", nameof(StatusKind.DoT))]
    [InlineData("FREEZE", nameof(StatusKind.Debuff))]
    [InlineData("STUN", nameof(StatusKind.Debuff))]
    [InlineData("WEAKEN", nameof(StatusKind.Debuff))]
    [InlineData("SUNDER", nameof(StatusKind.Debuff))]
    [InlineData("SPORE", nameof(StatusKind.Debuff))]
    [InlineData("RAGE", nameof(StatusKind.Buff))]
    [InlineData("WARD", nameof(StatusKind.Buff))]
    [InlineData("HASTE", nameof(StatusKind.Buff))]
    [InlineData("REGEN", nameof(StatusKind.HoT))]
    public void Each_status_carries_the_type_05_section_5_gives_it(string id, string kind)
    {
        StatusFixtures.Catalogue().Of(id).Kind.ShouldBe(Enum.Parse<StatusKind>(kind));
    }

    /// <summary>Exactly the DoT and HoT kinds are cadence-driven — a <c>Debuff</c> and a <c>Buff</c> have no per-second amount for a boundary to land.</summary>
    [Fact]
    public void Only_the_DoTs_and_the_HoT_are_driven_by_the_05_section_3_1_cadence()
    {
        var ticking = StatusFixtures.Catalogue().Statuses.Where(s => s.Ticks).Select(s => s.Id).ToArray();

        ticking.ShouldBe(new[] { "BURN", "POISON", "BLEED", "REGEN" });
    }

    /// <summary>A stack ceiling is authored for exactly five of the twelve, and none for the other seven.</summary>
    /// <remarks>
    /// The seven <c>null</c>s are a statement, not seven holes: stacking is fixed for <c>BURN</c>,
    /// <c>POISON</c>, <c>BLEED</c>, <c>SUNDER</c> and <c>SPORE</c> and stated for nothing else, so
    /// for the rest the per-effect block governs and this catalogue must not pre-empt it.
    /// </remarks>
    [Fact]
    public void Only_the_five_statuses_05_section_5_states_a_stacking_rule_for_carry_one()
    {
        var catalogue = StatusFixtures.Catalogue();

        catalogue.Statuses.Where(s => s.Stacking is not null).Select(s => s.Id)
            .ShouldBe(new[] { "BURN", "POISON", "BLEED", "SUNDER", "SPORE" });

        catalogue.Of("BURN").Stacking!.MaxStacks.ShouldBe(5);
        catalogue.Of("POISON").Stacking!.MaxStacks.ShouldBe(3);
        catalogue.Of("SUNDER").Stacking!.MaxStacks.ShouldBe(5);
        catalogue.Of("SPORE").Stacking!.MaxStacks.ShouldBe(4);

        // BLEED is "does not stack; reapplication refreshes" — NONE plus refreshOnReapply.
        catalogue.Of("BLEED").Stacking!.Mode.ShouldBe(StackingMode.NONE);
        catalogue.Of("BLEED").Stacking!.RefreshOnReapply.ShouldBe(true);
    }

    /// <summary>The stacking block an application uses: the effect's, else the status's, else the canonical one.</summary>
    /// <remarks>
    /// All three arms are probed. The third is the one Thornmaw's <c>RAGE</c> needs — it authors no
    /// <c>stacking</c> at all and <c>RAGE</c> is not one of the five — and refusing there would
    /// throw on authored content.
    /// </remarks>
    [Fact]
    public void The_stacking_block_falls_back_from_the_effect_to_05_section_5_to_18_section_1()
    {
        var catalogue = StatusFixtures.Catalogue();
        var authored = new EffectStacking { Mode = StackingMode.ADDITIVE, MaxStacks = 2 };

        catalogue.StackingFor("BURN", authored).ShouldBeSameAs(authored);
        catalogue.StackingFor("BURN", authored: null).MaxStacks.ShouldBe(5);
        catalogue.StackingFor("RAGE", authored: null).ShouldBe(StatusCatalogue.CanonicalStacking);
    }

    /// <summary><c>FREEZE</c>'s potency is a literal -50% ASPD, and it is the only row that carries one.</summary>
    [Fact]
    public void FREEZE_is_the_only_status_whose_potency_05_section_5_states_as_a_literal()
    {
        var catalogue = StatusFixtures.Catalogue();

        catalogue.Statuses.Where(s => s.FixedPotency is not null).Select(s => s.Id)
            .ShouldBe(new[] { "FREEZE" });

        catalogue.Of("FREEZE").FixedPotency.ShouldBe(-0.5);
    }

    /// <summary>The six <c>TargetStatPct</c> rows name a stat; the other six name none and refuse to be asked.</summary>
    /// <remarks>
    /// The refusal half is the negative control: a <c>RequireStat</c> that returned a default would
    /// silently debuff <c>MAX_HP</c> on every status that has no stat, and the six positive rows
    /// would still pass.
    /// </remarks>
    [Fact]
    public void The_six_stat_statuses_name_their_stat_and_the_other_six_refuse_to_be_asked()
    {
        var catalogue = StatusFixtures.Catalogue();

        catalogue.Of("FREEZE").RequireStat().ShouldBe(StatId.ASPD);
        catalogue.Of("HASTE").RequireStat().ShouldBe(StatId.ASPD);
        catalogue.Of("WEAKEN").RequireStat().ShouldBe(StatId.ATK);
        catalogue.Of("RAGE").RequireStat().ShouldBe(StatId.ATK);
        catalogue.Of("SUNDER").RequireStat().ShouldBe(StatId.DEF);
        catalogue.Of("SPORE").RequireStat().ShouldBe(StatId.HEAL_PCT);

        var thrown = Should.Throw<InvalidOperationException>(() => catalogue.Of("BURN").RequireStat());
        thrown.Message.ShouldContain("names no stat", Case.Sensitive);
    }

    /// <summary><c>RAGE</c> "decays over D s" but authors no curve — the hole stays a hole and fails by name.</summary>
    /// <remarks>
    /// <c>RAGE</c> is the only row carrying the key, so the assertion is stated over the whole
    /// catalogue rather than over that one row: a second status quietly acquiring a decay would
    /// otherwise be invisible here.
    /// </remarks>
    [Fact]
    public void RAGEs_decay_curve_is_unauthorised_and_asking_for_it_fails_by_name()
    {
        var catalogue = StatusFixtures.Catalogue();

        catalogue.Statuses.ShouldAllBe(s => s.DecayCurve == null);
        catalogue.Statuses.Count.ShouldBe(12, "ShouldAllBe passes over an empty collection");

        var thrown = Should.Throw<InvalidOperationException>(
            () => catalogue.Of("RAGE").RequireDecayCurve());

        thrown.Message.ShouldContain("authorises no decay curve", Case.Sensitive);
        thrown.Message.ShouldContain("RAGE", Case.Sensitive);
    }

    /// <summary>A status id outside the twelve is refused rather than silently doing nothing.</summary>
    /// <remarks>
    /// <c>StatusOps</c> deliberately does not validate the id — the effect schema encloses the set —
    /// so an effect built in code rather than loaded from JSON arrives here unchecked, and this is
    /// where it stops.
    /// </remarks>
    [Fact]
    public void A_thirteenth_status_id_is_refused()
    {
        var thrown = Should.Throw<Core.Rules.Effects.EffectContextException>(
            () => StatusFixtures.Catalogue().Of("PETRIFY"));

        thrown.Message.ShouldContain("twelve statuses", Case.Sensitive);
    }

    /// <summary>Exactly five potency units are authored — the floor under the per-row mapping below.</summary>
    /// <remarks>
    /// This replaced a theory that could not fail: it paired each potency unit with a status unit
    /// and then asserted only <c>Enum.IsDefined</c> on both, and because every <c>InlineData</c> was
    /// a <c>nameof</c>, both checks were true at compile time. The pairing — the entire claim in its
    /// name — was never checked; mapping <c>TargetAspdPct</c> onto <c>FlatHp</c> left all five
    /// passing. What survives is the half that bites: a sixth unit fails here, forcing somebody to
    /// extend the mapping.
    /// </remarks>
    [Fact]
    public void The_05_section_6_1a_potency_units_are_the_five_that_section_states()
    {
        Enum.GetValues<PotencyBasis>().Length.ShouldBe(5);
    }

    /// <summary>
    /// The on-hit statuses that enemy rows apply are all catalogue statuses, and each row's unit is
    /// the one this catalogue gives that status.
    /// </summary>
    /// <remarks>
    /// This is the assertion the theory above cannot make. Mapping the two enums proves the
    /// vocabularies overlap; this proves the rows agree — that an enemy's <c>FREEZE</c> row really
    /// is measured in the unit this catalogue gives <c>FREEZE</c>. A row retyped to a different unit
    /// would pass every enum-level check and apply a fraction of Max HP as a fraction of ATK.
    /// </remarks>
    [Theory]
    [InlineData("BLEED", nameof(StatusPotencyBasis.ApplierAtkPctPerSecond))]
    [InlineData("POISON", nameof(StatusPotencyBasis.TargetMaxHpPctPerSecond))]
    [InlineData("BURN", nameof(StatusPotencyBasis.ApplierAtkPctPerSecond))]
    [InlineData("FREEZE", nameof(StatusPotencyBasis.TargetStatPct))]
    [InlineData("SUNDER", nameof(StatusPotencyBasis.TargetStatPct))]
    [InlineData("SPORE", nameof(StatusPotencyBasis.TargetStatPct))]
    public void Every_status_05_section_6_1a_applies_carries_the_unit_this_catalogue_gives_it(
        string statusId, string basis)
    {
        StatusFixtures.Catalogue().Of(statusId).Basis.ShouldBe(Enum.Parse<StatusPotencyBasis>(basis));
    }

    /// <summary>The <c>dataId</c>s are the twelve table positions, one-based, and cover the catalogue exactly.</summary>
    /// <remarks>
    /// One-based because <c>CombatLog.NoDataId</c> is <c>0</c> and means "names no content": a
    /// zero-based <c>BURN</c> would be indistinguishable from an event naming nothing. The set
    /// equality in both directions is the floor — a mapping that lost a status would leave every
    /// event for it carrying a <c>dataId</c> the replayer cannot resolve.
    /// </remarks>
    [Fact]
    public void Every_status_has_a_distinct_one_based_05_section_7_dataId()
    {
        var ids = StatusFixtures.Catalogue().Statuses.Select(s => s.Id).ToArray();

        StatusLogId.All.OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(ids.OrderBy(id => id, StringComparer.Ordinal));

        var ordinals = ids.Select(StatusLogId.Of).ToArray();

        ordinals.ShouldBe(new ushort[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
        ordinals.Distinct().Count().ShouldBe(12);
        ordinals.ShouldAllBe(o => o != CombatLog.NoDataId);
    }

    /// <summary>A status with no <c>dataId</c> is refused rather than logged as naming nothing.</summary>
    [Fact]
    public void A_status_outside_the_twelve_has_no_dataId()
    {
        var thrown = Should.Throw<Core.Rules.Effects.EffectContextException>(
            () => StatusLogId.Of("PETRIFY"));

        thrown.Message.ShouldContain("APPENDED, never inserted", Case.Sensitive);
    }
}
