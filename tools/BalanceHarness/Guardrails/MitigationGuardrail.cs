using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.BalanceHarness.Guardrails;

/// <summary>
/// 🔒 `05` §9 guardrail 5 — <em>"mitigation never exceeds 0.85 for any reachable DEF at any
/// chapter"</em>, evaluated in <b>both</b> directions as a closed form.
/// </summary>
/// <remarks>
/// <para>
/// The two directions are genuinely different assertions and either alone would miss half the game:
/// </para>
/// <list type="number">
///   <item><b>Hero attacking.</b> Defender is every derivable enemy and boss DEF at every
///   <c>(chapter, tier)</c>; attacker is the par-scaled hero, with <b>its own PEN</b> and its own
///   level. A build with 0.10 PEN mitigates less of the same wall than one with none.</item>
///   <item><b>Enemy attacking.</b> Defender is the par hero's DEF; attacker is the enemy, whose PEN
///   is <c>enemies.json#/derivation/fixedStats/PEN</c> (authored 0.0 for every archetype) at
///   <c>EnemyLevel(c, t)</c>.</item>
/// </list>
/// <para>
/// 🔒 <b>Every DEF here is derived from authored coefficients, never guessed.</b> Enemy DEF is `05`
/// §6's <c>power × defPerPower × defCoef</c>; the powers are `02` §4.3's <c>EnemyPower(i)</c> over the
/// four stage multipliers and `03` §1.1's node indices; Elites multiply the power by `05` §6.2's
/// authored 2.2 and the <c>ARMORED</c> modifier multiplies the resulting DEF by its authored 1.8.
/// Boss DEF uses the script's own `17` §1.2 <c>def</c> coefficient at <c>EnemyPower(42)</c>. Hero DEF
/// is the archetype statline placed at par by `29` §2.5.3.
/// </para>
/// <para>
/// 🔒 <b>Closed form rather than sampling, and that is what makes it exhaustive.</b> The curve is
/// monotone in DEF, so enumerating every derivable DEF finds the true maximum. A simulation could
/// only ever report a lower bound, and <em>"never exceeds"</em> is not a claim a lower bound can
/// support.
/// </para>
/// </remarks>
public static class MitigationGuardrail
{
    /// <summary>`03` §1.1 — the three spine stages' node counts, in walk order.</summary>
    public static IReadOnlyList<(int Nodes, double StageMultiplier, string Name)> Stages { get; } =
    [
        (12, NodePower.Stage1Multiplier, "stage1"),
        (14, NodePower.Stage2Multiplier, "stage2"),
        (16, NodePower.Stage3Multiplier, "stage3"),
    ];

    /// <summary>Evaluates the guardrail over the whole authored game.</summary>
    /// <param name="model">The mitigation curve, with `05` §4's authored dials.</param>
    /// <param name="enemies">`05` §6's derivation coefficients and level table.</param>
    /// <param name="bosses">`17` §1.2's per-script coefficients.</param>
    /// <param name="parPower">`29` §4's twenty-four cells.</param>
    /// <param name="parHeroes">
    /// The par-scaled hero of every <c>(chapter, tier, archetype)</c> — both the attacker whose PEN
    /// reduces enemy DEF and the defender whose own DEF the enemies attack.
    /// </param>
    public static GuardrailResult Evaluate(
        MitigationModel model,
        EnemyModel enemies,
        BossRoster bosses,
        ParPowerTable parPower,
        IReadOnlyList<ParHeroDef> parHeroes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(bosses);
        ArgumentNullException.ThrowIfNull(parPower);
        ArgumentNullException.ThrowIfNull(parHeroes);

        var samples = new List<MitigationSample>();

        foreach (var chapter in parPower.Chapters)
        {
            foreach (var tier in Tiers.All)
            {
                var par = parPower.Power(chapter, tier);
                var level = enemies.Level(chapter, tier);
                var heroes = parHeroes
                    .Where(h => h.Chapter == chapter && h.Tier == tier)
                    .ToArray();

                foreach (var def in ReachableEnemyDefs(enemies, bosses, chapter, par))
                {
                    // Direction 1 — the hero swings at that wall, with its own PEN and level.
                    foreach (var hero in heroes)
                    {
                        samples.Add(new MitigationSample(
                            model.Mitigation(def.Def, hero.Pen, level),
                            $"hero {hero.ArchetypeId} (PEN {Num(hero.Pen)}) vs {def.Description}",
                            chapter, tier, def.Def, hero.Pen, level));
                    }
                }

                // Direction 2 — every enemy swings at the par hero, at the authored enemy PEN.
                foreach (var hero in heroes)
                {
                    samples.Add(new MitigationSample(
                        model.Mitigation(hero.Def, enemies.FixedPen, level),
                        $"enemy (PEN {Num(enemies.FixedPen)}) vs par hero {hero.ArchetypeId} " +
                        $"DEF {Num(hero.Def)}",
                        chapter, tier, hero.Def, enemies.FixedPen, level));
                }
            }
        }

        var breaches = samples.Count(s => s.Mitigation > MitigationModel.Ceiling);
        var worst = samples.Count == 0 ? null : samples.MaxBy(s => s.Mitigation);
        var name =
            $"Mitigation never exceeds {Num(MitigationModel.Ceiling)} for any reachable DEF at any chapter";

        if (samples.Count == 0)
        {
            return new GuardrailResult(
                5, name, GuardrailVerdict.Inconclusive,
                "no DEF values were derivable, so nothing was evaluated", [], 0);
        }

        // The report prints the worst sample per (chapter, tier), which is what names WHERE the
        // ceiling is first crossed — the whole point of the guardrail on a game that doubles power
        // every chapter while the mitigation denominator grows only with level.
        var details = samples
            .GroupBy(s => (s.Chapter, s.Tier))
            .OrderBy(g => g.Key.Chapter)
            .ThenBy(g => g.Key.Tier)
            .Select(g => g.MaxBy(s => s.Mitigation)!)
            .Select(s =>
                $"{(s.Mitigation > MitigationModel.Ceiling ? "BREACH" : "  ok  ")} " +
                $"C{Int(s.Chapter)} {s.Tier,-6} max mitigation={Num(s.Mitigation)} " +
                $"(DEF {Num(s.Def)}, attacker PEN {Num(s.AttackerPen)}, attacker level " +
                $"{Int(s.AttackerLevel)}) — {s.Description}")
            .ToList();

        return new GuardrailResult(
            5,
            name,
            breaches == 0 ? GuardrailVerdict.Pass : GuardrailVerdict.Fail,
            $"{Int(breaches)}/{Int(samples.Count)} derivable (DEF, attacker) pairs exceed " +
            $"{Num(MitigationModel.Ceiling)}; maximum reached is {Num(worst!.Mitigation)} at " +
            $"C{Int(worst.Chapter)} {worst.Tier} — {worst.Description}",
            details,
            samples.Count);
    }

