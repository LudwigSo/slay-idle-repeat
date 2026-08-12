using Shouldly;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// <see cref="RunStateReading"/> run through the shared <see cref="IRunStateView"/> contract, plus
/// the two rules that are its own rather than the interface's.
/// </summary>
/// <remarks>
/// 🔒 M1-05's <c>Run</c> projection and M3's run controller derive from
/// <see cref="RunStateViewContract"/> the same way. That is the whole point of the base class: the
/// contract is written once and every implementation is measured against the same rules (steering
/// S7).
/// </remarks>
public sealed class RunStateReadingTests : RunStateViewContract
{
    private protected override IRunStateView Create(RunStateFacts facts) =>
        new RunStateReading
        {
            PerksByCategory = facts.PerksByCategory,
            DieFacesByKind = facts.DieFacesByKind,
            PetCount = facts.PetCount,
            GoldHeld = facts.GoldHeld,
            BattlesWonThisRun = facts.BattlesWonThisRun,
            StageIndex = facts.StageIndex,
            Chapter = facts.Chapter,
            Tier = facts.Tier,
        };

    /// <summary>
    /// 🔒 The two <b>positional</b> readings have no default: <c>STAGE_INDEX</c> and <c>CHAPTER</c>
    /// are <c>required</c>, so a caller cannot omit them and get a plausible stage 1.
    /// </summary>
    /// <remarks>
    /// The counters do default to zero, and the difference is the rule (steering S6): no perks and
    /// no gold are real readings of a fresh run, whereas <em>no stage</em> is not a position — `18`
    /// §4 types <c>STAGE_INDEX</c> as <c>1..3</c>, so there is no zero to fall back to and inventing
    /// a 1 would be arguing from the hole to a filled value.
    /// </remarks>
    [Fact]
    public void The_positional_readings_are_required_and_the_counters_default_to_zero()
    {
        var required = typeof(RunStateReading)
            .GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false).Length > 0)
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        required.ShouldBe([nameof(RunStateReading.Chapter), nameof(RunStateReading.StageIndex)]);

        var counters = EffectTestBattle.Run();

        counters.PetCount.ShouldBe(0);
        counters.GoldHeld.ShouldBe(0);
        counters.BattlesWonThisRun.ShouldBe(0);
        counters.PerkCount(null).ShouldBe(0);
        counters.DistinctPerkCategories.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 <c>DISTINCT_PERK_CATEGORIES</c> is derived from the perk table, so no instance of this
    /// record can report a category count that disagrees with its own perks.
    /// </summary>
    /// <remarks>
    /// Asserted as a property of the type rather than of one instance: a record with both a table and
    /// an independently-settable count can be built reporting three perks across five categories, and
    /// every test written against it would still pass.
    /// </remarks>
    [Fact]
    public void The_category_count_cannot_disagree_with_the_perk_table()
    {
        var settable = typeof(RunStateReading)
            .GetProperty(nameof(RunStateReading.DistinctPerkCategories))!;

        settable.CanWrite.ShouldBeFalse(
            "a settable category count is a second source of truth for what the perk table already says");

        var reading = EffectTestBattle.Run() with
        {
            PerksByCategory = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["OFFENSE"] = 2,
                ["DEFENSE"] = 1,
            },
        };

        reading.DistinctPerkCategories.ShouldBe(2);
        (reading with { PerksByCategory = new Dictionary<string, int>(StringComparer.Ordinal) })
            .DistinctPerkCategories.ShouldBe(0);
    }
}
