using Shouldly;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>
/// The authored template catalogue: what ops may send, and every send it refuses before a message is
/// written.
/// </summary>
public sealed class MailTemplateCatalogueTests
{
    private static MailTemplateCatalogue Catalogue() => InboxWorlds.Catalogue();

    private static IReadOnlyList<MailSendRefusal> Refusals(
        string templateId, IReadOnlyDictionary<string, string>? parameters = null) =>
        Catalogue().Check(templateId, parameters ?? InboxWorlds.CompensationParams())
            .Select(r => r.Refusal)
            .ToArray();

    [Fact]
    public void The_shipped_catalogue_reads_out_of_the_content_set()
    {
        var templates = Catalogue().Templates;

        templates.ShouldNotBeEmpty(
            "an empty catalogue would refuse every send with 'no such template' and every case " +
            "below would pass for the wrong reason.");
        templates.ShouldAllBe(
            t => t.Body.StartsWith("loc.mail.", StringComparison.Ordinal),
            "a template's identity IS its body's loc key, which is what makes the content set name " +
            "the string and keeps it out of the orphan sweep.");
    }

    [Fact]
    public void No_milestone_template_is_authored()
    {
        Catalogue().Templates.ShouldNotContain(
            t => t.Category == MessageCategory.MILESTONE,
            "the one thing that would send a MILESTONE message is the expiry job's 'only if the " +
            "value is material', and nothing anywhere authors what material means. Copy written for " +
            "a sender that does not exist is the plausible-looking hole this repository refuses.");
    }

    [Fact]
    public void A_well_formed_send_is_not_refused()
    {
        Refusals(InboxWorlds.CompensationTemplate).ShouldBeEmpty(
            "the positive control: without it every refusal case below would pass against a " +
            "catalogue that refused everything.");
    }

    [Fact]
    public void A_template_nobody_authored_is_refused()
    {
        Refusals("loc.mail.compensation.invented").ShouldBe(
            new[] { MailSendRefusal.UNKNOWN_TEMPLATE },
            "a message is a template and typed parameters, never a body somebody typed.");
    }

    [Fact]
    public void A_send_that_leaves_a_parameter_out_is_refused()
    {
        var partial = new Dictionary<string, string>(StringComparer.Ordinal) { ["hours"] = "3" };

        Refusals(InboxWorlds.CompensationTemplate, partial).ShouldBe(
            new[] { MailSendRefusal.MISSING_PARAM },
            "the template's own text names {dateUtc}. Sending without it renders the sentence with " +
            "a hole in it, to everybody the predicate selected.");
    }

    [Fact]
    public void A_send_that_supplies_a_parameter_the_template_does_not_name_is_refused()
    {
        var extra = new Dictionary<string, string>(InboxWorlds.CompensationParams(), StringComparer.Ordinal)
        {
            ["operator"] = "someone",
        };

        Refusals(InboxWorlds.CompensationTemplate, extra).ShouldBe(
            new[] { MailSendRefusal.UNEXPECTED_PARAM },
            "it would be carried by every stored row and rendered nowhere — and a value an operator " +
            "believes reached the player is worse than one they know did not.");
    }

    [Theory]
    [InlineData("three")]
    [InlineData("3.5")]
    [InlineData("")]
    public void A_parameter_of_the_wrong_type_is_refused(string hours)
    {
        var wrong = new Dictionary<string, string>(InboxWorlds.CompensationParams(), StringComparer.Ordinal)
        {
            ["hours"] = hours,
        };

        Refusals(InboxWorlds.CompensationTemplate, wrong).ShouldBe(
            new[] { MailSendRefusal.MALFORMED_PARAM },
            "the template declares hours as an INTEGER. The types exist so a send that would render " +
            "'unavailable for three.5 hours' is refused by the tool rather than by 300,000 players.");
    }

    [Theory]
    [InlineData(MailParamType.TEXT, "a subject", true)]
    [InlineData(MailParamType.TEXT, "   ", false)]
    [InlineData(MailParamType.INTEGER, "-7", true)]
    [InlineData(MailParamType.INTEGER, "7,5", false)]
    [InlineData(MailParamType.DATE_UTC, "2026-03-14T18:00:00Z", true)]
    [InlineData(MailParamType.DATE_UTC, "14/03/2026", false)]
    public void Each_parameter_type_accepts_exactly_its_own_values(
        MailParamType type, string value, bool wellTyped)
    {
        MailTemplateCatalogue.IsWellTyped(type, value).ShouldBe(
            wellTyped,
            "'7,5' is a well-formed number in de-DE and this check must not depend on which host " +
            "the tool is run from; '14/03/2026' is a date in one locale and a fault in another.");
    }

    [Fact]
    public void A_parameter_type_nobody_taught_it_to_check_is_refused_rather_than_accepted()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => MailTemplateCatalogue.IsWellTyped((MailParamType)99, "anything"))
            .Message.ShouldContain(
                "without being given a way to be checked",
                Case.Sensitive,
                "a default of 'well typed' would accept every value of the new type, which is the " +
                "one direction a type check must never fail in.");
    }

    [Fact]
    public void The_shipping_locale_is_the_one_the_templates_must_resolve_in()
    {
        MailTemplateCatalogue.ShippingLocale.ShouldBe(
            "en",
            "the localisation ruling makes EN the authored source and every DE value an " +
            "untranslated placeholder awaiting a named human localiser. A DE-parity send gate would " +
            "refuse every message this game can currently send.");
    }

    [Fact]
    public void Every_authored_template_resolves_in_the_shipping_locale()
    {
        foreach (var template in Catalogue().Templates)
        {
            Worlds.Content
                .ReadText("loc/" + MailTemplateCatalogue.ShippingLocale + ".json#/strings/" + template.Body)
                .ShouldNotBeNullOrWhiteSpace(
                    "'" + template.Body + "' is authored in the catalogue and must have text, or a " +
                    "player is shown the machinery instead of a message.");

            Worlds.Content
                .ReadText("loc/" + MailTemplateCatalogue.ShippingLocale + ".json#/strings/" + template.Title)
                .ShouldNotBeNullOrWhiteSpace("…and so must its subject line: " + template.Title);
        }
    }
}
