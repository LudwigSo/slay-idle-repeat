using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 All 44 ops of `18` §2 reach a route, and each family's disposition is the one the document
/// gives it.
/// </summary>
/// <remarks>
/// <para>
/// Steering S3 — the resolver's <c>switch</c> has a <c>default</c> arm only because C# requires one
/// on an enum switch, so it cannot be what catches a forty-fifth op: that op would fall into it and
/// throw in whichever battle first authored one. These tests enumerate
/// <see cref="EffectOps.All"/> and are what catch it at build time.
/// </para>
/// <para>
/// 🔒 <b>The Phase 1a accommodation is gone.</b> Both loops used to tolerate a
/// <see cref="NotSupportedException"/> from <c>CombatFlowOps.RandomOutcome</c> and record the op as
/// <em>stubbed</em>/<em>unproven</em>, because its handler had not been written; the remark then said
/// the accommodation is deleted when the handler lands, and M2-12's implementation phase landed it.
/// Every one of the 44 is now resolved for real and its disposition asserted — which is the stronger
/// claim, and the reason the loops below hand in a `14` §8.1 combat draw stream: the forty-fourth op
/// takes one draw, and an evaluation carrying no stream is refused rather than answered.
/// </para>
/// </remarks>
public sealed class EffectOpResolverTests
{
    /// <summary>
    /// Every op is routed — no op reaches the <c>default</c> arm, and the count is floored at 44.
    /// </summary>
    [Fact]
    public void Every_op_of_18_2_is_routed()
    {
        EffectOps.All.Count.ShouldBe(44, "18 §11 — and S3's floor under the loop below");

        var unrouted = new List<string>();

        foreach (var op in EffectOps.All)
        {
            var hero = EffectTestBattle.Hero(maxHp: 1000);
            var enemy = EffectTestBattle.Enemy("EN_1", 1, maxHp: 1000);
            var bench = new OpTestBench()
                .WithStat(hero, StatId.ATK, 100.0)
                .WithStat(enemy, StatId.ATK, 100.0)
                .WithStat(enemy, StatId.CRIT, 0.5)
                .WithHighestBucket(enemy, StatId.CRIT);

            var evaluation = EffectTestBattle.Context(hero, hero, enemy) with
            {
                CurrentTarget = enemy,
                Attacker = enemy,

                // `14` §8.1's combat stream — RANDOM_OUTCOME takes exactly one draw and refuses an
                // evaluation that carries none, so without this the routing claim could not be made
                // for the forty-fourth op at all.
                Rng = EffectTestBattle.CombatRng(6),
            };

            var context = bench.Context(
                evaluation, damageDealt: 100.0, healAmount: 50.0, overhealAmount: 10.0);

            try
            {
                EffectOpResolver.Resolve(OpFixtures.Exemplar(op), context);
            }
            catch (EffectContextException e) when (e.Message.Contains("is not one of 18 §2's 44", StringComparison.Ordinal))
            {
                unrouted.Add($"{op} fell through to the default arm");
            }
        }

        unrouted.ShouldBeEmpty();
    }

