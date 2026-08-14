namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §3.1's <b>DoT/HoT cadence</b>, as a pure function of two ticks.
/// </summary>
/// <remarks>
/// <para>
/// The rule, verbatim: <em>"one instance per <c>statusId</c> per target. The instance ticks once per
/// second of battle time: <b>on the 20th simulation tick after first application, and every 20 ticks
/// thereafter</b>. Reapplication adds stacks / refreshes duration per the status's stacking rule
/// (`18` §6) but <b>never re-anchors the cadence</b>."</em>
/// </para>
/// <para>
/// 🔒 <b>Three separate claims, and each is a place an implementation can be wrong while looking
/// right.</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>The anchor is the FIRST application, not the battle.</b> Those two agree on every fight
///     whose status lands on tick 0, which is most of the ones a test writes by hand: a status
///     anchored at 0 ticks on 20, 40, 60 — and so does a battle-anchored one. They separate only at
///     an anchor that is not a multiple of 20. <see cref="TicksPerCadence"/> is that multiple, and
///     <c>StatusCadenceTests</c> anchors at tick 7 for exactly this reason.
///   </item>
///   <item>
///     <b>The first tick is the 20th <em>after</em> application, not the application itself.</b>
///     An off-by-one that fires on the anchor tick as well gives a 3 s <c>BURN</c> four ticks
///     instead of three — a 33% damage error on every DoT in the game, on a fight that otherwise
///     looks entirely correct.
///   </item>
///   <item>
///     <b>Reapplication does not move the anchor.</b> A cadence that re-anchored on every
///     application would let a status reapplied every 19 ticks tick <em>never</em>, which is the
///     opposite of the stacking it was reapplied for.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>Stated in ticks, never in accumulated seconds.</b> `05` §3.1 writes the rule in simulation
/// ticks and <see cref="BattleClock"/> already records why: an accumulator puts a one-second boundary
/// at <c>0.9999999999999999</c> and the tick lands late on one architecture and not the other.
/// Integer arithmetic on the tick index has no such failure mode, which is why nothing here rounds
/// and nothing here touches a <see cref="double"/>.
/// </para>
/// </remarks>
internal static class StatusCadence
{
    /// <summary>
    /// 🔒 `05` §3.1 — <em>"once per second of battle time … every 20 ticks"</em>, which is `05` §3's
    /// tick rate.
    /// </summary>
    /// <remarks>
    /// Derived from <c>CombatLog.TicksPerSecond</c> rather than written as <c>20</c>, because the two
    /// are the same fact: the cadence is <em>one second</em>, and the 20 is only how many ticks that
    /// is. A literal here would be a second copy of `05` §3's tick rate that nothing keeps in step —
    /// and it is deliberately not authored in <c>content/statuses.json</c> either, for the same
    /// reason.
    /// </remarks>
    internal const int TicksPerCadence = CombatLog.TicksPerSecond;

    /// <summary>
    /// 🔒 Whether a cadence boundary falls on <paramref name="tick"/> for an instance first applied
    /// on <paramref name="anchorTick"/>.
    /// </summary>
    /// <param name="anchorTick">
    /// The tick of <b>first</b> application. Reapplication never changes it (`05` §3.1).
    /// </param>
    /// <param name="tick">The tick being run.</param>
    /// <returns>
    /// <c>true</c> on <c>anchorTick + 20</c>, <c>anchorTick + 40</c>, … and nowhere else — in
    /// particular <b>not</b> on <paramref name="anchorTick"/> itself.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="anchorTick"/> is negative. `05` §3.1's pre-tick is tick 0 and there is no
    /// earlier moment for a status to have been applied at; a negative anchor would put a boundary
    /// before the fight started.
    /// </exception>
    internal static bool LandsOn(int anchorTick, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchorTick);

        var elapsed = tick - anchorTick;

        // 🔒 Strictly greater than zero, which is the "AFTER first application" half of the rule.
        // `elapsed >= 0` would also fire on the anchor tick itself, because 0 % 20 == 0 — the
        // off-by-one that gives every DoT one extra tick.
        return elapsed > 0 && elapsed % TicksPerCadence == 0;
    }

    /// <summary>
    /// How many cadence boundaries have fallen on or before <paramref name="tick"/> for an instance
    /// anchored at <paramref name="anchorTick"/>.
    /// </summary>
    /// <remarks>
    /// Not used by the timeline, which asks <see cref="LandsOn"/> once per tick. It exists because it
    /// is the independent statement of the same rule that a test can hold the per-tick answer against
    /// — a counting bug and a boundary bug do not look alike, and one of them is what an
    /// off-by-one in <see cref="LandsOn"/> would be.
    /// </remarks>
    /// <param name="anchorTick">The tick of first application.</param>
    /// <param name="tick">The tick being asked about.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="anchorTick"/> is negative.</exception>
    internal static int TicksLandedBy(int anchorTick, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchorTick);

        var elapsed = tick - anchorTick;

        return elapsed <= 0 ? 0 : elapsed / TicksPerCadence;
    }
}
