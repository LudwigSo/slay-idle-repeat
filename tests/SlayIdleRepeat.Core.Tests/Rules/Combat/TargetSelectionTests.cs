using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

public sealed class TargetSelectionTests
{
    /// <summary>
    /// The hero targets the highest <c>targetPriority</c> even when that enemy is at full health and
    /// the others are nearly dead.
    /// </summary>
    [Fact]
    public void The_hero_targets_the_highest_targetPriority_before_it_looks_at_HP()
    {
        var target = SelectForHero(
            Enemy(0, hp: 1, priority: 0.0),
            Enemy(1, hp: 100, priority: 1.0),
            Enemy(2, hp: 5, priority: 0.0));

        target!.Id.ShouldBe("ENEMY_1");
    }

    /// <summary>Equal priority is broken by lowest current HP.</summary>
    [Fact]
    public void Equal_priority_is_broken_by_lowest_current_HP()
    {
        var target = SelectForHero(
            Enemy(0, hp: 80),
            Enemy(1, hp: 12),
            Enemy(2, hp: 40));

        target!.Id.ShouldBe("ENEMY_1");
    }

    /// <summary>
    /// A <c>-1</c> priority deprioritises an enemy: the hero keeps hitting the queen while the
    /// sporelings are up, even though they are the weakest thing on the field.
    /// </summary>
    [Fact]
    public void A_deprioritised_sporeling_is_ignored_while_the_queen_is_alive()
    {
        var queen = Enemy(0, hp: 500, priority: 0.0, id: "SPOREQUEEN");
        var sporelings = new[]
        {
            Enemy(1, hp: 10, priority: -1.0, id: "SPORELING_A"),
            Enemy(2, hp: 10, priority: -1.0, id: "SPORELING_B"),
        };

        SelectForHero(new[] { queen }.Concat(sporelings).ToArray())!.Id.ShouldBe("SPOREQUEEN");

        // And once she is down, the sporelings are all that is left — the -1 does not exclude them.
        queen.SetCurrentHp(0);
        SelectForHero(new[] { queen }.Concat(sporelings).ToArray())!.Id.ShouldBe("SPORELING_A");
    }

    /// <summary>
    /// Enemies tied on priority and HP fall back to the actor index, not to list order.
    /// </summary>
    [Fact]
    public void An_exact_tie_is_broken_by_the_actor_index_and_never_by_list_order()
    {
        var a = Enemy(0, hp: 35, id: "SWARM_A");
        var b = Enemy(1, hp: 35, id: "SWARM_B");
        var c = Enemy(2, hp: 35, id: "SWARM_C");

        SelectForHero(a, b, c)!.Id.ShouldBe("SWARM_A");
        SelectForHero(c, b, a)!.Id.ShouldBe("SWARM_A");
        SelectForHero(b, c, a)!.Id.ShouldBe("SWARM_A");
    }

    /// <summary>
    /// A pet's targeted ability selects the highest current HP — the opposite of the hero's
    /// tie-break — and reads no <c>targetPriority</c>.
    /// </summary>
    [Fact]
    public void A_pet_ability_chips_the_tanky_one_and_ignores_targetPriority()
    {
        var actors = new[]
        {
            Enemy(0, hp: 10, priority: 1.0),
            Enemy(1, hp: 900),
            Enemy(2, hp: 300),
        };

        var pet = new BattleActor(BattleTestBench.Pet(0), NoStatusTimelineDouble.Instance);

        TargetSelection.ForPetAbility(Context(pet, actors.Prepend(pet)))!.Id.ShouldBe("ENEMY_1");

        // The hero, on the same field, goes for the forced-focus one instead.
        TargetSelection.ForBasicAttack(Context(Hero(), actors.Prepend(Hero())))!.Id.ShouldBe("ENEMY_0");
    }

