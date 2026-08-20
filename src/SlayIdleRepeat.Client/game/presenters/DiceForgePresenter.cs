using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Dice;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>How far the Dice Forge screen has got with the read everything it draws depends on.</summary>
public enum DiceForgeStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is standing on an unresolved dice forge tile.</summary>
    Ready = 2,

    /// <summary>The run was read and is not standing on a forge.</summary>
    NotAtAForge = 3,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 4,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 5,
}

/// <summary>What one submission from this screen did.</summary>
public enum DiceForgeSubmission
{
    /// <summary>The command went to the host and was accepted.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the screen's own state does not permit it.</summary>
    RefusedNotAvailable = 2,

    /// <summary>The command went to the host and the rules layer refused it.</summary>
    RefusedByRules = 3,

    /// <summary>
    /// 🔒 The call itself did not complete. Told apart from <see cref="RefusedByRules"/> because a
    /// refusal is an answer about the game and a fault is the game not answering.
    /// </summary>
    HostUnavailable = 4,
}

/// <summary>One face of the run's die, as the screen draws it.</summary>
/// <param name="FaceIndex">1-based, 1..6 — the index <c>DICE_FORGE_CHOOSE</c> names.</param>
/// <param name="Current">What the face shows now, already composed with this run's upgrades.</param>
/// <param name="Upgradeable">
/// Whether the forge can act on it. `04` §1 makes only a Pip face a legal source, so a face an
/// earlier forge already turned into a Surge or a Fortune is offered as unpressable rather than
/// hidden — hiding it would leave the die looking as if it had four faces.
/// </param>
public sealed record DiceForgeFaceRow(int FaceIndex, string Current, bool Upgradeable);

/// <summary>One upgrade the forge offers, as the screen draws it.</summary>
/// <param name="OptionIndex">The index <c>DICE_FORGE_CHOOSE</c> names.</param>
/// <param name="Name">The option's name, resolved.</param>
/// <param name="NeedsPipCount">
/// Whether the option needs a target pip count — true for exactly one of them, "raise the pips",
/// which is relative to the face being upgraded rather than a fixed target.
/// </param>
public sealed record DiceForgeOptionRow(int OptionIndex, string Name, bool NeedsPipCount);

/// <summary>
/// Drives the Dice Forge screen: shows the six faces of the run's own die, and forges one.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The menu is the handler's, not this screen's.</b> <c>Handlers.DiceForgeChoose.Menu</c> is
/// the offered subset of `03`'s upgrade table — Star and Chain are withheld because nothing can
/// resolve a roll that lands on one — and this screen reads it rather than listing options of its
/// own. A screen with its own list would offer an option the rules layer refuses, or miss one it
/// allows, and either way the player finds out by pressing.
/// </para>
/// <para>
/// 🔴 <b>There is no way off this tile but forging.</b> A Dice Forge is resolved by
/// <c>DICE_FORGE_CHOOSE</c> and by nothing else, so this screen offers no "leave" — and it does not
/// need one: every one of the six faces of a starting die is a legal source, and the higher-pip
/// option is legal on all but the six. What it does offer, on every face and every option, is a
/// refusal that says so rather than a control that silently does nothing.
/// </para>
/// </remarks>
public sealed class DiceForgePresenter
{
    private const string TitleNameKey = "loc.dice_forge.title.name";
    private const string FaceLabelKey = "loc.dice_forge.face.label";
    private const string UpgradeActionKey = "loc.dice_forge.upgrade.action";
    private const string HigherPipNameKey = "loc.dice_forge.higher_pip.name";
    private const string ToSurgeNameKey = "loc.dice_forge.to_surge.name";
    private const string ToFortuneNameKey = "loc.dice_forge.to_fortune.name";
    private const string WithheldBlockKey = "loc.dice_forge.withheld.block";
    private const string LoadingStatusKey = "loc.dice_forge.loading.status";
    private const string RunMissingStatusKey = "loc.dice_forge.run_missing.status";
    private const string NotAtAForgeStatusKey = "loc.dice_forge.not_at_a_forge.status";
    private const string ReadUnavailableStatusKey = "loc.dice_forge.read_unavailable.status";
    private const string RefusedStatusKey = "loc.dice_forge.refused.status";
    private const string HostUnavailableStatusKey = "loc.dice_forge.host_unavailable.status";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>
    /// The tile kind a dice forge is, as the run reports it.
    /// </summary>
    /// <remarks>
    /// 🔒 Read off the rules layer's own enum and NOT transcribed, for the reason every other
    /// decision screen in this build gives: a kind inserted above this one renumbers this constant
    /// with it, where a copied literal would go on naming whatever took its place.
    /// </remarks>
    public const int DiceForgeTileKind = (int)TileKind.DiceForge;

