using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>How a load should treat the source it was handed.</summary>
/// <remarks>
/// The one knob that matters is <see cref="OverrideDocuments"/>: sweeps, experiments and what-ifs
/// all run as overrides, so the canonical data is only ever edited when a change is adopted.
/// Overrides are named explicitly and layered in the order given — never auto-discovered, because
/// an experiment that applies itself just by existing on disk is an experiment nobody knows is
/// running.
/// </remarks>
public sealed record ContentLoadOptions
{
    /// <summary>A plain load of the canonical data, with no overrides.</summary>
    public static ContentLoadOptions Canonical { get; } = new();

    /// <summary>A load that also enforces the rules that only matter for a build going to players.</summary>
    public static ContentLoadOptions Shipping { get; } = new() { ShippingBuild = true };

    /// <summary>Override document paths, applied in order. Each must be a sparse patch keyed by canonical file name.</summary>
    public IReadOnlyList<string> OverrideDocuments { get; init; } = [];

    /// <summary>True when this content is going in front of players, which turns on the ship gates.</summary>
    /// <remarks>
    /// Today that means one rule: a build that ships to players must fail while any translation
    /// sentinel remains. All DE values are sentinels right now, so the gate cannot be on by default
    /// without failing the current milestones on purpose — behind this flag it fails on the day it
    /// should instead.
    /// </remarks>
    public bool ShippingBuild { get; init; }
}

/// <summary>The outcome of a load: a snapshot, or the reasons there isn't one.</summary>
public sealed class ContentLoadResult
{
    /// <summary>Creates a result.</summary>
    public ContentLoadResult(ContentSnapshot? snapshot, IReadOnlyList<ContentIssue> issues)
    {
        Snapshot = snapshot;
        Issues = issues;
    }

    /// <summary>True when the content validated cleanly and a snapshot was produced.</summary>
    public bool Succeeded => Issues.Count == 0 && Snapshot is not null;

    /// <summary>The snapshot, or null when validation failed.</summary>
    public ContentSnapshot? Snapshot { get; }

    /// <summary>Every finding, ordered by location then code so the report is stable.</summary>
    public IReadOnlyList<ContentIssue> Issues { get; }

    /// <summary>The snapshot, or a <see cref="ContentLoadException"/> naming every issue.</summary>
    public ContentSnapshot Require() =>
        Succeeded ? Snapshot! : throw new ContentLoadException(Issues);
}

/// <summary>Raised when content that must be valid is not.</summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Creates the exception from the issues that caused it.</summary>
    public ContentLoadException(IReadOnlyList<ContentIssue> issues)
        : base(Describe(issues)) => Issues = issues;

    /// <summary>The findings that stopped the load.</summary>
    public IReadOnlyList<ContentIssue> Issues { get; }

    private static string Describe(IReadOnlyList<ContentIssue> issues) =>
        $"Content validation failed with {issues.Count} issue(s):{Environment.NewLine}" +
        string.Join(Environment.NewLine, issues.Select(i => "  " + i));
}
