using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services;

/// <summary>
/// The Home screen's seam over the stored rows: one read of the player, projected into the numbers
/// the hub draws, and one <c>START_RUN</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every derived Energy number comes from <c>HomeEnergyView</c>, and none is computed here.</b>
/// The maximum, the countdown and the run cost are rules arithmetic over tuning that is
/// <c>internal</c> to <c>SlayIdleRepeat.Core</c>; a copy at this layer would disagree with the
/// command the first time either was retuned, and the player would believe the screen.
/// </para>
/// <para>
/// The clock is read once, at the moment a view model is asked for, and handed to the projection as
/// a value — which is the arrangement <see cref="IClockPort"/> exists to make possible.
/// </para>
/// </remarks>
public sealed class HomeScreen : IHomeScreen
{
    /// <summary>Builds the seam over the host, the clock, the content set and the power source.</summary>
    /// <param name="host">Where the player's own state is read and commands are submitted.</param>
    /// <param name="clock">The one sanctioned reading of now.</param>
    /// <param name="content">The loaded content set the energy block is read from.</param>
    /// <param name="power">Where the power number comes from.</param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public HomeScreen(
        IGameHost host,
        IClockPort clock,
        ContentSnapshot content,
        IHeroPowerSource power,
        PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(power);

        // The collaborators are validated and not yet stored: this is a Phase 1 skeleton, and a
        // field nothing reads is a build error under the analyser set. Phase 3 keeps them.
    }

    /// <inheritdoc/>
    public Task<HomeViewModel> GetViewModelAsync(CancellationToken ct) =>
        throw new NotImplementedException(
            "HomeScreen.GetViewModelAsync is a Phase 1 skeleton: the tests stating what it must " +
            "map are written and red. Phase 3 implements it over the host, the clock, the content " +
            "set and the power source this instance was built with.");

    /// <inheritdoc/>
    public Task<StartRunOutcome> StartRunAsync(int stageId, CancellationToken ct) =>
        throw new NotImplementedException(
            "HomeScreen.StartRunAsync is a Phase 1 skeleton: the tests stating each of its three " +
            "outcomes are written and red. Phase 3 implements it.");
}
