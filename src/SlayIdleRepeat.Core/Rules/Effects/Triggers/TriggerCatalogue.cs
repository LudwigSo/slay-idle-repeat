using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 `18` §3's 23 trigger kinds, stated as data: which loop fires each, which parameters each
/// admits, and which it cannot do without.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>23, and the count is not relaxed.</b> `18` §11: <em>"23 triggers = 21 + <c>ON_DEATH</c> +
/// <c>ON_REVIVE</c>"</em>. <see cref="TriggerKind"/> declares them and this declares one row per
/// member, checked against the enum rather than trusted — steering S3, because a catalogue that can
/// silently lose a row is a catalogue whose every rule passes over the rows that remain.
/// </para>
///
/// <para>
/// ═══ <b>THREE RULINGS AGAINST `17` AND `05`, RECORDED AS ERRATA</b> ═══
/// </para>
/// <para>
/// <b>R9 · <c>ON_HP_THRESHOLD</c> is <see cref="TriggerKind.ON_LOW_HP"/>; there is no 24th
/// trigger.</b> `17` §1.1 tabulates a mechanic vocabulary that includes <c>ON_HP_THRESHOLD</c>
/// — <em>"fires once when boss HP crosses a value"</em> — which is boss-design shorthand, not a DSL
/// kind. `18` §3's <c>ON_LOW_HP</c> is <em>"Self HP crosses a threshold downward"</em> with
/// <c>threshold</c> and <c>once</c>, which is the same sentence. The Ossuary King's
/// <c>ON_HP_THRESHOLD 1%</c> Rise Again (`17` §4) is
/// <c>{"kind":"ON_LOW_HP","threshold":0.01,"once":true}</c>. Erratum on `17` §1.1.
/// </para>
/// <para>
/// <b>R11 · <see cref="TriggerKind.ON_ATTACK"/> gains <c>chance</c>.</b> `18` §3 gives
/// <c>ON_ATTACK</c> only <c>everyNth</c>, while `06` authors per-attack random perks;
/// <c>ON_HIT</c> and <c>ON_CRIT</c> already carry <c>chance</c>, so this is a uniformity fix rather
/// than a new concept, extended per `18` §10's procedure — code, schema, document and test in one
/// commit. ⚠️ <c>ON_KILL</c> does <b>not</b> gain it: §10 extends the DSL by the smallest step that
/// expresses the design, and no design asks for a chancy kill trigger.
/// </para>
/// <para>
/// <b>R2 · <c>once</c> is a boolean.</b> `18` §3 and §7.4 both write it as one; `05` §3.1's
/// <em>"at most their authored <c>once</c> count"</em> is loose prose for "the authored limit".
/// <see cref="EffectTrigger.Once"/> shipped boolean in M2-01 and stays boolean. Erratum on `05`
/// §3.1's wording.
/// </para>
///
/// <para>
/// ═══ <b>WHAT AN ABSENT PARAMETER MEANS</b> 🔒 ═══
/// </para>
/// <para>
/// <see cref="EffectTrigger"/>'s own remarks defer this: <em>"`18` §3 gives no default for any of
/// these, so M2-04 rules on each as it wires it."</em> The line drawn is between a parameter that
/// <b>narrows</b> a trigger the §3 table already describes in full, and one the §3 table's own
/// sentence cannot be read without.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>A narrowing parameter absent means "not narrowed".</b> `18` §3 describes
///     <c>ON_HIT</c> as <em>"each successful hit landed"</em> and <c>chance</c> narrows that;
///     <c>ON_ATTACK</c> as <em>"each attack made"</em> and <c>everyNth</c> narrows that;
///     <c>ON_BATTLE_END</c> as <em>"once when a battle ends"</em> and <c>onlyIfWon</c> narrows that
///     — §7.5's <c>CP_BLOOD_PRICE</c> drawback writes no <c>onlyIfWon</c> and must land on a loss.
///     <c>cooldown</c>, <c>tileType</c>, <c>faceKind</c> and <c>category</c> are the same shape.
///     This is not a default filled into a hole: the unnarrowed sentence <b>is</b> the row.
///   </item>
///   <item>
///     🔒 <b>A constitutive parameter absent is a failure</b>, per steering S6 — never coerced to a
///     plausible value at read time. <see cref="TriggerKind.PERIODIC"/> with no <c>interval</c> has
///     no period; <see cref="TriggerKind.ON_LOW_HP"/> with no <c>threshold</c> names no crossing;
///     <see cref="TriggerKind.ON_PHASE_ENTER"/> with no <c>phase</c> cannot say which entry it
///     means, and answering "every entry" would fire Thornmaw's phase-3 summons three times
///     (`17` §2). <see cref="Validate"/> throws on all three.
///   </item>
/// </list>
/// <para>
/// ⚠️ <c>startDelay</c> is the one parameter that is neither: it is constitutive of <b>when</b> a
/// <c>PERIODIC</c> first fires, and `17`'s twenty boss periodics all omit it. Its ruling is
/// <see cref="TriggerSchedule"/>'s, where the arithmetic is.
/// </para>
/// </remarks>
internal static class TriggerCatalogue
{
    /// <summary>🔒 `18` §11 — the trigger count, in one place.</summary>
    internal const int TriggerKindCount = 23;

