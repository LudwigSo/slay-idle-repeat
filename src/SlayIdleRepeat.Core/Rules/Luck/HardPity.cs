namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The mandatory guarantee: after N draws without satisfying it, the N-th draw <em>is</em> forced to
/// satisfy it, and the counter goes back to zero.
/// </summary>
/// <remarks>
/// <para>
/// The whole decision lives here, in one predicate. That is what makes "there is exactly one place
/// in the codebase where a guarantee can fire" a checkable claim rather than a policy: nothing
/// outside this namespace may name this type, and the only route to it is the luck service.
/// </para>
/// <para>
/// A counter counts <em>misses since its own last reset</em> — not draws since the account was
/// created, and not draws of a different class. Two ladders of the same class run independently and
/// simultaneously, which is why the counter is addressed by the guarantee it protects rather than
/// by the class alone.
/// </para>
/// </remarks>
internal static class HardPity
{
    /// <summary>
    /// Whether the draw about to be made is the forced one.
    /// </summary>
    /// <remarks>
    /// The comparison is on the draw's own ordinal — misses so far plus this draw — so a rung of
    /// <c>N</c> forces the N-th draw, not the (N+1)-th. Getting that off by one shifts every
    /// guarantee in the game by a draw.
    /// </remarks>
    /// <param name="missesBeforeDraw">The counter's value before this draw. Never negative.</param>
    /// <param name="everyNth">The rung's authored N. Always at least 1.</param>
    /// <returns><see langword="true"/> when this draw must be forced.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="missesBeforeDraw"/> is negative, or <paramref name="everyNth"/> is below 1.
    /// </exception>
    internal static bool Fires(int missesBeforeDraw, int everyNth) =>
        throw new NotImplementedException();

    /// <summary>The counter after a draw that did not satisfy this rung's guarantee.</summary>
    /// <param name="misses">The counter's value before the draw. Never negative.</param>
    /// <returns>The counter's value after the draw.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="misses"/> is negative.</exception>
    internal static int Advance(int misses) => throw new NotImplementedException();

    /// <summary>
    /// The counter after a draw that satisfied this rung's guarantee, forced or not.
    /// </summary>
    /// <remarks>
    /// A natural draw that reaches or beats the guarantee resets the counter exactly as a forced one
    /// does — overshooting a guarantee is satisfying it, and a counter that kept climbing through an
    /// overshoot would fire a redundant guarantee a few draws later.
    /// </remarks>
    /// <returns>The reset value.</returns>
    internal static int Reset() => throw new NotImplementedException();
}
