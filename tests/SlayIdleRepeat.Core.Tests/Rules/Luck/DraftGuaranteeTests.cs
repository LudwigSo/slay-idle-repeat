using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The five <c>DRAFT</c> rules, through the luck façade: which one fires, at exactly which draft, and
/// what each one forces.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every case here names the rule.</b> Five rules can each force an option into the next draft,
/// so "an option was forced" is an assertion four of the five could be broken under. Each case
/// asserts the <see cref="DraftGuarantee"/> the resolution reports, and the band or category that
/// force carries — never the bare presence of a force.
/// </para>
/// <para>
/// 🔒 <b>The three thresholds are read differently and each is pinned separately.</b> The Legendary
/// pity's authored number names the forced draft itself, so it fires at a counter of N−1. The quality
/// floor and the upgrade famine both authorable count the drafts that pass <em>before</em> the next
/// one is floored, so they fire at a counter of N. Getting any of the three off by one shifts a
/// guarantee by a whole draft, and nothing else in the suite would notice.
/// </para>
/// </remarks>
public sealed class DraftGuaranteeTests
{
    private static LuckTuning Tuning => LuckTuning.Read(LuckDocuments.LuckOnly());

    private static DraftRule Rule => Tuning.Draft;

    /// <summary>A run at stage 1 holding a Sustain perk and something upgradable — nothing due.</summary>
    private static DraftDemand Quiet =>
        new(Stage: 1, IsBoss: false, OwnsSustainPerk: true, OwnsNonMaxedPerk: true);

    private static IReadOnlyList<DraftForce> Resolve(DraftCounters counters, DraftDemand demand) =>
        LuckService.ResolveDraft(Tuning, counters, demand, optionCount: 3);

    /// <summary>The guarantees a resolution reports, in the order it assigned them.</summary>
    private static DraftGuarantee[] Fired(DraftCounters counters, DraftDemand demand) =>
        Resolve(counters, demand).Select(force => force.Guarantee).ToArray();

    // ------------------------------------------------------------------ nothing due

