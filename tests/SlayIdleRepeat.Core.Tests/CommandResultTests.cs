using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary><see cref="CommandResult"/>'s construction guards and the transport/domain tier boundary.</summary>
public sealed class CommandResultTests
{
    private static readonly IReadOnlyList<DomainEvent> NoEvents = Array.Empty<DomainEvent>();

    private static WorldSlice AnySlice() => Worlds.OutsideARun();

    private static CurrencyChanged AnyEvent() => new(1, CurrencyId.GOLD, 1L, "fixture");

    // ------------------------------------------------------- the tier boundary

    [Theory]
    [MemberData(nameof(TransportTierReasons))]
    public void A_transport_tier_rejection_is_refused(RejectionReason reason)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() =>
            CommandResult.Reject(reason, AnySlice()));

        thrown.ParamName.ShouldBe("Rejection");
        thrown.Message.ShouldContain("TRANSPORT-TIER REJECTION", Case.Sensitive);
        thrown.Message.ShouldContain(reason.ToString(), Case.Sensitive);
    }

    [Theory]
    [MemberData(nameof(DomainTierReasons))]
    public void A_domain_tier_rejection_is_accepted(RejectionReason reason)
    {
        CommandResult.Reject(reason, AnySlice()).Rejection.ShouldBe(reason);
    }

    [Theory]
    [InlineData((RejectionReason)0)]
    [InlineData((RejectionReason)999)]
    public void An_undeclared_rejection_reason_is_refused(RejectionReason undeclared)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() =>
            CommandResult.Reject(undeclared, AnySlice()));

        thrown.ParamName.ShouldBe("reason");
        thrown.Message.ShouldContain("This is not a rejection reason", Case.Sensitive);
    }

    /// <summary>No <c>with</c> path skirts the guard: <c>with</c> re-runs the constructor, not the factory.</summary>
    [Fact]
    public void The_tier_guard_runs_on_the_constructor_too()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandResult(
            Accepted: false, RejectionReason.RATE_LIMITED, AnySlice(), NoEvents));
    }

    // ------------------------------------------------------- the three-way agreement

    [Fact]
    public void Accepted_and_Rejection_are_two_halves_of_one_answer()
    {
        Should.Throw<ArgumentException>(() => new CommandResult(
                Accepted: true, RejectionReason.ILLEGAL_STATE, AnySlice(), NoEvents))
            .Message.ShouldContain("An ACCEPTED result carries a Rejection", Case.Sensitive);

        Should.Throw<ArgumentException>(() => new CommandResult(
                Accepted: false, null, AnySlice(), NoEvents))
            .Message.ShouldContain("A REFUSED result carries no Rejection", Case.Sensitive);
    }

    [Fact]
    public void A_refused_result_carries_no_events()
    {
        Should.Throw<ArgumentException>(() => new CommandResult(
                Accepted: false, RejectionReason.ILLEGAL_STATE, AnySlice(), new[] { AnyEvent() }))
            .Message.ShouldContain("A REFUSED result carries 1 event(s)", Case.Sensitive);
    }

    // ------------------------------------------------------- absent values

    [Fact]
    public void A_result_always_carries_the_resulting_state()
    {
        Should.Throw<ArgumentNullException>(() => CommandResult.Accept(null!, NoEvents))
            .ParamName.ShouldBe("NewState");
    }

    /// <summary>An empty event list is how a command that produced none says so; null is not the same thing.</summary>
    [Fact]
    public void A_null_event_list_is_not_an_empty_one()
    {
        Should.Throw<ArgumentNullException>(() => CommandResult.Accept(AnySlice(), null!))
            .ParamName.ShouldBe("Events");
    }

    /// <summary>A readonly record struct's <c>default</c> bypasses every constructor guard above.</summary>
    [Fact]
    public void The_default_struct_is_not_a_result_and_says_so()
    {
        var uninitialised = default(CommandResult);

        Should.Throw<InvalidOperationException>(() => uninitialised.NewState)
            .Message.ShouldContain("default(CommandResult)", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => uninitialised.Events)
            .Message.ShouldContain("default(CommandResult)", Case.Sensitive);
    }

    /// <summary>...and rendering the default struct does not itself throw, so it stays inspectable in a debugger.</summary>
    [Fact]
    public void The_default_struct_still_renders()
    {
        default(CommandResult).ToString().ShouldContain("NewState = default", Case.Sensitive);
    }

    // ------------------------------------------------------- rendering

    [Fact]
    public void The_result_renders_through_its_own_PrintMembers_and_not_the_synthesized_one()
    {
        var german = new CultureInfo("de-DE");

        german.NumberFormat.NumberDecimalSeparator.ShouldNotBe(
            CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator,
            "the forward guard below is only meaningful if the runtime actually has a German " +
            "culture. Under globalization-invariant mode new CultureInfo(\"de-DE\") silently " +
            "returns the invariant culture and the comparison would hold over nothing.");

        var result = CommandResult.Reject(RejectionReason.CAP_REACHED, AnySlice());

        Render(result, CultureInfo.InvariantCulture).ShouldBe(
            "CommandResult { Accepted = False, Rejection = CAP_REACHED, NewState = present, Events = 0 }",
            "the synthesized PrintMembers renders NewState by dumping the WorldSlice and Events by " +
            "its type name, and reads both through the accessors that throw on the default struct.");

        Render(result, german).ShouldBe(Render(result, CultureInfo.InvariantCulture));
    }

    public static TheoryData<RejectionReason> TransportTierReasons() => Theory(RejectionReasons.TransportTier);

    public static TheoryData<RejectionReason> DomainTierReasons() => Theory(RejectionReasons.DomainTier);

    private static TheoryData<RejectionReason> Theory(IReadOnlyList<RejectionReason> reasons)
    {
        var data = new TheoryData<RejectionReason>();

        foreach (var reason in reasons)
        {
            data.Add(reason);
        }

        return data;
    }

    private static string Render(CommandResult result, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return result.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
