namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 `14` §8.1's <b>third</b> draw regime — the combat one: draw <c>i</c> of
/// <see cref="RngStreams.Combat"/> is derived from a server-issued <c>battleSeed</c>, exactly the way
/// <see cref="MetaDrawScope"/>'s meta regime is derived from a command seed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Landed opening this file, closing a gap the M1/M2 merge surfaced.</b> M2 authored two
/// production call sites — <c>BattleSimulation</c>'s constructor and <c>EncounterFight.Run</c> —
/// that each wrote <c>new DeterministicRng(battleSeed, RngStreams.Combat, position)</c> directly.
/// That is exactly the shape <c>DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng</c>
/// exists to refuse, but the rule was authored on <c>main</c> (M0/M1) while M2 built combat on its own
/// branch, so the two never met until this merge — the rule was never wrong, it simply never ran
/// against this code before.
/// </para>
/// <para>
/// 🔒 <b>Why this is a third regime and not a reuse of <see cref="RunRngScope"/> or
/// <see cref="MetaDrawScope"/>.</b> A battle's counter is not persisted <em>as a counter</em> the way
/// a run stream's position is: what a caller persists (or re-supplies) is the <c>battleSeed</c> and,
/// when a fight spans a resumed stream (a pre-battle draw phase followed by the in-fight ticks), the
/// end position of that earlier phase — <c>BattlePlan.RngPosition</c>. That is closer to
/// <see cref="MetaDrawScope"/>'s "the seed is the persisted state" shape than to
/// <see cref="RunRngScope"/>'s write-back, but it is not a meta draw either: `14` §8.1's meta regime
/// always starts at <c>i = 0</c>, while a resumed combat stream may not.
/// </para>
/// <para>
/// ⚠️ <b>No caching, unlike <see cref="MetaDrawScope"/>.</b> A battle opens exactly one
/// <see cref="RngStreams.Combat"/> stream for its whole lifetime — <c>BattleSimulation</c> already
/// holds the single instance on its own <c>Rng</c> property — so there is nothing here to reuse across
/// calls within one fight, and no per-name dictionary to keep in step.
/// </para>
/// </remarks>
internal static class BattleRngScope
{
    /// <summary>
    /// Opens the one <see cref="RngStreams.Combat"/> stream for a fight, over a server-issued
    /// <paramref name="battleSeed"/>.
    /// </summary>
    /// <param name="battleSeed">The seed handed in from outside <c>Rules/</c> — never a run seed.</param>
    /// <param name="position">
    /// The draw index to resume from — <c>0</c> for a fresh fight, or a prior phase's end position
    /// (<c>BattlePlan.RngPosition</c>) when this stream continues one already opened.
    /// </param>
    internal static DeterministicRng Open(ulong battleSeed, ulong position = 0) =>
        new(battleSeed, RngStreams.Combat, position);
}
