using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// 🔒 `18` §8's effect-id order — <em>"the ascending lexicographic order of effect IDs, not draft
/// order. This removes the last source of order-dependence between client and server."</em>
/// </summary>
public sealed class EffectOrderTests
{
    /// <summary>
    /// 🔒 The pair the whole rule turns on. <c>"PK_A"</c> and <c>"PKA"</c> sort in <b>opposite
    /// orders</b> under an ordinal comparison and under <c>en-US</c> collation, because the
    /// underscore is U+005F (above <c>'A'</c>, U+0041) ordinally and a minor, low-weight difference
    /// culturally.
    /// </summary>
    /// <remarks>
    /// Steering S1 — the guard is shown to bite before it is trusted. Swapping
    /// <see cref="EffectOrder.IdComparer"/> to <see cref="StringComparer.CurrentCulture"/> makes
    /// this case fail with the two ids transposed, on any machine whose collation is ICU's default;
    /// the assertion below on <see cref="StringComparer.CurrentCulture"/> itself is what proves the
    /// two comparers really do disagree on this pair, so the case cannot quietly stop being a test
    /// of anything if a future .NET makes the two agree.
    /// </remarks>
    [Fact]
    public void Effect_id_order_is_ordinal_and_a_culture_aware_comparer_would_order_this_pair_the_other_way()
    {
        const string Underscored = "PK_A";
        const string Bare = "PKA";

        // The premise: the two comparers disagree about this pair. If this ever stops holding, the
        // case below is no longer testing ordinality and has to be given a pair that does.
        var ordinal = Math.Sign(StringComparer.Ordinal.Compare(Underscored, Bare));
        var cultural = Math.Sign(StringComparer.CurrentCulture.Compare(Underscored, Bare));

        ordinal.ShouldBe(1, "'_' is U+005F, above 'A' at U+0041, so PK_A sorts AFTER PKA ordinally");
        cultural.ShouldBe(
            -1,
            "culture-aware collation treats '_' as a minor difference, so PK_A sorts BEFORE PKA — " +
            "the opposite answer, and the reason 18 §8 cannot be implemented with the default comparer");

        new[] { Bare, Underscored }.InEffectIdOrder().ShouldBe([Bare, Underscored]);
        new[] { Underscored, Bare }.InEffectIdOrder().ShouldBe([Bare, Underscored]);
    }

    /// <summary>
    /// The same divergence over <see cref="EffectDefinition"/>s, through the extension the nine
    /// call sites of `05` §3.1 and `18` §8 actually use.
    /// </summary>
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

    /// <summary>
    /// 🔒 <c>OrderBy(e =&gt; e.Id)</c> with no comparer is the mistake this helper exists to stop,
    /// and it produces a different order for this input. Pinning that difference is what makes the
    /// helper's existence defensible rather than decorative.
    /// </summary>
    [Fact]
    public void A_bare_OrderBy_over_the_same_ids_produces_a_different_order()
    {
        var ids = new[] { "PK_A", "PKA", "PK0" };

#pragma warning disable CA1309 // deliberately the culture-sensitive form — that is the point
        var bare = ids.OrderBy(id => id).ToArray();
#pragma warning restore CA1309

        bare.ShouldNotBe(
            ids.InEffectIdOrder().ToArray(),
            "if these ever agree, the default comparer has become ordinal and this case has stopped " +
            "demonstrating anything — pick a pair that still diverges rather than deleting it");
    }

    [Fact]
    public void The_id_comparer_is_the_ordinal_one()
    {
        EffectOrder.IdComparer.ShouldBeSameAs(StringComparer.Ordinal);
    }

    [Fact]
    public void The_effect_comparer_orders_by_id_ordinally()
    {
        var effects = new List<EffectDefinition> { Effect("PK_A"), Effect("PKA") };

        effects.Sort(EffectOrder.ById);

        effects.Select(e => e.Id).ShouldBe(["PKA", "PK_A"]);
    }

    /// <summary>
    /// 🔒 Including <c>Compare(null, null)</c>. A reference-equality fast path ahead of the guard
    /// would answer <c>0</c> for the one pair where "a null effect has no id to order by" is most
    /// true, so a half-null collection would be rejected while an all-null one sorted happily.
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

    /// <summary>
    /// The ordering is stable in the sense `18` §8 needs: the same set produces the same sequence
    /// regardless of the order it was collected in — <em>"not draft order"</em>.
    /// </summary>
    [Fact]
    public void The_order_does_not_depend_on_the_order_the_effects_were_collected_in()
    {
        var ids = new[] { "PK_ZEAL", "CP_BLOOD_PRICE", "PK_ARSENAL", "SYS_ENRAGE", "PK_A", "PKA" };
        var expected = ids.InEffectIdOrder().ToArray();

        ids.Reverse().InEffectIdOrder().ShouldBe(expected);
        ids.OrderBy(id => id.Length).InEffectIdOrder().ShouldBe(expected);
    }

    /// <summary>
    /// A sanity check on the culture the suite runs under: the divergence case above is stated over
    /// <see cref="StringComparer.CurrentCulture"/>, so a run under the invariant culture would still
    /// exercise it (invariant collation is culture-aware, not ordinal) — but it is worth saying so
    /// in a test rather than in a comment.
    /// </summary>
    [Fact]
    public void Invariant_culture_is_still_not_ordinal()
    {
        Math.Sign(StringComparer.InvariantCulture.Compare("PK_A", "PKA")).ShouldBe(
            -1,
            "InvariantCulture is a COLLATION, not a code-unit comparison — 'culture-invariant' is " +
            "not 'ordinal', so a run under it would order these the same wrong way");
    }

    private static EffectDefinition Effect(string id) =>
        new() { Id = id, Op = EffectOp.STAT_ADD_PCT, Stat = StatSelector.Of(StatId.ATK) };
}
