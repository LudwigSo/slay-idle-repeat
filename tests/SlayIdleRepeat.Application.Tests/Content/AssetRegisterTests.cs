using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The third category of document that is not snapshot content: an asset register under
/// <c>assets/</c>, which is paired with a schema and validated like any data file and then left
/// out of the snapshot at the last step.
/// </summary>
/// <remarks>
/// Both halves are load-bearing and each is satisfiable on its own by the wrong change. Dropping a
/// register earlier — before pairing — would leave its schema governing nothing and fail the load
/// as an orphan. Carrying it into the snapshot would let a re-export of the art it lists restamp
/// the content version and invalidate every pinned run, for a change no rule can read.
/// </remarks>
public sealed class AssetRegisterTests
{
    private const string GadgetSchemaPath = "schema/gadgets.schema.json";
    private const string RegisterPath = "assets/gadgets.json";
    private const string SnapshotDocumentPath = "tuning/gadgets.json";

    private const string GadgetSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "$id": "https://slayidlerepeat.local/schema/gadgets.schema.json",
      "type": "object",
      "additionalProperties": false,
      "required": ["$schema", "count"],
      "properties": {
        "$schema": { "type": "string" },
        "count": { "type": "integer", "minimum": 0 }
      }
    }
    """;

    private static string Gadgets(int count) => $$"""
    {
      "$schema": "../schema/gadgets.schema.json",
      "count": {{count}}
    }
    """;

    /// <summary>The miniature valid set plus one extra document, paired by the stem convention.</summary>
    private static InMemoryContentSource SourceWith(string documentPath, int count) =>
        ContentTestData.Valid()
            .Set(GadgetSchemaPath, GadgetSchema)
            .Set(documentPath, Gadgets(count));

    // ------------------------------------------------------- against the real shipped data set

    /// <summary>
    /// The floor for the case below: if the shipped tree ever stopped holding asset registers, that
    /// case would pass by having nothing to exclude.
    /// </summary>
    [Fact]
    public void The_shipped_data_set_holds_asset_registers_so_the_exclusion_case_is_not_vacuous()
    {
        RepoData.Documents.Keys
            .Count(ContentLayout.IsAssetRegister)
            .ShouldBeGreaterThanOrEqualTo(2, "the art and audio registers both ship under assets/");
    }

    /// <summary>
    /// Asserted as both halves at once. "No issues" alone is satisfied by a loader that snapshots
    /// the registers; "no assets/ document" alone is satisfied by a loader that drops them before
    /// pairing and orphans <c>schema/asset_manifest_art.schema.json</c> and its audio twin.
    /// </summary>
    [Fact]
    public void The_shipped_asset_registers_are_paired_and_validated_yet_absent_from_the_snapshot()
    {
        var result = ContentLoader.Load(RepoData.Source());

        result.Issues.ShouldBeEmpty();
        result.Require().DocumentPaths.ShouldNotContain(
            p => p.StartsWith(ContentLayout.AssetRegisterDirectory, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------- against the fake source

    [Fact]
    public void A_document_under_the_asset_register_directory_is_not_in_the_snapshot()
    {
        var snapshot = ContentLoader.Load(SourceWith(RegisterPath, 3)).Require();

        snapshot.DocumentPaths.ShouldNotContain(RegisterPath);
    }

    /// <summary>
    /// The negative control for the case above: the identical document under a directory that is
    /// not the register directory IS in the snapshot. Without it the rule is equally satisfied by a
    /// loader that snapshots nothing at all.
    /// </summary>
    [Fact]
    public void The_same_document_outside_the_asset_register_directory_is_in_the_snapshot()
    {
        var snapshot = ContentLoader.Load(SourceWith(SnapshotDocumentPath, 3)).Require();

        snapshot.DocumentPaths.ShouldContain(SnapshotDocumentPath);
    }

    /// <summary>
    /// A register is still paired: its schema governs it, so the schema is not an orphan and the
    /// register itself is not an ungoverned file. Both would be reported as issues.
    /// </summary>
    [Fact]
    public void An_asset_register_still_pairs_with_its_schema_rather_than_orphaning_it()
    {
        var result = ContentLoader.Load(SourceWith(RegisterPath, 3));

        result.Issues.ShouldBeEmpty();
    }

    /// <summary>
    /// And it is still validated: a value its schema forbids is reported, even though the document
    /// never reaches the snapshot.
    /// </summary>
    [Fact]
    public void An_asset_register_that_violates_its_schema_is_still_reported()
    {
        var result = ContentLoader.Load(SourceWith(RegisterPath, -1));

        result.Issues.ShouldContain(i => i.Location.StartsWith(RegisterPath, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the stamp, both directions

    /// <summary>
    /// The property the whole exclusion exists for: an art re-export rewrites a register and must
    /// not restamp the content version, because no rule can read a register and every pinned run
    /// would be invalidated for nothing.
    /// </summary>
    [Fact]
    public void Editing_an_asset_register_leaves_the_content_stamp_exactly_where_it_was()
    {
        var before = ContentLoader.Load(SourceWith(RegisterPath, 3)).Require();
        var after = ContentLoader.Load(SourceWith(RegisterPath, 4)).Require();

        after.Version.ShouldBe(before.Version);
    }

    /// <summary>
    /// The other direction, without which the case above is satisfied by a stamp that never moves
    /// at all: the same one-value edit to a document that IS snapshot content does move it.
    /// </summary>
    [Fact]
    public void Editing_a_snapshotted_document_moves_the_content_stamp()
    {
        var before = ContentLoader.Load(SourceWith(SnapshotDocumentPath, 3)).Require();
        var after = ContentLoader.Load(SourceWith(SnapshotDocumentPath, 4)).Require();

        after.Version.ShouldNotBe(before.Version);
    }

    // ------------------------------------------------------------------------ the path predicate

    /// <summary>
    /// The predicate discriminates on the directory, not on a prefix: <c>assetsx/</c> is a
    /// different directory that merely starts with the same six characters, <c>content/assets/</c>
    /// is a content type, and a schema is never a register whatever it is named.
    /// </summary>
    [Theory]
    [InlineData("assets/x.json", true)]
    [InlineData("assets/nested/x.json", true)]
    [InlineData("assetsx/x.json", false)]
    [InlineData("content/assets/x.json", false)]
    [InlineData("schema/assets.schema.json", false)]
    [InlineData("tuning/x.json", false)]
    public void IsAssetRegister_answers_for_the_directory_and_not_for_a_bare_prefix(
        string documentPath, bool expected)
    {
        ContentLayout.IsAssetRegister(documentPath).ShouldBe(expected);
    }

    [Fact]
    public void The_asset_register_directory_is_declared_with_its_trailing_slash_so_a_prefix_cannot_match()
    {
        ContentLayout.AssetRegisterDirectory.ShouldBe("assets/");
    }
}
