using System.Globalization;
using Godot;
using SlayIdleRepeat.Client.Composition;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Scenes;

/// <summary>
/// S05 — the board a run is played on: a driving adapter over <see cref="BoardPresenter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It renders what the presenter says and forwards its presses. No rules, no ports, no adapters,
/// and above all no decision about what may be pressed: which of the roll, the tile
/// acknowledgement and the two branches is live at any moment is the presenter's answer, and this
/// half only draws it. That split is load-bearing rather than stylistic here — there is no scene
/// test harness in this repository, so anything decided in this file is decided where nothing can
/// check it.
/// </para>
/// <para>
/// 🔒 <b>The whole track is drawn, every node of it, always.</b> <c>BoardView</c> regenerates the
/// run's graph from its seed, so each node is drawn as its own tile kind and the branch previews
/// carry the labels and icons the generator actually authored. `16` D42 fixes the visibility: no
/// fog, no preview range, nothing clipped — a die that only answers a number is only an interesting
/// decision if the player can see what the numbers reach. The row WRAPS rather than scrolls, because
/// the whole board is in the world from the first frame. ⚠️ `16` D67 amends D42's framing clause and
/// nothing else: the camera RIDES the board rather than framing all of it at once, because at the
/// length a chapter may author, a whole-board framing draws every tile too small to read — and the
/// overview control is what reaches the far end. ⚠️ Still absent from the HUD: the perks list and
/// the consumable pouch, both owned by later milestones.
/// </para>
/// <para>
/// 🔒 <b>There is still no art here at all, placeholder or otherwise, and no VFX.</b> The board is
/// drawn as engine primitives — a cylinder for a tile, a box for a run of track — in the palette
/// below, and the dice tray and HP bar are still containers and coloured rectangles. What changed is
/// the MEDIUM and not the rule: `15` §E9's board art is unblocked (`16` D68) and unbuilt, and
/// arrives through <c>BoardTile.tscn</c> and <c>BoardPathSegment.tscn</c>. The one actor with a
/// model is the hero, which <c>Home.tscn</c> already shipped.
/// ⚠️ So a node's tile kind is drawn as a COLOUR, from the table below, and the name of the one the
/// run stands on is the only tile named in words. That is the honest limit of a board with no icon
/// set: the colours tell nodes apart and the caption says what the player is on. <b>One node is the
/// exception, and it is an exception on purpose:</b> a node the run may not walk past wears a collar
/// and two bars, because a rule the player cannot see coming may not be signalled by a colour.
/// <b>And the mark is NAMED, in a line of its own under the board</b>: a shape is only a rule to
/// somebody who has already been told what it means, so the shape says which node and
/// <see cref="GateLabelPath"/> says what it does. See <see cref="RenderWorld"/> and
/// <c>BoardTile.tscn</c>.
/// The die tumble, the dust puff and the floating result number the design
/// asks for are not built: they are procedural in-engine work by ruling, never a sprite sheet, and
/// this task adds no asset row for them.
/// </para>
/// <para>
/// 🔒 <b>The hero HOPS from tile to tile, one hop per node the run traversed.</b> Not a stylistic
/// choice first: <c>chr_hero_rogue.glb</c> is a static unrigged mesh with no skeleton and no clip of
/// any kind — <c>hero_export.py</c> exports with animations off — so there is no walk cycle to play
/// and none is invented. A hop is what a piece on a board does anyway, and one hop per node is what
/// makes the number the die came up readable from the motion. The junction pause needs no timer: a
/// movement stops when it must LEAVE a junction, so the last hop of the chain IS the junction and
/// the fork panel is already the live control by the time the hero lands on it.
/// </para>
/// <para>
/// ⚠️ Every type size, colour and gap in <c>Board.tscn</c> is a per-node
/// override, because the shared theme resource and the display faces it will carry do not exist yet
/// — they are M8-03's, and these overrides are debt owed to it rather than a naming scheme of this
/// screen's own. The sizes were chosen against the engine's default font, so they have to be
/// re-checked — not merely re-applied — when the real faces land. The layout itself is structural:
/// containers and stretch ratios, so it holds its proportions across the whole supported aspect
/// range without an override taking part.
/// </para>
/// <para>
/// 🔴 <b>One destination this screen stops at rather than builds.</b> A run that has ended belongs
/// to a screen this milestone does not own — see <see cref="TheRunEndScreensAreNotBuiltHere"/>. The
/// board says in words what the player is waiting on, disables the roll, and navigates nowhere.
/// </para>
/// <para>
/// 🔒 <b>Four destinations it does navigate to, and it navigates to each once.</b> The open battle,
/// the perk draft a won fight leaves, and the shop, campfire and shrine tiles a run lands on all
/// hand control back to this same screen rather than to a new one, so the board is read again on
/// return — every one of them is left by a command that has moved the run underneath it. A
/// destination still open after its own screen has handed back is a dead end rather than a reason to
/// go round again; see <see cref="TheBattleDidNotCloseWhenItsReplayEnded"/> and
/// <see cref="TheDecisionDidNotCloseWhenItsScreenHandedBack"/>.
/// </para>
/// </remarks>
public partial class Board : Node3D
{
    /// <summary>Where this scene lives, for the screens that instantiate it.</summary>
    public const string ScenePath = "res://game/scenes/Board.tscn";

    /// <summary>
    /// 🔴 Named so the dead end can be found. A battle that is still open once its replay has been
    /// watched is a battle nothing on either screen can close, and re-entering it would put the
    /// player in a loop between two screens with no way out of either.
    /// </summary>
    private const string TheBattleDidNotCloseWhenItsReplayEnded =
        "A battle was still open when its replay handed control back, so it is not entered a second " +
        "time. The confirmation that closes a battle is the replay's last step, and it is not made " +
        "when the fight could not be simulated at all — a corrupt battle counter, a simulator that " +
        "threw, a log with no events — or when the rules layer refused the result. Either way the run " +
        "stays parked in the battle phase, which refuses every command except that confirmation, and " +
        "a board that re-entered the replay on every read would trap the player between two screens. " +
        "This message reached a player once, for a whole milestone, because the shipped prediction " +
        "refused every fight by construction: if it is on screen now, read the replay's own status " +
        "line for which of the reasons it was.";

    /// <summary>
    /// 🔴 Named so the dead end can be found, and the exact counterpart of the battle's. A decision
    /// screen hands back once the run has left the state that opened it, so a run still in that state
    /// on the read that follows is a decision nothing on either screen can close.
    /// </summary>
    private const string TheDecisionDidNotCloseWhenItsScreenHandedBack =
        "A decision was still open when its screen handed control back, so it is not entered a " +
        "second time. Every one of these screens leaves only once the run itself says the draft has " +
        "closed or the tile has cleared, so a run that comes back still holding either could not be " +
        "read at all or was refused the command that would have finished it — and a board that " +
        "re-entered the screen on every read would trap the player between two screens.";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found. The run's death and results screens are
    /// M7-08's. A finished run is drawn as finished and left there; this screen offers no way out of
    /// it, because every way out is somebody else's.
    /// </summary>
    private const string TheRunEndScreensAreNotBuiltHere =
        "The death and results screens are not built in this milestone: a run that has ended is " +
        "drawn as ended and has nowhere to go. The board neither banks it nor abandons it.";

    /// <summary>
    /// 🔴 Deliberately unbuilt, and named so it can be found — but NOT a dead end any more, which is
    /// why it is a warning and the sentence above is an error.
    /// </summary>
    private const string TheMinigameScreenIsNotBuiltHere =
        "The minigame screen is not built: the run is standing on a tile that has no screen to " +
        "open. It is not stuck — the board's own control resolves the tile through the command that " +
        "screen would have submitted, at the minigame's lowest outcome tier, so the rest of the run " +
        "is reachable. What the player does not get is the game. See UnbuiltTileScreens, which is " +
        "the whole of the placeholder and goes when that screen lands. The event card's half of " +
        "this is gone: an Event tile opens its own screen now and is a destination like the shop " +
        "and the campfire.";

