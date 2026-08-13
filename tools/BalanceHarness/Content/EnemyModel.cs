using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.BalanceHarness.Content;

/// <summary>
/// 🔒 <c>content/enemies/enemies.json</c> — `05` §6.0's <c>EnemyLevel(c, t)</c> and `05` §6's
/// coefficient rows, which are what makes an enemy's DEF derivable rather than guessable.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Guardrail 5 needs a DEF, and this is where a DEF legitimately comes from.</b> `05` §6:
/// <c>DEF = power × defPerPower × defCoef</c>. Every reachable enemy DEF at a <c>(chapter, tier)</c>
/// is therefore that expression over the eight archetype rows and `02` §4.3's node powers — no free
/// variable, and no number the harness had to make up. <c>EnemyDerivation.Derive</c> in
/// <c>SlayIdleRepeat.Core</c> is the same expression; it is <c>internal</c>, so the coefficients are
/// read here and the arithmetic is restated for the closed form only. That restatement is pinned
/// against the shipped file by <c>EnemyModelTests</c>.
/// </para>
/// <para>
/// ⚠️ The file's own <c>enemyLevel</c> block records an erratum: `05` §6.0 says the level table
/// <em>"lives in <c>data/tuning/par_power.json</c>"</em> and it does not, because `21` §3.1 fixes
/// <c>tuning/</c> at sixteen files. The harness reads it where it actually is.
/// </para>
/// </remarks>
public sealed class EnemyModel
{
    /// <summary>`05` §6 — the document.</summary>
    public const string Document = "content/enemies/enemies.json";

    private readonly IReadOnlyDictionary<int, int> _baseLevelByChapter;
    private readonly IReadOnlyDictionary<Tier, int> _tierBonus;

    private EnemyModel(
        IReadOnlyDictionary<int, int> baseLevelByChapter,
        IReadOnlyDictionary<Tier, int> tierBonus,
        double hpPerPower,
        double atkPerPower,
        double defPerPower,
        double baseAspd,
        IReadOnlyList<EnemyArchetypeRow> archetypes,
        double fixedPen,
        double elitePowerMultiplier,
        double armouredDefMultiplier)
    {
        _baseLevelByChapter = baseLevelByChapter;
        _tierBonus = tierBonus;
        HpPerPower = hpPerPower;
        AtkPerPower = atkPerPower;
        DefPerPower = defPerPower;
        BaseAspd = baseAspd;
        Archetypes = archetypes;
        FixedPen = fixedPen;
        ElitePowerMultiplier = elitePowerMultiplier;
        ArmouredDefMultiplier = armouredDefMultiplier;
    }

    /// <summary>🔒 `05` §6.2 — <c>elites.powerMultiplier</c>. An Elite is its archetype at 2.2× power.</summary>
    public double ElitePowerMultiplier { get; }

    /// <summary>
    /// 🔒 `05` §6.2 — the <c>ARMORED</c> Elite Modifier's <c>defMult</c>. The single largest DEF
    /// multiplier in the authored game, and therefore what makes guardrail 5's <em>"any reachable
    /// DEF"</em> maximal rather than merely typical.
    /// </summary>
    public double ArmouredDefMultiplier { get; }

    /// <summary>`05` §6.2 — the Elite Modifier whose parameters carry a DEF multiplier.</summary>
    public const string ArmouredModifierId = "ARMORED";

    /// <summary>`05` §6 — <c>MaxHP = power × hpPerPower × hpCoef</c>. Authored 0.6.</summary>
    public double HpPerPower { get; }

    /// <summary>`05` §6 — <c>ATK = power × atkPerPower × atkCoef</c>. Authored 0.045.</summary>
    public double AtkPerPower { get; }

    /// <summary>🔒 `05` §6 — <c>DEF = power × defPerPower × defCoef</c>. Authored 0.03. Guardrail 5's input.</summary>
    public double DefPerPower { get; }

    /// <summary>`05` §6 — <c>ASPD = baseAspd × aspdCoef</c>. Authored 1.0.</summary>
    public double BaseAspd { get; }

    /// <summary>`05` §6.1's eight archetype rows, in authored order.</summary>
    public IReadOnlyList<EnemyArchetypeRow> Archetypes { get; }

    /// <summary>
    /// 🔒 `05` §6 — <c>derivation.fixedStats.PEN</c>, authored 0.0 for every archetype. Guardrail 5's
    /// enemy-side attacker penetration, read rather than assumed to be zero.
    /// </summary>
    public double FixedPen { get; }

