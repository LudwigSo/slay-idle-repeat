using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>One <c>STAT_CONVERT</c>, read off the effect: take from one stat, give to another.</summary>
/// <param name="From">Stat A — the effect's <c>stat</c>.</param>
/// <param name="To">Stat B — the effect's <c>toStat</c>.</param>
/// <param name="Fraction">The proportion of <paramref name="From"/> that moves.</param>
internal readonly record struct StatConversion(StatId From, StatId To, double Fraction);

/// <summary>One <c>STAT_CAP_OVERRIDE</c>, read off the effect.</summary>
/// <param name="Kind">Which of the three things the op does.</param>
/// <param name="Stat">The stat whose ceiling is raised, or whose overshoot is redirected.</param>
/// <param name="ToStat">Where a <see cref="StatCapKind.REDIRECT_EXCESS"/> sends it; <c>null</c> otherwise.</param>
/// <param name="Value">The new ceiling, the redirect ratio, or the heal ceiling's fraction of Max HP.</param>
internal readonly record struct StatCapOverride(
    StatCapKind Kind, StatId Stat, StatId? ToStat, double Value);

/// <summary>The two stat ops aggregation leaves open — <c>STAT_CONVERT</c> and <c>STAT_CAP_OVERRIDE</c> — as pure arithmetic over <see cref="StatId"/> and <see cref="double"/>.</summary>
/// <remarks>
/// The arithmetic lives here rather than in <c>Rules/Stats/</c>: the intra-<c>Rules</c> layering
/// keeps this namespace from naming <c>ActorStats</c>, <c>StatCaps</c> or <c>StatDelta</c>, all of
/// which the frozen-block plumbing in <c>Rules/Stats/StatOpBehaviour.cs</c> uses. So op meaning lives
/// here, alongside the other forty-one ops, and the frozen-block discipline lives one layer up.
/// </remarks>
internal static class StatOps
{
    /// <summary>The conversion an effect authors.</summary>
    /// <param name="effect">A <see cref="EffectOp.STAT_CONVERT"/>.</param>
    /// <param name="fraction">The effect's scaled value: the proportion of the source stat that moves.</param>
    /// <exception cref="EffectContextException">Either end of the conversion is unwritten or invalid.</exception>
    internal static StatConversion Conversion(EffectDefinition effect, double fraction)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var from = SingleStat(effect, effect.Stat, "stat", "the source");
        var to = effect.ToStat ?? throw new EffectContextException(
            effect.Id,
            "STAT_CONVERT names no toStat",
            "18 §2.1 converts 'a percentage of stat A into stat B'. M2-03 added 'toStat' for stat B " +
            "under 18 §10 because the eight-part shape carried one 'stat' key and neither end was " +
            "identifiable; game-data/schema/effect.schema.json requires it.");

        if (from == to)
        {
            throw new EffectContextException(
                effect.Id,
                $"STAT_CONVERT converts {from} into itself",
                "18 §2.1 moves a proportion of stat A into stat B. A conversion onto its own source " +
                "is a no-op with a rounding error attached — the two deltas cancel — and it reads as " +
                "a live effect.");
        }

