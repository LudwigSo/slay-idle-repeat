using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>The 23 trigger kinds, stated as data: which loop fires each, which parameters each admits, and which it cannot do without.</summary>
/// <remarks>
/// <para>
/// Checked against the enum rather than trusted: a catalogue that can silently lose a row is a
/// catalogue whose every rule passes over the rows that remain.
/// </para>
/// <para>
/// An absent parameter means two different things depending on the kind. For most parameters,
/// absence means "not narrowed" — <c>ON_HIT</c> with no <c>chance</c> fires on every hit,
/// <c>ON_BATTLE_END</c> with no <c>onlyIfWon</c> fires on a loss too — and this is not a default
/// filled into a hole, the unnarrowed sentence simply is the row. For a few kinds the parameter is
/// constitutive and its absence is a failure: <see cref="TriggerKind.PERIODIC"/> with no
/// <c>interval</c> has no period, <see cref="TriggerKind.ON_LOW_HP"/> with no <c>threshold</c> names
/// no crossing, <see cref="TriggerKind.ON_PHASE_ENTER"/> with no <c>phase</c> can't say which entry
/// it means. <see cref="Validate"/> throws on all three.
/// </para>
/// <para><c>startDelay</c> is neither — it's constitutive of when a <c>PERIODIC</c> first fires, but its ruling lives in <see cref="TriggerSchedule"/>, where the arithmetic is.</para>
/// </remarks>
internal static class TriggerCatalogue
{
    /// <summary>The trigger count, in one place.</summary>
    internal const int TriggerKindCount = 23;

    /// <summary>The token every failure raised from this file names, so S2 can pin the rule.</summary>
    private const string Reference = "18 §3 partitions the 23 trigger kinds into closed parameter shapes.";

    private static readonly IReadOnlyDictionary<TriggerKind, TriggerKindFacts> Rows = Build();

    /// <summary>Every kind this catalogue holds a row for, in <see cref="TriggerKind"/> wire-value order.</summary>
    /// <remarks>
    /// Built from the rows, not from <c>Enum.GetValues</c>: a list derived from the enum would report
    /// 23 whatever the catalogue held, so every rule stated over it would quantify over the enum and
    /// prove nothing about the rows.
    /// </remarks>
    internal static IReadOnlyList<TriggerKind> All { get; } =
        Rows.Keys.OrderBy(k => (int)k).ToArray();

    /// <summary>The facts about one kind.</summary>
    /// <param name="Layer">Which loop fires it.</param>
    /// <param name="Admits">Every parameter this kind takes.</param>
    /// <param name="Requires">The subset without which the kind can't be read at all — see the type remarks.</param>
    internal readonly record struct TriggerKindFacts(
        TriggerLayer Layer,
        TriggerParameter Admits,
        TriggerParameter Requires);

    /// <summary>The facts about <paramref name="kind"/>.</summary>
    /// <param name="kind">One of the 23 kinds.</param>
    /// <exception cref="EffectContextException">The kind is not one of the 23.</exception>
    internal static TriggerKindFacts FactsOf(TriggerKind kind) =>
        Rows.TryGetValue(kind, out var facts)
            ? facts
            : throw new EffectContextException(
                kind.ToString(),
                $"{(int)kind} is not one of `18` §3's {TriggerKindCount} trigger kinds",
                Reference);

    /// <summary>Which loop fires <paramref name="kind"/>.</summary>
    /// <param name="kind">One of the 23 kinds.</param>
    internal static TriggerLayer LayerOf(TriggerKind kind) => FactsOf(kind).Layer;

    /// <summary>Refuses a trigger whose parameters its kind doesn't admit, whose constitutive parameter is missing, or whose value is out of range.</summary>
    /// <param name="trigger">The trigger to check.</param>
    /// <exception cref="EffectContextException">The trigger is malformed.</exception>
    /// <remarks>
    /// Called once per instance by <see cref="TriggerRegistry.Register"/>, not once per tick: a
    /// trigger's shape cannot change during a battle, so re-checking it every tick would just spend
    /// the fight's time budget for no reason.
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
    /// Presence, not truthiness: <c>{"once": false}</c> and an absent <c>once</c> are different
    /// authored statements, and a check on the value alone would let a kind that doesn't admit
    /// <c>once</c> through as "nothing written".
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

        // The whole-tick rule, applied eagerly: the simulation is fixed-tick, so a span that isn't a
        // whole number of ticks points between two ticks and at neither. Checked here rather than at
        // first firing, so a malformed span throws at registration instead of mid-battle.
        _ = TriggerSchedule.CooldownTicks(trigger);

        if (trigger.Kind == TriggerKind.PERIODIC)
        {
            _ = TriggerSchedule.FirstFiringTick(trigger, 0);
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
        const TriggerParameter none = TriggerParameter.NONE;

        return new Dictionary<TriggerKind, TriggerKindFacts>
        {
            [TriggerKind.ALWAYS] = new(TriggerLayer.PASSIVE, none, none),

            [TriggerKind.ON_BATTLE_START] = new(TriggerLayer.COMBAT, none, none),
            [TriggerKind.ON_BATTLE_END] = new(TriggerLayer.COMBAT, TriggerParameter.ONLY_IF_WON, none),

            // ON_ATTACK carries chance as well as everyNth, for uniformity with ON_HIT/ON_CRIT.
            // ON_KILL does not — no design asks for a chancy kill trigger.
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
            [TriggerKind.ON_ROLL] = new(TriggerLayer.RUN, TriggerParameter.NONE, none),
            [TriggerKind.ON_PERK_TAKEN] = new(TriggerLayer.RUN, TriggerParameter.CATEGORY, none),
            [TriggerKind.ON_STAGE_GATE] = new(TriggerLayer.RUN, none, none),
            [TriggerKind.ON_RUN_START] = new(TriggerLayer.RUN, none, none),
            [TriggerKind.ON_RUN_END] = new(TriggerLayer.RUN, none, none),
        };
    }

    /// <summary>The parameters in a flag set, spelled as the DSL spells them, for a failure message.</summary>
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
        TriggerParameter.CATEGORY => "category",
        _ => parameter.ToString(),
    };

    private static string Format(double value) => InvariantText.Text(value);

    private static string Format(int value) => InvariantText.Text(value);
}
