using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §5's <b>Type</b> column, verbatim — the four kinds of status the section names.
/// </summary>
/// <remarks>
/// It is not a label. `05` §3.1's one-second cadence is driven by exactly two of these four
/// (<see cref="DoT"/> and <see cref="HoT"/>), and the cadence rule then routes them differently:
/// a DoT tick is <em>"a damage event, not an attack"</em> while a HoT tick <em>"routes through
/// <c>Heal()</c> (§4.3)"</em>. A <see cref="Debuff"/> or a <see cref="Buff"/> has no cadence at all.
/// </remarks>
internal enum StatusKind
{
    /// <summary>`05` §5 — damage over time. <c>BURN</c>, <c>POISON</c>, <c>BLEED</c>.</summary>
    DoT = 1,

    /// <summary>`05` §5 — a negative modifier. <c>FREEZE</c>, <c>STUN</c>, <c>WEAKEN</c>, <c>SUNDER</c>, <c>SPORE</c>.</summary>
    Debuff = 2,

    /// <summary>`05` §5 — a positive modifier. <c>RAGE</c>, <c>WARD</c>, <c>HASTE</c>.</summary>
    Buff = 3,

    /// <summary>`05` §5 — healing over time. <c>REGEN</c>, and only <c>REGEN</c>.</summary>
    HoT = 4,
}

/// <summary>
/// 🔒 `05` §5 — what a status's <c>X</c> is a fraction of.
/// </summary>
/// <remarks>
/// <para>
/// One member per unit `05` §5 writes, and no more. It is the status-catalogue counterpart of
/// <c>Rules.Combat.Enemies.PotencyBasis</c>, which types the same quantity for `05` §6.1a's on-hit
/// rows — that section defers its stacking and its units to <em>"the status catalogue"</em>, which is
/// this. <c>StatusCatalogueTests</c> pins the two vocabularies against each other rather than leaving
/// two enums that must agree with nothing making them.
/// </para>
/// <para>
/// 🔒 <b>The sign belongs to the authored number, not to the basis.</b> `05` §5 writes
/// <c>WEAKEN</c> as <em>−X% ATK</em> and <c>RAGE</c> as <em>+X% ATK</em> against the same unit, and
/// <c>content/enemies/enemies.json</c> already authors its potencies signed
/// (<c>SUNDER −0.05</c>, <c>FREEZE −0.5</c>, <c>SPORE −0.1</c>, <c>BURN +0.3</c>). A basis that
/// carried the sign would negate them twice.
/// </para>
/// </remarks>
internal enum StatusPotencyBasis
{
    /// <summary>
    /// `05` §5 — <c>BURN</c>'s <em>"X% of attacker ATK per second"</em> and <c>BLEED</c>'s
    /// <em>"X% of the applier's ATK"</em>. 🔒 Read off the <b>applier</b> at application and fixed
    /// there, which is what makes `05` §3.1's <em>"DEF mitigation does not apply"</em> true.
    /// </summary>
    ApplierAtkPctPerSecond = 1,

    /// <summary>
    /// `05` §5 — <c>POISON</c>'s <em>"X% of target Max HP per second"</em> and <c>REGEN</c>'s
    /// <em>"Heal X% Max HP per second"</em>.
    /// </summary>
    TargetMaxHpPctPerSecond = 2,

    /// <summary>
    /// `05` §5 — a signed percentage of one of the target's stats, which is how the section states
    /// six of the twelve. <see cref="StatusDefinition.Stat"/> names which.
    /// </summary>
    TargetStatPct = 3,

    /// <summary>
    /// `05` §5 — <c>WARD</c>'s <em>"absorb shield, flat HP amount"</em>. 🔒 The pool itself is
    /// `05` §4.1's and <b>M2-09</b>'s; this basis only says what the number means.
    /// </summary>
    FlatHp = 4,

    /// <summary>
    /// `05` §5 — <c>STUN</c>, which is <em>"cannot act for D s"</em> and carries no magnitude at
    /// all. Named rather than left as a zero, because a zero potency is a real potency.
    /// </summary>
    None = 5,
}

