using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// Shared fixtures for the `05` §1–2 / `18` §8 suite.
/// </summary>
/// <remarks>
/// 🔒 Every stat block these helpers build goes through <see cref="ActorStats.From"/> over
/// <see cref="StatIds.Combat"/> — none of them has a shortcut past the completeness rule. That is
/// deliberate: a test fixture that could build a partial block would be the one place `05` §2's
/// "an unstated stat is a bug, not a zero" did not hold, and it is exactly where a fifteenth stat
/// would first fail to be noticed.
/// </remarks>
internal static class StatFixtures
{
    /// <summary>A complete block with every combat stat at zero.</summary>
    internal static ActorStats Zeroed() =>
        ActorStats.From(StatIds.Combat.ToDictionary(stat => stat, _ => 0.0));

    /// <summary>A complete block with every combat stat at zero except the ones named.</summary>
    /// <remarks>
    /// ⚠️ A <b>synthetic</b> block for arithmetic cases, not an actor. Every unnamed stat is zero,
    /// including <c>HEAL_PCT</c>, whose real `05` §2 default is 1.0 — the point of these cases is
    /// which step touches which number, and a block of zeros makes that legible. Anything asserting
    /// a <em>default</em> uses <see cref="HeroCurve"/>, which is the `05` §2 curve.
    /// </remarks>
    internal static ActorStats Block(params (StatId Stat, double Value)[] values)
    {
        var map = StatIds.Combat.ToDictionary(stat => stat, _ => 0.0);
        foreach (var (stat, value) in values)
        {
            map[stat] = value;
        }

        return ActorStats.From(map);
    }

    /// <summary>One stat-op effect.</summary>
    internal static EffectDefinition Effect(string id, EffectOp op, StatId stat, double value) =>
        new() { Id = id, Op = op, Stat = StatSelector.Of(stat), Value = value };

    /// <summary>One stat-op effect over `18` §9.1's <c>ALL_COMBAT</c> selector.</summary>
    internal static EffectDefinition AllCombatEffect(string id, EffectOp op, double value) =>
        new() { Id = id, Op = op, Stat = StatSelector.AllCombat, Value = value };

    /// <summary>
    /// `05` §2's hero base curve as the shipped <c>content/combat_caps.json</c> authors it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Restated here because <c>Core.Tests</c> has no JSON reader. The copy cannot drift: the shipped
    /// file is asserted against `05` §2 separately by <c>CombatCapsDataTests</c> in the
    /// <c>Application</c> suite. What is tested here is the curve's arithmetic; what is tested there is
    /// the transcription.
    /// </remarks>
    internal static HeroBaseCurve HeroCurve() =>
        HeroBaseCurve.From(
            new Dictionary<StatId, (double Base, double PerLevel)>
            {
                [StatId.MAX_HP] = (250, 45),
                [StatId.ATK] = (30, 6),
                [StatId.DEF] = (15, 3),
                [StatId.ASPD] = (1.00, 0),
                [StatId.CRIT] = (0.05, 0),
                [StatId.CDMG] = (0.50, 0),
                [StatId.LIFESTEAL] = (0.00, 0),
                [StatId.DODGE] = (0.02, 0),
                [StatId.BLOCK] = (0.00, 0),
                [StatId.PEN] = (0.00, 0),
                [StatId.DMG_PCT] = (0.00, 0),
                [StatId.DR_PCT] = (0.00, 0),
                [StatId.HEAL_PCT] = (1.00, 0),
                [StatId.THORNS] = (0.00, 0),
            },
            1,
            200);

    /// <summary>`05` §1's six caps.</summary>
    internal static StatCaps Caps() =>
        StatCaps.From(new Dictionary<StatId, double>
        {
            [StatId.CRIT] = 0.75,
            [StatId.LIFESTEAL] = 0.40,
            [StatId.DODGE] = 0.50,
            [StatId.BLOCK] = 0.60,
            [StatId.PEN] = 0.70,
            [StatId.DR_PCT] = 0.60,
        });

    /// <summary>
    /// 🔒 `05` §4's two 📐 dials as the shipped document authors them — <c>120</c> and <c>20</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ A restatement for <see cref="HeroCurve"/>'s reason and with its safeguard: the shipped document
    /// is asserted against `05` §4 by <c>CombatCapsDataTests</c>, and a content build rule mirrors it
    /// against <c>tuning/power_model.json</c>.
    /// </remarks>
    internal static MitigationConstants Mitigation() => new(Flat: 120, PerLevel: 20);

    /// <summary>🔒 `05` §4.1's 📐 <c>wardCapPct</c>, as the shipped document authors it.</summary>
    /// <remarks>See <see cref="Mitigation"/> for why a restatement here is safe.</remarks>
    internal const double WardCapPct = 1.0;

