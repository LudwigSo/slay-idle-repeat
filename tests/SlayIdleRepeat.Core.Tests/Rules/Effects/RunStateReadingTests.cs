using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// <see cref="RunStateReading"/> — the one <see cref="IRunStateView"/>. Internal seam: 18 §4's
/// run-state condition functions read it, and no public entry point constructs one directly.
/// </summary>
public sealed class RunStateReadingTests
{
    private static RunStateReading Reading(
        IReadOnlyDictionary<string, int>? perks = null,
        IReadOnlyDictionary<string, int>? faces = null) =>
        new()
        {
            StageIndex = 1,
            Chapter = 1,
            PerksByCategory = perks ?? new Dictionary<string, int>(StringComparer.Ordinal),
            DieFacesByKind = faces ?? new Dictionary<string, int>(StringComparer.Ordinal),
        };

    /// <summary><c>PERK_COUNT</c> answers per category, and a null category is every perk held.</summary>
    [Fact]
    public void PerkCount_answers_per_category_and_in_total()
    {
        var view = Reading(perks: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["OFFENSE"] = 4,
            ["DEFENSE"] = 2,
            ["ECONOMY"] = 1,
        });

        view.PerkCount("OFFENSE").ShouldBe(4);
        view.PerkCount("DEFENSE").ShouldBe(2);
        view.PerkCount(null).ShouldBe(7, "a null category is every perk held");
    }

    /// <summary>A category the run holds no perk from is <c>0</c>, not a failure.</summary>
    [Fact]
    public void An_unheld_category_reads_zero()
    {
        var view = Reading(perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 });

        view.PerkCount("DICE_AND_BOARD").ShouldBe(0);
        view.PerkCount("").ShouldBe(0);
    }

    [Fact]
    public void Category_and_face_kind_lookups_are_ordinal()
    {
        var view = Reading(
            perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 },
            faces: new Dictionary<string, int>(StringComparer.Ordinal) { ["Star"] = 2 });

        view.PerkCount("offense").ShouldBe(0, "a differently-cased category is a different key");
        view.DieFaceCount("STAR").ShouldBe(0);
        view.DieFaceCount("Star").ShouldBe(2);
    }

    /// <summary>
    /// <c>DISTINCT_PERK_CATEGORIES</c> is derived from the perk table — it counts categories holding
    /// at least one perk, a zero-count category is not one of them, and re-deriving after a
    /// <c>with</c> keeps it agreeing with the table it summarises.
    /// </summary>
    [Fact]
    public void DistinctPerkCategories_counts_only_categories_that_hold_a_perk()
    {
        var view = Reading(perks: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["OFFENSE"] = 4,
            ["DEFENSE"] = 2,
            ["UTILITY"] = 0,
        });

        view.DistinctPerkCategories.ShouldBe(2);
        view.PerkCount(null).ShouldBe(6);

        (view with { PerksByCategory = new Dictionary<string, int>(StringComparer.Ordinal) })
            .DistinctPerkCategories.ShouldBe(0);
    }

    [Fact]
    public void DieFaceCount_answers_per_face_kind()
    {
        var view = Reading(faces: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pip"] = 16,
            ["Star"] = 2,
        });

        view.DieFaceCount("Pip").ShouldBe(16);
        view.DieFaceCount("Star").ShouldBe(2);
        view.DieFaceCount("Void").ShouldBe(0);
    }

    /// <summary>
    /// <c>PERK_COUNT</c>'s null category means "every perk"; <c>DIE_FACE_COUNT</c>'s face kind is not
    /// optional, so the two must not answer alike.
    /// </summary>
    [Fact]
    public void DieFaceCount_of_a_null_face_kind_throws_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Reading().DieFaceCount(null!));
    }

    /// <summary>A fresh run's counters default to zero readings, and nowhere throws.</summary>
    [Fact]
    public void An_empty_run_reads_zero_rather_than_failing()
    {
        var view = EffectTestBattle.Run();

        view.PerkCount(null).ShouldBe(0);
        view.PerkCount("OFFENSE").ShouldBe(0);
        view.DistinctPerkCategories.ShouldBe(0);
        view.DieFaceCount("Star").ShouldBe(0);
        view.PetCount.ShouldBe(0);
        view.GoldHeld.ShouldBe(0);
        view.BattlesWonThisRun.ShouldBe(0);
    }
}
