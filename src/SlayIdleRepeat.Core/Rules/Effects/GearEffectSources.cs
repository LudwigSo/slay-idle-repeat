using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The three gear sources of the collection step, and the naming they share.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every effect these sources contribute is synthesised, and its id is spelled so it cannot
/// collide with an authored one.</b> Authored effect ids are upper-case identifiers; these open with
/// a bracket, which sorts below every letter and is unrepresentable in the authored id space. The
/// resolution order is stated over ids, so two contributions that could ever be held at once must
/// differ — which is why a gear id names the slot and the position rather than the stat, and an affix
/// id names the slot and the affix.
/// </para>
/// <para>
/// The <em>holding</em> is a different key from the id and is spelled from the gear instance, not from
/// the slot: it has to survive a battle boundary, and re-equipping the same item into another slot is
/// the same holding while equipping a different item into the same slot is not.
/// </para>
/// <para>
/// These live here rather than beside the gear rules because the layering runs one way: a gear
/// derivation must never read the effect pipeline it feeds, so the pipeline reads the derivation.
/// </para>
/// </remarks>
internal static class GearEffectNames
{
    /// <summary>The synthetic id of one slot's primary or secondary contribution.</summary>
    internal static string StatEffectId(GearSlot slot, bool primary) =>
        $"(gear:{slot}:{(primary ? "primary" : "secondary")})";

    /// <summary>The synthetic id of one affix roll on one slot.</summary>
    internal static string AffixEffectId(GearSlot slot, string affixId) => $"(affix:{slot}:{affixId})";

    /// <summary>The holding a slot's stat contribution belongs to.</summary>
    internal static EffectInstanceId StatHolding(GearInstance item, bool primary) =>
        EffectInstanceId.Of($"gear:{item.InstanceId.Value}:{(primary ? "primary" : "secondary")}");

    /// <summary>The holding one affix roll belongs to.</summary>
    internal static EffectInstanceId AffixHolding(GearInstance item, string affixId) =>
        EffectInstanceId.Of($"affix:{item.InstanceId.Value}:{affixId}");

    /// <summary>The holding one set breakpoint's effect belongs to.</summary>
    internal static EffectInstanceId SetHolding(GearFamilyAxis set, string effectId) =>
        EffectInstanceId.Of($"set:{set}:{effectId}");

    /// <summary>The equipped items in slot order — the order every gear source enumerates in.</summary>
    /// <remarks>
    /// <para>
    /// Slot order rather than the caller's, because the collection order is a tiebreak of the
    /// resolution order and therefore has to be a function of the build. A list handed over in
    /// whatever order a dictionary enumerated would put a device-dependent order into a stat block.
    /// </para>
    /// <para>
    /// 🔒 <b>The comparator is total, and the slot alone is not.</b> This is a plain unstable sort, so
    /// two items sharing a slot would be ordered by the algorithm rather than by the data — which is
    /// the same defect the effect resolution order exists to close, reintroduced at the step before
    /// it. A worn loadout holds one item per slot and could never reach it; the seam that takes an
    /// explicit list can, and a side-by-side preview is exactly a candidate item beside the equipped
    /// one in its own slot. The identity breaks the tie because it is the only other thing an item
    /// carries that orders at all.
    /// </para>
    /// <para>
    /// Each source re-orders rather than trusting its caller, and pays for the pass with the
    /// per-element null check: a source is constructible on its own, so "already ordered" is not
    /// something it can assume.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<GearInstance> InSlotOrder(IReadOnlyList<GearInstance> equipped)
    {
        ArgumentNullException.ThrowIfNull(equipped);

        var ordered = new List<GearInstance>(equipped.Count);

        foreach (var item in equipped)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(equipped));
            ordered.Add(item);
        }

        ordered.Sort(static (left, right) =>
        {
            var bySlot = left.Slot.CompareTo(right.Slot);

            return bySlot != 0
                ? bySlot
                : string.CompareOrdinal(left.InstanceId.Value, right.InstanceId.Value);
        });

        return ordered.AsReadOnly();
    }
}