    /// <summary>The token every failure raised from this file names, so S2 can pin the rule.</summary>
    private const string Reference = "18 §3 partitions the 23 trigger kinds into closed parameter shapes.";

    private static readonly IReadOnlyDictionary<TriggerKind, TriggerKindFacts> Rows = Build();

    /// <summary>
    /// 🔒 Every kind this catalogue holds a row for, in <see cref="TriggerKind"/> wire-value order.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Built from the rows, not from <c>Enum.GetValues</c>.</b> The difference is the whole
    /// point of steering S3: a list derived from the enum would report 23 whatever the catalogue
    /// held, so every rule stated over it — the layer partition, the parameter partition — would
    /// quantify over the enum and prove nothing about the rows. <c>TriggerCatalogueTests</c> compares
    /// this against <c>Enum.GetValues&lt;TriggerKind&gt;()</c> in both directions, which is an
    /// assertion only because the two are independently constructed.
    /// </remarks>
    internal static IReadOnlyList<TriggerKind> All { get; } =
        Rows.Keys.OrderBy(k => (int)k).ToArray();

    /// <summary>The facts about one kind.</summary>
    /// <param name="Layer">Which loop fires it.</param>
    /// <param name="Admits">Every parameter `18` §3 gives it.</param>
    /// <param name="Requires">
    /// The subset without which the §3 row cannot be read at all — see the type remarks.
    /// </param>
    internal readonly record struct TriggerKindFacts(
        TriggerLayer Layer,
        TriggerParameter Admits,
        TriggerParameter Requires);

    /// <summary>The facts about <paramref name="kind"/>.</summary>
    /// <param name="kind">One of `18` §3's 23 kinds.</param>
    /// <exception cref="EffectContextException">The kind is not one of the 23.</exception>
    internal static TriggerKindFacts FactsOf(TriggerKind kind) =>
        Rows.TryGetValue(kind, out var facts)
            ? facts
            : throw new EffectContextException(
                kind.ToString(),
                $"{(int)kind} is not one of `18` §3's {TriggerKindCount} trigger kinds",
                Reference);

    /// <summary>Which loop fires <paramref name="kind"/>.</summary>
    /// <param name="kind">One of `18` §3's 23 kinds.</param>
    internal static TriggerLayer LayerOf(TriggerKind kind) => FactsOf(kind).Layer;

    /// <summary>
    /// 🔒 Refuses a trigger whose parameters `18` §3 does not give its kind, whose constitutive
    /// parameter is missing, or whose value is outside the range §3 and the schema state.
    /// </summary>
    /// <param name="trigger">The trigger to check.</param>
    /// <exception cref="EffectContextException">The trigger is malformed.</exception>
    /// <remarks>
    /// Called once per instance by <see cref="TriggerRegistry.Register"/>, not once per tick: a
    /// trigger's shape cannot change during a battle, and re-checking it 1800 times would be 1800
    /// chances to spend the budget `05` §3 gives the whole fight.
    /// </remarks>
    internal static void Validate(EffectTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var facts = FactsOf(trigger.Kind);
        var written = Written(trigger);

        var surplus = written & ~facts.Admits;
        if (surplus != TriggerParameter.NONE)
        {
            throw new EffectContextException(
                trigger.Kind.ToString(),
                $"it carries {Name(surplus)}, which `18` §3 does not give it",
                Reference + " A parameter on the wrong kind is a key that silently means nothing.");
        }

        var missing = facts.Requires & ~written;
        if (missing != TriggerParameter.NONE)
        {
            throw new EffectContextException(
                trigger.Kind.ToString(),
                $"it carries no {Name(missing)}, without which `18` §3's row states no rule",
                "Steering S6: a hole is left absent and greppable, never coerced to a plausible " +
                "value at read time.");
        }

        RequireRanges(trigger);
    }

