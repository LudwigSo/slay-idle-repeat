using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The audio register against the design doc. Every expected number below was counted from the
/// doc's own id lists, not copied from its stated totals — the point was to check the claim.
/// </summary>
public sealed class AudioTranscriptionTests
{
    /// <summary>Doc family headings vs the ids each family actually lists.</summary>
    [Theory]
    [InlineData("dice", "20 §4.1", 3, 3)]
    [InlineData("board_movement", "20 §4.2", 12, 12)]
    [InlineData("combat", "20 §4.3", 26, 26)]
    [InlineData("pets_mounts", "20 §4.4", 8, 8)]
    [InlineData("meta_progression", "20 §4.5", 18, 18)]
    [InlineData("ui", "20 §4.6", 19, 19)]
    [InlineData("music", "20 §3", 12, 12)]
    public void Each_family_carries_the_doc_20_claim_and_the_ids_it_actually_lists(
        string familyId, string source, int claimed, int listed)
    {
        var family = ManifestFiles.Shipped.Audio.Families
            .SingleOrDefault(f => f.Id == familyId)
            .ShouldNotBeNull($"doc 20 declares {familyId}");

        family.SourceSection.ShouldBe(source);
        family.ClaimedCount.ShouldBe(claimed);
        family.TranscribedCount.ShouldBe(listed);
        family.CountsAgree.ShouldBeTrue();
        ManifestFiles.Shipped.AudioInFamily(familyId).Count().ShouldBe(listed);
    }

    /// <summary>The doc's arithmetic checked out exactly: 11+12+26+8+18+19 = 94 SFX, plus 12 music = 106. ⚠️ Doc 20 claims 94 SFX and 106 combined. Eight dice-family rows are gone with the die's special faces and the reroll — the five per-face landings, sfx_reroll, sfx_nudge and sfx_face_upgrade — so the shipped register is 86 and 98.</summary>
    [Fact]
    public void The_register_carries_86_SFX_and_98_total_and_the_claim_agrees()
    {
        var totals = ManifestFiles.Shipped.Audio.Totals;

        totals.ClaimedSfx.ShouldBe(86);
        totals.TranscribedSfx.ShouldBe(86);
        totals.ClaimedMusic.ShouldBe(12);
        totals.TranscribedMusic.ShouldBe(12);
        totals.ClaimedCombined.ShouldBe(98);
        totals.TranscribedCombined.ShouldBe(98);

        ManifestFiles.Shipped.Audio.Families
            .Where(f => f.Id != "music")
            .Sum(f => f.TranscribedCount)
            .ShouldBe(86);
    }

    /// <summary>S3 floor first: "no family disagrees" is true of an empty family list too.</summary>
    [Fact]
    public void No_audio_family_disagrees_with_doc_20()
    {
        var families = ManifestFiles.Shipped.Audio.Families;

        families.Count.ShouldBe(7, "20 §3 plus §4.1–§4.6");
        families.Where(f => !f.CountsAgree).ShouldBeEmpty();
        families.ShouldAllBe(f => f.ClaimedCount == f.TranscribedCount);
    }

    /// <summary>The twelve music tracks, with the loop length the doc states for each.</summary>
    [Theory]
    [InlineData("mus_home", "Home / Camp", 120)]
    [InlineData("mus_ch1_greenwood", "Chapter 1 board", 110)]
    [InlineData("mus_ch2_mire", "Chapter 2 board", 110)]
    [InlineData("mus_ch3_crypt", "Chapter 3 board", 110)]
    [InlineData("mus_ch4_ember", "Chapter 4 board", 110)]
    [InlineData("mus_ch5_frost", "Chapter 5 board", 110)]
    [InlineData("mus_ch6_clockwork", "Chapter 6 board", 110)]
    [InlineData("mus_ch7_bloom", "Chapter 7 board", 110)]
    [InlineData("mus_ch8_astral", "Chapter 8 board", 130)]
    [InlineData("mus_boss", "All bosses", 100)]
    [InlineData("mus_boss_final", "The Dicelord only", 130)]
    [InlineData("mus_arena", "Arena / PvP", 100)]
    public void Every_music_track_matches_doc_20_section_3(string id, string use, int seconds)
    {
        var track = ManifestFiles.Shipped.RequireAudio(id);

        track.IsMusic.ShouldBeTrue();
        track.Use.ShouldBe(use);
        track.LengthSeconds.ShouldBe(seconds);
        track.Descriptor.ShouldNotBeNullOrWhiteSpace();
        track.Format.ShouldBe("OGG Vorbis, q6, 44.1 kHz stereo", "20 §5");
    }

