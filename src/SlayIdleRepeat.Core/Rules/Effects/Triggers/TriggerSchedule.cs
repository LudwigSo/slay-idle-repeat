using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The arithmetic behind every timed trigger — <see cref="TriggerKind.PERIODIC"/>'s firing
/// schedule and the internal cooldowns of <c>ON_HIT_TAKEN</c>, <c>ON_DODGE</c> and <c>ON_BLOCK</c>.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>R8 — A <c>PERIODIC</c>'s CLOCK STARTS WHEN ITS EFFECT BECOMES ACTIVE</b> ═══
/// </para>
/// <para>
/// `17` §1.1 defines the boss mechanic <c>PERIODIC</c> as <em>"fires every N seconds <b>from phase
/// entry</b>"</em>. `18` §3 defines the DSL trigger over battle time, with <c>interval</c> and
/// <c>startDelay</c> and no phase anchor. Eight boss fights depend on the difference and the two
/// documents do not agree. <b>The ruling is one rule and no new parameter: the clock starts when the
/// owning effect becomes active, and <c>startDelay</c> is measured from that anchor.</b>
/// </para>
/// <list type="bullet">
///   <item>An effect inside a boss phase block becomes active at <c>ON_PHASE_ENTER</c>, so it
///   anchors at <b>phase entry</b> — `17` §1.1 exactly.</item>
///   <item>A perk periodic (<c>PK_AEGIS</c>, <c>PK_DEATHMARK</c>) becomes active at battle start, so
///   it anchors <b>there</b> — `18` §3 exactly.</item>
///   <item>🔒 <c>SYS_ENRAGE</c> is a <c>BATTLE</c>-scope built-in present on every boss (`05` §3.1),
///   <b>not</b> phase-scoped, so its <c>startDelay: 70.0</c> is measured from <b>battle start</b> —
///   which is what <em>"a hard enrage at 70 s"</em> means (`17` §1).</item>
/// </list>
/// <para>
/// The anchor is set once, by <see cref="TriggerInstance.Activate"/>, and re-anchoring a live
/// instance is refused there — `05` §3.1's phase check can enter two phases inside one tick, and an
/// instance that re-anchored on the second entry would push its first firing a full interval into
/// the future for free.
/// </para>
///
/// <para>
/// ═══ 🔒 <b>AN ABSENT <c>startDelay</c> IS ONE INTERVAL</b> ═══
/// </para>
/// <para>
/// Twenty of `17`'s boss periodics write an <c>interval</c> and no <c>startDelay</c> — Thornmaw's
/// <c>PERIODIC 8s</c> Root, Ossify's <c>PERIODIC 14s</c>, Scramble's <c>PERIODIC 14s</c>. The
/// question <c>startDelay</c> answers is <em>when the first firing lands</em>, and a periodic that
/// answered it with <c>0</c> would fire on the anchor tick itself. Two clauses rule that out:
/// </para>
/// <list type="number">
///   <item>`17` §1.1 reads <em>"fires every N seconds <b>from</b> phase entry"</em>, not "on phase
///   entry and every N seconds after" — the first firing of "every 8 s from X" is at
///   <c>X + 8</c>.</item>
///   <item>`17` §1 requires that <em>"every damaging mechanic has a visible 1.0–1.5 s wind-up"</em>,
///   and `17` §11 makes the telegraph an implementation deliverable. A damaging periodic firing on
///   the anchor tick has nowhere to put its telegraph: it would have to be emitted before the phase
///   the mechanic belongs to was entered.</item>
/// </list>
/// <para>
/// So the schedule is <c>anchor + startDelay + k × interval</c> for <c>k = 0, 1, 2, …</c>, with
/// <c>startDelay</c> defaulting to <c>interval</c>. <c>SYS_ENRAGE</c> writes
/// <c>{interval: 1.0, startDelay: 70.0}</c> and fires at 70 s, 71 s, 72 s — three firings is
/// <c>1.08³ = 1.259712</c> on ATK (R1: <c>STAT_MULT</c>'s value <b>is</b> the multiplier), which is
/// what `17` §1's <em>"+8% ATK per second, compounding"</em> means.
/// </para>
///
/// <para>
/// ═══ 🔒 <b>EVERYTHING IS COUNTED IN TICKS, NOT SECONDS</b> ═══
/// </para>
/// <para>
/// `05` §3 is a fixed-tick simulation at 20 Hz, and a trigger schedule accumulated as
/// <c>double</c> seconds would drift: <c>0.05</c> is not representable in binary, so 1800 additions
/// of it land somewhere other than 90. Two clients would then disagree about which tick the 47th
/// enrage stack fell on, and `14` §8.2's cross-platform gate would report it as a determinism break
/// — correctly. Integer tick arithmetic has no accumulation point at all, which is why `05` §1.1's
/// 4 dp rounding rule has nothing to round here.
/// </para>
/// <para>
/// 🔒 That makes <b>whole ticks</b> a real constraint on an authored interval, and
/// <see cref="Ticks"/> enforces it rather than rounding silently — the same rule, for the same
/// reason, that <c>CombatLog.AppendTelegraph</c> applies to a telegraph's lead. An interval of
/// <c>0.03 s</c> is 0.6 ticks: it points between two ticks and therefore at neither.
/// </para>
/// </remarks>
internal static class TriggerSchedule
{
    /// <summary>
    /// 🔒 `05` §3 — the tick rate: <c>TICK = 0.05 s</c>, 20 ticks per second.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>An alias for <see cref="BattleTicks.PerSecond"/>, which is the one statement.</b> This
    /// file used to declare the <c>20</c> itself, recording that <c>CombatLog.TicksPerSecond</c> said
    /// the same thing and that R17's layering — <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c> —
    /// left this file, at the bottom, unable to name it. That is exactly the reasoning that made
    /// <c>OpRounding</c> a second statement of <c>StatRounding</c>, and it has the same fix:
    /// <c>Core.Primitives</c> sits beneath every layer that counts ticks.
    /// </remarks>
    internal const int TicksPerSecond = BattleTicks.PerSecond;

