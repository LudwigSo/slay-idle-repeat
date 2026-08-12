// Temporary smoke: does the full engine compose at all? Replaced by the real CLI.
using System.Diagnostics;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;

var content = GameDataLoader.Load();
Console.WriteLine($"documents: {content.DocumentPaths.Count}");

var hero = ActorStats.From(new Dictionary<StatId, double>
{
    [StatId.MAX_HP] = 1120, [StatId.ATK] = 145, [StatId.DEF] = 74, [StatId.ASPD] = 1.05,
    [StatId.CRIT] = 0.08, [StatId.CDMG] = 0.55, [StatId.LIFESTEAL] = 0.02, [StatId.DODGE] = 0.03,
    [StatId.BLOCK] = 0.0, [StatId.PEN] = 0.02, [StatId.DMG_PCT] = 0.05, [StatId.DR_PCT] = 0.03,
    [StatId.HEAL_PCT] = 1.0, [StatId.THORNS] = 0.0,
});

Console.WriteLine($"powerIndex@10 = {PowerCalculator.PowerIndex(hero, 10, content)}");
Console.WriteLine($"effHp = {PowerCalculator.EffectiveHp(hero, content)}  dps = {PowerCalculator.Dps(hero, 10, content)}");

// EnemyPower(42) for Chapter 1 Normal: ParPower 1000 x (1 + 0.035*42) x 2.20
var bossPower = 1000.0 * (1.0 + (0.035 * 42)) * 2.20;
Console.WriteLine($"bossPower(ch1, NORMAL) = {bossPower}");

var stopwatch = Stopwatch.StartNew();
var result = CombatSimulator.SimulateBossFight(
    battleSeed: 0xDEADBEEFUL, hero, heroLevel: 10,
    bossId: "BOSS_THORNMAW", bossPower, enemyLevel: 10, content);
stopwatch.Stop();

Console.WriteLine(
    $"THORNMAW: heroWon={result.HeroWon} ticks={result.DurationTicks} " +
    $"({result.DurationTicks / 20.0:0.00}s) hp={result.HeroHpRemaining} events={result.Log.Count} " +
    $"in {stopwatch.Elapsed.TotalMilliseconds:0.000} ms");

return 0;
