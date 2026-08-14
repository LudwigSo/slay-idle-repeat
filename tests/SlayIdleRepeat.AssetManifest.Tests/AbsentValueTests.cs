using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The population of values the design docs authorise nothing for.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why these are absences and not JSON nulls.</b> `game-data/README.md` says a hole should be
/// <c>null</c> and greppable, and in <c>tuning/</c> that is exactly right: each of those 96 nulls is
/// a missing <em>number</em> somebody could quietly fill with a plausible zero, and M0-09's
/// <c>RealDataNegativeCaseTests</c> pins the population — total and per file — so that filling one
/// is a deliberate act. But that guard counts nulls across the <b>whole snapshot</b>, and
/// <c>game-data/assets/*.json</c> is in the snapshot. A 974-row register carrying "§C states no
/// pivot for a 9-slice panel" as a null would have added roughly 3,500 to a population of 96 and
/// destroyed the guard's meaning.
/// </para>
/// <para>
/// So the register omits the member instead, and this file supplies the same discipline scoped to
/// the register: every absence population is pinned, so filling one — or opening a new one — fails
/// here and has to be argued for. The reader still surfaces an absent member as <c>null</c>, so the
/// C# side of S6 is unchanged.
/// </para>
/// </remarks>
public sealed class AbsentValueTests
{
    /// <summary>
    /// 🔒 The register contributes ZERO JSON nulls to the ContentSnapshot. If this ever fails,
    /// M0-09's 96-hole population test fails with it, and its per-file breakdown stops meaning
    /// anything.
    /// </summary>
    [Theory]
    [InlineData("assets/asset_manifest_art.json")]
    [InlineData("assets/asset_manifest_audio.json")]
    public void The_register_adds_no_JSON_nulls_to_the_content_snapshot(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(ManifestFiles.DataRoot, relativePath));

        using var document = System.Text.Json.JsonDocument.Parse(json);

        CountNulls(document.RootElement).ShouldBe(0,
            "game-data/assets is inside the ContentSnapshot, and " +
            "RealDataNegativeCaseTests.The_shipped_data_set_still_carries_exactly_its_96_unauthorised_holes " +
            "pins the snapshot-wide null population at 96 tuning holes. A categorical absence in " +
            "this register is a different thing and is encoded by omitting the member.");
    }

    /// <summary>The art absences, by field. Each number is a deliberate, argued population.</summary>
    [Fact]
    public void The_art_register_carries_exactly_its_known_absences()
    {
        var art = ManifestFiles.Shipped.Art.Assets;

        // subject — E10 24 parallax layers · E11 60 boots/ring/amulet · E17 83 · E19 32 · E20 49 · E21 13
        art.Count(a => a.Subject is null).ShouldBe(261);

        // deliverySize — E10 4 scene backgrounds · E17 86 (§C: "variable, 9-slice") · E20 49 · E21 5
        art.Count(a => a.DeliverySize is null).ShouldBe(144);

        // pivot — E9 112 · E10 28 · E16 11 · E17 86 · E19 32 · E21 15. §C names only two classes.
        art.Count(a => a.Pivot is null).ShouldBe(284);

        // atlas — E8 14 (DSC_TILE_ATLAS) · E10 28 ("not atlased") · E20 49 · E21 15
        art.Count(a => a.Atlas is null).ShouldBe(106);

        art.Count(a => a.Biome is null).ShouldBe(646);
        art.Count(a => a.Cut is null).ShouldBe(942, "everything the O8 ruling did not cut");
    }

    /// <summary>Biome and palette are absent together, always — one without the other is a defect.</summary>
    [Fact]
    public void Biome_and_palette_are_present_or_absent_together()
    {
        var art = ManifestFiles.Shipped.Art.Assets;

        art.Count(a => a.Biome is not null).ShouldBe(328);
        art.ShouldAllBe(a => (a.Biome == null) == (a.PaletteColours == null));
    }

    /// <summary>The audio absences, by field.</summary>
    [Fact]
    public void The_audio_register_carries_exactly_its_known_absences()
    {
        var audio = ManifestFiles.Shipped.Audio.Assets;

        audio.Count(a => a.Descriptor is null).ShouldBe(8,
            "doc 20 attaches no descriptor of their own to these eight SFX");
        audio.Count(a => a.Use is null).ShouldBe(94, "20 §4 gives SFX no Use column");
        audio.Count(a => a.LengthSeconds is null).ShouldBe(94, "loop length is a music property");
        audio.Count(a => a.DurationSeconds is null).ShouldBe(98,
            "12 music tracks plus the 86 SFX whose descriptor states no duration");
        audio.Count(a => a.GroupDescriptor is null).ShouldBe(106, "nobody has ruled one yet");
        audio.Count(a => a.DucksMusic is null).ShouldBe(12, "20 §5's ducking row is about SFX");
    }

    /// <summary>
    /// 🔒 An absent member and a member the reader forgot must not look the same. Every row still
    /// declares the five fields that are always meaningful, and dropping one is a read-time failure
    /// (proved by ManifestValidatorTests).
    /// </summary>
    [Fact]
    public void Every_row_declares_the_members_that_are_never_optional()
    {
        var art = ManifestFiles.Shipped.Art.Assets;

        art.ShouldNotBeEmpty();
        art.ShouldAllBe(a => a.Id.Length > 0);
        art.ShouldAllBe(a => a.Section.Length > 0);
        art.ShouldAllBe(a => a.SourceSection.Length > 0);
        art.ShouldAllBe(a => a.IdSource == "doc" || a.IdSource == "convention");

        var audio = ManifestFiles.Shipped.Audio.Assets;

        audio.ShouldNotBeEmpty();
        audio.ShouldAllBe(a => a.Id.Length > 0);
        audio.ShouldAllBe(a => a.Kind == "mus" || a.Kind == "sfx");
        audio.ShouldAllBe(a => a.Family.Length > 0);
        audio.ShouldAllBe(a => a.Format.Length > 0);
    }

    private static int CountNulls(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null => 1,
        System.Text.Json.JsonValueKind.Array => value.EnumerateArray().Sum(CountNulls),
        System.Text.Json.JsonValueKind.Object => value.EnumerateObject().Sum(p => CountNulls(p.Value)),
        _ => 0,
    };
}
