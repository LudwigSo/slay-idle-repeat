using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// `15` §G and `20` §6 — <em>"Commercial licence for each tool confirmed in writing"</em>, and the
/// one property of that record that matters most: <b>unset never reads as confirmed</b>.
/// </summary>
/// <remarks>
/// 🔒 M8-01b is ⛔ and the product owner owns it. Nothing an agent can run may set
/// <c>confirmedInWriting</c>, and no code path in this assembly may default it. Steering S6: the
/// hole stays a hole, greppable, and is never coerced at read time.
/// </remarks>
public sealed class ToolLicenceTests
{
    [Fact]
    public void The_shipped_register_declares_midjourney_and_leaves_its_confirmation_unset()
    {
        var midjourney = ProvenanceFixtures.ShippedLicences.ShouldHaveSingleItem();

        // 🔒 Against the CONSTANT, not the literal. MidjourneyProvenance.KindName is both the
        // record's discriminator and the key ToolsNamed matches against this register; renaming it
        // on one side alone would turn every Midjourney record into UnknownTool — a different,
        // quieter failure than the licence rule this file is about.
        midjourney.Tool.ShouldBe(MidjourneyProvenance.KindName);
        midjourney.Tool.ShouldBe("midjourney");
        midjourney.AppliesTo.ShouldBe("art");

        // 🔒 The whole point. Not false — UNSET.
        midjourney.ConfirmedInWriting.ShouldBeNull(
            "M8-01b is the product owner's and it is not done. An agent setting this is out of scope " +
            "by ruling, and a default that made it non-null would be the claim 15 §G calls a formality.");
        midjourney.ConfirmationRef.ShouldBeNull();
        midjourney.IsConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void An_unset_confirmation_is_not_readable_as_consent()
    {
        var unset = new ToolLicence("midjourney", "art", null, null, "nobody has confirmed anything");

        unset.IsConfirmed.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => unset.RequireConfirmedInWriting())
            .Message.ShouldMatchWildcard(
                "*not confirmed in writing (confirmedInWriting=<unset>*M8-01b*do not read an absent value as consent*");
    }

    /// <summary>
    /// 🔒 `15` §G calls the bare claim a formality. A <c>true</c> with nothing filed behind it is
    /// exactly that, so it is not a confirmation.
    /// </summary>
    [Fact]
    public void A_true_with_no_written_reference_behind_it_is_not_a_confirmation()
    {
        var bare = new ToolLicence("midjourney", "art", true, null, "someone ticked a box");

        bare.IsConfirmed.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => bare.RequireConfirmedInWriting());
    }

    [Fact]
    public void A_confirmation_with_a_written_reference_is_accepted()
    {
        var confirmed = ProvenanceFixtures.ConfirmedLicence();

        confirmed.IsConfirmed.ShouldBeTrue();
        confirmed.RequireConfirmedInWriting().ShouldBe("docs/legal/midjourney-terms-2026-09-01.pdf");
    }

    [Fact]
    public void An_explicit_false_is_reported_as_false_rather_than_as_unset()
    {
        // Steering S2 — the message must distinguish "nobody looked" from "we looked and the
        // answer was no". They call for different actions.
        Should.Throw<InvalidOperationException>(
                () => new ToolLicence("suno", "audio", false, null, "terms reviewed and declined")
                    .RequireConfirmedInWriting())
            .Message.ShouldMatchWildcard("*confirmedInWriting=false*");
    }

    [Fact]
    public void Two_rows_for_one_tool_are_a_loud_failure()
    {
        Should.Throw<ProvenanceFormatException>(() => new ToolLicenceRegister(
                [
                    new ToolLicence("midjourney", "art", null, null, "one"),
                    new ToolLicence("midjourney", "art", true, "somewhere", "two"),
                ]))
            .Message.ShouldMatchWildcard("*declares the tool 'midjourney' twice*two answers*");
    }

    /// <summary>
    /// `20` §2.1 — the audio tools are deliberately absent from the register. No Suno, no Udio, no
    /// ElevenLabs licence is held (M8 kickoff, 2026-08-12), and a row with no confirmation would
    /// read as a shortlist somebody had settled on.
    /// </summary>
    [Fact]
    public void No_audio_generation_tool_is_declared_because_none_is_licensed()
    {
        var tools = ProvenanceFixtures.ShippedLicences.Select(l => l.Tool).ToArray();

        // Case-insensitively: the claim is that no audio generator is declared, and a row keyed
        // 'Suno' would satisfy an ordinal ShouldNotContain while falsifying the claim.
        foreach (var banned in new[] { "suno", "udio", "elevenlabs" })
        {
            tools.ShouldNotContain(
                t => t.Equals(banned, StringComparison.OrdinalIgnoreCase),
                $"no licence is held for {banned} (M8 kickoff, 2026-08-12).");
        }

        // 🔒 Shouldly's ShouldAllBe passes on an EMPTY collection, so the floor comes first.
        tools.Length.ShouldBeGreaterThanOrEqualTo(1);
        ProvenanceFixtures.ShippedLicences.ShouldAllBe(l => l.AppliesTo == "art");
    }

    [Fact]
    public void An_absent_confirmedInWriting_member_parses_as_null_rather_than_false()
    {
        var register = ProvenanceStore.ReadLicences(
            """
            { "licences": [ { "tool": "t", "appliesTo": "art", "note": "no confirmation member at all" } ] }
            """);

        register.Find("t")!.ConfirmedInWriting.ShouldBeNull();
        register.Unconfirmed.Count().ShouldBe(1);
    }
}
