using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// The run-scoped percentage move on a <b>non-combat</b> stat — Gold gain, shop prices — summed
/// across the shrine buffs and curses the run carries.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this exists beside the aggregation pipeline rather than inside it.</b> An actor's stat
/// block holds `18` §2.1's fourteen COMBAT stats and nothing else: an effect naming one of the
/// twelve non-combat stats is valued, reported in <c>HeroBuild.UnappliedEffects</c>, and then not
/// applied to anything, because there is no slot for it. Two of the run's own modifiers are exactly
/// that shape — <c>SHR_GOLD</c> (Gilded Tongue, +15% Gold this run) and <c>CUR_MISERLY</c> (shop
/// prices +50%) — so routing them through the block would have left both inert while looking wired.
/// </para>
/// <para>
/// Reads the same authored magnitudes the effect sources read, from the same documents, so a tuning
/// edit moves both together. What it does NOT do is re-implement the aggregation order: these are
/// flat additive percentages summed and applied once, which is what `05` §1.1's <c>PctAdd</c> bucket
/// would do to them anyway in the absence of any multiplier on a non-combat stat.
/// </para>
/// <para>
/// Run buffs are deliberately not consulted: all three are <c>FlatAdd</c> on a combat stat
/// (`03` §7), so none of them can move a non-combat percentage.
/// </para>
/// </remarks>
internal static class RunModifierTotals
{
    /// <summary>
    /// The summed percentage move on <paramref name="stat"/> — <c>0.15</c> for a run holding one
    /// Gilded Tongue, <c>0.0</c> for a run holding nothing that touches it.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="content">The version-stamped snapshot the shrine pool is read from.</param>
    /// <param name="stat">The non-combat stat being asked about.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static double PctAdd(Run run, ContentSnapshot content, StatId stat)
    {
        ArgumentNullException.ThrowIfNull(run);

        return PctAdd(run.ShrineBuffs, run.Curses, content, stat);
    }

