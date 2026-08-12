using SlayIdleRepeat.Core.Rules.Combat;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The event lists behind every committed row of <c>CombatLogReferenceVectors.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrored, event for event, by the independent generator that produced the table. Changing a
/// list here without regenerating the table is a break the table announces.
/// </para>
/// <para>
/// 🔒 These are built as raw lists rather than through <see cref="CombatLog"/>, and that is the
/// point of the separation: the table pins the <b>encoding</b>, so it has to be able to express
/// shapes the builder's emission rules forbid — a bare <c>BattleEnd</c>, an event of every type in
/// ordinal order. <see cref="CombatLogTests"/> covers the builder's rules; nothing here does.
/// </para>
/// </remarks>
internal static class ReferenceLogs
{
    private const byte Enemy0 = CombatActor.FirstEnemy;

    /// <summary>A status id, standing in for `05` §5's <c>BURN</c>.</summary>
    private const ushort Burn = 1;

    /// <summary>
    /// 🔒 The one reference row whose events are produced by <see cref="CombatLog"/> itself rather
    /// than written out — so the terminal <see cref="CombatEventType.BattleEnd"/>'s own six fields
    /// are inside the committed hash.
    /// </summary>
    /// <remarks>
    /// Every other row bypasses the builder, which is what the table is for (it must be able to
    /// express shapes the builder forbids). But that left <c>Complete</c>'s <c>BattleEnd</c> — the
    /// last event of every log in the game, and inside the <c>LogHash</c> `11` §6 compares —
    /// pinned by nothing: its actor ids and <c>DataId</c> could be changed to anything and the
    /// whole suite stayed green.
    /// </remarks>
    internal static IReadOnlyList<CombatEvent> CompletedBattle()
    {
        var log = new CombatLog();

        log.Append(0, CombatEventType.Shield, CombatActor.Hero, CombatActor.Hero, 100.0);
        log.Append(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None);
        log.Append(4, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536);
        log.Append(9, CombatEventType.ActorDeath, CombatActor.Hero, Enemy0);

        return log.Complete(heroWon: true, 10, 214.5).Log;
    }

    /// <summary>The event list behind one committed row.</summary>
    internal static IReadOnlyList<CombatEvent> Instance(string rowId) => rowId switch
    {
        "empty-log" => [],

        "battle-start-only" =>
        [
            new CombatEvent(0, CombatEventType.BattleStart, CombatActor.None, CombatActor.None, 0.0, 0),
        ],

        "hit-single" =>
        [
            new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536, 0),
        ],

        "two-events-ordered" =>
        [
            new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536, 0),
            new CombatEvent(5, CombatEventType.Hit, Enemy0, CombatActor.Hero, 17.9004, 0),
        ],

        "two-events-swapped" =>
        [
            new CombatEvent(5, CombatEventType.Hit, Enemy0, CombatActor.Hero, 17.9004, 0),
            new CombatEvent(3, CombatEventType.Hit, CombatActor.Hero, Enemy0, 41.2536, 0),
        ],

        "duplicate-events" =>
        [
            new CombatEvent(7, CombatEventType.StatusTick, Enemy0, CombatActor.Hero, 12.5, 4),
            new CombatEvent(7, CombatEventType.StatusTick, Enemy0, CombatActor.Hero, 12.5, 4),
        ],

        "every-event-type" => AllEventTypes,

        "run-effect-queued" =>
        [
            new CombatEvent(880, CombatEventType.RunEffectQueued, Enemy0, CombatActor.None, 3.0, 41),
        ],

        "telegraph" =>
        [
            new CombatEvent(600, CombatEventType.Telegraph, Enemy0, CombatActor.Hero, 1.2, 41),
        ],

        "actor-ceiling" =>
        [
            new CombatEvent(1, CombatEventType.Attack, CombatActor.MaxId, CombatActor.None, 0.0, 65534),
        ],

        "tick-ceiling" =>
        [
            new CombatEvent(1799, CombatEventType.BattleEnd, CombatActor.None, CombatActor.None, 0.0, 0),
        ],

        "large-damage-value" =>
        [
            new CombatEvent(1200, CombatEventType.Hit, CombatActor.Hero, Enemy0, 8388609.0001, 0),
            new CombatEvent(1201, CombatEventType.Hit, CombatActor.Hero, Enemy0, 8388609.0002, 0),
        ],

        "enrage-stack" => EnrageStack,

        "status-stack-then-expire" =>
        [
            new CombatEvent(20, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 1.0, Burn),
            new CombatEvent(40, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 2.0, Burn),
            new CombatEvent(60, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 3.0, Burn),
            new CombatEvent(60, CombatEventType.StatusApplied, Enemy0, CombatActor.Hero, 3.0, Burn),
            new CombatEvent(80, CombatEventType.StatusTick, Enemy0, CombatActor.Hero, 37.5, Burn),
            new CombatEvent(100, CombatEventType.StatusExpired, Enemy0, CombatActor.Hero, 2.0, Burn),
            new CombatEvent(120, CombatEventType.StatusExpired, Enemy0, CombatActor.Hero, 0.0, Burn),
        ],

        "completed-battle" => CompletedBattle(),

        "negative-value" =>
        [
            new CombatEvent(9, CombatEventType.Heal, CombatActor.Hero, CombatActor.Hero, -0.5, 0),
        ],

        _ => throw new InvalidOperationException(
            $"The reference table row '{rowId}' names no event list in {nameof(ReferenceLogs)}."),
    };

    /// <summary>
    /// One event of every <see cref="CombatEventType"/> member, in ordinal order — the row that
    /// pins every ordinal, so inserting a member mid-enum goes red rather than silently rewriting
    /// every <c>LogHash</c> in existence.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Enum.GetValues{TEnum}()"/> rather than from a written-out list, so a
    /// member added to the enum and forgotten here cannot leave the row quietly short: the count
    /// stops matching the committed <c>eventCount</c>.
    /// </remarks>
    internal static IReadOnlyList<CombatEvent> AllEventTypes { get; } =
        Enum.GetValues<CombatEventType>()
            .OrderBy(type => (int)type)
            .Select(type => new CombatEvent((int)type, type, CombatActor.Hero, Enemy0, 1.0, (ushort)type))
            .ToArray();

    /// <summary>
    /// <c>SYS_ENRAGE</c> firing at 1 Hz from 70 s (`05` §3.1) — the first five of the twenty ticks
    /// the 90 s cap admits.
    /// </summary>
    /// <remarks>
    /// <see cref="CombatEvent.Value"/> is the resulting <b>stack count</b>, per
    /// <see cref="CombatEvent"/>'s slot table — <c>1..5</c>, not the ×1.08 multiplier.
    /// </remarks>
    internal static IReadOnlyList<CombatEvent> EnrageStack { get; } =
        Enumerable.Range(0, 5)
            .Select(i => new CombatEvent(
                1400 + (20 * i), CombatEventType.StatusApplied, Enemy0, Enemy0, i + 1, 9))
            .ToArray();
}
