using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// One actor's complete 14-stat block. An unstated stat is a bug, not a zero.
/// </summary>
/// <remarks>
/// No public constructor and no <c>default</c> — the only routes in are <see cref="From"/> and
/// <see cref="With"/>, and both go through the same completeness check. <see cref="From"/> is
/// stated over <see cref="StatIds.Combat"/> at run time rather than a fixed-arity constructor, so a
/// new combat stat makes every existing construction site throw and name what's missing, instead of
/// compiling with a silent hole. An extra key is rejected as loudly as a missing one — a non-combat
/// stat in the map means a caller confused the wider DSL vocabulary with this 14-stat block. Every
/// value must already be rounded (<see cref="StatRounding.IsRounded"/>), since an unrounded value
/// here means an accumulation point upstream is missing its rounding step. Not named
/// <c>*Snapshot</c>, since nothing in combat is persisted state — that suffix belongs to the
/// persistence contract elsewhere. Holds the 14 combat stats only; the non-combat stats are run and
/// meta modifiers with no place in a fight.
/// </remarks>
public sealed class ActorStats : IEquatable<ActorStats>
{
    /// <summary>
    /// Which slot of <see cref="_values"/> each combat stat occupies, derived from
    /// <see cref="StatIds.Combat"/> rather than from the enum's wire numbers.
    /// </summary>
    /// <remarks>
    /// Built from the classification, not from a numeric offset: an index derived from a number
    /// rather than from membership is a rule that goes quietly wrong if the enum is renumbered.
    /// </remarks>
    private static readonly IReadOnlyDictionary<StatId, int> Slots =
        StatIds.Combat.Select((stat, slot) => (stat, slot)).ToDictionary(x => x.stat, x => x.slot);

    private readonly double[] _values;

    private ActorStats(double[] values) => _values = values;

    /// <summary>The stats a block must state — the fourteen combat stats, read off the enum.</summary>
    internal static IReadOnlyList<StatId> Required => StatIds.Combat;

    /// <summary>One stat's value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stat"/> is not one of the fourteen combat stats.
    /// </exception>
    internal double this[StatId stat] => _values[SlotOf(stat)];

    /// <summary>
    /// Which slot of a fourteen-wide working array a combat stat occupies.
    /// </summary>
    /// <remarks>
    /// Exposed so <see cref="StatAggregation"/> can index its own array with the same derivation
    /// rather than keeping a second copy of it. Two slot maps that must agree, in two files, is one
    /// map too many.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stat"/> is not one of the fourteen combat stats.
    /// </exception>
    internal static int SlotOf(StatId stat) =>
        Slots.TryGetValue(stat, out var slot)
            ? slot
            : throw new ArgumentOutOfRangeException(
                nameof(stat), stat,
                $"05 §1's actor block holds the {Required.Count.ToString(CultureInfo.InvariantCulture)} " +
                $"combat stats only. {Describe(stat)} is not one of them.");

    /// <summary>Every stat and its value, in table order.</summary>
    internal IEnumerable<KeyValuePair<StatId, double>> Values =>
        Required.Select(stat => new KeyValuePair<StatId, double>(stat, _values[Slots[stat]]));

