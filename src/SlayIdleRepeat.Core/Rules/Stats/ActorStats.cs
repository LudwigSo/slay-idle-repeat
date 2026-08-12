using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `05` §1–2 — one actor's complete 14-stat block. <b>An unstated stat is a bug, not a zero.</b>
/// </summary>
/// <remarks>
/// <para>
/// `05` §2, verbatim and 🔒: <em>"every actor — hero and enemy alike — carries a complete 14-stat
/// block with these defaults. An unstated stat is a bug, not a zero."</em> This type is that
/// sentence made mechanical, in four ways that each close a different hole:
/// </para>
/// <list type="number">
/// <item><b>There is no public constructor and no <c>default</c>.</b> It is a sealed class with one
/// private constructor, so there is no zero-initialised value of this type to reach for and no
/// <c>new ActorStats()</c> to write. The only routes in are <see cref="From"/> and
/// <see cref="With"/>, and both go through the same completeness check.</item>
/// <item><b><see cref="From"/> is stated over <see cref="StatIds.Combat"/>, at run time.</b> It is
/// not a fourteen-parameter constructor and not a hard-coded list: it asks the enum what the combat
/// stats are, so a fifteenth combat member of <see cref="StatId"/> makes <em>every</em> existing
/// construction site throw on the commit that adds it, naming the stat nobody supplied. A
/// fixed-arity constructor would keep compiling and keep returning a block with a silent hole.</item>
/// <item><b>An extra key is rejected as loudly as a missing one.</b> A non-combat stat
/// (<c>GOLD_PCT</c>) or an undeclared enum value in the map is a caller confusing the 26-stat DSL
/// vocabulary with the 14-stat actor block, and silently dropping it would lose an effect.</item>
/// <item><b>Every value must already be rounded</b> (<see cref="StatRounding.IsRounded"/>). `05`
/// §1.1 rounds at every accumulation point; a value arriving here unrounded means an accumulation
/// point upstream is missing its <c>Math.Round(x, 4)</c>, and this is the last place that is cheap
/// to notice.</item>
/// </list>
/// <para>
/// ⚠️ <b>Not named <c>*Snapshot</c>, deliberately.</b> `05` §1 calls the simulator's inputs
/// <c>heroSnapshot</c> and <c>enemySnapshot</c>, but nothing in combat is persisted state: the
/// <c>*Snapshot</c> suffix belongs to `14` §16.6's persistence contract and its committed field-order
/// pin, and borrowing it here would drag a battle input into that contract.
/// </para>
/// <para>
/// The block holds the 14 <b>combat</b> stats only. The 12 non-combat stats of `18` §2.1
/// (<c>GOLD_PCT</c>, <c>DROP_CHANCE</c>, …) are run and meta modifiers with no place in a fight, and
/// <c>ALL_COMBAT</c> selects exactly the fourteen here — see <see cref="StatSelector"/>.
/// </para>
/// </remarks>
public sealed class ActorStats : IEquatable<ActorStats>
{
    /// <summary>
    /// Which slot of <see cref="_values"/> each combat stat occupies, derived from
    /// <see cref="StatIds.Combat"/> rather than from the enum's wire numbers.
    /// </summary>
    /// <remarks>
    /// Built from the classification, not from <c>(int)stat - 1</c>: the wire values are append-only
    /// and a renumbering would be caught elsewhere, but an index derived from a number rather than
    /// from membership is a rule that goes quietly wrong, which is the reasoning
    /// <see cref="StatIds.IsCombat"/> already records for itself.
    /// </remarks>
    private static readonly IReadOnlyDictionary<StatId, int> Slots =
        StatIds.Combat.Select((stat, slot) => (stat, slot)).ToDictionary(x => x.stat, x => x.slot);

    private readonly double[] _values;

    private ActorStats(double[] values) => _values = values;

    /// <summary>
    /// 🔒 The stats a block must state — exactly `05` §1's fourteen, read off the enum.
    /// </summary>
    internal static IReadOnlyList<StatId> Required => StatIds.Combat;

    /// <summary>One stat's value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stat"/> is not one of `05` §1's fourteen combat stats.
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
    /// <paramref name="stat"/> is not one of `05` §1's fourteen combat stats.
    /// </exception>
    internal static int SlotOf(StatId stat) =>
        Slots.TryGetValue(stat, out var slot)
            ? slot
            : throw new ArgumentOutOfRangeException(
                nameof(stat), stat,
                $"05 §1's actor block holds the {Required.Count.ToString(CultureInfo.InvariantCulture)} " +
                $"combat stats only. {Describe(stat)} is not one of them.");

    /// <summary>Every stat and its value, in `05` §1's table order.</summary>
    internal IEnumerable<KeyValuePair<StatId, double>> Values =>
        Required.Select(stat => new KeyValuePair<StatId, double>(stat, _values[Slots[stat]]));

    /// <summary>
    /// Builds a block from a complete map of `05` §1's fourteen combat stats.
    /// </summary>
    /// <param name="values">One entry per combat stat. Not more, not fewer.</param>
    /// <exception cref="ArgumentException">
    /// A combat stat is missing, a stat that is not a combat stat is present, or a value is NaN,
    /// infinite, a negative zero, or not rounded to four decimal places (`05` §1.1).
    /// </exception>
    internal static ActorStats From(IReadOnlyDictionary<StatId, double> values)
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
    /// so a pass through `18` §8 does not build and tear down two dictionaries per step boundary.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The array is not exactly <see cref="Required"/> long, or a value is NaN, infinite, a negative
    /// zero, or not rounded to four decimal places (`05` §1.1).
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

    /// <summary>The block as `05` §1's table reads, for a failure message.</summary>
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
