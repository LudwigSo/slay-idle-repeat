using Shouldly;
using SlayIdleRepeat.Application.Tests.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// 🔒 An accepted claim's stamp rides the accepted command's own commit, end to end through the
/// gateway that builds it.
/// </summary>
/// <remarks>
/// <para>
/// M5-08 shipped the stamp as a call the use case made AFTER the player's save, and registered the
/// window honestly: a crash between the two left a reward PAID and still reading as claimable, so a
/// second <c>CLAIM_INBOX</c> under a different command id would pay it again. The ledger cannot
/// help there — it replays the same id, not a different one. The integration merge onto M5-04's
/// commit rule closed it: the ids ride <c>CommandCommit.Claim</c>.
/// </para>
/// <para>
/// These cases are the wiring half — that the gateway puts the claim on the commit at all — which
/// <c>IUnitOfWorkContractTests</c>' all-or-nothing cases cannot see, because they hand the commit in
/// ready-made. Both halves are needed: a gateway that never built a claim would leave that suite
/// green over a payment nothing ever asked for.
/// </para>
/// </remarks>
public sealed class CommandGatewayInboxClaimTests
{
    private static async Task<long> ShardsAsync(GatewayWorld world)
    {
        var (row, _) = await world.RowsAsync();
        var player = Player.Rehydrate(row, Worlds.Content);

        return player.IsSuccess
            ? player.Value.BalanceOf(CurrencyId.SOUL_SHARDS)
            : throw new InvalidOperationException("the committed player does not rehydrate: " + player.Error);
    }

    [Fact]
    public async Task An_accepted_claim_commits_the_grant_and_the_stamp_together()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_paid", player: world.Player), Worlds.Cancel);
        var before = await ShardsAsync(world);

        var reply = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("CLAIM_INBOX", 1, "c-claim", "{}"), Worlds.Cancel);

        Replies.Parse(reply, expectedStatus: 200);

        (await ShardsAsync(world)).ShouldBe(
            before + 500,
            "the grant is on the player's committed row, not only in the returned state.");
        (await world.Messages.GetActiveAsync(world.Player, Worlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldNotBeNull(
                "and the message that authorised it is spent in the SAME commit, so no crash can "
                + "leave the wallet moved and the message still claimable.");
    }

    [Fact]
    public async Task A_second_claim_under_a_new_command_id_pays_nothing_more()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_paid", player: world.Player), Worlds.Cancel);

        await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("CLAIM_INBOX", 1, "c-first", "{}"), Worlds.Cancel);
        var afterFirst = await ShardsAsync(world);

        var second = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player, Envelopes.Body("CLAIM_INBOX", 2, "c-second", "{}"), Worlds.Cancel);

        Replies.Parse(second, expectedStatus: 200);
        (await ShardsAsync(world)).ShouldBe(
            afterFirst,
            "the stamp written by the first claim is the ONLY thing between a resent CLAIM_INBOX "
            + "under a NEW command id and a second payout. The ledger replays the same id; it "
            + "cannot help with a different one.");
    }

    [Fact]
    public async Task A_command_that_claims_nothing_carries_no_claim_on_its_commit()
    {
        var world = await GatewayWorld.WithAStartingPlayerAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_untouched", player: world.Player), Worlds.Cancel);

        var hello = await world.Gateway.SubmitPlayerCommandAsync(
            world.Player,
            SessionEnvelopes.BeginSession(1, "c-hello", Worlds.Content.Version.Value),
            Worlds.Cancel);

        // 🔴 The floor under the negative control. This claimed an EMPTY contentHash, which
        // ContentVersionCheck refuses BEFORE dispatch — so the command never reached the
        // claim-building path at all, and both assertions below held over a commit that was never
        // made. A negative control that cannot reach the code it controls is not one.
        Replies.Parse(hello, expectedStatus: 200)
            .TryGetProperty("rejected", out _)
            .ShouldBeFalse(
                "BEGIN_SESSION must be ACCEPTED here, or the claim-building path this case is the "
                + "negative control for is never reached.");

        world.UnitOfWork.Commits.ShouldNotBeEmpty(
            "and it must have committed, or 'no commit carries a claim' is true of no commits.");

        world.UnitOfWork.Commits.ShouldAllBe(
            c => c.Claim == null,
            "the negative control: a gateway that put a claim on every commit would spend every "
            + "message a player owns the first time they said hello.");
        (await world.Messages.GetActiveAsync(world.Player, Worlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldBeNull();
    }
}
