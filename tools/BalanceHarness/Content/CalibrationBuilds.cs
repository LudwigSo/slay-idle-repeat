using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 <c>tuning/calibration_builds.json</c> — `29` §2.5's reference par build, the five `05` §9 build
/// archetypes and `29` §2.5.3's scaling rule.
/// </summary>
/// <remarks>
/// <para>
/// `05` §9 🔒: <em>"the harness loads these; it never synthesises its own"</em>. Every loadout the
/// sweep fights with comes out of this file.
/// </para>
/// <para>
/// 🔒 <b>This reader deliberately does not look at <c>frozenPerks</c>, <c>pets</c>, <c>mount</c> or
/// <c>draftPriority</c>, and must not be taught to.</b> Those keys name perk and pet ids that do not
/// exist in the repository until M3-07 and M4-07 — <c>content/perks/</c> and <c>content/pets/</c>
/// carry nothing for them to resolve to. Resolving them is impossible, stubbing them would put a
/// fabricated effect into every measured number, and inventing an id is steering S6's forbidden hole.
/// The five fourteen-stat lines are sufficient for all five guardrails this milestone owns, so the
/// reader stops at <c>id</c> and <c>stats</c>.
/// </para>
/// <para>
/// ⚠️ <c>standardDummy</c> and <c>measurementProtocol</c> are read by nothing here either. They exist
/// for `29` §2.5's <c>EmpiricalPower</c> and `05` §9's assertion <b>A10</b>, whose dummy carries an
/// <c>INVULNERABLE</c> flag that <c>SlayIdleRepeat.Core</c> does not implement — there is no such
/// concept in `05` §4's damage pipeline. A10 is `21` §11.2's and owned by M6-09; faking invulnerability
/// here would make its ±12% agreement figure a measurement of the fake.
/// </para>
/// </remarks>
public sealed class CalibrationBuilds
{
    /// <summary>`29` §2.5 / `05` §9 — the document.</summary>
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

    /// <summary>`29` §2.1 — the level the reference par build is evaluated at (authored 10).</summary>
    public int ReferenceParBuildLevel { get; }

    /// <summary>🔒 `29` §2.1 — the statline that <b>defines</b> <c>K_POWER</c> by <c>PlayerPower := 1000</c>.</summary>
    public StatLine ReferenceParBuild { get; }

    /// <summary>`29` §2.1 — the <c>1000</c> the reference build is defined to score.</summary>
    public double ReferenceParBuildTargetPower { get; }

    /// <summary>`05` §9's five build archetypes, in authored order. <c>id</c> and <c>stats</c> only.</summary>
    public IReadOnlyList<BuildArchetype> Archetypes { get; }

    /// <summary>`29` §2.5.3 — the stats the scalar multiplies. Authored <c>maxHp</c>, <c>atk</c>, <c>def</c>.</summary>
    public IReadOnlyList<StatId> ScaledStats { get; }

    /// <summary>`29` §2.5.3 — <em>"to within 0.1%"</em>. Authored 0.001.</summary>
    public double BisectionTolerance { get; }

    /// <summary>`29` §2.5.3 — <em>"round <c>s</c> to 4 dp"</em>.</summary>
    public int ScalarDecimalPlaces { get; }

    /// <summary>`29` §2.5.3 — the evaluation level when a loadout targets no content. Authored 40.</summary>
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

            // 🔒 id and stats ONLY. See the type remarks: frozenPerks and pets name ids that do not
            // exist until M3-07 / M4-07 and this reader must not even look at them.
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

/// <summary>
/// 🔒 One of `05` §9's five build archetypes, reduced to what this milestone may honestly read.
/// </summary>
/// <param name="Id">The authored id, e.g. <c>ARCH_CRIT</c>.</param>
/// <param name="Stats">The fourteen-stat loadout, as authored.</param>
/// <remarks>
/// There is deliberately no <c>Perks</c> or <c>Pets</c> member. See <see cref="CalibrationBuilds"/>'
/// remarks: a member here would be a place for someone to later resolve an id that does not exist.
/// </remarks>
public sealed record BuildArchetype(string Id, StatLine Stats);
