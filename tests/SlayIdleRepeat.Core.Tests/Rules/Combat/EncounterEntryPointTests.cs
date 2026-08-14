using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 <see cref="CombatSimulator.SimulateEncounter"/>, the first public entry point for a normal
/// encounter, run against the real shipped <c>enemies.json</c>.
/// </summary>
/// <remarks>
/// Before this method, <c>EnemyCatalogue</c>, <c>ChapterEnemyPool</c>, <c>EnemyDerivation</c> and
/// <c>EliteModifierDraw</c> were reachable only from inside <c>Core</c>, and no production roster ever
/// set <see cref="ActorPlan.IsElite"/> to a computed value. Every case runs the real engine against
/// the real content tree, not a fake seam.
/// </remarks>
public sealed class EncounterEntryPointTests
{
    /// <summary>An arbitrary but fixed battle seed, used across this suite's fights.</summary>
    private const ulong BattleSeed = 0xE4_C0_1D_E4_0000_0001UL;

    /// <summary>Chapter 1's authored base level (`05` §6.0), and this suite's chapter throughout.</summary>
    private const int Chapter = 1;

    /// <summary>NORMAL — ordinal 0 of `05` §6.0's tier table.</summary>
    private const int NormalTier = 0;

    private static readonly Lazy<ContentSnapshot> LazyContent = new(GameDataLoader.Load);

    private static ContentSnapshot Content => LazyContent.Value;

    /// <summary>A hero strong enough to win against a Chapter 1 roster inside `05` §3's 1800-tick bound.</summary>
    private static ActorStats StrongHero() => ActorStats.From(new Dictionary<StatId, double>
    {
        [StatId.MAX_HP] = 5_000.0,
        [StatId.ATK] = 400.0,
        [StatId.ASPD] = 1.0,
        [StatId.DEF] = 200.0,
        [StatId.CRIT] = 0.05,
        [StatId.CDMG] = 0.5,
        [StatId.LIFESTEAL] = 0.0,
        [StatId.DODGE] = 0.02,
        [StatId.BLOCK] = 0.0,
        [StatId.PEN] = 0.0,
        [StatId.DMG_PCT] = 0.0,
        [StatId.DR_PCT] = 0.0,
        [StatId.HEAL_PCT] = 1.0,
        [StatId.THORNS] = 0.0,
    });

    // ═══════════════════════════════════════════════════════════ acceptance 1: a real encounter

