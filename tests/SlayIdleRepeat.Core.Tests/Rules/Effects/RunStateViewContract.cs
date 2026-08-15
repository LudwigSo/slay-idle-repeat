using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// The shared contract suite for <see cref="IRunStateView"/> — every implementation is run through it.
/// </summary>
/// <remarks>
/// To implement it: derive a test class from this one, override <see cref="Create"/> to build your
/// implementation at the stated readings, and the rules below run against it. Nothing may be
/// overridden — a rule an implementation can opt out of is not a contract.
/// </remarks>
public abstract class RunStateViewContract
{
/// <summary>
/// The readings a contract test asks an implementation to represent — every field of
/// <see cref="IRunStateView"/>, stated as data.
/// </summary>
    public sealed record RunStateFacts(
        IReadOnlyDictionary<string, int> PerksByCategory,
        IReadOnlyDictionary<string, int> DieFacesByKind,
        int PetCount,
        long GoldHeld,
        int BattlesWonThisRun,
        int StageIndex,
        int Chapter,
        int Tier);

    /// <summary>
    /// Builds the implementation under test at the given readings. <c>private protected</c>, not
    /// <c>protected</c>: <see cref="IRunStateView"/> is <c>internal</c> to
    /// <c>SlayIdleRepeat.Core</c> and reaches this assembly only via an <c>InternalsVisibleTo</c>
    /// grant, so a <c>protected</c> member of a <c>public</c> class could not name it.
    /// </summary>
    private protected abstract IRunStateView Create(RunStateFacts facts);

    private static RunStateFacts Facts(
        IReadOnlyDictionary<string, int>? perks = null,
        IReadOnlyDictionary<string, int>? faces = null,
        int petCount = 0,
        long gold = 0,
        int battlesWon = 0,
        int stage = 1,
        int chapter = 1,
        int tier = 0) =>
        new(
            perks ?? new Dictionary<string, int>(StringComparer.Ordinal),
            faces ?? new Dictionary<string, int>(StringComparer.Ordinal),
            petCount,
            gold,
            battlesWon,
            stage,
            chapter,
            tier);

    /// <summary>Every scalar reading comes back exactly as it went in.</summary>
    [Fact]
    public void The_scalar_readings_are_reported_as_given()
    {
        var view = Create(Facts(petCount: 3, gold: 1_450, battlesWon: 6, stage: 3, chapter: 5, tier: 2));

        view.PetCount.ShouldBe(3);
        view.GoldHeld.ShouldBe(1_450);
        view.BattlesWonThisRun.ShouldBe(6);
        view.StageIndex.ShouldBe(3);
        view.Chapter.ShouldBe(5);
        view.Tier.ShouldBe(2);
    }

    /// <summary>
    /// <c>GOLD_HELD</c> is a <see cref="long"/>, and an implementation must carry a balance past
    /// <see cref="int.MaxValue"/> rather than wrapping — gold is uncapped.
    /// </summary>
    [Fact]
    public void Gold_beyond_the_int_range_survives()
    {
        Create(Facts(gold: (long)int.MaxValue + 1_000)).GoldHeld.ShouldBe((long)int.MaxValue + 1_000);
    }

    /// <summary><c>PERK_COUNT</c> is an int, optionally by category.</summary>
    [Fact]
    public void PerkCount_answers_per_category_and_in_total()
    {
        var view = Create(Facts(perks: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["OFFENSE"] = 4,
            ["DEFENSE"] = 2,
            ["ECONOMY"] = 1,
        }));

