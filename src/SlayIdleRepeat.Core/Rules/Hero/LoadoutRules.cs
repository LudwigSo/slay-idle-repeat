using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>The rules that decide what a loadout may contain, and what applying a preset produces.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>An equipped slot is a reference into the stock, never a copy of an item.</b> Everything here
/// is stated over the identities the loadout names and the identities the stock holds, and nothing
/// here constructs, copies or alters an item — the stock is the one place a rolled instance lives.
/// </para>
/// <para>
/// 🔒 <b>A held item is not an equippable one.</b> An item parked in the overflow holding list is
/// owned and unreachable until space exists; equipping from there would let a full inventory be
/// emptied through the hero screen, which is the reclaim rule's job and nothing else's.
/// </para>
/// <para>
/// It deliberately says nothing about entitlement. No type under <c>Rules/</c> may name
/// <c>Entitlements</c> — `30` §3 licenses exactly one exemption and it is the ad-reward cap's — so
/// "may this player write a fourth preset" is answered by the handler, where the session is visible.
/// </para>
/// </remarks>
internal static class LoadoutRules
{
    /// <summary>Whether the loadout may be changed at all right now.</summary>
    /// <param name="run">The run in the command's slice, or <see langword="null"/> outside a run.</param>
    /// <returns><see langword="true"/> when no run is in progress.</returns>
    /// <remarks>
    /// 🔒 `07` §4: equipping is free and unlimited OUTSIDE a run, and the loadout cannot be changed
    /// during one. Stated here rather than spelled at each call site, because every command that
    /// moves the hero's gear needs the same predicate — <c>APPLY_PRESET</c> today, <c>EQUIP</c>
    /// (M4-03) and the destructive forge operations (M4-04) next — and three copies of it is three
    /// chances for one of them to read the condition slightly differently.
    /// <para>
    /// An ENDED run does not block: it can no longer be played, so nothing it holds is being changed
    /// under the player. The run's own <c>StartingLoadout</c> is frozen regardless, so even a change
    /// that slipped through could not alter what an in-progress run is fighting with — what this
    /// prevents is the player's screen disagreeing with the run they are in.
    /// </para>
    /// </remarks>
    internal static bool MayChangeLoadout(Model.Run? run) =>
        run is null or { Phase: RunPhase.Ended };

    /// <summary>Whether an identity is one this player could wear right now.</summary>
    /// <param name="item">The instance a command named.</param>
    /// <param name="stock">The player's inventory.</param>
    /// <returns><see langword="true"/> when the stock holds it in stock rather than in overflow.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stock"/> is null.</exception>
    internal static bool IsEquippable(GearInstanceId item, Model.Gear.Inventory stock)
    {
        ArgumentNullException.ThrowIfNull(stock);

        var availability = stock.Availability(item);

        // A LOCKED item is equippable: the lock protects it from a destructive operation, which is
        // the opposite of taking it out of use.
        return availability is Model.Gear.ItemAvailability.AVAILABLE
            or Model.Gear.ItemAvailability.LOCKED;
    }

    /// <summary>
    /// The loadout applying <paramref name="preset"/> actually produces: every slot the preset names
    /// whose item the player still holds, and nothing else.
    /// </summary>
    /// <param name="preset">The saved loadout.</param>
    /// <param name="stock">The player's inventory.</param>
    /// <returns>The loadout to install. Empty when the player owns none of the preset's items.</returns>
    /// <remarks>
    /// 🔒 <b>Best-effort, not all-or-nothing, and that is the ruling rather than a shortcut.</b> A
    /// preset is a record of a build the player once had; an item in it can be salvaged, merged away
    /// or lost to any later operation. Refusing the whole preset because one ring is gone would make
    /// presets stop working as the player plays, which is exactly when they are worth having — and
    /// the alternative of deleting the preset would destroy the only record of the build. So the
    /// missing slots are simply left empty, and the preset is unchanged: `12` §2.2 keeps a preset the
    /// player may no longer write as one they may still load.
    /// <para>
    /// It builds up from <see cref="Loadout.Empty"/> rather than editing the current loadout, so
    /// applying a preset REPLACES what the hero is wearing rather than merging into it. A merge would
    /// leave the player wearing pieces of two builds and no way to tell which.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static Loadout Applied(Loadout preset, Model.Gear.Inventory stock)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(stock);

        var applied = Loadout.Empty;

        foreach (var (slot, item) in preset.Gear)
        {
            if (IsEquippable(item, stock))
            {
                applied = applied.With(slot, item);
            }
        }

        return applied;
    }
}
