using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `05` §1 — the stat ceilings, and `18` §8 step 9's <em>"apply caps, honouring
/// <c>STAT_CAP_OVERRIDE</c>"</em>.
/// </summary>
/// <remarks>
/// <para>
/// `05` §1 caps six of the fourteen combat stats — CRIT 0.75, LS 0.40, DODGE 0.50, BLOCK 0.60,
/// PEN 0.70, DR% 0.60 — and `05` §1.1 rules that <em>"every cap above lives in
/// <c>res://data/combat_caps.json</c>"</em>. Nothing here knows any of those six numbers; they are
/// read by <see cref="CombatCaps"/> from <c>content/combat_caps.json</c>, which is what makes a
/// re-cap a data edit rather than a build.
/// </para>
/// <para>
/// 🔒 <b>Absence means uncapped, and it is the only meaning `05` authorises.</b> The eight
/// uncapped stats — MaxHP, ATK, DEF, ASPD, CDMG, DMG%, HEAL%, THORN — are uncapped because `05` §1's
/// Notes column caps them nowhere.
/// </para>
/// <para>
/// ⚠️ <b>Upper bound only. There is deliberately no floor.</b> `05` §1 types six stats as
/// <c>0..1</c> and `18` §8 step 9 says "apply caps" — one direction. Clamping at zero as well would
/// be a rule the design has not authorised, and `16` R6 / <c>game-data/README.md</c> are explicit
/// that a plausible invention is worse than a visible hole. The consequence is real and is recorded
/// as errata for the milestone: a debuff stack that drives DR% below zero would <em>amplify</em>
/// incoming damage at `05` §4 step 6, and nothing in `05` says whether that is intended. No authored
/// content can reach it today (`05` §5's debuffs touch ASPD, ATK, DEF and healing, none of which is
/// capped), so the honest answer is to leave the hole visible rather than to fill it.
/// </para>
/// </remarks>
internal sealed class StatCaps
{
    private readonly IReadOnlyDictionary<StatId, double> _maxima;

    private StatCaps(IReadOnlyDictionary<StatId, double> maxima) => _maxima = maxima;

    /// <summary>No cap on any stat — the state before `05` §1's six are read from data.</summary>
    /// <remarks>
    /// Exists so a test or the balance harness can aggregate without a content snapshot, and so the
    /// "no cap bound anything" case is expressible rather than being spelled as an empty dictionary
    /// literal at four call sites.
    /// </remarks>
    internal static StatCaps None { get; } = new(new Dictionary<StatId, double>());

    /// <summary>The stats that carry a ceiling, in `05` §1's table order.</summary>
    internal IReadOnlyList<StatId> Capped =>
        StatIds.Combat.Where(_maxima.ContainsKey).ToArray();

    /// <summary>Builds a cap table.</summary>
    /// <param name="maxima">Ceilings by stat. A stat absent from the map is uncapped.</param>
    /// <exception cref="ArgumentException">
    /// A key is not one of `05` §1's fourteen combat stats, or a ceiling is not a finite number
    /// rounded to four decimal places (`05` §1.1).
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

    /// <summary>The ceiling on a stat, or <c>null</c> when `05` §1 caps it nowhere.</summary>
    internal double? Maximum(StatId stat) => _maxima.TryGetValue(stat, out var maximum) ? maximum : null;

    /// <summary>
    /// The same table with one stat's ceiling replaced — `18` §2.1's <c>STAT_CAP_OVERRIDE</c>,
    /// <em>"raise or redirect a stat cap"</em>.
    /// </summary>
    /// <remarks>
    /// The <em>mechanism</em> only. Which effects override which caps, and by how much, is
    /// <see cref="IStatOpBehaviour.OverrideCaps"/>'s — see the remarks there for why neither of `18`'s
    /// two <c>STAT_CAP_OVERRIDE</c> semantics is implementable from what the documents authorise
    /// today.
    /// </remarks>
    internal StatCaps With(StatId stat, double maximum)
    {
        var table = new Dictionary<StatId, double>(_maxima) { [stat] = maximum };

        return From(table);
    }

    /// <summary>`18` §8 step 9 — the value, bounded above by its ceiling.</summary>
    internal double Apply(StatId stat, double value) =>
        _maxima.TryGetValue(stat, out var maximum) && value > maximum ? maximum : value;
}
