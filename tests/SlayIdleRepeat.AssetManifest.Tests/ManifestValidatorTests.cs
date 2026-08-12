using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// Every <see cref="ManifestValidator"/> rule, proven to fire.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A test that cannot fail is a defect, not a weak test — M0's single largest defect class. Each
/// case below therefore takes the real shipped manifest, applies one surgical textual mutation,
/// and asserts the rule fires. <c>ManifestFiles.WithArtEdit</c> throws when its search string
/// matches nothing, so a mutation that silently stopped applying — after a regeneration renames a
/// field, say — fails loudly instead of quietly testing nothing.
/// </para>
/// <para>
/// 🔒 Each case pins <b>which</b> rule fired, by code AND by location, not merely that the issue
/// list was non-empty. Several of these rules can produce a <see cref="ManifestIssueCode.CountMismatch"/>,
/// and a case that only checked the code would pass while a different rule fired somewhere else.
/// </para>
/// </remarks>
public sealed class ManifestValidatorTests
{
    [Fact]
    public void The_shipped_manifest_has_no_findings()
    {
        var issues = ManifestValidator.Validate(ManifestFiles.Shipped);

        issues.ShouldBeEmpty(
            "the shipped register must be internally consistent:\n" +
            string.Join("\n", issues.Select(i => i.ToString())));
    }

    /// <summary>
    /// S3 floor. Every rule below iterates a collection; if the register were empty they would all
    /// pass forever, and so would the case above.
    /// </summary>
    [Fact]
    public void The_validator_actually_has_subjects_to_rule_over()
    {
        var manifest = ManifestFiles.Shipped;

        manifest.Art.Assets.Count.ShouldBeGreaterThan(900);
        manifest.Audio.Assets.Count.ShouldBeGreaterThan(100);
        manifest.Art.Sections.ShouldNotBeEmpty();
        manifest.Art.Atlases.ShouldNotBeEmpty();
        manifest.Audio.Families.ShouldNotBeEmpty();
        manifest.Art.Discrepancies.ShouldNotBeEmpty();
        manifest.Audio.Discrepancies.ShouldNotBeEmpty();
        manifest.Art.Assets.Count(a => a.Biome is not null).ShouldBeGreaterThan(300);
    }

    /// <summary>
    /// 🔒 S4, for the records the validator does NOT self-expire. Only <c>DSC_E&lt;n&gt;_COUNT</c>,
    /// <c>DSC_E1_TOTAL</c> and <c>DSC_AUDIO_TOTALS</c> go stale on their own; the other eleven are
    /// prose about a transcription decision and no rule can recompute them. With only a
    /// <c>Count &gt; 5</c> floor guarding them, eight of the fourteen could be deleted and every
    /// other case here would stay green — including <c>DSC_TILE_ATLAS</c> and
    /// <c>DSC_UNASSIGNED_ATLASES</c>, which <see cref="ArtFieldTests"/> cites as the evidence for
    /// its null-atlas rule. Pinning the identities makes adding or dropping one a deliberate act.
    /// </summary>
    [Fact]
    public void The_recorded_discrepancies_are_exactly_the_ones_this_transcription_argued_for()
    {
        var art = ManifestFiles.Shipped.Art.Discrepancies;
        var audio = ManifestFiles.Shipped.Audio.Discrepancies;

        art.Select(d => d.Id).ShouldBe([
            "DSC_E20_COUNT", "DSC_E1_TOTAL", "DSC_O8_TOTAL", "DSC_DIE_BODY",
            "DSC_DIE_VARIANT_SUFFIX", "DSC_E4_POSES", "DSC_TILE_ATLAS", "DSC_UNASSIGNED_ATLASES",
            "DSC_ATLAS_VFX_EMPTY", "DSC_MISSING_SIZES",
        ], ignoreOrder: true);

        audio.Select(d => d.Id).ShouldBe([
            "DSC_STINGER_LENGTH", "DSC_SFX_BAND", "DSC_DUCKING_SET", "DSC_SHARED_DESCRIPTORS",
        ], ignoreOrder: true);

        // A record with an empty claim, observation or detail records nothing. The reader only
        // rejects a null, so the emptiness has to be pinned here.
        var all = art.Concat(audio).ToArray();
        all.Length.ShouldBe(14);
        all.ShouldAllBe(d => d.SourceSection.Length > 0);
        all.ShouldAllBe(d => d.Claim.Length > 0);
        all.ShouldAllBe(d => d.Observed.Length > 0);
        all.ShouldAllBe(d => d.Detail.Length > 0);
    }

