using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

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

/// <summary>One row of the shop's offer, as the screen draws it.</summary>
/// <param name="SlotIndex">The index <c>SHOP_BUY</c> names to buy this slot.</param>
/// <param name="Name">The slot's name, resolved — the pool it drew from, or the item it holds.</param>
/// <param name="Price">What it costs in run-local Gold, with the run's own price modifiers applied.</param>
/// <param name="Buyable">
/// Whether pressing it could succeed. False for a slot already bought, one the run cannot afford,
/// and one whose pool had nothing left — and the screen says WHICH through <paramref name="Blocked"/>
/// rather than greying it out silently.
/// </param>
/// <param name="Blocked">The resolved reason it cannot be pressed, or empty when it can.</param>
public sealed record ShopSlotCard(
    int SlotIndex, string Name, long Price, bool Buyable, string Blocked);

/// <summary>
/// Drives the run Shop screen: draws the four slots the run's own visit stocked, buys them,
/// restocks, and leaves.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The offer is PROJECTED, never re-derived here.</b> <c>Rules.Economy.ShopView</c> reads the
/// four rows off the position the run recorded when it stocked, and that is the same derivation
/// <c>SHOP_BUY</c> charges against. A screen that drew its own would be a second copy of derived
/// data, and the failure that produces is a shop showing one thing and charging for another.
/// </para>
/// <para>
/// 🔒 <b>A slot that cannot be pressed says why.</b> Bought, unaffordable and an empty pool are three
/// different sentences, and a screen that greyed all three out identically would leave a player
/// unable to tell "you already have this" from "you cannot afford this".
/// </para>
/// <para>
/// 🔒 Every way this screen can come to nothing gets its own sentence: an empty shop, a screen
/// opened where no shop is, a refused departure and a host that never answered all look identical
/// on a page made entirely of text.
/// </para>
/// </remarks>
public sealed class ShopPresenter
{
    private const string TitleNameKey = "loc.shop.title.name";
    private const string LeaveActionKey = "loc.shop.leave.action";
    private const string BuyActionKey = "loc.shop.buy.action";
    private const string RefreshActionKey = "loc.shop.refresh.action";
    private const string GoldLabelKey = "loc.shop.gold.label";
    private const string SoldLabelKey = "loc.shop.sold.label";
    private const string UnaffordableLabelKey = "loc.shop.unaffordable.label";
    private const string EmptySlotLabelKey = "loc.shop.empty_slot.label";
    private const string RefreshSpentBlockKey = "loc.shop.refresh_spent.block";
    private const string OfferUnavailableStatusKey = "loc.shop.offer_unavailable.status";
    private const string LoadingStatusKey = "loc.shop.loading.status";
    private const string RunMissingStatusKey = "loc.shop.run_missing.status";
    private const string NotAtAShopStatusKey = "loc.shop.not_at_a_shop.status";
    private const string ReadUnavailableStatusKey = "loc.shop.read_unavailable.status";
    private const string RefusedStatusKey = "loc.shop.refused.status";
    private const string UnaffordableStatusKey = "loc.shop.unaffordable.status";
    private const string HostUnavailableStatusKey = "loc.shop.host_unavailable.status";

