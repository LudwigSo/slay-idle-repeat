using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary><c>CLAIM_INBOX</c>: what a claim pays, what it refuses, and what it must never pay twice.</summary>
public sealed class ClaimInboxTests
{
    private static CommandResult Claim(WorldSlice slice, params string[] messageIds) =>
        GameRules.Apply(
            slice,
            messageIds.Length == 0 ? new ClaimInboxCommand() : new ClaimInboxCommand(messageIds),
            Worlds.Context);

    private static long ShardsIn(CommandResult result) =>
        result.NewState.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

    // ------------------------------------------------------------------------------- CLAIM ALL

    [Fact]
    public void Claiming_everything_pays_every_claimable_message()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_a"), InboxWorlds.Message("MSG_b")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice);

        result.Accepted.ShouldBeTrue();
        ShardsIn(result).ShouldBe(
            before + (2 * InboxWorlds.DefaultAmount),
            "CLAIM ALL is the primary button: one tap pays everything the inbox is holding, or a " +
            "player with forty messages taps forty times.");
    }

    [Fact]
    public void Claiming_everything_reports_each_message_it_paid()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_a"), InboxWorlds.Message("MSG_b")));

        var claimed = Claim(slice).Events.OfType<MailClaimed>().Select(e => e.MessageId.Value).ToArray();

        claimed.ShouldBe(
            new[] { "MSG_a", "MSG_b" },
            "the event IS the instruction to stamp the row claimed, so a message paid without one " +
            "stays claimable and is paid again by the next command.");
    }

    [Fact]
    public void Claiming_everything_pays_nothing_twice()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_spent", claimedAtUtc: Worlds.NowUtc.AddHours(-1)),
            InboxWorlds.Message("MSG_owed")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice);

        ShardsIn(result).ShouldBe(
            before + InboxWorlds.DefaultAmount,
            "a message already stamped claimed has been paid. The stamp is the only thing between a " +
            "second CLAIM_INBOX and a second payout, because the ledger only replays the SAME " +
            "command id.");
        result.Events.OfType<MailClaimed>().ShouldHaveSingleItem().MessageId.Value.ShouldBe("MSG_owed");
    }

    [Fact]
    public void Claiming_everything_skips_a_message_this_build_cannot_pay_and_still_pays_the_rest()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_chest", attachments: new[] { new MailAttachment("CHEST", 1) }),
            InboxWorlds.Message("MSG_shards")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice);

        result.Accepted.ShouldBeTrue(
            "one row nobody can pay must not block CLAIM ALL — that is a player locked out of every " +
            "other reward they are owed by an ops mistake.");
        ShardsIn(result).ShouldBe(before + InboxWorlds.DefaultAmount);
        result.Events.OfType<MailClaimed>().ShouldHaveSingleItem().MessageId.Value.ShouldBe("MSG_shards");
    }

    [Fact]
    public void Claiming_an_empty_inbox_is_accepted_and_pays_nothing()
    {
        var result = Claim(InboxWorlds.WithInbox(InboxView.Empty));

        result.Accepted.ShouldBeTrue(
            "an empty inbox is not an error. A player who taps CLAIM ALL with nothing to collect has " +
            "asked for everything they are owed and received it.");
        result.Events.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------- a named claim

    [Fact]
    public void A_named_claim_pays_only_the_message_it_names()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_a"), InboxWorlds.Message("MSG_b")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice, "MSG_a");

        ShardsIn(result).ShouldBe(before + InboxWorlds.DefaultAmount);
        result.Events.OfType<MailClaimed>().ShouldHaveSingleItem().MessageId.Value.ShouldBe("MSG_a");
    }

    [Fact]
    public void A_named_claim_of_a_message_the_account_does_not_hold_is_refused()
    {
        var result = Claim(
            InboxWorlds.WithInbox(InboxWorlds.View(InboxWorlds.Message("MSG_mine"))), "MSG_someone_elses");

        result.Rejection.ShouldBe(
            RejectionReason.NOT_OWNED,
            "the player asked for a specific message and does not have it. Answering 'nothing to " +
            "claim' would read as 'you already collected that'.");
    }

    [Fact]
    public void A_named_claim_of_a_message_this_build_cannot_pay_is_refused()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_chest", attachments: new[] { new MailAttachment("CHEST", 1) })));

        var result = Claim(slice, "MSG_chest");

        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            "the player tapped a message and it cannot be paid. CLAIM ALL skips it silently because " +
            "it was not asked for; a named claim was, so it is answered.");
    }

    [Fact]
    public void A_named_claim_of_an_already_paid_message_is_accepted_and_pays_nothing()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_spent", claimedAtUtc: Worlds.NowUtc.AddHours(-1))));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice, "MSG_spent");

        result.Accepted.ShouldBeTrue(
            "a duplicate claim replays its outcome rather than refusing: a client that lost the " +
            "first response and retried under a new command id must not be told it did something " +
            "wrong.");
        ShardsIn(result).ShouldBe(before);
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void A_named_claim_that_repeats_one_id_pays_it_once()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(InboxWorlds.Message("MSG_a")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = Claim(slice, "MSG_a", "MSG_a");

        ShardsIn(result).ShouldBe(
            before + InboxWorlds.DefaultAmount,
            "the filter is a list of what to claim, not a list of times to claim it. A client that " +
            "sent the same id twice would otherwise be paid twice for one message.");
        result.Events.OfType<MailClaimed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_empty_filter_claims_everything_claimable()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(
            InboxWorlds.Message("MSG_a"), InboxWorlds.Message("MSG_b")));
        var before = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);

        var result = GameRules.Apply(
            slice, new ClaimInboxCommand(Array.Empty<string>()), Worlds.Context);

        ShardsIn(result).ShouldBe(
            before + (2 * InboxWorlds.DefaultAmount),
            "the command's own contract is that an omitted filter and an empty one both mean 'claim " +
            "everything claimable' — the two are kept distinct on the wire, not in the answer.");
    }

    // ------------------------------------------------------------------------------ the grants

    [Fact]
    public void A_currency_grant_is_published_as_income_with_the_inbox_as_its_reason()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(InboxWorlds.Message("MSG_a")));

        var moved = Claim(slice).Events.OfType<CurrencyChanged>()
            .Single(e => e.Id == CurrencyId.SOUL_SHARDS);

        moved.Delta.ShouldBe(InboxWorlds.DefaultAmount);
        moved.Reason.ShouldBe(
            MailAttachmentGrants.Reason,
            "an income report has to be able to separate what the inbox paid from what a run did, " +
            "and a balance that moved for no stated cause is the one thing it cannot.");
    }

    [Fact]
    public void Energy_above_the_bar_lands_in_the_reserve()
    {
        // The bar's maximum at Legend 1 is the authored base; asking for far more than the deficit
        // is what forces the overflow rather than a value tuned to today's number.
        var slice = InboxWorlds.WithInbox(
            InboxWorlds.View(InboxWorlds.Message(
                "MSG_energy", attachments: new[] { new MailAttachment("ENERGY", 40) })),
            InboxWorlds.EnergyAt(bar: 118, reserve: 0));

        var banks = Claim(slice).NewState.Player.Energy;

        banks.Energy.ShouldBe(120, "the bar fills to its maximum first.");
        banks.Reserve.ShouldBe(
            38,
            "and what it could not hold goes to the Reserve rather than being discarded — the whole " +
            "point of the Reserve is that a grant arriving at a full bar is not a returning-player " +
            "tax.");
    }

    [Fact]
    public void Energy_the_bar_can_hold_never_reaches_the_reserve()
    {
        var slice = InboxWorlds.WithInbox(
            InboxWorlds.View(InboxWorlds.Message(
                "MSG_energy", attachments: new[] { new MailAttachment("ENERGY", 2) })),
            InboxWorlds.EnergyAt(bar: 100, reserve: 0));

        var banks = Claim(slice).NewState.Player.Energy;

        banks.Energy.ShouldBe(102);
        banks.Reserve.ShouldBe(
            0,
            "the Reserve receives OVERFLOW only. A grant that banked into it while the bar had room " +
            "would fill a bank that never regenerates with Energy the player could have spent.");
    }

    [Fact]
    public void A_message_carrying_several_attachments_pays_all_of_them()
    {
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(InboxWorlds.Message(
            "MSG_multi",
            attachments: new[]
            {
                new MailAttachment("SOUL_SHARDS", 200),
                new MailAttachment("CROWNS", 300),
            })));
        var shards = slice.Player.BalanceOf(CurrencyId.SOUL_SHARDS);
        var crowns = slice.Player.BalanceOf(CurrencyId.CROWNS);

        var result = Claim(slice);

        ShardsIn(result).ShouldBe(shards + 200);
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(crowns + 300);
    }

    // ------------------------------------------------------------------------------- the seams

    [Fact]
    public void A_claim_on_a_slice_with_no_inbox_is_a_loading_defect()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => GameRules.Apply(Worlds.OutsideARun(), new ClaimInboxCommand(), Worlds.Context));

        thrown.Message.ShouldContain(
            "CLAIM_INBOX was dispatched",
            Case.Sensitive,
            "reading a missing projection as an empty one would tell a player holding unclaimed " +
            "compensation that they have nothing to collect — which is indistinguishable, to them, " +
            "from the reward having been taken away.");
    }

    [Fact]
    public void The_claim_order_is_the_inbox_order_rather_than_the_order_the_ids_arrived()
    {
        var older = InboxWorlds.Message("MSG_z", createdAtUtc: Worlds.NowUtc.AddDays(-9));
        var newer = InboxWorlds.Message("MSG_a", createdAtUtc: Worlds.NowUtc.AddDays(-1));
        var slice = InboxWorlds.WithInbox(InboxWorlds.View(newer, older));

        var claimed = Claim(slice).Events.OfType<MailClaimed>().Select(e => e.MessageId.Value).ToArray();

        claimed.ShouldBe(
            new[] { "MSG_z", "MSG_a" },
            "oldest first, and it is not cosmetic: an Energy attachment that overflows into the " +
            "Reserve depends on how full the bar already is, so two orders bank different amounts.");
    }

    [Fact]
    public void A_claim_is_a_meta_command_and_leaves_a_run_in_flight_untouched()
    {
        var slice = new WorldSlice(
            Worlds.NewPlayer(), Worlds.NewRun(), InboxWorlds.View(InboxWorlds.Message("MSG_a")));

        var result = Claim(slice);

        result.Accepted.ShouldBeTrue(
            "a player can collect their mail mid-run; the meta guard that refuses a handler writing " +
            "the run it was only handed to read is what proves the claim did not touch it.");
    }
}
