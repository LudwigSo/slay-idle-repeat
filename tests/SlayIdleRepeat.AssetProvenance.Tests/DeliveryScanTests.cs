using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// The delivery set: what counts as a delivered asset, and what the scan must never quietly treat
/// as "nothing delivered".
/// </summary>
public sealed class DeliveryScanTests
{
    [Fact]
    public void Every_asset_file_under_the_root_is_found_wherever_the_pipeline_put_it()
    {
        using var tree = new TempTree();
        tree.Write("art/characters/chr_hero_body_idle.png");
        tree.Write("art/ui/ui_panel_main_9slice.png");
        tree.Write("audio/music/mus_home.ogg");
        tree.Write("audio/sfx/source/sfx_crit.wav");

        var delivered = DeliveredAssets.Scan(tree.Root);

        delivered.Count.ShouldBe(4);
        delivered.Select(d => d.Id).ShouldBe(
            ["chr_hero_body_idle", "mus_home", "sfx_crit", "ui_panel_main_9slice"]);
    }

    /// <summary>
    /// The store lives under the delivery root and must not scan as an asset — but nothing else is
    /// excluded. A per-directory allow-list would stop covering the pipeline the first time it
    /// renamed its output folder.
    /// </summary>
    [Fact]
    public void The_provenance_store_itself_is_not_a_delivery()
    {
        using var tree = new TempTree();
        tree.Write("provenance/records/chr_hero_body_idle.json", "{}");
        tree.Write("provenance/README.md", "#");

        // A DELIVERY-FORMAT file inside the store: the .json and .md above are already dropped by
        // the extension filter, so without this the case would prove the filter, not the exclusion.
        tree.Write("provenance/records/chr_hero_body_idle.png");
        tree.Write("art/chr_hero_body_idle.png");

        DeliveredAssets.Scan(tree.Root).ShouldHaveSingleItem().RelativePath.ShouldBe("art/chr_hero_body_idle.png");
    }

    /// <summary>
    /// The exclusion is exact. On Linux <c>Provenance/</c> is a different directory from the store,
    /// and excluding it case-insensitively would drop whatever was put there from the delivery set.
    /// </summary>
    [Fact]
    public void Only_the_store_directory_itself_is_excluded_and_not_a_differently_cased_neighbour()
    {
        using var tree = new TempTree();
        tree.Write("Provenance/chr_hero_body_idle.png");

        var delivered = DeliveredAssets.Scan(tree.Root);

        if (OperatingSystem.IsWindows())
        {
            // Windows cannot hold `provenance/` and `Provenance/` at once, so the distinction is
            // unobservable here. The assertion that matters is the one on the comparison itself.
            delivered.Count.ShouldBeLessThanOrEqualTo(1);
            return;
        }

        delivered.ShouldHaveSingleItem().RelativePath.ShouldBe("Provenance/chr_hero_body_idle.png");
    }

    [Fact]
    public void A_working_file_that_is_not_a_delivery_format_is_ignored()
    {
        using var tree = new TempTree();
        tree.Write("source/chr_hero_body_idle.psd");
        tree.Write("source/notes.md");
        tree.Write("source/chr_hero_body_idle.xcf");

        DeliveredAssets.Scan(tree.Root).ShouldBeEmpty();
        DeliveredAssets.DeliveredExtensions.ShouldBe([".png", ".ogg", ".wav"]);
    }

    /// <summary>
    /// An absent delivery root is a defect, not an empty delivery set — the two are indistinguishable
    /// to a caller and only one of them is a pass.
    /// </summary>
    [Fact]
    public void A_missing_delivery_root_is_a_loud_failure_rather_than_zero_deliveries()
    {
        using var tree = new TempTree();

        Should.Throw<ProvenanceFormatException>(
                () => DeliveredAssets.Scan(Path.Combine(tree.Root, "gone")))
            .Message.ShouldMatchWildcard(
                "*does not exist*a missing root would otherwise be reported as an empty one, and an empty one is a pass*");
    }

    [Fact]
    public void The_scan_records_where_the_file_is_so_a_failure_can_be_acted_on()
    {
        using var tree = new TempTree();
        tree.Write("art/chapter1/greenwood/chr_enemy_thorn_sprout_idle.png");

        var delivered = DeliveredAssets.Scan(tree.Root).ShouldHaveSingleItem();

        delivered.RelativePath.ShouldBe("art/chapter1/greenwood/chr_enemy_thorn_sprout_idle.png");
        delivered.Extension.ShouldBe(".png");
    }

    /// <summary>
    /// The committed state, stated as a fact rather than assumed: the register holds 1,080 slots and
    /// <b>zero</b> of them are delivered.
    /// </summary>
    [Fact]
    public void Nothing_is_delivered_in_this_repository_today()
    {
        DeliveredAssets.Scan(ProvenanceFixtures.DeliveryRoot).ShouldBeEmpty(
            "when the first asset lands, DeliveryDeclaration.AwaitingFirstDelivery must be flipped " +
            "in the same commit, and this case rewritten to state the new count.");
    }
}
