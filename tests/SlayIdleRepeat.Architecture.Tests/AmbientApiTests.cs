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

    /// <summary>
    /// 🔒 `14` §8.2 / `30` §7 — every type in the domain-event hierarchy declares its own
    /// <c>PrintMembers</c>, so an event renders identically under every culture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the blind spot of the rule above, closed</b> (carried-forward item 9). A record's
    /// <em>synthesized</em> <c>PrintMembers</c> appends each member through
    /// <c>StringBuilder.Append(object)</c> — the <c>ToString()</c> call happens inside
    /// <c>StringBuilder</c>, on a boxed value, so <c>BannedApi.CultureViolations</c> never sees a
    /// call whose declaring type is <c>System.Int32</c> or <c>System.Int64</c> and the event passes
    /// while rendering <c>Delta = −10</c> (U+2212 MINUS SIGN) under <c>sv-SE</c> against
    /// <c>Delta = -10</c> (U+002D) in the container.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this rule does and does not prove.</b> It proves the hook is <em>declared</em> —
    /// which is what a future event author would otherwise have to be told — not that its body
    /// passes a culture. The body is covered from two other sides: the IL rule above, which DOES see
    /// an explicit <c>ToString()</c> inside a hand-written body, and
    /// <c>CurrencyChangedTests.ToString_renders_identically_under_any_culture</c> in
    /// <c>SlayIdleRepeat.Core.Tests</c>, which renders a negative delta under <c>sv-SE</c> and
    /// compares. Neither is sufficient alone: the behavioural test is per event, and this is what
    /// makes the <em>next</em> event inherit the convention.
    /// </para>
    /// <para>
    /// 🔒 <b>The floor</b> (steering S3). The subject set is "types under <c>Core/Events/</c>", which
    /// a namespace rename would empty — taking the rule permanently green with it. Two names are
    /// pinned by identity, not by count, because a count is satisfied by any two types that happen
    /// to land there.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_domain_event_declares_an_invariant_PrintMembers()
    {
        var events = Domain.CoreTypesUnder(Domain.EventsNamespace)
            .Where(t => t.DeclaringType is null && !Domain.IsCompilerGenerated(t))
            .ToArray();

        var names = events.Select(t => t.Name).ToArray();

        Assert.Contains(Domain.DomainEventType, names, StringComparer.Ordinal);
        Assert.Contains(Domain.CurrencyChangedEvent, names, StringComparer.Ordinal);

        var offenders = events
            .Where(t => !DeclaresAuthoredPrintMembers(t))
            .Select(t =>
                $"{t.FullName} does not declare a PrintMembers, so its ToString() runs the compiler's — " +
                "which appends every member through StringBuilder.Append(object) and formats with the " +
                "AMBIENT culture. A long Delta of -10 renders as '−10' under sv-SE and '-10' in the " +
                "container, and Core_and_Application_contain_no_culture_sensitive_formatting cannot see it " +
                "through the boxing (14 §8.2). Declare 'protected override bool PrintMembers(StringBuilder)' " +
                "and append with CultureInfo.InvariantCulture, as DomainEvent and CurrencyChanged do.");

        ArchRule.Empty(
            offenders,
            "Every type in the 30 §7 event hierarchy declares an invariant PrintMembers — an event reads " +
            "the same on every machine (14 §8.2).");
    }

    /// <summary>
    /// Whether the author — rather than the compiler — declared this record's <c>PrintMembers</c>.
    /// </summary>
    /// <remarks>
    /// 🔒 The <c>[CompilerGenerated]</c> half is the whole check: <b>every</b> record has a
    /// <c>PrintMembers</c>, so a presence test alone would be true of every event that ever exists
    /// and would report success forever — steering <b>S1</b>'s assertion-true-of-every-value.
    /// </remarks>
    private static bool DeclaresAuthoredPrintMembers(Mono.Cecil.TypeDefinition type) =>
        type.Methods.Any(m =>
            m.Name.Equals("PrintMembers", StringComparison.Ordinal) &&
            !Domain.IsCompilerGenerated(m));
}
