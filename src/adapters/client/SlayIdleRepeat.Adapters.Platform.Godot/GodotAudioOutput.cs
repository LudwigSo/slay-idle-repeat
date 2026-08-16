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
    private readonly global::Godot.Node _audioSceneRoot;

    /// <summary>
    /// Takes the node new players are parented under, so their lifetime is the scene's.
    /// </summary>
    /// <remarks>
    /// 🔒 Fully qualified through <c>global::</c> on purpose: this adapter's own namespace
    /// ends in <c>Godot</c>, so a bare <c>Godot.Node</c> binds to the adapter's namespace and
    /// does not compile.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="audioSceneRoot"/> is null.</exception>
    public GodotAudioOutput(global::Godot.Node audioSceneRoot)
    {
        ArgumentNullException.ThrowIfNull(audioSceneRoot);

        _audioSceneRoot = audioSceneRoot;
    }

    /// <summary>Plays a sound once on the named bus.</summary>
    /// <remarks>
    /// One player per shot, freed by its own finish signal. Overlapping sounds are the normal
    /// case in this game — two dice landing, a hit and a coin pickup in the same frame — and a
    /// single reused player would cut each one off at the next.
    /// </remarks>
    /// <exception cref="ArgumentException">Either argument is null, empty or whitespace, or no bus is named that.</exception>
    /// <exception cref="FileNotFoundException">The engine's filesystem holds no resource at that path.</exception>
    /// <exception cref="InvalidDataException">A resource is there, and the engine could not load it.</exception>
    public void PlayOneShot(string streamResourcePath, string busName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamResourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(busName);

        // Both looked up before anything is constructed. The engine's own behaviour for each is to
        // carry on quietly — an unknown bus falls back to Master, a missing resource yields a player
        // with no stream — and a sound that silently plays on the wrong bus at the wrong volume is
        // the kind of defect that is only ever found by ear, months later.
        var bus = RequireBusIndex(busName);

        if (!global::Godot.ResourceLoader.Exists(streamResourcePath))
        {
            throw new FileNotFoundException(
                $"No audio resource at '{streamResourcePath}'. The engine would hand back a player with no " +
                "stream and play nothing at all, so the path is checked here while it can still say which " +
                "path was wrong.",
                streamResourcePath);
        }

        // The stream is loaded and checked BEFORE a player exists, because the player's only route
        // out of the scene is its own Finished signal: a player with no stream never starts, so it
        // never finishes, so nothing ever frees it. Loading can still fail after Exists said yes —
        // an unreadable import sidecar in an exported build is the usual way — and one leaked node
        // per attempted sound is a leak that grows with play time.
        var stream = global::Godot.GD.Load<global::Godot.AudioStream>(streamResourcePath);

        if (stream is null)
        {
            throw new InvalidDataException(
                $"The engine found a resource at '{streamResourcePath}' and could not load it as audio. " +
                "Playing it anyway would leave a silent player in the scene that never finishes and is " +
                "therefore never freed, so the failure is raised here where it still names the path.");
        }

        var player = new global::Godot.AudioStreamPlayer
        {
            Stream = stream,
            Bus = global::Godot.AudioServer.GetBusName(bus),
        };

        player.Finished += player.QueueFree;

        _audioSceneRoot.AddChild(player);
        player.Play();
    }

    /// <summary>Sets a bus's volume in decibels.</summary>
    /// <exception cref="ArgumentException"><paramref name="busName"/> is null, empty or whitespace, or no bus is named that.</exception>
    public void SetBusVolumeDecibels(string busName, float decibels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(busName);

        global::Godot.AudioServer.SetBusVolumeDb(RequireBusIndex(busName), decibels);
    }

    /// <summary>The index of a bus in the current layout, or a failure naming the bus that is missing.</summary>
    private static int RequireBusIndex(string busName)
    {
        var index = global::Godot.AudioServer.GetBusIndex(busName);

        return index >= 0
            ? index
            : throw new ArgumentException(
                $"The audio layout has no bus named '{busName}'. The engine answers an unknown bus with the " +
                "Master bus rather than an error, which would route the sound correctly enough to pass " +
                "unnoticed and mute nothing when the player turns that bus down.",
                nameof(busName));
    }
}
