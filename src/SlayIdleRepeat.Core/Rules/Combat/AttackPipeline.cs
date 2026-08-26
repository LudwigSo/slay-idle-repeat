using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// The ten-step damage pipeline: dodge/crit/block resolution, mitigation, ward absorption, healing and
/// reflect damage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three RNG draws per attack, always in this order: dodge, crit, block.</b> One resolved attack
/// advances the combat stream's position by exactly 1 draw if dodged (steps 4 and 5 never run), or 3
/// otherwise. Steps 4 and 5 draw <em>unconditionally</em>, even when the outcome is already decided —
/// a <c>FORCE_CRIT_NEXT</c> charge overrides the outcome, never the draw. Skipping the draw would make
/// the RNG stream's position depend on flow state a client might not yet have observed, desyncing the
/// replay for the rest of the fight. The same holds for block: a defender with <c>BLOCK = 0</c> still
/// costs a draw.
/// </para>
/// <para>
/// <b>Emission order</b> is step order, not source line order: <c>Attack</c>, then <c>Miss</c> or
/// <c>Crit</c>, <c>Block</c>, <c>WardBroken</c>, <c>Hit</c>, <c>PhaseChange</c>, <c>Heal</c>, and
/// finally the thorns reflect (itself a <c>Hit</c> carrying its own <c>WardBroken</c>/<c>PhaseChange</c>).
/// See <see cref="CombatEventType"/>.
/// </para>
/// <para>
/// This class never fires the on-hit trigger family (<c>BattleSimulation</c> does, from the returned
/// <see cref="AttackResolution"/>) and never composes <c>AttackMultiplier</c> (arrives already resolved,
/// unmultiplied by ATK). It also never decides whether a hit bypasses wards — that is a property of
/// the source effect's tag, decided once by the caller and reported through
/// <see cref="IAttackPipeline.DealMaxHpPctDamage"/>.
/// </para>
/// <para>
/// Stateful only in that it holds the battle it belongs to. One instance per fight, built by
/// <see cref="BattleSeams.For"/>, never static.
/// </para>
/// </remarks>
internal sealed class AttackPipeline : IAttackPipeline
{
    /// <summary>A block halves the hit.</summary>
    private const double BlockMultiplier = 0.5;

    /// <summary>The damage floor: never less than 10% of raw, applied before ward absorption.</summary>
    private const double DamageFloorFraction = 0.10;

    private readonly BattleServices _services;