    /// <summary>Enemies always target the hero, never a pet.</summary>
    [Fact]
    public void An_enemy_targets_the_hero_and_never_a_pet()
    {
        var hero = Hero();
        var pet = new BattleActor(BattleTestBench.Pet(0), NoStatusTimelineDouble.Instance);
        var enemy = Enemy(0);

        var roster = new[] { hero, pet, enemy };

        TargetSelection.ForEnemyAttack(enemy, roster)!.Id.ShouldBe("HERO");

        // And once the hero is down there is nothing left for it to hit.
        hero.SetCurrentHp(0);
        TargetSelection.ForEnemyAttack(enemy, roster).ShouldBeNull();
    }

    /// <summary>Pets cannot be targeted or killed, so no selection ever names one.</summary>
    [Fact]
    public void No_selection_ever_names_a_pet()
    {
        var hero = Hero();
        var enemyPet = new BattleActor(
            BattleTestBench.Pet(0) with
            {
                Id = "ENEMY_PET",
                Index = 9,
                LogId = 20,
                Side = BattleSide.ENEMY,
            },
            NoStatusTimelineDouble.Instance);
        var enemy = Enemy(0, hp: 900);

        var roster = new IEffectActorView[] { hero, enemyPet, enemy };

        TargetSelection.ForBasicAttack(Context(hero, roster))!.Id.ShouldBe("ENEMY_0");
        TargetSelection.ForPetAbility(Context(hero, roster))!.Id.ShouldBe("ENEMY_0");
    }

    /// <summary>A side with nothing living left selects nothing, rather than an arbitrary actor.</summary>
    [Fact]
    public void A_cleared_side_selects_nothing()
    {
        var hero = Hero();
        var dead = Enemy(0);
        dead.SetCurrentHp(0);

        TargetSelection.ForBasicAttack(Context(hero, new IEffectActorView[] { hero, dead })).ShouldBeNull();
        TargetSelection.ForPetAbility(Context(hero, new IEffectActorView[] { hero, dead })).ShouldBeNull();
    }

    /// <summary>
    /// A second implementation of <see cref="IEffectActorView"/> in the candidate list is two
    /// rosters, and it is refused.
    /// </summary>
    [Fact]
    public void A_foreign_actor_view_is_refused_rather_than_silently_selected()
    {
        var hero = Hero();
        var foreign = new EffectTestActor { Id = "FOREIGN", Index = 4, Side = BattleSide.ENEMY };

        Should.Throw<InvalidOperationException>(
                () => TargetSelection.ForBasicAttack(Context(hero, new IEffectActorView[] { hero, foreign })))
            .Message.ShouldContain("one roster");
    }

    private static BattleActor Hero() =>
        new(BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)), NoStatusTimelineDouble.Instance);

    private static BattleActor Enemy(int index, double hp = 100, double priority = 0.0, string? id = null)
    {
        var plan = BattleTestBench.Enemy(index, BattleTestBench.Stats(maxHp: 1000), priority);
        var actor = new BattleActor(id is null ? plan : plan with { Id = id }, NoStatusTimelineDouble.Instance);
        actor.SetCurrentHp(hp);

        return actor;
    }

    private static BattleActor? SelectForHero(params BattleActor[] enemies)
    {
        var hero = Hero();

        return TargetSelection.ForBasicAttack(Context(hero, enemies.Prepend<IEffectActorView>(hero)));
    }

    private static EffectEvaluationContext Context(
        IEffectActorView holder, IEnumerable<IEffectActorView> actors) =>
        new()
        {
            Holder = holder,
            Actors = actors.ToArray(),
            FightHorizonSeconds = 90.0,
        };
}

/// <summary>
/// The strict timeline, reachable from tests — <c>NoStatusTimeline</c>'s constructor is private and
/// its <c>Instance</c> is what <c>BattleSeams.Strict</c> holds.
/// </summary>
internal sealed class NoStatusTimelineDouble : IStatusTimeline
{
    internal static NoStatusTimelineDouble Instance { get; } = new();

    public void AdvanceTimers(BattleActor actor, int tick)
    {
    }

    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    public bool CanAct(BattleActor actor) => true;

    public int StacksOn(BattleActor actor, string statusId) => 0;

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
}
