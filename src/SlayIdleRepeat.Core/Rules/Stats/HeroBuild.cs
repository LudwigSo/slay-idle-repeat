using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The hero as the game actually sees them: the base curve they stand on, the effects their gear
/// contributes, and the stat block those two produce.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><see cref="BaseStats"/> plus <see cref="Effects"/> is what a fight is handed, never
/// <see cref="Stats"/>.</b> The simulator re-aggregates an actor's effects every pass, so a
/// pre-aggregated block passed as the base applies the whole loadout twice — it throws nothing, the
/// fight completes, and the only symptom is a hero who is silently far too strong.
/// </para>
/// <para>
/// <see cref="Stats"/>, <see cref="MaxHp"/> and <see cref="UnappliedEffects"/> are a reading at one
/// moment: nothing caches them, because equipping an item changes them and a stale copy is a screen
/// that disagrees with the fight.
/// </para>
/// <para>
/// <see cref="UnappliedEffects"/> is where a gold-gain or pet-aura affix ends up. Those are
/// collected, valued and reported rather than dropped — the stat block holds fourteen combat stats
/// and nothing else, so the pipeline names what it did not apply instead of losing it.
/// </para>
/// </remarks>
public sealed class HeroBuild
{
    private HeroBuild(
        ActorStats baseStats,
        IReadOnlyList<EffectDefinition> effects,
        IReadOnlyList<CollectedEffect> collected,
        AggregatedStats aggregated,
        IReadOnlyList<GearInstance> equipped)
    {
        BaseStats = baseStats;
        Effects = effects;
        Collected = collected;
        Aggregated = aggregated;
        Equipped = equipped;
    }

    /// <summary><c>Base(stat)</c> at this Legend Level — the block a fight starts from.</summary>
    public ActorStats BaseStats { get; }

    /// <summary>What the loadout contributes, in resolution order. This is the list a fight holds.</summary>
    public IReadOnlyList<EffectDefinition> Effects { get; }

    /// <summary>The items the block was built from, in slot order.</summary>
    public IReadOnlyList<GearInstance> Equipped { get; }

    /// <summary>
    /// The same effects as <see cref="Effects"/>, each still carrying the holding it came from —
    /// what a fight has to be handed.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Not interchangeable with <see cref="Effects"/>, and the difference is load-bearing.</b>
    /// A roster refuses an <c>ON_KILL</c> effect that arrives without an instance id, because
    /// <c>ON_KILL</c> counters are run-scoped and a battle-local id would reset one every fight. The
    /// four-piece bonus of an authored set is exactly such an effect, so a hero in a full set flattened
    /// down to bare definitions cannot be put in a fight at all. The holdings are spelled from the gear
    /// instance rather than the slot, so they survive a battle boundary.
    /// <para>
    /// Internal: a caller outside <c>Core</c> reaching a fight through <c>CombatSimulator</c>'s public
    /// doors is handed battle-local ids and that same refusal, which is the correct answer for a
    /// caller with no run to take an id from.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<CollectedEffect> Collected { get; }

    /// <summary>
    /// The aggregation this build reads its published figures off.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Internal, and this type is a class rather than a record so that it can be.</b> A
    /// positional record's parameters are public properties, so carrying the aggregate as one would
    /// have exported <see cref="AggregatedStats"/> — and with it the attack pipeline's heal
    /// ceiling — to every consumer of a hero screen. The three facts a caller outside <c>Core</c>
    /// actually needs are published individually instead.
    /// </remarks>
    internal AggregatedStats Aggregated { get; }

    /// <summary>The capped, rounded fourteen-stat block.</summary>
    public ActorStats Stats => Aggregated.Final;

    /// <summary>
    /// The health pool the fight runs on, after every multiplicative source.
    /// </summary>
    /// <remarks>
    /// Not the capped <c>MAX_HP</c> stat, which is the same number only while nothing multiplies it.
    /// A screen reading the stat instead would show the smaller of the two under the larger's name.
    /// </remarks>
    public double MaxHp => Aggregated.PostMultiplierMaxHp;

