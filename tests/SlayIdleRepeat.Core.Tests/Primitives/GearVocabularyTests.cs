using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// The gear vocabulary: the three closed enums an item is described in, and the two value types a
/// rolled item is built out of.
/// </summary>
/// <remarks>
/// All three enums are wire vocabularies — an equip command, a gear row and an analytics record all
/// carry a slot, a family and an axis — so the numeric values are pinned exactly and none of them is
/// zero. A <c>0</c> member would make <c>default(T)</c> a real member, and an uninitialised column
/// would read back as a weapon, a blade or a balanced set rather than as the hole it is.
/// </remarks>
public sealed class GearVocabularyTests
{
    /// <summary>Six slots, twenty-four families, four axes.</summary>
    /// <remarks>
    /// The arithmetic these three counts state is the whole roster: six slots times four families is
    /// twenty-four base items, and four axes is four sets with exactly one piece per slot each. A
    /// count alone would be satisfied by a renamed member, so each row carries an identity too.
    /// </remarks>
    [Fact]
    public void The_three_gear_vocabularies_carry_the_members_the_roster_is_counted_from()
    {
        Enum.GetValues<GearSlot>().Length.ShouldBe(6);
        Enum.GetValues<GearSlot>().ShouldContain(GearSlot.AMULET);

        Enum.GetValues<GearFamily>().Length.ShouldBe(
            24, "twenty-four base items across six slots is the design set's own arithmetic for its " +
                "hundred and twenty gear entries at five rarities.");
        Enum.GetValues<GearFamily>().ShouldContain(GearFamily.IDOL);

        Enum.GetValues<GearFamilyAxis>().Length.ShouldBe(
            4, "there are exactly four sets, one per axis, which is why an SS item's set is a lookup " +
               "from its family rather than a stored field.");
        Enum.GetValues<GearFamilyAxis>().ShouldContain(GearFamilyAxis.AGILE);
    }

    /// <summary>No member of any gear vocabulary carries the value zero.</summary>
    [Theory]
    [InlineData(typeof(GearSlot))]
    [InlineData(typeof(GearFamily))]
    [InlineData(typeof(GearFamilyAxis))]
    public void No_member_of_a_gear_vocabulary_is_zero(Type vocabulary)
    {
        Enum.GetValues(vocabulary).Cast<object>().Select(Convert.ToInt32).ShouldNotContain(
            0,
            $"a zero member makes default({vocabulary.Name}) a real one, so an uninitialised column " +
            "would load as a genuine value and nothing downstream could tell it from an authored one.");
    }

    /// <summary>The slot wire values, pinned exactly.</summary>
    [Theory]
    [InlineData(GearSlot.WEAPON, 1)]
    [InlineData(GearSlot.HELMET, 2)]
    [InlineData(GearSlot.ARMOR, 3)]
    [InlineData(GearSlot.BOOTS, 4)]
    [InlineData(GearSlot.RING, 5)]
    [InlineData(GearSlot.AMULET, 6)]
    public void A_slots_wire_value_is_the_one_it_ships_with(GearSlot slot, int wire)
    {
        ((int)slot).ShouldBe(
            wire,
            "these are wire values: an equip command, a gear row and an analytics record all carry " +
            "one. Append, never renumber — a renumbered member re-labels every stored row.");
    }

    /// <summary>The family wire values, pinned exactly, in slot order.</summary>
    [Theory]
    [InlineData(GearFamily.BLADE, 1)]
    [InlineData(GearFamily.BOW, 4)]
    [InlineData(GearFamily.HOOD, 5)]
    [InlineData(GearFamily.MASK, 8)]
    [InlineData(GearFamily.LEATHERS, 9)]
    [InlineData(GearFamily.SCALEMAIL, 12)]
    [InlineData(GearFamily.TREADS, 13)]
    [InlineData(GearFamily.SANDALS, 16)]
    [InlineData(GearFamily.BAND, 17)]
    [InlineData(GearFamily.SEAL, 20)]
    [InlineData(GearFamily.PENDANT, 21)]
    [InlineData(GearFamily.IDOL, 24)]
    public void A_familys_wire_value_is_the_one_it_ships_with(GearFamily family, int wire)
    {
        ((int)family).ShouldBe(wire);
    }

    /// <summary>The four families of a slot are contiguous, and the roster runs one to twenty-four.</summary>
    /// <remarks>
    /// The layout the wire values above spell out, stated once as a property. It is a layout and not
    /// a specification — the axis is authored per row in the catalogue rather than derived from this
    /// arithmetic — which is exactly why it is pinned here and nowhere else.
    /// </remarks>
    [Fact]
    public void The_family_roster_runs_one_to_twenty_four_with_no_gaps()
    {
        Enum.GetValues<GearFamily>().Select(family => (int)family).ShouldBe(
            Enumerable.Range(1, 24));
    }

    /// <summary>The axis wire values, pinned exactly.</summary>
    [Theory]
    [InlineData(GearFamilyAxis.BALANCED, 1)]
    [InlineData(GearFamilyAxis.HEAVY, 2)]
    [InlineData(GearFamilyAxis.CASTER, 3)]
    [InlineData(GearFamilyAxis.AGILE, 4)]
    public void An_axis_wire_value_is_the_one_it_ships_with(GearFamilyAxis axis, int wire)
    {
        ((int)axis).ShouldBe(wire);
    }

