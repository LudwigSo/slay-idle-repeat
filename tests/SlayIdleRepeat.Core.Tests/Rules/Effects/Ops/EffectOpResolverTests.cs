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
/// 🔴 <b>PHASE 1a</b> — <c>RANDOM_OUTCOME</c>'s handler is a stub that throws
/// <see cref="NotSupportedException"/> naming M2-12's boss-engine phase (see
/// <c>CombatFlowOps.RandomOutcome</c>). Both loops below therefore tolerate <b>that one exception
/// type</b>, which still proves the routing claim — the op reached its arm rather than the
/// <c>default</c> — and nothing else: no other op throws it, and the disposition loop records the
/// op as unproven rather than passing it. 🔒 Both accommodations are deleted when the handler
/// lands; grep <c>NotSupportedException</c> in this file.
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
            catch (NotSupportedException)
            {
                // 🔴 PHASE 1a — RANDOM_OUTCOME's handler is a stub. Reaching it IS the routing claim
                //    this test makes; the behaviour is RandomOutcomeOpTests' (currently red).
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
        var unproven = new List<string>();

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

            try
            {
                var outcome = EffectOpResolver.Resolve(
                    OpFixtures.Exemplar(op),
                    bench.Context(evaluation, damageDealt: 100.0, healAmount: 50.0, overhealAmount: 10.0));

                if (outcome.Disposition != expected)
                {
                    wrong.Add($"{op}: expected {expected}, got {outcome.Disposition}");
                }
            }
            catch (NotSupportedException)
            {
                // 🔴 PHASE 1a — RANDOM_OUTCOME's handler is a stub, so its disposition cannot be
                //    observed yet. Recorded as unproven rather than silently counted as correct.
                unproven.Add(op.ToString());
            }
        }

        wrong.ShouldBeEmpty();

        // 🔴 PHASE 1a — exactly the one stubbed op, and no other. When M2-12's handler lands this
        //    goes empty and the catch above is deleted with it.
        unproven.ShouldBe([nameof(EffectOp.RANDOM_OUTCOME)]);
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
        var statOps = EffectOps.All.Where(o => EffectOps.FamilyOf(o) == EffectOpFamily.STAT).ToArray();

        // S3 — the floor, and it matters twice over here: both assertions below are "empty", so an
        // empty SUBJECT set would be doubly invisible.
        statOps.Length.ShouldBe(6, "18 §2.1");

        foreach (var op in statOps)
        {
            EffectOpResolver.Resolve(
                OpFixtures.Exemplar(op), bench.Context(EffectTestBattle.Context(hero, hero)));
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
}
