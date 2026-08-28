using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Moderation;

/// <summary>
/// The background sweep, driven directly rather than through a host: what it observes, what it
/// measures, what it raises — and, on the shipped envelope, that it raises nothing at all.
/// </summary>
public sealed class PlausibilitySweepTests
{
    private static readonly PlayerId Alice = new("PLAYER_alice");
    private static readonly PlayerId Bob = new("PLAYER_bob");

    private sealed record Rig(
        PlausibilitySweep Sweep, VolatileModerationStore Store, AdjustableClock Clock);

    private static Rig Build(PlausibilityEnvelope? envelope = null)
    {
        var store = new VolatileModerationStore();
        var clock = new AdjustableClock();

        return new Rig(
            new PlausibilitySweep(store, clock, new CountingIdGenerator(), envelope ?? PlausibilityEnvelope.Unauthored),
            store,
            clock);
    }

    private static PlausibilityObservation Account(
        PlayerId player, long wallet, long xp, int mismatches) =>
        new(player, AdjustableClock.Start, wallet, xp, mismatches);

    [Fact]
    public async Task The_first_sweep_measures_nothing_because_there_is_nothing_to_measure_against()
    {
        var rig = Build(new PlausibilityEnvelope(MaxCurrencyPerDay: 1));
        rig.Store.SetObservableAccounts([Account(Alice, 1_000_000, 0, 0), Account(Bob, 0, 0, 0)]);

        var result = await rig.Sweep.RunOnceAsync(CancellationToken.None);

        result.AccountsObserved.ShouldBe(2);
        result.DeltasMeasured.ShouldBe(
            0,
            "a cumulative total is not a trajectory. An account's first reading has no predecessor, "
            + "and treating a lifetime balance as a one-tick gain would flag every existing player "
            + "the moment the sweep was first enabled.");
        result.FlagsRaised.ShouldBe(0);
    }

    [Fact]
    public async Task The_second_sweep_measures_each_account_against_its_own_previous_reading()
    {
        var rig = Build(new PlausibilityEnvelope(MaxCurrencyPerDay: 1_000));
        rig.Store.SetObservableAccounts([Account(Alice, 100, 0, 0), Account(Bob, 100, 0, 0)]);

        await rig.Sweep.RunOnceAsync(CancellationToken.None);

        rig.Clock.Advance(TimeSpan.FromDays(1));
        rig.Store.SetObservableAccounts([Account(Alice, 100_100, 0, 0), Account(Bob, 200, 0, 0)]);

        var result = await rig.Sweep.RunOnceAsync(CancellationToken.None);

        result.DeltasMeasured.ShouldBe(2);
        result.FlagsRaised.ShouldBe(
            1,
            "Alice gained 100 000 in a day against a 1 000/day threshold; Bob gained 100 and is not "
            + "flagged — the negative control that stops this from being 'everything trips'.");

        var queued = await rig.Store.ReadReviewsAsync(ReviewState.OPEN, CancellationToken.None);

        queued.Count.ShouldBe(1);
        queued[0].Subject.ShouldBe(Alice, "and the flag names the account it is about.");
        queued[0].Source.ShouldBe(ReviewSource.PLAUSIBILITY_SWEEP);
        queued[0].State.ShouldBe(
            ReviewState.OPEN,
            "🔒 flags go to a review queue, never to an automatic action. Nothing this class can "
            + "produce is already decided.");
        queued[0].Reason.ShouldContain("100000", Case.Sensitive);
    }