    /// <summary>
    /// A run with every counter cold and nothing owed forces nothing at all. The control under every
    /// case below: a resolver that forced something unconditionally would satisfy all of them.
    /// </summary>
    [Fact]
    public void A_run_that_owes_nothing_forces_nothing()
    {
        Resolve(DraftCounters.Unstarted, Quiet).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ 1 · Legendary pity

    /// <summary>
    /// The Legendary pity fires on draft <b>15</b> — a counter of 14 — and forces a Legendary, not
    /// merely "something".
    /// </summary>
    /// <remarks>
    /// The authored <c>legendaryPityDraftNumber</c> names the forced draft's own ordinal, so it is
    /// the rung's <c>N</c> and takes no correction. That is the opposite reading from the quality
    /// floor's and the famine's, which is why all three are pinned rather than one standing for all.
    /// </remarks>
    [Fact]
    public void The_Legendary_pity_fires_on_the_fifteenth_draft_and_forces_a_Legendary()
    {
        var forces = Resolve(
            DraftCounters.Unstarted with { DraftsSinceLegendaryOffered = LuckDocuments.ShippedDraftLegendaryPityN - 1 },
            Quiet);

        var legendary = forces.ShouldHaveSingleItem();

        legendary.Guarantee.ShouldBe(
            DraftGuarantee.LegendaryPity,
            "the counter that moved is the Legendary one; a resolution that reported some other rule " +
            "would reset the wrong counter and leave this guarantee owed forever.");
        legendary.RarityAtLeast.ShouldBe(
            PerkRarity.Legendary,
            "24 §4.7's Legendary pity forces a LEGENDARY. Forcing Rare-or-better would satisfy a " +
            "test that only asked whether something was forced.");
        legendary.SlotIndex.ShouldBe(0, "the highest-priority force takes slot 0.");
    }

    /// <summary>And it does <b>not</b> fire on draft 14 — the boundary, from below.</summary>
    [Fact]
    public void The_Legendary_pity_does_not_fire_on_the_fourteenth_draft()
    {
        Fired(
            DraftCounters.Unstarted with { DraftsSinceLegendaryOffered = LuckDocuments.ShippedDraftLegendaryPityN - 2 },
            Quiet)
            .ShouldNotContain(DraftGuarantee.LegendaryPity);
    }

    // ------------------------------------------------------------------ 2 · Sustain anti-brick

    /// <summary>
    /// The anti-brick fires past Stage 2 for a run holding no Sustain perk, and forces the
    /// <b>Sustain category</b> rather than a rarity.
    /// </summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public void The_anti_brick_fires_past_Stage_2_and_forces_the_Sustain_category(int stage, bool isBoss)
    {
        var forces = Resolve(
            DraftCounters.Unstarted,
            new DraftDemand(stage, isBoss, OwnsSustainPerk: false, OwnsNonMaxedPerk: true));

        var antiBrick = forces.ShouldHaveSingleItem();

        antiBrick.Guarantee.ShouldBe(DraftGuarantee.SustainAntiBrick);
        antiBrick.Category.ShouldBe(
            PerkCategory.Sustain,
            "the anti-brick's whole content is WHICH category is forced — a force that named no " +
            "category would let the draft pay it with any option at all.");
        antiBrick.RarityAtLeast.ShouldBeNull(
            "the anti-brick states no band. Flooring a rarity here would silently make it a second " +
            "quality floor.");
    }

    /// <summary>It does not fire at stage 1 or 2 — the run still has time to draw one naturally.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_anti_brick_does_not_fire_before_the_end_of_Stage_2(int stage)
    {
        Fired(
            DraftCounters.Unstarted,
            new DraftDemand(stage, IsBoss: false, OwnsSustainPerk: false, OwnsNonMaxedPerk: true))
            .ShouldNotContain(DraftGuarantee.SustainAntiBrick);
    }

    /// <summary>And it does not fire when a Sustain perk is already owned, however late the run is.</summary>
    /// <remarks>
    /// The negative control that tells the rule apart from "force a Sustain option every late
    /// draft": a resolver keyed on the stage alone passes both cases above and fails this one.
    /// </remarks>
    [Fact]
    public void The_anti_brick_does_not_fire_when_a_Sustain_perk_is_already_owned()
    {
        Fired(
            DraftCounters.Unstarted,
            new DraftDemand(Stage: 3, IsBoss: false, OwnsSustainPerk: true, OwnsNonMaxedPerk: true))
            .ShouldNotContain(DraftGuarantee.SustainAntiBrick);
    }

    /// <summary>The state predicate on its own, so the anti-brick's two inputs are separately pinned.</summary>
    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(3, false, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(3, false, true, false)]
    public void The_anti_brick_is_due_only_past_Stage_2_with_no_Sustain_perk(
        int stage, bool isBoss, bool ownsSustain, bool expected)
    {
        DraftGuarantees.AntiBrickDue(
            Rule.SustainAntiBrick,
            new DraftDemand(stage, isBoss, ownsSustain, OwnsNonMaxedPerk: true))
            .ShouldBe(expected);
    }

    // ------------------------------------------------------------------ 3 · F1 quality floor

    /// <summary>
    /// The quality floor fires on the <b>4th</b> draft — three consecutive all-Common drafts, then
    /// the next one — and forces the authored band.
    /// </summary>
    /// <remarks>
    /// The authored number counts the drafts that pass BEFORE the floor applies, so the forced draft
    /// is the (N+1)-th and the counter it fires at is N. The Legendary pity above reads its own
    /// number the other way; both are pinned so neither reading can be copied onto the other.
    /// </remarks>
    [Fact]
    public void The_quality_floor_fires_on_the_fourth_draft_and_forces_Rare_or_better()
    {
        var forces = Resolve(
            DraftCounters.Unstarted with { DraftsWithoutAboveCommon = LuckDocuments.ShippedDraftQualityFloorN },
            Quiet);

        var floor = forces.ShouldHaveSingleItem();

        floor.Guarantee.ShouldBe(DraftGuarantee.QualityFloor);
        floor.RarityAtLeast.ShouldBe(
            PerkRarity.Rare,
            "24 §4.7 F1 forces RARE-or-better. Forcing Legendary would pass a bare 'something was " +
            "forced' assertion and hand out a Legendary every fourth grey draft.");
    }

    /// <summary>And not on the 3rd — the boundary, from below.</summary>
    [Fact]
    public void The_quality_floor_does_not_fire_on_the_third_draft()
    {
        Fired(
            DraftCounters.Unstarted with { DraftsWithoutAboveCommon = LuckDocuments.ShippedDraftQualityFloorN - 1 },
            Quiet)
            .ShouldNotContain(DraftGuarantee.QualityFloor);
    }

    // ------------------------------------------------------------------ 4 · F2 Codex bias

    /// <summary>A never-drafted perk carries the authored multiplier in the fresh-pool draw.</summary>
    [Fact]
    public void A_never_drafted_perk_carries_the_authored_weight_multiplier()
    {
        LuckService.DraftFreshPoolWeight(Tuning, everDrafted: false).ShouldBe(
            LuckDocuments.ShippedDraftCodexBiasMultiplier,
            "24 §4.7 F2: ×1.35 on the perks the player has never drafted.");
    }

    /// <summary>An already-drafted one carries no multiplier at all — the control on the bias.</summary>
    [Fact]
    public void An_already_drafted_perk_carries_no_multiplier()
    {
        LuckService.DraftFreshPoolWeight(Tuning, everDrafted: true).ShouldBe(
            1.0,
            "a bias applied to every perk is no bias: it would rescale the whole pool and change " +
            "nothing, while reading as a working rule.");
    }

    /// <summary>At most one of the three options may be bias-selected.</summary>
    /// <remarks>
    /// The cap is the half of F2 that stops the bias becoming the draft: without it a fresh account,
    /// whose whole catalogue is never-drafted, would see three biased options every time and the
    /// rarity table would stop meaning anything.
    /// </remarks>
    [Fact]
    public void At_most_one_option_per_draft_is_bias_selected()
    {
        LuckService.MaxCodexBiasedOptions(Tuning).ShouldBe(
            LuckDocuments.ShippedDraftMaxBiasSelectedOptions);
        LuckService.MaxCodexBiasedOptions(Tuning).ShouldBe(1);
    }

    /// <summary>The Codex bias moves no counter and forces no option — it is a weight, not a guarantee.</summary>
    [Fact]
    public void The_Codex_bias_forces_nothing_and_moves_no_counter()
    {
        Resolve(DraftCounters.Unstarted, Quiet).ShouldBeEmpty();

        LuckService.DraftCountersAfter(
            DraftCounters.Unstarted,
            new DraftOffering(OfferedLegendary: true, OfferedAboveCommon: true, OfferedOwnedUpgrade: true))
            .ShouldBe(DraftCounters.Unstarted);
    }

    // ------------------------------------------------------------------ 5 · F3 upgrade famine

    /// <summary>
    /// The upgrade famine fires on the <b>6th</b> draft — five upgrade-free drafts, then the next —
    /// and forces an option drawn from the owned-but-not-maxed pool.
    /// </summary>
    [Fact]
    public void The_upgrade_famine_fires_on_the_sixth_draft_and_forces_an_owned_upgrade()
    {
        var forces = Resolve(
            DraftCounters.Unstarted with { DraftsWithoutOwnedUpgrade = LuckDocuments.ShippedDraftUpgradeFamineN },
            Quiet);

        var famine = forces.ShouldHaveSingleItem();

        famine.Guarantee.ShouldBe(DraftGuarantee.UpgradeFamine);
        famine.RequiresOwnedUpgrade.ShouldBeTrue(
            "24 §4.7 F3 forces an UPGRADE of a perk the run already holds. A force that only floored " +
            "a rarity would be paid by a fresh grant and the famine would never end.");
        famine.RarityAtLeast.ShouldBeNull(
            "the famine states no band — the perk's own rarity is whatever the owned perk is.");
    }

    /// <summary>And not on the 5th — the boundary, from below.</summary>
    [Fact]
    public void The_upgrade_famine_does_not_fire_on_the_fifth_draft()
    {
        Fired(
            DraftCounters.Unstarted with { DraftsWithoutOwnedUpgrade = LuckDocuments.ShippedDraftUpgradeFamineN - 1 },
            Quiet)
            .ShouldNotContain(DraftGuarantee.UpgradeFamine);
    }

    /// <summary>
    /// It cannot fire for a run that owns nothing upgradable, however long the famine has run.
    /// </summary>
    /// <remarks>
    /// The guard is the whole reason this rule is not simply "force an upgrade every 6th draft": a
    /// run whose every owned perk is at Tier III has no upgrade to be starved of, and forcing one
    /// would mean either an impossible option or a fresh grant dressed up as an upgrade.
    /// </remarks>
    [Fact]
    public void The_upgrade_famine_does_not_fire_when_no_owned_perk_is_below_its_max_tier()
    {
        Fired(
            DraftCounters.Unstarted with { DraftsWithoutOwnedUpgrade = LuckDocuments.ShippedDraftUpgradeFamineN + 10 },
            Quiet with { OwnsNonMaxedPerk = false })
            .ShouldNotContain(DraftGuarantee.UpgradeFamine);
    }

    // ------------------------------------------------------------------ several at once

    /// <summary>
    /// A draft owing both a Legendary and a Sustain option pays <b>both</b>, in distinct slots and in
    /// the stated priority order.
    /// </summary>
    /// <remarks>
    /// Distinct slots rather than one: a single forced slot would let the Legendary swallow the
    /// anti-brick's obligation, and the run that was already bricked would stay bricked while its
    /// counter read as satisfied.
    /// </remarks>
    [Fact]
    public void A_draft_owing_two_guarantees_pays_both_in_distinct_slots()
    {
        var forces = Resolve(
            DraftCounters.Unstarted with { DraftsSinceLegendaryOffered = LuckDocuments.ShippedDraftLegendaryPityN - 1 },
            new DraftDemand(Stage: 3, IsBoss: false, OwnsSustainPerk: false, OwnsNonMaxedPerk: true));

        forces.Select(force => force.Guarantee).ShouldBe(
            new[] { DraftGuarantee.LegendaryPity, DraftGuarantee.SustainAntiBrick });
        forces.Select(force => force.SlotIndex).ShouldBe(new[] { 0, 1 });
    }

    /// <summary>Every guarantee firing at once still lands on three distinct slots, in priority order.</summary>
    [Fact]
    public void The_priority_order_is_Legendary_then_anti_brick_then_quality_floor_then_famine()
    {
        var forces = Resolve(
            new DraftCounters(
                LuckDocuments.ShippedDraftLegendaryPityN - 1,
                LuckDocuments.ShippedDraftQualityFloorN,
                LuckDocuments.ShippedDraftUpgradeFamineN),
            new DraftDemand(Stage: 3, IsBoss: false, OwnsSustainPerk: false, OwnsNonMaxedPerk: true));

        forces.Select(force => force.Guarantee).Take(3).ShouldBe(new[]
        {
            DraftGuarantee.LegendaryPity,
            DraftGuarantee.SustainAntiBrick,
            DraftGuarantee.QualityFloor,
        });

        forces.Take(3).Select(force => force.SlotIndex).ShouldBe(
            new[] { 0, 1, 2 },
            "four guarantees and three slots: the lowest-priority one goes unpaid this draft and its " +
            "counter stays standing, but no two forces may ever share a slot.");
    }

    // ------------------------------------------------------------------ counter movement

    /// <summary>A draft that offered nothing advances all three counters by one.</summary>
    [Fact]
    public void A_draft_that_offered_nothing_advances_every_counter()
    {
        LuckService.DraftCountersAfter(
            new DraftCounters(3, 1, 2),
            new DraftOffering(OfferedLegendary: false, OfferedAboveCommon: false, OfferedOwnedUpgrade: false))
            .ShouldBe(new DraftCounters(4, 2, 3));
    }

    /// <summary>Each counter is reset by its own offering and by nothing else.</summary>
    /// <remarks>
    /// Driven one counter at a time. A resolver that reset all three whenever anything was offered
    /// would pass a case that only ever moved one of them.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false, 0, 6, 6)]
    [InlineData(false, true, false, 6, 0, 6)]
    [InlineData(false, false, true, 6, 6, 0)]
    public void Each_counter_is_reset_by_its_own_offering_alone(
        bool legendary, bool aboveCommon, bool upgrade, int expectedLegendary, int expectedCommon, int expectedUpgrade)
    {
        LuckService.DraftCountersAfter(
            new DraftCounters(5, 5, 5),
            new DraftOffering(legendary, aboveCommon, upgrade))
            .ShouldBe(new DraftCounters(expectedLegendary, expectedCommon, expectedUpgrade));
    }

    /// <summary>
    /// A Legendary offered <em>naturally</em> resets the pity counter exactly as a forced one does.
    /// </summary>
    /// <remarks>
    /// Overshooting a guarantee is satisfying it: a counter that kept climbing through a lucky draft
    /// would fire a redundant pity a draft or two later, which is the player being punished for good
    /// luck.
    /// </remarks>
    [Fact]
    public void A_naturally_offered_Legendary_resets_the_pity_counter()
    {
        LuckService.DraftCountersAfter(
            DraftCounters.Unstarted with { DraftsSinceLegendaryOffered = 2 },
            new DraftOffering(OfferedLegendary: true, OfferedAboveCommon: true, OfferedOwnedUpgrade: false))
            .DraftsSinceLegendaryOffered.ShouldBe(0);
    }

    // ------------------------------------------------------------------ guards

    /// <summary>The façade refuses a null registry rather than resolving against nothing.</summary>
    [Fact]
    public void A_null_registry_is_refused()
    {
        Should.Throw<ArgumentNullException>(() =>
            LuckService.ResolveDraft(null!, DraftCounters.Unstarted, Quiet, optionCount: 3));
        Should.Throw<ArgumentNullException>(() =>
            LuckService.DraftFreshPoolWeight(null!, everDrafted: false));
        Should.Throw<ArgumentNullException>(() => LuckService.MaxCodexBiasedOptions(null!));
    }

    /// <summary>A draft of no options is a defect, not an empty force list.</summary>
    [Fact]
    public void A_draft_with_no_slots_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            LuckService.ResolveDraft(Tuning, DraftCounters.Unstarted, Quiet, optionCount: 0));
    }
}
