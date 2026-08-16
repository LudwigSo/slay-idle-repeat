using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Gear;

/// <summary>
/// One rolled gear item: the eleven things it carries, the eleven ways it refuses to be built, and
/// the hand-written equality that a synthesized record <c>Equals</c> would get wrong.
/// </summary>
/// <remarks>
/// The equality is the load-bearing part. A synthesized record compares an
/// <c>IReadOnlyList&lt;T&gt;</c> component <em>by reference</em>, so two items that rolled the same
/// affixes would be unequal unless they happened to share the very same list — and every comparison
/// of an item against its merge inputs, its reforge candidate or its stored self would answer
/// "different" for a reason nobody could see.
/// </remarks>
public sealed class GearInstanceTests
{
    /// <summary>Every member reads back as it was given.</summary>
    [Fact]
    public void A_rolled_item_carries_everything_it_was_built_from()
    {
        var affixes = new[]
        {
            new GearAffixRoll("AFX_CRIT_CHANCE", 0.0642),
            new GearAffixRoll("AFX_PEN", 0.1103),
        };

        var item = new GearInstance(
            new GearInstanceId("gi_0001"),
            "GEAR_WEAPON_BLADE",
            GearSlot.WEAPON,
            GearFamily.BLADE,
            Rarity.A,
            3,
            0.7321,
            2,
            1,
            affixes,
            locked: true);

        item.InstanceId.ShouldBe(new GearInstanceId("gi_0001"));
        item.DefId.ShouldBe("GEAR_WEAPON_BLADE");
        item.Slot.ShouldBe(GearSlot.WEAPON);
        item.Family.ShouldBe(GearFamily.BLADE);
        item.Rarity.ShouldBe(Rarity.A);
        item.ChapterOrigin.ShouldBe(3);
        item.Quality.ShouldBe(0.7321);
        item.EnhanceLevel.ShouldBe(2);
        item.EnhanceFailures.ShouldBe(1);
        item.Affixes.ShouldBe(affixes);
        item.Locked.ShouldBeTrue();
    }

    /// <summary>A freshly rolled item carries no enhancement, no failures and no lock.</summary>
    [Fact]
    public void A_freshly_rolled_item_carries_no_enhancement_and_no_lock()
    {
        var item = Item();

        item.EnhanceLevel.ShouldBe(0);
        item.EnhanceFailures.ShouldBe(
            0,
            "the mercy counter lives on the item rather than on the player precisely so it cannot be " +
            "farmed on a cheap item and spent on an expensive one, and a fresh roll has nothing to " +
            "carry over.");
        item.Locked.ShouldBeFalse();
        item.Affixes.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- the guards

    /// <summary>A null affix list is refused rather than read as no affixes.</summary>
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

    /// <summary>A slot outside the six is refused.</summary>
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

    /// <summary>A family outside the twenty-four is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void A_family_outside_the_twenty_four_is_refused(int family)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(family: (GearFamily)family));

        thrown.ParamName.ShouldBe("family");
        thrown.Message.ShouldContain("twenty-four item families", Case.Sensitive);
    }

    /// <summary>A rarity off the ladder is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void A_rarity_off_the_ladder_is_refused(int rarity)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => Item(rarity: (Rarity)rarity));

