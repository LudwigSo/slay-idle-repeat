using Shouldly;
using SlayIdleRepeat.AssetPipeline;
using SlayIdleRepeat.AssetProvenance;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The guard that keeps every byte this generator writes out of <c>assets/</c> and
/// <c>game-data/</c>, and the <c>procedural</c> provenance record it writes instead.
/// </summary>
public sealed class PlaceholderOutputTests
{
    [Fact]
    public void A_path_under_artifacts_is_accepted()
    {
        var expected = Path.Combine(PlaceholderFiles.RepositoryRoot, "artifacts", "placeholders");

        PlaceholderOutput.RequireArtifactsPath(expected).ShouldBe(expected);

        // 🔒 The guard's own remarks say the segment comparison is ORDINAL, because `.gitignore`
        // ignores lower-case `artifacts/` and, on a case-sensitive filesystem, `Artifacts/` is a
        // different and tracked directory.
        Should.Throw<InvalidOperationException>(() => PlaceholderOutput.RequireArtifactsPath(
                Path.Combine(PlaceholderFiles.RepositoryRoot, "Artifacts", "placeholders")))
            .Message.ShouldContain("is not under 'artifacts/'", Case.Sensitive);
    }

    [Fact]
    public void A_path_under_assets_is_refused_because_the_provenance_gate_scans_the_filesystem()
    {
        var thrown = Should.Throw<InvalidOperationException>(() =>
            PlaceholderOutput.RequireArtifactsPath(
                Path.Combine(PlaceholderFiles.RepositoryRoot, "assets", "placeholders")));

        // 🔒 Steering S2 — pin WHICH rule fired. Three separate refusals throw the same type here.
        thrown.Message.ShouldContain("is under 'assets/'", Case.Sensitive);
        thrown.Message.ShouldContain("M8-01a's provenance gate", Case.Sensitive);
    }

    [Fact]
    public void A_path_under_game_data_is_refused_by_ruling_A8()
    {
        var thrown = Should.Throw<InvalidOperationException>(() =>
            PlaceholderOutput.RequireArtifactsPath(
                Path.Combine(PlaceholderFiles.RepositoryRoot, "game-data", "placeholders")));

        thrown.Message.ShouldContain("is under 'game-data/'", Case.Sensitive);
        thrown.Message.ShouldContain("ruling A8", Case.Sensitive);
    }

    [Fact]
    public void A_path_outside_artifacts_altogether_is_refused()
    {
        var thrown = Should.Throw<InvalidOperationException>(() =>
            PlaceholderOutput.RequireArtifactsPath(
                Path.Combine(PlaceholderFiles.RepositoryRoot, "docs", "placeholders")));

        thrown.Message.ShouldContain("is not under 'artifacts/'", Case.Sensitive);
    }

    [Fact]
    public void A_path_that_climbs_back_out_of_artifacts_into_assets_is_refused()
    {
        // 🔒 The check is on the RESOLVED path's segments, not on whether the word "artifacts"
        // appears in the string. This is the case that tells the two apart.
        var thrown = Should.Throw<InvalidOperationException>(() =>
            PlaceholderOutput.RequireArtifactsPath(Path.Combine(
                PlaceholderFiles.RepositoryRoot, "artifacts", "..", "assets", "placeholders")));

        thrown.Message.ShouldContain("is under 'assets/'", Case.Sensitive);
    }

    [Fact]
    public void A_batch_that_was_pointed_outside_artifacts_refuses_before_it_draws_anything()
    {
        var output = Path.Combine(PlaceholderFiles.RepositoryRoot, "assets", "placeholders");

        var thrown = Should.Throw<InvalidOperationException>(() => new PlaceholderBatch(
            new PlaceholderBatchOptions(output, Commit, ThresholdSet.Uncalibrated())));

        // 🔒 Steering S2. RequireArtifactsPath has three separate refusals and they all throw
        // InvalidOperationException, and so does WriteProvenance on a malformed record — pinning
        // the type alone would accept any of them.
        thrown.Message.ShouldContain("is under 'assets/'", Case.Sensitive);

        // 🔒 And "before it draws": the guard is in the constructor, so a refused batch must not
        // have created its output directory. That is what makes this a refusal rather than a late
        // cleanup.
        Directory.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public void The_provenance_record_is_a_procedural_one_that_M8_01a_validator_accepts()
    {
        var asset = SampleRows.Require(SampleRows.BiomeEnemy);
        var spec = AssetSpec.Resolve(asset);
        var record = PlaceholderOutput.RecordFor(
            asset, spec, GenerationCanvas.For(spec.TargetSize), Commit);

        record.Kind.ShouldBe(ProceduralProvenance.KindName);
        record.AssetId.ShouldBe(asset.Id);
        record.RepoCommit.ShouldBe(Commit);

        // 🔒 Forbidden on an art record: an art record's tool is named by its kind, and a second
        // tool field would be two sources of truth for one fact.
        record.Tooling.ShouldBeNull();
        record.ToolsNamed.ShouldBeEmpty("a procedural art record's \"tool\" is this repository.");

        RecordValidator.Validate(record, AssetMedium.Art).ShouldBeEmpty();
    }

    [Fact]
    public void The_provenance_record_round_trips_through_M8_01a_own_writer_and_reader()
    {
        var asset = SampleRows.Require(SampleRows.Mount);
        var spec = AssetSpec.Resolve(asset);
        var canvas = GenerationCanvas.For(spec.TargetSize);
        var record = PlaceholderOutput.RecordFor(asset, spec, canvas, Commit);

        var read = ProvenanceStore.ReadRecord(ProvenanceStore.WriteRecord(record), asset.Id);

        var procedural = read.ShouldBeOfType<ProceduralProvenance>();

        // 🔒 Order-insensitive, because M8-01a's writer sorts the parameter object ordinally by key
        // so a record's bytes are stable whatever order a generator built the dictionary in. The
        // claim here is that no parameter was dropped or altered, which is what a round trip is for.
        procedural.Parameters.Count.ShouldBe(record.Parameters.Count);
        foreach (var (key, value) in record.Parameters)
        {
            procedural.Parameters.Keys.ShouldContain(key);
            procedural.Parameters[key].ShouldBe(value);
        }

        procedural.Generator.ShouldBe("SlayIdleRepeat.AssetPlaceholders");
    }

    [Fact]
    public void The_record_names_every_parameter_that_fixed_the_pixels()
    {
        var asset = SampleRows.Require(SampleRows.BiomeBoss);
        var spec = AssetSpec.Resolve(asset);
        var canvas = GenerationCanvas.For(spec.TargetSize);
        var parameters = PlaceholderOutput.RecordFor(asset, spec, canvas, Commit).Parameters;

        // 🔒 A provenance record whose parameters do not reproduce the file is not evidence. The
        // nine stated thresholds are the ones a reader would have to set to get the same bytes.
        foreach (var key in ThresholdSet.Keys.Where(PlaceholderThresholds.ForPipeline().IsCalibrated))
        {
            parameters.Keys.ShouldContain(key);
        }

        parameters["generationCanvas"].ShouldBe($"{canvas.Width}x{canvas.Height}");
        parameters["deliverySize"].ShouldBe("1024x1024");
        parameters["pivot"].ShouldBe(spec.Pivot);
    }

    /// <summary>A well-formed 40-hex commit. M8-01a's validator refuses anything else.</summary>
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
}
