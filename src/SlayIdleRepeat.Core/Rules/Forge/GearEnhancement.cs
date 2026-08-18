using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>
/// One enhancement attempt: what it costs, what chance it has, and what the item looks like
/// afterwards.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>An attempt never destroys or downgrades the item.</b> A failure costs the stones and
/// nothing else — the level stands, the affixes stand, the item stands. Destructive enhancement is a
/// monetisation mechanic, and there is no monetisation here for it to serve.
/// </para>
/// <para>
/// <b>The chance comes back from the luck façade, and is not computed here.</b> The failure mercy is
/// the <c>ENHANCE</c> class's guarantee, so it fires in one place; this type asks what the chance is
/// and draws against it.
/// </para>
/// <para>
/// <b>One draw per attempt, taken whatever the outcome.</b> Success and failure consume the same
/// single index, so a resumed stream lands in the same place either way.
/// </para>
/// </remarks>
internal static class GearEnhancement
{
    /// <summary>An attempt with nothing riding on it — no ad bonus and no subscription charge.</summary>
    internal const double NoLuckyBonus = 0.0;

    /// <summary>Whether the item is already at the ceiling, so no attempt is possible.</summary>
    /// <param name="enhanceLevel">The level the item stands at.</param>
    /// <param name="tuning">The forge numbers.</param>
    /// <returns><see langword="true"/> when nothing can be attempted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    internal static bool IsAtCeiling(int enhanceLevel, ForgeTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return enhanceLevel >= tuning.MaxEnhanceLevel;
    }

    /// <summary>The chance one attempt on this item would actually have, mercy and bonus included.</summary>
    /// <param name="enhanceLevel">The level the item stands at.</param>
    /// <param name="consecutiveFailures">The item's mercy counter.</param>
    /// <param name="luckyBonus">The one-attempt bonus riding on it, as a share. Zero when none is.</param>
    /// <param name="tuning">The forge numbers, for the level's unmodified chance.</param>
    /// <param name="mercy">The authored mercy slope and ceiling.</param>
    /// <returns>The chance the attempt is drawn against.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The item is already at the ceiling.</exception>
    internal static double EffectiveRate(
        int enhanceLevel,
        int consecutiveFailures,
        double luckyBonus,
        ForgeTuning tuning,
        EnhanceRule mercy)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return LuckService.EnhanceSuccessRate(
            tuning.EnhanceSuccessRate(enhanceLevel + 1), consecutiveFailures, luckyBonus, mercy);
    }

    /// <summary>Takes one attempt at the next level.</summary>
    /// <param name="item">The item being enhanced.</param>
    /// <param name="luckyBonus">The one-attempt bonus riding on it, as a share. Zero when none is.</param>
    /// <param name="tuning">The forge numbers.</param>
    /// <param name="mercy">The authored mercy slope and ceiling.</param>
    /// <param name="draws">The already-opened draw stream, continued. Exactly one index is consumed.</param>
    /// <returns>The item as it now stands, whether the attempt succeeded, and the chance it had.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The item is already at the ceiling.</exception>
    internal static (GearInstance Item, bool Succeeded, double Rate) Attempt(
        GearInstance item, double luckyBonus, ForgeTuning tuning, EnhanceRule mercy, DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(draws);

        var rate = EffectiveRate(item.EnhanceLevel, item.EnhanceFailures, luckyBonus, tuning, mercy);
        var succeeded = draws.NextDouble() < rate;

        var failures = LuckService.EnhanceFailuresAfter(
            item.EnhanceFailures, succeeded, luckyBonus > 0.0, mercy);

        var enhanced = new GearInstance(
            item.InstanceId,
            item.DefId,
            item.Slot,
            item.Family,
            item.Rarity,
            item.ChapterOrigin,
            item.Quality,
            succeeded ? item.EnhanceLevel + 1 : item.EnhanceLevel,
            failures,
            item.Affixes,
            item.Locked);

        return (enhanced, succeeded, rate);
    }

    // StatMultiplier moved to ForgeTuning when it gained its first callers; its reasons are there.
}