    /// <summary>
    /// Every DEF an actor in a chapter can present, from the authored coefficients alone.
    /// </summary>
    public static IReadOnlyList<(double Def, string Description)> ReachableEnemyDefs(
        EnemyModel enemies, BossRoster bosses, int chapter, double parPower)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(bosses);

        var defs = new List<(double, string)>();
        var nodeIndex = 0;

        foreach (var (nodes, stageMultiplier, stageName) in Stages)
        {
            for (var n = 0; n < nodes; n++, nodeIndex++)
            {
                var power = NodePower.NodeEnemyPower(parPower, nodeIndex, stageMultiplier);

                foreach (var row in enemies.Archetypes)
                {
                    var plain = enemies.Def(power, row.DefCoef);
                    defs.Add((plain, $"{row.Id} at {stageName} node {Int(nodeIndex)}"));

                    // `05` §6.2 — an Elite is the same row at 2.2x power, and ARMORED multiplies the
                    // resulting DEF by 1.8. Both numbers are authored; this is the top of the range.
                    var elite = enemies.Def(power * enemies.ElitePowerMultiplier, row.DefCoef);
                    defs.Add((elite, $"Elite {row.Id} at {stageName} node {Int(nodeIndex)}"));
                    defs.Add((
                        HarnessRounding.Round(elite * enemies.ArmouredDefMultiplier),
                        $"ARMORED Elite {row.Id} at {stageName} node {Int(nodeIndex)}"));
                }
            }
        }

        // `03` §1.1 — the boss node is 42, and `17` §1.2 gives the script its own DEF coefficient.
        var bossPower = NodePower.BossPower(parPower);
        var boss = bosses.ForChapter(chapter);
        defs.Add((enemies.Def(bossPower, boss.DefCoef), $"{boss.Id} at boss node {Int(NodePower.BossNodeIndex)}"));

        return defs;
    }

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A par-scaled hero, reduced to the two stats guardrail 5 reads.</summary>
/// <param name="Chapter">The chapter it is at par for.</param>
/// <param name="Tier">The tier.</param>
/// <param name="ArchetypeId">Which build.</param>
/// <param name="Def">Its DEF — what the enemies attack.</param>
/// <param name="Pen">Its PEN — what reduces the enemies' DEF when it attacks.</param>
public sealed record ParHeroDef(int Chapter, Tier Tier, string ArchetypeId, double Def, double Pen)
{
    /// <summary>Reads the two stats off a scaled statline.</summary>
    public static ParHeroDef From(int chapter, Tier tier, string archetypeId, StatLine stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        return new ParHeroDef(chapter, tier, archetypeId, stats[StatId.DEF], stats[StatId.PEN]);
    }
}

/// <summary>One evaluated point of `05` §4's curve.</summary>
internal sealed record MitigationSample(
    double Mitigation,
    string Description,
    int Chapter,
    Tier Tier,
    double Def,
    double AttackerPen,
    int AttackerLevel);
