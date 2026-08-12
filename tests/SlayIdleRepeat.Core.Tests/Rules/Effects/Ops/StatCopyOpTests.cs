using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.4's <c>STAT_COPY</c> — <b>the op whose <c>target</c> names the copy source, not the
/// recipient</b> (R13).
/// </summary>
/// <remarks>
/// The test names carry the inversion on purpose. `18` never flags it, every other op in the DSL
/// reads the other way, and the failure mode is a later reader "correcting" it — at which point
/// <c>PK_PACK_LEADER</c> gives the hero the pets' crit chance (zero) instead of the reverse and
/// nothing goes red unless a test says so in its own title.
/// </remarks>
public sealed class StatCopyOpTests
{
    /// <summary>
    /// 🔴 R13, stated as arithmetic. `06`: <c>PK_PACK_LEADER</c> is <em>"pets gain <b>your</b> crit
    /// chance"</em> — the pet holds the effect, the hero is the <c>target</c>, and the bucket lands
    /// on the pet.
    /// </summary>
    [Fact]
    public void STAT_COPY_reads_the_target_as_the_copy_SOURCE_and_writes_onto_the_HOLDER()
    {
        var hero = EffectTestBattle.Hero();
        var pet = EffectTestBattle.Pet("PET_WOLF", 1);

        var bench = new OpTestBench()
            .WithStat(hero, StatId.CRIT, 0.62)
            .WithStat(pet, StatId.CRIT, 0.0);

        // The pet HOLDS the effect; ATTACKER names the hero as the copy source.
        var packLeader = OpFixtures.Effect(
            "PK_PACK_LEADER_T1_CRIT", EffectOp.STAT_COPY, 1.0, EffectTarget.ATTACKER) with
        {
            Stat = StatSelector.Of(StatId.CRIT),
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
        };

        var evaluation = EffectTestBattle.Context(pet, hero, pet) with { Attacker = hero };
        StatCopyOp.Resolve(packLeader, bench.Context(evaluation));

        bench.PercentBuckets.ShouldBe(
            [("PET_WOLF", StatId.CRIT, 0.62)],
            "18 §2.4 copies the SOURCE's final resolved stat ONTO THE HOLDER — inverting it would " +
            "give the hero the pet's 0.0 crit, which is what PK_PACK_LEADER must not do");

        // 🔒 "as a percent-bucket add FOR DURATION" — the bucket is not permanent.
        bench.OnlyLifetime("AddPercentBucket:CRIT").Duration!.Scope.ShouldBe(DurationScope.BATTLE);
    }

    /// <summary>The <c>value</c> is a factor on the copied stat, not a replacement for it.</summary>
    [Fact]
    public void STAT_COPY_multiplies_the_copied_stat_by_its_value()
    {
        var hero = EffectTestBattle.Hero();
        var pet = EffectTestBattle.Pet("PET_WOLF", 1);
        var bench = new OpTestBench().WithStat(hero, StatId.CRIT, 0.4);

        var half = OpFixtures.Effect("PK_HALF_COPY", EffectOp.STAT_COPY, 0.5, EffectTarget.ATTACKER) with
        {
            Stat = StatSelector.Of(StatId.CRIT),
        };

        var evaluation = EffectTestBattle.Context(pet, hero, pet) with { Attacker = hero };
        StatCopyOp.Resolve(half, bench.Context(evaluation)).ShouldBe(0.2);
    }

