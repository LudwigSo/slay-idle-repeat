using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// Builds the four seams of <see cref="BattleSeams"/> once the battle exists — see
/// <see cref="BattleServices"/> for why they cannot simply be values.
/// </summary>
/// <param name="services">The battle's log, draw stream, roster and phase routing.</param>
internal delegate BattleSeams BattleSeamFactory(BattleServices services);

/// <summary>
/// 🔒 Everything one <c>Simulate</c> is given — the roster, the seed, `05` §3's bounds, and the four
/// parts of the engine that are not this task's.
/// </summary>
/// <remarks>
/// <para>
/// It is the <b>internal</b> face of the simulator. <c>CombatSimulator.Simulate</c>'s public overload
/// is `05` §1's <c>Simulate(seed, heroSnapshot, enemySnapshot)</c> and can express a plain fight and
/// nothing else; this is what a boss, a duel or the balance harness needs, and `30` §11.2 keeps it
/// inside <c>Core</c> on purpose.
/// </para>
/// </remarks>
internal sealed record BattlePlan
{
    /// <summary>
    /// 🔒 `14` §8.1's <c>battleSeed</c>. Combat draw <c>i</c> is <c>Hash64(battleSeed, "combat", i)</c>
    /// — <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c>, built once per fight.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The <c>runSeed</c> never enters this layer.</b> `02` §2 keeps it on the server; the
    /// battle seed is derived from it upstream and handed in, which is what lets `11` §6 re-run a duel
    /// server-side from the same value.
    /// </remarks>
    public required ulong BattleSeed { get; init; }

    /// <summary>
    /// Every actor, in `05` §3.1 order: hero, pets in slot order, then enemies by index.
    /// </summary>
    public required IReadOnlyList<ActorPlan> Actors { get; init; }

    /// <summary>`05` §1's caps, before <c>STAT_CAP_OVERRIDE</c> (`18` §8 step 9).</summary>
    public required StatCaps Caps { get; init; }

    /// <summary>`05` §3 / §3.3's bounds. Defaults to <see cref="CombatRules.PvE"/>.</summary>
    public CombatRules Rules { get; init; } = CombatRules.PvE;

    /// <summary>
    /// 🔒 The <b>run's</b> trigger counters, held across battles — not one built per fight. `18` §3:
    /// <em>"<c>ON_ATTACK</c> counters reset at battle start; <c>ON_KILL</c> counters persist across
    /// battles."</em> Handing the same instance to every battle of a run is what makes that true
    /// structurally.
    /// </summary>
    public required IRunTriggerCounters RunCounters { get; init; }

    /// <summary>The run's state for `18` §4's nine run conditions. <c>null</c> in a duel or a sweep.</summary>
    public IRunStateView? Run { get; init; }

    /// <summary>The four seams `05` §3.1 does not give this task. Defaults to the strict set.</summary>
    public BattleSeamFactory Seams { get; init; } = static _ => BattleSeams.Strict;

