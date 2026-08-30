using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Hero;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The scripted fake for <see cref="IGameHost"/>: one profile, no run, and an answer a scenario
/// chooses for the next command.
/// </summary>
/// <remarks>
/// <para>
/// It holds one real starting player built through <c>Player.CreateStarting</c> rather than a
/// hand-assembled shape, because the port hands out the aggregates themselves — a made-up state
/// here would let a caller be written against a slice the domain cannot produce.
/// </para>
/// <para>
/// 🔒 <b>None of the port's clauses can be switched off.</b> The profile is minted once and the id
/// never moves; an id this host did not mint is <see cref="OwnStateLookup.NoSuchPlayer"/>; a run it
/// is not in is <see cref="OwnStateLookup.NoSuchRun"/>; a refusal is a returned outcome and never
/// an exception. Those are properties of every implementation, not defaults a fake may relax — same
/// construction, and the same reason, as <see cref="InMemoryPlatformInfo"/> refusing a blank device
/// model.
/// </para>
/// <para>
/// ⚠️ <b>What it deliberately is not.</b> It runs no rules: an accepted command changes nothing and
/// emits no events. A scenario about what a command <em>does</em> belongs against the real
/// in-process host, which is the composition that actually calls the domain.
/// </para>
/// </remarks>
public sealed class InMemoryGameHost : IGameHost
{
    /// <summary>The profile a freshly constructed host mints on first use.</summary>
    public const string StartPlayerId = "PLAYER_inmemoryhost";

    /// <summary>The instant the starting profile is stamped with — 2026-08-12 05:00 UTC, a Wednesday.</summary>
    public static readonly DateTimeOffset StartInstant = new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero);

    private readonly WorldSlice _state;

    private bool _opened;
    private RejectionReason? _refusal;

    /// <summary>Builds a host whose one profile is a real starting player over <paramref name="content"/>.</summary>
    /// <param name="content">The content set the starting player is built against.</param>
    /// <param name="playerId">The identity to mint, defaulting to <see cref="StartPlayerId"/>.</param>
    /// <param name="startedAtUtc">When the profile was created, defaulting to <see cref="StartInstant"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">The content set cannot produce a starting player.</exception>
    public InMemoryGameHost(
        ContentSnapshot content, string playerId = StartPlayerId, DateTimeOffset? startedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var starting = Player.CreateStarting(
            new PlayerId(playerId), HeroNames.Default(content), startedAtUtc ?? StartInstant, content);

        if (starting.IsFailure)
        {
            throw new ArgumentException(
                "the starting player this fake mints does not rehydrate against the content set it " +
                "was handed: " + starting.Error + " A fake standing in for a host that could not " +
                "open a profile at all would let a scenario run against a state no real host reaches.",
                nameof(content));
        }

        _state = new WorldSlice(starting.Value, null);
    }

    /// <summary>The profile this host mints, whether or not it has been opened yet.</summary>
    /// <remarks>Not named <c>Player</c>: that would shadow the aggregate type this class constructs.</remarks>
    public PlayerId Profile => _state.Player.Id;

    /// <summary>Refuses every command from now on with <paramref name="reason"/>.</summary>
    /// <param name="reason">Why. Any reason either tier produces.</param>
    /// <returns>This host, so a scenario reads as one statement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="reason"/> is not a declared reason. The numeric values are permanent wire
    /// values, so an undeclared one is a rejection no server could ever send.
    /// </exception>
    public InMemoryGameHost RefuseEveryCommand(RejectionReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "RejectionReason's numeric values are wire values and its member set is the whole " +
                "catalogue, so a value outside it is one no implementation can produce. A fake that " +
                "accepted it would let a caller switch on a rejection that cannot arrive.");
        }

        _refusal = reason;
        return this;
    }

    /// <summary>Accepts every command from now on — the state a freshly constructed host is in.</summary>
    /// <returns>This host, so a scenario reads as one statement.</returns>
    public InMemoryGameHost AcceptEveryCommand()
    {
        _refusal = null;
        return this;
    }

    /// <inheritdoc/>
    public Task<PlayerId> OpenProfileAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _opened = true;

        return Task.FromResult(_state.Player.Id);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// No profile has been opened, or <paramref name="player"/> is not the one this host minted.
    /// Neither is a player asking for something they cannot have — it is a caller submitting on
    /// behalf of a profile that was never created.
    /// </exception>
    public Task<ApplyCommandOutcome> SubmitAsync(
        PlayerId player, RunId? run, GameCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();

        if (!_opened || player != _state.Player.Id)
        {
            throw new InvalidOperationException(
                $"no state is stored for '{player}'. A host loads the slice before it applies " +
                "anything, and there is nothing to load — which is a composition defect rather than " +
                "a command to refuse.");
        }

        if (run is { } addressed && _state.Run?.Id != addressed)
        {
            return Task.FromResult(ApplyCommandOutcome.Reject(RejectionReason.RUN_NOT_FOUND, _state));
        }

        return Task.FromResult(
            _refusal is { } reason
                ? ApplyCommandOutcome.Reject(reason, _state)
                : ApplyCommandOutcome.Accept(_state, [], []));
    }

    /// <inheritdoc/>
    public Task<OwnStateResult> ReadOwnStateAsync(PlayerId player, RunId? run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_opened || player != _state.Player.Id)
        {
            return Task.FromResult(new OwnStateResult(OwnStateLookup.NoSuchPlayer, null));
        }

        if (run is { } asked && _state.Run?.Id != asked)
        {
            return Task.FromResult(new OwnStateResult(OwnStateLookup.NoSuchRun, null));
        }

        return Task.FromResult(
            new OwnStateResult(
                OwnStateLookup.Found,
                new OwnStateView(_state.Player.ToSnapshot(), _state.Run?.ToSnapshot())));
    }
}
