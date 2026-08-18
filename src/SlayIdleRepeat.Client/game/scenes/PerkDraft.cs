using Godot;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content.Perks;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S07 — the perk draft a won battle opens: a driving adapter over <see cref="PerkDraftPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards five presses — three cards, the reroll and the
/// skip. No rules, no ports, no adapters, and above all no decision about what may be pressed: which
/// of them is live is the presenter's answer, and this half only draws it. That split is
/// load-bearing rather than stylistic here — there is no scene test harness in this repository, so
/// anything decided in this file is decided where nothing can check it.
/// </para>
/// <para>
/// 🔒 <b>This screen never advances itself.</b> Nothing here picks, skips or rerolls except a
/// player's press. It leaves for the board only once the draft the screen exists for has CLOSED —
/// which is a fact about the run, read back with the command's own outcome, not a timeout and not a
/// choice made on the player's behalf. A draft that chose for the player would spend the one
/// decision a run is actually made of.
/// </para>
/// <para>
/// 🔴 <b>Three separate absences share this screen and never share a sentence.</b> The quieter ad
/// reroll and the outlined fourth card are two different affordances waiting on the ad reward
/// system, each with its own line; the free-reroll allowance the design describes was never built at
/// all, and says so in a third. Both ad slots keep their layout so the milestone that lands them
/// fills a slot rather than re-laying out the screen.
/// </para>
/// <para>
/// 🔴 <b>A card whose numbers cannot be rendered says so</b> — the presenter answers with a named
/// line in place of a half-substituted sentence, and the tokens that stopped it go to the log rather
/// than to the player.
/// </para>
/// <para>
/// ⚠️ Every type size, colour, corner and outline in <c>PerkDraft.tscn</c> and
/// <c>PerkDraftCard.tscn</c> is a per-node override, because the shared theme resource does not
/// exist yet — it is M8-03's, and these overrides are debt owed to it rather than a naming scheme of
/// this screen's own. They have to be re-checked, not merely re-applied, when the real kit lands.
/// </para>
/// <para>
/// 🔴 <b>There is no art here, placeholder or otherwise.</b> The category strip, the rarity gem and
/// the icon slot are containers, coloured rectangles and typed characters the engine already
/// provides; the icon slot is drawn EMPTY because no atlas exists, and an empty slot is honest where
/// a stand-in glyph would not be — see <see cref="TheIconSlotIsEmptyBecauseThereIsNoArt"/>.
/// </para>
/// <para>
/// 🔒 <b>The three <c>DRAFT</c> luck-protection counters are drawn, always.</b> <c>24</c> §1.1's
/// Visibility rule is a 🔒 and its Disclosure rule is a store-policy requirement on both platforms;
/// the presenter answers the rows already resolved and already counted, and this half only lays them
/// out. They sit in the SCROLLING band with the offer they describe rather than in the bottom action
/// column: a disclosure is standing information a player can go and read, where the two status lines
/// above are transient answers about the last command, and four more rows of text pinned above the
/// primary action would push it out of the thumb's reach on the tallest supported screen.
/// </para>
/// <para>
/// 🔴 <b>Rarity is never carried by colour alone</b> — the gem takes a frame shape and a symbol as
/// well, so a player who cannot separate the four hues still reads four distinct marks. See
/// <see cref="RarityIsAShapeAndASymbolBeforeItIsAColour"/>.
/// </para>
/// </remarks>
public partial class PerkDraft : Control
{
    /// <summary>Where this scene lives, for the screen that instantiates it.</summary>
    public const string ScenePath = "res://game/scenes/PerkDraft.tscn";

    /// <summary>
    /// 🔴 Named so the dead end can be found. A draft screen whose own read did not answer has
    /// nothing to draw and no command it can legally submit, and there is no authored caption for a
    /// "back" control to give it — so it reports what happened and stops there rather than handing
    /// back to a board that would report the same failure with one screen less context.
    /// </summary>
    private const string TheUnreadableRunHasNowhereToGo =
        "The draft screen could not read the run it was opened for, so it has no cards to show and " +
        "no command it may submit. It stays and names the failure rather than handing back: the " +
        "board it came from reads the same row through the same host, so returning would move the " +
        "player one screen away from the message without changing the answer. There is no authored " +
        "caption for a back control, so none is drawn — a screen may not invent wording.";

    /// <summary>
    /// 🔴 Named so it can be found. Every card keeps the slot its icon will occupy and draws
    /// nothing in it, because the build ships no atlas at all and reports as much at boot.
    /// </summary>
    private const string TheIconSlotIsEmptyBecauseThereIsNoArt =
        "No atlas ships with this build and the client says so at boot, so there is no perk icon to " +
        "draw. The slot is kept at its full size and left empty rather than filled with a stand-in " +
        "glyph or a category-coloured square, both of which would read as an icon that had loaded.";

