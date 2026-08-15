using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The stat ceilings, applied honouring <c>STAT_CAP_OVERRIDE</c>.
/// </summary>
/// <remarks>
/// Nothing here knows the six capped stats' numbers; they're read by <see cref="CombatCaps"/> from
/// data, which makes a re-cap a data edit rather than a build. Absence means uncapped — the only
/// meaning authorised — for the eight stats with no ceiling. Upper bound only, deliberately no
/// floor: a debuff stack that drives DR% below zero would amplify incoming damage, and nothing
/// authorises a floor to prevent that, so the hole is left visible rather than filled with an
/// invented clamp. No authored content can reach that case today.
/// </remarks>
internal sealed class StatCaps
{
    private readonly IReadOnlyDictionary<StatId, double> _maxima;

    private StatCaps(IReadOnlyDictionary<StatId, double> maxima) => _maxima = maxima;

    /// <summary>No cap on any stat — the state before any caps are read from data.</summary>
    /// <remarks>
    /// Exists so a test or the balance harness can aggregate without a content snapshot, and so the
    /// "no cap bound anything" case is expressible rather than being spelled as an empty dictionary
    /// literal at every call site.
    /// </remarks>
    internal static StatCaps None { get; } = new(new Dictionary<StatId, double>());

    /// <summary>The stats that carry a ceiling, in table order.</summary>
    internal IReadOnlyList<StatId> Capped =>
        StatIds.Combat.Where(_maxima.ContainsKey).ToArray();

    /// <summary>Builds a cap table.</summary>
    /// <param name="maxima">Ceilings by stat. A stat absent from the map is uncapped.</param>
    /// <exception cref="ArgumentException">
    /// A key is not one of the fourteen combat stats, or a ceiling is not a finite number rounded to
    /// four decimal places.
    /// </exception>
    internal static StatCaps From(IReadOnlyDictionary<StatId, double> maxima)
    {
        ArgumentNullException.ThrowIfNull(maxima);

        var table = new Dictionary<StatId, double>(maxima.Count);
        foreach (var (stat, maximum) in maxima)
        {
            if (!StatIds.Combat.Contains(stat))
            {
                throw new ArgumentException(
                    $"05 §1 caps combat stats; '{stat}' is not one of the fourteen. The 12 non-combat " +
                    "stats of 18 §2.1 never reach the actor stat block, so a cap on one would bind " +
                    "nothing and read as though it did.",
                    nameof(maxima));
            }

            if (!StatRounding.IsRounded(maximum))
            {
                throw new ArgumentException(
                    $"05 §1.1: the cap on {stat} is {maximum.ToString("R", CultureInfo.InvariantCulture)}, " +
                    $"which is not a finite value rounded to {StatRounding.Decimals} decimal places. A cap " +
                    "that is not itself rounded puts an unrounded value into every stat it binds.",
                    nameof(maxima));
            }

            table[stat] = maximum;
        }

        return new StatCaps(table);
    }

    /// <summary>The ceiling on a stat, or <c>null</c> when it's capped nowhere.</summary>
    internal double? Maximum(StatId stat) => _maxima.TryGetValue(stat, out var maximum) ? maximum : null;

    /// <summary>
    /// The same table with one stat's ceiling replaced — <c>STAT_CAP_OVERRIDE</c>'s mechanism.
    /// </summary>
    /// <remarks>
    /// The mechanism only; which effects override which caps, and by how much, belongs to
    /// <see cref="IStatOpBehaviour.OverrideCaps"/>.
    /// </remarks>
    internal StatCaps With(StatId stat, double maximum)
    {
        var table = new Dictionary<StatId, double>(_maxima) { [stat] = maximum };

        return From(table);
    }

    /// <summary>The value, bounded above by its ceiling.</summary>
    internal double Apply(StatId stat, double value) =>
        _maxima.TryGetValue(stat, out var maximum) && value > maximum ? maximum : value;
}
