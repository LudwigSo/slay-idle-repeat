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
/// <param name="ReducedMotionSeconds">The scene's own duration for what it still animates under reduced motion.</param>
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
    double PoseHoldSeconds,
    double ReducedMotionSeconds);

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
    /// <summary>Creates a playhead.</summary>
    /// <param name="timings">How long each motion takes and how far it goes.</param>
    /// <param name="reducedMotion">When true, every motion completes on its first advance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timings"/> is null.</exception>
    public BattleChoreography(BattleMotionTimings timings, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(timings);
        _ = reducedMotion;
    }

    /// <summary>Whether any motion is still in flight.</summary>
    public bool InProgress => false;

    /// <summary>Starts the motion a cue asks for, if it asks for one. A cue with no motion changes nothing.</summary>
    public void Play(ReplayCue cue) => _ = cue;

    /// <summary>Moves every motion in flight on. A negative or non-finite delta moves nothing.</summary>
    /// <param name="deltaSeconds">Seconds since the last advance, already scaled by the replay speed.</param>
    public void Advance(double deltaSeconds) => _ = deltaSeconds;

    /// <summary>How an actor is drawn now. An actor nothing has moved stands at rest.</summary>
    public ActorPose PoseOf(byte actorId)
    {
        _ = actorId;

        return default;
    }

    /// <summary>Lays an actor down at once, in the pose a fall ends in, with nothing in flight.</summary>
    public void SnapFallen(byte actorId) => _ = actorId;

    /// <summary>Takes an actor off the stage until an Enter brings it on.</summary>
    public void SnapAbsent(byte actorId) => _ = actorId;
}
