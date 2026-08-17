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
    /// <remarks>
    /// The amplitude is left at the engine's default rather than passed, because the device
    /// default is the only strength this game has an opinion about. ⚠️ On Android the motor
    /// stays silent without the <c>VIBRATE</c> permission in the export preset, which is the
    /// export task's to grant; the call succeeds either way, so a silent handset is not
    /// distinguishable from a switched-off one here.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationMilliseconds"/> is negative.</exception>
    public void Vibrate(int durationMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(durationMilliseconds);

        global::Godot.Input.VibrateHandheld(durationMilliseconds);
    }
}
