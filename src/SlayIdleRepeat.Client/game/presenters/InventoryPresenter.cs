using System.Globalization;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Inventory screen has got with the read everything it draws depends on.</summary>
public enum InventoryStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The stock was read and holds something.</summary>
    Ready = 2,

    /// <summary>
    /// The stock was read and is empty. Named rather than folded into <see cref="Ready"/>: a fresh
    /// account really does own nothing, and a blank grid with no sentence reads as a rendering fault.
    /// </summary>
    Empty = 3,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 4,
}

/// <summary>What one submission from this screen did.</summary>
public enum InventorySubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules refused it.</summary>
    Rejected = 3,

    /// <summary>The host never answered.</summary>
    HostUnavailable = 4,
}

/// <summary>
/// S16 — the gear stock and the equip path: what the player owns, what wearing each item would
/// change, and the one command this screen submits.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every number on this screen comes from <c>InventoryView</c>, and none is computed here.</b>
/// <c>08</c> §5's *"side-by-side delta vs the currently equipped item in that slot, with green/red
/// arrows per stat"* is the projection's answer, from the same derivation the hero's own stat block
/// comes from. A subtraction on this side would be a second answer to "what does this item give", and
/// the two would disagree the first time either moved — with the player believing the screen.
/// </para>
/// <para>
/// 🔒 <b>The arrows are the SIGN of a delta, not wording, so no locale key names one.</b> What the
/// locale table carries is captions; the magnitudes and their signs are read at render time. That is
/// also why the bag's occupancy is two numbers beside a caption rather than a sentence: <c>08</c> §5's
/// ceiling is a 📐 and a ceiling written into a translated string could not be retuned.
/// </para>
/// <para>
/// 🔴 <b>Sorting, filtering, locking and the Forge are not here, and nothing pretends otherwise.</b>
/// They are M9-01's. This screen is scoped to the path M7's exit criterion names — carrying M4-03's
/// loot into the next run — which is a grid, a comparison and an equip.
/// </para>
/// <para>
/// 🔴 <b>An item held in overflow is shown and cannot be equipped, and the card says which.</b>
/// <c>08</c> §5 holds a drop that arrives at a full stock rather than refusing it, so hiding those
/// items would hide exactly what a player needs in order to make room. <c>EQUIP</c> refuses one with
/// <c>INVENTORY_FULL</c>, so the reason is on the card rather than arriving as a surprise after a tap.
/// </para>
/// </remarks>
public sealed class InventoryPresenter
{
    /// <summary>
    /// ⚠️ Deliberately absent, and named so it can be found. There is no control here for buying bag
    /// space, and there is nothing this screen could submit if there were.
    /// </summary>
    private const string ThereIsNoWayToBuyBagSpace =
        "08 §5's errata makes inventory capacity a flat 1000 equal to the base, retiring 10 §4's " +
        "+20-per-purchase Crown ladder and 10 §2's flat 400-Soul-Shard alternative — both stay " +
        "authored, priced and UNSPENDABLE. The same product ruling explicitly declined to add " +
        "EXPAND_INVENTORY to 14 §2.3's command vocabulary, so nothing in the game can move the " +
        "ceiling. A screen offering to expand the bag would be offering a command that does not " +
        "exist, and one showing the retired 320 would be drawing a limit the game does not have.";

    private const string TitleNameKey = "loc.inventory.title.name";
    private const string CapacityLabelKey = "loc.inventory.capacity.label";
    private const string HeldLabelKey = "loc.inventory.held.label";
    private const string EquippedBadgeKey = "loc.inventory.equipped.badge";
    private const string EquipActionKey = "loc.inventory.equip.action";
    private const string CloseActionKey = "loc.inventory.close.action";
    private const string HeldNotEquippableBlockKey = "loc.inventory.held_not_equippable.block";
    private const string LoadingStatusKey = "loc.inventory.loading.status";
    private const string EmptyStatusKey = "loc.inventory.empty.status";
    private const string UnavailableStatusKey = "loc.inventory.unavailable.status";
    private const string RefusedStatusKey = "loc.inventory.refused.status";
    private const string HostUnavailableStatusKey = "loc.inventory.host_unavailable.status";

    /// <summary>What a line with nothing to say answers.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>Separates what the bag holds from what it can hold, as the board separates HP.</summary>
    private const char OverSeparator = '/';

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;

    private bool _submissionInFlight;

