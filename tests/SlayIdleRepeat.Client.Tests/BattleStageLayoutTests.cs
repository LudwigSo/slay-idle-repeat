using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

public sealed class BattleStageLayoutTests
{
    private const byte HeroSlot = 0;
    private const byte FirstPetSlot = 1;
    private const byte SecondPetSlot = 2;
    private const byte FirstEnemySlot = 4;

    // Not the shipped distances, so a layout with those written in lands somewhere a case can see.
    private static readonly BattleStageMetrics Metrics = new(HalfGap: 3f, EnemySpacing: 1.5f, ArcDepth: 0.5f);

    [Fact]
    public void Place_stands_the_hero_at_minus_HalfGap_on_the_centre_line_facing_the_enemies()
    {
        var hero = PlacementOf(Place(Roster(enemies: 1)), HeroSlot);

        hero.Position.X.ShouldBe(-Metrics.HalfGap, 0.0001f, "the hero's side of the stage is negative X.");
        hero.Position.Z.ShouldBe(0f, 0.0001f, "the hero stands on the centre line, level with the first enemy.");
        hero.Facing.ShouldBe(StageFacing.PositiveX);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, -1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, -2, 2)]
    [InlineData(4, 2, 2)]
    public void Place_fills_the_enemy_slots_centre_first_then_alternating_from_minus_Z(
        int slot, int spacingSteps, int arcSteps)
    {
        var enemy = PlacementOf(Place(Roster(enemies: 5)), (byte)(FirstEnemySlot + slot));

        enemy.Position.Z.ShouldBe(
            spacingSteps * Metrics.EnemySpacing,
            0.0001f,
            "the slot order is 0, −s, +s, −2s, +2s so an arriving actor takes the next slot and moves nobody.");
        enemy.Position.X.ShouldBe(
            Metrics.HalfGap + (arcSteps * Metrics.ArcDepth),
            0.0001f,
            "each step out from the centre slot is pushed back by ArcDepth so the line reads as an arc.");
    }

    [Fact]
    public void Place_faces_every_enemy_toward_the_hero()
    {
        var facings = Place(Roster(enemies: 3))
            .Where(p => p.ActorId >= FirstEnemySlot)
            .Select(p => p.Facing);

        facings.ShouldBe(new[] { StageFacing.NegativeX, StageFacing.NegativeX, StageFacing.NegativeX });
    }

    [Fact]
    public void Place_leaves_every_earlier_actor_where_it_stood_when_a_summon_arrives()
    {
        var before = Place(Roster(enemies: 2));
        var after = Place(Roster(enemies: 2, summons: 1));

        before.Count.ShouldBe(3, "the hero and two enemies.");
        after.Take(3).ShouldBe(
            before,
            "a summon takes the next free slot; a layout that re-centred the enemies on their new count " +
            "would slide every one already standing there.");
        after[3].Position.ShouldNotBe(after[1].Position);
        after[3].Position.ShouldNotBe(after[2].Position);
    }

    [Fact]
    public void Place_stands_a_pet_behind_the_hero_facing_the_same_way()
    {
        var pet = PlacementOf(Place(Roster(enemies: 1, pets: 1)), FirstPetSlot);

        pet.Position.X.ShouldBeLessThan(
            -Metrics.HalfGap, "behind the hero is further from the enemies than the hero stands.");
        pet.Facing.ShouldBe(StageFacing.PositiveX);
    }

    [Fact]
    public void Place_gives_two_pets_two_different_places()
    {
        var placements = Place(Roster(enemies: 1, pets: 2));

        PlacementOf(placements, FirstPetSlot).Position
            .ShouldNotBe(PlacementOf(placements, SecondPetSlot).Position);
    }

    [Fact]
    public void Place_answers_one_placement_per_actor_in_the_order_given()
    {
        var actors = Roster(enemies: 2, pets: 1);

        Place(actors).Select(p => p.ActorId).ShouldBe(actors.Select(a => a.ActorId));
    }

    private static IReadOnlyList<StagePlacement> Place(IReadOnlyList<ReplayActor> actors) =>
        BattleStageLayout.Place(actors, Metrics);

    private static StagePlacement PlacementOf(IReadOnlyList<StagePlacement> placements, byte actorId) =>
        placements.SingleOrDefault(p => p.ActorId == actorId)
            .ShouldNotBeNull($"actor {actorId} was placed nowhere, or more than once.");

    private static IReadOnlyList<ReplayActor> Roster(int enemies, int pets = 0, int summons = 0)
    {
        var actors = new List<ReplayActor> { Actor(HeroSlot, ReplaySide.Hero, 0) };

        actors.AddRange(Enumerable.Range(0, pets)
            .Select(i => Actor((byte)(FirstPetSlot + i), ReplaySide.Pet, i + 1)));
        actors.AddRange(Enumerable.Range(0, enemies)
            .Select(i => Actor((byte)(FirstEnemySlot + i), ReplaySide.Enemy, i + 1)));
        actors.AddRange(Enumerable.Range(0, summons)
            .Select(i => Actor((byte)(FirstEnemySlot + enemies + i), ReplaySide.Enemy, enemies + i + 1, summon: true)));

        return actors;
    }

    private static ReplayActor Actor(byte slot, ReplaySide side, int sideIndex, bool summon = false) =>
        new(slot, side, sideIndex, MaxHp: 10, StartingHp: 10, EndingHp: 10,
            Identity: "GRUNT", IsElite: false, IsBoss: false, IsSummon: summon);
}
