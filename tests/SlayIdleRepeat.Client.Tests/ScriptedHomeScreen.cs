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
    private readonly Exception? _startFault;
    private readonly StartRunOutcome _outcome;

    private ScriptedHomeScreen(
        HomeViewModel? view, Exception? fault, StartRunOutcome outcome, Exception? startFault = null)
    {
        _view = view;
        _fault = fault;
        _outcome = outcome;
        _startFault = startFault;
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

    /// <summary>
    /// A seam whose read answers, and whose <c>START_RUN</c> raises rather than refusing.
    /// </summary>
    /// <remarks>
    /// 🔴 The real seam does exactly this for every refusal it has no sentence for — a run opened
    /// on another device, most plainly. It is not a fixture convenience: it is the one behaviour of
    /// the shipped <c>HomeScreen</c> that a press can meet and that no outcome describes.
    /// </remarks>
    /// <param name="view">What the screen draws.</param>
    /// <param name="startFault">What the submission raises.</param>
    internal static ScriptedHomeScreen RaisingOnStart(HomeViewModel view, Exception startFault) =>
        new(view, null, Outcomes.Refused, startFault);

    /// <summary>The chapter this fixture offers unless a case says otherwise.</summary>
    /// <remarks>
    /// 🔴 <b>A real chapter, and the default is the whole point.</b> A press is refused for three
    /// separate reasons - a state that offers something else, a submission already outstanding, and a
    /// campaign with NO chapter to run - and this builder used to answer the third one for every case
    /// built on it. A case about the launch STATE was then turned away before the state was ever
    /// consulted, and stayed green against a presenter carrying no state check at all. That defect
    /// survived a whole review phase, because a fixture default that disarms a case looks exactly
    /// like a case that passes.
    /// So the default is the state in which the assertions discriminate, and a case that wants a
    /// campaign offering nothing says so out loud - <c>with { NextStageId = null }</c> - which is a
    /// line a reader can see.
    /// </remarks>
    internal const int OfferedChapter = 7;

    /// <summary>The bar's maximum every fixture here draws over. Named so a case can spell it.</summary>
    internal const int FixtureEnergyMax = 120;

    /// <summary>The countdown a fixture carries unless it says otherwise — the reference's 4:12.</summary>
    internal static readonly TimeSpan DefaultRefillIn = TimeSpan.FromSeconds(252);

    /// <summary>A view model with the five fields the launch states are decided from.</summary>
    /// <remarks>
    /// 🔒 <c>energyShortfall</c> is its own argument rather than <c>energyCost - energy</c>, and that
    /// is the point: a run is paid from the main bar and the Reserve together, the view model carries
    /// only the bar, and so the two are independent. Passing them separately is what lets a case put
    /// the screen in the state this build's rules can actually produce — a bar below the price with
    /// nothing missing, because the Reserve holds the difference.
    /// </remarks>
    /// <param name="energy">The main bar.</param>
    /// <param name="energyCost">What a run costs.</param>
    /// <param name="energyShortfall">How much of the cost the two banks cannot cover. Zero when they can.</param>
    /// <param name="power">The hero's power, or <c>null</c> when there was no reading.</param>
    /// <param name="recommendedPower">The stage's recommendation, or <c>null</c> when there is none.</param>
    /// <param name="nextStageId">
    /// The chapter the campaign offers next, or <c>null</c> for a player it offers nothing. See
    /// <see cref="OfferedChapter"/> for why the default is a chapter rather than an absence.
    /// </param>
    /// <param name="energyRefillIn">
    /// How long until the next point of Energy, and <see cref="TimeSpan.Zero"/> when both banks are
    /// full.
    /// </param>
    /// <remarks>
    /// 🔴 <paramref name="energyRefillIn"/> is a knob because it was not one. Every case built on
    /// this builder got the same 252 seconds, so <c>EnergyPillCaption</c>'s other branch — hidden at
    /// full, which is a requirement the brief states in as many words — was UNREACHABLE from the
    /// shared fixture, and nothing anywhere drew the pill without a caption. The default is the
    /// countdown, because the countdown is the state the pill is in for all but the last minutes of
    /// a full bar; a case about being full says <c>TimeSpan.Zero</c> out loud.
    /// </remarks>
    internal static HomeViewModel ViewModel(
        int energy,
        int energyCost,
        int energyShortfall = 0,
        double? power = null,
        double? recommendedPower = null,
        int? nextStageId = OfferedChapter,
        TimeSpan? energyRefillIn = null) =>
        new(
            PlayerName: "Ryn, Ashblade",
            PlayerLevel: 63,
            Crowns: 412_345L,
            Energy: energy,
            EnergyMax: FixtureEnergyMax,
            EnergyRefillIn: energyRefillIn ?? DefaultRefillIn,
            Power: power,
            NextStageId: nextStageId,
            NextStageName: null,
            RecommendedPower: recommendedPower,
            EnergyCost: energyCost,
            EnergyShortfall: energyShortfall,
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

        return _startFault is { } fault
            ? Task.FromException<StartRunOutcome>(fault)
            : Task.FromResult(_outcome);
    }

    private static class Outcomes
    {
        /// <summary>What a faulted seam would answer if anything asked it to start a run.</summary>
        internal static StartRunOutcome Refused { get; } = StartRunOutcome.StageLocked(0);
    }
}
