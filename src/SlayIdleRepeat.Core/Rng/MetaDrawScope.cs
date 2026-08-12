namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 `14` §8.1's <b>second</b> draw regime — the out-of-run one: draw <c>i</c> of stream <c>s</c> is
/// <c>Hash64(GameContext.CommandSeed, s, i)</c> with <c>i</c> starting at <b>0 for each command</b>
/// and <b>no persisted counter</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>It is a different regime from <see cref="RunRngScope"/>, not a variant of it</b>, and the
/// difference that matters is <em>lifetime</em>. A run stream's position is authoritative run state:
/// `14` §8.1 makes <em>"the persisted stream position the draw counter"</em>, <c>Apply</c> folds it
/// back into the <c>Run</c>, and a draw consumed is a draw the player can never see again. A meta
/// draw has none of that. The command is atomic and `14` §16.3's idempotency <b>replays its stored
/// outcome</b>, so a meta draw can never be re-rolled by resubmission — which is precisely why it
/// needs no counter, and why giving it one would be a defect rather than a convenience.
/// </para>
/// <para>
/// 🔒 <b>What that buys, stated plainly, because it is the whole reason this type is separate.</b>
/// Nothing here can move a run's stream positions. <c>GameRules.FoldRngPositions</c> runs its
/// hand-written-position check on a <c>CommandKind.Meta</c> command too — a meta command is
/// dispatched perfectly happily with a run in the slice — so a scope that reached
/// <c>Run.CommitStreamPositions</c> would turn a shop visit into a determinism defect. This type
/// names no aggregate at all, and <c>Core_internal_layering_holds</c> keeps it that way:
/// <c>Core/Rng/</c> sits below <c>Core/Model/</c>, so it <em>could</em> not name a <c>Run</c> even if
/// a future author wanted to.
/// </para>
/// <para>
/// 🔒 <b>Which commands reach it.</b> Exactly the nine ⚄ rows of `14` §2.3 —
/// <c>BEGIN_SESSION</c>, <c>REROLL_QUEST</c>, <c>SPIN_WHEEL</c>, <c>REFORGE_ITEM</c>,
/// <c>RETUNE_ITEM</c>, the three container opens and <c>START_DUEL</c>. The pairing (a seed exactly
/// when the command draws) is pinned by <c>CommandSeedPinTests</c> in
/// <c>SlayIdleRepeat.Core.Tests</c>; <c>HandlerInput.MetaDraws</c> is the only door, and it refuses a
/// command whose context carries no seed rather than inventing one — `30` §3's invariant is that
/// <b>the domain never invents entropy</b>.
/// </para>
/// <para>
/// ⚠️ <b>One scope per <c>Apply</c> call, exactly like <see cref="RunRngScope"/>.</b> It is mutable
/// — the streams it opens count within the command — and it is not thread-safe. What it owns is that
/// one name yields the <em>same</em> stream for the whole command: a handler that asked for a stream
/// twice must continue the sequence rather than restart it at 0 and draw the same value twice.
/// </para>
/// <para>
/// ⚠️ <b>The stream names for `30` §2.3's two daily draws do not exist yet, and this type does not
/// invent them.</b> `14` §8.1's registry table is authored for the run streams and says a system
/// needing randomness <em>"draws from one of these streams or gets a new row here"</em> — no row
/// names the quest slate or the Daily shop block. Adding those rows is a `14` §8.1 amendment owned by
/// <b>M4-09</b> together with the draws themselves (<c>GapRegister</c>: <c>QuestSlate</c>,
/// <c>DailyShopStock</c>), and <see cref="Stream"/> refuses an unregistered name today for exactly
/// that reason — through <see cref="DeterministicRng"/>'s own guard, so there is one registry and one
/// refusal rather than two.
/// </para>
/// <para>
/// ⚠️ <b>There is deliberately no second entry point taking an explicit draw index.</b> Meta draws
/// genuinely <em>are</em> randomly accessible — with no persisted counter, "draw 3 of the day's quest
/// stream" is a pure function of the seed and the index — but a second API over the same seed would
/// let one command <em>count</em> on a stream <b>and</b> <em>address</em> it, double-spending an
/// index with both calls individually correct. The random-access property is proved as a property of
/// the algebra in <c>MetaDrawScopeTests</c>, against <c>Hash64.Of</c> directly, rather than bought
/// with a method that can be misused.
/// </para>
/// </remarks>
internal sealed class MetaDrawScope
{
    private readonly ulong _commandSeed;

    /// <summary>
    /// One stream per name, opened lazily and reused for the rest of the command.
    /// </summary>
    /// <remarks>
    /// Ordinal, for the reason <see cref="RunRngScope"/> copies ordinally: under
    /// <c>OrdinalIgnoreCase</c> the key <c>"DROPS"</c> <em>is</em> <c>"drops"</c>, and a scope that
    /// conflated the two would answer for a stream `14` §8.1 does not have.
    /// </remarks>
    private readonly Dictionary<string, DeterministicRng> _open = new(StringComparer.Ordinal);

    /// <summary>
    /// Opens the scope over one command's server-issued seed.
    /// </summary>
    /// <param name="commandSeed">
    /// 🔒 <c>GameContext.CommandSeed</c>, already known to be present. ⚠️ <c>0</c> is a legitimate
    /// seed and not an absence — which is why <c>GameContext.CommandSeed</c> is
    /// <see cref="Nullable{T}"/> and why the "is there a seed" question is answered by
    /// <c>HandlerInput.MetaDraws</c>, once, rather than by a sentinel here.
    /// </param>
    internal MetaDrawScope(ulong commandSeed) => _commandSeed = commandSeed;

    /// <summary>
    /// 🔒 The stream this command draws <paramref name="streamName"/> from, starting at draw
    /// <b>0</b> and reused for the rest of the command.
    /// </summary>
    /// <param name="streamName">A row of `14` §8.1's stream registry — see <see cref="RngStreams"/>.</param>
    /// <returns>
    /// The stream. ⚠️ Its <c>Position</c> is a <b>within-command</b> count and is <b>never</b>
    /// persisted: nothing folds it back, and the next command over the same seed would start at 0
    /// again. That is the regime, not an oversight — see the type's remarks.
    /// </returns>
    /// <remarks>
    /// The scope does not wrap <c>NextUInt</c>/<c>NextDouble</c>/<c>Range</c>/<c>WeightedPick</c>,
    /// for the reason <see cref="RunRngScope"/> does not: <see cref="DeterministicRng.Position"/>
    /// already <em>is</em> the count, and a second copy of the one-call-one-index contract is a
    /// second copy to keep in step.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="streamName"/> is not a row of the `14` §8.1 registry. Raised by
    /// <see cref="DeterministicRng"/>'s own guard rather than restated here.
    /// </exception>
    internal DeterministicRng Stream(string streamName)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        if (_open.TryGetValue(streamName, out var open))
        {
            return open;
        }

        // 🔒 Position 0, explicitly and always. There is no committed map to seed from — that is the
        // entire difference from RunRngScope — and writing the default out is what makes a future
        // reader see the choice rather than infer it from an omitted argument.
        var stream = new DeterministicRng(_commandSeed, streamName, position: 0);

        _open.Add(streamName, stream);

        return stream;
    }

}
