using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// The gear-grant event: the item it carries whole, the source it came from, whether a guarantee
/// forced it, and the rendering four downstream consumers read off the same list.
/// </summary>
/// <remarks>
/// It carries the item rather than an id, which is the one narrow place an event may name a
/// <c>Model/</c> type. Analytics projects a row from it, the economy log appends one, Feats count off
/// it and the client replays it as an animation — an id would send every one of them back to an
/// aggregate whose state has since moved on.
/// </remarks>
public sealed class GearGrantedTests
{
    /// <summary>The event carries its ordinal, the item, the source and the pity flag.</summary>
    [Fact]
    public void A_gear_grant_carries_the_item_the_source_and_whether_pity_forced_it()
    {
        var item = Item();
        var granted = new GearGranted(3, item, SourceClass.DROP_RUN, FromPity: true);

        granted.Sequence.ShouldBe(3);
        granted.Item.ShouldBe(item);
        granted.Source.ShouldBe(SourceClass.DROP_RUN);
        granted.FromPity.ShouldBeTrue();
    }

    /// <summary>
    /// The pity flag is reported rather than inferred from the rarity, so a natural draw that landed
    /// on the guaranteed band is still a different event.
    /// </summary>
    /// <remarks>
    /// The two are different events to a player, to the analytics stream and to duplicate protection.
    /// Two grants that differ only in the flag are two rows, and nothing may collapse them.
    /// </remarks>
    [Fact]
    public void Two_grants_of_the_same_item_differing_only_in_the_pity_flag_are_different_events()
    {
        var item = Item();

        new GearGranted(1, item, SourceClass.DROP_RUN, FromPity: true)
            .ShouldNotBe(new GearGranted(1, item, SourceClass.DROP_RUN, FromPity: false));
    }

    /// <summary>It reaches its consumers as a <c>DomainEvent</c>, ordered by the base's ordinal.</summary>
    [Fact]
    public void A_gear_grant_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new GearGranted(7, Item(), SourceClass.CHEST_PREMIUM, FromPity: false);

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the " +
            "same ordinal while the derived record still read back correctly.");

        typeof(GearGranted).GetProperty(nameof(DomainEvent.Sequence))!.DeclaringType.ShouldBe(
            typeof(DomainEvent),
            "consumers read Sequence off DomainEvent; a redeclared one on the subtype would shadow it");
    }

    /// <summary>A grant with no item is refused rather than emitted.</summary>
    [Fact]
    public void A_gear_grant_with_no_item_is_refused()
    {
        var thrown = Should.Throw<ArgumentNullException>(
            () => new GearGranted(1, null!, SourceClass.DROP_RUN, FromPity: false));

        thrown.ParamName.ShouldBe(nameof(GearGranted.Item));
        thrown.Message.ShouldContain(
            "attribute or animate",
            Case.Sensitive,
            "the economy log, the analytics projection, the Feat counters and the client's replay all " +
            "read the item out of this event; the refusal says so rather than merely reporting a null.");
    }

    /// <summary>A copy carries the validated item rather than re-opening the slot.</summary>
    /// <remarks>
    /// What a get-only property buys over the positional <c>init</c> one: an <c>init</c> accessor is
    /// assignable through <c>with</c> without re-running the property initialiser.
    /// </remarks>
    [Fact]
    public void A_copy_carries_the_validated_item()
    {
        var item = Item();
        var granted = new GearGranted(1, item, SourceClass.DROP_RUN, FromPity: false);

        (granted with { Sequence = 9 }).Item.ShouldBe(item);
        (granted with { FromPity = true }).Item.ShouldBe(item);
    }

    /// <summary>The whole rendering: the ordinal first, then the item, the source and the flag.</summary>
    /// <remarks>
    /// A quality of <c>0.7321</c> and an affix magnitude of <c>0.1103</c> both render with a decimal
    /// comma under <c>sv-SE</c>, and this event's text reaches the economy log and the analytics
    /// stream — where a comma is a number the reader parses differently.
    /// </remarks>
    [Fact]
    public void ToString_renders_the_ordinal_first_and_the_same_text_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");
        var granted = new GearGranted(2, Item(), SourceClass.DROP_RUN, FromPity: true);

        0.7321.ToString(swedish).ShouldNotBe(
            0.7321.ToString(CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture. Under " +
            "globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

        Render(granted, swedish).ShouldBe(Render(granted, CultureInfo.InvariantCulture));
        Render(granted, CultureInfo.InvariantCulture).ShouldBe(
            "GearGranted { Sequence = 2, Item = GearInstance { InstanceId = gi_0001, " +
            "DefId = GEAR_WEAPON_BLADE, Slot = WEAPON, Family = BLADE, Rarity = A, ChapterOrigin = 1, " +
            "Quality = 0.7321, EnhanceLevel = 0, EnhanceFailures = 0, Locked = False, " +
            "Affixes = [AFX_PEN=0.1103] }, Source = DROP_RUN, FromPity = True }");
    }

    private static string Render(GearGranted granted, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return granted.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static GearInstance Item() => new(
        new GearInstanceId("gi_0001"),
        "GEAR_WEAPON_BLADE",
        GearSlot.WEAPON,
        GearFamily.BLADE,
        Rarity.A,
        1,
        0.7321,
        0,
        0,
        [new GearAffixRoll("AFX_PEN", 0.1103)],
        locked: false);
}