    /// <summary>
    /// Builds a block from a complete map of the fourteen combat stats.
    /// </summary>
    /// <param name="values">One entry per combat stat. Not more, not fewer.</param>
    /// <exception cref="ArgumentException">
    /// A combat stat is missing, a stat that is not a combat stat is present, or a value is NaN,
    /// infinite, a negative zero, or not rounded to four decimal places.
    /// </exception>
    /// <remarks>
    /// Public because it's the only way to build the argument the public combat-simulation entry
    /// point takes, so an external caller (e.g. the balance harness) can actually construct one — the
    /// rest of this type stays internal.
    /// </remarks>
    public static ActorStats From(IReadOnlyDictionary<StatId, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var missing = Required.Where(stat => !values.ContainsKey(stat)).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"05 §2: an actor's stat block states all " +
                $"{Required.Count.ToString(CultureInfo.InvariantCulture)} combat stats — " +
                $"'an unstated stat is a bug, not a zero'. Missing: {string.Join(", ", missing)}. " +
                "Defaulting them to 0 is the specific mistake that ruling forbids; HEAL_PCT alone " +
                "would silently disable every heal in the game, because its documented base is 1.0.",
                nameof(values));
        }

        var extra = values.Keys.Where(stat => !Slots.ContainsKey(stat))
                                .Select(stat => Describe(stat))
                                .ToArray();
        if (extra.Length > 0)
        {
            throw new ArgumentException(
                $"05 §1 fixes the actor stat block at " +
                $"{Required.Count.ToString(CultureInfo.InvariantCulture)} combat stats, and these are not " +
                $"among them: {string.Join(", ", extra)}. 18 §2.1's vocabulary is 26 stats wide — the " +
                "other 12 are run and meta modifiers with no place in a fight. Dropping them quietly " +
                "here would lose whatever effect put them in the map.",
                nameof(values));
        }

        var slots = new double[Required.Count];
        foreach (var stat in Required)
        {
            slots[Slots[stat]] = Checked(stat, values[stat], nameof(values));
        }

        return new ActorStats(slots);
    }

    /// <summary>
    /// Builds a block from a fourteen-wide working array indexed by <see cref="SlotOf"/>.
    /// </summary>
    /// <remarks>
    /// The shape <see cref="StatAggregation"/> works in. It is the same completeness rule stated
    /// over an array rather than a map — a wrong length is a missing (or extra) stat — and it exists
    /// so aggregation does not build and tear down two dictionaries per step boundary.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The array is not exactly <see cref="Required"/> long, or a value is NaN, infinite, a negative
    /// zero, or not rounded to four decimal places.
    /// </exception>
    internal static ActorStats FromSlots(IReadOnlyList<double> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.Count != Required.Count)
        {
            throw new ArgumentException(
                $"05 §2: an actor's stat block is exactly " +
                $"{Required.Count.ToString(CultureInfo.InvariantCulture)} combat stats wide — 'an " +
                "unstated stat is a bug, not a zero' — and this array is " +
                $"{slots.Count.ToString(CultureInfo.InvariantCulture)}.",
                nameof(slots));
        }

        var values = new double[Required.Count];
        for (var slot = 0; slot < values.Length; slot++)
        {
            values[slot] = Checked(Required[slot], slots[slot], nameof(slots));
        }

        return new ActorStats(values);
    }

    /// <summary>The block as a fourteen-wide working array indexed by <see cref="SlotOf"/>.</summary>
    internal double[] ToSlots() => (double[])_values.Clone();

    /// <summary>The same block with one stat replaced.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stat"/> is not a combat stat.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a rounded, finite number.</exception>
    internal ActorStats With(StatId stat, double value)
    {
        var slot = SlotOf(stat);
        var slots = (double[])_values.Clone();
        slots[slot] = Checked(stat, value, nameof(value));

        return new ActorStats(slots);
    }

    /// <inheritdoc />
    public bool Equals(ActorStats? other) =>
        other is not null && (ReferenceEquals(this, other) || _values.AsSpan().SequenceEqual(other._values));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ActorStats);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var value in _values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary>The block rendered for a failure message.</summary>
    public override string ToString() =>
        string.Join(
            ", ",
            Values.Select(v => $"{v.Key}={v.Value.ToString("R", CultureInfo.InvariantCulture)}"));

    private static double Checked(StatId stat, double value, string parameterName)
    {
        if (!StatRounding.IsRounded(value))
        {
            throw new ArgumentException(
                $"05 §1.1: {stat} is {value.ToString("R", CultureInfo.InvariantCulture)}, which is not a " +
                $"finite value rounded to {StatRounding.Decimals} decimal places. Every double in a stat " +
                "block is rounded at the accumulation point that produced it; one arriving unrounded " +
                "means a Math.Round(x, 4) is missing upstream, and a negative zero means the " +
                "`+ 0.0` normalisation is missing from it — CanonicalStateWriter throws rather than " +
                "encoding one, because -0.0 == 0.0 in C# while the bit patterns differ.",
                parameterName);
        }

        return value;
    }

    /// <summary>Renders a stat for a message, without assuming it is a declared member.</summary>
    private static string Describe(StatId stat) =>
        Enum.IsDefined(stat)
            ? stat.ToString()
            : $"({((int)stat).ToString(CultureInfo.InvariantCulture)})";
}