    /// <summary>Which parameters the author actually wrote.</summary>
    /// <remarks>
    /// ⚠️ <b>Presence, not truthiness.</b> <c>{"once": false}</c> and an absent <c>once</c> are
    /// different authored statements, and only the first is a parameter the kind must admit — a
    /// check on the <em>value</em> would let <c>{"kind":"ALWAYS","once":false}</c> through as
    /// "nothing written".
    /// </remarks>
    private static TriggerParameter Written(EffectTrigger trigger)
    {
        var written = TriggerParameter.NONE;

        if (trigger.OnlyIfWon is not null)
        {
            written |= TriggerParameter.ONLY_IF_WON;
        }

        if (trigger.EveryNth is not null)
        {
            written |= TriggerParameter.EVERY_NTH;
        }

        if (trigger.Chance is not null)
        {
            written |= TriggerParameter.CHANCE;
        }

        if (trigger.Cooldown is not null)
        {
            written |= TriggerParameter.COOLDOWN;
        }

        if (trigger.Threshold is not null)
        {
            written |= TriggerParameter.THRESHOLD;
        }

        if (trigger.Once is not null)
        {
            written |= TriggerParameter.ONCE;
        }

        if (trigger.Interval is not null)
        {
            written |= TriggerParameter.INTERVAL;
        }

        if (trigger.StartDelay is not null)
        {
            written |= TriggerParameter.START_DELAY;
        }

        if (trigger.Phase is not null)
        {
            written |= TriggerParameter.PHASE;
        }

        if (trigger.TileType is not null)
        {
            written |= TriggerParameter.TILE_TYPE;
        }

        if (trigger.FaceKind is not null)
        {
            written |= TriggerParameter.FACE_KIND;
        }

        if (trigger.Category is not null)
        {
            written |= TriggerParameter.CATEGORY;
        }

        return written;
    }

    /// <summary>
    /// The ranges the schema states, restated for triggers that were never validated against it.
    /// </summary>
    private static void RequireRanges(EffectTrigger trigger)
    {
        var kind = trigger.Kind.ToString();

        if (trigger.EveryNth is { } everyNth && everyNth < 1)
        {
            throw new EffectContextException(
                kind,
                $"its everyNth is {Format(everyNth)}",
                "`18` §3's counter fires on every Nth occurrence, and there is no zeroth or " +
                "minus-third attack. The schema states minimum 1.");
        }

        RequireProbability(kind, trigger.Chance, "chance");
        RequireProbability(kind, trigger.Threshold, "threshold");
        RequireNonNegativeSeconds(kind, trigger.Cooldown, "cooldown");
        RequireNonNegativeSeconds(kind, trigger.StartDelay, "startDelay");

        if (trigger.Interval is { } interval && (double.IsNaN(interval) || interval <= 0.0))
        {
            throw new EffectContextException(
                kind,
                $"its interval is {Format(interval)}",
                "`18` §3 fires a PERIODIC every interval seconds, and an interval of zero or less " +
                "is either an infinite loop inside one tick or a period that runs backwards. The " +
                "schema states exclusiveMinimum 0.");
        }

        if (trigger.Phase is { } phase && phase is < 1 or > 3)
        {
            throw new EffectContextException(
                kind,
                $"its phase is {Format(phase)}",
                "`17` §1 gives every boss exactly three phases, at 100%, 66% and 33% Max HP.");
        }
    }

    private static void RequireProbability(string kind, double? value, string name)
    {
        if (value is not { } probability)
        {
            return;
        }

        if (double.IsNaN(probability) || probability is < 0.0 or > 1.0)
        {
            throw new EffectContextException(
                kind,
                $"its {name} is {Format(probability)}",
                $"`18` §3's {name} is a 0..1 fraction. A value outside it is a content error that " +
                "would otherwise read as 'always' or 'never' and never be noticed.");
        }
    }

    private static void RequireNonNegativeSeconds(string kind, double? value, string name)
    {
        if (value is not { } seconds)
        {
            return;
        }

        if (double.IsNaN(seconds) || seconds < 0.0)
        {
            throw new EffectContextException(
                kind,
                $"its {name} is {Format(seconds)}",
                $"`18` §3's {name} is a span of battle time, and battle time does not run backwards " +
                "from the anchor (`05` §3).");
        }
    }