/// <summary>
/// 🔒 One row of `05` §5's status table, with every parameter that section states and none it does not.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The per-application <c>X</c> and <c>D</c> are deliberately absent.</b> `05` §5 writes them
/// as <c>X</c> and <c>D</c> because they belong to the effect that applies the status — its
/// <c>value</c> and its <c>duration</c> (`18` §1, §6). A copy here would be a second, disagreeing
/// statement of every perk's numbers, and <c>StatusOps.Apply</c> already passes both through.
/// </para>
/// </remarks>
/// <param name="Id">`05` §5 — the status id, one of the twelve.</param>
/// <param name="Kind">`05` §5's Type column.</param>
/// <param name="Basis">`05` §5 — what <c>X</c> is a fraction of.</param>
/// <param name="Stat">
/// `05` §5 — the stat a <see cref="StatusPotencyBasis.TargetStatPct"/> status writes;
/// <c>null</c> for every other basis, which names no stat.
/// </param>
/// <param name="FixedPotency">
/// 🔒 `05` §5 — the potency the section states as a <b>literal</b> rather than as <c>X</c>.
/// <c>FREEZE</c> is the only such row (<em>"−50% ASPD"</em>), so the number is the status's and an
/// effect that authored its own would be overriding a constant `05` §5 fixed. <c>null</c> everywhere
/// else, where <c>X</c> is the effect's value.
/// </param>
/// <param name="ScalesWithTargetMissingHp">
/// 📐 `05` §5 — <c>BLEED</c> alone: <em>"each tick deals that amount × (1 + target's missing-HP
/// fraction)"</em>. The coefficient is <see cref="StatusCatalogue.BleedMissingHpScaling"/>.
/// </param>
/// <param name="Stacking">
/// `05` §5's stacking rule for the five statuses it states one for, and <c>null</c> for the seven it
/// does not. 🔒 The <c>null</c> is a statement, not a hole: `05` §5 fixes no stacking for those
/// seven, so `18` §6's per-effect block governs — see <see cref="StatusCatalogue.StackingFor"/>.
/// </param>
/// <param name="DecayCurve">
/// 🔒 `05` §5 says <c>RAGE</c> <em>"decays over D s"</em> and states no curve; <c>null</c> means the
/// documents authorise none. See <see cref="RequireDecayCurve"/>.
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
    /// 🔒 Whether `05` §3.1's one-second cadence drives this status — <b>the</b> question the
    /// cadence engine asks of a row.
    /// </summary>
    /// <remarks>
    /// Exactly the two kinds `05` §3.1 names: <em>"every <c>DoT</c>/<c>HoT</c> instance whose cadence
    /// boundary falls on this tick applies its tick"</em>. A <c>Debuff</c> or a <c>Buff</c> has no
    /// per-second amount, so it has nothing for a cadence to land.
    /// </remarks>
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
    /// 🔒 <c>RAGE</c>'s decay curve, or a throw naming the hole.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>THE DEFERRAL IS REGISTERED, and this is the pointer to it.</b> `05` §5 states that
    /// <c>RAGE</c> <em>"decays over <c>D</c> s"</em> and authors no curve — not linear, not stepped,
    /// not exponential — and neither does any boss script, perk row or on-hit row in the content set.
    /// Steering S6 forbids inventing one, so <c>RAGE</c> ships holding its full potency for its
    /// duration, which is the only shape `18` §6 can express, and the missing decay is a <c>null</c>
    /// in <c>content/statuses.json</c> rather than an invisible linear ramp.
    /// <para>
    /// The obligation is recorded where the repository's one expiring register can fire on it —
    /// <c>SubjectSetFloorTests.Pending</c>, under the name <c>StatusDecayCurve</c> (steering S4).
    /// Read that entry before changing this: the subject being tracked is <em>"something gives
    /// <c>RAGE</c> its decay"</em>, not the string, so a milestone that picks another name should
    /// <b>rename</b> the entry rather than delete it. This remark is the inbound path, on
    /// <c>DurationScopes</c>' and <c>NoPetAbilities</c>' precedent: a note addressed to a future
    /// milestone is worthless in a test file that milestone will never open.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">`05` §5 authorises no curve.</exception>
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
/// 🔒 <c>content/statuses.json</c>, read — the whole of `05` §5.
/// </summary>
/// <remarks>
/// <para>
/// The document holds every number `05` §5 states and nothing else: the stack ceilings it fixes for
/// five of the twelve, the two constants it writes as literals (<c>FREEZE</c>'s ASPD reduction and
/// <c>STUN</c>'s per-application cap and immunity window), and 📐 <c>BLEED</c>'s missing-HP scaling
/// term. It sits under <c>content/</c> rather than <c>tuning/</c> on
/// <c>combat_caps.json</c>'s precedent and for its reason.
/// </para>
/// <para>
/// ⚠️ <b>The cadence itself is NOT in the data.</b> `05` §3.1 states it in ticks of `05` §3's 20 Hz
/// clock, which is already <c>CombatLog.TicksPerSecond</c>; a second copy in a content file would be
/// two numbers that must agree with nothing making them, which is the shape M2-07's mitigation-dial
/// mirror rule exists to police. <see cref="StatusCadence"/> derives it.
/// </para>
/// </remarks>
internal sealed record StatusCatalogue(
    double BleedMissingHpScaling,
    double StunMaxSecondsPerApplication,
    double StunImmunityWindowSeconds,
    IReadOnlyList<StatusDefinition> Statuses)
{
    /// <summary>The snapshot-relative path of the document.</summary>
    internal const string Document = "content/statuses.json";

    /// <summary>🔒 `05` §5 fixes exactly twelve statuses. Asserted at load — see <see cref="Read"/>.</summary>
    internal const int ExpectedStatusCount = 12;

    /// <summary>📐 `05` §5 — <c>BLEED</c>'s missing-HP scaling term.</summary>
    internal const string BleedMissingHpScalingPointer = Document + "#/bleedMissingHpScaling";

    /// <summary>`05` §5 — <em>"Max 1.5 s per application"</em>.</summary>
    internal const string StunMaxSecondsPointer = Document + "#/stun/maxSecondsPerApplication";

    /// <summary>`05` §5 — <em>"with a 3 s immunity window after"</em>.</summary>
    internal const string StunImmunityWindowPointer = Document + "#/stun/immunityWindowSeconds";

    /// <summary>🔒 `05` §5 — <c>RAGE</c>'s unauthorised decay curve. See <see cref="StatusDefinition.RequireDecayCurve"/>.</summary>
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
    /// 🔒 `18` §1's canonical stacking block — the one the document prints in <em>Anatomy of an
    /// effect</em> — used when neither the applying effect nor `05` §5 states one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A transcription, and it is not the FREEZE hole being filled.</b> Two different questions
    /// are being kept apart here. <em>"What stack ceiling does `05` §5 authorise for <c>FREEZE</c>?"</em>
    /// has no answer — `05` §6.1a's <c>CASTER</c> table asks for one, `05` §5 states none, and that
    /// hole stays a <c>null</c> in <c>content/enemies/enemies.json</c> exactly where M2-11 left it
    /// (steering S6). <em>"What stacking block does an effect use when it authors none?"</em> is a
    /// different question, and `18` §1 answers it by printing one:
    /// <c>{"mode": "ADDITIVE", "maxStacks": 1}</c>. That is the only stacking block the DSL document
    /// writes for an effect with nothing special going on, and <c>EffectStackSet</c> already names it
    /// <em>"`18` §1's canonical"</em>.
    /// </para>
    /// <para>
    /// It is also the only reading under which the content set runs: `18` §7.8's Thornmaw phase-3
    /// <c>RAGE</c> authors no <c>stacking</c> at all, and refusing here would throw on authored spec
    /// content.
    /// </para>
    /// </remarks>
    internal static EffectStacking CanonicalStacking { get; } =
        new() { Mode = StackingMode.ADDITIVE, MaxStacks = 1 };

    /// <summary>
    /// 🔒 The twelve, indexed by id — `05` §5's set, which is closed.
    /// </summary>
    internal IReadOnlyDictionary<string, StatusDefinition> ById { get; } =
        Statuses.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>One status by id.</summary>
    /// <exception cref="EffectContextException">
    /// The id is outside `05` §5's twelve. <c>StatusOps</c> deliberately does not validate the id —
    /// <c>effect.schema.json</c> encloses the set and this catalogue is the type that holds it — so
    /// an effect built in code rather than loaded from JSON arrives here unchecked.
    /// </exception>
    internal StatusDefinition Of(string statusId) =>
        ById.TryGetValue(statusId, out var found)
            ? found
            : throw new EffectContextException(
                statusId,
                "it is not one of 05 §5's twelve statuses",
                "05 §5 fixes BURN, POISON, BLEED, FREEZE, STUN, WEAKEN, SUNDER, SPORE, RAGE, WARD, " +
                "HASTE and REGEN, and game-data/schema/effect.schema.json encloses the same set. A " +
                "thirteenth reaching here came from an effect built in code rather than loaded from " +
                "JSON, which is outside that enforcement.");

    /// <summary>
    /// 🔒 The `18` §6 stacking block one application uses: the effect's, else `05` §5's, else
    /// `18` §1's canonical one.
    /// </summary>
    /// <remarks>
    /// The precedence is the documents': `18` §6 is a <em>per-effect</em> block and `05` §5 is the
    /// <em>status's</em> rule, so an effect that authors one is being specific about its own
    /// application and wins. Every on-hit row in <c>content/enemies/enemies.json</c> authors one that
    /// already agrees with `05` §5, so the first two arms do not disagree anywhere in the content set
    /// today — <c>StatusCatalogueTests</c> pins that.
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
    internal static StatusCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = content.GetDocument(Document).Root;
        if (!rows.TryGetMember("statuses", out var list) || list!.Kind != ContentValueKind.Array)
        {
            throw new FormatException(
                $"{Document} carries no 'statuses' array. 05 §5's table is the whole point of the " +
                "file, and its schema requires exactly twelve rows.");
        }

        var statuses = new List<StatusDefinition>(list.Items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < list.Items.Count; index++)
        {
            var row = Row(list.Items[index]);

            // 🔒 Duplicate ids are refused BY NAME. ToDictionary's own failure is a bare
            // ArgumentException naming neither the document nor the id, and the schema's uniqueItems
            // compares whole objects — so two rows sharing an id with different bodies pass it.
            if (!seen.Add(row.Id))
            {
                throw new FormatException(
                    $"{Document} declares '{row.Id}' twice. 05 §3.1 is 'one instance per statusId " +
                    "per target', which needs the id to identify one row; the schema's uniqueItems " +
                    "compares whole rows and cannot see two that differ elsewhere.");
            }

            statuses.Add(row);
        }

        // 🔒 The twelve-row bound is asserted HERE and not only in the schema. Review found the type
        // claiming 'its schema requires exactly twelve rows' while nothing at run time checked it —
        // and a snapshot built without schema validation (every in-code fixture) would load a
        // partial catalogue whose first symptom is Of() blaming its caller for the file's defect.
        if (statuses.Count != ExpectedStatusCount)
        {
            throw new FormatException(
                $"{Document} carries {statuses.Count.ToString(CultureInfo.InvariantCulture)} statuses. " +
                "05 §5 fixes exactly twelve, and its schema declares minItems and maxItems 12; a " +
                "catalogue that is short fails later, mid-battle, as an unknown-status error.");
        }

        return new StatusCatalogue(
            content.ReadDouble(BleedMissingHpScalingPointer),
            content.ReadDouble(StunMaxSecondsPointer),
            content.ReadDouble(StunImmunityWindowPointer),
            statuses);
    }

    private static StatusDefinition Row(ContentValue row)
    {
        var id = Text(row, "id");

        return new StatusDefinition(
            id,
            Lookup(Kinds, Text(row, "type"), "05 §5's Type column", id),
            Lookup(Bases, Text(row, "potencyBasis"), "05 §5's potency units", id),
            row.TryGetMember("stat", out var stat) && stat!.Kind == ContentValueKind.Text

                // Through the same Lookup as type and potencyBasis, not Enum.Parse: review found
                // that Enum.Parse threw a bare BCL ArgumentException naming no document and no row,
                // and silently accepted any of the fourteen StatIds where the schema's $defs/statId
                // admits four.
                ? Lookup(Stats, stat.AsText(), "05 §5's four debuffable stats", id)
                : null,
            row.TryGetMember("fixedPotency", out var fixedPotency) &&
            fixedPotency!.Kind == ContentValueKind.Number
                ? fixedPotency.AsDouble()
                : null,
            row.TryGetMember("scalesWithTargetMissingHp", out var scales) &&
            scales!.Kind == ContentValueKind.Boolean && scales.AsBoolean(),
            Stacking(row),

            // 🔒 The tree is walked rather than the value read through a pointer, and that is what
            // keeps RAGE's authored null a null. ContentSnapshot.Read throws
            // UnauthorisedTunableException on an unauthorised value, so reading decayCurve that way
            // would fail at load and take every fight with it. Carrying the null is what makes
            // StatusDefinition.RequireDecayCurve the one place that fails, by name, if anything
            // ever tries to use it — `game-data/README.md`'s rule for exactly this shape.
            row.TryGetMember("decayCurve", out var decay) && decay!.Kind == ContentValueKind.Text
                ? decay.AsText()
                : null);
    }

    private static EffectStacking? Stacking(ContentValue row)
    {
        if (!row.TryGetMember("stacking", out var stacking) || stacking!.Kind != ContentValueKind.Object)
        {
            return null;
        }

        return new EffectStacking
        {
            Mode = Lookup(Modes, Text(stacking, "mode"), "18 §6's stacking modes", Text(row, "id")),
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

    private static string Text(ContentValue value, string member) =>
        value.TryGetMember(member, out var found) && found!.Kind == ContentValueKind.Text
            ? found.AsText()
            : throw new FormatException(
                $"A {Document} row carries no '{member}'. Its schema requires the key, so a row " +
                "reaching here without one was not validated against that schema.");

    private static TValue Lookup<TValue>(
        IReadOnlyDictionary<string, TValue> table, string token, string what, string id)
        where TValue : struct
    {
        if (table.TryGetValue(token, out var found))
        {
            return found;
        }

        throw new FormatException(
            $"{Document}'s {id} row carries '{token}', which is not one of {what}. The schema " +
            "encloses the set, so a token reaching here was not validated against it — and the " +
            $"vocabulary in code is the one 05 §5 states: {string.Join(", ", table.Keys)}.");
    }
}
