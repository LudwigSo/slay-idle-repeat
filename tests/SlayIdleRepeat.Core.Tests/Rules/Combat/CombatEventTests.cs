using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §7's event shape, asserted as the wire contract it is.
/// </summary>
/// <remarks>
/// Everything here would be a triviality if <see cref="CombatEvent"/> were an implementation
/// detail. It is not: `05` §8 replays it, `11` §6 hashes it to detect tampering and M5-12 hashes it
/// on three architectures. A change to any of these is a change to every <c>LogHash</c> in
/// existence, so each one is pinned rather than left to review.
/// </remarks>
public sealed class CombatEventTests
{
    /// <summary>The six fields `05` §7 names, in `05` §7's order, at `05` §7's widths.</summary>
    /// <remarks>
    /// Asserted through <c>CanonicalFieldOrder</c> — the <b>writer's own</b> traversal — rather than
    /// through <c>typeof(CombatEvent).GetProperties()</c>, so this pins the bytes rather than a
    /// second opinion about them. Property order is not guaranteed by reflection at all; the
    /// primary constructor's parameter order is.
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
    /// 🔒 The event is a shape the one serialiser can actually see.
    /// </summary>
    /// <remarks>
    /// The converse of <see cref="CanonicalStateWriterTests"/>' refusal tests, and the reason
    /// <see cref="CombatEvent"/> departs from `05` §7's <c>public readonly struct</c> with public
    /// fields: that shape hashes to nothing.
    /// </remarks>
    [Fact]
    public void The_event_is_a_canonical_record()
    {
        CanonicalStateWriter.IsCanonicalRecord(typeof(CombatEvent)).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 No public instance field on the event — the shape that would hash as zero bytes.
    /// </summary>
    /// <remarks>
    /// Asserted directly as well as through <c>IsCanonicalRecord</c> above, because the two say
    /// different things. <c>IsCanonicalRecord</c> would also go false if someone added a second
    /// constructor, and a failure that says "not a canonical record" sends the reader looking at
    /// constructors. This one names the actual hazard (S2).
    /// </remarks>
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
    /// 🔒 <see cref="CombatEvent.Value"/> is a <see cref="double"/>. See the type's remarks: `14`
    /// §16.6 has no <c>float</c> row, so a <c>float</c> here would be unhashable, and
    /// <see cref="CombatLogPrecisionTests"/> shows what it would cost even if it were not.
    /// </summary>
    [Fact]
    public void The_value_field_is_a_double()
    {
        typeof(CombatEvent).GetProperty(nameof(CombatEvent.Value))!.PropertyType.ShouldBe(typeof(double));
    }

    /// <summary>
    /// 🔒 Every <see cref="CombatEventType"/> ordinal, written out. The ordinal is what is hashed, so
    /// this table <b>is</b> the wire contract.
    /// </summary>
    /// <remarks>
    /// The first seventeen are `05` §7's list in §7's order; <c>Telegraph</c> is an addition and is
    /// appended last precisely so none of the seventeen moves. Driven by <c>nameof</c> so a rename is
    /// still a <b>compile</b> failure.
    /// </remarks>
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
    /// The enum has exactly the eighteen members above and no nineteenth that slipped in unpinned.
    /// </summary>
    /// <remarks>
    /// S3: <see cref="Every_event_type_holds_its_documented_ordinal"/> is a theory over a written-out
    /// list, so a member added to the enum and not added there is a member nothing checks.
    /// </remarks>
    [Fact]
    public void The_event_vocabulary_is_exactly_the_documented_eighteen()
    {
        Enum.GetValues<CombatEventType>().Length.ShouldBe(18,
            "17 from 05 §7 plus Telegraph (17 §1, §11). A new member must be APPENDED, given the next " +
            "ordinal, added to Every_event_type_holds_its_documented_ordinal, and regenerated into the " +
            "every-event-type reference row");
    }

    /// <summary>
    /// 🔒 The seventeen `05` §7 members occupy <c>0..16</c> contiguously, and every addition is
    /// above them.
    /// </summary>
    /// <remarks>
    /// Stated as its own rule because it is the one an author is most likely to break by instinct:
    /// alphabetising the enum, or slotting <c>Telegraph</c> in beside <c>PhaseChange</c> where it
    /// reads better, both silently rewrite every <c>LogHash</c>.
    /// </remarks>
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
    /// 🔒 And changing any one field changes the <b>hash</b>. The stronger claim, and the one
    /// `11` §6 relies on: a tampered log must not be able to differ from the honest one in a field
    /// the encoding cannot see.
    /// </summary>
    /// <remarks>
    /// This is exactly the assertion a public-field <see cref="CombatEvent"/> would fail — silently,
    /// on four of the six fields — which is why it is stated field by field rather than as one
    /// "the hash is sensitive" smoke test.
    /// </remarks>
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
