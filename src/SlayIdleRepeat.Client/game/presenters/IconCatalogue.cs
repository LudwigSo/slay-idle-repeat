namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The glyphs the Home HUD's tiles carry, one per tile.</summary>
public enum HudIcon
{
    /// <summary>The main Energy bank.</summary>
    Energy,

    /// <summary>The Energy Reserve behind it.</summary>
    EnergyReserve,

    /// <summary>The Crowns wallet.</summary>
    Crowns,

    /// <summary>The Soul Shards wallet.</summary>
    SoulShards,

    /// <summary>A run's Gold.</summary>
    Gold,

    /// <summary>The hero's power index.</summary>
    Power,

    /// <summary>The chapter a run stands in, or the furthest one cleared.</summary>
    Chapter,

    /// <summary>The hero's hit points inside a run.</summary>
    Hp,
}

/// <summary>
/// The textures the Home scene may load, by name: every HUD icon's resource path, and the set the
/// scene rule holds the scene's <c>Texture2D</c> resources against.
/// </summary>
/// <remarks>
/// <para>
/// A table rather than a file-name convention in the scene, because the scene binds a tile to a
/// path and nothing else checks that the Energy tile's path is the energy glyph. Naming the mapping
/// once, where a test can read it, is what lets a swapped pair be caught before it draws.
/// </para>
/// <para>
/// Declared <c>partial</c> for the status glyphs: their half adds its own paths beside
/// <see cref="Catalogued"/> and joins them into <see cref="All"/>, so the scene rule keeps one set
/// to check against rather than one per icon family.
/// </para>
/// </remarks>
public static partial class IconCatalogue
{
    /// <summary>Where every HUD glyph lives.</summary>
    public const string Directory = "res://game/art/icons/";

    private const string SvgExtension = ".svg";

    private static readonly string[] Catalogued = Enum.GetValues<HudIcon>().Select(PathOf).ToArray();

    /// <summary>Every texture path the Home scene may load.</summary>
    public static IReadOnlyList<string> All => Catalogued;

    /// <summary>The resource path of <paramref name="icon"/>'s file.</summary>
    /// <param name="icon">The glyph.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="icon"/> is no glyph this catalogue names.</exception>
    public static string PathOf(HudIcon icon) => Directory + FileStemOf(icon) + SvgExtension;

    private static string FileStemOf(HudIcon icon) => icon switch
    {
        HudIcon.Energy => "hud_energy",
        HudIcon.EnergyReserve => "hud_energy_reserve",
        HudIcon.Crowns => "hud_crowns",
        HudIcon.SoulShards => "hud_soul_shards",
        HudIcon.Gold => "hud_gold",
        HudIcon.Power => "hud_power",
        HudIcon.Chapter => "hud_chapter",
        HudIcon.Hp => "hud_hp",
        _ => throw new ArgumentOutOfRangeException(
            nameof(icon), icon, "this glyph has no file named for it; add its row here before a scene loads it."),
    };
}
