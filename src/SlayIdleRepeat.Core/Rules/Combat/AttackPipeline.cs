using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` §4, §4.1, §4.2 and §4.3 — the ten-step damage pipeline, the ward pool's damage side,
/// <c>ReflectDamage</c> and <c>Heal()</c>.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE THREE DRAWS, AND THEIR ORDER</b> ═══
/// </para>
/// <para>
/// `05` §4 draws three times, at steps <b>1 (dodge)</b>, <b>4 (crit)</b> and <b>5 (block)</b>, in
/// that order, from the battle's one combat stream
/// (<c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>, `14` §8.1). So one resolved attack
/// advances <c>DeterministicRng.Position</c> by exactly:
/// </para>
/// <code>
///   1  — the swing was dodged (step 1 returns; steps 4 and 5 never run)
///   3  — otherwise
/// </code>
/// <para>
/// 🔴 <b>Steps 4 and 5 draw unconditionally, even when the outcome is already decided.</b>
/// `18` §2.4's <c>FORCE_CRIT_NEXT</c> makes the next N attacks crit; the charge overrides the
/// <em>outcome</em> and never the <em>draw</em>. Skipping the draw would make the stream's position
/// a function of the attacker's flow state, so a client that had not yet observed a charge would
/// desynchronise from the server for the rest of the fight — and <c>Position</c> is persisted stream
/// state. A `05` §4 step that reads <c>Rng.NextDouble()</c> reads it every time it is reached.
/// The same holds at step 5: a defender with <c>BLOCK = 0</c> still costs a draw, because whether it
/// blocks is a property of the number drawn and not of whether drawing was worthwhile.
/// </para>
/// <para>
/// ═══ 🔒 <b>THE EMISSION SEQUENCE</b> ═══
/// </para>
/// <para>
/// <see cref="CombatEventType"/> fixes it, and it is `05` §4's <b>step</b> order rather than its
/// line order: <c>Attack</c> (the loop's), then <c>Miss</c> <em>or</em> <c>Crit</c>, <c>Block</c>,
/// <c>WardBroken</c>, <c>Hit</c>, <c>PhaseChange</c>, <c>Heal</c>, and finally the thorns reflect,
/// which is itself a <c>Hit</c> carrying its own <c>WardBroken</c> and <c>PhaseChange</c>.
/// </para>
/// <para>
/// ═══ 🔒 <b>WHAT THIS CLASS DOES NOT DO</b> ═══
/// </para>
/// <list type="bullet">
///   <item><b>It never fires the on-hit family.</b> <c>BattleSimulation</c> reads the returned
///   <see cref="AttackResolution"/> and fires <c>ON_DODGE</c> / <c>ON_BLOCK</c> / <c>ON_HIT</c> /
///   <c>ON_CRIT</c> / <c>ON_HIT_TAKEN</c> / <c>ON_KILL</c> in its own documented order. The three
///   moments this pipeline <em>does</em> own are the ones `05` §4.3 and §3.1/§4 put <b>inside</b>
///   it: <c>ON_HEAL</c>, fired from <see cref="Heal"/> after the HP is applied; <c>ON_LOW_HP</c>
///   plus the phase check, routed through <see cref="BattleServices.AfterHpDecrease"/> after every
///   HP decrease; and <c>ON_LETHAL</c>, fired from <see cref="ApplyToHp"/> between ward absorption
///   and the HP write, routed through <see cref="BattleServices.FireLethal"/>.</item>
///   <item><b>It never composes <c>AttackMultiplier</c>.</b> `05` §4 makes it a per-attack transient
///   written only by <c>ATTACK_MULT_NEXT</c> charges, per-attack multiplier effects and a DSL
///   <c>DAMAGE</c> op; <c>CombatFlowState.ConsumeAttackMultiplier</c> composes the first and the
///   op layer supplies the third. It arrives here as a number, already unmultiplied by ATK —
///   step 2 is what applies <c>attacker.ATK</c>.</item>
///   <item><b>It never decides whether a hit bypasses wards.</b> `05` §4.1's list (b) is keyed on
///   `18` §7.5's reserved <c>drawback</c> tag, which <c>EffectTagging.IsSelfInflictedCost</c> reads
///   once and <see cref="IAttackPipeline.DealMaxHpPctDamage"/> reports — so the tag is read in one
///   place and never re-derived here.</item>
/// </list>
/// <para>
/// ⚠️ <b>A stateful class under <c>Rules/</c></b> only in the sense that it holds the battle it
/// belongs to — <c>CombatLog</c>'s precedent. One instance per fight, built by
/// <see cref="BattleSeams.For"/>, never static.
/// </para>
/// </remarks>
internal sealed class AttackPipeline : IAttackPipeline
{
    /// <summary>
    /// 🔒 `05` §4 step 5 — <em>"a block halves the hit"</em>. Not 📐: `05` §1's stat table states the
    /// same number a second time (<em>"a block halves the hit"</em>) and `05` §4 writes it as a
    /// literal in the pseudocode, while every genuinely tunable number in this file arrives from
    /// <c>combat_caps.json</c>.
    /// </summary>
    private const double BlockMultiplier = 0.5;

    /// <summary>
    /// 🔒 `05` §4 step 7 — <em>"never less than 10% of raw"</em>. Stated as a named constant rather
    /// than as <c>0.10</c> in the expression so that the one place it could drift is greppable; `05`
    /// §4 does not mark it 📐 and <c>combat_caps.json</c> does not carry it.
    /// </summary>
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
    /// <remarks>
    /// 🔒 `05` §4's ten steps, in order, with `05` §1.1's 4-dp rounding at every accumulation point.
    /// The two orderings that are not obvious from the pseudocode are stated on the class.
    /// </remarks>
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
        var source = Actor(attacker);
        var target = Actor(defender);

        RequireFinite(attackMultiplier, sourceEffectId, "AttackMultiplier");

        // ── 1 · Dodge ────────────────────────────────────────────────────────────────────────
        //
        // 🔒 The FIRST draw of the three, always taken. `05` §4: "if Rng.NextDouble() <
        // defender.DODGE: log(MISS); return".
        if (_services.Rng.NextDouble() < target.Stats[StatId.DODGE])
        {
            _services.Log.Append(
                _services.Tick, CombatEventType.Miss, source.LogId, target.LogId);

            return new AttackResolution(Missed: true, Crit: false, Blocked: false, 0.0, 0.0);
        }

        // ── 2 · Base damage ──────────────────────────────────────────────────────────────────
        //
        // 🔒 The multiplier arrives UNMULTIPLIED by ATK. `05` §4.2 makes a DAMAGE op's value the
        // AttackMultiplier itself, so PK_CLEAVE at `value: 0.40` is a ×0.4 attack and not 0.4 × ATK
        // of flat damage — multiplying by ATK at the op as well would square the attacker's power.
        var raw = StatRounding.Round(
            source.Stats[StatId.ATK] * attackMultiplier * (1.0 + source.Stats[StatId.DMG_PCT]));

        // ── 3 · Mitigation ───────────────────────────────────────────────────────────────────
        var dmg = StatRounding.Round(raw * (1.0 - Mitigation(source, target)));

        // ── 4 · Crit ─────────────────────────────────────────────────────────────────────────
        //
        // 🔒 The SECOND draw, taken before the FORCE_CRIT_NEXT charge is consulted — see the class
        // remarks for why the charge may not skip it.
        var rolled = _services.Rng.NextDouble() < source.Stats[StatId.CRIT];
        var forced = source.Flow.ConsumeForcedCrit();
        var isCrit = rolled || forced;

        if (isCrit)
        {
            dmg = StatRounding.Round(dmg * (1.0 + source.Stats[StatId.CDMG]));
        }

        // ── 5 · Block ────────────────────────────────────────────────────────────────────────
        //
        // 🔒 The THIRD draw, always taken.
        var blocked = _services.Rng.NextDouble() < target.Stats[StatId.BLOCK];
        if (blocked)
        {
            dmg = StatRounding.Round(dmg * BlockMultiplier);
        }

        // ── 6 · Incoming-damage modifiers ────────────────────────────────────────────────────
        dmg = IncomingDamage(dmg, target);

        // ── 7 · Floor, BEFORE ward absorption ────────────────────────────────────────────────
        //
        // 🔒 `05` §4.1: "the §4 step-7 floor applies BEFORE absorption; there is no re-floor after.
        // A fully absorbed hit deals 0 HP damage — the floor exists to defeat mitigation stacking,
        // not shields."
        dmg = Math.Max(dmg, StatRounding.Round(raw * DamageFloorFraction));

        // ── 8 · On-damage basis ──────────────────────────────────────────────────────────────
        //
        // 🔒 Lifesteal and thorns read THIS — the post-mitigation, post-floor hit BEFORE absorption.
        // `05` §4.1: "a lifesteal attacker still heals off a fully-warded hit, and thorns still
        // reflect it."
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

        // ── 10 · On-damage effects ───────────────────────────────────────────────────────────
        //
        // Both read `basis`, never `hpLost`.
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
    /// 🔒 `05` §4.2 — <em>"bypasses everything: no dodge, mitigation, crit, block, DR,
    /// <c>DAMAGE_TAKEN_MULT</c>, floor or wards. HP is reduced directly; the phase check still runs;
    /// no lifesteal or thorns."</em> `05` §4.1's bypass class <b>(a)</b>, and the reason the ward
    /// pool is not consulted here at all rather than consulted with a flag.
    /// <para>
    /// 🔴 <b>Its <c>Hit</c> carries no source, and so does
    /// <see cref="DealMaxHpPctDamage"/>'s — the interface's shape, not a choice.</b>
    /// <see cref="IAttackPipeline"/> hands these two ops a target and an amount and <em>no
    /// attacker</em>, so there is nothing to name; <see cref="ReflectDamage"/> is the contrast, where
    /// the thorns holder is in hand and is named. `05` §8 makes the log the replay, so a boss's
    /// <c>DAMAGE_TRUE</c> tick (Sporequeen's Rot aura, `17` §8) currently replays as damage from
    /// nowhere. Closing it means routing the firing holder — <c>BattleSimulation.ResolveFired</c>
    /// knows it — down to this layer, and belongs with <b>M2-12</b>, the first task with a consumer.
    /// Recorded rather than guessed at: naming the target as its own attacker would be worse than
    /// naming none.
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
    /// 🔒 `05` §4.2 — <em>"no dodge, crit, block, mitigation or floor; <c>DR%</c> and
    /// <c>DAMAGE_TAKEN_MULT</c> <b>do</b> apply; wards absorb; no lifesteal or thorns."</em>
    /// <paramref name="bypassesWards"/> is `05` §4.1's class <b>(b)</b>, already decided by
    /// <c>EffectTagging.IsSelfInflictedCost</c> and never re-derived here.
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
    /// 🔒 `05` §4.3, verbatim: <c>healed = min(amount × target.HEALPct, target.MaxHP − target.HP)</c>,
    /// <c>overheal = amount × target.HEALPct − healed</c>, then <c>log(Heal)</c> and <c>ON_HEAL</c>
    /// with both readings in hand — which is what makes `18` §2.2's <c>HEAL_AMOUNT</c> and
    /// <c>OVERHEAL_AMOUNT</c> value modes readable at all.
    /// <para>
    /// ⚠️ <b>The scaled amount is clamped at 0 and `05` §4.3 does not say so.</b> <c>HEAL%</c> is the
    /// <b>recipient's</b> stat with base 1.0, and `05` §5's <c>SPORE</c> is <em>"−X% healing
    /// received, stacks to 4"</em> — four stacks of −30% is −20%, at which point the formula as
    /// written turns a heal into damage that no <c>Hit</c> event describes and no phase check
    /// observes. Recorded as errata; the clamp is at the scaling, so the overheal a
    /// <c>PK_TRANSFUSION</c> reads is 0 rather than negative.
    /// </para>
    /// </remarks>
    public void Heal(IEffectActorView target, double amount, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "healing");

        var scaled = Math.Max(0.0, StatRounding.Round(amount * actor.Stats[StatId.HEAL_PCT]));

        // 🔴 `18` §7.6's HEAL_CEILING, re-read from the live aggregate on every heal. `05` §4.3's
        //    headroom is `MaxHP − HP`; a ceiling replaces MaxHP as the bar this heal may reach, which
        //    is Avatar of War's "you can no longer be healed above 80% Max HP" (`09` §4). Cross-task
        //    review found the seam that computes it (IStatOpBehaviour.HealCeilingFraction) wired to
        //    nothing, so that build kept its ×1.20 ATK as a pure buff where the design authored a
        //    trade-off. Clamped at 0 for a ceiling already below current HP: `05` §4.3 heals, and a
        //    negative headroom would turn a heal into damage no Hit event describes — the same
        //    erratum, and the same clamp, as the scaled amount above.
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

        // 🔒 AFTER the HP is applied (`05` §4.3), and fired even on a pure overheal: PK_TRANSFUSION's
        // whole input is OVERHEAL_AMOUNT, and a heal into a full bar is exactly when it is largest.
        _services.AfterHeal(actor, healed, overheal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §4.1 — every grant adds a segment, clipped by the pool cap
    /// (📐 <c>wardCapPct</c> × <c>AggregatedStats.PostMultiplierMaxHp</c>) and by the granting
    /// effect's own <c>sourceCapPct</c>. <c>Shield</c> is emitted on <b>every</b> grant, carrying
    /// what was actually added — a grant clipped to nothing still emits, because `05` §4.1 says
    /// "every grant" and a replayer that saw no event would draw no shield flash for a cast that
    /// visibly happened.
    /// <para>
    /// ⚠️ <b>The segment has no <c>expiresAt</c>, and that is the interface's shape rather than a
    /// choice.</b> <see cref="IAttackPipeline.GrantWard"/> carries no duration — M2-03 declared it
    /// that way and M2-10 is coding against the signature, so it is not changed here. `18` §6's
    /// durations reach the pool through <see cref="BattleServices.GrantWard"/>, which takes the
    /// expiry tick; <see cref="WardPool"/> implements the full ordering either way. Recorded as
    /// errata rather than closed by widening a signature three tasks depend on.
    /// </para>
    /// </remarks>
    public void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(amount, sourceEffectId, "ward");

        // 🔒 Through the battle's one grant routine, which is also `18` §6's route
        // (BattleServices.GrantWard) — so the pool cap, the per-source cap and the Shield event are
        // decided in one place no matter which of the two doors the grant came through.
        _services.GrantWard(actor, amount, sourceCapPct, sourceEffectId, expiresAtTick: null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §4.2 / R4 — <c>REFLECT</c> <em>"adds to <c>THORN</c> for its duration"</em>.
    /// <para>
    /// ⚠️ <b>The duration is not held, exactly as <c>DAMAGE_TAKEN_MULT</c>'s is not</b>
    /// (<c>BattleFlowSink.AddDamageTakenMultiplier</c>). `18` §6's duration bookkeeping is M2-06's
    /// evaluator driven by M2-10's slot-2 expiry sweep, and neither is wired to
    /// <c>CombatFlowState</c>; adding a second, disagreeing statement of `18` §6 here is what
    /// steering S6 forbids. The addition is therefore permanent for the fight, which is the same
    /// known gap M2-08 recorded for the sibling op and is stated here so the two are one decision.
    /// </para>
    /// </remarks>
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
        var actor = Actor(target);

        RequireFinite(fraction, sourceEffectId, "thorns");

        actor.Flow.AddThorns(fraction, sourceEffectId);
    }

    /// <summary>
    /// 🔒 `05` §4 — <c>ReflectDamage</c>: <em>"a non-attack damage event: no dodge, crit, block or
    /// floor; reduced by the receiver's <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c>; absorbed by the
    /// receiver's wards; triggers no lifesteal and — anti-loop rule — never triggers the receiver's
    /// thorns in turn."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The anti-loop rule is structural, not a flag.</b> This method never calls itself and
    /// nothing else calls it except `05` §4 step 10, so a reflect cannot reach a second reflect —
    /// which is what `05` §3.1 means by listing it beside the death-save rule. A <c>bool
    /// canReflect</c> parameter would put the rule in the caller's hands and make "reflect that
    /// reflects" one wrong argument away.
    /// </remarks>
    /// <param name="receiver">The attacker whose hit is being reflected back.</param>
    /// <param name="thorned">
    /// 🔒 The actor whose <c>THORN</c> produced it — the <c>Hit</c>'s source. `05` §8 makes the log
    /// the replay, and a reflect logged with no source draws as damage from nowhere; the defender is
    /// in hand at the one call site, so there is nothing to infer.
    /// </param>
    /// <param name="amount">`05` §4 step 10's <c>basis × defender.THORN</c>.</param>
    /// <param name="sourceEffectId">The `18` §8 id of the attack that provoked it.</param>
    internal void ReflectDamage(
        BattleActor receiver, BattleActor thorned, double amount, string sourceEffectId)
    {
        RequireFinite(amount, sourceEffectId, "reflected damage");

        var dmg = IncomingDamage(StatRounding.Round(Math.Max(0.0, amount)), receiver);

        ApplyToHp(receiver, dmg, absorbedByWards: true, thorned.LogId);
    }

    /// <summary>
    /// 🔒 `05` §4 step 3 — <c>effDef / (effDef + flat + perLevel × attacker.Level)</c>, over the two
    /// 📐 dials `05` §4 calls <em>"the two most important balance dials in the game"</em>.
    /// </summary>
    /// <remarks>
    /// The constants arrive from <c>content/combat_caps.json#/mitigation</c> through
    /// <see cref="BattleServices.Mitigation"/> and are never written here — a <c>120</c> in this
    /// expression would be the third copy of a number a content build rule already mirrors against
    /// <c>tuning/power_model.json</c>.
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
    /// 🔒 `05` §4 step 6 — <c>dmg × (1 − DR%) × Π DamageTakenMult</c>, the product in ascending
    /// effect-id order. Shared by the attack, <c>DAMAGE_MAXHP_PCT</c> and the thorns reflect, which
    /// are the three routes `05` §4.2 says it applies to.
    /// </summary>
    private static double IncomingDamage(double damage, BattleActor defender)
    {
        var afterDr = StatRounding.Round(damage * (1.0 - defender.Stats[StatId.DR_PCT]));

        return StatRounding.Round(afterDr * defender.Flow.DamageTakenMultiplier());
    }

    /// <summary>
    /// 🔒 `05` §4 step 9 — ward absorption, the HP decrease, the <c>Hit</c>, then the phase check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four are one method because their <b>order</b> is the rule, and it is stated in two
    /// places that must agree: <see cref="CombatEventType"/>'s emission sequence puts
    /// <c>WardBroken</c> before <c>Hit</c> and <c>PhaseChange</c> after it, and `05` §3.1 requires
    /// the phase check to run <em>"once ward absorption and the floor are settled"</em>. Four call
    /// sites each remembering that order is four chances to get it wrong.
    /// </para>
    /// <para>
    /// 🔒 <b><c>Hit</c> is emitted even when the whole hit was absorbed.</b> `05` §4.1 is explicit
    /// that <em>"a fully absorbed hit deals 0 HP damage"</em>, and `05` §8 makes the log the replay:
    /// a swing that produced no event at all would draw as the attacker missing.
    /// </para>
    /// </remarks>
    /// <returns>`05` §4 step 9's <c>dmg</c> after absorption — what actually came off HP.</returns>
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

        // 🔴 `18` §2.4's SURVIVE_LETHAL, consumed HERE and nowhere else. The op armed a save on
        //    CombatFlowState and cross-task review found ConsumeDeathSave with no production caller
        //    at all, so every "survive a lethal hit" perk in the game was inert and `05` §3.1's
        //    anti-loop `once` count was unreachable code guarding nothing.
        //
        //    🔒 It is checked BEFORE the write rather than after, because `05` §3.1 puts an actor
        //    out of play "at that moment" it reaches 0 — an actor restored on the next slot would
        //    have spent a tick dead, firing ON_DEATH and being skipped by target selection. `18` §3
        //    is explicit that a SURVIVE_LETHAL actor never died, so there is no ON_DEATH and no
        //    ON_REVIVE here; REVIVE is the other op and is consumed in ResolveDeaths.
        //
        //    🔒 hpLost is REDUCED to what actually came off, so the Hit event stays truthful. `05`
        //    §8 makes the log the replay, and a Hit carrying the full lethal amount beside an actor
        //    standing at 1 HP is a frame a replayer cannot draw. It is also what step 10's ON_HIT
        //    readings see, so a lifesteal off the saving blow leeches the real number.
        if (hpLost > 0.0 && target.CurrentHp - hpLost <= 0.0)
        {
            // 🔴 `18` §3's ON_LETHAL, fired HERE and nowhere else — "would take fatal damage",
            //    between ward absorption (above) and the HP write (below). `05` §4 step 9 is
            //    exactly this order. TriggerCatalogue and TriggerRegistry have carried this moment's
            //    description since M2-04 ("inside `05` §4, when the hit would be fatal, before slot
            //    6"), and nothing fired it: `18` §7.4's PK_UNBREAKABLE — the doc's own worked
            //    example — could not be authored in its documented shape, because `once` is admitted
            //    only on ON_LETHAL/ON_LOW_HP and SURVIVE_LETHAL/REVIVE had to be armed on
            //    ON_BATTLE_START instead (see DeathSaveTests before this fix).
            //
            //    🔒 Fired BEFORE ConsumeDeathSave, on purpose: a SURVIVE_LETHAL/REVIVE authored on
            //    ON_LETHAL arms its save from inside this firing, and the consume immediately below
            //    is what looks for it. A save armed any other way (ON_BATTLE_START, say) is
            //    unaffected — it was already sitting on CombatFlowState waiting.
            //
            //    🔒 One call, guarded by the same `if` as the consume — never per damage
            //    sub-component. ApplyToHp is `05` §4's one choke point for the attack,
            //    DAMAGE_MAXHP_PCT and the thorns reflect (see the class remarks), so this is the one
            //    place a lethal hit from any of the three routes fires ON_LETHAL exactly once.
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

    /// <summary>
    /// `05` §1's <c>THORN</c> for one actor — the aggregated stat plus every live <c>REFLECT</c>
    /// addition (`05` §4.2, R4).
    /// </summary>
    private static double Thorns(BattleActor actor) =>
        StatRounding.Round(actor.Stats[StatId.THORNS] + actor.Flow.ThornsBonus());

    /// <summary>
    /// 🔒 `05` §4's pipeline produces real quantities — the refusal, forwarded to
    /// <see cref="OpRounding.RequireFinite"/>, which is where it is stated.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>A forwarder, not a second statement.</b> This method used to carry its own near-verbatim
    /// copy of the refusal — same type, same <c>"R"</c> invariant formatting, same rationale, on the
    /// same values under the same labels. The guard is still needed <em>here</em>: <c>StatusTimeline</c>
    /// and the boss scripts reach this pipeline directly, without passing through
    /// <c>Rules.Effects.Ops</c>, so nothing on that path has already rounded the number. What is gone
    /// is the second wording of the rule.
    /// </remarks>
    /// <param name="value">The pipeline number.</param>
    /// <param name="sourceEffectId">The effect that produced it — named in the failure message.</param>
    /// <param name="what">What the number is, in the reader's terms.</param>
    /// <exception cref="EffectContextException"><paramref name="value"/> is NaN or infinite.</exception>
    private static void RequireFinite(double value, string sourceEffectId, string what) =>
        OpRounding.RequireFinite(value, sourceEffectId, what);

    /// <summary>
    /// The roster's own actor behind an `18` §4/§5 view — forwarded to <see cref="BattleActor.Of"/>,
    /// which is where the cast and its diagnosis are stated.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>BattleFlowSink</c>'s guard and its reason: a battle has one roster and one view of it, so
    /// a foreign implementation means the pipeline is writing HP into a roster the tick loop will
    /// never read. This method used to say so in its own words while <c>TargetSelection</c> said it in
    /// different ones; the sentence now lives in one place.
    /// </remarks>
    private static BattleActor Actor(IEffectActorView view) => BattleActor.Of(view);
}
