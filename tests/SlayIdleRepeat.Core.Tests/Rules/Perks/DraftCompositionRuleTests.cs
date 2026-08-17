using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// The three plain composition rules — no duplicate options, at least two categories, and the
/// per-option owned-upgrade bias — as the draft engine enforces them.
/// </summary>
/// <remarks>
/// <para>
/// Every case fixes its seeds. The engine draws counter-based, so a sweep over a fixed range of
/// seeds is as deterministic as one seed and says something a single draft cannot: a rule enforced
/// by narrowing a pool holds on every draw, not on the one the author happened to pick.
/// </para>
/// <para>
/// These three carry no counter and no guarantee. They are asserted here, against the engine, rather
/// than beside the five <c>DRAFT</c> rules, because that is the difference between them.
/// </para>
/// </remarks>
public sealed class DraftCompositionRuleTests
{
    /// <summary>The seed range every sweep runs over. Fixed, so a failure is reproducible by number.</summary>
    private const ulong FirstSeed = 1;

    /// <summary>How many seeds a sweep covers.</summary>
    private const int SeedCount = 300;

    private static LuckTuning Tuning => LuckTuning.Read(LuckDocuments.LuckOnly());

    private static PerkCatalogue Catalogue => PerkCatalogue.Read(PerkDocuments.Shipped);

    private static PerkCatalogue Clashing => PerkCatalogue.Read(PerkDocuments.OneCategoryDominates);

    private static DraftedPerks Owning(params (string PerkId, int Tier)[] tiers) =>
        new(new ReadOnlyDictionary<string, int>(
            tiers.ToDictionary(t => t.PerkId, t => t.Tier, StringComparer.Ordinal)));

    private static IReadOnlyList<DraftOption> Draft(
        PerkCatalogue catalogue, DraftedPerks owned, ulong seed, int stage) =>
        PerkDraftEngine.GenerateOptions(
            catalogue,
            owned,
            new DeterministicRng(seed, RngStreams.Draft),
            Tuning,
            Array.Empty<DraftForce>(),
            new HashSet<string>(StringComparer.Ordinal),
            stage,
            isElite: false,
            isBoss: false);

    private static IEnumerable<ulong> Seeds =>
        Enumerable.Range(0, SeedCount).Select(offset => FirstSeed + (ulong)offset);

    // ------------------------------------------------------------------ 1 · no duplicate options