    /// <summary>🔒 The claim the whole job is built around, driven end to end.</summary>
    [Fact]
    public async Task On_the_shipped_envelope_the_sweep_observes_everything_and_flags_nothing()
    {
        var rig = Build();
        rig.Store.SetObservableAccounts([Account(Alice, 0, 0, 0)]);

        await rig.Sweep.RunOnceAsync(CancellationToken.None);

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        rig.Store.SetObservableAccounts([Account(Alice, long.MaxValue / 4, long.MaxValue / 4, 5_000)]);

        var result = await rig.Sweep.RunOnceAsync(CancellationToken.None);

        result.DeltasMeasured.ShouldBe(
            1, "it still measures — the skeleton is complete except for the numbers nobody authored.");
        result.FlagsRaised.ShouldBe(
            0,
            "and it flags nothing, because every threshold is null. This is the difference between "
            + "a job that is off and a job that is watching for a number no one has written down.");

        (await rig.Store.ReadReviewsAsync(ReviewState.OPEN, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_measure_raises_its_own_entry_so_a_reviewer_sees_which_trajectory_tripped()
    {
        var rig = Build(new PlausibilityEnvelope(
            MaxCurrencyPerDay: 10, MaxLegendXpPerDay: 10, MaxBattleHashMismatchesPerDay: 10));

        rig.Store.SetObservableAccounts([Account(Alice, 0, 0, 0)]);
        await rig.Sweep.RunOnceAsync(CancellationToken.None);

        rig.Clock.Advance(TimeSpan.FromDays(1));
        rig.Store.SetObservableAccounts([Account(Alice, 1_000, 1_000, 1_000)]);

        var result = await rig.Sweep.RunOnceAsync(CancellationToken.None);

        result.FlagsRaised.ShouldBe(3);

        var queued = await rig.Store.ReadReviewsAsync(ReviewState.OPEN, CancellationToken.None);

        foreach (var measure in Enum.GetValues<PlausibilityMeasure>())
        {
            queued.ShouldContain(
                entry => entry.Reason.Contains(measure.ToString(), StringComparison.Ordinal),
                $"the reviewer opens the reason text, not this record — without {measure} named in "
                + "it they cannot tell which trajectory tripped. Three entries that all said "
                + "'currency' would satisfy a bare count.");
        }

        queued.Select(entry => entry.EntryId).Distinct().Count().ShouldBe(
            3, "three findings are three entries, each with its own identity a reviewer can close.");
    }

    [Fact]
    public async Task The_sweep_stores_the_reading_it_took_so_the_next_one_measures_from_there()
    {
        var rig = Build();
        rig.Store.SetObservableAccounts([Account(Alice, 500, 40, 2)]);

        rig.Clock.Advance(TimeSpan.FromHours(3));
        await rig.Sweep.RunOnceAsync(CancellationToken.None);

        var stored = await rig.Store.ReadPreviousObservationAsync(Alice, CancellationToken.None);

        stored.ShouldNotBeNull();
        stored.WalletTotal.ShouldBe(500);
        stored.LegendXp.ShouldBe(40);
        stored.BattleHashMismatches.ShouldBe(2);
        stored.ObservedAtUtc.ShouldBe(
            rig.Clock.UtcNow,
            "the reading is stamped with the sweep's own instant, not with whatever the row was "
            + "last written at — the window between two sweeps is what the rate is measured over.");
    }

    [Fact]
    public async Task An_account_that_disappears_between_sweeps_is_simply_not_measured()
    {
        var rig = Build(new PlausibilityEnvelope(MaxCurrencyPerDay: 1));

        rig.Store.SetObservableAccounts([Account(Alice, 0, 0, 0), Account(Bob, 0, 0, 0)]);
        await rig.Sweep.RunOnceAsync(CancellationToken.None);

        rig.Clock.Advance(TimeSpan.FromDays(1));
        rig.Store.SetObservableAccounts([Account(Bob, 5_000, 0, 0)]);

        var result = await rig.Sweep.RunOnceAsync(CancellationToken.None);

        result.AccountsObserved.ShouldBe(1);
        result.FlagsRaised.ShouldBe(1, "Bob is still measured against Bob's own previous reading.");
    }

    /// <summary>
    /// The refresh publishes exactly the live locks — and it evaluates them at the injected clock's
    /// instant, which the lifted and not-yet-started accounts are what prove.
    /// </summary>
    [Fact]
    public async Task The_standing_refresh_publishes_exactly_the_accounts_a_live_account_action_locks()
    {
        var rig = Build();
        var snapshot = new AccountStandingSnapshot();

        var lifted = new PlayerId("PLAYER_lifted");
        var future = new PlayerId("PLAYER_future");
        var excluded = new PlayerId("PLAYER_excluded");
        var renamed = new PlayerId("PLAYER_renamed");

        rig.Store.AddSanction(new PlayerSanction(
            "SAN_1", Alice, SanctionKind.ACCOUNT_ACTION, "REV_1", "ops.rita", AdjustableClock.Start));
        rig.Store.AddSanction(new PlayerSanction(
            "SAN_2", excluded, SanctionKind.SHADOW_EXCLUDE_LADDER, "REV_2", "ops.rita", AdjustableClock.Start));
        rig.Store.AddSanction(new PlayerSanction(
            "SAN_3", Bob, SanctionKind.RATING_RESET, "REV_3", "ops.rita", AdjustableClock.Start));
        rig.Store.AddSanction(new PlayerSanction(
            "SAN_4", renamed, SanctionKind.NAME_RESET, "REV_4", "ops.rita", AdjustableClock.Start));
        rig.Store.AddSanction(new PlayerSanction(
            "SAN_5", lifted, SanctionKind.ACCOUNT_ACTION, "REV_5", "ops.rita",
            AdjustableClock.Start, AdjustableClock.Start.AddMinutes(1)));
        rig.Store.AddSanction(new PlayerSanction(
            "SAN_6", future, SanctionKind.ACCOUNT_ACTION, "REV_6", "ops.rita",
            AdjustableClock.Start.AddMinutes(10)));

        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        await rig.Sweep.RefreshStandingAsync(snapshot, CancellationToken.None);

        snapshot.IsAccountActioned(Alice).ShouldBeTrue();
        snapshot.IsAccountActioned(lifted).ShouldBeFalse(
            "lifted four minutes ago. A refresh that read the wall clock, or passed MaxValue as the "
            + "instant, would still lock this account.");
        snapshot.IsAccountActioned(future).ShouldBeFalse(
            "not applied for another five minutes. A refresh that passed MinValue, or ignored the "
            + "instant entirely, would lock it early — the two probes pin the window from both sides.");

        foreach (var unlocked in new[] { excluded, Bob, renamed })
        {
            snapshot.IsAccountActioned(unlocked).ShouldBeFalse(
                "only an account action is a lock; publishing any other rung as one would turn the "
                + "quietest sanctions in the ladder into the loudest.");
        }

        snapshot.LockedAccounts.ShouldBe(1);
    }

    [Fact]
    public void The_sweep_refuses_to_be_composed_without_any_of_its_four_pieces()
    {
        var store = new VolatileModerationStore();
        var clock = new AdjustableClock();
        var ids = new CountingIdGenerator();

        Should.Throw<ArgumentNullException>(() => new PlausibilitySweep(null!, clock, ids, PlausibilityEnvelope.Unauthored));
        Should.Throw<ArgumentNullException>(() => new PlausibilitySweep(store, null!, ids, PlausibilityEnvelope.Unauthored));
        Should.Throw<ArgumentNullException>(() => new PlausibilitySweep(store, clock, null!, PlausibilityEnvelope.Unauthored));
        Should.Throw<ArgumentNullException>(() => new PlausibilitySweep(store, clock, ids, null!));
    }
}
