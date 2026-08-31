using Shouldly;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>
/// 🔒 The kill switch's second half: mail off hides the inbox, and the expiry job still auto-grants.
/// </summary>
/// <remarks>
/// Stated as a claim about the job's SHAPE rather than about a run, because it is a claim about
/// something that must never be possible: a switch thrown during an incident cannot be what destroys
/// the rewards that incident is about to compensate. A behavioural test would show the sweep
/// granting with the switch off; this shows it could not do otherwise.
/// </remarks>
public sealed class InboxKillSwitchTests
{
    [Fact]
    public void The_expiry_sweep_is_built_without_any_way_to_read_the_kill_switch()
    {
        var parameters = typeof(InboxExpiryJob).GetConstructors().ShouldHaveSingleItem().GetParameters();

        parameters.ShouldNotBeEmpty(
            "a job that took nothing would make the assertion below trivially true.");

        parameters.ShouldNotContain(
            parameter => parameter.ParameterType == typeof(FeatureFlags),
            "the sweep takes the stores, the sink and the content set — and no flags source, so " +
            "there is no version of it that stops granting when mail is switched off.");
    }

    [Fact]
    public void The_claim_seam_is_built_without_any_way_to_read_the_kill_switch_either()
    {
        // The other side of the same fact: the switch is asked ONCE, at the wire gate, before
        // dispatch. A second read here would be a second gate free to disagree with the first.
        var parameters = typeof(InboxCommandSupport).GetConstructors()
            .ShouldHaveSingleItem().GetParameters();

        parameters.ShouldNotBeEmpty(
            "a seam that took nothing would make the assertion below trivially true — the same floor " +
            "its sibling above already carries.");

        parameters.ShouldNotContain(parameter => parameter.ParameterType == typeof(FeatureFlags));
    }

    [Fact]
    public void The_unresolved_flag_value_the_sweep_runs_under_leaves_mail_on()
    {
        LocalHostAmbience.NoRemoteConfigResolved().MailEnabled.ShouldBeTrue(
            "the sweep applies its claims under the 'nothing killed' identity rather than under the " +
            "live config. If that identity ever stopped meaning 'mail on', the sweep would start " +
            "refusing its own auto-grants at the wire gate's rule.");
    }
}
