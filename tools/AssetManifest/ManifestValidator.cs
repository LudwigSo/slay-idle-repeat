using System.Text.RegularExpressions;

namespace SlayIdleRepeat.AssetManifest;

/// <summary>What kind of thing is wrong with the register.</summary>
public enum ManifestIssueCode
{
    /// <summary>Two rows declare the same asset id.</summary>
    DuplicateId,

    /// <summary>An id does not match the `15` §D1 naming convention.</summary>
    MalformedId,

    /// <summary>A stored count does not match the rows actually present.</summary>
    CountMismatch,

    /// <summary>A row count disagrees with the design doc and nothing records the disagreement.</summary>
    UnrecordedDiscrepancy,

    /// <summary>A recorded discrepancy has stopped describing a real disagreement.</summary>
    StaleDiscrepancy,

    /// <summary>A row's transcribed values contradict each other or the header blocks.</summary>
    InconsistentRow,
}

/// <summary>One finding, located at the row or block it is about.</summary>
public sealed record ManifestIssue(ManifestIssueCode Code, string Location, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Code} · {Location} · {Message}";
}

/// <summary>
/// The rules that hold BETWEEN the manifest and the counts the design docs claim, and between the
/// manifest's own header blocks and its rows.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This validator never reconciles.</b> A section whose transcribed count disagrees with
/// `15` §E1 is not an error here — it is an error only if the disagreement is <em>unrecorded</em>.
/// Reconciling the manifest totals is O30's job at task M11-01, and a check that "fixed" a
/// discrepancy would destroy the evidence O30 needs.
/// </para>
/// <para>
/// The converse is enforced just as hard, and is the reason this type exists rather than a pile of
/// one-off assertions: a recorded discrepancy that has stopped being true fails as
/// <see cref="ManifestIssueCode.StaleDiscrepancy"/>. A declared exception must expire by itself —
/// including when it has been <em>satisfied</em>. If someone adds §E20's missing fiftieth icon to
/// the design doc and regenerates, <c>DSC_E20_COUNT</c> goes red instead of sitting here for five
/// milestones describing a disagreement that no longer exists.
/// </para>
/// </remarks>
public static partial class ManifestValidator
{
    /// <summary>The `15` §D1 category prefixes an asset id may carry.</summary>
    public static IReadOnlySet<string> IdPrefixes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "chr", "pet", "mnt", "tile", "board", "bg", "gear", "icon", "ui", "die", "vfx", "store",
    };

    /// <summary>Every rule, over a loaded register.</summary>
    public static IReadOnlyList<ManifestIssue> Validate(AssetManifestSet manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<ManifestIssue>();

        CheckIdsAreUniqueAndWellFormed(manifest, issues);
        CheckSectionCounts(manifest, issues);
        CheckArtTotals(manifest, issues);
        CheckAtlases(manifest, issues);
        CheckBiomeScoping(manifest, issues);
        CheckCutRows(manifest, issues);
        CheckAudioFamilies(manifest, issues);
        CheckAudioTotals(manifest, issues);
        CheckDiscrepancyRecords(manifest, issues);

        return issues
            .OrderBy(i => i.Location, StringComparer.Ordinal)
            .ThenBy(i => i.Code)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .ToArray();
    }

    private static void CheckIdsAreUniqueAndWellFormed(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        foreach (var duplicate in manifest.Art.Assets.Select(a => a.Id)
                     .Concat(manifest.Audio.Assets.Select(a => a.Id))
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.DuplicateId, duplicate.Key,
                $"is declared {duplicate.Count()} times. Three later tasks key their records to this " +
                "id, so a collision silently merges two asset slots."));
        }

        foreach (var asset in manifest.Art.Assets)
        {
            var prefix = asset.Id.Split('_', 2)[0];
            if (!IdPrefixes.Contains(prefix) || !ArtId().IsMatch(asset.Id))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.MalformedId, asset.Id,
                    $"is not a 15 §D1 id: expected snake_case behind one of " +
                    $"{string.Join(", ", IdPrefixes.OrderBy(p => p, StringComparer.Ordinal))}."));
            }
        }

        foreach (var asset in manifest.Audio.Assets.Where(a => !AudioId().IsMatch(a.Id)))
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.MalformedId, asset.Id,
                "is not a 20 §5 id: expected snake_case behind mus_ or sfx_."));
        }

        foreach (var asset in manifest.Audio.Assets)
        {
            var expected = asset.IsMusic ? "mus_" : "sfx_";
            if (!asset.Id.StartsWith(expected, StringComparison.Ordinal))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, asset.Id,
                    $"is kind '{asset.Kind}' but its id does not start with '{expected}' (20 §5)."));
            }
        }
    }

    private static void CheckSectionCounts(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var known = manifest.Art.Sections.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var asset in manifest.Art.Assets.Where(a => !known.Contains(a.Section)))
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.InconsistentRow, asset.Id,
                $"names section '{asset.Section}', which the sections block does not declare."));
        }

        foreach (var section in manifest.Art.Sections)
        {
            var actual = manifest.ArtInSection(section.Id).ToArray();

            if (actual.Length != section.TranscribedCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records transcribedCount {section.TranscribedCount} but the assets array holds " +
                    $"{actual.Length} rows for it. The stored count and the data have diverged."));
            }

            // 🔒 The FLAG must match the arithmetic. Not the same check as the one above: this is
            // what stops a regeneration quietly flipping countsAgree to true and hiding a real
            // disagreement from O30 behind a stored boolean nobody recomputes.
            var agrees = section.ClaimedCount == section.TranscribedCount;
            if (agrees != section.CountsAgree)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"stores countsAgree={section.CountsAgree}, but 15 §E1 claims " +
                    $"{section.ClaimedCount} and {section.TranscribedCount} rows are transcribed."));
            }

            if (actual.Count(a => a.Derived) != section.DerivedCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records derivedCount {section.DerivedCount} but {actual.Count(a => a.Derived)} " +
                    "rows carry derived:true."));
            }

            if (actual.Count(a => !a.IsActive) != section.CutCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records cutCount {section.CutCount} but {actual.Count(a => !a.IsActive)} rows " +
                    "carry a cut ruling."));
            }
        }
    }

    private static void CheckArtTotals(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var totals = manifest.Art.Totals;
        Compare(issues, "totals/transcribed", totals.Transcribed, manifest.Art.Assets.Count);
        Compare(issues, "totals/cut", totals.Cut, manifest.CutArt.Count());
        Compare(issues, "totals/derived", totals.Derived, manifest.DerivedArt.Count());
        Compare(issues, "totals/active", totals.Active, manifest.ActiveArt.Count());

        if (totals.Active != totals.Transcribed - totals.Cut)
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.CountMismatch, "totals/active",
                $"is {totals.Active}, but transcribed({totals.Transcribed}) − cut({totals.Cut}) = " +
                $"{totals.Transcribed - totals.Cut}."));
        }

        var sectionSum = manifest.Art.Sections.Sum(s => s.TranscribedCount);
        if (sectionSum != totals.Transcribed)
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.CountMismatch, "totals/transcribed",
                $"is {totals.Transcribed} but the section counts sum to {sectionSum}."));
        }
    }

    private static void CheckAtlases(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var declared = manifest.Art.Atlases.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var atlas in manifest.Art.Atlases)
        {
            // `atlas_biome_{n}` is a template in §D2, standing for the eight per-chapter atlases.
            var members = atlas.Id.Contains('{', StringComparison.Ordinal)
                ? manifest.Art.Assets.Where(a => a.Atlas is not null &&
                      a.Atlas.StartsWith(atlas.Id[..atlas.Id.IndexOf('{', StringComparison.Ordinal)],
                          StringComparison.Ordinal)).ToArray()
                : manifest.Art.Assets.Where(a =>
                      string.Equals(a.Atlas, atlas.Id, StringComparison.Ordinal)).ToArray();

            Compare(issues, $"atlases/{atlas.Id}/assetCount", atlas.AssetCount, members.Length);
            Compare(issues, $"atlases/{atlas.Id}/uncutAssetCount", atlas.UncutAssetCount,
                members.Count(m => m.IsActive));
        }

        foreach (var asset in manifest.Art.Assets.Where(a => a.Atlas is not null))
        {
            var atlas = asset.Atlas!;
            var known = declared.Contains(atlas) ||
                        (BiomeAtlas().IsMatch(atlas) && declared.Contains("atlas_biome_{n}"));

            if (!known)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, asset.Id,
                    $"is packed into '{atlas}', which 15 §D2 does not declare."));
            }
        }
    }

    private static void CheckBiomeScoping(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var byKey = manifest.Art.Biomes.ToDictionary(b => b.Key, StringComparer.Ordinal);

        foreach (var asset in manifest.Art.Assets)
        {
            if (asset.Biome is null)
            {
                if (asset.PaletteColours is not null)
                {
                    issues.Add(new ManifestIssue(
                        ManifestIssueCode.InconsistentRow, asset.Id,
                        "carries a palette but no biome. 15 §A5 scopes a palette to a biome."));
                }

                continue;
            }

            if (!byKey.TryGetValue(asset.Biome, out var biome))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, asset.Id,
                    $"names biome '{asset.Biome}', which the biomes block does not declare."));
                continue;
            }

            if (asset.PaletteColours is null)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, asset.Id,
                    $"is scoped to biome '{asset.Biome}' but carries no palette (15 §A5)."));
            }
            else if (asset.PaletteColours != biome.Palette)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, asset.Id,
                    $"carries a palette that is not biome '{asset.Biome}'s locked six from 15 §A5."));
            }
        }
    }

    private static void CheckCutRows(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        foreach (var section in manifest.Art.Sections)
        {
            var rows = manifest.ArtInSection(section.Id).ToArray();
            var cut = rows.Where(r => !r.IsActive).ToArray();

            if (section.Cut is null && cut.Length > 0)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, $"sections/{section.Id}",
                    $"declares no cut, but {cut.Length} of its rows carry one. A ruling that removed " +
                    "assets must be recorded on the section too, or the section reads as live."));
            }

            if (section.Cut is not null && rows.Length > 0 && cut.Length != rows.Length)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, $"sections/{section.Id}",
                    $"is cut ('{section.Cut}') but only {cut.Length} of its {rows.Length} rows carry " +
                    "the ruling. A partly-cut section is not what a section-level cut means."));
            }
        }
    }

    private static void CheckAudioFamilies(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var declared = manifest.Audio.Families.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var asset in manifest.Audio.Assets.Where(a => !declared.Contains(a.Family)))
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.InconsistentRow, asset.Id,
                $"names family '{asset.Family}', which the families block does not declare."));
        }

        foreach (var family in manifest.Audio.Families)
        {
            var actual = manifest.AudioInFamily(family.Id).Count();
            Compare(issues, $"families/{family.Id}/transcribedCount", family.TranscribedCount, actual);

            var agrees = family.ClaimedCount == family.TranscribedCount;
            if (agrees != family.CountsAgree)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"families/{family.Id}",
                    $"stores countsAgree={family.CountsAgree}, but doc 20 claims {family.ClaimedCount} " +
                    $"and {family.TranscribedCount} rows are transcribed."));
            }
        }
    }

    private static void CheckAudioTotals(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var totals = manifest.Audio.Totals;
        var music = manifest.Audio.Assets.Count(a => a.IsMusic);
        var sfx = manifest.Audio.Assets.Count(a => !a.IsMusic);

        Compare(issues, "audio totals/transcribedMusic", totals.TranscribedMusic, music);
        Compare(issues, "audio totals/transcribedSfx", totals.TranscribedSfx, sfx);
        Compare(issues, "audio totals/transcribedCombined", totals.TranscribedCombined, music + sfx);
        Compare(issues, "audio totals/sfxWithoutDescriptor", totals.SfxWithoutDescriptor,
            manifest.Audio.Assets.Count(a => !a.IsMusic && a.Descriptor is null));
    }

    /// <summary>
    /// 🔒 The two-way rule: every real disagreement is recorded, and every record still describes a
    /// real disagreement.
    /// </summary>
    private static void CheckDiscrepancyRecords(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var recorded = manifest.Art.Discrepancies.Concat(manifest.Audio.Discrepancies).ToArray();

        // 🔒 Keyed on the record's ID, by the convention DSC_<section>_COUNT, not on prose inside
        // it. A rule that searched the `sourceSection` text would be satisfied by any record that
        // happened to cite the same section — including one about something else entirely.
        foreach (var section in manifest.Art.Sections.Where(s => !s.CountsAgree))
        {
            var expected = $"DSC_{section.Id}_COUNT";
            if (!recorded.Any(d => string.Equals(d.Id, expected, StringComparison.Ordinal)))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.UnrecordedDiscrepancy, $"sections/{section.Id}",
                    $"disagrees with 15 §E1 ({section.ClaimedCount} claimed, " +
                    $"{section.TranscribedCount} transcribed) and no record '{expected}' exists. " +
                    "A mismatch is evidence for O30 at M11-01; it must be written down, not left " +
                    "for someone to rediscover."));
            }
        }

        // The self-expiry direction. A `DSC_E<n>_COUNT` record names one section's count; once that
        // section's counts agree, the record is describing something that is no longer true.
        foreach (var record in recorded)
        {
            var match = SectionCountRecord().Match(record.Id);
            if (!match.Success)
            {
                continue;
            }

            var sectionId = match.Groups["section"].Value;
            var section = manifest.Art.Sections
                .FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.Ordinal));

            if (section is null)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.StaleDiscrepancy, record.Id,
                    $"names section '{sectionId}', which the sections block does not declare."));
            }
            else if (section.CountsAgree)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.StaleDiscrepancy, record.Id,
                    $"records a count disagreement for {sectionId}, but 15 §E1's claim " +
                    $"({section.ClaimedCount}) and the transcription ({section.TranscribedCount}) now " +
                    "agree. Delete the record — an exception that outlives the condition it " +
                    "describes is worse than none."));
            }
        }

        CheckTotalRecord(manifest, recorded, issues, "DSC_E1_TOTAL",
            manifest.Art.Totals.ClaimedBySummaryTable != manifest.Art.Totals.Transcribed,
            $"15 §E1's TOTAL ({manifest.Art.Totals.ClaimedBySummaryTable}) and the transcribed row " +
            $"count ({manifest.Art.Totals.Transcribed})");

        var audio = manifest.Audio.Totals;
        CheckTotalRecord(manifest, recorded, issues, "DSC_AUDIO_TOTALS",
            audio.ClaimedSfx != audio.TranscribedSfx ||
            audio.ClaimedMusic != audio.TranscribedMusic ||
            audio.ClaimedCombined != audio.TranscribedCombined,
            $"doc 20's claimed totals ({audio.ClaimedSfx} SFX / {audio.ClaimedMusic} music / " +
            $"{audio.ClaimedCombined} combined) and the transcribed ones ({audio.TranscribedSfx} / " +
            $"{audio.TranscribedMusic} / {audio.TranscribedCombined})");
    }

    private static void CheckTotalRecord(
        AssetManifestSet manifest, IReadOnlyList<Discrepancy> recorded, List<ManifestIssue> issues,
        string recordId, bool disagrees, string subject)
    {
        _ = manifest;
        var present = recorded.Any(d => string.Equals(d.Id, recordId, StringComparison.Ordinal));

        if (disagrees && !present)
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.UnrecordedDiscrepancy, recordId,
                $"is absent, but {subject} disagree."));
        }
        else if (!disagrees && present)
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.StaleDiscrepancy, recordId,
                $"is recorded, but {subject} now agree. Delete it."));
        }
    }

    private static void Compare(List<ManifestIssue> issues, string location, int stored, int actual)
    {
        if (stored != actual)
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.CountMismatch, location,
                $"records {stored} but the data holds {actual}."));
        }
    }

    [GeneratedRegex("^(chr|pet|mnt|tile|board|bg|gear|icon|ui|die|vfx|store)_[a-z0-9_]+$")]
    private static partial Regex ArtId();

    [GeneratedRegex("^(mus|sfx)_[a-z0-9_]+$")]
    private static partial Regex AudioId();

    [GeneratedRegex("^atlas_biome_[1-8]$")]
    private static partial Regex BiomeAtlas();

    [GeneratedRegex("^DSC_(?<section>E[0-9]{1,2})_COUNT$")]
    private static partial Regex SectionCountRecord();
}