    /// <summary>
    /// A <see cref="ContentSnapshot"/> holding a <c>content/combat_caps.json</c> of the shipped shape,
    /// with optional mutations.
    /// </summary>
    /// <param name="drop">A pointer segment path to remove, for a negative case.</param>
    /// <param name="capOverrides">`05` §1 ceilings to author differently, by <see cref="StatId"/>.</param>
    /// <param name="mitigation">`05` §4's two dials, if not the shipped <c>(120, 20)</c> pair.</param>
    /// <remarks>
    /// 🔒 Both overrides are how a fight's constants are varied from <em>outside</em> <c>Core.Rules</c>:
    /// they are 📐 data, so a test needing a different game asks for a different document rather than
    /// reaching for the internal <c>StatCaps</c>/<c>MitigationConstants</c> the public entry points
    /// deliberately do not accept.
    /// <para>
    /// ⚠️ The ceilings are what make a draw forceable: a stat of <c>1.0</c> always fires only if the
    /// ceiling lets it survive `18` §8 step 9, and against the shipped 0.50 <c>DODGE</c> cap an "always
    /// dodges" case is unreachable.
    /// </para>
    /// </remarks>
    internal static ContentSnapshot CombatCapsSnapshot(
        string[]? drop = null,
        IReadOnlyDictionary<StatId, decimal>? capOverrides = null,
        (decimal Flat, decimal PerLevel)? mitigation = null)
    {
        var capValues = new Dictionary<StatId, decimal>
        {
            [StatId.CRIT] = 0.75m,
            [StatId.LIFESTEAL] = 0.40m,
            [StatId.DODGE] = 0.50m,
            [StatId.BLOCK] = 0.60m,
            [StatId.PEN] = 0.70m,
            [StatId.DR_PCT] = 0.60m,
        };

        foreach (var (capped, ceiling) in capOverrides ?? new Dictionary<StatId, decimal>())
        {
            capValues[capped] = ceiling;
        }

        var caps = capValues
            .Select(c => new KeyValuePair<string, ContentValue>(
                c.Key.ToString(), ContentValue.Number(c.Value)))
            .ToList();

        var rows = new Dictionary<StatId, (decimal Base, decimal PerLevel)>
        {
            [StatId.MAX_HP] = (250m, 45m),
            [StatId.ATK] = (30m, 6m),
            [StatId.DEF] = (15m, 3m),
            [StatId.ASPD] = (1.00m, 0m),
            [StatId.CRIT] = (0.05m, 0m),
            [StatId.CDMG] = (0.50m, 0m),
            [StatId.LIFESTEAL] = (0.00m, 0m),
            [StatId.DODGE] = (0.02m, 0m),
            [StatId.BLOCK] = (0.00m, 0m),
            [StatId.PEN] = (0.00m, 0m),
            [StatId.DMG_PCT] = (0.00m, 0m),
            [StatId.DR_PCT] = (0.00m, 0m),
            [StatId.HEAL_PCT] = (1.00m, 0m),
            [StatId.THORNS] = (0.00m, 0m),
        };

        var stats = rows.Select(row => new KeyValuePair<string, ContentValue>(
            row.Key.ToString(),
            ContentValue.Object(
            [
                new("base", ContentValue.Number(row.Value.Base)),
                new("perLevel", ContentValue.Number(row.Value.PerLevel)),
            ]))).ToList();

        if (drop is ["caps", var cap])
        {
            caps.RemoveAll(c => string.Equals(c.Key, cap, StringComparison.Ordinal));
        }

        if (drop is ["heroBaseStats", var stat])
        {
            stats.RemoveAll(s => string.Equals(s.Key, stat, StringComparison.Ordinal));
        }

        var root = ContentValue.Object(
        [
            new("caps", ContentValue.Object(caps)),
            new("heroBaseStats", ContentValue.Object(
            [
                new("legendLevelMin", ContentValue.Number(1m)),
                new("legendLevelMax", ContentValue.Number(200m)),
                new("stats", ContentValue.Object(stats)),
            ])),
            new("wardCapPct", drop is ["wardCapPct"] ? ContentValue.Unauthorised : ContentValue.Number(1.0m)),
            new("pvpMaxFightSeconds", ContentValue.Number(60m)),
            new("mitigation", ContentValue.Object(
            [
                new("flatConstant", ContentValue.Number(mitigation?.Flat ?? 120m)),
                new("perLevelConstant", ContentValue.Number(mitigation?.PerLevel ?? 20m)),
            ])),
        ]);

        return new ContentSnapshot(
            ContentVersion.FromHex(new string('7', ContentVersion.HexLength)),
            [new ContentDocument(CombatCapsDocument, root)]);
    }

    /// <summary>The document path `05` §1.1 and `11` §4.3 name, as the repository holds it.</summary>
    internal const string CombatCapsDocument = "content/combat_caps.json";
}
