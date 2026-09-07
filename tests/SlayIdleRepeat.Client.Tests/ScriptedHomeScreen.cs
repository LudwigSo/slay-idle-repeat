using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// An <see cref="IHomeScreen"/> answering exactly what a case tells it to, and recording what it
/// was asked to start.
/// </summary>
/// <remarks>
/// The five launch states are decided from ONE view model, so a fake that hands the presenter a
/// chosen one is the whole fixture. It records the start calls because two of the five states must
/// not start a run at all, and a presenter that submitted anyway would look identical from outside.
/// </remarks>
internal sealed class ScriptedHomeScreen : IHomeScreen
{
    private readonly HomeViewModel? _view;
    private readonly Exception? _fault;
    private readonly StartRunOutcome _outcome;

    private ScriptedHomeScreen(HomeViewModel? view, Exception? fault, StartRunOutcome outcome)
    {
        _view = view;
        _fault = fault;
        _outcome = outcome;
    }

    /// <summary>How many times the presenter asked to start a run.</summary>
    internal int StartCallCount { get; private set; }

    /// <summary>The stage the last start named, or <c>null</c> when none was asked for.</summary>
    internal int? LastStageStarted { get; private set; }

    /// <summary>A seam answering one view model, and starting the run it is asked for.</summary>
    /// <param name="view">What the screen draws.</param>
    internal static ScriptedHomeScreen Answering(HomeViewModel view) =>
        new(view, null, StartRunOutcome.Started(new RunId("RUN_home_hub_fixture")));

    /// <summary>A seam whose read does not answer.</summary>
    /// <param name="fault">What went wrong.</param>
    internal static ScriptedHomeScreen Faulting(Exception fault) => new(null, fault, Outcomes.Refused);

    /// <summary>A view model with the four fields the launch states are decided from.</summary>
    /// <param name="energy">The main bar.</param>
    /// <param name="energyCost">What a run costs.</param>
    /// <param name="power">The hero's power, or <c>null</c> when there was no reading.</param>
    /// <param name="recommendedPower">The stage's recommendation, or <c>null</c> when there is none.</param>
    internal static HomeViewModel ViewModel(
        int energy, int energyCost, double? power = null, double? recommendedPower = null) =>
        new(
            PlayerName: "Ryn, Ashblade",
            PlayerLevel: 63,
            Crowns: 412_345L,
            Energy: energy,
            EnergyMax: 120,
            EnergyRefillIn: TimeSpan.FromSeconds(252),
            Power: power,
            NextStageId: null,
            NextStageName: null,
            RecommendedPower: recommendedPower,
            EnergyCost: energyCost,
            RewardTags: [],
            Badges: HomeBadges.None);

    /// <inheritdoc/>
    public Task<HomeViewModel> GetViewModelAsync(CancellationToken ct) =>
        _fault is { } fault
            ? Task.FromException<HomeViewModel>(fault)
            : Task.FromResult(_view!);

    /// <inheritdoc/>
    public Task<StartRunOutcome> StartRunAsync(int stageId, CancellationToken ct)
    {
        StartCallCount++;
        LastStageStarted = stageId;

        return Task.FromResult(_outcome);
    }

    private static class Outcomes
    {
        /// <summary>What a faulted seam would answer if anything asked it to start a run.</summary>
        internal static StartRunOutcome Refused { get; } = StartRunOutcome.StageLocked(0);
    }
}
