using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// <c>tuning/calibration_builds.json</c> — the reference par build, the five build archetypes and the
/// scaling rule. Every loadout the sweep fights with comes out of this file; the harness never
/// synthesises its own.
/// </summary>
/// <remarks>
/// This reader deliberately does not look at <c>frozenPerks</c>, <c>pets</c>, <c>mount</c> or
/// <c>draftPriority</c>, and must not be taught to: those keys name perk and pet ids that do not exist
/// in the repository yet, so resolving them is impossible and stubbing them would put a fabricated
/// effect into every measured number. <c>standardDummy</c> and <c>measurementProtocol</c> are likewise
/// unread — their <c>INVULNERABLE</c> flag has no equivalent in <c>SlayIdleRepeat.Core</c>'s damage
/// pipeline, and faking it would make that assertion's agreement figure a measurement of the fake.
/// </remarks>
public sealed class CalibrationBuilds
{
    public const string Document = "tuning/calibration_builds.json";

    private CalibrationBuilds(
        int referenceParBuildLevel,
        StatLine referenceParBuild,
        double referenceParBuildTargetPower,
        IReadOnlyList<BuildArchetype> archetypes,
        IReadOnlyList<StatId> scaledStats,
        double bisectionTolerance,
        int scalarDecimalPlaces,
        int defaultLevel)
    {
        ReferenceParBuildLevel = referenceParBuildLevel;
        ReferenceParBuild = referenceParBuild;
        ReferenceParBuildTargetPower = referenceParBuildTargetPower;
        Archetypes = archetypes;
        ScaledStats = scaledStats;
        BisectionTolerance = bisectionTolerance;
        ScalarDecimalPlaces = scalarDecimalPlaces;
        DefaultLevel = defaultLevel;
    }

    /// <summary>The level the reference par build is evaluated at (authored 10).</summary>
    public int ReferenceParBuildLevel { get; }

    /// <summary>The statline that defines <c>K_POWER</c> by <c>PlayerPower := 1000</c>.</summary>
    public StatLine ReferenceParBuild { get; }

    /// <summary>The <c>1000</c> the reference build is defined to score.</summary>
    public double ReferenceParBuildTargetPower { get; }

    /// <summary>The five build archetypes, in authored order. <c>id</c> and <c>stats</c> only.</summary>
    public IReadOnlyList<BuildArchetype> Archetypes { get; }

    /// <summary>The stats the scalar multiplies. Authored <c>maxHp</c>, <c>atk</c>, <c>def</c>.</summary>
    public IReadOnlyList<StatId> ScaledStats { get; }

    /// <summary>Bisection tolerance. Authored 0.001.</summary>
    public double BisectionTolerance { get; }

    /// <summary>Decimal places the scalar is rounded to.</summary>
    public int ScalarDecimalPlaces { get; }

    /// <summary>The evaluation level when a loadout targets no content. Authored 40.</summary>
    public int DefaultLevel { get; }

    /// <summary>Reads the document. Every value is read; none is defaulted.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A value is authored <c>null</c>.</exception>
    public static CalibrationBuilds Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var archetypeRows = content.Read($"{Document}#/archetypes").Items;
        var archetypes = new List<BuildArchetype>(archetypeRows.Count);

        for (var i = 0; i < archetypeRows.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);

            // id and stats only — see the type remarks on frozenPerks/pets.
            archetypes.Add(new BuildArchetype(
                content.ReadText($"{Document}#/archetypes/{index}/id"),
                AuthoredStatNames.Read(content, $"{Document}#/archetypes/{index}/stats")));
        }

        var scaledStatKeys = content.Read($"{Document}#/scalingRule/scaledStats").Items;
        var scaledStats = new List<StatId>(scaledStatKeys.Count);
        foreach (var key in scaledStatKeys)
        {
            scaledStats.Add(StatOf(key.AsText($"{Document}#/scalingRule/scaledStats")));
        }

        return new CalibrationBuilds(
            content.ReadInt32($"{Document}#/referenceParBuild/level"),
            AuthoredStatNames.Read(content, $"{Document}#/referenceParBuild/stats"),
            content.ReadDouble($"{Document}#/referenceParBuild/targetPower"),
            archetypes,
            scaledStats,
            content.ReadDouble($"{Document}#/scalingRule/bisectionTolerance"),
            content.ReadInt32($"{Document}#/scalingRule/scalarDecimalPlaces"),
            content.ReadInt32($"{Document}#/scalingRule/defaultLevel"));
    }

    /// <summary>The archetype with the given id.</summary>
    /// <exception cref="KeyNotFoundException">No archetype carries that id.</exception>
    public BuildArchetype Archetype(string id) =>
        Archetypes.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException(
            $"'{id}' is not a build archetype in {Document}. The authored five are " +
            $"{string.Join(", ", Archetypes.Select(a => a.Id))}, and `05` §9 sweeps exactly those.");

    private static StatId StatOf(string authoredKey)
    {
        foreach (var (stat, key) in AuthoredStatNames.ByStat)
        {
            if (string.Equals(key, authoredKey, StringComparison.Ordinal))
            {
                return stat;
            }
        }

        throw new KeyNotFoundException(
            $"'{authoredKey}' in {Document}#/scalingRule/scaledStats is not one of `05` §1's " +
            "fourteen combat stats. The scaling rule can only scale a stat an actor carries.");
    }
}

/// <summary>One of the five build archetypes, reduced to what this reader may honestly read.</summary>
/// <param name="Id">The authored id, e.g. <c>ARCH_CRIT</c>.</param>
/// <param name="Stats">The fourteen-stat loadout, as authored.</param>
/// <remarks>
/// There is deliberately no <c>Perks</c> or <c>Pets</c> member — see <see cref="CalibrationBuilds"/>'
/// remarks.
/// </remarks>
public sealed record BuildArchetype(string Id, StatLine Stats);
