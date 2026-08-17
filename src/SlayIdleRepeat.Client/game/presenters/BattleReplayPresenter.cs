using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How fast the replay consumes the log.</summary>
/// <remarks>
/// 🔒 The numbers are the multipliers themselves rather than ordinals, because the whole meaning of
/// a speed here is arithmetic: the replay consumes this many ticks per tick of wall time. The fight
/// is already decided, so a faster setting changes nothing except how long the player watches.
/// </remarks>
public enum BattleSpeed
{
    /// <summary>Real time — one log tick per twentieth of a second.</summary>
    Single = 1,

    /// <summary>Twice as fast.</summary>
    Double = 2,

    /// <summary>Three times as fast, the fastest setting short of skipping.</summary>
    Triple = 3,
}

/// <summary>What one call that could have submitted the battle's result actually did.</summary>
public enum BattleSubmission
{
    /// <summary>Nothing was submitted, because nothing was owed yet or the result is already in.</summary>
    NothingToSubmit = 1,

    /// <summary>The result went to the host and was accepted.</summary>
    Submitted = 2,

    /// <summary>
    /// 🔒 Nothing was submitted because there is no simulated result to submit. Named rather than
    /// folded into <see cref="NothingToSubmit"/>: this is the arm that must never learn to invent a
    /// hash.
    /// </summary>
    RefusedNotAvailable = 3,

    /// <summary>The result went to the host and the rules layer refused it.</summary>
    RefusedByRules = 4,
}

/// <summary>One actor in the fight, as much of it as the log actually fixes.</summary>
/// <param name="ActorId">The slot the log identifies this actor by.</param>
/// <param name="StartingHp">
/// What it began the fight with, or null when the log does not fix it. Null is a real answer for a
/// surviving enemy and must stay one.
/// </param>
/// <param name="EndingHp">What it finished the fight with, or null when the log does not fix that.</param>
/// <remarks>
/// 🔴 <b>A maximum HP is not in the log and is not derivable for every actor.</b> The hero's start
/// follows from the reported remaining HP plus every hit taken minus every heal received, and any
/// actor the log records a death for ended at zero, which makes its start follow the same way. An
/// enemy that survived gives neither equation an anchor, so its bar has no denominator — and a
/// denominator invented here would draw a health bar that is wrong by whatever the guess was off by.
/// </remarks>
public sealed record ReplayActor(byte ActorId, double? StartingHp, double? EndingHp);

/// <summary>
/// Drives the Battle Replay screen: what the pre-computed log shows, how fast it is shown, and the
/// one command that closes the battle the run is standing in.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with nothing of the engine anywhere near it. Playback is driven by
/// <see cref="AdvanceAsync"/> taking a delta rather than by a clock, because the scene already has a
/// per-frame delta and a replay measured against wall time would be a replay a case cannot step.
/// </para>
/// <para>
/// 🔴 <b>The fight is not predicted locally today, and that is the largest thing to know about this
/// screen.</b> The seed half is real — see <see cref="LocalBattleSimulation"/> — and the stat half
/// does not exist at any accessibility. So the screen is built to animate a real log and reports, by
/// name, that it has none.
/// </para>
/// <para>
/// 🔴 Speed does not persist across runs — see
/// <see cref="TheSpeedSettingHasNowhereToPersistYet"/>. It arrives as a constructor argument so the
/// store that will own it has a seam to arrive at.
/// </para>
/// <para>
/// 🔴 Enemy names are unreachable — see <see cref="TheEnemysNameIsNotReachableHere"/> — so the
/// banner names an actor by its role.
/// </para>
/// </remarks>
public sealed class BattleReplayPresenter
{
    /// <summary>
    /// 🔴 Deliberately not stored, and named so it can be found. The settings screen that would own
    /// a speed preference is unbuilt, so this screen takes its opening speed rather than remembering
    /// one.
    /// </summary>
    private const string TheSpeedSettingHasNowhereToPersistYet =
        "The design has the speed toggle persist across runs, and it should: a player who wants " +
        "every fight at triple speed wants it once, not forty times. The screen that owns that " +
        "preference — audio, haptics, speed, accessibility — is not built, and there is no profile " +
        "field, no local store and no command that carries one. Writing a store here would put the " +
        "settings screen's first decision in a battle screen, so the opening speed is a constructor " +
        "argument instead: the seam the store arrives at, with nothing invented behind it.";

    /// <summary>
    /// 🔴 Deliberately unread, and named so it can be found. Nothing a client holds carries an
    /// enemy's name, so the banner names the actor by its role and index.
    /// </summary>
    private const string TheEnemysNameIsNotReachableHere =
        "The design's banner reads an enemy's name and its elite modifier. Neither is reachable: " +
        "enemy derivation, the chapter's enemy pools and the elite modifier table are all internal " +
        "to the rules assembly, and the log identifies an actor by a slot number and nothing else. " +
        "What the slot number does fix is the actor's ROLE — the hero, a pet, an enemy by index — " +
        "so that is what the banner says. A name invented here would be a different enemy from the " +
        "one the server settled the fight against.";

