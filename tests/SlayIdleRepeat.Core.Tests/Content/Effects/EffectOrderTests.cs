using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// Effect-id order: the ascending lexicographic order of effect IDs, not draft order — this removes
/// the last source of order-dependence between client and server.
/// </summary>
public sealed class EffectOrderTests
{
    /// <summary>
    /// The pair the whole rule turns on: <c>"PK_A"</c> and <c>"PKA"</c> sort in opposite orders
    /// ordinally and under culture-aware collation, because the underscore is U+005F (above <c>'A'</c>)
    /// ordinally and a minor, low-weight difference culturally.
    /// </summary>
    [Fact]
    public void Effect_id_order_is_ordinal_and_a_culture_aware_comparer_would_order_this_pair_the_other_way()
    {
        const string Underscored = "PK_A";
        const string Bare = "PKA";

        // The premise: the two comparers disagree about this pair. If this ever stops holding, the
        // case below is no longer testing ordinality and has to be given a pair that does.
        Math.Sign(StringComparer.CurrentCulture.Compare(Underscored, Bare)).ShouldBe(
            -1,
            "culture-aware collation orders PK_A BEFORE PKA — the opposite answer, and the reason " +
            "18 §8 cannot be implemented with the default comparer");

        new[] { Bare, Underscored }.InEffectIdOrder().ShouldBe([Bare, Underscored]);
        new[] { Underscored, Bare }.InEffectIdOrder().ShouldBe([Bare, Underscored]);
    }

    /// <summary>The same divergence over <see cref="EffectDefinition"/>s, through the extension real call sites use.</summary>
    [Fact]
    public void Effects_sort_by_id_ordinally()
    {
        var effects = new[]
        {
            Effect("PK_A"),
            Effect("PKA"),
            Effect("PK0"),
        };

        effects.InEffectIdOrder().Select(e => e.Id).ShouldBe(["PK0", "PKA", "PK_A"]);
    }

    [Fact]
    public void The_effect_comparer_orders_by_id_ordinally()
    {
        var effects = new List<EffectDefinition> { Effect("PK_A"), Effect("PKA") };

        effects.Sort(EffectOrder.ById);

        effects.Select(e => e.Id).ShouldBe(["PKA", "PK_A"]);
    }

    /// <summary>
    /// Including <c>Compare(null, null)</c>: a reference-equality fast path ahead of the guard
    /// would let an all-null collection sort happily while a half-null one is rejected.
    /// </summary>
    [Fact]
    public void The_effect_comparer_refuses_a_null_effect_rather_than_sorting_it_to_an_end()
    {
        Should.Throw<ArgumentNullException>(() => EffectOrder.ById.Compare(null, Effect("PKA")));
        Should.Throw<ArgumentNullException>(() => EffectOrder.ById.Compare(Effect("PKA"), null));
        Should.Throw<ArgumentNullException>(() => EffectOrder.ById.Compare(null, null));
    }

    [Fact]
    public void The_effect_comparer_answers_zero_for_the_same_instance()
    {
        var effect = Effect("PK_A");

        EffectOrder.ById.Compare(effect, effect).ShouldBe(0);
    }

    [Fact]
    public void Ordering_a_null_sequence_throws_rather_than_yielding_nothing()
    {
        Should.Throw<ArgumentNullException>(
            () => ((IEnumerable<EffectDefinition>)null!).InEffectIdOrder().ToArray());

        Should.Throw<ArgumentNullException>(
            () => ((IEnumerable<string>)null!).InEffectIdOrder().ToArray());
    }

    /// <summary>The ordering is stable: the same set produces the same sequence regardless of the order it was collected in.</summary>
    [Fact]
    public void The_order_does_not_depend_on_the_order_the_effects_were_collected_in()
    {
        var ids = new[] { "PK_ZEAL", "CP_BLOOD_PRICE", "PK_ARSENAL", "SYS_ENRAGE", "PK_A", "PKA" };
        var expected = ids.InEffectIdOrder().ToArray();

        ids.Reverse().InEffectIdOrder().ShouldBe(expected);
        ids.OrderBy(id => id.Length).InEffectIdOrder().ShouldBe(expected);
    }

    private static EffectDefinition Effect(string id) =>
        new() { Id = id, Op = EffectOp.STAT_ADD_PCT, Stat = StatSelector.Of(StatId.ATK) };
}
