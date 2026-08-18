using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the run Shop screen has got with the read everything it draws depends on.</summary>
public enum ShopStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is standing on an unresolved shop tile.</summary>
    Ready = 2,

    /// <summary>
    /// The run was read and is not standing on a shop. Named rather than folded into
    /// <see cref="RunMissing"/>: the run is fine, the screen is simply open on the wrong tile.
    /// </summary>
    NotAtAShop = 3,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,
}

/// <summary>What one submission from this screen did.</summary>
public enum ShopSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering — a screen that
    /// collapsed them would tell a player their move was illegal when the network dropped.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>
/// Drives the run Shop screen: says where the player is standing, says why there is nothing to buy,
/// and leaves.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔴 <b>There is no shop here, and that is the whole screen.</b> The run row carries no offer
/// state — no stock, no prices, no refresh count — so the rules layer refuses every purchase and
/// every refresh outright, and resolving the tile is the only thing a shop visit can legally do.
/// This screen therefore renders <b>no buy slot and no refresh control</b>: an affordance for an
/// offer that does not exist would assert that an offer exists, and a player who pressed it would
/// be told their own purchase was illegal. It says in words that nothing is stocked, and offers the
/// one action that works. See <see cref="TheOfferModelIsNotBuiltYet"/>.
/// </para>
/// <para>
/// 🔒 Every way this screen can come to nothing gets its own sentence: an empty shop, a screen
/// opened where no shop is, a refused departure and a host that never answered all look identical
/// on a page made entirely of text.
/// </para>
/// </remarks>
public sealed class ShopPresenter
{
    /// <summary>
    /// ⚠️ Deliberately not built, and named so it can be found. A run's shop has no stock because
    /// nothing in the persisted run describes one, and inventing a slot here would be inventing the
    /// economy that fills it.
    /// </summary>
    private const string TheOfferModelIsNotBuiltYet =
        "SHOP_BUY validates its slot index and then refuses every call, and SHOP_REFRESH refuses " +
        "every call, both because the run row carries no offer, no price, no visit count and no " +
        "refresh allowance. The tile's own resolution deliberately does nothing but clear the " +
        "pending tile, which is what lets the run walk away. The offer catalogue, the refresh " +
        "economy and the consumable pouch are one later milestone's work; until it lands, a buy " +
        "slot drawn here would be a control whose only possible outcome is a refusal, and a price " +
        "drawn beside it would be a number this build invented.";

    private const string TitleNameKey = "loc.shop.title.name";
    private const string LeaveActionKey = "loc.shop.leave.action";
    private const string NothingStockedBlockKey = "loc.shop.nothing_stocked.block";
    private const string LoadingStatusKey = "loc.shop.loading.status";
    private const string RunMissingStatusKey = "loc.shop.run_missing.status";
    private const string NotAtAShopStatusKey = "loc.shop.not_at_a_shop.status";
    private const string ReadUnavailableStatusKey = "loc.shop.read_unavailable.status";
    private const string RefusedStatusKey = "loc.shop.refused.status";
    private const string HostUnavailableStatusKey = "loc.shop.host_unavailable.status";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>
    /// The tile kind a shop is, as the run reports it.
    /// </summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed. The pending tile arrives as a
    /// bare number, but the enum that assigns each kind its number is public and this project
    /// already names it — so a kind inserted above this one renumbers this constant with it, where a
    /// copied literal would go on naming whatever had moved into slot six. That silent renumbering
    /// is the one residue <see cref="BoardTileKinds"/> cannot close for the table it must transcribe,
    /// and it is not a price worth paying where nothing forces it.
    /// </remarks>
    public const int ShopTileKind = (int)TileKind.Shop;

    /// <summary>
    /// How many buy slots this screen draws. 🔒 Zero, and it is a stated number rather than an
    /// absent control so that a case can pin the absence — see <see cref="TheOfferModelIsNotBuiltYet"/>.
    /// </summary>
    public const int BuySlotCount = 0;

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly PlayerId _player;
    private readonly RunId _run;

    private bool _submissionInFlight;

    /// <summary>Builds the screen over the host, the strings and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ShopPresenter(IGameHost gameHost, LocaleStringCatalogue strings, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);

        _gameHost = gameHost;
        _strings = strings;
        _player = player;
        _run = run;
    }

    /// <summary>How far the read this screen depends on has got.</summary>
    public ShopStage Stage { get; private set; } = ShopStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>Whether the screen offers a refresh control. 🔒 Never — there is nothing to refresh.</summary>
    public bool RefreshOffered => false;

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The leave control's caption, resolved.</summary>
    public string LeaveText => _strings.Resolve(LeaveActionKey);

    /// <summary>The named reason there is nothing to buy, resolved.</summary>
    /// <remarks>
    /// A permanent line rather than one shown on a failure. Nothing about this build stocks a shop
    /// on a good day, so a line that came and went would suggest a state where it does.
    /// </remarks>
    public string NothingStockedText => _strings.Resolve(NothingStockedBlockKey);

    /// <summary>The line saying what the screen is doing while it is not yet an answer, resolved.</summary>
    public string StatusText => Stage switch
    {
        ShopStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        ShopStage.Ready => NothingLeftToSay,
        ShopStage.NotAtAShop => _strings.Resolve(NotAtAShopStatusKey),
        ShopStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// 🔒 The fault is read before the rejection, because a faulted submission carries no rejection
    /// at all and the two must not share a sentence: one says the game refused you, the other says
    /// the game did not answer.
    /// </remarks>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>Reads the run this screen is about and settles everything drawn from it.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Awaited inside the guard rather than merely called inside it: a real host's read is an
            // async method, so its failure arrives as a faulted task and a try around the call alone
            // would never see it.
            var state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception)
        {
            Stage = ShopStage.ReadUnavailable;
        }
    }

    /// <summary>Submits <c>RESOLVE_TILE</c>, which is the whole of what a shop visit can do.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<ShopSubmission> LeaveAsync(CancellationToken ct)
    {
        if (Stage != ShopStage.Ready)
        {
            return ShopSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ResolveTileCommand(), ct).ConfigureAwait(false);
    }

    /// <remarks>
    /// 🔒 The latch is taken BEFORE the await, not after it. Taken afterwards, a second call
    /// arriving while the first is in flight finds it unset and submits again — which is how a
    /// double-tap on the one control this screen has spends two commands, and how it shipped once
    /// already on another screen in this milestone.
    /// </remarks>
    private async Task<ShopSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return ShopSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                return ShopSubmission.RefusedByRules;
            }

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a tile the command has cleared.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                Carry(moved);
            }

            return ShopSubmission.Submitted;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting
            // one would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return ShopSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a departure that never answered left the run on the tile, so
            // the retry has to be able to reach the host.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Run is not { } run)
        {
            Stage = ShopStage.RunMissing;
            return;
        }

        Carry(run);
    }

    private void Carry(RunSnapshot run) =>
        Stage = run.PendingTileKind == ShopTileKind ? ShopStage.Ready : ShopStage.NotAtAShop;
}
