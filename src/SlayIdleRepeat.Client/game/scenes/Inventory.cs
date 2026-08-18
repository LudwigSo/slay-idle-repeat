using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S16 — the gear stock and the equip path: a driving adapter over <see cref="InventoryPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press per card. No rules, no ports, no
/// adapters, and no decision about what may be equipped: which cards are live is the presenter's
/// answer and this half only draws it. That split is load-bearing rather than stylistic — there is no
/// scene test harness in this repository, so anything decided here is decided where nothing can check
/// it.
/// </para>
/// <para>
/// 🔒 <b>Every number on a card comes off <c>InventoryView</c>, and this file computes none of them.</b>
/// <c>08</c> §5's per-stat delta is the projection's, from the same derivation the hero's stat block
/// uses. What this half decides is only how a sign is drawn: a gain green, a loss red, and the arrow
/// is a typed character because no atlas ships with this build.
/// </para>
/// <para>
/// 🔒 <b>The arrow is never the only channel.</b> The delta's sign is carried by the arrow glyph, by
/// the leading <c>+</c> or <c>−</c> on the number itself, and by colour — three channels, so a player
/// who reads none of the hues apart still sees which way a stat moves. Colour alone on a
/// green/red pair is the single most common accessibility failure there is.
/// </para>
/// <para>
/// 🔴 <b>Percent and flat stats are formatted differently, because they are different quantities.</b>
/// <c>GearStatDeltaView.IsPercent</c> says which, and a screen that drew both the same way would show
/// a number nothing in the game computes — a 0.05 crit fraction rendered beside a 120 attack figure as
/// though the two were comparable.
/// </para>
/// <para>
/// ⚠️ Every type size, colour, corner and outline in the three <c>Inventory*.tscn</c> files is a
/// per-node override, because the shared theme resource does not exist yet — it is M8-03's, and these
/// overrides are debt owed to it rather than a naming scheme of this screen's own. The primary
/// button's four-state set is the same one M7-07's three screens carry, which means M8-03 now has four
/// copies of one button to reconcile rather than four unrelated buttons.
/// </para>
/// </remarks>
public partial class Inventory : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Inventory.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string InventoryMarker = "SIR_INVENTORY_READY";

    private const string ItemCardScenePath = "res://game/scenes/InventoryItemCard.tscn";
    private const string DeltaRowScenePath = "res://game/scenes/InventoryDeltaRow.tscn";

    private const string CardRarityGemPath = "Body/Column/HeaderRow/RarityGem";
    private const string CardRarityGlyphPath = "Body/Column/HeaderRow/RarityGem/RarityGlyph";
    private const string CardNameLabelPath = "Body/Column/HeaderRow/NameLabel";
    private const string CardEquippedBadgePath = "Body/Column/HeaderRow/EquippedBadge";
    private const string CardDeltaColumnPath = "Body/Column/DeltaColumn";
    private const string CardBlockLabelPath = "Body/Column/BlockLabel";
    private const string CardEquipButtonPath = "Body/Column/EquipButton";

    private const string RowStatLabelPath = "StatLabel";
    private const string RowDeltaLabelPath = "DeltaLabel";

    private const string SafeAreaPath = "%SafeArea";
    private const string TitleLabelPath = "%TitleLabel";
    private const string CapacityLabelPath = "%CapacityLabel";
    private const string CapacityValuePath = "%CapacityValue";
    private const string ItemColumnPath = "%ItemColumn";
    private const string HeldLabelPath = "%HeldLabel";
    private const string HeldColumnPath = "%HeldColumn";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string CloseButtonPath = "%CloseButton";

    /// <summary>The theme entry a label's own text colour is written into.</summary>
    private const string FontColourOverride = "font_color";

    /// <summary>The panel entry a card's or a gem's whole face is written into.</summary>
    private const string PanelStyleOverride = "panel";

    /// <summary>What a stat this item improves is drawn in.</summary>
    private static readonly Color GainColour = new(0.45f, 0.83f, 0.5f);

    /// <summary>And one it worsens.</summary>
    private static readonly Color LossColour = new(0.94f, 0.47f, 0.43f);

    /// <summary>A stat neither side moves — drawn quiet rather than as a colour with a meaning.</summary>
    private static readonly Color LevelColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>The ink a rarity letter is drawn in — the screen's own ground rather than black.</summary>
    private static readonly Color GemInkColour = new(0.07f, 0.07f, 0.09f);

    /// <summary>What a band this build was never taught is marked with.</summary>
    private static readonly Color UnknownMarkColour = new(0.36f, 0.38f, 0.45f);

    private InventoryPresenter? _presenter;
    private Control? _returnTo;
    private CancellationToken _lifetime;

    private Label? _titleLabel;
    private Label? _capacityLabel;
    private Label? _capacityValue;
    private VBoxContainer? _itemColumn;
    private Label? _heldLabel;
    private VBoxContainer? _heldColumn;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Button? _closeButton;

    private IReadOnlyList<InventoryItemView>? _drawnStoredFrom;
    private IReadOnlyList<InventoryItemView>? _drawnHeldFrom;
    private readonly List<Button> _equipButtons = [];
    private bool _busy;

    /// <summary>Binds the screen to its driver and the screen it returns to.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="returnTo">The screen shown again when this one closes.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Drive(InventoryPresenter presenter, Control returnTo, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(returnTo);

        _presenter = presenter;
        _returnTo = returnTo;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _capacityLabel = GetNode<Label>(CapacityLabelPath);
        _capacityValue = GetNode<Label>(CapacityValuePath);
        _itemColumn = GetNode<VBoxContainer>(ItemColumnPath);
        _heldLabel = GetNode<Label>(HeldLabelPath);
        _heldColumn = GetNode<VBoxContainer>(HeldColumnPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _closeButton = GetNode<Button>(CloseButtonPath);

        _closeButton.Pressed += OnClosePressed;

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

        _ = StartAsync();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _closeButton is not null && IsInstanceValid(_closeButton))
        {
            _closeButton.Pressed -= OnClosePressed;
        }

        base.Dispose(disposing);
    }

    private async Task StartAsync()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        await presenter.StartAsync(_lifetime).ConfigureAwait(true);

        Render();
    }

    private void Render()
    {
        // Every node this writes to is checked, not just the one: a scene-unique name that no longer
        // resolves leaves a null behind, and a null-forgiving operator over it would turn a renamed node
        // into a crash here instead of a blank label.
        if (_presenter is not { } presenter || !IsInstanceValid(this) || !IsInsideTree() ||
            _titleLabel is null || _capacityLabel is null || _capacityValue is null ||
            _itemColumn is null || _heldLabel is null || _heldColumn is null ||
            _statusLabel is null || _rejectionLabel is null || _closeButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;
        _capacityLabel.Text = presenter.CapacityLabel;
        _capacityValue.Text = presenter.CapacityValue;

        RenderItems(presenter);

        _heldLabel.Text = presenter.HeldLabel;
        _heldLabel.Visible = _heldLabel.Text.Length > 0;

        // Hidden rather than blanked once it has nothing to say: an empty label still claims a full line
        // of height, so a blank one is a sentence a player can see room for and cannot read. Both sit
        // OUTSIDE the scrolling band — a refusal a scroll position can hide is the same silence as never
        // printing it.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;

        _closeButton.Text = presenter.CloseText;
        _closeButton.Disabled = _busy;

        GD.Print(InventoryMarker);
    }

    /// <summary>Draws both bands, rebuilding a band only when its own list has changed.</summary>
    private void RenderItems(InventoryPresenter presenter)
    {
        if (_itemColumn is not { } stored || _heldColumn is not { } held)
        {
            return;
        }

        if (!ReferenceEquals(_drawnStoredFrom, presenter.Stored) ||
            !ReferenceEquals(_drawnHeldFrom, presenter.Held))
        {
            _equipButtons.Clear();

            Build(stored, presenter, presenter.Stored, equippable: true);
            Build(held, presenter, presenter.Held, equippable: false);

            _drawnStoredFrom = presenter.Stored;
            _drawnHeldFrom = presenter.Held;
        }

        foreach (var button in _equipButtons)
        {
            if (IsInstanceValid(button))
            {
                button.Disabled = _busy;
            }
        }
    }

    private void Build(
        VBoxContainer column,
        InventoryPresenter presenter,
        IReadOnlyList<InventoryItemView> items,
        bool equippable)
    {
        Clear(column);

        if (GD.Load<PackedScene>(ItemCardScenePath) is not { } cardScene ||
            GD.Load<PackedScene>(DeltaRowScenePath) is not { } rowScene)
        {
            GD.PushError(
                "The inventory card or delta-row scene did not load, so the stock is not drawn. A " +
                "player cannot equip what they cannot see, so this is a defect rather than a degradation.");

            return;
        }

        foreach (var item in items)
        {
            column.AddChild(BuildCard(cardScene, rowScene, presenter, item, equippable));
        }
    }

    private PanelContainer BuildCard(
        PackedScene cardScene,
        PackedScene rowScene,
        InventoryPresenter presenter,
        InventoryItemView item,
        bool equippable)
    {
        var card = cardScene.Instantiate<PanelContainer>();

        DrawRarity(
            card.GetNode<PanelContainer>(CardRarityGemPath),
            card.GetNode<Label>(CardRarityGlyphPath),
            item.Rarity);

        // 🔴 The FAMILY and the enhancement, not a name: `15` §E11 delegates gear naming to `08` §1's
        // six slots × four families and no document authors a per-item display name, so there is nothing
        // to resolve. Drawn as the authored family id plus the +N the forge reached, which is honest and
        // is what M8-03's kit will replace with real wording rather than with a different id.
        card.GetNode<Label>(CardNameLabelPath).Text = Describe(item);

        var badge = card.GetNode<Label>(CardEquippedBadgePath);

        badge.Text = item.IsEquipped ? presenter.EquippedBadge : string.Empty;
        badge.Visible = item.IsEquipped;

        DrawDeltas(card.GetNode<VBoxContainer>(CardDeltaColumnPath), rowScene, item);

        var block = card.GetNode<Label>(CardBlockLabelPath);

        // 🔒 The sentence goes on the card that cannot be equipped, not into a status line: a player
        // reading why has the item in front of them, and EQUIP would answer INVENTORY_FULL for it.
        block.Text = equippable ? string.Empty : presenter.HeldNotEquippableText;
        block.Visible = !equippable;

        var equip = card.GetNode<Button>(CardEquipButtonPath);

        equip.Text = presenter.EquipText;
        ButtonTextColours.ApplyTo(equip, LiveColour, UnavailableColour);

        // The worn item offers no equip either: it is already on, and a control that submitted a command
        // to change nothing would spend a round trip to report success at doing so.
        equip.Visible = equippable && !item.IsEquipped;
        equip.Disabled = !equip.Visible || _busy;

        if (equip.Visible)
        {
            // Captured by value, so the item a press equips is the item the card was drawn for even
            // after the list is rebuilt beneath it.
            var pressed = item;

            equip.Pressed += () => OnEquipPressed(pressed);

            _equipButtons.Add(equip);
        }

        return card;
    }

    /// <summary>Draws one row per stat the comparison answered for.</summary>
    private static void DrawDeltas(
        VBoxContainer column, PackedScene rowScene, InventoryItemView item)
    {
        Clear(column);

        foreach (var delta in item.Deltas)
        {
            var row = rowScene.Instantiate<HBoxContainer>();

            row.GetNode<Label>(RowStatLabelPath).Text = delta.Stat;

            var value = row.GetNode<Label>(RowDeltaLabelPath);

            value.Text = Format(delta);
            value.AddThemeColorOverride(FontColourOverride, Ink(delta.Delta));

            column.AddChild(row);
        }
    }

    /// <summary>
    /// One delta as a player reads it: an arrow, a sign and a magnitude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Three channels for one fact.</b> The arrow, the sign and the colour all say which way the
    /// stat moves, so a player who reads none of the hues apart loses nothing. Colour alone on a
    /// green/red pair is the most common accessibility failure there is, and it costs two characters to
    /// avoid.
    /// </para>
    /// <para>
    /// 🔴 <b>Percent and flat are formatted differently because they are different quantities.</b> A
    /// crit fraction of 0.05 and an attack figure of 120 are not comparable, and drawing both as bare
    /// numbers would invite exactly that comparison.
    /// </para>
    /// </remarks>
    private static string Format(GearStatDeltaView delta)
    {
        var arrow = delta.Delta > 0 ? "↑" : delta.Delta < 0 ? "↓" : "–";

        var magnitude = delta.IsPercent
            ? Math.Abs(delta.Delta * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%"
            : Math.Abs(delta.Delta).ToString("0.#", CultureInfo.InvariantCulture);

        var sign = delta.Delta > 0 ? "+" : delta.Delta < 0 ? "−" : string.Empty;

        return arrow + " " + sign + magnitude;
    }

    private static Color Ink(double delta) =>
        delta > 0 ? GainColour : delta < 0 ? LossColour : LevelColour;

    /// <summary>What a card calls its item, in the absence of any authored name.</summary>
    private static string Describe(InventoryItemView item) =>
        item.EnhanceLevel == 0
            ? item.Family.ToString()
            : item.Family + " +" + item.EnhanceLevel.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gives the gem its band's fill, its frame shape and its letter, all three at once.</summary>
    /// <remarks>
    /// 🔒 The same treatment the perk draft's gem takes, for the same reason and with the same values —
    /// rarity is never carried by colour alone, and a player moving between the two screens should not
    /// have to learn the band twice. ⚠️ Duplicated rather than shared because the two screens have no
    /// common component yet; M8-03's kit is where one belongs, and this is a fourth entry on the debt it
    /// already owes.
    /// </remarks>
    private static void DrawRarity(PanelContainer gem, Label glyph, Rarity rarity)
    {
        var (fill, symbol, corners) = Band(rarity);

        glyph.Text = symbol;
        glyph.AddThemeColorOverride(FontColourOverride, GemInkColour);

        if (gem.GetThemeStylebox(PanelStyleOverride) is not StyleBoxFlat face ||
            face.Duplicate() is not StyleBoxFlat band)
        {
            GD.PushError("An inventory gem has no flat face to shape, so a rarity keeps the default frame.");

            return;
        }

        band.BgColor = fill;
        band.CornerRadiusTopLeft = corners.X;
        band.CornerRadiusTopRight = corners.Y;
        band.CornerRadiusBottomRight = corners.Z;
        band.CornerRadiusBottomLeft = corners.W;

        gem.AddThemeStyleboxOverride(PanelStyleOverride, band);
    }

    /// <remarks>
    /// 🔒 Five bands here, against the perk draft's four: gear uses the whole authored ladder including
    /// SS, which is the band a perk cannot have. The four they share carry identical values.
    /// </remarks>
    private static (Color Fill, string Symbol, Vector4I Corners) Band(Rarity rarity) => rarity switch
    {
        Rarity.C => (new Color(0.6039f, 0.6471f, 0.6941f), "C", new Vector4I(0, 0, 0, 0)),
        Rarity.B => (new Color(0.298f, 0.6863f, 0.3137f), "B", new Vector4I(36, 36, 36, 36)),
        Rarity.A => (new Color(0.2314f, 0.5098f, 0.9647f), "A", new Vector4I(34, 0, 34, 0)),
        Rarity.S => (new Color(0.9608f, 0.651f, 0.1373f), "S", new Vector4I(0, 0, 34, 34)),
        Rarity.SS => (new Color(0.7569f, 0.2314f, 0.9098f), "SS", new Vector4I(18, 36, 18, 36)),
        _ => (UnknownMarkColour, "?", new Vector4I(18, 18, 18, 18)),
    };

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame are
    /// both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt column
    /// from briefly drawing two sets of cards over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnEquipPressed(InventoryItemView item) =>
        _ = SubmitAsync(presenter => presenter.EquipAsync(item, _lifetime));

    private async Task SubmitAsync(Func<InventoryPresenter, Task<InventorySubmission>> submit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;

        try
        {
            Render();

            _ = await submit(presenter).ConfigureAwait(true);
        }
        finally
        {
            _busy = false;
        }

        Render();
    }

    private void OnClosePressed()
    {
        if (_busy || _returnTo is null)
        {
            return;
        }

        InventoryHandover.Return(this, _returnTo);
    }
}