    /// <summary>
    /// Checks everything a roster must satisfy before a tick runs, and returns the plan.
    /// </summary>
    /// <exception cref="ArgumentException">The roster breaks one of the rules below.</exception>
    /// <remarks>
    /// 🔒 <b>Checked up front, because every one of these fails silently at run time.</b> A duplicate
    /// index makes <c>BattleRoster</c>'s tie-breaks non-total and the fight's outcome depend on list
    /// construction; a duplicate log id makes the replayer draw two actors on one HP bar; a
    /// battle-local id on an <c>ON_KILL</c> effect resets <c>PK_MIDAS</c> every fight. All three
    /// produce a legal-looking log.
    /// </remarks>
    internal BattlePlan Validated()
    {
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(Caps);
        ArgumentNullException.ThrowIfNull(RunCounters);
        ArgumentNullException.ThrowIfNull(Seams);

        Rules.Validated();

        if (Actors.Count == 0)
        {
            throw new ArgumentException(
                "A battle with no actors has no outcome. `05` §3's roster is a hero, 0-3 pets and 1-5 enemies.",
                nameof(Actors));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var indices = new HashSet<int>();
        var logIds = new HashSet<byte>();

        foreach (var actor in Actors)
        {
            ArgumentNullException.ThrowIfNull(actor, nameof(Actors));

            Unique(ids.Add(actor.Id), "id", actor.Id,
                "`IEffectActorView.Id` is what OWNER matches on and it takes the first hit, so two " +
                "actors sharing one would give a sporeling the wrong summoner.");

            Unique(indices.Add(actor.Index), "index", actor.Index.ToString(CultureInfo.InvariantCulture),
                "`05` §3.1's index is the loop's initiative order and `18` §5's tie-break. A duplicate " +
                "makes both non-total, so the fight's outcome depends on how the roster list was built.");

            Unique(logIds.Add(actor.LogId), "log id", actor.LogId.ToString(CultureInfo.InvariantCulture),
                "`05` §7's SourceId/TargetId address one actor each. Two actors on one id makes the " +
                "replay draw the second resuming the first's HP bar (CombatActor).");

            if (actor.LogId == CombatActor.None)
            {
                throw new ArgumentException(
                    $"Actor '{actor.Id}' takes log id {CombatActor.None.ToString(CultureInfo.InvariantCulture)}, " +
                    "which CombatActor reserves for \"no actor\". Every BattleStart, BattleEnd and " +
                    "RunEffectQueued would then read as this actor.",
                    nameof(Actors));
            }

            RequireStableOnKillIds(actor);
        }

        RequireTwoSides();

        return this;
    }

    /// <summary>
    /// 🔒 A fight needs a hero and something to fight — checked here, because both failures produce a
    /// <b>legal-looking one-tick log</b> rather than an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `05` §3.1's slot 8 breaks when the hero is down or the enemies are cleared, and both questions
    /// are asked of a roster: with no hero-side <c>HERO</c> the fight ends at tick 0 as a loss, and
    /// with no killable enemy it ends at tick 0 as a win. Neither throws, both seal a valid log, and
    /// the balance harness would read a batch of them as content being trivially easy or trivially
    /// impossible.
    /// </para>
    /// <para>
    /// ⚠️ <b>Exactly one hero-side hero, and this holds in a duel too.</b> `05` §3.3's Ghost Duel is
    /// <em>"two hero-shaped sides"</em> — but the defending hero is on <see cref="BattleSide.ENEMY"/>
    /// (<c>CombatActor</c> puts it at <c>FirstEnemy</c>), so it satisfies the enemy clause rather
    /// than doubling the hero one.
    /// </para>
    /// </remarks>
    private void RequireTwoSides()
    {
        var heroes = Actors.Count(a => a.Side == BattleSide.HERO && a.Kind == EffectActorKind.HERO);
        if (heroes != 1)
        {
            throw new ArgumentException(
                $"The roster carries {heroes.ToString(CultureInfo.InvariantCulture)} hero-side heroes; " +
                "`05` §3 gives a fight exactly one. With none, `05` §3.1's slot 8 sees a downed hero on " +
                "tick 0 and the fight ends as a one-tick loss with a perfectly valid log — which is the " +
                "failure this whole method exists to make loud.",
                nameof(Actors));
        }

        if (!Actors.Any(a => a.Side == BattleSide.ENEMY && a.Kind != EffectActorKind.PET))
        {
            throw new ArgumentException(
                "The roster carries no killable enemy — `05` §3 gives a fight 1-5, and `05` §3.2 makes " +
                "pets untargetable and unkillable, so a pet-only enemy side is the same as an empty " +
                "one. Slot 8 would see the enemies cleared on tick 0 and report a one-tick win.",
                nameof(Actors));
        }
    }

    /// <summary>
    /// 🔒 <c>TriggerRegistry</c>'s minting rule, at the one place that can enforce half of it: an
    /// <c>ON_KILL</c> effect must arrive with an instance id the run layer owns.
    /// </summary>
    private static void RequireStableOnKillIds(ActorPlan actor)
    {
        foreach (var held in actor.Effects)
        {
            if (held.Effect?.Trigger?.Kind != TriggerKind.ON_KILL || held.InstanceId is not null)
            {
                continue;
            }

            throw new ArgumentException(
                $"Actor '{actor.Id}' holds ON_KILL effect '{held.Effect.Id}' with no instance id, so the " +
                "simulator would mint a battle-local one. `18` §3 makes ON_KILL counters run-scoped — " +
                "PK_MIDAS's \"every 6th enemy killed\" — and a battle-local id resets that counter every " +
                "fight, which produces an entirely legal-looking log and is wrong in the only number " +
                "that matters. Pass the id the run layer holds for this effect.",
                nameof(Actors));
        }
    }

    private static void Unique(bool added, string what, string value, string consequence)
    {
        if (!added)
        {
            throw new ArgumentException(
                $"Two actors share the {what} '{value}'. {consequence}", nameof(Actors));
        }
    }
}
