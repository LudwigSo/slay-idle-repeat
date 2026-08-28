using Shouldly;
using SlayIdleRepeat.Application.Queries;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Queries;

/// <summary>
/// The read-model convention driven directly. No cross-player view exists yet — all six belong to
/// M12, M13 and M14 — so the seam has no production implementation, and a shape nothing implements
/// is a shape nothing has ever been checked against.
/// </summary>
public sealed class ReadModelConventionTests
{
    /// <summary>A stand-in for the ten-minute ladder view, so the convention has a subject today.</summary>
    private sealed record ProbeLadderView(string PlayerId, int Rank) : IReadModelView
    {
        public static TimeSpan StalenessBudget => TimeSpan.FromMinutes(10);
    }

    /// <summary>A second view on a different budget — the control that keeps the first claim honest.</summary>
    private sealed record ProbeBossView(long Damage) : IReadModelView
    {
        public static TimeSpan StalenessBudget => TimeSpan.FromSeconds(30);
    }

    private sealed class ProbeLadderQuery : IReadModelQuery<ProbeLadderView>
    {
    }

    private sealed class ProbeBossQuery : IReadModelQuery<ProbeBossView>
    {
    }

    /// <summary>A query that states the primary explicitly, the way a write-model read would have to.</summary>
    private sealed class ProbePrimaryLadderQuery : IReadModelQuery<ProbeLadderView>
    {
        public ReadRouting Routing => ReadRouting.Primary;
    }

    [Fact]
    public void A_querys_staleness_budget_is_the_one_its_view_declares()
    {
        IReadModelQuery<ProbeLadderView> query = new ProbeLadderQuery();

        query.StalenessBudget.ShouldBe(
            TimeSpan.FromMinutes(10),
            "the budget belongs to the VIEW and the port only restates it — two places to declare it " +
            "is two numbers to keep in step, and the UI would then state one while the cache honoured " +
            "the other.");
    }

    [Fact]
    public void A_second_views_budget_produces_a_second_querys_budget()
    {
        IReadModelQuery<ProbeBossView> query = new ProbeBossQuery();

        query.StalenessBudget.ShouldBe(
            TimeSpan.FromSeconds(30),
            "the negative control for the case above: a port that returned a constant, or the first " +
            "view's number, would satisfy that one and fail this one.");
    }

    [Fact]
    public void A_query_that_states_nothing_is_replica_eligible()
    {
        IReadModelQuery<ProbeLadderView> query = new ProbeLadderQuery();

        query.Routing.ShouldBe(
            ReadRouting.ReplicaEligible,
            "a cross-player read model is eventual by construction, so the replica is where it " +
            "belongs; the primary is the answer that has to be argued for.");
    }

    [Fact]
    public void A_query_can_state_the_primary_instead()
    {
        IReadModelQuery<ProbeLadderView> query = new ProbePrimaryLadderQuery();

        query.Routing.ShouldBe(
            ReadRouting.Primary,
            "routing that could only ever be one value would be decoration — a read whose freshness " +
            "cannot tolerate a replica must be able to say so and be believed.");

        query.StalenessBudget.ShouldBe(
            TimeSpan.FromMinutes(10),
            "and stating the routing does not disturb the budget: the two are declared in different " +
            "places on purpose, and a port that had to restate both would have two numbers to drift.");
    }

    [Fact]
    public void Routing_has_no_value_a_type_falls_into_by_saying_nothing()
    {
        Enum.IsDefined(typeof(ReadRouting), default(ReadRouting)).ShouldBeFalse(
            "an uninitialised field, a zeroed struct or a missing JSON member must not silently mean " +
            "'primary' or 'replica'. Where a read is served from is argued for and named; the one " +
            "spelling nobody chose has to be no answer at all.");

        Enum.GetValues<ReadRouting>().ShouldBe(
            new[] { ReadRouting.Primary, ReadRouting.ReplicaEligible },
            ignoreOrder: true,
            customMessage: "two destinations exist and no more — one Postgres with an eligible " +
            "replica is the ceiling, so a third name here would be a topology nothing can route to.");
    }
}
