using System.Text.Json;
using SlayIdleRepeat.Client.Game.Presenters;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>
/// Reads the placeholder generator's atlas manifests off the filesystem.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Absent is the ordinary answer, and on this branch it is the only one any checkout gives.</b>
/// Four separate facts put it there, and none of them is fixable from the client: nobody has run the
/// generator here; its output directory is gitignored and policy forbids it entering the engine
/// project; the generator emits loose per-asset images and a rectangles-only manifest and never
/// rasterises an atlas page, so there is no atlas texture to load by construction; and the three
/// loading-screen slots a boot screen would want are skipped permanently for want of a delivery
/// size. So this reads the manifest format the generator really emits, and says plainly when there
/// is none — it does not invent one.
/// </para>
/// <para>
/// 🔒 A manifest that is present and will not parse is left to throw. That is a different fact from
/// absence, and the boot reports it as the unanticipated failure it is rather than as the empty
/// state every machine is already in.
/// </para>
/// <para>
/// ⚠️ Reads through <c>System.IO</c>, so it finds nothing once the game's resources are packed into
/// an archive — the same limitation the content root already fails by name on. That is stated
/// rather than worked around here: the packed-resource path belongs to the task that owns it.
/// </para>
/// </remarks>
public sealed class PlaceholderAtlasCatalogue : IBootAtlasCatalogue
{
    /// <summary>The generator's output directory for atlas metadata, relative to a checkout root.</summary>
    private static readonly string[] ManifestDirectorySegments = ["artifacts", "placeholders", "atlas"];

    private const string ManifestSearchPattern = "*.json";

    /// <summary>The array of placed rectangles in one emitted manifest.</summary>
    private const string PlacementsMember = "placements";

    private readonly string _searchStartDirectory;

    /// <summary>Takes the directory the search for a manifest directory starts at and climbs from.</summary>
    /// <param name="searchStartDirectory">An absolute directory inside the checkout, or wherever the game was installed.</param>
    /// <exception cref="ArgumentException"><paramref name="searchStartDirectory"/> is null, empty or whitespace.</exception>
    public PlaceholderAtlasCatalogue(string searchStartDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchStartDirectory);

        _searchStartDirectory = searchStartDirectory;
    }

    /// <summary>How the manifest directory is spelled, for a message that has to be actionable.</summary>
    public static string ManifestDirectoryRelativePath => string.Join('/', ManifestDirectorySegments);

    /// <inheritdoc/>
    /// <exception cref="JsonException">A manifest is present and malformed.</exception>
    public BootAtlasResult Load()
    {
        var directory = FindManifestDirectory();

        if (directory is null)
        {
            return BootAtlasResult.Absent(
                $"no '{ManifestDirectoryRelativePath}' directory at or above '{_searchStartDirectory}' — " +
                "the placeholder generator has not been run here, and its output is never committed");
        }

        string[] manifests;

        try
        {
            manifests = Directory.GetFiles(directory, ManifestSearchPattern);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            // A directory that cannot be listed — no permission, or removed between the check and
            // the listing — is absence with a cause, not a manifest that will not parse. This whole
            // read is optional, so refusing to start the game over it would be the worse defect.
            return BootAtlasResult.Absent(
                $"'{directory}' could not be listed — {unreadable.GetType().Name}: " +
                $"{unreadable.Message}");
        }

        if (manifests.Length == 0)
        {
            return BootAtlasResult.Absent(
                $"'{directory}' exists and holds no atlas manifest — a generator run that packed " +
                "nothing, which is a different fact from never having run one");
        }

        var placements = 0;

        foreach (var manifest in manifests)
        {
            placements += CountPlacements(manifest);
        }

        return BootAtlasResult.Loaded(manifests.Length, placements);
    }

    /// <summary>
    /// How many placements one manifest holds — and a refusal to count a file that is not a
    /// manifest, because reporting it as an atlas holding nothing would file a generator run that
    /// emitted rubbish under the same word as one that packed an empty page.
    /// </summary>
    private static int CountPlacements(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));

        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                $"'{manifestPath}' is valid JSON but is not an atlas manifest: its root is " +
                $"{root.ValueKind} rather than an object.");
        }

        // A manifest with no placements member at all packed nothing, which is a fact rather than a
        // defect. One that carries the member as something other than an array is the defect.
        if (!root.TryGetProperty(PlacementsMember, out var placements))
        {
            return 0;
        }

        return placements.ValueKind == JsonValueKind.Array
            ? placements.GetArrayLength()
            : throw new JsonException(
                $"'{manifestPath}' carries '{PlacementsMember}' as {placements.ValueKind} rather " +
                "than an array of placed rectangles.");
    }

    /// <summary>
    /// Climbs from the start directory looking for the generator's output, because the engine
    /// project sits several levels below the checkout root the generator writes at.
    /// </summary>
    private string? FindManifestDirectory()
    {
        var directory = new DirectoryInfo(_searchStartDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. ManifestDirectorySegments]);

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
