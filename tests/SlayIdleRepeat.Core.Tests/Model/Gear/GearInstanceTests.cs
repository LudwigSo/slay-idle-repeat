using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// The validated minting door every item goes through — roller and rehydrate both — and the
/// hand-written equality <c>Loadout</c> and the merge rules compare items with.
/// </summary>
public sealed class GearInstanceTests
{
    // ---------------------------------------------------------------- the guards

    /// <remarks>
    /// The constructor is called directly rather than through this file's builder: the builder
    /// substitutes an empty list for a missing one, which is exactly the reading the guard forbids.
    /// </remarks>
    [Fact]
    public void A_null_affix_list_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new GearInstance(
                new GearInstanceId("gi_0001"),
                "GEAR_WEAPON_BLADE",
                GearSlot.WEAPON,
                GearFamily.BLADE,
                Rarity.A,
                1,
                0.5,
                0,
                0,
                null!,
                locked: false))
            .ParamName.ShouldBe("affixes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void A_slot_outside_the_six_is_refused(int slot)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(slot: (GearSlot)slot));

        thrown.ParamName.ShouldBe("slot");
        thrown.Message.ShouldContain(
            "six equipment slots",
            Case.Sensitive,
            "three enum guards in this constructor raise the same type; the message is what says " +
            "which vocabulary the caller missed.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void A_family_outside_the_twenty_four_is_refused(int family)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(family: (GearFamily)family));

        thrown.ParamName.ShouldBe("family");
        thrown.Message.ShouldContain("twenty-four item families", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_rarity_off_the_ladder_is_refused(int rarity)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(rarity: (Rarity)rarity));

        thrown.ParamName.ShouldBe("rarity");
        thrown.Message.ShouldContain("rarity on the ladder", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_chapter_of_origin_below_the_first_chapter_is_refused(int chapter)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(chapterOrigin: chapter))
            .ParamName.ShouldBe("chapterOrigin");
    }

    [Fact]
    public void A_negative_enhancement_level_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(enhanceLevel: -1))
            .ParamName.ShouldBe("enhanceLevel");
    }

    [Fact]
    public void A_negative_failure_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(enhanceFailures: -1))
            .ParamName.ShouldBe("enhanceFailures");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_base_item_id_is_refused(string? blank)
    {
        var thrown = Should.Throw<ArgumentException>(() => Item(defId: blank!));

        thrown.ParamName.ShouldBe("defId");
        thrown.Message.ShouldMatchWildcard("*GearInstance*");
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    [InlineData(2.0)]
    [InlineData(0.12345)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_quality_outside_the_range_or_not_already_rounded_is_refused(double quality)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(quality: quality));

        thrown.ParamName.ShouldBe("quality");
        thrown.Message.ShouldContain("already rounded", Case.Sensitive);
    }

    /// <remarks>The boundary control: the guard refuses values outside the range, not values on it.</remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0001)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void A_quality_on_either_end_of_the_range_is_accepted(double quality)
    {
        Item(quality: quality).Quality.ShouldBe(quality);
    }

    /// <remarks>
    /// A repeated affix would double one stat and read to a player as a single unusually strong roll.
    /// </remarks>
    [Fact]
    public void The_same_affix_rolled_twice_onto_one_item_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => Item(affixes:
        [
            new GearAffixRoll("AFX_PEN", 0.05),
            new GearAffixRoll("AFX_DODGE", 0.03),
            new GearAffixRoll("AFX_PEN", 0.07),
        ]));

        thrown.ParamName.ShouldBe("affixes");
        thrown.Message.ShouldContain("AFX_PEN", Case.Sensitive);
        thrown.Message.ShouldContain(
            "without replacement",
            Case.Sensitive,
            "which rule fired: this constructor also raises ArgumentException for a blank defId, and " +
            "the two have entirely different fixes.");
    }

    /// <remarks>The negative control: the rule is one id per item, not one magnitude per item.</remarks>
    [Fact]
    public void Two_different_affixes_at_the_same_magnitude_are_not_a_duplicate()
    {
        Item(affixes:
        [
            new GearAffixRoll("AFX_PEN", 0.05),
            new GearAffixRoll("AFX_DODGE", 0.05),
        ]).Affixes.Count.ShouldBe(2);
    }

    // ---------------------------------------------------------------- equality

    /// <remarks>
    /// The case a synthesized record <c>Equals</c> fails: it compares the affix list by reference.
    /// </remarks>
    [Fact]
    public void Two_items_with_equal_affixes_in_different_lists_are_equal_and_hash_equal()
    {
        var first = Item(affixes:
            [new GearAffixRoll("AFX_PEN", 0.0812), new GearAffixRoll("AFX_DODGE", 0.0411)]);
        var second = Item(affixes:
            [new GearAffixRoll("AFX_PEN", 0.0812), new GearAffixRoll("AFX_DODGE", 0.0411)]);

        ReferenceEquals(first.Affixes, second.Affixes).ShouldBeFalse(
            "the two lists have to be genuinely different instances, or this case proves nothing");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(
            second.GetHashCode(),
            "a hash that ignored the affixes would still be consistent with an equality that reads " +
            "them, but two items differing only in affixes would then collide on every lookup.");
    }

    /// <remarks>Order is preserved rather than normalised: it is the order they were drawn in.</remarks>
    [Fact]
    public void Two_items_whose_affixes_differ_only_in_order_are_not_equal()
    {
        var first = Item(affixes:
            [new GearAffixRoll("AFX_PEN", 0.0812), new GearAffixRoll("AFX_DODGE", 0.0411)]);
        var second = Item(affixes:
            [new GearAffixRoll("AFX_DODGE", 0.0411), new GearAffixRoll("AFX_PEN", 0.0812)]);

        first.ShouldNotBe(second);
    }

    /// <remarks>
    /// Member by member: a hand-written <c>Equals</c> that forgot a line would still answer true
    /// for two identical items and false for two wholly different ones.
    /// </remarks>
    [Fact]
    public void Every_member_takes_part_in_equality()
    {
        var item = Item(affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]);

        item.ShouldNotBe(Item(instanceId: new GearInstanceId("gi_other"),
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(defId: "GEAR_WEAPON_AXE",
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(slot: GearSlot.RING,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(family: GearFamily.AXE,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(rarity: Rarity.S,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(chapterOrigin: 5,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(quality: 0.9,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(enhanceLevel: 4,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(enhanceFailures: 4,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(locked: true,
            affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]));
        item.ShouldNotBe(Item(affixes: [new GearAffixRoll("AFX_PEN", 0.0813)]));
        item.ShouldNotBe(Item());
    }

    // ---------------------------------------------------------------- the affix copy

    [Fact]
    public void Mutating_the_callers_array_afterwards_does_not_change_the_item()
    {
        var supplied = new[] { new GearAffixRoll("AFX_PEN", 0.0812) };
        var item = Item(affixes: supplied);

        supplied[0] = new GearAffixRoll("AFX_DODGE", 0.5);

        item.Affixes[0].AffixId.ShouldBe(
            "AFX_PEN",
            "a rolled item is immutable. Holding the caller's own list behind a read-only interface " +
            "would let an item's affixes change after it was minted, and the copy is what stops that.");
    }

    [Fact]
    public void The_affix_list_cannot_be_cast_back_to_a_writable_array()
    {
        var item = Item(affixes: [new GearAffixRoll("AFX_PEN", 0.0812)]);

        (item.Affixes as GearAffixRoll[]).ShouldBeNull(
            "an IReadOnlyList<T> that IS a T[] casts straight back and is writable through, so the " +
            "copy has to be wrapped rather than merely made.");

        var writable = item.Affixes as IList<GearAffixRoll>;

        writable.ShouldNotBeNull("the wrapper does implement IList<T> — it is read-only, not absent");
        writable.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => writable[0] = new GearAffixRoll("AFX_DODGE", 0.5));
    }

    /// <summary>A valid item, with any one member moved.</summary>
    private static GearInstance Item(
        GearInstanceId? instanceId = null,
        string? defId = "GEAR_WEAPON_BLADE",
        GearSlot slot = GearSlot.WEAPON,
        GearFamily family = GearFamily.BLADE,
        Rarity rarity = Rarity.A,
        int chapterOrigin = 1,
        double quality = 0.5,
        int enhanceLevel = 0,
        int enhanceFailures = 0,
        IReadOnlyList<GearAffixRoll>? affixes = null,
        bool locked = false) =>
        new(
            instanceId ?? new GearInstanceId("gi_0001"),
            defId!,
            slot,
            family,
            rarity,
            chapterOrigin,
            quality,
            enhanceLevel,
            enhanceFailures,
            affixes ?? [],
            locked);
}
