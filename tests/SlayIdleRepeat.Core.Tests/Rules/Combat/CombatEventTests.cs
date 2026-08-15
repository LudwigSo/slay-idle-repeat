using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The event shape, asserted as the wire contract it is.</summary>
/// <remarks>
/// Everything here would be a triviality if <see cref="CombatEvent"/> were an implementation
/// detail. It is not: it is replayed, hashed to detect tampering, and hashed cross-platform. A
/// change to any of these is a change to every <c>LogHash</c> in existence.
/// </remarks>
public sealed class CombatEventTests
{
    /// <summary>The six documented fields, in order, at their widths.</summary>
    /// <remarks>
    /// Asserted through <c>CanonicalFieldOrder</c> — the writer's own traversal — rather than
    /// through reflection's <c>GetProperties()</c>, which does not guarantee order; the primary
    /// constructor's parameter order does.
    /// </remarks>
    [Fact]
    public void The_event_carries_exactly_the_six_documented_fields_in_order()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(CombatEvent)).ShouldBe(
        [
            "Tick:System.Int32",
            "Type:SlayIdleRepeat.Core.Rules.Combat.CombatEventType",
            "SourceId:System.Byte",
            "TargetId:System.Byte",
            "Value:System.Double",
            "DataId:System.UInt16",
        ]);
    }

    /// <summary>
    /// The event is a shape the one serialiser can actually see — the reason
    /// <see cref="CombatEvent"/> is a positional record rather than a struct with public fields,
    /// which hashes to nothing.
    /// </summary>
    [Fact]
    public void The_event_is_a_canonical_record()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(CombatEvent)).ShouldBeTrue();
    }

    /// <summary>
    /// No public instance field on the event — the shape that would hash as zero bytes. Asserted
    /// directly as well as through <c>IsCanonicalRecord</c> above, since a failure there could point
    /// the reader at the wrong hazard (e.g. a second constructor).
    /// </summary>
    [Fact]
    public void The_event_declares_no_public_field()
    {
        typeof(CombatEvent)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name)
            .ShouldBeEmpty(
                "a public field is neither a primary-constructor parameter nor a property, so it " +
                "contributes ZERO BYTES to LogHash — 05 §7 declares CombatEvent that way and it is why " +
                "this type is a positional record struct instead");
    }

    /// <summary>
    /// <see cref="CombatEvent.Value"/> is a <see cref="double"/>: a <c>float</c> here would be
    /// unhashable. See <see cref="CombatLogPrecisionTests"/> for what it would cost even if it were not.
    /// </summary>
    [Fact]
    public void The_value_field_is_a_double()
    {
        typeof(CombatEvent).GetProperty(nameof(CombatEvent.Value))!.PropertyType.ShouldBe(typeof(double));
    }

    /// <summary>
    /// Every <see cref="CombatEventType"/> ordinal, written out. The ordinal is what is hashed, so
    /// this table is the wire contract. <c>Telegraph</c> is appended last precisely so none of the
    /// original seventeen moves. Driven by <c>nameof</c> so a rename is still a compile failure.
    /// </summary>
    [Theory]
    [InlineData(nameof(CombatEventType.BattleStart), 0)]
    [InlineData(nameof(CombatEventType.Attack), 1)]
    [InlineData(nameof(CombatEventType.Hit), 2)]
    [InlineData(nameof(CombatEventType.Crit), 3)]
    [InlineData(nameof(CombatEventType.Miss), 4)]
    [InlineData(nameof(CombatEventType.Block), 5)]
    [InlineData(nameof(CombatEventType.Heal), 6)]
    [InlineData(nameof(CombatEventType.Shield), 7)]
    [InlineData(nameof(CombatEventType.StatusApplied), 8)]
    [InlineData(nameof(CombatEventType.StatusExpired), 9)]
    [InlineData(nameof(CombatEventType.StatusTick), 10)]
    [InlineData(nameof(CombatEventType.PetAbility), 11)]
    [InlineData(nameof(CombatEventType.WardBroken), 12)]
    [InlineData(nameof(CombatEventType.RunEffectQueued), 13)]
    [InlineData(nameof(CombatEventType.ActorDeath), 14)]
    [InlineData(nameof(CombatEventType.PhaseChange), 15)]
    [InlineData(nameof(CombatEventType.BattleEnd), 16)]
    [InlineData(nameof(CombatEventType.Telegraph), 17)]
    public void Every_event_type_holds_its_documented_ordinal(string member, int ordinal)
    {
        ((int)Enum.Parse<CombatEventType>(member, ignoreCase: false)).ShouldBe(ordinal,
            "the ordinal is what LogHash hashes — moving one silently rewrites every hash already " +
            "issued, including those stored against duels in flight (11 §6)");
    }

    /// <summary>
    /// The enum has exactly the eighteen members above and no nineteenth that slipped in unpinned:
    /// <see cref="Every_event_type_holds_its_documented_ordinal"/> is a theory over a written-out
    /// list, so a member added to the enum and not added there is a member nothing checks.
    /// </summary>
    [Fact]
    public void The_event_vocabulary_is_exactly_the_documented_eighteen()
    {
        Enum.GetValues<CombatEventType>().Length.ShouldBe(18,
            "17 from 05 §7 plus Telegraph (17 §1, §11). A new member must be APPENDED, given the next " +
            "ordinal, added to Every_event_type_holds_its_documented_ordinal, and regenerated into the " +
            "every-event-type reference row");
    }

    /// <summary>
    /// The original seventeen members occupy <c>0..16</c> contiguously, and every addition is above
    /// them — the rule an author is most likely to break by instinct, e.g. alphabetising the enum
    /// or slotting a new member in where it reads better.
    /// </summary>
    [Fact]
    public void Nothing_was_inserted_below_the_documented_members()
    {
        var documented = Enum.GetValues<CombatEventType>()
            .Where(type => (int)type <= (int)CombatEventType.BattleEnd)
            .ToArray();

        documented.Length.ShouldBe(17);
        ((int)CombatEventType.BattleEnd).ShouldBe(16);
        Enum.GetValues<CombatEventType>().Select(t => (int)t).ShouldBe(Enumerable.Range(0, 18));
    }

    /// <summary>An event is a value: two with the same six fields are the same event.</summary>
    /// <remarks>
    /// Record equality is what lets a replay test compare logs, and what makes a
    /// <c>LogHash</c> collision between two <i>unequal</i> logs the real failure rather than a
    /// curiosity.
    /// </remarks>
    [Fact]
    public void Two_events_with_the_same_fields_are_equal()
    {
        var left = new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, 41.2536, 0);
        var right = new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, CombatActor.FirstEnemy, 41.2536, 0);

        right.ShouldBe(left);
        right.GetHashCode().ShouldBe(left.GetHashCode());
    }

    /// <summary>Changing any one field changes the event — none of the six is decorative.</summary>
    [Fact]
    public void Every_field_participates_in_equality()
    {
        var baseline = new CombatEvent(3, CombatEventType.Hit, 1, 2, 4.0, 5);

        baseline.ShouldNotBe(baseline with { Tick = 4 });
        baseline.ShouldNotBe(baseline with { Type = CombatEventType.Crit });
        baseline.ShouldNotBe(baseline with { SourceId = 9 });
        baseline.ShouldNotBe(baseline with { TargetId = 9 });
        baseline.ShouldNotBe(baseline with { Value = 4.5 });
        baseline.ShouldNotBe(baseline with { DataId = 9 });
    }

    /// <summary>
    /// Changing any one field changes the hash — the stronger claim a tamper check relies on: a
    /// tampered log must not be able to differ from the honest one in a field the encoding cannot
    /// see. Stated field by field rather than as one "the hash is sensitive" smoke test.
    /// </summary>
    [Fact]
    public void Every_field_participates_in_the_hash()
    {
        var baseline = new CombatEvent(3, CombatEventType.Hit, 1, 2, 4.0, 5);
        var mutations = new[]
        {
            baseline with { Tick = 4 },
            baseline with { Type = CombatEventType.Crit },
            baseline with { SourceId = 9 },
            baseline with { TargetId = 9 },
            baseline with { Value = 4.5 },
            baseline with { DataId = 9 },
        };

        var hashes = mutations
            .Select(m => CanonicalStateWriter.HashCombatLog(new[] { m }))
            .Append(CanonicalStateWriter.HashCombatLog(new[] { baseline }))
            .ToArray();

        hashes.Length.ShouldBe(7);
        hashes.ShouldBeUnique(
            "a field that does not move the hash is a field an attacker can edit for free (11 §6), and a " +
            "field the cross-platform determinism gate cannot see diverge (M5-12)");
    }
}
