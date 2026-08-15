using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core.Rules.Board.Resolution;

/// <summary>
/// 🔒 `03` §2 — <c>TILE_CAMPFIRE</c>'s rest option: heal 40% of Max HP.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>No RNG at all.</b> The campfire's three options are fixed — there is nothing to draw, and
/// opening a stream to draw nothing would advance a `14` §8.1 counter and change every later draw in
/// the run.
/// </para>
/// <para>
/// ⚠️ <b>Only the heal is here.</b> The campfire's other two options — upgrade a perk's tier, gain
/// 2 Reroll Charges — act on state that does not exist: drafted perks are M3-06's
/// (<c>GapRegister</c>'s <c>DraftedPerks</c> entry) and reroll charges are tracked nowhere in this
/// codebase at all. <c>CampfireChoose</c> refuses both with <c>ILLEGAL_STATE</c> rather than
/// accepting them as silent no-ops, which would tell a player they had rested when nothing happened.
/// </para>
/// </remarks>
internal static class CampfireResolver
{
    /// <summary>
    /// Rests at the campfire, healing <c>#/inRunIncome/campfire/healPctMaxHp</c> of Max HP.
    /// </summary>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// An accepted result with no events: a heal is a <c>Run</c> mutation and there is no HP domain
    /// event in the game (`30` §7 authors <c>CurrencyChanged</c> and <c>DiceRolled</c> and no third).
    /// </returns>
    /// <remarks>
    /// 🔒 The share is <b>read per command</b> out of this command's own content snapshot, never
    /// held as a C# constant: `21` §3.1 makes a balance number data, and
    /// <see cref="CampfireTuning"/>'s own remarks record why the block exists.
    /// </remarks>
    internal static HandlerResult Heal(HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;
        var tuning = CampfireTuning.Read(input.Context.Content);

        // ⚠️ Overheal is clamped HERE, by the rule that computes it — Run.SetHitPoints refuses a
        // current above the maximum rather than trimming it silently (30 §11.5), so a healing rule
        // that over-delivered could not look correct.
        var healed = (int)Math.Round(run.MaxHp * tuning.HealPctMaxHp, MidpointRounding.ToEven);

        run.SetHitPoints(Math.Min(run.MaxHp, run.CurrentHp + healed), run.MaxHp);

        return HandlerResult.Accept();
    }
}
