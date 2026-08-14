using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The balance harness over the shipped <c>game-data</c>, built once for the whole suite.
/// </summary>
/// <remarks>
/// 🔒 A cache, not a fixture that hides anything: the harness cases run into the hundreds and each
/// would otherwise re-read forty-six JSON documents and re-derive <c>K_POWER</c>. ⚠️ Sharing is safe
/// because <see cref="SweepRunner"/> holds only immutable catalogues; every negative case builds its
/// own snapshot through <c>GameDataLoader.LoadWith</c>.
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