    /// <summary>
    /// 🔒 A span of battle time in whole ticks.
    /// </summary>
    /// <param name="seconds">The span, in seconds.</param>
    /// <param name="parameter">Which `18` §3 parameter this is, for the failure message.</param>
    /// <param name="token">The trigger kind, so a failure pins which rule fired (steering S2).</param>
    /// <exception cref="EffectContextException">
    /// The span is not a whole number of ticks, or does not fit a fight.
    /// </exception>
    internal static int Ticks(double seconds, string parameter, string token)
    {
        // 🔒 Finiteness is checked separately, and before. `05` §3's whole-tick predicate answers
        //    "no" to a NaN and to a mis-authored decimal alike, and the two want different sentences:
        //    one names an arithmetic failure upstream, the other tells the author which multiple to
        //    use. BattleTicks.IsWhole owns the predicate; the wording stays here.
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            throw new EffectContextException(
                token,
                $"its {parameter} is {Format(seconds)} s",
                "`05` §3 measures battle time in finite ticks; a NaN or an infinity is an " +
                "arithmetic failure upstream, not a span.");
        }

        if (!BattleTicks.IsWhole(seconds, out var whole))
        {
            throw new EffectContextException(
                token,
                $"its {parameter} is {Format(seconds)} s, which is {Format(seconds * TicksPerSecond)} ticks",
                $"`05` §3's simulation is fixed-tick, so a trigger fires ON a tick — a fractional " +
                $"span points between two ticks and therefore at neither. Use a multiple of " +
                $"{Format(BattleTicks.SecondsPerTick)} s.");
        }

        // 🔒 Bounded by the longest fight `05` §3 admits, and checked BEFORE the cast: a
        // `startDelay` of 1e18 s would otherwise wrap into a negative tick and fire immediately —
        // an overflow that reads exactly like a working trigger.
        if (whole is < 0 or > MaxSpanTicks)
        {
            throw new EffectContextException(
                token,
                $"its {parameter} is {Format(seconds)} s",
                $"`05` §3 caps a fight at 90 s, so a span of more than {MaxSpanTicks} ticks names a " +
                "moment no fight reaches, and a negative one names a moment before the anchor.");
        }