    [Fact]
    public void A_duplicate_asset_id_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"id\": \"tile_icon_elite\"", "\"id\": \"tile_icon_enemy\"");

        Only(mutated, ManifestIssueCode.DuplicateId, "tile_icon_enemy")
            .Message.ShouldContain("declared 2 times", Case.Sensitive);
    }

    [Fact]
    public void An_id_outside_the_15_D1_prefixes_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"id\": \"tile_icon_enemy\"", "\"id\": \"sprite_icon_enemy\"");

        Only(mutated, ManifestIssueCode.MalformedId, "sprite_icon_enemy")
            .Message.ShouldContain("15 §D1", Case.Sensitive);
    }

    [Fact]
    public void A_stored_section_count_that_no_longer_matches_the_rows_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"id\": \"E8\",\n      \"category\": \"Tile icons\",\n      \"claimedCount\": 14,\n      \"transcribedCount\": 14",
            "\"id\": \"E8\",\n      \"category\": \"Tile icons\",\n      \"claimedCount\": 14,\n      \"transcribedCount\": 13");

        var issues = ManifestValidator.Validate(mutated)
            .Where(i => i.Location == "sections/E8")
            .ToArray();

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.CountMismatch &&
            i.Message.Contains("transcribedCount 13", StringComparison.Ordinal) &&
            i.Message.Contains("holds 14 rows", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 The flag must match the arithmetic. This is the rule that stops a regeneration flipping
    /// <c>countsAgree</c> to true and hiding §E20's real disagreement behind a stored boolean.
    /// </summary>
    [Fact]
    public void A_countsAgree_flag_that_contradicts_the_arithmetic_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"transcribedCount\": 49,\n      \"countsAgree\": false",
            "\"transcribedCount\": 49,\n      \"countsAgree\": true");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.CountMismatch &&
            i.Location == "sections/E20" &&
            i.Message.Contains("stores countsAgree=True", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 The self-expiry direction (S4). Flipping E20's claim to 49 makes the counts agree — at
    /// which point <c>DSC_E20_COUNT</c> is describing a disagreement that no longer exists, and
    /// must fail rather than sit here looking meaningful.
    /// </summary>
    [Fact]
    public void A_discrepancy_record_that_stopped_being_true_is_reported_as_stale()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"claimedCount\": 50,\n      \"transcribedCount\": 49,\n      \"countsAgree\": false",
            "\"claimedCount\": 49,\n      \"transcribedCount\": 49,\n      \"countsAgree\": true");

        var stale = ManifestValidator.Validate(mutated)
            .SingleOrDefault(i => i.Code == ManifestIssueCode.StaleDiscrepancy &&
                                  i.Location == "DSC_E20_COUNT")
            .ShouldNotBeNull("DSC_E20_COUNT must expire once E20's counts agree");

        stale.Message.ShouldContain("now", Case.Sensitive);
        stale.Message.ShouldContain("agree", Case.Sensitive);
        stale.Message.ShouldContain("Delete the record", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 And the other direction: a real disagreement nobody wrote down. Deleting the E20 record
    /// while the counts still disagree must go red — that record is O30's evidence at M11-01.
    /// </summary>
    [Fact]
    public void A_count_disagreement_with_no_discrepancy_record_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit("\"id\": \"DSC_E20_COUNT\"", "\"id\": \"DSC_UNRELATED\"");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.UnrecordedDiscrepancy &&
            i.Location == "sections/E20" &&
            i.Message.Contains("O30", StringComparison.Ordinal) &&
            i.Message.Contains("M11-01", StringComparison.Ordinal));
    }

    [Fact]
    public void A_totals_block_that_drifted_from_the_rows_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit("\"transcribed\": 974", "\"transcribed\": 975");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.CountMismatch &&
            i.Location == "totals/transcribed" &&
            i.Message.Contains("records 975 but the data holds 974", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 Removing the O8 cut from a row must move the active total — which is the whole reason
    /// the cut rows were kept in the data instead of deleted.
    /// </summary>
    [Fact]
    public void Un_cutting_an_E19_row_moves_the_active_total_and_is_reported()
    {
        // 🔒 Anchored on `idSource`, which only asset rows carry. The bare `"cut": "O8 …"` string
        // first occurs in the SECTIONS block, so a naive mutation would edit E19's section-level
        // ruling and prove nothing about a row.
        var mutated = ManifestFiles.WithArtEdit(
            "\"idSource\": \"convention\",\n      \"cut\": \"O8 — procedural in-engine, ruled 2026-08-12\"",
            "\"idSource\": \"convention\",\n      \"cut\": null");

        mutated.ActiveArt.Count().ShouldBe(943, "one VFX sheet is no longer cut");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i => i.Code == ManifestIssueCode.CountMismatch && i.Location == "totals/cut");
        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.InconsistentRow &&
            i.Location == "sections/E19" &&
            i.Message.Contains("partly-cut section", StringComparison.Ordinal));
    }

    [Fact]
    public void An_atlas_whose_stored_count_drifted_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"id\": \"atlas_pets\",\n      \"contents\": \"All 24 pets\",\n      \"assetCount\": 48",
            "\"id\": \"atlas_pets\",\n      \"contents\": \"All 24 pets\",\n      \"assetCount\": 47");

        Only(mutated, ManifestIssueCode.CountMismatch, "atlases/atlas_pets/assetCount")
            .Message.ShouldContain("records 47 but the data holds 48", Case.Sensitive);
    }

    [Fact]
    public void An_asset_packed_into_an_atlas_15_D2_never_declared_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit("\"atlas\": \"atlas_hero\"", "\"atlas\": \"atlas_heroes\"");

        // 🔒 Located on the row that was mutated, not merely "some row somewhere": the same edit
        // also drifts atlas_hero's stored count, and an unlocated assertion would not distinguish
        // the undeclared-atlas rule from anything else that emits InconsistentRow.
        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.InconsistentRow &&
            i.Location == "chr_hero_armor_leathers_a" &&
            i.Message.Contains("atlas_heroes", StringComparison.Ordinal) &&
            i.Message.Contains("15 §D2 does not declare", StringComparison.Ordinal));
    }

    /// <summary>A biome-scoped row must carry that biome's locked six from `15` §A5, not another's.</summary>
    [Fact]
    public void A_row_carrying_the_wrong_biome_palette_is_reported()
    {
        // 🔒 Anchored on the row's own `biome`/`palette` pair. The bare `"base": "#5FBF5F"` string
        // first occurs in the BIOMES header block, so a naive mutation would redefine greenwood
        // itself and fire this rule from all 112 of its rows at once — proving the header check,
        // not the row check this case is named for.
        var mutated = ManifestFiles.WithArtEdit(
            "\"biome\": \"greenwood\",\n      \"palette\": {\n        \"base\": \"#5FBF5F\",",
            "\"biome\": \"greenwood\",\n      \"palette\": {\n        \"base\": \"#C4462A\",");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.InconsistentRow &&
            i.Location == "chr_enemy_greenwood_brute_attack" &&
            i.Message.Contains("locked six from 15 §A5", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_naming_a_biome_the_manifest_never_declared_is_reported()
    {
        var mutated = ManifestFiles.WithArtEdit(
            "\"biome\": \"greenwood\"", "\"biome\": \"greenwoode\"");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.InconsistentRow &&
            i.Location == "chr_enemy_greenwood_brute_attack" &&
            i.Message.Contains("greenwoode", StringComparison.Ordinal) &&
            i.Message.Contains("biomes block does not declare", StringComparison.Ordinal));
    }

    [Fact]
    public void An_audio_family_whose_stored_count_drifted_is_reported()
    {
        var mutated = ManifestFiles.WithAudioEdit(
            "\"id\": \"dice\",\n      \"label\": \"Dice\",\n      \"claimedCount\": 11,\n      \"transcribedCount\": 11",
            "\"id\": \"dice\",\n      \"label\": \"Dice\",\n      \"claimedCount\": 11,\n      \"transcribedCount\": 10");

        // 🔒 Two distinct rules emit CountMismatch under `families/dice`: the stored-vs-actual
        // compare at `families/dice/transcribedCount`, and the countsAgree-vs-arithmetic check at
        // `families/dice`. A prefix filter plus a bare code assertion could not tell them apart, so
        // this pins the exact location and message of the one the case is named for — and pins the
        // second one too, since this edit is supposed to trip both.
        Only(mutated, ManifestIssueCode.CountMismatch, "families/dice/transcribedCount")
            .Message.ShouldContain("records 10 but the data holds 11", Case.Sensitive);

        Only(mutated, ManifestIssueCode.CountMismatch, "families/dice")
            .Message.ShouldContain("stores countsAgree=True", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Doc 20's totals all agree today, so <c>DSC_AUDIO_TOTALS</c> is deliberately absent. Break
    /// the agreement and its absence must be reported — otherwise "we checked and it agreed" and
    /// "we never checked" look identical in the artefact.
    /// </summary>
    [Fact]
    public void An_audio_total_that_starts_disagreeing_with_no_record_is_reported()
    {
        var mutated = ManifestFiles.WithAudioEdit("\"claimedSfx\": 94", "\"claimedSfx\": 95");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.UnrecordedDiscrepancy &&
            i.Location == "DSC_AUDIO_TOTALS" &&
            i.Message.Contains("disagree", StringComparison.Ordinal));
    }

    [Fact]
    public void An_audio_row_whose_kind_and_id_prefix_disagree_is_reported()
    {
        var mutated = ManifestFiles.WithAudioEdit(
            "\"id\": \"sfx_die_pickup\",\n      \"kind\": \"sfx\"",
            "\"id\": \"sfx_die_pickup\",\n      \"kind\": \"mus\"");

        var issues = ManifestValidator.Validate(mutated);

        issues.ShouldContain(i =>
            i.Code == ManifestIssueCode.InconsistentRow &&
            i.Location == "sfx_die_pickup" &&
            i.Message.Contains("does not start with 'mus_'", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 A missing required member must throw at read time, not become <c>default</c>.
    /// `game-data/README.md`: a hole filled with a plausible value is invisible.
    /// </summary>
    [Fact]
    public void A_missing_required_member_fails_loudly_at_read_time_rather_than_defaulting()
    {
        var broken = ManifestFiles.ArtJson()
            .Replace("\"derived\": false", "\"absent\": false", StringComparison.Ordinal);

        var thrown = Should.Throw<AssetManifestFormatException>(
            () => AssetManifestReader.LoadFrom(broken, ManifestFiles.AudioJson()));

        thrown.Message.ShouldContain("has no member 'derived'", Case.Sensitive);
        thrown.Message.ShouldContain("would hide the hole", Case.Sensitive);
    }

    /// <summary>A mutation that matches nothing is itself a failure — the guard on the guards.</summary>
    [Fact]
    public void A_mutation_that_no_longer_applies_fails_instead_of_testing_nothing()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => ManifestFiles.WithArtEdit("\"fieldThatDoesNotExist\": 1", "x"));

        thrown.Message.ShouldContain("matches nothing", Case.Sensitive);
        thrown.Message.ShouldContain("would prove nothing", Case.Sensitive);
    }

    private static ManifestIssue Only(
        AssetManifestSet manifest, ManifestIssueCode code, string location)
    {
        var issues = ManifestValidator.Validate(manifest);

        return issues.SingleOrDefault(i => i.Code == code && i.Location == location)
               ?? throw new ShouldAssertException(
                   $"expected exactly one {code} at '{location}', got:\n" +
                   string.Join("\n", issues.Select(i => i.ToString())));
    }
}