        // The fraction is not rounded here — rounding happens at accumulation points (results), not
        // on an authored value. Rounding twice moves the answer: an authored 0.123456 of a post-step
        // DEF of 1000 is 123.456, but 1000 x Round(0.123456) is 123.5.
        return new StatConversion(from, to, fraction);
    }

    /// <summary>The two signed deltas one conversion produces, given the source stat's post-aggregation value.</summary>
    /// <remarks>
    /// A pair, never a mutation: the source loses what the destination gains, and expressing it as
    /// two signed deltas lets the caller apply them against a frozen block rather than trusting the
    /// order of application.
    /// </remarks>
    /// <param name="conversion">The conversion.</param>
    /// <param name="sourceValue">The source stat as it stood after the earlier aggregation steps.</param>
    /// <param name="effectId">The effect id, for a rounding failure's message.</param>
    internal static (double FromDelta, double ToDelta) Deltas(
        StatConversion conversion, double sourceValue, string effectId)
    {
        var moved = OpRounding.Round(sourceValue * conversion.Fraction, effectId, "converted amount");

        return (-moved, moved);
    }

    /// <summary>The cap override an effect authors.</summary>
    /// <param name="effect">A <see cref="EffectOp.STAT_CAP_OVERRIDE"/>.</param>
    /// <param name="value">The effect's scaled value.</param>
    /// <exception cref="EffectContextException">The override is missing a key its <c>capKind</c> needs.</exception>
    internal static StatCapOverride CapOverride(EffectDefinition effect, double value)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var kind = effect.CapKind ?? throw new EffectContextException(
            effect.Id,
            "STAT_CAP_OVERRIDE names no capKind",
            "18 §2.1 'raises OR redirects', and 18 §7.6's HEAL_CEILING does neither to a stat cap — " +
            "the three are different operations with different arithmetic, so there is no default " +
            "that could stand for the others.");

        var stat = SingleStat(effect, effect.Stat, "stat", "the capped");

        // Rounded for STAT_MAX and HEAL_CEILING, since the ceiling itself lands in the cap table and
        // a cap must be rounded. Not for REDIRECT_EXCESS, whose value is a ratio that
        // RedirectedAmount multiplies later — pre-rounding a multiplicand would double-round.
        var rounded = kind == StatCapKind.REDIRECT_EXCESS
            ? value
            : OpRounding.Round(value, effect.Id, "cap override value");

        StatId? toStat = null;

        if (kind == StatCapKind.REDIRECT_EXCESS)
        {
            toStat = effect.ToStat ?? throw new EffectContextException(
                effect.Id,
                "a REDIRECT_EXCESS override names no toStat",
                "09 §4's Perfect Strike sends crit chance above the 75% cap INTO crit damage; a " +
                "redirect with no destination would discard the overshoot and read as a cap raise.");
        }
        else if (effect.ToStat is not null)
        {
            // Refused, not ignored: only REDIRECT_EXCESS has a destination, and the JSON schema
            // can't express this conditional-required rule, so it's checked here instead.
            throw new EffectContextException(
                effect.Id,
                $"a {kind} override names a toStat",
                "18 §2.1's destination belongs to REDIRECT_EXCESS alone — a raise and a heal ceiling " +
                "send nothing anywhere. Ignoring it would be a field that means nothing while " +
                "reading as though it did.");
        }

        if (kind == StatCapKind.REDIRECT_EXCESS && toStat == stat)
        {
            throw new EffectContextException(
                effect.Id,
                $"a REDIRECT_EXCESS override sends {stat}'s overshoot back into {stat}",
                "18 §8 step 9 has already capped the stat when the redirect is computed, so putting " +
                "the excess back would either re-breach the cap or be clipped away again — a loop or " +
                "a no-op, neither of which 09 §4 describes.");
        }

        return new StatCapOverride(kind, stat, toStat, rounded);
    }

    /// <summary>
    /// The amount a <see cref="StatCapKind.REDIRECT_EXCESS"/> moves, given how far the stat
    /// overshot its ceiling.
    /// </summary>
    /// <param name="over">The uncapped value.</param>
    /// <param name="ceiling">The ceiling that was applied, or <c>null</c> when the stat is uncapped.</param>
    /// <param name="ratio">The effect's <c>value</c> — how much destination one unit of overshoot buys.</param>
    /// <param name="effectId">The effect id, for a rounding failure's message.</param>
    /// <remarks>Nothing here knows any specific ratio — that's a content decision authored as the effect's <c>value</c>. An uncapped stat redirects nothing, since there's no overshoot to take.</remarks>
    internal static double RedirectedAmount(double over, double? ceiling, double ratio, string effectId) =>
        ceiling is { } cap && over > cap
            ? OpRounding.Round((over - cap) * ratio, effectId, "redirected excess")
            : 0.0;

    /// <summary>The single stat a basic stat op names, for a fired activation.</summary>
    /// <remarks>Delegates to the same reading <see cref="Conversion"/> and <see cref="CapOverride"/> use, so the three stat ops that must name exactly one stat can't answer the question differently.</remarks>
    /// <exception cref="EffectContextException">The effect names no <c>stat</c>, or names a group selector (<c>ALL_COMBAT</c> or <c>HIGHEST_PCT_BONUS</c>).</exception>
    internal static StatId SingleStatOf(EffectDefinition effect) =>
        SingleStat(effect, effect.Stat, "stat", "the");

    private static StatId SingleStat(
        EffectDefinition effect, StatSelector? selector, string key, string role)
    {
        if (selector is not { } present)
        {
            throw new EffectContextException(
                effect.Id,
                $"{effect.Op} names no '{key}'",
                "18 §2.1's stat ops each name a stat, and game-data/schema/effect.schema.json " +
                "requires it. An effect built in code rather than loaded from JSON is outside that " +
                "enforcement, which is what happened here.");
        }

        return present.Kind == StatSelectorKind.SINGLE
            ? present.Stat!.Value
            : throw new EffectContextException(
                effect.Id,
                $"{role} stat of a {effect.Op} is the {present} selector",
                "18 §2.1 needs one concrete stat here. 18 §9.1's CP_GLASS_HEART is authored as two " +
                "effects precisely because a group selector cannot express a single-stat op, and " +
                "the schema's stat definition excludes ALL_COMBAT for the same reason.");
    }
}
