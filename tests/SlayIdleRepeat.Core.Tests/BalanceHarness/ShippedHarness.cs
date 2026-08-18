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

    /// <summary>
    /// An overlay's documents, plus every shipped document the overlay does not carry at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>ADDITIVE, and the direction is the whole correctness of this helper.</b> The overlay wins
    /// every path it declares and the shipped set fills only the gaps — never the reverse. Several
    /// fixture sets author a <em>partial</em> copy of a shipped document on purpose (a
    /// <c>tuning/forge.json</c> holding only the inventory block, so the capacity cases can choose a
    /// capacity), and letting the shipped one win there would silently replace a number a case is
    /// asserting.
    /// </para>
    /// <para>
    /// ⚠️ Which also means it cannot repair a document the overlay declares WRONGLY — it only supplies
    /// ones the overlay is silent about. That is the intended limit: a fixture that authors a document
    /// badly should fail on its own content rather than be quietly patched from <c>game-data/</c>.
    /// </para>
    /// <para>
    /// 🔴 <b>Why fixture sets keep needing this.</b> A handler's content dependencies grow, and a
    /// hand-assembled set is a list of the documents its readers needed on the day it was written.
    /// M7-06c made <c>CONFIRM_BATTLE_RESULT</c> recompute the fight and M7-06d made <c>START_RUN</c>
    /// score Max HP off the hero's build — each pulled the combat caps, the enemy ladder, the gear
    /// catalogue and the par table into suites that had never read any of them. The alternative is
    /// authoring those documents by hand in every fixture set, which is a second <c>game-data/</c> that
    /// drifts the first time the real one is retuned.
    /// </para>
    /// <para>
    /// The overlay's own <c>Version</c> is kept, not the shipped set's: the stamp identifies the
    /// documents a fixture actually reads, and taking the real set's would claim this snapshot is the
    /// shipped content when the whole point is that it is not.
    /// </para>
    /// </remarks>
    internal static Core.Content.ContentSnapshot WithShippedGaps(Core.Content.ContentSnapshot overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        var merged = new Dictionary<string, Core.Content.ContentDocument>(StringComparer.Ordinal);

        foreach (var path in overlay.DocumentPaths)
        {
            merged[path] = overlay.GetDocument(path);
        }

        foreach (var path in Content.DocumentPaths)
        {
            if (!merged.ContainsKey(path))
            {
                merged[path] = Content.GetDocument(path);
            }
        }

        return new Core.Content.ContentSnapshot(overlay.Version, merged.Values);
    }
}
