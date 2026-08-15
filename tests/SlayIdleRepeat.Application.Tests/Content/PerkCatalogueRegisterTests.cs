using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// Checks the register at <c>.claude/handovers/M3-07-unauthored-perks-register.json</c>, which
/// records which standard-catalogue perk ids are unauthored.
/// </summary>
/// <remarks>
/// The 82-id catalogue is transcribed by hand below rather than derived from shipped content — a
/// list derived from the content would trivially say the catalogue is complete. Every catalogue id
/// must be in exactly one of "authored in perks.json" or "registered as unauthored" — never
/// neither, never both — and neither file may name an id outside the catalogue.
/// </remarks>
public sealed class PerkCatalogueRegisterTests
{
    private const string RegisterRelativePath = ".claude/handovers/M3-07-unauthored-perks-register.json";

    /// <summary>
    /// The 82-id catalogue, hand-transcribed: Offense 22, Defense 18, Sustain 12, Dice &amp; Board 4
    /// (the category's other 8 rows live elsewhere), Economy 10, Trigger/Synergy 16.
    /// </summary>
    private static readonly string[] Catalogue =
    [
        // Offense (22)
        "PK_SHARP_EDGE", "PK_QUICK_HANDS", "PK_KEEN_EYE", "PK_HEAVY_SWING", "PK_PIERCING",
        "PK_BRUTALITY", "PK_EXECUTIONER", "PK_FLURRY", "PK_OVERPOWER", "PK_CRIT_CASCADE",
        "PK_RUPTURE", "PK_IGNITE", "PK_GIANT_SLAYER", "PK_MOMENTUM_ATK", "PK_CLEAVE",
        "PK_DEATHMARK", "PK_BERSERK", "PK_TWIN_STRIKE", "PK_SUNDERING", "PK_APEX",
        "PK_ANNIHILATE", "PK_CHAIN_DEATH",

        // Defense (18)
        "PK_TOUGH_HIDE", "PK_IRON_SKIN", "PK_NIMBLE", "PK_BULWARK", "PK_STOIC", "PK_THORNS",
        "PK_SECOND_SKIN", "PK_WARDED", "PK_EVASIVE", "PK_STALWART", "PK_ANCHOR", "PK_REACTIVE",
        "PK_IMMOVABLE", "PK_AEGIS", "PK_LAST_STAND", "PK_MIRROR", "PK_UNBREAKABLE", "PK_FORTRESS",

        // Sustain (12)
        "PK_LEECH", "PK_REGEN", "PK_VITAL_SURGE", "PK_BLOODLETTER", "PK_FEAST", "PK_HEALERS_TOUCH",
        "PK_SANGUINE", "PK_RESTORATION", "PK_UNDYING", "PK_TRANSFUSION", "PK_PHOENIX", "PK_ETERNAL",

        // Dice & Board (4 — the other 8 of the 12 live elsewhere)
        "PK_PATHFINDER", "PK_SCOUT", "PK_LEAPFROG", "PK_CARTOGRAPHER",

        // Economy (10)
        "PK_GREED", "PK_HAGGLER", "PK_SCAVENGER", "PK_LUCKY_FIND", "PK_PROSPECTOR",
        "PK_MERCHANT_FRIEND", "PK_BOUNTY", "PK_ALCHEMY", "PK_MIDAS", "PK_HOARD",

        // Trigger/Synergy (16)
        "PK_GLASS", "PK_TURTLE", "PK_JUGGERNAUT", "PK_DUELIST", "PK_SWARMBANE", "PK_OPENER",
        "PK_CLOSER", "PK_PACK_LEADER", "PK_SYMBIOSIS", "PK_ECHO", "PK_MOMENTUM_CH", "PK_GAMBLER",
        "PK_ARSENAL", "PK_PERFECTIONIST", "PK_AVATAR", "PK_SINGULARITY",
    ];

    private static string[] AuthoredIds()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();
        var root = snapshot.GetDocument("content/perks/perks.json").Root;

        root.TryGetMember("perks", out var rows).ShouldBeTrue();

