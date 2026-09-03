namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How long each body motion takes and how far it goes. Handed in by the scene; nothing here defaults them.</summary>
/// <param name="SwingSeconds">A swing's whole duration; it peaks at the midpoint and returns.</param>
/// <param name="SwingReach">How far a swing advances toward its counterpart at its peak.</param>
/// <param name="RecoilSeconds">A recoil's whole duration; it peaks at the midpoint and returns.</param>
/// <param name="RecoilDistance">How far a recoil moves away from its striker at its peak.</param>
/// <param name="DodgeSeconds">A dodge's whole duration; it peaks at the midpoint and returns.</param>
/// <param name="DodgeSideStep">How far a dodge steps aside at its peak.</param>
/// <param name="BraceSeconds">A brace's whole duration; it peaks at the midpoint and returns.</param>
/// <param name="BraceSquash">How much a brace squashes at its peak, as a fraction of height.</param>
/// <param name="FallSeconds">How long a fall takes to land.</param>
/// <param name="FallTipDegrees">How far a fallen actor has tipped over once it has landed.</param>
/// <param name="FallSink">How far a fallen actor has sunk once it has landed.</param>
/// <param name="EnterSeconds">How long an entering actor takes to grow to full size.</param>
/// <param name="PoseHoldSeconds">How long a wind-up pulses when its cue states no seconds.</param>
public sealed record BattleMotionTimings(
    double SwingSeconds,
    double SwingReach,
    double RecoilSeconds,
    double RecoilDistance,
    double DodgeSeconds,
    double DodgeSideStep,
    double BraceSeconds,
    double BraceSquash,
    double FallSeconds,
    double FallTipDegrees,
    double FallSink,
    double EnterSeconds,
    double PoseHoldSeconds);

/// <summary>How one actor is drawn this frame, relative to where the layout stands it.</summary>
/// <param name="Advance">Displacement toward <paramref name="Toward"/>; negative is away from it.</param>
/// <param name="Toward">The actor the advance is measured toward, or null when the actor is not displaced.</param>
/// <param name="SideStep">Lateral displacement, across the line to the counterpart.</param>
/// <param name="Squash">How much the actor is squashed, as a fraction of its height.</param>
/// <param name="Lift">How far the actor is raised off the floor.</param>
/// <param name="TipDegrees">How far the actor has tipped over.</param>
/// <param name="Sink">How far the actor has sunk into the floor.</param>
/// <param name="Scale">The actor's size; 1 at rest.</param>
/// <param name="Pulse">How strongly the actor's wind-up glows, 0 to 1.</param>
/// <param name="Fallen">Whether the actor has gone down. Terminal: no later motion moves it.</param>
/// <param name="Present">Whether the actor is on the stage at all.</param>
public readonly record struct ActorPose(
    double Advance,
    byte? Toward,
    double SideStep,
    double Squash,
    double Lift,
    double TipDegrees,
    double Sink,
    double Scale,
    double Pulse,
    bool Fallen,
    bool Present);

/// <summary>
/// The playhead over every actor's body motion: which motion each is in, how far through it is, and
/// the pose that follows. Engine-free, like <see cref="BoardWalk"/>, and for the same reason.
/// </summary>
/// <remarks>
/// A later motion pre-empts one still in flight on the same actor. A fall is terminal. Under reduced
/// motion every motion completes on its first advance, and what a motion leaves behind — an actor
/// fallen, an actor present — is kept.
/// </remarks>
public sealed class BattleChoreography
{
    private static readonly ActorPose Rest = new(
        Advance: 0d, Toward: null, SideStep: 0d, Squash: 0d, Lift: 0d, TipDegrees: 0d, Sink: 0d,
        Scale: 1d, Pulse: 0d, Fallen: false, Present: true);

    private readonly BattleMotionTimings _timings;
    private readonly bool _reducedMotion;
    private readonly Dictionary<byte, ActorState> _actors = new();

    /// <summary>Creates a playhead.</summary>
    /// <param name="timings">How long each motion takes and how far it goes.</param>
    /// <param name="reducedMotion">When true, every motion completes on its first advance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timings"/> is null.</exception>
    public BattleChoreography(BattleMotionTimings timings, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(timings);

        _timings = timings;
        _reducedMotion = reducedMotion;
    }

