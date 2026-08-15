namespace SlayIdleRepeat.AssetProvenance;

/// <summary>One asset file that has actually been delivered into the production area.</summary>
/// <param name="Id">The file stem, which becomes the asset id.</param>
/// <param name="RelativePath">Its path below the delivery root, for readable failure messages.</param>
/// <param name="Extension">The delivered extension, lower-cased.</param>
public sealed record DeliveredAsset(string Id, string RelativePath, string Extension);

/// <summary>
/// Enumerates the delivery set: the asset files committed under <c>assets/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The scan covers the whole of <c>assets/</c> minus the provenance store, rather than one blessed
/// output directory, so a future pipeline or tool writing its output somewhere new is covered from
/// day one with no edit here.
/// </para>
/// <para>
/// The scan reads the filesystem, not the git index, so an uncommitted local placeholder run under
/// <c>assets/</c> will trip the gate on that machine until cleaned up — the correct outcome, since
/// anything present on disk is something whose provenance can be asked for.
/// </para>
/// <para>
/// The delivery set is limited to the formats the design docs actually name for delivery, so a
/// working file in another format (e.g. a layered <c>.psd</c> source beside its exported
/// <c>.png</c>) is not scanned. An asset shipped in an unauthorised format is therefore invisible
/// to this forward direction; that is caught by the per-asset QA checklist's naming item instead.
/// </para>
/// </remarks>
public static class DeliveredAssets
{
    /// <summary>The production area, relative to the repository root.</summary>
    public const string AssetsDirectory = "assets";

    /// <summary>The delivered formats: <c>.png</c> for art, OGG for shipping audio, WAV for SFX source.</summary>
    public static IReadOnlyList<string> DeliveredExtensions { get; } = [".png", ".ogg", ".wav"];

    /// <summary>Absolute path of the delivery root, given a repository root.</summary>
    public static string RootFor(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Path.Combine(repositoryRoot, AssetsDirectory);
    }

    /// <summary>
    /// Every delivered asset under the root, ordered by id.
    /// </summary>
    /// <exception cref="ProvenanceFormatException">
    /// The root does not exist. Reported as a failure rather than an empty result — the two states
    /// are indistinguishable to a caller and only one of them should be a pass.
    /// </exception>
    public static IReadOnlyList<DeliveredAsset> Scan(string deliveryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryRoot);

        if (!Directory.Exists(deliveryRoot))
        {
            throw new ProvenanceFormatException(
                AssetsDirectory,
                $"does not exist at '{deliveryRoot}'. The delivery set is scanned from here; a " +
                "missing root would otherwise be reported as an empty one, and an empty one is a " +
                "pass.");
        }

        // Ordinal, not OrdinalIgnoreCase: on a case-sensitive filesystem `assets/Provenance/` is a
        // DIFFERENT directory from the store, and an ignore-case exclusion would silently drop
        // whatever was put there out of the delivery set.
        var excluded = Path.Combine(deliveryRoot, ProvenanceStore.DirectoryName) + Path.DirectorySeparatorChar;
        var delivered = new List<DeliveredAsset>();

        foreach (var file in Directory.GetFiles(deliveryRoot, "*", SearchOption.AllDirectories))
        {
            if (file.StartsWith(excluded, StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (!DeliveredExtensions.Contains(extension, StringComparer.Ordinal))
            {
                continue;
            }

            delivered.Add(new DeliveredAsset(
                Path.GetFileNameWithoutExtension(file),
                Path.GetRelativePath(deliveryRoot, file).Replace('\\', '/'),
                extension));
        }

        return [.. delivered.OrderBy(d => d.Id, StringComparer.Ordinal).ThenBy(d => d.RelativePath, StringComparer.Ordinal)];
    }
}