    /// <summary>The armour slot is spelled the American way, which is the design set's own.</summary>
    /// <remarks>
    /// The member name is the authored token: the gear catalogue parses <c>"ARMOR"</c> out of
    /// <c>content/gear/gear.json</c> by name and by case, so the British spelling would refuse the
    /// shipped file outright.
    /// </remarks>
    [Fact]
    public void The_armour_slot_is_spelled_the_american_way()
    {
        Enum.GetNames<GearSlot>().ShouldContain("ARMOR");
        Enum.GetNames<GearSlot>().ShouldNotContain("ARMOUR");
    }

    // ---------------------------------------------------------------- GearInstanceId

    /// <summary>An instance id carries the identifier text it was given.</summary>
    [Fact]
    public void An_instance_id_carries_its_identifier()
    {
        var id = new GearInstanceId("gi_0001");

        id.Value.ShouldBe("gi_0001");
        id.ToString().ShouldBe("gi_0001", "a log line reads the id rather than the record's shape");
    }

    /// <summary>A blank instance id names no item and is refused where it is constructed.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void An_instance_id_refuses_a_blank_identifier(string? blank)
    {
        Should.Throw<ArgumentException>(() => new GearInstanceId(blank!))
            .Message.ShouldMatchWildcard(
                "*GearInstanceId*",
                "the refusal must name the id type. Every id in this namespace guards the same way, " +
                "and a message that does not say which one threw sends the reader to the wrong seam.");
    }

    /// <summary>The default id has no constructor to run, and says so rather than rendering blank.</summary>
    [Fact]
    public void The_default_instance_id_renders_as_a_default_rather_than_as_nothing()
    {
        default(GearInstanceId).ToString().ShouldBe("default(GearInstanceId)");
    }

    // ---------------------------------------------------------------- GearAffixRoll

    /// <summary>A blank affix id is refused: an affix nothing can price, re-roll or display.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\r\n")]
    public void An_affix_roll_refuses_a_blank_affix_id(string? blank)
    {
        Should.Throw<ArgumentException>(() => new GearAffixRoll(blank!, 0.05))
            .Message.ShouldMatchWildcard("*GearAffixRoll*");
    }

    /// <summary>A magnitude that is not a finite, already-rounded number — or is negative zero — is refused.</summary>
    /// <remarks>
    /// The rounding arm is the one that matters most and is the least obvious: persisted state carries
    /// no unrounded double — the canonical writer refuses one — so an affix rounded on the way in here
    /// would hide whichever roll produced it. Round at the roll, not at the record.
    /// </remarks>
    [Theory]
    [InlineData(-0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.12345)]
    [InlineData(0.000012)]
    public void An_affix_roll_refuses_a_magnitude_that_is_not_a_rounded_number(double value)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => new GearAffixRoll("AFX_CRIT_CHANCE", value));

        thrown.ParamName.ShouldBe(nameof(GearAffixRoll.Value));
        thrown.Message.ShouldContain(
            "Round at the roll",
            Case.Sensitive,
            "several guards in Primitives raise ArgumentOutOfRangeException; only this one says where " +
            "the rounding belongs, which is the whole reason it refuses rather than rounds.");
    }

    /// <summary>An already-rounded magnitude is accepted — zero and, since the damage-reduction affix re-signed, negative rolls included.</summary>
    /// <remarks>The boundary control: the rule refuses unrounded values, not small or signed ones.</remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0001)]
    [InlineData(-0.0001)]
    [InlineData(-0.08)]
    [InlineData(0.1234)]
    [InlineData(1.0)]
    [InlineData(350.0)]
    public void An_affix_roll_accepts_an_already_rounded_magnitude(double value)
    {
        new GearAffixRoll("AFX_CRIT_DAMAGE", value).Value.ShouldBe(value);
    }

    /// <summary>An affix roll renders the same text under any culture.</summary>
    /// <remarks>
    /// A magnitude of <c>0.0642</c> renders as <c>0,0642</c> under <c>sv-SE</c>, which would put a
    /// comma inside a log line and inside any text assembled from one.
    /// </remarks>
    [Fact]
    public void An_affix_roll_renders_the_same_text_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");
        var roll = new GearAffixRoll("AFX_CRIT_CHANCE", 0.0642);

        0.0642.ToString(swedish).ShouldNotBe(
            0.0642.ToString(CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture. Under " +
            "globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

        Render(roll.ToString, swedish).ShouldBe("AFX_CRIT_CHANCE=0.0642");
        Render(roll.ToString, CultureInfo.InvariantCulture).ShouldBe("AFX_CRIT_CHANCE=0.0642");
    }

    /// <summary>The default roll has no constructor to run, and says so rather than throwing.</summary>
    [Fact]
    public void The_default_affix_roll_renders_as_a_default_rather_than_throwing()
    {
        default(GearAffixRoll).ToString().ShouldBe("default(GearAffixRoll)");
    }

    private static string Render(Func<string> render, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