    /// <summary>
    /// The simulator's fixed tick rate, transcribed.
    /// </summary>
    /// <remarks>
    /// 🔒 A transcription rather than a reference: the log's own tick constants are internal to the
    /// rules assembly and no public member restates them. It is pinned by driving a real fight to
    /// the fight-length cap and checking that this screen consumes it in exactly the capped number
    /// of seconds — a wrong transcription there is a replay that ends halfway through or runs on
    /// past the end, which is why the pin is a behaviour and not a comparison of two constants.
    /// </remarks>
    private const int TicksPerSecond = 20;

    private const string TitleNameKey = "loc.battle.title.name";
    private const string HeroLabelKey = "loc.battle.hero.label";
    private const string EnemyLabelKey = "loc.battle.enemy.label";
    private const string SpeedLabelKey = "loc.battle.speed.label";
    private const string SpeedSingleActionKey = "loc.battle.speed_single.action";
    private const string SpeedDoubleActionKey = "loc.battle.speed_double.action";
    private const string SpeedTripleActionKey = "loc.battle.speed_triple.action";
    private const string SkipActionKey = "loc.battle.skip.action";
    private const string PhaseOneNameKey = "loc.battle.phase_one.name";
    private const string PhaseTwoNameKey = "loc.battle.phase_two.name";
    private const string PhaseThreeNameKey = "loc.battle.phase_three.name";
    private const string LoadingStatusKey = "loc.battle.loading.status";
    private const string NoRunStatusKey = "loc.battle.no_run.status";
    private const string PhaseNotBattleStatusKey = "loc.battle.phase_not_battle.status";
    private const string SeedUnavailableStatusKey = "loc.battle.seed_unavailable.status";
    private const string HeroStatsUnavailableStatusKey = "loc.battle.hero_stats_unavailable.status";
    private const string SimulatorFailedStatusKey = "loc.battle.simulator_failed.status";
    private const string LogEmptyStatusKey = "loc.battle.log_empty.status";
    private const string ReadUnavailableStatusKey = "loc.battle.read_unavailable.status";
    private const string VictoryStatusKey = "loc.battle.victory.status";
    private const string DefeatStatusKey = "loc.battle.defeat.status";
    private const string RefusedStatusKey = "loc.battle.refused.status";

    /// <summary>The status line of a screen whose fight is the answer: there is nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>How long the boss phase band stays up once a phase is crossed.</summary>
    private static readonly TimeSpan PhaseBandDwell = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// What every animation on this screen is shortened to under reduced motion.
    /// </summary>
    /// <remarks>
    /// 🔒 An accessibility clause, not a taste setting, and stated as one number because the clause
    /// is one number: reduced motion shortens every animation to this, the phase band included.
    /// </remarks>
    private static readonly TimeSpan ReducedMotionDwell = TimeSpan.FromMilliseconds(100);

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly IBattleSimulationSource _simulations;
    private readonly PlayerId _player;
    private readonly RunId _run;
    private readonly bool _reducedMotion;

    /// <summary>Builds the screen over the host, the strings, the prediction and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and the result is submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="simulations">What predicts the battle locally, or names why it cannot.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run whose battle is being watched.</param>
    /// <param name="initialSpeed">
    /// The speed the replay opens at. An argument rather than a stored preference — see
    /// <see cref="TheSpeedSettingHasNowhereToPersistYet"/>.
    /// </param>
    /// <param name="reducedMotion">
    /// Whether every animation is shortened and every burst suppressed. An argument for the same
    /// reason: the accessibility screen that would set it is not built.
    /// </param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public BattleReplayPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        IBattleSimulationSource simulations,
        PlayerId player,
        RunId run,
        BattleSpeed initialSpeed = BattleSpeed.Single,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(simulations);

        _gameHost = gameHost;
        _strings = strings;
        _simulations = simulations;
        _player = player;
        _run = run;
        _reducedMotion = reducedMotion;

