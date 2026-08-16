using System.Collections.Concurrent;
using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="IRewardedAdPort"/>: a placement becomes ready when it is
/// preloaded, and a scenario decides how the show ends.
/// </summary>
/// <remarks>
/// <para>
/// A preloaded placement completes and carries a verification token; an unprepared one answers
/// <see cref="AdResultKind.NoFill"/>. <see cref="ScriptOutcome"/> replaces the first half so a
/// caller's dismissal and error branches can be exercised, and <see cref="DeclareNoFill"/> keeps a
/// placement from ever loading so the second half can be reached on purpose.
/// </para>
/// <para>
/// 🔒 <b>No knob here can break the port's readiness promise.</b> The two scripting methods are
/// deliberately separate: a scripted <see cref="AdResultKind.NoFill"/> would let a placement report
/// ready and then answer that no ad was available, which is the port telling its caller two
/// different things about one moment. <see cref="ScriptOutcome"/> therefore refuses that kind and
/// points at <see cref="DeclareNoFill"/>, which produces the same outcome the honest way — by
/// keeping the placement unready.
/// </para>
/// <para>
/// The stores are concurrent because the boot path warms every placement it knows about, and
/// fanning that out across tasks is the obvious way to write it. The real adapters take it without
/// noticing; a plain <c>HashSet</c> here would corrupt, and the failure would belong to whichever
/// implementation the scenario happened to be wired with rather than to the code under test.
/// </para>
/// </remarks>
public sealed class InMemoryRewardedAd : IRewardedAdPort
{
    private readonly ConcurrentDictionary<string, bool> _loaded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _neverFills = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AdResultKind> _scripted = new(StringComparer.Ordinal);
    private long _shown;

    /// <summary>Decides how the next show of a loaded <paramref name="adPlacementId"/> ends.</summary>
    /// <param name="adPlacementId">The placement to script.</param>
    /// <param name="kind">How its show ends. Never <see cref="AdResultKind.NoFill"/>.</param>
    /// <returns>This fake, so several placements chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="adPlacementId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is undeclared, or is <see cref="AdResultKind.NoFill"/> — use
    /// <see cref="DeclareNoFill"/>.
    /// </exception>
    public InMemoryRewardedAd ScriptOutcome(string adPlacementId, AdResultKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "an outcome kind outside the declared members falls through every switch that reads " +
                "it, so no implementation may produce one — this fake least of all.");
        }

        if (kind == AdResultKind.NoFill)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                $"'{adPlacementId}' would then report ready and answer NoFill, which is the port " +
                "saying an ad was available and that none was. Call DeclareNoFill instead: it " +
                "produces the same outcome by leaving the placement unready, which is what a real " +
                "adapter with no inventory does.");
        }

        _scripted[adPlacementId] = kind;
        return this;
    }

    /// <summary>Declares that <paramref name="adPlacementId"/> never has an ad to show.</summary>
    /// <param name="adPlacementId">The placement with no inventory.</param>
    /// <returns>This fake, so several placements chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="adPlacementId"/> is blank.</exception>
    public InMemoryRewardedAd DeclareNoFill(string adPlacementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        _neverFills[adPlacementId] = true;
        _loaded.TryRemove(adPlacementId, out _);
        return this;
    }

    /// <inheritdoc/>
    public bool IsReady(string adPlacementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        return _loaded.ContainsKey(adPlacementId);
    }

    /// <inheritdoc/>
    public Task<AdOutcome> ShowAsync(string adPlacementId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);
        ct.ThrowIfCancellationRequested();

        if (!_loaded.ContainsKey(adPlacementId))
        {
            return Task.FromResult(new AdOutcome(AdResultKind.NoFill, VerificationToken: null));
        }

        var kind = _scripted.TryGetValue(adPlacementId, out var scripted)
            ? scripted
            : AdResultKind.Completed;

        return Task.FromResult(kind == AdResultKind.Completed
            ? new AdOutcome(kind, TokenFor(adPlacementId))
            : new AdOutcome(kind, VerificationToken: null));
    }

    /// <inheritdoc/>
    public Task PreloadAsync(string adPlacementId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        if (!_neverFills.ContainsKey(adPlacementId))
        {
            _loaded[adPlacementId] = true;
        }

        return Task.CompletedTask;
    }

    private string TokenFor(string adPlacementId) =>
        $"verification-{adPlacementId}-{Interlocked.Increment(ref _shown).ToString(CultureInfo.InvariantCulture)}";
}