    /// <summary>Every SFX carries the doc's source/shipping format.</summary>
    [Fact]
    public void Every_sfx_carries_the_doc_20_section_5_format()
    {
        var sfx = ManifestFiles.Shipped.Audio.Assets.Where(a => !a.IsMusic).ToArray();

        sfx.Length.ShouldBe(86);
        sfx.ShouldAllBe(a =>
            a.Format == "WAV 16-bit 44.1 kHz mono in source, converted to OGG q4 for shipping");
    }

    /// <summary>The doc's table names four ducking SFX where its prose names only two; the manifest follows the more specific table and records the conflict as DSC_DUCKING_SET.</summary>
    [Fact]
    public void Exactly_the_four_SFX_in_doc_20_section_5_duck_the_music()
    {
        ManifestFiles.Shipped.Audio.Assets
            .Where(a => a.DucksMusic == true)
            .Select(a => a.Id)
            .ShouldBe(["sfx_crit", "sfx_levelup", "sfx_boss_phase", "sfx_merge_success"],
                ignoreOrder: true);

        ManifestFiles.Shipped.Audio.Discrepancies.ShouldContain(d => d.Id == "DSC_DUCKING_SET");
    }

    /// <summary>Durations doc 20 states inline in a descriptor are transcribed; the rest are null.</summary>
    [Theory]
    [InlineData("sfx_die_tumble", 0.8)]
    [InlineData("sfx_stage_gate", 1.2)]
    [InlineData("sfx_campfire", 1.0)]
    [InlineData("sfx_boss_telegraph", 1.2)]
    [InlineData("sfx_victory", 1.5)]
    [InlineData("sfx_levelup", 1.2)]
    [InlineData("sfx_merge_charge", 0.9)]
    [InlineData("sfx_mount_summon", 0.8)]
    public void Inline_durations_are_transcribed(string id, double seconds)
    {
        ManifestFiles.Shipped.RequireAudio(id).DurationSeconds.ShouldBe(seconds);
    }

    /// <summary>The other 78 SFX state no duration; the manifest does not infer one from the doc's stated band — a band is not a value.</summary>
    [Fact]
    public void SFX_without_a_stated_duration_carry_null_rather_than_the_section_1_band()
    {
        var sfx = ManifestFiles.Shipped.Audio.Assets.Where(a => !a.IsMusic).ToArray();

        sfx.Count(a => a.DurationSeconds is not null).ShouldBe(8);
        sfx.Count(a => a.DurationSeconds is null).ShouldBe(78);
    }

    /// <summary>The doc caps celebratory stingers at 1.2s, but the victory fanfare is 1.5s — transcribed as written, not clamped to the cap.</summary>
    [Fact]
    public void The_victory_stinger_exceeds_the_section_1_cap_and_that_is_recorded()
    {
        ManifestFiles.Shipped.RequireAudio("sfx_victory").DurationSeconds.ShouldBe(1.5);

        var record = ManifestFiles.Shipped.Audio.Discrepancies
            .SingleOrDefault(d => d.Id == "DSC_STINGER_LENGTH")
            .ShouldNotBeNull();

        record.Observed.ShouldContain("1.5", Case.Sensitive);
    }

    /// <summary>Eight SFX share a descriptor slot with a neighbour and get no descriptor of their own; they stay null rather than inheriting the neighbour's text, which would be a ruling, not a transcription.</summary>
    [Fact]
    public void The_eight_SFX_doc_20_never_describes_carry_a_null_descriptor()
    {
        ManifestFiles.Shipped.Audio.Assets
            .Where(a => !a.IsMusic && a.Descriptor is null)
            .Select(a => a.Id)
            .ShouldBe([
                "sfx_hit_light", "sfx_hit_medium", "sfx_hit_slash", "sfx_hit_pierce",
                "sfx_pet_zap", "sfx_pet_heal", "sfx_ui_toggle_on", "sfx_ui_toggle_off",
            ], ignoreOrder: true);

        ManifestFiles.Shipped.Audio.Totals.SfxWithoutDescriptor.ShouldBe(8);
        ManifestFiles.Shipped.Audio.Discrepancies.ShouldContain(d => d.Id == "DSC_SHARED_DESCRIPTORS");
    }

    /// <summary>groupDescriptor is reserved for a human ruling that a descriptor covers a run of ids; none exists yet, so it is null throughout.</summary>
    [Fact]
    public void No_row_claims_a_group_descriptor_nobody_has_ruled()
    {
        ManifestFiles.Shipped.Audio.Assets.ShouldAllBe(a => a.GroupDescriptor == null);
        ManifestFiles.Shipped.Audio.Assets.ShouldNotBeEmpty();
    }

    /// <summary>S3 floor.</summary>
    [Fact]
    public void The_audio_register_is_populated()
    {
        var audio = ManifestFiles.Shipped.Audio;

        audio.Assets.Count.ShouldBe(98);
        audio.Families.Count.ShouldBe(7);
        audio.Discrepancies.Count.ShouldBeGreaterThan(3);
        audio.Status.ShouldBe("transcribed");
    }
}
