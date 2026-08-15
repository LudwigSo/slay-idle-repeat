using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary><c>content/enemies/enemies.json</c> as the derivation reads it.</summary>
public sealed class EnemyCatalogueTests
{
    [Fact]
    public void The_document_is_the_one_05_section_6_names()
    {
        EnemyCatalogue.Document.ShouldBe("content/enemies/enemies.json");

        EnemyCatalogue.HpPerPowerPointer.ShouldBe("content/enemies/enemies.json#/derivation/hpPerPower");
        EnemyCatalogue.ElitePowerMultiplierPointer
            .ShouldBe("content/enemies/enemies.json#/elites/powerMultiplier");
        EnemyCatalogue.NoRepeatPointer
            .ShouldBe("content/enemies/enemies.json#/elites/noRepeatWithPreviousEliteInRun");
        EnemyCatalogue.FixedStatPointer(StatId.HEAL_PCT)
            .ShouldBe("content/enemies/enemies.json#/derivation/fixedStats/HEAL_PCT");
        EnemyCatalogue.TierBonusPointer("MYTHIC")
            .ShouldBe("content/enemies/enemies.json#/enemyLevel/tierBonus/MYTHIC");
    }

    [Fact]
    public void Reading_the_document_produces_every_table_05_section_6_puts_in_data()
    {
        var catalogue = EnemyCatalogue.Read(EnemyFixtures.Snapshot());

        catalogue.Derivation.HpPerPower.ShouldBe(0.60);
        catalogue.Derivation.AtkPerPower.ShouldBe(0.045);
        catalogue.Derivation.DefPerPower.ShouldBe(0.030);
        catalogue.Derivation.BaseAspd.ShouldBe(1.00);
        catalogue.Derivation.RoundingDecimals.ShouldBe(4, "05 §6 — every term rounded to 4 dp");

        catalogue.Archetypes.Count.ShouldBe(8);
        catalogue.Archetype(EnemyArchetype.WARDEN).DefCoef.ShouldBe(2.20);
        catalogue.Archetype(EnemyArchetype.SWARM).UnitsPerDraw.ShouldBe(3, "05 §6.1 — SWARM (×3 units)");

        catalogue.Levels.Of(8, 2).ShouldBe(100, "05 §6.0 — Ch8 base 80 plus Mythic +20");

        catalogue.ElitePowerMultiplier.ShouldBe(2.2);
        catalogue.ModifiersPerElite.ShouldBe(1, "05 §6.2 — plus ONE Elite Modifier");
        catalogue.NoRepeatWithPreviousEliteInRun.ShouldBeTrue();
        catalogue.EliteModifiers.Count.ShouldBe(8);
        catalogue.EliteIdentities.Count.ShouldBe(16);
        catalogue.ChapterPools.Count.ShouldBe(8);

        catalogue.DefaultTargetPriority.ShouldBe(0);
        catalogue.DeprioritisedTargetPriority.ShouldBe(-1);
        catalogue.ForcedTargetPriority.ShouldBe(1);
    }

    /// <summary>
    /// <c>WARDEN</c>'s <c>SUNDER</c> holds wherever a <c>WARDEN</c> appears, and a <c>CASTER</c>'s
    /// status is its chapter's. The other six shapes apply nothing.
    /// </summary>
    [Fact]
    public void On_hit_resolves_per_archetype_and_for_a_CASTER_per_chapter()
    {
        var catalogue = EnemyCatalogue.Read(EnemyFixtures.Snapshot());

        var sunder = catalogue.OnHitFor(EnemyArchetype.WARDEN, chapter: 1);
        sunder!.StatusId.ShouldBe("SUNDER");
        sunder.ProcChancePerLandedHit.ShouldBe(0.35);
        sunder.Potency.ShouldBe(-0.05);
        sunder.Basis.ShouldBe(PotencyBasis.TargetDefPctPerStack);
        sunder.DurationSeconds.ShouldBe(6.0);
        sunder.MaxStacks.ShouldBe(5);
        sunder.RefreshOnReapply.ShouldBe(true);

        // The same parameter set in every chapter.
        catalogue.OnHitFor(EnemyArchetype.WARDEN, chapter: 7).ShouldBe(sunder);

        catalogue.OnHitFor(EnemyArchetype.CASTER, chapter: 1)!.StatusId.ShouldBe("BLEED");
        catalogue.OnHitFor(EnemyArchetype.CASTER, chapter: 7)!.StatusId.ShouldBe("SPORE");
        catalogue.OnHitFor(EnemyArchetype.CASTER, chapter: 7)!.Potency.ShouldBe(-0.10);

        foreach (var quiet in new[]
                 {
                     EnemyArchetype.GRUNT, EnemyArchetype.SWARM, EnemyArchetype.BRUTE,
                     EnemyArchetype.SKIRMISHER, EnemyArchetype.LEECH, EnemyArchetype.REAVER,
                 })
        {
            catalogue.OnHitFor(quiet, chapter: 1).ShouldBeNull($"05 §6.1a gives {quiet} no on-hit status");
        }
    }

