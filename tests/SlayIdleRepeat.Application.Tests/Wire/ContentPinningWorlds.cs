using System.Globalization;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Tests.Content;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>An <see cref="IContentPinStore"/> that counts what was asked of it and of whom.</summary>
/// <remarks>
/// A decorator over the shipped fake rather than a second implementation, so a case reading a pin
/// back reads what the real fake stored, and the counters stay the only thing this type adds.
/// </remarks>
internal sealed class RecordingContentPinStore(IContentPinStore inner) : IContentPinStore
{
    /// <summary>How many times a run pin has been asked for.</summary>
    internal int RunPinReads { get; private set; }

    /// <summary>How many times a session pin has been asked for.</summary>
    internal int SessionPinReads { get; private set; }

    /// <summary>Every run pin written, in order.</summary>
    internal List<(RunId Run, ContentVersion Version)> RunPinWrites { get; } = [];

    /// <summary>Every session pin written, in order.</summary>
    internal List<(PlayerId Player, ContentVersion Version)> SessionPinWrites { get; } = [];

    /// <inheritdoc/>
    public Task<ContentVersion?> ReadRunPinAsync(RunId run, CancellationToken ct)
    {
        RunPinReads++;

        return inner.ReadRunPinAsync(run, ct);
    }

    /// <inheritdoc/>
    public Task WriteRunPinAsync(RunId run, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        RunPinWrites.Add((run, version));

        return inner.WriteRunPinAsync(run, version, atUtc, ct);
    }

    /// <inheritdoc/>
    public Task<ContentVersion?> ReadSessionPinAsync(PlayerId player, CancellationToken ct)
    {
        SessionPinReads++;

        return inner.ReadSessionPinAsync(player, ct);
    }

    /// <inheritdoc/>
    public Task WriteSessionPinAsync(
        PlayerId player, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        SessionPinWrites.Add((player, version));

        return inner.WriteSessionPinAsync(player, version, atUtc, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RetainedVersion>> ListRetainedAsync(CancellationToken ct) =>
        inner.ListRetainedAsync(ct);
}

/// <summary>
/// The pinning half of a gateway world: the store, the snapshots a pin can still resolve to, the
/// warn sink an unresolvable pin has to speak through, and a count of how often the resolver ran.
/// </summary>
internal sealed class PinnedContent
{
    private readonly Dictionary<string, ContentSnapshot> _resolvable = new(StringComparer.Ordinal);

    private PinnedContent(ContentSnapshot current, IEnumerable<ContentSnapshot> alsoResolvable)
    {
        Current = current;
        Store = new RecordingContentPinStore(new InMemoryContentPinStore());

        foreach (var snapshot in alsoResolvable)
        {
            _resolvable[snapshot.Version.Value] = snapshot;
        }

        Pinning = new ContentPinning(Store, current, Resolve, Warnings.Add);
    }

    /// <summary>The snapshot the server is serving now.</summary>
    internal ContentSnapshot Current { get; }

    /// <summary>The counting store the gateway writes its pins through.</summary>
    internal RecordingContentPinStore Store { get; }

    /// <summary>The composed seam the gateway is handed.</summary>
    internal ContentPinning Pinning { get; }

    /// <summary>Every line the fallback spoke through the warn sink.</summary>
    internal List<string> Warnings { get; } = [];

    /// <summary>How many times a pinned version was handed to the resolver.</summary>
    internal int ResolverCalls { get; private set; }

    /// <summary>Pins over the shipped content set, with nothing older still retained.</summary>
    internal static PinnedContent OverTheShippedSnapshot() => new(Worlds.Content, []);

    /// <summary>Pins over the shipped content set, with <paramref name="older"/> still resolvable.</summary>
    internal static PinnedContent AlsoServing(ContentSnapshot older) => new(Worlds.Content, [older]);

    private ContentSnapshot? Resolve(ContentVersion version)
    {
        ResolverCalls++;

        if (version.Equals(Current.Version))
        {
            return Current;
        }

        return _resolvable.TryGetValue(version.Value, out var snapshot) ? snapshot : null;
    }
}

/// <summary>The shipped content set with one balance number moved, so two snapshots differ in play.</summary>
/// <remarks>
/// <c>skipGoldReward</c> is the whole outcome of <c>SKIP_DRAFT</c>: a run judged against one
/// snapshot pays 60 Gold and against the other 90, which is a behavioural difference rather than an
/// object-identity one. Loaded through the real loader, so the retuned set carries its own genuine
/// version stamp instead of a hand-written one.
/// </remarks>
internal static class RetunedContent
{
    /// <summary>What a draft skip pays under the shipped content set.</summary>
    internal const long ShippedSkipReward = 60L;

    /// <summary>What a draft skip pays under <see cref="Snapshot"/>.</summary>
    internal const long RetunedSkipReward = 90L;

    private static readonly Lazy<ContentSnapshot> LazySnapshot = new(() =>
        ContentLoader
            .Load(RepoData.SourceWithEdit(
                "tuning/currencies.json",
                "\"skipGoldReward\": " + ShippedSkipReward.ToString(CultureInfo.InvariantCulture),
                "\"skipGoldReward\": " + RetunedSkipReward.ToString(CultureInfo.InvariantCulture)))
            .Require());

    /// <summary>The retuned snapshot, stamped with its own version.</summary>
    internal static ContentSnapshot Snapshot => LazySnapshot.Value;
}

/// <summary>Envelope bodies whose subject is the content hash the client claims to hold.</summary>
internal static class SessionEnvelopes
{
    /// <summary>A <c>BEGIN_SESSION</c> body claiming <paramref name="contentHash"/>.</summary>
    internal static string BeginSession(long sequence, string commandId, string contentHash) =>
        Envelopes.Body(
            "BEGIN_SESSION",
            sequence,
            commandId,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"clientVersion\": \"1.0\", \"contentHash\": \"{contentHash}\"}}"));
}
