namespace SlayIdleRepeat.Core.Rng;

/// <summary>The combat draw regime: draw <c>i</c> of <see cref="RngStreams.Combat"/> is derived from a server-issued <c>battleSeed</c>, the same way <see cref="MetaDrawScope"/>'s regime is derived from a command seed.</summary>
/// <remarks>
/// A third regime, not a reuse of <see cref="RunRngScope"/> or <see cref="MetaDrawScope"/>: a
/// battle's counter is not persisted as a counter the way a run stream's position is. What a caller
/// persists (or re-supplies) is the <c>battleSeed</c> and, when a fight spans a resumed stream, the
/// end position of the earlier phase (<c>BattlePlan.RngPosition</c>) — closer to
/// <see cref="MetaDrawScope"/>'s "the seed is the persisted state" shape, except a resumed combat
/// stream may not start at <c>i = 0</c> the way a meta draw always does.
/// <para>
/// No caching, unlike <see cref="MetaDrawScope"/>: a battle opens exactly one
/// <see cref="RngStreams.Combat"/> stream for its whole lifetime, already held by the caller, so
/// there is nothing here to reuse across calls within one fight.
/// </para>
/// </remarks>
internal static class BattleRngScope
{
    /// <summary>Opens the one <see cref="RngStreams.Combat"/> stream for a fight, over a server-issued <paramref name="battleSeed"/>.</summary>
    /// <param name="battleSeed">The seed handed in from outside <c>Rules/</c> — never a run seed.</param>
    /// <param name="position">
    /// The draw index to resume from — <c>0</c> for a fresh fight, or a prior phase's end position
    /// (<c>BattlePlan.RngPosition</c>) when this stream continues one already opened.
    /// </param>
    internal static DeterministicRng Open(ulong battleSeed, ulong position = 0) =>
        new(battleSeed, RngStreams.Combat, position);
}
