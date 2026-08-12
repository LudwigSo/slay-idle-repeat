using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §2 — <see cref="CommandResult"/>'s guards, and the tier boundary it is the last place to
/// enforce.
/// </summary>
public sealed class CommandResultTests
{
    private static readonly IReadOnlyList<DomainEvent> NoEvents = Array.Empty<DomainEvent>();

    private static WorldSlice AnySlice() => Worlds.OutsideARun();

    private static CurrencyChanged AnyEvent() => new(1, CurrencyId.GOLD, 1L, "fixture");

    // ------------------------------------------------------- the tier boundary of 30 §2

    /// <summary>
    /// 🔒 `30` §2 / `14` §16.2 — a <b>transport-tier</b> reason cannot be put in a
    /// <c>CommandResult</c> at all. It is refused at construction, so no seam has to remember to
    /// check.
    /// </summary>
    /// <remarks>
    /// Stated over <b>every</b> transport-tier value rather than one representative, and read off
    /// <c>RejectionReasons.TransportTier</c> rather than transcribed: a value appended to `14` §16.2
    /// and classified transport is covered on the commit that adds it, and a rule that named
    /// <c>RATE_LIMITED</c> alone would have said nothing about the other nine.
    /// </remarks>
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

    /// <summary>Every domain-tier reason is accepted — the rule above must not refuse the whole enum.</summary>
    [Theory]
    [MemberData(nameof(DomainTierReasons))]
    public void A_domain_tier_rejection_is_accepted(RejectionReason reason)
    {
        CommandResult.Reject(reason, AnySlice()).Rejection.ShouldBe(reason);
    }

    /// <summary>
    /// 🔒 A value `14` §16.2 has no row for — an uninitialised field, or a number cast in from the
    /// wire — is refused rather than quietly acquiring a tier.
    /// </summary>
    [Fact]
    public void An_undeclared_rejection_reason_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CommandResult.Reject(default, AnySlice()));
        Should.Throw<ArgumentOutOfRangeException>(() => CommandResult.Reject((RejectionReason)999, AnySlice()));
    }

    /// <summary>
    /// 🔒 The tier guard runs on <b>every</b> construction path, not just the factory — which is why
    /// every component is <c>get</c>-only and there is no <c>with</c> path around it.
    /// </summary>
    [Fact]
    public void The_tier_guard_runs_on_the_constructor_too()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CommandResult(
            Accepted: false, RejectionReason.RATE_LIMITED, AnySlice(), NoEvents));
    }

    // ------------------------------------------------------- the three-way agreement

    /// <summary>
    /// 🔒 <c>Accepted</c> is exactly "there is no <c>Rejection</c>". Each illegal combination is a
    /// different defect and says which (steering S2).
    /// </summary>
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

    /// <summary>
    /// 🔒 A refused command carries no events: it changed nothing, so there is nothing for `14` §7.1
    /// to log, `14` §2.4 to animate or `28` D to count.
    /// </summary>
    [Fact]
    public void A_refused_result_carries_no_events()
    {
        Should.Throw<ArgumentException>(() => new CommandResult(
                Accepted: false, RejectionReason.ILLEGAL_STATE, AnySlice(), new[] { AnyEvent() }))
            .Message.ShouldContain("A REFUSED result carries 1 event(s)", Case.Sensitive);
    }

    /// <summary>An accepted result may carry events, or none.</summary>
    [Fact]
    public void An_accepted_result_may_carry_events_or_none()
    {
        CommandResult.Accept(AnySlice(), NoEvents).Events.ShouldBeEmpty();
        CommandResult.Accept(AnySlice(), new[] { AnyEvent() }).Events.ShouldHaveSingleItem();
    }

    // ------------------------------------------------------- absent values

    /// <summary>The resulting state is never null — a rejection carries the slice it was handed.</summary>
    [Fact]
    public void A_result_always_carries_the_resulting_state()
    {
        Should.Throw<ArgumentNullException>(() => CommandResult.Accept(null!, NoEvents))
            .ParamName.ShouldBe("NewState");
    }

    /// <summary>
    /// An <b>empty</b> event list is how a command that produced none says so; a null is not an
    /// empty list, and four consumers would each have to invent an answer for it.
    /// </summary>
    [Fact]
    public void A_null_event_list_is_not_an_empty_one()
    {
        Should.Throw<ArgumentNullException>(() => CommandResult.Accept(AnySlice(), null!))
            .ParamName.ShouldBe("Events");
    }

    /// <summary>
    /// 🔒 <c>default(CommandResult)</c> is not a result, and says so rather than answering
    /// <c>null</c> three frames from the uninitialised field it came out of.
    /// </summary>
    /// <remarks>
    /// `30` §2 specifies a <c>readonly record struct</c>, so the language can hand out an instance
    /// that ran no constructor — carrying <c>Accepted = false</c> with no <c>Rejection</c>, a
    /// combination the constructor refuses. <c>Result&lt;T&gt;</c> answered the same problem by
    /// being a class; the shape here is specified, so the accessors are where it is answered.
    /// </remarks>
    [Fact]
    public void The_default_struct_is_not_a_result_and_says_so()
    {
        var uninitialised = default(CommandResult);

        Should.Throw<InvalidOperationException>(() => uninitialised.NewState)
            .Message.ShouldContain("default(CommandResult)", Case.Sensitive);

        Should.Throw<InvalidOperationException>(() => uninitialised.Events)
            .Message.ShouldContain("default(CommandResult)", Case.Sensitive);
    }

    /// <summary>
    /// …and rendering it does not raise. A diagnostic that threw out of a debugger tooltip would
    /// make the very state this guard exists to describe the hardest one to look at.
    /// </summary>
    [Fact]
    public void The_default_struct_still_renders()
    {
        default(CommandResult).ToString().ShouldContain("NewState = default", Case.Sensitive);
    }

    // ------------------------------------------------------- rendering

    /// <summary>
    /// 🔒 `14` §8.2 — the result renders identically under every culture. A record's synthesized
    /// <c>PrintMembers</c> would append through <c>StringBuilder.Append(object)</c> and format with
    /// the ambient culture, which the architecture suite's IL scan cannot see through the boxing.
    /// </summary>
    [Fact]
    public void ToString_renders_identically_under_any_culture()
    {
        var german = new CultureInfo("de-DE");

        german.NumberFormat.NumberDecimalSeparator.ShouldNotBe(
            CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator,
            "this assertion is only meaningful if the runtime actually has a German culture. Under " +
            "globalization-invariant mode new CultureInfo(\"de-DE\") silently returns the invariant " +
            "culture and the comparison below would hold over nothing.");

        var result = CommandResult.Reject(RejectionReason.CAP_REACHED, AnySlice());

        Render(result, german).ShouldBe(Render(result, CultureInfo.InvariantCulture));
        Render(result, CultureInfo.InvariantCulture)
            .ShouldContain("Accepted = False, Rejection = CAP_REACHED", Case.Sensitive);
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
