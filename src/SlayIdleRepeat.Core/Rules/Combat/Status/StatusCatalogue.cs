using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The four kinds of status.
/// </summary>
/// <remarks>
/// Not a label: the one-second cadence is driven by exactly two of these four
/// (<see cref="DoT"/> and <see cref="HoT"/>), which route differently — a DoT tick is a damage
/// event, a HoT tick routes through <c>Heal()</c>. A <see cref="Debuff"/> or <see cref="Buff"/> has
/// no cadence at all.
/// </remarks>
internal enum StatusKind
{
    /// <summary>Damage over time. <c>BURN</c>, <c>POISON</c>, <c>BLEED</c>.</summary>
    DoT = 1,

    /// <summary>A negative modifier. <c>FREEZE</c>, <c>STUN</c>, <c>WEAKEN</c>, <c>SUNDER</c>, <c>SPORE</c>.</summary>
    Debuff = 2,

    /// <summary>A positive modifier. <c>RAGE</c>, <c>WARD</c>, <c>HASTE</c>.</summary>
    Buff = 3,

    /// <summary>Healing over time. <c>REGEN</c>, and only <c>REGEN</c>.</summary>
    HoT = 4,
}

/// <summary>
/// What a status's magnitude is a fraction of.
/// </summary>
/// <remarks>
/// The sign belongs to the authored number, not to the basis: <c>WEAKEN</c> and <c>RAGE</c> use the
/// same unit with opposite signs, and content already authors potencies signed. A basis that carried
/// the sign would negate them twice.
/// </remarks>
internal enum StatusPotencyBasis
{
    /// <summary>
    /// A percentage of the applier's ATK per second. Read off the applier at application and fixed
    /// there, which is what makes it bypass DEF mitigation.
    /// </summary>
    ApplierAtkPctPerSecond = 1,

    /// <summary>A percentage of the target's Max HP per second.</summary>
    TargetMaxHpPctPerSecond = 2,

    /// <summary>
    /// A signed percentage of one of the target's stats. <see cref="StatusDefinition.Stat"/> names which.
    /// </summary>
    TargetStatPct = 3,

    /// <summary>A flat HP amount, e.g. <c>WARD</c>'s absorb shield.</summary>
    FlatHp = 4,

    /// <summary>
    /// No magnitude at all, e.g. <c>STUN</c>. Named rather than left as a zero, since a zero potency
    /// is a real potency.
    /// </summary>
    None = 5,
}

/// <summary>
/// One row of the status table, with every authored parameter.
/// </summary>
/// <remarks>
/// The per-application value and duration are deliberately absent: they belong to the applying
/// effect, and a copy here would be a second, disagreeing statement of every perk's numbers.
/// </remarks>
/// <param name="Id">The status id, one of the thirteen.</param>
/// <param name="Kind">The Type column.</param>
/// <param name="Basis">What the magnitude is a fraction of.</param>
/// <param name="Stat">
/// The stat a <see cref="StatusPotencyBasis.TargetStatPct"/> status writes; <c>null</c> for every
/// other basis.
/// </param>
/// <param name="FixedPotency">
/// The potency stated as a literal rather than as the effect's value. <c>FREEZE</c> is the only such
/// row; <c>null</c> everywhere else.
/// </param>
/// <param name="ScalesWithTargetMissingHp">
/// <c>BLEED</c> alone: each tick scales by <c>(1 + target's missing-HP fraction)</c>. The
/// coefficient is <see cref="StatusCatalogue.BleedMissingHpScaling"/>.
/// </param>
/// <param name="Stacking">
/// The stacking rule for the five statuses that have one; <c>null</c> for the rest, where the
/// per-effect block governs — see <see cref="StatusCatalogue.StackingFor"/>.
/// </param>
/// <param name="DecayCurve">
/// <c>RAGE</c> decays over its duration but no curve is authored; <c>null</c> means none is
/// authorised. See <see cref="RequireDecayCurve"/>.
/// </param>
internal sealed record StatusDefinition(
    string Id,
    StatusKind Kind,
    StatusPotencyBasis Basis,
    StatId? Stat,
    double? FixedPotency,
    bool ScalesWithTargetMissingHp,
    EffectStacking? Stacking,
    string? DecayCurve)
{
    /// <summary>
    /// Whether the one-second cadence drives this status.
    /// </summary>
    internal bool Ticks => Kind is StatusKind.DoT or StatusKind.HoT;

    /// <summary>
    /// The stat a <see cref="StatusPotencyBasis.TargetStatPct"/> row writes, or a throw naming the row.
    /// </summary>
    /// <exception cref="InvalidOperationException">The basis names no stat.</exception>
    internal StatId RequireStat() =>
        Stat ?? throw new InvalidOperationException(
            $"05 §5's {Id} has basis {Basis}, which names no stat. Only TargetStatPct rows write a " +
            "stat — FREEZE and HASTE write ASPD, WEAKEN and RAGE write ATK, SUNDER writes DEF and " +
            "SPORE writes HEAL_PCT. Asking any other row for one is a caller that did not check the " +
            "basis first, and substituting a stat here would silently debuff the wrong one.");

    /// <summary>
    /// <c>RAGE</c>'s decay curve, or a throw naming the hole.
    /// </summary>
    /// <remarks>
    /// No curve is authored anywhere in the content set, so <c>RAGE</c> ships holding its full
    /// potency for its duration — the missing decay is a <c>null</c> rather than an invented,
    /// invisible linear ramp. Tracked in <c>SubjectSetFloorTests.Pending</c> under
    /// <c>StatusDecayCurve</c> until a milestone rules on it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No curve is authorised.</exception>
    internal string RequireDecayCurve() =>
        DecayCurve ?? throw new InvalidOperationException(
            $"05 §5 authorises no decay curve for {Id}. It says the status 'decays over D s' and " +
            "names no shape for the decay, and no boss script, perk or on-hit row authors one " +
            "either. 16 R6: a plausible linear ramp would be a balance decision invented here and " +
            "invisible everywhere afterwards, so this fails instead. See the remarks on this " +
            "member for the register entry that expires when a milestone rules on it.");

    /// <inheritdoc />
    public override string ToString() =>
        $"{Id} ({Kind}, {Basis}" +
        (Stat is { } stat ? $" {stat}" : string.Empty) +
        (FixedPotency is { } fixedPotency
            ? $", fixed {fixedPotency.ToString("R", CultureInfo.InvariantCulture)}"
            : string.Empty) +
        $", stacking {Stacking?.Mode.ToString() ?? "unstated by 05 §5"})";
}