    /// <summary>
    /// The same total, over the two lists rather than the aggregate — the form a projection built
    /// from a persisted row can call.
    /// </summary>
    /// <remarks>
    /// Both overloads exist because both callers are real and neither can be the other: a handler
    /// holds a live <c>Run</c>, and a read-only view holds a <c>RunSnapshot</c> and cannot construct
    /// one. The aggregate overload delegates here rather than duplicating the arithmetic, so a shop
    /// screen and the purchase it leads to cannot come to different prices.
    /// </remarks>
    /// <param name="shrineBuffs">The shrine buff ids taken this run.</param>
    /// <param name="curses">The curse ids active on this run.</param>
    /// <param name="content">The version-stamped snapshot the shrine pool is read from.</param>
    /// <param name="stat">The non-combat stat being asked about.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static double PctAdd(
        IReadOnlyList<string> shrineBuffs,
        IReadOnlyList<string> curses,
        ContentSnapshot content,
        StatId stat)
    {
        ArgumentNullException.ThrowIfNull(shrineBuffs);
        ArgumentNullException.ThrowIfNull(curses);
        ArgumentNullException.ThrowIfNull(content);

        var total = 0.0;

        foreach (var curseId in curses)
        {
            if (CurseEffects.Stat(curseId) is { } move && move.Stat == stat)
            {
                total += move.PctAdd;
            }
        }

        if (shrineBuffs.Count > 0)
        {
            var pool = ShrineTuning.Read(content);

            foreach (var buffId in shrineBuffs)
            {
                total += MagnitudeOf(pool, buffId, stat);
            }
        }

        return DeterminismRounding.Round(total);
    }

    /// <summary>
    /// Scales a positive Gold income by the run's Gold-gain modifiers, rounded to whole Gold.
    /// </summary>
    /// <param name="run">The run receiving the Gold.</param>
    /// <param name="content">The version-stamped snapshot.</param>
    /// <param name="amount">The unscaled income. Never negative.</param>
    /// <returns>What the run actually receives, never below zero.</returns>
    /// <remarks>
    /// 🔒 <b>Income only, never a spend.</b> `03` §7a.5 reads "+15% Gold from all SOURCES this run"
    /// and `19` Part E reads "20% of all Gold GAINED is lost" — both are statements about money
    /// arriving. Scaling a price or a cost here as well would double-count against
    /// <c>CUR_MISERLY</c>, which is already a shop-price modifier in its own right, and would make a
    /// cursed player pay a discount on event costs.
    /// <para>
    /// Clamped at zero rather than allowed negative: a hypothetical stack of gold penalties past
    /// -100% must pay nothing, not take Gold the source never gave.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    internal static long ScaleGoldIncome(Run run, ContentSnapshot content, long amount)
    {
        ArgumentNullException.ThrowIfNull(run);

        return ScaleGoldIncome(run.ShrineBuffs, run.Curses, content, amount);
    }

    /// <summary>
    /// The same scaling, over the two lists rather than the aggregate — the form a projection built
    /// from a persisted row can call.
    /// </summary>
    /// <remarks>
    /// Both overloads exist for the reason the two <see cref="PctAdd(Run, ContentSnapshot, StatId)"/>
    /// overloads do, and the consequence here is sharper: a screen that previews a minigame's reward
    /// ladder holds a <c>RunSnapshot</c> and cannot construct a <c>Run</c>, so without this it would
    /// have to show the reward table's own figure — which is not what a run carrying a Gold buff or a
    /// Gold curse is about to be paid. The aggregate overload delegates here rather than restating the
    /// arithmetic, so the preview and the payout are one number by construction.
    /// </remarks>
    /// <param name="shrineBuffs">The shrine buff ids taken this run.</param>
    /// <param name="curses">The curse ids active on this run.</param>
    /// <param name="content">The version-stamped snapshot.</param>
    /// <param name="amount">The unscaled income. Never negative.</param>
    /// <returns>What the run actually receives, never below zero.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    internal static long ScaleGoldIncome(
        IReadOnlyList<string> shrineBuffs,
        IReadOnlyList<string> curses,
        ContentSnapshot content,
        long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        if (amount == 0)
        {
            return 0;
        }

        var pct = PctAdd(shrineBuffs, curses, content, StatId.GOLD_PCT);

        if (pct == 0.0)
        {
            return amount;
        }

        var scaled = Math.Round(amount * (1.0 + pct), MidpointRounding.ToEven);

        return scaled <= 0 ? 0L : (long)scaled;
    }

    /// <summary>
    /// Scales a shop price by the run's shop-price modifiers, rounded to whole Gold.
    /// </summary>
    /// <remarks>
    /// Applied after <see cref="ShopPricing.Price"/> rather than inside it: that formula is `03` §7's
    /// verbatim and is deliberately tier-invariant and parameterless beyond the four things the
    /// document names. A curse is not part of the price formula — it is a modifier on the answer.
    /// <para>
    /// Floored at 1 rather than 0: a price of zero would make the slot free, which no authored
    /// modifier is meant to do, and a run holding a hypothetical -100% would otherwise empty the
    /// shop for nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="price"/> is negative.</exception>
    internal static long ScaleShopPrice(
        IReadOnlyList<string> shrineBuffs,
        IReadOnlyList<string> curses,
        ContentSnapshot content,
        long price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        if (price == 0)
        {
            return 0;
        }

        var pct = PctAdd(shrineBuffs, curses, content, StatId.SHOP_PRICE_PCT);

        if (pct == 0.0)
        {
            return price;
        }

        var scaled = Math.Round(price * (1.0 + pct), MidpointRounding.ToEven);

        return scaled < 1 ? 1L : (long)scaled;
    }

    /// <summary>The magnitude one shrine buff contributes to <paramref name="stat"/>, or zero.</summary>
    private static double MagnitudeOf(ShrineTuning pool, string buffId, StatId stat)
    {
        foreach (var row in pool.Buffs)
        {
            if (!string.Equals(row.Id, buffId, StringComparison.Ordinal))
            {
                continue;
            }

            return row is { Stat: { } token, Magnitude: { } magnitude } &&
                   RunStatTokens.TryParse(token, out var parsed) &&
                   parsed == stat
                ? magnitude
                : 0.0;
        }

        // A buff this content version no longer authors contributes nothing, on the effect sources'
        // precedent: a content rollback across a live run must not make the run unplayable.
        return 0.0;
    }
}
