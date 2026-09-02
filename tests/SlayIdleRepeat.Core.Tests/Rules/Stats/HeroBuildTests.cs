using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.TestSupport;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// What the hero actually is: the base curve, plus everything the equipped loadout contributes,
/// through the stat aggregation order. Every case fails if a single link of the chain is dropped —
/// the derivation, the enhancement multiplier, the affix mapping, the set breakpoint, or the
/// aggregation bucket each contribution lands in.
/// </summary>
public sealed class HeroBuildTests
{
    private const int Level = 1;

    /// <summary>The shipped content, because the build reads six documents at once.</summary>
    private static ContentSnapshot Content => ShippedHarness.Content;

    // ────────────────────────────────────────────────────────────────── the base curve

    /// <summary>
    /// A hero wearing nothing is exactly the authored base curve — all fourteen stats, including the
    /// ones whose base is zero and the one whose base is not.
    /// </summary>
    /// <remarks>
    /// The floor under every other case: if the empty build already disagreed with the curve, a
    /// difference measured against it would be measuring two things at once. <c>HEAL_PCT</c> is
    /// checked by name because a base of 1.0 is the one value a "zero means unstated" bug would
    /// destroy silently, disabling every heal and lifesteal tick in the game.
    /// </remarks>
    [Fact]
    public void A_hero_wearing_nothing_is_the_authored_base_curve()
    {
        var build = HeroBuild.Of(Level, [], Content);

        build.Effects.ShouldBeEmpty("an empty loadout contributes nothing to collect");

        Stat(build, StatId.MAX_HP).ShouldBe(295.0, "250 + 45 x 1");
        Stat(build, StatId.ATK).ShouldBe(36.0, "30 + 6 x 1");
        Stat(build, StatId.DEF).ShouldBe(18.0, "15 + 3 x 1");
        Stat(build, StatId.ASPD).ShouldBe(1.0);
        Stat(build, StatId.CRIT).ShouldBe(0.05);
        Stat(build, StatId.CDMG).ShouldBe(0.5);
        Stat(build, StatId.HEAL_PCT).ShouldBe(1.0, "a multiplier on all healing received, not a zero");
        Stat(build, StatId.DODGE).ShouldBe(0.02, "the other stat whose base is not zero");
        Stat(build, StatId.LIFESTEAL).ShouldBe(0.0);
        Stat(build, StatId.BLOCK).ShouldBe(0.0);
        Stat(build, StatId.PEN).ShouldBe(0.0);
        Stat(build, StatId.DMG_PCT).ShouldBe(1.0, "a multiplier stat consumed bare, not an additive bucket");
        Stat(build, StatId.DR_PCT).ShouldBe(1.0, "the damage-taken multiplier's identity");
        Stat(build, StatId.THORNS).ShouldBe(0.0);
    }

    // ───────────────────────────────────────────────────── the item's own two stats

    /// <summary>
    /// An equipped weapon raises attack by the amount its own four inputs say. The literal is the
    /// point: "higher than nothing" is satisfied by a derivation off by any factor, so the figure is
    /// composed from the four authored numbers — power target, item fraction, band multiplier, slot
    /// coefficient — at the quality where the primary scale is exactly one.
    /// </summary>
    [Fact]
    public void An_equipped_weapon_raises_attack_by_the_amount_its_inputs_say()
    {
        Stat(HeroBuild.Of(Level, [Weapon("w")], Content), StatId.ATK).ShouldBe(
            112.0, "36 of hero, plus 1000 x 0.1 x 3.8 x 0.2 of blade");
    }

