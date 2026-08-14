using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The balance harness over the shipped <c>game-data</c>, built once for the whole suite.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A CACHE, not a fixture that hides anything.</b> Two <see cref="Lazy{T}"/> values over
/// <c>GameDataLoader.Load()</c> and <c>new SweepRunner(...)</c>, on
/// <c>ShippedBosses</c>' precedent and for its reason: the harness cases run into the hundreds and
/// each would otherwise re-read forty-six JSON documents off disk and re-derive <c>K_POWER</c>.
/// </para>
/// <para>
/// ⚠️ Sharing is safe because <see cref="SweepRunner"/> holds only immutable catalogues over an
/// immutable <c>ContentSnapshot</c>; <c>RunCell</c> writes nothing to it, which is the same property
/// that lets the sweep run on many threads. Every negative case builds its own snapshot through
/// <c>GameDataLoader.LoadWith</c> rather than touching this one.
/// </para>
/// </remarks>
internal static class ShippedHarness
{
    private static readonly Lazy<Core.Content.ContentSnapshot> LazyContent = new(GameDataLoader.Load);

    private static readonly Lazy<SweepRunner> LazyRunner = new(() => new SweepRunner(Content));

    /// <summary>The shipped <c>game-data</c> tree.</summary>
    internal static Core.Content.ContentSnapshot Content => LazyContent.Value;

    /// <summary>The harness over it, with `29` §2.1's constant already derived.</summary>
    internal static SweepRunner Runner => LazyRunner.Value;
}