    /// <summary>
    /// ⚠️ This task's choice, and the only colours on this screen that no document authors. The art
    /// manifest authors a rarity ladder and biome palettes; nothing anywhere authors a perk-category
    /// palette, and the design asks the card for a category colour bar. These six are chosen here,
    /// against the card's own fill, and are M8-03's to re-check with the rest of the kit.
    /// </summary>
    private const string TheCategoryPaletteIsChosenHere =
        "No document in this repository authors a colour per perk category. The six below are this " +
        "screen's own, picked for mutual separation and for contrast against the card fill, and " +
        "kept clear of the authored rarity ladder so a category strip is never mistaken for a " +
        "rarity. They are theme debt like every other override here, not a content decision. Two of " +
        "them were moved after measurement: the economy gold sat 9 degrees of hue from the " +
        "legendary gem and the trigger green 18 from the rare one, and two content-keyed colour " +
        "systems that close on the same card are a legibility bug however different their meanings " +
        "are. Every category now sits at least 30 degrees from every rarity in the ladder.";

    /// <summary>
    /// 🔴 Named because it is an accessibility requirement rather than a style choice. Rarity is
    /// carried by three channels at once — the gem's fill, the shape of its frame, and the band's own
    /// letter drawn inside it — so the four bands stay four distinct marks for a player who reads
    /// none of the hues apart. A gem that was only a coloured square would fail that outright, and no
    /// atlas ships with this build to draw a real one, so the shape is a corner radius and the symbol
    /// is a typed character.
    /// </summary>
    private const string RarityIsAShapeAndASymbolBeforeItIsAColour =
        "The four perk rarities are drawn as a square, a circle, a cut-corner lens and a flat-topped " +
        "shield, each carrying the band's own letter in dark ink on its fill. Shape and letter are " +
        "both readable with the colour removed; the colour is the third channel, not the only one. A " +
        "band this build was never taught takes a rounded square and a question mark rather than " +
        "borrowing a neighbour's frame.";

    /// <summary>The one line a headless run's screen state is read off.</summary>
    private const string PerkDraftMarker = "SIR_PERK_DRAFT_READY";

    /// <summary>Where the per-option card lives, instantiated once per card on offer.</summary>
    private const string CardScenePath = "res://game/scenes/PerkDraftCard.tscn";

    /// <summary>And where one counter row lives, instantiated once per counter.</summary>
    /// <remarks>
    /// A scene instanced three times rather than three hand-built rows: the three differ in their
    /// caption and their numbers and in nothing else, and a fourth counter — <c>24</c> §4.7 could
    /// grow one — should cost a row rather than a copy of a subtree.
    /// </remarks>
    private const string GuaranteeRowScenePath = "res://game/scenes/GuaranteeRow.tscn";

    private const string CardCategoryBarPath = "Body/Column/HeaderRow/CategoryBar";
    private const string CardIconSlotPath = "Body/Column/HeaderRow/IconSlot";
    private const string CardNameLabelPath = "Body/Column/HeaderRow/NameLabel";
    private const string CardRarityGemPath = "Body/Column/HeaderRow/RarityGem";
    private const string CardRarityGlyphPath = "Body/Column/HeaderRow/RarityGem/RarityGlyph";
    private const string RowCaptionLabelPath = "CaptionLabel";
    private const string RowValueLabelPath = "ValueLabel";
    private const string CardTierBadgePath = "Body/Column/TierBadge";
    private const string CardEffectLabelPath = "Body/Column/EffectLabel";
    private const string CardSynergyRowPath = "Body/Column/SynergyRow";
    private const string CardSynergyLabelPath = "Body/Column/SynergyRow/SynergyLabel";
    private const string CardSynergyValuePath = "Body/Column/SynergyRow/SynergyValue";
    private const string CardPressButtonPath = "PressButton";

    private const string SafeAreaPath = "%SafeArea";
    private const string GroundPath = "%Ground";
    private const string TitleLabelPath = "%TitleLabel";
    private const string CardColumnPath = "%CardColumn";
    private const string AdFourthOptionCardPath = "%AdFourthOptionCard";
    private const string AdFourthOptionNameLabelPath = "%AdFourthOptionNameLabel";
    private const string AdFourthOptionBlockLabelPath = "%AdFourthOptionBlockLabel";
    private const string AdRerollButtonPath = "%AdRerollButton";
    private const string AdRerollBlockLabelPath = "%AdRerollBlockLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string RerollCostLabelPath = "%RerollCostLabel";
    private const string RerollCostValuePath = "%RerollCostValue";
    private const string RerollButtonPath = "%RerollButton";
    private const string FreeRerollBlockLabelPath = "%FreeRerollBlockLabel";
    private const string GuaranteeColumnPath = "%GuaranteeColumn";
    private const string GuaranteeNotDueBlockLabelPath = "%GuaranteeNotDueBlockLabel";
    private const string SkipRewardLabelPath = "%SkipRewardLabel";
    private const string SkipRewardValuePath = "%SkipRewardValue";
    private const string SkipButtonPath = "%SkipButton";