        return rows!.Items
            .Select(r => { r.TryGetMember("id", out var id); return id!.AsText("id"); })
            .ToArray();
    }

    private static string[] RegisteredUnauthoredIds()
    {
        var path = Path.Combine(RepoData.RepositoryRoot, RegisterRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).ShouldBeTrue($"the register M3-07's task charter requires must exist at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("unauthoredForM3_07b").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToArray();
    }

    /// <summary>Floor: the transcription itself has not been trimmed.</summary>
    [Fact]
    public void The_catalogue_transcription_holds_82_ids_across_6_categories()
    {
        Catalogue.Length.ShouldBe(82, "06 §3.1-3.6: 22+18+12+4+10+16");
        Catalogue.Distinct(StringComparer.Ordinal).Count().ShouldBe(82, "no id transcribed twice");
    }

    /// <summary>The register's own count matches the file it authors.</summary>
    [Fact]
    public void The_register_names_36_unauthored_ids()
    {
        RegisteredUnauthoredIds().Length.ShouldBe(36);
    }

    /// <summary>perks.json ships exactly the rows M3-07's report claims.</summary>
    [Fact]
    public void Perks_json_ships_46_authored_ids()
    {
        AuthoredIds().Length.ShouldBe(46);
    }

    /// <summary>Direction 1 (undeclared): every catalogue id is authored or registered; none falls through both.</summary>
    [Fact]
    public void Every_catalogue_id_is_either_authored_or_registered_as_unauthored()
    {
        var authored = AuthoredIds().ToHashSet(StringComparer.Ordinal);
        var unauthored = RegisteredUnauthoredIds().ToHashSet(StringComparer.Ordinal);

        var neither = Catalogue.Where(id => !authored.Contains(id) && !unauthored.Contains(id)).ToArray();

        neither.ShouldBeEmpty(
            "every 06 §3 id must be authored in perks.json or named in the M3-07 register — " +
            $"found in neither: {string.Join(", ", neither)}");
    }

    /// <summary>Direction 2 (double-claimed): no id is both authored and registered as unauthored — that would be two disagreeing claims about the same row.</summary>
    [Fact]
    public void No_id_is_both_authored_and_registered_as_unauthored()
    {
        var authored = AuthoredIds().ToHashSet(StringComparer.Ordinal);
        var unauthored = RegisteredUnauthoredIds().ToHashSet(StringComparer.Ordinal);

        var both = authored.Intersect(unauthored, StringComparer.Ordinal).ToArray();

        both.ShouldBeEmpty($"claimed by both perks.json and the register: {string.Join(", ", both)}");
    }

    /// <summary>No stray: every authored id is really in the catalogue, never invented.</summary>
    [Fact]
    public void Every_authored_id_is_drawn_from_the_06_section_3_catalogue()
    {
        var catalogue = Catalogue.ToHashSet(StringComparer.Ordinal);
        var strays = AuthoredIds().Where(id => !catalogue.Contains(id)).ToArray();

        strays.ShouldBeEmpty($"authored but not in 06 §3.1-3.6: {string.Join(", ", strays)}");
    }

    /// <summary>No stray in the register either — it must not name an id nobody asked about.</summary>
    [Fact]
    public void Every_registered_id_is_drawn_from_the_06_section_3_catalogue()
    {
        var catalogue = Catalogue.ToHashSet(StringComparer.Ordinal);
        var strays = RegisteredUnauthoredIds().Where(id => !catalogue.Contains(id)).ToArray();

        strays.ShouldBeEmpty($"registered but not in 06 §3.1-3.6: {string.Join(", ", strays)}");
    }

    /// <summary>
    /// Self-expiry for <c>ContentInvariants.KnownForwardPerkReferences</c>: every entry must be
    /// both still referenced from <c>tuning/calibration_builds.json</c> and still unauthored, or it
    /// has gone stale (nothing else fails on it automatically).
    /// </summary>
    [Fact]
    public void Every_known_forward_perk_reference_is_still_genuinely_unresolved()
    {
        var authored = AuthoredIds().ToHashSet(StringComparer.Ordinal);
        var stillOrphaned = ContentInvariants.KnownForwardPerkReferences
            .Where(id => authored.Contains(id))
            .ToArray();

        stillOrphaned.ShouldBeEmpty(
            $"authored in perks.json but still listed as a known forward reference: {string.Join(", ", stillOrphaned)} " +
            "— remove the entry from ContentInvariants.KnownForwardPerkReferences now that it resolves.");

        // Every entry must also still be a real draftPriority reference in calibration_builds.json,
        // not a name that was edited out from under the exemption (which would make the exemption
        // fire for nothing while masking a genuine new orphan at the same id).
        var calibrationBuilds = RepoData.Documents["tuning/calibration_builds.json"];
        var unreferenced = ContentInvariants.KnownForwardPerkReferences
            .Where(id => !calibrationBuilds.Contains($"\"{id}\"", StringComparison.Ordinal))
            .ToArray();

        unreferenced.ShouldBeEmpty(
            $"no longer referenced by tuning/calibration_builds.json: {string.Join(", ", unreferenced)} " +
            "— remove the entry from ContentInvariants.KnownForwardPerkReferences.");
    }

    /// <summary>Exact union: authored + registered accounts for the whole 82-row catalogue.</summary>
    [Fact]
    public void Authored_plus_registered_equals_the_whole_catalogue_with_no_duplicate_claims()
    {
        var union = AuthoredIds().Concat(RegisteredUnauthoredIds()).ToArray();

        union.Length.ShouldBe(82, "46 authored + 36 registered = 82");
        union.Distinct(StringComparer.Ordinal).Count().ShouldBe(82, "no id claimed twice across the two files");
        union.ToHashSet(StringComparer.Ordinal).ShouldBe(Catalogue.ToHashSet(StringComparer.Ordinal), ignoreOrder: true);
    }
}
