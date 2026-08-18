using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// `08` §5 — the Inventory screen (S16): the stock, the side-by-side delta, and the one command it
/// submits.
/// </summary>
/// <remarks>
/// 🔴 This is the screen M7's exit criterion leans on for its last verb — *"gear banked <b>and
/// equipped</b> between runs"* — so the cases that matter are the ones about what it will and will not
/// submit. Two states carry no comparison and each does so for its own reason, and one of those two
/// is refused before it reaches the host at all.
/// </remarks>
public sealed class InventoryPresenterTests
{
    private static readonly PlayerId Player = new("PLAYER_inv_7f31");

    // ---- what the screen submits, and what it refuses on its own -------------------------------

    /// <summary>
    /// 🔒 <b>An item held in overflow is never submitted, and the refusal happens HERE.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `08` §5 holds a drop arriving at a full stock rather than refusing it, and <c>EQUIP</c> answers
    /// <c>INVENTORY_FULL</c> for one — so the round trip is knowable in advance. Spending it anyway
    /// would teach the player that the sentence on the card is not to be trusted, which is the opposite
    /// of what naming the absence is for. Asserted on the HOST's call count, because "refused here"
    /// is a claim about a call that must not happen.
    /// </remarks>
    [Fact]
    public async Task An_item_held_in_overflow_is_refused_without_reaching_the_host()
    {
        var host = RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null);
        var presenter = Build(host);

        await presenter.StartAsync(CancellationToken.None);

        presenter.Held.ShouldNotBeEmpty("the premise: this stock is holding something in overflow.");

        var before = host.SubmitCallCount;
        var outcome = await presenter.EquipAsync(presenter.Held[0], CancellationToken.None);

        outcome.ShouldBe(InventorySubmission.RefusedNotAvailable);
        host.SubmitCallCount.ShouldBe(
            before,
            "an item in overflow was sent to the host. EQUIP refuses it with INVENTORY_FULL, and the " +
            "card already carries that sentence — spending the round trip teaches the player the card " +
            "is not to be trusted.");
    }

    /// <summary>…and a null item is a caller defect rather than a refusal.</summary>
    [Fact]
    public async Task Equipping_nothing_is_a_defect()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        await Should.ThrowAsync<ArgumentNullException>(
            () => presenter.EquipAsync(null!, CancellationToken.None));
    }

    // ---- the read's states ---------------------------------------------------------------------

    /// <summary>An empty stock is its own state, with a sentence rather than a blank grid.</summary>
    /// <remarks>
    /// 🔒 Named rather than folded into <c>Ready</c>: a fresh account really does own nothing, and a
    /// grid with no cards and no explanation is indistinguishable from one that failed to render.
    /// </remarks>
    [Fact]
    public async Task An_empty_stock_says_so()
    {
        var presenter = Build(RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null));

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(InventoryStage.Empty);
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace(
            "an empty grid with no sentence reads as a screen that failed to draw.");
    }

    /// <summary>A read that found no player leaves the screen saying the stock could not be read.</summary>
    [Fact]
    public async Task A_read_that_answers_nothing_is_a_state_rather_than_an_escape()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        await presenter.StartAsync(CancellationToken.None);

        presenter.Stage.ShouldBe(InventoryStage.ReadUnavailable);
        presenter.Stored.ShouldBeEmpty();
        presenter.Held.ShouldBeEmpty();
        presenter.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>Building the screen reads nothing and submits nothing.</summary>
    /// <remarks>
    /// A read in a constructor can only throw; the read belongs to <c>StartAsync</c>, where a failure
    /// has somewhere to be reported.
    /// </remarks>
    [Fact]
    public void Building_the_screen_reaches_the_host_in_no_way_at_all()
    {
        var host = RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null);

        _ = Build(host);

        host.ReadCallCount.ShouldBe(0);
        host.SubmitCallCount.ShouldBe(0);
    }

    // ---- the captions --------------------------------------------------------------------------

    /// <summary>
    /// 🔒 The bag's occupancy counts the held items in, and shows the authored ceiling beside it.
    /// </summary>
    /// <remarks>
    /// 🔴 Held items are IN the figure on purpose: `08` §5's overflow exists precisely because the bag is
    /// full, so a count that excluded them would report room the player does not have — and would do it
    /// on the one screen they would go to in order to make some.
    /// </remarks>
    [Fact]
    public async Task The_occupancy_counts_held_items_and_names_the_ceiling()
    {
        var presenter = Build(RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null));

        await presenter.StartAsync(CancellationToken.None);

        var occupied = presenter.Stored.Count + presenter.Held.Count;

        presenter.CapacityValue.ShouldBe(
            occupied.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            presenter.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the occupancy has to count what the bag is holding as well as what is in it, and it has " +
            "to name the ceiling it is measured against.");
    }

    /// <summary>The overflow heading appears only when something is held.</summary>
    [Fact]
    public async Task The_overflow_heading_appears_only_when_something_is_held()
    {
        var empty = Build(RecordingGameHost.Finding(PlayerState.WithEmptyStock(Player), null));

        await empty.StartAsync(CancellationToken.None);

        empty.HeldLabel.ShouldBeEmpty("nothing is held, so a heading over nothing would be a lie.");

        var overflowing = Build(
            RecordingGameHost.Finding(PlayerState.WithOverflowingStock(Player), null));

        await overflowing.StartAsync(CancellationToken.None);

        overflowing.HeldLabel.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>Every caption resolves rather than falling through to its own key.</summary>
    [Fact]
    public void Every_caption_resolves()
    {
        var presenter = Build(RecordingGameHost.FindingNoSuchPlayer());

        foreach (var caption in new[]
        {
            presenter.Title,
            presenter.CapacityLabel,
            presenter.EquippedBadge,
            presenter.EquipText,
            presenter.CloseText,
            presenter.HeldNotEquippableText,
        })
        {
            caption.ShouldNotBeNullOrWhiteSpace();
            caption.StartsWith("loc.", StringComparison.Ordinal).ShouldBeFalse(
                "a caption fell through to its own key, so the string set does not carry it: " + caption);
        }
    }

    /// <summary>
    /// 🔒 No member of this screen reports a way to buy bag space.
    /// </summary>
    /// <remarks>
    /// 🔴 Stated over the type's surface rather than over one value, because the failure guarded against
    /// is a member being ADDED. `08` §5's errata makes capacity a flat 1000 and the ruling that set it
    /// declined to add <c>EXPAND_INVENTORY</c> to `14` §2.3's vocabulary, so a control offering to
    /// expand the bag would be offering a command that does not exist.
    /// </remarks>
    [Fact]
    public void No_member_of_this_screen_offers_to_buy_bag_space()
    {
        var offenders =
            from property in typeof(InventoryPresenter).GetProperties()
            where property.Name.Contains("Expand", StringComparison.Ordinal) ||
                  property.Name.Contains("Purchase", StringComparison.Ordinal) ||
                  property.Name.Contains("Upgrade", StringComparison.Ordinal)
            select property.Name;

        offenders.ShouldBeEmpty(
            "a member of this screen reports bag expansion. No command can move the ceiling — 08 §5's " +
            "errata made capacity flat and EXPAND_INVENTORY was explicitly not added to the vocabulary " +
            "— so whatever this member offers, nothing can deliver it.");
    }

    private static InventoryPresenter Build(RecordingGameHost host)
    {
        var strings = RunDecisionContent.Strings();

        return new InventoryPresenter(
            host, RunDecisionContent.Catalogue(strings), BootContent.Shipped, Player);
    }
}
