using System.Text.RegularExpressions;

namespace SlayIdleRepeat.AssetManifest;

/// <summary>What kind of thing is wrong with the register.</summary>
public enum ManifestIssueCode
{
    /// <summary>Two rows declare the same asset id.</summary>
    DuplicateId,

    /// <summary>An id does not match the naming convention.</summary>
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
/// The rules that hold between the manifest and the counts it claims, and between its header
/// blocks and its rows.
/// </summary>
/// <remarks>
/// This validator never reconciles: a count mismatch is only an error if it is unrecorded, and a
/// recorded discrepancy that has stopped being true is itself an error
/// (<see cref="ManifestIssueCode.StaleDiscrepancy"/>) — an exception must expire on its own once
/// satisfied rather than keep describing a disagreement that no longer exists.
/// </remarks>
public static partial class ManifestValidator
{
    /// <summary>The category prefixes an asset id may carry.</summary>
    public static IReadOnlySet<string> IdPrefixes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "chr", "pet", "mnt", "tile", "board", "bg", "gear", "icon", "ui", "die", "vfx", "store",
    };

    /// <summary>Every rule, over a loaded register.</summary>
    public static IReadOnlyList<ManifestIssue> Validate(AssetManifestSet manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<ManifestIssue>();

        // Grouped once rather than re-scanned by every per-section/per-family rule below.
        var bySection = manifest.Art.Assets.ToLookup(a => a.Section, StringComparer.Ordinal);
        var byFamily = manifest.Audio.Assets.ToLookup(a => a.Family, StringComparer.Ordinal);

        CheckIdsAreUniqueAndWellFormed(manifest, issues);
        CheckSectionCounts(manifest, bySection, issues);
        CheckArtTotals(manifest, issues);
        CheckAtlases(manifest, issues);
        CheckBiomeScoping(manifest, issues);
        CheckCutRows(manifest, bySection, issues);
        CheckAudioFamilies(manifest, byFamily, issues);
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
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in manifest.Art.Assets.Select(a => a.Id)
                     .Concat(manifest.Audio.Assets.Select(a => a.Id)))
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        foreach (var (id, count) in counts.Where(entry => entry.Value > 1))
        {
            issues.Add(new ManifestIssue(
                ManifestIssueCode.DuplicateId, id,
                $"is declared {count} times. Three later tasks key their records to this " +
                "id, so a collision silently merges two asset slots."));
        }