    /// <summary>
    /// A slot whose stat the table calls a percentage lands in the FLAT bucket, and the proof is that
    /// it moves a stat whose base is zero.
    /// </summary>
    /// <remarks>
    /// 🔒 The one case that separates the two readings of the slot table. Aggregation is
    /// <c>(base + flat) x (1 + percent)</c> and the hero's base lifesteal is 0.0, so routing an
    /// amulet's authored lifesteal into the percent bucket multiplies zero and hands the player an
    /// item whose second stat does nothing at all. A test asserting only "lifesteal is higher than
    /// nothing" would be satisfied by the broken reading too — it is not, because zero is what the
    /// broken reading produces.
    /// </remarks>
    [Fact]
    public void A_percent_typed_slot_stat_moves_a_stat_whose_base_is_zero()
    {
        var build = HeroBuild.Of(Level, [Amulet("a")], Content);

        Stat(build, StatId.LIFESTEAL).ShouldBe(
            0.06,
            "the amulet's secondary is lifesteal, read straight from the per-rarity table at the " +
            "quality where the secondary scale is one — and the hero's base lifesteal is 0.0, so a " +
            "percent add would multiply zero and leave the stat exactly where it started");
    }

    /// <summary>An enhanced item is worth more than the same item unenhanced, by the authored ladder.</summary>
    /// <remarks>
    /// Two probes rather than one, and the second is what discriminates: "+15 beats +0" would also
    /// pass if the multiplier were applied at some arbitrary strength, so the gain is compared
    /// against the authored total the forge publishes. A ladder that stopped being applied fails the
    /// first; one applied at the wrong slope fails the second.
    /// </remarks>
    [Fact]
    public void An_enhanced_item_carries_the_authored_multiplier_over_the_same_unenhanced_item()
    {
        var forge = ForgeTuning.Read(Content);
        var fresh = HeroBuild.Of(Level, [Weapon("w", enhanceLevel: forge.MinEnhanceLevel)], Content);
        var maxed = HeroBuild.Of(Level, [Weapon("w", enhanceLevel: forge.MaxEnhanceLevel)], Content);

        var freshGain = Stat(fresh, StatId.ATK) - 36.0;
        var maxedGain = Stat(maxed, StatId.ATK) - 36.0;

        maxedGain.ShouldBeGreaterThan(freshGain, "a maxed item is not the same item as a fresh one");

        (maxedGain / (freshGain / forge.StatMultiplier(forge.MinEnhanceLevel))).ShouldBe(
            forge.StatMultiplier(forge.MaxEnhanceLevel),
            tolerance: 1e-4,
            "the gain scales by exactly the authored ladder, not by some other slope. The tolerance " +
            "is the determinism grid rather than an epsilon: both sides cross a rounding step, so the " +
            "residual is bounded by the fourth decimal place and by nothing tighter");
    }

    // ──────────────────────────────────────────────────────────────────── the affixes

    /// <summary>
    /// A <c>+X%</c> affix on a fraction-typed stat is X percentage points, not a percentage of a
    /// base that is zero.
    /// </summary>
    /// <remarks>
    /// The affix pool authors the bucket per row precisely so this is a data decision. Block's base
    /// is 0.0, so the percent reading contributes nothing and the flat reading contributes the roll —
    /// two answers a whole apart, and only one of them is a block chance.
    /// </remarks>
    [Fact]
    public void A_fraction_typed_affix_contributes_its_roll_as_percentage_points()
    {
        var armor = Inventories.Item(
            "armor", GearFamily.LEATHERS, Rarity.S, affixes: [new GearAffixRoll("AFX_BLOCK", 0.09)]);

        Stat(HeroBuild.Of(Level, [armor], Content), StatId.BLOCK).ShouldBe(
            0.09, "the hero's base block is 0.0, so the roll IS the block chance");
    }

    /// <summary>A <c>+X%</c> affix on a magnitude stat multiplies it.</summary>
    /// <remarks>
    /// The negative control on the case above: a mapping that made every affix flat would add 0.2 to
    /// a Max HP of nearly three hundred, which is not a bonus anybody would notice. The armor also
    /// contributes its own flat Max HP, so the expectation is stated over the whole aggregation
    /// rather than over the base.
    /// </remarks>
    [Fact]
    public void A_magnitude_affix_multiplies_the_stat_it_names()
    {
        var plain = Inventories.Item("plain", GearFamily.LEATHERS, Rarity.S);
        var rolled = Inventories.Item(
            "rolled", GearFamily.LEATHERS, Rarity.S, affixes: [new GearAffixRoll("AFX_MAX_HP", 0.2)]);

        var without = Stat(HeroBuild.Of(Level, [plain], Content), StatId.MAX_HP);
        var with = Stat(HeroBuild.Of(Level, [rolled], Content), StatId.MAX_HP);

        with.ShouldBe(
            DeterminismRounding.Round(without * 1.2),
            tolerance: 1e-4,
            "a +20% Max HP affix is a fifth more of everything flat, not a fifth of a hit point");
    }

