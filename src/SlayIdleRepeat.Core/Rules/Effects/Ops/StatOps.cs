using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>One <c>STAT_CONVERT</c>, read off the effect: take from one stat, give to another.</summary>
/// <param name="From">`18` §2.1's stat A — the effect's <c>stat</c>.</param>
/// <param name="To">`18` §2.1's stat B — the effect's <c>toStat</c>, added by M2-03 under §10.</param>
/// <param name="Fraction">The proportion of <paramref name="From"/> that moves.</param>
internal readonly record struct StatConversion(StatId From, StatId To, double Fraction);

/// <summary>One <c>STAT_CAP_OVERRIDE</c>, read off the effect.</summary>
/// <param name="Kind">Which of the three things the op does.</param>
/// <param name="Stat">The stat whose ceiling is raised, or whose overshoot is redirected.</param>
/// <param name="ToStat">Where a <see cref="StatCapKind.REDIRECT_EXCESS"/> sends it; <c>null</c> otherwise.</param>
/// <param name="Value">The new ceiling, the redirect ratio, or the heal ceiling's fraction of Max HP.</param>
internal readonly record struct StatCapOverride(
    StatCapKind Kind, StatId Stat, StatId? ToStat, double Value);

/// <summary>
/// 🔒 `18` §2.1's two ops that M2-07's aggregation left open — <c>STAT_CONVERT</c> (§8 step 6) and
/// <c>STAT_CAP_OVERRIDE</c> (§8 step 9) — as pure arithmetic over <see cref="StatId"/> and
/// <see cref="double"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why the arithmetic is here and the plumbing is in <c>Rules/Stats/</c>.</b> R17 makes
/// <c>Rules.Effects</c> the bottom of the intra-<c>Rules</c> layering, so nothing in this namespace
/// may name <c>ActorStats</c>, <c>StatCaps</c> or <c>StatDelta</c> — all of which
/// <c>IStatOpBehaviour</c>'s signature uses. The split is therefore: <b>op meaning here</b>, where
/// the other forty-one ops live and where `18` §10 says a new op goes, and <b>M2-07's frozen-block
/// discipline in <c>Rules/Stats/StatOpBehaviour.cs</c></b>, which is where that interface sits.
/// (M2-02 moves the seams to <c>Rules/Effects/</c> in wave 4; the arithmetic does not move with them.)
/// </para>
/// <para>
/// 🔒 <b>Both ops were unimplementable as authored, and both were fixed with keys, not values
/// (R6).</b> <c>STAT_CONVERT</c> had one <c>stat</c> key for "stat A into stat B", and
/// <c>STAT_CAP_OVERRIDE</c> had one <c>capKind</c> — <c>HEAL_CEILING</c> — which is not one of `05`
/// §1's six stat caps at all. See <see cref="EffectDefinition.ToStat"/> and
/// <see cref="StatCapKind"/> for the paperwork.
/// </para>
/// </remarks>
internal static class StatOps
{
    /// <summary>
    /// `18` §8 step 6 — the conversion an effect authors.
    /// </summary>
    /// <param name="effect">A <see cref="EffectOp.STAT_CONVERT"/>.</param>
    /// <param name="fraction">
    /// The effect's `18` §1.1-scaled value: the <em>proportion</em> of the source stat that moves.
    /// <c>PK_TURTLE</c>'s <em>"convert 20% of DEF into ATK"</em> is <c>0.20</c>.
    /// </param>
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

        return new StatConversion(from, to, OpRounding.Round(fraction, effect.Id, "conversion fraction"));
    }

    /// <summary>
    /// The two signed deltas one conversion produces, given the source stat's <b>post-step-5</b>
    /// value.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A pair, never a mutation.</b> `18` §2.1 <em>converts</em> — the source loses what the
    /// destination gains — and expressing it as two signed deltas is what lets the caller apply them
    /// against a frozen block, so that `18` §8 step 6's <em>"reads post-step-5 values"</em> is
    /// enforced by the pipeline rather than trusted (M2-07's <c>StatDelta</c> remarks say the same).
    /// </remarks>
    /// <param name="conversion">The conversion.</param>
    /// <param name="sourceValue">The source stat as it stood after `18` §8 step 5.</param>
    /// <param name="effectId">The effect id, for a rounding failure's message.</param>
    internal static (double FromDelta, double ToDelta) Deltas(
        StatConversion conversion, double sourceValue, string effectId)
    {
        var moved = OpRounding.Round(sourceValue * conversion.Fraction, effectId, "converted amount");

        return (-moved, moved);
    }

    /// <summary>`18` §8 step 9 — the cap override an effect authors.</summary>
    /// <param name="effect">A <see cref="EffectOp.STAT_CAP_OVERRIDE"/>.</param>
    /// <param name="value">The effect's `18` §1.1-scaled value.</param>
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
        var rounded = OpRounding.Round(value, effect.Id, "cap override value");

        var toStat = kind == StatCapKind.REDIRECT_EXCESS
            ? effect.ToStat ?? throw new EffectContextException(
                effect.Id,
                "a REDIRECT_EXCESS override names no toStat",
                "09 §4's Perfect Strike sends crit chance above the 75% cap INTO crit damage; a " +
                "redirect with no destination would discard the overshoot and read as a cap raise.")
            : (StatId?)null;

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
    /// <param name="ceiling">The ceiling `18` §8 step 9 applied, or <c>null</c> when the stat is uncapped.</param>
    /// <param name="ratio">The effect's <c>value</c> — how much destination one unit of overshoot buys.</param>
    /// <param name="effectId">The effect id, for a rounding failure's message.</param>
    /// <remarks>
    /// 🔒 <b>Nothing here knows <c>Perfect Strike</c>'s "1:4".</b> `09` §4 states the ratio and M3's
    /// talent catalogue authors it as the effect's <c>value</c>; which way round 1:4 reads is a
    /// content decision, and steering S6 forbids this file having an opinion. An <b>uncapped</b>
    /// stat redirects nothing — there is no overshoot to take.
    /// </remarks>
    internal static double RedirectedAmount(double over, double? ceiling, double ratio, string effectId) =>
        ceiling is { } cap && over > cap
            ? OpRounding.Round((over - cap) * ratio, effectId, "redirected excess")
            : 0.0;

    /// <summary>
    /// One concrete stat off a selector — never <c>ALL_COMBAT</c>, never <c>HIGHEST_PCT_BONUS</c>.
    /// </summary>
    /// <remarks>
    /// `18` §9.1's own ruling is the precedent: <c>CP_GLASS_HEART</c> is authored as <em>two</em>
    /// effects, a <c>STAT_MULT ALL_COMBAT</c> and a separate <c>STAT_SET MAX_HP</c>, <em>"precisely
    /// because the group selector cannot express the second"</em>. A conversion or a cap over
    /// fourteen stats at once has no stated meaning either.
    /// </remarks>
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
