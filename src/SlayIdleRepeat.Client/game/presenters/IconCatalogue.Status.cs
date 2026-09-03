namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>The status icons, keyed by the log's own status ordinal.</summary>
public static partial class IconCatalogue
{
    /// <summary>
    /// The <c>res://</c> path of the icon for a status the log names by ordinal, or null for an
    /// ordinal outside the authored statuses.
    /// </summary>
    public static string? Status(ushort logId)
    {
        _ = logId;

        return null;
    }
}
