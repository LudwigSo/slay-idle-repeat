using System.Reflection;
using Shouldly;
using SlayIdleRepeat.AssetManifest;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// A second manifest parser is a correctness problem, not a duplication one: the canonical reader
/// throws where the manifest is silent, so a convenience deserialiser written elsewhere could turn
/// missing sizes and pivots into <c>default</c> without anybody noticing.
/// </summary>
public sealed class SingleManifestReaderTests
{
    private const int MinimumProductionSourceFiles = 5;

    /// <summary>A second reader has to name the file it opens, so this string's absence from production sources is a real guard.</summary>
    private const string ManifestFileStem = "asset_manifest";

    [Fact]
    public void No_production_source_file_names_the_manifest_files_at_all()
    {
        var files = PipelineFiles.ProductionSourceFiles();
        files.Count.ShouldBeGreaterThan(
            MinimumProductionSourceFiles,
            "scanning an empty or near-empty source tree would prove nothing");

        var offenders = files
            .Where(file => File.ReadAllText(file).Contains(ManifestFileStem, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.ShouldBeEmpty(
            "SlayIdleRepeat.AssetPipeline reaches the register through " +
            "SlayIdleRepeat.AssetManifest and never opens game-data/assets itself");
    }

    /// <summary>A source scan cannot see a reader assembled out of pieces that never spell the file name, so this checks reflectively instead.</summary>
    [Fact]
    public void No_type_in_the_pipeline_assembly_hands_back_a_manifest_it_built_itself()
    {
        var producers = DeclaredMethods()
            .Where(method =>
                method.ReturnType == typeof(AssetManifestSet)
                || method.ReturnType == typeof(ArtManifest)
                || method.ReturnType == typeof(AudioManifest))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToArray();

        producers.ShouldBeEmpty(
            "only SlayIdleRepeat.AssetManifest.AssetManifestReader may produce a manifest");
    }

    /// <summary>Guards the case above: if nothing here touched the row type at all, "it produces no manifest" would be measuring nothing.</summary>
    [Fact]
    public void The_pipeline_consumes_M8_09s_row_type_rather_than_one_of_its_own()
    {
        var consumers = DeclaredMethods()
            .Where(method => method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(ArtAsset)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToArray();

        consumers.ShouldNotBeEmpty();
        consumers.ShouldContain($"{typeof(AssetSpec).FullName}.{nameof(AssetSpec.Resolve)}");
    }

    private static IEnumerable<MethodInfo> DeclaredMethods() =>
        typeof(AssetSpec).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly));
}
