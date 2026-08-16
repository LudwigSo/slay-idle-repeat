using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content.Gear;

/// <summary>One of the twenty-four base items: which slot it fills, its family, and that family's axis.</summary>
/// <param name="DefId">The authored id, e.g. <c>GEAR_WEAPON_BLADE</c>.</param>
/// <param name="Slot">The slot it is worn in.</param>
/// <param name="Family">Its family.</param>
/// <param name="Axis">
/// The family axis — the stat bias it shares with one family of every other slot, and the identity of
/// the set an SS copy of it belongs to.
/// </param>
internal readonly record struct GearDefinition(
    string DefId, GearSlot Slot, GearFamily Family, GearFamilyAxis Axis);

/// <summary>
/// The twenty-four base items, read out of <c>content/gear/gear.json</c>: the slot/family grid and
/// the family axis that decides an SS item's set.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is what makes <c>setId</c> derivable rather than stored. There are exactly four sets,
/// one per axis, so an item's set is a lookup from its family — and a lookup is cheaper to keep
/// correct than a duplicated field.
/// </para>
/// <para>
/// It refuses a grid that is not complete: six slots, four families each, every family used once and
/// every (slot, axis) pair filled exactly once. A missing row would silently make one family
/// unrollable, and a duplicated axis inside a slot would give one set two pieces in that slot and
/// another none — neither is visible in any single row, so the reader checks the shape rather than
/// the rows.
/// </para>
/// </remarks>
internal sealed class GearCatalogue
{
    /// <summary>The document the base items live in.</summary>
    internal const string DocumentPath = "content/gear/gear.json";

    /// <summary>The array the six slot blocks are authored under.</summary>
    internal const string ItemsReference = DocumentPath + "#/slots";

    /// <summary>The member each slot block holds its four families under.</summary>
    internal const string FamiliesMember = "families";

    /// <summary>Six slots times four families.</summary>
    internal const int BaseItemCount = 24;

    /// <summary>How many families each slot carries.</summary>
    internal const int FamiliesPerSlot = 4;

    private readonly IReadOnlyDictionary<GearFamily, GearDefinition> _byFamily;

    private GearCatalogue(
        IReadOnlyList<GearDefinition> definitions,
        IReadOnlyDictionary<GearFamily, GearDefinition> byFamily)
    {
        Definitions = definitions;
        _byFamily = byFamily;
    }

    /// <summary>The twenty-four rows, in the order the document lists them.</summary>
    internal IReadOnlyList<GearDefinition> Definitions { get; }

    /// <summary>The base item for one family.</summary>
    /// <param name="family">The family to look up.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="InvalidTunableException">The catalogue authors no row for this family.</exception>
    internal GearDefinition Definition(GearFamily family) =>
        _byFamily.TryGetValue(family, out var definition)
            ? definition
            : throw new InvalidTunableException(
                ItemsReference,
                $"The catalogue authors no base item for {family}. Every family is one of the " +
                "twenty-four rows; a family with no row is an item nothing can roll and a set piece " +
                "nothing can complete.");

    /// <summary>The four families of one slot, in the document's order.</summary>
    /// <param name="slot">The slot to look up.</param>
    /// <returns>Its families.</returns>
    internal IReadOnlyList<GearDefinition> Families(GearSlot slot)
    {
        var families = new List<GearDefinition>(FamiliesPerSlot);

        foreach (var definition in Definitions)
        {
            if (definition.Slot == slot)
            {
                families.Add(definition);
            }
        }

        return families;
    }

    /// <summary>Reads the base-item catalogue. Throws rather than defaulting on anything missing.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A token is not a declared member, or the grid is incomplete.</exception>
    internal static GearCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var slots = content.Read(ItemsReference);
        var slotCount = Enum.GetValues<GearSlot>().Length;

        if (slots.Kind != ContentValueKind.Array || slots.Items.Count != slotCount)
        {
            throw new InvalidTunableException(
                ItemsReference,
                $"The base-item catalogue is exactly {AuthoredToken.Render(slotCount)} slot blocks — " +
                "six slots with four families each, which is the roster every rarity band is a copy " +
                "of. This document authors " + slots + ".");
        }

        var definitions = new List<GearDefinition>(BaseItemCount);
        var byFamily = new Dictionary<GearFamily, GearDefinition>(BaseItemCount);

        for (var s = 0; s < slots.Items.Count; s++)
        {
            var slotPointer = ItemsReference + "/" + AuthoredToken.Render(s);
            var slot = AuthoredToken.Parse<GearSlot>(
                content, slotPointer + "/slot", "an equipment slot");

            var familiesReference = slotPointer + "/" + FamiliesMember;
            var families = content.Read(familiesReference);

            if (families.Kind != ContentValueKind.Array || families.Items.Count != FamiliesPerSlot)
            {
                throw new InvalidTunableException(
                    familiesReference,
                    $"{slot} authors {families.Items.Count} families, not " +
                    $"{AuthoredToken.Render(FamiliesPerSlot)}. The slot/family grid is what makes a " +
                    "set six pieces: a slot short of a family leaves one set unable to reach its " +
                    "six-piece bonus at all.");
            }

            for (var f = 0; f < families.Items.Count; f++)
            {
                var pointer = familiesReference + "/" + AuthoredToken.Render(f);

                var definition = new GearDefinition(
                    content.ReadText(pointer + "/id"),
                    slot,
                    AuthoredToken.Parse<GearFamily>(content, pointer + "/family", "an item family"),
                    AuthoredToken.Parse<GearFamilyAxis>(content, pointer + "/familyAxis", "a family axis"));

                if (!byFamily.TryAdd(definition.Family, definition))
                {
                    throw new InvalidTunableException(
                        pointer + "/family",
                        $"{definition.Family} is authored twice. A family names one base item, so a " +
                        "duplicate row would make one family unreachable and leave the item it " +
                        "displaced with no way to be rolled at all.");
                }

                definitions.Add(definition);
            }
        }

        var catalogue = new GearCatalogue(definitions.AsReadOnly(), byFamily);

        RequireCompleteGrid(catalogue);

        return catalogue;
    }

    /// <summary>
    /// Every slot carries four families, one per axis. Checked as a shape rather than per row,
    /// because neither failure is visible in any single row.
    /// </summary>
    private static void RequireCompleteGrid(GearCatalogue catalogue)
    {
        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            var families = catalogue.Families(slot);

            if (families.Count != FamiliesPerSlot)
            {
                throw new InvalidTunableException(
                    ItemsReference,
                    $"{slot} carries {AuthoredToken.Render(families.Count)} families, not " +
                    $"{AuthoredToken.Render(FamiliesPerSlot)}. The slot/family grid is what makes a set six " +
                    "pieces: a slot short of a family leaves one set unable to reach its six-piece " +
                    "bonus at all.");
            }

            var axes = new HashSet<GearFamilyAxis>();

            foreach (var definition in families)
            {
                if (!axes.Add(definition.Axis))
                {
                    throw new InvalidTunableException(
                        ItemsReference,
                        $"{slot} authors the {definition.Axis} axis twice. Each axis is one set, and " +
                        "a slot with two families on one axis gives that set two pieces in the slot " +
                        "while another set has none.");
                }
            }
        }
    }
}
