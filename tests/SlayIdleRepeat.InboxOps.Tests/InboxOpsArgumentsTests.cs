using Shouldly;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.InboxOps;
using Xunit;

namespace SlayIdleRepeat.InboxOps.Tests;

/// <summary>
/// The tool's command line: the safety gate that makes an economy-affecting send hard, and every
/// refusal that stops a malformed one before a database is opened.
/// </summary>
public sealed class InboxOpsArgumentsTests
{
    /// <summary>A well-formed send, with the one thing a case varies overridden.</summary>
    private static string[] Send(params string[] extra) =>
        new[]
        {
            "send",
            "--template", "loc.mail.compensation.outage.body",
            "--target", "PLAYER",
            "--players", "cohort.txt",
            "--operator", "ludwig",
        }.Concat(extra).ToArray();

    private static InboxOpsRequest Parsed(params string[] args)
    {
        var (request, errors) = InboxOpsArguments.Parse(args);

        request.ShouldNotBeNull("this command line should parse; it answered: " + string.Join("; ", errors));

        return request;
    }

    private static IReadOnlyList<string> Errors(params string[] args) =>
        InboxOpsArguments.Parse(args).Errors;

    // -------------------------------------------------------------------------- the safety gate

    [Fact]
    public void A_send_is_a_dry_run_unless_execute_is_typed()
    {
        Parsed(Send()).Execute.ShouldBeFalse(
            "there is no numeric rate limit on segment sends anywhere in the design set, and " +
            "inventing one would be a number nobody ruled on. What makes a wrong predicate hard to " +
            "send is that the dry run runs first and the operator has to come back.");
    }

    [Fact]
    public void Execute_is_explicit()
    {
        Parsed(Send("--execute")).Execute.ShouldBeTrue();
    }

    [Fact]
    public void An_operator_is_required()
    {
        var args = new[]
        {
            "send", "--template", "loc.mail.compensation.outage.body",
            "--target", "PLAYER", "--players", "cohort.txt",
        };

        Errors(args).ShouldContain(
            e => e.Contains("--operator is required", StringComparison.Ordinal),
            "a segment send is economy-affecting and must be attributable. ⚠️ The name is UNVERIFIED " +
            "— there is no operator identity system here — and a blank one is not even that.");
    }

    [Fact]
    public void A_candidate_file_is_required()
    {
        var args = new[]
        {
            "send", "--template", "loc.mail.compensation.outage.body",
            "--target", "ALL", "--operator", "ludwig",
        };

        Errors(args).ShouldContain(
            e => e.Contains("--players is required", StringComparison.Ordinal),
            "no adapter here enumerates player rows, so ALL means 'all of the candidates in this " +
            "file'. A send with none would reach nobody while reporting success.");
    }

    [Fact]
    public void A_target_is_required_rather_than_defaulted()
    {
        var args = new[]
        {
            "send", "--template", "loc.mail.compensation.outage.body",
            "--players", "cohort.txt", "--operator", "ludwig",
        };

        Errors(args).ShouldContain(
            e => e.Contains("--target is required", StringComparison.Ordinal),
            "defaulting it would pick who a grant reaches.");
    }

