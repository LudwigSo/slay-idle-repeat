using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The stat bounds: ceilings applied honouring <c>STAT_CAP_OVERRIDE</c>, plus the one authored
/// floor.
/// </summary>
/// <remarks>
/// Nothing here knows the bounded stats' numbers; they're read by <see cref="CombatCaps"/> from
/// data, which makes a re-bound a data edit rather than a build. Absence means unbounded — the
/// only meaning authorised — in both directions. One floor exists: <c>DR_PCT</c> is the
/// damage-taken multiplier, and the old "at most 60% reduction" ceiling re-expresses as a lower
/// bound of 0.4 on it. No other stat authors a floor and none is invented — a debuff stack that
/// drives another stat below zero stays visible rather than silently clamped.
/// <c>STAT_CAP_OVERRIDE</c>'s <c>STAT_MAX</c> rewrites ceilings only; no effect touches a floor.
/// </remarks>
internal sealed class StatCaps
{
    private readonly IReadOnlyDictionary<StatId, double> _maxima;

    private readonly IReadOnlyDictionary<StatId, double> _minima;

    private StatCaps(
        IReadOnlyDictionary<StatId, double> maxima, IReadOnlyDictionary<StatId, double> minima)
    {
        _maxima = maxima;
        _minima = minima;
    }

    /// <summary>No bound on any stat — the state before any are read from data.</summary>
    /// <remarks>
    /// Exists so a test or the balance harness can aggregate without a content snapshot, and so the
    /// "no bound bit" case is expressible rather than being spelled as an empty dictionary literal
    /// at every call site.
    /// </remarks>
    internal static StatCaps None { get; } = new(
        new Dictionary<StatId, double>(), new Dictionary<StatId, double>());

    /// <summary>The stats that carry a ceiling, in table order.</summary>
    internal IReadOnlyList<StatId> Capped =>
        StatIds.Combat.Where(_maxima.ContainsKey).ToArray();

    /// <summary>Builds a bounds table.</summary>
    /// <param name="maxima">Ceilings by stat. A stat absent from the map is uncapped.</param>
    /// <param name="minima">Floors by stat, or <c>null</c> for none. A stat absent is unfloored.</param>
    /// <exception cref="ArgumentException">
    /// A key is not one of the fourteen combat stats, a bound is not a finite number rounded to
    /// four decimal places, or a stat's floor sits above its ceiling.
    /// </exception>
    internal static StatCaps From(
        IReadOnlyDictionary<StatId, double> maxima,
        IReadOnlyDictionary<StatId, double>? minima = null)
    {
        ArgumentNullException.ThrowIfNull(maxima);

        var ceilings = Bounds(maxima, nameof(maxima), "cap");
        var floors = Bounds(minima ?? new Dictionary<StatId, double>(), nameof(minima), "floor");

        foreach (var (stat, floor) in floors)
        {
            if (ceilings.TryGetValue(stat, out var ceiling) && floor > ceiling)
            {
                throw new ArgumentException(
                    $"The floor on {stat} ({Text(floor)}) sits above its cap ({Text(ceiling)}), " +
                    "which no value can satisfy.",
                    nameof(minima));
            }
        }

        return new StatCaps(ceilings, floors);
    }

    private static Dictionary<StatId, double> Bounds(
        IReadOnlyDictionary<StatId, double> bounds, string parameterName, string kind)
    {
        var table = new Dictionary<StatId, double>(bounds.Count);
        foreach (var (stat, bound) in bounds)
        {
            if (!StatIds.Combat.Contains(stat))
            {
                throw new ArgumentException(
                    $"05 §1 caps combat stats; '{stat}' is not one of the fourteen. The 12 non-combat " +
                    $"stats of 18 §2.1 never reach the actor stat block, so a {kind} on one would bind " +
                    "nothing and read as though it did.",
                    parameterName);
            }

            if (!StatRounding.IsRounded(bound))
            {
                throw new ArgumentException(
                    $"05 §1.1: the {kind} on {stat} is {Text(bound)}, which is not a finite value " +
                    $"rounded to {StatRounding.Decimals} decimal places. A {kind} that is not itself " +
                    "rounded puts an unrounded value into every stat it binds.",
                    parameterName);
            }

            table[stat] = bound;
        }

        return table;
    }

    /// <summary>The ceiling on a stat, or <c>null</c> when it's capped nowhere.</summary>
    internal double? Maximum(StatId stat) => _maxima.TryGetValue(stat, out var maximum) ? maximum : null;

    /// <summary>The floor under a stat, or <c>null</c> when it's floored nowhere.</summary>
    internal double? Minimum(StatId stat) => _minima.TryGetValue(stat, out var minimum) ? minimum : null;

    /// <summary>
    /// The same table with one stat's ceiling replaced — <c>STAT_CAP_OVERRIDE</c>'s mechanism.
    /// Floors ride along untouched: the op's <c>STAT_MAX</c> arm rewrites ceilings only.
    /// </summary>
    /// <remarks>
    /// The mechanism only; which effects override which caps, and by how much, belongs to
    /// <see cref="IStatOpBehaviour.OverrideCaps"/>.
    /// </remarks>
    internal StatCaps With(StatId stat, double maximum)
    {
        var table = new Dictionary<StatId, double>(_maxima) { [stat] = maximum };

        return From(table, _minima);
    }

    /// <summary>The value, bounded above by its ceiling and below by its floor.</summary>
    internal double Apply(StatId stat, double value)
    {
        if (_maxima.TryGetValue(stat, out var maximum) && value > maximum)
        {
            return maximum;
        }

        return _minima.TryGetValue(stat, out var minimum) && value < minimum ? minimum : value;
    }

    private static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