    /// <summary>No draft of three ever offers the same perk twice.</summary>
    /// <remarks>
    /// Enforced by construction: slot <i>k</i> draws from a pool with the perks slots
    /// <c>0..k-1</c> already took removed. A repaired-after-the-fact version would cost extra draw
    /// indices and break the resumed-stream property, so this asserts the outcome the construction
    /// is for rather than the construction.
    /// </remarks>
    [Fact]
    public void No_draft_offers_the_same_perk_twice()
    {
        var offenders = Seeds
            .Select(seed => (Seed: seed, Options: Draft(Catalogue, Owning(), seed, stage: 1)))
            .Where(draft => draft.Options.Select(o => o.PerkId).Distinct(StringComparer.Ordinal).Count()
                            != draft.Options.Count)
            .Select(draft => $"seed {draft.Seed}: " + string.Join(", ", draft.Options.Select(o => o.PerkId)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "06 §4: no duplicate options within a single draft of 3. A duplicated option is a draft " +
            "of two choices dressed as three.");
    }

    // ------------------------------------------------------------------ 2 · category diversity

    /// <summary>Every draft spans at least two categories.</summary>
    /// <remarks>
    /// Run over the catalogue whose Common band is three Offense rows and one Defense row, because
    /// the five-row fixture gives every perk its own category — there, a draft of three distinct
    /// perks is diverse by construction and a diversity rule that did nothing would pass.
    /// </remarks>
    [Fact]
    public void Every_draft_spans_at_least_two_categories()
    {
        var offenders = Seeds
            .Select(seed => (Seed: seed, Options: Draft(Clashing, Owning(), seed, stage: 1)))
            .Where(draft => draft.Options.Select(o => o.Category).Distinct().Count()
                            < DraftCompositionRules.MinimumDistinctCategories)
            .Select(draft => $"seed {draft.Seed}: " + string.Join(", ", draft.Options.Select(o => o.Category)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "06 §4: at least 2 distinct categories among the 3 options. Three Offense perks is a " +
            "draft with no decision in it.");
    }

    /// <summary>
    /// The clashing fixture really can go mono-category, so the case above is not passing because the
    /// catalogue makes it impossible to fail.
    /// </summary>
    /// <remarks>
    /// 🔒 The floor under the diversity sweep (steering S3). A catalogue in which three same-category
    /// options cannot be drawn at all would satisfy the rule while quantifying over nothing, and
    /// nothing else here would say so.
    /// </remarks>
    [Fact]
    public void The_clashing_catalogue_holds_more_same_category_rows_than_a_draft_has_slots()
    {
        Clashing.OfRarity(PerkRarity.Common)
            .Count(perk => perk.Category == PerkCategory.Offense)
            .ShouldBeGreaterThanOrEqualTo(
                PerkDraftEngine.OptionCount,
                "the diversity sweep is only a rule if the pool it draws from could produce three " +
                "options of one category.");
    }

    // ------------------------------------------------------------------ 3 · owned-upgrade bias

    /// <summary>
    /// The 30% owned-upgrade bias actually fires: with one owned, non-maxed perk in a band the
    /// rarity table almost never draws, that perk still turns up in a large share of slots.
    /// </summary>
    /// <remarks>
    /// The discrimination is the band. A Stage-1 draft draws Legendary with weight 1 in 100, so the
    /// owned Legendary appearing in roughly three slots in ten can only be the bias drawing from the
    /// owned pool rather than the fresh one — an engine that never applied the bias offers it about
    /// nine times in nine hundred.
    /// </remarks>
    [Fact]
    public void The_owned_upgrade_bias_draws_from_the_owned_pool()
    {
        var owned = Owning((PerkDocuments.Legendary1, 1));

        var slots = Seeds
            .SelectMany(seed => Draft(Catalogue, owned, seed, stage: 1))
            .ToArray();

        var fromOwnedPool = slots.Count(option => option.PerkId == PerkDocuments.Legendary1);

        fromOwnedPool.ShouldBeGreaterThan(
            slots.Length / 10,
            "24 §4.7 F3 / 06 §4: each option has a 30% chance of being drawn from the owned-but-not-" +
            "maxed pool. A Stage-1 table draws Legendary with weight 1 in 100, so anything near a " +
            "tenth of the slots can only be the bias — and an engine that never applies it lands an " +
            "order of magnitude below this floor.");
    }

    /// <summary>And every such option is an upgrade of the owned perk, not a fresh grant.</summary>
    [Fact]
    public void An_option_the_bias_drew_upgrades_the_owned_perk()
    {
        var owned = Owning((PerkDocuments.Legendary1, 1));

        var offered = Seeds
            .SelectMany(seed => Draft(Catalogue, owned, seed, stage: 1))
            .Where(option => option.PerkId == PerkDocuments.Legendary1)
            .ToArray();

        offered.ShouldNotBeEmpty();
        offered.ShouldAllBe(option => option.IsUpgrade);
        offered.ShouldAllBe(option => option.NewTier == 2);
    }

    /// <summary>
    /// A run that owns nothing sees no bias at all — the control that tells the rule apart from
    /// "over-offer the top band".
    /// </summary>
    [Fact]
    public void A_run_owning_nothing_draws_every_option_from_the_fresh_pool()
    {
        var slots = Seeds
            .SelectMany(seed => Draft(Catalogue, Owning(), seed, stage: 1))
            .ToArray();

        slots.ShouldAllBe(option => !option.IsUpgrade);

        slots.Count(option => option.Rarity == PerkRarity.Legendary).ShouldBeLessThan(
            slots.Length / 10,
            "with nothing owned there is no owned pool to bias towards, so the Legendary share must " +
            "fall back to the rarity table's own 1-in-100 — this is what makes the bias case above a " +
            "measurement of the bias rather than of the table.");
    }

    /// <summary>The bias roll itself, at the boundary the authored probability sets.</summary>
    /// <remarks>
    /// Stated over the primitive because the engine's roll is one draw among three per slot: a
    /// boundary asserted through the engine would be asserting the draw stream's arithmetic as much
    /// as the rule's.
    /// </remarks>
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.2999, true)]
    [InlineData(0.3, false)]
    [InlineData(0.9999, false)]
    public void The_bias_roll_hits_below_the_authored_probability_and_not_at_it(double roll, bool expected)
    {
        DraftCompositionRules.OwnedUpgradeBiasHits(roll, LuckDocuments.ShippedDraftOwnedUpgradeBias)
            .ShouldBe(expected);
    }

    // ------------------------------------------------------------------ the predicates

    /// <summary>The duplicate predicate answers both ways.</summary>
    [Fact]
    public void The_duplicate_predicate_tells_a_repeated_id_from_a_distinct_one()
    {
        DraftCompositionRules.NoDuplicateOptions(new[] { "A", "B", "C" }).ShouldBeTrue();
        DraftCompositionRules.NoDuplicateOptions(new[] { "A", "B", "A" }).ShouldBeFalse();
    }