/// <summary>
/// The <c>GEAR</c> source: what the equipped items themselves contribute, at the enhancement level
/// they stand at.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the caller the derivation's own remarks describe.</b> The derivation answers what an
/// item rolled and deliberately leaves enhancement out; the forge prices the ladder and answers what
/// a level multiplies by. Composing the two is this type's whole job, and it is why a <c>+15</c> item
/// is worth more than a <c>+0</c> of the same roll.
/// </para>
/// <para>
/// <b>Both contributions are flat adds, whether or not the slot table calls the stat a percentage.</b>
/// The two kinds differ in how they <em>scale</em> — a flat stat with the chapter, a percent stat with
/// rarity alone — not in which bucket they land in. Aggregation multiplies the running value by
/// <c>1 + Σ percent</c>, and the hero's base lifesteal, block and penetration are zero: routing a
/// percent-typed slot stat into the percent bucket would multiply zero and hand the player an item
/// whose second stat does nothing.
/// </para>
/// </remarks>
internal sealed class GearEffectSource : IEffectSource
{
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads the equipped items' own two stats each.</summary>
    /// <param name="par">The par table, for the chapter half of an item's power.</param>
    /// <param name="drops">The gear tables — the slot coefficients and the percent-stat table.</param>
    /// <param name="forge">The forge numbers, for the enhancement ladder's multiplier.</param>
    /// <param name="equipped">The items currently worn. Never null; may be empty.</param>
    /// <exception cref="ArgumentNullException">An argument is null, or an element is.</exception>
    /// <exception cref="InvalidTunableException">A slot names a stat the vocabulary does not have.</exception>
    internal GearEffectSource(
        ParPowerTuning par, DropsTuning drops, ForgeTuning forge, IReadOnlyList<GearInstance> equipped)
    {
        ArgumentNullException.ThrowIfNull(par);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(forge);

        var effects = new List<SourcedEffect>();

        foreach (var item in GearEffectNames.InSlotOrder(equipped))
        {
            var multiplier = forge.StatMultiplier(item.EnhanceLevel);

            effects.Add(Contribution(
                GearStatDerivation.Primary(par, drops, item), item, multiplier, primary: true));
            effects.Add(Contribution(
                GearStatDerivation.Secondary(par, drops, item), item, multiplier, primary: false));
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.GEAR;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;

    private static SourcedEffect Contribution(
        DerivedGearStat derived, GearInstance item, double multiplier, bool primary)
    {
        var effect = new EffectDefinition
        {
            Id = GearEffectNames.StatEffectId(item.Slot, primary),
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatOf(derived, item)),
            Trigger = EffectDefaults.Always,
            Target = EffectDefaults.AbsentTarget,
            Value = DeterminismRounding.Round(derived.Value * multiplier),
        };

        return new SourcedEffect(effect, GearEffectNames.StatHolding(item, primary));
    }

    /// <summary>The stat a slot row's authored token names.</summary>
    /// <remarks>
    /// Refused rather than skipped when the token is not one of the stats: a slot whose stat nothing
    /// can resolve is an item wearing a number the game has nowhere to put, and dropping it silently
    /// would make that item weaker than its own tooltip.
    /// </remarks>
    private static StatId StatOf(DerivedGearStat derived, GearInstance item)
    {
        if (AuthoredToken.TryParse<StatId>(derived.Stat, out var stat))
        {
            return stat;
        }

        throw new InvalidTunableException(
            DropsTuning.SlotCoefficientsReference,
            $"The {item.Slot} row names '{derived.Stat}', which is not one of the stats. The authored " +
            $"set is {AuthoredToken.Names<StatId>()}, and an item whose stat cannot be resolved " +
            "contributes nothing while still reading as a rolled item.");
    }
}

/// <summary>The <c>AFFIXES</c> source: the rolled affixes on the equipped items.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>The stat and the bucket come from the authored pool, never from this code.</b> Which stat an
/// affix writes and whether it writes percentage points or a multiplier is a tuning row, so a
/// re-tune is a data edit; a table here would be a second answer no rule could see.
/// </para>
/// <para>
/// An affix the pool authors with no stat contributes nothing, and that is the authored statement
/// rather than a failure: the pool carries a null exactly where the design set describes a bonus the
/// stat block has no slot for. It is skipped silently here because the pool has already said so
/// loudly — the refusal that matters is a <em>half</em>-authored row, which the tuning reader makes.
/// </para>
/// </remarks>
internal sealed class GearAffixEffectSource : IEffectSource
{
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads the affixes rolled onto the equipped items.</summary>
    /// <param name="drops">The gear tables, for the affix pool's stat and bucket.</param>
    /// <param name="equipped">The items currently worn. Never null; may be empty.</param>
    /// <exception cref="ArgumentNullException">An argument is null, or an element is.</exception>
    /// <exception cref="InvalidTunableException">An item carries an affix the pool does not declare.</exception>
    internal GearAffixEffectSource(DropsTuning drops, IReadOnlyList<GearInstance> equipped)
    {
        ArgumentNullException.ThrowIfNull(drops);

        var effects = new List<SourcedEffect>();

        foreach (var item in GearEffectNames.InSlotOrder(equipped))
        {
            foreach (var roll in item.Affixes)
            {
                var definition = drops.Affix(roll.AffixId);

                if (!definition.WritesAStat)
                {
                    continue;
                }

                var effect = new EffectDefinition
                {
                    Id = GearEffectNames.AffixEffectId(item.Slot, roll.AffixId),
                    Op = definition.Op!.Value,
                    Stat = StatSelector.Of(definition.Stat!.Value),
                    Trigger = EffectDefaults.Always,
                    Target = EffectDefaults.AbsentTarget,

                    // The pool's context gate rides the synthesised effect untouched — the whole
                    // of what makes a target-gated affix a data row rather than a special case.
                    Condition = definition.Condition,
                    Value = roll.Value,
                };

                effects.Add(new SourcedEffect(effect, GearEffectNames.AffixHolding(item, roll.AffixId)));
            }
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.AFFIXES;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}

/// <summary>The <c>SET_BONUSES</c> source: what the loadout's completed set tiers grant.</summary>
/// <remarks>
/// Which sets are worn and which breakpoints they have reached is the set resolver's answer; what a
/// breakpoint grants is authored content. Neither half is restated here — this type is the join, and
/// it is the first production caller of both.
/// </remarks>
internal sealed class SetBonusEffectSource : IEffectSource
{
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads the set bonuses the worn loadout has earned.</summary>
    /// <param name="catalogue">The base-item catalogue, which maps a family to its axis.</param>
    /// <param name="drops">The gear tables, for the authored breakpoints.</param>
    /// <param name="sets">The authored set bonuses.</param>
    /// <param name="equipped">The items currently worn. Never null; may be empty.</param>
    /// <exception cref="ArgumentNullException">An argument is null, or an element is.</exception>
    internal SetBonusEffectSource(
        GearCatalogue catalogue,
        DropsTuning drops,
        SetBonusCatalogue sets,
        IReadOnlyList<GearInstance> equipped)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var effects = new List<SourcedEffect>();

        foreach (var active in SetBonusResolver.Resolve(catalogue, drops, equipped))
        {
            foreach (var granted in sets.Granted(active.Set, active.Pieces))
            {
                effects.Add(new SourcedEffect(
                    granted, GearEffectNames.SetHolding(active.Set, granted.Id)));
            }
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.SET_BONUSES;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}
