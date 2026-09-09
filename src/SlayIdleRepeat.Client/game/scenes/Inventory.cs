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
/// 🔒 <b>The grid is rebuilt only when its membership changes, and a kept cell always presses with the
/// item it now draws.</b> A tap that picks an item for a batch or turns a page re-renders the screen,
/// and a bag of a thousand items rebuilt on every such tap would stutter under a thumb. The cells are
/// kept and re-marked when the same items are still in the same order; a press looks the item up by
/// its cell at the moment of the tap rather than through a closure taken when the cell was built —
/// the closure was how an unlocked item stayed untappable until the grid happened to be rebuilt.
/// </para>
/// <para>
/// 🔒 <b>The painted faces are built once per band and shared.</b> A band's tint over the theme's face
/// is the same stylebox for every cell that wears it, so re-marking a thousand cells duplicates
/// nothing; the ten textures the cells wear are loaded once on the way in.
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

    /// <summary>The one line a headless run's screen state is read off — printed once the read has settled.</summary>
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

    /// <summary>The two figure columns of a stat row, in canvas units — room for "1234.5" and "↑ +12.5%".</summary>
    private const float StatValueWidth = 190f;
    private const float StatDeltaWidth = 250f;

    private const string MarginTopConstant = "margin_top";
    private const string MarginBottomConstant = "margin_bottom";
    private const string SeparationConstant = "separation";
    private const string FontColourOverride = "font_color";
    private const string PanelStyle = "panel";
    private const string ModulateAlphaProperty = "modulate:a";
    private const string CaptionVariation = "HudCaption";
    private const string ValueVariation = "HudValueSmall";
    private const string LineVariation = "GearCaption";
    private const string ChipVariation = "ChromeButton";

    /// <summary>What follows the slot's name on the filter chip: the multiplication sign, which every shipped face has.</summary>
    private const string FilterClearMark = "  ×";

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
    private const string CrownsChipPath = "%CrownsChip";
    private const string StonesChipPath = "%StonesChip";
    private const string DustChipPath = "%DustChip";
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
    private const string StoredGridPath = "%StoredGrid";
    private const string HeldLabelPath = "%HeldLabel";
    private const string HeldGridPath = "%HeldGrid";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RetryButtonPath = "%RetryButton";
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
    private const string ConfirmScrimPath = "%ConfirmScrim";
    private const string SheetPath = "%Sheet";
    private const string SheetMarginsPath = "%SheetMargins";
    private const string SheetGemPath = "%SheetGem";
    private const string SheetGemGlyphPath = "%SheetGemGlyph";
    private const string SheetTitlePath = "%SheetTitle";
    private const string LockButtonPath = "%LockButton";
    private const string SheetCloseButtonPath = "%SheetCloseButton";
    private const string SheetCaptionPath = "%SheetCaption";
    private const string SheetBadgesPath = "%SheetBadges";
    private const string DetailsPagePath = "%DetailsPage";
    private const string DetailsActionsPath = "%DetailsActions";
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
    private const string EnhanceActionsPath = "%EnhanceActions";
    private const string EnhanceTargetPath = "%EnhanceTarget";
    private const string EnhanceStatRowsPath = "%EnhanceStatRows";
    private const string EnhanceCostPath = "%EnhanceCost";
    private const string EnhanceOddsPath = "%EnhanceOdds";
    private const string EnhanceMercyPath = "%EnhanceMercy";
    private const string EnhanceBlockPath = "%EnhanceBlock";
    private const string EnhanceBackButtonPath = "%EnhanceBackButton";
    private const string EnhanceOnceButtonPath = "%EnhanceOnceButton";
    private const string MergePagePath = "%MergePage";
    private const string MergeActionsPath = "%MergeActions";
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
    private WalletChip? _crownsChip;
    private WalletChip? _stonesChip;
    private WalletChip? _dustChip;
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
    private GridContainer? _storedGrid;
    private Label? _heldLabel;
    private GridContainer? _heldGrid;
    private Label? _statusLabel;
    private Button? _retryButton;
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
    private Button? _confirmScrim;
    private Control? _sheet;
    private MarginContainer? _sheetMargins;
    private PanelContainer? _sheetGem;
    private Label? _sheetGemGlyph;
    private Label? _sheetTitle;
    private Button? _lockButton;
    private Button? _sheetCloseButton;
    private Label? _sheetCaption;
    private HBoxContainer? _sheetBadges;
    private Control? _detailsPage;
    private Control? _detailsActions;
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
    private Control? _enhanceActions;
    private Label? _enhanceTarget;
    private VBoxContainer? _enhanceStatRows;
    private Label? _enhanceCost;
    private Label? _enhanceOdds;
    private Label? _enhanceMercy;
    private Label? _enhanceBlock;
    private Button? _enhanceBackButton;
    private Button? _enhanceOnceButton;
    private Control? _mergePage;
    private Control? _mergeActions;
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

    private readonly Dictionary<GearSlot, Texture2D> _slotGlyphs = [];
    private readonly Dictionary<GearMark, Texture2D> _markGlyphs = [];
    private readonly Dictionary<(Rarity Rarity, bool Muted, StringName Face), StyleBoxFlat> _paintedFaces = [];
    private readonly Dictionary<Rarity, StyleBoxFlat> _paintedGems = [];

    private readonly Dictionary<GearSlot, SlotTileNodes> _slotTiles = [];
    private readonly List<CellNodes> _storedCells = [];
    private readonly List<CellNodes> _heldCells = [];
    private readonly List<CellNodes> _mergeCells = [];
    private readonly List<Button> _quickPickButtons = [];
    private readonly List<Button> _controls = [];

    private string _shownNotice = "";
    private int _noticeToken;
    private Tween? _noticeFade;
    private bool _readyReported;

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
        _crownsChip = GetNode<WalletChip>(CrownsChipPath);
        _stonesChip = GetNode<WalletChip>(StonesChipPath);
        _dustChip = GetNode<WalletChip>(DustChipPath);
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
        _storedGrid = GetNode<GridContainer>(StoredGridPath);
        _heldLabel = GetNode<Label>(HeldLabelPath);
        _heldGrid = GetNode<GridContainer>(HeldGridPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _retryButton = GetNode<Button>(RetryButtonPath);
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
        _confirmScrim = GetNode<Button>(ConfirmScrimPath);
        _sheet = GetNode<Control>(SheetPath);
        _sheetMargins = GetNode<MarginContainer>(SheetMarginsPath);
        _sheetGem = GetNode<PanelContainer>(SheetGemPath);
        _sheetGemGlyph = GetNode<Label>(SheetGemGlyphPath);
        _sheetTitle = GetNode<Label>(SheetTitlePath);
        _lockButton = GetNode<Button>(LockButtonPath);
        _sheetCloseButton = GetNode<Button>(SheetCloseButtonPath);
        _sheetCaption = GetNode<Label>(SheetCaptionPath);
        _sheetBadges = GetNode<HBoxContainer>(SheetBadgesPath);
        _detailsPage = GetNode<Control>(DetailsPagePath);
        _detailsActions = GetNode<Control>(DetailsActionsPath);
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
        _enhanceActions = GetNode<Control>(EnhanceActionsPath);
        _enhanceTarget = GetNode<Label>(EnhanceTargetPath);
        _enhanceStatRows = GetNode<VBoxContainer>(EnhanceStatRowsPath);
        _enhanceCost = GetNode<Label>(EnhanceCostPath);
        _enhanceOdds = GetNode<Label>(EnhanceOddsPath);
        _enhanceMercy = GetNode<Label>(EnhanceMercyPath);
        _enhanceBlock = GetNode<Label>(EnhanceBlockPath);
        _enhanceBackButton = GetNode<Button>(EnhanceBackButtonPath);
        _enhanceOnceButton = GetNode<Button>(EnhanceOnceButtonPath);
        _mergePage = GetNode<Control>(MergePagePath);
        _mergeActions = GetNode<Control>(MergeActionsPath);
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

        LoadGlyphs();

        Wire(_backButton, OnBackPressed);
        Wire(_sortButton, OnSortPressed);
        Wire(_filterButton, OnFilterPressed);
        Wire(_selectButton, OnSelectPressed);
        Wire(_retryButton, OnRetryPressed);
        Wire(_selectCancelButton, OnSelectCancelPressed);
        Wire(_salvageButton, OnSalvageRequested);
        Wire(_scrim, OnScrimPressed);
        Wire(_confirmScrim, OnConfirmCancelPressed);
        Wire(_lockButton, OnLockPressed);
        Wire(_sheetCloseButton, OnScrimPressed);
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

    /// <summary>The ten glyphs the cells and tiles wear, loaded once rather than per cell per redraw.</summary>
    private void LoadGlyphs()
    {
        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            _slotGlyphs[slot] = GD.Load<Texture2D>(IconCatalogue.PathOf(slot));
        }

        foreach (var mark in Enum.GetValues<GearMark>())
        {
            _markGlyphs[mark] = GD.Load<Texture2D>(IconCatalogue.PathOf(mark));
        }
    }

    /// <summary>The top safe inset lands inside the top bar and the bottom one under the tab row and the sheet, as on Home.</summary>
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
        var nodes = new SlotTileNodes(
            tile,
            tile.GetNode<Label>(CaptionPath),
            tile.GetNode<PanelContainer>(GemPath),
            tile.GetNode<Label>(GemGlyphPath),
            tile.GetNode<Label>(PlusPath),
            tile.GetNode<TextureRect>(MarkPath));

        tile.GetNode<TextureRect>(GlyphPath).Texture = _slotGlyphs[slot];
        nodes.Mark.Texture = _markGlyphs[GearMark.Upgrade];

        var tapped = slot;

        tile.Pressed += () => OnSlotPressed(tapped);

        _slotTiles[slot] = nodes;
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
                CustomMinimumSize = new Vector2(HomeLayout.MinimumTouchTarget, HomeLayout.MinimumTouchTarget),
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
        if (presenter.Busy)
        {
            foreach (var control in _controls)
            {
                if (IsInstanceValid(control))
                {
                    control.Disabled = true;
                }
            }
        }

        // Once: the marker means the read has settled, so a harness that waits for it snapshots a
        // stock and not a loading line.
        if (!_readyReported && presenter.Stage != InventoryStage.NotYetRead)
        {
            _readyReported = true;
            GD.Print(InventoryMarker);
        }
    }

    private void RenderTopBar(InventoryPresenter presenter)
    {
        Write(_backButton, presenter.LeaveText);
        _crownsChip?.Show(presenter.CrownsText);
        _stonesChip?.Show(presenter.EnhanceStonesText);
        _dustChip?.Show(presenter.MergeDustText);
        Enable(_backButton, !presenter.Busy);
    }

    private void RenderSlots(InventoryPresenter presenter)
    {
        foreach (var (slot, tile) in _slotTiles)
        {
            if (!IsInstanceValid(tile.Button))
            {
                continue;
            }

            var worn = presenter.Worn(slot);

            Enable(tile.Button, presenter.SlotsLive);

            if (worn is null)
            {
                ClearFace(tile.Button);
                tile.Caption.Text = presenter.EmptySlotText;
                tile.Gem.Visible = false;
                tile.Plus.Visible = false;
            }
            else
            {
                PaintFace(tile.Button, worn.Rarity, muted: false);
                tile.Caption.Text = presenter.FamilyName(worn.Family);
                PaintGem(tile.Gem, tile.GemGlyph, worn.Rarity);
                tile.Gem.Visible = true;
                WriteOrHide(tile.Plus, InventoryPresenter.EnhanceBadge(worn));
            }

            tile.Mark.Visible = presenter.UpgradeAvailable(slot);
        }

        Write(_heroPowerLabel, presenter.HeroPowerText);
    }

    private void RenderToolbar(InventoryPresenter presenter)
    {
        Write(_sortButton, presenter.SortText);
        Write(_capacityLabel, presenter.CapacityLabel + " " + presenter.CapacityValue);
        Write(_selectButton, presenter.SelectText);
        Write(_filterButton, presenter.SlotFilterText + FilterClearMark);
        Show(_filterButton, presenter.SlotFilter is not null);
        Enable(_filterButton, !presenter.Busy);
        Enable(_sortButton, !presenter.Busy && presenter.Settled);
        Enable(_selectButton, !presenter.Busy && presenter.Settled);
    }

    private void RenderGrid(InventoryPresenter presenter)
    {
        if (_storedGrid is null || _heldGrid is null)
        {
            return;
        }

        RebuildIfChanged(_storedGrid, _storedCells, presenter.Visible, OnCellPressed);
        RebuildIfChanged(_heldGrid, _heldCells, presenter.VisibleHeld, OnCellPressed);

        foreach (var cell in _storedCells)
        {
            MarkCell(presenter, cell, held: false);
        }

        foreach (var cell in _heldCells)
        {
            MarkCell(presenter, cell, held: true);
        }

        WriteOrHide(_heldLabel, presenter.HeldLabel);
        WriteOrHide(_statusLabel, presenter.StatusText);
        Write(_retryButton, presenter.RetryText);
        Show(_retryButton, presenter.CanRetry);
        WriteOrHide(_rejectionLabel, presenter.RejectionText);
        _rejectionLabel?.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LossRole));
    }

    /// <summary>Rebuilds a grid's cells only when the items it draws are no longer the same items in the same order.</summary>
    private void RebuildIfChanged(
        Container grid, List<CellNodes> cells, IReadOnlyList<InventoryItemView> items, Action<CellNodes> pressed)
    {
        if (SameMembership(cells, items))
        {
            for (var index = 0; index < items.Count; index++)
            {
                cells[index].Item = items[index];
            }

            return;
        }

        foreach (var cell in cells)
        {
            _controls.Remove(cell.Button);
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
            var button = _gearCellScene.Instantiate<Button>();
            var cell = new CellNodes(
                button,
                button.GetNode<TextureRect>(GlyphPath),
                button.GetNode<PanelContainer>(GemPath),
                button.GetNode<Label>(GemGlyphPath),
                button.GetNode<Label>(PlusPath),
                button.GetNode<TextureRect>(MarkPath),
                button.GetNode<TextureRect>(UpgradePath),
                button.GetNode<TextureRect>(CheckPath))
            {
                Item = item,
            };

            cell.Glyph.Texture = _slotGlyphs[item.Slot];
            cell.Upgrade.Texture = _markGlyphs[GearMark.Upgrade];
            cell.Check.Texture = _markGlyphs[GearMark.Selected];

            // The press resolves the cell's CURRENT item, not the one it was built with: a kept cell
            // outlives several reads, and the item it draws moves on with each.
            button.Pressed += () => pressed(cell);

            grid.AddChild(button);
            cells.Add(cell);
            _controls.Add(button);
        }
    }

    private static bool SameMembership(List<CellNodes> cells, IReadOnlyList<InventoryItemView> items)
    {
        if (cells.Count != items.Count)
        {
            return false;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (cells[index].Item.InstanceId != items[index].InstanceId || !IsInstanceValid(cells[index].Button))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes one cell's band, badges and marks from the item as it now stands.</summary>
    private void MarkCell(InventoryPresenter presenter, CellNodes cell, bool held)
    {
        if (!IsInstanceValid(cell.Button))
        {
            return;
        }

        var item = cell.Item;
        var selecting = presenter.Mode == InventoryMode.Select;
        var selectable = presenter.IsSelectable(item);

        // A cell that cannot join the batch wears the theme's own dead face rather than a fainter
        // band: the gem still says what it is, and the face says it is not a target right now.
        if (selecting && !selectable)
        {
            ClearFace(cell.Button);
        }
        else
        {
            PaintFace(cell.Button, item.Rarity, muted: held);
        }

        PaintGem(cell.Gem, cell.GemGlyph, item.Rarity);
        WriteOrHide(cell.Plus, InventoryPresenter.EnhanceBadge(item));

        if (item.Locked)
        {
            cell.Mark.Texture = _markGlyphs[GearMark.Lock];
            cell.Mark.Visible = true;
        }
        else if (item.IsEquipped)
        {
            cell.Mark.Texture = _markGlyphs[GearMark.Worn];
            cell.Mark.Visible = true;
        }
        else
        {
            cell.Mark.Visible = false;
        }

        cell.Upgrade.Visible = !selecting && presenter.IsBetterThanWorn(item);
        cell.Check.Visible = selecting && presenter.IsSelected(item);

        // In select mode an item that cannot join the batch is a dead target and is drawn as one; in
        // browse mode every cell opens its sheet, held ones included, because the sheet is where a
        // held item's reason lives.
        Enable(cell.Button, !presenter.Busy && !(selecting && !selectable));
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
        Enable(_salvageButton, !presenter.Busy && presenter.SelectedCount > 0);
        Enable(_selectCancelButton, !presenter.Busy);

        for (var index = 0; index < _quickPickButtons.Count && index < presenter.QuickPickRarities.Count; index++)
        {
            var button = _quickPickButtons[index];

            if (IsInstanceValid(button))
            {
                PaintFace(button, presenter.QuickPickRarities[index], muted: false);
                Enable(button, !presenter.Busy);
            }
        }
    }

    private void RenderSheet(InventoryPresenter presenter)
    {
        var open = presenter.Sheet != GearSheetPage.Closed && presenter.Inspected is not null;

        Show(_sheet, open);
        Show(_scrim, open);
        Enable(_scrim, !presenter.Busy);

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
        Show(_lockButton, presenter.CanToggleLock || item.Locked);
        Enable(_lockButton, presenter.CanToggleLock);
        Enable(_sheetCloseButton, !presenter.Busy);

        Fill(_sheetBadges, presenter.InspectedBadges, badge => Line(badge, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: false));

        Show(_detailsPage, presenter.Sheet == GearSheetPage.Details);
        Show(_detailsActions, presenter.Sheet == GearSheetPage.Details);
        Show(_enhancePage, presenter.Sheet == GearSheetPage.Enhance);
        Show(_enhanceActions, presenter.Sheet == GearSheetPage.Enhance);
        Show(_enhanceBlock, presenter.Sheet == GearSheetPage.Enhance && presenter.EnhanceBlockText.Length > 0);
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
        if (presenter.InspectedPowerLine is { } power)
        {
            Write(_powerCaption, power.Name);
            Write(_powerValue, power.Value);
            Write(_powerDelta, power.Delta);
            Tint(_powerDelta, power.Sign);
        }

        var projected = presenter.ProjectedHeroPowerLine;

        Show(_heroPowerRow, projected is not null);

        if (projected is not null)
        {
            Write(_heroPowerCaption, projected.Name);
            Write(_heroPowerValue, projected.Value);
            Tint(_heroPowerValue, projected.Sign);
        }

        Fill(_statRows, presenter.InspectedStats, StatRow);

        var affixes = presenter.InspectedAffixes;

        Write(_affixesCaption, presenter.AffixesLabel);
        Show(_affixesCaption, affixes.Count > 0);
        Fill(_affixRows, affixes, affix => Line(affix, ValueVariation, null, wrap: true));

        WriteOrHide(_setLine, presenter.InspectedSetText);
        Write(_enhanceLine, presenter.InspectedEnhanceText);
        WriteOrHide(_detailsBlock, presenter.InspectedBlockText);
        _detailsBlock?.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LockRole));

        Write(_equipButton, item.IsEquipped ? presenter.UnequipText : presenter.EquipText);
        Enable(_equipButton, item.IsEquipped ? presenter.CanUnequip : presenter.CanEquip);

        Write(_enhanceButton, presenter.EnhanceText);
        Write(_mergeButton, presenter.MergeText);
        Write(_sheetSalvageButton, presenter.SalvageText);
        Enable(_enhanceButton, !presenter.Busy && presenter.CanShowEnhance);
        Enable(_mergeButton, !presenter.Busy && presenter.CanShowMerge);
        Enable(_sheetSalvageButton, !presenter.Busy && presenter.CanSalvageInspected);
    }

    private void RenderEnhance(InventoryPresenter presenter)
    {
        Write(_enhanceTarget, presenter.EnhanceTargetText);
        Write(_enhanceCost, presenter.EnhanceCostText);
        Write(_enhanceOdds, presenter.EnhanceOddsText);
        WriteOrHide(_enhanceMercy, presenter.EnhanceMercyText);
        Write(_enhanceBlock, presenter.EnhanceBlockText);
        _enhanceBlock?.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, LossRole));
        Write(_enhanceBackButton, presenter.BackText);
        Write(_enhanceOnceButton, presenter.EnhanceOnceText);

        Fill(_enhanceStatRows, presenter.EnhanceStats, StatRow);

        Enable(_enhanceOnceButton, presenter.CanEnhance);
        Enable(_enhanceBackButton, !presenter.Busy);
    }

    private void RenderMerge(InventoryPresenter presenter)
    {
        Write(_mergeKeeperCaption, presenter.MergeKeeperLabel + " " + presenter.InspectedTitle);
        Write(_mergeInputsCaption, presenter.MergeInputsLabel);
        Write(_mergeResult, presenter.MergeResultText);
        Write(_mergeCost, presenter.MergeCostText);
        WriteOrHide(_mergeDust, presenter.MergeDustCostText);
        Write(_mergeBackButton, presenter.BackText);
        Write(_mergeConfirmButton, presenter.MergeConfirmText);

        if (_mergeCandidates is not null)
        {
            RebuildIfChanged(_mergeCandidates, _mergeCells, presenter.MergeCandidates, OnMergeCandidatePressed);

            foreach (var cell in _mergeCells)
            {
                if (!IsInstanceValid(cell.Button))
                {
                    continue;
                }

                var candidate = cell.Item;
                var picked = presenter.IsMergePick(candidate);

                PaintFace(cell.Button, candidate.Rarity, muted: !picked);
                PaintGem(cell.Gem, cell.GemGlyph, candidate.Rarity);
                WriteOrHide(cell.Plus, InventoryPresenter.EnhanceBadge(candidate));
                cell.Mark.Texture = _markGlyphs[GearMark.Worn];
                cell.Mark.Visible = candidate.IsEquipped;
                cell.Upgrade.Visible = false;
                cell.Check.Visible = picked;
                Enable(cell.Button, !presenter.Busy);
            }
        }

        Fill(_mergeWarnings, presenter.MergeWarnings, warning => Line(warning, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: true));

        Enable(_mergeConfirmButton, presenter.CanMerge);
        Enable(_mergeBackButton, !presenter.Busy);
    }

    private void RenderConfirm(InventoryPresenter presenter)
    {
        Show(_confirm, presenter.SalvageConfirmPending);
        Show(_confirmScrim, presenter.SalvageConfirmPending);
        Enable(_confirmScrim, !presenter.Busy);

        if (!presenter.SalvageConfirmPending)
        {
            return;
        }

        Write(_confirmTitle, presenter.SalvageText);
        Write(_confirmSummary, presenter.SalvageSummaryText);
        Write(_confirmCancelButton, presenter.CancelText);
        Write(_confirmSalvageButton, presenter.SalvageConfirmText);

        Fill(_confirmWarnings, presenter.SalvageWarnings, warning => Line(warning, CaptionVariation, HomeAccents.Of(_bands, LockRole), wrap: true));

        Enable(_confirmSalvageButton, presenter.CanSalvage);
        Enable(_confirmCancelButton, !presenter.Busy);
    }

    private void RenderTabs(InventoryPresenter presenter) =>
        _tabBar?.Show(presenter.TabLabel, static _ => false, HomeAccents.Of(_bands, HomeAccents.Action), ThisTab);

    // ================================================================================ the notice

    /// <summary>Puts the presenter's line over the diorama when it changes, and takes it away after a moment.</summary>
    private void RenderNotice(InventoryPresenter presenter)
    {
        var line = presenter.Notice;

        if (line.Length == 0 || line == _shownNotice)
        {
            return;
        }

        _shownNotice = line;
        Acknowledge(line, presenter.NoticeKind);
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
            if (token != _noticeToken)
            {
                return;
            }

            Withdraw();
            _shownNotice = "";

            // The presenter forgets the answer with the line: a refusal that outlived its notice was
            // re-announced on every redraw.
            _presenter?.ClearNotice();
            Render();
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

    private void OnCellPressed(CellNodes cell)
    {
        _presenter?.Open(cell.Item);
        Render();
    }

    private void OnMergeCandidatePressed(CellNodes cell)
    {
        _presenter?.ToggleMergePick(cell.Item);
        Render();
    }

    private void OnSortPressed()
    {
        _presenter?.CycleOrder();
        Render();
    }

    private void OnFilterPressed()
    {
        _presenter?.ClearSlotFilter();
        Render();
    }

    private void OnRetryPressed() => _ = StartAsync();

    private void OnSelectPressed()
    {
        _presenter?.EnterSelectMode();
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

    /// <summary>The scrim and the sheet's own close control do the same thing: close the sheet.</summary>
    private void OnScrimPressed()
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
    /// faces — a held item, an unpicked candidate — take a fainter blend. Each (band, muted, face)
    /// stylebox is built once and shared by every control that wears it.
    /// </remarks>
    private void PaintFace(Button button, Rarity rarity, bool muted)
    {
        foreach (var face in ButtonFaces)
        {
            if (PaintedFace(button, rarity, muted, face) is { } painted)
            {
                button.AddThemeStyleboxOverride(face, painted);
            }
        }
    }

    private StyleBoxFlat? PaintedFace(Button button, Rarity rarity, bool muted, StringName face)
    {
        if (_paintedFaces.TryGetValue((rarity, muted, face), out var cached))
        {
            return cached;
        }

        // The theme's own face is the template, read through the control before any override lands
        // on it — the button's variation decides which face, the band decides the tint.
        if (button.GetThemeStylebox(face) is not StyleBoxFlat original || original.Duplicate() is not StyleBoxFlat painted)
        {
            return null;
        }

        var (role, _, corners) = Band(rarity);
        var accent = HomeAccents.Of(_bands, role);

        painted.BorderColor = muted ? accent.Lerp(original.BorderColor, 0.55f) : accent;
        painted.BgColor = accent.Lerp(original.BgColor, muted ? 0.9f : 0.78f);
        painted.CornerRadiusTopLeft = corners.X;
        painted.CornerRadiusTopRight = corners.Y;
        painted.CornerRadiusBottomRight = corners.Z;
        painted.CornerRadiusBottomLeft = corners.W;

        _paintedFaces[(rarity, muted, face)] = painted;

        return painted;
    }

    /// <summary>Takes a band's paint off a button, so it draws the theme's own face again.</summary>
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

        if (!_paintedGems.TryGetValue(rarity, out var band))
        {
            if (gem.GetThemeStylebox(PanelStyle) is not StyleBoxFlat face || face.Duplicate() is not StyleBoxFlat painted)
            {
                return;
            }

            painted.BgColor = HomeAccents.Of(_bands, role);
            painted.CornerRadiusTopLeft = corners.X;
            painted.CornerRadiusTopRight = corners.Y;
            painted.CornerRadiusBottomRight = corners.Z;
            painted.CornerRadiusBottomLeft = corners.W;

            band = painted;
            _paintedGems[rarity] = band;
        }

        gem.AddThemeStyleboxOverride(PanelStyle, band);
    }

    /// <summary>
    /// Each band's theme role, its letter, and its corner SHAPE — five shapes, so a player who reads
    /// none of the hues apart still tells the bands: square, round, leaf, drop, lozenge.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A band this screen was never taught — it is not painted as another.</exception>
    private static (string Role, string Symbol, Vector4I Corners) Band(Rarity rarity) => rarity switch
    {
        Rarity.C => (RarityRolePrefix + "c", "C", new Vector4I(6, 6, 6, 6)),
        Rarity.B => (RarityRolePrefix + "b", "B", new Vector4I(60, 60, 60, 60)),
        Rarity.A => (RarityRolePrefix + "a", "A", new Vector4I(48, 6, 48, 6)),
        Rarity.S => (RarityRolePrefix + "s", "S", new Vector4I(6, 6, 48, 48)),
        Rarity.SS => (RarityRolePrefix + "ss", "SS", new Vector4I(30, 60, 30, 60)),
        _ => throw new ArgumentOutOfRangeException(
            nameof(rarity), rarity, "this band has no role, letter or shape here; teach the screen the band before it draws it."),
    };

    /// <summary>One stat line: the name, the figure, and the comparison in its accent.</summary>
    private HBoxContainer StatRow(GearStatLine line)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };

        row.AddThemeConstantOverride(SeparationConstant, 18);

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

        label.AddThemeColorOverride(FontColourOverride, HomeAccents.Of(_bands, sign > 0 ? HomeAccents.Gain : LossRole));
    }

    /// <summary>Empties a container and refills it, one control per item.</summary>
    private static void Fill<T>(Node? container, IReadOnlyList<T> items, Func<T, Control> build)
    {
        if (container is null || !IsInstanceValid(container))
        {
            return;
        }

        Clear(container);

        foreach (var item in items)
        {
            container.AddChild(build(item));
        }
    }

    private static void Show(Control? control, bool visible)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.Visible = visible;
        }
    }

    private static void Enable(Button? button, bool enabled)
    {
        if (button is not null && IsInstanceValid(button))
        {
            button.Disabled = !enabled;
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

    /// <summary>Writes a line, and hides the label when there is nothing to say: an empty label still claims its height.</summary>
    private static void WriteOrHide(Label? label, string text)
    {
        if (label is not null && IsInstanceValid(label))
        {
            label.Text = text;
            label.Visible = text.Length > 0;
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

    /// <summary>One slot tile's nodes, resolved once when the tile is built.</summary>
    private sealed record SlotTileNodes(
        Button Button, Label Caption, PanelContainer Gem, Label GemGlyph, Label Plus, TextureRect Mark);

    /// <summary>One cell's nodes, resolved once when the cell is built, and the item it currently draws.</summary>
    private sealed record CellNodes(
        Button Button,
        TextureRect Glyph,
        PanelContainer Gem,
        Label GemGlyph,
        Label Plus,
        TextureRect Mark,
        TextureRect Upgrade,
        TextureRect Check)
    {
        /// <summary>The item this cell draws now — reassigned on every read the cell survives.</summary>
        public InventoryItemView Item { get; set; } = null!;
    }
}