    /// <summary>
    /// ⚠️ This task's choice, and the only timing on this screen that is not authored. The design
    /// says the die panel opens on a long press of the roll button and does not say how long a long
    /// press is. The panel is therefore also reachable from a control of its own, so nothing about
    /// the disclosure depends on a number nobody wrote down.
    /// </summary>

    /// <summary>
    /// The one line a headless run's screen state is read off. Distinctive on purpose: a board
    /// resolved against real content has to be greppable out of an engine log full of everything
    /// else, the way the home screen's readout already is.
    /// </summary>
    private const string BoardMarker = "SIR_BOARD_READY";

    /// <summary>Where the per-face row of the die panel lives, instantiated once per face kind.</summary>


    private const string SafeAreaPath = "%SafeArea";
    private const string HudPath = "%Hud";
    private const string StageRowPath = "%StageRow";
    private const string HpLabelPath = "%HpLabel";
    private const string HpValuePath = "%HpValue";
    private const string HpBarPath = "%HpBar";
    private const string GoldLabelPath = "%GoldLabel";
    private const string GoldValuePath = "%GoldValue";
    private const string StageLabelPath = "%StageLabel";
    private const string StageValuePath = "%StageValue";
    private const string TrackFramePath = "%TrackFrame";
    private const string WorldPath = "%World";
    private const string CameraRigPath = "%CameraRig";
    private const string OverviewButtonPath = "%OverviewButton";

    /// <summary>Where the sentence saying what the gate mark means is written.</summary>
    /// <remarks>
    /// 🔴 <b>It sits directly under the track, above the caption naming the tile the run stands
    /// on.</b> The mark is a shape, and a shape on a screen with no icon set, no legend and no
    /// tooltip is a rule the player can see and cannot read — so the one thing this row is for is
    /// naming it in words while it still matters, rather than after a roll has been shortened. It is
    /// in the track's own frame rather than among the status lines below because it describes the
    /// track: those lines report the run's state, and this one is a standing fact about the board.
    /// </remarks>
    private const string GateLabelPath = "%GateLabel";
    private const string StandingOnRowPath = "%StandingOnRow";
    private const string StandingOnLabelPath = "%StandingOnLabel";
    private const string PendingTileLabelPath = "%PendingTileLabel";
    private const string StatusLabelPath = "%StatusLabel";
    private const string BlockLabelPath = "%BlockLabel";
    private const string RejectionLabelPath = "%RejectionLabel";
    private const string ForkPanelPath = "%ForkPanel";
    private const string ForkTitleLabelPath = "%ForkTitleLabel";
    private const string ForkButtonsPath = "%ForkButtons";
    private const string PromptPanelPath = "%PromptPanel";
    private const string RolledLabelPath = "%RolledLabel";
    private const string RolledValuePath = "%RolledValue";
    private const string DicePanelPath = "%DicePanel";
    private const string DiceLabelPath = "%DiceLabel";
    private const string DiceButtonsPath = "%DiceButtons";
    private const string DieChoicePanelPath = "%DieChoicePanel";
    private const string DieChoiceLabelPath = "%DieChoiceLabel";
    private const string DieChoiceButtonsPath = "%DieChoiceButtons";
    private const string RollButtonPath = "%RollButton";
    private const string ResolveButtonPath = "%ResolveButton";
    private const string AbandonButtonPath = "%AbandonButton";

    /// <summary>Separates the two halves of one value pair: the amount, then its denominator.</summary>
    private const char OverSeparator = '/';

    /// <summary>Separates the tiles of a fork branch's preview, in the order the branch walks them.</summary>
    private const string IconSeparator = " \u00b7 ";

    /// <summary>Marks the count on a tray control holding more than one die of the same number.</summary>
    /// <remarks>
    /// ⚠️ A symbol rather than a word, and the one string on this screen that is not a loc key: it is
    /// the multiplication sign, which is read the same way in both shipped locales and carries no
    /// grammar to translate. A worded count would need a plural rule per locale for a value the run
    /// already states as a numeral.
    /// </remarks>
    private const string HeldCountPrefix = " \u00d7";

    /// <summary>Joins the faces one command reported, in the order it produced them.</summary>

    /// <summary>The theme entry a control's own text size is written into.</summary>
    private const string FontSizeOverride = "font_size";

    /// <summary>What a fork branch's caption is drawn at, matching the body size beside it.</summary>
    private const int BranchFontSize = 48;

    /// <summary>The pip a node whose tile kind this build has no colour for is drawn as.</summary>
    /// <remarks>
    /// Reachable only for a tile number outside `03` §2's fifteen, which is a board generated by a
    /// newer rules layer than this scene. Drawn as the quiet grey rather than skipped, so the node is
    /// still counted and the track still measures the board.
    /// </remarks>
    private static readonly Color UnvisitedNodeColour = new(0.24f, 0.25f, 0.30f);

    /// <summary>
    /// What each of `03` §2's fifteen tile kinds is drawn as, with no icon set to draw instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Presentation, and debt owed to M8-03 exactly like every font size in the scene.</b>
    /// Colour is the only channel a container-and-rectangle screen has for fifteen kinds, so these
    /// are grouped by what a tile DOES to the player rather than chosen individually: the four
    /// fights are one red, the four payouts one gold, the three that open a choice one blue, the
    /// curse its own warning colour, and the empty node the quiet grey. Two tiles sharing a colour
    /// is deliberate — the player reads the group at a glance and the caption names the one they are
    /// standing on.
    /// </para>
    /// <para>
    /// 🔴 <b>The mini-boss shares the boss's red rather than being given a fifteenth colour, and that
    /// is the point.</b> The two are told apart by the barred frame one of them wears and the other
    /// does not, so the distinction survives a player who cannot separate two reds — which a
    /// fifteenth red would not. Colour groups these tiles; it never carries a rule on its own.
    /// </para>
    /// <para>
    /// 🔒 Keyed on <see cref="TileKind"/> rather than on the run's bare integer, so a kind added
    /// upstream is a MISSING entry answered by the grey above rather than a wrong colour: the
    /// dictionary is asked, never indexed.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<TileKind, Color> TileColours =
        new Dictionary<TileKind, Color>
        {
            [TileKind.Enemy] = new(0.78f, 0.31f, 0.28f),
            [TileKind.Elite] = new(0.78f, 0.31f, 0.28f),
            [TileKind.Boss] = new(0.91f, 0.45f, 0.38f),
            [TileKind.MiniBoss] = new(0.91f, 0.45f, 0.38f),
            [TileKind.Treasure] = new(0.85f, 0.72f, 0.34f),
            [TileKind.Cache] = new(0.85f, 0.72f, 0.34f),
            [TileKind.Shop] = new(0.85f, 0.72f, 0.34f),
            [TileKind.Minigame] = new(0.85f, 0.72f, 0.34f),
            [TileKind.Shrine] = new(0.40f, 0.62f, 0.80f),
            [TileKind.Campfire] = new(0.40f, 0.62f, 0.80f),
            [TileKind.Event] = new(0.40f, 0.62f, 0.80f),
            [TileKind.Portal] = new(0.55f, 0.45f, 0.78f),
            [TileKind.DiceForge] = new(0.55f, 0.45f, 0.78f),
            [TileKind.Curse] = new(0.62f, 0.36f, 0.60f),
            [TileKind.Empty] = new(0.30f, 0.31f, 0.36f),
        };

    /// <summary>And the one it is standing on — the token, in the palette's own live colour.</summary>
    private static readonly Color TokenColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>A control with something to do.</summary>
    private static readonly Color LiveColour = new(0.93f, 0.93f, 0.96f);

    /// <summary>And one with nothing to do — the same quiet grey every caption is drawn in.</summary>
    private static readonly Color UnavailableColour = new(0.66f, 0.67f, 0.73f);

    /// <summary>
    /// The abandon control once it is armed — the one warm colour on the screen.
    /// </summary>
    /// <remarks>
    /// The colour is not the confirmation; the caption is, and it says the run ends here. This is
    /// what stops the second press looking like the first one on a screen where every other control
    /// is the same off-white.
    /// </remarks>
    private static readonly Color ArmedColour = new(0.91f, 0.45f, 0.38f);

