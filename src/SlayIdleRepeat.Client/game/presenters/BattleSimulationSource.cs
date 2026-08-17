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
/// 🔒 <b>A vocabulary rather than a bool, because "nothing is animating" is the same still frame
/// for every one of these reasons.</b> A read that never answered, a run with no battle open, a run
/// whose phase is not the battle phase, a run that no longer records which battle this is, a hero
/// whose stat block this build cannot assemble, a simulator that threw and a simulation that came
/// back with an empty log all look identical on screen. Each is escaped by something completely
/// different — retry, leave the screen, finish the tile, report the run, wait for the feature,
/// report the crash, report the content — so each is named here and each gets its own sentence.
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

    /// <summary>
    /// 🔴 The hero's stat block cannot be built from anything a client can reach. Today's answer.
    /// </summary>
    HeroStatsUnavailable = 5,

    /// <summary>The simulator was called and threw. A state, never an escape.</summary>
    SimulatorFailed = 6,

    /// <summary>The simulator answered, and its log carries no events to draw.</summary>
    LogEmpty = 7,

    /// <summary>
    /// The read this screen depends on did not answer at all. A state, never an escape.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not one of the five stall causes the design enumerates — those are all reasons a fight
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

/// <summary>Predicts one battle of a run locally, from the run's own snapshot.</summary>
/// <remarks>
/// A client-local collaborator rather than a port: nothing crosses a process boundary here, and
/// the whole computation is a pure function of the run row plus the loaded content set.
/// </remarks>
public interface IBattleSimulationSource
{
    /// <summary>Attempts the prediction for the battle the given run is standing in.</summary>
    /// <param name="run">The run whose open battle is to be predicted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    BattleSimulationAttempt Simulate(RunSnapshot run);
}

/// <summary>
/// The production prediction: derives the battle's seed for real, then refuses at the stat block.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The seed half is real and the stat half is not, and that split is the whole of this
/// type.</b> The run snapshot carries the run's committed seed and a per-stream counter whose
/// combat row counts battles started, and the derivation that turns those two into a battle seed
/// is public. So this derives the seed, reports it, and can prove it against an independent
/// derivation of the same two numbers.
/// </para>
/// <para>
/// 🔴 It then stops, because <see cref="TheHerosStatBlockCannotBeBuiltHere"/>. It does not invent
/// stats and it does not invent a hash.
/// </para>
/// </remarks>
public sealed class LocalBattleSimulation : IBattleSimulationSource
{
    /// <summary>
    /// 🔴 Deliberately not attempted, and named so it can be found. Nothing a client can reach can
    /// turn a hero's stored row into the stat block the simulator takes, so this refuses by name
    /// rather than fabricating one.
    /// </summary>
    private const string TheHerosStatBlockCannotBeBuiltHere =
        "A stat block CAN be built from outside the rules assembly — the factory is public and the " +
        "balance harness builds one every sweep. The HERO's stat block cannot, and not merely " +
        "because the pieces are hidden: the code does not exist at any accessibility. There is no " +
        "affix-to-stat mapping anywhere, in code or in content, and no gear, talent or pet effect " +
        "source that could turn a loadout into a list of effects; the aggregation that would " +
        "combine them, the base curve the hero starts from and every gear derivation rule are all " +
        "internal on top of that. What a client is handed is the raw ingredients — legend level, " +
        "inventory, loadout, talent points — and no build snapshot, no stat block and no power " +
        "number. So the local prediction is blocked at the stat block, not at the seed, and a " +
        "plausible-looking block invented here would produce a fight that is not the fight the " +
        "server will settle, reported with a hash nobody recomputes.";

    /// <summary>
    /// 🔴 Named because the number it would need is the one thing the run row does not carry.
    /// </summary>
    private const string TheRunsCombatCounterIsTheOnlyBattleIndexThereIs =
        "No domain event announces a battle's seed and no field stores it: the command that opens " +
        "a fight advances the run's combat stream and discards the generator, and its own remarks " +
        "say the seed is recoverable from the accepted run snapshot without an event. The counter " +
        "counts battles STARTED, so the battle now open is the one before it. A run whose counter " +
        "is missing or zero is a run whose open battle nothing can name, which is a different " +
        "failure from a hero this build cannot equip and is reported as one.";

    /// <summary>The stream whose counter says how many battles this run has started.</summary>
    private const string CombatStreamName = "combat";

    /// <summary>
    /// The content set the simulator takes its caps from — the last argument of the call this type
    /// exists to make.
    /// </summary>
    /// <remarks>
    /// 🔴 Held and deliberately unread, for exactly as long as
    /// <see cref="TheHerosStatBlockCannotBeBuiltHere"/> holds. Every other argument of that call is
    /// in hand — the seed is derived, the levels are on the run row — and this one is too; the stat
    /// block is the only missing piece. Dropping it would make the day it becomes readable a change
    /// to the composition root as well as to this file, and would read as though the simulation
    /// needed nothing it does not have.
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
    public BattleSimulationAttempt Simulate(RunSnapshot run)
    {
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

        var battleSeed = SeedDerivation.BattleSeed(run.RunSeed, battleIndex);

        // 🔴 And this is as far as it goes — see TheHerosStatBlockCannotBeBuiltHere. The seed is
        // reported anyway, because which half is missing is the whole of what a report can say.
        return new BattleSimulationAttempt(
            BattleReadiness.HeroStatsUnavailable, battleSeed, SeedDerived: true, Result: null);
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
