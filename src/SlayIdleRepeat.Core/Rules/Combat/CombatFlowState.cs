using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// One `18` §2.4 <c>ATTACK_MULT_NEXT</c> grant: a multiplier, the swings it has left, and the effect
/// that granted it.
/// </summary>
/// <param name="Multiplier">The factor applied to `05` §4's <c>AttackMultiplier</c>.</param>
/// <param name="Charges">How many further attacks it applies to.</param>
/// <param name="SourceEffectId">
/// 🔒 `05` §4: the charges are <em>"consumed in ascending effect-id order"</em>, so the id is not a
/// log label here — it is the sort key that makes two simultaneous grants deterministic.
/// </param>
internal readonly record struct AttackMultiplierCharge(double Multiplier, int Charges, string SourceEffectId);

/// <summary>
/// One armed death save — `18` §2.4's <c>SURVIVE_LETHAL</c> or <c>REVIVE</c>.
/// </summary>
/// <param name="Hp">The HP the actor is left at.</param>
/// <param name="IsRevive">
/// <c>true</c> for <c>REVIVE</c>, which fires <c>ON_REVIVE</c>; <c>false</c> for
/// <c>SURVIVE_LETHAL</c>, which does not, because `18` §3 is explicit that the actor never died.
/// </param>
/// <param name="SourceEffectId">The `18` §8 effect id that armed it.</param>
/// <param name="FiresOnce">
/// 🔒 `05` §3.1's anti-loop rule — <em>"<c>SURVIVE_LETHAL</c> / <c>REVIVE</c> effects fire at most
/// their authored <c>once</c> count per battle"</em>, read from <c>EffectTrigger.Once</c>.
/// </param>
internal readonly record struct DeathSave(double Hp, bool IsRevive, string SourceEffectId, bool FiresOnce);

/// <summary>
/// 🔒 `18` §2.4's per-actor flow state — the charges, saves, buckets and multipliers the ops write
/// and `05` §4 reads. The half of <c>ICombatFlowSink</c> that is <em>state</em>; the half that is
/// <em>roster</em> lives on <see cref="BattleSimulation"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EffectOpSeams</c> assigns <c>ICombatFlowSink</c> to M2-08 with the reason: <em>"`18` §2.4's
/// flow state lives on the actor, and the tick loop that holds it is M2-08's."</em> This is that
/// state, one instance per <see cref="BattleActor"/>, created with it and discarded with the fight.
/// </para>
/// <para>
/// 🔒 <b>Everything ordered here is ordered by effect id, ordinally.</b> `05` §4 keys on that order
/// for the multiplier charges and for the <c>DAMAGE_TAKEN_MULT</c> product, and `18` §8 makes it
/// ordinal. A bare <c>OrderBy(x =&gt; x.Id)</c> would consult the ambient collation —
/// <c>StringOrderingRuleTests</c> fails the build on exactly that.
/// </para>
/// </remarks>
internal sealed class CombatFlowState
{
    private readonly List<AttackMultiplierCharge> _attackMultipliers = new();
    private readonly List<DeathSave> _armedSaves = new();
    private readonly List<(double Multiplier, string SourceEffectId)> _damageTakenMultipliers = new();
    private readonly Dictionary<StatId, double> _percentBuckets = new();
    private readonly Dictionary<string, int> _saveFirings = new(StringComparer.Ordinal);

    private int _forcedCritCharges;

    /// <summary>`18` §2.4's <c>FORCE_CRIT_NEXT</c> — how many further attacks always crit.</summary>
    internal int ForcedCritCharges => _forcedCritCharges;

    /// <summary>The `18` §2.4 percent buckets written onto this actor by <c>STAT_COPY</c>.</summary>
    /// <remarks>
    /// 🔒 R13 — <c>STAT_COPY</c> writes onto the <b>holder</b>, not the effect's <c>target</c>. These
    /// are read by <see cref="HighestPercentBonusStat"/> and folded into `18` §8 step 5 as
    /// <c>STAT_ADD_PCT</c>.
    /// </remarks>
    internal IReadOnlyDictionary<StatId, double> PercentBuckets => _percentBuckets;

    /// <summary>`18` §2.4 — arms an <c>ATTACK_MULT_NEXT</c> grant.</summary>
    internal void GrantAttackMultiplier(double multiplier, int charges, string sourceEffectId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charges);

