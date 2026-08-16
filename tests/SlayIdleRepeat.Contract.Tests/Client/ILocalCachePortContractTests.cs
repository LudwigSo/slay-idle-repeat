using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// States what <see cref="ILocalCachePort"/> <em>means</em> (<c>23</c> §4.1, §5 A8) — written once
/// against the interface so the in-memory fake and the file-backed adapter cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// <c>14</c> §7.2 makes this store the client's read-only local cache and never a second source of
/// truth. That claim is not mechanically checkable here — no assertion can see what a <em>caller</em>
/// believes about a hit — so it lives in the port's own remarks. What is checkable is every
/// distinction a caller has to be able to rely on when it does read one, and those are the cases
/// below.
/// </para>
/// <para>
/// 🔒 The two that matter most are <see cref="A_cached_empty_value_is_not_a_miss"/> and
/// <see cref="A_value_outlives_the_port_instance_that_wrote_it"/>. The first is the distinction a
/// nullable-returning cache exists to make; the second is the difference between a cache and a
/// dictionary that happens to satisfy the same interface.
/// </para>
/// </remarks>
[ContractSuiteFor(typeof(ILocalCachePort))]
public abstract class ILocalCachePortContractTests : IDisposable
{
    private bool _disposed;

    /// <summary>A key inside the closed key space, used by the cases that are not about keys.</summary>
    protected const string Key = "profile.v1_snapshot-3";

    /// <summary>A second key inside the closed key space.</summary>
    protected const string OtherKey = "content.revision";

    /// <summary>A cache of the implementation under test, over a backing store of its own.</summary>
    protected abstract ILocalCachePort Create();

    /// <summary>
    /// A fresh port over the same backing store as <paramref name="cache"/> — what the next launch
    /// of the app gets.
    /// </summary>
    /// <remarks>
    /// 🔒 Must return a <em>different</em> instance. Returning <paramref name="cache"/> itself makes
    /// both durability cases below read the store through the port that wrote it, which is the one
    /// arrangement they exist to rule out; the two cases assert that directly rather than trusting
    /// the fixture.
    /// </remarks>
    protected abstract ILocalCachePort Reopen(ILocalCachePort cache);

    /// <summary>The message both durability cases use when a fixture's <see cref="Reopen"/> is an identity.</summary>
    private const string ReopenMustBeANewInstance =
        "Reopen returned the same port instance. Both durability cases then read through the very "
        + "object that did the writing, and an implementation that keeps everything in a field of "
        + "that object passes them — which is exactly the implementation they exist to fail.";

    /// <summary>Releases whatever the fixture allocated.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            Dispose(disposing: true);
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------- miss versus empty value

    /// <summary>Nothing stored under a key reads back as <see langword="null"/>.</summary>
    [Fact]
    public async Task A_key_that_was_never_written_reads_back_as_null()
    {
        var cache = Create();

        (await cache.ReadAsync(Key, CancellationToken.None)).ShouldBeNull(
            "nothing has ever been written under this key, and null is how this port says so. The "
            + "case below writes zero bytes to the same key and requires a non-null answer; between "
            + "them they pin the distinction the nullable return exists for.");
    }

