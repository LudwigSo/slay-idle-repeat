using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// The STUN limits — a per-application cap and a mandatory immunity window after — for one actor.
/// </summary>
/// <remarks>
/// <para>
/// Both halves are needed and neither is sufficient: the cap alone leaves a build that reapplies
/// STUN constantly permanently stun-locking; the immunity window alone leaves one long stun doing
/// the same thing once.
/// </para>
/// <para>
/// The window is measured from the moment the stun ends, not from application: measured from
/// application, a short stun would leave less than the full immunity and the next stun could land
/// right at the boundary — half the protection intended, in a way no test of a single stun would
/// notice.
/// </para>
/// <para>
/// Everything here is in ticks, on <see cref="StatusCadence"/>'s reasoning: a window compared in
/// accumulated seconds lands a tick early or late depending on the residue's sign.
/// </para>
/// </remarks>
internal sealed class StunWindow
{
    private readonly int _maxTicksPerApplication;
    private readonly int _immunityTicks;

    private int _stunnedUntilTick = NotStunned;
    private int _immuneUntilTick = NotImmune;

    /// <summary>Not currently stunned. Distinct from "stunned until tick 0", which is a real state.</summary>
    private const int NotStunned = int.MinValue;

    /// <summary>Not currently immune.</summary>
    private const int NotImmune = int.MinValue;

    /// <summary>Builds the window from its two authored constants.</summary>
    /// <param name="maxSecondsPerApplication">The per-application cap.</param>
    /// <param name="immunityWindowSeconds">The immunity window after.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either constant is zero or negative.</exception>
    internal StunWindow(double maxSecondsPerApplication, double immunityWindowSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSecondsPerApplication);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(immunityWindowSeconds);

        _maxTicksPerApplication = BattleClock.TicksFor(maxSecondsPerApplication);
        _immunityTicks = BattleClock.TicksFor(immunityWindowSeconds);

        if (_maxTicksPerApplication <= 0 || _immunityTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSecondsPerApplication), maxSecondsPerApplication,
                "05 §5's stun cap and immunity window are both shorter than one simulation tick, so " +
                "neither can bind: a cap of zero ticks is a stun that never lands and a window of " +
                "zero is no immunity at all, which is the state 05 §5 calls stun-locking. 05 §3's " +
                "tick is 0.05 s.");
        }
    }

    /// <summary>The last tick on which the actor is still stunned, or <c>null</c> when it is not.</summary>
    internal int? StunnedUntilTick => _stunnedUntilTick == NotStunned ? null : _stunnedUntilTick;

    /// <summary>The last tick on which the actor is still stun-immune, or <c>null</c>.</summary>
    internal int? ImmuneUntilTick => _immuneUntilTick == NotImmune ? null : _immuneUntilTick;

    /// <summary>
    /// Whether the actor may swing on <paramref name="tick"/>.
    /// </summary>
    internal bool CanAct(int tick) => _stunnedUntilTick == NotStunned || tick > _stunnedUntilTick;

    /// <summary>
    /// Applies one <c>STUN</c>, capped and gated by the window.
    /// </summary>
    /// <param name="tick">The tick the application lands on.</param>
    /// <param name="requestedSeconds">
    /// The applying effect's duration. Clipped to the per-application cap; the clip is the rule, not
    /// a validation failure, because an effect authoring a longer stun is asking for the longest
    /// stun the game allows rather than making an error.
    /// </param>
    /// <returns>
    /// The tick the stun runs to, or <c>null</c> when the immunity window refused it. A caller that
    /// gets <c>null</c> must not register a status: this is an immunity to the stun, not merely to
    /// its duration.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requestedSeconds"/> is negative.</exception>
    internal int? Apply(int tick, double requestedSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestedSeconds);

        if (_immuneUntilTick != NotImmune && tick <= _immuneUntilTick)
        {
            return null;
        }

        var requestedTicks = BattleClock.TicksFor(requestedSeconds);
        var ticks = Math.Min(requestedTicks, _maxTicksPerApplication);

        if (ticks <= 0)
        {
            return null;
        }

        // A plain assignment: a reapplication arriving while a stun is live is always refused by the
        // immunity guard above (immuneUntilTick is always > stunnedUntilTick), so "extend to the
        // later end" can never be reached here — do not reintroduce a Math.Max for it.
        _stunnedUntilTick = tick + ticks - 1;

        // Anchored on the tick the stun ends, so the window is whole seconds of being actionable
        // rather than seconds the stun itself eats into.
        _immuneUntilTick = _stunnedUntilTick + _immunityTicks;

        return _stunnedUntilTick;
    }

    /// <summary>
    /// Clears the stun, leaving the immunity window standing — a status-removal effect.
    /// </summary>
    /// <remarks>
    /// The window survives deliberately: a cleanse that also cleared the immunity would make a hero
    /// carrying one a better stun-lock target than one who is not.
    /// </remarks>
    internal void Clear() => _stunnedUntilTick = NotStunned;

    /// <inheritdoc />
    public override string ToString() =>
        $"STUN cap {_maxTicksPerApplication.ToString(CultureInfo.InvariantCulture)}t, " +
        $"immunity {_immunityTicks.ToString(CultureInfo.InvariantCulture)}t, " +
        $"stunned until {StunnedUntilTick?.ToString(CultureInfo.InvariantCulture) ?? "-"}, " +
        $"immune until {ImmuneUntilTick?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
