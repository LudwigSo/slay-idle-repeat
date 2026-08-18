using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Composition;

/// <summary>Everything one run-end screen needs: the presenter that drives it.</summary>
/// <remarks>
/// A holder for one presenter rather than a bare presenter, matching every other composed screen in
/// this build. It is what gives the screen somewhere to arrive when it grows a second collaborator —
/// the way the replay's and the draft's motion settings arrived beside theirs — without every caller
/// and every handover changing shape on that day.
/// </remarks>
public sealed class ComposedRunEndScreen
{
    /// <summary>Holds the presenter the run-end screen renders.</summary>
    /// <param name="runEnd">Drives the death offer and the tally.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runEnd"/> is null.</exception>
    public ComposedRunEndScreen(RunEndPresenter runEnd)
    {
        ArgumentNullException.ThrowIfNull(runEnd);

        RunEnd = runEnd;
    }

    /// <summary>Drives the run-end screen.</summary>
    public RunEndPresenter RunEnd { get; }
}

/// <summary>
/// Builds the run-end presenter out of the graph the application root already composed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 It exists so the scene never has to. A scene renders and forwards input; naming the device locale
/// source and reaching into the loaded content set the run is projected against are both the composition
/// root's job, and this is the part of it this screen needs.
/// </para>
/// <para>
/// 🔒 <b>The revive arm is READ off the graph, not decided here.</b> <c>12</c> §3.2 makes the Plus promise
/// a decision taken once — <see cref="ClientComposition"/> says of itself that it is *"the only place that
/// branches on the entitlement"*, and <c>IsolationTests.No_entitlement_branch_outside_a_composition_root</c>
/// holds that mechanically. Taking the branch a second time here would falsify that claim even though
/// this file sits in a <c>Composition/</c> directory the rule permits: two sites reading one flag are two
/// answers waiting to disagree, and the disagreement would be a revive button one half of the graph
/// believes in.
/// </para>
/// <para>
/// Nothing is composed a second time. The host, the content set and the locale all come out of the graph
/// the root owns — a second graph would mean a second cache over the same directory, and the run is
/// projected against the same loaded content set the rest of the client draws from.
/// </para>
/// <para>
/// 🔒 <b>A run is required, and that is the shape of this screen.</b> S13/S14 close ONE run: the tally is
/// projected from that run's row and both commands are addressed to it. Nothing here is meaningful
/// between runs, which is exactly why <see cref="InventoryComposition"/> takes no run and this does.
/// </para>
/// </remarks>
public static class RunEndComposition
{
    /// <summary>Wires the run-end screen over an already-composed client, for one run.</summary>
    /// <param name="composed">The graph the application root built and holds.</param>
    /// <param name="player">The profile the boot opened.</param>
    /// <param name="run">The run being closed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="composed"/> is null.</exception>
    public static ComposedRunEndScreen CreateRunEndScreen(
        ComposedGodotClient composed, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(composed);

        var content = composed.Client.Content.Current;
        var strings = new LocaleStringCatalogue(content, composed.Capabilities.PlatformInfo.Locale);

        return new ComposedRunEndScreen(
            new RunEndPresenter(
                composed.Client.GameHost,
                strings,
                content,
                composed.Client.Revive,
                player,
                run));
    }
}
