using Shouldly;
using SlayIdleRepeat.Core.Tests.TestSupport;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <see cref="CombatSimulator.SimulateEncounter"/>, the first public entry point for a normal
/// encounter, run against the real shipped <c>enemies.json</c>.
/// </summary>
/// <remarks>Every case runs the real engine against the real content tree, not a fake seam.</remarks>
public sealed class EncounterEntryPointTests
{
    private const ulong BattleSeed = 0xE4_C0_1D_E4_0000_0001UL;
    private const int Chapter = 1;

    /// <summary>
    /// A chapter that still carries <c>05</c> §6.2's ×2.2 elite power multiplier. Chapter 1 does
    /// not: it authors <c>1.4</c>, because an Elite and a MINI-BOSS are one code path and chapter 1's
    /// two mini-bosses have to be passable by a hero with no gear. At <c>1.4</c> an Elite's derived
    /// ATK can fall BELOW a non-elite draw's whenever the elite identity's archetype has the lower
    /// <c>atkCoef</c> — <c>WARDEN</c>'s is <c>0.7</c> against <c>CASTER</c>'s <c>1.5</c> — so the
    /// "an Elite always hits harder" claim belongs to a chapter whose multiplier still says so.
    /// </summary>
    private const int ChapterWithStandardEliteMultiplier = 2;

    /// <summary>NORMAL — the base difficulty tier.</summary>
    private const int NormalTier = 0;

    private static readonly Lazy<ContentSnapshot> LazyContent = new(GameDataLoader.Load);

    private static ContentSnapshot Content => LazyContent.Value;

    /// <summary>A hero strong enough to win against a Chapter 1 roster inside the 1800-tick bound.</summary>
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
        [StatId.DMG_PCT] = 1.0,
        [StatId.DR_PCT] = 1.0,
        [StatId.HEAL_PCT] = 1.0,
        [StatId.THORNS] = 0.0,
    });

    /// <summary>A real fight against real <c>EnemyCatalogue</c> content, callable from outside <c>Core.Rules</c>.</summary>
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

    /// <summary>Negative control: an unauthored chapter throws rather than defaulting a level.</summary>
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

    /// <summary>
    /// A fight with an Elite drawn measurably differs from the same fight without one: the ×2.2 power
    /// multiplier is real. The punchbag hero is unkillable and deals nothing, so both fights run the
    /// full 1800 ticks and <see cref="SimulationResult.HeroHpRemaining"/> is a clean cumulative
    /// reading of the enemy's ATK.
    /// </summary>
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
        [StatId.DMG_PCT] = 1.0,
        [StatId.DR_PCT] = 1.0,
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
            BattleSeed, Punchbag(), 10, ChapterWithStandardEliteMultiplier, NormalTier,
            new[] { EnemyPower }, Content, eliteIndex: -1);

        var withElite = CombatSimulator.SimulateEncounter(
            BattleSeed, Punchbag(), 10, ChapterWithStandardEliteMultiplier, NormalTier,
            new[] { EnemyPower }, Content, eliteIndex: 0);

        withElite.LogHash.ShouldNotBe(
            withoutElite.LogHash, "05 §6.2's ×2.2 power multiplier changes the derived stat block");

        // Both fights run the full bound — proves the HP reading below is a like-for-like 1800 ticks
        // of enemy ATK, not one side simply running longer.
        withoutElite.DurationTicks.ShouldBe(CombatLog.MaxTicks);
        withElite.DurationTicks.ShouldBe(CombatLog.MaxTicks);

        withElite.HeroHpRemaining.ShouldBeLessThan(
            withoutElite.HeroHpRemaining,
            "05 §6.2's Elite is derived at 2.2x power: over the identical 1800-tick window it must " +
            $"deal strictly more cumulative damage than any non-elite draw. Without: HpRemaining=" +
            $"{withoutElite.HeroHpRemaining}. With: HpRemaining={withElite.HeroHpRemaining}.");
    }

    /// <summary>
    /// The public surface can attach an <c>APPLY_STATUS</c> effect to the hero and it actually
    /// resolves mid-fight. The negative control (no attached effect) proves the status came from the
    /// attached effect and not from content the enemy carries.
    /// </summary>
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
    /// Negative control on the attachment mechanism itself: an <c>ON_KILL</c> effect with no instance
    /// id is refused by <c>BattlePlan.Validated</c> rather than minted a battle-local one, since this
    /// public surface has no run to hand it an id from.
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
