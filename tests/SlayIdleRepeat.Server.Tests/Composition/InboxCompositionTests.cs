using Microsoft.Extensions.Configuration;
using Shouldly;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The inbox area's deployment contract: the one number a deployment may move, and what a process
/// with no message store does about it.
/// </summary>
public sealed class InboxCompositionTests
{
    private static IConfiguration Configured(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

    [Fact]
    public void The_batch_limit_binds_from_its_configuration_key()
    {
        InboxComposition.BatchLimit(Configured((InboxComposition.BatchLimitKey, "37"))).ShouldBe(
            37,
            "Inbox__ExpiryBatchLimit is the env spelling. It trades sweep duration against how long " +
            "a batch holds a connection, and nothing about the game changes when it moves.");
    }

    [Fact]
    public void An_unset_batch_limit_falls_back_to_the_documented_default()
    {
        InboxComposition.BatchLimit(Configured()).ShouldBe(InboxExpirySchedule.DefaultBatchLimit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    public void A_batch_limit_that_would_sweep_nothing_falls_back_rather_than_faulting_the_boot(string value)
    {
        InboxComposition.BatchLimit(Configured((InboxComposition.BatchLimitKey, value))).ShouldBe(
            InboxExpirySchedule.DefaultBatchLimit,
            "a typo in an operations value must not take the API down — and a sweep that took zero " +
            "messages would report a clean run for an inbox filling up behind it, which is worse " +
            "than either.");
    }

    [Fact]
    public void A_process_with_no_database_composes_no_claim_seam()
    {
        InboxComposition.Messages(Configured()).ShouldBeNull(
            "without ConnectionStrings:Postgres there is no message store. The claim command then " +
            "faults as the loading defect it is, rather than telling a player their rewards are " +
            "gone — which is the one answer a missing store must never give.");
    }

    [Fact]
    public void The_schedule_the_deployment_log_names_is_the_games_own_daily_boundary()
    {
        InboxComposition.SweepScheduledMarker.ShouldBe(
            "Inbox expiry sweep scheduled",
            "the startup line an operator greps for when asking whether the sweep is running at " +
            "all. Pinned because a deployment reads it and a rename would make it silently absent.");
    }
}