        return (int)whole;
    }

    /// <summary>
    /// 🔒 The longest span a trigger parameter may name — the `05` §3 fight cap, 90 s at 20 Hz.
    /// </summary>
    /// <remarks>
    /// A span exactly at the cap is admitted rather than refused: `05` §3.1's <c>SYS_ENRAGE</c> sits
    /// at 70 s of a 90 s fight, and a <c>startDelay</c> that lands on the last tick is a trigger that
    /// fires once. What is refused is arithmetic that cannot be a span at all.
    /// </remarks>
    internal const int MaxSpanTicks = BattleTicks.MaxPerFight;

    /// <summary>
    /// <see cref="TriggerKind.PERIODIC"/>'s interval, in whole ticks.
    /// </summary>
    /// <param name="trigger">A validated <c>PERIODIC</c> trigger.</param>
    /// <exception cref="EffectContextException">
    /// The trigger carries no <c>interval</c>, or one that is not a whole number of ticks.
    /// </exception>
    internal static int IntervalTicks(EffectTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        // 🔒 The absent case is a THROW, not a default (steering S6): 18 §3's PERIODIC row is
        // "every N seconds of battle time", and with no N there is no row.
        if (trigger.Interval is not { } interval)
        {
            throw new EffectContextException(
                trigger.Kind.ToString(),
                "it carries no interval",
                "`18` §3's PERIODIC fires every interval seconds of battle time. With no interval " +
                "there is no period, and defaulting one would invent a boss mechanic's cadence.");
        }

        var ticks = Ticks(interval, "interval", trigger.Kind.ToString());

        if (ticks < 1)
        {
            throw new EffectContextException(
                trigger.Kind.ToString(),
                $"its interval is {Format(interval)} s, which rounds to {Format((double)ticks)} ticks",
                "A period shorter than one tick fires unboundedly inside a single tick of `05` §3's " +
                "loop.");
        }

        return ticks;
    }

    /// <summary>
    /// 🔒 The tick a <c>PERIODIC</c> anchored at <paramref name="anchorTick"/> first fires on —
    /// <c>anchor + (startDelay ?? interval)</c>. See the type remarks for the ruling.
    /// </summary>
    /// <param name="trigger">A validated <c>PERIODIC</c> trigger.</param>
    /// <param name="anchorTick">The tick the owning effect became active on (R8).</param>
    internal static int FirstFiringTick(EffectTrigger trigger, int anchorTick)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentOutOfRangeException.ThrowIfNegative(anchorTick);

        var intervalTicks = IntervalTicks(trigger);

        var delayTicks = trigger.StartDelay is { } startDelay
            ? Ticks(startDelay, "startDelay", trigger.Kind.ToString())
            : intervalTicks;

        return anchorTick + delayTicks;
    }

    /// <summary>A cooldown in whole ticks — <c>0</c> when the trigger carries none.</summary>
    /// <param name="trigger">A validated trigger of a kind that admits <c>cooldown</c>.</param>
    /// <remarks>
    /// 🔒 An absent <c>cooldown</c> is <b>no cooldown</b>, not a defaulted one: `18` §3 describes
    /// <c>ON_HIT_TAKEN</c> as <em>"each hit received"</em> and <c>cooldown</c> narrows that. See
    /// <see cref="TriggerCatalogue"/> for the line between a narrowing parameter and a constitutive
    /// one.
    /// </remarks>
    internal static int CooldownTicks(EffectTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return trigger.Cooldown is { } cooldown
            ? Ticks(cooldown, "cooldown", trigger.Kind.ToString())
            : 0;
    }

    // 🔒 The convention, not a second statement of it — see Primitives/InvariantText.
    private static string Format(double value) => InvariantText.Text(value);
}