    /// <summary>Reads the document.</summary>
    /// <exception cref="MissingContentException">A pointer is absent.</exception>
    public static EnemyModel Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var levelRows = content.Read($"{Document}#/enemyLevel/baseByChapter").Items;
        var baseLevels = new Dictionary<int, int>(levelRows.Count);
        for (var i = 0; i < levelRows.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            baseLevels[content.ReadInt32($"{Document}#/enemyLevel/baseByChapter/{index}/chapter")] =
                content.ReadInt32($"{Document}#/enemyLevel/baseByChapter/{index}/level");
        }

        var tierBonus = new Dictionary<Tier, int>(Tiers.All.Count);
        foreach (var tier in Tiers.All)
        {
            tierBonus[tier] = content.ReadInt32($"{Document}#/enemyLevel/tierBonus/{tier}");
        }

        var archetypeRows = content.Read($"{Document}#/archetypes").Items;
        var archetypes = new List<EnemyArchetypeRow>(archetypeRows.Count);
        for (var i = 0; i < archetypeRows.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            var row = $"{Document}#/archetypes/{index}";

            archetypes.Add(new EnemyArchetypeRow(
                content.ReadText($"{row}/id"),
                content.ReadDouble($"{row}/hpCoef"),
                content.ReadDouble($"{row}/atkCoef"),
                content.ReadDouble($"{row}/defCoef"),
                content.ReadDouble($"{row}/aspdCoef")));
        }

        var modifiers = content.Read($"{Document}#/elites/modifiers").Items;
        var armouredDefMultiplier = double.NaN;
        for (var i = 0; i < modifiers.Count; i++)
        {
            var index = i.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(
                    content.ReadText($"{Document}#/elites/modifiers/{index}/id"),
                    ArmouredModifierId,
                    StringComparison.Ordinal))
            {
                armouredDefMultiplier =
                    content.ReadDouble($"{Document}#/elites/modifiers/{index}/parameters/defMult");
                break;
            }
        }

        if (double.IsNaN(armouredDefMultiplier))
        {
            throw new KeyNotFoundException(
                $"{Document}#/elites/modifiers carries no '{ArmouredModifierId}' row, so the highest " +
                "reachable DEF in the game cannot be derived. Guardrail 5 is stated over 'any " +
                "reachable DEF at any chapter'; evaluating it without the largest authored DEF " +
                "multiplier would answer a question nobody asked.");
        }

        return new EnemyModel(
            baseLevels,
            tierBonus,
            content.ReadDouble($"{Document}#/derivation/hpPerPower"),
            content.ReadDouble($"{Document}#/derivation/atkPerPower"),
            content.ReadDouble($"{Document}#/derivation/defPerPower"),
            content.ReadDouble($"{Document}#/derivation/baseAspd"),
            archetypes,
            content.ReadDouble($"{Document}#/derivation/fixedStats/PEN"),
            content.ReadDouble($"{Document}#/elites/powerMultiplier"),
            armouredDefMultiplier);
    }

    /// <summary>
    /// 🔒 `05` §6.0 — <c>EnemyLevel(c, t) = BaseEnemyLevel(c) + TierLevelBonus(t)</c>.
    /// <em>"All enemies, Elites, Guardians and bosses in a <c>(chapter, tier)</c> share this level"</em>,
    /// and `29` §2.5.3 makes it the hero's level too when a loadout targets that content.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The chapter is not in the authored table.</exception>
    public int Level(int chapter, Tier tier) =>
        _baseLevelByChapter.TryGetValue(chapter, out var baseLevel)
            ? baseLevel + _tierBonus[tier]
            : throw new KeyNotFoundException(
                $"{Document}#/enemyLevel/baseByChapter has no chapter " +
                $"{chapter.ToString(CultureInfo.InvariantCulture)} row.");

    /// <summary>🔒 `05` §6 — <c>DEF = power × defPerPower × defCoef</c>, rounded as the engine rounds it.</summary>
    public double Def(double power, double defCoef) =>
        HarnessRounding.Round(power * DefPerPower * defCoef);
}

/// <summary>
/// One row of `05` §6.1's archetype table, reduced to the four power coefficients — which is what a
/// derived stat needs and all guardrail 5 reads.
/// </summary>
/// <param name="Id">The archetype id, e.g. <c>WARDEN</c>.</param>
/// <param name="HpCoef">`05` §6 — the <c>MaxHP</c> coefficient.</param>
/// <param name="AtkCoef">`05` §6 — the <c>ATK</c> coefficient.</param>
/// <param name="DefCoef">🔒 `05` §6 — the <c>DEF</c> coefficient. <c>WARDEN</c>'s 2.2 is the game's highest.</param>
/// <param name="AspdCoef">`05` §6 — the <c>ASPD</c> coefficient.</param>
public sealed record EnemyArchetypeRow(
    string Id,
    double HpCoef,
    double AtkCoef,
    double DefCoef,
    double AspdCoef);