    /// <summary>Builds the pipeline for one fight.</summary>
    /// <param name="services">This fight's log, draw stream, roster and HP routing.</param>
    internal AttackPipeline(BattleServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <inheritdoc />
    /// <remarks>The ten steps, in order, rounding to 4 dp at every accumulation point.</remarks>
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
        var source = Actor(attacker);
        var target = Actor(defender);

        RequireFinite(attackMultiplier, sourceEffectId, "AttackMultiplier");

        // ── 1 · Dodge — the first draw of the three, always taken.
        if (_services.Rng.NextDouble() < target.Stats[StatId.DODGE])
        {
            _services.Log.Append(
                _services.Tick, CombatEventType.Miss, source.LogId, target.LogId);

            return new AttackResolution(Missed: true, Crit: false, Blocked: false, 0.0, 0.0);
        }

        // ── 2 · Base damage — the multiplier arrives unmultiplied by ATK; multiplying again at the
        // op layer would square the attacker's power. DMG% is a multiplier stat consumed bare
        // (base 1.0 is its identity), never inside a (1 + x) term.
        var raw = StatRounding.Round(
            source.Stats[StatId.ATK] * attackMultiplier * source.Stats[StatId.DMG_PCT]);

        // ── 3 · Mitigation ──────────────────────────────────────────────────────────────────
        var dmg = StatRounding.Round(raw * (1.0 - Mitigation(source, target)));

        // ── 4 · Crit — the second draw, taken before the FORCE_CRIT_NEXT charge is consulted;
        // see the class remarks for why the charge may not skip it.
        var rolled = _services.Rng.NextDouble() < source.Stats[StatId.CRIT];
        var forced = source.Flow.ConsumeForcedCrit();
        var isCrit = rolled || forced;

        if (isCrit)
        {
            dmg = StatRounding.Round(dmg * (1.0 + source.Stats[StatId.CDMG]));
        }

        // ── 5 · Block — the third draw, always taken.
        var blocked = _services.Rng.NextDouble() < target.Stats[StatId.BLOCK];
        if (blocked)
        {
            dmg = StatRounding.Round(dmg * BlockMultiplier);
        }

        // ── 6 · Incoming-damage modifiers ────────────────────────────────────────────────────
        dmg = IncomingDamage(dmg, target);

        // ── 7 · Floor, before ward absorption — a fully absorbed hit deals 0 HP damage; the
        // floor exists to defeat mitigation stacking, not shields.
        dmg = Math.Max(dmg, StatRounding.Round(raw * DamageFloorFraction));

        // ── 8 · On-damage basis — lifesteal and thorns read this: the post-mitigation,
        // post-floor hit before absorption, so a lifesteal attacker still heals off a fully
        // warded hit and thorns still reflect it.
        var basis = dmg;

        // The outcome events precede the Hit (CombatEventType's per-attack emission sequence).
        if (isCrit)
        {
            _services.Log.Append(_services.Tick, CombatEventType.Crit, source.LogId, target.LogId);
        }

        if (blocked)
        {
            _services.Log.Append(_services.Tick, CombatEventType.Block, source.LogId, target.LogId);
        }

        // ── 9 · Ward absorption, then HP, then the phase check ───────────────────────────────
        var hpLost = ApplyToHp(target, basis, absorbedByWards: true, source.LogId);

        // ── 10 · On-damage effects — both read `basis`, never `hpLost`.
        if (source.Stats[StatId.LIFESTEAL] > 0.0)
        {
            Heal(source, StatRounding.Round(basis * source.Stats[StatId.LIFESTEAL]), sourceEffectId);
        }

        var thorns = Thorns(target);
        if (thorns > 0.0)
        {
            ReflectDamage(source, target, StatRounding.Round(basis * thorns), sourceEffectId);
        }

        return new AttackResolution(Missed: false, isCrit, blocked, basis, hpLost);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Bypasses everything: no dodge, mitigation, crit, block, DR, <c>DAMAGE_TAKEN_MULT</c>, floor or
    /// wards. HP is reduced directly; the phase check still runs; no lifesteal or thorns.
    /// <para>
    /// Its <c>Hit</c> carries no source, because <see cref="IAttackPipeline"/> hands this op a target
    /// and an amount and no attacker. Naming the target as its own attacker would be worse than naming
    /// none, so a true-damage tick currently replays as damage from nowhere — recorded rather than
    /// guessed at.
    /// </para>
    /// </remarks>
    public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "true damage");

