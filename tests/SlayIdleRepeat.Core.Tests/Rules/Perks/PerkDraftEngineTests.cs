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
            catalogue,
            owned,
            rng,
            Tuning,
            forces ?? Unforced,
            everDrafted ?? NothingEverDrafted,
            stage,
            isElite,
            isBoss);

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
                Catalogue, NoneOwned(), Draft(1), null!, Unforced, NothingEverDrafted, 1, false, false));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                Catalogue, NoneOwned(), Draft(1), Tuning, null!, NothingEverDrafted, 1, false, false));
        Should.Throw<ArgumentNullException>(() =>
            PerkDraftEngine.GenerateOptions(
                Catalogue, NoneOwned(), Draft(1), Tuning, Unforced, null!, 1, false, false));
    }
}
