using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// What the Home screen's primary action does, decided from stored state and nothing else.
/// </summary>
/// <remarks>
/// A named value rather than a bool, because "there is no run", "the last run finished", "there is
/// no such profile" and "the read did not answer" are four different facts about the world that a
/// two-state answer would collapse into one. Nothing is persisted or remembered by the client to
/// decide this: every value below is a function of one <c>ReadOwnStateAsync</c> answer.
/// </remarks>
public enum HomeContinueDecision
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>
    /// There is no run to resume — either none was ever started, or the last one has ended. A
    /// finished run is what the next <c>START_RUN</c> clears, so a second run is startable.
    /// </summary>
    StartNewRun = 2,

    /// <summary>A run is open, and <see cref="HomePresenter.ContinuableRun"/> names it.</summary>
    ContinueRun = 3,

    /// <summary>Nothing is stored for this player. Named, so it can never read as "start".</summary>
    ProfileMissing = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,
}

/// <summary>
/// Drives the Home screen: the header the profile carries, and the one decision the screen exists
/// to make — start a run, or resume the one already open.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it. The scene above renders what is exposed here; the
/// composition root below decides what the collaborators actually are.
/// </para>
/// <para>
/// 🔒 <b>The absences are deliberate and are the design.</b> The header shows the two Energy amounts
/// the player snapshot literally carries and nothing derived from them: no maximum, no denominator,
/// no regeneration countdown, no Legend-XP percentage and no run Energy cost. Every tuning reader
/// that could compute one is internal to <c>SlayIdleRepeat.Core</c> and the host seam exposes no
/// derived-value read, so a value produced here would be a second copy of a formula the rules
/// already own — and two copies of a balance formula disagree the first time either is tuned.
/// </para>
/// <para>
/// ⚠️ Also absent, each because a later row owns it: the hero diorama, daily quests, ad widgets,
/// chest pity, event and guild cards, the inbox envelope, the account-link banner, the bottom
/// navigation, the Legend XP bar and the Dungeons entry.
/// </para>
/// </remarks>
public sealed class HomePresenter
{
    /// <summary>Builds the screen over the host, the strings and the profile boot opened.</summary>
    /// <param name="gameHost">The seam the player's own state is read through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public HomePresenter(IGameHost gameHost, LocaleStringCatalogue strings, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);

        _ = player;
    }

    /// <summary>What the primary action does, and why.</summary>
    public HomeContinueDecision Decision => throw NotBuilt();

    /// <summary>The run to resume, or null unless <see cref="Decision"/> is <see cref="HomeContinueDecision.ContinueRun"/>.</summary>
    public RunId? ContinuableRun => throw NotBuilt();

    /// <summary>The player's display name, as the snapshot carries it.</summary>
    public string DisplayName => throw NotBuilt();

    /// <summary>The player's Legend Level, as the snapshot carries it.</summary>
    public int LegendLevel => throw NotBuilt();

    /// <summary>The main Energy bar's amount, as the snapshot carries it — no denominator exists.</summary>
    public int Energy => throw NotBuilt();

    /// <summary>The Energy Reserve's amount, as the snapshot carries it — no capacity exists.</summary>
    public int EnergyReserve => throw NotBuilt();

    /// <summary>The caption beside <see cref="LegendLevel"/>, resolved.</summary>
    public string LegendLevelLabel => throw NotBuilt();

    /// <summary>The caption beside <see cref="Energy"/>, resolved.</summary>
    public string EnergyLabel => throw NotBuilt();

    /// <summary>The caption beside <see cref="EnergyReserve"/>, resolved.</summary>
    public string EnergyReserveLabel => throw NotBuilt();

    /// <summary>The primary action's caption for the current <see cref="Decision"/>, resolved.</summary>
    public string ActionText => throw NotBuilt();

    /// <summary>The line shown while there is no decision to offer, resolved.</summary>
    public string StatusText => throw NotBuilt();

    /// <summary>What went wrong when <see cref="Decision"/> is <see cref="HomeContinueDecision.ReadUnavailable"/>.</summary>
    public string? FailureDetail => throw NotBuilt();

    /// <summary>Reads the player's own state and settles <see cref="Decision"/>.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct)
    {
        _ = ct;

        throw NotBuilt();
    }

    private static NotImplementedException NotBuilt() =>
        new("HomePresenter is a declaration-only stub: the tests that describe it are written, the behaviour is not.");
}
