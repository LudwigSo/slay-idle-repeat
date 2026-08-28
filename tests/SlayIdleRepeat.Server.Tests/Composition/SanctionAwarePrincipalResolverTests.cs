using Shouldly;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Composition;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// 🔒 The one place a sanction reaches the wire: an authenticated caller carrying a live account
/// action is refused with HTTP 403, and no other kind of sanction changes anything.
/// </summary>
public sealed class SanctionAwarePrincipalResolverTests
{
    private static readonly PlayerId Locked = new("PLAYER_locked");
    private static readonly PlayerId Ordinary = new("PLAYER_ordinary");

    /// <summary>An inner resolver that answers whatever a test wants, and counts how often it was asked.</summary>
    private sealed class StubResolver(PrincipalResolution answer) : IPrincipalResolver
    {
        internal int Calls { get; private set; }

        public PrincipalResolution Resolve(string? authorizationHeader)
        {
            Calls++;

            return answer;
        }
    }

    private static AccountStandingSnapshot StandingLocking(params PlayerId[] locked)
    {
        var snapshot = new AccountStandingSnapshot();
        var applied = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        snapshot.Replace(
            locked
                .Select((player, index) => new PlayerSanction(
                    "SAN_" + index, player, SanctionKind.ACCOUNT_ACTION, "REV_" + index, "ops.rita", applied))
                .ToArray(),
            applied.AddDays(1));

        return snapshot;
    }

    [Fact]
    public void An_account_carrying_a_live_account_action_is_refused_with_403()
    {
        var resolver = new SanctionAwarePrincipalResolver(
            new StubResolver(PrincipalResolution.Resolved(Locked)), StandingLocking(Locked));

        var resolution = resolver.Resolve("Bearer whatever");

        resolution.Player.ShouldBeNull();
        resolution.RefusalStatus.ShouldBe(
            403,
            "403 is the account-state screen. A 200 rejection would tell the client the command was "
            + "decided; a 401 would send it into a token refresh loop it cannot win.");
    }

    [Fact]
    public void An_account_with_no_sanction_passes_straight_through_unchanged()
    {
        var resolved = PrincipalResolution.Resolved(Ordinary);
        var inner = new StubResolver(resolved);

        var resolution = new SanctionAwarePrincipalResolver(inner, StandingLocking(Locked))
            .Resolve("Bearer whatever");

        resolution.ShouldBeSameAs(
            resolved, "an unsanctioned caller is not re-decided, only passed along.");
        resolution.Player.ShouldBe(Ordinary);
        inner.Calls.ShouldBe(1, "and the inner resolver is asked exactly once per request.");
    }

    [Fact]
    public void An_account_carrying_only_a_ladder_exclusion_is_served_normally()
    {
        var snapshot = new AccountStandingSnapshot();
        var applied = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        snapshot.Replace(
            [new PlayerSanction("SAN_1", Ordinary, SanctionKind.SHADOW_EXCLUDE_LADDER, "REV_1", "ops", applied)],
            applied.AddDays(1));

        new SanctionAwarePrincipalResolver(new StubResolver(PrincipalResolution.Resolved(Ordinary)), snapshot)
            .Resolve("Bearer whatever")
            .RefusalStatus
            .ShouldBeNull(
                "the shadow rung is invisible to the player by definition — a 403 would make it the "
                + "loudest sanction in the ladder.");
    }

    [Fact]
    public void An_unauthenticated_caller_is_still_a_401_and_is_never_told_about_an_account()
    {
        var resolver = new SanctionAwarePrincipalResolver(
            new StubResolver(PrincipalResolution.Unauthorized()), StandingLocking(Locked));

        resolver.Resolve(null).RefusalStatus.ShouldBe(
            401,
            "the standing check runs AFTER authentication, so a caller who has not proved who they "
            + "are cannot learn whether an account they are guessing at is sanctioned.");
    }

    [Fact]
    public void An_inner_403_is_passed_along_rather_than_re_derived()
    {
        new SanctionAwarePrincipalResolver(
                new StubResolver(PrincipalResolution.Locked()), StandingLocking())
            .Resolve("Bearer whatever")
            .RefusalStatus
            .ShouldBe(403);
    }

    [Fact]
    public void The_decorator_refuses_to_be_composed_without_either_half()
    {
        Should.Throw<ArgumentNullException>(
            () => new SanctionAwarePrincipalResolver(null!, new AccountStandingSnapshot()));
        Should.Throw<ArgumentNullException>(
            () => new SanctionAwarePrincipalResolver(new StubResolver(PrincipalResolution.Unauthorized()), null!));
    }
}
