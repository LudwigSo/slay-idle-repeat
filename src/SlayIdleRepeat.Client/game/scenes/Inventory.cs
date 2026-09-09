using Godot;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Inventory;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S16 — the Gear screen: the hero and the six slots, the bag, and the Forge's three operations. A
/// driving adapter over <see cref="InventoryPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards one press per control. No rules, no ports, no
/// adapters, and no decision about what may be equipped, fused or salvaged: which controls are live,
/// which copies a fusion takes and what a batch would pay are all the presenter's answers, and this
/// half only draws them. That split is load-bearing rather than stylistic — there is no scene test
/// harness in this repository, so anything decided here is decided where nothing can check it.
/// </para>
/// <para>
/// 🔒 <b>Not one colour is written here.</b> The five rarity tints, the gain and loss accents and the
/// lock amber are named entries in <c>SlayTheme.tres</c>'s <c>SlayAccents</c> palette, read back
/// through the theme by role. What this file decides about a band is its SHAPE — the corner radii a
/// tile, a cell and a gem take — so that rarity is never carried by hue alone.
/// </para>
/// <para>
/// 🔒 <b>The grid is rebuilt only when its membership changes.</b> A tap that picks an item for a
/// batch or turns a page changes the presenter's state and re-renders the screen, and a bag of a
/// thousand items rebuilt on every such tap would stutter under a thumb. The cells are kept and
/// re-marked when the same items are still in the same order, and rebuilt otherwise.
/// </para>
/// <para>
/// ⚠️ Both motions on this screen are skippable and the screen is complete without them: the notice
/// fades in unless motion is reduced, and nothing else moves at all. The sheet appears rather than
/// slides, because a slide over a 3D diorama costs a frame the mobile renderer does not have to spare
/// and says nothing the sheet's own edge does not.
/// </para>
/// </remarks>
public partial class Inventory : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Inventory.tscn";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string InventoryMarker = "SIR_INVENTORY_READY";

    /// <summary>The one line every tab this build has no screen for prints, as Home prints its own.</summary>
    private const string StubMarker = "SIR_INVENTORY_STUB";

    private const string SlotTileScenePath = "res://game/scenes/SlotTile.tscn";
    private const string GearCellScenePath = "res://game/scenes/GearCell.tscn";

    /// <summary>The tab this screen already IS. A press on it goes nowhere, by design.</summary>
    private const HomeTab ThisTab = HomeTab.Gear;

    /// <summary>How long a notice stays up before it takes itself down.</summary>
    private const double NoticeSeconds = 4.0;

    /// <summary>How long the notice takes to fade in when motion is not reduced.</summary>
    private const double NoticeFadeSeconds = 0.18;

    private const string MarginTopConstant = "margin_top";
    private const string MarginBottomConstant = "margin_bottom";
    private const string FontColourOverride = "font_color";
    private const string PanelStyle = "panel";
    private const string ModulateAlphaProperty = "modulate:a";

    /// <summary>What follows the slot's name on the filter chip: the multiplication sign, which every shipped face has.</summary>
    private const string FilterClearMark = "  ×";
    /// <summary>The two figure columns of a stat row, in canvas units — room for "1234.5" and "↑ +12.5%".</summary>
    private const float StatValueWidth = 190f;
    private const float StatDeltaWidth = 250f;

    private const string CaptionVariation = "HudCaption";
    private const string ValueVariation = "HudValueSmall";
    private const string LineVariation = "GearCaption";
    private const string ChipVariation = "ChromeButton";

    /// <summary>The four faces a button draws, each of which a band's tint is written onto.</summary>
    private static readonly StringName[] ButtonFaces = ["normal", "hover", "pressed", "disabled"];

    /// <summary>The theme roles the five bands' tints are written under.</summary>
    private const string RarityRolePrefix = "rarity_";
    private const string LossRole = "loss_accent";
    private const string LockRole = "lock_accent";

    private const string GlyphPath = "%Glyph";
    private const string CaptionPath = "%Caption";
    private const string GemPath = "%Gem";
    private const string GemGlyphPath = "%GemGlyph";
    private const string PlusPath = "%Plus";
    private const string MarkPath = "%Mark";
    private const string UpgradePath = "%Upgrade";
    private const string CheckPath = "%Check";

    private const string BandsPath = "%Bands";
    private const string TopBarPath = "%TopBar";
    private const string BackButtonPath = "%BackButton";
    private const string TitleLabelPath = "%TitleLabel";
    private const string CrownsValuePath = "%CrownsValue";
    private const string StonesValuePath = "%StonesValue";
    private const string DustValuePath = "%DustValue";
    private const string LeftSlotsPath = "%LeftSlots";
    private const string RightSlotsPath = "%RightSlots";
    private const string HeroPowerLabelPath = "%HeroPowerLabel";
    private const string NoticeLabelPath = "%NoticeLabel";
    private const string ToolbarPath = "%Toolbar";
    private const string QuickRowPath = "%QuickRow";
    private const string SortButtonPath = "%SortButton";
    private const string FilterButtonPath = "%FilterButton";
    private const string CapacityLabelPath = "%CapacityLabel";
    private const string SelectButtonPath = "%SelectButton";
    private const string GridScrollPath = "%GridScroll";
    private const string StoredGridPath = "%StoredGrid";
    private const string HeldLabelPath = "%HeldLabel";
    private const string HeldGridPath = "%HeldGrid";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string SelectBarPath = "%SelectBar";
    private const string QuickPickLabelPath = "%QuickPickLabel";
    private const string QuickPicksPath = "%QuickPicks";
    private const string SalvageSummaryPath = "%SalvageSummary";
    private const string SelectCancelButtonPath = "%SelectCancelButton";
    private const string SalvageButtonPath = "%SalvageButton";
    private const string TabMarginsPath = "%Margins";
    private const string TabBarPath = "%TabBar";
    private const string ScrimPath = "%Scrim";
    private const string SheetPath = "%Sheet";
    private const string SheetMarginsPath = "%SheetMargins";
    private const string DetailsActionsPath = "%DetailsActions";
    private const string EnhanceActionsPath = "%EnhanceActions";
    private const string MergeActionsPath = "%MergeActions";
    private const string SheetGemPath = "%SheetGem";
    private const string SheetGemGlyphPath = "%SheetGemGlyph";
    private const string SheetTitlePath = "%SheetTitle";
    private const string LockButtonPath = "%LockButton";
    private const string SheetCloseButtonPath = "%SheetCloseButton";
    private const string SheetCaptionPath = "%SheetCaption";
    private const string SheetBadgesPath = "%SheetBadges";
    private const string DetailsPagePath = "%DetailsPage";
    private const string PowerCaptionPath = "%PowerCaption";
    private const string PowerValuePath = "%PowerValue";
    private const string PowerDeltaPath = "%PowerDelta";
    private const string HeroPowerRowPath = "%HeroPowerRow";
    private const string HeroPowerCaptionPath = "%HeroPowerCaption";
    private const string HeroPowerValuePath = "%HeroPowerValue";
    private const string StatRowsPath = "%StatRows";
    private const string AffixesCaptionPath = "%AffixesCaption";
    private const string AffixRowsPath = "%AffixRows";
    private const string SetLinePath = "%SetLine";
    private const string EnhanceLinePath = "%EnhanceLine";
    private const string DetailsBlockPath = "%DetailsBlock";
    private const string EquipButtonPath = "%EquipButton";
    private const string EnhanceButtonPath = "%EnhanceButton";
    private const string MergeButtonPath = "%MergeButton";
    private const string SheetSalvageButtonPath = "%SheetSalvageButton";
    private const string EnhancePagePath = "%EnhancePage";
    private const string EnhanceTargetPath = "%EnhanceTarget";
    private const string EnhanceStatRowsPath = "%EnhanceStatRows";
    private const string EnhanceCostPath = "%EnhanceCost";
    private const string EnhanceOddsPath = "%EnhanceOdds";
    private const string EnhanceBlockPath = "%EnhanceBlock";
    private const string EnhanceBackButtonPath = "%EnhanceBackButton";
    private const string EnhanceOnceButtonPath = "%EnhanceOnceButton";
    private const string MergePagePath = "%MergePage";
    private const string MergeKeeperCaptionPath = "%MergeKeeperCaption";
    private const string MergeInputsCaptionPath = "%MergeInputsCaption";
    private const string MergeCandidatesPath = "%MergeCandidates";
    private const string MergeResultPath = "%MergeResult";
    private const string MergeCostPath = "%MergeCost";
    private const string MergeDustPath = "%MergeDust";
    private const string MergeWarningsPath = "%MergeWarnings";
    private const string MergeBackButtonPath = "%MergeBackButton";
    private const string MergeConfirmButtonPath = "%MergeConfirmButton";
    private const string ConfirmPath = "%Confirm";
    private const string ConfirmTitlePath = "%ConfirmTitle";
    private const string ConfirmSummaryPath = "%ConfirmSummary";
    private const string ConfirmWarningsPath = "%ConfirmWarnings";
    private const string ConfirmCancelButtonPath = "%ConfirmCancelButton";
    private const string ConfirmSalvageButtonPath = "%ConfirmSalvageButton";

    private InventoryPresenter? _presenter;
    private Node3D? _returnTo;
    private CancellationToken _lifetime;
    private bool _reducedMotion;

    private Control? _bands;
    private MarginContainer? _topBar;
    private Button? _backButton;
    private Label? _titleLabel;
    private Label? _crownsValue;
    private Label? _stonesValue;
    private Label? _dustValue;
    private VBoxContainer? _leftSlots;
    private VBoxContainer? _rightSlots;
    private Label? _heroPowerLabel;
    private Label? _noticeLabel;
    private Control? _toolbar;
    private Control? _quickRow;
    private Button? _sortButton;
    private Button? _filterButton;
    private Label? _capacityLabel;
    private Button? _selectButton;
    private ScrollContainer? _gridScroll;
    private GridContainer? _storedGrid;
    private Label? _heldLabel;
    private GridContainer? _heldGrid;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Control? _selectBar;
    private Label? _quickPickLabel;
    private HBoxContainer? _quickPicks;
    private Label? _salvageSummary;
    private Button? _selectCancelButton;
    private Button? _salvageButton;
    private MarginContainer? _tabMargins;
    private TabBar? _tabBar;
    private Button? _scrim;
    private Control? _sheet;
    private MarginContainer? _sheetMargins;
    private Control? _detailsActions;
    private Control? _enhanceActions;
    private Control? _mergeActions;
    private PanelContainer? _sheetGem;
    private Label? _sheetGemGlyph;
    private Label? _sheetTitle;
    private Button? _lockButton;
    private Button? _sheetCloseButton;
    private Label? _sheetCaption;
    private HBoxContainer? _sheetBadges;
    private Control? _detailsPage;
    private Label? _powerCaption;
    private Label? _powerValue;
    private Label? _powerDelta;
    private Control? _heroPowerRow;
    private Label? _heroPowerCaption;
    private Label? _heroPowerValue;
    private VBoxContainer? _statRows;
    private Label? _affixesCaption;
    private VBoxContainer? _affixRows;
    private Label? _setLine;
    private Label? _enhanceLine;
    private Label? _detailsBlock;
    private Button? _equipButton;
    private Button? _enhanceButton;
    private Button? _mergeButton;
    private Button? _sheetSalvageButton;
    private Control? _enhancePage;
    private Label? _enhanceTarget;
    private VBoxContainer? _enhanceStatRows;
    private Label? _enhanceCost;
    private Label? _enhanceOdds;
    private Label? _enhanceBlock;
    private Button? _enhanceBackButton;
    private Button? _enhanceOnceButton;
    private Control? _mergePage;
    private Label? _mergeKeeperCaption;
    private Label? _mergeInputsCaption;
    private HBoxContainer? _mergeCandidates;
    private Label? _mergeResult;
    private Label? _mergeCost;
    private Label? _mergeDust;
    private VBoxContainer? _mergeWarnings;
    private Button? _mergeBackButton;
    private Button? _mergeConfirmButton;
    private Control? _confirm;
    private Label? _confirmTitle;
    private Label? _confirmSummary;
    private VBoxContainer? _confirmWarnings;
    private Button? _confirmCancelButton;
    private Button? _confirmSalvageButton;

    private PackedScene? _slotTileScene;
    private PackedScene? _gearCellScene;

    private readonly Dictionary<GearSlot, Button> _slotTiles = [];
    private readonly List<(Button Cell, InventoryItemView Item)> _storedCells = [];
    private readonly List<(Button Cell, InventoryItemView Item)> _heldCells = [];
    private readonly List<(Button Cell, InventoryItemView Item)> _mergeCells = [];
    private readonly List<Button> _quickPickButtons = [];
    private readonly List<Button> _controls = [];

    private string _shownNotice = "";
    private int _noticeToken;
    private Tween? _noticeFade;

    /// <summary>Binds the screen to its driver and the screen it returns to.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="returnTo">The screen shown again when this one closes.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <param name="reducedMotion">Whether the notice's fade is skipped.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Drive(InventoryPresenter presenter, Node3D returnTo, CancellationToken lifetime, bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(returnTo);

        _presenter = presenter;
        _returnTo = returnTo;
        _lifetime = lifetime;
        _reducedMotion = reducedMotion;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _bands = GetNode<Control>(BandsPath);
        _topBar = GetNode<MarginContainer>(TopBarPath);
        _backButton = GetNode<Button>(BackButtonPath);
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _crownsValue = GetNode<Label>(CrownsValuePath);
        _stonesValue = GetNode<Label>(StonesValuePath);
        _dustValue = GetNode<Label>(DustValuePath);
        _leftSlots = GetNode<VBoxContainer>(LeftSlotsPath);
        _rightSlots = GetNode<VBoxContainer>(RightSlotsPath);
        _heroPowerLabel = GetNode<Label>(HeroPowerLabelPath);
        _noticeLabel = GetNode<Label>(NoticeLabelPath);
        _toolbar = GetNode<Control>(ToolbarPath);
        _quickRow = GetNode<Control>(QuickRowPath);
        _sortButton = GetNode<Button>(SortButtonPath);
        _filterButton = GetNode<Button>(FilterButtonPath);
        _capacityLabel = GetNode<Label>(CapacityLabelPath);
        _selectButton = GetNode<Button>(SelectButtonPath);
        _gridScroll = GetNode<ScrollContainer>(GridScrollPath);
        _storedGrid = GetNode<GridContainer>(StoredGridPath);
        _heldLabel = GetNode<Label>(HeldLabelPath);
        _heldGrid = GetNode<GridContainer>(HeldGridPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _selectBar = GetNode<Control>(SelectBarPath);
        _quickPickLabel = GetNode<Label>(QuickPickLabelPath);
        _quickPicks = GetNode<HBoxContainer>(QuickPicksPath);
        _salvageSummary = GetNode<Label>(SalvageSummaryPath);
        _selectCancelButton = GetNode<Button>(SelectCancelButtonPath);
        _salvageButton = GetNode<Button>(SalvageButtonPath);
        _tabMargins = GetNode<MarginContainer>(TabMarginsPath);
        _tabBar = GetNode<TabBar>(TabBarPath);
        _scrim = GetNode<Button>(ScrimPath);
        _sheet = GetNode<Control>(SheetPath);
        _sheetMargins = GetNode<MarginContainer>(SheetMarginsPath);
        _detailsActions = GetNode<Control>(DetailsActionsPath);
        _enhanceActions = GetNode<Control>(EnhanceActionsPath);
        _mergeActions = GetNode<Control>(MergeActionsPath);
        _sheetGem = GetNode<PanelContainer>(SheetGemPath);
        _sheetGemGlyph = GetNode<Label>(SheetGemGlyphPath);
        _sheetTitle = GetNode<Label>(SheetTitlePath);
        _lockButton = GetNode<Button>(LockButtonPath);
        _sheetCloseButton = GetNode<Button>(SheetCloseButtonPath);
        _sheetCaption = GetNode<Label>(SheetCaptionPath);
        _sheetBadges = GetNode<HBoxContainer>(SheetBadgesPath);
        _detailsPage = GetNode<Control>(DetailsPagePath);
        _powerCaption = GetNode<Label>(PowerCaptionPath);
        _powerValue = GetNode<Label>(PowerValuePath);
        _powerDelta = GetNode<Label>(PowerDeltaPath);
        _heroPowerRow = GetNode<Control>(HeroPowerRowPath);
        _heroPowerCaption = GetNode<Label>(HeroPowerCaptionPath);
        _heroPowerValue = GetNode<Label>(HeroPowerValuePath);
        _statRows = GetNode<VBoxContainer>(StatRowsPath);
        _affixesCaption = GetNode<Label>(AffixesCaptionPath);
        _affixRows = GetNode<VBoxContainer>(AffixRowsPath);
        _setLine = GetNode<Label>(SetLinePath);
        _enhanceLine = GetNode<Label>(EnhanceLinePath);
        _detailsBlock = GetNode<Label>(DetailsBlockPath);
        _equipButton = GetNode<Button>(EquipButtonPath);
        _enhanceButton = GetNode<Button>(EnhanceButtonPath);
        _mergeButton = GetNode<Button>(MergeButtonPath);
        _sheetSalvageButton = GetNode<Button>(SheetSalvageButtonPath);
        _enhancePage = GetNode<Control>(EnhancePagePath);
        _enhanceTarget = GetNode<Label>(EnhanceTargetPath);
        _enhanceStatRows = GetNode<VBoxContainer>(EnhanceStatRowsPath);
        _enhanceCost = GetNode<Label>(EnhanceCostPath);
        _enhanceOdds = GetNode<Label>(EnhanceOddsPath);
        _enhanceBlock = GetNode<Label>(EnhanceBlockPath);
        _enhanceBackButton = GetNode<Button>(EnhanceBackButtonPath);
        _enhanceOnceButton = GetNode<Button>(EnhanceOnceButtonPath);
        _mergePage = GetNode<Control>(MergePagePath);
        _mergeKeeperCaption = GetNode<Label>(MergeKeeperCaptionPath);
        _mergeInputsCaption = GetNode<Label>(MergeInputsCaptionPath);
        _mergeCandidates = GetNode<HBoxContainer>(MergeCandidatesPath);
        _mergeResult = GetNode<Label>(MergeResultPath);
        _mergeCost = GetNode<Label>(MergeCostPath);
        _mergeDust = GetNode<Label>(MergeDustPath);
        _mergeWarnings = GetNode<VBoxContainer>(MergeWarningsPath);
        _mergeBackButton = GetNode<Button>(MergeBackButtonPath);
        _mergeConfirmButton = GetNode<Button>(MergeConfirmButtonPath);
        _confirm = GetNode<Control>(ConfirmPath);
        _confirmTitle = GetNode<Label>(ConfirmTitlePath);
        _confirmSummary = GetNode<Label>(ConfirmSummaryPath);
        _confirmWarnings = GetNode<VBoxContainer>(ConfirmWarningsPath);
        _confirmCancelButton = GetNode<Button>(ConfirmCancelButtonPath);
        _confirmSalvageButton = GetNode<Button>(ConfirmSalvageButtonPath);

        _slotTileScene = GD.Load<PackedScene>(SlotTileScenePath);
        _gearCellScene = GD.Load<PackedScene>(GearCellScenePath);

        Wire(_backButton, OnBackPressed);
        Wire(_sortButton, OnSortPressed);
        Wire(_filterButton, OnFilterPressed);
        Wire(_selectButton, OnSelectPressed);
        Wire(_selectCancelButton, OnSelectCancelPressed);
        Wire(_salvageButton, OnSalvageRequested);
        Wire(_scrim, OnScrimPressed);
        Wire(_lockButton, OnLockPressed);
        Wire(_sheetCloseButton, OnSheetClosePressed);
        Wire(_equipButton, OnEquipPressed);
        Wire(_enhanceButton, OnEnhancePagePressed);
        Wire(_mergeButton, OnMergePagePressed);
        Wire(_sheetSalvageButton, OnSheetSalvagePressed);
        Wire(_enhanceBackButton, OnBackToDetailsPressed);
        Wire(_enhanceOnceButton, OnEnhanceOncePressed);
        Wire(_mergeBackButton, OnBackToDetailsPressed);
        Wire(_mergeConfirmButton, OnMergeConfirmPressed);
        Wire(_confirmCancelButton, OnConfirmCancelPressed);
        Wire(_confirmSalvageButton, OnConfirmSalvagePressed);

        if (_tabBar is not null)
        {
            _tabBar.TabSelected += OnTabSelected;
        }

        BuildSlotTiles();
        BuildQuickPicks();

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after — so two cameras and two overlays
        // are alive at the moment this one becomes the visible screen.
        ScreenStage.Show(this);

        ApplySafeArea();
        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_tabBar is not null && IsInstanceValid(_tabBar))
        {
            _tabBar.TabSelected -= OnTabSelected;
        }

        Withdraw();
    }

    private void Wire(Button? button, Action handler)
    {
        if (button is null)
        {
            return;
        }

        button.Pressed += handler;
        _controls.Add(button);
    }

    /// <summary>The top safe inset lands inside the top bar and the bottom one under the tab row, as on Home.</summary>
    private void ApplySafeArea()
    {
        if (SafeAreaInsets.Resolve(GetViewport().GetVisibleRect().Size) is not { } insets)
        {
            return;
        }

        _topBar?.AddThemeConstantOverride(MarginTopConstant, (int)insets.Top);
        _tabMargins?.AddThemeConstantOverride(MarginBottomConstant, (int)insets.Bottom);
        _sheetMargins?.AddThemeConstantOverride(MarginBottomConstant, (int)insets.Bottom);
    }

    private void BuildSlotTiles()
    {
        if (_presenter is not { } presenter || _slotTileScene is null || _leftSlots is null || _rightSlots is null)
        {
            return;
        }

        foreach (var slot in presenter.LeftSlots)
        {
            _leftSlots.AddChild(BuildSlotTile(slot));
        }

        foreach (var slot in presenter.RightSlots)
        {
            _rightSlots.AddChild(BuildSlotTile(slot));
        }
    }

    private Button BuildSlotTile(GearSlot slot)
    {
        var tile = _slotTileScene!.Instantiate<Button>();

        tile.GetNode<TextureRect>(GlyphPath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(slot));
        tile.GetNode<TextureRect>(MarkPath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Upgrade));

        var tapped = slot;

        tile.Pressed += () => OnSlotPressed(tapped);

        _slotTiles[slot] = tile;
        _controls.Add(tile);

        return tile;
    }

    /// <summary>The quick-pick gems: one per band the presenter offers, each a full tap target carrying the band's letter.</summary>
    private void BuildQuickPicks()
    {
        if (_presenter is not { } presenter || _quickPicks is null)
        {
            return;
        }

        foreach (var rarity in presenter.QuickPickRarities)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(144, 144),
                ThemeTypeVariation = ChipVariation,
                Text = Band(rarity).Symbol,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };

            var picked = rarity;

            button.Pressed += () => OnQuickPickPressed(picked);

            _quickPicks.AddChild(button);
            _quickPickButtons.Add(button);
            _controls.Add(button);
        }
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

    // ================================================================================ rendering

    private void Render()
    {
        if (_presenter is not { } presenter || !IsInstanceValid(this) || !IsInsideTree() || _bands is null)
        {
            return;
        }

        RenderTopBar(presenter);
        RenderSlots(presenter);
        RenderGrid(presenter);
        RenderToolbar(presenter);
        RenderSelectBar(presenter);
        RenderSheet(presenter);
        RenderConfirm(presenter);
        RenderNotice(presenter);
        RenderTabs(presenter);

        // Every control goes quiet while a command is out, so a second tap has nothing to land on.
        foreach (var control in _controls)
        {
            if (IsInstanceValid(control) && presenter.Busy)
            {
                control.Disabled = true;
            }
        }

        GD.Print(InventoryMarker);
    }

    private void RenderTopBar(InventoryPresenter presenter)
    {
        Write(_titleLabel, presenter.Title);
        Write(_backButton, presenter.CloseText);
        Write(_crownsValue, PlayerNumber.Abbreviated(presenter.Crowns));
        Write(_stonesValue, PlayerNumber.Abbreviated(presenter.EnhanceStones));
        Write(_dustValue, PlayerNumber.Abbreviated(presenter.MergeDust));

        if (_backButton is not null)
        {
            _backButton.Disabled = presenter.Busy;
        }
    }

    private void RenderSlots(InventoryPresenter presenter)
    {
        foreach (var (slot, tile) in _slotTiles)
        {
            if (!IsInstanceValid(tile))
            {
                continue;
            }

            var worn = presenter.Worn(slot);
            var gem = tile.GetNode<PanelContainer>(GemPath);
            var plus = tile.GetNode<Label>(PlusPath);
            var mark = tile.GetNode<TextureRect>(MarkPath);
            var caption = tile.GetNode<Label>(CaptionPath);

            tile.Disabled = presenter.Busy;

            if (worn is null)
            {
                ClearFace(tile);
                caption.Text = presenter.EmptySlotText;
                gem.Visible = false;
                plus.Visible = false;
            }
            else
            {
                PaintFace(tile, worn.Rarity, muted: false);
                caption.Text = presenter.FamilyName(worn.Family);
                PaintGem(gem, tile.GetNode<Label>(GemGlyphPath), worn.Rarity);
                gem.Visible = true;
                plus.Text = InventoryPresenter.EnhanceBadge(worn);
                plus.Visible = plus.Text.Length > 0;
            }

            mark.Visible = presenter.UpgradeAvailable(slot);
        }

        Write(_heroPowerLabel, presenter.HeroPower is { } power
            ? presenter.HeroPowerLabel + " " + PlayerNumber.Abbreviated((long)Math.Round(power))
            : "");
    }

    private void RenderToolbar(InventoryPresenter presenter)
    {
        Write(_sortButton, presenter.SortText);
        Write(_capacityLabel, presenter.CapacityLabel + " " + presenter.CapacityValue);
        Write(_selectButton, presenter.Mode == InventoryMode.Select ? presenter.CancelText : presenter.SelectText);

        if (_filterButton is not null)
        {
            _filterButton.Text = presenter.SlotFilterText + FilterClearMark;
            _filterButton.Visible = presenter.SlotFilter is not null;
            _filterButton.Disabled = presenter.Busy;
        }

        if (_sortButton is not null)
        {
            _sortButton.Disabled = presenter.Busy;
        }

        if (_selectButton is not null)
        {
            _selectButton.Disabled = presenter.Busy || presenter.Stage is not (InventoryStage.Ready or InventoryStage.Empty);
        }
    }

    private void RenderGrid(InventoryPresenter presenter)
    {
        if (_storedGrid is null || _heldGrid is null)
        {
            return;
        }

        RebuildIfChanged(_storedGrid, _storedCells, presenter.Visible, OnCellPressed);
        RebuildIfChanged(_heldGrid, _heldCells, presenter.VisibleHeld, OnCellPressed);

        foreach (var (cell, item) in _storedCells)
        {
            MarkCell(presenter, cell, item, held: false);
        }

        foreach (var (cell, item) in _heldCells)
        {
            MarkCell(presenter, cell, item, held: true);
        }

        Write(_heldLabel, presenter.HeldLabel);

        if (_heldLabel is not null)
        {
            _heldLabel.Visible = _heldLabel.Text.Length > 0;
        }

        Write(_statusLabel, presenter.StatusText);
        Write(_rejectionLabel, presenter.RejectionText);

        if (_statusLabel is not null)
        {
            _statusLabel.Visible = _statusLabel.Text.Length > 0;
        }

        if (_rejectionLabel is not null)
        {
            _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
            _rejectionLabel.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LossRole));
        }
    }

    /// <summary>Rebuilds a grid's cells only when the items it draws are no longer the same items in the same order.</summary>
    private void RebuildIfChanged(
        Container grid,
        List<(Button Cell, InventoryItemView Item)> cells,
        IReadOnlyList<InventoryItemView> items,
        Action<InventoryItemView> pressed)
    {
        if (SameMembership(cells, items))
        {
            for (var index = 0; index < items.Count; index++)
            {
                cells[index] = (cells[index].Cell, items[index]);
            }

            return;
        }

        foreach (var (cell, _) in cells)
        {
            _controls.Remove(cell);
        }

        cells.Clear();
        Clear(grid);

        if (_gearCellScene is null)
        {
            GD.PushError("The gear cell scene did not load, so the bag is not drawn. A player cannot forge what they cannot see.");

            return;
        }

        foreach (var item in items)
        {
            var cell = _gearCellScene.Instantiate<Button>();
            var captured = item;

            cell.GetNode<TextureRect>(GlyphPath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(item.Slot));
            cell.GetNode<TextureRect>(UpgradePath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Upgrade));
            cell.GetNode<TextureRect>(CheckPath).Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Selected));
            cell.Pressed += () => pressed(captured);

            grid.AddChild(cell);
            cells.Add((cell, item));
            _controls.Add(cell);
        }
    }

    private static bool SameMembership(
        List<(Button Cell, InventoryItemView Item)> cells, IReadOnlyList<InventoryItemView> items)
    {
        if (cells.Count != items.Count)
        {
            return false;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (cells[index].Item.InstanceId != items[index].InstanceId || !IsInstanceValid(cells[index].Cell))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes one cell's band, badges and marks from the item as it now stands.</summary>
    private void MarkCell(InventoryPresenter presenter, Button cell, InventoryItemView item, bool held)
    {
        if (!IsInstanceValid(cell))
        {
            return;
        }

        var selecting = presenter.Mode == InventoryMode.Select;
        var selectable = presenter.IsSelectable(item);

        // A cell that cannot join the batch wears the theme's own dead face rather than a fainter
        // band: the gem still says what it is, and the face says it is not a target right now.
        if (selecting && !selectable)
        {
            ClearFace(cell);
        }
        else
        {
            PaintFace(cell, item.Rarity, muted: held);
        }

        PaintGem(cell.GetNode<PanelContainer>(GemPath), cell.GetNode<Label>(GemGlyphPath), item.Rarity);

        var plus = cell.GetNode<Label>(PlusPath);

        plus.Text = InventoryPresenter.EnhanceBadge(item);
        plus.Visible = plus.Text.Length > 0;

        var mark = cell.GetNode<TextureRect>(MarkPath);

        if (item.Locked)
        {
            mark.Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Lock));
            mark.Visible = true;
        }
        else if (item.IsEquipped)
        {
            mark.Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Worn));
            mark.Visible = true;
        }
        else
        {
            mark.Visible = false;
        }

        cell.GetNode<TextureRect>(UpgradePath).Visible = !selecting && presenter.IsBetterThanWorn(item);
        cell.GetNode<TextureRect>(CheckPath).Visible = selecting && presenter.IsSelected(item);

        // In select mode an item that cannot join the batch is a dead target and is drawn as one; in
        // browse mode every cell opens its sheet, held ones included, because the sheet is where a
        // held item's reason lives.
        cell.Disabled = presenter.Busy || (selecting && !selectable);
    }

    private void RenderSelectBar(InventoryPresenter presenter)
    {
        var selecting = presenter.Mode == InventoryMode.Select;

        // The toolbar and the quick-pick row trade places: sorting and filtering are browse-mode
        // questions, and the grid keeps its height either way.
        Show(_toolbar, !selecting);
        Show(_quickRow, selecting);
        Show(_selectBar, selecting);

        if (!selecting)
        {
            return;
        }

        Write(_quickPickLabel, presenter.QuickPickLabel);
        Write(_salvageSummary, presenter.SalvageSummaryText);
        Write(_selectCancelButton, presenter.CancelText);
        Write(_salvageButton, presenter.SalvageText);

        if (_salvageButton is not null)
        {
            _salvageButton.Disabled = presenter.Busy || presenter.SelectedCount == 0;
        }

        if (_selectCancelButton is not null)
        {
            _selectCancelButton.Disabled = presenter.Busy;
        }

        for (var index = 0; index < _quickPickButtons.Count && index < presenter.QuickPickRarities.Count; index++)
        {
            var button = _quickPickButtons[index];

            if (IsInstanceValid(button))
            {
                PaintFace(button, presenter.QuickPickRarities[index], muted: false);
                button.Disabled = presenter.Busy;
            }
        }
    }

    private void RenderSheet(InventoryPresenter presenter)
    {
        var open = presenter.Sheet != GearSheetPage.Closed && presenter.Inspected is not null;

        if (_sheet is not null)
        {
            _sheet.Visible = open;
        }

        if (_scrim is not null)
        {
            _scrim.Visible = open || presenter.SalvageConfirmPending;
            _scrim.Disabled = presenter.Busy;
        }

        if (!open || presenter.Inspected is not { } item)
        {
            return;
        }

        if (_sheetGem is not null && _sheetGemGlyph is not null)
        {
            PaintGem(_sheetGem, _sheetGemGlyph, item.Rarity);
        }

        Write(_sheetTitle, presenter.InspectedTitle);
        Write(_sheetCaption, presenter.InspectedCaption);
        Write(_lockButton, presenter.LockActionText);
        Write(_sheetCloseButton, presenter.CloseText);

        if (_lockButton is not null)
        {
            _lockButton.Visible = presenter.CanToggleLock || item.Locked;
            _lockButton.Disabled = !presenter.CanToggleLock;
        }

        if (_sheetCloseButton is not null)
        {
            _sheetCloseButton.Disabled = presenter.Busy;
        }

        if (_sheetBadges is not null)
        {
            Clear(_sheetBadges);

            foreach (var badge in presenter.InspectedBadges)
            {
                _sheetBadges.AddChild(Line(badge, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: false));
            }
        }

        Show(_detailsPage, presenter.Sheet == GearSheetPage.Details);
        Show(_detailsActions, presenter.Sheet == GearSheetPage.Details);
        Show(_enhancePage, presenter.Sheet == GearSheetPage.Enhance);
        Show(_enhanceActions, presenter.Sheet == GearSheetPage.Enhance);
        Show(_mergePage, presenter.Sheet == GearSheetPage.Merge);
        Show(_mergeActions, presenter.Sheet == GearSheetPage.Merge);

        switch (presenter.Sheet)
        {
            case GearSheetPage.Details:
                RenderDetails(presenter, item);
                break;

            case GearSheetPage.Enhance:
                RenderEnhance(presenter);
                break;

            case GearSheetPage.Merge:
                RenderMerge(presenter);
                break;

            default:
                break;
        }
    }

    private void RenderDetails(InventoryPresenter presenter, InventoryItemView item)
    {
        Write(_powerCaption, presenter.PowerLabel);
        Write(_powerValue, presenter.InspectedPowerText);
        Write(_powerDelta, presenter.InspectedPowerDelta);
        Tint(_powerDelta, Math.Sign(presenter.InspectedPowerDelta.StartsWith('↑') ? 1 : presenter.InspectedPowerDelta.StartsWith('↓') ? -1 : 0));

        var projected = presenter.ProjectedHeroPower;

        if (_heroPowerRow is not null)
        {
            _heroPowerRow.Visible = projected is not null && presenter.HeroPower is not null;
        }

        if (projected is { } after && presenter.HeroPower is { } now)
        {
            Write(_heroPowerCaption, presenter.HeroPowerLabel);
            Write(_heroPowerValue, PlayerNumber.Abbreviated((long)Math.Round(now)) + " → " + PlayerNumber.Abbreviated((long)Math.Round(after)));
            Tint(_heroPowerValue, Math.Sign(after - now));
        }

        if (_statRows is not null)
        {
            Clear(_statRows);

            foreach (var line in presenter.InspectedStats)
            {
                _statRows.AddChild(StatRow(line));
            }
        }

        if (_affixRows is not null && _affixesCaption is not null)
        {
            Clear(_affixRows);

            var affixes = presenter.InspectedAffixes;

            _affixesCaption.Text = presenter.AffixesLabel;
            _affixesCaption.Visible = affixes.Count > 0;

            foreach (var affix in affixes)
            {
                _affixRows.AddChild(Line(affix, ValueVariation, null, wrap: true));
            }
        }

        Write(_setLine, presenter.InspectedSetText);

        if (_setLine is not null)
        {
            _setLine.Visible = _setLine.Text.Length > 0;
        }

        Write(_enhanceLine, presenter.EnhanceText + " " + presenter.InspectedEnhanceText);
        Write(_detailsBlock, presenter.InspectedBlockText);

        if (_detailsBlock is not null)
        {
            _detailsBlock.Visible = _detailsBlock.Text.Length > 0;
            _detailsBlock.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LockRole));
        }

        if (_equipButton is not null)
        {
            _equipButton.Text = item.IsEquipped ? presenter.UnequipText : presenter.EquipText;
            _equipButton.Disabled = !(item.IsEquipped ? presenter.CanUnequip : presenter.CanEquip);
        }

        Write(_enhanceButton, presenter.EnhanceText);
        Write(_mergeButton, presenter.MergeText);
        Write(_sheetSalvageButton, presenter.SalvageText);

        if (_enhanceButton is not null)
        {
            _enhanceButton.Disabled = presenter.Busy || !presenter.CanShowEnhance;
        }

        if (_mergeButton is not null)
        {
            _mergeButton.Disabled = presenter.Busy || !presenter.CanShowMerge;
        }

        if (_sheetSalvageButton is not null)
        {
            _sheetSalvageButton.Disabled = presenter.Busy || !presenter.CanSalvageInspected;
        }
    }

    private void RenderEnhance(InventoryPresenter presenter)
    {
        Write(_enhanceTarget, presenter.EnhanceText + " " + presenter.EnhanceTargetText);
        Write(_enhanceCost, presenter.EnhanceCostText);
        Write(_enhanceOdds, presenter.EnhanceOddsText);
        Write(_enhanceBlock, presenter.EnhanceBlockText);
        Write(_enhanceBackButton, presenter.BackText);
        Write(_enhanceOnceButton, presenter.EnhanceOnceText);

        if (_enhanceBlock is not null)
        {
            _enhanceBlock.Visible = _enhanceBlock.Text.Length > 0;
            _enhanceBlock.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LossRole));
        }

        if (_enhanceStatRows is not null)
        {
            Clear(_enhanceStatRows);

            foreach (var line in presenter.EnhanceStats)
            {
                _enhanceStatRows.AddChild(StatRow(line));
            }
        }

        if (_enhanceOnceButton is not null)
        {
            _enhanceOnceButton.Disabled = !presenter.CanEnhance;
        }

        if (_enhanceBackButton is not null)
        {
            _enhanceBackButton.Disabled = presenter.Busy;
        }
    }

    private void RenderMerge(InventoryPresenter presenter)
    {
        Write(_mergeKeeperCaption, presenter.MergeKeeperLabel + " " + presenter.InspectedTitle);
        Write(_mergeInputsCaption, presenter.MergeInputsLabel);
        Write(_mergeResult, presenter.MergeResultText);
        Write(_mergeCost, presenter.MergeCostText);
        Write(_mergeDust, presenter.MergeDustText);
        Write(_mergeBackButton, presenter.BackText);
        Write(_mergeConfirmButton, presenter.MergeConfirmText);

        if (_mergeDust is not null)
        {
            _mergeDust.Visible = _mergeDust.Text.Length > 0;
        }

        if (_mergeCandidates is not null)
        {
            RebuildIfChanged(_mergeCandidates, _mergeCells, presenter.MergeCandidates, OnMergeCandidatePressed);

            foreach (var (cell, candidate) in _mergeCells)
            {
                if (!IsInstanceValid(cell))
                {
                    continue;
                }

                var picked = presenter.IsMergePick(candidate);

                PaintFace(cell, candidate.Rarity, muted: !picked);
                PaintGem(cell.GetNode<PanelContainer>(GemPath), cell.GetNode<Label>(GemGlyphPath), candidate.Rarity);

                var plus = cell.GetNode<Label>(PlusPath);

                plus.Text = InventoryPresenter.EnhanceBadge(candidate);
                plus.Visible = plus.Text.Length > 0;

                var mark = cell.GetNode<TextureRect>(MarkPath);

                mark.Texture = GD.Load<Texture2D>(IconCatalogue.PathOf(GearMark.Worn));
                mark.Visible = candidate.IsEquipped;
                cell.GetNode<TextureRect>(UpgradePath).Visible = false;
                cell.GetNode<TextureRect>(CheckPath).Visible = picked;
                cell.Disabled = presenter.Busy;
            }
        }

        if (_mergeWarnings is not null)
        {
            Clear(_mergeWarnings);

            foreach (var warning in presenter.MergeWarnings)
            {
                _mergeWarnings.AddChild(Line(warning, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: true));
            }
        }

        if (_mergeConfirmButton is not null)
        {
            _mergeConfirmButton.Disabled = !presenter.CanMerge;
        }

        if (_mergeBackButton is not null)
        {
            _mergeBackButton.Disabled = presenter.Busy;
        }
    }

    private void RenderConfirm(InventoryPresenter presenter)
    {
        if (_confirm is null)
        {
            return;
        }

        _confirm.Visible = presenter.SalvageConfirmPending;

        if (!presenter.SalvageConfirmPending)
        {
            return;
        }

        Write(_confirmTitle, presenter.SalvageText);
        Write(_confirmSummary, presenter.SalvageSummaryText);
        Write(_confirmCancelButton, presenter.CancelText);
        Write(_confirmSalvageButton, presenter.SalvageConfirmText);

        if (_confirmWarnings is not null)
        {
            Clear(_confirmWarnings);

            foreach (var warning in presenter.SalvageWarnings)
            {
                _confirmWarnings.AddChild(Line(warning, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: true));
            }
        }

        if (_confirmSalvageButton is not null)
        {
            _confirmSalvageButton.Disabled = !presenter.CanSalvage;
        }

        if (_confirmCancelButton is not null)
        {
            _confirmCancelButton.Disabled = presenter.Busy;
        }
    }

    private void RenderTabs(InventoryPresenter presenter) =>
        _tabBar?.Show(presenter.TabLabel, static _ => false, HomeAccents.Of(_bands, HomeAccents.Action), ThisTab);

    // ================================================================================ the notice

    /// <summary>Puts the presenter's line over the diorama when it changes, and takes it away after a moment.</summary>
    private void RenderNotice(InventoryPresenter presenter)
    {
        var line = presenter.NoticeKind == InventoryNoticeKind.None
            ? presenter.RejectionText
            : presenter.Notice;
        var kind = presenter.NoticeKind == InventoryNoticeKind.None && line.Length > 0
            ? InventoryNoticeKind.Setback
            : presenter.NoticeKind;

        if (line.Length == 0 || line == _shownNotice)
        {
            return;
        }

        _shownNotice = line;
        Acknowledge(line, kind);
    }

    private void Acknowledge(string line, InventoryNoticeKind kind)
    {
        if (_noticeLabel is null || !IsInstanceValid(_noticeLabel))
        {
            return;
        }

        _noticeLabel.Text = line;
        _noticeLabel.Visible = true;

        // A plain notice falls back to its variation's own ink rather than naming one: the theme
        // decides what a neutral sentence over the diorama looks like.
        switch (kind)
        {
            case InventoryNoticeKind.Gain:
                _noticeLabel.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, HomeAccents.Gain));
                break;

            case InventoryNoticeKind.Setback:
                _noticeLabel.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LossRole));
                break;

            default:
                _noticeLabel.RemoveThemeColorOverride(FontColourOverride);
                break;
        }

        Stop(ref _noticeFade);

        if (!_reducedMotion)
        {
            var faded = _noticeLabel.Modulate;

            faded.A = 0f;
            _noticeLabel.Modulate = faded;
            _noticeFade = CreateTween();
            _noticeFade.TweenProperty(_noticeLabel, ModulateAlphaProperty, 1.0f, NoticeFadeSeconds);
        }

        var token = ++_noticeToken;
        var timer = GetTree()?.CreateTimer(NoticeSeconds);

        if (timer is null)
        {
            return;
        }

        timer.Timeout += () =>
        {
            if (token == _noticeToken)
            {
                Withdraw();
                _presenter?.ClearNotice();
                _shownNotice = "";
            }
        };
    }

    private void Withdraw()
    {
        _noticeToken++;
        Stop(ref _noticeFade);

        if (_noticeLabel is not null && IsInstanceValid(_noticeLabel))
        {
            _noticeLabel.Visible = false;
            _noticeLabel.Text = "";
        }
    }

    // ================================================================================ the presses

    private void OnBackPressed()
    {
        if (_presenter is { Busy: true } || _returnTo is null)
        {
            return;
        }

        InventoryHandover.Return(this, _returnTo);
    }

    private void OnTabSelected(HomeTab tab)
    {
        switch (tab)
        {
            case ThisTab:
                break;

            case HomeTab.Home:
                OnBackPressed();
                break;

            default:
                GD.Print($"{StubMarker} destination=tab_{tab.ToString().ToLowerInvariant()} state=no_screen_yet");
                _presenter?.AcknowledgeNotOpenYet();
                Render();
                break;
        }
    }

    private void OnSlotPressed(GearSlot slot)
    {
        _presenter?.TapSlot(slot);
        Render();
    }

    private void OnCellPressed(InventoryItemView item)
    {
        _presenter?.Open(item);
        Render();
    }

    private void OnMergeCandidatePressed(InventoryItemView item)
    {
        _presenter?.ToggleMergePick(item);
        Render();
    }

    private void OnSortPressed() => _ = SubmitAsync(presenter => presenter.CycleOrderAsync(_lifetime));

    private void OnFilterPressed()
    {
        _presenter?.ClearSlotFilter();
        Render();
    }

    private void OnSelectPressed()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        if (presenter.Mode == InventoryMode.Select)
        {
            presenter.LeaveSelectMode();
        }
        else
        {
            presenter.EnterSelectMode();
        }

        Render();
    }

    private void OnSelectCancelPressed()
    {
        _presenter?.LeaveSelectMode();
        Render();
    }

    private void OnQuickPickPressed(Rarity rarity)
    {
        _presenter?.SelectBand(rarity);
        Render();
    }

    private void OnSalvageRequested()
    {
        _presenter?.RequestSalvage();
        Render();
    }

    private void OnScrimPressed()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        if (presenter.SalvageConfirmPending)
        {
            presenter.CancelSalvage();
        }
        else
        {
            presenter.CloseSheet();
        }

        Render();
    }

    private void OnSheetClosePressed()
    {
        _presenter?.CloseSheet();
        Render();
    }

    private void OnBackToDetailsPressed()
    {
        _presenter?.BackToDetails();
        Render();
    }

    private void OnEnhancePagePressed()
    {
        _presenter?.ShowEnhance();
        Render();
    }

    private void OnMergePagePressed()
    {
        _presenter?.ShowMerge();
        Render();
    }

    private void OnSheetSalvagePressed()
    {
        _presenter?.RequestSalvageInspected();
        Render();
    }

    private void OnConfirmCancelPressed()
    {
        _presenter?.CancelSalvage();
        Render();
    }

    private void OnEquipPressed() =>
        _ = SubmitAsync(presenter => presenter.Inspected is { IsEquipped: true }
            ? presenter.UnequipInspectedAsync(_lifetime)
            : presenter.EquipInspectedAsync(_lifetime));

    private void OnLockPressed() => _ = SubmitAsync(presenter => presenter.ToggleLockInspectedAsync(_lifetime));

    private void OnEnhanceOncePressed() => _ = SubmitAsync(presenter => presenter.EnhanceInspectedAsync(_lifetime));

    private void OnMergeConfirmPressed() => _ = SubmitAsync(presenter => presenter.MergeInspectedAsync(_lifetime));

    private void OnConfirmSalvagePressed() => _ = SubmitAsync(presenter => presenter.SalvageAsync(_lifetime));

    /// <summary>One shape for every command: draw the busy state, await, draw the answer.</summary>
    private async Task SubmitAsync(Func<InventoryPresenter, Task> submit)
    {
        if (_presenter is not { } presenter || presenter.Busy)
        {
            return;
        }

        try
        {
            var pending = submit(presenter);

            Render();

            await pending.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Render();
    }

    // ================================================================================ drawing helpers

    /// <summary>Gives a button's four faces the band's tint and shape, from the theme's palette.</summary>
    /// <remarks>
    /// 🔒 The band's colour is blended INTO the theme's face rather than replacing it, so a Mythic cell
    /// is still a dark cell with a violet edge and not a violet block a glyph cannot be read on. Muted
    /// faces — a held item, a cell that cannot join a batch, an unpicked candidate — take a fainter blend.
    /// </remarks>
    private void PaintFace(Button button, Rarity rarity, bool muted)
    {
        var (role, _, corners) = Band(rarity);
        var accent = HomeAccents.Of(_bands, role);

        foreach (var face in ButtonFaces)
        {
            if (button.GetThemeStylebox(face) is not StyleBoxFlat original || original.Duplicate() is not StyleBoxFlat painted)
            {
                continue;
            }

            painted.BorderColor = muted ? accent.Lerp(original.BorderColor, 0.55f) : accent;
            painted.BgColor = accent.Lerp(original.BgColor, muted ? 0.9f : 0.78f);
            painted.CornerRadiusTopLeft = corners.X;
            painted.CornerRadiusTopRight = corners.Y;
            painted.CornerRadiusBottomRight = corners.Z;
            painted.CornerRadiusBottomLeft = corners.W;

            button.AddThemeStyleboxOverride(face, painted);
        }
    }

    /// <summary>Takes a band's paint off a tile, so an emptied slot draws the theme's own face again.</summary>
    private static void ClearFace(Button button)
    {
        foreach (var face in ButtonFaces)
        {
            button.RemoveThemeStyleboxOverride(face);
        }
    }

    /// <summary>Gives the gem its band's fill, its frame shape and its letter, all three at once.</summary>
    /// <remarks>
    /// 🔒 The same treatment the perk draft's gem takes, for the same reason — rarity is never carried
    /// by colour alone, and a player moving between screens should not have to learn the band twice.
    /// </remarks>
    private void PaintGem(PanelContainer gem, Label glyph, Rarity rarity)
    {
        var (role, symbol, corners) = Band(rarity);

        glyph.Text = symbol;

        if (gem.GetThemeStylebox(PanelStyle) is not StyleBoxFlat face || face.Duplicate() is not StyleBoxFlat band)
        {
            return;
        }

        band.BgColor = HomeAccents.Of(_bands, role);
        band.CornerRadiusTopLeft = corners.X;
        band.CornerRadiusTopRight = corners.Y;
        band.CornerRadiusBottomRight = corners.Z;
        band.CornerRadiusBottomLeft = corners.W;

        gem.AddThemeStyleboxOverride(PanelStyle, band);
    }

    /// <summary>
    /// Each band's theme role, its letter, and its corner SHAPE — five shapes, so a player who reads
    /// none of the hues apart still tells the bands: square, round, leaf, drop, lozenge.
    /// </summary>
    private static (string Role, string Symbol, Vector4I Corners) Band(Rarity rarity) => rarity switch
    {
        Rarity.C => (RarityRolePrefix + "c", "C", new Vector4I(6, 6, 6, 6)),
        Rarity.B => (RarityRolePrefix + "b", "B", new Vector4I(60, 60, 60, 60)),
        Rarity.A => (RarityRolePrefix + "a", "A", new Vector4I(48, 6, 48, 6)),
        Rarity.S => (RarityRolePrefix + "s", "S", new Vector4I(6, 6, 48, 48)),
        Rarity.SS => (RarityRolePrefix + "ss", "SS", new Vector4I(30, 60, 30, 60)),
        _ => (RarityRolePrefix + "c", "?", new Vector4I(18, 18, 18, 18)),
    };

    /// <summary>One stat line: the name, the figure, and the comparison in its accent.</summary>
    private HBoxContainer StatRow(GearStatLine line)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };

        row.AddThemeConstantOverride("separation", 18);

        var name = Line(line.Name, LineVariation, null, wrap: true);

        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);

        // The figures keep a column of their own: a trimming label beside an expanding one is
        // otherwise squeezed to its ellipsis, and a stat row with no figure says nothing.
        var value = Line(line.Value, ValueVariation, null, wrap: false);

        value.CustomMinimumSize = new Vector2(StatValueWidth, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);

        var delta = Line(line.Delta, ValueVariation, null, wrap: false);

        delta.CustomMinimumSize = new Vector2(StatDeltaWidth, 0);
        delta.HorizontalAlignment = HorizontalAlignment.Right;
        Tint(delta, line.Sign);
        row.AddChild(delta);

        return row;
    }

    /// <summary>
    /// A label built in code, with the overflow decision every text control on this screen states: a
    /// sentence wraps, a figure keeps its width and trims — a wrapped figure in a row is one digit a line.
    /// </summary>
    private static Label Line(string text, string variation, Color? tint, bool wrap)
    {
        var label = new Label
        {
            Text = text,
            ThemeTypeVariation = variation,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        else
        {
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }

        if (tint is { } colour)
        {
            label.AddThemeColorOverride(FontColourOverride, colour);
        }

        return label;
    }

    /// <summary>Draws a comparison in the gain or loss accent — the third channel beside the arrow and the sign.</summary>
    private void Tint(Label? label, int sign)
    {
        if (label is null || !IsInstanceValid(label))
        {
            return;
        }

        if (sign == 0)
        {
            label.RemoveThemeColorOverride(FontColourOverride);

            return;
        }

        label.AddThemeColorOverride(
            FontColourOverride, HomeAccents.Of(_bands, sign > 0 ? HomeAccents.Gain : LossRole));
    }

    private static void Show(Control? control, bool visible)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.Visible = visible;
        }
    }

    private static void Write(Label? label, string text)
    {
        if (label is not null && IsInstanceValid(label))
        {
            label.Text = text;
        }
    }

    private static void Write(Button? button, string text)
    {
        if (button is not null && IsInstanceValid(button))
        {
            button.Text = text;
        }
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame are
    /// both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt column
    /// from briefly drawing two sets of rows over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void Stop(ref Tween? tween)
    {
        if (tween is not null && IsInstanceValid(tween))
        {
            tween.Kill();
        }

        tween = null;
    }
}
