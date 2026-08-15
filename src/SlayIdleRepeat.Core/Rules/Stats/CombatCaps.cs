using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// <c>content/combat_caps.json</c>, read — the balance constants that live in data rather than code.
/// </summary>
/// <remarks>
/// Nothing here has a default: every value is read through <see cref="ContentSnapshot.ReadDouble"/>,
/// which throws on a <c>null</c> or an absent pointer, since a cap that quietly defaulted to 1.0
/// would uncap crit and one that defaulted to 0 would delete it. Pointers are written out one per
/// constant rather than assembled from a loop, so a source search for the file's name finds every
/// read of it. Lives under <c>Rules/Stats/</c> rather than <c>Content/</c> because two of its members
/// (<see cref="PvpMaxFightSeconds"/>, <see cref="Mitigation"/>) aren't stats, and this type composes
/// <see cref="StatCaps"/> and <see cref="HeroBaseCurve"/>, which live in <c>Rules</c> and can't be
/// referenced from <c>Content</c>.
/// </remarks>
internal sealed record CombatCaps(
    StatCaps Caps,
    HeroBaseCurve HeroBase,
    double WardCapPct,
    double PvpMaxFightSeconds,
    MitigationConstants Mitigation)
{
    /// <summary>The snapshot-relative path of the document.</summary>
    internal const string Document = "content/combat_caps.json";

    /// <summary>The ward pool ceiling, as a fraction of post-aggregation Max HP.</summary>
    internal const string WardCapPctPointer = Document + "#/wardCapPct";

    /// <summary>The duel duration cap, overriding the ordinary fight timeout.</summary>
    internal const string PvpMaxFightSecondsPointer = Document + "#/pvpMaxFightSeconds";

    /// <summary>The flat term of the mitigation denominator.</summary>
    internal const string MitigationFlatConstantPointer = Document + "#/mitigation/flatConstant";

    /// <summary>The per-attacker-level term of the mitigation denominator.</summary>
    internal const string MitigationPerLevelConstantPointer = Document + "#/mitigation/perLevelConstant";

    /// <summary>The lower Legend Level bound of the hero base curve.</summary>
    internal const string LegendLevelMinPointer = Document + "#/heroBaseStats/legendLevelMin";

    /// <summary>The upper Legend Level bound of the hero base curve.</summary>
    internal const string LegendLevelMaxPointer = Document + "#/heroBaseStats/legendLevelMax";

    /// <summary>
    /// The six stats that carry a cap, and only those six. Stated here rather than derived from
    /// whatever keys the file happens to hold, since a cap silently disappearing from the data would
    /// otherwise read as "uncapped" — a legitimate state for the other eight stats.
    /// </summary>
    /// <remarks>
    /// A <see cref="List{T}"/> initialiser rather than a collection expression or array initialiser:
    /// both of those synthesize an undocumented global-namespace type for a constant list this size,
    /// which trips this project's namespace-boundary test. A list initialiser compiles to
    /// <c>Add</c> calls and leaves no type behind.
    /// </remarks>
    internal static IReadOnlyList<StatId> CappedStats { get; } = new List<StatId>
    {
        StatId.CRIT, StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DR_PCT,
    };

    /// <summary>The pointer holding a stat's cap.</summary>
    internal static string CapPointer(StatId stat) => $"{Document}#/caps/{stat}";

    /// <summary>The pointer holding a stat's base-curve intercept.</summary>
    internal static string HeroBasePointer(StatId stat) => $"{Document}#/heroBaseStats/stats/{stat}/base";

    /// <summary>The pointer holding a stat's base-curve slope.</summary>
    internal static string HeroPerLevelPointer(StatId stat) => $"{Document}#/heroBaseStats/stats/{stat}/perLevel";

    /// <summary>Reads the whole document.</summary>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A value is <c>null</c> in the data.</exception>
    internal static CombatCaps Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var maxima = new Dictionary<StatId, double>(CappedStats.Count);
        foreach (var stat in CappedStats)
        {
            maxima[stat] = content.ReadDouble(CapPointer(stat));
        }

        var rows = new Dictionary<StatId, (double Base, double PerLevel)>(StatIds.Combat.Count);
        foreach (var stat in StatIds.Combat)
        {
            rows[stat] = (content.ReadDouble(HeroBasePointer(stat)), content.ReadDouble(HeroPerLevelPointer(stat)));
        }

        return new CombatCaps(
            StatCaps.From(maxima),
            HeroBaseCurve.From(rows, content.ReadInt32(LegendLevelMinPointer), content.ReadInt32(LegendLevelMaxPointer)),
            content.ReadDouble(WardCapPctPointer),
            content.ReadDouble(PvpMaxFightSecondsPointer),
            new MitigationConstants(
                content.ReadDouble(MitigationFlatConstantPointer),
                content.ReadDouble(MitigationPerLevelConstantPointer)));
    }
}

/// <summary>
/// The two balance dials: <c>mitigation = effDef / (effDef + Flat + PerLevel × attacker.Level)</c>.
/// </summary>
/// <remarks>
/// Constants only — the mitigation formula itself lives with the attack-resolution pipeline, so
/// restating it here would put one of the game's most important dials in two places at once. The
/// same pair is also authored at <c>tuning/power_model.json#/mitigation</c> for the power-model
/// reference curve; a content-build rule fails if the two copies disagree.
/// </remarks>
/// <param name="Flat">The mitigation denominator's flat term.</param>
/// <param name="PerLevel">The mitigation denominator's per-attacker-level term.</param>
internal readonly record struct MitigationConstants(double Flat, double PerLevel)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"effDef / (effDef + {Flat.ToString("R", CultureInfo.InvariantCulture)} + " +
        $"{PerLevel.ToString("R", CultureInfo.InvariantCulture)} * attackerLevel)";
}
