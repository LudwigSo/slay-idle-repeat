using System.Buffers.Binary;
using System.Text;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Hosting;

/// <summary>The game, played entirely in this process: the composed object behind <see cref="IGameHost"/>.</summary>
/// <remarks>
/// <para>
/// It builds the store, the dispatcher and the two use cases itself — that construction is the
/// composition, and this is the only place those four are named. It names no concrete adapter: which
/// cache, clock and generator are wired is the composition root's decision, not this type's.
/// </para>
/// <para>
/// The host is the server for this process, so issuing a command's ambient values is its job: the
/// instant comes from the clock unchanged, and a per-command seed is drawn — only for the commands
/// that need one — from the id generator, the sanctioned source of fresh entropy here.
/// </para>
/// </remarks>
public sealed class InProcessGameHost : IGameHost
{
    /// <summary>
    /// The key naming which profile this installation plays. Host-owned and inside the cache's key
    /// space, and deliberately not one of the store's own two spellings: it names a row rather than
    /// holding one, so a reader who finds it beside them can tell which of the three is the pointer.
    /// </summary>
    private const string LocalProfileKey = "host.localProfile";

    /// <summary>What a minted profile identity is spelled with, so the id lies inside the cache's key space.</summary>
    private const string PlayerIdPrefix = "PLAYER_";

    private readonly IClockPort _clock;
    private readonly IIdGeneratorPort _ids;
    private readonly ContentSnapshot _content;
    private readonly Entitlements _entitlements;
    private readonly FeatureFlags _flags;

    private readonly ILocalCachePort _cache;
    private readonly WorldSliceStore _store;
    private readonly ApplyCommandUseCase _apply;
    private readonly ReadOwnStateUseCase _read;

    /// <summary>Composes a host over everything it needs, every piece of it stated by the caller.</summary>
    /// <param name="cache">Where the local profile and its runs are stored.</param>
    /// <param name="clock">The instant every command is applied at.</param>
    /// <param name="ids">The source of the profile's identity and of each meta command's seed.</param>
    /// <param name="content">The loaded, validated content set every command reads.</param>
    /// <param name="entitlements">The subscription entitlement, resolved once at boot.</param>
    /// <param name="flags">The kill switches, resolved once at boot.</param>
    /// <param name="sinks">Where an accepted command's events go, in delivery order. May be empty.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// Nothing is defaulted. A composition root that cannot say which entitlement or which flags it is
    /// running under is one that has not decided, and a default here would decide for it silently.
    /// </remarks>
    public InProcessGameHost(
        ILocalCachePort cache,
        IClockPort clock,
        IIdGeneratorPort ids,
        ContentSnapshot content,
        Entitlements entitlements,
        FeatureFlags flags,
        IReadOnlyList<IDomainEventSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(sinks);

        _cache = cache;
        _clock = clock;
        _ids = ids;
        _content = content;
        _entitlements = entitlements;
        _flags = flags;

        _store = new WorldSliceStore(cache);
        _apply = new ApplyCommandUseCase(_store, new DomainEventDispatcher(sinks));
        _read = new ReadOwnStateUseCase(_store);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The profile row is committed before the pointer that names it. A crash between the two leaks
    /// an unreferenced row and the next launch mints a fresh profile; the reverse leaves a pointer to
    /// a row that does not exist, and every later command fails on the load — a bricked install.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The starting row this host built does not rehydrate. A defect here or a content set whose
    /// authored Legend Level range excludes its own minimum, never a caller's doing.
    /// </exception>
    /// <exception cref="MissingContentException">The content set authors no Legend Level range.</exception>
    /// <exception cref="UnauthorisedTunableException">That range holds a deliberate <c>null</c>.</exception>
    /// <exception cref="InvalidTunableException">That range is authorised but unusable.</exception>
    public async Task<PlayerId> OpenProfileAsync(CancellationToken ct)
    {
        var named = await _cache.ReadAsync(LocalProfileKey, ct).ConfigureAwait(false);

        if (named is not null)
        {
            return new PlayerId(Encoding.UTF8.GetString(named));
        }

        var id = new PlayerId(PlayerIdPrefix + _ids.NewGuid().ToString("N"));

        // The name is the identity until something asks the player for one: nothing has, and a name
        // invented here would be a value with no author.
        var starting = Player.CreateStartingNamedAfterItsOwnId(id, _clock.UtcNow, _content);

        if (starting.IsFailure)
        {
            throw new InvalidOperationException(
                "The starting player row this host built does not rehydrate: " + starting.Error +
                " Rehydration is the one validated construction path, so this is either a defect in " +
                "Player.CreateStarting or a content set whose authored Legend Level range does not " +
                "contain its own minimum. It is NOT a state a caller can ask for.");
        }

        await _store.SaveAsync(new WorldSlice(starting.Value, null), ct).ConfigureAwait(false);

        await _cache.WriteAsync(LocalProfileKey, Encoding.UTF8.GetBytes(id.Value), ct)
            .ConfigureAwait(false);

        return id;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No profile row is stored for <paramref name="player"/>, or the stored row does not load.
    /// Neither is a player asking for something they cannot have.
    /// </exception>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player, RunId? run, GameCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new GameContext(
            _clock.UtcNow,
            GameRules.RequiresCommandSeed(command) ? FreshCommandSeed() : null,
            _content,
            _entitlements,
            _flags);

        return _apply.ExecuteAsync(new ApplyCommandRequest(player, run, command), context, ct);
    }

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct) =>
        _read.ReadAsync(new ReadOwnStateRequest(player, run), ct);

    /// <summary>One meta command's seed, folded from the one sanctioned source of fresh entropy here.</summary>
    /// <remarks>
    /// Both halves of the guid are folded in rather than the low eight bytes taken, because a
    /// generator is only required to make the whole identifier unique — a fake that varies its
    /// trailing bytes and a real one that varies its leading bytes are both conforming, and reading
    /// half of it would silently draw the same seed forever under one of them.
    /// </remarks>
    private ulong FreshCommandSeed()
    {
        Span<byte> bytes = stackalloc byte[16];

        // Checked rather than discarded. A write that did not happen leaves the buffer as the stack
        // left it, and the fold below would then draw the same seed for every command — the exact
        // failure the fold itself exists to rule out, and the one shape of it nothing would report.
        if (!_ids.NewGuid().TryWriteBytes(bytes))
        {
            throw new InvalidOperationException(
                "A guid did not fit sixteen bytes, so this command's seed would be folded from a " +
                "buffer nothing wrote.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(bytes) ^
               BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
    }
}
