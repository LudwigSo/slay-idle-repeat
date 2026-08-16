namespace SlayIdleRepeat.Adapters.Platform.Godot;

/// <summary>
/// Handheld vibration, through the engine's input layer.
/// </summary>
/// <remarks>
/// ⚠️ Implements no port, for the reason <see cref="GodotUserPaths"/> records. A desktop or
/// headless host has no handheld to buzz, so every call is a no-op there rather than a
/// failure — a missing motor is not an error condition.
/// </remarks>
public sealed class GodotHaptics
{
    /// <summary>Vibrates the handheld for the given duration, or does nothing where there is none.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationMilliseconds"/> is negative.</exception>
    public void Vibrate(int durationMilliseconds) => throw new NotImplementedException();
}
