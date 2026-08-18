using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>Which arm of the Campfire / Shrine screen the run's pending tile opened.</summary>
public enum CampfireStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The run is standing on an unresolved campfire: three options, one of which works.</summary>
    Campfire = 2,

    /// <summary>The run is standing on an unresolved shrine: two buff rows and no choice.</summary>
    Shrine = 3,

    /// <summary>The run was read and is standing on neither. The screen is open on the wrong tile.</summary>
    NotAtEither = 4,

    /// <summary>There is no such run for this player.</summary>
    RunMissing = 5,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 6,
}

/// <summary>What one submission from this screen did.</summary>
public enum CampfireSubmission
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

/// <summary>The three things a campfire offers.</summary>
/// <remarks>
/// Named rather than numbered at the call sites, and carrying no zero member, so the wire index a
/// choice travels as is stated exactly once — on <see cref="CampfireOptionRow.ChoiceIndex"/>.
/// </remarks>
public enum CampfireOption
{
    /// <summary>Rest, healing the authored share of Max HP. The one option that works.</summary>
    Rest = 1,

    /// <summary>Raise one owned perk a tier. Refused: nothing tracks a perk-tier upgrade.</summary>
    UpgradePerk = 2,

    /// <summary>Take two reroll charges. Refused: no draft reroll charge exists to be granted.</summary>
    RerollCharges = 3,
}

/// <summary>One campfire option as the screen draws it.</summary>
/// <param name="Option">Which option this is.</param>
/// <param name="ChoiceIndex">What <c>CAMPFIRE_CHOOSE</c> carries for it.</param>
/// <param name="Label">The option's caption, already resolved.</param>
/// <param name="Available">Whether pressing it can do anything at all.</param>
/// <param name="BlockText">
/// Why it cannot, already resolved, or empty for an option that works. 🔒 Its own sentence, never
/// shared: the two unavailable options are refused by the rules layer with the same wire value and
/// for two completely different missing systems.
/// </param>
public sealed record CampfireOptionRow(
    CampfireOption Option, int ChoiceIndex, string Label, bool Available, string BlockText);

/// <summary>One row of the shrine's offer as the screen draws it.</summary>
/// <param name="BuffId">The buff id, for the log.</param>
/// <param name="Name">The buff's name, already resolved through the key the buff pool authors.</param>
/// <param name="IsTaken">
/// Whether this is the row the resolver will actually apply. Exactly one row is, and the player did
/// not choose it — see <see cref="CampfirePresenter.ShrineChoiceBlockText"/>.
/// </param>
public sealed record CampfireShrineRow(string BuffId, string Name, bool IsTaken);

/// <summary>
/// Drives the Campfire / Shrine screen — one screen with two arms, because a campfire and a shrine
/// are two tile kinds that ask the same question and the design lists them on one row.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The two arms never overlap.</b> A campfire draws three option cards and no buff rows; a
/// shrine draws two buff rows and no option cards. Offering a campfire option on a shrine would put
/// a control on screen whose command the rules layer refuses for a reason the player cannot see,
/// and offering shrine rows on a campfire would describe a draw that never happened.
/// </para>
/// <para>
/// 🔴 <b>Two of the three campfire options are refused, for two different missing systems.</b>
/// Raising a perk tier is refused because nothing anywhere tracks perk tiers as an upgradeable
/// thing outside the draft; taking two reroll charges is refused because no draft reroll charge
/// exists to grant. Both come back on the wire as the same value, so the sentence is the only thing
/// telling them apart — see <see cref="TheTwoRefusedOptionsAreRefusedForDifferentReasons"/>.
/// </para>
/// <para>
/// 🔴 <b>The shrine's choice is not the player's, and this screen says so.</b> There is no shrine
/// choose command in the frozen vocabulary at all, so resolving the tile takes the first of the two
/// rows drawn and applies only its immediate-heal half. The screen shows both rows — they are what
/// the shrine really drew — marks the one that will be taken, and names the absence rather than
/// drawing two buttons one of which is a lie.
/// </para>
/// <para>
/// 🔴 <b>The Cleanse arm cannot fire in this build.</b> The resolver has one, and it is reachable
/// only with an active cleansable curse; a run holds no curse list, so the flag is always false and
/// the second row is always a drawn buff. Named, because a shrine that silently never cleanses
/// looks exactly like one that rolled badly.
/// </para>
/// </remarks>
public sealed class CampfirePresenter
{
    /// <summary>
    /// ⚠️ Deliberately kept apart, and named so it can be found. Both refused campfire options come
    /// back as one wire value, so nothing but the wording distinguishes two unrelated gaps.
    /// </summary>
    private const string TheTwoRefusedOptionsAreRefusedForDifferentReasons =
        "Choice 1 raises an owned perk by a tier and choice 2 grants two reroll charges. The " +
        "handler refuses both, deliberately, and refuses rather than silently succeeding because " +
        "neither thing is tracked: no field records a perk tier as upgradeable outside the draft, " +
        "and no field counts draft reroll charges at all. They are two different missing systems " +
        "with two different owners, and the refusal reaches the client as the same value for both. " +
        "One sentence for the pair would tell a player that the game has one hole where it has two.";