    /// <summary>
    /// An affix naming a non-combat stat is collected and REPORTED, never silently dropped.
    /// </summary>
    /// <remarks>
    /// 🔒 The stat block holds fourteen combat stats, so a gold-gain affix cannot land in it. What
    /// must not happen is the affix vanishing: the aggregation names what it did not apply, which is
    /// how a caller finds out that a rolled bonus is waiting on a consumer that does not exist yet.
    /// </remarks>
    [Fact]
    public void A_non_combat_affix_is_reported_rather_than_lost()
    {
        var ring = Inventories.Item(
            "ring", GearFamily.BAND, Rarity.S, affixes: [new GearAffixRoll("AFX_GOLD_GAIN", 0.3)]);

        var build = HeroBuild.Of(Level, [ring], Content);

        build.Effects.ShouldContain(
            e => e.Id.Contains("AFX_GOLD_GAIN", StringComparison.Ordinal),
            "the affix is collected — it is a real bonus the player rolled");

        build.Aggregated.SkippedNonCombatStatEffects.ShouldContain(
            e => e.Contains("AFX_GOLD_GAIN", StringComparison.Ordinal),
            "and it is named as unapplied rather than dropped on the floor");
    }

    /// <summary>
    /// The pet-aura affix — the other shipped non-combat row — is reported the same way, and it is
    /// the ONLY thing reported: the amulet's own combat contributions must not land beside it.
    /// </summary>
    /// <remarks>
    /// 🔒 D48 kept <c>AFX_PET_AURA_POWER</c> and <c>AFX_GOLD_GAIN</c> in the pool as the armed side
    /// of the cut — rolled, collected, reported, waiting on a consumer. The gold row's reporting is
    /// pinned above; this pins the pet row against the same shipped tuning, so a drops.json edit
    /// that unmapped either stat fails by name rather than by census. The single-item form is the
    /// control the case above lacks: a pipeline that skipped everything it collected would satisfy
    /// two ShouldContains, but not this.
    /// </remarks>
    [Fact]
    public void A_pet_aura_affix_is_the_only_skip_its_build_reports()
    {
        var amulet = Inventories.Item(
            "amulet", GearFamily.PENDANT, Rarity.S, affixes: [new GearAffixRoll("AFX_PET_AURA_POWER", 0.2)]);

        var build = HeroBuild.Of(Level, [amulet], Content);

        build.Effects.ShouldContain(
            e => e.Id.Contains("AFX_PET_AURA_POWER", StringComparison.Ordinal),
            "the affix is collected — it is a real bonus the player rolled");

        var skipped = build.Aggregated.SkippedNonCombatStatEffects.ShouldHaveSingleItem(
            "exactly the pet-aura affix is unapplied; the amulet's primary and secondary are " +
            "combat stats and belong in the block, not in the skip report");

        skipped.ShouldContain("AFX_PET_AURA_POWER", Case.Sensitive, "PET_AURA_PCT has no consumer yet");
    }

    /// <summary>
    /// The target-gated damage-vs-Elites affix reaches the fight's effect list with its gate — with
    /// zero per-affix code anywhere on the path.
    /// </summary>
    [Fact]
    public void A_target_gated_affix_is_collected_with_its_gate()
    {
        var weapon = Inventories.Item(
            "w", GearFamily.BLADE, Rarity.S, affixes: [new GearAffixRoll("AFX_DAMAGE_VS_ELITES", 0.25)]);

        var affix = HeroBuild.Of(Level, [weapon], Content)
            .Effects.SingleOrDefault(
                e => e.Id.Contains("AFX_DAMAGE_VS_ELITES", StringComparison.Ordinal))
            .ShouldNotBeNull("the rolled affix is a real bonus and reaches the fight's effect list");

        affix.Condition.ShouldNotBeNull("the pool's gate rides the synthesised effect unchanged")
            .Term.ShouldNotBeNull()
            .Fn.ShouldBe(ConditionFunction.TARGET_IS_ELITE);
        affix.Value.ShouldBe(0.25, "the roll is the magnitude");
    }

