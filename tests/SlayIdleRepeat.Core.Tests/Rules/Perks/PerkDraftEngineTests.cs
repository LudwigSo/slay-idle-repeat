using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Rules.Perks;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Content.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// <c>PerkDraftEngine.GenerateOptions</c>, over the small hermetic catalogue in
/// <see cref="PerkDocuments"/>.
/// </summary>
public sealed class PerkDraftEngineTests
{
    private static PerkCatalogue Catalogue => PerkCatalogue.Read(PerkDocuments.Shipped);

    private static DraftedPerks NoneOwned() =>
        Owning(new Dictionary<string, int>());

    private static DraftedPerks Owning(IReadOnlyDictionary<string, int> tiers) =>
        new(new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(tiers, StringComparer.Ordinal)));

    private static DeterministicRng Draft(ulong seed, ulong position = 0) =>
        new(seed, RngStreams.Draft, position);

    /// <summary>The shipped pity registry, which the engine now draws under.</summary>
    internal static LuckTuning Tuning => LuckTuning.Read(LuckDocuments.LuckOnly());

    /// <summary>No guarantee has fired — the shape every M3-06 case was written against.</summary>
    internal static IReadOnlyList<DraftForce> Unforced => Array.Empty<DraftForce>();

    /// <summary>A Codex that has seen nothing, so every perk carries the never-drafted bias.</summary>
    internal static IReadOnlySet<string> NothingEverDrafted =>
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>One draft, with the guarantee inputs a case is not about held at their neutral value.</summary>
    private static IReadOnlyList<DraftOption> Generate(
        PerkCatalogue catalogue,
        DraftedPerks owned,
        DeterministicRng rng,
        int stage,
        bool isElite,
        bool isBoss,
        IReadOnlyList<DraftForce>? forces = null,
        IReadOnlySet<string>? everDrafted = null) =>
        PerkDraftEngine.GenerateOptions(
            new DraftRequest(
                catalogue,
                owned,
                Tuning,
                DraftRarityWeights.For(stage, isElite, isBoss),
                forces ?? Unforced,
                everDrafted ?? NothingEverDrafted),
            rng);

    // ------------------------------------------------------------------ shape

    [Fact]
    public void Generates_exactly_three_options()
    {
        var options = Generate(
            Catalogue, NoneOwned(), Draft(1), stage: 1, isElite: false, isBoss: false);

        options.Count.ShouldBe(PerkDraftEngine.OptionCount);
        options.Count.ShouldBe(3);
    }

    [Fact]
    public void Is_deterministic_for_the_same_seed_position_and_owned_set()
    {
        var first = Generate(
            Catalogue, NoneOwned(), Draft(42), stage: 2, isElite: false, isBoss: false);
        var second = Generate(
            Catalogue, NoneOwned(), Draft(42), stage: 2, isElite: false, isBoss: false);

        first.ShouldBe(second);
    }

    [Fact]
    public void Every_offered_id_is_authored_in_the_catalogue()
    {
        var options = Generate(
            Catalogue, NoneOwned(), Draft(7), stage: 3, isElite: true, isBoss: false);

        foreach (var option in options)
        {
            Catalogue.Contains(option.PerkId).ShouldBeTrue(option.PerkId);
        }
    }

    // ------------------------------------------------------------------ fresh grant vs upgrade

    [Fact]
    public void An_unowned_perk_is_offered_as_a_fresh_grant_at_Tier_I()
    {
        // Boss draws only Epic/Legendary — pin the one-perk-per-band catalogue and assert every
        // option is the single Epic or single Legendary row, always unowned, always fresh.
        var options = Generate(
            Catalogue, NoneOwned(), Draft(99), stage: 1, isElite: false, isBoss: true);

        foreach (var option in options)
        {
            option.IsUpgrade.ShouldBeFalse();
            option.NewTier.ShouldBe(1);
        }
    }

    [Fact]
    public void An_owned_Tier_I_perk_offered_again_is_an_upgrade_to_Tier_II()
    {
        var owned = Owning(new Dictionary<string, int> { [PerkDocuments.Epic1] = 1 });

        // Boss table is Epic/Legendary only, and this fixture has exactly one Epic row — so every
        // Epic draw in this seed run must resolve to PerkDocuments.Epic1, already owned at Tier I.
        var options = Generate(
            Catalogue, owned, Draft(1234), stage: 1, isElite: false, isBoss: true);

        var epicOptions = options.Where(o => o.PerkId == PerkDocuments.Epic1).ToArray();
        epicOptions.ShouldNotBeEmpty("a Boss draft with only one Epic row must draw it at least once across three slots for this seed to be a useful probe");

        foreach (var option in epicOptions)
        {
            option.IsUpgrade.ShouldBeTrue();
            option.NewTier.ShouldBe(2);
        }
    }

    // ------------------------------------------------------------------ Tier III removal — mutated on purpose (S1)

    [Fact]
    public void A_perk_owned_at_its_max_tier_is_never_offered_in_its_own_band()
    {
        // Every Legendary row in this fixture (there is exactly one) is owned at Tier III. A Boss
        // draft, which only offers Epic/Legendary, must therefore fall back to Epic for every slot
        // that would otherwise have drawn the maxed Legendary.
        var owned = Owning(new Dictionary<string, int> { [PerkDocuments.Legendary1] = 3 });

        var options = Generate(
            Catalogue, owned, Draft(5), stage: 1, isElite: false, isBoss: true);

        options.ShouldAllBe(o => o.PerkId != PerkDocuments.Legendary1,
            "06 §1.1: once a perk is at Tier III it is removed from that run's draft pool");

        // 🔴 This used to read `ShouldAllBe(o => o.PerkId == Epic1)`, and it was true only because
        // this seed happened to draw the Epic band three times: the fallback's first stop is the
        // Common band, not the one surviving Epic row. M4-01b's no-duplicate rule makes three copies
        // of one perk unreachable by construction, so the claim is restated as what the fallback now
        // actually does — the one drawable row in the Boss bands is offered once, and the slots that
        // cannot have it leave those bands entirely rather than repeating it or reaching the maxed
        // Legendary. Neither half is weaker: the first still forbids the maxed perk, the second still
        // forbids every option outside the one legal Boss-band row.
        options.Count(o => o.PerkId == PerkDocuments.Epic1).ShouldBe(1,
            "the Boss table draws Epic/Legendary only and the sole Legendary is maxed, so the one Epic row is the only option those bands can pay — offered, and offered once");
        options.ShouldAllBe(o => o.PerkId == PerkDocuments.Epic1 || o.Rarity < PerkRarity.Epic,
            "with that row taken, the other two slots have nowhere left inside the Boss bands, so they fall out of them altogether");
    }

    [Fact]
    public void Every_perk_in_the_catalogue_maxed_is_a_defect_not_a_silent_default()
    {
        var owned = Owning(PerkDocuments.AllIds.ToDictionary(id => id, _ => 3));

        Should.Throw<InvalidOperationException>(() =>
            Generate(Catalogue, owned, Draft(1), stage: 1, isElite: false, isBoss: false));
    }

    // ------------------------------------------------------------------ the draw budget

    /// <summary>Three draw indices per option slot: the bias roll, the rarity band, then the perk.</summary>
    private const int DrawsPerSlot = 3;

    /// <summary>A stream position that is not zero, so the budget is read as a delta and not a total.</summary>
    private const ulong Resumed = 17;

    /// <summary>How many seeds each branch of the budget case is swept over.</summary>
    /// <remarks>
    /// A sweep rather than one seed: two of the branches — the bias roll hitting, and the roll
    /// missing — are chosen by a draw, not by an argument, so a single seed exercises whichever one
    /// it happened to roll and reports success for the other.
    /// <see cref="The_budget_sweep_reaches_both_sides_of_the_bias_roll"/> is the floor that says the
    /// sweep actually reached both.
    /// </remarks>
    private const int BudgetSeedCount = 40;

    /// <summary>The seeds every branch of the budget case is drawn under.</summary>
    private static IEnumerable<ulong> BudgetSeeds =>
        Enumerable.Range(1, BudgetSeedCount).Select(i => (ulong)i);

    /// <summary>The stream positions every branch is drawn from.</summary>
    private static IEnumerable<ulong> BudgetStarts => new[] { 0UL, Resumed };

    /// <summary>
    /// 🔒 <b>A draft consumes exactly nine draw indices — three per slot — on every branch.</b> Bias
    /// hit or missed, forced or unforced, pool floored or fallen back on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant the whole draft path is built around, and the one nothing asserted. The three
    /// options a client shows are never persisted: they are re-derived from the run's committed
    /// draft-stream position, on the client and on the server independently. A branch that took a
    /// fourth draw — an owned pool consulted only when it is non-empty, a re-roll after an
    /// unsatisfiable force — leaves the two at different positions from the next draft onwards, and
    /// the two then disagree about every perk the run is ever offered. That is a desync no test in
    /// this suite would otherwise notice, because every case here draws from its own fresh stream.
    /// </para>
    /// <para>
    /// Every branch in one case, collecting the offenders, so a failure names <em>which</em> branch
    /// went off-budget rather than which of nine near-identical facts failed first. Each branch runs
    /// from a resumed position too: a budget asserted only from zero cannot tell "nine draws" from
    /// "seek to nine".
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_draft_consumes_exactly_three_draw_indices_per_slot()
    {
        var maxedLegendary = Owning(new Dictionary<string, int> { [PerkDocuments.Legendary1] = 3 });
        var upgradable = Owning(new Dictionary<string, int> { [PerkDocuments.Legendary1] = 1 });

        var offenders = new List<string>();

        // 1 · nothing owned, no force: the bias roll is taken and has no owned pool to reach.
        Budget(offenders, "unforced, nothing owned", NoneOwned(), Unforced, stage: 1, isBoss: false);

        // 2 · an owned, non-maxed perk: the bias roll can now hit and take the other pool branch.
        Budget(offenders, "unforced, bias reachable", upgradable, Unforced, stage: 1, isBoss: false);

        // 3 · a satisfiable force on slot 0 — the floored-pool branch.
        Budget(
            offenders, "Legendary forced, satisfiable", NoneOwned(),
            new[] { Force(0, DraftGuarantee.LegendaryPity, PerkRarity.Legendary, null, false) },
            stage: 1, isBoss: false);

        // 4 · an UNSATISFIABLE force — the fallback branch, which is the one most likely to be
        // written as "draw again" rather than as "widen the pool".
        Budget(
            offenders, "Sustain forced, unsatisfiable", NoneOwned(),
            new[] { Force(0, DraftGuarantee.SustainAntiBrick, null, PerkCategory.DiceAndBoard, false) },
            stage: 1, isBoss: false);

        // 5 · a force per slot, all three at once.
        Budget(
            offenders, "every slot forced", upgradable,
            new[]
            {
                Force(0, DraftGuarantee.LegendaryPity, PerkRarity.Legendary, null, false),
                Force(1, DraftGuarantee.QualityFloor, PerkRarity.Rare, null, false),
                Force(2, DraftGuarantee.UpgradeFamine, null, null, true),
            },
            stage: 1, isBoss: false);

        // 6 · a band the pool cannot pay — the Boss table draws Epic/Legendary and the one
        // Legendary row is maxed out of the pool, so the band fallback runs on most slots.
        Budget(offenders, "band fallback", maxedLegendary, Unforced, stage: 1, isBoss: true);

        offenders.ShouldBeEmpty(
            "a draft re-derives the options a client is looking at from the run's committed stream " +
            "position, so a branch that spends a different number of draw indices desyncs the client " +
            "from the server for the rest of the run.");
    }

    /// <summary>One branch, swept over every seed and both stream positions.</summary>
    private static void Budget(
        ICollection<string> offenders,
        string branch,
        DraftedPerks owned,
        IReadOnlyList<DraftForce> forces,
        int stage,
        bool isBoss)
    {
        const int Expected = 9;

        foreach (var start in BudgetStarts)
        {
            foreach (var seed in BudgetSeeds)
            {
                var rng = Draft(seed, position: start);

                Generate(Catalogue, owned, rng, stage, isElite: false, isBoss: isBoss, forces: forces);

                var spent = rng.Position - start;

                if (spent != (ulong)(PerkDraftEngine.OptionCount * DrawsPerSlot) || spent != Expected)
                {
                    offenders.Add(
                        $"'{branch}' at seed {seed} from position {start} spent {spent} draw " +
                        $"indices; a draft of {PerkDraftEngine.OptionCount} slots spends " +
                        $"{DrawsPerSlot} each, which is {Expected}.");
                }
            }
        }
    }

    /// <summary>
    /// 🔒 The floor under the budget sweep (steering S3): it reaches drafts where the owned-upgrade
    /// bias roll <b>hit</b> and drafts where it <b>missed</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Those two are the branches the budget case cannot select by argument — they are chosen by the
    /// first of each slot's three draws — and a sweep that only ever rolled one of them would report
    /// the budget holding on a branch it never entered. Found by mutation, not by reading: adding a
    /// fourth draw guarded on a bias hit left the budget case <b>green</b> when it ran on a single
    /// seed.
    /// </para>
    /// <para>
    /// The rolls are recomputed rather than observed. The draw stream is positional, so slot
    /// <i>k</i>'s bias roll is the draw at <c>start + 3k</c> — which is also the layout claim the
    /// budget case rests on, stated here from the outside instead of taken on trust.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_budget_sweep_reaches_both_sides_of_the_bias_roll()
    {
        var bias = LuckService.OwnedUpgradeBias(Tuning);

        var rolls = BudgetStarts
            .SelectMany(start => BudgetSeeds.SelectMany(seed =>
                Enumerable.Range(0, PerkDraftEngine.OptionCount)
                    .Select(slot => Draft(seed, start + ((ulong)slot * DrawsPerSlot)).NextDouble())))
            .Select(roll => DraftCompositionRules.OwnedUpgradeBiasHits(roll, bias))
            .ToArray();

        rolls.ShouldContain(true, "no draft in the sweep takes the owned-pool branch at all.");
        rolls.ShouldContain(false, "every draft in the sweep takes it, so the fresh-pool branch is unswept.");
    }

    private static DraftForce Force(
        int slot,
        DraftGuarantee guarantee,
        PerkRarity? rarityAtLeast,
        PerkCategory? category,
        bool requiresOwnedUpgrade) =>
        new(slot, guarantee, rarityAtLeast, category, requiresOwnedUpgrade);

    // ------------------------------------------------------------------ null guards

    [Fact]
    public void Null_arguments_are_refused()
    {
        Should.Throw<ArgumentNullException>(() =>
            Generate(null!, NoneOwned(), Draft(1), 1, false, false));
        Should.Throw<ArgumentNullException>(() =>
            Generate(Catalogue, null!, Draft(1), 1, false, false));
        Should.Throw<ArgumentNullException>(() =>
            Generate(Catalogue, NoneOwned(), null!, 1, false, false));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                new DraftRequest(
                    Catalogue, NoneOwned(), null!, Weights, Unforced, NothingEverDrafted),
                Draft(1)));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                new DraftRequest(
                    Catalogue, NoneOwned(), Tuning, null!, Unforced, NothingEverDrafted),
                Draft(1)));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                new DraftRequest(
                    Catalogue, NoneOwned(), Tuning, Weights, null!, NothingEverDrafted),
                Draft(1)));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                new DraftRequest(Catalogue, NoneOwned(), Tuning, Weights, Unforced, null!),
                Draft(1)));

        // 🔒 The whole request left at its default, which a record STRUCT reaches without running a
        // constructor at all — the shape a guard inside DraftRequest could not have covered, and the
        // reason GenerateOptions null-guards the members rather than trusting the type.
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(default, Draft(1)));
    }

    /// <summary>A neutral rarity table, for the cases that are not about which battle the draft follows.</summary>
    private static IReadOnlyList<(PerkRarity Rarity, double Weight)> Weights =>
        DraftRarityWeights.For(1, isElite: false, isBoss: false);
}
