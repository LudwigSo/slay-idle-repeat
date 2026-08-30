using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
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

/// <summary>Which side of the stage an actor stands on, from the slot the log gives it.</summary>
/// <remarks>
/// 🔒 The one reading of the rules layer's actor roster in this build. The roster is internal and has
/// no public restatement, so it is transcribed — and transcribed <b>once</b>: a screen with a hero on
/// the left and enemies on the right has to know which is which, and a second copy of the boundary
/// living where nothing can test it is a copy that drifts silently into drawing a pet as an enemy.
/// </remarks>
public enum ReplaySide
{
    /// <summary>The player's own character, slot 0.</summary>
    Hero = 1,

    /// <summary>One of the three pet slots, 1 to 3, reserved whether they are filled or not.</summary>
    Pet = 2,

    /// <summary>An enemy or a summon, from slot 4 upward.</summary>
    Enemy = 3,
}

/// <summary>Which floating combat number an event calls for, if any.</summary>
/// <remarks>
/// 🔒 A kind rather than a colour. The design pairs each kind with a colour and a size — ordinary
/// damage plain, a critical one larger and louder, healing and a damage-over-time tick each their
/// own — but a colour is an engine value and this half of the screen is deliberately unable to name
/// one. Naming the kind keeps the reading of the log here, where a case can check it, and leaves the
/// palette with the half that draws.
/// </remarks>
public enum ReplayFloater
{
    /// <summary>No number rises for this event.</summary>
    None = 0,

    /// <summary>An ordinary blow.</summary>
    Hit = 1,

    /// <summary>A blow the log announced as a critical one, which is drawn larger as well.</summary>
    Crit = 2,

    /// <summary>Healing received.</summary>
    Heal = 3,

    /// <summary>A tick of something already on the actor, which is neither a blow nor a heal.</summary>
    DamageOverTime = 4,
}

/// <summary>Which procedural burst an event calls for, if any.</summary>
public enum ReplayBurst
{
    /// <summary>Nothing bursts for this event.</summary>
    None = 0,

    /// <summary>A blow landing.</summary>
    HitSpark = 1,

    /// <summary>The announcement of a critical blow, before the number it belongs to.</summary>
    CritPop = 2,

    /// <summary>An actor going down.</summary>
    DeathPuff = 3,
}

/// <summary>
/// One event of the log, read into what it asks the screen to draw.
/// </summary>
/// <param name="ActorId">The actor the drawing belongs to — the loser of the blow, or the pet that acted.</param>
/// <param name="Side">Which side of the stage that actor stands on.</param>
/// <param name="Floater">Which floating number rises, if any.</param>
/// <param name="FloaterAmount">The size of that number, always positive.</param>
/// <param name="Burst">Which procedural burst fires, if any.</param>
/// <param name="Health">
/// What this actor's health now stands at, or null when the event moves none or the log spawned no
/// actor on this slot to move.
/// </param>
/// <param name="Died">Whether the log records this actor going down here.</param>
/// <param name="StatusId">The status whose stack count changed, or null when none did.</param>
/// <param name="StatusStacks">How many are stacked now — zero when the status has gone.</param>
/// <remarks>
/// 🔒 <b>The whole reading of the log lives on this side of the split.</b> A hit's value is the health
/// actually lost after a ward absorbed what it could; a status tick's value is a signed delta and its
/// number is drawn unsigned; an applied status carries a stack count rather than a potency; a
/// critical hit is its own event emitted <em>before</em> the blow it describes. Every one of those is
/// a fact about the rules layer, and every one of them is checked by a case here rather than trusted
/// in a file no test can instantiate.
/// </remarks>
public readonly record struct ReplayCue(
    byte ActorId,
    ReplaySide Side,
    ReplayFloater Floater,
    double FloaterAmount,
    ReplayBurst Burst,
    double? Health,
    bool Died,
    ushort? StatusId,
    int StatusStacks);