    /// <summary>
    /// The localisation key each slot kind's name is drawn from, keyed on the token
    /// <c>ShopSlotRow.Kind</c> carries.
    /// </summary>
    /// <remarks>
    /// A map rather than a switch so an unrecognised token falls through to the item's own id rather
    /// than to a thrown exception: the kind vocabulary is `Core`'s and a fifth pool would reach this
    /// screen before this file learned about it.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> SlotNameKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PERK"] = "loc.shop.slot.perk.name",
            ["CONSUMABLE"] = "loc.shop.slot.consumable.name",
            ["RUN_BUFF"] = "loc.shop.slot.run_buff.name",
            ["HEAL"] = "loc.shop.slot.heal.name",
        };

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

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly PlayerId _player;
    private readonly RunId _run;
    private readonly ContentSnapshot _content;

    private IReadOnlyList<ShopSlotCard> _slots = Array.Empty<ShopSlotCard>();
    private bool _refreshOffered;
    private bool _submissionInFlight;

    /// <summary>Builds the screen over the host, the strings and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <param name="content">The loaded content set the offer's pools and prices are read from.</param>
    public ShopPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        PlayerId player,
        RunId run,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _player = player;
        _run = run;
        _content = content;
    }

    /// <summary>How far the read this screen depends on has got.</summary>
    public ShopStage Stage { get; private set; } = ShopStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The four slots the run's visit stocked, in slot order. Empty until the read lands.</summary>
    public IReadOnlyList<ShopSlotCard> Slots => _slots;

    /// <summary>
    /// Whether the screen offers a restock control — false once this visit's free refresh and the
    /// run's ad refreshes are all spent.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is a CEILING check, not a promise: the ad allowance is per run and this screen reads
    /// only the visit's own count, so a control offered here can still be refused. The refusal is
    /// reported like any other rather than pre-empted — pre-empting it would mean this screen
    /// keeping a second copy of the run's ad-use tally.
    /// </remarks>
    public bool RefreshOffered => _refreshOffered;

    /// <summary>The buy control's caption, resolved.</summary>
    public string BuyText => _strings.Resolve(BuyActionKey);

    /// <summary>The restock control's caption, resolved.</summary>
    public string RefreshText => _strings.Resolve(RefreshActionKey);

    /// <summary>The Gold label, resolved.</summary>
    public string GoldLabel => _strings.Resolve(GoldLabelKey);

    /// <summary>The run-local Gold the prices are against.</summary>
    public long Gold { get; private set; }

    /// <summary>The named reason the shop cannot be restocked again, resolved — empty while it can.</summary>
    public string RefreshSpentText =>
        _refreshOffered ? NothingLeftToSay : _strings.Resolve(RefreshSpentBlockKey);

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The leave control's caption, resolved.</summary>
    public string LeaveText => _strings.Resolve(LeaveActionKey);

    /// <summary>
    /// Whether the offer could be projected at all.
    /// </summary>
    /// <remarks>
    /// 🔒 False says the run IS at a stocked shop and the content set could not answer what it
    /// sells — a different fact from "there is no shop here", and the campfire screen's shrine arm
    /// draws the same distinction for the same reason. The screen keeps its departure either way:
    /// a player must never be held on a tile by a content read.
    /// </remarks>
    public bool OfferAvailable { get; private set; } = true;

    /// <summary>The line saying what the screen is doing while it is not yet an answer, resolved.</summary>
    public string StatusText => Stage switch
    {
        ShopStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        ShopStage.Ready when !OfferAvailable => _strings.Resolve(OfferUnavailableStatusKey),
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
        : RulesRejection switch
        {
            null => NothingLeftToSay,

            // Named apart from every other refusal, because it is the one the player can DO
            // something about — sell nothing, fight on, come back richer.
            RejectionReason.INSUFFICIENT_FUNDS => _strings.Resolve(UnaffordableStatusKey),
            _ => _strings.Resolve(RefusedStatusKey),
        };

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

    /// <summary>Buys one slot of the offer.</summary>
    /// <param name="slotIndex">The slot's index, as <see cref="Slots"/> reports it.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// A slot this screen has already drawn as unbuyable is refused here rather than submitted: the
    /// rules layer would refuse it too, but sending it would put a rejection on the status line for
    /// a control the screen itself had said was not available.
    /// </remarks>
    public async Task<ShopSubmission> BuyAsync(int slotIndex, CancellationToken ct)
    {
        if (Stage != ShopStage.Ready || SlotAt(slotIndex) is not { Buyable: true })
        {
            return ShopSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ShopBuyCommand(slotIndex), ct).ConfigureAwait(false);
    }

    /// <summary>Restocks the offer, under `03` §7's refresh economy.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<ShopSubmission> RefreshAsync(CancellationToken ct)
    {
        if (Stage != ShopStage.Ready || !_refreshOffered)
        {
            return ShopSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ShopRefreshCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Submits <c>SHOP_LEAVE</c> — the shop's clearing step, and the only way off this tile.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// 🔒 <c>RESOLVE_TILE</c> used to be what this sent, back when it cleared the tile. It now STOCKS
    /// the offer instead, so sending it here would refuse (the shop is already open) and the run
    /// would sit on the tile with no way off it.
    /// </remarks>
    public async Task<ShopSubmission> LeaveAsync(CancellationToken ct)
    {
        if (Stage != ShopStage.Ready)
        {
            return ShopSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ShopLeaveCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>The slot at that index, or <c>null</c> when the offer has no such slot.</summary>
    private ShopSlotCard? SlotAt(int slotIndex) =>
        slotIndex >= 0 && slotIndex < _slots.Count ? _slots[slotIndex] : null;

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

    private void Carry(RunSnapshot run)
    {
        if (run.PendingTileKind != ShopTileKind)
        {
            Stage = ShopStage.NotAtAShop;
            _slots = Array.Empty<ShopSlotCard>();
            _refreshOffered = false;

            return;
        }

        Stage = ShopStage.Ready;
        Gold = run.Gold;
        OfferAvailable = true;

        ShopView? view;

        try
        {
            // A shop tile with no offer yet is a visit whose RESOLVE_TILE has not landed — the board
            // sends it before handing over, so a null here is a momentary state rather than a broken
            // one, and an empty slot list is the honest thing to draw for it.
            view = ShopView.Project(run, _content);
        }
        catch (ContentException)
        {
            // 🔒 A content set that cannot answer what this shop sells is NAMED, not thrown: a
            // screen that fell over here would hold the player on the tile with no way off it,
            // which is the exact failure this whole change exists to prevent. Leaving stays live.
            OfferAvailable = false;
            _slots = Array.Empty<ShopSlotCard>();
            _refreshOffered = false;

            return;
        }

        _slots = view is null
            ? Array.Empty<ShopSlotCard>()
            : view.Slots.Select(Card).ToArray();

        _refreshOffered = view is not null && view.RefreshesUsedThisVisit < RefreshCeiling;
    }

    /// <summary>
    /// The most refreshes one visit can spend: `03` §7's one free plus the run's two ad-gated.
    /// </summary>
    /// <remarks>
    /// ⚠️ A CEILING for the control's visibility, not the rule. The ad half is counted per RUN and
    /// this screen reads only the visit's count, so the control can be offered and then refused —
    /// see <see cref="RefreshOffered"/>. Enforcing it properly here would mean mirroring the run's
    /// ad-use tally onto this screen, which is a second copy of a number the rules layer owns.
    /// </remarks>
    private const int RefreshCeiling = 3;

    /// <summary>One projected row, as the screen draws it.</summary>
    private ShopSlotCard Card(ShopSlotRow row) =>
        new(
            row.SlotIndex,
            NameOf(row),
            row.Price,
            Buyable: row.ItemId is not null || IsHeal(row),
            Blocked: BlockedReason(row));

    /// <summary>
    /// The Heal slot is the one row that legitimately carries no item id — `03` §7 sells a fixed
    /// effect there rather than a catalogue row.
    /// </summary>
    private static bool IsHeal(ShopSlotRow row) =>
        string.Equals(row.Kind, "HEAL", StringComparison.Ordinal);

    /// <summary>The slot's name: its pool's, or the item's own id where the pool has no name for it.</summary>
    private string NameOf(ShopSlotRow row) =>
        SlotNameKeys.TryGetValue(row.Kind, out var key)
            ? _strings.Resolve(key)
            : row.ItemId ?? row.Kind;

    /// <summary>Why this slot cannot be pressed, or empty when it can.</summary>
    /// <remarks>
    /// Ordered: an empty pool first, because a slot with nothing in it is neither bought nor
    /// unaffordable and reporting it as either would be wrong rather than merely unhelpful.
    /// </remarks>
    private string BlockedReason(ShopSlotRow row)
    {
        if (row.ItemId is null && !IsHeal(row))
        {
            return _strings.Resolve(EmptySlotLabelKey);
        }

        if (row.Purchased)
        {
            return _strings.Resolve(SoldLabelKey);
        }

        return row.Affordable ? NothingLeftToSay : _strings.Resolve(UnaffordableLabelKey);
    }
}
