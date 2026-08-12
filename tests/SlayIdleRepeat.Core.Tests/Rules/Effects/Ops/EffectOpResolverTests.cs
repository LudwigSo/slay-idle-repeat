using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 All 43 ops of `18` §2 reach a route, and each family's disposition is the one the document
/// gives it.
/// </summary>
/// <remarks>
/// Steering S3 — the resolver's <c>switch</c> has a <c>default</c> arm only because C# requires one
/// on an enum switch, so it cannot be what catches a forty-fourth op: that op would fall into it and
/// throw in whichever battle first authored one. These tests enumerate
/// <see cref="EffectOps.All"/> and are what catch it at build time.
/// </remarks>
public sealed class EffectOpResolverTests
{
    /// <summary>
    /// Every op is routed — no op reaches the <c>default</c> arm, and the count is floored at 43.
    /// </summary>
    [Fact]
    public void Every_op_of_18_2_is_routed()
    {
        EffectOps.All.Count.ShouldBe(43, "18 §11 — and S3's floor under the loop below");

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
            };

            var context = bench.Context(
                evaluation, damageDealt: 100.0, healAmount: 50.0, overhealAmount: 10.0);

            try
            {
                EffectOpResolver.Resolve(Exemplar(op), context);
            }
            catch (EffectContextException e) when (e.Message.Contains("is not one of 18 §2's 43", StringComparison.Ordinal))
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
            };

            var expected = EffectOps.FamilyOf(op) switch
            {
                // 🔒 18 §8 applies the six stat ops; a firing effect never does. STAT_COPY is in
                //    §2.4, resolves to a percent-bucket add, and IS resolved here.
                EffectOpFamily.STAT => OpDisposition.AGGREGATED,
                EffectOpFamily.RUN_AND_BOARD => OpDisposition.QUEUED_FOR_RUN,
                _ => OpDisposition.RESOLVED,
            };

            var outcome = EffectOpResolver.Resolve(
                Exemplar(op),
                bench.Context(evaluation, damageDealt: 100.0, healAmount: 50.0, overhealAmount: 10.0));

            if (outcome.Disposition != expected)
            {
                wrong.Add($"{op}: expected {expected}, got {outcome.Disposition}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 A stat op reaching the resolver mutates nothing at all — `18` §8's steps 4–9 own them, and
    /// a resolver that also applied them would double every bonus in the game.
    /// </summary>
    [Fact]
    public void A_stat_op_routed_here_reaches_no_seam()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        foreach (var op in EffectOps.All.Where(o => EffectOps.FamilyOf(o) == EffectOpFamily.STAT))
        {
            EffectOpResolver.Resolve(Exemplar(op), bench.Context(EffectTestBattle.Context(hero, hero)));
        }

        bench.Calls.ShouldBeEmpty();
        bench.Queued.ShouldBeEmpty();
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

    /// <summary>
    /// A minimal well-formed effect for each op — the shape its `18` §2 row and the schema branch
    /// require, and nothing more.
    /// </summary>
    private static EffectDefinition Exemplar(EffectOp op)
    {
        var effect = OpFixtures.Effect($"EX_{op}", op, 1.0, EffectTarget.CURRENT_TARGET);

        return op switch
        {
            EffectOp.STAT_ADD_FLAT or EffectOp.STAT_ADD_PCT or EffectOp.STAT_MULT or EffectOp.STAT_SET =>
                effect with { Stat = StatSelector.Of(StatId.ATK) },

            EffectOp.STAT_CONVERT =>
                effect with { Stat = StatSelector.Of(StatId.DEF), ToStat = StatId.ATK },

            EffectOp.STAT_CAP_OVERRIDE =>
                effect with { Stat = StatSelector.Of(StatId.CRIT), CapKind = StatCapKind.STAT_MAX },

            EffectOp.STAT_COPY =>
                effect with { Stat = StatSelector.Of(StatId.CRIT) },

            EffectOp.HEAL_LEECH => effect with { Value = 0.2 },

            EffectOp.APPLY_STATUS or EffectOp.EXTEND_STATUS or EffectOp.IMMUNE_STATUS =>
                effect with { StatusId = "BURN", Value = 2.0 },

            EffectOp.REMOVE_STATUS => effect with { StatusId = "BURN" },

            EffectOp.ATTACK_MULT_NEXT => effect with { Charges = 1 },
            EffectOp.FORCE_CRIT_NEXT => effect with { Value = null, Charges = 1 },

            EffectOp.SUMMON => effect with { Archetype = "SWARM", Value = 2.0 },

            EffectOp.MODIFY_DIE_FACE => effect with { NewFace = new DieFaceSpec("Star") },

            _ => effect,
        };
    }
}
