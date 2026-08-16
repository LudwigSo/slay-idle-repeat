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

    /// <summary>How many bytes the atomicity case writes — enough that a torn write is a short file.</summary>
    protected const int LongValueLength = 64 * 1024;

    /// <summary>
    /// Names Win32 reserves for devices. Every one of them is inside this port's key space, which
    /// says nothing about them at all.
    /// </summary>
    private static readonly string[] ReservedDeviceNames = ["nul", "con", "prn", "aux", "com1"];

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

    /// <summary>The message the null-key case uses on all three methods.</summary>
    private const string NullIsNotMerelyMalformed =
        "a null key is a MISSING argument, not a malformed one, and this port answers the two with "
        + "different exceptions. ArgumentException here is not a smaller failure — it is the answer "
        + "that lets one implementation throw ArgumentNullException and the other throw the base "
        + "type while every case in this suite stays green.";

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
    /// <para>
    /// 🔒 <c>null</c> is deliberately <em>not</em> a row here. It has a case of its own —
    /// <see cref="A_null_key_is_a_null_argument_fault_from_every_method"/> — which demands the
    /// derived <see cref="ArgumentNullException"/>. A row here would restate the same input at the
    /// weaker base type, and because the derived type satisfies the base one the theory would pass
    /// against either answer while the dedicated case demanded one of them. Two cases over one input
    /// with different strictness is a contradiction a reader has to resolve; the strict one wins.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("../x")]
    [InlineData("a b")]
    [InlineData("a:b")]
    public async Task A_key_outside_the_closed_key_space_is_an_argument_fault(string key)
    {
        var cache = Create();

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.ReadAsync(key, CancellationToken.None));

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.WriteAsync(key, new byte[] { 1 }, CancellationToken.None));

        await Should.ThrowAsync<ArgumentException>(
            async () => await cache.DeleteAsync(key, CancellationToken.None));
    }

    /// <summary>
    /// 🔒 A <see langword="null"/> key is an <see cref="ArgumentNullException"/> — the identity of
    /// the fault, not merely its base type — from all three methods.
    /// </summary>
    /// <remarks>
    /// A missing key and a malformed one are different mistakes and get different exceptions, which
    /// is the convention every <c>ArgumentNullException.ThrowIfNull</c> in this repository follows.
    /// <para>
    /// 🔒 This is stated as its own case because the theory above cannot state it. Its assertion is
    /// <see cref="ArgumentException"/>, <see cref="ArgumentNullException"/> derives from it, and a
    /// <c>null</c> row therefore passes whichever of the two an implementation throws — so the two
    /// implementations were free to disagree underneath a green case, and did. Asserting the derived
    /// type here is the only way the suite can see which one was thrown.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_null_key_is_a_null_argument_fault_from_every_method()
    {
        var cache = Create();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await cache.ReadAsync(null!, CancellationToken.None),
            NullIsNotMerelyMalformed);

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await cache.WriteAsync(null!, new byte[] { 1 }, CancellationToken.None),
            NullIsNotMerelyMalformed);

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await cache.DeleteAsync(null!, CancellationToken.None),
            NullIsNotMerelyMalformed);
    }

    /// <summary>
    /// 🔒 Two keys differing only in case are two entries — the key space is compared
    /// <em>ordinally</em> (<c>23</c> §5 A2), by the port and not by whatever is underneath it.
    /// </summary>
    /// <remarks>
    /// Every round-trip case above uses one key at a time and none of them can see this. A
    /// file-backed implementation that takes the key as a file name hands the comparison to the
    /// host: Windows and the default macOS filesystem ignore case and merge these two into one
    /// entry, a Linux build keeps them apart, and the same source then answers differently per
    /// developer machine — on the one port whose whole purpose is that its two implementations
    /// cannot answer differently at all.
    /// </remarks>
    [Fact]
    public async Task Two_keys_differing_only_in_case_are_two_entries()
    {
        var cache = Create();

        await cache.WriteAsync("Profile", new byte[] { 0xA1, 0xA2 }, CancellationToken.None);
        await cache.WriteAsync("profile", new byte[] { 0xB1, 0xB2 }, CancellationToken.None);

        (await cache.ReadAsync("Profile", CancellationToken.None)).ShouldBe(
            new byte[] { 0xA1, 0xA2 },
            "'Profile' and 'profile' are two keys in an ordinal key space, so the write to the "
            + "lower-case one must not have landed on this entry. An implementation that lets a "
            + "case-insensitive filesystem compare the names merges them, and does so on some hosts "
            + "and not others.");

        (await cache.ReadAsync("profile", CancellationToken.None)).ShouldBe(
            new byte[] { 0xB1, 0xB2 },
            "the same merge seen from the other side: the lower-case key must hold its own value.");
    }

    /// <summary>
    /// 🔒 A key and the same key with a trailing dot are two entries. Both are inside the key space
    /// and nothing about them is special.
    /// </summary>
    /// <remarks>
    /// The same family as the case above, and the reason it is stated separately is that it survives
    /// a fix aimed only at case: Win32 strips a trailing dot from a file name, so a key taken
    /// verbatim makes <c>a.</c> and <c>a</c> one entry on Windows however the case is compared. The
    /// port's key space admits <c>.</c> anywhere, including last, and says nothing that would let a
    /// caller know to avoid it.
    /// </remarks>
    [Fact]
    public async Task A_key_and_the_same_key_with_a_trailing_dot_are_two_entries()
    {
        var cache = Create();

        await cache.WriteAsync("a", new byte[] { 0x11 }, CancellationToken.None);
        await cache.WriteAsync("a.", new byte[] { 0x22 }, CancellationToken.None);

        (await cache.ReadAsync("a", CancellationToken.None)).ShouldBe(
            new byte[] { 0x11 },
            "'a' and 'a.' are two keys, and the write to the dotted one must not have landed here. "
            + "Win32 strips a trailing dot from a file name, so an implementation that names the "
            + "file after the key merges the pair silently and only on Windows.");

        (await cache.ReadAsync("a.", CancellationToken.None)).ShouldBe(
            new byte[] { 0x22 },
            "the dotted key must hold its own value rather than the other one's.");
    }

    /// <summary>
    /// 🔒 A Windows reserved device name is an ordinary key: <c>nul</c>, <c>con</c> and their
    /// siblings round-trip like any other.
    /// </summary>
    /// <remarks>
    /// This is the one that loses data without an error anywhere. Every name below is inside the
    /// port's key space, so a caller is entitled to use one; an implementation that names a file
    /// after the key opens a device instead of a file on Windows, where a write to <c>nul</c> is
    /// discarded outright and reads back empty. Nothing throws, nothing logs, and the read is a HIT
    /// carrying bytes nobody stored.
    /// <para>
    /// Collected rather than asserted one at a time so a failure names every device that misbehaved
    /// — the interesting distinction is "all of them" versus "just <c>nul</c>", and an assertion
    /// that stopped at the first cannot draw it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_windows_reserved_device_name_is_an_ordinary_key()
    {
        var cache = Create();
        var offenders = new List<string>();

        for (var index = 0; index < ReservedDeviceNames.Length; index++)
        {
            var name = ReservedDeviceNames[index];
            var value = new byte[] { 0xC0, (byte)index, 0xDE };

            try
            {
                await cache.WriteAsync(name, value, CancellationToken.None);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                offenders.Add($"'{name}' could not be written at all: {failure.GetType().Name}");
                continue;
            }

            var read = await cache.ReadAsync(name, CancellationToken.None);

            if (read is null)
            {
                offenders.Add($"'{name}' read back as a MISS immediately after it was written");
            }
            else if (!read.SequenceEqual(value))
            {
                offenders.Add(
                    $"'{name}' read back {read.Length} byte(s) instead of the {value.Length} written");
            }
        }

        offenders.ShouldBeEmpty(
            "every one of these is a legal key under 23 §5 A2 and this port promises them nothing "
            + "special. On Windows they name devices, so an implementation that takes the key as a "
            + $"file name silently discards the write and reads back empty: {string.Join("; ", offenders)}");
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

    /// <summary>
    /// 🔒 A write that is cancelled leaves the <em>previous</em> value readable and whole — never a
    /// truncated one, and never an empty entry.
    /// </summary>
    /// <remarks>
    /// The case above says the cancelled write throws; this one says what the store looks like
    /// afterwards, which is the half a caller actually depends on and the half nothing asserted. The
    /// failure it forbids is the expensive one: an implementation that opens the destination before
    /// it observes the token truncates the entry and then throws, so the caller sees the documented
    /// <see cref="OperationCanceledException"/>, believes nothing happened, and the next read is a
    /// <em>hit</em> carrying a prefix of bytes nobody ever stored. A short value is indistinguishable
    /// from a short value stored on purpose, so no later reader can recover.
    /// <para>
    /// The value is long on purpose: a torn write of a handful of bytes may land whole by accident,
    /// and this case must be about the store's state rather than about the size of one buffer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_cancelled_write_leaves_the_previous_value_whole()
    {
        var cache = Create();
        var original = Pattern(seed: 0x11);
        var replacement = Pattern(seed: 0x77);
        await cache.WriteAsync(Key, original, CancellationToken.None);

        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await cache.WriteAsync(Key, replacement, source.Token));

        var read = await cache.ReadAsync(Key, CancellationToken.None);

        read.ShouldNotBeNull(
            "a cancelled write removed the entry entirely. The value that was there before the "
            + "attempt is what a caller is entitled to read back.");

        read.Length.ShouldBe(
            original.Length,
            $"the entry is now {read.Length} bytes where {original.Length} were stored, so the "
            + "cancelled write truncated it. A short entry reads back as a hit and no later reader "
            + "can tell it from a short value stored on purpose.");

        read.SequenceEqual(original).ShouldBeTrue(
            "the entry is the right length but not the right bytes, so the cancelled write partly "
            + "landed. A write lands whole or not at all.");
    }

    /// <summary>A long, position-dependent value: any truncation or substitution changes it.</summary>
    /// <param name="seed">Distinguishes one pattern from another.</param>
    /// <returns><see cref="LongValueLength"/> bytes.</returns>
    private static byte[] Pattern(byte seed)
    {
        var bytes = new byte[LongValueLength];

        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(seed + index);
        }

        return bytes;
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