    /// <summary>
    /// The one authorised <c>null</c> in this file, and the loud failure it earns: Chapter 5's
    /// <c>FREEZE</c> has no authored stack count.
    /// </summary>
    [Fact]
    public void The_FREEZE_rows_unauthorised_stack_count_stays_null_and_throws_when_used()
    {
        var catalogue = EnemyCatalogue.Read(EnemyFixtures.Snapshot());
        var freeze = catalogue.OnHitFor(EnemyArchetype.CASTER, chapter: 5)!;

        freeze.StatusId.ShouldBe("FREEZE");
        freeze.MaxStacks.ShouldBeNull("05 §6.1a states no stack count for it and 05's status table says nothing");
        freeze.RefreshOnReapply.ShouldBeNull("05 §6.1a states 'refresh on reapply' for the WARDEN set only");

        var thrown = Should.Throw<InvalidOperationException>(() => freeze.RequireMaxStacks());
        thrown.Message.ShouldContain("FREEZE", Case.Sensitive);
        thrown.Message.ShouldContain("16 R6", Case.Sensitive);

        // And the rows that DO state one are unaffected — otherwise this could pass by being broken.
        catalogue.OnHitFor(EnemyArchetype.CASTER, chapter: 2)!.RequireMaxStacks().ShouldBe(3);
    }

    /// <summary>No curse is authored for <c>CURSED</c>, so the id is <c>null</c> and using it throws.</summary>
    [Fact]
    public void CURSEDs_unauthorised_curse_id_stays_null_and_throws_when_used()
    {
        var catalogue = EnemyCatalogue.Read(EnemyFixtures.Snapshot());
        var cursed = catalogue.Modifier(EliteModifier.CURSED);

        cursed.CurseId.ShouldBeNull();
        cursed.Parameter("killWithinSeconds").ShouldBe(20.0);

        var thrown = Should.Throw<InvalidOperationException>(() => cursed.RequireCurseId());
        thrown.Message.ShouldContain("CURSED", Case.Sensitive);
        thrown.Message.ShouldContain("content/curses/ is empty", Case.Sensitive);
    }

    /// <remarks>
    /// The modifier arrives as its name rather than as the enum value: <c>EliteModifier</c> is
    /// <c>internal</c> to <c>SlayIdleRepeat.Core</c> and an xUnit <c>[Theory]</c> method must be
    /// public, so a parameter of that type is less accessible than the method that declares it.
    /// </remarks>
    [Theory]
    [InlineData("ENRAGED", "atkMult", 1.50)]
    [InlineData("ENRAGED", "belowHpFraction", 0.40)]
    [InlineData("ARMORED", "defMult", 1.80)]
    [InlineData("ARMORED", "aspdMult", 0.80)]
    [InlineData("VAMPIRIC", "lifesteal", 0.35)]
    [InlineData("VOLATILE", "deathExplosionHeroMaxHpPct", 0.15)]
    [InlineData("SHIELDED", "startingWardMaxHpPct", 0.30)]
    [InlineData("SWIFT", "aspdMult", 1.60)]
    [InlineData("CURSED", "killWithinSeconds", 20.0)]
    [InlineData("REFLECTIVE", "thorns", 0.25)]
    public void Each_elite_modifier_carries_05_section_6_2s_authored_numbers(
        string modifier, string parameter, double expected) =>
        EnemyCatalogue.Read(EnemyFixtures.Snapshot())
            .Modifier(Enum.Parse<EliteModifier>(modifier))
            .Parameter(parameter)
            .ShouldBe(expected);

