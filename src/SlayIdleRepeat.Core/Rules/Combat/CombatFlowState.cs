using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>One <c>ATTACK_MULT_NEXT</c> grant: a multiplier, the swings it has left, and the effect that granted it.</summary>
/// <param name="Multiplier">The factor applied to the attack multiplier.</param>
/// <param name="Charges">How many further attacks it applies to.</param>
/// <param name="SourceEffectId">
/// Charges are consumed in ascending effect-id order, so the id is not a log label here — it is the
/// sort key that makes two simultaneous grants deterministic.
/// </param>
internal readonly record struct AttackMultiplierCharge(double Multiplier, int Charges, string SourceEffectId);

/// <summary>One armed death save — <c>SURVIVE_LETHAL</c> or <c>REVIVE</c>.</summary>
/// <param name="Hp">The HP the actor is left at.</param>
/// <param name="IsRevive">
/// <c>true</c> for <c>REVIVE</c>, which fires <c>ON_REVIVE</c>; <c>false</c> for
/// <c>SURVIVE_LETHAL</c>, which does not, since the actor never died.
/// </param>
/// <param name="SourceEffectId">The effect id that armed it.</param>
/// <param name="FiresOnce">The anti-loop rule: these effects fire at most their authored <c>once</c> count per battle.</param>
internal readonly record struct DeathSave(double Hp, bool IsRevive, string SourceEffectId, bool FiresOnce);

/// <summary>Per-actor flow state — the charges, saves, buckets and multipliers the ops write and the damage pipeline reads.</summary>
/// <remarks>
/// <para>One instance per <see cref="BattleActor"/>, created with it and discarded with the fight.</para>
/// <para>
/// Everything ordered here is ordered by effect id, ordinally: the damage pipeline keys on that order
/// for the multiplier charges and the incoming-damage-multiplier product. A bare
/// <c>OrderBy(x =&gt; x.Id)</c> would consult the ambient collation instead of ordinal comparison.
/// </para>
/// <para>
/// A stateful class, on <c>CombatLog</c>'s precedent: charges, saves and buckets are written by one op
/// and read by a later attack, so they have to live somewhere across ticks. It is per-actor, per-battle,
/// owned by exactly one caller, never shared and never static.
/// </para>
/// </remarks>
internal sealed class CombatFlowState
{
    private readonly List<AttackMultiplierCharge> _attackMultipliers = new();
    private readonly List<DeathSave> _armedSaves = new();
    private readonly List<(double Multiplier, string SourceEffectId)> _damageTakenMultipliers = new();
    private readonly List<(double Fraction, string SourceEffectId, long Order)> _thorns = new();
    private readonly Dictionary<StatId, double> _percentBuckets = new();
    private readonly Dictionary<string, int> _saveFirings = new(StringComparer.Ordinal);

    private int _forcedCritCharges;

    /// <summary>The total-order tie-break for <see cref="ThornsBonus"/> — a monotonic counter.</summary>
    /// <remarks>
    /// <c>List{T}.Sort</c> is unstable, and one actor can hold two live <c>REFLECT</c>s under the same
    /// authored effect id with different fractions. An id-only comparison would let the sort's internal
    /// choice decide the fourth decimal place — a determinism divergence that reproduces only sometimes.
    /// </remarks>
    private long _nextAdditionOrder;

    /// <summary><c>FORCE_CRIT_NEXT</c> — how many further attacks always crit.</summary>
    internal int ForcedCritCharges => _forcedCritCharges;

    /// <summary>The percent buckets written onto this actor by <c>STAT_COPY</c>.</summary>
    /// <remarks>
    /// <c>STAT_COPY</c> writes onto the holder, not the effect's target. Read by
    /// <see cref="HighestPercentBonusStat"/> and folded into stat aggregation as a <c>STAT_ADD_PCT</c>.
    /// </remarks>
    internal IReadOnlyDictionary<StatId, double> PercentBuckets => _percentBuckets;

    /// <summary>Arms an <c>ATTACK_MULT_NEXT</c> grant.</summary>
    internal void GrantAttackMultiplier(double multiplier, int charges, string sourceEffectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charges);

