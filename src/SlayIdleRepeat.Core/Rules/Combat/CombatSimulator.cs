using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Status;
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
    /// <param name="content">
    /// 🔒 The loaded, schema-validated content snapshot. `05` §1's simulator constants are 📐 and live
    /// in <c>content/combat_caps.json</c>; this is how they reach it. See the remarks.
    /// </param>
    /// <exception cref="ArgumentException">The roster is empty or breaks a <c>BattlePlan</c> rule.</exception>
    /// <exception cref="MissingContentException"><c>combat_caps.json</c> is absent or missing a pointer.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant is <c>null</c> in the data.</exception>
    /// <remarks>
    /// <para>
    /// A fight with no authored effects and no boss: every actor swings its basic attack on `05`
    /// §3.1's schedule until one side is cleared or the 90 s timeout decides it on remaining HP
    /// fraction. That is what `05` §1's three-argument signature can express, and it is what the
    /// balance harness's standard dummy (`05` §9, `29` §2.5) is.
    /// </para>
    /// <para>
    /// 🔒 <b>Why the snapshot, rather than the constants themselves.</b> `05` §4 says of the
    /// mitigation pair <em>"the two most important balance dials in the game. Expose them in
    /// data"</em>, `05` §4.1 puts <c>wardCapPct</c> in the same document and `05` §1.1 puts the six
    /// caps there too. This method is <c>static</c>, so it cannot hold the document — but it can be
    /// handed one, and <c>CombatCaps.Read</c> already knows every pointer. The three alternatives are
    /// each worse in a specific way: writing the numbers here would be a third copy of a pair the
    /// content build already mirrors against <c>tuning/power_model.json</c>; defaulting them is
    /// steering S6's forbidden hole (a zeroed mitigation pair mitigates <b>100%</b> of every hit and
    /// a zero <c>wardCapPct</c> deletes every shield in the game, both silently); and taking them as
    /// three bare <see cref="double"/>s puts two adjacent same-typed parameters in a public signature
    /// where transposing 120 and 20 compiles, throws nothing, and yields a plausible game `05` §9's
    /// A10 assertion would grade as content being mistuned. It also grows by one parameter per future
    /// 📐 dial — `11` §4.3's <c>pvpMaxFightSeconds</c> is in this very document and is M2-14's.
    /// </para>
    /// <para>
    /// ⚠️ <b>It widens no public <em>type</em>.</b> <see cref="ContentSnapshot"/> is already public
    /// (`30` §11.2's content boundary) and lives under <c>Content/</c>, which
    /// <c>AccessibilityBoundaryTests.Handlers_and_Rules_are_internal</c> does not govern — so R16's
    /// enumerated six-name closure (<c>Domain.PublicRuleTypes</c>) is untouched. The precedent for
    /// exceeding `05` §1's literal <c>Simulate(seed, heroSnapshot, enemySnapshot)</c> is M2-08's own:
    /// it already carried <c>heroLevel</c> and <c>enemyLevel</c>, added for `05` §4's
    /// <c>20 × attacker.Level</c> term — the very expression these dials complete. Recorded as errata
    /// against `05` §1.
    /// </para>
    /// <para>
    /// 🔴 <b>The caps are the document's too, since M2-09.</b> This overload passed
    /// <c>StatCaps.None</c> while it had no way to read them; it does now, so `18` §8 step 9 applies
    /// `05` §1's six ceilings to a public fight as the document says it should.
    /// </para>
    /// <para>
    /// ═══ 🔒 <b>WHY THIS COMPOSES ITS OWN SEAMS INSTEAD OF TAKING <c>BattleSeams.For</c></b> ═══
    /// </para>
    /// <para>
    /// M2-16a recorded, in <see cref="BossFight"/>'s remarks, that this method <em>"still cannot run
    /// a fight in which any `05` §5 status is applied"</em>: <see cref="BattleSeams.For"/> wires only
    /// `05` §4's attack pipeline, so its <c>Statuses</c> is <c>UnwiredStatusEngine</c> and the first
    /// <c>APPLY_STATUS</c> throws <em>"M2-10 has not landed"</em> — which stopped being true when
    /// M2-10 landed. `30` §11.2 exports this entry point for `14` §2.4's client-side local battle
    /// simulation, and `05` §6.1a gives a CASTER a biome status, so the export was unusable for its
    /// named consumer against ordinary content.
    /// </para>
    /// <para>
    /// 🔒 <b>The wiring is here rather than in <see cref="BattleSeams.For"/>, and that is the whole
    /// of the fix.</b> <c>For</c> takes a <see cref="BattleServices"/> and nothing else — it has no
    /// content snapshot, so it cannot read `05` §5's catalogue, and widening it would have to change
    /// <see cref="BattlePlan"/>'s default for every test bench in the repository. This method already
    /// holds the snapshot for <c>CombatCaps</c>. So the seam set is composed at the entry point that
    /// has the data, exactly as <see cref="BossFight"/> composes its own, and <c>For</c> keeps its
    /// meaning: the default a <see cref="BattlePlan"/> built without content gets.
    /// </para>
    /// <para>
    /// ⚠️ <b>Consequence for the caller:</b> the snapshot must now carry
    /// <c>content/statuses.json</c> as well as <c>content/combat_caps.json</c>. That is what the
    /// parameter has always been documented as — <em>"the loaded, schema-validated content
    /// snapshot"</em> — and a missing document fails loudly at <c>StatusCatalogue.Read</c> rather
    /// than mid-fight.
    /// </para>
    /// </remarks>
    public static SimulationResult Simulate(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        IReadOnlyList<ActorStats> enemies,
        int enemyLevel,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(content);

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

        // 🔒 One read of content/combat_caps.json, through the one type that knows its pointers.
        // CombatCaps refuses an absent pointer and a null value rather than defaulting either.
        var caps = CombatCaps.Read(content);

        // 🔒 `05` §5, WIRED. See the remarks above on why this is not BattleSeams.For.
        var statuses = StatusCatalogue.Read(content);

        return Simulate(new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = actors,
            Caps = caps.Caps,
            Mitigation = caps.Mitigation,
            WardCapPct = caps.WardCapPct,
            RunCounters = new RunTriggerCounters(),
            Seams = services =>
            {
                var attack = new AttackPipeline(services);
                var timeline = new StatusTimeline(services, attack, statuses);

                // Phases stays NoBossPhases and Summons stays NoSummons deliberately: this overload's
                // roster is stat blocks, so it carries no boss and authors no SUMMON, and both
                // defaults are refusals at the point content asks. SimulateBossFight is the entry
                // point for a roster that has either.
                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
        });
    }

    /// <summary>
    /// 🔒 `05` §9 — one authored boss fight, which is what the balance harness's five live guardrails
    /// are measured over.
    /// </summary>
    /// <param name="battleSeed">
    /// `14` §8.1's <c>battleSeed</c>. One fight per seed; <c>05</c> §9 sweeps 10 000 of them per
    /// <c>(chapter, tier, buildArchetype)</c>.
    /// </param>
    /// <param name="hero">The hero's `05` §1 block, before `18` §8.</param>
    /// <param name="heroLevel">The hero's Legend Level — `05` §4's <c>20 * attackerLevel</c> term.</param>
    /// <param name="bossId">
    /// A script id in <c>content/bosses/bosses.json</c> — the nine `17` §2-§9 bosses plus
    /// <c>BOSS_FTUE</c>. An unknown id throws.
    /// </param>
    /// <param name="bossPower">
    /// 🔒 `02` §4.3's <c>EnemyPower(i)</c> for the boss node, with <c>StageMult.Boss = 2.20</c>
    /// <b>already inside it</b>. `05` §6.3 and `17` §1: <em>"do not multiply by 2.20 again."</em> It
    /// is a parameter rather than something this method derives precisely so that it cannot be
    /// applied twice — the same reason every type under <c>Rules/Combat/Bosses/</c> takes it.
    /// </param>
    /// <param name="enemyLevel">
    /// 🔒 `05` §6.0's <c>EnemyLevel(c, t)</c> — <em>"all enemies, Elites, Guardians and bosses in a
    /// <c>(chapter, tier)</c> share this level"</em>, adds included.
    /// </param>
    /// <param name="content">The loaded, schema-validated content snapshot.</param>
    /// <param name="firstClear">`17` §1 — the first time a player fights a boss, phase 1 lasts 20% longer.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="bossId"/> is not in the document.</exception>
    /// <exception cref="MissingContentException">A document or pointer the fight needs is absent.</exception>
    /// <exception cref="UnauthorisedTunableException">A constant the fight needs is <c>null</c> in the data.</exception>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why this is a second public method and not a second public type.</b> `05` §9 and `30`
    /// §11.2 both have the harness call the simulator directly, and `30` §6 pins
    /// <c>tools/BalanceHarness</c> to <c>SlayIdleRepeat.Core</c> with no package references — while
    /// <c>Core.csproj</c> grants <c>InternalsVisibleTo</c> to <c>SlayIdleRepeat.Core.Tests</c> alone.
    /// Everything a boss fight needs (<c>BossCatalogue</c>, <c>BossEncounterBuilder</c>,
    /// <c>EnemyCatalogue</c>, <c>BattlePlan</c>, the six seams) is therefore unreachable from the
    /// harness, and without an entry point of this shape `05` §9's v1 deliverable cannot exist at all.
    /// </para>
    /// <para>
    /// 🔒 <b>R16's enumerated closure is untouched.</b> Every type in this signature —
    /// <see cref="ActorStats"/>, <see cref="SimulationResult"/>, <see cref="ContentSnapshot"/> and
    /// primitives — is already public, so <c>Domain.PublicRuleTypes</c> still names exactly six. The
    /// precedent is M2-09's, one method up: it widened the <em>parameter list</em> for the content
    /// snapshot and recorded that <em>"it widens no public <b>type</b>"</em>. What stays inside is the
    /// DSL — a caller cannot author an effect, reach a seam, or build a roster; it names a boss the
    /// document already carries.
    /// </para>
    /// <para>
    /// ⚠️ The composition itself lives in <see cref="BossFight"/>. Its remarks recorded that this
    /// method's <em>sibling</em> could not run a fight applying a `05` §5 status; that gap is closed
    /// — the plain overload above now composes its own <c>StatusTimeline</c> for the same reason this
    /// one does, and for the same reason neither uses <see cref="BattleSeams.For"/>.
    /// </para>
    /// </remarks>
    public static SimulationResult SimulateBossFight(
        ulong battleSeed,
        ActorStats hero,
        int heroLevel,
        string bossId,
        double bossPower,
        int enemyLevel,
        ContentSnapshot content,
        bool firstClear = false) =>
        BossFight.Run(battleSeed, hero, heroLevel, bossId, bossPower, enemyLevel, content, firstClear);

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