    [Fact]
    public void Reading_a_parameter_a_modifier_does_not_state_fails_rather_than_applying_zero()
    {
        var swift = EnemyCatalogue.Read(EnemyFixtures.Snapshot()).Modifier(EliteModifier.SWIFT);

        var thrown = Should.Throw<KeyNotFoundException>(() => swift.Parameter("defMult"));
        thrown.Message.ShouldContain("SWIFT", Case.Sensitive);
        thrown.Message.ShouldContain("aspdMult", Case.Sensitive);
    }

    [Fact]
    public void The_sixteen_elite_identities_map_to_the_base_archetypes_05_section_6_2_assigns()
    {
        var catalogue = EnemyCatalogue.Read(EnemyFixtures.Snapshot());

        catalogue.EliteIdentities.Count.ShouldBe(16);

        foreach (var (id, archetype) in EnemyFixtures.Identities)
        {
            catalogue.EliteIdentities[id].ShouldBe(archetype, $"05 §6.2 assigns {id} to {archetype}");
        }
    }

    /// <summary>Nothing in this document has a default: an unauthorised value is a throw.</summary>
    [Fact]
    public void An_unauthorised_derivation_constant_throws_rather_than_defaulting()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => EnemyCatalogue.Read(EnemyFixtures.Snapshot("derivation/hpPerPower")));

        Should.Throw<UnauthorisedTunableException>(
            () => EnemyCatalogue.Read(EnemyFixtures.Snapshot("derivation/fixedStats/HEAL_PCT")));
    }

    /// <summary>
    /// The floor under the six stats named in code: if <see cref="EnemyCatalogue.FixedStats"/>
    /// shrank, a stat would silently stop being read and the derivation's completeness check would
    /// report the wrong cause.
    /// </summary>
    [Fact]
    public void The_six_stats_05_section_6_fixes_are_named_in_code()
    {
        EnemyCatalogue.FixedStats.ShouldBe(new[]
        {
            StatId.BLOCK, StatId.PEN, StatId.DMG_PCT, StatId.DR_PCT, StatId.HEAL_PCT, StatId.THORNS,
        });

        EnemyCatalogue.FixedStats.Count.ShouldBe(StatIds.Combat.Count - 8,
            "05 §6 derives eight stats and fixes the rest; the two counts are one statement");
    }

    /// <summary>The floor under <see cref="EnemyArchetype"/> itself, and it is ordered.</summary>
    /// <remarks>
    /// <c>EnemyCatalogue.ReadPools</c> and <c>EnemyFixtures</c> both walk
    /// <c>Enum.GetValues&lt;EnemyArchetype&gt;()</c> and zip it positionally against the authored
    /// weight columns. Reorder the enum and every chapter's weights are silently permuted, with the
    /// shipped data unchanged and nothing red — the enum's declaration order is load-bearing data,
    /// not a style choice.
    /// </remarks>
    [Fact]
    public void The_eight_archetypes_are_declared_in_05_section_6_1s_table_order()
    {
        Enum.GetNames<EnemyArchetype>().ShouldBe(new[]
        {
            "GRUNT", "SWARM", "BRUTE", "SKIRMISHER", "WARDEN", "CASTER", "LEECH", "REAVER",
        });

        // And the pool built from that order really does carry the Chapter 1 row — which is what
        // makes the ordering claim above observable rather than decorative.
        var chapterOne = EnemyCatalogue.Read(EnemyFixtures.Snapshot()).Pool(1);

        chapterOne.WeightOf(EnemyArchetype.GRUNT).ShouldBe(40.0);
        chapterOne.WeightOf(EnemyArchetype.REAVER).ShouldBe(0.0, "05 §6.4 — Chapter 1 has no REAVER");
        chapterOne.TotalWeight.ShouldBe(100.0);
    }

    /// <summary>
    /// Three authored numbers that describe the code rather than tune it: each is rejected when it
    /// stops agreeing, so none of them is a dial nothing turns.
    /// </summary>
    [Fact]
    public void A_key_that_describes_the_code_is_rejected_when_it_stops_agreeing_with_it()
    {
        var wrongRounding = Should.Throw<ContentTypeMismatchException>(
            () => EnemyCatalogue.Read(EnemyFixtures.Snapshot(roundingDecimals: 2)));
        wrongRounding.Reference.ShouldBe(EnemyCatalogue.RoundingDecimalsPointer);

        var wrongCount = Should.Throw<ContentTypeMismatchException>(
            () => EnemyCatalogue.Read(EnemyFixtures.Snapshot(modifiersPerElite: 2)));
        wrongCount.Reference.ShouldBe(EnemyCatalogue.ModifiersPerElitePointer);
    }
}