    /// <summary>What a die of the tray, and a number on the choice prompt, is drawn at.</summary>
    /// <remarks>
    /// Square and thumb-sized: `13` §3 puts every control a run turn needs inside the thumb zone,
    /// and a tray of up to six numbers plus a six-way choice is a lot of controls to fit there.
    /// </remarks>
    private static readonly Vector2 DieControlSize = new(140, 140);

    private BoardPresenter? _presenter;

    /// <summary>Builds the replay of the fight the run is standing in, once there is one.</summary>
    private Func<ComposedBattleScreen>? _battle;

    /// <summary>Builds the draft screen for the draft a won fight has left open.</summary>
    private Func<ComposedPerkDraftScreen>? _perkDraft;

    /// <summary>Builds the shop screen for the shop tile the run has landed on.</summary>
    private Func<ComposedShopScreen>? _shop;

    /// <summary>Builds the campfire / shrine screen for the tile the run has landed on.</summary>
    private Func<ComposedCampfireScreen>? _campfire;

    /// <summary>Builds the event screen for the event tile the run has landed on.</summary>
    private Func<ComposedEventScreen>? _event;
    private Func<ComposedRunEndScreen>? _runEnd;

    /// <summary>Whether the battle now open has already had its replay watched.</summary>
    private bool _battleShown;

    /// <summary>Which decision the run's present state has already been handed over for, if any.</summary>
    private RunDecision? _decisionShown;

    /// <summary>The starting menu this run's ending leads back to.</summary>
    /// <remarks>
    /// 🔒 Held rather than looked up, because there is nothing to look it up by: it is a hidden sibling
    /// among the screens the application has opened, and a search of the parent for one would be a
    /// screen deciding its own navigation from the shape of the tree. It arrives with the handover for
    /// the reason <see cref="BoardHandover"/> states — only the caller knows where a run was entered
    /// from, and only Home is where a finished one leads.
    /// </remarks>
    private Home? _home;

    private CancellationToken _lifetime;

    /// <summary>
    /// ⚠️ <b>The distances this board is drawn at, and not one of them is authored.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 `03`, `13` and `15` state how the board must READ — every tile legible at 48 dp, branches
    /// beside the spine with a clear join, the hero at roughly 40% of screen height — and state no
    /// distance at all. Per steering rule S6 the hole stays open and greppable: these are exports
    /// authored on <c>Board.tscn</c>, marked there as this task's choices and owed to a ruling,
    /// rather than named constants that would read as decided. The same arrangement, for the same
    /// reason, as the font sizes below.
    /// <para>
    /// What IS authored is derived rather than picked, and lives in <c>BoardCameraRig</c> and
    /// <c>BoardFraming</c>: the hero is framed into the band the interface leaves free, and a
    /// pull-back solves its distance from the viewport's real aspect.
    /// </para>
    /// </remarks>
    [Export] public float NodeSpacing { get; set; } = 2.6f;

    /// <inheritdoc cref="NodeSpacing"/>
    [Export] public float WindAmplitude { get; set; } = 2.2f;

    /// <inheritdoc cref="NodeSpacing"/>
    [Export] public float WindWavelength { get; set; } = 34f;

    /// <inheritdoc cref="NodeSpacing"/>
    [Export] public float BranchOffset { get; set; } = 2.4f;

    /// <inheritdoc cref="NodeSpacing"/>
    [Export] public float HeroScale { get; set; } = 0.42f;

    /// <inheritdoc cref="NodeSpacing"/>
    [Export] public float HopArcHeight { get; set; } = 0.55f;

    private BoardWorld? _world;
    private BoardCameraRig? _rig;
    private Button? _overviewButton;

    /// <summary>
    /// Where the hero is DRAWN, which is not where the run is for as long as a hop is in flight.
    /// </summary>
    private readonly BoardWalk _walk = new();

    /// <summary>
    /// The rest of a submission, held back until the walk lands.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is the ordering the whole screen turns on.</b> <see cref="Report"/> is what opens
    /// the battle, the shop, the campfire and the perk draft, and what leaves for Home when the run
    /// closes. Run where it used to be — the instant the command returns — it hands the player to
    /// another screen on the frame the hop STARTS, over a hero visibly still crossing the board.
    /// Nothing goes red when it does.
    /// </remarks>
    private Action? _afterWalk;

    /// <summary>The node a pressed fork branch leads to, so the walk can animate the edge the command took.</summary>
    private int? _pendingBranchNodeId;

    /// <summary>Whether a board has been drawn into the 3D world yet.</summary>
    private bool _built;

    private Control? _hud;
    private Control? _stageRow;
    private Label? _hpLabel;
    private Label? _hpValue;
    private ProgressBar? _hpBar;
    private Label? _goldLabel;
    private Label? _goldValue;
    private Label? _stageLabel;
    private Label? _stageValue;
    private Control? _trackFrame;
    private Label? _gateLabel;
    private Control? _standingOnRow;
    private Label? _standingOnLabel;
    private Label? _pendingTileLabel;
    private Label? _statusLabel;
    private Label? _blockLabel;
    private Label? _rejectionLabel;
    private Control? _forkPanel;
    private Label? _forkTitleLabel;
    private HBoxContainer? _forkButtons;
    private Control? _dicePanel;
    private Label? _diceLabel;
    private HFlowContainer? _diceButtons;
    private Control? _dieChoicePanel;
    private Label? _dieChoiceLabel;
    private HFlowContainer? _dieChoiceButtons;
    private Control? _promptPanel;
    private Label? _rolledLabel;
    private Label? _rolledValue;
    private Button? _rollButton;
    private Button? _resolveButton;
    private Button? _abandonButton;

    /// <summary>
    /// The connection, for the two controls here the server has to answer. Null when there is none.
    /// </summary>
    /// <remarks>
    /// 🔴 This screen is the WORKED EXAMPLE of the offline affordance, not the whole of it. Rolling
    /// and resolving a tile are the two actions here that only a server can settle, so they are the
    /// two drawn out of use and answered with a toast while it cannot be reached. Every other screen
    /// with a server-backed control still has none of this, and that gap is named by the task that
    /// wrote this rather than half-filled in passing.
    /// </remarks>
    private ConnectionPresenter? _connection;

    /// <summary>Whether a submission is in flight, so a second press cannot start another.</summary>
    private bool _busy;

    /// <summary>Takes everything the composition root built for this screen, and the shutdown token.</summary>
    /// <remarks>
    /// The whole composed screen rather than its parts, because the parts had reached seven: two
    /// presenters and a factory for each of the five destinations a run can reach from here. The
    /// holder is the client's own composition type, and this reads factories off it exactly as it
    /// already did for the battle — it calls them, it assembles nothing.
    /// </remarks>
    /// <param name="screen">Everything the composition root built for this board's run.</param>
    /// <param name="home">
    /// The starting menu a finished run leads back to, kept for <see cref="LeaveToHome"/>.
    /// </param>
    /// <param name="lifetime">Cancelled when the application shuts down.</param>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> or <paramref name="home"/> is null.</exception>
    public void Drive(ComposedBoardScreen screen, Home home, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(home);

        _presenter = screen.Board;
        _battle = screen.Battle;
        _perkDraft = screen.PerkDraft;
        _shop = screen.Shop;
        _campfire = screen.Campfire;
        _event = screen.Event;
        _runEnd = screen.RunEnd;
        _connection = screen.Connection;
        _home = home;
        _lifetime = lifetime;
    }

    /// <summary>Shows this screen again and reads the run afresh, for a fight handing control back.</summary>
    /// <remarks>
    /// 🔒 The read is the point, not the showing. A replay that reached its end submitted the
    /// confirmation that closes the battle, so the run behind this screen is a different row from the
    /// one it drew: the phase has moved, the health has moved, and a won fight MAY have opened a
    /// draft — every fight but the last one does, and the run's last fight ends the run instead.
    /// Un-hiding without reading again would put a pre-battle board in front of a post-battle run.
    /// </remarks>
    public void Resume()
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        ScreenStage.Show(this);

