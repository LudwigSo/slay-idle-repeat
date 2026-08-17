using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The twenty-four base items read out of <c>content/gear/gear.json</c>: the slot/family grid, the
/// axis that decides an SS item's set, and the three ways an incomplete grid is refused.
/// </summary>
/// <remarks>
/// The grid is the thing, not the rows. Six slots times four families is what makes a set exactly six
/// pieces and what makes every family rollable, and neither property is visible in any single row —
/// so the reader checks the shape, and so does this.
/// </remarks>
public sealed class GearCatalogueTests
{
    /// <summary>The whole roster is present, and it is the roster the document lists.</summary>
    /// <remarks>
    /// The count alone would be satisfied by twenty-four copies of one row, so an identity comes with
    /// it: the ids are asserted in document order.
    /// </remarks>
    [Fact]
    public void The_catalogue_answers_the_twenty_four_base_items_the_document_lists()
    {
        var definitions = GearCatalogue.Read(GearDocuments.Shipped).Definitions;

        definitions.Count.ShouldBe(
            GearDocuments.ShippedBaseItems.Count,
            "six slots with four families each is the roster every rarity band is a copy of. A short " +
            "grid makes one family unrollable and leaves one set unable to reach six pieces.");
        definitions.Select(definition => definition.DefId).ShouldBe(
            GearDocuments.ShippedBaseItems.Select(item => item.DefId));
    }

    /// <summary>Every family appears exactly once across the whole grid.</summary>
    /// <remarks>
    /// A family names one base item. A family used twice would make one item unreachable and leave
    /// the item it displaced with no way to be rolled at all — invisible in a count of twenty-four.
    /// </remarks>
    [Fact]
    public void Every_family_appears_exactly_once_across_the_grid()
    {
        var families = GearCatalogue.Read(GearDocuments.Shipped).Definitions
            .Select(definition => definition.Family)
            .ToArray();

        families.Length.ShouldBe(Enum.GetValues<GearFamily>().Length);
        families.ShouldBeUnique();
        families.ShouldContain(GearFamily.BLADE);
        families.ShouldContain(GearFamily.IDOL);
    }

    /// <summary>Every slot carries four families, one per axis.</summary>
    /// <remarks>
    /// The axis half is what a count cannot catch: a slot with two BALANCED families gives that set
    /// two pieces in the slot while another set has none, and the slot still carries four.
    /// </remarks>
    [Theory]
    [InlineData(GearSlot.WEAPON)]
    [InlineData(GearSlot.HELMET)]
    [InlineData(GearSlot.ARMOR)]
    [InlineData(GearSlot.BOOTS)]
    [InlineData(GearSlot.RING)]
    [InlineData(GearSlot.AMULET)]
    public void Every_slot_carries_four_families_one_per_axis(GearSlot slot)
    {
        var families = GearCatalogue.Read(GearDocuments.Shipped).Families(slot);

        families.Count.ShouldBe(GearCatalogue.FamiliesPerSlot);
        families.Select(definition => definition.Axis).ShouldBe(
            Enum.GetValues<GearFamilyAxis>(),
            ignoreOrder: true,
            "each axis is one set, and a set reaches six pieces only if every slot offers it exactly " +
            "one family.");
        families.ShouldAllBe(definition => definition.Slot == slot);
    }

    /// <summary>The axis a known family carries is the one the document authors for it.</summary>
    /// <remarks>
    /// Four families are four apart in the enum's declaration order, so an axis <em>derived</em> from
    /// that arithmetic would agree with every row here. It is authored per row precisely because a
    /// layout coincidence is not a specification, and these are the rows that say so.
    /// </remarks>
    [Theory]
    [InlineData(GearFamily.BLADE, GearFamilyAxis.BALANCED)]
    [InlineData(GearFamily.AXE, GearFamilyAxis.HEAVY)]
    [InlineData(GearFamily.STAFF, GearFamilyAxis.CASTER)]
    [InlineData(GearFamily.BOW, GearFamilyAxis.AGILE)]
    [InlineData(GearFamily.PLATE, GearFamilyAxis.HEAVY)]
    [InlineData(GearFamily.SLIPPERS, GearFamilyAxis.CASTER)]
    [InlineData(GearFamily.IDOL, GearFamilyAxis.AGILE)]
    public void A_familys_axis_is_the_one_the_document_authors(GearFamily family, GearFamilyAxis axis)
    {
        GearCatalogue.Read(GearDocuments.Shipped).Definition(family).Axis.ShouldBe(axis);
    }