        _attackMultipliers.Add(new AttackMultiplierCharge(multiplier, charges, sourceEffectId));
    }

    /// <summary>The attack multiplier for one swing: base 1.0, times every armed <c>ATTACK_MULT_NEXT</c> charge, in ascending effect-id order, each of which is then spent.</summary>
    /// <remarks>
    /// <para>
    /// Every armed grant applies to the same swing, and that is a ruling: two grants alive at once are
    /// two independent promises about the next attack, and spending only the lower-id one would
    /// silently delay the other past the swing it was authored for.
    /// </para>
    /// <para>It resets to 1.0 after every resolved attack by construction: the transient is this return value and is never stored.</para>
    /// </remarks>
    internal double ConsumeAttackMultiplier()
    {
        if (_attackMultipliers.Count == 0)
        {
            return 1.0;
        }

        _attackMultipliers.Sort(static (left, right) =>
            EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId));

        var multiplier = 1.0;
        for (var i = 0; i < _attackMultipliers.Count; i++)
        {
            var charge = _attackMultipliers[i];
            multiplier = StatRounding.Round(multiplier * charge.Multiplier);
            _attackMultipliers[i] = charge with { Charges = charge.Charges - 1 };
        }

        _attackMultipliers.RemoveAll(static c => c.Charges <= 0);

        return multiplier;
    }

    /// <summary>Arms <c>FORCE_CRIT_NEXT</c>.</summary>
    internal void GrantForcedCrits(int charges)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charges);

        _forcedCritCharges += charges;
    }

    /// <summary>Spends one forced crit if any is armed. <c>true</c> when this swing is forced.</summary>
    internal bool ConsumeForcedCrit()
    {
        if (_forcedCritCharges <= 0)
        {
            return false;
        }

        _forcedCritCharges--;

        return true;
    }

    /// <summary>Arms a <c>SURVIVE_LETHAL</c> or <c>REVIVE</c> save.</summary>
    internal void ArmDeathSave(DeathSave save) => _armedSaves.Add(save);

    /// <summary>The armed save that may fire now, in ascending effect-id order, or <c>null</c> when every armed save has spent its authored <c>once</c>.</summary>
    /// <param name="revive">
    /// <c>true</c> to look for a <c>REVIVE</c> (the actor is at 0 HP), <c>false</c> for a
    /// <c>SURVIVE_LETHAL</c> (the hit would be fatal and has not landed).
    /// </param>
    /// <remarks>
    /// Consuming is what makes this an anti-loop rule: a save that returned itself forever would let a
    /// lethal hit resolve, restore the actor, resolve again and never terminate.
    /// </remarks>
    internal DeathSave? ConsumeDeathSave(bool revive)
    {
        _armedSaves.Sort(static (left, right) =>
            EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId));

        foreach (var save in _armedSaves)
        {
            if (save.IsRevive != revive)
            {
                continue;
            }

            var fired = _saveFirings.GetValueOrDefault(save.SourceEffectId);
            if (save.FiresOnce && fired >= 1)
            {
                continue;
            }

            _saveFirings[save.SourceEffectId] = fired + 1;

            return save;
        }

        return null;
    }

    /// <summary>How many times a death save from that effect has fired this battle.</summary>
    internal int DeathSaveFirings(string sourceEffectId) => _saveFirings.GetValueOrDefault(sourceEffectId);

    /// <summary>Adds a <c>DAMAGE_TAKEN_MULT</c>.</summary>
    internal void AddDamageTakenMultiplier(double multiplier, string sourceEffectId) =>
        _damageTakenMultipliers.Add((multiplier, sourceEffectId));

    /// <summary>The product of every live <c>DamageTakenMult</c>, in ascending effect-id order. <c>1.0</c> when none is active.</summary>
    internal double DamageTakenMultiplier()
    {
        if (_damageTakenMultipliers.Count == 0)
        {
            return 1.0;
        }

        _damageTakenMultipliers.Sort(static (left, right) =>
            EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId));

        var product = 1.0;
        foreach (var (multiplier, _) in _damageTakenMultipliers)
        {
            product = StatRounding.Round(product * multiplier);
        }

        return product;
    }

    /// <summary><c>REFLECT</c> adds to <c>THORN</c> for its duration.</summary>
    /// <remarks>
    /// The duration is not held here, exactly as <see cref="AddDamageTakenMultiplier"/>'s is not: it is
    /// not wired to this class, so the addition is permanent for the fight — a known gap, recorded
    /// rather than duplicated with a second, disagreeing statement.
    /// </remarks>
    internal void AddThorns(double fraction, string sourceEffectId) =>
        _thorns.Add((fraction, sourceEffectId, _nextAdditionOrder++));

    /// <summary>The <c>THORN</c> contribution from live <c>REFLECT</c>s — a sum, in ascending effect-id order. <c>0</c> when none is active.</summary>
    /// <remarks>
    /// A sum rather than a product: two 10% reflects return 20%, not 21%. The ordering is still
    /// imposed, since floating-point addition is not associative and an unordered sum is not a
    /// deterministic one.
    /// </remarks>
    internal double ThornsBonus()
    {
        if (_thorns.Count == 0)
        {
            return 0.0;
        }

        // Ordinal effect id, then addition order — a total order. See _nextAdditionOrder for why
        // the second key is not optional.
        _thorns.Sort(static (left, right) =>
        {
            var byEffect = EffectOrder.IdComparer.Compare(left.SourceEffectId, right.SourceEffectId);

            return byEffect != 0 ? byEffect : left.Order.CompareTo(right.Order);
        });

        var total = 0.0;
        foreach (var (fraction, _, _) in _thorns)
        {
            total = StatRounding.Round(total + fraction);
        }

        return total;
    }

    /// <summary><c>STAT_COPY</c> write — a percent-bucket add onto the holder.</summary>
    internal void AddPercentBucket(StatId stat, double fraction) =>
        _percentBuckets[stat] = StatRounding.Round(_percentBuckets.GetValueOrDefault(stat) + fraction);

    /// <summary><c>HIGHEST_PCT_BONUS</c> — whichever stat carries the largest percent bucket at copy time.</summary>
    /// <remarks>
    /// <para>
    /// The tie-break is errata: two stats can carry the same bucket, and picking by
    /// <c>Dictionary</c> iteration order would make the answer depend on insertion history and hash
    /// layout — a determinism break that reproduces only sometimes. The stat block's declaration order
    /// is used instead, since it is already canonical, stated in a locked document, and total.
    /// </para>
    /// <para>Returns <c>null</c> when no bucket has been written, rather than naming an arbitrary stat: a copy with nothing to copy is a hole for the op to report, not a zero to invent.</para>
    /// </remarks>
    internal StatId? HighestPercentBonusStat()
    {
        StatId? best = null;
        var bestValue = double.NegativeInfinity;

        foreach (var stat in StatIds.Combat)
        {
            if (!_percentBuckets.TryGetValue(stat, out var value) || value <= bestValue)
            {
                continue;
            }

            best = stat;
            bestValue = value;
        }

        return best;
    }
}
