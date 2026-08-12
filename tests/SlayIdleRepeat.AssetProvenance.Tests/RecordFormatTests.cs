using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// The record format itself: the three kinds `15` §B0 / `20` §2 need, and the loud failures that
/// keep a half-written record out of the store.
/// </summary>
public sealed class RecordFormatTests
{
    [Fact]
    public void A_midjourney_record_carries_every_field_15_B0_tabulates()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney());
        var read = (MidjourneyProvenance)ProvenanceStore.ReadRecord(json, ProvenanceFixtures.ArtId);

        read.Kind.ShouldBe("midjourney");
        read.JobId.ShouldBe("9f0e3a12-7c44-4c1e-9a6f-2b1d0c5e8a77");
        read.Seed.ShouldBe("1477201933");
        read.Sref.ShouldBe("3821991");
        read.AspectRatio.ShouldBe("1:1");
        read.Style.ShouldBe("raw");
        read.Stylize.ShouldBe("250");
        read.ModelVersion.ShouldBe("v6.1");
        read.Date.ShouldBe("2026-09-01");
        read.Prompt.ShouldBe("chibi cartoon fantasy game art, 2.5 heads tall proportions");
    }

    [Fact]
    public void A_procedural_record_carries_a_generator_and_a_commit_where_B0_puts_a_job_and_a_seed()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Procedural());
        var read = (ProceduralProvenance)ProvenanceStore.ReadRecord(json, ProvenanceFixtures.OtherArtId);

        read.Kind.ShouldBe("procedural");
        read.Generator.ShouldBe("SlayIdleRepeat.PlaceholderGenerator");
        read.RepoCommit.ShouldBe(ProvenanceFixtures.SomeCommit);
        read.Parameters.Count.ShouldBe(2);
        read.Parameters["width"].ShouldBe("512");

        // 🔒 The fields that only fit a generated asset are not on this type at all — they cannot
        // be left blank here, because there is nowhere to leave them.
        read.GetType().GetProperty("Seed").ShouldBeNull();
        read.GetType().GetProperty("JobId").ShouldBeNull();
    }

    [Fact]
    public void A_cc0_record_carries_its_source_url_licence_and_retrieval_date()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Cc0());
        var read = (Cc0Provenance)ProvenanceStore.ReadRecord(json, ProvenanceFixtures.AudioId);

        read.Kind.ShouldBe("cc0");
        read.Source.ShouldBe("Kenney UI Audio");
        read.Url.ShouldBe("https://kenney.nl/assets/ui-audio");
        read.Licence.ShouldBe("CC0-1.0");
        read.DateRetrieved.ShouldBe("2026-09-01");
    }

    [Fact]
    public void An_audio_record_round_trips_20_s2_1s_tool_and_version()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Cc0());

        json.ShouldContain("\"tool\": \"Audacity\"", Case.Sensitive);
        json.ShouldContain("\"toolVersion\": \"3.5.1\"", Case.Sensitive);

        ProvenanceStore.ReadRecord(json, ProvenanceFixtures.AudioId)
            .Tooling.ShouldBe(new AudioTooling("Audacity", "3.5.1"));
    }

    /// <summary>
    /// 🔒 There is no per-record "licence confirmed" member, and no writer emits one. The M8
    /// kickoff put that concept on the tool, unset, in <c>tool-licences.json</c>; a per-asset copy
    /// would be 1,080 places for a default to read as consent.
    /// </summary>
    [Fact]
    public void No_record_kind_carries_a_licence_confirmed_member()
    {
        var kinds = new ProvenanceRecord[]
        {
            ProvenanceFixtures.Midjourney(),
            ProvenanceFixtures.Procedural(),
            ProvenanceFixtures.Cc0(),
        };

        kinds.Length.ShouldBe(3);

        foreach (var record in kinds)
        {
            record.GetType().GetProperties()
                .Select(p => p.Name)
                .ShouldNotContain(
                    n => n.Contains("Licence", StringComparison.OrdinalIgnoreCase) &&
                         n.Contains("Confirm", StringComparison.OrdinalIgnoreCase),
                    $"{record.Kind} must not carry a per-record licence-confirmed member.");

            ProvenanceStore.WriteRecord(record)
                .ShouldNotContain("confirmed", Case.Insensitive);
        }
    }

    [Fact]
    public void A_record_filed_under_one_id_that_claims_another_is_a_loud_failure()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney());

        var thrown = Should.Throw<ProvenanceFormatException>(
            () => ProvenanceStore.ReadRecord(json, "some_other_id"));

        thrown.Location.ShouldBe("some_other_id");
        thrown.Message.ShouldMatchWildcard("*filed as 'some_other_id.json'*assetId member says*chr_hero_body_idle*");
    }

    [Theory]
    [InlineData("jobId")]
    [InlineData("prompt")]
    [InlineData("seed")]
    [InlineData("sref")]
    [InlineData("aspectRatio")]
    [InlineData("style")]
    [InlineData("stylize")]
    [InlineData("modelVersion")]
    [InlineData("date")]
    public void A_midjourney_record_missing_any_B0_member_fails_naming_that_member(string member)
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney())
            .Replace($"\"{member}\":", "\"someOtherName\":", StringComparison.Ordinal);

        var thrown = Should.Throw<ProvenanceFormatException>(
            () => ProvenanceStore.ReadRecord(json, ProvenanceFixtures.ArtId));

        // Steering S2 — pin WHICH member is missing, not merely that something was.
        thrown.Message.ShouldMatchWildcard($"*has no member '{member}'*");
    }

    [Fact]
    public void A_fourth_kind_is_rejected_rather_than_read_as_one_of_the_three()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney())
            .Replace("\"midjourney\"", "\"stable-diffusion\"", StringComparison.Ordinal);

        Should.Throw<ProvenanceFormatException>(() => ProvenanceStore.ReadRecord(json, ProvenanceFixtures.ArtId))
            .Message.ShouldMatchWildcard("*kind 'stable-diffusion'*not one of midjourney, procedural, cc0*");
    }

    /// <summary>
    /// 🔒 `20` §2.1 asks for the tool AND its version. Half of the pair is not a lesser record, it
    /// is a different fact, and the store refuses it rather than reading the missing half as null.
    /// </summary>
    [Theory]
    [InlineData("\"toolVersion\"", "\"unusedVersion\"")]
    [InlineData("\"tool\"", "\"unusedTool\"")]
    public void Half_of_20_s2_1s_tool_version_pair_is_refused(string member, string renamed)
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Cc0())
            .Replace(member, renamed, StringComparison.Ordinal);

        Should.Throw<ProvenanceFormatException>(() => ProvenanceStore.ReadRecord(json, ProvenanceFixtures.AudioId))
            .Message.ShouldMatchWildcard("*20 §2.1 asks for the tool AND its version*Write both, or neither*");
    }

    [Fact]
    public void A_record_that_is_not_json_names_the_asset_it_was_filed_under()
    {
        Should.Throw<ProvenanceFormatException>(
                () => ProvenanceStore.ReadRecord("{ not json", ProvenanceFixtures.ArtId))
            .Location.ShouldBe(ProvenanceFixtures.ArtId);
    }

    [Fact]
    public void A_wrong_typed_member_is_refused_rather_than_coerced()
    {
        var json = ProvenanceStore.WriteRecord(ProvenanceFixtures.Midjourney())
            .Replace("\"seed\": \"1477201933\"", "\"seed\": 1477201933", StringComparison.Ordinal);

        Should.Throw<ProvenanceFormatException>(() => ProvenanceStore.ReadRecord(json, ProvenanceFixtures.ArtId))
            .Message.ShouldMatchWildcard("*member 'seed' is Number*must be a string*");
    }

    [Fact]
    public void Two_records_for_one_asset_are_a_loud_failure_rather_than_a_last_one_wins()
    {
        Should.Throw<ProvenanceFormatException>(
                () => new ProvenanceRecordSet(
                    [ProvenanceFixtures.Midjourney(), ProvenanceFixtures.Procedural(ProvenanceFixtures.ArtId)],
                    ProvenanceFixtures.ShippedStore.Licences))
            .Message.ShouldMatchWildcard("*has two provenance records*no way to tell which*");
    }
}
