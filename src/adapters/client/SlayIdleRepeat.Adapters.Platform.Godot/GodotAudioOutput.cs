namespace SlayIdleRepeat.Adapters.Platform.Godot;

/// <summary>
/// Plays sound and drives the mixer buses, through the engine's audio server.
/// </summary>
/// <remarks>
/// ⚠️ Implements no port, for the reason <see cref="GodotUserPaths"/> records. The deferred
/// audio port needs a sound vocabulary, a music vocabulary and a bus vocabulary that the
/// audio milestone owns; inventing all three here to satisfy a port declaration would be
/// deciding that milestone's content from outside it.
/// </remarks>
public sealed class GodotAudioOutput
{
    /// <summary>
    /// Takes the node new players are parented under, so their lifetime is the scene's.
    /// </summary>
    /// <remarks>
    /// 🔒 Fully qualified through <c>global::</c> on purpose: this adapter's own namespace
    /// ends in <c>Godot</c>, so a bare <c>Godot.Node</c> binds to the adapter's namespace and
    /// does not compile.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="audioSceneRoot"/> is null.</exception>
    public GodotAudioOutput(global::Godot.Node audioSceneRoot) => throw new NotImplementedException();

    /// <summary>Plays a sound once on the named bus.</summary>
    /// <exception cref="ArgumentException">Either argument is null, empty or whitespace.</exception>
    public void PlayOneShot(string streamResourcePath, string busName) => throw new NotImplementedException();

    /// <summary>Sets a bus's volume in decibels.</summary>
    /// <exception cref="ArgumentException"><paramref name="busName"/> is null, empty or whitespace.</exception>
    public void SetBusVolumeDecibels(string busName, float decibels) => throw new NotImplementedException();
}
