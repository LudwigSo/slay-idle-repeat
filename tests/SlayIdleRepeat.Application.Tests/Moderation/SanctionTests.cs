using Shouldly;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Moderation;

/// <summary>
/// The sanctions ladder as data, and the one rung that is not: an account action is HTTP 403 and
/// every other kind is HTTP nothing at all.
/// </summary>
public sealed class SanctionTests
{
    private static readonly PlayerId Subject = new("PLAYER_subject");
    private static readonly DateTimeOffset Applied = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static PlayerSanction Sanction(SanctionKind kind, DateTimeOffset? lifted = null) =>
        new("SAN_1", Subject, kind, "REV_1", "ops.rita", Applied, lifted);

    [Fact]
    public void The_ladder_is_the_three_rungs_plus_the_name_outcome()
    {
        Enum.GetValues<SanctionKind>().ShouldBe(
            new[]
            {
                SanctionKind.SHADOW_EXCLUDE_LADDER,
                SanctionKind.RATING_RESET,
                SanctionKind.ACCOUNT_ACTION,
                SanctionKind.NAME_RESET,
            },
            ignoreOrder: true);

        ((int)SanctionKind.SHADOW_EXCLUDE_LADDER).ShouldBeLessThan(
            (int)SanctionKind.RATING_RESET,
            "the three rungs are numbered in escalation order, so a stored value reads as a "
            + "position on the ladder rather than as an arbitrary tag.");
        ((int)SanctionKind.RATING_RESET).ShouldBeLessThan((int)SanctionKind.ACCOUNT_ACTION);
    }

    [Fact]
    public void An_active_account_action_is_the_only_kind_that_refuses_a_request()
    {
        AccountStanding
            .RefusalStatusFor([Sanction(SanctionKind.ACCOUNT_ACTION)], Applied.AddDays(1))
            .ShouldBe(
                403,
                "an account action locks the account, and the client shows the account-state screen.");
    }

    [Theory]
    [InlineData(SanctionKind.SHADOW_EXCLUDE_LADDER)]
    [InlineData(SanctionKind.RATING_RESET)]
    [InlineData(SanctionKind.NAME_RESET)]
    public void Every_other_kind_leaves_the_account_served(SanctionKind kind)
    {
        AccountStanding.RefusalStatusFor([Sanction(kind)], Applied.AddDays(1)).ShouldBeNull(
            $"{kind} is recorded and acted on by the surface that owns it. A shadow ladder "
            + "exclusion that refused requests would stop being shadow; a rating or name reset is a "
            + "one-off write, not a standing refusal.");
    }

    [Fact]
    public void A_lifted_account_action_stops_refusing_at_the_instant_it_was_lifted()
    {
        var lifted = Sanction(SanctionKind.ACCOUNT_ACTION, Applied.AddDays(7));

        AccountStanding.RefusalStatusFor([lifted], Applied.AddDays(7).AddTicks(-1)).ShouldBe(403);
        AccountStanding.RefusalStatusFor([lifted], Applied.AddDays(7)).ShouldBeNull(
            "lifted-at is exclusive: the moment it is lifted, the account is served again.");
    }

    [Fact]
    public void An_account_action_that_has_not_started_yet_refuses_nothing()
    {
        AccountStanding
            .RefusalStatusFor([Sanction(SanctionKind.ACCOUNT_ACTION)], Applied.AddTicks(-1))
            .ShouldBeNull("applied-at is inclusive, so a tick earlier is a tick before the lock.");

        AccountStanding
            .RefusalStatusFor([Sanction(SanctionKind.ACCOUNT_ACTION)], Applied)
            .ShouldBe(403);
    }

    [Fact]
    public void No_sanctions_at_all_refuses_nothing()
    {
        AccountStanding.RefusalStatusFor([], Applied).ShouldBeNull();
    }

    [Fact]
    public void A_lifted_account_action_beside_a_live_one_still_refuses()
    {
        var sanctions = new[]
        {
            Sanction(SanctionKind.ACCOUNT_ACTION, Applied.AddDays(1)),
            new PlayerSanction("SAN_2", Subject, SanctionKind.ACCOUNT_ACTION, "REV_2", "ops.sam", Applied.AddDays(2)),
        };

        AccountStanding.RefusalStatusFor(sanctions, Applied.AddDays(3)).ShouldBe(
            403,
            "the question is whether ANY account action stands, not whether the first one does.");
    }

    [Fact]
    public void The_standing_snapshot_locks_exactly_the_accounts_with_a_live_account_action()
    {
        var other = new PlayerId("PLAYER_other");
        var snapshot = new AccountStandingSnapshot();

        snapshot.IsAccountActioned(Subject).ShouldBeFalse(
            "a process that has not refreshed locks nobody — refusing service on absent moderation "
            + "data would take the whole game down when the moderation store did.");

        snapshot.Replace(
            [
                Sanction(SanctionKind.ACCOUNT_ACTION),
                new PlayerSanction("SAN_2", other, SanctionKind.SHADOW_EXCLUDE_LADDER, "REV_2", "ops", Applied),
            ],
            Applied.AddDays(1));

        snapshot.IsAccountActioned(Subject).ShouldBeTrue();
        snapshot.IsAccountActioned(other).ShouldBeFalse(
            "a ladder exclusion is not a lock — the negative control that stops this snapshot from "
            + "being 'every sanctioned account'.");
        snapshot.LockedAccounts.ShouldBe(1);
    }

    [Fact]
    public void Replacing_the_snapshot_releases_accounts_whose_action_was_lifted()
    {
        var snapshot = new AccountStandingSnapshot();

        snapshot.Replace([Sanction(SanctionKind.ACCOUNT_ACTION)], Applied);
        snapshot.IsAccountActioned(Subject).ShouldBeTrue();

        snapshot.Replace([Sanction(SanctionKind.ACCOUNT_ACTION, Applied.AddHours(1))], Applied.AddHours(2));

        snapshot.IsAccountActioned(Subject).ShouldBeFalse(
            "the set is swapped whole, so a lifted action releases the account on the next refresh "
            + "rather than surviving as a stale lock nobody can clear.");
        snapshot.LockedAccounts.ShouldBe(0);
    }

    [Fact]
    public void A_sanction_knows_its_own_window()
    {
        var open = Sanction(SanctionKind.RATING_RESET);

        open.IsActiveAt(Applied.AddTicks(-1)).ShouldBeFalse();
        open.IsActiveAt(Applied).ShouldBeTrue();
        open.IsActiveAt(Applied.AddYears(10)).ShouldBeTrue("an unlifted sanction never expires by itself.");
    }
}
