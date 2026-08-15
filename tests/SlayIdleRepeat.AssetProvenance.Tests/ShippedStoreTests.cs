using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// The gate over what is actually committed — the run <c>build/ci/Invoke-ProvenanceGate.ps1</c>
/// performs, asserted here so that a red CI job is reproducible from <c>dotnet test</c>.
/// </summary>
public sealed class ShippedStoreTests
{
    [Fact]
    public void The_committed_store_and_register_pass_the_gate()
    {
        var report = ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceFixtures.ShippedStore,
            DeliveredAssets.Scan(ProvenanceFixtures.DeliveryRoot));

        report.Violations.ShouldBeEmpty();
        report.Passed.ShouldBeTrue();
    }

    /// <summary>
    /// The honest statement of what that green means today: pinning the coverage state, not merely
    /// <c>Passed</c>, stops "0 delivered, all clear" being read as "1,048 assets, all documented".
    /// </summary>
    [Fact]
    public void That_pass_is_over_zero_deliveries_and_the_report_says_so()
    {
        var report = ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceFixtures.ShippedStore,
            DeliveredAssets.Scan(ProvenanceFixtures.DeliveryRoot));

        report.Coverage.ShouldBe(DeliveryCoverage.AwaitingFirstDelivery);
        report.DeliveredAssets.ShouldBe(0);
        report.Records.ShouldBe(0);
        report.VerifiedPairings.ShouldBe(0);

        // …but it is NOT a pass over an empty register: 1,080 rows were quantified over, and the
        // floor and the canaries were checked against them.
        report.RegisteredAssets.ShouldBe(1080);
        report.RegisteredAssets.ShouldBeGreaterThanOrEqualTo(ProvenanceGate.MinimumRegisteredAssets);
        report.ActiveAssets.ShouldBe(1048);
        report.CutAssets.ShouldBe(32);

        report.Headline().ShouldStartWith("AWAITING FIRST DELIVERY", Case.Sensitive);
    }

    [Fact]
    public void The_committed_store_holds_no_records_yet_and_that_is_the_declared_state()
    {
        ProvenanceFixtures.ShippedStore.Records.ShouldBeEmpty();
        DeliveryDeclaration.AwaitingFirstDelivery.ShouldBeTrue();
    }

    /// <summary>A missing store must not read as an empty one — the whole gate turns on that distinction.</summary>
    [Fact]
    public void A_missing_store_is_a_loud_failure()
    {
        using var tree = new TempTree();

        Should.Throw<ProvenanceFormatException>(() => ProvenanceStore.Load(Path.Combine(tree.Root, "gone")))
            .Message.ShouldMatchWildcard("*does not exist*worse than no gate at all*");
    }

    [Fact]
    public void A_store_with_no_records_directory_is_a_loud_failure()
    {
        using var tree = new TempTree();
        tree.Write(
            $"provenance/{ProvenanceStore.LicenceFileName}",
            """{ "licences": [] }""");

        Should.Throw<ProvenanceFormatException>(
                () => ProvenanceStore.Load(Path.Combine(tree.Root, "provenance")))
            .Message.ShouldMatchWildcard("*records*does not exist*committed empty*");
    }

    [Fact]
    public void A_store_with_no_licence_register_is_a_loud_failure()
    {
        using var tree = new TempTree();
        tree.Directory_("provenance/records");

        Should.Throw<ProvenanceFormatException>(
                () => ProvenanceStore.Load(Path.Combine(tree.Root, "provenance")))
            .Message.ShouldMatchWildcard("*tool-licences.json*is missing*a gate with no register would pass every tool*");
    }

    /// <summary>
    /// The store is flat. A record one directory deep would be loaded by nothing and reported by
    /// nothing — a quiet place to hide a record in a store whose whole premise is loudness.
    /// </summary>
    [Fact]
    public void A_record_filed_in_a_subdirectory_is_refused_rather_than_ignored()
    {
        using var tree = new TempTree();
        var store = tree.WriteStore([]);
        tree.Write(
            $"provenance/{ProvenanceStore.RecordsDirectoryName}/art/{ProvenanceFixtures.ArtId}.json",
            ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney()));

        Should.Throw<ProvenanceFormatException>(() => ProvenanceStore.Load(store))
            .Message.ShouldMatchWildcard("*has 1 subdirector(y/ies) — 'art'*The store is flat*");
    }

    /// <summary>
    /// A populated store round-trips through disk. The cases above prove the empty state is honest;
    /// this proves the store works when it stops being empty.
    /// </summary>
    [Fact]
    public void A_populated_store_loads_back_every_record_it_was_written_with()
    {
        using var tree = new TempTree();
        var store = tree.WriteStore(
        [
            ProvenanceFixtures.Midjourney(),
            ProvenanceFixtures.Procedural(),
            ProvenanceFixtures.Cc0(),
        ]);

        var loaded = ProvenanceStore.Load(store);

        loaded.Records.Count.ShouldBe(3);
        loaded.Find(ProvenanceFixtures.ArtId).ShouldBeOfType<MidjourneyProvenance>();
        loaded.Find(ProvenanceFixtures.OtherArtId).ShouldBeOfType<ProceduralProvenance>();
        loaded.Find(ProvenanceFixtures.AudioId).ShouldBeOfType<Cc0Provenance>();
        loaded.Licences.Licences.ShouldHaveSingleItem().Tool.ShouldBe("midjourney");
    }

    /// <summary>
    /// The end-to-end shape M8-02 onwards will be in: assets on disk, records beside them, and a
    /// written licence confirmation as the only thing left between them and a green gate.
    /// </summary>
    [Fact]
    public void A_delivered_asset_with_a_record_still_fails_until_the_licence_is_confirmed_in_writing()
    {
        using var tree = new TempTree();
        tree.Write("art/chr_hero_body_idle.png");
        var store = tree.WriteStore([ProvenanceFixtures.Midjourney()]);

        var report = ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceStore.Load(store),
            DeliveredAssets.Scan(tree.Root),
            awaitingFirstDelivery: false);

        report.Violations.ShouldHaveSingleItem().Code.ShouldBe(ViolationCode.UnconfirmedLicence);
    }
}