        _attackMultipliers.Add(new AttackMultiplierCharge(multiplier, charges, sourceEffectId));
    }

    /// <summary>
    /// 🔒 `05` §4's <c>AttackMultiplier</c> for one swing — <em>"base 1.0 on every basic attack"</em>,
    /// times every armed <c>ATTACK_MULT_NEXT</c> charge, in ascending effect-id order, each of which
    /// is then spent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Every armed grant applies to the same swing, and that is a ruling.</b> `05` §4 says the
    /// charges are <em>"consumed in ascending effect-id order"</em> and does not say one per attack.
    /// Two grants alive at once — <c>PK_OPENER</c>'s ×3 opener and a <c>PK_GAMBLER</c> roll — are two
    /// independent promises about <em>the next attack</em>, and spending only the lower-id one would
    /// silently delay the other past the swing it was authored for. Ordering matters anyway, because
    /// double multiplication is not commutative in the low bits and `11` §6 compares those bits.
    /// </para>
    /// <para>
    /// It <b>resets to 1.0 after every resolved attack</b> (`05` §4) by construction: the transient
    /// is this return value and is never stored.
    /// </para>
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
            multiplier = Math.Round(multiplier * charge.Multiplier, StatRounding.Decimals);
            _attackMultipliers[i] = charge with { Charges = charge.Charges - 1 };
        }

        _attackMultipliers.RemoveAll(static c => c.Charges <= 0);

        return multiplier;
    }

    /// <summary>`18` §2.4 — arms <c>FORCE_CRIT_NEXT</c>.</summary>
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

    /// <summary>`18` §2.4 — arms a <c>SURVIVE_LETHAL</c> or <c>REVIVE</c> save.</summary>
    internal void ArmDeathSave(DeathSave save) => _armedSaves.Add(save);

    /// <summary>
    /// 🔒 `05` §3.1's anti-loop rule — the armed save that may fire now, in ascending effect-id
    /// order, or <c>null</c> when every armed save has spent its authored <c>once</c>.
    /// </summary>
    /// <param name="revive">
    /// <c>true</c> to look for a <c>REVIVE</c> (the actor is at 0 HP), <c>false</c> for a
    /// <c>SURVIVE_LETHAL</c> (the hit would be fatal and has not landed).
    /// </param>
    /// <remarks>
    /// 🔒 <b>Consuming is what makes the rule an anti-loop rule.</b> A save that returned itself
    /// forever would let a lethal hit resolve, restore the actor, resolve again and never terminate —
    /// which is why `05` §3.1 lists it beside the thorns rule rather than with the ops.
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

    /// <summary>`18` §2.4 — adds a <c>DAMAGE_TAKEN_MULT</c>.</summary>
    internal void AddDamageTakenMultiplier(double multiplier, string sourceEffectId) =>
        _damageTakenMultipliers.Add((multiplier, sourceEffectId));

    /// <summary>
    /// 🔒 `05` §4 step 6 — <em>"<c>Π defender.DamageTakenMult</c> … product, ascending effect-id
    /// order"</em>. <c>1.0</c> when none is active.
    /// </summary>
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
            product = Math.Round(product * multiplier, StatRounding.Decimals);
        }

        return product;
    }

    /// <summary>`18` §2.4's <c>STAT_COPY</c> write — a percent-bucket add onto the holder.</summary>
    internal void AddPercentBucket(StatId stat, double fraction) =>
        _percentBuckets[stat] = Math.Round(
            _percentBuckets.GetValueOrDefault(stat) + fraction, StatRounding.Decimals);

    /// <summary>
    /// 🔒 `18` §2.4's <c>HIGHEST_PCT_BONUS</c> — <em>"whichever stat carries the largest percent
    /// bucket at copy time"</em> (Cogitator's Recalibrate, `17` §7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The tie-break is errata.</b> `18` §2.4 authors none, and two stats carrying the same
    /// bucket is reachable — a build with "+10% ATK" and "+10% DEF" and nothing else. Picking by
    /// iteration order over a <c>Dictionary</c> would make the answer depend on insertion history and
    /// hash layout, which is a `14` §8.2 determinism break that reproduces only sometimes. The
    /// declaration order of `05` §1's table (<c>StatIds.Combat</c>) is used instead: it is already the
    /// canonical order of the stat block, it is stated in a locked document, and it is total.
    /// </para>
    /// <para>
    /// Returns <c>null</c> when no bucket has been written, rather than naming an arbitrary stat —
    /// steering S6. A copy with nothing to copy is a hole for the op to report, not a zero to invent.
    /// </para>
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