    /// <summary>The name key each withheld-free menu option is drawn through, in menu order.</summary>
    /// <remarks>
    /// A map from the option's SHAPE rather than its index: the menu is a filter over the upgrade
    /// table, so an option added or withheld there renumbers every index after it, and a
    /// position-keyed list of names would silently mislabel the survivors.
    /// </remarks>
    private string NameOf(DiceForgeUpgradeOption option) => option switch
    {
        { IsHigherPip: true } => _strings.Resolve(HigherPipNameKey),
        { Kind: DieFaceKind.Surge } => _strings.Resolve(ToSurgeNameKey),
        { Kind: DieFaceKind.Fortune } => _strings.Resolve(ToFortuneNameKey),

        // An option the menu grew that this screen has no name for: its own token, so the control is
        // still pressable and still says something, rather than being drawn blank.
        _ => option.Kind?.ToString() ?? UpgradeText,
    };

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
    public DiceForgePresenter(
        IGameHost gameHost, LocaleStringCatalogue strings, PlayerId player, RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);

        _gameHost = gameHost;
        _strings = strings;
        _player = player;
        _run = run;
    }

    /// <summary>How far the read this screen depends on has got.</summary>
    public DiceForgeStage Stage { get; private set; } = DiceForgeStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The six faces of the run's die, in face order. Empty until the read lands.</summary>
    public IReadOnlyList<DiceForgeFaceRow> Faces { get; private set; } = [];

    /// <summary>The upgrades the forge offers, in menu order.</summary>
    public IReadOnlyList<DiceForgeOptionRow> Options { get; private set; } = [];

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The word a face row is labelled with, resolved.</summary>
    public string FaceLabel => _strings.Resolve(FaceLabelKey);

    /// <summary>The forge control's caption, resolved.</summary>
    public string UpgradeText => _strings.Resolve(UpgradeActionKey);

    /// <summary>The named reason two of the table's options are not offered, resolved.</summary>
    /// <remarks>
    /// A permanent line rather than one shown on a failure. Star and Chain are withheld in every
    /// build that cannot resolve them, so a line that came and went would suggest a state where they
    /// are on the menu.
    /// </remarks>
    public string WithheldText => _strings.Resolve(WithheldBlockKey);

    /// <summary>The line saying what the screen is doing while it is not yet an answer, resolved.</summary>
    public string StatusText => Stage switch
    {
        DiceForgeStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        DiceForgeStage.Ready => NothingLeftToSay,
        DiceForgeStage.NotAtAForge => _strings.Resolve(NotAtAForgeStatusKey),
        DiceForgeStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// The fault is read before the rejection, because a faulted submission carries no rejection at
    /// all: one says the game refused you, the other says the game did not answer.
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
            var state = await _gameHost.ReadOwnStateAsync(_player, _run, ct).ConfigureAwait(false);

            Settle(state);
        }
        catch (Exception)
        {
            Stage = DiceForgeStage.ReadUnavailable;
        }
    }

    /// <summary>Forges one face into one option's result.</summary>
    /// <param name="faceIndex">The face, 1-based.</param>
    /// <param name="optionIndex">The option's index in <see cref="Options"/>.</param>
    /// <param name="higherPipValue">
    /// The target pip count, required by the "raise the pips" option and ignored by the others.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public async Task<DiceForgeSubmission> ForgeAsync(
        int faceIndex, int optionIndex, int? higherPipValue, CancellationToken ct)
    {
        if (Stage != DiceForgeStage.Ready)
        {
            return DiceForgeSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(
                new DiceForgeChooseCommand(faceIndex, optionIndex, higherPipValue), ct)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// 🔒 The latch is taken BEFORE the await, not after it. Taken afterwards, a second press
    /// arriving while the first is in flight finds it unset and submits again — which on a forge is
    /// a second face upgraded from a single tap.
    /// </remarks>
    private async Task<DiceForgeSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return DiceForgeSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                return DiceForgeSubmission.RefusedByRules;
            }

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a tile the command has cleared.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                Carry(moved);
            }

            return DiceForgeSubmission.Submitted;
        }
        catch (Exception)
        {
            HostFaulted = true;
            RulesRejection = null;

            return DiceForgeSubmission.HostUnavailable;
        }
        finally
        {
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Run is not { } run)
        {
            Stage = DiceForgeStage.RunMissing;

            return;
        }

        Carry(run);
    }

    private void Carry(RunSnapshot run)
    {
        if (run.PendingTileKind != DiceForgeTileKind)
        {
            Stage = DiceForgeStage.NotAtAForge;
            Faces = [];
            Options = [];

            return;
        }

        Stage = DiceForgeStage.Ready;
        Faces = RunDieView.Project(run).Faces
            .Select(face => new DiceForgeFaceRow(face.FaceIndex, face.Rendered, face.Upgradeable))
            .ToArray();

        Options = DiceForgeMenu.Offered
            .Select((option, index) => new DiceForgeOptionRow(index, NameOf(option), option.IsHigherPip))
            .ToArray();
    }
}