    /// <summary>Builds the screen's driver.</summary>
    /// <param name="gameHost">The host every read and submission goes through.</param>
    /// <param name="strings">The device-locale string catalogue.</param>
    /// <param name="content">The loaded content set the projection reads against.</param>
    /// <param name="player">The profile whose stock this is.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public InventoryPresenter(
        IGameHost gameHost, LocaleStringCatalogue strings, ContentSnapshot content, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
    }

    /// <summary>How far the read has got.</summary>
    public InventoryStage Stage { get; private set; } = InventoryStage.NotYetRead;

    /// <summary>What the rules said about the last submission, or <c>null</c> if they said nothing.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission never reached the host at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The items in the bag proper, each with what wearing it would change.</summary>
    public IReadOnlyList<InventoryItemView> Stored { get; private set; } = [];

    /// <summary>The items a full bag is holding. Shown, never equippable.</summary>
    public IReadOnlyList<InventoryItemView> Held { get; private set; } = [];

    /// <summary>How many items the bag can hold, as authored — see <see cref="ThereIsNoWayToBuyBagSpace"/>.</summary>
    public int Capacity { get; private set; }

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The caption the bag's occupancy is drawn beside, resolved.</summary>
    public string CapacityLabel => _strings.Resolve(CapacityLabelKey);

    /// <summary>
    /// The occupancy itself — what the bag holds over what it can hold.
    /// </summary>
    /// <remarks>
    /// 🔒 Held items are counted in, and that is the honest reading: <c>08</c> §5's overflow exists
    /// because the bag is FULL, so a figure that excluded them would read as room the player does not
    /// have. It is why the number can exceed the ceiling, and why the ceiling is shown beside it.
    /// </remarks>
    public string CapacityValue =>
        Count(Stored.Count + Held.Count) + OverSeparator + Count(Capacity);

    /// <summary>The heading over the overflow band, resolved. Empty when nothing is held.</summary>
    public string HeldLabel => Held.Count == 0 ? NothingLeftToSay : _strings.Resolve(HeldLabelKey);

    /// <summary>The sentence on a held item's card saying why it cannot be worn, resolved.</summary>
    public string HeldNotEquippableText => _strings.Resolve(HeldNotEquippableBlockKey);

    /// <summary>The mark on the item worn in a slot, resolved.</summary>
    public string EquippedBadge => _strings.Resolve(EquippedBadgeKey);

    /// <summary>The equip control's caption, resolved.</summary>
    public string EquipText => _strings.Resolve(EquipActionKey);

    /// <summary>The way back, resolved.</summary>
    public string CloseText => _strings.Resolve(CloseActionKey);

    /// <summary>The one line a player reads for the state the screen is in, resolved.</summary>
    public string StatusText => Stage switch
    {
        InventoryStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        InventoryStage.Ready => NothingLeftToSay,
        InventoryStage.Empty => _strings.Resolve(EmptyStatusKey),
        _ => _strings.Resolve(UnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>Reads the stock and settles what the screen draws.</summary>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            Settle(await _gameHost.ReadOwnStateAsync(_player, null, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A read that faulted answered nothing, and a screen with nothing to draw says so rather
            // than drawing a stock it did not see.
            Stage = InventoryStage.ReadUnavailable;
            Stored = [];
            Held = [];
        }
    }

    /// <summary>Submits <c>EQUIP</c> for one owned item into its own slot.</summary>
    /// <remarks>
    /// 🔒 The slot comes off the ITEM, never from the caller. An item's slot is a fact about the item
    /// (<c>08</c> §1's six slots × four families), so a screen that passed one would be able to ask for
    /// a blade in a boot slot — which the rules layer refuses, but only after a round trip that told the
    /// player their tap was wrong when the screen had all it needed to know it never was.
    /// </remarks>
    /// <param name="item">The item to wear.</param>
    /// <param name="ct">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public async Task<InventorySubmission> EquipAsync(InventoryItemView item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Stage is not (InventoryStage.Ready or InventoryStage.Empty) || _submissionInFlight)
        {
            return InventorySubmission.RefusedNotAvailable;
        }

        // 🔒 Refused here rather than sent: an item in overflow cannot be worn, EQUIP answers
        // INVENTORY_FULL, and the card already carries that sentence. Spending a round trip to be told
        // what the screen knew is how a player learns to distrust the card.
        if (Held.Any(held => held.InstanceId == item.InstanceId))
        {
            return InventorySubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;

        try
        {
            var outcome = await _gameHost
                .SubmitAsync(_player, null, new EquipCommand(item.InstanceId, item.Slot), ct)
                .ConfigureAwait(false);

            HostFaulted = false;
            RulesRejection = outcome.Rejection;

            if (outcome.Rejection is not null)
            {
                return InventorySubmission.Rejected;
            }

            await StartAsync(ct).ConfigureAwait(false);

            return InventorySubmission.Submitted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            HostFaulted = true;
            RulesRejection = null;

            return InventorySubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a player taps again after a submission that never answered, and a
            // latch left shut strands them on the screen.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Player is not { } player)
        {
            Stage = InventoryStage.ReadUnavailable;
            Stored = [];
            Held = [];

            return;
        }

        try
        {
            var view = InventoryView.Project(player, _content);

            Stored = view.Stored;
            Held = view.Held;
            Capacity = view.Capacity;
            Stage = Stored.Count + Held.Count == 0 ? InventoryStage.Empty : InventoryStage.Ready;
        }
        catch (ContentException)
        {
            // A content set that cannot answer is a state, not an exception to leak: the read itself
            // succeeded, so the screen says the stock could not be read rather than that the player
            // could not be found.
            Stage = InventoryStage.ReadUnavailable;
            Stored = [];
            Held = [];
        }
    }

    /// <summary>
    /// A count as a player reads it. Never abbreviated: a bag holds at most four figures and the
    /// thousands rule has nothing to act on at this ceiling.
    /// </summary>
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
