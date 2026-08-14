using Shouldly;
using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetProvenance;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// One real run of the whole thing, over a deliberately diverse ten-row sample.
/// </summary>
/// <remarks>
/// <para>
/// The sample covers all eight `15` §C delivery sizes the register holds, both §C pivots,
/// biome-scoped and not, and atlased and not. Ten rows is enough to reach every branch and small
/// enough to keep a unit suite fast; ⚠️ <b>the full ~641-row batch is a CLI run, not a test.</b>
/// </para>
/// <para>
/// 🔒 The run is shared through a class fixture. It draws ten placeholders on canvases up to
/// 2048×2048 and drives every one through all seven `15` §B4 steps — doing that once per case would
/// make this suite the slowest in the repository for no extra coverage.
/// </para>
/// </remarks>
public sealed class PlaceholderBatchTests(SampleBatch batch) : IClassFixture<SampleBatch>
{
    private readonly PlaceholderBatchReport report = batch.Report;

    [Fact]
    public void The_sample_still_covers_every_branch_the_batch_cases_are_written_against()
    {
        var rows = SampleRows.Generatable.Select(SampleRows.Require).ToArray();

        // 🔒 S3 over the SAMPLE, not over the register. Eight batch cases and four renderer cases
        // quantify over SampleRows.Generatable, so it is this suite's entire subject set — and a row
        // quietly dropped from it narrows twelve assertions at once while every one stays green.
        // Every claim in SampleRows' own remarks is asserted here rather than described there.
        rows.Select(row => row.RequireDeliverySize()).Distinct().Count().ShouldBe(8);
        rows.Select(row => row.Pivot)
            .Distinct()
            .ShouldBe([Doc15Pivots.Center, Doc15Pivots.BottomCenter], ignoreOrder: true);
        rows.Count(row => row.Biome is not null).ShouldBeGreaterThan(0);
        rows.Count(row => row.Biome is null).ShouldBeGreaterThan(0);
        rows.Count(row => row.Atlas is null).ShouldBe(1);
        rows.Select(row => row.Atlas)
            .Where(atlas => atlas is not null)
            .Distinct()
            .Count()
            .ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Every_generatable_row_in_the_sample_produced_a_placeholder()
    {
        report.Generated.Count.ShouldBe(SampleRows.Generatable.Count);
        report.Generated
            .Select(placeholder => placeholder.AssetId)
            .ShouldBe(SampleRows.Generatable, ignoreOrder: true);
    }

    [Fact]
    public void Nothing_failed()
    {
        // 🔒 S3 floor. "Nothing failed" over a run that attempted nothing is vacuously true, and a
        // typo in one SampleRows id produces exactly that.
        report.Accounted.ShouldBe(SampleRows.Generatable.Count + SampleRows.Skippable.Count);

        report.Failed.ShouldBeEmpty(
            "a failure here is a defect in this generator or in `15` §B4's steps, never a hole in " +
            $"the doc: [{string.Join("; ", report.Failed.Select(f => $"{f.AssetId} {f.Message}"))}].");
    }

    [Fact]
    public void Each_of_the_three_refusals_is_reported_under_its_own_reason()
    {
        foreach (var (id, reason) in SampleRows.Skippable)
        {
            var skip = report.Skipped.SingleOrDefault(entry => entry.AssetId == id);

            skip.ShouldNotBeNull($"'{id}' should have been skipped and was not.");

            // 🔒 Steering S2: which refusal fired, not merely that one did. O30 needs the three
            // counts separately at M11-01, and 95 rows carry neither a size nor a pivot.
            skip.Reason.ShouldBe(reason);
        }
    }

    [Fact]
    public void A_row_missing_both_a_size_and_a_pivot_is_reported_under_the_size()
    {
        var asset = SampleRows.Require(SampleRows.RowWithoutDeliverySize);

        // The precedence only means anything if the row really is missing both.
        asset.DeliverySize.ShouldBeNull();
        asset.Pivot.ShouldBeNull();

        report.Skipped
            .Single(skip => skip.AssetId == asset.Id)
            .Reason
            .ShouldBe(
                PlaceholderSkipReason.NoDeliverySize,
                "AssetSpec.Resolve refuses the size first, so reporting the pivot would put this " +
                "row in the wrong column of the gap O30 reconciles.");
    }

    [Fact]
    public void The_run_accounts_for_every_row_it_considered()
    {
        report.Accounted.ShouldBe(
            SampleRows.Generatable.Count + SampleRows.Skippable.Count,
            "generated + skipped + failed has to equal what the run looked at, or one of the four " +
            "numbers is wrong and nobody can tell which.");
    }

    [Fact]
    public void The_register_totals_the_report_reconciles_against_are_the_registers_own()
    {
        // 🔒 The register holds 974 art rows and 106 audio ones — 1,080 ids in all, of which 1,048
        // are uncut. Audio is out of scope for M8-10 (no licence, no capability), so the universe
        // here is the 942 uncut ART rows. The report carries both totals so that arithmetic is
        // checkable rather than remembered.
        //
        // 🔒 S3 floor first. Every equality below reads both sides off the same loaded register, so
        // 0 == 0 satisfies all of them — which is exactly what a reader that silently returned no
        // rows would produce. The two counts are measured against the committed files, 2026-08-14.
        PlaceholderFiles.Shipped.Art.Assets.Count.ShouldBe(974);
        PlaceholderFiles.Shipped.Audio.Assets.Count.ShouldBe(106);

        report.ArtRowsInRegister.ShouldBe(PlaceholderFiles.Shipped.Art.Assets.Count);
        report.AudioRowsInRegister.ShouldBe(PlaceholderFiles.Shipped.Audio.Assets.Count);
        (report.ArtRowsInRegister + report.AudioRowsInRegister).ShouldBe(
            PlaceholderFiles.Shipped.AllIds.Count);
    }

    [Fact]
    public void No_placeholder_fails_doc_15_Part_F_item_7_or_item_10()
    {
        // 🔒 The two fully mechanical items, and the only two this generator entirely controls: the
        // canvas it draws, the pivot it centres against, the name it writes, the atlas it packs
        // into. A failure here is a defect in this tool.
        PlaceholderBatchReport.MechanicalItems.ShouldBe([7, 10], ignoreOrder: true);

        report.MechanicalFailures.ShouldBeEmpty(
            string.Join(
                "; ",
                report.MechanicalFailures.Select(
                    failure => $"{failure.AssetId} item {failure.Outcome.ItemNumber}: " +
                               failure.Outcome.Reason)));

        // Floored, so an empty MechanicalFailures over an empty batch cannot pass this.
        report.Generated
            .SelectMany(placeholder => placeholder.Qa.Outcomes)
            .Count(outcome => PlaceholderBatchReport.MechanicalItems.Contains(outcome.ItemNumber)
                              && outcome.Verdict == QaVerdict.Pass)
            .ShouldBe(SampleRows.Generatable.Count * 2);
    }

    [Fact]
    public void The_batch_is_blocked_by_uncalibrated_thresholds_and_cannot_be_better_than_that()
    {
        // 🔒 This is the honest ceiling and it is not a defect. Five of `15` Part F's eleven items
        // are Human and can only return HumanGapOnly; four more read cutoffs that ship null and
        // return Uncalibrated. Uncalibrated outranks HumanGapOnly in QaBatchResult's own precedence,
        // so a full eleven-item run over the shipped register tops out here — not at
        // AwaitingHumanReview, and never at Accepted.
        report.Decision.ShouldBe(QaDecision.BlockedByUncalibratedThreshold);

        var checklist = new QaChecklist();
        checklist.OfClassification(QaClassification.Human).Count.ShouldBe(5);
        checklist.OfClassification(QaClassification.MechanicalUncalibratedThreshold).Count.ShouldBe(4);
        checklist.OfClassification(QaClassification.Mechanical).Count.ShouldBe(2);
    }

    [Fact]
    public void The_run_declares_its_one_knowing_departure_from_doc_15()
    {
        // 🔒 `15` §A3 forbids text in a generated image and Part F item 8 forbids it again; every
        // placeholder carries its id. Part F will not report that — item 8 is a Human item and
        // returns HumanGapOnly on every asset — so the batch report is the only place it appears.
        report.Departures.Select(departure => departure.Id).ShouldBe(["DEP_A3_ID_STAMP"]);
        report.Departures[0].DocReference.ShouldBe("15 §A3, Part F item 8");

        report.Generated
            .SelectMany(placeholder => placeholder.Qa.Outcomes)
            .Where(outcome => outcome.ItemNumber == 8)
            .Select(outcome => outcome.Verdict)
            .Distinct()
            .ShouldBe(
                [QaVerdict.HumanGapOnly],
                "if item 8 ever graded the stamp mechanically, this departure would be reported " +
                "twice — and this case is why the report carries it at all.");
    }

    [Fact]
    public void Every_placeholder_in_the_sample_carries_its_id_stamp()
    {
        // Floored: Unstamped is empty over an empty batch too, and the renderer draws no stamp at
        // all when the card is too small to carry one legibly.
        report.Generated.Count.ShouldBe(SampleRows.Generatable.Count);
        report.Unstamped.ShouldBeEmpty(
            "a placeholder exists so a missing asset is self-identifying on screen, and an " +
            $"unstamped one is a grey box: [{string.Join(", ", report.Unstamped)}].");
    }

    [Fact]
    public void The_gate_is_handed_the_shipped_register_and_a_caller_cannot_substitute_another()
    {
        // 🔒 S6. Three of the nine processing values ForPipeline states — the outline colour
        // tolerance, the palette match tolerance and the neutral list — are also read by Part F
        // items 3 and 5. Handing the gate the pipeline's set would grade those two items against
        // the generator's own working numbers, so PlaceholderBatchOptions takes the register's JSON
        // and builds the set itself: there is no parameter to pass a different one through.
        var options = new PlaceholderBatchOptions(
            batch.Directory, "0123456789abcdef0123456789abcdef01234567", PlaceholderFiles.ThresholdsJson());

        ThresholdSet.Keys.Count.ShouldBeGreaterThanOrEqualTo(17);
        ThresholdSet.Keys.Where(options.QaThresholds.IsCalibrated).ShouldBeEmpty();
    }

    [Fact]
    public void Doc_15_A4_silhouette_item_reports_uncalibrated_for_every_asset_and_never_passes()
    {
        var outcomes = report.Generated
            .SelectMany(placeholder => placeholder.Qa.Outcomes)
            .Where(outcome => outcome.ItemNumber == 1)
            .ToArray();

        // 🔒 Floored before the Distinct(). Item 1 is graded once per generated placeholder, so
        // nine of the ten silently losing it would leave a single Uncalibrated verdict behind and
        // satisfy an assertion whose name says "every asset".
        outcomes.Length.ShouldBe(SampleRows.Generatable.Count);
        outcomes.Select(outcome => outcome.Verdict).Distinct().ShouldBe([QaVerdict.Uncalibrated]);
    }

    [Fact]
    public void The_run_takes_both_known_deviations_and_reports_the_atlas_page_cap_contradiction()
    {
        report.Deviations.Keys.ShouldContain(ResizeStep.LanczosDeviationId);
        report.Deviations.Keys.ShouldContain(ExportStep.PngquantDeviationId);
        report.Deviations[ResizeStep.LanczosDeviationId].ShouldBe(SampleRows.Generatable.Count);

        // Step 7 emits it on every pack, and the sample packs more than one atlas.
        report.Contradictions[AtlasPackStep.PageCapContradictionId].ShouldBe(report.Atlases.Count);
        report.Atlases.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void No_asset_reports_the_delivery_aspect_contradiction()
    {
        // 🔒 The whole point of the CON_DELIVERY_ASPECT ruling. Generating on the delivery aspect
        // makes step 5's resample uniform, so the contradiction M8-06 declared against a square
        // canvas never fires — including for the 512×384 mount in the sample, which is the row it
        // was declared about.
        // 🔒 S3 floor: ShouldNotContain over an empty dictionary is vacuous, and an empty
        // Contradictions is the shape of a run that tallied none at all. Step 7 emits the page-cap
        // contradiction on every pack, so its presence proves the tally is live.
        report.Contradictions.Keys.ShouldContain(AtlasPackStep.PageCapContradictionId);
        report.Contradictions.Keys.ShouldNotContain(ResizeStep.DeliveryAspectContradictionId);

        // 🔒 And the row the ruling was declared about really was in this run. Asserting only that
        // the register still says 512×384 proves nothing about what was generated.
        report.Generated.Select(placeholder => placeholder.AssetId).ShouldContain(SampleRows.Mount);
        SampleRows.Require(SampleRows.Mount).DeliverySize.ShouldBe(new PixelSize(512, 384));
    }

    [Fact]
    public void Every_atlas_in_the_sample_holds_exactly_its_own_members_and_no_other()
    {
        report.Atlases.ShouldNotBeEmpty();

        foreach (var pack in report.Atlases)
        {
            var expected = report.Generated
                .Where(placeholder => placeholder.Atlas == pack.AtlasId)
                .Select(placeholder => placeholder.AssetId)
                .OrderBy(id => id, StringComparer.Ordinal);

            pack.Placements
                .Select(placement => placement.AssetId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ShouldBe(expected);
        }

        // 🔒 The one row `15` §D2 assigns no atlas is packed into nothing, and still passes item 10.
        report.Atlases.ShouldNotContain(pack => pack.Placements.Any(p => p.AssetId == SampleRows.TileIcon));
        SampleRows.Require(SampleRows.TileIcon).Atlas.ShouldBeNull();
    }

    [Fact]
    public void Every_generated_placeholder_left_an_image_and_a_provenance_record_under_artifacts()
    {
        foreach (var placeholder in report.Generated)
        {
            var image = Path.Combine(batch.Directory, placeholder.FileName);
            var provenance = Path.Combine(
                batch.Directory,
                PlaceholderOutput.ProvenanceDirectoryName,
                placeholder.AssetId + ProvenanceStore.RecordExtension);

            File.Exists(image).ShouldBeTrue($"'{image}' was not written.");
            File.Exists(provenance).ShouldBeTrue($"'{provenance}' was not written.");

            new FileInfo(image).Length.ShouldBe(placeholder.EncodedBytes);

            // Read back through M8-01a's own reader, so a record this tool wrote that its own
            // store cannot parse fails here rather than in a gate run months later.
            ProvenanceStore.ReadRecord(File.ReadAllText(provenance), placeholder.AssetId)
                .Kind.ShouldBe(ProceduralProvenance.KindName);
        }

        // 🔒 Nothing leaked into the store the provenance gate reads.
        Directory.Exists(Path.Combine(
                PlaceholderFiles.RepositoryRoot,
                ProvenanceStore.StoreDirectory.Replace('/', Path.DirectorySeparatorChar),
                ProvenanceStore.RecordsDirectoryName))
            .ShouldBeTrue("the real store must still exist — this asserts against the right place.");

        Directory.GetFiles(
                Path.Combine(
                    PlaceholderFiles.RepositoryRoot,
                    ProvenanceStore.StoreDirectory.Replace('/', Path.DirectorySeparatorChar),
                    ProvenanceStore.RecordsDirectoryName),
                "*" + ProvenanceStore.RecordExtension)
            .ShouldBeEmpty(
                "a placeholder record reached assets/provenance/records/. Placeholders are not " +
                "deliveries and must not enter the store or the gate's coverage count.");
    }

    [Fact]
    public void The_delivered_image_is_exactly_the_registers_delivery_size()
    {
        foreach (var placeholder in report.Generated)
        {
            var expected = SampleRows.Require(placeholder.AssetId).RequireDeliverySize();
            using var decoded = SkiaSharp.SKBitmap.Decode(
                Path.Combine(batch.Directory, placeholder.FileName));

            decoded.ShouldNotBeNull($"'{placeholder.FileName}' did not decode as a PNG.");
            decoded.Width.ShouldBe(expected.Width);
            decoded.Height.ShouldBe(expected.Height);
        }
    }
}

/// <summary>
/// The shared run: ten real register rows plus the three that must be refused, drawn once.
/// </summary>
public sealed class SampleBatch : IDisposable
{
    private readonly ScratchDirectory scratch = PlaceholderFiles.Scratch("sample-batch");

    /// <summary>Draws the sample.</summary>
    public SampleBatch()
    {
        var considered = SampleRows.Generatable
            .Concat(SampleRows.Skippable.Keys)
            .ToHashSet(StringComparer.Ordinal);

        Report = new PlaceholderBatch(new PlaceholderBatchOptions(
                scratch.Path,
                "0123456789abcdef0123456789abcdef01234567",
                PlaceholderFiles.ThresholdsJson(),
                Include: asset => considered.Contains(asset.Id)))
            .Run(PlaceholderFiles.Shipped);
    }

    /// <summary>What the run did.</summary>
    public PlaceholderBatchReport Report { get; }

    /// <summary>Where it wrote.</summary>
    public string Directory => scratch.Path;

    /// <inheritdoc/>
    public void Dispose() => scratch.Dispose();
}
