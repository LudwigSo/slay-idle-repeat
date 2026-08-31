using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `14` §16.3 / §16.5 — the run's lifetime is authored ONCE and every other home that repeats
/// it agrees, number for number.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule exists.</b> The window a run may sit untouched before it is settled as a
/// death is a game rule, and it lives in <c>game-data/tuning/progression.json</c>. How long the
/// stored row that holds that run survives is ops config, and it lives in the environment as
/// <c>Cache__RunStateTtlHours</c>. `14` §16.5's config-home rule puts them in different homes
/// deliberately — and the tuning entry's own <c>_doc</c> then says, in as many words, "the two must
/// agree or a run row outlives or predeceases the rule that ends it". <b>Nothing checked that.</b>
/// M5 shipped the number in four places written by three different tasks, cross-referenced only in
/// prose.
/// </para>
/// <para>
/// Disagreement is silent in both directions and neither is a crash. Raise the tuning window above
/// the row TTL and the row is swept while the rule still calls the run live: the player's run
/// vanishes mid-run with no settlement and no death. Lower it below, and rows outlive the rule that
/// ends them — harmless until the archive is read, at which point it holds runs that were settled
/// hours before the row says they ended.
/// </para>
/// <para>
/// 🔒 <b>The subject is the four homes by identity, not a count</b> (steering S3). Each arm below
/// fails when it cannot find its own subject, because "no home disagreed" is exactly what a rule
/// that stopped finding three of them would report. What this does NOT close: a <em>deployment</em>
/// that overrides <c>Cache__RunStateTtlHours</c> to something else. Only the repository's own
/// declared default is checkable here, and that is stated rather than smoothed over — the value a
/// live environment carries is outside anything this suite can read.
/// </para>
/// </remarks>
public sealed class RunLifetimeAgreementRuleTests
{
    /// <summary>The tuning document that authors the rule, and the path into it.</summary>
    private const string TuningFile = "game-data/tuning/progression.json";

    /// <summary>The local stack, which declares the ops default the server boots with.</summary>
    private const string LocalStackFile = "docker-compose.yml";

    /// <summary>The composition root that reads the ops setting, and carries the fallback for it.</summary>
    private const string CompositionFile = "src/SlayIdleRepeat.Server/Composition/PersistenceComposition.cs";

    /// <summary>
    /// 🔒 `14` §16.3 — every home that spells the run lifetime spells the same number.
    /// </summary>
    [Fact]
    public void Every_home_that_repeats_the_run_lifetime_agrees_with_the_tuning_that_authors_it()
    {
        var authored = AuthoredExpiryHours();

        authored.ShouldBeGreaterThan(
            0,
            $"{TuningFile} authors runLifetime.expiryHours as the run's whole lifetime; a zero or " +
            "negative window would settle every run the instant it started.");

        var offenders = new List<string>();

        Agree(offenders, "the local stack's " + LocalStackFile + " default for Cache__RunStateTtlHours",
            LocalStackDefaultHours(), authored,
            "the stored run row is swept on this clock while the rule above decides whether the run " +
            "is still live. Higher here and settled runs linger in the archive under the wrong " +
            "instant; lower and a live run's row disappears mid-run with no settlement and no death.");

        Agree(offenders, CompositionFile + "'s fallback when Cache:RunStateTtlHours is unset",
            CompositionFallbackHours(), authored,
            "a deployment that does not set the variable boots on this number instead, so it is a " +
            "second declared default and has to be the same one.");

        Agree(offenders, "ContentRetention.WindowAlignedToRunTtl",
            ContentRetentionHours(), authored,
            "content bundles are kept exactly as long as a run could still be pinned to them — the " +
            "constant's own remark says it is aligned to the run TTL rather than authored. Aligned " +
            "to a number that has moved is not aligned.");

        ArchRule.Empty(
            offenders,
            $"the run lifetime is authored once, in {TuningFile}, and every home that repeats it " +
            "agrees (14 §16.3). The tuning entry's own _doc already states the requirement; this is " +
            "the check behind it. Move the number in every home in the same commit, or — better — " +
            "delete the repetition that no longer needs to exist.");
    }

    private static void Agree(
        ICollection<string> offenders, string home, int found, int authored, string consequence)
    {
        if (found != authored)
        {
            offenders.Add(
                $"{home} reads {found.ToString(CultureInfo.InvariantCulture)} hours and " +
                $"{TuningFile} authors {authored.ToString(CultureInfo.InvariantCulture)}: {consequence}");
        }
    }

    private static int AuthoredExpiryHours()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile(TuningFile)));

        document.RootElement.TryGetProperty("runLifetime", out var lifetime).ShouldBeTrue(
            $"{TuningFile} carries no runLifetime object. Every arm below is stated against it, so " +
            "its absence turns this whole rule into a comparison of nothing with nothing.");

        lifetime.TryGetProperty("expiryHours", out var hours).ShouldBeTrue(
            $"{TuningFile}'s runLifetime carries no expiryHours. Same silence, one level down.");

        return hours.GetInt32();
    }

    private static int LocalStackDefaultHours() =>
        Single(
            new Regex(@"Cache__RunStateTtlHours:\s*'(?<hours>\d+)'", RegexOptions.None, TimeSpan.FromSeconds(5)),
            LocalStackFile,
            "the compose file is the deployment contract the server's own README points at; if the " +
            "variable moved or changed quoting, this rule stops watching the ops half entirely.");

    private static int CompositionFallbackHours() =>
        Single(
            new Regex(
                @"GetValue\(\s*""Cache:RunStateTtlHours""\s*,\s*(?<hours>\d+)\s*\)",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)),
            CompositionFile,
            "the fallback is read off the GetValue call itself. If the composition root now binds " +
            "an options object instead, point this arm at the new default rather than deleting it.");

    private static int ContentRetentionHours()
    {
        var field = ProductionAssemblies.Application
            .GetType("SlayIdleRepeat.Application.Services.Content.ContentRetention", throwOnError: false)
            ?.GetField("WindowAlignedToRunTtl");

        field.ShouldNotBeNull(
            "ContentRetention.WindowAlignedToRunTtl no longer resolves. It is the one home here " +
            "that is a compiled value rather than text, and a rename would otherwise drop it out of " +
            "this rule without anything going red.");

        return (int)((TimeSpan)field.GetValue(null)!).TotalHours;
    }

    private static int Single(Regex pattern, string file, string why)
    {
        var matches = pattern.Matches(File.ReadAllText(RepoFile(file)));

        matches.Count.ShouldBe(
            1,
            $"{file} holds {matches.Count} declarations this rule can read, and it is written for " +
            $"exactly one. {why}");

        return int.Parse(matches[0].Groups["hours"].Value, CultureInfo.InvariantCulture);
    }

    private static string RepoFile(string relativePath) =>
        Path.Combine(RepoLayout.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