/// <summary>
/// <c>content/statuses.json</c>, read.
/// </summary>
/// <remarks>
/// The cadence itself is not in the data: it is derived by <see cref="StatusCadence"/> from the
/// tick rate rather than duplicated as a second number that could drift from it.
/// </remarks>
internal sealed record StatusCatalogue(
    double BleedMissingHpScaling,
    double StunMaxSecondsPerApplication,
    double StunImmunityWindowSeconds,
    IReadOnlyList<StatusDefinition> Statuses)
{
    /// <summary>The snapshot-relative path of the document.</summary>
    internal const string Document = "content/statuses.json";

    /// <summary>
    /// Exactly thirteen statuses are fixed — 05 §5's twelve, plus the CHILL the ailment rework
    /// added. Asserted at load — see <see cref="Read"/>.
    /// </summary>
    internal const int ExpectedStatusCount = 13;

    /// <summary>The status table itself, the pointer every row fault is stated against.</summary>
    internal const string StatusesPointer = Document + "#/statuses";

    /// <summary><c>BLEED</c>'s missing-HP scaling term.</summary>
    internal const string BleedMissingHpScalingPointer = Document + "#/bleedMissingHpScaling";

    /// <summary>The per-application STUN cap.</summary>
    internal const string StunMaxSecondsPointer = Document + "#/stun/maxSecondsPerApplication";

    /// <summary>The STUN immunity window after.</summary>
    internal const string StunImmunityWindowPointer = Document + "#/stun/immunityWindowSeconds";

    /// <summary><c>RAGE</c>'s unauthorised decay curve. See <see cref="StatusDefinition.RequireDecayCurve"/>.</summary>
    internal const string RageDecayCurvePointer = Document + "#/statuses/8/decayCurve";

    private static readonly IReadOnlyDictionary<string, StatusKind> Kinds =
        new Dictionary<string, StatusKind>(StringComparer.Ordinal)
        {
            ["DOT"] = StatusKind.DoT,
            ["DEBUFF"] = StatusKind.Debuff,
            ["BUFF"] = StatusKind.Buff,
            ["HOT"] = StatusKind.HoT,
        };

    private static readonly IReadOnlyDictionary<string, StatusPotencyBasis> Bases =
        new Dictionary<string, StatusPotencyBasis>(StringComparer.Ordinal)
        {
            ["APPLIER_ATK_PCT_PER_SECOND"] = StatusPotencyBasis.ApplierAtkPctPerSecond,
            ["TARGET_MAX_HP_PCT_PER_SECOND"] = StatusPotencyBasis.TargetMaxHpPctPerSecond,
            ["TARGET_STAT_PCT"] = StatusPotencyBasis.TargetStatPct,
            ["FLAT_HP"] = StatusPotencyBasis.FlatHp,
            ["NONE"] = StatusPotencyBasis.None,
        };

    private static readonly IReadOnlyDictionary<string, StatId> Stats =
        new Dictionary<string, StatId>(StringComparer.Ordinal)
        {
            ["ATK"] = StatId.ATK,
            ["DEF"] = StatId.DEF,
            ["ASPD"] = StatId.ASPD,
            ["HEAL_PCT"] = StatId.HEAL_PCT,
        };

    private static readonly IReadOnlyDictionary<string, StackingMode> Modes =
        new Dictionary<string, StackingMode>(StringComparer.Ordinal)
        {
            ["ADDITIVE"] = StackingMode.ADDITIVE,
            ["NONE"] = StackingMode.NONE,
        };

    /// <summary>
    /// The canonical stacking block, used when neither the applying effect nor the status states one.
    /// </summary>
    /// <remarks>
    /// This is a different question from "what stack ceiling is authorised for FREEZE" (which has no
    /// answer and stays <c>null</c> in enemy content): it is what an effect uses when it authors no
    /// stacking block at all, which the DSL answers with <c>{"mode": "ADDITIVE", "maxStacks": 1}</c>.
    /// </remarks>
    internal static EffectStacking CanonicalStacking { get; } =
        new() { Mode = StackingMode.ADDITIVE, MaxStacks = 1 };

    /// <summary>
    /// The thirteen, indexed by id.
    /// </summary>
    internal IReadOnlyDictionary<string, StatusDefinition> ById { get; } =
        Statuses.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>One status by id.</summary>
    /// <exception cref="EffectContextException">
    /// The id is outside the thirteen. <c>StatusOps</c> deliberately does not validate the id, so an
    /// effect built in code rather than loaded from JSON arrives here unchecked.
    /// </exception>
    internal StatusDefinition Of(string statusId) =>
        ById.TryGetValue(statusId, out var found)
            ? found
            : throw new EffectContextException(
                statusId,
                "it is not one of the authored statuses",
                "05 §5 fixes BURN, POISON, BLEED, FREEZE, STUN, WEAKEN, SUNDER, SPORE, RAGE, WARD, " +
                "HASTE and REGEN, the ailment rework adds CHILL, and game-data/schema/effect.schema.json " +
                "encloses the same set. A fourteenth reaching here came from an effect built in code rather than loaded from " +
                "JSON, which is outside that enforcement.");

    /// <summary>
    /// The stacking block one application uses: the effect's, else the status's, else the canonical one.
    /// </summary>
    /// <remarks>
    /// A per-effect block is more specific than the status's own rule, so an effect that authors one wins.
    /// </remarks>
    /// <param name="statusId">The status being applied.</param>
    /// <param name="authored">The applying effect's own block, or <c>null</c>.</param>
    internal EffectStacking StackingFor(string statusId, EffectStacking? authored) =>
        authored ?? Of(statusId).Stacking ?? CanonicalStacking;

    /// <summary>Reads the whole document.</summary>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A value is <c>null</c> where one is required.</exception>
    /// <exception cref="ContentTypeMismatchException">
    /// A row is malformed, duplicated, or outside one of the closed vocabularies, or the table is
    /// not the expected number of rows.
    /// </exception>
    internal static StatusCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = content.GetDocument(Document).Root;
        if (!rows.TryGetMember("statuses", out var list) || list!.Kind != ContentValueKind.Array)
        {
            throw new ContentTypeMismatchException(
                StatusesPointer,
                list?.Kind ?? ContentValueKind.Unauthorised,
                "the 'statuses' array. 05 §5's table is the whole point of the " +
                "file, and its schema requires exactly thirteen rows");
        }

        var statuses = new List<StatusDefinition>(list.Items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < list.Items.Count; index++)
        {
            var pointer = RowPointer(index);
            var row = Row(list.Items[index], pointer);

            // Refused by name here: ToDictionary's own failure names neither the document nor the
            // id, and the schema's uniqueItems compares whole objects, so two rows sharing an id
            // with different bodies would pass it.
            if (!seen.Add(row.Id))
            {
                throw new ContentTypeMismatchException(
                    pointer,
                    ContentValueKind.Object,
                    $"a status this document declares once — it declares '{row.Id}' twice. 05 §3.1 " +
                    "is 'one instance per statusId per target', which needs the id to identify one " +
                    "row; the schema's uniqueItems compares whole rows and cannot see two that " +
                    "differ elsewhere");
            }

            statuses.Add(row);
        }

        // Asserted here and not only in the schema: a snapshot built without schema validation
        // (every in-code fixture) would otherwise load a partial catalogue silently.
        if (statuses.Count != ExpectedStatusCount)
        {
            throw new ContentTypeMismatchException(
                StatusesPointer,
                ContentValueKind.Array,
                "exactly thirteen statuses — it carries " +
                $"{statuses.Count.ToString(CultureInfo.InvariantCulture)}. " +
                "05 §5 fixes twelve and the ailment rework adds CHILL, and the schema declares minItems and maxItems 13; a " +
                "catalogue that is short fails later, mid-battle, as an unknown-status error");
        }

        return new StatusCatalogue(
            content.ReadDouble(BleedMissingHpScalingPointer),
            content.ReadDouble(StunMaxSecondsPointer),
            content.ReadDouble(StunImmunityWindowPointer),
            statuses);
    }

    /// <summary>The content pointer of one row — what every fault below is stated against.</summary>
    private static string RowPointer(int index) =>
        $"{StatusesPointer}/{index.ToString(CultureInfo.InvariantCulture)}";

    private static StatusDefinition Row(ContentValue row, string pointer)
    {
        var id = Text(row, "id", pointer);

        return new StatusDefinition(
            id,
            Lookup(Kinds, Text(row, "type", pointer), "05 §5's Type column", id, $"{pointer}/type"),
            Lookup(
                Bases,
                Text(row, "potencyBasis", pointer),
                "05 §5's potency units",
                id,
                $"{pointer}/potencyBasis"),
            row.TryGetMember("stat", out var stat) && stat!.Kind == ContentValueKind.Text

                // Through the same Lookup as type and potencyBasis, not Enum.Parse: Enum.Parse would
                // silently accept any of the fourteen StatIds where the schema admits four.
                ? Lookup(Stats, stat.AsText(), "05 §5's four debuffable stats", id, $"{pointer}/stat")
                : null,
            row.TryGetMember("fixedPotency", out var fixedPotency) &&
            fixedPotency!.Kind == ContentValueKind.Number
                ? fixedPotency.AsDouble()
                : null,
            row.TryGetMember("scalesWithTargetMissingHp", out var scales) &&
            scales!.Kind == ContentValueKind.Boolean && scales.AsBoolean(),
            Stacking(row, pointer),

            // The tree is walked rather than the value read through a pointer, which is what keeps
            // RAGE's authored null a null instead of throwing UnauthorisedTunableException at load.
            row.TryGetMember("decayCurve", out var decay) && decay!.Kind == ContentValueKind.Text
                ? decay.AsText()
                : null);
    }

    private static EffectStacking? Stacking(ContentValue row, string pointer)
    {
        if (!row.TryGetMember("stacking", out var stacking) || stacking!.Kind != ContentValueKind.Object)
        {
            return null;
        }

        return new EffectStacking
        {
            Mode = Lookup(
                Modes,
                Text(stacking, "mode", $"{pointer}/stacking"),
                "18 §6's stacking modes",
                Text(row, "id", pointer),
                $"{pointer}/stacking/mode"),
            MaxStacks = stacking.TryGetMember("maxStacks", out var max) &&
                        max!.Kind == ContentValueKind.Number
                ? max.AsInt32()
                : null,
            RefreshOnReapply = stacking.TryGetMember("refreshOnReapply", out var refresh) &&
                               refresh!.Kind == ContentValueKind.Boolean
                ? refresh.AsBoolean()
                : null,
        };
    }

    private static string Text(ContentValue value, string member, string pointer) =>
        value.TryGetMember(member, out var found) && found!.Kind == ContentValueKind.Text
            ? found.AsText()
            : throw new ContentTypeMismatchException(
                $"{pointer}/{member}",
                found?.Kind ?? ContentValueKind.Unauthorised,
                $"a '{member}'. Its schema requires the key, so a row " +
                "reaching here without one was not validated against that schema");

    private static TValue Lookup<TValue>(
        IReadOnlyDictionary<string, TValue> table, string token, string what, string id, string pointer)
        where TValue : struct
    {
        if (table.TryGetValue(token, out var found))
        {
            return found;
        }

        throw new ContentTypeMismatchException(
            pointer,
            ContentValueKind.Text,
            $"one of {what} — the {id} row carries '{token}'. The schema " +
            "encloses the set, so a token reaching here was not validated against it — and the " +
            $"vocabulary in code is the one 05 §5 states: {string.Join(", ", table.Keys)}");
    }
}
