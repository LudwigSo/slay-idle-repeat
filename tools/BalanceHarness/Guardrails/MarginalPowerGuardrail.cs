using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>Guardrail 6 — every stat is top-3 by marginal power in at least one archetype.</summary>
/// <remarks>
/// <para>
/// The verdict here is step-independent, which is the point: the closed-form power model has no term
/// at all for <see cref="StatId.HEAL_PCT"/> or <see cref="StatId.THORNS"/> (<c>EffectiveHP</c> and
/// <c>DPS</c> are built entirely from other stats), so their marginal power is exactly zero under
/// every possible step definition — not small, structurally absent. Neither can be top-3 anywhere and
/// the guardrail fails. <see cref="Evaluate"/> establishes this by varying each over two very
/// different values and asserting <c>PowerIndex</c> doesn't move, rather than by ranking. No per-stat
/// budget is invented to make a ranking come out instead.
/// </para>
/// <para>
/// The step itself is a harness definition with no authored basis: <see cref="RelativeStep"/> (+1% of
/// the archetype's own value) compares equal proportional investments, since an absolute +1 is a
/// rounding error on MAX_HP 1600 but a doubling on CRIT 0.05; <see cref="AbsoluteProbeForZero"/>
/// (+0.01 absolute, labelled in the report) covers a stat held at 0, where 1% of zero is zero.
/// </para>
/// <para>
/// Only the FAIL verdict is step-independent. Which of the other twelve stats appear in the breach
/// list is NOT: an absolute +0.01 on a stat held at 0 is a far larger investment than 1% of a stat
/// held at 1600 (e.g. <see cref="StatId.LIFESTEAL"/> tops three archetypes purely for holding it at 0
/// at a high weight), so that ordering is diagnosis, and the summary says so when it applies.
/// </para>
/// </remarks>
public static class MarginalPowerGuardrail
{
    /// <summary>Harness definition — the step as a fraction of the archetype's own value.</summary>
    public const double RelativeStep = 0.01;

    /// <summary>Harness definition — the absolute step for a stat the archetype holds at 0.</summary>
    public const double AbsoluteProbeForZero = 0.01;

    public const int TopN = 3;

    /// <summary>The two stats the closed-form power model has no term for.</summary>
    public static IReadOnlyList<StatId> StructurallyInvisibleStats { get; } =
        [StatId.HEAL_PCT, StatId.THORNS];

