using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Sweep;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// The balance harness over the shipped <c>game-data</c>, built once for the whole suite. Sharing is
/// safe because <see cref="SweepRunner"/> holds only immutable catalogues; every negative case builds
/// its own snapshot through <c>GameDataLoader.LoadWith</c>.
/// </summary>
internal static class ShippedHarness
{
    private static readonly Lazy<Core.Content.ContentSnapshot> LazyContent = new(GameDataLoader.Load);

    private static readonly Lazy<SweepRunner> LazyRunner = new(() => new SweepRunner(Content));

    internal static Core.Content.ContentSnapshot Content => LazyContent.Value;

    internal static SweepRunner Runner => LazyRunner.Value;
}
