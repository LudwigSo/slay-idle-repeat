using Shouldly;
using SlayIdleRepeat.Core.Rng;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rng;

/// <summary>
/// 🔒 `14` §8.1's <b>meta</b> draw regime: <em>"draw <c>i</c> is <c>Hash64(CommandSeed, s, i)</c> with
/// <c>i</c> starting at 0 for each command and <b>no persisted counter</b>."</em>
/// </summary>
/// <remarks>
/// 🔒 Every claim is checked against <c>Hash64.Of</c> directly rather than a recorded value: a table
/// generated from the scope itself would prove only that it agrees with itself.
/// <para>
/// ⚠️ The stream names are <c>drops</c> and <c>board</c> deliberately — the registry has no row for a
/// quest or Daily-shop draw, and a container open is a ⚄ meta command that draws gear, so nothing here
/// invents a name. The rows M4-09 will need are a `14` §8.1 amendment, which is why
/// <see cref="An_unregistered_stream_is_refused"/> asserts the refusal rather than working around it.
/// </para>
/// </remarks>
public sealed class MetaDrawScopeTests
{
    private const ulong Seed = 0x0123_4567_89AB_CDEFUL;

    /// <summary>🔒 Draw 0 of a stream is exactly <c>Hash64(CommandSeed, s, 0)</c>.</summary>
    /// <remarks>
    /// <c>NextUInt</c> is the top 32 bits of the draw (`14` §8's <c>DeterministicRng</c>), so the
    /// expectation is written that way rather than compared against the raw 64-bit hash — restating
    /// the projection is what makes this a test of the <em>seed and index</em> rather than of the
    /// projection.
    /// </remarks>
    [Fact]
    public void Draw_zero_is_the_hash_of_the_seed_the_stream_and_zero()
    {
        var drawn = new MetaDrawScope(Seed).Stream(RngStreams.Drops).NextUInt();

        drawn.ShouldBe(
            (uint)(Hash64.Of(Seed, RngStreams.Drops, 0UL) >> 32),
            "14 §8.1: draw i is Hash64(CommandSeed, s, i), and i starts at 0 for each command.");
    }

    /// <summary>🔒 Successive draws walk <c>i = 0, 1, 2 …</c> within the command.</summary>
    /// <remarks>
    /// This is the property M4-09 needs to draw a slate: three quests are draws 0, 1 and 2 of one
    /// stream, not three draws of index 0. A scope that handed back a fresh stream each time would
    /// draw the same quest three times, and <see cref="Draw_zero_is_the_hash_of_the_seed_the_stream_and_zero"/>
    /// would still pass.
    /// </remarks>
    [Fact]
    public void Successive_draws_advance_the_index_within_the_command()
    {
        var scope = new MetaDrawScope(Seed);
        var stream = scope.Stream(RngStreams.Drops);

        var drawn = new[] { stream.NextUInt(), stream.NextUInt(), stream.NextUInt() };

        drawn.ShouldBe(new[]
        {
            (uint)(Hash64.Of(Seed, RngStreams.Drops, 0UL) >> 32),
            (uint)(Hash64.Of(Seed, RngStreams.Drops, 1UL) >> 32),
            (uint)(Hash64.Of(Seed, RngStreams.Drops, 2UL) >> 32),
        });

        stream.Position.ShouldBe(3UL, "Position is the NEXT draw index, and every call is one index.");
    }

    /// <summary>
    /// 🔒 One name yields the <b>same</b> stream for the whole command — a second ask continues the
    /// sequence rather than restarting it.
    /// </summary>
    /// <remarks>
    /// ⚠️ The failure this closes is silent and generous: a scope that re-opened the stream would hand
    /// back <em>the same value twice</em>, so a quest slate drawn in two places would contain
    /// duplicates and a wheel spun twice in one command would land on one segment. It is the same
    /// property <c>RunRngScope</c> owns for the run streams.
    /// </remarks>
    [Fact]
    public void One_name_is_one_stream_for_the_whole_command()
    {
        var scope = new MetaDrawScope(Seed);

        var first = scope.Stream(RngStreams.Drops).NextUInt();
        var second = scope.Stream(RngStreams.Drops).NextUInt();

        first.ShouldNotBe(second, "the second ask must continue the sequence, not restart it at 0.");
        second.ShouldBe((uint)(Hash64.Of(Seed, RngStreams.Drops, 1UL) >> 32), "…at index 1, specifically.");

        scope.Stream(RngStreams.Drops).Position.ShouldBe(2UL);
    }

