using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// States what <see cref="IRewardedAdPort"/> <em>means</em> (<c>23</c> §4.1, §5 A8) — written once
/// against the interface so the auto-grant adapter and the in-memory fake answer the same
/// questions the same way.
/// </summary>
/// <remarks>
/// <c>23</c> §5 A3 is the reason half of these cases exist: an adapter translates vendor failures
/// into domain results, so the application never catches a vendor exception. A suite that only
/// checked the happy path would let one implementation translate and the other throw, and the
/// caller's <c>try</c> block would then be both necessary and dead depending on the build.
/// </remarks>
[ContractSuiteFor(typeof(IRewardedAdPort))]
public abstract class IRewardedAdPortContractTests
{
    /// <summary>A placement this suite uses for the cases that are not about readiness.</summary>
    protected const string Placement = "revive_run";

    /// <summary>A second placement, for the cases that need two.</summary>
    protected const string OtherPlacement = "double_run_reward";

    /// <summary>A rewarded-ad port of the implementation under test.</summary>
    protected abstract IRewardedAdPort Create();

    /// <summary>
    /// A placement this implementation can be brought to readiness for after a
    /// <see cref="IRewardedAdPort.PreloadAsync"/>, without a network or any vendor infrastructure.
    /// </summary>
    /// <remarks>
    /// 🔒 This hook exists so <see cref="A_placement_that_reports_ready_never_answers_NoFill"/> is
    /// unconditional rather than an implication that goes silent whenever nothing happens to be
    /// ready. An implementation that cannot make <em>any</em> placement ready without infrastructure
    /// has no fixture in this suite at all — which is the same ruling that decided which ports this
    /// milestone declares and which it defers.
    /// </remarks>
    protected abstract string ReadyPlacement { get; }

    // ------------------------------------------------------------------------------- placement id

    /// <summary>A blank placement is an argument fault from every method (<c>23</c> §4.1).</summary>
    /// <remarks>
    /// Not a <see cref="AdResultKind.NoFill"/> and not a silent no-op: a blank placement is a
    /// programming error at the call site, and reporting it as an ad outcome would have the caller
    /// retry it forever against a placement that does not exist.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_placement_is_an_argument_fault_from_every_method(string? placement)
    {
        var ad = Create();

        Should.Throw<ArgumentException>(() => ad.IsReady(placement!));

        await Should.ThrowAsync<ArgumentException>(
            async () => await ad.ShowAsync(placement!, CancellationToken.None));

        await Should.ThrowAsync<ArgumentException>(
            async () => await ad.PreloadAsync(placement!, CancellationToken.None));
    }

    // ---------------------------------------------------------------------------------- outcomes

    /// <summary>
    /// Every outcome carries a defined <see cref="AdResultKind"/> (<c>23</c> §5 A3).
    /// </summary>
    /// <remarks>
    /// An adapter mapping a vendor status code straight through a cast produces a value outside the
    /// enum, which every <c>switch</c> in the application then falls through — granting nothing and
    /// reporting nothing.
    /// </remarks>
    [Fact]
    public async Task The_outcome_kind_is_always_a_defined_value()
    {
        var ad = Create();

        foreach (var placement in new[] { Placement, OtherPlacement, ReadyPlacement })
        {
            var outcome = await ad.ShowAsync(placement, CancellationToken.None);

            Enum.IsDefined(outcome.Kind).ShouldBeTrue(
                $"'{placement}' produced AdResultKind {(int)outcome.Kind}, which is not a declared "
                + "member. A cast from a vendor status code falls through every switch downstream.");
        }
    }

    /// <summary>
    /// A vendor failure never arrives as an exception — showing an unprepared placement produces an
    /// outcome (<c>23</c> §5 A3).
    /// </summary>
    [Fact]
    public async Task Showing_an_unprepared_placement_produces_an_outcome_rather_than_throwing()
    {
        var ad = Create();

        var outcome = await ad.ShowAsync(Placement, CancellationToken.None);

        Enum.IsDefined(outcome.Kind).ShouldBeTrue(
            "no ad was preloaded for this placement. The contract is that the caller gets NoFill or "
            + "Error, never an exception it has to know the vendor to interpret.");
    }