    /// <summary>
    /// The effects this build valued but could not apply, by id — a gold-gain or pet-aura affix,
    /// whose stat is outside the fourteen the block holds.
    /// </summary>
    /// <remarks>
    /// Named rather than dropped: a caller that could not read this would have no way to tell an
    /// affix the pipeline deliberately skipped from one it silently lost.
    /// </remarks>
    public IReadOnlyList<string> UnappliedEffects => Aggregated.SkippedNonCombatStatEffects;

    /// <summary>
    /// Builds the hero from a player and, where there is one, the run they are inside.
    /// </summary>
    /// <param name="player">The player aggregate.</param>
    /// <param name="run">
    /// The run in the command's slice, or <see langword="null"/> outside a run.
    /// <para>
    /// 🔒 A run fights with the loadout it was started with — but that snapshot holds <em>identities</em>,
    /// not items, and the aggregate's own ruling is that this is correct: what is frozen is which
    /// items are equipped, and what an item IS lives in the stock. So an enhancement applied to a worn
    /// item mid-run is worn immediately, and this build reflects it.
    /// </para>
    /// <para>
    /// ⚠️ That has a consequence for anything checking a client's reported fight against a re-derived
    /// one: the hero is a function of the stock at the moment of derivation, so a battle opened
    /// before an enhancement and confirmed after it is two different heroes at the same seed. The
    /// answer is a matter for whichever handler compares the two, not for this type.
    /// </para>
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
            content,
            run?.DraftedPerks);
    }

    /// <summary>
    /// Builds the hero from the persisted rows — the door an assembly outside <c>Core</c> comes in
    /// through, since the aggregates have no public factory that returns one.
    /// </summary>
    /// <param name="player">The player's row.</param>
    /// <param name="run">The row of the run they are inside, or <see langword="null"/> outside a run.</param>
    /// <param name="content">The version-stamped content snapshot being read.</param>
    /// <returns>The build the aggregates would have produced.</returns>
    /// <remarks>
    /// Rehydrated rather than read field by field, so this door and the domain's own cannot drift:
    /// the loadout a run froze names identities the stock resolves, and resolving them a second way
    /// here is how a hero screen ends up disagreeing with the fight it is describing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">A row does not rehydrate.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The Legend Level is outside the authored curve.</exception>
    public static HeroBuild Of(PlayerSnapshot player, RunSnapshot? run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(content);

        return Of(
            RowDoor.Player(player, content, nameof(player)),
            run is null ? null : RowDoor.Run(run, nameof(run)),
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
    /// <remarks>
    /// ⚠️ <b>Internal, despite being the seam a side-by-side item comparison wants.</b> Nothing
    /// outside <c>Core</c> can construct a <see cref="GearInstance"/> — it has no public constructor
    /// and no public factory returning one — so a public overload here would be an entry point no
    /// outside assembly could call, and the architecture suite says so by name. A comparison seam
    /// that takes what a client actually holds is a separate decision, not something to fake by
    /// widening a keyword.
    /// </remarks>
    internal static HeroBuild Of(
        int legendLevel,
        IReadOnlyList<GearInstance> equipped,
        ContentSnapshot content,
        DraftedPerks? perks = null)
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
            new SetBonusEffectSource(catalogue, drops, sets, inSlotOrder),
            new PerkEffectSource(content, perks ?? NoPerks));

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
            collected,
            StatAggregation.Aggregate(baseStats, effects, caps.Caps, StatAggregationSeams.Strict),
            inSlotOrder);
    }

    /// <summary>
    /// The drafted-perk reading of a build outside a run — a hero screen, a preview, a comparison.
    /// </summary>
    /// <remarks>
    /// An empty holding rather than an absent source: the perk source is composed unconditionally so
    /// that "this build has no perks" and "nobody wired perks in" cannot look alike from the fight's
    /// side, which is the shape the whole source list was in before M3-07's row was cleared.
    /// </remarks>
    private static DraftedPerks NoPerks { get; } =
        new(new Dictionary<string, int>(StringComparer.Ordinal));

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
