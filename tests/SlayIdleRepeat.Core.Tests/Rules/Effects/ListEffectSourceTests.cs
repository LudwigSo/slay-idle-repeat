using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// <c>ListEffectSource</c> — the one in-<c>Core</c> <see cref="IEffectSource"/>. Internal seam:
/// no public entry point takes a source (a fight takes a flat effect list), and the order a source
/// reports is <see cref="EffectResolutionOrder"/>'s same-id tiebreak.
/// </summary>
public sealed class ListEffectSourceTests
{
    private static EffectDefinition Effect(string id, double value = 0.1) =>
        new() { Id = id, Op = EffectOp.STAT_ADD_PCT, Stat = StatSelector.Of(StatId.ATK), Value = value };

    private static SourcedEffect Held(EffectDefinition effect, string holding) =>
        new(effect, EffectInstanceId.Of(holding));

    private static IReadOnlyList<SourcedEffect> Holdings(params EffectDefinition[] effects) =>
        effects.Select((e, i) => Held(e, $"slot{i}:{e.Id}")).ToArray();

    /// <summary>Deliberately NOT in id order: a source that quietly sorted would pass an in-order fixture.</summary>
    [Fact]
    public void The_effects_are_reported_in_the_order_given()
    {
        var source = new ListEffectSource(
            EffectSourceKind.GEAR,
            Holdings(Effect("AFF_Z"), Effect("AFF_A"), Effect("AFF_M")));

        source.Effects.Select(e => e.Effect.Id).ShouldBe(new[] { "AFF_Z", "AFF_A", "AFF_M" });
    }

    /// <summary>
    /// The same authored id twice is kept twice, under two distinct holdings — de-duplicating would
    /// silently halve a legitimate build, and collapsing the holdings would give both copies one
    /// shared trigger counter.
    /// </summary>
    [Fact]
    public void Two_effects_with_one_id_are_both_reported_as_distinct_holdings()
    {
        var source = new ListEffectSource(
            EffectSourceKind.GEAR, Holdings(Effect("AFF_KEEN", 0.05), Effect("AFF_KEEN", 0.07)));

        source.Effects.Select(e => e.Effect.Value).ShouldBe(new double?[] { 0.05, 0.07 });
        source.Effects[0].Instance.ShouldNotBe(source.Effects[1].Instance);
    }

    /// <summary>
    /// The source does not alias a list the caller can still mutate — both passes of one resolution
    /// must see one answer.
    /// </summary>
    [Fact]
    public void Mutating_the_list_that_was_passed_in_does_not_change_the_source()
    {
        var mutable = new List<SourcedEffect> { Held(Effect("AFF_A"), "slot0:AFF_A") };
        var source = new ListEffectSource(EffectSourceKind.AFFIXES, mutable);

        mutable.Add(Held(Effect("AFF_B"), "slot1:AFF_B"));
        mutable[0] = Held(Effect("AFF_REPLACED"), "slot0:AFF_REPLACED");

        source.Effects.Select(e => e.Effect.Id).ShouldBe(new[] { "AFF_A" });
    }

    /// <summary>The resolution order is stated over ids and a hole has none.</summary>
    [Fact]
    public void A_null_effect_is_refused_at_construction()
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new ListEffectSource(
                EffectSourceKind.GEAR,
                new[] { Held(Effect("AFF_A"), "slot0:AFF_A"), new SourcedEffect(null!, EffectInstanceId.Of("x")) }));

        thrown.Message.ShouldContain("element 1", Case.Sensitive);
    }

    /// <summary>A kind outside the declared ten has no position in the source order, so the tiebreak would be undefined for it.</summary>
    [Fact]
    public void A_kind_outside_18_8_step_1s_ten_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => new ListEffectSource((EffectSourceKind)99, Array.Empty<SourcedEffect>()));

        thrown.Message.ShouldContain("ten sources", Case.Sensitive);
    }
}
