using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>The gear-grant event: the item it refuses to go without, and the text it renders.</summary>
public sealed class GearGrantedTests
{
    [Fact]
    public void A_gear_grant_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new GearGranted(7, Item(), SourceClass.CHEST_PREMIUM, FromPity: false);

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the " +
            "same ordinal while the derived record still read back correctly.");
    }

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

    /// <summary>
    /// A quality of <c>0.7321</c> and an affix magnitude of <c>0.1103</c> both render with a decimal
    /// comma under <c>sv-SE</c>, and this event's text reaches the economy log and analytics.
    /// </summary>
    [Fact]
    public void ToString_renders_the_ordinal_first_and_the_same_text_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");
        var granted = new GearGranted(2, Item(), SourceClass.DROP_RUN, FromPity: true);

        0.7321.ToString(swedish).ShouldNotBe(
            0.7321.ToString(CultureInfo.InvariantCulture),
            "under globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the " +
            "invariant culture, and the comparison below would then hold over nothing.");

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