        // 🔒 Snapped, never eased. The run behind this screen moved while another screen was up —
        // that is the whole reason Resume reads again — so easing the camera across the gap would
        // animate a journey the player did not take and did not see. StartAsync's read puts the
        // hero down at wherever the run now is, for the same reason.
        _rig?.Snap();

        _ = StartAsync();
    }

    /// <summary>
    /// Stands this board down for good and hands the player back to the starting menu, for a run that
    /// has ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>FREED, not hidden, which is the opposite of what <see cref="Resume"/> does and for the
    /// opposite reason.</b> A run is entered once and ends once — by a death or by a chapter cleared,
    /// and <c>02</c> §6 makes those one moment — so there is nothing on this screen a later run could
    /// use. <c>BoardComposition</c> builds the next run a board of its own with its own presenters and
    /// its own read, so keeping this one would leave a dead board and a finished projection in memory
    /// per run for the life of the application, and re-showing it would be the second run played on the
    /// first run's screen.
    /// </para>
    /// <para>
    /// 🔒 <b>Home is RE-READ rather than merely un-hidden</b>, for the reason <see cref="Resume"/> gives
    /// about this screen: the run behind it has closed and its payout is banked, so the profile Home
    /// drew is a different row from the one it holds. Un-hiding alone would leave a CONTINUE offering to
    /// resume the run that has just ended, onto the board this call is freeing.
    /// </para>
    /// <para>
    /// 🔒 <b>Detached before it is queued.</b> <c>QueueFree</c> alone defers removal to the end of the
    /// frame, and this board is hidden but still processing — it counts a held roll button every frame —
    /// so a board merely queued would keep running over the Home it just revealed. The free stays
    /// queued rather than taken because this is reached from the run-end screen's handler.
    /// </para>
    /// </remarks>
    /// <returns>
    /// True when the starting menu is back and this board has stood down. False says it is still here
    /// and still the player's only screen, which is <see cref="RunEndHandover.Leave"/>'s cue to hand
    /// back to it rather than free the screen in front of it over nothing.
    /// </returns>
    internal bool LeaveToHome()
    {
        if (_home is not { } home)
        {
            GD.PushError(
                "A run ended and this board has no starting menu to return to. Only a screen that can " +
                "be returned to may instantiate the board, and it must pass Home to Drive.");

            return false;
        }

        if (!IsInstanceValid(home))
        {
            // The starting menu freed underneath a run is a shutdown, which is the ordinary way it
            // happens on a handset. Named rather than navigated to, and the board stays.
            GD.PushError("A run ended and the starting menu it was entered from is gone.");

            return false;
        }

        home.Resume();

        GetParent()?.RemoveChild(this);
        QueueFree();

        return true;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Resolved once. A scene-unique lookup is a string search of the owner's table each time it
        // is asked, and this screen redraws on every command and on every frame the ring is open.
        _hud = GetNode<Control>(HudPath);
        _stageRow = GetNode<Control>(StageRowPath);
        _hpLabel = GetNode<Label>(HpLabelPath);
        _hpValue = GetNode<Label>(HpValuePath);
        _hpBar = GetNode<ProgressBar>(HpBarPath);
        _goldLabel = GetNode<Label>(GoldLabelPath);
        _goldValue = GetNode<Label>(GoldValuePath);
        _stageLabel = GetNode<Label>(StageLabelPath);
        _stageValue = GetNode<Label>(StageValuePath);
        _trackFrame = GetNode<Control>(TrackFramePath);
        _world = GetNode<BoardWorld>(WorldPath);
        _rig = GetNode<BoardCameraRig>(CameraRigPath);
        _overviewButton = GetNode<Button>(OverviewButtonPath);
        _gateLabel = GetNode<Label>(GateLabelPath);
        _standingOnRow = GetNode<Control>(StandingOnRowPath);
        _standingOnLabel = GetNode<Label>(StandingOnLabelPath);
        _pendingTileLabel = GetNode<Label>(PendingTileLabelPath);
        _statusLabel = GetNode<Label>(StatusLabelPath);
        _blockLabel = GetNode<Label>(BlockLabelPath);
        _rejectionLabel = GetNode<Label>(RejectionLabelPath);
        _forkPanel = GetNode<Control>(ForkPanelPath);
        _forkTitleLabel = GetNode<Label>(ForkTitleLabelPath);
        _forkButtons = GetNode<HBoxContainer>(ForkButtonsPath);
        _promptPanel = GetNode<Control>(PromptPanelPath);
        _rolledLabel = GetNode<Label>(RolledLabelPath);
        _rolledValue = GetNode<Label>(RolledValuePath);
        _dicePanel = GetNode<Control>(DicePanelPath);
        _diceLabel = GetNode<Label>(DiceLabelPath);
        _diceButtons = GetNode<HFlowContainer>(DiceButtonsPath);
        _dieChoicePanel = GetNode<Control>(DieChoicePanelPath);
        _dieChoiceLabel = GetNode<Label>(DieChoiceLabelPath);
        _dieChoiceButtons = GetNode<HFlowContainer>(DieChoiceButtonsPath);
        _rollButton = GetNode<Button>(RollButtonPath);
        _resolveButton = GetNode<Button>(ResolveButtonPath);
        _abandonButton = GetNode<Button>(AbandonButtonPath);

        _rollButton.Pressed += OnRollPressed;
        _resolveButton.Pressed += OnResolvePressed;
        _abandonButton.Pressed += OnAbandonPressed;
        _overviewButton.Pressed += OnOverviewPressed;

        // Painted once, because nothing about which colour belongs to which state changes while the
        // screen is up. It is painted at all because a Button draws its text by draw mode, and the
        // disabled mode every one of these controls spends most of its life in has an engine default
        // of half-transparent grey that no override of font_color reaches.
        foreach (var button in new[] { _rollButton, _resolveButton, _overviewButton })
        {
            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);
        }

        _rig.Follows(_world.HeroAnchor);

        // Claims the viewport for this screen's own camera and puts its overlay up. Every screen
        // does this on the way in, because every handover in this build leaves the outgoing screen
        // in the tree — hidden, or freed only on the frame after — so two cameras and two overlays
        // are alive at the moment this one becomes the visible screen.
        ScreenStage.Show(this);

        SafeAreaInsets.ApplyTo(GetNode<MarginContainer>(SafeAreaPath), GetViewport().GetVisibleRect().Size);

        _rig.Snap();

        Render();

        _ = StartAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The matching half of the subscriptions in <c>_Ready</c>. The buttons are children and die
    /// with this node either way, but a handler left connected across a scene that is merely
    /// detached and re-added would fire twice — and once is the whole contract of a roll.
    /// </remarks>
    public override void _ExitTree()
    {
        if (_rollButton is not null)
        {
            _rollButton.Pressed -= OnRollPressed;
        }

        if (_resolveButton is not null)
        {
            _resolveButton.Pressed -= OnResolvePressed;
        }

        if (_abandonButton is not null)
        {
            _abandonButton.Pressed -= OnAbandonPressed;
        }

    }

    /// <remarks>
    /// Nothing awaits this task, so its exceptions have nowhere to surface: the whole body is
    /// guarded, or a continuation that threw would leave a board that silently stopped moving.
    /// </remarks>
    private async Task StartAsync()
    {
        try
        {
            if (_presenter is not { } presenter)
            {
                GD.PushError(
                    "The board entered the tree with no presenter. Only a screen that already has " +
                    "a run may instantiate it, and it must call Drive before adding it to the tree.");

                return;
            }

            // No ConfigureAwait(false): the continuation writes to nodes, and only the thread the
            // engine runs the scene tree on may do that.
            await presenter.StartAsync(_lifetime);

            // A read is not a move. Whatever the run's position turns out to be, the hero is put
            // down on it rather than walked to it: nothing the player did produced the difference.
            _walk.SnapTo(presenter.StandingOn?.NodeId);

            Render();
            Report(presenter);
        }
        catch (Exception failure)
        {
            GD.PushError($"The board stopped unexpectedly: {failure}");
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
            _hud is null || _hpLabel is null || _hpValue is null || _hpBar is null ||
            _goldLabel is null || _goldValue is null || _stageLabel is null || _stageValue is null ||
            _trackFrame is null || _world is null || _rig is null || _overviewButton is null ||
            _gateLabel is null || _standingOnRow is null ||
            _standingOnLabel is null || _pendingTileLabel is null ||
            _statusLabel is null || _blockLabel is null || _rejectionLabel is null ||
            _forkPanel is null || _forkTitleLabel is null || _forkButtons is null ||
            _promptPanel is null || _rollButton is null || _resolveButton is null ||
            _abandonButton is null)
        {
            return;
        }

        // The one predicate most of the screen turns on: whether the read produced a run there is
        // anything to draw. An ended run still carries its numbers and still draws them — what it
        // does not carry is anything to press.
        var carried = presenter.Stage is BoardStage.Ready or BoardStage.RunEnded;

        // 🔒 Drawn only when there ARE numbers. Before the read answers, and in the two states where
        // it never will, the HP, Gold and stage are all still the zero an unset field carries — and
        // "HP 0/0" told to a player mid-run is not a placeholder, it is a plausible value in a hole.
        // The block leaves instead and the status line says which state this is.
        _hud.Visible = carried;

        // 🔒 The track frame is NEVER hidden, and that is a layout fact rather than a content one.
        // Its two children hide themselves when they have nothing to show, but the frame itself has
        // to stay: hiding it once left the scrolling middle band as the column's only expander, and
        // in the three states where there is no run the whole screen packed against the top with
        // the primary action stranded in the upper third.
        _trackFrame.Visible = true;

        _hpLabel.Text = presenter.HpLabel;
        _hpValue.Text = $"{presenter.CurrentHp.ToString(CultureInfo.InvariantCulture)}" +
                        $"{OverSeparator}{presenter.MaxHp.ToString(CultureInfo.InvariantCulture)}";
        _hpBar.MaxValue = Math.Max(presenter.MaxHp, 1);
        _hpBar.Value = presenter.CurrentHp;

        _goldLabel.Text = presenter.GoldLabel;
        _goldValue.Text = presenter.Gold.ToString(CultureInfo.InvariantCulture);

        _stageLabel.Text = presenter.StageLabel;
        _stageValue.Text = StageReadout(presenter);

        // Hidden rather than blanked once it has nothing to say, which is what every other screen in
        // this build does with the same line and for the same reason: an empty label still claims a
        // full line of height, so a blank one is a sentence a player can see room for and cannot
        // read. The whole ROW goes, caption included — "Stage" with no number after it reads as a
        // value that failed to load rather than as a value there is not yet one of.
        if (_stageRow is not null)
        {
            _stageRow.Visible = _stageValue.Text.Length > 0;
        }

        RenderWorld(presenter);

        // 🔴 Read AFTER the board is built, and hidden with it. The sentence is about a mark on a
        // tile, so on the one path where no tile is drawn at all — a template that would not load —
        // it would be a rule stated about a board that is not on the screen. Hidden rather than
        // blanked, like every other sentence here: an empty label still claims its line of height.
        _gateLabel.Text = presenter.GateRuleText;
        _gateLabel.Visible = _gateLabel.Text.Length > 0 && _world.IsBuilt;

        _overviewButton.Text = presenter.OverviewText(_rig.Mode == BoardCameraMode.Follow
            ? BoardOverviewStep.ToTheStage
            : _rig.Mode == BoardCameraMode.Stage
                ? BoardOverviewStep.ToTheWholeBoard
                : BoardOverviewStep.BackToTheHero);

        // 🔒 Live in EVERY state a board is up in, and the one control on this screen the turn latch
        // does not touch. `16` D42 as amended by D67 makes it the whole compliance mechanism for a
        // board the camera rides through — so gating it behind the latch that guards turn order
        // would make the rule conditional on the game being idle. It moves no run state, so a press
        // landing mid-hop can corrupt nothing.
        _overviewButton.Visible = _world.IsBuilt;

        _standingOnLabel.Text = presenter.StandingOnLabel;
        _pendingTileLabel.Text = presenter.PendingTileName;

        // 🔴 Hidden while a hop is in flight, along with the block sentence, the fork panel and the
        // resolve button below. Every one of them is a statement about the tile the run is ON, and
        // for as long as the hero is still crossing the board it has not arrived there yet. The
        // numbers — HP, gold, stage, and the number the die came up — are drawn at once, because
        // "you rolled 4", four hops, then "you are on the Shop" is the order a player reads it in.
        _standingOnRow.Visible = _pendingTileLabel.Text.Length > 0 && !_walk.InProgress;

        _statusLabel.Text = presenter.StatusText;
        _statusLabel.Visible = _statusLabel.Text.Length > 0;

        _blockLabel.Text = presenter.BlockText;
        _blockLabel.Visible = _blockLabel.Text.Length > 0 && !_walk.InProgress;

        _rejectionLabel.Text = presenter.RejectionText;
        _rejectionLabel.Visible = _rejectionLabel.Text.Length > 0;

        RenderFork(presenter);
        RenderRolled(presenter);
        RenderFixedDice(presenter);
        RenderFixedDieChoice(presenter);

        // 🔒 The three things that move a run on share the bottom of the screen and are never live
        // together: a pending tile is exactly what refuses a roll, and so is an open fork. Swapping
        // between them rather than greying two out is what keeps the control the player is supposed
        // to press the largest one on screen and inside the thumb zone — a 340-pixel dead roll
        // button over two small live branch buttons is the design's rule exactly inverted.
        var tilePending = presenter.RollBlock == BoardRollBlock.TilePending;
        var forkOpen = presenter.RollBlock == BoardRollBlock.ForkOpen;

        _rollButton.Text = presenter.RollText;
        _rollButton.Visible = !tilePending && !forkOpen && !_walk.InProgress;
        _rollButton.Disabled = _busy || presenter.RollBlock != BoardRollBlock.None;

        _resolveButton.Text = presenter.ResolveText;
        _resolveButton.Visible = tilePending && !_walk.InProgress;
        _resolveButton.Disabled = _busy;

        // 🔒 Drawn out of use rather than disabled. A disabled control accepts no press, and a press
        // is exactly what the offline rule answers with a toast — so the affordance dims the face and
        // marks it, and the press handler is what refuses to submit.
        OfflineActionAffordance.ApplyTo(_rollButton, _connection);
        OfflineActionAffordance.ApplyTo(_resolveButton, _connection);

        // 🔒 Drawn from AbandonOffered and from NOTHING else on this screen — not from the block,
        // not from whether a tile is pending, not from whether a fork is open. It is the one control
        // that has to be there in every state a live run can stand in, INCLUDING the states that
        // hide every other control, because it is the only way out of a tile this build has no
        // screen for.
        _abandonButton.Text = presenter.AbandonText;
        _abandonButton.Visible = presenter.AbandonOffered;
        _abandonButton.Disabled = _busy;

        ButtonTextColours.ApplyTo(
            _abandonButton,
            presenter.AbandonArmed ? ArmedColour : UnavailableColour,
            UnavailableColour);

    }

    /// <summary>
    /// Draws the whole board as a place: a puck for every node, a run of track between every pair,
    /// and the hero standing on one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The whole board, every node of it (`16` D42).</b> What D67 amended is where the CAMERA
    /// is, never what is drawn: every tile of every stage is in the world from the first frame, with
    /// no fog, no preview range and nothing generated as the run approaches it. The overview control
    /// is what reaches the far end of it.
    /// </para>
    /// <para>
    /// Built once. A board cannot change while the run that generated it is alive — it regenerates
    /// from the run seed and the run's seed does not move — so rebuilding it each render would write
    /// a few hundred transforms a frame for a picture that is already correct.
    /// </para>
    /// <para>
    /// 🔒 <b>The palette is <see cref="TileColours"/>, unmoved.</b> The fifteen kinds and the six
    /// colours they group into are the same ones the pip strip drew, with the same argument behind
    /// them; what changed is that a colour is now on a puck rather than on a rectangle. Keeping the
    /// table here is also what keeps the contrast suite's transcription guard honest.
    /// </para>
    /// </remarks>
    private void RenderWorld(BoardPresenter presenter)
    {
        if (_world is not { } world || _rig is not { } rig)
        {
            return;
        }

        if (!_built && presenter.Board is { } board)
        {
            world.Build(
                board,
                new BoardLayoutMetrics(NodeSpacing, WindAmplitude, WindWavelength, BranchOffset),
                ColourOf,
                HeroScale);

            _built = world.IsBuilt;

            if (_built)
            {
                rig.Follows(world.HeroAnchor);
            }
        }

        if (!_built)
        {
            return;
        }

        DrawHero();

        rig.Frames(world.BoardBounds, world.StageBounds(world.StageOf(_walk.ShownNodeId)));
    }

    /// <summary>
    /// Puts the hero where the playhead says it is — mid-hop on an arc, or standing on a node.
    /// </summary>
    private void DrawHero()
    {
        if (_world is not { } world)
        {
            return;
        }

        if (_walk.InProgress)
        {
            world.HopHero(_walk.HopFromNodeId, _walk.HopToNodeId, (float)_walk.HopProgress, HopArcHeight);

            return;
        }

        world.PlaceHero(_walk.ShownNodeId);
    }

    /// <summary>The colour one tile kind is drawn as, or the quiet grey for a kind this build has none for.</summary>
    private static Color ColourOf(TileKind tile) =>
        TileColours.TryGetValue(tile, out var colour) ? colour : UnvisitedNodeColour;

    /// <summary>Empties a container now, rather than at the end of the frame.</summary>
    /// <remarks>
    /// 🔒 <c>QueueFree</c> alone defers removal, so a child freed and a child added in the same
    /// frame are both in the tree and both laid out until it ends. This screen redraws twice on one
    /// path when a command completes synchronously, so that is the ordinary case here rather than a
    /// rare one: detaching first is what keeps a rebuilt row from briefly drawing twice over.
    /// </remarks>
    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The only thing on this screen that runs per frame, and it runs only while a hop is in flight.
    /// The tail of the submission is released on the one call that finishes the walk — see
    /// <see cref="_afterWalk"/> for why it was held back at all.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (!_walk.InProgress)
        {
            return;
        }

        var landed = _walk.Advance(delta);

        DrawHero();

        if (!landed)
        {
            return;
        }

        var tail = _afterWalk;
        _afterWalk = null;

        tail?.Invoke();
    }

    private void RenderFork(BoardPresenter presenter)
    {
        if (_forkPanel is not { } panel || _forkButtons is not { } buttons ||
            _forkTitleLabel is not { } title)
        {
            return;
        }

        Clear(buttons);

        if (presenter.Fork is not { } fork)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        title.Text = presenter.ForkTitle;

        foreach (var branch in fork.Branches)
        {
            var button = new Button
            {
                Text = BranchCaption(presenter, branch),
                Disabled = _busy,
                CustomMinimumSize = new Vector2(0, 200),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };

            // A control built in code inherits the engine's default face, which is caption-sized on
            // a canvas this wide — unreadable inside a button this tall, on the run's one real
            // navigation choice. The sibling screens set the same override for the same reason.
            button.AddThemeFontSizeOverride(FontSizeOverride, BranchFontSize);

            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);

            // Captured by value into the handler, so the index a press submits is the index the
            // button was drawn for even after the list is rebuilt beneath it. The node the edge
            // leads to rides along, because a junction has two successors and the walk cannot name
            // a route from the two endpoints alone.
            var index = branch.BranchIndex;
            var toNodeId = branch.ToNodeId;

            button.Pressed += () => OnBranchPressed(index, toNodeId);

            buttons.AddChild(button);
        }
    }

    /// <remarks>
    /// ⚠️ A readout, not a prompt. This panel used to be the reroll's 4-second acceptance window,
    /// with a countdown ring and a control inside it. There is no reroll and no window: what is left
    /// is the number the last roll came up, shown until the next one replaces it, and hidden before
    /// the run has rolled at all rather than showing a zero nothing produced.
    /// </remarks>
    private void RenderRolled(BoardPresenter presenter)
    {
        if (_promptPanel is not { } panel || _rolledLabel is not { } label ||
            _rolledValue is not { } value)
        {
            return;
        }

        if (presenter.LastRolledPips is not { } pips)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        label.Text = presenter.RolledLabel;
        value.Text = pips.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One branch's caption: what it is, then the bias it was drawn under, then its own first tiles.
    /// </summary>
    /// <remarks>
    /// 🔒 Three lines rather than one, because they are three different claims and the player is
    /// choosing between two paths on the strength of them: the action, the generator's label, and the
    /// tiles the draw actually produced. The spine edge has only the first — it was drawn under no
    /// bias — so it gets one line rather than two padded ones.
    /// </remarks>
    private static string BranchCaption(BoardPresenter presenter, ForkBranch branch)
    {
        var caption = presenter.BranchText(branch);
        var label = presenter.BranchLabelText(branch);

        if (label.Length > 0)
        {
            caption += "\n" + label;
        }

        // Named in words rather than drawn as coloured squares, unlike the track: there are at most
        // three of them, they are the whole content of a decision, and a row of three anonymous
        // colours inside a button is a preview the player has to have memorised the track to read.
        var icons = branch.Icons ?? [];

        if (icons.Count > 0)
        {
            caption += "\n" + string.Join(IconSeparator, icons.Select(presenter.TileName));
        }

        return caption;
    }

    /// <summary>
    /// Draws the tray of fixed dice the run owns — one control per number held, spending it on press.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A control per NUMBER, not per die</b>, with the count beside it when more than one is
    /// held. Two dice showing a 3 are the same holding twice, so two identical buttons would be a
    /// choice between indistinguishable things.
    /// </para>
    /// <para>
    /// ⚠️ Disabled by exactly what disables the roll, because the rules layer refuses both movement
    /// commands from the same states. Hidden, not greyed, when the tray is empty: a heading over
    /// nothing reads as dice that failed to load.
    /// </para>
    /// </remarks>
    private void RenderFixedDice(BoardPresenter presenter)
    {
        if (_dicePanel is not { } panel || _diceLabel is not { } label ||
            _diceButtons is not { } buttons)
        {
            return;
        }

        Clear(buttons);

        if (!presenter.FixedDiceOffered)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        label.Text = presenter.FixedDiceLabel;

        var live = presenter.RollBlock == BoardRollBlock.None;

        foreach (var held in presenter.FixedDice)
        {
            var pips = held.Pips;

            var button = new Button
            {
                Text = held.Count > 1
                    ? $"{pips.ToString(CultureInfo.InvariantCulture)}{HeldCountPrefix}" +
                      held.Count.ToString(CultureInfo.InvariantCulture)
                    : pips.ToString(CultureInfo.InvariantCulture),
                Disabled = _busy || !live,
                CustomMinimumSize = DieControlSize,
            };

            button.AddThemeFontSizeOverride(FontSizeOverride, BranchFontSize);

            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);

            // Captured by value, for the reason the branch index is: the tray is rebuilt underneath
            // these handlers every time a die is granted or spent.
            button.Pressed += () => OnFixedDiePressed(pips);

            buttons.AddChild(button);
        }
    }

    /// <summary>
    /// Draws the prompt that names a granted die's number — six controls, one per side.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Live from every state, unlike every other control on this screen.</b> Naming a number
    /// moves nothing, and a grant can land while a tile is unresolved or a battle is open; refusing
    /// it until the board was clear would leave the player holding a reward they cannot open in the
    /// states they most want to open it. Only <c>_busy</c> takes it out of use.
    /// </remarks>
    private void RenderFixedDieChoice(BoardPresenter presenter)
    {
        if (_dieChoicePanel is not { } panel || _dieChoiceLabel is not { } label ||
            _dieChoiceButtons is not { } buttons)
        {
            return;
        }

        Clear(buttons);

        if (!presenter.FixedDieChoiceOffered)
        {
            panel.Visible = false;

            return;
        }

        panel.Visible = true;
        label.Text = presenter.FixedDieChoiceLabel;

        for (var pips = Die.MinPips; pips <= Die.MaxPips; pips++)
        {
            var chosen = pips;

            var button = new Button
            {
                Text = pips.ToString(CultureInfo.InvariantCulture),
                Disabled = _busy,
                CustomMinimumSize = DieControlSize,
            };

            button.AddThemeFontSizeOverride(FontSizeOverride, BranchFontSize);

            ButtonTextColours.ApplyTo(button, LiveColour, UnavailableColour);

            button.Pressed += () => OnFixedDieChosen(chosen);

            buttons.AddChild(button);
        }
    }

    private string StageReadout(BoardPresenter presenter) =>
        presenter is { StageNumber: { } stage, StageCount: { } count }
            ? $"{stage.ToString(CultureInfo.InvariantCulture)}{OverSeparator}" +
              $"{count.ToString(CultureInfo.InvariantCulture)}"
            : "";

    /// <remarks>
    /// 🔒 The offline answer comes FIRST and stops the submission. Rolling is a server-settled action,
    /// so a press that reached the presenter while the connection is out would queue a command the
    /// player was never told about — "silently broken" is the phrase the rule uses for it.
    /// </remarks>
    private void OnRollPressed()
    {
        if (!OfflineActionAffordance.MayBeSubmitted(_connection))
        {
            return;
        }

        _ = SubmitAsync(presenter => presenter.RollAsync(_lifetime));
    }

    /// <remarks>Resolving a tile is server-settled too — see <see cref="OnRollPressed"/>.</remarks>
    private void OnResolvePressed()
    {
        if (!OfflineActionAffordance.MayBeSubmitted(_connection))
        {
            return;
        }

        _ = SubmitAsync(presenter => presenter.ResolvePendingTileAsync(_lifetime));
    }

    /// <remarks>
    /// The first press arms and the second submits, and the presenter holds which one this is — see
    /// <see cref="BoardPresenter.AbandonRunAsync"/>. Routed through the same
    /// <see cref="SubmitAsync"/> as every other control, so the arming press redraws the caption.
    /// </remarks>
    private void OnAbandonPressed() =>
        _ = SubmitAsync(presenter => presenter.AbandonRunAsync(_lifetime));

    private void OnBranchPressed(int branchIndex, int? toNodeId)
    {
        _pendingBranchNodeId = toNodeId;

        _ = SubmitAsync(presenter => presenter.ChooseForkAsync(branchIndex, _lifetime));
    }

    /// <summary>Steps the camera: the hero, then the stage, then the whole board, then back.</summary>
    private void OnOverviewPressed()
    {
        _rig?.Step();

        Render();
    }

    private void OnFixedDiePressed(int pips) =>
        _ = SubmitAsync(presenter => presenter.UseFixedDieAsync(pips, _lifetime));

    private void OnFixedDieChosen(int pips) =>
        _ = SubmitAsync(presenter => presenter.ChooseFixedDieAsync(pips, _lifetime));

    /// <remarks>
    /// Every control is taken out of use for the whole round trip and put back once, on one path.
    /// A second press landing mid-flight would submit a command against a run the first has already
    /// moved — and on a board that is a second roll, which is the one thing a turn may not be.
    /// </remarks>
    private async Task SubmitAsync(Func<BoardPresenter, Task<BoardSubmission>> submit)
    {
        if (_busy || _presenter is not { } presenter)
        {
            return;
        }

        _busy = true;

        // A command returns the camera to the hero first, so a player who rolls from the overview
        // watches it come back in as the hop starts rather than watching the board move under a
        // camera that stayed out. The two eases run together and the pull-back is the shorter of
        // the two.
        _rig?.Follow();

        // 🔒 Where the HERO is, never presenter.Position. The two differ for exactly as long as an
        // animation is in flight, and passing the run's position would walk from the destination to
        // itself — correct on every path today, and silently wrong the first time a walk is cut
        // short.
        var from = _walk.ShownNodeId;
        var via = _pendingBranchNodeId;
        _pendingBranchNodeId = null;

        Render();

        try
        {
            await submit(presenter);

            var hops = presenter.WalkFrom(from, via);

            if (hops.Count == 0)
            {
                _walk.SnapTo(presenter.StandingOn?.NodeId);
                Finish(presenter);

                return;
            }

            // 🔒 The tail is DEFERRED to the frame the walk lands on. Report opens the battle, the
            // shop, the campfire and the draft, and leaves for Home when the run closes — every one
            // of those takes the player off this board, and done here it would take them off it
            // mid-hop.
            _afterWalk = () => Finish(presenter);

            _walk.Begin(hops);
            Render();
        }
        catch (Exception failure)
        {
            GD.PushError($"A board command failed: {failure}");

            _walk.SnapTo(presenter.StandingOn?.NodeId);
            Finish(presenter);
        }
    }

    /// <summary>Releases the turn and does what the command was going to do next.</summary>
    /// <remarks>
    /// Called once per submission, on whichever of the three paths that submission took: the walk
    /// landing, a move that had nothing to walk, or a command that threw. The order matters — the
    /// latch is released and the screen redrawn BEFORE <see cref="Report"/>, because Report may
    /// detach and free this board and nothing may touch it afterwards.
    /// </remarks>
    private void Finish(BoardPresenter presenter)
    {
        _busy = false;

        Render();
        Report(presenter);
    }

    /// <summary>
    /// Prints, on one greppable line, what this screen resolved against the run and content the
    /// build actually shipped — including the destinations it stopped at.
    /// </summary>
    private void Report(BoardPresenter presenter)
    {
        GD.Print(
            $"{BoardMarker} stage={presenter.Stage} block={presenter.RollBlock} " +
            $"hp={presenter.CurrentHp}/{presenter.MaxHp} gold={presenter.Gold} " +
            $"position={presenter.Position} track={Describe(presenter.TrackIndex)} " +
            $"stage_no={Describe(presenter.StageNumber)}/{Describe(presenter.StageCount)} " +
            $"tile={presenter.PendingTile?.Kind.ToString(CultureInfo.InvariantCulture) ?? "none"} " +
            $"fork={presenter.Fork?.Branches.Count.ToString(CultureInfo.InvariantCulture) ?? "none"} " +
            $"rolled={Describe(presenter.LastRolledPips)} rejection={Describe(presenter.RulesRejection)} " +
            $"nodes={presenter.Board?.Nodes.Count ?? 0} shown={Describe(_walk.ShownNodeId)} " +
            $"walking={_walk.InProgress} camera={_rig?.Mode.ToString() ?? "none"}");

        if (LeaveIfTheRunHasClosed(presenter))
        {
            // This board has been detached and queued for freeing. Nothing below may touch it.
            return;
        }

        ReportUnbuiltDestination(presenter);
        OpenBattle(presenter);
        OpenDecision(presenter);
    }

    /// <summary>
    /// Takes the player back to the starting menu the moment the run this board is drawing has
    /// closed, and says whether this board has stood down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>ABANDON_RUN's way off, and the only one it has.</b> Abandoning pays the run out and
    /// closes it inside its own handler — there is no unbanked tally left for S14 to offer and no
    /// <c>END_RUN</c> left for it to accept — so a board that handed over to the results screen
    /// would put the player on a screen whose one control is refused. Home is where a closed run
    /// belongs, and this is what gets them there.
    /// </para>
    /// <para>
    /// 🔒 <b>Asked of a closed run however it closed</b>, rather than only after this screen's own
    /// abandon: a board entered on a run that was already over reached exactly the same dead end,
    /// and it is the same fix. <see cref="ReportUnbuiltDestination"/> still stands behind it for the
    /// one case that genuinely has nowhere to go, which is a board driven without a Home.
    /// </para>
    /// <para>
    /// ⚠️ <b>The tally an abandoned run is owed is not drawn.</b> <c>13</c>'s S14 lists a reward
    /// tally for every ending, and this one goes straight past it, because the payout is applied
    /// before the client ever reads the run back. Showing it means splitting <c>ABANDON_RUN</c> into
    /// a marker and an <c>END_RUN</c> the way a death and a victory are already split — the results
    /// screen's row, not this control's.
    /// </para>
    /// </remarks>
    private bool LeaveIfTheRunHasClosed(BoardPresenter presenter) =>
        presenter.Stage == BoardStage.RunEnded && LeaveToHome();

    /// <remarks>
    /// Reported rather than navigated to. A finished run belongs to a screen a later row owns, and a
    /// run that reaches it stops here with the reason named in the log.
    /// <para>
    /// 🔒 <b>Two levels, because the two gaps are not the same gap.</b> A finished run is an ERROR
    /// here: this screen has nowhere to send it and the player is stuck looking at it. A Minigame
    /// tile is a WARNING: the screen is missing and the log says so, but the run is not stuck — the
    /// tile's own command still resolves it, so the line records a placeholder taken rather than a
    /// dead end reached. Pushing both as errors would make the one that traps a player unfindable
    /// among the ones that do not.
    /// </para>
    /// <para>
    /// The Event tile is no longer either of those. It opens its own screen, is routed like the shop
    /// and the campfire, and reaches this method only as an ordinary pending tile.
    /// </para>
    /// </remarks>
    private static void ReportUnbuiltDestination(BoardPresenter presenter)
    {
        if (presenter.RollBlock == BoardRollBlock.RunEnded)
        {
            GD.PushError($"{BoardMarker} halted · {TheRunEndScreensAreNotBuiltHere}");
        }

        if (presenter.PendingTileHasNoScreen)
        {
            GD.PushWarning(
                $"{BoardMarker} placeholder · {TheMinigameScreenIsNotBuiltHere} " +
                $"tile={presenter.PendingTile?.Kind.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        }
    }

    /// <summary>Hands over to the replay of the fight the run is standing in, at most once per fight.</summary>
    /// <remarks>
    /// 🔒 The latch is what makes the return path terminate. This runs after every read and after
    /// every accepted command, and the replay's return path is itself a read — so without it a
    /// battle the replay could not close would send the player straight back into the replay, and
    /// round again, forever. A run that has left the battle phase clears the latch, because the next
    /// battle is a different battle.
    /// </remarks>
    private void OpenBattle(BoardPresenter presenter)
    {
        if (presenter.RollBlock != BoardRollBlock.BattleOpen)
        {
            _battleShown = false;

            return;
        }

        if (_battleShown)
        {
            GD.PushError($"{BoardMarker} halted · {TheBattleDidNotCloseWhenItsReplayEnded}");

            return;
        }

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        if (_battle is not { } battle)
        {
            GD.PushError(
                "A battle is open and this screen has no way to build its replay. Only a screen " +
                "that already has a run may instantiate the board, and it must pass the battle " +
                "factory to Drive.");

            return;
        }

        // Latched on the handover having HAPPENED, not on having been attempted. A handover that
        // could not load its scene left the board on screen with the battle still open, and a latch
        // set anyway would answer the next read with the dead-end sentence for a replay nobody ever
        // watched.
        _battleShown = BattleHandover.Show(this, battle(), _lifetime);
    }

    /// <summary>Hands over to the screen the run's own state belongs on, at most once per state.</summary>
    /// <remarks>
    /// 🔒 The latch is what makes the return path terminate, exactly as the battle's does. Each of
    /// these screens hands back by reading the run and finding the state that opened it gone — so a
    /// run that comes back still holding it is one the screen could not finish, and without the latch
    /// the board would send the player straight back in, and round again, forever. A run that has
    /// left the state clears the latch, because the next shop is a different shop.
    /// </remarks>
    private void OpenDecision(BoardPresenter presenter)
    {
        if (DecisionFor(presenter) is not { } decision)
        {
            _decisionShown = null;

            return;
        }

        if (_decisionShown == decision)
        {
            GD.PushError($"{BoardMarker} halted · {TheDecisionDidNotCloseWhenItsScreenHandedBack}");

            return;
        }

        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        // Latched on the handover having HAPPENED, not on having been attempted — a handover that
        // could not load its scene left the board on screen with the decision still open, and a latch
        // set anyway would answer the next read with the dead-end sentence for a screen nobody saw.
        _decisionShown = HandOver(decision) ? decision : null;
    }

    /// <summary>Which screen, if any, the run's present state belongs on.</summary>
    /// <remarks>
    /// 🔴 The tile numbers are NOT restated here. Which integer is a shop and which is a shrine is a
    /// transcription of another assembly's internal enum, and each of those screens already owns its
    /// own copy and pins it with a case of its own — so this asks them rather than keeping a third
    /// copy that nothing would notice going stale.
    /// </remarks>
    private static RunDecision? DecisionFor(BoardPresenter presenter)
    {
        // 🔒 Asked FIRST, and ahead of the block, because a run that is over outranks anything still
        // pending on it. A hero at zero hit points leaves the fight's tile pending — a loss does not
        // clear it, which is what lets 02 §6's revive restart the same fight — so a board that read the
        // block first would send a dead run to the tile's own screen and offer it a shop.
        if (presenter.RunAwaitingResults)
        {
            return RunDecision.RunEnd;
        }

        return presenter.RollBlock switch
        {
            BoardRollBlock.DraftOpen => RunDecision.PerkDraft,
            BoardRollBlock.TilePending => presenter.PendingTile?.Kind switch
            {
                ShopPresenter.ShopTileKind => RunDecision.Shop,
                CampfirePresenter.CampfireTileKind or CampfirePresenter.ShrineTileKind =>
                    RunDecision.Campfire,

                // 🔒 The tile alone, drawn card or not: the draw is the event screen's own first
                // command, so a tile just landed on and a tile being resumed onto are one
                // destination — and this arm is what makes the resume path real.
                EventPresenter.EventTileKind => RunDecision.Event,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>Builds the screen for one decision and puts it in front of this one.</summary>
    private bool HandOver(RunDecision decision)
    {
        switch (decision)
        {
            case RunDecision.PerkDraft when _perkDraft is { } draft:
                return PerkDraftHandover.Show(this, draft(), _lifetime);

            case RunDecision.Shop when _shop is { } shop:
                return ShopHandover.Show(this, shop(), _lifetime);

            case RunDecision.Campfire when _campfire is { } campfire:
                return CampfireHandover.Show(this, campfire(), _lifetime);

            case RunDecision.Event when _event is { } tileEvent:
                return EventHandover.Show(this, tileEvent(), _lifetime);

            case RunDecision.RunEnd when _runEnd is { } runEnd:
                return RunEndHandover.Show(this, runEnd(), _lifetime);

            default:
                GD.PushError(
                    $"The run reached {decision} and this screen has no way to build it. Only a " +
                    "screen that already has a run may instantiate the board, and it must pass the " +
                    "composed screen to Drive.");

                return false;
        }
    }

    private static string Describe<T>(T? value) where T : struct =>
        value?.ToString() ?? "none";

    /// <summary>The screens a run's own state sends it to from here.</summary>
    /// <remarks>
    /// Named rather than tested inline, so the latch that stops a returned screen being re-entered
    /// has one value to compare — and so a shrine and a campfire, which are two tile kinds on ONE
    /// screen, count as one destination rather than two the run could be bounced between.
    /// </remarks>
    private enum RunDecision
    {
        /// <summary>S07, the perk draft a won fight leaves open.</summary>
        PerkDraft = 1,

        /// <summary>S08, the shop tile.</summary>
        Shop = 2,

        /// <summary>S11, the campfire and the shrine — one screen with two arms.</summary>
        Campfire = 3,

        // 5 was the Dice Forge tile's own screen. The forge installed die-face replacements and
        // the die has no faces to replace, so the screen is gone and the tile resolves in place —
        // a landing that costs nothing. The number stays retired rather than reused.

        /// <summary>
        /// S13 and S14, the death offer and the reward tally — one screen, because <c>02</c> §6 makes
        /// them one moment.
        /// </summary>
        RunEnd = 4,

        /// <summary>
        /// `19` Part A, the event card the tile draws — one destination whether the card is already
        /// drawn or not, because the draw is that screen's own first command.
        /// </summary>
        Event = 6,
    }
}
