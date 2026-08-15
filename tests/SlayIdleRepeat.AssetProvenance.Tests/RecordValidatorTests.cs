using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>The field rules that make a record auditable by somebody who was not in the generation session.</summary>
public sealed class RecordValidatorTests
{
    [Fact]
    public void The_three_well_formed_kinds_validate_clean()
    {
        RecordValidator.Validate(ProvenanceFixtures.Midjourney(), AssetMedium.Art).ShouldBeEmpty();
        RecordValidator.Validate(ProvenanceFixtures.Procedural(), AssetMedium.Art).ShouldBeEmpty();
        RecordValidator.Validate(ProvenanceFixtures.Cc0(), AssetMedium.Audio).ShouldBeEmpty();
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
    public void A_blank_15_B0_parameter_is_reported_by_name(string member)
    {
        var record = Blank(ProvenanceFixtures.Midjourney(), member);

        var problems = RecordValidator.Validate(record, AssetMedium.Art);

        // Steering S2 — the message names the member, so a case cannot pass because a DIFFERENT
        // field happened to be blank.
        problems.ShouldHaveSingleItem().ShouldMatchWildcard($"*{member} is blank*");
    }

    [Theory]
    [InlineData("01-09-2026")]
    [InlineData("2026-9-1")]
    [InlineData("Sept 2026")]
    [InlineData("")]
    public void A_date_that_is_not_ISO_8601_is_rejected(string date)
    {
        RecordValidator.Validate(ProvenanceFixtures.Midjourney() with { Date = date }, AssetMedium.Art)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard($"*date is '{date}'*not an ISO-8601 YYYY-MM-DD date*");
    }

    [Theory]
    [InlineData("8f9267b")]
    [InlineData("milestone/M8")]
    [InlineData("8F9267B0000000000000000000000000000000AB")]
    public void A_procedural_record_needs_a_full_40_hex_commit(string commit)
    {
        RecordValidator.Validate(ProvenanceFixtures.Procedural() with { RepoCommit = commit }, AssetMedium.Art)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard($"*repoCommit is '{commit}'*not a full 40-hex commit*");
    }

    [Fact]
    public void A_procedural_record_may_declare_no_parameters_but_not_omit_the_member()
    {
        RecordValidator.Validate(
                ProvenanceFixtures.Procedural() with
                {
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal),
                },
                AssetMedium.Art)
            .ShouldBeEmpty();

        // The other half of the sentence. Unreachable through ProvenanceStore, which throws on an
        // absent member — but reachable by any caller that builds a record in memory, which is
        // what a generator does before it writes one.
        RecordValidator.Validate(
                ProvenanceFixtures.Procedural() with { Parameters = null! }, AssetMedium.Art)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard("*parameters is absent*a claim about nothing*");
    }

    [Theory]
    [InlineData("kenney.nl/assets/ui-audio")]
    [InlineData("../packs/ui-audio")]
    [InlineData("ftp://example.invalid/pack.zip")]
    public void A_cc0_record_needs_an_absolute_http_url_somebody_else_can_check(string url)
    {
        RecordValidator.Validate(ProvenanceFixtures.Cc0() with { Url = url }, AssetMedium.Audio)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard($"*url is '{url}'*not an absolute http(s) URL*");
    }

    /// <summary>An AUDIO record must name its tool and version, in both directions: required on audio, forbidden on art.</summary>
    [Fact]
    public void Audio_requires_20_s2_1s_tool_and_version()
    {
        RecordValidator.Validate(ProvenanceFixtures.Cc0() with { Tooling = null }, AssetMedium.Audio)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard("*audio, and it carries no tool/toolVersion*20 §2.1*");
    }

    /// <inheritdoc cref="Audio_requires_20_s2_1s_tool_and_version"/>
    [Fact]
    public void Art_must_not_carry_a_second_tool_field_beside_its_kind()
    {
        RecordValidator.Validate(
                ProvenanceFixtures.Midjourney() with { Tooling = new AudioTooling("Photoshop", "26.0") },
                AssetMedium.Art)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard("*art, and it carries tool='Photoshop'*second source of truth*");
    }

    [Theory]
    [InlineData("", "3.5.1", "tool")]
    [InlineData("Audacity", "  ", "toolVersion")]
    public void A_blank_half_of_the_tool_pair_is_reported_by_name(string tool, string version, string member)
    {
        RecordValidator.Validate(
                ProvenanceFixtures.Cc0() with { Tooling = new AudioTooling(tool, version) },
                AssetMedium.Audio)
            .ShouldHaveSingleItem()
            .ShouldMatchWildcard($"*{member} is blank*");
    }

    private static MidjourneyProvenance Blank(MidjourneyProvenance record, string member) => member switch
    {
        "jobId" => record with { JobId = "" },
        "prompt" => record with { Prompt = "   " },
        "seed" => record with { Seed = "" },
        "sref" => record with { Sref = "" },
        "aspectRatio" => record with { AspectRatio = "" },
        "style" => record with { Style = "" },
        "stylize" => record with { Stylize = "" },
        "modelVersion" => record with { ModelVersion = "" },
        _ => throw new ArgumentOutOfRangeException(
            nameof(member),
            member,
            "This helper must blank the member the case names. A silent fall-through would blank " +
            "nothing and the case would pass over an intact record."),
    };
}