    /// <summary>The hero screen composes a build wearing the gated affix, showing the off-gate block.</summary>
    /// <remarks>
    /// The strict gate treats a context-gated standing effect as inactive rather than refusing it —
    /// a refusal here bricks the hero screen for anyone wearing the affix — and outside a fight
    /// there is no target for the gate to hold against, so the multiplier shows its base.
    /// </remarks>
    [Fact]
    public void A_build_wearing_the_gated_affix_composes_off_gate_to_the_identity_multiplier()
    {
        var weapon = Inventories.Item(
            "w", GearFamily.BLADE, Rarity.S, affixes: [new GearAffixRoll("AFX_DAMAGE_VS_ELITES", 0.25)]);

        Stat(HeroBuild.Of(Level, [weapon], Content), StatId.DMG_PCT).ShouldBe(
            1.0, "off its gate the affix contributes nothing, so the multiplier shows its base");
    }

    // ───────────────────────────────────────────────────────────────── the set bonuses

    /// <summary>Two SS pieces of one family axis grant that set's two-piece bonus.</summary>
    [Fact]
    public void Two_SS_pieces_of_one_axis_grant_its_two_piece_bonus()
    {
        var build = HeroBuild.Of(Level, BloodmoonPieces(2), Content);

        Stat(build, StatId.LIFESTEAL).ShouldBe(
            0.1,
            "Bloodmoon's two-piece bonus is +10% lifesteal, the hero's base is 0.0, and neither a " +
            "blade nor leathers carries any — so the authored magnitude IS the answer here, and a " +
            "bonus authored at half or double it fails");
    }

    /// <summary>One piece grants nothing, and a lower band does not count towards a set.</summary>
    /// <remarks>
    /// Two negative controls on the case above, and the second is the one that discriminates: a
    /// resolver that counted every piece regardless of band would satisfy a "two pieces grant it"
    /// test with two C-rarity items, and a set would stop being an endgame goal.
    /// </remarks>
    [Fact]
    public void A_set_bonus_needs_two_pieces_and_needs_them_at_the_top_band()
    {
        var single = HeroBuild.Of(Level, BloodmoonPieces(1), Content);
        var lowBand = HeroBuild.Of(
            Level,
            [
                Inventories.Item("w", GearFamily.BLADE, Rarity.S),
                Inventories.Item("a", GearFamily.LEATHERS, Rarity.S),
            ],
            Content);

        single.Effects.ShouldNotContain(
            e => e.Id.StartsWith("SET_BONUS", StringComparison.Ordinal),
            "one piece has reached no breakpoint");

        lowBand.Effects.ShouldNotContain(
            e => e.Id.StartsWith("SET_BONUS", StringComparison.Ordinal),
            "a set piece is an SS item; two S-rarity pieces of one axis are two items");
    }

    /// <summary>A set breakpoint nobody has authored grants nothing rather than throwing.</summary>
    /// <remarks>
    /// Seven of the twelve breakpoints ship unauthored, and a full six-piece set is reachable the
    /// moment a player owns one. A reader that refused a null would make the strongest loadout in
    /// the game unplayable.
    /// </remarks>
    [Fact]
    public void An_unauthored_breakpoint_grants_nothing_and_does_not_refuse_the_build()
    {
        var full = HeroBuild.Of(Level, BloodmoonPieces(6), Content);

        full.Effects.Count(e => e.Id.StartsWith("SET_BONUS", StringComparison.Ordinal)).ShouldBe(
            2, "the two- and four-piece bonuses are authored; the six-piece is not");
    }

    // ──────────────────────────────────────────────────────────── order and determinism