        ApplyToHp(actor, StatRounding.Round(Math.Max(0.0, amount)), absorbedByWards: false, CombatActor.None);
    }

    /// <inheritdoc />
    /// <remarks>
    /// No dodge, crit, block, mitigation or floor; <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> do apply;
    /// wards absorb; no lifesteal or thorns. <paramref name="bypassesWards"/> is decided once by the
    /// caller and never re-derived here.
    /// </remarks>
    public void DealMaxHpPctDamage(
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "max-HP-percent damage");

        var dmg = IncomingDamage(StatRounding.Round(Math.Max(0.0, amount)), actor);

        ApplyToHp(actor, dmg, absorbedByWards: !bypassesWards, CombatActor.None);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>healed = min(amount × target.HEALPct, target.MaxHP − target.HP)</c>,
    /// <c>overheal = amount × target.HEALPct − healed</c>, then <c>Heal</c> is logged and
    /// <c>ON_HEAL</c> fires with both readings in hand.
    /// <para>
    /// The scaled amount is clamped at 0: <c>HEAL%</c> is the recipient's stat and stacked healing
    /// debuffs can push it negative, which would otherwise turn a heal into damage no <c>Hit</c> event
    /// describes. The clamp is at the scaling, so the overheal a consumer reads is 0 rather than negative.
    /// </para>
    /// </remarks>
    public void Heal(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "healing");

        var scaled = Math.Max(0.0, StatRounding.Round(amount * actor.Stats[StatId.HEAL_PCT]));

        // The heal ceiling (a fraction of Max HP, when authored) is re-read from the live
        // aggregate on every heal rather than cached, since it can move mid-fight. Clamped at 0
        // for a ceiling already below current HP, for the same reason the scaled amount above is.
        var ceiling = actor.Aggregated.HealCeilingFraction is { } fraction
            ? StatRounding.Round(actor.MaxHp * fraction)
            : actor.MaxHp;

        var headroom = Math.Max(0.0, StatRounding.Round(ceiling - actor.CurrentHp));

        var healed = Math.Min(scaled, headroom);
        var overheal = StatRounding.Round(scaled - healed);

        if (healed > 0.0)
        {
            actor.SetCurrentHp(actor.CurrentHp + healed);
        }

        _services.Log.Append(
            _services.Tick, CombatEventType.Heal, CombatActor.None, actor.LogId, healed);

        // Fired after the HP is applied, and even on a pure overheal: an overheal-reading effect's
        // whole input is largest exactly when a heal lands on a full bar.
        _services.AfterHeal(actor, healed, overheal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every grant adds a segment, clipped by the pool cap and by the granting effect's own
    /// per-source cap. <c>Shield</c> is emitted on every grant, carrying what was actually added — a
    /// grant clipped to nothing still emits, so a replayer sees the shield flash for a cast that
    /// visibly happened even if it added no ward.
    /// <para>
    /// The segment has no <c>expiresAt</c> because this interface carries no duration — a
    /// caller-side constraint this method is not free to change. Timed grants reach the pool through
    /// <see cref="BattleServices.GrantWard"/> instead.
    /// </para>
    /// </remarks>
    public void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "ward");

        // Through the battle's one grant routine, so the pool cap, the per-source cap and the
        // Shield event are decided in one place no matter which door the grant came through.
        _services.GrantWard(actor, amount, sourceCapPct, sourceEffectId, expiresAtTick: null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>REFLECT</c> adds to <c>THORN</c> for its duration. The duration is not tracked here — duration
    /// bookkeeping lives elsewhere and is not wired to this class, so the addition is permanent for the
    /// fight; a known gap, recorded rather than duplicated.
    /// </remarks>
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(fraction, sourceEffectId, "thorns");

        actor.Flow.AddThorns(fraction, sourceEffectId);
    }

    /// <summary>
    /// A non-attack damage event: no dodge, crit, block or floor; reduced by the receiver's <c>DR%</c>
    /// and <c>DAMAGE_TAKEN_MULT</c>; absorbed by the receiver's wards; triggers no lifesteal and — the
    /// anti-loop rule — never triggers the receiver's thorns in turn.
    /// </summary>
    /// <remarks>
    /// The anti-loop rule is structural, not a flag: this method never calls itself and nothing else
    /// calls it except the attack pipeline's step 10, so a reflect cannot reach a second reflect. A
    /// <c>bool canReflect</c> parameter would put the rule in the caller's hands instead.
    /// </remarks>
    /// <param name="receiver">The attacker whose hit is being reflected back.</param>
    /// <param name="thorned">The actor whose <c>THORN</c> produced it — the <c>Hit</c>'s source.</param>
    /// <param name="amount">The basis times the defender's <c>THORN</c>.</param>
    /// <param name="sourceEffectId">The id of the attack that provoked it.</param>
    internal void ReflectDamage(
        BattleActor receiver, BattleActor thorned, double amount, string sourceEffectId)
    {
        RequireFinite(amount, sourceEffectId, "reflected damage");

        var dmg = IncomingDamage(StatRounding.Round(Math.Max(0.0, amount)), receiver);

        ApplyToHp(receiver, dmg, absorbedByWards: true, thorned.LogId);
    }

    /// <summary>
    /// Mitigation: <c>effDef / (effDef + flat + perLevel × attacker.Level)</c>, over the two most
    /// important balance dials in the game.
    /// </summary>
    /// <remarks>
    /// The constants arrive from content and are never written here as literals.
    /// </remarks>
    private double Mitigation(BattleActor attacker, BattleActor defender)
    {
        var dials = _services.Mitigation;

        var effDef = StatRounding.Round(
            defender.Stats[StatId.DEF] * (1.0 - attacker.Stats[StatId.PEN]));

        var denominator = StatRounding.Round(
            effDef + dials.Flat + (dials.PerLevel * attacker.Plan.Level));

        if (denominator <= 0.0)
        {
            throw new EffectContextException(
                attacker.Id,
                $"its mitigation denominator against '{defender.Id}' is " +
                $"{denominator.ToString("R", CultureInfo.InvariantCulture)}",
                "`05` §4 step 3 is effDef / (effDef + 120 + 20 * attacker.Level) and both constants " +
                "are 📐 in content/combat_caps.json#/mitigation. A non-positive denominator means the " +
                "dials were authored at or below zero against a zero-DEF defender, which is a 0/0 " +
                "rather than a mitigation — 05 §4's own sanity check has DEF 120 mitigating 0.46 at " +
                "attacker level 1, so the shipped dials cannot reach here.");
        }

        return StatRounding.Round(effDef / denominator);
    }

    /// <summary>
    /// <c>dmg × DR% × Π DamageTakenMult</c>, the product in ascending effect-id order. Shared by
    /// the attack, <c>DAMAGE_MAXHP_PCT</c> and the thorns reflect. <c>DR%</c> is the damage-taken
    /// multiplier consumed bare (base 1.0 is its identity, and it stays a stat — the
    /// <c>DAMAGE_TAKEN_MULT</c> op is a separate factor, not a spelling of it).
    /// </summary>
    private static double IncomingDamage(double damage, BattleActor defender)
    {
        var afterDr = StatRounding.Round(damage * defender.Stats[StatId.DR_PCT]);

        return StatRounding.Round(afterDr * defender.Flow.DamageTakenMultiplier());
    }

    /// <summary>Ward absorption, the HP decrease, the <c>Hit</c>, then the phase check.</summary>
    /// <remarks>
    /// <para>
    /// The four are one method because their order is the rule: <c>WardBroken</c> precedes
    /// <c>Hit</c>, and <c>PhaseChange</c> follows it once absorption and the floor are settled. Four
    /// call sites each remembering that order is four chances to get it wrong.
    /// </para>
    /// <para>
    /// <c>Hit</c> is emitted even when the whole hit was absorbed — a fully absorbed hit deals 0 HP
    /// damage, and a swing with no event at all would draw as the attacker missing.
    /// </para>
    /// </remarks>
    /// <returns>What actually came off HP, after absorption.</returns>
    private double ApplyToHp(BattleActor target, double damage, bool absorbedByWards, byte sourceLogId)
    {
        var hpLost = damage;

        if (absorbedByWards)
        {
            hpLost = target.Wards.Absorb(damage, out var brokenByDamage);

            if (brokenByDamage)
            {
                _services.Log.Append(
                    _services.Tick, CombatEventType.WardBroken, CombatActor.None, target.LogId);
            }
        }

        // A death save (SURVIVE_LETHAL) is consumed HERE, before the HP write: an actor must go out
        // of play the moment it reaches 0, so a save checked after the write would have already let
        // the actor's death fire and be skipped by target selection for one tick.
        //
        // hpLost is reduced to what actually came off, so the Hit event — and any lifesteal reading
        // it — stays truthful even when a save intervened.
        if (hpLost > 0.0 && target.CurrentHp - hpLost <= 0.0)
        {
            // ON_LETHAL fires HERE and nowhere else — between ward absorption (above) and the HP
            // write (below) — because a SURVIVE_LETHAL/REVIVE armed from inside this firing is what
            // the consume immediately below looks for. One call, guarded by the same `if` as the
            // consume: this is the pipeline's one choke point for the attack, DAMAGE_MAXHP_PCT and
            // the thorns reflect, so a lethal hit from any of the three fires it exactly once.
            _services.FireLethal(target);

            if (target.Flow.ConsumeDeathSave(revive: false) is { } save)
            {
                hpLost = Math.Max(0.0, StatRounding.Round(target.CurrentHp - save.Hp));
            }
        }

        if (hpLost > 0.0)
        {
            target.SetCurrentHp(target.CurrentHp - hpLost);
        }

        _services.Log.Append(_services.Tick, CombatEventType.Hit, sourceLogId, target.LogId, hpLost);

        _services.AfterHpDecrease(target);

        return hpLost;
    }

    /// <summary>One actor's <c>THORN</c> — the aggregated stat plus every live <c>REFLECT</c> addition.</summary>
    private static double Thorns(BattleActor actor) =>
        StatRounding.Round(actor.Stats[StatId.THORNS] + actor.Flow.ThornsBonus());

    /// <summary>Forwards the finite-value refusal to <see cref="OpRounding.RequireFinite"/>.</summary>
    /// <remarks>
    /// This pipeline is reached directly by status ticks and boss scripts, without passing through the
    /// op layer, so nothing on that path has already rounded the number — the guard is still needed
    /// here.
    /// </remarks>
    /// <param name="value">The pipeline number.</param>
    /// <param name="sourceEffectId">The effect that produced it — named in the failure message.</param>
    /// <param name="what">What the number is, in the reader's terms.</param>
    /// <exception cref="EffectContextException"><paramref name="value"/> is NaN or infinite.</exception>
    private static void RequireFinite(double value, string sourceEffectId, string what) =>
        OpRounding.RequireFinite(value, sourceEffectId, what);

    /// <summary>The roster's own actor behind an effect view — forwarded to <see cref="BattleActor.Of"/>.</summary>
    private static BattleActor Actor(IEffectActorView view) => BattleActor.Of(view);
}
