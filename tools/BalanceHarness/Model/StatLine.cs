using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.BalanceHarness.Model;

/// <summary>
/// 🔒 The harness's mutable-by-copy view of `05` §1's fourteen combat stats — what an archetype
/// loadout is, what the scaling rule scales, and what guardrail 6 perturbs.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this exists next to <see cref="ActorStats"/> rather than instead of it.</b>
/// <c>ActorStats</c>' indexer and its <c>With</c> are <c>internal</c> to <c>SlayIdleRepeat.Core</c>,
/// so from this assembly an <c>ActorStats</c> is <b>write-only</b>: it can be built and handed to the
/// simulator, and nothing can be read back out of it or changed on it. `29` §2.5.3's scaling rule
/// multiplies three stats by a scalar and guardrail 6 bumps one stat at a time, so the harness needs
/// a readable, copy-on-write stat block. This is that block, and <see cref="ToActorStats"/> is the
/// only place the two meet.
/// </para>
/// <para>
/// 🔒 <b>Every value is rounded on the way in</b>, through <see cref="HarnessRounding.Round"/>.
/// <c>ActorStats.From</c> requires it and refuses a negative zero, and rounding at construction is
/// the only way a scalar produced by bisection — an arbitrary <c>double</c> — cannot reach it
/// unrounded. It also makes <see cref="ToActorStats"/> total: it never throws for a block this type
/// built.
/// </para>
/// <para>
/// ⚠️ The slot order is <see cref="StatIds.Combat"/>'s, read at run time rather than restated, so a
/// change to `05` §1's fourteen is a compile-and-run failure here rather than a silent mismatch.
/// </para>
/// </remarks>
public sealed class StatLine
{
    private readonly double[] _values;

    private StatLine(double[] values) => _values = values;

    /// <summary>`05` §1's fourteen, in <see cref="StatIds.Combat"/> order.</summary>
    public static IReadOnlyList<StatId> Order => StatIds.Combat;

    /// <summary>The value of one combat stat.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stat"/> is not a combat stat.</exception>
    public double this[StatId stat] => _values[SlotOf(stat)];

    /// <summary>Builds a block from a complete map of `05` §1's fourteen stats.</summary>
    /// <param name="values">All fourteen combat stats. A missing or extra one is refused.</param>
    /// <exception cref="ArgumentException">The map is not exactly the fourteen combat stats.</exception>
    public static StatLine From(IReadOnlyDictionary<StatId, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = StatIds.Combat.Where(stat => !values.ContainsKey(stat)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"05 §2: an actor's stat block states all {Count(StatIds.Combat.Count)} combat stats — " +
                $"'an unstated stat is a bug, not a zero'. Missing: {string.Join(", ", missing)}. " +
                "HEAL_PCT is the case that proves it: its authored base is 1.0 and a defaulted 0 " +
                "silently disables every heal the actor could receive.",
                nameof(values));
        }

        var extra = values.Keys.Where(stat => !StatIds.IsCombat(stat)).ToArray();
        if (extra.Length > 0)
        {
            throw new ArgumentException(
                $"05 §1 fixes the actor stat block at {Count(StatIds.Combat.Count)} combat stats, and " +
                $"these are not among them: {string.Join(", ", extra)}.",
                nameof(values));
        }

        var slots = new double[StatIds.Combat.Count];
        for (var slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = HarnessRounding.Round(values[StatIds.Combat[slot]]);
        }

        return new StatLine(slots);
    }

    /// <summary>The same block with one stat replaced — guardrail 6's per-stat probe.</summary>
    public StatLine With(StatId stat, double value)
    {
        var slots = (double[])_values.Clone();
        slots[SlotOf(stat)] = HarnessRounding.Round(value);

        return new StatLine(slots);
    }

    /// <summary>
    /// 🔒 `29` §2.5.3 — <em>"multiply <c>maxHp</c>, <c>atk</c>, <c>def</c> by a single scalar
    /// <c>s</c> (ratio stats unchanged)"</em>.
    /// </summary>
    /// <param name="scalar">The single scalar, already rounded to the authored decimal places.</param>
    /// <param name="scaledStats">
    /// 🔒 <c>tuning/calibration_builds.json#/scalingRule/scaledStats</c>, read rather than restated.
    /// The three stats are authored data, so a design change to which stats scale does not need a
    /// code change here.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scalar"/> is not finite and positive.</exception>
    public StatLine ScaledBy(double scalar, IReadOnlyList<StatId> scaledStats)
    {
        ArgumentNullException.ThrowIfNull(scaledStats);

        if (!double.IsFinite(scalar) || scalar <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scalar),
                scalar,
                "29 §2.5.3's scalar places a loadout at a power target by multiplication, so it is a " +
                "finite positive number. A zero or negative one would produce a stat block the " +
                "simulator cannot fight with, and the bisection that produced it has a bracketing bug.");
        }

        var slots = (double[])_values.Clone();
        foreach (var stat in scaledStats)
        {
            var slot = SlotOf(stat);
            slots[slot] = HarnessRounding.Round(slots[slot] * scalar);
        }

        return new StatLine(slots);
    }

    /// <summary>
    /// 🔒 The one crossing into <c>Core</c>: the block as the simulator's <see cref="ActorStats"/>.
    /// </summary>
    /// <remarks>
    /// Total for any block this type built, because <see cref="HarnessRounding"/> already ran over
    /// every value. It is a fresh dictionary per call by necessity —
    /// <c>ActorStats.From(IReadOnlyDictionary&lt;StatId, double&gt;)</c> is the only public factory —
    /// so callers on the hot path build the <c>ActorStats</c> once per cell, not once per fight.
    /// </remarks>
    public ActorStats ToActorStats()
    {
        var values = new Dictionary<StatId, double>(StatIds.Combat.Count);
        for (var slot = 0; slot < _values.Length; slot++)
        {
            values[StatIds.Combat[slot]] = _values[slot];
        }

        return ActorStats.From(values);
    }

    /// <summary>The fourteen values in <see cref="Order"/>, for reporting.</summary>
    /// <remarks>
    /// 🔒 A wrapper, not the backing array — <c>ContentSnapshot.DocumentPaths</c>' precedent and its
    /// reason: an <c>IReadOnlyList&lt;double&gt;</c> that <em>is</em> a <c>double[]</c> can be cast back
    /// to <c>double[]</c> and written through, and this type is copy-on-write everywhere else
    /// (<see cref="With"/> and <see cref="ScaledBy"/> both clone). A caller mutating a statline in place
    /// would move a hero the sweep had already scaled to par, and no seed would explain the result.
    /// </remarks>
    public IReadOnlyList<double> Values => Array.AsReadOnly(_values);

    /// <inheritdoc />
    public override string ToString() =>
        string.Join(
            " ",
            StatIds.Combat.Select((stat, slot) =>
                $"{stat}={_values[slot].ToString("0.####", CultureInfo.InvariantCulture)}"));

    private static int SlotOf(StatId stat)
    {
        for (var slot = 0; slot < StatIds.Combat.Count; slot++)
        {
            if (StatIds.Combat[slot] == stat)
            {
                return slot;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(stat),
            stat,
            $"05 §1's stat block holds the {Count(StatIds.Combat.Count)} combat stats; " +
            $"{stat} is one of `18` §2.1's run and meta modifiers and has no place in a fight.");
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