    /// <summary>
    /// ⚠️ Deliberately not offered, and named so it can be found. The shrine picks for the player
    /// because the command that would let them pick was never written.
    /// </summary>
    private const string TheShrineChoiceIsNotThePlayers =
        "The design gives a shrine two options and a choice between them. The frozen command " +
        "vocabulary has no shrine choose in it, so resolving the tile is the only thing that can " +
        "happen, and the resolver takes the first of the two rows it drew — its own remarks call " +
        "that an assumption awaiting a choose command. Only the immediate-heal half of a taken buff " +
        "is applied, because nothing aggregates a run-scoped stat buff yet. Drawing two pressable " +
        "options here would offer a choice that cannot be submitted; drawing one would hide the " +
        "second row the shrine really drew. Both rows are shown, the taken one is marked, and the " +
        "absence is a sentence.";

    private const string TitleNameKey = "loc.campfire.title.name";
    private const string ShrineTitleNameKey = "loc.campfire.shrine_title.name";
    private const string ShrineBuffsLabelKey = "loc.campfire.shrine_buffs.label";
    private const string RestActionKey = "loc.campfire.rest.action";
    private const string UpgradePerkActionKey = "loc.campfire.upgrade_perk.action";
    private const string RerollChargesActionKey = "loc.campfire.reroll_charges.action";
    private const string ContinueActionKey = "loc.campfire.continue.action";
    private const string UpgradePerkBlockKey = "loc.campfire.upgrade_perk_untracked.block";
    private const string RerollChargesBlockKey = "loc.campfire.reroll_charges_untracked.block";
    private const string ShrineChoiceBlockKey = "loc.campfire.shrine_choice_absent.block";
    private const string ShrineCleanseBlockKey = "loc.campfire.shrine_cleanse_absent.block";
    private const string LoadingStatusKey = "loc.campfire.loading.status";
    private const string RunMissingStatusKey = "loc.campfire.run_missing.status";
    private const string NotAtACampfireStatusKey = "loc.campfire.not_at_a_campfire.status";
    private const string ReadUnavailableStatusKey = "loc.campfire.read_unavailable.status";
    private const string ShrineUnavailableStatusKey = "loc.campfire.shrine_unavailable.status";
    private const string RefusedStatusKey = "loc.campfire.refused.status";
    private const string HostUnavailableStatusKey = "loc.campfire.host_unavailable.status";

    /// <summary>The status line of a screen that has nothing left to say.</summary>
    private const string NothingLeftToSay = "";

    /// <summary>
    /// The tile kind a shrine is, as the run reports it.
    /// </summary>
    /// <remarks>
    /// 🔒 A transcription, for the same reason <see cref="BoardTileKinds"/> is one, and stated as an
    /// INDEX into that table so a case can ask the table what sits here and fail the day a kind is
    /// inserted above it.
    /// </remarks>
    public const int ShrineTileKind = 3;

    /// <summary>The tile kind a campfire is, as the run reports it. Transcribed with the one above.</summary>
    public const int CampfireTileKind = 7;

    /// <summary>The index <c>CAMPFIRE_CHOOSE</c> carries for resting — the one option that works.</summary>
    private const int RestChoiceIndex = 0;

    /// <summary>The index for raising a perk a tier, refused for its own named reason.</summary>
    private const int UpgradePerkChoiceIndex = 1;

    /// <summary>The index for taking reroll charges, refused for a different named reason.</summary>
    private const int RerollChargesChoiceIndex = 2;

    private readonly IGameHost _gameHost;
    private readonly LocaleStringCatalogue _strings;
    private readonly ContentSnapshot _content;
    private readonly PlayerId _player;
    private readonly RunId _run;

    private bool _submissionInFlight;

