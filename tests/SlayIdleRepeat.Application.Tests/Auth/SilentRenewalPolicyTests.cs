using Shouldly;
using SlayIdleRepeat.Application.Auth;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Auth;

/// <summary>
/// When the client renews its access token in the background — a pure function of the token's own two
/// instants, the configured fraction, and now.
/// </summary>
public sealed class SilentRenewalPolicyTests
{
    /// <summary>The instant every token in this file is issued at. Fixed: a policy asked "now" is untestable.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    /// <summary>The configured access-token lifetime, in minutes.</summary>
    private const int LifetimeMinutes = 60;

    /// <summary>The configured renewal fraction.</summary>
    private const double Fraction = 0.8;

    private static DateTimeOffset Expires => Issued.AddMinutes(LifetimeMinutes);

    [Fact]
    public void Decide_puts_the_renewal_instant_at_the_configured_fraction_of_the_lifetime()
    {
        var decision = SilentRenewalPolicy.Decide(Issued, Expires, Fraction, Issued);

        decision.RenewAtUtc.ShouldBe(
            Issued.AddMinutes(48),
            "48 minutes is 60 × 0.8. A renewal instant derived from anything but the fraction and the " +
            "two instants would be a second copy of a server-operations number living in the client.");
    }

    /// <summary>
    /// …and it follows the fraction rather than agreeing with it by coincidence: at 1.0 the client
    /// renews only when the token actually expires.
    /// </summary>
    /// <remarks>
    /// Also the negative control for the refusal case below — 1.0 is inside the interval, so a guard
    /// that refused everything would fail here rather than reading as correct.
    /// </remarks>
    [Fact]
    public void Decide_at_a_fraction_of_one_renews_only_at_expiry()
    {
        var decision = SilentRenewalPolicy.Decide(Issued, Expires, 1.0, Expires.AddTicks(-1));

        decision.RenewAtUtc.ShouldBe(Expires);
        decision.RenewalIsDue.ShouldBeFalse(
            "one tick before the renewal instant is not yet the renewal instant.");
    }

    /// <summary>
    /// The boundary is inclusive: renewal is due <em>at</em> the instant, not one tick after it.
    /// </summary>
    /// <remarks>
    /// Both sides, because an exclusive comparison passes the "48 minutes and one tick" case on its
    /// own. Stated in the direction that renews slightly early: the cost of renewing a tick early is
    /// one extra request, and the cost of renewing a tick late is a 401 in the middle of a run.
    /// </remarks>
    [Theory]
    [InlineData(47, false)]
    [InlineData(48, true)]
    [InlineData(59, true)]
    public void Decide_reports_renewal_due_from_the_renewal_instant_onwards(int minutesSinceIssue, bool due)
    {
        var decision = SilentRenewalPolicy.Decide(
            Issued, Expires, Fraction, Issued.AddMinutes(minutesSinceIssue));

        decision.RenewalIsDue.ShouldBe(due);
    }

    [Fact]
    public void Decide_reports_renewal_due_for_a_token_that_is_already_past_its_expiry()
    {
        var decision = SilentRenewalPolicy.Decide(Issued, Expires, Fraction, Expires.AddMinutes(1));

        decision.RenewalIsDue.ShouldBeTrue(
            "an expired token is the one state renewal is least optional in, and a policy that only " +
            "answered inside the lifetime window would leave the client holding a dead token.");
    }

    /// <summary>A fraction outside the interval is refused loudly rather than clamped into range.</summary>
    /// <remarks>
    /// Clamping turns a misconfigured deployment into a silently different one: 0 would renew on every
    /// request and 1.5 would renew after the token is already dead, and both would look like the
    /// configured value working. The name of the argument is asserted so the operator reading the
    /// crash knows which environment variable to fix.
    /// </remarks>
    [Theory]
    [InlineData(0d)]
    [InlineData(-0.1)]
    [InlineData(1.0000001)]
    [InlineData(double.NaN)]
    public void Decide_refuses_a_fraction_outside_the_interval_rather_than_clamping_it(double fraction)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => SilentRenewalPolicy.Decide(Issued, Expires, fraction, Issued))
            .ParamName.ShouldBe("renewalFraction");
    }
}
