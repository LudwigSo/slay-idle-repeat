using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Auth;
using SlayIdleRepeat.Application.Wire;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Auth;

/// <summary>
/// The ladder a client climbs when a request comes back unauthorised: one refresh, then device-secret
/// re-authentication, and only then the player.
/// </summary>
public sealed class UnauthorizedRecoveryPolicyTests
{
    /// <summary>The command being recovered. An obviously-fake fixture id, not a generated one.</summary>
    private const string InFlightCommand = "cmd_fixture_0001";

    [Fact]
    public void OnUnauthorized_asks_for_a_refresh_the_first_time()
    {
        var attempt = UnauthorizedRecoveryPolicy.Begin(new CommandId(InFlightCommand));

        UnauthorizedRecoveryPolicy.OnUnauthorized(attempt).Step.ShouldBe(
            UnauthorizedRecoveryStep.REFRESH_THE_ACCESS_TOKEN,
            "an expired access token is curable without the player, and anything louder on the first " +
            "401 is a session interrupted for a problem the client can fix by itself.");
    }

    [Fact]
    public void OnUnauthorized_asks_for_device_secret_reauthentication_once_a_refresh_has_been_spent()
    {
        var first = UnauthorizedRecoveryPolicy.OnUnauthorized(
            UnauthorizedRecoveryPolicy.Begin(new CommandId(InFlightCommand)));

        UnauthorizedRecoveryPolicy.OnUnauthorized(first.Attempt).Step.ShouldBe(
            UnauthorizedRecoveryStep.REAUTHENTICATE_WITH_THE_DEVICE_SECRET,
            "the second 401 asked for another refresh. Exactly one refresh is permitted per attempt: " +
            "a refresh token the server has already rejected will be rejected again, and retrying it " +
            "spends a mid-run expiry looping instead of curing it.");
    }

    [Fact]
    public void OnUnauthorized_surfaces_to_the_player_only_after_both_silent_cures_failed()
    {
        var first = UnauthorizedRecoveryPolicy.OnUnauthorized(
            UnauthorizedRecoveryPolicy.Begin(new CommandId(InFlightCommand)));
        var second = UnauthorizedRecoveryPolicy.OnUnauthorized(first.Attempt);

        UnauthorizedRecoveryPolicy.OnUnauthorized(second.Attempt).Step.ShouldBe(
            UnauthorizedRecoveryStep.SURFACE_TO_THE_PLAYER,
            "the device secret is the account's root credential; once it has been refused there is " +
            "nothing left for the client to try, and continuing to retry silently would leave the " +
            "player looking at a screen that never updates and never explains itself.");
    }

    /// <summary>The top rung is terminal: a further 401 keeps surfacing rather than throwing or looping.</summary>
    /// <remarks>
    /// The fork this settles is what the policy does when it is asked again after the ladder is spent.
    /// Throwing would make the caller's error path depend on how many times it has already asked.
    /// </remarks>
    [Fact]
    public void OnUnauthorized_keeps_surfacing_to_the_player_once_the_ladder_is_spent()
    {
        var first = UnauthorizedRecoveryPolicy.OnUnauthorized(
            UnauthorizedRecoveryPolicy.Begin(new CommandId(InFlightCommand)));
        var second = UnauthorizedRecoveryPolicy.OnUnauthorized(first.Attempt);
        var third = UnauthorizedRecoveryPolicy.OnUnauthorized(second.Attempt);

        UnauthorizedRecoveryPolicy.OnUnauthorized(third.Attempt).Step.ShouldBe(
            UnauthorizedRecoveryStep.SURFACE_TO_THE_PLAYER);
    }

    /// <summary>The retried request is the same request, at every rung.</summary>
    /// <remarks>
    /// Compared as bytes rather than by record equality: this is an idempotency key, and a value that
    /// compares equal while differing in normalisation, casing or an invisible character is a value
    /// the server files under a different record.
    /// </remarks>
    [Fact]
    public void OnUnauthorized_carries_the_same_commandId_byte_for_byte_through_every_rung()
    {
        var command = new CommandId(InFlightCommand);

        var first = UnauthorizedRecoveryPolicy.OnUnauthorized(UnauthorizedRecoveryPolicy.Begin(command));
        var second = UnauthorizedRecoveryPolicy.OnUnauthorized(first.Attempt);
        var third = UnauthorizedRecoveryPolicy.OnUnauthorized(second.Attempt);

        Utf8(new CommandId("cmd_fixture_0002")).ShouldNotBe(
            Utf8(command),
            "two different ids produced identical bytes, so the comparisons below cannot see a " +
            "changed key either.");

        Utf8(first.Attempt.CommandId).ShouldBe(Utf8(command));
        Utf8(second.Attempt.CommandId).ShouldBe(Utf8(command));
        Utf8(third.Attempt.CommandId).ShouldBe(
            Utf8(command),
            "the retry carries a key the server has never seen, so work the first attempt already " +
            "committed is committed a second time — the idempotency record no longer matches it.");
    }

    /// <summary>
    /// 🔒 A mid-run token expiry is never player-visible and never loses progress.
    /// </summary>
    /// <remarks>
    /// The whole reason the ladder exists, stated as one walk: the 401 is cured by a refresh the
    /// player never sees, the retry carries the same key, and the session that recovered starts from
    /// the bottom rung again — so the second expiry of a long idle session is cured silently too
    /// rather than climbing straight to the player.
    /// </remarks>
    [Fact]
    public void A_mid_run_token_expiry_is_never_player_visible_and_never_loses_progress()
    {
        var command = new CommandId(InFlightCommand);

        var expired = UnauthorizedRecoveryPolicy.OnUnauthorized(UnauthorizedRecoveryPolicy.Begin(command));
        var recovered = UnauthorizedRecoveryPolicy.OnAuthorized(expired.Attempt);
        var expiredAgain = UnauthorizedRecoveryPolicy.OnUnauthorized(recovered);

        expired.Step.ShouldBe(
            UnauthorizedRecoveryStep.REFRESH_THE_ACCESS_TOKEN,
            "the first expiry of the run reached the player.");

        expiredAgain.Step.ShouldBe(
            UnauthorizedRecoveryStep.REFRESH_THE_ACCESS_TOKEN,
            "a successful request did not reset the ladder, so the second expiry of a long session " +
            "climbed a rung it had already paid for and a player who did nothing wrong was shown a " +
            "sign-in screen.");

        Utf8(expiredAgain.Attempt.CommandId).ShouldBe(
            Utf8(command),
            "the recovered attempt is retrying a different command, which is progress lost or " +
            "duplicated rather than resumed.");
    }

    private static byte[] Utf8(CommandId id) => Encoding.UTF8.GetBytes(id.Value);
}
