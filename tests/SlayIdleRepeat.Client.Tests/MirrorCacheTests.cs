using System.Text;
using Shouldly;
using SlayIdleRepeat.Adapters.Cache.LocalFile;
using SlayIdleRepeat.Client.Game.Net;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The read-only mirror as it survives a restart: what is stored, how, and why a hit can never
/// decide anything.
/// </summary>
/// <remarks>
/// 🔒 The serializer sits entirely ABOVE the byte cache — the port stays byte-oriented, so what is
/// actually on disk is assertable rather than each implementation's private business.
/// </remarks>
public sealed class MirrorCacheTests : IDisposable
{
    /// <summary>The two bytes every gzip member starts with.</summary>
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    private readonly string _cacheRoot = RepoPaths.ScratchCacheRoot();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Persist_then_restore_answers_what_the_server_said()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var stored = new StateMirror();
        stored.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 7));

        await new MirrorCache(cache).PersistAsync(stored, CancellationToken.None);

        var restored = new StateMirror();
        var hit = await new MirrorCache(cache).RestoreAsync(restored, CancellationToken.None);

        hit.ShouldBeTrue(
            "the whole point of the mirror is an instant cold start: a restore that reports a miss " +
            "over a store it just wrote leaves the first screen blank until the network answers.");
        restored.StateHash.ShouldBe(
            NetWorlds.SomeHash,
            "the hash is what decides whether a later server answer moved anything. A restore that " +
            "dropped it would make the first real answer look like a change and announce a resync " +
            "for a state the player was already looking at.");
        restored.Sequence.ShouldBe(
            7,
            "and the sequence is what stops a slow answer to an old command walking the screen " +
            "backwards. A mirror restored at zero treats every stale answer as news.");
        restored.Profile.ShouldNotBeNull(
            "a mirror with a hash and no projections has nothing to draw, which is the one thing it " +
            "exists for.");
        restored.Run.ShouldNotBeNull("likewise the run the resume card is written from");
    }

    [Fact]
    public async Task The_stored_bytes_are_gzip()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var mirror = new StateMirror();
        mirror.Apply(NetWorlds.State(NetWorlds.SomeHash));

        await new MirrorCache(cache).PersistAsync(mirror, CancellationToken.None);
        var bytes = await cache.ReadAsync(MirrorCache.MirrorKey, CancellationToken.None);

        bytes.ShouldNotBeNull(
            $"nothing was written under '{MirrorCache.MirrorKey}', so the mirror is not being " +
            "persisted at all and the round-trip above is passing on something else.");
        bytes.Take(GzipMagic.Length).ShouldBe(
            GzipMagic,
            "the cache is specified as gzipped, and it holds two whole projections of a profile and " +
            "a run — the shape that compresses best and the one a handset can least afford to write " +
            "raw. Asserted on the magic bytes rather than on a length, because a length comparison " +
            "passes for any encoding that happens to be shorter.");
    }

    [Fact]
    public async Task A_malformed_blob_is_a_miss()
    {
        var cache = new LocalFileCache(_cacheRoot);
        await cache.WriteAsync(
            MirrorCache.MirrorKey,
            Encoding.UTF8.GetBytes("this was never a mirror"),
            CancellationToken.None);
        var mirror = new StateMirror();

        var hit = await new MirrorCache(cache).RestoreAsync(mirror, CancellationToken.None);

        hit.ShouldBeFalse(
            "a store the client may discard at any moment is a store that can come back wrong — an " +
            "eviction mid-write, a format change, a half-flushed file. Reporting a hit over bytes it " +
            "could not read would hand the screen whatever the parse left behind.");
        mirror.StateHash.ShouldBeNull(
            "and nothing may have reached the mirror on the way to deciding that. A restore that " +
            "wrote fields as it parsed and then failed leaves a half-filled screen the server never " +
            "described.");
    }

    /// <summary>
    /// 🔒 The control for the malformed case: an empty store is the ordinary first launch, and it
    /// has to be told apart from a bad read only by both being false, never by either throwing.
    /// </summary>
    [Fact]
    public async Task An_absent_key_restores_nothing_and_is_not_a_fault()
    {
        var mirror = new StateMirror();

        var hit = await new MirrorCache(new LocalFileCache(_cacheRoot))
            .RestoreAsync(mirror, CancellationToken.None);

        hit.ShouldBeFalse(
            "the first launch of every installation reads an empty store. A restore that reported a " +
            "hit here would be reporting one unconditionally, which makes the two cases above " +
            "meaningless.");
    }

    /// <summary>
    /// 🔒 <b>A hit is never authoritative, and that is structural rather than promised.</b>
    /// </summary>
    /// <remarks>
    /// A restore fills the mirror through the same door a server answer uses, so a cached value can
    /// carry nothing a server could not have said — and the first answer overwrites it outright,
    /// because the ladder owes a resync before anything may be believed.
    /// </remarks>
    [Fact]
    public async Task A_restored_mirror_is_overwritten_by_the_first_server_answer()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var stored = new StateMirror();
        stored.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 3));
        await new MirrorCache(cache).PersistAsync(stored, CancellationToken.None);

        var mirror = new StateMirror();
        await new MirrorCache(cache).RestoreAsync(mirror, CancellationToken.None);
        var moved = mirror.Apply(NetWorlds.State(NetWorlds.AnotherHash, sequence: 4));

        moved.ShouldBeTrue(
            "the server said something different from what was cached and the mirror did not move. " +
            "A restore that made the cached value sticky — by short-circuiting on a hash it already " +
            "holds, or by writing fields the apply path does not — turns the cache into a second " +
            "source of truth, which is the one thing it may never be.");
        mirror.StateHash.ShouldBe(
            NetWorlds.AnotherHash,
            "the server's answer wins unconditionally. A cached value that disagrees is stale, not a " +
            "second opinion to be reconciled against.");
    }

    /// <summary>…and an answer that says the same thing announces nothing, cached or not.</summary>
    [Fact]
    public async Task A_server_answer_matching_the_restored_state_reports_no_change()
    {
        var cache = new LocalFileCache(_cacheRoot);
        var stored = new StateMirror();
        stored.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 3));
        await new MirrorCache(cache).PersistAsync(stored, CancellationToken.None);

        var mirror = new StateMirror();
        await new MirrorCache(cache).RestoreAsync(mirror, CancellationToken.None);
        mirror.Apply(NetWorlds.State(NetWorlds.SomeHash, sequence: 4));

        mirror.LastChanged.ShouldBeFalse(
            "a reconnect that finds the server exactly where the cache left it changed nothing a " +
            "player could see, and announcing it would be announcing the network rather than the " +
            "game. It is also the control for the case above: an implementation that reported a " +
            "change on every apply would satisfy that one and mean nothing.");
    }
}
