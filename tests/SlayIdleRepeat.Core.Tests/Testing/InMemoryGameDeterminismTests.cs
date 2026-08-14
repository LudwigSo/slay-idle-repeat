using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>🔒 `30` §6 — <em>"A fixed seed. Reproducible byte-for-byte."</em></summary>
/// <remarks>
/// 🔒 Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not record equality:
/// <c>PlayerSnapshot</c> carries three dictionaries and a record compares those by <b>reference</b>, so
/// two snapshots describing the identical player are <em>never</em> equal and such a test would fail
/// for a reason unrelated to determinism.
/// <para>
/// ⚠️ Nothing in M1 <em>draws</em> — the two daily draws are M4-09's — so the seed's effect on state is
/// <b>zero</b> today. The test that says so is the honest one, and it is the tripwire that goes red on
/// the commit M4-09 takes the draw.
/// </para>
/// </remarks>
public sealed class InMemoryGameDeterminismTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    /// <summary>
    /// 🔒 Two fresh harnesses, same seed, same command sequence: identical state and an identical
    /// event list.
    /// </summary>
    /// <remarks>
    /// The event list is compared as a <b>sequence</b>, not as a set and not by count: `14` §7.1's
    /// economy log and `14` §2.4's animation script both read the order, and a harness that produced
    /// the right rows in the wrong order would satisfy any weaker comparison. Every M1 event is a
    /// <c>CurrencyChanged</c>, which is a record over four value-typed components, so sequence
    /// equality here is genuine value equality rather than reference identity.
    /// </remarks>
    [Fact]
    public void Two_fresh_harnesses_with_one_seed_produce_identical_state_and_events()
    {
        var (firstState, firstEvents) = Run(Harnesses.Seed);
        var (secondState, secondEvents) = Run(Harnesses.Seed);

        firstState.ShouldBe(secondState);
        firstEvents.ShouldBe(secondEvents);

        firstEvents.Count.ShouldBeGreaterThan(
            Days,
            "a 180-day drive produces at least one row per game day; if this list were empty the " +
            "comparison above would hold over nothing (S3).");
    }

    /// <summary>
    /// 🔒 The same drive, twice inside <b>one process</b>: nothing static accumulates between runs.
    /// </summary>
    /// <remarks>
    /// A different failure from the one above and worth its own test. Two harnesses built in one
    /// process share every static in <c>Core</c> — <c>GameRules</c>' dispatch table above all, which
    /// is built once in the static initialiser. A table that mutated, a cached tuning that held a
    /// player's numbers, or a counter that lived on a static would make the second simulation of a
    /// process differ from the first, and `21` §9 runs 14 profiles × 200 seeds in one process.
    /// </remarks>
    [Fact]
    public void The_same_drive_twice_in_one_process_gives_the_same_answer()
    {
        var first = Run(Harnesses.Seed);
        var second = Run(Harnesses.Seed);
        var third = Run(Harnesses.Seed);

        second.State.ShouldBe(first.State);
        third.State.ShouldBe(first.State);
        second.Events.ShouldBe(first.Events);
        third.Events.ShouldBe(first.Events);
    }

    /// <summary>
    /// 🔒 Two harnesses in flight <b>at once</b>, interleaved, do not contaminate each other.
    /// </summary>
    /// <remarks>
    /// The sequential test runs each simulation to completion and cannot see this; `21` §9's sweep is a
    /// loop over profiles, and a harness holding shared mutable state would produce a result that
    /// depended on the interleaving.
    /// <para>
    /// 🔴 It does <b>not</b> pin that the per-command seed counter is per player rather than global, and
    /// structurally cannot: each harness holds one player, so a global counter produces byte-identical
    /// sequences anyway, and nothing in M1 reads <c>CommandSeed</c> at all. Measured — deriving from the
    /// global counter leaves every determinism test green. That becomes testable on M4-09's first draw.
    /// </para>
    /// </remarks>
    [Fact]
    public void Two_interleaved_harnesses_do_not_contaminate_each_other()
    {
        var alone = Run(Harnesses.Seed);

        var left = Harnesses.New();
        var leftPlayer = left.CreatePlayer();
        var right = Harnesses.New(seed: Harnesses.Seed ^ 0xFFFF_FFFF_FFFF_FFFFUL);
        var rightPlayer = right.CreatePlayer();

        for (var day = 0; day < Days; day++)
        {
            Harnesses.DriveDay(left, leftPlayer, CommandsPerDay);
            Harnesses.DriveDay(right, rightPlayer, CommandsPerDay);
        }

        CanonicalStateWriter.HashMetaCommandState(left.State(leftPlayer).Player.ToSnapshot())
            .ShouldBe(
                alone.State,
                "the left harness ran the SAME drive as the sequential one — DriveDay, not a " +
                "hand-written cadence — with another simulation running a day between every one of " +
                "its days.");

        CanonicalStateWriter.HashMetaCommandState(right.State(rightPlayer).Player.ToSnapshot())
            .ShouldBe(alone.State, "…and so did the right one, on a different seed (see below).");

        left.Events.ShouldBe(
            alone.Events,
            "the event lists are per harness: the interleaving must not merge one simulation's rows " +
            "into another's, which a static accumulator would.");
    }

    /// <summary>
    /// 🔒 Two <b>different</b> seeds produce the same state today — a statement with an expiry rather
    /// than a weak assertion.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a claim that the seed does not matter: a claim that <b>nothing in M1 draws</b>. The two
    /// things that draw from the day's seed are M4-09's, and <c>BeginSession</c> deliberately takes the
    /// seam and draws nothing.
    /// <para>
    /// 🔒 It is the tripwire on that deferral: the commit taking the first draw turns this red, which is
    /// when someone must replace it with <em>different seeds produce different draws, the same seed
    /// reproduces them</em>. Without it every test above keeps passing over a seed nobody could tell was
    /// being ignored.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_different_seed_changes_nothing_yet_and_this_test_expires_at_M4_09()
    {
        var first = Run(Harnesses.Seed);
        var second = Run(Harnesses.Seed ^ 0xDEAD_BEEF_CAFE_F00DUL);
        var zero = Run(0UL);

        second.State.ShouldBe(
            first.State,
            "nothing in M1 draws from CommandSeed. When M4-09 takes 19 B's quest slate or 10 §5.1's " +
            "Daily shop block from the day's draw seed, this goes RED — and that is the point: " +
            "replace it then with 'different seeds draw differently, the same seed reproduces', do " +
            "not delete it.");

        zero.State.ShouldBe(
            first.State,
            "…including seed 0, which is a legitimate seed and not an absence (30 §3).");

        second.Events.ShouldBe(first.Events);
    }

    /// <summary>
    /// 🔒 The comparison itself has teeth: a drive that differs by <b>one command</b> produces a
    /// different state hash.
    /// </summary>
    /// <remarks>
    /// Without this every determinism assertion above could be satisfied by a
    /// <c>HashMetaCommandState</c> that answered a constant, or by a <c>Run</c> that discarded its
    /// simulation and hashed a fresh player. Steering <b>S1</b>: a comparison that cannot report a
    /// difference is not a comparison.
    /// </remarks>
    [Fact]
    public void The_state_comparison_reports_a_difference_when_there_is_one()
    {
        var full = Run(Harnesses.Seed);
        var shorter = Run(Harnesses.Seed, days: Days - 1);

        shorter.State.ShouldNotBe(full.State);
        shorter.Events.Count.ShouldBeLessThan(full.Events.Count);
    }

    /// <summary>
    /// One 180-day drive: the player's `14` §16.6 <c>stateHash</c> and the whole event list.
    /// </summary>
    private static (string State, IReadOnlyList<DomainEvent> Events) Run(ulong seed, int days = Days)
    {
        var (game, player) = Harnesses.WithPlayer(seed: seed);

        Harnesses.Drive(game, player, days, CommandsPerDay);

        return (
            CanonicalStateWriter.HashMetaCommandState(game.State(player).Player.ToSnapshot()),
            game.Events.ToArray());
    }
}
