using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The actor-id layout of the log: the meaning of a <see cref="byte"/> in <see cref="CombatEvent.SourceId"/>.</summary>
public sealed class CombatActorTests
{
    /// <summary>Hero, then pets in slot order, then enemies by index, with a sentinel above them all.</summary>
    [Fact]
    public void The_layout_is_the_documented_actor_order()
    {
        CombatActor.Hero.ShouldBe((byte)0);
        CombatActor.FirstPet.ShouldBe((byte)1);
        CombatActor.PetSlots.ShouldBe(3);
        CombatActor.FirstEnemy.ShouldBe((byte)4);
        CombatActor.MaxId.ShouldBe((byte)254);
        CombatActor.None.ShouldBe((byte)255);
    }

    /// <summary>
    /// The sentinel is above every addressable actor, so no participant can be mistaken for "no
    /// actor": an off-by-one letting <see cref="CombatActor.MaxId"/> reach the sentinel would make
    /// the last summon of a long boss fight render as nobody.
    /// </summary>
    [Fact]
    public void The_sentinel_is_above_every_addressable_actor()
    {
        CombatActor.MaxId.ShouldBeLessThan(CombatActor.None);
        CombatActor.None.ShouldBe(byte.MaxValue);
        CombatActor.IsActor(CombatActor.None).ShouldBeFalse();
        CombatActor.IsActor(CombatActor.MaxId).ShouldBeTrue();
        CombatActor.IsActor(CombatActor.Hero).ShouldBeTrue();
    }

    /// <summary>Pets take the three ids between the hero and the enemies, in slot order.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void A_pet_takes_its_slots_id(int slot, byte expected)
    {
        CombatActor.Pet(slot).ShouldBe(expected);
    }

    /// <summary>
    /// Pet ids are reserved whether or not the slot is filled, so an actor's id is a function of its
    /// role, not how many pets the player brought — a replayer never has to re-derive "which id is
    /// the second enemy" from the roster.
    /// </summary>
    [Fact]
    public void Enemy_ids_do_not_move_with_the_pet_count()
    {
        CombatActor.Enemy(0).ShouldBe(CombatActor.FirstEnemy);
        CombatActor.Enemy(0).ShouldBe((byte)(CombatActor.FirstPet + CombatActor.PetSlots));
    }

    /// <summary>A pet slot outside the documented three is a bug, not a fourth pet.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void A_pet_slot_outside_the_documented_three_is_refused(int slot)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CombatActor.Pet(slot));
    }

    /// <summary>Enemies and their summons run from <see cref="CombatActor.FirstEnemy"/> upward.</summary>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 5)]
    [InlineData(4, 8)]
    [InlineData(250, 254)]
    public void An_enemy_takes_its_index_above_the_pets(int index, byte expected)
    {
        CombatActor.Enemy(index).ShouldBe(expected);
    }

    /// <summary>
    /// The ceiling fails loudly rather than wrapping into the sentinel: without this, summon 251
    /// would be logged as <see cref="CombatActor.None"/> and the replayer would draw its actions as
    /// happening to nobody.
    /// </summary>
    [Theory]
    [InlineData(251)]
    [InlineData(252)]
    [InlineData(1000)]
    public void An_enemy_index_above_the_ceiling_is_refused(int index)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CombatActor.Enemy(index));
    }

    /// <summary>A negative enemy index is a bug too.</summary>
    [Fact]
    public void A_negative_enemy_index_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CombatActor.Enemy(-1));
    }

    /// <summary>
    /// The worst authored fight's standing population against the range: 1 hero + 3 pets + 5
    /// enemies, plus the most prolific boss summoner (2 shards every 10 s) over the 90 s cap.
    /// </summary>
    [Fact]
    public void The_worst_authored_fight_is_far_inside_the_range()
    {
        const int enemies = 5;
        const int summonsPerFiring = 2;
        var firings = CombatLog.MaxTicks / (10 * 20);      // PERIODIC 10 s at 20 ticks/second
        var highestIndex = enemies + (firings * summonsPerFiring) - 1;

        firings.ShouldBe(9);
        highestIndex.ShouldBe(22);
        Should.NotThrow(() => CombatActor.Enemy(highestIndex));
        CombatActor.Enemy(highestIndex).ShouldBeLessThan(CombatActor.MaxId);
    }
}
