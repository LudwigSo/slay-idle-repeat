using SlayIdleRepeat.Application.Services;

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
    /// <summary>
    /// The literal the engine answers on every platform it cannot identify — which is every
    /// platform except Android, iOS, macOS and Windows.
    /// </summary>
    /// <remarks>
    /// 🔒 It lives here and not in <c>HostAnswers</c>: an engine's own word for "I do not know" is
    /// this adapter's knowledge, and a table of engine literals in <c>Application</c> would be the
    /// vendor concept crossing the boundary `23` §5 A2 exists to stop.
    /// </remarks>
    private const string UnidentifiedDeviceLiteral = "GenericDevice";

    /// <summary>The player's locale, as a BCP-47 tag.</summary>
    /// <remarks>
    /// 🔒 <b>The translation is <see cref="HostAnswers.ToLocale"/>'s, not this class's.</b> The
    /// engine answers in its own shape — <c>language_Script_COUNTRY_VARIANT@extra</c> — and every
    /// implementation of the platform port faces the same normalisation. Doing it here as well
    /// would be the same rule written twice, in the one place no test can reach: calling this
    /// property outside the runtime is a fatal fault. What is left here is the engine call.
    /// <para>
    /// 🔒 The engine's RESOLVED locale, not the operating system's. <c>TranslationServer</c> starts
    /// from the OS answer and then honours the two overrides the engine itself defines — the
    /// <c>--language</c> command-line flag and <c>internationalization/locale/test</c> — so a run
    /// launched with a locale named on the command line (<c>Run-Game.ps1</c> does this) reads that
    /// locale everywhere. Reading <c>OS.GetLocale()</c> here made the flag a no-op for every string
    /// this game shows, because nothing in it goes through the engine's translation tables.
    /// </para>
    /// </remarks>
    public string Locale => HostAnswers.ToLocale(global::Godot.TranslationServer.GetLocale()).Name;

    /// <summary>The device model, or <see langword="null"/> when the engine cannot identify it.</summary>
    /// <remarks>
    /// ⚠️ <b>Behaviour change, deliberate:</b> this used to pass <c>GenericDevice</c> through
    /// verbatim on the argument that the string is the engine stating an absence and a caller must
    /// read it as one. <c>IPlatformInfoPort</c> has since ruled how an absence arrives — as
    /// <see langword="null"/>, so that no caller has to know which host produced it — and this now
    /// answers that way. Nothing reads this member yet; <c>GodotClientComposition</c> only holds the
    /// instance.
    /// </remarks>
    public string? DeviceModel =>
        HostAnswers.ToDeviceModel(global::Godot.OS.GetModelName(), UnidentifiedDeviceLiteral);

    /// <summary>The host platform's name.</summary>
    /// <remarks>
    /// The engine's own spelling — <c>Android</c>, <c>iOS</c>, <c>Windows</c>, <c>Linux</c>,
    /// <c>macOS</c>, <c>Web</c> — kept verbatim so it can be matched against the engine's
    /// documentation rather than against a mapping table that has to be maintained here.
    /// </remarks>
    public string PlatformName => global::Godot.OS.GetName();
}
