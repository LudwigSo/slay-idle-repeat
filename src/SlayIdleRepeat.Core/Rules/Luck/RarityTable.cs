using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// A weighted rarity table, and the two operations the luck rules perform on one: flooring it at a
/// rarity and scaling a single row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Flooring is proportional, and it is one rule for every source class.</b> A floor zeroes the
/// weight of every rarity strictly below it and rescales the survivors to sum to one, preserving
/// their relative ratios. Nothing here is per-class: a class-specific renormalisation would make
/// "an A-or-better guarantee" mean a different distribution in a chest than in a crate, which is
/// exactly the kind of quiet divergence a single stated rule prevents.
/// </para>
/// <para>
/// <b>Forcing a guarantee is flooring the table.</b> A forced draw floors at the guaranteed rarity
/// and draws from the renormalised remainder, rather than short-circuiting to a fixed rarity. That
/// keeps a resolution at exactly one draw whether or not pity fired — which is what makes a stream
/// position reproducible — and it is the only reading under which a forced grant can still overshoot
/// its own guarantee and reset a higher counter.
/// </para>
/// <para>
/// A table is immutable; both operations answer a new one. Validation is at construction and again
/// before a draw: a table whose surviving weight is zero is refused <em>before</em> anything is
/// drawn, so a rejected call consumes no draw index.
/// </para>
/// </remarks>
internal sealed class RarityTable
{
    private readonly IReadOnlyList<RarityWeight> _rows;

    private RarityTable(IReadOnlyList<RarityWeight> rows) => _rows = rows;

    /// <summary>Builds a table from weighted rows.</summary>
    /// <param name="rows">
    /// The rows, at most one per rarity. Every weight must be finite and non-negative, and at least
    /// one must be positive.
    /// </param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A rarity appears twice, a weight is negative or not finite, or every weight is zero.
    /// </exception>
    internal static RarityTable Of(IEnumerable<RarityWeight> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var supplied = rows.ToArray();
        var seen = new HashSet<Rarity>();
        var total = 0.0;

        foreach (var row in supplied)
        {
            if (!Enum.IsDefined(row.Rarity))
            {
                throw new ArgumentException(
                    "A row draws a rarity that is not on the ladder. A table is authored in the " +
                    "ladder's own vocabulary, and an undeclared band would sort below every floor.",
                    nameof(rows));
            }

            if (!seen.Add(row.Rarity))
            {
                throw new ArgumentException(
                    $"The rarity {row.Rarity} appears twice. One of the two weights would vanish " +
                    "silently, and which one is an ordering accident.",
                    nameof(rows));
            }

            if (!double.IsFinite(row.Weight) || row.Weight < 0.0)
            {
                throw new ArgumentException(
                    $"The {row.Rarity} row carries weight {Render(row.Weight)}. A weight must be a " +
                    "finite, non-negative number — anything else makes the cumulative walk " +
                    "non-monotonic and its answer arbitrary.",
                    nameof(rows));
            }

            total += row.Weight;
        }

        if (total <= 0.0)
        {
            throw new ArgumentException(
                "No row carries any weight, so nothing can be drawn from this table at all.",
                nameof(rows));
        }

        return new RarityTable(Ascending(supplied));
    }

    /// <summary>The rows, in ascending rarity order.</summary>
    internal IReadOnlyList<RarityWeight> Rows => _rows;

    /// <summary>The sum of every row's weight.</summary>
    internal double TotalWeight => Sum(_rows);

    /// <summary>
    /// Whether anything can be drawn from this table at all — some row still carries a positive
    /// weight.
    /// </summary>
    /// <remarks>
    /// Asked before a draw rather than after, so a table that a floor has emptied is refused without
    /// consuming a draw index.
    /// </remarks>
    internal bool HasPositiveWeight
    {
        get
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Weight > 0.0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// This table with every rarity below <paramref name="floor"/> zeroed and the survivors rescaled
    /// to sum to one, preserving their relative ratios.
    /// </summary>
    /// <param name="floor">The lowest rarity the draw may produce.</param>
    /// <returns>A new table; this one is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="floor"/> is not a declared rarity.</exception>
    /// <exception cref="InvalidOperationException">
    /// No rarity at or above the floor carries any weight, so the floor cannot be satisfied from this
    /// table at all.
    /// </exception>
    internal RarityTable FloorAt(Rarity floor)
    {
        RequireDeclared(floor, nameof(floor));

        var surviving = 0.0;

        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Rarity >= floor)
            {
                surviving += _rows[i].Weight;
            }
        }

        if (surviving <= 0.0)
        {
            throw new InvalidOperationException(
                $"No rarity at or above the floor {floor} carries any weight in this table, so the " +
                "floor cannot be satisfied from it at all. A floored draw is refused before the " +
                "draw is taken, so the stream is left where it stood.");
        }

        var floored = new RarityWeight[_rows.Count];

        for (var i = 0; i < floored.Length; i++)
        {
            floored[i] = _rows[i].Rarity < floor
                ? new RarityWeight(_rows[i].Rarity, 0.0)
                : new RarityWeight(_rows[i].Rarity, _rows[i].Weight / surviving);
        }

        return new RarityTable(floored);
    }

    /// <summary>This table with one rarity's weight multiplied.</summary>
    /// <remarks>
    /// The soft-pity ramp's only effect on a table. A rarity the table does not carry is left alone
    /// rather than added: a curve targeting a rarity a class cannot drop must not conjure it.
    /// </remarks>
    /// <param name="rarity">The rarity whose weight is scaled.</param>
    /// <param name="multiplier">The multiplier. Finite and never negative.</param>
    /// <returns>A new table; this one is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rarity"/> is not a declared rarity, or the multiplier is negative or not
    /// finite.
    /// </exception>
    internal RarityTable Scale(Rarity rarity, double multiplier)
    {
        RequireDeclared(rarity, nameof(rarity));

        if (!double.IsFinite(multiplier) || multiplier < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplier),
                multiplier,
                "A soft-pity multiplier is a finite, non-negative number. Anything else makes the " +
                "cumulative walk non-monotonic and its answer arbitrary.");
        }

        var scaled = new RarityWeight[_rows.Count];

        for (var i = 0; i < scaled.Length; i++)
        {
            scaled[i] = _rows[i].Rarity == rarity
                ? new RarityWeight(rarity, _rows[i].Weight * multiplier)
                : _rows[i];
        }

        return new RarityTable(scaled);
    }

    private static void RequireDeclared(Rarity rarity, string parameter)
    {
        if (!Enum.IsDefined(rarity))
        {
            throw new ArgumentOutOfRangeException(
                parameter, rarity, "That is not a rarity on the ladder.");
        }
    }

    /// <summary>The rows in ascending rarity order — the order the weighted walk consumes them in.</summary>
    private static IReadOnlyList<RarityWeight> Ascending(IEnumerable<RarityWeight> rows) =>
        rows.OrderBy(row => row.Rarity).ToArray();

    /// <summary>Σ weights, in row order — the order the weighted walk accumulates them in.</summary>
    private static double Sum(IReadOnlyList<RarityWeight> rows)
    {
        var total = 0.0;

        for (var i = 0; i < rows.Count; i++)
        {
            total += rows[i].Weight;
        }

        return total;
    }

    private static string Render(double value) => value.ToString(CultureInfo.InvariantCulture);
}