    /// <summary>Evaluates the guardrail over the five authored archetypes.</summary>
    /// <param name="content">The loaded snapshot — <c>PowerIndex</c> reads every weight from it.</param>
    /// <param name="archetypes">The five build archetypes.</param>
    /// <param name="level">
    /// <c>calibration_builds.json#/scalingRule/defaultLevel</c> (authored 40) — the level a loadout
    /// targeting no content is evaluated at, which is what a build archetype does. Read, not chosen.
    /// </param>
    public static GuardrailResult Evaluate(
        ContentSnapshot content, IReadOnlyList<BuildArchetype> archetypes, int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(archetypes);

        var rankings = archetypes.Select(a => Rank(content, a, level)).ToArray();
        var details = new List<string>();

        // Established by variation, not by inspecting the formula: if PowerIndex is unchanged across
        // two very different values of a stat, no step can make its marginal power non-zero.
        foreach (var stat in StructurallyInvisibleStats)
        {
            foreach (var archetype in archetypes)
            {
                var low = PowerCalculator.PowerIndex(
                    archetype.Stats.With(stat, 0.0).ToActorStats(), level, content);
                var high = PowerCalculator.PowerIndex(
                    archetype.Stats.With(stat, 25.0).ToActorStats(), level, content);

                details.Add(
                    $"STRUCTURAL {archetype.Id,-18} {stat,-9} PowerIndex(0)={Num(low)} " +
                    $"PowerIndex(25)={Num(high)} delta={Num(high - low)} — `29` §2.3 has no term for " +
                    "this stat, so its marginal power is exactly 0 under EVERY step definition");
            }
        }

        var topThree = new Dictionary<StatId, List<string>>();
        foreach (var stat in StatIds.Combat)
        {
            topThree[stat] = [];
        }

        foreach (var ranking in rankings)
        {
            details.Add(
                $"RANKING    {ranking.ArchetypeId,-18} " +
                string.Join(
                    " | ",
                    ranking.Entries.Select((e, i) =>
                        $"{Int(i + 1)}.{e.Stat}={Num(e.MarginalPower)}{(e.UsedAbsoluteProbe ? "*" : "")}")));

            foreach (var entry in ranking.Entries.Take(TopN))
            {
                topThree[entry.Stat].Add(ranking.ArchetypeId);
            }
        }

        var missing = StatIds.Combat.Where(s => topThree[s].Count == 0).ToArray();

        foreach (var stat in StatIds.Combat)
        {
            details.Add(
                topThree[stat].Count == 0
                    ? $"BREACH     {stat,-9} top-3 in NO archetype"
                    : $"  ok       {stat,-9} top-3 in {string.Join(", ", topThree[stat])}");
        }

        return new GuardrailResult(
            6,
            "Every stat is top-3 by marginal power in at least one archetype",
            missing.Length == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            missing.Length == 0
                ? $"all {Int(StatIds.Combat.Count)} stats reach the top 3 of some archetype"
                : $"{Int(missing.Length)} of {Int(StatIds.Combat.Count)} stats are top-3 in no " +
                  $"archetype: {string.Join(", ", missing)}. " +
                  (missing.All(StructurallyInvisibleStats.Contains)
                      ? "CAUSE: `29` §2.3's closed form carries no term for them, so their marginal " +
                        "power is exactly 0 under every possible step definition — this verdict does " +
                        "not depend on the harness's step choice."
                      : "At least one of these is NOT one of the two stats the power model cannot " +
                        "see, so the step definition is implicated and the ranking needs reading."),
            details,
            StatIds.Combat.Count * archetypes.Count);
    }

    /// <summary>
    /// The stats of one archetype ordered by marginal power, descending.
    /// </summary>
    public static ArchetypeRanking Rank(ContentSnapshot content, BuildArchetype archetype, int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(archetype);

        var baseline = PowerCalculator.PowerIndex(archetype.Stats.ToActorStats(), level, content);
        var entries = new List<MarginalPowerEntry>(StatIds.Combat.Count);

        foreach (var stat in StatIds.Combat)
        {
            var current = archetype.Stats[stat];
            var usedAbsolute = current == 0.0;
            var step = usedAbsolute ? AbsoluteProbeForZero : current * RelativeStep;

            var bumped = PowerCalculator.PowerIndex(
                archetype.Stats.With(stat, current + step).ToActorStats(), level, content);

            entries.Add(new MarginalPowerEntry(stat, bumped - baseline, step, usedAbsolute));
        }

        // Ties broken by StatId order so the ranking is stable and reproducible rather than
        // dependent on the sort's implementation.
        return new ArchetypeRanking(
            archetype.Id,
            baseline,
            entries.OrderByDescending(e => e.MarginalPower).ThenBy(e => e.Stat).ToArray());
    }

    private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One archetype's stats ordered by marginal power.</summary>
/// <param name="ArchetypeId">Which build.</param>
/// <param name="BaselinePowerIndex">Its unperturbed <c>PowerIndex</c>.</param>
/// <param name="Entries">Descending by marginal power, ties broken by <see cref="StatId"/>.</param>
public sealed record ArchetypeRanking(
    string ArchetypeId, double BaselinePowerIndex, IReadOnlyList<MarginalPowerEntry> Entries);

/// <summary>One stat's marginal power in one archetype.</summary>
/// <param name="Stat">The stat.</param>
/// <param name="MarginalPower">The change in <c>PowerIndex</c> the step produced.</param>
/// <param name="Step">The step actually applied.</param>
/// <param name="UsedAbsoluteProbe">True when the archetype holds the stat at 0 and the labelled absolute probe was used.</param>
public sealed record MarginalPowerEntry(
    StatId Stat, double MarginalPower, double Step, bool UsedAbsoluteProbe);