    [Fact]
    public void A_template_is_required()
    {
        var args = new[] { "send", "--target", "ALL", "--players", "c.txt", "--operator", "l" };

        Errors(args).ShouldContain(e => e.Contains("--template is required", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------ the payload

    [Fact]
    public void Parameters_are_read_as_name_equals_value()
    {
        Parsed(Send("--param", "hours=3", "--param", "dateUtc=2026-03-14")).Params
            .ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["hours"] = "3",
                ["dateUtc"] = "2026-03-14",
            });
    }

    [Fact]
    public void A_parameter_given_twice_is_refused()
    {
        Errors(Send("--param", "hours=3", "--param", "hours=4")).ShouldContain(
            e => e.Contains("was given twice", StringComparison.Ordinal),
            "two values for one slot is a send whose text depends on argument order.");
    }

    [Fact]
    public void A_parameter_that_is_not_name_equals_value_is_refused()
    {
        Errors(Send("--param", "hours")).ShouldContain(
            e => e.Contains("is not name=value", StringComparison.Ordinal));
    }

    [Fact]
    public void Attachments_are_read_as_type_equals_amount()
    {
        var attachments = Parsed(Send("--attach", "SOUL_SHARDS=500", "--attach", "ENERGY=120")).Attachments;

        attachments.Select(a => a.Type).ShouldBe(new[] { "SOUL_SHARDS", "ENERGY" });
        attachments.Select(a => a.Amount).ShouldBe(new[] { 500L, 120L });
    }

    [Fact]
    public void An_attachment_amount_that_is_not_a_whole_number_is_refused()
    {
        Errors(Send("--attach", "SOUL_SHARDS=lots")).ShouldContain(
            e => e.Contains("not a whole number", StringComparison.Ordinal));
    }

    [Fact]
    public void An_attachment_amount_is_read_invariantly()
    {
        Errors(Send("--attach", "SOUL_SHARDS=1.5")).ShouldContain(
            e => e.Contains("not a whole number", StringComparison.Ordinal),
            "'1,5' is a whole number in some locales and this must not depend on the host the tool " +
            "is run from.");
    }

    // ---------------------------------------------------------------------------- the predicate

    [Fact]
    public void A_clause_is_read_as_field_operator_operand()
    {
        var clause = Parsed(Send("--where", "HIGHEST_CHAPTER:AT_LEAST:3")).Clauses.ShouldHaveSingleItem();

        clause.Field.ShouldBe(MailSegmentField.HIGHEST_CHAPTER);
        clause.Operator.ShouldBe(MailSegmentOperator.AT_LEAST);
        clause.Operand.ShouldBe(3);
    }

    [Theory]
    [InlineData("PLUS_ACTIVE")]
    [InlineData("REGION")]
    [InlineData("CLIENT_VERSION")]
    [InlineData("GUILD_MEMBERSHIP")]
    [InlineData("AFFECTED_RUN_WINDOW")]
    public void A_field_no_stored_profile_can_answer_is_refused_at_the_command_line(string field)
    {
        Errors(Send("--where", field + ":AT_LEAST:1")).ShouldContain(
            e => e.Contains("cannot be answered", StringComparison.Ordinal),
            "refused where the operator can see it, with the reason — a clause silently ignored " +
            "would widen the send to every candidate in the file.");
    }

    [Fact]
    public void A_field_nobody_named_is_refused_with_the_vocabulary()
    {
        Errors(Send("--where", "FAVOURITE_COLOUR:AT_LEAST:1")).ShouldContain(
            e => e.Contains("HIGHEST_CHAPTER", StringComparison.Ordinal),
            "the refusal lists what the operator MAY ask, so a typo is one line away from being " +
            "fixed rather than one search away.");
    }

    [Fact]
    public void A_clause_of_the_wrong_shape_is_refused()
    {
        Errors(Send("--where", "HIGHEST_CHAPTER>3")).ShouldContain(
            e => e.Contains("FIELD:OPERATOR:OPERAND", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_operator_is_refused()
    {
        Errors(Send("--where", "HIGHEST_CHAPTER:MORE_THAN:3")).ShouldContain(
            e => e.Contains("is not an operator", StringComparison.Ordinal));
    }

    // --------------------------------------------------------------------------------- the shell

    [Fact]
    public void The_templates_verb_needs_nothing_else()
    {
        Parsed("templates").Verb.ShouldBe(InboxOpsVerb.TEMPLATES);
    }

    [Fact]
    public void No_verb_at_all_is_refused()
    {
        Errors().ShouldContain(e => e.Contains("no verb", StringComparison.Ordinal));
    }

    [Fact]
    public void A_verb_the_tool_does_not_have_is_refused()
    {
        Errors("delete").ShouldContain(e => e.Contains("is not a verb", StringComparison.Ordinal));
    }

    [Fact]
    public void An_option_the_tool_does_not_know_is_refused_rather_than_ignored()
    {
        Errors(Send("--force")).ShouldContain(
            e => e.Contains("--force", StringComparison.Ordinal),
            "an ignored flag is an operator believing they asked for something they did not.");
    }

    [Fact]
    public void An_option_given_no_value_is_refused()
    {
        Errors("send", "--template").ShouldContain(
            e => e.Contains("was given no value", StringComparison.Ordinal));
    }

    [Fact]
    public void The_usage_text_states_that_the_operator_is_unverified()
    {
        InboxOpsArguments.Usage.ShouldContain(
            "UNVERIFIED",
            Case.Sensitive,
            "said at every reader: there is no operator identity system in this repository, and an " +
            "audit column somebody trusts is worse than one they know to distrust.");
    }

    [Fact]
    public void The_usage_text_states_that_ALL_means_the_supplied_candidates()
    {
        // The phrase is asserted on one line of the usage block: matching across the wrap would pin
        // the text's line breaks, and a re-flow that changed nothing an operator reads would go red.
        InboxOpsArguments.Usage.ShouldContain(
            "\"all of the candidates in this file\"",
            Case.Sensitive,
            "the honest limit, printed where an operator meets it rather than left in a comment.");
    }
}