        view.PerkCount("OFFENSE").ShouldBe(4);
        view.PerkCount("DEFENSE").ShouldBe(2);
        view.PerkCount(null).ShouldBe(7, "a null category is every perk held");
    }

    /// <summary>A category the run holds no perk from is <c>0</c>, not a failure.</summary>
    [Fact]
    public void An_unheld_category_reads_zero()
    {
        var view = Create(Facts(perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 }));

        view.PerkCount("DICE_AND_BOARD").ShouldBe(0);
        view.PerkCount("").ShouldBe(0);
    }

    /// <summary>Categories and face kinds are matched ordinally.</summary>
    [Fact]
    public void Category_and_face_kind_lookups_are_ordinal()
    {
        var view = Create(Facts(
            perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 },
            faces: new Dictionary<string, int>(StringComparer.Ordinal) { ["Star"] = 2 }));

        view.PerkCount("offense").ShouldBe(0, "a differently-cased category is a different key");
        view.DieFaceCount("STAR").ShouldBe(0);
        view.DieFaceCount("Star").ShouldBe(2);
    }

    /// <summary>
    /// <c>DISTINCT_PERK_CATEGORIES</c> agrees with <c>PERK_COUNT</c> — it counts the categories that
    /// hold at least one perk, and a category recorded as zero is not one of them.
    /// </summary>
    [Fact]
    public void DistinctPerkCategories_counts_only_categories_that_hold_a_perk()
    {
        var view = Create(Facts(perks: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["OFFENSE"] = 4,
            ["DEFENSE"] = 2,
            ["UTILITY"] = 0,
        }));

        view.DistinctPerkCategories.ShouldBe(2);
        view.PerkCount(null).ShouldBe(6);
    }

    /// <summary>An empty run reads zero everywhere, and nowhere throws.</summary>
    [Fact]
    public void An_empty_run_reads_zero_rather_than_failing()
    {
        var view = Create(Facts());

        view.PerkCount(null).ShouldBe(0);
        view.PerkCount("OFFENSE").ShouldBe(0);
        view.DistinctPerkCategories.ShouldBe(0);
        view.DieFaceCount("Star").ShouldBe(0);
        view.PetCount.ShouldBe(0);
        view.GoldHeld.ShouldBe(0);
        view.BattlesWonThisRun.ShouldBe(0);
    }

    /// <summary><c>DIE_FACE_COUNT</c> is an int, by face kind.</summary>
    [Fact]
    public void DieFaceCount_answers_per_face_kind()
    {
        var view = Create(Facts(faces: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pip"] = 16,
            ["Star"] = 2,
        }));

        view.DieFaceCount("Pip").ShouldBe(16);
        view.DieFaceCount("Star").ShouldBe(2);
        view.DieFaceCount("Void").ShouldBe(0);
    }

    /// <summary>
    /// A null face kind is a caller error, not a reading. <c>PERK_COUNT</c>'s category is optional and
    /// null means "every perk"; <c>DIE_FACE_COUNT</c>'s is not, so the two must not answer alike.
    /// </summary>
    [Fact]
    public void DieFaceCount_of_a_null_face_kind_throws_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Create(Facts()).DieFaceCount(null!));
    }

    /// <summary>
    /// The view is a reading, not a live handle: asking twice gives the same answer, and nothing on it
    /// mutates anything. Run-state functions are only pure functions of current state if that state
    /// does not move under the evaluator between two reads of one pass.
    /// </summary>
    [Fact]
    public void Reading_the_view_twice_gives_the_same_answer()
    {
        var view = Create(Facts(
            perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 },
            faces: new Dictionary<string, int>(StringComparer.Ordinal) { ["Star"] = 2 },
            petCount: 3,
            gold: 1_450));

        view.PerkCount(null).ShouldBe(4);
        view.PerkCount("OFFENSE").ShouldBe(4);
        view.DistinctPerkCategories.ShouldBe(1);
        view.DieFaceCount("Star").ShouldBe(2);
        view.PetCount.ShouldBe(3);
        view.GoldHeld.ShouldBe(1_450);

        view.PerkCount(null).ShouldBe(4);
        view.PerkCount("OFFENSE").ShouldBe(4);
        view.DistinctPerkCategories.ShouldBe(1);
        view.DieFaceCount("Star").ShouldBe(2);
        view.PetCount.ShouldBe(3);
        view.GoldHeld.ShouldBe(1_450);
    }

    /// <summary>
    /// The interface exposes no way to write. Asserted by reflection rather than by reading it, because
    /// the whole value of a read-only view is that no later member quietly adds a setter.
    /// </summary>
    [Fact]
    public void The_interface_declares_no_mutating_member()
    {
        var offenders = typeof(IRunStateView)
            .GetMethods()
            .Where(m => m.ReturnType == typeof(void) || m.Name.StartsWith("set_", StringComparison.Ordinal))
            .Select(m => $"IRunStateView.{m.Name} returns void or is a setter — this view is read-only")
            .ToArray();

        offenders.ShouldBeEmpty();

        // Floored, or the assertion above passes forever over an emptied interface.
        typeof(IRunStateView).GetMethods().Length.ShouldBe(
            9,
            "18 §4 has nine run-state functions: PERK_COUNT, DISTINCT_PERK_CATEGORIES, PET_COUNT, " +
            "DIE_FACE_COUNT, GOLD_HELD, BATTLES_WON_THIS_RUN, STAGE_INDEX, CHAPTER, TIER");
    }
}