        Speed = initialSpeed;
    }

    /// <summary>Why the replay has a fight to animate, or why it has none. Null before the read.</summary>
    public BattleReadiness? Readiness { get; private set; }

    /// <summary>The battle's seed as this screen re-derived it, or zero when it derived none.</summary>
    public ulong BattleSeed { get; private set; }

    /// <summary>
    /// Whether <see cref="BattleSeed"/> is a real derivation.
    /// </summary>
    /// <remarks>
    /// 🔒 True even for a refusal, and that is the point: the local prediction is blocked at the
    /// hero's stat block and not at the seed, so a reader who cannot tell the two apart would file
    /// the wrong bug.
    /// </remarks>
    public bool SeedDerived { get; private set; }

    /// <summary>Whether the hero won, or null while no fight has been simulated.</summary>
    public bool? HeroWon { get; private set; }

    /// <summary>How long the fight runs, in log ticks. Zero when there is no fight.</summary>
    public int TotalTicks { get; private set; }

    /// <summary>Where the playhead stands, in log ticks.</summary>
    public int CurrentTick { get; private set; }

    /// <summary>Whether the playhead has reached the end of the log.</summary>
    public bool Complete => TotalTicks > 0 && CurrentTick >= TotalTicks;

    /// <summary>
    /// The events the last advance crossed — everything after the previous playhead, up to and
    /// including the current one.
    /// </summary>
    /// <remarks>
    /// 🔒 Half-open on purpose. An event sitting exactly on the previous playhead was drawn by the
    /// advance that reached it, so re-emitting it would double every spark on a slow frame; an event
    /// sitting exactly on the new playhead has just happened and belongs to this step.
    /// </remarks>
    public IReadOnlyList<CombatEvent> StepEvents { get; private set; } = [];

    /// <summary>Every actor the log mentions, with whatever the log fixes about its health.</summary>
    public IReadOnlyList<ReplayActor> Actors { get; private set; } = [];

    /// <summary>The boss phase last crossed at or before the playhead, or null for a fight with no boss.</summary>
    public int? CurrentBossPhase { get; private set; }

    /// <summary>Whether the phase band is still up.</summary>
    public bool PhaseBandVisible { get; private set; }

    /// <summary>How fast the log is being consumed.</summary>
    public BattleSpeed Speed { get; private set; }

    /// <summary>
    /// Whether the skip is offered. Always.
    /// </summary>
    /// <remarks>
    /// 🔒 Unconditionally true, in every state including every state where there is nothing to skip.
    /// It is an accessibility clause rather than a convenience, and a screen that withdraws it in
    /// the states where it is least sure of itself is a screen a player can be trapped on.
    /// </remarks>
    public bool SkipAvailable => false;

    /// <summary>Why the rules layer refused the result, or null when it did not.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>The screen's heading.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The caption over the hero's health bar.</summary>
    public string HeroLabel => _strings.Resolve(HeroLabelKey);

    /// <summary>The caption over the enemy's health bar.</summary>
    public string EnemyLabel => _strings.Resolve(EnemyLabelKey);

    /// <summary>The caption beside the speed control.</summary>
    public string SpeedLabel => _strings.Resolve(SpeedLabelKey);

    /// <summary>The ×1 control's caption.</summary>
    public string SpeedSingleText => _strings.Resolve(SpeedSingleActionKey);

    /// <summary>The ×2 control's caption.</summary>
    public string SpeedDoubleText => _strings.Resolve(SpeedDoubleActionKey);

    /// <summary>The ×3 control's caption.</summary>
    public string SpeedTripleText => _strings.Resolve(SpeedTripleActionKey);

    /// <summary>The skip control's caption.</summary>
    public string SkipText => _strings.Resolve(SkipActionKey);

    /// <summary>What the boss phase band reads, or nothing when no band is up.</summary>
    public string PhaseBandText => NothingLeftToSay;

    /// <summary>What the screen says about its own state.</summary>
    public string StatusText => NothingLeftToSay;

    /// <summary>Reads the run this screen is about and settles the fight it will animate.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct)
    {
        Scaffold(ct);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves the playhead by one frame's worth of time, and closes the battle once it lands.
    /// </summary>
    /// <param name="deltaSeconds">How much wall time has passed since the last advance.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<BattleSubmission> AdvanceAsync(double deltaSeconds, CancellationToken ct) =>
        Task.FromResult(BattleSubmission.NothingToSubmit);

    /// <summary>
    /// Jumps to the end of the fight and closes the battle, without replaying what was skipped.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    public Task<BattleSubmission> SkipAsync(CancellationToken ct) =>
        Task.FromResult(BattleSubmission.NothingToSubmit);

    /// <summary>Steps the speed setting on, wrapping the fastest back round to real time.</summary>
    public void CycleSpeed()
    {
    }

    /// <summary>
    /// ⚠️ Phase-1 scaffolding: reads every collaborator once so the declarations above compile
    /// under warnings-as-errors before the behaviour behind them exists. Deleted by the phase that
    /// implements them.
    /// </summary>
    private void Scaffold(CancellationToken ct)
    {
        _ = _gameHost;
        _ = _simulations;
        _ = _player;
        _ = _run;
        _ = _reducedMotion;
        _ = ct;
        _ = TicksPerSecond;
        _ = PhaseBandDwell;
        _ = ReducedMotionDwell;
        _ = TheSpeedSettingHasNowhereToPersistYet;
        _ = TheEnemysNameIsNotReachableHere;
        _ = PhaseOneNameKey;
        _ = PhaseTwoNameKey;
        _ = PhaseThreeNameKey;
        _ = LoadingStatusKey;
        _ = NoRunStatusKey;
        _ = PhaseNotBattleStatusKey;
        _ = SeedUnavailableStatusKey;
        _ = HeroStatsUnavailableStatusKey;
        _ = SimulatorFailedStatusKey;
        _ = LogEmptyStatusKey;
        _ = ReadUnavailableStatusKey;
        _ = VictoryStatusKey;
        _ = DefeatStatusKey;
        _ = RefusedStatusKey;
    }
}