    /// <summary>The diversity predicate answers both ways.</summary>
    [Fact]
    public void The_diversity_predicate_tells_a_mono_category_draft_from_a_mixed_one()
    {
        DraftCompositionRules.CategoryDiversity(
            new[] { PerkCategory.Offense, PerkCategory.Offense, PerkCategory.Defense }).ShouldBeTrue();
        DraftCompositionRules.CategoryDiversity(
            new[] { PerkCategory.Offense, PerkCategory.Offense, PerkCategory.Offense }).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ the narrowing that serves it

    /// <summary>
    /// 🔒 The per-slot narrowing decision, over the whole (categories so far, slots left) space a
    /// three-option draft can reach — and one row past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine used to restate this rule as "narrow the last slot when every earlier slot shares
    /// one category", which is correct for a threshold of exactly 2 and quietly wrong for any other
    /// value — so <see cref="DraftCompositionRules.MinimumDistinctCategories"/> read like a dial and
    /// was not one: raising it would have changed what the tests demanded and nothing about what the
    /// engine did. The decision reads the threshold now, and this is where the reading is pinned.
    /// </para>
    /// <para>
    /// Both directions matter. Narrowing too late is a mono-category draft — a draft with no
    /// decision in it. Narrowing too early is a stricter rule than the one stated: a draft is
    /// allowed to repeat a category as long as it still spans enough of them, and forbidding the
    /// repeat outright would quietly ban two Offense options beside a Defense one.
    /// </para>
    /// </remarks>
    [Theory]
    // slot 0 of 3 — everything is still reachable, so nothing is owed.
    [InlineData(0, 3, false)]
    // slot 1 of 3, one category taken — the last slot can still widen the draft on its own.
    [InlineData(1, 2, false)]
    // slot 2 of 3, still one category — this is the last chance, so it is owed.
    [InlineData(1, 1, true)]
    // slot 2 of 3, already two categories — the threshold is met and the slot is free.
    [InlineData(2, 1, false)]
    // already past the threshold, and a slot beyond the last: never owed.
    [InlineData(3, 1, false)]
    [InlineData(2, 0, false)]
    // a draft with fewer slots than the threshold: owed from the very first slot, which is the
    // arm the old "only the last slot" narrowing could not express at all.
    [InlineData(0, 2, true)]
    public void A_slot_owes_a_new_category_only_when_the_slots_left_could_not_reach_the_threshold(
        int distinctSoFar, int slotsRemaining, bool expected)
    {
        DraftCompositionRules.MustContributeNewCategory(distinctSoFar, slotsRemaining)
            .ShouldBe(expected);
    }

    /// <summary>Neither half of the decision is a count that can go negative.</summary>
    [Fact]
    public void A_negative_category_count_or_slot_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DraftCompositionRules.MustContributeNewCategory(-1, 1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DraftCompositionRules.MustContributeNewCategory(1, -1));
    }

    /// <summary>
    /// 🔒 The two constants are compatible: a draft cannot be asked for more distinct categories
    /// than it has slots to put them in.
    /// </summary>
    /// <remarks>
    /// The coupling made explicit rather than left implicit. The narrowing reads the threshold now,
    /// so raising it genuinely changes the engine — but raising it past the option count states a
    /// rule no draft can satisfy, and the engine would fall back on every slot and produce whatever
    /// it could while every diversity assertion in this file went red without saying why.
    /// </remarks>
    [Fact]
    public void The_diversity_threshold_fits_inside_a_draft()
    {
        DraftCompositionRules.MinimumDistinctCategories.ShouldBeGreaterThan(
            1, "a threshold of 1 is satisfied by any draft at all, which is no rule.");

        DraftCompositionRules.MinimumDistinctCategories.ShouldBeLessThanOrEqualTo(
            PerkDraftEngine.OptionCount,
            "a draft of " + PerkDraftEngine.OptionCount + " options cannot span more than that many " +
            "categories, so a threshold above it is unsatisfiable by construction rather than by " +
            "bad luck — every draft would fall through the narrowing and the rule would be stated " +
            "but never met.");
    }

    // ------------------------------------------------------------------ determinism

    /// <summary>The composed draft is still byte-identical for a given seed.</summary>
    /// <remarks>
    /// The whole point of a fixed draw budget per slot: the options a client is looking at are
    /// re-derived, never stored, so a draft that composed differently on a second pass would leave a
    /// resumed run staring at three different cards.
    /// </remarks>
    [Fact]
    public void A_composed_draft_is_identical_on_a_second_pass()
    {
        var owned = Owning((PerkDocuments.Rare1, 1));

        Draft(Catalogue, owned, seed: 4242, stage: 2)
            .ShouldBe(Draft(Catalogue, owned, seed: 4242, stage: 2));
    }
}