    /// <summary>
    /// 🔒 A value written as zero bytes reads back as an <em>empty array</em>, not as
    /// <see langword="null"/>. The distinction is load-bearing (<c>14</c> §7.2).
    /// </summary>
    /// <remarks>
    /// Collapse the two and "we have never fetched this" becomes indistinguishable from "the server
    /// told us this is empty". The client then either re-fetches an empty answer on every launch or
    /// caches an absence it never observed — and both implementations would have to make the same
    /// wrong choice for anything to notice.
    /// </remarks>
    [Fact]
    public async Task A_cached_empty_value_is_not_a_miss()
    {
        var cache = Create();

        await cache.WriteAsync(Key, ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        var read = await cache.ReadAsync(Key, CancellationToken.None);

        read.ShouldNotBeNull(
            "an empty value was written, so this is a HIT. Returning null here makes 'cached empty' "
            + "and 'never fetched' the same answer, which is the one distinction this port's "
            + "nullable return exists to make.");
        read.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------- round-trip

    /// <summary>What was written is what comes back, byte for byte.</summary>
    [Fact]
    public async Task A_written_value_reads_back_byte_for_byte()
    {
        var cache = Create();
        var value = new byte[] { 0x00, 0x01, 0xFF, 0x7F, 0x80, 0x00 };

        await cache.WriteAsync(Key, value, CancellationToken.None);

        (await cache.ReadAsync(Key, CancellationToken.None)).ShouldBe(value);
    }

    /// <summary>
    /// A read hands back a <em>copy</em>: mutating it does not change what is cached.
    /// </summary>
    /// <remarks>
    /// An in-memory implementation that returns its own buffer passes every other case here while
    /// letting any caller silently corrupt the store for every later reader. The file-backed
    /// adapter cannot have the defect at all, which is exactly why it has to be stated once, over
    /// the interface, rather than left to whoever writes the fake.
    /// <para>
    /// 🔒 The expected bytes are a <em>separate array</em> from the one that was written, and that
    /// is the whole construction. Compare the second read against the written array and an
    /// implementation that aliases the caller's buffer on the way in AND hands the same buffer back
    /// out passes: the mutation below changes the written array too, so both sides of the comparison
    /// move together and the worst implementation available looks correct.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_read_returns_a_copy_rather_than_the_stored_buffer()
    {
        var cache = Create();
        var expected = new byte[] { 1, 2, 3, 4 };
        await cache.WriteAsync(Key, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);

        var first = await cache.ReadAsync(Key, CancellationToken.None);
        first.ShouldNotBeNull();
        first[0] = 0xEE;

        (await cache.ReadAsync(Key, CancellationToken.None)).ShouldBe(
            expected,
            "mutating the array a read handed back changed what is cached.");
    }

    /// <summary>Writing twice to one key leaves the second value; there is no append and no conflict.</summary>
    [Fact]
    public async Task A_second_write_to_the_same_key_wins()
    {
        var cache = Create();
        await cache.WriteAsync(Key, new byte[] { 1, 1, 1 }, CancellationToken.None);

        await cache.WriteAsync(Key, new byte[] { 9 }, CancellationToken.None);

        (await cache.ReadAsync(Key, CancellationToken.None)).ShouldBe(new byte[] { 9 });
    }

    // ------------------------------------------------------------------------------------ delete

    /// <summary>After a delete, the key is a miss again.</summary>
    [Fact]
    public async Task Deleting_a_key_makes_it_a_miss_again()
    {
        var cache = Create();
        await cache.WriteAsync(Key, new byte[] { 7 }, CancellationToken.None);

        await cache.DeleteAsync(Key, CancellationToken.None);

        (await cache.ReadAsync(Key, CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>Deleting a key that is not there is a no-op, not a fault.</summary>
    /// <remarks>
    /// A caller clearing a cache would otherwise have to establish what is in it first, which is a
    /// read it does not need and a race it cannot win.
    /// </remarks>
    [Fact]
    public async Task Deleting_an_absent_key_is_a_no_op()
    {
        var cache = Create();
        await cache.WriteAsync(OtherKey, new byte[] { 5 }, CancellationToken.None);

        await cache.DeleteAsync(Key, CancellationToken.None);

        (await cache.ReadAsync(OtherKey, CancellationToken.None)).ShouldBe(new byte[] { 5 });
    }

    // --------------------------------------------------------------------------------- key space

    /// <summary>
    /// 🔒 The key space is closed and ordinal — non-empty, and only <c>A-Z a-z 0-9 . _ -</c>
    /// (<c>23</c> §5 A2). Everything else is an argument fault from all three methods.
    /// </summary>
    /// <remarks>
    /// Both path separators and <c>..</c> are in the theory because a file-backed implementation is
    /// one of the two this suite runs against: a key that can name a directory can name one outside
    /// the store. Closing the key space at the port means no implementation has to write its own
    /// sanitiser, and no two of them can disagree about what a key is.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../x")]
    [InlineData("a b")]
    [InlineData("a:b")]
    public async Task A_key_outside_the_closed_key_space_is_an_argument_fault(string? key)
    {
        var cache = Create();

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.ReadAsync(key!, CancellationToken.None));

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.WriteAsync(key!, new byte[] { 1 }, CancellationToken.None));

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.DeleteAsync(key!, CancellationToken.None));
    }

    // ------------------------------------------------------------------------------ cancellation

    /// <summary>An already-cancelled token is observed by every method (<c>23</c> §5 A3).</summary>
    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_every_method()
    {
        var cache = Create();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await cache.ReadAsync(Key, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await cache.WriteAsync(Key, new byte[] { 1 }, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await cache.DeleteAsync(Key, source.Token));
    }

    // -------------------------------------------------------------------------------- durability

    /// <summary>
    /// 🔒 A value outlives the port instance that wrote it: a fresh port over the same backing
    /// store reads it back (<c>14</c> §7.2).
    /// </summary>
    /// <remarks>
    /// This is the difference between a cache and a dictionary that happens to satisfy the same
    /// interface. Every other case in this suite is satisfied by an implementation that forgets
    /// everything the moment the app closes — which is precisely the implementation that makes the
    /// next cold start re-download a profile the device already had.
    /// </remarks>
    [Fact]
    public async Task A_value_outlives_the_port_instance_that_wrote_it()
    {
        var cache = Create();
        var value = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await cache.WriteAsync(Key, value, CancellationToken.None);

        var reopened = Reopen(cache);
        reopened.ShouldNotBeSameAs(cache, ReopenMustBeANewInstance);

        (await reopened.ReadAsync(Key, CancellationToken.None)).ShouldBe(
            value,
            "a port reopened over the same backing store did not see a value written through the "
            + "previous one.");
    }

    /// <summary>A delete outlives the port instance too — a reopened port does not resurrect it.</summary>
    [Fact]
    public async Task A_delete_outlives_the_port_instance_that_made_it()
    {
        var cache = Create();
        await cache.WriteAsync(Key, new byte[] { 1 }, CancellationToken.None);
        await cache.DeleteAsync(Key, CancellationToken.None);

        var reopened = Reopen(cache);
        reopened.ShouldNotBeSameAs(cache, ReopenMustBeANewInstance);

        (await reopened.ReadAsync(Key, CancellationToken.None)).ShouldBeNull(
            "the durability case above is satisfied by an implementation that persists writes and "
            + "keeps deletes in memory, which resurrects a key on the next launch.");
    }
}
