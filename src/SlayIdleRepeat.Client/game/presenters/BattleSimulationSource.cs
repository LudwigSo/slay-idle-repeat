using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// Why a battle replay has a fight to animate, or exactly why it has none.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A vocabulary rather than a bool, because "nothing is animating" is the same still frame
/// for every one of these reasons.</b> A read that never answered, a run with no battle open, a run
/// whose phase is not the battle phase, a run that no longer records which battle this is, a
/// simulator that threw and a simulation that came back with an empty log all look identical on
/// screen. Each is escaped by something completely different — retry, leave the screen, finish the
/// tile, report the run, report the crash, report the content — so each is named here and each gets
/// its own sentence.
/// </para>
/// <para>
/// 🔒 <b>There were eight, and the fifth was <c>HeroStatsUnavailable</c> — retired, not renumbered.</b>
/// It said the hero's stat block could not be assembled by anything a client can reach, which was true
/// when it was written and was made false by <c>HeroBuild</c> and <c>RunBattle.Simulate</c> going
/// public. Its value 5 is deliberately left unused: a later state taking that number would inherit
/// the retired one's meaning in every log line and screenshot that still carries it.
/// </para>
/// </remarks>
public enum BattleReadiness
{
    /// <summary>A real simulation result is in hand and the replay can play it.</summary>
    Ready = 1,

    /// <summary>The profile carries no run at all, so there is no fight to be in.</summary>
    NoRun = 2,

    /// <summary>The run exists but is not standing in the battle phase.</summary>
    PhaseNotBattle = 3,

    /// <summary>The run carries no battle counter, so the battle's seed cannot be re-derived.</summary>
    SeedUnavailable = 4,

    /// <summary>The simulator was called and threw. A state, never an escape.</summary>
    SimulatorFailed = 6,

    /// <summary>The simulator answered, and its log carries no events to draw.</summary>
    LogEmpty = 7,

    /// <summary>
    /// The read this screen depends on did not answer at all. A state, never an escape.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not one of the four stall causes the design enumerates — those are all reasons a fight
    /// failed to materialise, and this is a screen that never learned whether there was one. It is
    /// named separately for the same reason they are named separately from each other: a retry is
    /// the answer here and is the answer to none of them.
    /// </remarks>
    ReadUnavailable = 8,
}

/// <summary>
/// One attempt at predicting a battle locally: how far it got, the seed it got there with, and
/// the result when there is one.
/// </summary>
/// <param name="Readiness">How far the attempt got.</param>
/// <param name="BattleSeed">The seed the attempt derived, or zero when it derived none.</param>
/// <param name="SeedDerived">
/// Whether <paramref name="BattleSeed"/> is a real derivation rather than the zero of an attempt
/// that never got that far. Reported separately because zero is a legitimate seed value and a
/// caller reading the number alone could not tell the two apart.
/// </param>
/// <param name="Result">The simulated fight, or null when the attempt produced none.</param>
public sealed record BattleSimulationAttempt(
    BattleReadiness Readiness, ulong BattleSeed, bool SeedDerived, SimulationResult? Result);

/// <summary>Predicts one battle of a run locally, from the persisted rows it is fought from.</summary>
/// <remarks>
/// <para>
/// A client-local collaborator rather than a port: nothing crosses a process boundary here, and
/// the whole computation is a pure function of the two rows plus the loaded content set.
/// </para>
/// <para>
/// 🔒 <b>Both rows, because a fight is composed from both.</b> The enemy is scaled by the run and the
/// hero is composed from the profile's loadout, so a prediction handed the run alone could only fight
/// a hero it had invented — which is the whole reason this took the player's row as soon as it took a
/// fight at all. The screen already reads both in one call, so this costs no extra read.
/// </para>
/// </remarks>
public interface IBattleSimulationSource
{
    /// <summary>Attempts the prediction for the battle the given run is standing in.</summary>
    /// <param name="player">The profile row the hero is composed from — where the loadout's items live.</param>
    /// <param name="run">The run whose open battle is to be predicted.</param>
    /// <exception cref="ArgumentNullException">A row is null.</exception>
    BattleSimulationAttempt Simulate(PlayerSnapshot player, RunSnapshot run);
}