/// <summary>One actor in the fight, as much of it as the log actually fixes.</summary>
/// <param name="ActorId">The slot the log identifies this actor by.</param>
/// <param name="Side">Which side of the stage it stands on, read off that slot.</param>
/// <param name="SideIndex">Which one of its side it is — zero for the hero, first is one otherwise.</param>
/// <param name="MaxHp">
/// The bar's denominator, as the log states it. Null only for an actor the log mentions without ever
/// spawning, which is a malformed log rather than an ordinary fight.
/// </param>
/// <param name="StartingHp">
/// What it began the fight with. Null carries the same meaning as <paramref name="MaxHp"/>'s null and
/// no other.
/// </param>
/// <param name="EndingHp">What it finished the fight with, or null when the log does not fix that.</param>
/// <remarks>
/// <para>
/// 🔴 <b>The maximum is read off the log's own <c>ActorSpawned</c>, and nothing here derives or guesses
/// one.</b> This record used to publish a null <paramref name="MaxHp"/> for every enemy that survived a
/// fight, on the reasoning that a maximum was not in the log and could not be recovered: the hero's
/// start follows from the reported remaining HP with every change the log records undone in reverse,
/// and an actor the log records a death for ended at zero — but a surviving enemy anchors neither
/// equation. That reasoning was sound and its conclusion was a battle screen with <em>no enemy health
/// bar</em>, which a losing hero never fixed because it never killed anything. The answer was to put
/// the maximum in the log rather than to keep deriving around its absence; see
/// <see cref="CombatEventType.ActorSpawned"/>.
/// </para>
/// <para>
/// 🔒 <b><paramref name="StartingHp"/> still prefers the walk, and only falls back to the maximum.</b>
/// The hero opens on the health its run persisted rather than on full, so a start taken from
/// <paramref name="MaxHp"/> would draw its bar opening fuller than the fight it is replaying. The walk
/// answers for the hero and for anything that died; the fallback answers for everything else, all of
/// which opens full.
/// </para>
/// </remarks>
public sealed record ReplayActor(
    byte ActorId, ReplaySide Side, int SideIndex, double? MaxHp, double? StartingHp, double? EndingHp);

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
/// 🔒 <b>The fight is predicted locally, from the two persisted rows and the seed the run
/// committed</b> — see <see cref="LocalBattleSimulation"/>, which fights it through the same
/// composition <c>CONFIRM_BATTLE_RESULT</c> recomputes it through. When there is no fight to animate
/// the screen says which of the four reasons it is, by name, and submits nothing.
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

    /// <summary>
    /// 🔴 Deliberately unwritten, and named so it can be found. The content set names a hero's side
    /// and an enemy's side and nothing between them.
    /// </summary>
    private const string APetHasNoCaptionOfItsOwn =
        "The log reserves three slots for pets whether they are filled or not, and a pet that acts " +
        "appears in the roster like anything else. There is no caption for one: this screen's " +
        "strings name the hero's side and the enemy's side, and inventing a third word here would " +
        "put an untranslated literal on screen in front of a German player. A pet is captioned as " +
        "the hero's side with its slot number until a string exists for it.";

    /// <summary>
    /// The slot the log identifies the hero by.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The one transcription of the rules layer's actor roster in this build</b>, which is
    /// internal and has no public restatement: slot 0 is the hero, 1 to 3 are the pet slots —
    /// reserved whether they are filled or not — 4 to 254 are the encounter's enemies in order and
    /// then any summons, and 255 is nobody. It is read here, where a case can check it, and handed on
    /// as a <see cref="ReplaySide"/> so that nothing downstream has to know the numbers again.
    /// </remarks>
    private const byte HeroSlot = 0;

    /// <summary>The first slot the log gives to an enemy — every slot below it is the hero's side.</summary>
    private const byte FirstEnemySlot = 4;

    /// <summary>The slot the log uses for nobody, on the events that belong to no actor.</summary>
    private const byte NoActorSlot = 255;

    /// <summary>Separates one actor's caption from its index, and joins the actors a banner names.</summary>
    private const string CaptionGap = " ";

    /// <summary>Joins the opponents a banner names, in slot order.</summary>
    private const string OpponentJoin = " · ";

    /// <summary>The rounding every accumulated health value is held to, as the rules layer holds it.</summary>
    private const int HealthDecimals = 4;

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

    /// <summary>The fight being animated, or null while there is none to animate.</summary>
    private SimulationResult? _fight;

    /// <summary>How much of the LOG's own time the playhead has consumed, in seconds.</summary>
    /// <remarks>
    /// Log time rather than wall time: every advance folds the speed multiplier in as it arrives, so
    /// a rate change applies to the time that has not happened yet and never rescales what has.
    /// </remarks>
    private double _elapsed;

    /// <summary>
    /// The index of the first log entry the playhead has not crossed yet.
    /// </summary>
    /// <remarks>
    /// 🔒 A cursor rather than a remembered tick. It starts at the very first entry, so the first
    /// step's window is open at the true beginning of the fight: tick zero carries the one event that
    /// says a fight has begun, and a window that started above it would drop that event from every
    /// fight in the game. It is a cursor rather than a query because this is advanced from the
    /// engine's per-frame callback — re-reading an eighteen-hundred-tick log sixty times a second to
    /// find the handful of events one frame crossed is the shape that turns a ninety-second fight
    /// into a collection pause on a handset. Ticks never decrease in a log, so one forward walk over
    /// the whole fight sees every event exactly once.
    /// </remarks>
    private int _logCursor;

    /// <summary>Every phase change the log carries, in order, as the tick and the phase entered.</summary>
    /// <remarks>
    /// 🔒 Lifted out of the log once, for the same reason the cursor exists: the band is settled on
    /// every frame and the log is not something to re-read on every frame. A real fight carries at
    /// most three of these.
    /// </remarks>
    private (int Tick, int Phase)[] _phaseChanges = [];

    /// <summary>The tick of the last phase change the playhead has crossed.</summary>
    private int _lastPhaseChangeTick;

    /// <summary>Whether the result has already been put to the host, however it answered.</summary>
    /// <remarks>
    /// 🔒 Set on the attempt rather than on acceptance. The scene drives this from a per-frame
    /// callback that keeps firing after the fight ends, so a flag set only on success would resubmit
    /// a refused confirmation sixty times a second for as long as the screen is up.
    /// </remarks>
    private bool _confirmationSettled;

    /// <summary>
    /// Whether the blow now being read was announced as a critical one.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A reconstruction of the rules layer's private per-attack emission order, and there is no
    /// other way to colour the number.</b> A critical hit is its own event rather than a flag on the
    /// blow, and it is emitted <em>before</em> the blow it describes: the attack opens the sequence, a
    /// miss ends it, and a crit, a block, a broken ward and the hit itself follow in that order. So
    /// the announcement is held across the events between it and the number it belongs to, and
    /// cleared by the next attack — because an announcement whose blow never landed, fully absorbed
    /// by a ward or dodged, must not colour somebody else's number several ticks later. It is a real
    /// coupling to an order that is not promised to a client, which is exactly why it is kept on this
    /// side of the split, where the cases below hold it.
    /// </remarks>
    private bool _critPending;

    /// <summary>
    /// Where the playhead has walked each actor's health to, by slot.
    /// </summary>
    /// <remarks>
    /// 🔒 Walked here rather than by whatever draws the bars, so that a watched fight and a skipped
    /// one cannot disagree: every step is held to the same four decimals the rules layer holds its own
    /// values to, and a skip puts each actor on the value the fight ended on. Two arithmetics for one
    /// health bar is two answers for the same fight, differing in whichever decimal the player is
    /// least likely to look at and most likely to screenshot.
    /// </remarks>
    private readonly Dictionary<byte, double?> _health = new();

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
    /// 🔒 True even for a refusal that got past the seed — a simulator that threw and an empty log
    /// both report the seed they were derived at, because that is the number whoever reads the bug
    /// report needs to reproduce the fight. Only a run that cannot name its own battle reports none,
    /// and zero is a legal seed, so this flag is the only thing telling the two apart.
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

    /// <summary>
    /// What those events ask the screen to draw, in the order the log put them in.
    /// </summary>
    /// <remarks>
    /// 🔒 The events themselves say what happened; these say what is shown. Everything that reads the
    /// log's own semantics to get from one to the other — that a hit's value is health lost, that a
    /// tick's is signed, that an applied status carries stacks, that a crit announces the blow after
    /// it — happens here and is checked here. Most events ask for nothing at all and produce none of
    /// these, so a step that crossed only a battle's opening is empty.
    /// </remarks>
    public IReadOnlyList<ReplayCue> StepCues { get; private set; } = [];

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
    public bool SkipAvailable => true;

    /// <summary>Why the rules layer refused the result, or null when it did not.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>The screen's heading.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The caption over the hero's health bar.</summary>
    public string HeroLabel => _strings.Resolve(HeroLabelKey);

    /// <summary>The caption over the enemy's health bar.</summary>
    public string EnemyLabel => _strings.Resolve(EnemyLabelKey);

    /// <summary>
    /// The banner over the fight, naming every opponent in it.
    /// </summary>
    /// <remarks>
    /// 🔴 By role and index rather than by name — see <see cref="TheEnemysNameIsNotReachableHere"/>.
    /// A fight whose roster is not settled yet falls back to the side's own caption, so the banner is
    /// never blank.
    /// </remarks>
    public string OpponentLabel
    {
        get
        {
            var named = "";

            foreach (var actor in Actors)
            {
                if (actor.Side != ReplaySide.Enemy)
                {
                    continue;
                }

                named = named.Length == 0
                    ? CaptionOf(actor.ActorId)
                    : named + OpponentJoin + CaptionOf(actor.ActorId);
            }

            return named.Length > 0 ? named : EnemyLabel;
        }
    }

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
    /// <remarks>
    /// By name rather than by number: the log reports a phase as a bare integer and the table giving
    /// that integer a meaning is inside the rules assembly, so a band drawing the number would flash
    /// a digit across the screen at the moment a boss changes what it does.
    /// </remarks>
    public string PhaseBandText =>
        PhaseBandVisible && PhaseNameKeyFor(CurrentBossPhase) is { } key
            ? _strings.Resolve(key)
            : NothingLeftToSay;

    /// <summary>
    /// What the screen says about its own state, resolved.
    /// </summary>
    /// <remarks>
    /// 🔒 The refusal is read before the outcome. A confirmation the rules layer turned down leaves
    /// the run parked in the battle phase for good — the board will not roll and the revive is itself
    /// one of the commands that phase refuses — so a screen printing "you won" over it would look
    /// exactly like one that had worked.
    /// </remarks>
    public string StatusText
    {
        get
        {
            if (RulesRejection is not null)
            {
                return _strings.Resolve(RefusedStatusKey);
            }

            return Readiness switch
            {
                null => _strings.Resolve(LoadingStatusKey),
                BattleReadiness.Ready => FinishedText(),
                BattleReadiness.NoRun => _strings.Resolve(NoRunStatusKey),
                BattleReadiness.PhaseNotBattle => _strings.Resolve(PhaseNotBattleStatusKey),
                BattleReadiness.SeedUnavailable => _strings.Resolve(SeedUnavailableStatusKey),
                BattleReadiness.SimulatorFailed => _strings.Resolve(SimulatorFailedStatusKey),
                BattleReadiness.LogEmpty => _strings.Resolve(LogEmptyStatusKey),
                _ => _strings.Resolve(ReadUnavailableStatusKey),
            };
        }
    }

    /// <summary>Reads the run this screen is about and settles the fight it will animate.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <summary>The four tile kinds that open a fight, as the run reports them.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed, for the reason
    /// <see cref="ShopPresenter.ShopTileKind"/> states at length: the pending tile arrives as a bare
    /// number, the enum that assigns each kind its number is public, and a copied literal would go on
    /// naming whatever moved into its slot.
    /// </para>
    /// <para>
    /// 🔴 <b>They live on THIS screen because a fight is how these tiles are left, and until M7-10z
    /// nothing in the client knew that.</b> <c>RESOLVE_TILE</c> says of every fight tile that it is
    /// *"acknowledged and not cleared"* — so the board's Resolve control submitted a command that
    /// succeeded and changed nothing, and a run standing on an enemy was stuck there for good. The way
    /// out is <c>START_BATTLE</c>, which moves the run to <c>BattlePending</c>, which is the state the
    /// board already opens this screen on.
    /// </para>
    /// <para>
    /// 🔴 <b>A kind added to the fight family upstream and not added here parks the run on it for
    /// good</b>, and the mini-boss is why that is stated rather than left to be rediscovered: it
    /// arrived as a fight kind the rules layer accepts <c>START_BATTLE</c> for while this list still
    /// named three, so the board sent <c>RESOLVE_TILE</c>, the rules layer accepted it, nothing
    /// cleared, and every run stopped on a node it cannot roll past — the M7-10z dead end again, on a
    /// node no run can avoid. The list is not derivable from the enum, so it is the one that has to be
    /// re-read whenever the enum grows.
    /// </para>
    /// </remarks>
    public const int EnemyTileKind = (int)TileKind.Enemy;

    /// <summary>The elite fight's tile kind. Read with the three beside it.</summary>
    public const int EliteTileKind = (int)TileKind.Elite;

    /// <summary>The mini-boss fight's tile kind. Read with the three beside it.</summary>
    public const int MiniBossTileKind = (int)TileKind.MiniBoss;

    /// <summary>The Boss fight's tile kind. Read with the three beside it.</summary>
    public const int BossTileKind = (int)TileKind.Boss;

    /// <summary>Whether a pending tile of this kind is left by fighting it.</summary>
    /// <remarks>
    /// 🔒 Asked rather than restated, so the board holds no fifth copy of the four numbers. The
    /// board already asks <c>ShopPresenter</c> and <c>CampfirePresenter</c> the same way.
    /// </remarks>
    /// <param name="tileKind">The kind the run reports for its pending tile.</param>
    public static bool OpensAFight(int tileKind) =>
        tileKind is EnemyTileKind or EliteTileKind or MiniBossTileKind or BossTileKind;

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception)
        {
            // The readiness is the whole answer this screen has room for. It picks which sentence the
            // player reads; the failure's own identity goes to the log, because an untranslated type
            // name beside a line that was just translated helps nobody reading it.
            Readiness = BattleReadiness.ReadUnavailable;
        }
    }

    /// <summary>
    /// Moves the playhead by one frame's worth of time, and closes the battle once it lands.
    /// </summary>
    /// <param name="deltaSeconds">How much wall time has passed since the last advance.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<BattleSubmission> AdvanceAsync(double deltaSeconds, CancellationToken ct)
    {
        if (_fight is not { } fight)
        {
            return BattleSubmission.NothingToSubmit;
        }

        // The multiplier is folded in as the time arrives, so the setting decides how much of the log
        // the NEXT frame consumes and never restates how much the previous ones did.
        _elapsed += deltaSeconds * (int)Speed;

        MovePlayheadTo((int)Math.Floor(_elapsed * TicksPerSecond), emitting: true);

        return Complete
            ? await ConfirmAsync(fight, ct).ConfigureAwait(false)
            : BattleSubmission.NothingToSubmit;
    }

    /// <summary>
    /// Jumps to the end of the fight and closes the battle, without replaying what was skipped.
    /// </summary>
    /// <remarks>
    /// 🔒 Nothing whatever is submitted when there is no simulated fight. The rules layer checks the
    /// hash's shape and never recomputes it, so a well-formed number invented here would buy a full
    /// kill payout for a battle nobody fought.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    public async Task<BattleSubmission> SkipAsync(CancellationToken ct)
    {
        if (_fight is not { } fight)
        {
            return BattleSubmission.RefusedNotAvailable;
        }

        _elapsed = TotalTicks / (double)TicksPerSecond;

        // Emitting nothing on purpose: the point of skipping is not watching the fight, and handing
        // the scene every event of a ninety-second log in one frame would spray eighteen hundred
        // ticks of floating damage numbers across the screen a player asked to be spared.
        MovePlayheadTo(TotalTicks, emitting: false);
        AnchorHealthToTheEnd();

        return await ConfirmAsync(fight, ct).ConfigureAwait(false);
    }

    /// <summary>Steps the speed setting on, wrapping the fastest back round to real time.</summary>
    /// <remarks>
    /// The playhead is deliberately untouched: this control changes how fast the rest of the fight is
    /// watched, not which part of it is being watched.
    /// </remarks>
    public void CycleSpeed() =>
        Speed = Speed switch
        {
            BattleSpeed.Single => BattleSpeed.Double,
            BattleSpeed.Double => BattleSpeed.Triple,
            _ => BattleSpeed.Single,
        };

    /// <summary>
    /// Names one actor by the side its slot puts it on and which one of that side it is.
    /// </summary>
    /// <remarks>
    /// 🔴 The hero's own caption carries no index — there is only ever one — and a pet borrows the
    /// hero's side, see <see cref="APetHasNoCaptionOfItsOwn"/>. Both halves come out of the content
    /// set, so a caption a player reads is a caption a translator was paid for.
    /// </remarks>
    /// <param name="actorId">The slot the log identifies the actor by.</param>
    public string CaptionOf(byte actorId)
    {
        var side = SideOf(actorId);

        if (side == ReplaySide.Hero)
        {
            return HeroLabel;
        }

        var caption = side == ReplaySide.Enemy ? EnemyLabel : HeroLabel;

        return caption + CaptionGap + SideIndexOf(actorId).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Where the playhead has walked one actor's health to, or null where the log fixes none.
    /// </summary>
    /// <param name="actorId">The slot the log identifies the actor by.</param>
    public double? HealthOf(byte actorId) => _health.GetValueOrDefault(actorId);

    /// <summary>Which side of the stage a slot puts an actor on.</summary>
    private static ReplaySide SideOf(byte actorId) => actorId switch
    {
        HeroSlot => ReplaySide.Hero,
        < FirstEnemySlot => ReplaySide.Pet,
        _ => ReplaySide.Enemy,
    };

    /// <summary>Which one of its own side an actor is — the first of a side is one, the hero is none.</summary>
    private static int SideIndexOf(byte actorId) => SideOf(actorId) switch
    {
        ReplaySide.Hero => 0,
        ReplaySide.Pet => actorId,
        _ => actorId - FirstEnemySlot + 1,
    };

    /// <summary>How many log ticks the phase band stays up for, at the dwell in force.</summary>
    private int PhaseBandTicks =>
        (int)Math.Round((_reducedMotion ? ReducedMotionDwell : PhaseBandDwell).TotalSeconds *
                        TicksPerSecond);

    private string FinishedText()
    {
        if (!Complete || HeroWon is not { } won)
        {
            return NothingLeftToSay;
        }

        return _strings.Resolve(won ? VictoryStatusKey : DefeatStatusKey);
    }

    private static string? PhaseNameKeyFor(int? phase) => phase switch
    {
        1 => PhaseOneNameKey,
        2 => PhaseTwoNameKey,
        3 => PhaseThreeNameKey,
        _ => null,
    };

    /// <remarks>
    /// 🔒 <b>The player's row goes to the prediction as well as the run's, and dropping it is exactly
    /// how the fight went missing.</b> The hero is composed from the profile's loadout, so a
    /// prediction handed the run alone can only fight an invented hero — and for one milestone this
    /// method held both rows in hand and passed one, while the screen reported the hero's stats as
    /// unbuildable.
    /// </remarks>
    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View is not { Run: { } run } view)
        {
            Readiness = BattleReadiness.NoRun;
            return;
        }

        var attempt = _simulations.Simulate(view.Player, run);

        Readiness = attempt.Readiness;
        BattleSeed = attempt.BattleSeed;
        SeedDerived = attempt.SeedDerived;

        if (attempt.Readiness != BattleReadiness.Ready || attempt.Result is not { } fight)
        {
            return;
        }

        _fight = fight;
        HeroWon = fight.HeroWon;
        TotalTicks = fight.DurationTicks;
        Actors = ActorsIn(fight);
        _phaseChanges = PhaseChangesIn(fight);

        foreach (var actor in Actors)
        {
            _health[actor.ActorId] = actor.StartingHp;
        }
    }

    /// <summary>Puts the playhead on a tick, clamped to the fight, and settles what that shows.</summary>
    /// <param name="reachedTick">Where the playhead would land.</param>
    /// <param name="emitting">Whether the events crossed are handed on to be drawn.</param>
    private void MovePlayheadTo(int reachedTick, bool emitting)
    {
        CurrentTick = Math.Min(TotalTicks, reachedTick);

        ConsumeThrough(CurrentTick, emitting);
        SettlePhaseBand();
    }

    /// <remarks>
    /// Half-open. An event sitting exactly on the previous playhead was drawn by the advance that
    /// reached it, and one sitting exactly on the new playhead has just happened. The cursor is
    /// walked whether the step is drawn or not, so what a skip jumped over is consumed rather than
    /// left for the next advance to spray across the screen.
    /// </remarks>
    private void ConsumeThrough(int throughTick, bool emitting)
    {
        StepEvents = [];
        StepCues = [];

        if (_fight is not { } fight)
        {
            return;
        }

        var log = fight.Log;
        var from = _logCursor;

        while (_logCursor < log.Count && log[_logCursor].Tick <= throughTick)
        {
            _logCursor++;
        }

        if (!emitting || _logCursor == from)
        {
            return;
        }

        var crossed = new CombatEvent[_logCursor - from];
        var drawn = 0;

        for (var index = 0; index < crossed.Length; index++)
        {
            crossed[index] = log[from + index];

            if (Draws(crossed[index].Type))
            {
                drawn++;
            }
        }

        StepEvents = crossed;
        StepCues = CuesFor(crossed, drawn);
    }

    /// <summary>
    /// Reads a step's events into what they ask the screen to draw.
    /// </summary>
    /// <remarks>
    /// 🔒 Sized by <see cref="Draws"/> and filled by <see cref="CueFor"/>, which is why the two are
    /// written next to each other: one array, exactly as long as the number of events that ask for
    /// anything, on the frames that crossed one. Every other event still walks past here, because the
    /// attack that opens a sequence draws nothing and yet is the thing that clears a stale critical
    /// announcement.
    /// </remarks>
    private IReadOnlyList<ReplayCue> CuesFor(CombatEvent[] crossed, int drawn)
    {
        if (drawn == 0)
        {
            foreach (var entry in crossed)
            {
                Opened(entry);
            }

            return [];
        }

        var cues = new ReplayCue[drawn];
        var next = 0;

        foreach (var entry in crossed)
        {
            if (Draws(entry.Type))
            {
                cues[next++] = CueFor(entry);
            }
            else
            {
                Opened(entry);
            }
        }

        return cues;
    }

    /// <summary>Which events ask the screen to draw something at all.</summary>
    private static bool Draws(CombatEventType type) => type switch
    {
        CombatEventType.Crit => true,
        CombatEventType.Hit => true,
        CombatEventType.Heal => true,
        CombatEventType.StatusTick => true,
        CombatEventType.StatusApplied => true,
        CombatEventType.StatusExpired => true,
        CombatEventType.PetAbility => true,
        CombatEventType.ActorDeath => true,
        _ => false,
    };

    /// <summary>And what one of them asks for.</summary>
    private ReplayCue CueFor(CombatEvent entry) => entry.Type switch
    {
        CombatEventType.Crit => Announced(entry),
        CombatEventType.Hit => Struck(entry),
        CombatEventType.Heal => Mended(entry),
        CombatEventType.StatusTick => Ticked(entry),
        CombatEventType.StatusApplied => Stacked(entry, (int)Math.Round(entry.Value)),
        CombatEventType.StatusExpired => Stacked(entry, 0),
        CombatEventType.PetAbility => Acted(entry),
        _ => Slain(entry),
    };

    /// <remarks>
    /// 🔒 The one thing an event that draws nothing still does: an attack opens a new sequence, so an
    /// announcement left over from the previous one — a critical blow a ward swallowed whole, or one
    /// that missed — dies here rather than colouring the next number gold.
    /// </remarks>
    private void Opened(CombatEvent entry)
    {
        if (entry.Type == CombatEventType.Attack)
        {
            _critPending = false;
        }
    }

    private ReplayCue Announced(CombatEvent entry)
    {
        _critPending = true;

        return Cue(entry.TargetId, burst: ReplayBurst.CritPop);
    }

    /// <remarks>The value is the health actually lost, after whatever a ward absorbed.</remarks>
    private ReplayCue Struck(CombatEvent entry)
    {
        var critical = _critPending;

        _critPending = false;

        return Cue(
            entry.TargetId,
            floater: critical ? ReplayFloater.Crit : ReplayFloater.Hit,
            amount: entry.Value,
            burst: ReplayBurst.HitSpark,
            health: Move(entry.TargetId, -entry.Value));
    }

    /// <remarks>The value is what was actually restored, with any overheal already excluded.</remarks>
    private ReplayCue Mended(CombatEvent entry) =>
        Cue(
            entry.TargetId,
            floater: ReplayFloater.Heal,
            amount: entry.Value,
            health: Move(entry.TargetId, entry.Value));

    /// <remarks>
    /// The value is signed — a tick that heals is still a tick — and the number drawn is its size.
    /// </remarks>
    private ReplayCue Ticked(CombatEvent entry) =>
        Cue(
            entry.TargetId,
            floater: ReplayFloater.DamageOverTime,
            amount: Math.Abs(entry.Value),
            health: Move(entry.TargetId, entry.Value));

    /// <remarks>An applied status carries a stack count rather than a potency.</remarks>
    private ReplayCue Stacked(CombatEvent entry, int stacks) =>
        Cue(entry.TargetId, statusId: entry.DataId, stacks: stacks);

    /// <remarks>A pet's ability is drawn over the pet, which is the event's source rather than its target.</remarks>
    private ReplayCue Acted(CombatEvent entry) =>
        Cue(entry.SourceId, burst: ReplayBurst.HitSpark);

    /// <remarks>The event's target is the actor that died; its source is whatever killed it.</remarks>
    private ReplayCue Slain(CombatEvent entry) =>
        Cue(entry.TargetId, burst: ReplayBurst.DeathPuff, health: Fell(entry.TargetId), died: true);

    /// <summary>Takes an actor the log records a death for down to nothing.</summary>
    /// <remarks>
    /// Set outright rather than subtracted to. A death is the one thing that fixes an actor's final
    /// health, and a walk that had drifted a fraction above zero would leave a sliver of bar standing
    /// under an actor the log has just killed.
    /// </remarks>
    private double? Fell(byte slot)
    {
        if (_health.GetValueOrDefault(slot) is null)
        {
            return null;
        }

        _health[slot] = 0;

        return 0;
    }

    private ReplayCue Cue(
        byte actorId,
        ReplayFloater floater = ReplayFloater.None,
        double amount = 0,
        ReplayBurst burst = ReplayBurst.None,
        double? health = null,
        bool died = false,
        ushort? statusId = null,
        int stacks = 0) =>
        new(actorId, SideOf(actorId), floater, amount, burst, health, died, statusId, stacks);

    /// <summary>
    /// Moves one actor's health, held to the rounding the rules layer holds its own values to.
    /// </summary>
    /// <remarks>
    /// 🔒 Rounded at every step rather than at the end. Health is walked one blow at a time over a
    /// fight that can carry hundreds of them, and an unrounded walk drifts away from the value the
    /// same fight ends on when it is skipped instead of watched — two numbers for one battle,
    /// differing in the last decimal, which is the decimal a screenshot of a boss kill is read on.
    /// </remarks>
    private double? Move(byte slot, double delta)
    {
        if (_health.GetValueOrDefault(slot) is not { } current)
        {
            return null;
        }

        var moved = Math.Round(Math.Max(0, current + delta), HealthDecimals);

        _health[slot] = moved;

        return moved;
    }

    /// <remarks>
    /// 🔒 What a skip owes the bars. Nothing of the fight was drawn, so the health cannot be walked
    /// there — it is put on the value the fight ENDED on, which is the same value the walk lands on
    /// and the whole reason both are held to the same rounding.
    /// </remarks>
    private void AnchorHealthToTheEnd()
    {
        foreach (var actor in Actors)
        {
            if (actor.EndingHp is { } ending)
            {
                _health[actor.ActorId] = Math.Round(ending, HealthDecimals);
            }
        }
    }

    /// <remarks>
    /// 🔒 The phase is read off the event's own value rather than counted from the events crossed.
    /// The two agree in every log the simulator emits today, because a boss enters its first phase on
    /// tick zero and only ever walks upward — but that is the rules layer's private emission order
    /// and not a promise made to a client, and a screen that counted would announce the wrong
    /// transition the moment it changed.
    /// </remarks>
    private static (int Tick, int Phase)[] PhaseChangesIn(SimulationResult fight)
    {
        var changes = new List<(int Tick, int Phase)>();

        foreach (var entry in fight.Log)
        {
            if (entry.Type == CombatEventType.PhaseChange)
            {
                changes.Add((entry.Tick, (int)Math.Round(entry.Value)));
            }
        }

        return [.. changes];
    }

    private void SettlePhaseBand()
    {
        int? entered = null;

        foreach (var (tick, phase) in _phaseChanges)
        {
            if (tick <= CurrentTick)
            {
                entered = phase;
                _lastPhaseChangeTick = tick;
            }
        }

        CurrentBossPhase = entered;
        PhaseBandVisible = entered is not null && CurrentTick - _lastPhaseChangeTick < PhaseBandTicks;
    }

    private async Task<BattleSubmission> ConfirmAsync(SimulationResult fight, CancellationToken ct)
    {
        if (_confirmationSettled)
        {
            return BattleSubmission.NothingToSubmit;
        }

        _confirmationSettled = true;

        // Bare decimal digits, because the handler parses with no number styles permitted at all: a
        // hexadecimal form, a prefix, a sign or a separator is refused as a malformed command, and a
        // refused confirmation leaves the run stuck in the battle phase for good.
        var confirmation = new ConfirmBattleResultCommand(
            fight.LogHash.ToString(CultureInfo.InvariantCulture), fight.HeroWon);

        var outcome = await _gameHost
            .SubmitAsync(_player, _run, confirmation, ct)
            .ConfigureAwait(false);

        RulesRejection = outcome.Rejection;

        return outcome.Accepted ? BattleSubmission.Submitted : BattleSubmission.RefusedByRules;
    }

    /// <remarks>
    /// 🔒 One pass over the log settles all four numbers: which actors are in the fight, what each was
    /// spawned with, how far its health moved in total, and which of them died. See
    /// <see cref="ReplayActor"/> for which of them each end of a bar is taken from.
    /// </remarks>
    private static IReadOnlyList<ReplayActor> ActorsIn(SimulationResult fight)
    {
        var mentioned = new SortedSet<byte>();
        var spawned = new Dictionary<byte, double>();
        var moved = new Dictionary<byte, double>();
        var slain = new HashSet<byte>();

        foreach (var entry in fight.Log)
        {
            Mention(mentioned, entry.SourceId);
            Mention(mentioned, entry.TargetId);

            if (entry.TargetId == NoActorSlot)
            {
                continue;
            }

            switch (entry.Type)
            {
                case CombatEventType.ActorSpawned:

                    // Assigned rather than accumulated, and first-writer-wins: ids are never reused,
                    // so a second spawn for one slot is a malformed log and taking the later one would
                    // silently redraw a bar mid-fight against a denominator the earlier events were
                    // not measured on.
                    spawned.TryAdd(entry.TargetId, entry.Value);
                    break;

                case CombatEventType.Hit:
                    Accumulate(moved, entry.TargetId, -entry.Value);
                    break;

                case CombatEventType.Heal:
                    Accumulate(moved, entry.TargetId, entry.Value);
                    break;

                case CombatEventType.StatusTick:
                    Accumulate(moved, entry.TargetId, entry.Value);
                    break;

                case CombatEventType.ActorDeath:
                    slain.Add(entry.TargetId);
                    break;
            }
        }

        return
        [
            .. mentioned.Select(slot => Bar(
                slot,
                spawned.TryGetValue(slot, out var maxHp) ? maxHp : null,
                EndingHpOf(slot, fight, slain),
                moved.GetValueOrDefault(slot)))
        ];
    }

    /// <summary>One actor's bar: its denominator, and both ends the log fixes.</summary>
    /// <remarks>
    /// 🔒 The starting value is derived by running the walk backwards, and it accounts for exactly what
    /// the walk accounts for — every blow, every heal and every tick of something already on the actor.
    /// A derivation that counted one fewer kind of event than the walk applies would put the bar's own
    /// beginning out of reach of its end, so a fight watched to the finish would stop somewhere other
    /// than the health the result reports. The spawned maximum is the fallback rather than the first
    /// answer for that reason: it is right about every actor that opens full and wrong about the one
    /// that does not.
    /// </remarks>
    private static ReplayActor Bar(byte slot, double? maxHp, double? anchoredEndingHp, double moved)
    {
        var startingHp = anchoredEndingHp is { } anchored
            ? Math.Round(anchored - moved, HealthDecimals)
            : maxHp;

        // Both ends satisfy `end = start + moved` on either arm, which is what lets a skip and a
        // watched fight land on the same number — see AnchorHealthToTheEnd.
        var endingHp = anchoredEndingHp ??
            (startingHp is { } start ? Math.Round(start + moved, HealthDecimals) : null);

        return new ReplayActor(slot, SideOf(slot), SideIndexOf(slot), maxHp, startingHp, endingHp);
    }

    /// <summary>
    /// What an actor finished on where the log states it outright, rather than leaving it to the walk.
    /// </summary>
    /// <remarks>
    /// 🔒 Two actors are anchored and the rest are not, and the difference is which end of the bar is
    /// the known one. The hero's finish is reported by the result and anything the log records a death
    /// for finished at zero — so for those two the START is the derived end. Everything else opens on
    /// the maximum it was spawned with, so the FINISH is the derived one. A survivor's finish used to be
    /// null and a skip left its bar wherever it opened; it is derived here because the spawn event made
    /// the other end knowable.
    /// </remarks>
    private static double? EndingHpOf(byte slot, SimulationResult fight, HashSet<byte> slain)
    {
        if (slot == HeroSlot)
        {
            return fight.HeroHpRemaining;
        }

        return slain.Contains(slot) ? 0 : null;
    }

    private static void Mention(SortedSet<byte> mentioned, byte slot)
    {
        if (slot != NoActorSlot)
        {
            mentioned.Add(slot);
        }
    }

    /// <remarks>Rounded at each accumulation, the way the rules layer rounds its own values.</remarks>
    private static void Accumulate(Dictionary<byte, double> totals, byte slot, double amount) =>
        totals[slot] = Math.Round(totals.GetValueOrDefault(slot) + amount, HealthDecimals);
}