    /// <summary>The panel entry a card's whole face — fill, outline, corners and shadow — is written into.</summary>
    private const string PanelStyleOverride = "panel";

    /// <summary>The theme entry the rarity letter's own ink is written into.</summary>
    private const string FontColourOverride = "font_color";

    /// <summary>The band letters, which are the rarity ladder's own codes rather than wording.</summary>
    private const string CommonSymbol = "C";
    private const string RareSymbol = "B";
    private const string EpicSymbol = "A";
    private const string LegendarySymbol = "S";

    /// <summary>And what a band this build was never taught is marked with.</summary>
    private const string UnknownSymbol = "?";

    /// <summary>The animated property of an upgrade card's entrance.</summary>
    private const string ScaleProperty = "scale";

    /// <summary>Separates a price from the balance it is read against, as the board separates HP.</summary>
    private const char OverSeparator = '/';

    /// <summary>Joins one card's unresolved tokens on the log line, where several may pile up.</summary>
    private const string LogJoin = " · ";

    /// <summary>
    /// How long a number has to be held before its exact value replaces its shortened one.
    /// </summary>
    /// <remarks>
    /// ⚠️ This task's choice of number and the only gesture timing on this screen. It is a press
    /// LENGTH rather than an animation, so the design's transition ceiling does not bind it; it is
    /// set long enough that a tap meant for the control underneath is never read as a hold, and short
    /// enough that a player who wants the exact figure is not made to wait for it.
    /// </remarks>
    private const double LongPressSeconds = 0.4;

    /// <summary>How wide an upgrade card's border is drawn, against eight for every other card.</summary>
    private const int UpgradeBorderWidth = 12;

    /// <summary>What an upgrade card grows from as it arrives.</summary>
    private const float IntroScale = 0.94f;

    /// <summary>
    /// And over how long — well inside the design's ceiling for a screen's own motion, and skipped
    /// outright by a tap anywhere on the ground.
    /// </summary>
    private const double IntroSeconds = 0.22;

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>
    /// The border an owned-perk upgrade is drawn in — the authored gold of the art manifest's own
    /// rarity ladder, which is where the only gold this game has ever written down lives.
    /// </summary>
    private static readonly Color UpgradeBorderColour = new(0.9608f, 0.651f, 0.1373f);

    /// <summary>What a mark whose value this build was never taught is drawn as.</summary>
    /// <remarks>
    /// 🔒 The card's own outline grey rather than any real category's or rarity's colour. A perk
    /// band this table has no entry for is a band the client does not know, and answering it with a
    /// neighbour's colour would put a plausible value in a hole.
    /// </remarks>
    private static readonly Color UnknownMarkColour = new(0.36f, 0.38f, 0.45f);

    /// <summary>
    /// The ink a rarity letter is drawn in, which is the screen's own ground rather than black: all
    /// four bands are light enough to carry it at better than five to one.
    /// </summary>
    private static readonly Color GemInkColour = new(0.07f, 0.07f, 0.09f);

    private PerkDraftPresenter? _presenter;
    private Board? _board;
    private CancellationToken _lifetime;

    private ColorRect? _ground;
    private Label? _titleLabel;
    private VBoxContainer? _cardColumn;
    private Control? _adFourthOptionCard;
    private Label? _adFourthOptionNameLabel;
    private Label? _adFourthOptionBlockLabel;
    private Button? _adRerollButton;
    private Label? _adRerollBlockLabel;
    private VBoxContainer? _guaranteeColumn;
    private Label? _guaranteeNotDueBlockLabel;
    private Label? _statusLabel;
    private Label? _rejectionLabel;
    private Label? _rerollCostLabel;
    private Label? _rerollCostValue;
    private Button? _rerollButton;
    private Label? _freeRerollBlockLabel;
    private Label? _skipRewardLabel;
    private Label? _skipRewardValue;
    private Button? _skipButton;

    /// <summary>The press control of each card now drawn, in the order the presenter offers them.</summary>
    private readonly List<Button> _cardButtons = [];

    /// <summary>The counter rows the last draw built, so they are rebuilt only when they change.</summary>
    /// <remarks>
    /// Held by reference-identity of the presenter's list, exactly as <c>_drawnFrom</c> holds the
    /// cards': the rows are rebuilt on a new projection and left alone on a re-render that changed
    /// nothing, which is what stops a scroll position resetting under the player's thumb every tick.
    /// </remarks>
    private IReadOnlyList<PerkDraftGuaranteeRow>? _rowsFrom;

    /// <summary>The entrances still running, with the card each one is settling, so a tap can end them.</summary>
    private readonly List<(Control Card, Tween Intro)> _intro = [];