    /// <summary>Looking a family up answers its own row, slot, id and axis together.</summary>
    [Fact]
    public void Definition_round_trips_a_family_to_its_own_row()
    {
        var catalogue = GearCatalogue.Read(GearDocuments.Shipped);

        foreach (var item in GearDocuments.ShippedBaseItems)
        {
            var family = Enum.Parse<GearFamily>(item.Family);
            var definition = catalogue.Definition(family);

            definition.Family.ShouldBe(family);
            definition.DefId.ShouldBe(item.DefId);
            definition.Slot.ShouldBe(Enum.Parse<GearSlot>(item.Slot));
            definition.Axis.ShouldBe(Enum.Parse<GearFamilyAxis>(item.Axis));
        }
    }

    /// <summary>An id is read from the document rather than derived from the family's name.</summary>
    /// <remarks>
    /// The negative control on the round trip: a reader that composed <c>GEAR_{slot}_{family}</c>
    /// would agree with every shipped row and would silently ignore a re-authored id.
    /// </remarks>
    [Fact]
    public void The_reader_answers_the_base_item_id_the_document_authors()
    {
        var slots = ContentValue.Array(
            GearDocuments.ShippedBaseItems
                .Select(item => item.Slot)
                .Distinct(StringComparer.Ordinal)
                .Select(slot => GearDocuments.SlotBlock(
                    slot,
                    GearDocuments.ShippedBaseItems
                        .Where(item => string.Equals(item.Slot, slot, StringComparison.Ordinal))
                        .Select(item => GearDocuments.FamilyRow(
                            item.Family == "BLADE" ? "GEAR_RENAMED_BLADE" : item.DefId,
                            item.Family,
                            item.Axis))
                        .ToArray())));

        var catalogue = GearCatalogue.Read(GearDocuments.With(slots: slots));

        catalogue.Definition(GearFamily.BLADE).DefId.ShouldBe("GEAR_RENAMED_BLADE");
        catalogue.Definition(GearFamily.AXE).DefId.ShouldBe(
            "GEAR_WEAPON_AXE", "re-authoring one id must leave its neighbours where the document put them");
    }