        foreach (var asset in manifest.Art.Assets)
        {
            // IdPrefixes is the single source of truth for legal prefixes; SnakeCaseId() governs
            // shape alone, so the two can't drift the way a duplicated regex list would.
            var underscore = asset.Id.IndexOf('_', StringComparison.Ordinal);
            var prefix = underscore > 0 ? asset.Id[..underscore] : asset.Id;

            if (!IdPrefixes.Contains(prefix) || !SnakeCaseId().IsMatch(asset.Id))
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

    private static void CheckSectionCounts(
        AssetManifestSet manifest, ILookup<string, ArtAsset> bySection, List<ManifestIssue> issues)
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
            // One pass per section over the pre-built lookup, rather than a full-register scan per section.
            var rows = 0;
            var derived = 0;
            var cut = 0;

            foreach (var asset in bySection[section.Id])
            {
                rows++;
                if (asset.Derived)
                {
                    derived++;
                }

                if (!asset.IsActive)
                {
                    cut++;
                }
            }

            if (rows != section.TranscribedCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records transcribedCount {section.TranscribedCount} but the assets array holds " +
                    $"{rows} rows for it. The stored count and the data have diverged."));
            }

            // The stored flag must match the arithmetic, not just the row count above — this
            // catches a regeneration that quietly flips countsAgree without recomputing it.
            var agrees = section.ClaimedCount == section.TranscribedCount;
            if (agrees != section.CountsAgree)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"stores countsAgree={section.CountsAgree}, but 15 §E1 claims " +
                    $"{section.ClaimedCount} and {section.TranscribedCount} rows are transcribed."));
            }

            if (derived != section.DerivedCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records derivedCount {section.DerivedCount} but {derived} " +
                    "rows carry derived:true."));
            }

            if (cut != section.CutCount)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.CountMismatch, $"sections/{section.Id}",
                    $"records cutCount {section.CutCount} but {cut} rows " +
                    "carry a cut ruling."));
            }
        }
    }

    private static void CheckArtTotals(AssetManifestSet manifest, List<ManifestIssue> issues)
    {
        var totals = manifest.Art.Totals;
        // One pass, not three: CutArt, DerivedArt and ActiveArt each walk the whole register.
        var cut = 0;
        var derived = 0;
        foreach (var asset in manifest.Art.Assets)
        {
            if (!asset.IsActive)
            {
                cut++;
            }

            if (asset.Derived)
            {
                derived++;
            }
        }

        Compare(issues, "totals/transcribed", totals.Transcribed, manifest.Art.Assets.Count);
        Compare(issues, "totals/cut", totals.Cut, cut);
        Compare(issues, "totals/derived", totals.Derived, derived);
        Compare(issues, "totals/active", totals.Active, manifest.Art.Assets.Count - cut);

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
            // Resolved by Atlas.Covers, the single place the template-matching rule lives, so this
            // loop and AssetManifestSet.AtlasMembers can't disagree the way separate copies did.
            var members = manifest.Art.Assets
                .Where(a => a.Atlas is not null && atlas.Covers(a.Atlas))
                .ToArray();

            Compare(issues, $"atlases/{atlas.Id}/assetCount", atlas.AssetCount, members.Length);
            Compare(issues, $"atlases/{atlas.Id}/uncutAssetCount", atlas.UncutAssetCount,
                members.Count(m => m.IsActive));
        }

        foreach (var asset in manifest.Art.Assets.Where(a => a.Atlas is not null))
        {
            var atlas = asset.Atlas!;
            var known = declared.Contains(atlas) || manifest.Art.Atlases.Any(a => a.Covers(atlas));

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
        // TryAdd, not ToDictionary: a duplicate biome key is a data defect to report, not an
        // ArgumentException thrown out of a method whose contract is to return findings.
        var byKey = new Dictionary<string, Biome>(StringComparer.Ordinal);
        foreach (var biomeEntry in manifest.Art.Biomes)
        {
            if (!byKey.TryAdd(biomeEntry.Key, biomeEntry))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, $"biomes/{biomeEntry.Key}",
                    "is declared more than once. 15 §A5 locks one palette per biome, and a second " +
                    "declaration makes which palette a row must carry ambiguous."));
            }
        }

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

    private static void CheckCutRows(
        AssetManifestSet manifest, ILookup<string, ArtAsset> bySection, List<ManifestIssue> issues)
    {
        foreach (var section in manifest.Art.Sections)
        {
            var rows = 0;
            var cut = 0;

            foreach (var asset in bySection[section.Id])
            {
                rows++;
                if (!asset.IsActive)
                {
                    cut++;
                }
            }

            if (section.Cut is null && cut > 0)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, $"sections/{section.Id}",
                    $"declares no cut, but {cut} of its rows carry one. A ruling that removed " +
                    "assets must be recorded on the section too, or the section reads as live."));
            }

            if (section.Cut is not null && rows > 0 && cut != rows)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.InconsistentRow, $"sections/{section.Id}",
                    $"is cut ('{section.Cut}') but only {cut} of its {rows} rows carry " +
                    "the ruling. A partly-cut section is not what a section-level cut means."));
            }
        }
    }

    private static void CheckAudioFamilies(
        AssetManifestSet manifest, ILookup<string, AudioAsset> byFamily, List<ManifestIssue> issues)
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
            var actual = byFamily[family.Id].Count();
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

        // Keyed on the record's ID (DSC_<section>_COUNT), not prose inside it — matching on
        // sourceSection text would be satisfied by any record that happened to cite the section.
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

        CheckAudioFamilyRecords(manifest, recorded, issues);

        CheckTotalRecord(recorded, issues, "DSC_E1_TOTAL",
            manifest.Art.Totals.ClaimedBySummaryTable != manifest.Art.Totals.Transcribed,
            $"15 §E1's TOTAL ({manifest.Art.Totals.ClaimedBySummaryTable}) and the transcribed row " +
            $"count ({manifest.Art.Totals.Transcribed})");

        var audio = manifest.Audio.Totals;
        CheckTotalRecord(recorded, issues, "DSC_AUDIO_TOTALS",
            audio.ClaimedSfx != audio.TranscribedSfx ||
            audio.ClaimedMusic != audio.TranscribedMusic ||
            audio.ClaimedCombined != audio.TranscribedCombined,
            $"doc 20's claimed totals ({audio.ClaimedSfx} SFX / {audio.ClaimedMusic} music / " +
            $"{audio.ClaimedCombined} combined) and the transcribed ones ({audio.TranscribedSfx} / " +
            $"{audio.TranscribedMusic} / {audio.TranscribedCombined})");
    }

    /// <summary>The same two-way discrepancy rule, applied per audio family.</summary>
    private static void CheckAudioFamilyRecords(
        AssetManifestSet manifest, IReadOnlyList<Discrepancy> recorded, List<ManifestIssue> issues)
    {
        foreach (var family in manifest.Audio.Families.Where(f => !f.CountsAgree))
        {
            var expected = $"DSC_{family.Id.ToUpperInvariant()}_COUNT";
            if (!recorded.Any(d => string.Equals(d.Id, expected, StringComparison.Ordinal)))
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.UnrecordedDiscrepancy, $"families/{family.Id}",
                    $"disagrees with doc 20 ({family.ClaimedCount} claimed, " +
                    $"{family.TranscribedCount} transcribed) and no record '{expected}' exists. " +
                    "A mismatch is evidence for O30 at M11-01; it must be written down, not left " +
                    "for someone to rediscover."));
            }
        }

        // And the self-expiry direction. A record naming a family whose counts now agree is
        // describing something that stopped being true.
        foreach (var record in recorded)
        {
            var match = FamilyCountRecord().Match(record.Id);
            if (!match.Success)
            {
                continue;
            }

            // Only for a token that resolves to a declared family: e.g. DSC_E20_COUNT also
            // matches this shape but belongs to the section rule above.
            var name = match.Groups["family"].Value;
            var family = manifest.Audio.Families
                .FirstOrDefault(f => string.Equals(f.Id, name, StringComparison.OrdinalIgnoreCase));

            if (family is not null && family.CountsAgree)
            {
                issues.Add(new ManifestIssue(
                    ManifestIssueCode.StaleDiscrepancy, record.Id,
                    $"records a count disagreement for family '{family.Id}', but doc 20's claim " +
                    $"({family.ClaimedCount}) and the transcription ({family.TranscribedCount}) now " +
                    "agree. Delete the record — an exception that outlives the condition it " +
                    "describes is worse than none."));
            }
        }
    }

    private static void CheckTotalRecord(
        IReadOnlyList<Discrepancy> recorded, List<ManifestIssue> issues,
        string recordId, bool disagrees, string subject)
    {
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

    /// <summary>Shape only — snake_case behind a lowercase prefix. Legal prefixes are <see cref="IdPrefixes"/>.</summary>
    [GeneratedRegex("^[a-z][a-z0-9]*_[a-z0-9_]+$")]
    private static partial Regex SnakeCaseId();

    [GeneratedRegex("^(mus|sfx)_[a-z0-9_]+$")]
    private static partial Regex AudioId();

    [GeneratedRegex("^DSC_(?<section>E[0-9]{1,2})_COUNT$")]
    private static partial Regex SectionCountRecord();

    [GeneratedRegex("^DSC_(?<family>[A-Z0-9_]+)_COUNT$")]
    private static partial Regex FamilyCountRecord();
}
