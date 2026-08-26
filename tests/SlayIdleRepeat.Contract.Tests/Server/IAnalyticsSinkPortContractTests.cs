using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IAnalyticsSinkPort"/> <em>means</em> (<c>23</c> §4.2) — fire-and-forget:
/// a bad argument is the only throw, and everything a backend does wrong stays behind the adapter.
/// </summary>
[ContractSuiteFor(typeof(IAnalyticsSinkPort))]
public abstract class IAnalyticsSinkPortContractTests
{
    /// <summary>A player any implementation must be able to attribute to.</summary>
    protected static readonly PlayerId Player = new("PLAYER_contract");

    /// <summary>A valid event any implementation must take.</summary>
    protected static AnalyticsEvent Event() =>
        new("contract_probe", new Dictionary<string, string> { ["kind"] = "probe" });

    /// <summary>A sink of the implementation under test.</summary>
    protected abstract IAnalyticsSinkPort Create();

    [Fact]
    public void Track_refuses_a_null_event()
    {
        var sink = Create();

        Should.Throw<ArgumentNullException>(() => sink.Track(Player, null!))
            .ParamName.ShouldBe(
                "analyticsEvent",
                "the failure has to name the argument the caller got wrong — a null event is the " +
                "caller's bug, and a bad argument is the only thing this port throws for.");
    }

    [Fact]
    public void Track_accepts_a_valid_event_without_throwing()
    {
        var sink = Create();

        Should.NotThrow(
            () => sink.Track(Player, Event()),
            "Track is fire-and-forget: whatever the backend's state, a valid call returns quietly. " +
            "A sink that can throw here turns an analytics outage into a gameplay error.");
    }

    [Fact]
    public void Track_accepts_the_same_event_repeatedly()
    {
        var sink = Create();
        var analyticsEvent = Event();

        Should.NotThrow(
            () =>
            {
                sink.Track(Player, analyticsEvent);
                sink.Track(Player, analyticsEvent);
            },
            "repeats are the caller's business — deduplication, if any, is the backend's. A sink " +
            "that refuses the second call decides a product question at the transport.");
    }
}
