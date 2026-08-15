namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The DoT/HoT cadence, as a pure function of two ticks.
/// </summary>
/// <remarks>
/// <para>
/// One instance per status id per target, ticking once per second of battle time: on the 20th
/// simulation tick after first application, and every 20 ticks thereafter. Reapplication adds
/// stacks / refreshes duration but never re-anchors the cadence.
/// </para>
/// <para>
/// Three places an implementation can be wrong while looking right: the anchor is the first
/// application, not the battle (they separate only when the anchor isn't a multiple of 20); the
/// first tick is the 20th <em>after</em> application, not the application itself (an off-by-one
/// there gives a 3 s BURN four ticks instead of three); and reapplication must not move the anchor
/// (otherwise a status reapplied every 19 ticks would never tick).
/// </para>
/// <para>
/// Stated in ticks, never in accumulated seconds: an accumulator puts a one-second boundary at
/// <c>0.9999999999999999</c>, landing the tick late on one architecture and not another.
/// </para>
/// </remarks>
internal static class StatusCadence
{
    /// <summary>
    /// The tick rate — once per second of battle time.
    /// </summary>
    /// <remarks>
    /// Derived from <c>CombatLog.TicksPerSecond</c> rather than written as <c>20</c>, since the
    /// cadence is one second and 20 is only how many ticks that is.
    /// </remarks>
    internal const int TicksPerCadence = CombatLog.TicksPerSecond;

    /// <summary>
    /// Whether a cadence boundary falls on <paramref name="tick"/> for an instance first applied on
    /// <paramref name="anchorTick"/>.
    /// </summary>
    /// <param name="anchorTick">
    /// The tick of first application. Reapplication never changes it.
    /// </param>
    /// <param name="tick">The tick being run.</param>
    /// <returns>
    /// <c>true</c> on <c>anchorTick + 20</c>, <c>anchorTick + 40</c>, … and nowhere else — in
    /// particular not on <paramref name="anchorTick"/> itself.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="anchorTick"/> is negative — there is no earlier moment for a status to have
    /// been applied at.
    /// </exception>
    internal static bool LandsOn(int anchorTick, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(anchorTick);

        var elapsed = tick - anchorTick;

        // Strictly greater than zero: `elapsed >= 0` would also fire on the anchor tick itself
        // (0 % 20 == 0), the off-by-one that gives every DoT one extra tick.
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
