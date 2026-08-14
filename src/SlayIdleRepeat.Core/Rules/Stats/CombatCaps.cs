using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 <c>content/combat_caps.json</c>, read — the constants `05` §1–2, §4, §4.1 and `11` §4.3 put
/// in data.
/// </summary>
/// <remarks>
/// <para>
/// `05` §1.1: <em>"📐 TUNABLE: every cap above lives in <c>res://data/combat_caps.json</c>"</em>;
/// `05` §4.1 puts <c>wardCapPct</c> there; `05` §4 says of the two mitigation dials <em>"expose them
/// in data"</em>; `11` §4.3 names <c>pvpMaxFightSeconds</c> in the same file. The repository path is
/// <c>game-data/content/combat_caps.json</c> — a single document under <c>content/</c>, not a
/// seventeenth <c>tuning/</c> file, because doc 21's catalogue fixes that directory at sixteen and
/// these are balance constants rather than economy dials.
/// </para>
/// <para>
/// 🔒 <b>Nothing here has a default.</b> Every value is read through
/// <see cref="ContentSnapshot.ReadDouble"/>, which throws <c>UnauthorisedTunableException</c> on a
/// <c>null</c> and <c>MissingContentException</c> on an absent pointer. That is deliberate and it is
/// <c>game-data/README.md</c>'s rule: <em>"<c>null</c> means the design docs do not authorise a value
/// here. It is never a legitimate runtime value."</em> A cap that quietly defaulted to 1.0 would
/// uncap crit; one that defaulted to 0 would delete it.
/// </para>
/// <para>
/// The pointers are written out one per constant rather than assembled from a loop, so that
/// <c>grep combat_caps.json</c> over the source finds every read of the file.
/// </para>
/// <para>
/// ⚠️ <b>Why a document reader lives under <c>Rules/Stats/</c> rather than under <c>Content/</c>,
/// recorded because it looks wrong.</b> Two of its five members — <see cref="PvpMaxFightSeconds"/>
/// (`11` §4.3, M2-14's) and <see cref="Mitigation"/> (`05` §4, M2-09's) — are not stats, and `30`
/// §11.4 puts definition types in <c>Content</c>. It cannot go there: it composes
/// <see cref="StatCaps"/> and <see cref="HeroBaseCurve"/>, which are <c>Rules</c>, and
/// <c>Core_internal_layering_holds</c> forbids <c>Content</c> from naming <c>Rules</c>. Splitting it
/// so that each constant sits beside its consumer would mean four readers of one document and four
/// places to forget a pointer. The consequence, stated so M2-09 and M2-14 are not surprised by it:
/// they read their constants <em>sideways</em>, out of <c>Rules/Stats</c>. If that becomes
/// uncomfortable, the move that works is the whole of <c>Rules/Stats</c>'s value types
/// (<see cref="ActorStats"/>, <see cref="StatCaps"/>, <see cref="HeroBaseCurve"/>,
/// <see cref="StatRounding"/>) going to <c>Content</c> together — which is a milestone decision, not
/// a file move, and is recorded as errata for the conductor.
/// </para>
/// </remarks>
internal sealed record CombatCaps(
    StatCaps Caps,
    HeroBaseCurve HeroBase,
    double WardCapPct,
    double PvpMaxFightSeconds,
    MitigationConstants Mitigation)
{
    /// <summary>The snapshot-relative path of the document (`05` §1.1, `11` §4.3).</summary>
    internal const string Document = "content/combat_caps.json";

    /// <summary>`05` §4.1 — the ward pool ceiling, as a fraction of post-step-7 Max HP.</summary>
    internal const string WardCapPctPointer = Document + "#/wardCapPct";

    /// <summary>`11` §4.3 — the duel duration cap, overriding `05` §3's 90 s default.</summary>
    internal const string PvpMaxFightSecondsPointer = Document + "#/pvpMaxFightSeconds";

    /// <summary>`05` §4 — the flat term of the mitigation denominator.</summary>
    internal const string MitigationFlatConstantPointer = Document + "#/mitigation/flatConstant";

    /// <summary>`05` §4 — the per-attacker-level term of the mitigation denominator.</summary>
    internal const string MitigationPerLevelConstantPointer = Document + "#/mitigation/perLevelConstant";

    /// <summary>`05` §2 — the lower Legend Level bound of the hero base curve.</summary>
    internal const string LegendLevelMinPointer = Document + "#/heroBaseStats/legendLevelMin";

    /// <summary>`05` §2 — the upper Legend Level bound of the hero base curve.</summary>
    internal const string LegendLevelMaxPointer = Document + "#/heroBaseStats/legendLevelMax";

    /// <summary>
    /// 🔒 The six stats `05` §1's Notes column caps, and only those six. Stated here rather than
    /// derived from whatever keys the file happens to hold: a cap silently disappearing from the
    /// data would otherwise read as "uncapped" — which is a legitimate state for the other eight
    /// stats and therefore indistinguishable from a deletion.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A <see cref="List{T}"/> initialiser, not <c>[ … ]</c> and not <c>new[] { … }</c>.</b>
    /// The same trap <see cref="Content.Effects.EffectCondition"/> records, one step further on. The
    /// collection expression targeting <see cref="IReadOnlyList{T}"/> synthesises
    /// <c>&lt;&gt;z__ReadOnlyArray`1</c> in the <b>global</b> namespace, and an array initialiser of
    /// six or more constants additionally emits a <c>&lt;PrivateImplementationDetails&gt;</c> blob
    /// there (<c>__StaticArrayInitTypeSize=24</c>) for <c>RuntimeHelpers.InitializeArray</c>. Neither
    /// carries a <c>CompilerGeneratedAttribute</c>, and
    /// <c>AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace</c> — which
    /// filters on that attribute — reports both as undocumented Core namespaces. A list initialiser
    /// compiles to <c>Add</c> calls and leaves no type behind.
    /// </remarks>
    internal static IReadOnlyList<StatId> CappedStats { get; } = new List<StatId>
    {
        StatId.CRIT, StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DR_PCT,
    };

    /// <summary>The pointer holding a stat's cap (`05` §1).</summary>
    internal static string CapPointer(StatId stat) => $"{Document}#/caps/{stat}";

    /// <summary>The pointer holding a stat's base-curve intercept (`05` §2).</summary>
    internal static string HeroBasePointer(StatId stat) => $"{Document}#/heroBaseStats/stats/{stat}/base";

    /// <summary>The pointer holding a stat's base-curve slope (`05` §2).</summary>
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
/// 🔒 `05` §4's two balance dials: <c>mitigation = effDef / (effDef + Flat + PerLevel × attacker.Level)</c>.
/// </summary>
/// <remarks>
/// <para>
/// `05` §4: <em>"the <c>120</c> and <c>20</c> constants are the two most important balance dials in
/// the game. Expose them in data."</em> They are the reason `05` §4's own sanity check works out —
/// DEF 120 against a level-1 attacker mitigates 0.46, DEF 600 mitigates 0.81 — and the
/// <c>20 × attackerLevel</c> term is what keeps defence needing to grow.
/// </para>
/// <para>
/// ⚠️ <b>The constants only. The formula is `05` §4's and belongs to M2-09</b>, which owns the
/// ten-step <c>ResolveAttack</c> pipeline. Restating it here would put one of the game's two most
/// important dials in two places at once, which is the exact failure the mirror rule below already
/// exists to prevent in data.
/// </para>
/// <para>
/// 🔒 The same pair is authored twice: `29` §2.3's <c>MitigationVsReference</c> uses them and they
/// live at <c>tuning/power_model.json#/mitigation</c>. A <c>DeclaredRules</c> mirror rule fails the
/// content build if the two copies disagree — if they ever did, `05` §9's assertion A10 (the closed
/// form tracking <c>EmpiricalPower</c> within ±12%) would be comparing two different games.
/// </para>
/// </remarks>
/// <param name="Flat">`05` §4's <c>120</c> term.</param>
/// <param name="PerLevel">`05` §4's <c>20</c> term, multiplied by the attacker's Level.</param>
internal readonly record struct MitigationConstants(double Flat, double PerLevel)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"effDef / (effDef + {Flat.ToString("R", CultureInfo.InvariantCulture)} + " +
        $"{PerLevel.ToString("R", CultureInfo.InvariantCulture)} * attackerLevel)";
}