/// <summary>
/// The production prediction: derives the battle's seed, then fights the battle through the same
/// door the confirming handler settles it through.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One composition behind two doors, and this is the client's door.</b> <c>14</c> §9 has the
/// client compute the fight locally from the seed the run committed and report its <c>LogHash</c>,
/// and <c>CONFIRM_BATTLE_RESULT</c> recompute the same fight and win any disagreement. Both go
/// through <see cref="RunBattle.Simulate(PlayerSnapshot, RunSnapshot, ContentSnapshot)"/>, which is
/// what makes the two answers the same answer: a second composition here would diverge from the
/// handler's the first time either changed, and the divergence would read as tampering — so honest
/// players would be the ones it caught.
/// </para>
/// <para>
/// 🔴 <b>It refused at the hero's stat block until 2026-08-19, and that refusal was stale rather
/// than wrong.</b> It was written when no <c>AffixId → StatId</c> mapping and no gear effect source
/// existed at any accessibility; <c>M7-06b</c> authored both and made <c>HeroBuild</c> and
/// <c>RunBattle</c> public for this caller, and nothing came back here to call them. What shipped
/// was a battle screen that reported a missing feature that existed, and a run that could not leave
/// <c>RunPhase.BattlePending</c> — which refuses every other command, so the profile could not start
/// another run either. Found by playing the exported build, not by any test: the four cases that
/// pinned the refusal quoted its reasoning back as their own justification.
/// </para>
/// <para>
/// 🔒 <b>The phase and the counter are still answered here rather than by the exception.</b>
/// <c>RunBattle</c> throws for both, and a screen that reported them as
/// <see cref="BattleReadiness.SimulatorFailed"/> would send a player to report a crash for a run
/// that is merely standing somewhere else.
/// </para>
/// </remarks>
public sealed class LocalBattleSimulation : IBattleSimulationSource
{
    /// <summary>
    /// 🔴 Named because the number it would need is the one thing the run row does not carry.
    /// </summary>
    private const string TheRunsCombatCounterIsTheOnlyBattleIndexThereIs =
        "No domain event announces a battle's seed and no field stores it: the command that opens " +
        "a fight advances the run's combat stream and discards the generator, and its own remarks " +
        "say the seed is recoverable from the accepted run snapshot without an event. The counter " +
        "counts battles STARTED, so the battle now open is the one before it. A run whose counter " +
        "is missing or zero is a run whose open battle nothing can name — a corrupt row, which is a " +
        "different failure from a simulator that threw on a sound one and is reported as one.";

    /// <summary>The stream whose counter says how many battles this run has started.</summary>
    private const string CombatStreamName = "combat";

    /// <summary>
    /// The content set the simulator takes its caps from — the last argument of the call this type
    /// exists to make.
    /// </summary>
    /// <remarks>
    /// The enemy's archetype, the chapter's par power, the combat caps and the boss's own row all
    /// come out of it, so it is the argument that makes the fight this chapter's fight rather than an
    /// arithmetic one. Held from construction rather than passed per call: it is the same loaded set
    /// for the life of the screen, and a per-call one would let two fights of a run be predicted
    /// against two different content versions.
    /// </remarks>
    private readonly ContentSnapshot _content;

    /// <summary>Builds the prediction over the loaded content set the simulator reads its caps from.</summary>
    /// <param name="content">The loaded content set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public LocalBattleSimulation(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _content = content;
    }

    /// <inheritdoc/>
    public BattleSimulationAttempt Simulate(PlayerSnapshot player, RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);

        // Refused before anything is derived: the counter of a run with no open battle names the
        // LAST fight, so a seed reported here would be a real number for the wrong fight.
        if (run.Phase != RunPhase.BattlePending)
        {
            return Refused(BattleReadiness.PhaseNotBattle);
        }

        if (!TryReadBattleIndex(run, out var battleIndex))
        {
            return Refused(BattleReadiness.SeedUnavailable);
        }

        // Derived here as well as inside the fight, and deliberately: this is the number a bug report
        // carries, and it has to be reportable on the arms where the fight itself does not answer.
        var battleSeed = SeedDerivation.BattleSeed(run.RunSeed, battleIndex);

        SimulationResult fight;

        try
        {
            fight = RunBattle.Simulate(player, run, _content);
        }
        catch (Exception)
        {
            // 🔒 Every exception, not a chosen list. RunBattle documents five types, four of them
            // reachable once the arguments are non-null — a row that does not rehydrate, a run not
            // standing in a fight, a chapter or boss with no authored row, a missing content pointer —
            // and the one thing they have in common on screen is a still frame. Naming a subset here
            // would let the fifth take the scene down mid-run instead of putting a sentence on it, and
            // the identity of the failure belongs in the log rather than in a state vocabulary a
            // player reads. The seed is reported alongside it, because it is what turns "the fight did
            // not play" into a report somebody can reproduce.
            return new BattleSimulationAttempt(
                BattleReadiness.SimulatorFailed, battleSeed, SeedDerived: true, Result: null);
        }

        // A fight the simulator answered with no events at all is not something this screen can
        // animate, and it is a content or rules fault rather than a crash — so it is reported as
        // itself rather than as a fight of zero length that plays instantly and confirms.
        if (fight.Log.Count == 0)
        {
            return new BattleSimulationAttempt(
                BattleReadiness.LogEmpty, battleSeed, SeedDerived: true, Result: null);
        }

        return new BattleSimulationAttempt(
            BattleReadiness.Ready, battleSeed, SeedDerived: true, fight);
    }

    /// <summary>
    /// Reads the index of the battle the run is standing in, or refuses when the row does not name
    /// one. See <see cref="TheRunsCombatCounterIsTheOnlyBattleIndexThereIs"/>.
    /// </summary>
    /// <remarks>
    /// An absent counter and a zero one are the same fact — a run whose open battle nothing can
    /// name — so they take the same exit rather than one of them reaching a derivation that refuses
    /// a negative index by throwing.
    /// </remarks>
    private static bool TryReadBattleIndex(RunSnapshot run, out int battleIndex)
    {
        battleIndex = 0;

        if (!run.RngStreamPositions.TryGetValue(CombatStreamName, out var battlesStarted) ||
            battlesStarted == 0)
        {
            return false;
        }

        battleIndex = (int)(battlesStarted - 1);

        return true;
    }

    private static BattleSimulationAttempt Refused(BattleReadiness readiness) =>
        new(readiness, BattleSeed: 0, SeedDerived: false, Result: null);
}