    /// <summary>
    /// 🔒 `18` §2.4 — <em>"reads the start-of-tick snapshot, so mutual copies cannot recurse"</em>,
    /// exhibited: two actors copying each other both read the same frozen block, so neither sees the
    /// other's output.
    /// </summary>
    /// <remarks>
    /// The property lives on <see cref="IResolvedStatReader"/> and not on the op — an op cannot know
    /// how the simulator stores stats — so this drives the <b>real</b> op through a reader that is
    /// literally a fixed map. Under a live reader the second copy would read 0.4 + 0.6 = 1.0 and
    /// write 1.0, and the pair would ratchet on every subsequent tick.
    /// </remarks>
    [Fact]
    public void Two_actors_copying_each_other_both_read_the_start_of_tick_snapshot()
    {
        var first = EffectTestBattle.Hero() with { Id = "A", Index = 0 };
        var second = EffectTestBattle.Enemy("B", 1);

        var bench = new OpTestBench()
            .WithStat(first, StatId.CRIT, 0.4)
            .WithStat(second, StatId.CRIT, 0.6);

        var copy = OpFixtures.Effect("X_COPY", EffectOp.STAT_COPY, 1.0, EffectTarget.CURRENT_TARGET) with
        {
            Stat = StatSelector.Of(StatId.CRIT),
        };

        var aCopiesB = EffectTestBattle.Context(first, first, second) with { CurrentTarget = second };
        var bCopiesA = EffectTestBattle.Context(second, first, second) with { CurrentTarget = first };

        StatCopyOp.Resolve(copy, bench.Context(aCopiesB));
        StatCopyOp.Resolve(copy, bench.Context(bCopiesA));

        bench.PercentBuckets.ShouldBe(
            [("A", StatId.CRIT, 0.6), ("B", StatId.CRIT, 0.4)],
            "each read the other's START-OF-TICK crit; a live read would have made the second 1.0");
    }

    /// <summary>
    /// `18` §2.4 — <c>HIGHEST_PCT_BONUS</c>, <em>"whichever stat carries the largest percent bucket
    /// at copy time"</em>. Cogitator's <em>Recalibrate</em> (`17` §7).
    /// </summary>
    /// <remarks>
    /// 🔒 Resolved against the <b>copy source</b>, because §2.4 copies <em>"the copy-source's final
    /// resolved stat"</em> — which bucket is largest is a fact about the actor being copied.
    /// </remarks>
    [Fact]
    public void HIGHEST_PCT_BONUS_names_the_copy_sources_largest_bucket_not_the_holders()
    {
        var boss = EffectTestBattle.Enemy("BOSS_COGITATOR", 1);
        var hero = EffectTestBattle.Hero();

        var bench = new OpTestBench()
            .WithHighestBucket(hero, StatId.ATK)
            .WithHighestBucket(boss, StatId.DEF)
            .WithStat(hero, StatId.ATK, 1.35);

        var recalibrate = OpFixtures.Effect(
            "BOSS_COGITATOR_RECALIBRATE", EffectOp.STAT_COPY, 1.0, EffectTarget.CURRENT_TARGET) with
        {
            Stat = StatSelector.HighestPctBonus,
        };

        var evaluation = EffectTestBattle.Context(boss, boss, hero) with { CurrentTarget = hero };
        StatCopyOp.Resolve(recalibrate, bench.Context(evaluation));

        bench.PercentBuckets.ShouldBe(
            [("BOSS_COGITATOR", StatId.ATK, 1.35)],
            "the hero's largest bucket is ATK; reading the boss's would have copied DEF");
    }

    /// <summary>
    /// 🔒 <c>ALL_COMBAT</c> is refused rather than expanded — §2.4 admits <em>"a stat name or
    /// <c>HIGHEST_PCT_BONUS</c>"</em>, and expanding would turn Recalibrate into a copy of the whole
    /// stat block.
    /// </summary>
    [Fact]
    public void STAT_COPY_refuses_the_ALL_COMBAT_group_selector()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench();

        var group = OpFixtures.Effect("PK_X", EffectOp.STAT_COPY, 1.0, EffectTarget.CURRENT_TARGET) with
        {
            Stat = StatSelector.AllCombat,
        };

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };

        var thrown = Should.Throw<EffectContextException>(
            () => StatCopyOp.Resolve(group, bench.Context(evaluation)));

        // 🔒 The EFFECT id, not the op name — every other refusal in the layer names the effect, and
        //    the review found this one throwing "STAT_COPY" instead, which is the one failure that
        //    could not be traced back to a content row.
        thrown.Token.ShouldBe("PK_X");
        thrown.Message.ShouldContain("copy fourteen", Case.Sensitive);

        bench.PercentBuckets.ShouldBeEmpty();
    }
}