        thrown.ParamName.ShouldBe("rarity");
        thrown.Message.ShouldContain("rarity on the ladder", Case.Sensitive);
    }

    /// <summary>A chapter of origin below the first chapter is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_chapter_of_origin_below_the_first_chapter_is_refused(int chapter)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(chapterOrigin: chapter))
            .ParamName.ShouldBe("chapterOrigin");
    }

    /// <summary>A negative enhancement level is refused.</summary>
    [Fact]
    public void A_negative_enhancement_level_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(enhanceLevel: -1))
            .ParamName.ShouldBe("enhanceLevel");
    }

    /// <summary>A negative failure count is refused.</summary>
    [Fact]
    public void A_negative_failure_count_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Item(enhanceFailures: -1))
            .ParamName.ShouldBe("enhanceFailures");
    }

    /// <summary>A blank base-item id is refused: an item that names no base item.</summary>
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

    /// <summary>A quality outside <c>[0, 1]</c> or not already rounded is refused.</summary>
    /// <remarks>
    /// The rounding arm is the one a caller trips without noticing: the UI shows the scalar directly
    /// as a percentage and the canonical state writer refuses an unrounded double, so an item minted
    /// from a raw draw would be unpersistable and would display a quality bar nobody chose.
    /// </remarks>
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

    /// <summary>Both ends of the quality range are accepted.</summary>
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

    /// <summary>The same affix rolled twice onto one item is refused.</summary>
    /// <remarks>
    /// Refused here rather than in the roller because an item carrying the same affix twice would
    /// double one stat and read to a player as a single unusually strong roll — the roller drawing
    /// without replacement is what makes this refusal unreachable in practice, not a substitute for it.
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

    /// <summary>Two affixes that differ only in value are not a duplicate.</summary>
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

    /// <summary>
    /// Two items built from two different lists holding equal affixes are equal, and hash equal.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the case a synthesized record <c>Equals</c> fails. It compares the affix list by
    /// reference, so these two — identical in every member — would be unequal, and the hash would
    /// disagree too.
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

    /// <summary>Affixes in a different order are a different item.</summary>
    /// <remarks>
    /// Order is preserved rather than normalised: it is the order they were drawn in, and a re-tune
    /// that locks "the first two" needs it to mean something stable.
    /// </remarks>
    [Fact]
    public void Two_items_whose_affixes_differ_only_in_order_are_not_equal()
    {
        var first = Item(affixes:
            [new GearAffixRoll("AFX_PEN", 0.0812), new GearAffixRoll("AFX_DODGE", 0.0411)]);
        var second = Item(affixes:
            [new GearAffixRoll("AFX_DODGE", 0.0411), new GearAffixRoll("AFX_PEN", 0.0812)]);

        first.ShouldNotBe(second);
    }

    /// <summary>Every member takes part in equality, one at a time.</summary>
    /// <remarks>
    /// Member by member rather than in one lump: a hand-written <c>Equals</c> that simply forgot a
    /// line would still answer true for two identical items and false for two wholly different ones.
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

    /// <summary>An item is never equal to null.</summary>
    [Fact]
    public void An_item_is_never_equal_to_null()
    {
        Item().Equals(null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- the affix copy

    /// <summary>Mutating the caller's array afterwards does not change the item.</summary>
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

    /// <summary>The affix list cannot be cast back to the array underneath it.</summary>
    /// <remarks>
    /// The other half of the copy: a copy handed out as a bare <c>T[]</c> casts straight back and is
    /// writable through, which is the same hole with one more step.
    /// </remarks>
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

    // ---------------------------------------------------------------- rendering

    /// <summary>The whole rendering, under an arbitrary culture as well as the invariant one.</summary>
    /// <remarks>
    /// A quality of <c>0.7321</c> renders as <c>0,7321</c> under <c>sv-SE</c>. The rendering reaches
    /// diagnostics and failure messages on both sides of the wire, and a decimal comma in one of them
    /// is a number the other side reads differently.
    /// </remarks>
    [Fact]
    public void ToString_renders_the_same_text_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");
        var item = Item(quality: 0.7321, affixes: [new GearAffixRoll("AFX_PEN", 0.1103)]);

        0.7321.ToString(swedish).ShouldNotBe(
            0.7321.ToString(CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture. Under " +
            "globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

        Render(item, swedish).ShouldBe(Render(item, CultureInfo.InvariantCulture));
        Render(item, CultureInfo.InvariantCulture).ShouldBe(
            "GearInstance { InstanceId = gi_0001, DefId = GEAR_WEAPON_BLADE, Slot = WEAPON, " +
            "Family = BLADE, Rarity = A, ChapterOrigin = 1, Quality = 0.7321, EnhanceLevel = 0, " +
            "EnhanceFailures = 0, Locked = False, Affixes = [AFX_PEN=0.1103] }");
    }

    private static string Render(GearInstance item, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return item.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
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