    /// <summary>
    /// The three dispositions, each over the family the document assigns it — so that "resolved",
    /// "queued for the run controller" and "applied by `18` §8's aggregation" cannot be confused
    /// with "did nothing".
    /// </summary>
    [Fact]
    public void The_disposition_of_every_op_is_the_one_its_family_carries()
    {
        var wrong = new List<string>();

        foreach (var op in EffectOps.All)
        {
            var hero = EffectTestBattle.Hero(maxHp: 1000);
            var enemy = EffectTestBattle.Enemy("EN_1", 1, maxHp: 1000);
            var bench = new OpTestBench()
                .WithStat(hero, StatId.ATK, 100.0)
                .WithStat(enemy, StatId.ATK, 100.0)
                .WithStat(enemy, StatId.CRIT, 0.5)
                .WithHighestBucket(enemy, StatId.CRIT);

            var evaluation = EffectTestBattle.Context(hero, hero, enemy) with
            {
                CurrentTarget = enemy,
                Attacker = enemy,
                Rng = EffectTestBattle.CombatRng(6),
            };

            var expected = op switch
            {
                // 🔒 M2-R1 — 18 §8 applies STAT_CONVERT and STAT_CAP_OVERRIDE ONLY, at steps 6 and
                //    9; a firing effect never does, because their arithmetic needs a
                //    post-aggregation value. The other four 18 §2.1 ops now resolve THROUGH
                //    ITriggeredStatSink when a trigger fires them — see FiredStat's remarks.
                //    STAT_COPY is in §2.4, resolves to a percent-bucket add, and IS resolved here.
                EffectOp.STAT_CONVERT or EffectOp.STAT_CAP_OVERRIDE => OpDisposition.AGGREGATED,
                _ when EffectOps.FamilyOf(op) == EffectOpFamily.RUN_AND_BOARD => OpDisposition.QUEUED_FOR_RUN,
                _ => OpDisposition.RESOLVED,
            };

            var outcome = EffectOpResolver.Resolve(
                OpFixtures.Exemplar(op),
                bench.Context(evaluation, damageDealt: 100.0, healAmount: 50.0, overhealAmount: 10.0));

            if (outcome.Disposition != expected)
            {
                wrong.Add($"{op}: expected {expected}, got {outcome.Disposition}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 <c>STAT_CONVERT</c> and <c>STAT_CAP_OVERRIDE</c> reaching the resolver mutate nothing at
    /// all — `18` §8's steps 6 and 9 own them, and their arithmetic needs a post-aggregation value
    /// neither a trigger nor this resolver has in hand.
    /// </summary>
    [Fact]
    public void STAT_CONVERT_and_STAT_CAP_OVERRIDE_reach_no_seam()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();
        var aggregationOnlyOps = new[] { EffectOp.STAT_CONVERT, EffectOp.STAT_CAP_OVERRIDE };

        foreach (var op in aggregationOnlyOps)
        {
            EffectOpResolver.Resolve(
                OpFixtures.Exemplar(op), bench.Context(EffectTestBattle.Context(hero, hero)));
        }

        bench.Calls.ShouldBeEmpty();
        bench.Queued.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 M2-R1 — a FIRED `18` §2.1 basic stat op reaches exactly one seam,
    /// <c>ITriggeredStatSink</c>, and no other: it must not also mutate HP, a status, the flow state
    /// or the run queue, which is what would happen if the routing accidentally fell through to
    /// another op family.
    /// </summary>
    [Fact]
    public void A_fired_basic_stat_op_reaches_only_the_triggered_stat_sink()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();
        var firedStatOps = new[]
        {
            EffectOp.STAT_ADD_FLAT, EffectOp.STAT_ADD_PCT, EffectOp.STAT_MULT, EffectOp.STAT_SET,
        };

        // S3 — the floor: the two lists above plus this one are 18 §2.1's whole six.
        (firedStatOps.Length + 2).ShouldBe(6, "18 §2.1");

        foreach (var op in firedStatOps)
        {
            EffectOpResolver.Resolve(
                OpFixtures.Exemplar(op, target: EffectTarget.SELF),
                bench.Context(EffectTestBattle.Context(hero, hero)));
        }

        bench.TriggeredStatFirings.Count.ShouldBe(4, "one Apply call per fired op, one target each");
        bench.Queued.ShouldBeEmpty();

        // Nothing besides the four Apply(...) rows reached ANY other recorded seam.
        bench.Calls.Count.ShouldBe(4);
        bench.Calls.ShouldAllBe(c => c.StartsWith($"{nameof(ITriggeredStatSink.Apply)}:"));
    }

    /// <summary>Both arguments are required.</summary>
    [Fact]
    public void The_arguments_are_required()
    {
        var hero = EffectTestBattle.Hero();
        var context = new OpTestBench().Context(EffectTestBattle.Context(hero, hero));

        Should.Throw<ArgumentNullException>(() => EffectOpResolver.Resolve(null!, context));
        Should.Throw<ArgumentNullException>(
            () => EffectOpResolver.Resolve(OpFixtures.Effect("X", EffectOp.DAMAGE, 1.0), null!));
    }
}
