namespace SlayIdleRepeat.AssetProvenance;

/// <summary>One asset file that has actually been delivered into the production area.</summary>
/// <param name="Id">The file stem, which `15` §D1 and `20` §5 make the asset id.</param>
/// <param name="RelativePath">Its path below the delivery root, for readable failure messages.</param>
/// <param name="Extension">The delivered extension, lower-cased.</param>
public sealed record DeliveredAsset(string Id, string RelativePath, string Extension);

/// <summary>
/// Enumerates the delivery set: the asset files committed under <c>assets/</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The scan is over the whole of <c>assets/</c> minus the provenance store, rather than over one
/// blessed output directory. The gate's forward direction is "no delivered asset without a
/// provenance record", and a rule that only looks in the folder the pipeline is currently
/// configured to write to is a rule that any future change of that folder switches off silently.
/// M8-06's pipeline and M8-10's placeholder run may put their output wherever they like; both are
/// covered from the day they land, with no edit here.
/// </para>
/// <para>
/// 🔒 M8-10's placeholder output is a build artifact and is never committed (M8 tracker), so it is
/// never in the delivery set and needs no record. The scan reads the <em>filesystem</em>, not the
/// git index, so a local placeholder run that writes under <c>assets/</c> will trip the gate on
/// that developer's machine until the output is cleaned — which is the correct outcome, not a
/// loophole: something present is something whose provenance can be asked for.
/// </para>
/// <para>
/// ⚠️ <b>The stated limit.</b> The delivery set is defined by the formats `15` §D1 and `20` §5
/// actually name, so a file under <c>assets/</c> in any other format is a working file and is not
/// scanned — including one whose stem is a register id, e.g. a layered <c>.psd</c> source beside
/// its exported <c>.png</c>. That means an asset shipped in an unauthorised format (say a
/// <c>.jpg</c>) is invisible to the forward direction. Widening the list would mean inventing
/// formats no design document authorises, which steering S6 forbids; narrowing it to "anything
/// that is not on a working-file list" means inventing that list instead. The honest position is
/// that the delivery formats are the ones the docs name, and that `15` §F's per-asset QA checklist
/// — <em>"File named per §D1"</em> — is what catches a delivery in a format §D1 does not allow.
/// </para>
/// </remarks>
public static class DeliveredAssets
{
    /// <summary>The production area, relative to the repository root.</summary>
    public const string AssetsDirectory = "assets";

    /// <summary>
    /// The delivered formats: `15` §D1 names <c>.png</c> for art, `20` §5 names OGG for shipping
    /// and WAV for SFX source. A working file in some other format is not a delivery — see the
    /// stated limit on this type.
    /// </summary>
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
    /// 🔒 The root does not exist. An absent delivery root must not read as "nothing delivered
    /// yet": the two states are indistinguishable to a caller and only one of them is a pass.
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

        // 🔒 Ordinal, not OrdinalIgnoreCase, and the name comes from ProvenanceStore rather than
        // from a literal here. On Linux — which is what CI runs — `assets/Provenance/` is a
        // DIFFERENT directory from the store, and an ignore-case exclusion would quietly drop
        // whatever was put there out of the delivery set. Over-excluding is the loophole;
        // under-excluding costs nothing, because the real store holds no delivery-format files.
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
