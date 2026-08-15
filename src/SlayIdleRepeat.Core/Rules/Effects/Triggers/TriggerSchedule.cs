using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>The arithmetic behind every timed trigger — a <c>PERIODIC</c>'s firing schedule and the internal cooldowns of <c>ON_HIT_TAKEN</c>, <c>ON_DODGE</c> and <c>ON_BLOCK</c>.</summary>
/// <remarks>
/// <para>
/// A <c>PERIODIC</c>'s clock starts when its owning effect becomes active, not on some separate
/// phase or battle anchor: an effect inside a boss phase block anchors at phase entry, a perk
/// periodic anchors at battle start, and a battle-scope built-in (like an enrage) anchors at battle
/// start too. The anchor is set once, by <see cref="TriggerInstance.Activate"/>, and re-anchoring a
/// live instance is refused there — a boss can enter two phases inside one tick, and re-anchoring on
/// the second entry would push the first firing a full interval into the future for free.
/// </para>
/// <para>
/// An absent <c>startDelay</c> is one interval, not zero: the parameter answers "when does the
/// first firing land", and firing on the anchor tick itself would leave a damaging periodic with
/// nowhere to put its telegraph. So the schedule is <c>anchor + startDelay + k × interval</c>, with
/// <c>startDelay</c> defaulting to <c>interval</c>.
/// </para>
/// <para>
/// Everything is counted in ticks, not seconds: the simulation is fixed-tick, and a schedule
/// accumulated as <c>double</c> seconds would drift, since a tick length isn't exactly representable
/// in binary — two clients could then disagree about which tick a firing landed on. Integer tick
/// arithmetic has no accumulation point at all. That makes whole ticks a real constraint on an
/// authored interval, and <see cref="Ticks"/> enforces it rather than rounding silently.
/// </para>
/// </remarks>
internal static class TriggerSchedule
{
    /// <summary>The tick rate: 0.05 s, 20 ticks per second.</summary>
    /// <remarks>An alias of <see cref="BattleTicks.PerSecond"/>, not a second statement of it.</remarks>
    internal const int TicksPerSecond = BattleTicks.PerSecond;

    /// <summary>A span of battle time in whole ticks.</summary>
    /// <param name="seconds">The span, in seconds.</param>
    /// <param name="parameter">Which trigger parameter this is, for the failure message.</param>
    /// <param name="token">The trigger kind, so a failure pins which rule fired.</param>
    /// <exception cref="EffectContextException">The span is not a whole number of ticks, or does not fit a fight.</exception>
    internal static int Ticks(double seconds, string parameter, string token)
    {
        // Finiteness is checked separately, and first: a NaN and a mis-authored decimal both fail
        // the whole-tick predicate, but want different messages.
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

        // Bounded by the longest fight, checked before the cast: an astronomically large span would
        // otherwise wrap into a negative tick and fire immediately — an overflow that reads exactly
        // like a working trigger.
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

    /// <summary>The longest span a trigger parameter may name — the fight cap, 90 s at 20 Hz.</summary>
    /// <remarks>A span exactly at the cap is admitted rather than refused — a startDelay that lands on the last tick is a trigger that fires once. What's refused is arithmetic that cannot be a span at all.</remarks>
    internal const int MaxSpanTicks = BattleTicks.MaxPerFight;

    /// <summary><see cref="TriggerKind.PERIODIC"/>'s interval, in whole ticks.</summary>
    /// <param name="trigger">A validated <c>PERIODIC</c> trigger.</param>
    /// <exception cref="EffectContextException">The trigger carries no <c>interval</c>, or one that is not a whole number of ticks.</exception>
    internal static int IntervalTicks(EffectTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

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

    /// <summary>The tick a <c>PERIODIC</c> anchored at <paramref name="anchorTick"/> first fires on — <c>anchor + (startDelay ?? interval)</c>.</summary>
    /// <param name="trigger">A validated <c>PERIODIC</c> trigger.</param>
    /// <param name="anchorTick">The tick the owning effect became active on.</param>
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
    /// <remarks>An absent <c>cooldown</c> is no cooldown, not a defaulted one — it's a narrowing parameter, not a constitutive one. See <see cref="TriggerCatalogue"/>.</remarks>
    internal static int CooldownTicks(EffectTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return trigger.Cooldown is { } cooldown
            ? Ticks(cooldown, "cooldown", trigger.Kind.ToString())
            : 0;
    }

    private static string Format(double value) => InvariantText.Text(value);
}
