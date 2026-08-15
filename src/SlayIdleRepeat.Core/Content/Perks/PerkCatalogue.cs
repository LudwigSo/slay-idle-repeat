using System.Globalization;

namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>The standard drafted perk catalogue, read out of <c>content/perks/perks.json</c>.</summary>
/// <remarks>
/// Read per command out of the command's own <c>ContentSnapshot</c>, never cached statically: the
/// snapshot is versioned per command, so a static cache would serve one command's content to another.
/// </remarks>
public sealed class PerkCatalogue
{
    /// <summary>The document the catalogue is transcribed into.</summary>
    public const string DocumentPath = "content/perks/perks.json";

    /// <summary>The authored perk rows.</summary>
    public const string PerksReference = DocumentPath + "#/perks";

    private readonly IReadOnlyDictionary<string, PerkCatalogueEntry> _byId;

    private PerkCatalogue(IReadOnlyList<PerkCatalogueEntry> all, IReadOnlyDictionary<string, PerkCatalogueEntry> byId)
    {
        All = all;
        _byId = byId;
    }

    /// <summary>Every authored perk, in the document's order.</summary>
    public IReadOnlyList<PerkCatalogueEntry> All { get; }

    /// <summary>The perk with this id.</summary>
    /// <exception cref="ArgumentException">No perk carries this id.</exception>
    public PerkCatalogueEntry Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (_byId.TryGetValue(id, out var perk))
        {
            return perk;
        }

        throw new ArgumentException(
            "06 §3 authors no perk '" + id + "'. A perk id reaching here that the catalogue does " +
            "not carry means a Run persisted an owned-perk id this content version no longer has — " +
            "a content rollback across a live run, not a player input.",
            nameof(id));
    }

    /// <summary>Whether <paramref name="id"/> is an authored perk.</summary>
    public bool Contains(string id) => id is not null && _byId.ContainsKey(id);

    /// <summary>Every perk of one rarity band, in the document's order.</summary>
    public IReadOnlyList<PerkCatalogueEntry> OfRarity(PerkRarity rarity)
    {
        var matches = new List<PerkCatalogueEntry>(All.Count);

        foreach (var perk in All)
        {
            if (perk.Rarity == rarity)
            {
                matches.Add(perk);
            }
        }

        return matches;
    }

    /// <summary>Reads the perk catalogue. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    public static PerkCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var array = content.Read(PerksReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                PerksReference,
                "06 §3 authors a non-empty perk list. This document authors " + array + ".");
        }

        var perks = new PerkCatalogueEntry[array.Items.Count];
        var byId = new Dictionary<string, PerkCatalogueEntry>(array.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var perk = ReadPerk(content, PerksReference + "/" + Text(i));

            if (!byId.TryAdd(perk.Id, perk))
            {
                throw new InvalidTunableException(
                    PerksReference + "/" + Text(i) + "/id",
                    "'" + perk.Id + "' is authored twice. 14 §6 makes a duplicate id a build " +
                    "failure: the second row would be drawn twice as often as every other and the " +
                    "first would be unreachable by id.");
            }

            perks[i] = perk;
        }

        return new PerkCatalogue(Array.AsReadOnly(perks), byId);
    }

    private static PerkCatalogueEntry ReadPerk(ContentSnapshot content, string pointer)
    {
        var id = RequiredText(content, pointer, "id");
        var name = RequiredText(content, pointer, "name");
        var category = ParseCategory(content.ReadText(pointer + "/category"), pointer + "/category");
        var rarity = ParseRarity(content.ReadText(pointer + "/rarity"), pointer + "/rarity");
        var iconId = RequiredText(content, pointer, "iconId");
        var description = RequiredText(content, pointer, "description");

        var tiers = content.Read(pointer + "/tiers");
        if (tiers.Kind != ContentValueKind.Array || tiers.Items.Count == 0)
        {
            throw new InvalidTunableException(
                pointer + "/tiers",
                "'" + id + "' authors no tiers. 06 §1.1 fixes at least one (Tier I, the base " +
                "effect) for every perk.");
        }

        return new PerkCatalogueEntry
        {
            Id = id,
            Name = name,
            Category = category,
            Rarity = rarity,
            IconId = iconId,
            Description = description,
            TierCount = tiers.Items.Count,
            Excludes = ReadStringArray(content, pointer + "/excludes"),
            Requires = ReadStringArray(content, pointer + "/requires"),
            PoolTags = ReadStringArray(content, pointer + "/poolTags"),
        };
    }

    private static IReadOnlyList<string> ReadStringArray(ContentSnapshot content, string pointer)
    {
        if (!content.IsAuthorised(pointer))
        {
            return [];
        }

        var array = content.Read(pointer);
        if (array.Kind != ContentValueKind.Array)
        {
            throw new ContentTypeMismatchException(pointer, array.Kind, nameof(ContentValueKind.Array));
        }

        var values = new string[array.Items.Count];
        for (var i = 0; i < array.Items.Count; i++)
        {
            values[i] = array.Items[i].AsText(pointer + "/" + Text(i));
        }

        return Array.AsReadOnly(values);
    }

    private static PerkCategory ParseCategory(string token, string pointer) => token switch
    {
        "OFFENSE" => PerkCategory.Offense,
        "DEFENSE" => PerkCategory.Defense,
        "SUSTAIN" => PerkCategory.Sustain,
        "DICE_AND_BOARD" => PerkCategory.DiceAndBoard,
        "ECONOMY" => PerkCategory.Economy,
        "TRIGGER_SYNERGY" => PerkCategory.TriggerSynergy,
        _ => throw new InvalidTunableException(
            pointer,
            "'" + token + "' is not one of 06 §2's six standard categories (OFFENSE, DEFENSE, " +
            "SUSTAIN, DICE_AND_BOARD, ECONOMY, TRIGGER_SYNERGY)."),
    };

    private static PerkRarity ParseRarity(string token, string pointer) => token switch
    {
        "COMMON" => PerkRarity.Common,
        "RARE" => PerkRarity.Rare,
        "EPIC" => PerkRarity.Epic,
        "LEGENDARY" => PerkRarity.Legendary,
        _ => throw new InvalidTunableException(
            pointer,
            "'" + token + "' is not one of 06 §4's four rarity bands (COMMON, RARE, EPIC, LEGENDARY)."),
    };

    private static string RequiredText(ContentSnapshot content, string pointer, string name)
    {
        var reference = pointer + "/" + name;
        var text = content.ReadText(reference);

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidTunableException(reference, "'" + name + "' must not be blank.")
            : text;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
