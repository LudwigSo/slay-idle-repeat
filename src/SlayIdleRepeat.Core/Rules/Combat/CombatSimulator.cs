using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 `05` — <em>"<c>Simulate(seed, heroSnapshot, enemySnapshot)</c> returns an identical result
/// every time, on any device, on the server."</em> The fixed-tick combat engine.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THIS TYPE IS PUBLIC, AND WHAT ELSE HAD TO BECOME PUBLIC WITH IT</b> ═══
/// </para>
/// <para>
/// `30` §11.2 is 🔒 and names this and <c>PowerCalculator</c> as <em>"the only two <c>Rules</c> types
/// that are public"</em>, with a named external consumer each: the client's local battle simulation
/// (`14` §2.4) and the Hero screen's power readout (`29` §1). `05` §7 separately declares
/// <see cref="SimulationResult"/>, <see cref="CombatEvent"/> and <see cref="CombatEventType"/>
/// public, because `05` §8 has the client replay the log and `11` §6 has the backend recompute
/// <c>LogHash</c> over it. M2-15 recorded the two documents as an unresolved contradiction and left
/// the result types internal, because widening <c>Domain.PublicRuleTypes</c> is an edit to a locked
/// section.
/// </para>
/// <para>
/// 🔒 <b>R15 — `30` §11.2 enumerates public <em>entry points</em>, not the closure of the public
/// surface.</b> A public entry point's parameter and return types are public <b>by consequence</b>:
/// a public <see cref="Simulate(ulong, ActorStats, int, IReadOnlyList{ActorStats}, int)"/> returning
/// an internal <c>SimulationResult</c> does not compile (CS0050/CS0051). Nothing §11.2 protects is
/// weakened — its actual claim is that <c>GameRules.Apply</c> is the only public way to change state,
/// and none of these types mutate anything.
/// </para>
/// <para>
/// 🔒 <b>R16 — the widening is enumerated, never a blanket rule.</b> <c>Domain.PublicRuleTypes</c>
/// now lists exactly <c>CombatSimulator</c>, <c>PowerCalculator</c>, <c>SimulationResult</c>,
/// <c>CombatEvent</c>, <c>CombatEventType</c> and <c>ActorStats</c> — the signature closure of the
/// method below and nothing more. A blanket <em>"anything reachable from a public type"</em> rule
/// would let a third public entry point appear without a decision; enumerated, it takes a diff.
/// </para>
/// <para>
/// 🔒 <b>The public overload is `05` §1's signature and nothing wider</b>, which is what keeps that
/// closure at six names. Everything a fight can carry beyond a stat block and a level — authored
/// effects, boss phases, <c>targetPriority</c>, `05` §3.3's duel bounds, the balance harness's seams
/// — goes through the <b>internal</b> <see cref="Simulate(BattlePlan)"/>, because `30` §11.2 exports
/// the simulator, not the DSL.
/// </para>
/// </remarks>
public static class CombatSimulator
{
    /// <summary>
    /// 🔒 `05` §1 — simulates one fight and returns its replay.
    /// </summary>
    /// <param name="battleSeed">
    /// `14` §8.1's <c>battleSeed</c>. The <c>runSeed</c> it is derived from never enters this layer
    /// (`02` §2).
    /// </param>
    /// <param name="hero">The hero's `05` §1 block, before `18` §8.</param>
    /// <param name="heroLevel">The hero's Legend Level — `05` §4's <c>20 * attackerLevel</c> term.</param>
    /// <param name="enemies">
    /// One `05` §1 block per enemy, in `05` §3.1 index order. One to five (`05` §3).
    /// </param>
    /// <param name="enemyLevel">
    /// 🔒 `05` §6.0 — <em>"all enemies, Elites, Guardians and bosses in a <c>(chapter, tier)</c> share
    /// this level"</em>, which is why one value covers the whole side.
    /// </param>
    /// <exception cref="ArgumentException">The roster is empty or breaks a <c>BattlePlan</c> rule.</exception>
    /// <remarks>
    /// A fight with no authored effects and no boss: every actor swings its basic attack on `05`
    /// §3.1's schedule until one side is cleared or the 90 s timeout decides it on remaining HP
    /// fraction. That is what `05` §1's three-argument signature can express, and it is what the
    /// balance harness's standard dummy (`05` §9, `29` §2.5) is.
    /// </remarks>
    public static SimulationResult Simulate(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        IReadOnlyList<ActorStats> enemies,
        int enemyLevel)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemies);

        if (enemies.Count == 0)
        {
            throw new ArgumentException(
                "A fight with no enemies is already won and has no ticks to run. `05` §3's roster is a " +
                "hero, 0-3 pets and 1-5 enemies.",
                nameof(enemies));
        }

        var actors = new List<ActorPlan>(enemies.Count + 1)
        {
            new()
            {
                Id = "HERO",
                Index = 0,
                LogId = CombatActor.Hero,
                Side = BattleSide.HERO,
                Kind = EffectActorKind.HERO,
                BaseStats = hero,
                Level = heroLevel,
            },
        };

        for (var i = 0; i < enemies.Count; i++)
        {
            actors.Add(new ActorPlan
            {
                Id = $"ENEMY_{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                Index = CombatActor.FirstEnemy + i,
                LogId = CombatActor.Enemy(i),
                Side = BattleSide.ENEMY,
                Kind = EffectActorKind.ENEMY,
                BaseStats = enemies[i] ??
                    throw new ArgumentException(
                        $"Enemy {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} has no " +
                        "stat block. `05` §2: 'an unstated stat is a bug, not a zero.'",
                        nameof(enemies)),
                Level = enemyLevel,
            });
        }

        return Simulate(new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = actors,
            Caps = StatCaps.None,
            RunCounters = new RunTriggerCounters(),
        });
    }

    /// <summary>
    /// 🔒 The full entry point — the one a boss fight, a Ghost Duel or the balance harness uses.
    /// </summary>
    /// <param name="plan">The roster, the seed, `05` §3's bounds and the four engine seams.</param>
    /// <remarks>
    /// Internal because `30` §11.2 exports the simulator rather than the DSL: <see cref="BattlePlan"/>
    /// reaches <c>EffectDefinition</c>, <c>StatCaps</c> and the seam interfaces, and making those
    /// public would turn R16's enumerated six-name closure into most of <c>Core</c>.
    /// </remarks>
    internal static SimulationResult Simulate(BattlePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new BattleSimulation(plan).Run();
    }
}