    /// <summary>
    /// 🔒 Acceptance 1 — a real fight against real <c>EnemyCatalogue</c> content, callable from
    /// outside <c>Core.Rules</c>.
    /// </summary>
    [Fact]
    public void Runs_a_real_fight_against_the_shipped_enemy_catalogue()
    {
        var result = CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), heroLevel: 10, Chapter, NormalTier,
            enemyPowers: new[] { 400.0 }, Content);

        result.ShouldNotBeNull();
        result.Log.Count.ShouldBeGreaterThan(0, "a real fight logs at least BattleStart plus a swing");
        result.DurationTicks.ShouldBeInRange(1, CombatLog.MaxTicks);
        result.HeroWon.ShouldBeTrue("this hero block was calibrated to win against Chapter 1 power 400");

        result.Log.ShouldContain(
            e => e.Type == CombatEventType.Attack,
            "a real ENEMY_0 body was derived from content/enemies/enemies.json and actually swings");
    }

    /// <summary>Negative control: an empty roster is refused rather than silently won at tick 0.</summary>
    [Fact]
    public void An_empty_roster_throws_rather_than_a_free_win()
    {
        Should.Throw<ArgumentException>(() => CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, Chapter, NormalTier, Array.Empty<double>(), Content));
    }

    /// <summary>Negative control: a chapter `05` §6.0 does not author throws rather than defaulting a level.</summary>
    [Fact]
    public void An_unauthored_chapter_throws()
    {
        Should.Throw<KeyNotFoundException>(() => CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, chapter: 99, NormalTier, new[] { 400.0 }, Content));
    }

    /// <summary>Negative control: an <c>eliteIndex</c> outside the roster is refused, not silently ignored.</summary>
    [Fact]
    public void An_out_of_range_elite_index_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, Chapter, NormalTier, new[] { 400.0 }, Content, eliteIndex: 1));
    }

    // ═══════════════════════════════════════════════════════════ acceptance 5: elite wiring

    /// <summary>
    /// 🔒 A fight with an Elite drawn measurably differs from the same fight without one: `05` §6.2's
    /// ×2.2 power multiplier is real, and <c>EliteModifierDraw</c> now has a production caller.
    /// </summary>
    /// <remarks>
    /// Two shapes over the identical seed, hero and enemy power — the only difference is
    /// <paramref name="eliteIndex"/>, so nothing but the elite treatment can move the outcome. The
    /// punchbag hero is unkillable and deals nothing, so both fights run the full 1800 ticks and
    /// <see cref="SimulationResult.HeroHpRemaining"/> is a clean cumulative reading of the enemy's ATK.
    /// <c>DurationTicks</c> is pinned equal below so the HP reading cannot be explained by fight length.
    /// </remarks>
    private static ActorStats Punchbag() => ActorStats.From(new Dictionary<StatId, double>
    {
        [StatId.MAX_HP] = 1_000_000_000.0,
        [StatId.ATK] = 1.0,
        [StatId.ASPD] = 1.0,
        [StatId.DEF] = 300.0,
        [StatId.CRIT] = 0.0,
        [StatId.CDMG] = 0.5,
        [StatId.LIFESTEAL] = 0.0,
        [StatId.DODGE] = 0.0,
        [StatId.BLOCK] = 0.0,
        [StatId.PEN] = 0.0,
        [StatId.DMG_PCT] = 0.0,
        [StatId.DR_PCT] = 0.0,
        [StatId.HEAL_PCT] = 1.0,
        [StatId.THORNS] = 0.0,
    });

    [Fact]
    public void An_elite_slot_measurably_differs_from_the_same_slot_without_one()
    {
        // A power so large that neither a non-elite nor an elite-scaled (x2.2) derivation is
        // killable by Punchbag()'s ATK 1.0 inside 1800 ticks.
        const double EnemyPower = 50_000_000.0;

        var withoutElite = CombatSimulator.SimulateEncounter(
            BattleSeed, Punchbag(), 10, Chapter, NormalTier, new[] { EnemyPower }, Content, eliteIndex: -1);

        var withElite = CombatSimulator.SimulateEncounter(
            BattleSeed, Punchbag(), 10, Chapter, NormalTier, new[] { EnemyPower }, Content, eliteIndex: 0);

        withElite.LogHash.ShouldNotBe(
            withoutElite.LogHash, "05 §6.2's ×2.2 power multiplier changes the derived stat block");

        // 🔒 Both fights run the full bound — proves the HP reading below is a like-for-like 1800
        // ticks of enemy ATK, not one side simply running longer.
        withoutElite.DurationTicks.ShouldBe(CombatLog.MaxTicks);
        withElite.DurationTicks.ShouldBe(CombatLog.MaxTicks);

        // 🔴 Steering S2: assert the identity, not just the symptom. The Elite's ATK, derived at
        // 2.2x power, is strictly higher than ANY 05 §6.1 archetype's ATK at 1x power for every one
        // of Chapter 1's eight possible non-elite draws — so the hero must have taken strictly MORE
        // cumulative damage over the identical 1800 ticks with an Elite in the roster.
        withElite.HeroHpRemaining.ShouldBeLessThan(
            withoutElite.HeroHpRemaining,
            "05 §6.2's Elite is derived at 2.2x power: over the identical 1800-tick window it must " +
            $"deal strictly more cumulative damage than any non-elite draw. Without: HpRemaining=" +
            $"{withoutElite.HeroHpRemaining}. With: HpRemaining={withElite.HeroHpRemaining}.");
    }

    // ═══════════════════════════════════════════════════════════ acceptance 3: attaching an effect

    /// <summary>
    /// 🔒 The public surface can attach an <c>APPLY_STATUS</c> effect to the hero and it actually
    /// resolves mid-fight — unreachable through the public surface until this method existed.
    /// </summary>
    /// <remarks>
    /// Two shapes: with the held effect a real <see cref="CombatEventType.StatusApplied"/> lands on the
    /// enemy; without it nothing of the kind appears — the control proving the status came from the
    /// attached effect and not from content the enemy carries.
    /// </remarks>
    [Fact]
    public void An_attached_APPLY_STATUS_effect_actually_resolves_during_the_fight()
    {
        var proof = new EffectDefinition
        {
            Id = "TEST_PROOF_APPLY_STATUS",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "POISON",
            Value = 0.02,
            Target = EffectTarget.CURRENT_TARGET,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_HIT },
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 5.0 },
        };

        var withEffect = CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, Chapter, NormalTier, new[] { 400.0 }, Content,
            heroEffects: new[] { proof });

        var withoutEffect = CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, Chapter, NormalTier, new[] { 400.0 }, Content);

        withEffect.Log.ShouldContain(
            e => e.Type == CombatEventType.StatusApplied,
            "the attached APPLY_STATUS effect should have resolved onto the hero's attack target");

        withoutEffect.Log.ShouldNotContain(
            e => e.Type == CombatEventType.StatusApplied,
            "the negative control: the identical fight with no attached effect applies no status at all");
    }

    /// <summary>
    /// Negative control on the attachment mechanism itself: an <c>ON_KILL</c> effect with no
    /// instance id is refused by <c>BattlePlan.Validated</c> rather than minted a battle-local one —
    /// `18` §3 makes an <c>ON_KILL</c> counter run-scoped, and this public surface has no run to hand
    /// it an id from.
    /// </summary>
    [Fact]
    public void An_attached_ON_KILL_effect_with_no_instance_id_is_refused()
    {
        var onKill = new EffectDefinition
        {
            Id = "TEST_ON_KILL_WITH_NO_INSTANCE_ID",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "POISON",
            Value = 0.01,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_KILL },
        };

        Should.Throw<ArgumentException>(() => CombatSimulator.SimulateEncounter(
            BattleSeed, StrongHero(), 10, Chapter, NormalTier, new[] { 400.0 }, Content,
            heroEffects: new[] { onKill }));
    }
}
