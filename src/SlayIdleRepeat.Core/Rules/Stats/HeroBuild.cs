using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The hero as the game actually sees them: the base curve they stand on, the effects their gear
/// contributes, and the stat block those two produce.
/// </summary>
/// <param name="BaseStats">
/// <c>Base(stat)</c> at this Legend Level — the block a fight starts from. A battle is handed
/// <em>this</em> plus <see cref="Effects"/>, never <see cref="Stats"/>: the simulator re-aggregates
/// every pass, so a pre-aggregated block passed as a base would apply the whole loadout twice.
/// </param>
/// <param name="Effects">
/// What the loadout contributes, in resolution order. This is the list a fight holds.
/// </param>
/// <param name="Aggregated">
/// The aggregated result out of battle — what a hero screen reads and what the power index is
/// computed over. It is a reading at one moment: nothing caches it, because equipping an item
/// changes it and a stale copy is a screen that disagrees with the fight.
/// </param>
/// <param name="Equipped">
/// The items the block was built from, in slot order. Carried so a caller can show what produced a
/// number without resolving the loadout a second time and possibly differently.
/// </param>
/// <remarks>
/// <see cref="Aggregated"/>'s <c>SkippedNonCombatStatEffects</c> is where a gold-gain or pet-aura
/// affix ends up. Those are collected, valued and reported rather than dropped — the stat block holds
/// fourteen combat stats and nothing else, so the pipeline names what it did not apply instead of
/// losing it.
/// </remarks>
internal sealed record HeroBuild(
    ActorStats BaseStats,
    IReadOnlyList<EffectDefinition> Effects,
    AggregatedStats Aggregated,
    IReadOnlyList<GearInstance> Equipped)
{
    /// <summary>The capped, rounded fourteen-stat block.</summary>
    internal ActorStats Stats => Aggregated.Final;

    /// <summary>
    /// Builds the hero from a player and, where there is one, the run they are inside.
    /// </summary>
    /// <param name="player">The player aggregate.</param>
    /// <param name="run">
    /// The run in the command's slice, or <see langword="null"/> outside a run. A run fights with the
    /// loadout it was started with, not the one the hero screen shows now — that snapshot is the
    /// whole reason a run carries one.
    /// </param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <returns>The build.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The Legend Level is outside the authored curve.</exception>
    internal static HeroBuild Of(Player player, Model.Run? run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        return Of(
            player.LegendLevel,
            Equip(run is null ? player.Loadout : run.StartingLoadout, player.Inventory),
            content);
    }

    /// <summary>
    /// Builds the hero from a level and an explicit item list — the seam a harness, a preview or a
    /// comparison uses, and the one the whole derivation is actually stated over.
    /// </summary>
    /// <param name="legendLevel">The Legend Level, inside the authored curve.</param>
    /// <param name="equipped">The items worn. Never null; may be empty.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <returns>The build.</returns>
    /// <exception cref="ArgumentNullException">An argument is null, or an element is.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The Legend Level is outside the authored curve.</exception>
    internal static HeroBuild Of(
        int legendLevel, IReadOnlyList<GearInstance> equipped, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(equipped);
        ArgumentNullException.ThrowIfNull(content);

        var caps = CombatCaps.Read(content);
        var drops = DropsTuning.Read(content);
        var par = ParPowerTuning.Read(content);
        var forge = ForgeTuning.Read(content);
        var catalogue = GearCatalogue.Read(content);
        var sets = SetBonusCatalogue.Read(content, drops.SetBreakpoints);

        var inSlotOrder = GearEffectNames.InSlotOrder(equipped);

        var sources = EffectSourceSet.Of(
            new GearEffectSource(par, drops, forge, inSlotOrder),
            new GearAffixEffectSource(drops, inSlotOrder),
            new SetBonusEffectSource(catalogue, drops, sets, inSlotOrder));

        var collected = EffectResolutionOrder.Sort(sources.Collect());
        var effects = new EffectDefinition[collected.Count];

        for (var i = 0; i < effects.Length; i++)
        {
            effects[i] = collected[i].Effect;
        }

        var baseStats = caps.HeroBase.At(legendLevel);

        return new HeroBuild(
            baseStats,
            Array.AsReadOnly(effects),
            StatAggregation.Aggregate(baseStats, effects, caps.Caps, StatAggregationSeams.Strict),
            inSlotOrder);
    }

    /// <summary>The instances a loadout's slots name, as the stock currently holds them.</summary>
    /// <remarks>
    /// 🔒 <b>A slot naming an item the stock no longer holds is skipped, not refused</b>, on the
    /// preset rule's precedent and for its reason. A loadout is a list of identities; a run freezes
    /// one at the start and plays it to the end, so refusing here would mean a single destructive
    /// operation could leave an in-flight run unable to fight at all. The invariant that stops this
    /// happening for the player's own loadout is enforced on the aggregate, in the one place that can
    /// see both halves — this reader is downstream of it and does not restate it.
    /// </remarks>
    private static IReadOnlyList<GearInstance> Equip(Loadout loadout, Model.Gear.Inventory stock)
    {
        var worn = new List<GearInstance>(loadout.EquippedCount);

        foreach (var (_, identity) in loadout.Gear)
        {
            if (stock.Find(identity) is { } item)
            {
                worn.Add(item);
            }
        }

        return worn;
    }
}
