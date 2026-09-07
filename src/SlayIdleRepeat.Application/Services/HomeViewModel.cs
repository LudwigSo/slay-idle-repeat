using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services;

/// <summary>The five destinations the Home screen's tab bar reaches. Five, and no sixth.</summary>
/// <remarks>
/// Named after the systems they open rather than after the reference's captions: the reference calls
/// them Skills and Relics, and this build's are Talents and Collection. A tab named for a screen the
/// game does not have is a tab nobody can implement.
/// </remarks>
public enum HomeTab
{
    /// <summary>This screen. The tab that is always the selected one while it is on the page.</summary>
    Home = 1,

    /// <summary>Worn gear and the stock behind it.</summary>
    Gear = 2,

    /// <summary>The talent trees.</summary>
    Talents = 3,

    /// <summary>Pets, mounts and the rest of what is collected rather than worn.</summary>
    Collection = 4,

    /// <summary>The shop.</summary>
    Shop = 5,
}

/// <summary>Which tabs are carrying a "there is something new here" dot.</summary>
/// <remarks>
/// 🔒 <b>Decided here so the client never decides it.</b> A dot means a system has something the
/// player has not seen, which is a fact about stored state — and a screen inferring it from the
/// numbers it happens to have drawn would light a tab for a reason nothing else in the game agrees
/// with. Flags rather than counts: the reference is explicit that a tab carries a dot, never a
/// number.
/// </remarks>
/// <param name="Home">The Home tab.</param>
/// <param name="Gear">The Gear tab.</param>
/// <param name="Talents">The Talents tab.</param>
/// <param name="Collection">The Collection tab.</param>
/// <param name="Shop">The Shop tab.</param>
public sealed record HomeBadges(bool Home, bool Gear, bool Talents, bool Collection, bool Shop)
{
    /// <summary>No tab is carrying a dot.</summary>
    public static HomeBadges None { get; } = new(false, false, false, false, false);

    /// <summary>Whether <paramref name="tab"/> is carrying a dot.</summary>
    /// <param name="tab">The tab asked about.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A tab this record was never told whether to light. Thrown rather than answered <c>false</c>,
    /// for <c>HomePresenter.ProfileCarried</c>'s reason: a sixth tab arriving as a silent "no dot"
    /// is a tab that can never announce anything, and nothing would fail.
    /// </exception>
    public bool On(HomeTab tab) => tab switch
    {
        HomeTab.Home => Home,
        HomeTab.Gear => Gear,
        HomeTab.Talents => Talents,
        HomeTab.Collection => Collection,
        HomeTab.Shop => Shop,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tab), tab,
            "this tab has no badge flag. Add one here — a default would answer 'nothing new' for a " +
            "tab that may well have something, and the omission would be invisible."),
    };
}

/// <summary>Everything the Home screen draws, in one answer.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Numbers, never strings.</b> Abbreviation ("412K"), thousands separators and the countdown's
/// <c>4:12</c> are the client's, because <c>PlayerNumber</c> already owns that rule for every screen
/// and a second spelling of it here would be a second rule. What crosses this seam is what the rules
/// decided.
/// </para>
/// <para>
/// ⚠️ <b>Four fields have no authorised source yet and are deliberately absent rather than
/// plausible</b> (steering S6): <see cref="NextStageId"/>, <see cref="NextStageName"/>,
/// <see cref="RecommendedPower"/> and <see cref="RewardTags"/>. The par-power table and the chapter
/// names are <c>internal</c> to <c>SlayIdleRepeat.Core</c> and the reference's reward line names no
/// authored vocabulary at all, so a number here would be one this build invented. They are carried
/// as nullable/empty so the day a route exists there is one place to fill in, and greppable so the
/// hole cannot be mistaken for an answer.
/// </para>
/// </remarks>
/// <param name="PlayerName">The player's display name, as the row carries it.</param>
/// <param name="PlayerLevel">Their Legend Level, as the row carries it.</param>
/// <param name="Crowns">
/// The Crowns balance. Crowns, not Gold: Gold is run-scoped and wiped at run end, so a between-runs
/// screen showing it would show a balance that is about to stop existing.
/// </param>
/// <param name="Energy">The main Energy bar.</param>
/// <param name="EnergyMax">What the main bar holds at this Legend Level.</param>
/// <param name="EnergyRefillIn">
/// How long until the next point, or <see cref="TimeSpan.Zero"/> when both banks are full — which is
/// the pill's instruction to hide its caption.
/// </param>
/// <param name="Power">The hero's power index, or <c>null</c> when no reading could be taken.</param>
/// <param name="NextStageId">⚠️ Absent — see the type's remarks.</param>
/// <param name="NextStageName">⚠️ Absent — see the type's remarks.</param>
/// <param name="RecommendedPower">⚠️ Absent — see the type's remarks.</param>
/// <param name="EnergyCost">What starting a run costs.</param>
/// <param name="EnergyShortfall">
/// How much more Energy a run needs than the player holds, or zero when they can afford it.
/// <para>
/// 🔒 <b>The one thing that decides whether the launch block offers a start or a refill.</b> It is
/// carried rather than derived from <see cref="Energy"/> against <see cref="EnergyCost"/>, because
/// a run is paid from the main bar AND the Reserve and this view model deliberately does not carry
/// the Reserve — the pill reads <c>bar/max</c>, since the Reserve is a separate bank and not part
/// of the bar's denominator. A screen subtracting the two numbers it can see would refuse a tap the
/// rules accept, for every player whose Reserve is holding the difference.
/// </para>
/// </param>
/// <param name="RewardTags">⚠️ Empty — see the type's remarks.</param>
/// <param name="Badges">Which tabs are carrying a dot.</param>
public sealed record HomeViewModel(
    string PlayerName,
    int PlayerLevel,
    long Crowns,
    int Energy,
    int EnergyMax,
    TimeSpan EnergyRefillIn,
    double? Power,
    int? NextStageId,
    string? NextStageName,
    double? RecommendedPower,
    int EnergyCost,
    int EnergyShortfall,
    IReadOnlyList<string> RewardTags,
    HomeBadges Badges);

