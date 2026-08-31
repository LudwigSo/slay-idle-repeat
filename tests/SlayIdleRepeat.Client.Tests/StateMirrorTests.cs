using Shouldly;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The local copy of what the server last said: what moves it, what does not, and what it refuses to
/// be moved backwards by.
/// </summary>
/// <remarks>
/// 🔴 The mirror is never authoritative, so the only thing it can get wrong is <em>what it shows</em>
/// — and the two ways it can get that wrong are announcing a change that did not happen and taking an
/// old answer over a newer one. The first turns every reconnect into a "Caught up." toast a player
/// learns to ignore; the second walks a screen back to a state the player has already left.
/// </remarks>
public sealed class StateMirrorTests
{
    // ---- what the hash decides -------------------------------------------------------------------

    [Fact]
    public void Apply_reports_a_change_and_records_the_projections_when_the_hash_is_new()
    {
        var mirror = new StateMirror();

        var moved = mirror.Apply(NetWorlds.Accepted(sequence: 1, NetWorlds.SomeHash));

        moved.ShouldBeTrue(
            "an empty mirror holds no hash at all, so the first answer it is given is by definition a " +
            "change. If this is red the mirror is treating 'no hash yet' and 'this hash' as the same " +
            "value, and nothing a player is shown would ever be announced as new.");
        mirror.StateHash.ShouldBe(NetWorlds.SomeHash);
        mirror.Sequence.ShouldBe(1);
        mirror.Profile.ShouldNotBeNull(
            "the answer carried a player projection and the mirror is what a screen draws from, so " +
            "dropping it would leave the screen with a hash and nothing to render.");
        mirror.Run.ShouldNotBeNull(
            "the answer carried a run projection. A mirror that recorded the hash but not the row it " +
            "hashes would report 'the state changed' and then show the previous state.");
    }

    [Fact]
    public void Apply_reports_no_change_when_the_hash_is_the_one_already_held()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 1, NetWorlds.SomeHash));

        var moved = mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 2));

        moved.ShouldBeFalse(
            "the state hash covers the whole row on the server's side, so an identical hash IS an " +
            "identical state — that is what the wire carries it for. A red here means the mirror is " +
            "deciding change some other way, and every reconnect that found nothing new would still " +
            "raise the 'Caught up.' toast, which trains a player to ignore the one that matters.");
        mirror.LastChanged.ShouldBeFalse(
            "the flag a scene polls has to agree with the answer the apply returned; two spellings of " +
            "one fact that can disagree is worse than one.");
        mirror.Sequence.ShouldBe(
            2,
            "an unchanged state is still a NEWER answer. Leaving the sequence behind would make the " +
            "next resync ask from a point the client has already passed and re-read outcomes it holds.");
    }

    [Fact]
    public void Apply_reports_a_change_when_the_hash_moves()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 1, NetWorlds.SomeHash));

        var moved = mirror.Apply(NetWorlds.State(NetWorlds.AnotherHash, sequence: 2));

        moved.ShouldBeTrue(
            "a different hash is a different state, and this is the case that decides whether the " +
            "'Caught up.' toast appears at all. If the mirror cannot tell two hashes apart, a resync " +
            "that genuinely moved the game says nothing.");
        mirror.StateHash.ShouldBe(NetWorlds.AnotherHash);
    }

    // ---- an out-of-order answer never walks the mirror backwards ----------------------------------

    /// <summary>
    /// 🔒 Commands are sent in order, but their ANSWERS are separate exchanges that can overtake one
    /// another.
    /// </summary>
    [Fact]
    public void Apply_ignores_a_command_result_whose_sequence_is_behind_what_is_held()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 5, NetWorlds.AnotherHash));

        var moved = mirror.Apply(NetWorlds.Accepted(sequence: 2, NetWorlds.SomeHash));

        moved.ShouldBeFalse(
            "sequence 2 is older than the sequence 5 already held, so it describes a state the player " +
            "has already left. Applying it would be a change in the wrong direction, and the screen " +
            "would visibly rewind.");
        mirror.StateHash.ShouldBe(
            NetWorlds.AnotherHash,
            "the newer answer's hash has to survive the older one arriving. A red here is the actual " +
            "defect this case exists for: a slow reply to an old command overwriting a fast reply to a " +
            "new one, which shows a player a board they have already rolled past.");
        mirror.Sequence.ShouldBe(
            5,
            "the high-water mark is what makes the check work at all. If an ignored answer could still " +
            "drag the sequence down, the next older answer would be accepted and the guard would only " +
            "delay the rewind by one exchange.");
    }

    [Fact]
    public void Apply_takes_a_command_result_at_the_sequence_already_held()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 3, NetWorlds.SomeHash));

        var moved = mirror.Apply(NetWorlds.Accepted(sequence: 3, NetWorlds.AnotherHash));

        moved.ShouldBeTrue(
            "the same sequence is a REPLAY, which is exactly what the server answers a retried command " +
            "id with, and a replay is the authoritative outcome rather than a stale one. Excluding it " +
            "along with the older ones would make the idempotent retry the one exchange the mirror " +
            "refuses to learn from — the negative control for the case above, which must stay a " +
            "statement about OLDER answers and not about every non-advancing one.");
    }

    // ---- a refusal read no state -----------------------------------------------------------------

    [Fact]
    public void Apply_leaves_the_projections_alone_when_the_answer_carried_no_hash()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 1, NetWorlds.SomeHash));

        var moved = mirror.Apply(NetWorlds.Rejected(sequence: 2, RejectionReason.INSUFFICIENT_FUNDS));

        moved.ShouldBeFalse(
            "a refusal rides HTTP 200 with no state hash because the server read no state — there is " +
            "nothing it could have changed. Reporting a change here would flash the resync " +
            "announcement every time a player tapped something they cannot afford.");
        mirror.StateHash.ShouldBe(
            NetWorlds.SomeHash,
            "the refusal carried no projections either. A mirror that copied the nulls in would blank " +
            "the screen on a rejected command, which is the one moment a player most needs to still " +
            "see what they have.");
        mirror.Profile.ShouldNotBeNull();
        mirror.Run.ShouldNotBeNull();
    }

    // ---- the resync read -------------------------------------------------------------------------

    [Fact]
    public void Apply_keeps_the_sequence_it_had_when_a_state_read_reports_none()
    {
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.Accepted(sequence: 7, NetWorlds.SomeHash));

        mirror.Apply(NetWorlds.State(NetWorlds.AnotherHash, sequence: null));

        mirror.Sequence.ShouldBe(
            7,
            "a run-state read answers with a null sequence when the idempotency ledger no longer knows " +
            "the scope, which is an absence of information rather than a report of zero. Resetting to " +
            "zero would make every later command's answer look like an out-of-order one and the mirror " +
            "would stop taking them.");
    }
}