    /// <summary>Builds the screen over the host, the strings, the content set and the run.</summary>
    /// <param name="gameHost">The seam the run is read through and its commands are submitted through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the shrine's draw is projected against.</param>
    /// <param name="player">The profile this run belongs to.</param>
    /// <param name="run">The run being played.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public CampfirePresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player,
        RunId run)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _gameHost = gameHost;
        _strings = strings;
        _content = content;
        _player = player;
        _run = run;
    }

    /// <summary>Which arm the run's pending tile opened.</summary>
    public CampfireStage Stage { get; private set; } = CampfireStage.NotYetRead;

    /// <summary>Why the rules layer refused the last command that reached it, or null when none did.</summary>
    public RejectionReason? RulesRejection { get; private set; }

    /// <summary>Whether the last submission failed to complete at all.</summary>
    public bool HostFaulted { get; private set; }

    /// <summary>The three campfire options, in choice order. 🔒 Empty on the shrine arm.</summary>
    public IReadOnlyList<CampfireOptionRow> Options { get; private set; } = [];

    /// <summary>The two rows the shrine drew, in slot order. 🔒 Empty on the campfire arm.</summary>
    public IReadOnlyList<CampfireShrineRow> ShrineRows { get; private set; } = [];

    /// <summary>Whether the shrine's draw could be projected at all.</summary>
    /// <remarks>
    /// False rather than a crash, for the same reason the draft's cards have such a flag: the
    /// projection reads the buff pool out of the content set, and a set without one is a failure to
    /// report in words rather than to take the client down with.
    /// <para>
    /// 🔒 <b>What is caught is the content set failing to answer, and only that</b> — the exceptions
    /// a <c>ContentSnapshot</c> read raises. A blanket catch would turn a programming error inside
    /// the projection into the same quiet sentence, and this screen would report a missing buff pool
    /// while the rows it drew disagreed with the ones the tile is about to apply.
    /// </para>
    /// </remarks>
    public bool ShrineRowsAvailable { get; private set; }

    /// <summary>The campfire arm's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleNameKey);

    /// <summary>The shrine arm's heading, resolved.</summary>
    public string ShrineTitle => _strings.Resolve(ShrineTitleNameKey);

    /// <summary>The lead-in the two buff rows are listed under, resolved.</summary>
    public string ShrineBuffsLabel => _strings.Resolve(ShrineBuffsLabelKey);

    /// <summary>The shrine arm's one action, resolved.</summary>
    public string ContinueText => _strings.Resolve(ContinueActionKey);

    /// <summary>
    /// The named fact that the shrine's choice is not the player's, resolved.
    /// </summary>
    /// <remarks>A permanent line — see <see cref="TheShrineChoiceIsNotThePlayers"/>.</remarks>
    public string ShrineChoiceBlockText => _strings.Resolve(ShrineChoiceBlockKey);

    /// <summary>The named fact that the Cleanse arm cannot fire, resolved.</summary>
    public string ShrineCleanseBlockText => _strings.Resolve(ShrineCleanseBlockKey);

    /// <summary>The line saying what the screen is doing while its arm is not an answer, resolved.</summary>
    public string StatusText => Stage switch
    {
        CampfireStage.NotYetRead => _strings.Resolve(LoadingStatusKey),
        CampfireStage.Campfire => NothingLeftToSay,
        CampfireStage.Shrine => ShrineRowsAvailable
            ? NothingLeftToSay
            : _strings.Resolve(ShrineUnavailableStatusKey),
        CampfireStage.NotAtEither => _strings.Resolve(NotAtACampfireStatusKey),
        CampfireStage.RunMissing => _strings.Resolve(RunMissingStatusKey),
        _ => _strings.Resolve(ReadUnavailableStatusKey),
    };

    /// <summary>The line about the last command the host answered, resolved — empty until one has.</summary>
    /// <remarks>
    /// 🔒 The fault is read first, because a faulted submission carries no rejection at all and the
    /// two must not share a sentence. The two refused options do not appear here: they are refused
    /// by this screen without a round trip, and each carries its own sentence on its own card.
    /// </remarks>
    public string RejectionText => HostFaulted
        ? _strings.Resolve(HostUnavailableStatusKey)
        : RulesRejection is null ? NothingLeftToSay : _strings.Resolve(RefusedStatusKey);

    /// <summary>Reads the run this screen is about and settles whichever arm its tile opened.</summary>
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
            Stage = CampfireStage.ReadUnavailable;
        }
    }

    /// <summary>Submits <c>CAMPFIRE_CHOOSE</c> for one of the campfire's options.</summary>
    /// <remarks>
    /// 🔒 An option this screen draws as unavailable is refused here rather than submitted. The
    /// rules layer would refuse it too, but with a value shared by four other things — so a round
    /// trip would replace a sentence that names the missing system with one that names nothing.
    /// </remarks>
    /// <param name="option">The option pressed.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<CampfireSubmission> ChooseAsync(CampfireOption option, CancellationToken ct)
    {
        if (Stage != CampfireStage.Campfire)
        {
            return CampfireSubmission.RefusedNotAvailable;
        }

        var row = Options.FirstOrDefault(offered => offered.Option == option);

        if (row is not { Available: true })
        {
            return CampfireSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new CampfireChooseCommand(row.ChoiceIndex), ct).ConfigureAwait(false);
    }

    /// <summary>Submits <c>RESOLVE_TILE</c>, which is how the shrine arm is left.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<CampfireSubmission> ContinueAsync(CancellationToken ct)
    {
        if (Stage != CampfireStage.Shrine)
        {
            return CampfireSubmission.RefusedNotAvailable;
        }

        return await SubmitAsync(new ResolveTileCommand(), ct).ConfigureAwait(false);
    }

    /// <summary>The three option rows a campfire offers, built from the keys above.</summary>
    /// <remarks>See <see cref="TheTwoRefusedOptionsAreRefusedForDifferentReasons"/>.</remarks>
    private IReadOnlyList<CampfireOptionRow> CampfireOptions() =>
    [
        new CampfireOptionRow(
            CampfireOption.Rest,
            RestChoiceIndex,
            _strings.Resolve(RestActionKey),
            Available: true,
            NothingLeftToSay),
        new CampfireOptionRow(
            CampfireOption.UpgradePerk,
            UpgradePerkChoiceIndex,
            _strings.Resolve(UpgradePerkActionKey),
            Available: false,
            _strings.Resolve(UpgradePerkBlockKey)),
        new CampfireOptionRow(
            CampfireOption.RerollCharges,
            RerollChargesChoiceIndex,
            _strings.Resolve(RerollChargesActionKey),
            Available: false,
            _strings.Resolve(RerollChargesBlockKey)),
    ];

    /// <remarks>
    /// 🔒 The latch is taken BEFORE the await, not after it. Taken afterwards, a second press
    /// arriving while the first is in flight finds it unset and submits again — which is how a
    /// double-tap on the rest option heals twice, and how it shipped once already on another screen
    /// in this milestone.
    /// </remarks>
    private async Task<CampfireSubmission> SubmitAsync(GameCommand command, CancellationToken ct)
    {
        if (_submissionInFlight)
        {
            return CampfireSubmission.RefusedNotAvailable;
        }

        _submissionInFlight = true;
        HostFaulted = false;

        try
        {
            var outcome = await _gameHost.SubmitAsync(_player, _run, command, ct).ConfigureAwait(false);

            RulesRejection = outcome.Rejection;

            if (!outcome.Accepted)
            {
                return CampfireSubmission.RefusedByRules;
            }

            // The state comes back with the outcome rather than being read again: a second read
            // would be a window in which the screen still draws a tile the command has cleared.
            if (outcome.State.Run?.ToSnapshot() is { } moved)
            {
                Carry(moved);
            }

            return CampfireSubmission.Submitted;
        }
        catch (Exception)
        {
            // A faulted call carried no outcome, so there is no rejection to report and reporting
            // one would be inventing an answer the game never gave.
            HostFaulted = true;
            RulesRejection = null;

            return CampfireSubmission.HostUnavailable;
        }
        finally
        {
            // Released on completion: a rest that never answered healed nothing and left the tile
            // pending, so the retry has to be able to reach the host.
            _submissionInFlight = false;
        }
    }

    private void Settle(OwnStateResult state)
    {
        if (state.Lookup != OwnStateLookup.Found || state.View?.Run is not { } run)
        {
            Stage = CampfireStage.RunMissing;
            return;
        }

        Carry(run);
    }

    private void Carry(RunSnapshot run)
    {
        Options = [];
        ShrineRows = [];
        ShrineRowsAvailable = false;

        if (run.PendingTileKind == CampfireTileKind)
        {
            Stage = CampfireStage.Campfire;
            Options = CampfireOptions();

            return;
        }

        if (run.PendingTileKind != ShrineTileKind)
        {
            Stage = CampfireStage.NotAtEither;

            return;
        }

        Stage = CampfireStage.Shrine;

        ProjectShrine(run);
    }

    /// <remarks>See <see cref="ShrineRowsAvailable"/> for why only a content read's failure is caught.</remarks>
    private void ProjectShrine(RunSnapshot run)
    {
        try
        {
            if (ShrineView.Project(run, _content) is not { } shrine)
            {
                return;
            }

            ShrineRows = Draw(shrine);
            ShrineRowsAvailable = true;
        }
        catch (ContentException)
        {
            ShrineRows = [];
            ShrineRowsAvailable = false;
        }
    }

    /// <summary>The projection's rows as the screen draws them, named through the pool's own keys.</summary>
    private IReadOnlyList<CampfireShrineRow> Draw(ShrineView shrine)
    {
        var rows = new CampfireShrineRow[shrine.Rows.Count];

        for (var slot = 0; slot < rows.Length; slot++)
        {
            rows[slot] = new CampfireShrineRow(
                shrine.Rows[slot].BuffId,
                _strings.Resolve(shrine.Rows[slot].DisplayNameKey),
                slot == shrine.TakenRowIndex);
        }

        return Array.AsReadOnly(rows);
    }
}