    /// <summary>🔒 Two streams over one seed are independent: consuming one does not shift the other.</summary>
    /// <remarks>
    /// `14` §8.1's stated reason for named streams at all — <em>"so consuming randomness in one system
    /// never shifts another"</em>. Under the meta regime the same has to hold, or M4-09's quest draw
    /// would move the Daily shop block depending on how many quests the pool happened to reject.
    /// </remarks>
    [Fact]
    public void Two_streams_over_one_seed_do_not_shift_each_other()
    {
        var scope = new MetaDrawScope(Seed);

        scope.Stream(RngStreams.Drops).NextUInt();
        scope.Stream(RngStreams.Drops).NextUInt();

        scope.Stream(RngStreams.Board).NextUInt().ShouldBe(
            (uint)(Hash64.Of(Seed, RngStreams.Board, 0UL) >> 32),
            "the board stream is still at draw 0 after two drops draws.");
    }

    /// <summary>
    /// 🔒 <b>No persisted counter:</b> a second scope over the same seed starts again at draw 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 This is the whole difference from <c>RunRngScope</c>, and it is a <b>feature</b> rather than
    /// an omission: `14` §16.3's idempotency <em>replays a resubmitted command's stored outcome</em>,
    /// so a meta draw can never be re-rolled by resubmission — and therefore needs no counter to stop
    /// it being. A counter here would be state nothing folds back and nothing reads.
    /// </para>
    /// <para>
    /// ⚠️ It is asserted because the alternative is invisible: a scope that <em>did</em> carry a
    /// counter across commands would produce perfectly plausible values, and only a bug report asking
    /// "why did the replay differ" would ever find it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_second_scope_over_the_same_seed_starts_at_draw_zero_again()
    {
        var first = new MetaDrawScope(Seed);
        first.Stream(RngStreams.Drops).NextUInt();
        first.Stream(RngStreams.Drops).NextUInt();

        new MetaDrawScope(Seed).Stream(RngStreams.Drops).Position.ShouldBe(
            0UL,
            "there is no persisted counter (14 §8.1): each command's draws start at 0. Carrying one " +
            "across commands would make an idempotent replay produce a different outcome from the one " +
            "14 §16.3 promises to return.");
    }

    /// <summary>🔒 Zero is a legitimate seed, not an absence.</summary>
    /// <remarks>
    /// <c>GameContext.CommandSeed</c> is <see cref="Nullable{T}"/> precisely because of this: a
    /// sentinel <c>0</c> meaning "no seed" would make one seed in 2^64 silently unusable, and the
    /// host has no way to know it drew it.
    /// </remarks>
    [Fact]
    public void Zero_is_a_seed_like_any_other()
    {
        new MetaDrawScope(0).Stream(RngStreams.Drops).NextUInt().ShouldBe(
            (uint)(Hash64.Of(0UL, RngStreams.Drops, 0UL) >> 32));
    }

    /// <summary>
    /// 🔒 A stream name `14` §8.1 does not carry is <b>refused</b> — which is what defers the quest and
    /// Daily-shop draws rather than letting them be spelled into existence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The names in the theory below are the ones M4-09 would plausibly reach for</b>, and that
    /// is the point: `14` §8.1's registry is closed — <em>"a system that needs randomness draws from
    /// one of these streams or gets a new row here"</em> — so adding the rows is a document amendment,
    /// not a string literal at a call site. The same seed and a different string is a different
    /// sequence, silently.
    /// </para>
    /// <para>
    /// 🔒 The refusal comes from <c>DeterministicRng</c>'s own guard rather than from a second copy in
    /// the scope, so there is one registry and one message.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("quests")]
    [InlineData("daily_shop")]
    [InlineData("DROPS")]
    [InlineData("")]
    public void An_unregistered_stream_is_refused(string streamName)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new MetaDrawScope(Seed).Stream(streamName));

        thrown.Message.ShouldContain(
            "14 §8.1",
            Case.Sensitive,
            "the message must name the registry, because the fix is to amend it — not to widen the " +
            "guard. 'DROPS' is in the list to pin the ORDINAL comparison: under a case-insensitive " +
            "one it would silently be the drops stream.");
    }

    /// <summary>A null stream name is refused as a null, not as an unregistered name.</summary>
    [Fact]
    public void A_null_stream_name_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new MetaDrawScope(Seed).Stream(null!));
    }
}