    /// <summary>A family authored twice is refused, naming the family and the pointer.</summary>
    [Fact]
    public void A_duplicated_family_is_refused()
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => GearCatalogue.Read(GearDocuments.With(slots: SlotsWithWeaponFamilies(
                GearDocuments.FamilyRow("GEAR_WEAPON_BLADE", "BLADE", "BALANCED"),
                GearDocuments.FamilyRow("GEAR_WEAPON_AXE", "BLADE", "HEAVY"),
                GearDocuments.FamilyRow("GEAR_WEAPON_STAFF", "STAFF", "CASTER"),
                GearDocuments.FamilyRow("GEAR_WEAPON_BOW", "BOW", "AGILE")))));

        thrown.Message.ShouldContain(
            nameof(GearFamily.BLADE),
            Case.Sensitive,
            "this reader raises InvalidTunableException for a duplicated family, a short slot block, " +
            "a wrong slot count and a repeated axis. The message has to say which rule fired.");
        thrown.Reference.ShouldStartWith(GearCatalogue.ItemsReference, Case.Sensitive);
    }

    /// <summary>A slot block short of a family is refused before the grid is even assembled.</summary>
    [Fact]
    public void A_short_slot_block_is_refused()
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => GearCatalogue.Read(GearDocuments.With(slots: SlotsWithWeaponFamilies(
                GearDocuments.FamilyRow("GEAR_WEAPON_BLADE", "BLADE", "BALANCED"),
                GearDocuments.FamilyRow("GEAR_WEAPON_AXE", "AXE", "HEAVY"),
                GearDocuments.FamilyRow("GEAR_WEAPON_STAFF", "STAFF", "CASTER")))));

        thrown.Message.ShouldContain(
            nameof(GearSlot.WEAPON),
            Case.Sensitive,
            "which slot is short, not merely that some block was — the reader raises the same type " +
            "for four different shape failures.");
        thrown.Message.ShouldContain("six-piece", Case.Sensitive);
    }

    /// <summary>A grid with the wrong number of slot blocks is refused.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void A_grid_with_the_wrong_slot_count_is_refused(int blocks)
    {
        var shipped = GearDocuments.Slots();

        var slots = ContentValue.Array(blocks <= shipped.Items.Count
            ? shipped.Items.Take(blocks)
            : shipped.Items.Concat([shipped.Items[0]]));

        Should.Throw<InvalidTunableException>(() => GearCatalogue.Read(GearDocuments.With(slots: slots)))
            .Reference.ShouldBe(
                GearCatalogue.ItemsReference,
                "the refusal points at the array itself: a block count is a property of the whole " +
                "grid, not of any row in it.");
    }

    /// <summary>A slot that authors one axis twice is refused, even with four families.</summary>
    /// <remarks>
    /// The case a per-row check cannot see, and the reason the reader validates the assembled grid:
    /// every row here names a real family and a real axis, and the slot carries exactly four.
    /// </remarks>
    [Fact]
    public void A_slot_that_authors_one_axis_twice_is_refused()
    {
        var thrown = Should.Throw<InvalidTunableException>(
            () => GearCatalogue.Read(GearDocuments.With(slots: SlotsWithWeaponFamilies(
                GearDocuments.FamilyRow("GEAR_WEAPON_BLADE", "BLADE", "BALANCED"),
                GearDocuments.FamilyRow("GEAR_WEAPON_AXE", "AXE", "BALANCED"),
                GearDocuments.FamilyRow("GEAR_WEAPON_STAFF", "STAFF", "CASTER"),
                GearDocuments.FamilyRow("GEAR_WEAPON_BOW", "BOW", "AGILE")))));

        thrown.Message.ShouldContain(nameof(GearFamilyAxis.BALANCED), Case.Sensitive);
        thrown.Message.ShouldContain(
            "twice",
            Case.Sensitive,
            "a slot with two families on one axis gives that set two pieces in the slot while another " +
            "set has none — which no count of four would show.");
    }

    /// <summary>A token that is not exactly one member's name is refused.</summary>
    [Theory]
    [InlineData("blade")]
    [InlineData("1")]
    [InlineData(" BLADE")]
    public void A_family_token_that_is_not_exactly_a_member_name_is_refused(string authored)
    {
        Should.Throw<InvalidTunableException>(
                () => GearCatalogue.Read(GearDocuments.With(slots: SlotsWithWeaponFamilies(
                    GearDocuments.FamilyRow("GEAR_WEAPON_BLADE", authored, "BALANCED"),
                    GearDocuments.FamilyRow("GEAR_WEAPON_AXE", "AXE", "HEAVY"),
                    GearDocuments.FamilyRow("GEAR_WEAPON_STAFF", "STAFF", "CASTER"),
                    GearDocuments.FamilyRow("GEAR_WEAPON_BOW", "BOW", "AGILE")))))
            .Message.ShouldContain(authored, Case.Sensitive);
    }

    /// <summary>Asking for a family the catalogue has no row for is refused rather than answered.</summary>
    [Fact]
    public void Definition_refuses_a_family_the_catalogue_has_no_row_for()
    {
        Should.Throw<InvalidTunableException>(
                () => GearCatalogue.Read(GearDocuments.Shipped).Definition((GearFamily)99))
            .Message.ShouldContain(
                "99",
                Case.Sensitive,
                "a family with no row is an item nothing can roll and a set piece nothing can " +
                "complete; the refusal has to say which family the caller asked for.");
    }

    /// <summary>A missing document is a <c>MissingContentException</c>, not an empty catalogue.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_answering_an_empty_grid()
    {
        Should.Throw<MissingContentException>(
                () => GearCatalogue.Read(GearDocuments.Without(GearDocuments.GearDocumentPath)))
            .Reference.ShouldBe(GearCatalogue.DocumentPath);
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => GearCatalogue.Read(null!))
            .ParamName.ShouldBe("content");
    }

    /// <summary>The shipped grid with the weapon slot's four families replaced.</summary>
    private static ContentValue SlotsWithWeaponFamilies(params ContentValue[] families)
    {
        var shipped = GearDocuments.Slots();

        return ContentValue.Array(
            shipped.Items
                .Skip(1)
                .Prepend(GearDocuments.SlotBlock(nameof(GearSlot.WEAPON), families)));
    }
}
