using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §5's <c>STUN</c> limits — <em>"Max 1.5 s per application, with a 3 s immunity window
/// after"</em> — for one actor.
/// </summary>
/// <remarks>
/// <para>
/// `05` §5 does not offer this as a balance preference. It says: <em>"<b>Stun immunity is
/// mandatory.</b> Without it, stun-locking becomes the only viable build."</em> So the two numbers
/// are the rule, they live in <c>content/statuses.json</c> beside the rest of §5, and this type is
/// the whole of their enforcement.
/// </para>
/// <para>
/// 🔒 <b>Both halves are needed and neither is sufficient.</b> The 1.5 s cap alone leaves a build
/// that reapplies <c>STUN</c> every 1.5 s permanently stun-locking; the immunity window alone leaves
/// one 30 s stun doing the same thing once. `05` §5 states both in one sentence, and
/// <c>StunWindowTests</c> probes each with the other satisfied.
/// </para>
/// <para>
/// ⚠️ <b>The window is measured from the moment the stun <em>ends</em>.</b> `05` §5 writes
/// <em>"after"</em>. Measured from application instead, a 1.5 s stun would leave only 1.5 s of real
/// immunity and the next stun could land at exactly the 3 s mark — half the protection the section
/// asked for, in a way no test of a single stun would notice.
/// </para>
/// <para>
/// Everything here is in <b>ticks</b>, on <see cref="StatusCadence"/>'s reasoning: `05` §3's clock is
/// integral and a window compared in accumulated seconds lands a tick early or late depending on the
/// residue's sign.
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

    /// <summary>Builds the window from `05` §5's two authored constants.</summary>
    /// <param name="maxSecondsPerApplication">`05` §5 — <em>"Max 1.5 s per application"</em>.</param>
    /// <param name="immunityWindowSeconds">`05` §5 — <em>"a 3 s immunity window after"</em>.</param>
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
    /// 🔒 `05` §3.1 slot 4a's <em>"and not stunned"</em> — whether the actor may swing on
    /// <paramref name="tick"/>.
    /// </summary>
    internal bool CanAct(int tick) => _stunnedUntilTick == NotStunned || tick > _stunnedUntilTick;

    /// <summary>
    /// Applies one <c>STUN</c>, capped and gated by `05` §5's window.
    /// </summary>
    /// <param name="tick">The tick the application lands on.</param>
    /// <param name="requestedSeconds">
    /// The applying effect's <c>D</c>. Clipped to `05` §5's per-application cap; the clip is the
    /// rule, not a validation failure, because an effect authoring a 4 s stun is asking for the
    /// longest stun the game allows rather than making an error.
    /// </param>
    /// <returns>
    /// The tick the stun runs to, or <c>null</c> when the immunity window refused it. A caller that
    /// gets <c>null</c> must not register a status: `05` §5's immunity is an immunity to the stun,
    /// not merely to its duration.
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

        // 🔴 A PLAIN ASSIGNMENT, and the Math.Max it replaced was DEAD CODE whose comment claimed the
        // opposite of the shipped behaviour — found by review.
        //
        // That comment said "a reapplication that lands while the actor is still stunned EXTENDS to
        // the later of the two ends". It can never happen. The line below this one sets
        // _immuneUntilTick = _stunnedUntilTick + _immunityTicks, which is strictly greater than
        // _stunnedUntilTick, so every application arriving while a stun is live is refused by the
        // immunity guard above and never reaches here — and Clear() sets NotStunned, so the cleanse
        // route takes the first arm too. Making the extension reachable would need the immunity
        // window not to cover the stun's own duration, which `05` §5 does not authorise.
        //
        // A comment stating a rule the code cannot execute is worse than no comment: the next reader
        // budgets for a behaviour that is not there.
        _stunnedUntilTick = tick + ticks - 1;

        // 🔒 "a 3 s immunity window AFTER" — anchored on the tick the stun ends, so the window is
        // three whole seconds of being actionable rather than three seconds that the stun itself
        // eats into.
        _immuneUntilTick = _stunnedUntilTick + _immunityTicks;

        return _stunnedUntilTick;
    }

    /// <summary>
    /// Clears the stun, leaving the immunity window standing — `18` §2.3's status removal.
    /// </summary>
    /// <remarks>
    /// 🔒 The window survives, and that is the whole point of it: a cleanse that also cleared the
    /// immunity would make a hero carrying one a <em>better</em> stun-lock target than one who is
    /// not, which inverts `05` §5's ruling.
    /// </remarks>
    internal void Clear() => _stunnedUntilTick = NotStunned;

    /// <inheritdoc />
    public override string ToString() =>
        $"STUN cap {_maxTicksPerApplication.ToString(CultureInfo.InvariantCulture)}t, " +
        $"immunity {_immunityTicks.ToString(CultureInfo.InvariantCulture)}t, " +
        $"stunned until {StunnedUntilTick?.ToString(CultureInfo.InvariantCulture) ?? "-"}, " +
        $"immune until {ImmuneUntilTick?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