    /// <summary>
    /// The card list the column was last built from, by reference.
    /// </summary>
    /// <remarks>
    /// 🔒 What stops the column being torn down and rebuilt on every redraw. A rebuild frees three
    /// nodes and restarts an entrance, and this screen redraws twice for every press — so rebuilding
    /// unconditionally would replay the upgrade card's arrival each time a button was merely greyed
    /// out. The presenter hands back a fresh list exactly when the cards have actually changed.
    /// </remarks>
    private IReadOnlyList<PerkDraftCard>? _drawnFrom;

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Whether one of the two numbers is being held right now.</summary>
    /// <remarks>
    /// 🔒 Read by the hold timer when it elapses, and cleared on teardown as well as on release. A
    /// timer created by the tree outlives the node that asked for it, so this flag is what stops a
    /// hold started just before the screen closed from reaching a presenter nobody is looking at.
    /// </remarks>
    private bool _holdingANumber;

    /// <summary>Takes the presenter, the board to hand back to, and the app's shutdown token.</summary>
    /// <param name="presenter">Drives this screen.</param>
    /// <param name="board">The board the draft was entered from, returned to once it closes.</param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException">The presenter or the board is null.</exception>
    public void Drive(PerkDraftPresenter presenter, Board board, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(board);

        _presenter = presenter;
        _board = board;
        _lifetime = lifetime;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws on every press.
        _ground = GetNode<ColorRect>(GroundPath);
        _titleLabel = GetNode<Label>(TitleLabelPath);
        _cardColumn = GetNode<VBoxContainer>(CardColumnPath);
        _adFourthOptionCard = GetNode<Control>(AdFourthOptionCardPath);
        _adFourthOptionNameLabel = GetNode<Label>(AdFourthOptionNameLabelPath);
        _adFourthOptionBlockLabel = GetNode<Label>(AdFourthOptionBlockLabelPath);
        _adRerollButton = GetNode<Button>(AdRerollButtonPath);
        _adRerollBlockLabel = GetNode<Label>(AdRerollBlockLabelPath);
        _guaranteeColumn = GetNode<VBoxContainer>(GuaranteeColumnPath);
        _guaranteeNotDueBlockLabel = GetNode<Label>(GuaranteeNotDueBlockLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _rerollCostLabel = GetNode<Label>(RerollCostLabelPath);
        _rerollCostValue = GetNode<Label>(RerollCostValuePath);
        _rerollButton = GetNode<Button>(RerollButtonPath);
        _freeRerollBlockLabel = GetNode<Label>(FreeRerollBlockLabelPath);
        _skipRewardLabel = GetNode<Label>(SkipRewardLabelPath);
        _skipRewardValue = GetNode<Label>(SkipRewardValuePath);
        _skipButton = GetNode<Button>(SkipButtonPath);

        _rerollButton.Pressed += OnRerollPressed;
        _skipButton.Pressed += OnSkipPressed;
        _ground.GuiInput += OnGroundInput;

        // The two readouts that carry a number, and the only two controls here that answer a hold
        // rather than a press. Both take input in the scene file for it; a label that ignores the
        // pointer would leave the exact value unreachable with nothing red anywhere.
        _rerollCostValue.GuiInput += OnNumberInput;
        _skipRewardValue.GuiInput += OnNumberInput;

        // Painted once, because nothing about which colour belongs to which state changes while the
        // screen is up. It is painted at all because a Button draws its text by draw mode, and the
        // disabled mode two of these three spend their whole life in has an engine default of
        // half-transparent grey that no override of font_color reaches.
        foreach (var button in new[] { _rerollButton, _adRerollButton, _skipButton })
        {
            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);
        }

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewportRect().Size);

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>. The controls are children and die
    /// with this node either way, but a handler left connected across a scene that is merely
    /// detached and re-added would fire twice — and once is the whole contract of a pick.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_rerollButton is not null)
        {
            _rerollButton.Pressed -= OnRerollPressed;
        }

        if (_skipButton is not null)
        {
            _skipButton.Pressed -= OnSkipPressed;
        }

        if (_ground is not null)
        {
            _ground.GuiInput -= OnGroundInput;
        }

        if (_rerollCostValue is not null)
        {
            _rerollCostValue.GuiInput -= OnNumberInput;
        }

        if (_skipRewardValue is not null)
        {
            _skipRewardValue.GuiInput -= OnNumberInput;
        }

        // Cleared here as well as on release: a hold timer already running belongs to the tree and
        // fires whether this screen is still there or not.
        _holdingANumber = false;

