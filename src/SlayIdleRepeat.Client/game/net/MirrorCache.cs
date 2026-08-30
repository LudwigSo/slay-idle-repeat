using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contracts;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Stores and restores <see cref="StateMirror"/> through the byte cache, gzipped at the boundary.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The serializer lives entirely ABOVE the port, which stays byte-oriented: a generic read would
/// put serialization inside the port and leave each implementation free to store something else.
/// </para>
/// <para>
/// 🔒 A restore fills the mirror only by handing it an answer shaped exactly like a server's, so a
/// hit can carry nothing a server could not have said — and it is overwritten by the first real
/// answer, because a reconnect owes a resync before anything may be believed.
/// </para>
/// <para>
/// 🔴 <b>What is stored is a run-state answer, so a mirror holding no run is not stored at all.</b>
/// The one construction path into the mirror is a whole run-state read, and that shape requires a
/// run — so a player between runs gets no instant cold start. Writing some other shape would be
/// inventing a second door into the mirror, which is the thing that would make the cache
/// authoritative.
/// </para>
/// </remarks>
public sealed class MirrorCache
{
    /// <summary>The one key the mirror lives under, inside the port's closed ordinal key space.</summary>
    public const string MirrorKey = "mirror.serverState";

    /// <summary>
    /// What this class writes, so a blob from an older shape is a miss rather than a bad parse.
    /// </summary>
    /// <remarks>
    /// One stable key with the version INSIDE the payload, rather than a versioned key: a versioned
    /// key leaves the previous format's file behind on a store the client is never told to sweep.
    /// </remarks>
    private const int FormatVersion = 1;

    private const string FormatVersionMember = "formatVersion";
    private const string StateMember = "state";

    /// <summary>A read carries everything the mirror holds, so it asks from the beginning.</summary>
    private const long WholeState = 0;

    private readonly ILocalCachePort _cache;

    /// <summary>Wires the round-trip over the byte cache it stores through.</summary>
    /// <param name="cache">The on-device byte cache.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    public MirrorCache(ILocalCachePort cache)
    {
        ArgumentNullException.ThrowIfNull(cache);

        _cache = cache;
    }

    /// <summary>Fills the mirror from the last thing the server said, if anything is stored.</summary>
    /// <param name="mirror">The mirror to fill.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True when a stored answer was applied.</returns>
    /// <remarks>A malformed or unknown-version blob is a MISS, never a fault.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    public async Task<bool> RestoreAsync(StateMirror mirror, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mirror);

        if (await _cache.ReadAsync(MirrorKey, ct).ConfigureAwait(false) is not { } stored)
        {
            return false;
        }

        if (Read(stored) is not { } state)
        {
            return false;
        }

        // The same door a server answer comes through, and the only one: nothing here writes a
        // field the wire could not have carried.
        mirror.Apply(state);

        return true;
    }

    /// <summary>Stores what the mirror currently holds.</summary>
    /// <param name="mirror">The mirror to read.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    public async Task PersistAsync(StateMirror mirror, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mirror);

        if (mirror.Profile is not { } profile || mirror.Run is not { } run ||
            mirror.StateHash is not { } stateHash)
        {
            return;
        }

        var answer = WireJson.Render(
            new RunStateResponse(
                WireProtocol.PROTOCOL_VERSION,
                run.Id,
                mirror.Sequence,
                WholeState,
                profile,
                run,
                stateHash,
                MissedOutcomes: [],
                ResyncFull: null));

        await _cache.WriteAsync(MirrorKey, Compress(Envelope(answer)), ct).ConfigureAwait(false);
    }

    /// <summary>Wraps a rendered answer in the envelope that carries the format version.</summary>
    /// <remarks>
    /// Spliced rather than re-serialised: the answer is already this dialect's own JSON, and
    /// encoding it a second time would let the two renderings disagree over the same state.
    /// </remarks>
    private static string Envelope(string answer) =>
        $"{{\"{FormatVersionMember}\":{FormatVersion},\"{StateMember}\":{answer}}}";

    /// <summary>The stored answer, or null when the blob is not one this build can read.</summary>
    private static WireRunState? Read(byte[] stored)
    {
        try
        {
            using var document = JsonDocument.Parse(Decompress(stored));

            if (!document.RootElement.TryGetProperty(FormatVersionMember, out var version) ||
                !version.TryGetInt32(out var written) || written != FormatVersion ||
                !document.RootElement.TryGetProperty(StateMember, out var state))
            {
                return null;
            }

            return WireJson.ParseRunState(state.GetRawText());
        }
        catch (Exception fault) when (
            fault is InvalidDataException or JsonException or WireParseException)
        {
            // Exactly the case the port licenses: a store a caller may discard at any moment can
            // come back half-written, evicted mid-flush or written by a shape this build retired.
            return null;
        }
    }

    private static byte[] Compress(string payload)
    {
        using var packed = new MemoryStream();

        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(payload));
        }

        return packed.ToArray();
    }

    private static string Decompress(byte[] stored)
    {
        using var packed = new MemoryStream(stored);
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
