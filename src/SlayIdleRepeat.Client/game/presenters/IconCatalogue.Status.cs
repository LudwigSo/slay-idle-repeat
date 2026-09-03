namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The status icons, keyed by the log's own status ordinal.</summary>
public static partial class IconCatalogue
{
    private const string StatusIconDirectory = "res://game/art/icons/";

    /// <summary>The thirteen authored statuses, in the order the rules layer numbers them from one.</summary>
    private static readonly string[] StatusIconPaths = StatusIconPathsOf(
        "burn", "poison", "bleed", "freeze", "stun", "weaken", "sunder",
        "spore", "rage", "ward", "haste", "regen", "chill");

    /// <summary>
    /// The <c>res://</c> path of the icon for a status the log names by ordinal, or null for an
    /// ordinal outside the authored statuses.
    /// </summary>
    public static string? Status(ushort logId) =>
        logId >= 1 && logId <= StatusIconPaths.Length ? StatusIconPaths[logId - 1] : null;

    private static string[] StatusIconPathsOf(params string[] statusNames)
    {
        var paths = new string[statusNames.Length];

        for (var index = 0; index < statusNames.Length; index++)
        {
            paths[index] = $"{StatusIconDirectory}icon_status_{statusNames[index]}.svg";
        }

        return paths;
    }
}