    /// <summary>The build is a function of what is worn, not of the order it was handed over in.</summary>
    /// <remarks>
    /// The collection order is a tiebreak of the resolution order, so a source enumerating whatever
    /// order a caller happened to supply would put a device-dependent order into a stat block — the
    /// determinism this whole layer exists to keep.
    /// </remarks>
    [Fact]
    public void The_build_does_not_depend_on_the_order_the_items_were_handed_over_in()
    {
        var items = BloodmoonPieces(4);
        var reversed = items.Reverse().ToArray();

        var forwards = HeroBuild.Of(Level, items, Content);
        var backwards = HeroBuild.Of(Level, reversed, Content);

        backwards.Effects.Select(e => e.Id).ShouldBe(forwards.Effects.Select(e => e.Id));
        backwards.Stats.ShouldBe(forwards.Stats);
    }

    /// <summary>The three gear sources are all live, and each one is the kind it claims.</summary>
    /// <remarks>
    /// Stated over the effect ids because the build hands back a flat list: the collection step's
    /// first three sources are exactly the three this task wired, and a build that quietly stopped
    /// collecting from one of them would still produce a plausible stat block.
    /// </remarks>
    [Fact]
    public void All_three_gear_sources_contribute_to_one_build()
    {
        var pieces = BloodmoonPieces(2).ToList();
        pieces[0] = Inventories.Item(
            pieces[0].InstanceId.Value,
            GearFamily.BLADE,
            Rarity.SS,
            affixes: [new GearAffixRoll("AFX_CRIT_CHANCE", 0.08)]);

        var build = HeroBuild.Of(Level, pieces, Content);

        build.Effects.ShouldContain(e => e.Id.StartsWith("(gear:", StringComparison.Ordinal));
        build.Effects.ShouldContain(e => e.Id.StartsWith("(affix:", StringComparison.Ordinal));
        build.Effects.ShouldContain(e => e.Id.StartsWith("SET_BONUS", StringComparison.Ordinal));
    }

    /// <summary>
    /// A build that passes a capped stat's ceiling lands exactly on it. The fixture has to overshoot
    /// — five crit rings — and equality there kills two mutations at once: no cap answers the raw
    /// total, and a cap applied per contribution answers it too, since no single contribution
    /// reaches the ceiling on its own.
    /// </summary>
    [Fact]
    public void A_build_that_passes_a_capped_stats_ceiling_lands_exactly_on_it()
    {
        var ceiling = CombatCaps.Read(Content).Caps.Apply(StatId.CRIT, double.MaxValue);

        var stacked = Enumerable.Range(0, 5)
            .Select(n => Inventories.Item(
                "ring_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GearFamily.BAND,
                Rarity.SS,
                affixes: [new GearAffixRoll("AFX_CRIT_CHANCE", 0.08)]))
            .ToArray();

        var crit = Stat(HeroBuild.Of(Level, stacked, Content), StatId.CRIT);

        crit.ShouldBe(
            ceiling,
            "five rings carry 0.05 base + 5 x (0.08 slot + 0.08 affix) = 0.85, which is over the " +
            "ceiling — so the capped answer IS the ceiling, and an uncapped pipeline answers 0.85");
    }

    // ──────────────────────────────────────────────────────────────────────── fixtures

    private static double Stat(HeroBuild build, StatId stat) =>
        build.Stats.Values.Single(pair => pair.Key == stat).Value;

    private static GearInstance Weapon(string id, int enhanceLevel = 0) =>
        Inventories.Item(id, GearFamily.BLADE, Rarity.S, enhanceLevel: enhanceLevel);

    private static GearInstance Amulet(string id) =>
        Inventories.Item(id, GearFamily.PENDANT, Rarity.S);

    /// <summary>The Bloodmoon set — the BALANCED family of each slot — at SS, taking the first N.</summary>
    private static IReadOnlyList<GearInstance> BloodmoonPieces(int count) =>
        new[]
        {
            GearFamily.BLADE,
            GearFamily.LEATHERS,
            GearFamily.HOOD,
            GearFamily.TREADS,
            GearFamily.BAND,
            GearFamily.PENDANT,
        }
        .Take(count)
        .Select((family, index) => Inventories.Item(
            "bloodmoon_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            family,
            Rarity.SS))
        .ToArray();
}
