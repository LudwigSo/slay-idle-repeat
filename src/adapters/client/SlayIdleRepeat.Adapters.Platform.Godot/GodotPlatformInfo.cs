namespace SlayIdleRepeat.Adapters.Platform.Godot;

/// <summary>
/// Reads the locale and the device the game is running on out of the engine.
/// </summary>
/// <remarks>
/// ⚠️ Implements no port, for the reason <see cref="GodotUserPaths"/> records — and this is the
/// one class in the project whose port now exists. M7-01b declared
/// <c>IPlatformInfoPort</c> over a BCL reader (<c>Adapters.Platform.Host</c>) and the in-memory
/// fake, and deliberately did not conform this class to it: a fixture over these members would be
/// a fatal fault, not a test. Conforming it is a rename plus one translation —
/// <see cref="Locale"/> becomes a <c>CultureInfo</c> and an unidentified
/// <see cref="DeviceModel"/> becomes <see langword="null"/> — waiting only on somewhere that can
/// run it.
/// </remarks>
public sealed class GodotPlatformInfo
{
    /// <summary>Separates the engine's trailing keyword list from the locale proper.</summary>
    private const char ExtraKeywordSeparator = '@';

    /// <summary>The player's locale, as a BCP-47 tag.</summary>
    /// <remarks>
    /// The engine answers in its own shape — <c>language_Script_COUNTRY_VARIANT@extra</c> —
    /// which is the same information under different punctuation for everything up to the
    /// variant. ⚠️ The trailing keyword list has no BCP-47 counterpart to translate into, so
    /// it is dropped rather than guessed at; nothing in the game reads currency or calendar
    /// preferences from the host today.
    /// </remarks>
    public string Locale
    {
        get
        {
            var engineLocale = global::Godot.OS.GetLocale();
            var extra = engineLocale.IndexOf(ExtraKeywordSeparator);

            return (extra < 0 ? engineLocale : engineLocale[..extra]).Replace('_', '-');
        }
    }

    /// <summary>The device model, for crash grouping and support.</summary>
    /// <remarks>
    /// ⚠️ Passed through exactly as the engine gives it, including the literal
    /// <c>GenericDevice</c> it answers on every platform it cannot identify — which is every
    /// platform except Android, iOS, macOS and Windows. That string is the engine stating an
    /// absence, and a caller must read it as one; substituting something friendlier here
    /// would turn "we do not know" into a device model that does not exist.
    /// </remarks>
    public string DeviceModel => global::Godot.OS.GetModelName();

    /// <summary>The host platform's name.</summary>
    /// <remarks>
    /// The engine's own spelling — <c>Android</c>, <c>iOS</c>, <c>Windows</c>, <c>Linux</c>,
    /// <c>macOS</c>, <c>Web</c> — kept verbatim so it can be matched against the engine's
    /// documentation rather than against a mapping table that has to be maintained here.
    /// </remarks>
    public string PlatformName => global::Godot.OS.GetName();
}
