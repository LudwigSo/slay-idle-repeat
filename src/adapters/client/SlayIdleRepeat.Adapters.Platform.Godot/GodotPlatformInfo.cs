namespace SlayIdleRepeat.Adapters.Platform.Godot;

/// <summary>
/// Reads the locale and the device the game is running on out of the engine.
/// </summary>
/// <remarks>
/// ⚠️ Implements no port, for the reason <see cref="GodotUserPaths"/> records. Locale and
/// device model are two members of one deferred platform-info port rather than two ports,
/// and they are grouped here the same way so the eventual declaration is a rename.
/// </remarks>
public sealed class GodotPlatformInfo
{
    /// <summary>The player's locale, as a BCP-47 tag.</summary>
    public string Locale => throw new NotImplementedException();

    /// <summary>The device model, for crash grouping and support.</summary>
    public string DeviceModel => throw new NotImplementedException();

    /// <summary>The host platform's name.</summary>
    public string PlatformName => throw new NotImplementedException();
}
