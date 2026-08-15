using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>Where the manifest lives, and how the content pipeline sees it.</summary>
/// <remarks>
/// <c>game-data/assets/</c> is not outside the existing validator's reach: it falls through to the
/// stem pairing rule, which enforces a schema for every data file and vice versa. These cases pin
/// that pairing so a rename cannot quietly drop the manifest out of build-time validation.
/// </remarks>
public sealed class ManifestLayoutTests
{
    [Theory]
    [InlineData("asset_manifest_art.json")]
    [InlineData("asset_manifest_audio.json")]
    public void Each_manifest_file_is_where_the_loader_and_the_content_validator_both_look(string fileName)
    {
        File.Exists(Path.Combine(ManifestFiles.AssetsDirectory, fileName))
            .ShouldBeTrue($"game-data/assets/{fileName} is the register three M8 tasks read");
    }

    /// <summary>The stem rule: <c>assets/X.json</c> is governed by <c>schema/X.schema.json</c>; renaming one without the other fails the content-validation CI job.</summary>
    [Theory]
    [InlineData("asset_manifest_art")]
    [InlineData("asset_manifest_audio")]
    public void Each_manifest_pairs_with_a_schema_of_the_same_stem(string stem)
    {
        File.Exists(Path.Combine(ManifestFiles.AssetsDirectory, $"{stem}.json")).ShouldBeTrue();
        File.Exists(Path.Combine(ManifestFiles.SchemaDirectory, $"{stem}.schema.json"))
            .ShouldBeTrue(
                $"ContentLayout.SchemaFor('assets/{stem}.json') resolves to " +
                $"'schema/{stem}.schema.json'. Without it the build fails with MissingSchema; " +
                "with a schema and no data file it fails with OrphanSchema.");
    }

    /// <summary>Every JSON file in the directory is one of the two manifests, and each is paired.</summary>
    [Fact]
    public void The_assets_directory_holds_nothing_that_escapes_schema_pairing()
    {
        var files = Directory.GetFiles(ManifestFiles.AssetsDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        files.ShouldBe(["asset_manifest_art", "asset_manifest_audio"], ignoreOrder: true);

        foreach (var stem in files)
        {
            File.Exists(Path.Combine(ManifestFiles.SchemaDirectory, $"{stem}.schema.json"))
                .ShouldBeTrue($"{stem}.json would otherwise be an unvalidated file under game-data");
        }
    }

    /// <summary>
    /// The schema's <c>title</c> names the file it governs, following the convention every other
    /// schema in <c>game-data/schema/</c> uses.
    /// </summary>
    [Theory]
    [InlineData("asset_manifest_art", "assets/asset_manifest_art.json")]
    [InlineData("asset_manifest_audio", "assets/asset_manifest_audio.json")]
    public void Each_schema_titles_itself_with_the_file_it_governs(string stem, string title)
    {
        var schema = File.ReadAllText(
            Path.Combine(ManifestFiles.SchemaDirectory, $"{stem}.schema.json"));

        schema.ShouldContain("\"$schema\": \"https://json-schema.org/draft/2020-12/schema\"",
            Case.Sensitive);
        schema.ShouldContain($"\"title\": \"{title}\"", Case.Sensitive);
        schema.ShouldContain("\"additionalProperties\": false", Case.Sensitive);
    }

    /// <summary>UTF-8 without BOM, LF line endings — a CRLF here would turn every later one-line change into a whole-file diff.</summary>
    [Theory]
    [InlineData("assets/asset_manifest_art.json")]
    [InlineData("assets/asset_manifest_audio.json")]
    [InlineData("schema/asset_manifest_art.schema.json")]
    [InlineData("schema/asset_manifest_audio.schema.json")]
    public void Every_new_file_is_utf8_without_a_BOM_and_uses_LF(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(ManifestFiles.DataRoot, relativePath));

        bytes.Length.ShouldBeGreaterThan(0);
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            .ShouldBeFalse("game-data/README.md: UTF-8, no BOM");

        // A file with no newline at all would satisfy the LF-only check below vacuously.
        bytes.Count(b => b == (byte)'\n')
            .ShouldBeGreaterThan(100, $"{relativePath} is a pretty-printed multi-line JSON file");

        for (var i = 1; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                bytes[i - 1].ShouldNotBe((byte)'\r', $"CRLF at byte {i} of {relativePath}");
            }
        }
    }

    /// <summary>Both manifests declare the relative <c>$schema</c> every data file here carries.</summary>
    [Theory]
    [InlineData("asset_manifest_art")]
    [InlineData("asset_manifest_audio")]
    public void Each_manifest_points_at_its_own_schema(string stem)
    {
        var json = File.ReadAllText(Path.Combine(ManifestFiles.AssetsDirectory, $"{stem}.json"));

        json.ShouldContain($"\"$schema\": \"../schema/{stem}.schema.json\"", Case.Sensitive);
        json.ShouldContain("\"_status\": \"transcribed\"", Case.Sensitive);
        json.ShouldContain("\"_doc\":", Case.Sensitive);
    }

    /// <summary>The manifest is loadable from the <c>game-data</c> root alone.</summary>
    [Fact]
    public void The_register_loads_from_the_game_data_root()
    {
        var loaded = AssetManifestReader.Load(ManifestFiles.DataRoot);

        loaded.Art.Assets.ShouldNotBeEmpty();
        loaded.Audio.Assets.ShouldNotBeEmpty();
        loaded.AllIds.Count.ShouldBe(1080);
    }

    /// <summary>A missing directory says what is missing rather than throwing an IO error.</summary>
    [Fact]
    public void Loading_from_a_root_with_no_assets_directory_fails_with_a_located_message()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"sir-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);

        try
        {
            var thrown = Should.Throw<AssetManifestFormatException>(
                () => AssetManifestReader.Load(empty));

            thrown.Location.ShouldBe("assets");
            thrown.Message.ShouldContain("no manifest directory", Case.Sensitive);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}
