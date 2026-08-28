using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The in-memory pin store: what a pin answers before anything wrote one, what a rewrite does to
/// the one that was there, and what a sweep is handed.
/// </summary>
public sealed class ContentPinsTests
{
    private static readonly ContentVersion First = ContentVersion.FromHex(new string('1', ContentVersion.HexLength));
    private static readonly ContentVersion Second = ContentVersion.FromHex(new string('2', ContentVersion.HexLength));
    private static readonly DateTimeOffset Noon = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task An_unpinned_run_and_an_unpinned_player_both_answer_nothing()
    {
        var store = new InMemoryContentPinStore();

        (await store.ReadRunPinAsync(new RunId("RUN_never_pinned"), Cancel)).ShouldBeNull(
            "an absent pin is the answer that sends a command to the current snapshot; a stamp " +
            "invented here would pin a run to content nobody chose");
        (await store.ReadSessionPinAsync(new PlayerId("PLAYER_never_pinned"), Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task A_written_pin_reads_back_as_the_version_it_was_written_with()
    {
        var store = new InMemoryContentPinStore();
        var run = new RunId("RUN_1");
        var player = new PlayerId("PLAYER_1");

        await store.WriteRunPinAsync(run, First, Noon, Cancel);
        await store.WriteSessionPinAsync(player, Second, Noon, Cancel);

        (await store.ReadRunPinAsync(run, Cancel)).ShouldBe(First);
        (await store.ReadSessionPinAsync(player, Cancel)).ShouldBe(Second);
    }

    [Fact]
    public async Task A_rewritten_session_pin_replaces_the_one_before_it()
    {
        var store = new InMemoryContentPinStore();
        var player = new PlayerId("PLAYER_1");

        await store.WriteSessionPinAsync(player, First, Noon, Cancel);
        await store.WriteSessionPinAsync(player, Second, Noon.AddHours(1), Cancel);

        (await store.ReadSessionPinAsync(player, Cancel)).ShouldBe(
            Second,
            "a session pin is the player's CURRENT answer and moves on the next session begin — " +
            "keeping the older one would judge a returning client against content it has replaced");
        (await store.ListRetainedAsync(Cancel)).Select(retained => retained.Version).ShouldBe(
            [Second], "the replaced version is no longer referenced by anything, so nothing retains it");
    }

    [Fact]
    public async Task A_run_and_a_player_spelled_the_same_hold_separate_pins()
    {
        var store = new InMemoryContentPinStore();

        await store.WriteRunPinAsync(new RunId("shared_text"), First, Noon, Cancel);
        await store.WriteSessionPinAsync(new PlayerId("shared_text"), Second, Noon, Cancel);

        (await store.ReadRunPinAsync(new RunId("shared_text"), Cancel)).ShouldBe(
            First,
            "run pins and session pins are two spaces: an identifier that happens to be spelled the " +
            "same in both must not let a session begin move a run's pin, which never moves");
        (await store.ReadSessionPinAsync(new PlayerId("shared_text"), Cancel)).ShouldBe(Second);
    }

    [Fact]
    public async Task A_version_two_pins_name_is_retained_once_at_the_later_of_the_two_instants()
    {
        var store = new InMemoryContentPinStore();

        await store.WriteRunPinAsync(new RunId("RUN_1"), First, Noon, Cancel);
        await store.WriteSessionPinAsync(new PlayerId("PLAYER_1"), First, Noon.AddHours(6), Cancel);
        await store.WriteRunPinAsync(new RunId("RUN_2"), Second, Noon.AddHours(2), Cancel);

        var retained = await store.ListRetainedAsync(Cancel);

        retained.Count.ShouldBe(2, "one entry per VERSION, not one per pin — a sweep decides about bundles");
        retained.Single(entry => entry.Version.Equals(First)).LastReferencedAtUtc.ShouldBe(
            Noon.AddHours(6),
            "the newest reference is what keeps a bundle alive; taking the run pin's earlier instant " +
            "would sweep a version a live session is still on");
        retained.Single(entry => entry.Version.Equals(Second)).LastReferencedAtUtc.ShouldBe(Noon.AddHours(2));
    }
}
