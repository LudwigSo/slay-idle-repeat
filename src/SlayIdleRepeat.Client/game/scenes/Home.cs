using System.Globalization;
using Godot;
using SlayIdleRepeat.Application.Services;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S03 — the home screen, and the hub a session starts and ends in: a driving adapter over
/// <see cref="HomePresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards presses. No rules, no ports, no adapters, and no
/// arithmetic of its own: the boot screen composes the presenters and hands them over,
/// <see cref="HomeLayout"/> decides where every band and target sits, and this half owns only the
/// things that genuinely need the engine — the safe-area query, the drawn ground, the controls, the
/// two animations and the handovers.
/// </para>
/// <para>
/// 🔒 <b>Four bands, and only one of them flexes.</b> A top bar of resource pills, the hero band the
/// 3D diorama shows through, the launch block that starts a run, and the five-tab bar. The three
/// outer heights are exported below and handed to <see cref="HomeLayout"/>; the hero band takes
/// whatever is left, so a taller handset gives its extra height to the diorama and a shorter one
/// takes it away. A pinned viewport would push the primary action off the bottom of a 9:20 screen.
/// </para>
/// <para>
/// 🔒 <b>Every number this screen is laid out from is an <c>[Export]</c> on <c>Home.tscn</c></b>, for
/// the reason <c>HomeLayoutMetrics</c> records at length: a layout constant baked into the renderer
/// is a design decision hidden where no reviewer looks for it.
/// </para>
/// <para>
/// 🔒 <b>Not one colour is written here.</b> The action ember, the energy sky and the gain jade are
/// named entries in <c>SlayTheme.tres</c>, read back through the theme by role. A hard-coded colour
/// wins over the theme silently, so retuning the theme would leave exactly the controls coloured in
/// code looking like the old palette.
/// </para>
/// <para>
/// ⚠️ <b>Both animations are skippable and the screen is complete without them.</b> The count-up and
/// the gain flash are turned off together by <c>reducedMotion</c>, which arrives from the
/// composition root; with them off the pills draw their settled values immediately and nothing on
/// the screen is unreadable or unreachable. ⚠️ Nothing WRITES that preference yet — no settings
/// screen exists — so every shipped build composes it false. That gap is the Settings screen's, and
/// it is the same gap <c>BattleComposition</c> and <c>MinigameComposition</c> already carry.
/// </para>
/// <para>
/// ⚠️ <b>Eleven destinations on this screen log and go nowhere.</b> Three tabs (Talents, Collection,
/// Shop), the three side-rail entries, the settings control, the three resource sheets and the
/// refill offer are all systems this build has no screen for. Each prints one greppable line rather
/// than doing nothing at all, so a tap that led nowhere is visible in a log instead of reading as a
/// dead control. The two that DO have screens are wired: the Gear tab opens the inventory, and the
/// stage card's Change opens the chapter picker.
/// </para>
/// <para>
/// ⚠️ <b>The hero diorama is a modelled rogue, and it is still not a RULED asset.</b>
/// <c>Hero.tscn</c> frames <c>game/art/chr_hero_rogue.glb</c>, whose sockets carry the two weapons
/// this screen states the starting loadout with. It carries no outline shell, no per-actor light rig
/// and no manifest row: the three §C6 rulings D61 reopened still owe themselves answers, and a kit
/// that grows makes that debt grow with it rather than discharging any of it.
/// </para>
/// <para>
/// 🔒 <b>It carries no light and no environment of its own.</b> A viewport has one
/// <see cref="WorldEnvironment"/> and this build's lives on <see cref="AppRoot"/> for the life of the
/// application, with the key light beside it — the same reason <see cref="ScreenStage"/> gives for
/// keeping it off the screens.
/// </para>
/// <para>
/// Both live decisions reach a screen: START opens the board the run it started is played on, and
/// CONTINUE opens the board the run already open is played on — unless the read named no run to
/// resume, which is reported rather than guessed at. See <see cref="TheRunToResumeWasNotNamed"/>.
/// </para>
/// </remarks>
public partial class Home : Node3D
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/Home.tscn";

    /// <summary>
    /// ⚠️ Named so a resume that cannot happen is still reported. The decision to resume is taken
    /// from the run the profile read named, and a read that answered <c>ContinueRun</c> without
    /// naming one is a state nothing should paper over. Nothing is navigated to in that case: a
    /// board opened on a run nobody named would read the wrong run, or none.
    /// </summary>
    private const string TheRunToResumeWasNotNamed =
        "The profile read answered that a run is open but carried no run id, so there is nothing " +
        "to resume onto. The decision above is reported, not taken.";

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a decision
    /// reached against real content has to be greppable out of an engine log full of everything
    /// else, the way the boot's cold-start measurement already is.
    /// </summary>
    private const string HomeMarker = "SIR_HOME_READY";

    /// <summary>
    /// The one line every destination this build has no screen for prints.
    /// </summary>
    /// <remarks>
    /// 🔒 One marker for all eleven, so the set can be counted with one search rather than
    /// rediscovered by tapping. A stub that printed nothing is indistinguishable from a control that
    /// is wired and broken.
    /// </remarks>
    private const string StubMarker = "SIR_HOME_STUB";

    /// <summary>The button variation each colour role resolves to.</summary>
    private const string ActionButtonVariation = "ActionButton";
    private const string EnergyButtonVariation = "EnergyButton";
    private const string QuietActionVariation = "PrimaryButton";

    /// <summary>The stage card's two looks: its own, and the one it wears under the recommendation.</summary>
    private const string StageCardVariation = "StageCard";
    private const string StageCardWarnedVariation = "StageCardWarned";

    private const string MarginTopConstant = "margin_top";
    private const string MarginBottomConstant = "margin_bottom";

    private const string BandsPath = "%Bands";
    private const string TopBarPath = "%TopBar";
    private const string AvatarLevelPath = "%AvatarLevel";
    private const string CrownsPillPath = "%CrownsPill";
    private const string EnergyPillPath = "%EnergyPill";
    private const string PowerPillPath = "%PowerPill";
    private const string SettingsButtonPath = "%SettingsButton";
    private const string HeroNamePath = "%HeroName";
    private const string HeroCaptionPath = "%HeroCaption";
    private const string MailButtonPath = "%MailButton";
    private const string RankingButtonPath = "%RankingButton";
    private const string QuestsButtonPath = "%QuestsButton";
    private const string LaunchPath = "%Launch";
    private const string StageCardPath = "%StageCard";
    private const string StageTextPath = "%StageText";
    private const string StageTitlePath = "%StageTitle";
    private const string StageSubtitlePath = "%StageSubtitle";
    private const string NoticeLabelPath = "%NoticeLabel";
    private const string ChangeButtonPath = "%ChangeButton";
    private const string StartButtonPath = "%StartButton";
    private const string CostBadgePath = "%CostBadge";
    private const string RewardLinePath = "%RewardLine";
    private const string TabDockPath = "%TabDock";
    private const string TabMarginsPath = "%Margins";
    private const string TabBarPath = "%TabBar";

    /// <summary>The tab this screen IS. It is never a destination to hand over to.</summary>
    private const HomeTab ThisTab = HomeTab.Home;

    // ------------------------------------------------------------------ the layout's numbers

    /// <summary>The top bar's own height, before the top safe inset is added to it.</summary>
    [Export] public float TopBarHeight { get; set; } = 192f;

    /// <summary>The launch block's height.</summary>
    [Export] public float LaunchHeight { get; set; } = 468f;

    /// <summary>The tab row's height, before the bottom safe inset is added to it.</summary>
    [Export] public float TabBarHeight { get; set; } = 198f;

    /// <summary>The padding at each end of the top bar.</summary>
    [Export] public float TopBarSidePadding { get; set; } = 36f;

    /// <summary>The gap between two neighbouring items in the top bar.</summary>
    [Export] public float TopBarItemGap { get; set; } = 18f;

    /// <summary>The avatar's width.</summary>
    [Export] public float AvatarWidth { get; set; } = 108f;

    /// <summary>The settings glyph as drawn. Its TARGET is never smaller than a thumb.</summary>
    [Export] public float SettingsGlyphWidth { get; set; } = 90f;

    /// <summary>The padding at each end of one pill.</summary>
    [Export] public float PillHorizontalPadding { get; set; } = 27f;

    /// <summary>A resource glyph's width inside a pill.</summary>
    [Export] public float PillIconWidth { get; set; } = 48f;

    /// <summary>The gap between a pill's icon, its value and its caption.</summary>
    [Export] public float PillInnerGap { get; set; } = 15f;

    /// <summary>
    /// ⚠️ The width budget one character of a pill's value must come in under.
    /// </summary>
    /// <remarks>
    /// A BUDGET, not a measured face: nothing in the design set authors a font metric, and the
    /// bundled face is still the engine's default. It is exported so the number a reviewer can
    /// check sits beside the layout it decides rather than inside it.
    /// </remarks>
    [Export] public float PillValueCharacterAdvance { get; set; } = 15f;

    /// <inheritdoc cref="PillValueCharacterAdvance"/>
    [Export] public float PillCaptionCharacterAdvance { get; set; } = 12f;

    /// <summary>The padding at each end of the launch block.</summary>
    [Export] public float LaunchSidePadding { get; set; } = 42f;

    /// <summary>The gap between two of the launch block's three rows.</summary>
    [Export] public float LaunchRowGap { get; set; } = 30f;

    /// <summary>The stage card's height.</summary>
    [Export] public float StageCardHeight { get; set; } = 156f;

    /// <summary>The primary button's height.</summary>
    [Export] public float StartButtonHeight { get; set; } = 168f;

    /// <summary>The reward line's height.</summary>
    [Export] public float RewardLineHeight { get; set; } = 78f;

    /// <summary>The Change control as drawn — see <see cref="SettingsGlyphWidth"/>.</summary>
    [Export] public float ChangeButtonWidth { get; set; } = 96f;

    /// <inheritdoc cref="ChangeButtonWidth"/>
    [Export] public float ChangeButtonHeight { get; set; } = 78f;

    private HomePresenter? _presenter;
    private ChapterSelectPresenter? _picker;
    private Func<RunId, ComposedBoardScreen>? _board;
    private Func<ComposedInventoryScreen>? _gear;
    private bool _reducedMotion;
    private CancellationToken _lifetime;

    private Control? _bands;
    private MarginContainer? _topBar;
    private Label? _avatarLevel;
    private ResourcePill? _crownsPill;
    private ResourcePill? _energyPill;
    private ResourcePill? _powerPill;
    private Button? _settingsButton;
    private Label? _heroName;
    private Label? _heroCaption;
    private Button? _mailButton;
    private Button? _rankingButton;
    private Button? _questsButton;
    private Control? _launch;
    private PanelContainer? _stageCard;
    private Control? _stageText;
    private Label? _stageTitle;
    private Label? _stageSubtitle;
    private Label? _noticeLabel;
    private Button? _changeButton;
    private Button? _startButton;
    private Label? _costBadge;
    private Label? _rewardLine;
    private Control? _tabDock;
    private MarginContainer? _tabMargins;
    private TabBar? _tabBar;

    /// <summary>Every measurement this screen is laid out from, as the exports state them.</summary>
    private HomeLayoutMetrics Metrics => new(
        new HomeBandMetrics(TopBarHeight, LaunchHeight, TabBarHeight),
        new HomeTopBarMetrics(TopBarSidePadding, TopBarItemGap, AvatarWidth, SettingsGlyphWidth),
        new HomePillMetrics(
            PillHorizontalPadding,
            PillIconWidth,
            PillInnerGap,
            PillValueCharacterAdvance,
            PillCaptionCharacterAdvance),
        new HomeLaunchMetrics(
            LaunchSidePadding,
            LaunchRowGap,
            StageCardHeight,
            StartButtonHeight,
            RewardLineHeight,
            ChangeButtonWidth,
            ChangeButtonHeight));

    /// <summary>
    /// Takes both presenters the composition root built, and the token the app shuts down through.
    /// </summary>
    /// <remarks>
    /// The picker's presenter arrives here rather than being built on the press, because it reads
    /// the same profile this screen does and a read started by a tap is a tap that waits. This
    /// screen starts it and forwards it; it never composes it.
    /// </remarks>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="picker">Drives the screen the stage card's Change control opens.</param>
    /// <param name="board">
    /// Builds the board for a run. A factory rather than a presenter, because which run this screen
    /// opens is not known until the profile read answers — or until a <c>START_RUN</c> comes back.
    /// </param>
    /// <param name="gear">Builds the Inventory screen the Gear tab opens.</param>
    /// <param name="reducedMotion">Whether the count-up and the gain flash are skipped.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public void Drive(
        HomePresenter presenter,
        ChapterSelectPresenter picker,
        Func<RunId, ComposedBoardScreen> board,
        Func<ComposedInventoryScreen> gear,
        bool reducedMotion,
        CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(gear);

        _presenter = presenter;
        _picker = picker;
        _board = board;
        _gear = gear;
        _reducedMotion = reducedMotion;
        _lifetime = lifetime;
    }

    /// <summary>Shows this screen again and reads the profile afresh, for a run that has ended.</summary>
    /// <remarks>
    /// 🔒 <b>The read is the point, not the showing.</b> A run that has ended banked its payout and
    /// closed itself, so the profile behind this screen is a different row from the one it drew: the
    /// Legend Level, both Energy amounts and the chapter the campaign offers next have all moved,
    /// and the launch block has moved with them. Un-hiding without reading again would offer to
    /// resume the run that has just ended, onto a board that has been freed. This screen is never
    /// freed, which is what makes it the thing to come back to.
    /// </remarks>
    public void Resume()
    {
        // Validity before tree membership: asking a freed node whether it is inside the tree is itself
        // the crash, and a run that ends as the application shuts down is the ordinary case here.
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        ScreenStage.Show(this);

        _ = StartAsync();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws whenever either read behind it moves.
        _bands = GetNode<Control>(BandsPath);
        _topBar = GetNode<MarginContainer>(TopBarPath);
        _avatarLevel = GetNode<Label>(AvatarLevelPath);
        _crownsPill = GetNode<ResourcePill>(CrownsPillPath);
        _energyPill = GetNode<ResourcePill>(EnergyPillPath);
        _powerPill = GetNode<ResourcePill>(PowerPillPath);
        _settingsButton = GetNode<Button>(SettingsButtonPath);
        _heroName = GetNode<Label>(HeroNamePath);
        _heroCaption = GetNode<Label>(HeroCaptionPath);
        _mailButton = GetNode<Button>(MailButtonPath);
        _rankingButton = GetNode<Button>(RankingButtonPath);
        _questsButton = GetNode<Button>(QuestsButtonPath);
        _launch = GetNode<Control>(LaunchPath);
        _stageCard = GetNode<PanelContainer>(StageCardPath);
        _stageText = GetNode<Control>(StageTextPath);
        _stageTitle = GetNode<Label>(StageTitlePath);
        _stageSubtitle = GetNode<Label>(StageSubtitlePath);
        _noticeLabel = GetNode<Label>(NoticeLabelPath);
        _changeButton = GetNode<Button>(ChangeButtonPath);
        _startButton = GetNode<Button>(StartButtonPath);
        _costBadge = GetNode<Label>(CostBadgePath);
        _rewardLine = GetNode<Label>(RewardLinePath);
        _tabDock = GetNode<Control>(TabDockPath);
        _tabMargins = GetNode<MarginContainer>(TabMarginsPath);
        _tabBar = GetNode<TabBar>(TabBarPath);

        _startButton.Pressed += OnStartPressed;
        _changeButton.Pressed += OnChangePressed;
        _settingsButton.Pressed += OnSettingsPressed;
        _mailButton.Pressed += OnMailPressed;
        _rankingButton.Pressed += OnRankingPressed;
        _questsButton.Pressed += OnQuestsPressed;
        _crownsPill.ResourceTapped += OnResourceTapped;
        _energyPill.ResourceTapped += OnResourceTapped;
        _powerPill.ResourceTapped += OnResourceTapped;
        _crownsPill.Held += OnPillHeld;
        _energyPill.Held += OnPillHeld;
        _powerPill.Held += OnPillHeld;
        _tabBar.TabSelected += OnTabSelected;

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after — so two cameras and two overlays
        // are alive at the moment this one becomes the visible screen.
        ScreenStage.Show(this);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>. The controls are children and die
    /// with this node either way, but a handler left connected across a scene that is merely
    /// detached and re-added would fire twice, and once is the whole contract of a primary action.
    /// </remarks>
    public override void _ExitTree()
    {
        Disconnect(_startButton, OnStartPressed);
        Disconnect(_changeButton, OnChangePressed);
        Disconnect(_settingsButton, OnSettingsPressed);
        Disconnect(_mailButton, OnMailPressed);
        Disconnect(_rankingButton, OnRankingPressed);
        Disconnect(_questsButton, OnQuestsPressed);

        foreach (var pill in new[] { _crownsPill, _energyPill, _powerPill })
        {
            if (pill is not null && IsInstanceValid(pill))
            {
                pill.ResourceTapped -= OnResourceTapped;
                pill.Held -= OnPillHeld;
            }
        }

        // The presenter outlives this node — it is the composition root's, and Resume brings the
        // screen back over the same one. A reveal left standing would come back with the figures a
        // finger once held for and nobody holding anything.
        _presenter?.ConcealFullValues();

        if (_tabBar is not null && IsInstanceValid(_tabBar))
        {
            _tabBar.TabSelected -= OnTabSelected;
        }
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a screen that silently stopped moving.
    /// The reads run one after the other rather than together — they go to the same host over the
    /// same local cache, and starting a second before the first answers buys nothing here.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            var presenter = _presenter;
            var picker = _picker;

            if (presenter is null || picker is null)
            {
                GD.PushError(
                    "The home screen entered the tree with no presenter. Only the boot screen may " +
                    "instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.RefreshAsync(_lifetime);
            await picker.StartAsync(_lifetime);

            Render();
            Report(presenter, picker);
        }
        catch (Exception failure)
        {
            GD.PushError($"The home screen stopped unexpectedly: {failure}");
        }
    }

    /// <summary>
    /// Re-reads the screen, for the retry a failed read offers and for a run that has just ended.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Both models, never one.</b> This used to re-read the launch block alone, which left the
    /// header's decision at whatever the last pass said: a Retry that succeeded came back under the
    /// notice line the header's failed read had put there, and a run that had just ended came back
    /// still offering to resume it.
    /// </remarks>
    private async Task ReloadAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                return;
            }

            await presenter.RefreshAsync(_lifetime);

            Render();
        }
        catch (Exception failure)
        {
            GD.PushError($"The home screen's launch block stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow read is the ordinary case on a handset.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() || _bands is null)
        {
            return;
        }

        Reband();
        RenderTopBar(presenter);
        RenderHeroBand(presenter);
        RenderLaunch(presenter);

        _tabBar?.Show(
            presenter.TabLabel,
            presenter.TabBadgeOn,
            HomeAccents.Of(_bands, HomeAccents.Action),
            ThisTab);
    }

    /// <summary>
    /// Gives each band the height <see cref="HomeLayout"/> says it has, and each safe inset the
    /// band it belongs inside.
    /// </summary>
    /// <remarks>
    /// 🔒 The top inset grows the TOP BAR and moves its row down; the bottom inset grows the TAB
    /// DOCK and leaves the row above it alone. A bar of fixed total height that subtracted the inset
    /// from itself would look right on a handset with a small gesture bar and shrink five targets
    /// below a thumb on one without.
    /// </remarks>
    private void Reband()
    {
        var canvas = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var insets = SafeAreaInsets.Resolve(canvas) ?? Presenters.SafeAreaInsets.None;

        HomeLayout layout;

        try
        {
            layout = HomeLayout.For(canvas.X, canvas.Y, insets, Metrics);
        }
        catch (ArgumentOutOfRangeException refused)
        {
            // A canvas too short for the pinned bands. The containers still stack, so the screen is
            // drawn rather than blank — what is lost is the exact banding, and that is worth saying.
            GD.PushWarning($"The home screen could not be banded for this viewport: {refused.Message}");

            return;
        }

        Height(_topBar, layout.TopBar.Height);
        _topBar?.AddThemeConstantOverride(MarginTopConstant, (int)insets.Top);

        Height(_launch, layout.Launch.Height);
        Height(_stageCard, layout.StageCard.Height);
        Height(_startButton, layout.StartButton.Height);
        Height(_rewardLine, layout.RewardLine.Height);

        Target(_settingsButton, layout.SettingsButton);
        Target(_changeButton, layout.ChangeButton);

        Height(_tabDock, layout.TabBar.Height);
        _tabMargins?.AddThemeConstantOverride(MarginBottomConstant, (int)insets.Bottom);
    }

    private void RenderTopBar(HomePresenter presenter)
    {
        Write(_avatarLevel, presenter.HubLevelText);
        Write(_settingsButton, presenter.SettingsLabel);

        var pill = Metrics.Pill;

        _crownsPill?.Show(
            presenter.HubCrowns, presenter.CrownsPillTextFor, "", pill, _reducedMotion);
        _energyPill?.Show(
            presenter.HubEnergy,
            presenter.EnergyPillTextFor,
            presenter.EnergyCaptionVisible ? presenter.EnergyPillCaption : "",
            pill,
            _reducedMotion);
        _powerPill?.Show(
            presenter.HubPower, presenter.PowerPillTextFor, "", pill, _reducedMotion);

        // 🔒 The gain accent is a flourish on a number that already says it. With motion reduced it
        // simply does not happen, and the pill still reads the higher figure.
        if (presenter.PowerRose)
        {
            _powerPill?.Flash(HomeAccents.Of(_bands, HomeAccents.Gain), _reducedMotion);
        }
    }

    private void RenderHeroBand(HomePresenter presenter)
    {
        Write(_heroName, presenter.HubPlayerName);
        Write(_heroCaption, presenter.HeroCaption);
        Write(_mailButton, presenter.RailLabel(HomeRailEntry.Mail));
        Write(_rankingButton, presenter.RailLabel(HomeRailEntry.Ranking));
        Write(_questsButton, presenter.RailLabel(HomeRailEntry.Quests));
    }

    /// <remarks>
    /// 🔒 The three rows stay on the page in every state. What changes inside the stage card is
    /// which of its two texts is showing: the stage the campaign offers next, or the one sentence
    /// that says why there is nothing to offer. A fourth row for the second would move everything
    /// under it the moment a read faulted.
    /// </remarks>
    private void RenderLaunch(HomePresenter presenter)
    {
        var notice = presenter.Notice;
        var sayingSomethingWentWrong = notice.Length > 0;

        Write(_noticeLabel, notice);
        Show(_noticeLabel, sayingSomethingWentWrong);
        Show(_stageText, !sayingSomethingWentWrong);
        Show(_changeButton, !sayingSomethingWentWrong);

        Write(_stageTitle, presenter.StageTitle);
        Write(_stageSubtitle, presenter.StageSubtitle);
        Write(_changeButton, presenter.ChangeLabel);
        Write(_rewardLine, presenter.RewardLineText);

        if (_stageCard is not null && IsInstanceValid(_stageCard))
        {
            _stageCard.ThemeTypeVariation = presenter.StageCardWarned
                ? StageCardWarnedVariation
                : StageCardVariation;
        }

        // 🔒 Which of the two state models the button answers to is the PRESENTER's rule, not this
        // scene's: a run already open outranks every launch state, and the argument for it lives on
        // HomePrimaryAction where a test can hold it. This half only draws what it was told.
        Write(_startButton, presenter.PrimaryActionLabel);
        Write(_costBadge, BadgeText(presenter.PrimaryActionBadge));

        if (_startButton is not null && IsInstanceValid(_startButton))
        {
            _startButton.ThemeTypeVariation = VariationFor(presenter.PrimaryActionColour);

            // Disabled rather than hidden while the read is still out: a primary action that
            // vanishes reads as a screen that lost its purpose, while a disabled one reads as a
            // screen waiting, which is what it is.
            _startButton.Disabled = presenter.PrimaryAction == HomePrimaryAction.Wait;
        }
    }

    /// <summary>
    /// What the badge on the primary button says.
    /// </summary>
    /// <remarks>
    /// 🔒 A placeholder says NOTHING rather than a number: a price quoted before the read answers is
    /// a figure the screen invented. The badge's width is the scene's, so a blank one does not let
    /// the button's word slide across when the read lands.
    /// </remarks>
    private static string BadgeText(HomeCostBadge badge) => badge.Kind switch
    {
        HomeCostBadgeKind.Price or HomeCostBadgeKind.Shortfall => PlayerNumber.Full(badge.Amount),
        _ => "",
    };

    private static string VariationFor(HomeColourRole role) => role switch
    {
        HomeColourRole.Action => ActionButtonVariation,
        HomeColourRole.Energy => EnergyButtonVariation,
        _ => QuietActionVariation,
    };

    // ------------------------------------------------------------------------------ the presses

    /// <remarks>
    /// Five outcomes and each is its own. A run already open is resumed; a startable state submits
    /// one; a refill offer opens the sheet that sells Energy; a failed read is retried; and a read
    /// still out does nothing, which is what its disabled button already says.
    /// </remarks>
    private void OnStartPressed()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        switch (presenter.PrimaryAction)
        {
            case HomePrimaryAction.Resume:
                if (presenter.ContinuableRun is not { } run)
                {
                    GD.PushError($"Continue was taken · {TheRunToResumeWasNotNamed}");

                    return;
                }

                ShowBoard(run);
                break;

            case HomePrimaryAction.StartRun:
                _ = StartRunAsync();
                break;

            case HomePrimaryAction.OfferRefill:
                Stub("refill_sheet");
                break;

            case HomePrimaryAction.Retry:
                _ = ReloadAsync();
                break;

            default:
                break;
        }
    }

    /// <remarks>
    /// 🔒 The double tap is refused inside the presenter, before the await, and this method simply
    /// gets a null back for the second press. A screen that latched here instead would still let
    /// two presses through on the frame the first one starts.
    /// </remarks>
    private async Task StartRunAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                return;
            }

            var outcome = await presenter.PressStartAsync(_lifetime);

            if (outcome is null)
            {
                // A press that started nothing may still have MOVED the block: the seam raises a
                // refusal it has no sentence for — a run opened on another device, most plainly —
                // and the presenter settles into its failure state for it. Returning without
                // redrawing would leave the button reading Start with nothing behind it.
                Render();

                return;
            }

            if (outcome.Result == StartRunResult.Started && outcome.Run is { } run)
            {
                ShowBoard(run);

                return;
            }

            // Refused. The row the refusal was decided on is the one to redraw from, so the block
            // says why rather than repeating the offer it just made.
            await ReloadAsync();
        }
        catch (Exception failure)
        {
            GD.PushError($"The home screen could not start a run: {failure}");
        }
    }

    private void OnChangePressed() => ShowChapterSelect();

    private void OnSettingsPressed() => Stub("settings");

    private void OnMailPressed() => Stub("rail_mail");

    private void OnRankingPressed() => Stub("rail_ranking");

    private void OnQuestsPressed() => Stub("rail_quests");

    private void OnResourceTapped(HudIcon resource) =>
        Stub("resource_sheet_" + resource.ToString().ToLowerInvariant());

    /// <summary>A pill held down shows the screen's figures in full; letting go shortens them again.</summary>
    /// <remarks>
    /// 🔒 One state for all three pills, not one per pill: the gesture asks "how many, exactly?"
    /// of the whole screen, and the presenter answers it once. Which form each number then takes is
    /// the presenter's rule, so what this file forwards is the single fact the engine event carries.
    /// </remarks>
    /// <param name="held">Whether a finger is holding a pill.</param>
    private void OnPillHeld(bool held)
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        if (held)
        {
            presenter.RevealFullValues();
        }
        else
        {
            presenter.ConcealFullValues();
        }

        Render();
    }

    /// <remarks>
    /// The tab this screen already IS goes nowhere on purpose — a navigation control that reloads
    /// the screen the player is looking at is a control that appears to do nothing but throws a
    /// read away.
    /// </remarks>
    private void OnTabSelected(HomeTab tab)
    {
        switch (tab)
        {
            case ThisTab:
                break;

            case HomeTab.Gear:
                OpenGear();
                break;

            default:
                Stub("tab_" + tab.ToString().ToLowerInvariant());
                break;
        }
    }

    /// <summary>Opens S16, the gear stock and the equip path.</summary>
    /// <remarks>
    /// 🔒 <b>Between runs is the only place this belongs, and Home is where a player already is.</b>
    /// A run freezes its loadout at <c>START_RUN</c>, so a bag opened mid-run could change nothing
    /// about the fight in progress.
    /// </remarks>
    private void OpenGear()
    {
        if (_gear is not { } compose)
        {
            return;
        }

        _ = InventoryHandover.Show(this, compose(), _lifetime);
    }

    /// <summary>Says, on one greppable line, that a destination was asked for and does not exist.</summary>
    private static void Stub(string destination) =>
        GD.Print($"{StubMarker} destination={destination} state=no_screen_yet");

    // ---------------------------------------------------------------------------- the handovers

    /// <summary>Puts the board for one run beside this screen and stands down.</summary>
    /// <remarks>
    /// 🔒 This instantiates unconditionally, and that is the point rather than a caveat: a run that
    /// ends frees its board, so the next run builds a board of its own with its own presenters and
    /// its own read. This screen is passed twice — once as the screen standing down and once as the
    /// starting menu the run's ending leads back to — and here they are the same node.
    /// </remarks>
    private void ShowBoard(RunId run)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        if (_board is not { } board)
        {
            GD.PushError(
                $"A run {run} was opened and this screen has no way to build its board. Only the " +
                "boot screen may instantiate it, and it must pass the board factory.");

            return;
        }

        BoardHandover.Show(this, this, board(run), _lifetime);
    }

    /// <summary>Puts the chapter picker beside this screen and stands down.</summary>
    /// <remarks>
    /// <para>
    /// The picker is added to this screen's own parent rather than to this screen, and this screen
    /// is hidden — the same handover shape the application root uses for the boot screen. A child
    /// would be drawn inside a ground this screen still owns, and freeing the outgoing screen from
    /// inside its own handler is a node destroying the object the call is running on.
    /// </para>
    /// <para>
    /// 🔒 <b>This method instantiates unconditionally, and the picker is what makes that safe: it
    /// FREES ITSELF once it has handed a board over.</b> A back path exists — a run that ends comes
    /// back here — so a picker that stayed in the tree would be joined by a second one on the next
    /// Change, with its own presenter and its own read, and one more on every run after that.
    /// </para>
    /// </remarks>
    private void ShowChapterSelect()
    {
        if (!IsInstanceValid(this) || !IsInsideTree() || _picker is not { } picker || _board is null)
        {
            return;
        }

        var parent = GetParent();

        if (parent is null)
        {
            GD.PushError("The home screen has no parent to hand the chapter picker to.");

            return;
        }

        var scene = GD.Load<PackedScene>(ChapterSelect.ScenePath);

        if (scene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The chapter picker could not be loaded from '{ChapterSelect.ScenePath}'.");

            return;
        }

        var picked = scene.Instantiate<ChapterSelect>();

        picked.Drive(picker, _board, this, _lifetime);

        ScreenStage.Hide(this);

        parent.AddChild(picked);
    }

    // ------------------------------------------------------------------------ the small writers

    /// <summary>
    /// Writes one control's text, or nothing when the control is not there to write into.
    /// </summary>
    /// <remarks>
    /// A scene-unique name that no longer resolves leaves a null behind, and a null-forgiving
    /// operator over it would turn a renamed node into a crash here instead of a blank label. Every
    /// write goes through this so the check is stated once.
    /// </remarks>
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

    private static void Show(Control? control, bool visible)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.Visible = visible;
        }
    }

    /// <summary>Pins one control's height, leaving whatever width its container gives it.</summary>
    private static void Height(Control? control, float height)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X, height);
        }
    }

    /// <summary>Pins one control to the tap target the layout measured for it.</summary>
    private static void Target(Control? control, HomeRect target)
    {
        if (control is not null && IsInstanceValid(control))
        {
            control.CustomMinimumSize = new Vector2(target.Width, target.Height);
        }
    }

    private static void Disconnect(Button? button, Action handler)
    {
        if (button is not null && IsInstanceValid(button))
        {
            button.Pressed -= handler;
        }
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the content the build
    /// actually shipped — and what the picker it prepared resolved with it.
    /// </summary>
    /// <remarks>
    /// One line rather than two, because a launch produces exactly one of each and the interesting
    /// claim spans both: that composition, the content load, the profile read, the power reading,
    /// the energy projection and the gating evaluation all ran, in the engine, against real authored
    /// data. The picker's half is described by the picker's own file so the format lives beside the
    /// screen it is about.
    /// </remarks>
    private static void Report(HomePresenter home, ChapterSelectPresenter picker)
    {
        var power = home.Power?.ToString(CultureInfo.InvariantCulture) ?? "";

        GD.Print(
            $"{HomeMarker} decision={home.Decision} launch={home.LaunchState} " +
            $"legend_level={home.LegendLevel} energy={home.EnergyPillText} " +
            $"refill_in={home.EnergyPillCaption} crowns={home.CrownsPillText} " +
            $"power={power} power_standing={home.PowerStanding} " +
            $"stage={home.StageTitle} badge={home.CostBadge.Kind}:{home.CostBadge.Amount} " +
            $"chapters={picker.Chapters.Count} picker_stage={picker.Stage} " +
            $"verdicts=[{ChapterSelect.Describe(picker)}]");
    }
}