        FinishIntro();
    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a draft that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The perk draft entered the tree with no presenter. Only a screen that already " +
                    "has a run may instantiate it, and it must call Drive before adding it.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            Render();
            Report(presenter);
            LeaveIfTheDraftHasClosed();
        }
        catch (Exception failure)
        {
            GD.PushError($"The perk draft stopped unexpectedly: {failure}");
        }
    }

    /// <summary>Writes the presenter's state into the scene, if the scene is still there to write into.</summary>
    private void Render()
    {
        var presenter = _presenter;

        // Validity before tree membership: asking a freed node whether it is inside the tree is
        // itself the crash, and a shutdown during a slow command is the ordinary case on a handset.
        // Every node this writes to is checked, not just the one: a scene-unique name that no longer
        // resolves leaves a null behind, and a null-forgiving operator over it would turn a renamed
        // node into a crash here instead of a blank label.
        if (presenter is null || !IsInstanceValid(this) || !IsInsideTree() ||
            _titleLabel is null || _cardColumn is null || _adFourthOptionCard is null ||
            _adFourthOptionNameLabel is null || _adFourthOptionBlockLabel is null ||
            _adRerollButton is null || _adRerollBlockLabel is null ||
            _guaranteeColumn is null || _guaranteeNotDueBlockLabel is null ||
            _statusLabel is null || _rejectionLabel is null ||
            _rerollCostLabel is null || _rerollCostValue is null || _rerollButton is null ||
            _freeRerollBlockLabel is null || _skipRewardLabel is null ||
            _skipRewardValue is null || _skipButton is null)
        {
            return;
        }

        _titleLabel.Text = presenter.Title;

        RenderCards(presenter);

        // 🔒 Both ad slots are drawn whatever the read said, and neither is ever hidden. Their whole
        // point is that the milestone which lands them fills a slot the layout already has, instead
        // of finding a screen laid out as though the offer had never been planned.
        _adFourthOptionNameLabel.Text = presenter.AdFourthOptionName;
        _adFourthOptionBlockLabel.Text = presenter.AdFourthOptionBlockText;
        _adFourthOptionCard.Visible = true;

        _adRerollButton.Text = presenter.AdRerollText;
        _adRerollButton.Disabled = !presenter.AdRerollAvailable;
        _adRerollBlockLabel.Text = presenter.AdRerollBlockText;

        RenderGuarantees(presenter);

        _rerollCostLabel.Text = presenter.RerollCostLabel;

        // The price, then the balance it is read against — the board's own idiom for a pair of
        // numbers that belong together, and the only honest way to show a Gold balance on a screen
        // whose string set authors a caption for the price and none for the purse. Both come off the
        // presenter already written the way a player reads them: shortened past ten thousand, exact
        // while the readout is being held.
        _rerollCostValue.Text =
            $"{presenter.RerollGoldCostText}{OverSeparator}{presenter.GoldText}";

        _rerollButton.Text = presenter.RerollText;
        _rerollButton.Disabled = _busy || presenter.Stage != PerkDraftStage.Ready;
        _freeRerollBlockLabel.Text = presenter.FreeRerollBlockText;

        _skipRewardLabel.Text = presenter.SkipRewardLabel;
        _skipRewardValue.Text = presenter.SkipGoldRewardText;

        _skipButton.Text = presenter.SkipText;
        _skipButton.Disabled = _busy || presenter.Stage != PerkDraftStage.Ready;

        // Hidden rather than blanked once it has nothing to say, which is what every other screen in
        // this build does with the same two lines and for the same reason: an empty label still
        // claims a full line of height, so a blank one is a sentence a player can see room for and
        // cannot read. They are two lines because they answer two different questions — what state
        // the screen is in, and what the game said about the last command.
        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;
    }

    /// <summary>Draws the cards on offer, rebuilding them only when the offer itself has changed.</summary>
    private void RenderCards(PerkDraftPresenter presenter)
    {
        if (_cardColumn is not { } column)
        {
            return;
        }

        if (!ReferenceEquals(_drawnFrom, presenter.Cards))
        {
            BuildCards(column, presenter);

            _drawnFrom = presenter.Cards;
        }

        foreach (var card in _cardButtons)
        {
            if (IsInstanceValid(card))
            {
                card.Disabled = _busy;
            }
        }
    }

    private void BuildCards(VBoxContainer column, PerkDraftPresenter presenter)
    {
        FinishIntro();
        _cardButtons.Clear();
        Clear(column);

        if (presenter.Cards.Count == 0)
        {
            return;
        }

        var cardScene = GD.Load<PackedScene>(CardScenePath);

        if (cardScene is null)
        {
            // Load answers null rather than throwing when the resource is missing or its import
            // cannot be read, so an unnamed null reference is all a caller gets unless it says so.
            GD.PushError($"The perk draft card could not be loaded from '{CardScenePath}'.");

            return;
        }

        foreach (var offer in presenter.Cards)
        {
            var card = BuildCard(cardScene, presenter, offer);

            column.AddChild(card);

            // 🔒 After the card is in the tree, never before: a tween is created BY a node and a
            // node outside the tree has no tree to create one on.
            if (offer.IsUpgrade)
            {
                PlayEntrance(card);
            }
        }
    }

    /// <summary>
    /// Draws the three <c>DRAFT</c> counters, rebuilding the rows only when the projection changed.
    /// </summary>
    /// <remarks>
    /// 🔒 Every row is drawn, whatever its counter stands at — <c>24</c> §1.1's Visibility rule is
    /// <em>always</em>, and a row hidden at zero is the case a "show it when it matters" reading would
    /// drop. A row whose guarantee is not currently due keeps its caption and its rung and is drawn in
    /// the same quiet grey every other unavailable control on this screen takes, with the reason in its
    /// own sentence beneath: the number is still disclosed, and nothing claims a countdown is running.
    /// </remarks>
    private void RenderGuarantees(PerkDraftPresenter presenter)
    {
        if (_guaranteeColumn is not { } column || _guaranteeNotDueBlockLabel is not { } block)
        {
            return;
        }

        if (!ReferenceEquals(_rowsFrom, presenter.Guarantees))
        {
            BuildGuarantees(column, presenter.Guarantees);

            _rowsFrom = presenter.Guarantees;
        }

        block.Text = presenter.GuaranteeNotDueBlockText;
        block.Visible = block.Text.Length > 0;
    }

    /// <summary>Instantiates one row per counter, in the order the guarantees fire.</summary>
    private static void BuildGuarantees(
        VBoxContainer column, IReadOnlyList<PerkDraftGuaranteeRow> rows)
    {
        Clear(column);

        if (GD.Load<PackedScene>(GuaranteeRowScenePath) is not { } rowScene)
        {
            GD.PushError(
                "The guarantee row scene did not load, so the draft's luck-protection counters are " +
                "not drawn. 24 §1.1 requires them shown, so this is a defect rather than a degradation.");

            return;
        }

        foreach (var row in rows)
        {
            var drawn = rowScene.Instantiate<HBoxContainer>();
            var ink = row.Live ? LiveColour : UnavailableColour;

            var caption = drawn.GetNode<Label>(RowCaptionLabelPath);
            var value = drawn.GetNode<Label>(RowValueLabelPath);

            caption.Text = row.Label;
            value.Text = row.Value;

            // The VALUE takes the state colour and the caption keeps its own quiet grey: the caption is
            // already the quiet half of the row, so dimming it too would leave the row's own emphasis
            // pointing at nothing.
            value.AddThemeColorOverride(FontColourOverride, ink);

            column.AddChild(drawn);
        }
    }

    private PanelContainer BuildCard(
        PackedScene cardScene, PerkDraftPresenter presenter, PerkDraftCard offer)
    {
        var card = cardScene.Instantiate<PanelContainer>();

        card.GetNode<ColorRect>(CardCategoryBarPath).Color = CategoryColour(offer.Category);

        DrawRarity(
            card.GetNode<PanelContainer>(CardRarityGemPath),
            card.GetNode<Label>(CardRarityGlyphPath),
            offer.Rarity);

        card.GetNode<Label>(CardNameLabelPath).Text = offer.Name;
        card.GetNode<Label>(CardTierBadgePath).Text = presenter.TierBadge(offer);
        card.GetNode<Label>(CardEffectLabelPath).Text = presenter.EffectLine(offer);

        var synergyRow = card.GetNode<Control>(CardSynergyRowPath);

        // 🔒 The hint comes off the presenter already NAMED. The projection carries PK_* ids because
        // that is what a run stores; resolving one to the perk's own name is a catalogue lookup
        // against the loaded content set, which the presenter holds and a scene may not — so the ids
        // never reach this side except on the log line at the bottom of this file.
        var hint = presenter.SynergyLine(offer);

        synergyRow.Visible = hint.Length > 0;
        card.GetNode<Label>(CardSynergyLabelPath).Text = presenter.SynergyLabel;
        card.GetNode<Label>(CardSynergyValuePath).Text = hint;

        var press = card.GetNode<Button>(CardPressButtonPath);

        // Applied even though the card's own text is empty and its name is drawn by a label beside
        // it: a control that opts out of the one helper written to stop an engine default reaching
        // a draw mode is a control that silently keeps one the day it grows a caption.
        ButtonTextColours.ApplyTo(press, LiveColour, UnavailableColour);
        press.Disabled = _busy;

        // Captured by value into the handler, so the index a press submits is the index the card was
        // drawn for even after the list is rebuilt beneath it.
        var index = offer.OptionIndex;

        press.Pressed += () => OnCardPressed(index);

        _cardButtons.Add(press);

        if (offer.IsUpgrade)
        {
            GildBorder(card);
        }

        return card;
    }

    /// <summary>Draws an owned-perk upgrade's border in gold, and only its border.</summary>
    /// <remarks>
    /// 🔒 Duplicated off the card's OWN authored face rather than built here, so an upgrade card
    /// differs from its siblings in exactly one property and every other number describing a card
    /// stays written down in one place.
    /// </remarks>
    private static void GildBorder(PanelContainer card)
    {
        if (card.GetThemeStylebox(PanelStyleOverride) is not StyleBoxFlat face ||
            face.Duplicate() is not StyleBoxFlat gold)
        {
            GD.PushError("A perk draft card has no flat face to gild, so an upgrade is unmarked.");

            return;
        }

        gold.BorderColor = UpgradeBorderColour;
        gold.SetBorderWidthAll(UpgradeBorderWidth);

        card.AddThemeStyleboxOverride(PanelStyleOverride, gold);
    }

    /// <summary>Grows an upgrade card into place rather than letting it simply appear.</summary>
    private void PlayEntrance(Control card)
    {
        // The pivot follows the container's own sizing rather than being guessed before it: a card
        // scaled about a pivot of zero grows out of its top-left corner and overlaps its neighbour.
        card.Resized += () => card.PivotOffset = card.Size / 2;
        card.Scale = new Vector2(IntroScale, IntroScale);

        var intro = card.CreateTween();

        intro.SetTrans(Tween.TransitionType.Back);
        intro.SetEase(Tween.EaseType.Out);
        intro.TweenProperty(card, ScaleProperty, Vector2.One, IntroSeconds);

        _intro.Add((card, intro));
    }

    /// <summary>Ends every entrance still running and leaves each card at its settled size.</summary>
    /// <remarks>
    /// 🔒 Called by a tap on the ground as well as by a press and by teardown, because the design
    /// requires every one of a screen's own animations to be skippable. Validity is asked before
    /// anything else of both halves: a tween bound to a card the engine has already freed is a dead
    /// object, and asking a dead object anything is itself the crash. The settled size is written
    /// directly rather than stepped to, so a killed entrance can never leave a card part-grown.
    /// </remarks>
    private void FinishIntro()
    {
        foreach (var (card, intro) in _intro)
        {
            if (IsInstanceValid(intro) && intro.IsValid())
            {
                intro.Kill();
            }

            if (IsInstanceValid(card))
            {
                card.Scale = Vector2.One;
            }
        }

        _intro.Clear();
    }

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same frame
    /// are both in the tree and both laid out until it ends. Detaching first is what keeps a rebuilt
    /// column from briefly drawing two sets of cards over each other.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>Gives the gem its band's fill, its frame shape and its letter, all three at once.</summary>
    /// <remarks>
    /// 🔒 Duplicated off the gem's OWN authored face rather than built here, so the frame differs
    /// from the authored one in exactly the two properties a band decides and every other number
    /// describing a gem stays written down in one place. See
    /// <see cref="RarityIsAShapeAndASymbolBeforeItIsAColour"/>.
    /// </remarks>
    private static void DrawRarity(PanelContainer gem, Label glyph, PerkRarity rarity)
    {
        var (fill, ink, symbol, corners) = RarityBand(rarity);

        glyph.Text = symbol;
        glyph.AddThemeColorOverride(FontColourOverride, ink);

        if (gem.GetThemeStylebox(PanelStyleOverride) is not StyleBoxFlat face ||
            face.Duplicate() is not StyleBoxFlat band)
        {
            GD.PushError("A perk draft gem has no flat face to shape, so a rarity keeps the default frame.");

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
    /// 🔒 A fill, an ink, a symbol and a frame per band, and an honest answer for a band this build
    /// was never taught. The four fills are the art manifest's own rarity ladder, taken from its
    /// bottom four codes because a perk has four rarities and the ladder has five — the fifth belongs
    /// to gear and is deliberately unused here rather than mapped onto a perk band that does not
    /// exist. The corners are read as top-left, top-right, bottom-right, bottom-left against a gem
    /// 72 across, so 36 is a full round and the two asymmetric pairs are shapes rather than radii.
    /// </remarks>
    private static (Color Fill, Color Ink, string Symbol, Vector4I Corners) RarityBand(
        PerkRarity rarity) => rarity switch
    {
        PerkRarity.Common =>
            (new Color(0.6039f, 0.6471f, 0.6941f), GemInkColour, CommonSymbol, new Vector4I(0, 0, 0, 0)),
        PerkRarity.Rare =>
            (new Color(0.298f, 0.6863f, 0.3137f), GemInkColour, RareSymbol, new Vector4I(36, 36, 36, 36)),
        PerkRarity.Epic =>
            (new Color(0.2314f, 0.5098f, 0.9647f), GemInkColour, EpicSymbol, new Vector4I(34, 0, 34, 0)),
        PerkRarity.Legendary =>
            (new Color(0.9608f, 0.651f, 0.1373f), GemInkColour, LegendarySymbol, new Vector4I(0, 0, 34, 34)),
        _ => (UnknownMarkColour, LiveColour, UnknownSymbol, new Vector4I(18, 18, 18, 18)),
    };

    /// <remarks>See <see cref="TheCategoryPaletteIsChosenHere"/>.</remarks>
    private static Color CategoryColour(PerkCategory category) => category switch
    {
        PerkCategory.Offense => new Color(0.9412f, 0.3412f, 0.3608f),
        PerkCategory.Defense => new Color(0.1686f, 0.702f, 0.7529f),
        PerkCategory.Sustain => new Color(0.8784f, 0.3922f, 0.6902f),
        PerkCategory.DiceAndBoard => new Color(0.5569f, 0.502f, 0.9686f),
        PerkCategory.Economy => new Color(0.6471f, 0.8392f, 0.1922f),
        PerkCategory.TriggerSynergy => new Color(0.8039f, 0.3608f, 0.949f),
        _ => UnknownMarkColour,
    };

    private void OnCardPressed(int optionIndex) =>
        _ = SubmitAsync(presenter => presenter.PickAsync(optionIndex, _lifetime));

    private void OnRerollPressed() => _ = SubmitAsync(presenter => presenter.RerollAsync(_lifetime));

    private void OnSkipPressed() => _ = SubmitAsync(presenter => presenter.SkipAsync(_lifetime));

    /// <remarks>
    /// The design makes every one of a screen's own animations skippable, and a tap on the ground is
    /// where "anywhere else" lands. It moves nothing and submits nothing.
    /// </remarks>
    /// <param name="event">The input the ground received.</param>
    private void OnGroundInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true })
        {
            FinishIntro();
        }
    }

    /// <summary>
    /// A number held down shows its exact value; letting go puts the shortened one back.
    /// </summary>
    /// <remarks>
    /// 🔒 Only the gesture is here. Which form each number takes is the presenter's answer, so what
    /// this file decides is the single fact an engine event carries — whether the finger is down —
    /// and nothing about how a number is written.
    /// </remarks>
    /// <param name="event">The input one of the two readouts received.</param>
    private void OnNumberInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                HoldNumber(mouse.Pressed);
                break;

            case InputEventScreenTouch touch:
                HoldNumber(touch.Pressed);
                break;
        }
    }

    private void HoldNumber(bool pressed)
    {
        _holdingANumber = pressed;

        if (!pressed)
        {
            _presenter?.ConcealFullValues();
            Render();

            return;
        }

        // The tree's timer rather than a node of this screen's own: it is one shot, it is created on
        // the press and it is gone after it, so a timer node would be a permanent child kept for a
        // gesture most players never make.
        var hold = GetTree()?.CreateTimer(LongPressSeconds);

        if (hold is null)
        {
            GD.PushError("A perk draft number was held while the screen was outside the tree.");

            return;
        }

        hold.Timeout += OnHoldElapsed;
    }

    /// <remarks>
    /// The flag is read FIRST, and it is cleared on teardown as well as on release: this timer
    /// belongs to the tree and fires whether or not the screen that asked for it is still there.
    /// </remarks>
    private void OnHoldElapsed()
    {
        if (!_holdingANumber || !IsInstanceValid(this) || _presenter is not { } presenter)
        {
            return;
        }

        presenter.RevealFullValues();
        Render();
    }

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path. A
    /// second press landing mid-flight would submit a command against a run the first has already
    /// moved — and on a draft that is a second perk, which is the one thing a draft may not grant.
    /// </remarks>
    private async Task SubmitAsync(Func<PerkDraftPresenter, Task<PerkDraftSubmission>> submit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        // Latched before the await, not after it: taken afterwards, a second press arriving while
        // the first is in flight finds it unset and submits again.
        _busy = true;
        FinishIntro();
        Render();

        try
        {
            await submit(presenter);

            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"A perk draft command failed: {failure}");
        }
        finally
        {
            _busy = false;
            Render();
            LeaveIfTheDraftHasClosed();
        }
    }

    /// <summary>
    /// Hands back to the board once the draft this screen exists for is no longer open.
    /// </summary>
    /// <remarks>
    /// 🔒 Read off the run the command answered with, never off which command was pressed. A pick
    /// and a skip both close the draft and a reroll does not, but that is the rules layer's answer
    /// to give: a screen that left because it had submitted a skip would leave on a skip the rules
    /// layer had refused. A run that could not be read at all stays here and says so — see
    /// <see cref="TheUnreadableRunHasNowhereToGo"/>.
    /// </remarks>
    private void LeaveIfTheDraftHasClosed()
    {
        if (_presenter is not { Stage: PerkDraftStage.NoDraft } || _board is not { } board ||
            !IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        PerkDraftHandover.Return(this, board);
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the run and content the
    /// build actually shipped — including the tokens that stopped a card's sentence.
    /// </summary>
    /// <remarks>
    /// 🔒 The unresolved tokens go HERE and never to the player. They name authored fields, which is
    /// exactly what someone fixing the catalogue needs and exactly what nobody playing the game can
    /// act on.
    /// </remarks>
    private static void Report(PerkDraftPresenter presenter)
    {
        var unresolved = string.Join(
            LogJoin,
            presenter.Cards
                     .Where(card => card.UnresolvedTokens.Count > 0)
                     .Select(card => $"{card.PerkId}:{string.Join(',', card.UnresolvedTokens)}"));

        GD.Print(
            $"{PerkDraftMarker} stage={presenter.Stage} cards={presenter.Cards.Count} " +
            $"available={presenter.CardsAvailable} gold={presenter.Gold} " +
            $"reroll_cost={presenter.RerollGoldCost} skip_reward={presenter.SkipGoldReward} " +
            $"host_faulted={presenter.HostFaulted} " +
            $"rejection={presenter.RulesRejection?.ToString() ?? "none"} " +
            $"unresolved=[{unresolved}]");
    }
}
