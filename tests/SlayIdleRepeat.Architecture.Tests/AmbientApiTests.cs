using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `14` §8.1 / `23` §6 — the banned ambient APIs. 🔒 Never <c>System.Random</c>,
/// <c>GD.Randi()</c>, <c>Random.Shared</c>, <c>DateTime.Now</c>, <c>Guid.NewGuid()</c> or
/// <c>Environment.TickCount</c> anywhere in `Core` or `Application`.
/// </summary>
public sealed class AmbientApiTests
{
    /// <summary>
    /// `23` §6 / `14` §8.1 — the authoritative check: an IL scan of the compiled `Core`
    /// and `Application` assemblies. Unlike a grep it is not defeated by a `using` alias,
    /// a fully-qualified call or an extension method, and it cannot fire on a comment.
    /// </summary>
    [Fact]
    public void Core_and_Application_contain_no_ambient_time_or_randomness()
    {
        var offenders = BannedApi.Violations(ProductionAssemblies.CoreModule)
            .Select(v => $"[Core] {v}")
            .Concat(BannedApi.Violations(ProductionAssemblies.ApplicationModule).Select(v => $"[Application] {v}"));

        ArchRule.Empty(
            offenders,
            "Core and Application contain no ambient time or randomness — IL scan (14 §8.1, 23 §6).");
    }

    /// <summary>
    /// `14` §8.1 — "CI greps for these and fails", implemented literally as a second,
    /// independent mechanism over the `.cs` files of the two projects, with comments and
    /// string literals stripped first. It catches what the IL scan cannot: a banned API
    /// written inside a `#if`-excluded branch that never reaches the assembly.
    /// </summary>
    [Fact]
    public void Core_and_Application_source_contains_no_banned_ambient_api()
    {
        var directories = new[]
        {
            RepoLayout.ProjectDirectory(ProductionAssemblies.CoreName),
            RepoLayout.ProjectDirectory(ProductionAssemblies.ApplicationName),
        };

        var offenders =
            from directory in directories
            from file in RepoLayout.SourceFiles(directory)
            let source = SourceText.Read(file)
            from rule in BannedApi.SourcePatterns
            from hit in source.Hits(rule.Pattern)
            select $"{hit}  [{rule.Reason}]";

        ArchRule.Empty(
            offenders,
            "Core and Application source contains no banned ambient API — source grep (14 §8.1).");
    }

    /// <summary>
    /// `14` §8.2 — 🔒 no culture-sensitive formatting or parsing in `Core` or `Application`:
    /// no `ToString`/`Parse`/`TryParse` on a number or a date without an `IFormatProvider`,
    /// and no parameterless `ToUpper`/`ToLower`.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `14` §8.2 requires a byte-identical `LogHash` across x64 and ARM64, and the ban list
    /// this suite inherited covered time, identity and randomness — everything that varies by
    /// WHEN the code runs — and nothing that varies by WHERE. `CanonicalStateWriter` is
    /// exactly where the difference would land: a parameterless `double.ToString()` renders
    /// `1,5` on a `de-DE` laptop and `1.5` in the Linux container, which is two byte streams,
    /// two hashes, and a determinism failure that reproduces only on the machine of whoever
    /// wrote it.
    /// </para>
    /// <para>
    /// Not covered by `InvariantGlobalization`: `tools/ContentValidator` sets it, the Godot
    /// client does not, and a defence that is on in one host and off in another is worse than
    /// none — it makes the bug appear only in the host nobody tests on.
    /// </para>
    /// </remarks>
    [Fact]
    public void Core_and_Application_contain_no_culture_sensitive_formatting()
    {
        var offenders = BannedApi.CultureViolations(ProductionAssemblies.CoreModule)
            .Select(v => $"[Core] {v}")
            .Concat(BannedApi.CultureViolations(ProductionAssemblies.ApplicationModule).Select(v => $"[Application] {v}"));

        ArchRule.Empty(
            offenders,
            "Core and Application format and parse with an explicit culture — LogHash is byte-identical " +
            "across architectures and locales (14 §8.2).");
    }
}
