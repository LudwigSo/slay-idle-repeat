using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// 🔒 <b>Ruling A8 (M8, 2026-08-12), held mechanically.</b> The provenance store lives at
/// <c>assets/provenance/</c> and <b>not</b> under <c>game-data/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>LocalFileContentSource</c> recursively enumerates every <c>*.json</c> under
/// <c>game-data/</c> into the <c>ContentSnapshot</c>, and therefore into
/// <c>ContentHashing.Compute</c>. Provenance records grow by one per generated asset, so a store
/// under <c>game-data/</c> would move the content version stamp — which `14` §6 makes load-bearing
/// for replay and <c>CONTENT_VERSION_MISMATCH</c> — 1,048 times over the life of the pipeline, once
/// per asset, for a file the game never reads.
/// </para>
/// <para>
/// A ruling recorded only in a kickoff note is a ruling the next agent re-derives from scratch or
/// breaks. These cases are the difference. They also check the second, subtler way back in: a
/// schema pairing. `14` §6 pairs <c>game-data/&lt;x&gt;.json</c> with
/// <c>game-data/schema/&lt;x&gt;.schema.json</c>, so a schema authored for the store would be the
/// invitation to move the data next to it.
/// </para>
/// </remarks>
public sealed class ProvenanceLayoutTests
{
    [Fact]
    public void The_store_is_not_under_game_data()
    {
        var store = Path.GetFullPath(ProvenanceFixtures.StoreRoot);
        var gameData = Path.GetFullPath(ProvenanceFixtures.DataRoot) + Path.DirectorySeparatorChar;

        Directory.Exists(store).ShouldBeTrue($"the store must exist at '{store}'.");
        store.StartsWith(gameData, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
            "ruling A8: everything under game-data/ enters the ContentSnapshot and would move the " +
            "content version stamp once per generated asset (14 §6).");
    }

    [Fact]
    public void The_store_is_where_the_tool_says_it_is()
    {
        ProvenanceStore.StoreDirectory.ShouldBe("assets/provenance");

        Path.GetFullPath(ProvenanceFixtures.StoreRoot)
            .ShouldBe(Path.GetFullPath(Path.Combine(
                ProvenanceFixtures.RepositoryRoot,
                ProvenanceStore.StoreDirectory.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// `14` §6 — nothing under <c>game-data/</c> is a provenance artefact, in either direction: no
    /// record, no licence register, and no schema that would pair one back in.
    /// </summary>
    [Fact]
    public void Nothing_under_game_data_is_a_provenance_artefact()
    {
        var files = Directory.GetFiles(ProvenanceFixtures.DataRoot, "*", SearchOption.AllDirectories);

        // 🔒 Steering S3 — floor the subject set. A game-data tree that moved or emptied would
        // otherwise satisfy this rule forever by having nothing to look at.
        files.Length.ShouldBeGreaterThan(50);

        var offenders = files
            .Select(f => Path.GetRelativePath(ProvenanceFixtures.RepositoryRoot, f).Replace('\\', '/'))
            .Where(f => f.Contains("provenance", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith("/" + ProvenanceStore.LicenceFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            "ruling A8: the provenance store stays out of game-data/, and no schema pairing pulls " +
            "it back in. Found: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// `15` §G / `20` §2.1 — the store directory is committed even while it is empty, so that a
    /// missing store is a defect rather than a state. Git does not track empty directories, hence
    /// the <c>.gitkeep</c>.
    /// </summary>
    [Fact]
    public void The_records_directory_is_committed_empty_rather_than_absent()
    {
        var records = Path.Combine(ProvenanceFixtures.StoreRoot, ProvenanceStore.RecordsDirectoryName);

        Directory.Exists(records).ShouldBeTrue();
        File.Exists(Path.Combine(records, ".gitkeep")).ShouldBeTrue(
            "git does not track an empty directory, and ProvenanceStore.Load fails loudly on a " +
            "missing one — so without the .gitkeep the gate would be red on a fresh clone.");
    }

    [Fact]
    public void The_delivery_root_is_the_production_area_and_not_the_runtime_content_tree()
    {
        DeliveredAssets.AssetsDirectory.ShouldBe("assets");

        Path.GetFullPath(ProvenanceFixtures.DeliveryRoot)
            .ShouldNotBe(Path.GetFullPath(ProvenanceFixtures.DataRoot));
    }

    /// <summary>
    /// `15` §G — the store carries its own README. The ruling, the record format and the ⛔ on
    /// <c>tool-licences.json</c> have to be findable from the directory itself, not only from a
    /// milestone note.
    /// </summary>
    [Fact]
    public void The_store_documents_the_ruling_that_put_it_here()
    {
        var readme = Path.Combine(ProvenanceFixtures.StoreRoot, "README.md");

        File.Exists(readme).ShouldBeTrue();

        var text = File.ReadAllText(readme);
        text.ShouldContain("Ruling A8", Case.Sensitive);
        text.ShouldContain("ContentSnapshot", Case.Sensitive);
        text.ShouldContain("M8-01b", Case.Sensitive);
    }
}
