using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// 🔒 The shared contract suite for <see cref="IRunStateView"/> — every implementation is run through
/// it, including the ones M1-05 and M3 have not written yet.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Steering S7, in the words of the retro that produced it: <em>"Add the <c>InMemory</c> fake AND
/// the shared contract suite in the same change as the port. M0's first port shipped without its
/// suite and its two implementations already disagreed on the exception type they threw."</em>
/// <see cref="IRunStateView"/> is not a port — it declares no I/O — but it has the property that
/// matters here: it will have several implementations written by people who never read each other's,
/// months apart.
/// </para>
/// <para>
/// <b>To implement <see cref="IRunStateView"/>:</b> derive a test class from this one, override
/// <see cref="Create"/> to build your implementation at the stated readings, and the rules below run
/// against it. Nothing else is required, and nothing below may be overridden — a rule an
/// implementation is allowed to opt out of is not a contract.
/// </para>
/// </remarks>
public abstract class RunStateViewContract
{
    /// <summary>
    /// The readings a contract test asks an implementation to represent — every field of
    /// <see cref="IRunStateView"/>, stated as data.
    /// </summary>
    /// <param name="PerksByCategory">Perks held, by `06` §2 category.</param>
    /// <param name="DieFacesByKind">Die faces, by `04` §1 kind.</param>
    /// <param name="PetCount">Pets equipped.</param>
    /// <param name="GoldHeld">Gold held.</param>
    /// <param name="BattlesWonThisRun">Battles won this run.</param>
    /// <param name="StageIndex">The stage, 1..3.</param>
    /// <param name="Chapter">The chapter.</param>
    /// <param name="Tier">The tier's ordinal.</param>
    public sealed record RunStateFacts(
        IReadOnlyDictionary<string, int> PerksByCategory,
        IReadOnlyDictionary<string, int> DieFacesByKind,
        int PetCount,
        long GoldHeld,
        int BattlesWonThisRun,
        int StageIndex,
        int Chapter,
        int Tier);

    /// <summary>Builds the implementation under test at the given readings.</summary>
    /// <remarks>
    /// ⚠️ <c>private protected</c>, not <c>protected</c>: <see cref="IRunStateView"/> is
    /// <c>internal</c> to <c>SlayIdleRepeat.Core</c> and reaches this assembly only through `30`
    /// §11.3's <c>InternalsVisibleTo</c> grant, so a <c>protected</c> member of a <c>public</c> class
    /// could not name it. Every implementation of this contract lives in this assembly, which is what
    /// makes that the right accessibility rather than merely the one that compiles.
    /// </remarks>
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
    /// <see cref="int.MaxValue"/> rather than wrapping — `18` §1.1's <c>PK_HOARD</c> is uncapped.
    /// </summary>
    [Fact]
    public void Gold_beyond_the_int_range_survives()
    {
        Create(Facts(gold: (long)int.MaxValue + 1_000)).GoldHeld.ShouldBe((long)int.MaxValue + 1_000);
    }

    /// <summary>`18` §4 — <c>PERK_COUNT</c>: <em>"int, optionally by category"</em>.</summary>
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

    /// <summary>
    /// A category the run holds no perk from is <c>0</c>, not a failure: a perk category with nothing
    /// in it is a legitimate reading, and `PK_ARSENAL` counts exactly that.
    /// </summary>
    [Fact]
    public void An_unheld_category_reads_zero()
    {
        var view = Create(Facts(perks: new Dictionary<string, int>(StringComparer.Ordinal) { ["OFFENSE"] = 4 }));

        view.PerkCount("DICE_AND_BOARD").ShouldBe(0);
        view.PerkCount("").ShouldBe(0);
    }

    /// <summary>🔒 Categories and face kinds are matched ordinally (`14` §8.2).</summary>
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
    /// 🔒 <c>DISTINCT_PERK_CATEGORIES</c> agrees with <c>PERK_COUNT</c> — it counts the categories
    /// that hold at least one perk, and a category recorded as zero is not one of them.
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

    /// <summary>`18` §4 — <c>DIE_FACE_COUNT</c>: <em>"int, by face kind"</em>.</summary>
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
    /// 🔒 A null face kind is a caller error, not a reading. <c>PERK_COUNT</c>'s category is
    /// <em>optional</em> per `18` §4 and null means "every perk"; <c>DIE_FACE_COUNT</c>'s is not, so
    /// the two must not answer alike.
    /// </summary>
    /// <remarks>
    /// The exception type is part of the contract — S7's cautionary tale is precisely two
    /// implementations of one port that disagreed on which one they threw.
    /// </remarks>
    [Fact]
    public void DieFaceCount_of_a_null_face_kind_throws_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Create(Facts()).DieFaceCount(null!));
    }

    /// <summary>
    /// 🔒 The view is a reading, not a live handle: asking twice gives the same answer, and nothing
    /// on it mutates anything.
    /// </summary>
    /// <remarks>
    /// This is what <c>ConditionPurityRuleTests</c> enforces on the evaluator's side; here it is
    /// enforced on the implementation's, because `18` §4's <em>"pure functions of current state"</em>
    /// is only true if the state does not move under the evaluator between two reads of one pass.
    /// </remarks>
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
    /// 🔒 The interface exposes no way to write. Asserted by reflection rather than by reading it,
    /// because the whole value of a read-only view is that no later member quietly adds a setter.
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

        // 🔒 Floored, or the assertion above passes forever over an emptied interface (steering S3).
        typeof(IRunStateView).GetMethods().Length.ShouldBe(
            9,
            "18 §4 has nine run-state functions: PERK_COUNT, DISTINCT_PERK_CATEGORIES, PET_COUNT, " +
            "DIE_FACE_COUNT, GOLD_HELD, BATTLES_WON_THIS_RUN, STAGE_INDEX, CHAPTER, TIER");
    }
}