    /// <summary>
    /// 🔒 A verification token is present <b>if and only if</b> the ad completed, and non-blank when
    /// present (<c>23</c> §5 A2).
    /// </summary>
    /// <remarks>
    /// Both directions are load-bearing and they fail differently. A token on a
    /// <see cref="AdResultKind.Dismissed"/> or <see cref="AdResultKind.NoFill"/> outcome is a grant
    /// the server would accept for an ad nobody watched. A <em>missing</em> token on a
    /// <see cref="AdResultKind.Completed"/> outcome is the subscriber auto-grant path, which is
    /// legitimate — so the rule cannot be "Completed implies a token" alone; it has to be the
    /// biconditional over the token's presence, with blankness caught separately.
    /// </remarks>
    [Fact]
    public async Task A_verification_token_is_present_exactly_when_the_ad_completed()
    {
        var ad = Create();
        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);

        foreach (var placement in new[] { ReadyPlacement, Placement, OtherPlacement })
        {
            var outcome = await ad.ShowAsync(placement, CancellationToken.None);

            if (outcome.Kind != AdResultKind.Completed)
            {
                outcome.VerificationToken.ShouldBeNull(
                    $"'{placement}' returned {outcome.Kind} with a verification token. A token is "
                    + "what the server verifies a grant against; issuing one for an ad that was not "
                    + "watched is a grant for nothing.");
                continue;
            }

            if (outcome.VerificationToken is not null)
            {
                outcome.VerificationToken.ShouldNotBeNullOrWhiteSpace(
                    $"'{placement}' completed and returned a blank token. A blank token is neither "
                    + "an honest absence nor something the server can check.");
            }
        }
    }

    // -------------------------------------------------------------------------------- readiness

    /// <summary>
    /// 🔒 <see cref="IRewardedAdPort.IsReady"/> answering <see langword="true"/> means the next
    /// <see cref="IRewardedAdPort.ShowAsync"/> does not answer
    /// <see cref="AdResultKind.NoFill"/> (<c>23</c> §4.1).
    /// </summary>
    /// <remarks>
    /// No-fill means no ad was available and the port has just said one was. An implementation that
    /// can report both is telling the caller two different things about the same moment: the UI
    /// enables the button on the first answer and has to apologise on the second.
    /// </remarks>
    [Fact]
    public async Task A_placement_that_reports_ready_never_answers_NoFill()
    {
        var ad = Create();
        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);

        ad.IsReady(ReadyPlacement).ShouldBeTrue(
            $"'{ReadyPlacement}' is this fixture's declared ready placement, so a preload must make "
            + "IsReady true — otherwise the case below quantifies over nothing.");

        var outcome = await ad.ShowAsync(ReadyPlacement, CancellationToken.None);

        outcome.Kind.ShouldNotBe(
            AdResultKind.NoFill,
            "IsReady had just answered true for this placement.");
    }

    /// <summary>Preloading is idempotent and total: repeating it changes nothing and completes (<c>23</c> §4.1).</summary>
    [Fact]
    public async Task Preloading_is_idempotent_and_total()
    {
        var ad = Create();

        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);
        var afterFirst = ad.IsReady(ReadyPlacement);

        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);
        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);

        ad.IsReady(ReadyPlacement).ShouldBe(
            afterFirst,
            "a second and third preload of an already-loaded placement changed its readiness.");

        // Total over any placement in the key space, including one that will never fill: a
        // preload that throws makes the boot path's warm-up a source of failures of its own.
        await ad.PreloadAsync(Placement, CancellationToken.None);
    }

    // ------------------------------------------------------------------------------ cancellation

    /// <summary>An already-cancelled token is observed by <see cref="IRewardedAdPort.ShowAsync"/> (<c>23</c> §4.1).</summary>
    /// <remarks>
    /// The one exception the port declares beside a bad argument. It is stated over
    /// <c>ShowAsync</c> alone: <c>PreloadAsync</c> is documented as total, so pinning a throw there
    /// would contradict the port's own contract.
    /// </remarks>
    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_ShowAsync()
    {
        var ad = Create();
        await ad.PreloadAsync(ReadyPlacement, CancellationToken.None);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await ad.ShowAsync(ReadyPlacement, source.Token));
    }
}
