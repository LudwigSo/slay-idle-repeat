using System.Reflection;
using Shouldly;
using SlayIdleRepeat.AssetManifest;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C15 — there is exactly one manifest reader in this repository, and it is M8-09's.
/// </summary>
/// <remarks>
/// 🔒 A second parser is not a duplication problem, it is a correctness one. M8-09's reader fetches
/// every member by name and throws where the manifest is silent, precisely so that `15`'s holes
/// stay visible; a convenience deserialiser written here would turn 144 missing sizes and 284
/// missing pivots into <c>default</c> without anybody noticing.
/// </remarks>
public sealed class SingleManifestReaderTests
{
    private const int MinimumProductionSourceFiles = 5;

    /// <summary>
    /// The manifest file stem. A second reader has to name the file it opens, so the absence of
    /// this string from every production source is a real, greppable guard.
    /// </summary>
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

    /// <summary>
    /// 🔒 The reflection half. A source scan cannot see a reader assembled out of pieces that never
    /// spell the file name; a method that hands back a manifest can only have built one.
    /// </summary>
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

    /// <summary>
    /// 🔒 The floor under the case above. If nothing in this assembly touched M8-09's row type at
    /// all, "it produces no manifest" would be true of an assembly that had nothing to do with the
    /// register, and the guard would be measuring the wrong thing.
    /// </summary>
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