/// <summary>Which of the three things a <c>StartRun</c> can do actually happened.</summary>
public enum StartRunResult
{
    /// <summary>A run was started, and <see cref="StartRunOutcome.Run"/> names it.</summary>
    Started = 1,

    /// <summary>
    /// The two Energy banks together do not cover the run's cost.
    /// <see cref="StartRunOutcome.EnergyShortfall"/> says by how much.
    /// </summary>
    InsufficientEnergy = 2,

    /// <summary>
    /// The stage asked for is not one this player may enter yet.
    /// <see cref="StartRunOutcome.LockedStageId"/> says which.
    /// </summary>
    StageLocked = 3,
}

/// <summary>What a <c>StartRun</c> did, and — when it did nothing — exactly what stopped it.</summary>
/// <remarks>
/// 🔒 <b>None of the three is an exception.</b> Two of them are ordinary answers to an ordinary tap:
/// a player out of Energy and a player who has not unlocked a stage are both playing the game
/// correctly, and the screen has a sentence for each. Only a fault the player cannot cause throws.
/// <para>
/// 🔒 <b>Each refusal carries its own payload, not just its code</b> (steering S2). A shortfall of 8
/// and a locked stage 12 are what the screen says out loud; a bare code would let a wrong branch
/// answer the right enum member with nothing to check it against.
/// </para>
/// </remarks>
public sealed record StartRunOutcome
{
    private StartRunOutcome(
        StartRunResult result, RunId? run, int? energyShortfall, int? lockedStageId)
    {
        Result = result;
        Run = run;
        EnergyShortfall = energyShortfall;
        LockedStageId = lockedStageId;
    }

    /// <summary>Which of the three happened.</summary>
    public StartRunResult Result { get; }

    /// <summary>The run that started, or <c>null</c> for either refusal.</summary>
    public RunId? Run { get; }

    /// <summary>
    /// How much more Energy the run needed than the two banks held, or <c>null</c> unless the
    /// refusal was <see cref="StartRunResult.InsufficientEnergy"/>. Always at least one.
    /// </summary>
    public int? EnergyShortfall { get; }

    /// <summary>
    /// The stage that is locked, or <c>null</c> unless the refusal was
    /// <see cref="StartRunResult.StageLocked"/>.
    /// </summary>
    public int? LockedStageId { get; }

    /// <summary>A run started.</summary>
    /// <param name="run">The run that started.</param>
    public static StartRunOutcome Started(RunId run) =>
        new(StartRunResult.Started, run, null, null);

    /// <summary>The banks could not cover the cost.</summary>
    /// <param name="shortfall">How much more Energy was needed. At least one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shortfall"/> is below one.</exception>
    public static StartRunOutcome InsufficientEnergy(int shortfall) =>
        shortfall < 1
            ? throw new ArgumentOutOfRangeException(
                nameof(shortfall), shortfall,
                "a shortfall of nothing is a run that could have started, so this outcome would be " +
                "refusing a tap the rules would have accepted.")
            : new StartRunOutcome(StartRunResult.InsufficientEnergy, null, shortfall, null);

    /// <summary>The stage is not unlocked.</summary>
    /// <param name="stageId">The stage that is locked.</param>
    public static StartRunOutcome StageLocked(int stageId) =>
        new(StartRunResult.StageLocked, null, null, stageId);
}

/// <summary>The Home screen's seam: one read of everything it draws, and the one thing it does.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not a port, and deliberately not under <c>Ports/</c>.</b> A port is an application-owned
/// interface an external system conforms to, and the catalogue enforces that reading:
/// <c>DependencyRuleTests.Every_port_has_at_least_two_implementations</c> demands a real adapter
/// beside the in-memory fake. There is no external system behind a home screen — it is a projection
/// over <see cref="Ports.Client.IGameHost"/>, which is the port — so declaring it one would be a lie
/// the catalogue then enforces by demanding an adapter nobody can write.
/// </para>
/// <para>
/// Named <c>IHomeScreen</c> rather than the brief's <c>IHomeScreenPresenter</c>: "presenter" already
/// means a plain-C# class under <c>res://game/presenters/</c> in this build, and a second meaning
/// for it at a different layer is the kind of vocabulary collision that makes a review argument out
/// of a file name.
/// </para>
/// </remarks>
public interface IHomeScreen
{
    /// <summary>Everything the screen draws, as of now.</summary>
    /// <param name="ct">Cancellation.</param>
    Task<HomeViewModel> GetViewModelAsync(CancellationToken ct);

    /// <summary>Starts a run at one stage, or says exactly why it did not.</summary>
    /// <param name="stageId">The stage the launch block is offering.</param>
    /// <param name="ct">Cancellation.</param>
    Task<StartRunOutcome> StartRunAsync(int stageId, CancellationToken ct);
}
