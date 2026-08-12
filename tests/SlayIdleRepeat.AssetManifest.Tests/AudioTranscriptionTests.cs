using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetManifest.Tests;

/// <summary>
/// The audio register against doc `20`. 🔒 Every expected number below was counted from `20`'s own
/// id lists, not copied from its stated totals — the point of the exercise was to check the claim,
/// and doc 20's claims all survived it.
/// </summary>
public sealed class AudioTranscriptionTests
{
    /// <summary>`20` §4.1–§4.6 family headings vs the ids each family actually lists.</summary>
    [Theory]
    [InlineData("dice", "20 §4.1", 11, 11)]
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

    /// <summary>
    /// 🔒 Doc 20's arithmetic checks out exactly — 11+12+26+8+18+19 = 94 SFX, plus 12 music = 106.
    /// Unlike `15` §E20, nothing here needed recording as a discrepancy.
    /// </summary>
    [Fact]
    public void Doc_20_claims_94_SFX_and_106_total_and_both_are_correct()
    {
        var totals = ManifestFiles.Shipped.Audio.Totals;

        totals.ClaimedSfx.ShouldBe(94);
        totals.TranscribedSfx.ShouldBe(94);
        totals.ClaimedMusic.ShouldBe(12);
        totals.TranscribedMusic.ShouldBe(12);
        totals.ClaimedCombined.ShouldBe(106);
        totals.TranscribedCombined.ShouldBe(106);

        ManifestFiles.Shipped.Audio.Families
            .Where(f => f.Id != "music")
            .Sum(f => f.TranscribedCount)
            .ShouldBe(94);
    }

    [Fact]
    public void No_audio_family_disagrees_with_doc_20()
    {
        ManifestFiles.Shipped.Audio.Families
            .Where(f => !f.CountsAgree)
            .ShouldBeEmpty();
    }

    /// <summary>`20` §3's twelve tracks, with the loop length the table states for each.</summary>
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

    /// <summary>Every SFX carries `20` §5's source/shipping format.</summary>
    [Fact]
    public void Every_sfx_carries_the_doc_20_section_5_format()
    {
        var sfx = ManifestFiles.Shipped.Audio.Assets.Where(a => !a.IsMusic).ToArray();

        sfx.Length.ShouldBe(94);
        sfx.ShouldAllBe(a =>
            a.Format == "WAV 16-bit 44.1 kHz mono in source, converted to OGG q4 for shipping");
    }

    /// <summary>
    /// 🔒 `20` §5's Ducking row names exactly four. `20` §1's prose names only two ("the crit and
    /// level-up stingers"); the manifest follows §5, the more specific of the two, and records the
    /// difference as DSC_DUCKING_SET rather than silently picking a side.
    /// </summary>
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

    /// <summary>
    /// 🔒 The other 86 SFX state no duration, and the manifest does not infer one from §1's
    /// 60–400 ms band. A band is not a value.
    /// </summary>
    [Fact]
    public void SFX_without_a_stated_duration_carry_null_rather_than_the_section_1_band()
    {
        var sfx = ManifestFiles.Shipped.Audio.Assets.Where(a => !a.IsMusic).ToArray();

        sfx.Count(a => a.DurationSeconds is not null).ShouldBe(8);
        sfx.Count(a => a.DurationSeconds is null).ShouldBe(86);
    }

    /// <summary>
    /// 🔒 `20` §1 caps celebratory stingers at 1.2 s; §4.3's victory fanfare is 1.5 s. Transcribed
    /// as written and recorded, not clamped to the cap.
    /// </summary>
    [Fact]
    public void The_victory_stinger_exceeds_the_section_1_cap_and_that_is_recorded()
    {
        ManifestFiles.Shipped.RequireAudio("sfx_victory").DurationSeconds.ShouldBe(1.5);

        var record = ManifestFiles.Shipped.Audio.Discrepancies
            .SingleOrDefault(d => d.Id == "DSC_STINGER_LENGTH")
            .ShouldNotBeNull();

        record.Observed.ShouldContain("1.5", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Eight SFX have no descriptor of their own because doc 20 attaches none — the id shares a
    /// '·' segment with a neighbour, or the pair is written `a / b`. They stay null and greppable
    /// rather than inheriting a neighbour's text, which would be a ruling, not a transcription.
    /// </summary>
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

    /// <summary>
    /// 🔒 groupDescriptor is reserved for a human ruling that a descriptor covers a run of ids.
    /// No such ruling exists, so it is null throughout — inventing one is not transcription.
    /// </summary>
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

        audio.Assets.Count.ShouldBe(106);
        audio.Families.Count.ShouldBe(7);
        audio.Discrepancies.Count.ShouldBeGreaterThan(3);
        audio.Status.ShouldBe("transcribed");
    }
}
