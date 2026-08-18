using System.Globalization;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Application.UseCases;

/// <summary>A player asking for the fight their run is standing in.</summary>
/// <param name="Player">Whose run to read.</param>
public sealed record SimulatePendingBattleRequest(PlayerId Player);

/// <summary>What the read found.</summary>
public enum PendingBattleLookup
{
    /// <summary>The run has a battle open and the fight is in the view.</summary>
    Found = 1,

    /// <summary>Nothing is stored for that player at all.</summary>
    NoSuchPlayer = 2,

    /// <summary>
    /// That player has no run standing in a battle — no run at all, or one that has not opened one.
    /// </summary>
    /// <remarks>
    /// The two are one answer because the escape is the same: nothing to render, and the screen
    /// should not have asked. They are held apart from <see cref="NoSuchPlayer"/>, whose escape is a
    /// different address entirely.
    /// </remarks>
    NoOpenBattle = 3,
}

/// <summary>The fight a run is standing in, and the two numbers a caller needs beside it.</summary>
/// <param name="BattleSeed">The seed this fight was derived at.</param>
/// <param name="Fight">The replay: the log a screen animates and the outcome it lands on.</param>
/// <param name="LogHash">
/// <paramref name="Fight"/>'s hash, spelled the way <c>CONFIRM_BATTLE_RESULT</c> parses it.
/// </param>
/// <remarks>
/// 🔒 The hash is formatted here, once. The confirming command parses it with
/// <see cref="NumberStyles.None"/> against the invariant culture, so a caller that spelled the same
/// number with a group separator, a sign or in hex would be refused while holding a perfectly
/// correct fight — and would have no way to tell that from a fight the server disagreed with.
/// </remarks>
public sealed record PendingBattleView(ulong BattleSeed, SimulationResult Fight, string LogHash)
{
    /// <summary>How stale this view may be: not at all.</summary>
    /// <remarks>
    /// Every read model declares one, and this one is not a judgement call: the fight is a function
    /// of the player's own rows, which are read through the write model with no staleness tolerated
    /// at all, and the hash it carries is checked against a fight the server recomposes from those
    /// same rows. A view served a moment late is a hash the server disagrees with, which a caller
    /// cannot tell apart from a fight it got wrong.
    /// </remarks>
    public static TimeSpan StalenessBudget => TimeSpan.Zero;
}

/// <summary>What the read answered: the lookup, and the fight when there is one.</summary>
/// <param name="Lookup">What was found.</param>
/// <param name="View">The fight, or <c>null</c> when there is none.</param>
public sealed record PendingBattleResult(PendingBattleLookup Lookup, PendingBattleView? View);

/// <summary>
/// The read that turns a run standing in an open battle into the fight it is standing in.
/// </summary>
/// <remarks>
/// <para>
/// Stored rows out and nothing in: it writes nothing, so opening a battle screen cannot slide a
/// run's expiry or stamp a player as active, and looking at a fight twice is the same run as looking
/// once.
/// </para>
/// <para>
/// 🔒 <b>It decides nothing about the fight.</b> Which enemy a tile is, which seed the battle runs
/// at and what the hero's stat block is are all the rules assembly's, reached through one entry
/// point. This layer loads the rows, calls it, and formats the hash — which is the whole of what an
/// orchestration layer is allowed to be.
/// </para>
/// <para>
/// ⚠️ Deliberately not a method on the host interface. The host is a transport seam, and a battle is
/// simulated <em>locally</em> from a server-issued seed; hanging this off it would turn a pure local
/// computation into a network round trip the day the transport stops being this process.
/// </para>
/// </remarks>
public sealed class SimulatePendingBattleUseCase
{
    private readonly WorldSliceStore _store;
    private readonly ContentSnapshot _content;

    /// <summary>Builds the use case over the store it reads through and the content the fight reads.</summary>
    /// <param name="store">Where the rows live.</param>
    /// <param name="content">The loaded, validated content set.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public SimulatePendingBattleUseCase(WorldSliceStore store, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(content);

        _store = store;
        _content = content;
    }

    /// <summary>Reads the fight the player's run is standing in.</summary>
    /// <param name="request">Whose run to read.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The lookup and, when there is a battle open, the fight.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async Task<PendingBattleResult> ExecuteAsync(
        SimulatePendingBattleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _store.ReadSnapshotsAsync(request.Player, ct).ConfigureAwait(false);

        if (stored is null)
        {
            return new PendingBattleResult(PendingBattleLookup.NoSuchPlayer, null);
        }

        // 🔒 Asked, never decided. What counts as a run standing in a battle is a phase, a pending
        // tile, that tile's kind and a drawn combat counter, and the composition below already reads
        // all four — so a second opinion formed here would be a game rule above the domain, and a
        // narrower one: it would answer "there is a fight" for states the composition refuses.
        if (stored.Run is not { } open || !RunBattle.HasOpenBattle(open))
        {
            return new PendingBattleResult(PendingBattleLookup.NoOpenBattle, null);
        }

        var fight = RunBattle.Simulate(stored.Player, open, _content);

        return new PendingBattleResult(
            PendingBattleLookup.Found,
            new PendingBattleView(
                RunBattle.SeedOf(open),
                fight,
                fight.LogHash.ToString(CultureInfo.InvariantCulture)));
    }
}