    /// <summary>The 23 rows. One per <see cref="TriggerKind"/> member, and the test checks that.</summary>
    private static Dictionary<TriggerKind, TriggerKindFacts> Build()
    {
        // 🔒 The five shapes 18 §3's table repeats, named once so a row reads as its 18 §3 row.
        const TriggerParameter none = TriggerParameter.NONE;

        return new Dictionary<TriggerKind, TriggerKindFacts>
        {
            [TriggerKind.ALWAYS] = new(TriggerLayer.PASSIVE, none, none),

            [TriggerKind.ON_BATTLE_START] = new(TriggerLayer.COMBAT, none, none),
            [TriggerKind.ON_BATTLE_END] = new(TriggerLayer.COMBAT, TriggerParameter.ONLY_IF_WON, none),

            // 🔒 R11: ON_ATTACK carries chance as well as everyNth. ON_KILL does not — see the type
            // remarks. The two kinds therefore no longer share a schema branch either.
            [TriggerKind.ON_ATTACK] = new(
                TriggerLayer.COMBAT,
                TriggerParameter.EVERY_NTH | TriggerParameter.CHANCE,
                none),

            [TriggerKind.ON_HIT] = new(TriggerLayer.COMBAT, TriggerParameter.CHANCE, none),
            [TriggerKind.ON_CRIT] = new(TriggerLayer.COMBAT, TriggerParameter.CHANCE, none),
            [TriggerKind.ON_HIT_TAKEN] = new(
                TriggerLayer.COMBAT,
                TriggerParameter.CHANCE | TriggerParameter.COOLDOWN,
                none),

            [TriggerKind.ON_DODGE] = new(TriggerLayer.COMBAT, TriggerParameter.COOLDOWN, none),
            [TriggerKind.ON_BLOCK] = new(TriggerLayer.COMBAT, TriggerParameter.COOLDOWN, none),

            [TriggerKind.ON_KILL] = new(TriggerLayer.COMBAT, TriggerParameter.EVERY_NTH, none),
            [TriggerKind.ON_DEATH] = new(TriggerLayer.COMBAT, none, none),
            [TriggerKind.ON_REVIVE] = new(TriggerLayer.COMBAT, none, none),

            [TriggerKind.ON_LOW_HP] = new(
                TriggerLayer.COMBAT,
                TriggerParameter.THRESHOLD | TriggerParameter.ONCE,
                TriggerParameter.THRESHOLD),

            [TriggerKind.ON_LETHAL] = new(TriggerLayer.COMBAT, TriggerParameter.ONCE, none),
            [TriggerKind.ON_HEAL] = new(TriggerLayer.COMBAT, none, none),

            [TriggerKind.PERIODIC] = new(
                TriggerLayer.COMBAT,
                TriggerParameter.INTERVAL | TriggerParameter.START_DELAY,
                TriggerParameter.INTERVAL),

            [TriggerKind.ON_PHASE_ENTER] = new(
                TriggerLayer.COMBAT,
                TriggerParameter.PHASE,
                TriggerParameter.PHASE),

            [TriggerKind.ON_TILE_RESOLVED] = new(TriggerLayer.RUN, TriggerParameter.TILE_TYPE, none),
            [TriggerKind.ON_ROLL] = new(TriggerLayer.RUN, TriggerParameter.FACE_KIND, none),
            [TriggerKind.ON_PERK_TAKEN] = new(TriggerLayer.RUN, TriggerParameter.CATEGORY, none),
            [TriggerKind.ON_STAGE_GATE] = new(TriggerLayer.RUN, none, none),
            [TriggerKind.ON_RUN_START] = new(TriggerLayer.RUN, none, none),
            [TriggerKind.ON_RUN_END] = new(TriggerLayer.RUN, none, none),
        };
    }

    /// <summary>The parameters in a flag set, spelled as `18` §3 spells them, for a failure message.</summary>
    internal static string Name(TriggerParameter parameters)
    {
        if (parameters == TriggerParameter.NONE)
        {
            return "no parameter";
        }

        var names = Enum.GetValues<TriggerParameter>()
            .Where(p => p != TriggerParameter.NONE && parameters.HasFlag(p))
            .Select(Spelling)
            .ToArray();

        return string.Join(", ", names);
    }

    /// <summary>The DSL's own spelling of a parameter — <c>ONLY_IF_WON</c> is <c>onlyIfWon</c> in JSON.</summary>
    private static string Spelling(TriggerParameter parameter) => parameter switch
    {
        TriggerParameter.ONLY_IF_WON => "onlyIfWon",
        TriggerParameter.EVERY_NTH => "everyNth",
        TriggerParameter.CHANCE => "chance",
        TriggerParameter.COOLDOWN => "cooldown",
        TriggerParameter.THRESHOLD => "threshold",
        TriggerParameter.ONCE => "once",
        TriggerParameter.INTERVAL => "interval",
        TriggerParameter.START_DELAY => "startDelay",
        TriggerParameter.PHASE => "phase",
        TriggerParameter.TILE_TYPE => "tileType",
        TriggerParameter.FACE_KIND => "faceKind",
        TriggerParameter.CATEGORY => "category",
        _ => parameter.ToString(),
    };

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
