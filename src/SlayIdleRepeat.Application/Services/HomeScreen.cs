using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

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
/// <para>
/// 🔒 <b>Orchestration only.</b> Nothing below decides a game rule: it loads the row, asks
/// <see cref="HomeEnergyView"/> and <see cref="NextChapterView"/> what they answer, asks
/// <see cref="IHeroPowerSource"/> for a reading, and shapes the result. The one branch that looks
/// like a decision — which refusal a rejected <c>START_RUN</c> becomes — is a translation of the
/// domain's own reason into the sentence the screen has for it, never a second judgement about
/// whether the run was legal.
/// </para>
/// </remarks>
public sealed class HomeScreen : IHomeScreen
{
    /// <summary>
    /// The tier the launch block offers. Normal, because that is the only rung the ladder opens
    /// without a clear, and the hub offers the campaign's next chapter rather than a difficulty
    /// choice — choosing a tier is Chapter Select's screen and its own command argument.
    /// </summary>
    private const DifficultyTier OfferedTier = DifficultyTier.NORMAL;

    private readonly IGameHost _host;
    private readonly IClockPort _clock;
    private readonly ContentSnapshot _content;
    private readonly IHeroPowerSource _power;
    private readonly PlayerId _player;

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

        _host = host;
        _clock = clock;
        _content = content;
        _power = power;
        _player = player;
    }

    /// <inheritdoc/>
    public async Task<HomeViewModel> GetViewModelAsync(CancellationToken ct)
    {
        var view = await ReadOwnStateAsync(ct).ConfigureAwait(false);

        var energy = HomeEnergyView.Project(view.Player, _content, _clock.UtcNow);
        var next = NextChapterView.Project(view.Player, _content);

        return new HomeViewModel(
            view.Player.DisplayName,
            view.Player.LegendLevel,
            Crowns(view.Player),
            energy.Current,
            energy.Max,
            energy.RefillIn,
            _power.Read(view.Player, view.Run).PowerIndex,
            next?.ChapterId,

            // ⚠️ Absent, and NextChapterView's remarks say why: a chapter's authored name is a loc
            // key, and resolving one belongs to the client's catalogue. The client names the chapter
            // from NextStageId; a key carried in a field called a name would be drawn as one.
            NextStageName: null,
            next?.RecommendedPower,
            energy.RunCost,
            energy.Shortfall,

            // ⚠️ No reward vocabulary is authored anywhere in game-data/, so the reward line has
            // nothing to name (steering S6). Empty, greppable, and never invented.
            RewardTags: [],

            // ⚠️ Nothing in this build records "the player has not seen this", so no tab may be lit.
            Badges: HomeBadges.None);
    }

    /// <inheritdoc/>
    public async Task<StartRunOutcome> StartRunAsync(int stageId, CancellationToken ct)
    {
        var outcome = await _host
            .SubmitAsync(_player, run: null, new StartRunCommand(stageId, OfferedTier), ct)
            .ConfigureAwait(false);

        if (outcome.Accepted)
        {
            return StartRunOutcome.Started(
                outcome.State.Run?.Id ?? throw new InvalidOperationException(
                    "START_RUN was accepted and the state carries no run. START_RUN is the one " +
                    "command whose whole job is to attach one, so a host answering this way is " +
                    "wired to something that is not the domain."));
        }

        // 🔒 Which sentence a refusal deserves is Core's reading of its own catalogue, not this
        // layer's: a switch over the domain's reasons written here would be a rule decided above the
        // domain, and the source of this whole assembly is scanned to keep one out.
        return RunStartRefusals.Of(outcome.Rejection!.Value) switch
        {
            // The price, and how much of it is missing — measured over the row the host handed back
            // rather than over the one this screen last drew, so the number the player is told is
            // the one the refusal was decided on.
            RunStartRefusal.NotEnoughEnergy => InsufficientEnergy(outcome.State.Player.ToSnapshot()),

            // `10` §7's ladder. The stage id is what tells this refusal from the other (steering S2).
            RunStartRefusal.StageLocked => StartRunOutcome.StageLocked(stageId),

            // 🔒 Anything else is a refusal this screen has no sentence for — most plainly a run
            // that is already open. Answering one of the two above would put a wrong reason in front
            // of the player, and coercing it to a default is exactly what steering S6 forbids, so it
            // is raised rather than translated.
            // ⚠️ Open gap for the presenter: the launch block must not submit a second START_RUN
            // while one is in flight, or a double tap arrives here.
            _ => throw new InvalidOperationException(
                "START_RUN was refused " + outcome.Rejection +
                ", which the Home screen has no sentence for: it offers only the chapter the ladder " +
                "has already opened, and only while the player is in no run. Add an outcome for it " +
                "here rather than answering one of the two the screen can say out loud."),
        };
    }

    /// <summary>The refusal for a run the two banks could not pay for, carrying the shortfall.</summary>
    /// <remarks>
    /// The shortfall is <see cref="HomeEnergyView"/>'s, so the number in the refusal and the number
    /// on the pill are one reading of one rule. It cannot be zero on this branch — the domain
    /// refused the very spend the projection is measuring — and
    /// <see cref="StartRunOutcome.InsufficientEnergy"/> refuses a zero rather than reporting a
    /// shortfall of nothing.
    /// </remarks>
    private StartRunOutcome InsufficientEnergy(PlayerSnapshot player) =>
        StartRunOutcome.InsufficientEnergy(
            HomeEnergyView.Project(player, _content, _clock.UtcNow).Shortfall);

    /// <summary>The player's own row, or the fault that says why there is none.</summary>
    /// <remarks>
    /// A missing profile is not a state the screen can draw around: every number on it is a field of
    /// that row. It is raised rather than answered with a blank hub, which would show a player who
    /// exists a screen that says they own nothing.
    /// </remarks>
    private async Task<OwnStateView> ReadOwnStateAsync(CancellationToken ct)
    {
        var result = await _host.ReadOwnStateAsync(_player, run: null, ct).ConfigureAwait(false);

        return result.View ?? throw new InvalidOperationException(
            "the Home screen asked for player " + _player.Value + "'s own state and the host " +
            "answered " + result.Lookup + ". Every number this screen draws is a field of that row, " +
            "so there is nothing to draw and no plausible stand-in for it.");
    }

    /// <summary>
    /// The Crowns balance — the meta currency, never the run's Gold, which is wiped at run end and
    /// is not carried on this row at all.
    /// </summary>
    /// <remarks>
    /// Indexed rather than probed: <c>Player.Rehydrate</c> refuses a row whose wallet is missing a
    /// player-scoped currency, so an absent key is a corrupt row and not a balance of nothing. A
    /// <c>TryGetValue</c> falling back to zero would draw "0 Crowns" over it (steering S6).
    /// </remarks>
    private static long Crowns(PlayerSnapshot player) => player.Wallet[CurrencyId.CROWNS];
}
