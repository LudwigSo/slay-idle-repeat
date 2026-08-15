using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>Every op reaches a route, and each family's disposition matches what it should be.</summary>
/// <remarks>
/// The resolver's <c>switch</c> has a <c>default</c> arm only because C# requires one on an enum
/// switch — it must not be what silently catches an unrouted op. These tests enumerate
/// <see cref="EffectOps.All"/> so a new op can't fall through unnoticed. The loops hand in a combat
/// draw stream because <c>RANDOM_OUTCOME</c> draws once and refuses an evaluation carrying none.
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

                // RANDOM_OUTCOME takes exactly one draw and refuses an evaluation that carries none.
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
    /// The three dispositions, each over its family — so "resolved", "queued for the run controller"
    /// and "applied by aggregation" cannot be confused with "did nothing".
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
                // STAT_CONVERT/STAT_CAP_OVERRIDE apply only via aggregation — their arithmetic needs
                // a post-aggregation value a firing effect doesn't have. Other stat ops route
                // through ITriggeredStatSink instead.
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

    /// <summary><c>STAT_CONVERT</c> and <c>STAT_CAP_OVERRIDE</c> mutate nothing at the resolver — aggregation owns them.</summary>
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
    /// A fired basic stat op reaches exactly one seam, <c>ITriggeredStatSink</c>, and no other — not
    /// HP, a status, the flow state or the run queue.
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

        // The floor: the two lists above plus this one make up the whole family.
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