    /// <summary>Whether any motion is still in flight.</summary>
    public bool InProgress
    {
        get
        {
            foreach (var actor in _actors.Values)
            {
                if (actor.Motion != ReplayMotion.None)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Starts the motion a cue asks for, if it asks for one. A cue with no motion changes nothing.</summary>
    public void Play(ReplayCue cue)
    {
        if (cue.Motion == ReplayMotion.None)
        {
            return;
        }

        var actor = StateOf(cue.ActorId);

        if (actor.Fallen)
        {
            return;
        }

        actor.Motion = cue.Motion;
        actor.Counterpart = cue.CounterpartId;
        actor.Elapsed = 0d;
        actor.Seconds = SecondsOf(cue.Motion, cue.MotionSeconds);
        actor.Fallen = cue.Motion == ReplayMotion.Fall;
        actor.Present |= cue.Motion == ReplayMotion.Enter;
    }

    /// <summary>Moves every motion in flight on. A negative or non-finite delta moves nothing.</summary>
    /// <param name="deltaSeconds">Seconds since the last advance, already scaled by the replay speed.</param>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return;
        }

        foreach (var actor in _actors.Values)
        {
            if (actor.Motion == ReplayMotion.None)
            {
                continue;
            }

            actor.Elapsed = _reducedMotion ? actor.Seconds : actor.Elapsed + deltaSeconds;

            if (actor.Elapsed >= actor.Seconds)
            {
                actor.Motion = ReplayMotion.None;
            }
        }
    }

    /// <summary>How an actor is drawn now. An actor nothing has moved stands at rest.</summary>
    public ActorPose PoseOf(byte actorId)
    {
        if (!_actors.TryGetValue(actorId, out var actor))
        {
            return Rest;
        }

        if (actor.Fallen)
        {
            var landed = actor.Motion == ReplayMotion.Fall ? actor.Progress : 1d;

            return Rest with
            {
                TipDegrees = landed * _timings.FallTipDegrees,
                Sink = landed * _timings.FallSink,
                Fallen = true,
            };
        }

        return PoseInFlight(actor) with { Present = actor.Present };
    }

    /// <summary>Lays an actor down at once, in the pose a fall ends in, with nothing in flight.</summary>
    public void SnapFallen(byte actorId)
    {
        var actor = StateOf(actorId);

        actor.Motion = ReplayMotion.None;
        actor.Fallen = true;
    }

    /// <summary>Takes an actor off the stage until an Enter brings it on.</summary>
    public void SnapAbsent(byte actorId)
    {
        var actor = StateOf(actorId);

        actor.Motion = ReplayMotion.None;
        actor.Present = false;
    }

    /// <summary>
    /// Puts an actor on the stage at full size at once, with no grow-in. An actor already there is
    /// left as it is, in flight or fallen alike.
    /// </summary>
    public void SnapPresent(byte actorId)
    {
        var actor = StateOf(actorId);

        if (actor.Present)
        {
            return;
        }

        actor.Motion = ReplayMotion.None;
        actor.Present = true;
    }

    private ActorPose PoseInFlight(ActorState actor)
    {
        var progress = actor.Progress;

        return actor.Motion switch
        {
            ReplayMotion.Swing => Rest with
            {
                Advance = _timings.SwingReach * PeakAtMidpoint(progress),
                Toward = actor.Counterpart,
            },
            ReplayMotion.Recoil => Rest with
            {
                Advance = -_timings.RecoilDistance * PeakAtMidpoint(progress),
                Toward = actor.Counterpart,
            },
            ReplayMotion.Dodge => Rest with
            {
                SideStep = _timings.DodgeSideStep * PeakAtMidpoint(progress),
                Toward = actor.Counterpart,
            },
            ReplayMotion.Brace => Rest with { Squash = _timings.BraceSquash * PeakAtMidpoint(progress) },
            ReplayMotion.WindUp => Rest with { Pulse = PeakAtMidpoint(progress) },
            ReplayMotion.Enter => Rest with { Scale = progress },
            _ => Rest,
        };
    }

    private double SecondsOf(ReplayMotion motion, double cueSeconds) => motion switch
    {
        ReplayMotion.Swing => _timings.SwingSeconds,
        ReplayMotion.Recoil => _timings.RecoilSeconds,
        ReplayMotion.Dodge => _timings.DodgeSeconds,
        ReplayMotion.Brace => _timings.BraceSeconds,
        ReplayMotion.WindUp => cueSeconds > 0d ? cueSeconds : _timings.PoseHoldSeconds,
        ReplayMotion.Fall => _timings.FallSeconds,
        ReplayMotion.Enter => _timings.EnterSeconds,
        _ => 0d,
    };

    private ActorState StateOf(byte actorId)
    {
        if (!_actors.TryGetValue(actorId, out var actor))
        {
            actor = new ActorState();
            _actors[actorId] = actor;
        }

        return actor;
    }

    /// <summary>A half sine: 0 at the start, 1 at the midpoint, 0 again at the end.</summary>
    private static double PeakAtMidpoint(double progress) => Math.Sin(Math.PI * progress);

    private sealed class ActorState
    {
        public ReplayMotion Motion { get; set; }

        public byte Counterpart { get; set; }

        public double Elapsed { get; set; }

        public double Seconds { get; set; }

        public bool Fallen { get; set; }

        public bool Present { get; set; } = true;

        public double Progress => Seconds > 0d ? Math.Clamp(Elapsed / Seconds, 0d, 1d) : 1d;
    }
}
