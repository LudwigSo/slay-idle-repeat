using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Handlers;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 `30` §2 — the transition function. <em>"The whole domain reduces to one signature. Everything
/// else in this document is detail about its arguments."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The five properties of `30` §2.1, and where each one actually lives</b> — because a
/// property nothing enforces is a paragraph:
/// </para>
/// <list type="table">
///   <item><term><b>P1 · Pure</b></term><description>No I/O, no clock, no ambient randomness. Time
///   arrives as <c>GameContext.NowUtc</c> and randomness as the run's committed seed or
///   <c>GameContext.CommandSeed</c> (`30` §3). Enforced by
///   <c>DomainPurityTests.Domain_has_no_ambient_time_or_randomness</c> and
///   <c>AmbientApiTests</c>. The one static this type holds is <see cref="Dispatch"/>, which is
///   built once and never written again — a table, not state.</description></item>
///   <item><term><b>P2 · Synchronous</b></term><description>Enforced assembly-wide by
///   <c>DomainPurityTests.Domain_is_synchronous</c>; async in the domain is always a symptom of
///   hidden I/O.</description></item>
///   <item><term><b>P3 · Total</b></term><description>Every command on every state returns a
///   result. An unregistered command type is <c>ILLEGAL_STATE</c>, not an exception. ⚠️ The
///   converse is drawn deliberately: an <b>exception</b> out of <see cref="Apply"/> means the
///   <em>caller</em> or the <em>domain</em> is wrong — a null argument, a slice missing the run its
///   command needs, a handler that hand-wrote an RNG counter — never that the player asked for
///   something they cannot have.</description></item>
///   <item><term><b>P4 · Immutable</b></term><description>The slice is <b>cloned</b> before the
///   handler runs (see <see cref="Clone"/>), so the caller's aggregates are untouched whatever the
///   handler does, and a rejection returns the caller's own slice.</description></item>
///   <item><term><b>P5 · Complete</b></term><description>Enforced by
///   <c>DomainPurityTests.Every_command_type_is_handled_by_Apply</c>: a concrete <c>GameCommand</c>
///   subtype named by neither this type nor anything under <c>Core/Handlers/</c> fails the
///   build.</description></item>
/// </list>
/// <para>
/// 🔒 <b>A façade, not a god function</b> (`30` §2.2). <see cref="Apply"/> owns the five things that
/// are true of <em>every</em> command — clone, catch up, dispatch, fold the RNG counters back, stamp
/// the events — and owns no game rule at all. Everything a specific command means is in its handler.
/// </para>
/// <para>
/// 🔒 <b>The dispatch table carries all 49 rows of `14` §2.3 from M1-02</b>, and the landing order
/// is why it could: M1-06 shipped the base type and an empty table, because
/// <c>Every_command_type_is_handled_by_Apply</c> fails the build for any concrete subtype no row
/// names — with zero subtypes the rule quantified over nothing and stayed green, so the seam could
/// precede the vocabulary. From M1-02 that rule is <b>fully loaded</b>: 49 concrete subtypes, 49
/// rows, and a fiftieth command declared without a row turns the build red on the commit that
/// declares it.
/// </para>
/// </remarks>
public static class GameRules
{
    /// <summary>
    /// 🔒 `30` §2.2's dispatch table — <b>the</b> place a command is bound to its handler, its
    /// `14` §2.3 wire name and its <see cref="CommandKind"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>M1-02 adds 49 rows below and nothing else moves.</b> One row is one call:
    /// </para>
    /// <code>
    /// private static readonly CommandDispatch Dispatch = new CommandDispatch()
    ///     .Deferred&lt;RollDiceCommand&gt;("ROLL_DICE", CommandKind.Run, "M3-02")
    ///     .Handled&lt;BeginSessionCommand&gt;("BEGIN_SESSION", CommandKind.Meta, BeginSession.Handle);
    /// </code>
    /// <para>
    /// A command whose <em>system</em> arrives later is registered with <c>Deferred</c> and rejects
    /// with <c>ILLEGAL_STATE</c> naming its milestone; swapping it to <c>Handled</c> on the day that
    /// milestone lands is a one-line edit. Every <c>Deferred</c> row also carries a
    /// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c> entry, which is what makes the deferral
    /// expire by itself instead of waiting to be noticed.
    /// </para>
    /// <para>
    /// 🔒 <b>M1-09 put the first real handler under <c>Core/Handlers/</c>, and the sentence that used
    /// to stand here — "handlers stay out of it until M1-09" — is corrected rather than left to go
    /// stale (steering <b>S4</b>'s known limit).</b> <c>Domain.HandlersNamespace</c> has moved from
    /// <c>SubjectSetFloorTests.Pending</c> to <c>Live</c>, and both rules keyed on it are awake:
    /// <c>Handlers_and_Rules_are_internal</c> now quantifies over a real type on its <c>Handlers</c>
    /// half for the first time, and <c>Every_command_type_is_handled_by_Apply</c>'s dispatch surface
    /// is no longer <c>GameRules</c> alone. M1-06's reason for keeping the plumbing out still holds
    /// for the plumbing: <c>CommandDispatch</c>, <c>CommandRegistration</c> and <c>HandlerInput</c>
    /// are not handlers and stay where they are.
    /// </para>
    /// <para>
    /// 🔒 <b>M1-02 landed the 49 rows and nothing reshaped</b> — one row is one chained call, exactly
    /// as the sketch above promised. The order is `14` §2.3's own: the 19 run rows in the table's
    /// order, then the 30 meta rows in theirs, so the registry and the document can be read side by
    /// side. <c>SlayIdleRepeat.Core.Tests.CommandVocabularyTests</c> pins the resulting <b>set</b>
    /// against a hand-transcribed literal list in both directions, because 49 <em>wrong</em> names
    /// also count 49.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>START_RUN</c> is <c>CommandKind.Run</c>, and it is the row worth pausing on.</b>
    /// `14` §2.3 submits it on the <em>player</em> endpoint, because no <c>runId</c> exists yet — a
    /// transport fact. The kind is a domain fact: it decides whether <see cref="Apply"/> opens a
    /// <c>RunRngScope</c> over the run's `14` §8.1 counters and whether the run's `14` §16.3 sliding
    /// TTL moves. Matching the kind to the endpoint would give the one command that commits
    /// <c>runSeed</c> no scope at all.
    /// </para>
    /// <para>
    /// 🔒 <b>Forty-eight rows are <c>Deferred</c> and one is <c>Handled</c>.</b> M1-09 swapped
    /// <c>BEGIN_SESSION</c> — `30` §2.3's day cycle — to <c>Handled</c>, which is the one-line edit
    /// this table's shape was designed for and the first time <see cref="Execute"/>'s
    /// <c>registration.IsHandled</c> arm runs over the production table. ⚠️ <b>The consequence for
    /// every handler-shaped rule stated over this table, which used to be quantifying over
    /// nothing:</b> they now have exactly one subject, so a floor by identity rather than by count is
    /// what keeps them honest — see <c>Every_command_type_is_handled_by_Apply</c>. Each row's owner
    /// is the task the tracker gives for the
    /// system behind the command — read off <c>IMPLEMENTATION_TRACKER.md</c>'s task rows rather than
    /// inferred, because a wrong owner is a deferral that expires at the wrong time (steering
    /// <b>S4</b>). The owner lives <em>here</em>, on the row, rather than in a mirrored
    /// <c>GapRegister</c> entry per command — which is not a preference but a mechanical fact:
    /// <c>GapRegister.Expired</c> fires on a <c>Gap</c> whose <c>Subject</c> already exists in
    /// <c>Core</c>, and the subject would be the command type this very row registers, so the entry
    /// would fail the build the moment it was written. (The softer argument holds as well: forty-nine
    /// <c>WaitsFor</c> names for systems whose milestones have not chosen them is what the register's
    /// own remarks call "the invention S6 forbids, dressed as bookkeeping".) What <c>GapRegister</c>
    /// carries for this task is the thing it can decide — the `14` §2.3 <b>inventory</b>, transcribed
    /// into <c>Surfaces</c>, which fails the build if a row of the registry ever stops being
    /// declared. `14` §2.3's <b>payload</b> column is deferred separately, in <c>CommandPayload</c>.
    /// </para>
    /// </remarks>
    private static readonly CommandDispatch Dispatch = new CommandDispatch()

        // ------------------------------------------------ `14` §2.3 — the 19 RUN commands
        .Deferred<StartRunCommand>("START_RUN", CommandKind.Run, "M3-15")
        .Deferred<RollDiceCommand>("ROLL_DICE", CommandKind.Run, "M3-04")
        .Deferred<UseRerollCommand>("USE_REROLL", CommandKind.Run, "M3-04")
        .Deferred<ChooseForkCommand>("CHOOSE_FORK", CommandKind.Run, "M3-02")
        .Deferred<ResolveTileCommand>("RESOLVE_TILE", CommandKind.Run, "M3-03")
        .Deferred<PickPerkCommand>("PICK_PERK", CommandKind.Run, "M3-06")
        .Deferred<RerollDraftCommand>("REROLL_DRAFT", CommandKind.Run, "M3-06")
        .Deferred<SkipDraftCommand>("SKIP_DRAFT", CommandKind.Run, "M3-06")
        .Deferred<ShopBuyCommand>("SHOP_BUY", CommandKind.Run, "M3-08")
        .Deferred<ShopRefreshCommand>("SHOP_REFRESH", CommandKind.Run, "M3-08")
        .Deferred<EventChooseCommand>("EVENT_CHOOSE", CommandKind.Run, "M3-09")
        .Deferred<MinigameSubmitCommand>("MINIGAME_SUBMIT", CommandKind.Run, "M3-10")
        .Deferred<CampfireChooseCommand>("CAMPFIRE_CHOOSE", CommandKind.Run, "M3-11")
        .Deferred<StartBattleCommand>("START_BATTLE", CommandKind.Run, "M3-05")
        .Deferred<ConfirmBattleResultCommand>("CONFIRM_BATTLE_RESULT", CommandKind.Run, "M3-05")
        .Deferred<ReviveCommand>("REVIVE", CommandKind.Run, "M3-13")
        .Deferred<UseConsumableCommand>("USE_CONSUMABLE", CommandKind.Run, "M3-08")
        .Deferred<EndRunCommand>("END_RUN", CommandKind.Run, "M3-13")
        .Deferred<AbandonRunCommand>("ABANDON_RUN", CommandKind.Run, "M3-13")

        // ----------------------------------------------- `14` §2.3 — the 30 META commands
        .Handled<BeginSessionCommand>("BEGIN_SESSION", CommandKind.Meta, BeginSession.Handle)
        .Deferred<SkipFtueCommand>("SKIP_FTUE", CommandKind.Meta, "M4-12")
        .Deferred<EquipCommand>("EQUIP", CommandKind.Meta, "M4-03")
        .Deferred<MergeCommand>("MERGE", CommandKind.Meta, "M4-04")
        .Deferred<EnhanceCommand>("ENHANCE", CommandKind.Meta, "M4-04")
        .Deferred<SalvageCommand>("SALVAGE", CommandKind.Meta, "M4-04")
        .Deferred<SpendTalentCommand>("SPEND_TALENT", CommandKind.Meta, "M4-06")
        .Deferred<RespecCommand>("RESPEC", CommandKind.Meta, "M4-06")
        .Deferred<LevelPetCommand>("LEVEL_PET", CommandKind.Meta, "M4-07")
        .Deferred<AscendPetCommand>("ASCEND_PET", CommandKind.Meta, "M4-07")
        .Deferred<EquipPetCommand>("EQUIP_PET", CommandKind.Meta, "M4-07")
        .Deferred<EquipMountCommand>("EQUIP_MOUNT", CommandKind.Meta, "M4-08")
        .Deferred<ClaimQuestCommand>("CLAIM_QUEST", CommandKind.Meta, "M4-09")
        .Deferred<RerollQuestCommand>("REROLL_QUEST", CommandKind.Meta, "M4-09")
        .Deferred<ClaimAdRewardCommand>("CLAIM_AD_REWARD", CommandKind.Meta, "M15-03")
        .Deferred<ClaimCalendarCommand>("CLAIM_CALENDAR", CommandKind.Meta, "M4-09")
        .Deferred<ClaimInboxCommand>("CLAIM_INBOX", CommandKind.Meta, "M5-08")
        .Deferred<SpinWheelCommand>("SPIN_WHEEL", CommandKind.Meta, "M4-09")
        .Deferred<SetFocusCommand>("SET_FOCUS", CommandKind.Meta, "M4-04")
        .Deferred<ReforgeItemCommand>("REFORGE_ITEM", CommandKind.Meta, "M4-04")
        .Deferred<RetuneItemCommand>("RETUNE_ITEM", CommandKind.Meta, "M4-04")
        .Deferred<SavePresetCommand>("SAVE_PRESET", CommandKind.Meta, "M4-10")
        .Deferred<ApplyPresetCommand>("APPLY_PRESET", CommandKind.Meta, "M4-10")
        .Deferred<ShopPurchaseCommand>("SHOP_PURCHASE", CommandKind.Meta, "M4-09")
        .Deferred<OpenChestCommand>("OPEN_CHEST", CommandKind.Meta, "M4-02")
        .Deferred<OpenEggCommand>("OPEN_EGG", CommandKind.Meta, "M4-02")
        .Deferred<OpenCrateCommand>("OPEN_CRATE", CommandKind.Meta, "M4-02")
        .Deferred<UploadGhostCommand>("UPLOAD_GHOST", CommandKind.Meta, "M12-01")
        .Deferred<StartDuelCommand>("START_DUEL", CommandKind.Meta, "M12-04")
        .Deferred<SubmitDuelCommand>("SUBMIT_DUEL", CommandKind.Meta, "M12-04");

    /// <summary>
    /// The shared empty event list. It is what an accepted command that produced nothing returns, so
    /// the no-op path allocates nothing — and, less obviously, it is what stops <see cref="Stamp"/>
    /// handing a <b>handler's own</b> empty list on as the result's: a handler that returned a
    /// <c>List&lt;DomainEvent&gt;</c> it still holds could otherwise append to
    /// <c>CommandResult.Events</c> after <c>Apply</c> returned.
    /// </summary>
    private static readonly ReadOnlyCollection<DomainEvent> NoEvents =
        Array.AsReadOnly(Array.Empty<DomainEvent>());

    /// <summary>
    /// 🔒 Recorded assumption <b>A5</b> — the `30` §7 attribution token every regeneration accrual
    /// is logged under, and the column `21` §8.3 groups <c>income_attribution.csv</c> by.
    /// </summary>
    /// <remarks>
    /// A stable <c>lower_snake_case</c> <em>identifier</em>, not a balance number, so `21` §3.1's
    /// "tunables are data" rule does not reach it — there is no dial here, only a name. It is
    /// recorded as an assumption anyway because <see cref="AdvanceTime"/> is the first high-volume
    /// producer of <c>CurrencyChanged</c> in the game and `30` §7 fixes no vocabulary for
    /// <c>Reason</c>: whatever token lands here is the one the economy dashboards are built on.
    /// </remarks>
    private const string EnergyRegenReason = "energy_regen";

    /// <summary>
    /// 🔒 Every registered command type, by the `14` §2.3 wire name its dispatch row declares — the
    /// <b>single</b> declared source of that mapping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the alternative was a guess. M1-07 needed a type→wire-name mapping to pin
    /// the <c>CommandSeed</c> invariant and had to build a documented <em>heuristic</em>
    /// (<c>SpinWheelCommand</c> → <c>SPIN_WHEEL</c>, which reads <c>OpenPvPCommand</c> as
    /// <c>OPEN_PV_P</c>) because no authored scheme existed — carried-forward item 4. Declaring the
    /// name on the registration and reading it here retires the heuristic: the mapping is now
    /// stated, not inferred, and it is stated in the one place that also has to know the command's
    /// handler and kind, so the three cannot drift apart.
    /// </para>
    /// <para>
    /// ⚠️ <c>internal</c> until something outside <c>Core</c> needs it. Today's consumers are
    /// <c>SlayIdleRepeat.Core.Tests.CommandSeedPin</c> and <c>CommandVocabularyTests</c>, which the
    /// `30` §11.3 <c>InternalsVisibleTo</c> grant already reaches; M5-03's wire envelope is the first
    /// caller that will need it public, and it should read this rather than declare a second table
    /// (`30` §11.6).
    /// </para>
    /// <para>
    /// 🔒 <b>M5-03 will need three members, not this one</b> — named here so that task does not
    /// discover it halfway through and declare a second table anyway. `14` §2.3 splits the endpoints
    /// (<c>/run/{runId}/command</c> against <c>/player/command</c>) on the <see cref="CommandKind"/>,
    /// which this dictionary does not carry: it is reachable only through
    /// <see cref="RegistrationFor"/> → <c>CommandRegistration</c>, and all three are
    /// <c>internal</c>. Making them public is a pure accessibility edit — no signature exposes a
    /// type that would have to become public with them — but it is three edits and one ruling, not
    /// one edit. ⚠️ <c>START_RUN</c> is the exception in both directions and M5-03 owns it: a
    /// <c>CommandKind.Run</c> row that arrives on the <em>player</em> endpoint.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, Type> CommandTypesByWireName => Dispatch.TypesByWireName;

    /// <summary>The dispatch row for a command type, or <c>null</c> when no row names it.</summary>
    /// <remarks>Exposed for the domain suite, which drives the table's decisions directly.</remarks>
    internal static CommandRegistration? RegistrationFor(Type commandType) => Dispatch.For(commandType);

    /// <summary>
    /// 🔒 `30` §2 — the one public way to change state in this game.
    /// </summary>
    /// <param name="state">The aggregates this command may read or write (`30` §4.1).</param>
    /// <param name="command">What the player intends (`14` §2.3).</param>
    /// <param name="context">Everything ambient, passed as data (`30` §3).</param>
    /// <returns>
    /// Whether the command was accepted, the domain-tier reason if it was not, the complete
    /// resulting state, and the events it produced.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// 🔒 A <b>defect</b>, never a refusal: the slice does not carry the run its command acts on, an
    /// aggregate does not round-trip through its own snapshot, a handler hand-wrote an RNG stream
    /// position, a handler stamped an event's <c>Sequence</c> itself, or — M1-09's addition — a
    /// `14` §2.3 <b>⚄</b> command reached a handler that draws while <c>GameContext.CommandSeed</c>
    /// is <c>null</c> (<see cref="HandlerInput"/>'s <c>MetaDraws</c>). Every one of the five is a
    /// miswired caller or a rule that is wrong; none is a player asking for something they cannot
    /// have.
    /// </exception>
    public static CommandResult Apply(WorldSlice state, GameCommand command, GameContext context) =>
        Execute(Dispatch, state, command, context);

    /// <summary>
    /// <see cref="Apply"/>'s body, over an explicit dispatch table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Parameterised for the reason <c>GapRegister.Expired(entries)</c> and
    /// <c>SnapshotFieldOrderPin.Violations(...)</c> are</b>: the domain suite has to drive these
    /// rules against shapes that must <b>never</b> be committed to <c>Core</c> — a handler that
    /// hand-writes an RNG counter, a handler that stamps its own <c>Sequence</c>, a command whose
    /// system does not exist. ⚠️ M1-02 filled the real table with 49 rows and <b>none of that
    /// changed</b>: every one of those rows was <c>Deferred</c>, so the production table held
    /// <em>no handler at all</em>, and every handler-shaped rule stated over it would have been
    /// asserted over nothing and would have reported success forever (steering <b>S3</b>). 🔒 <b>M1-09
    /// changed exactly that much and no more:</b> the production table now holds <b>one</b> handler,
    /// so the two shapes below are no longer merely unreachable there — they are shapes a real
    /// handler could grow — and this parameter is still what lets the suite drive them without
    /// committing one. The third shape —
    /// "a command whose system does not exist" — is the one the real table now has, 49 times, and
    /// <c>Commands.CommandVocabularyTests</c> drives it there rather than here.
    /// </para>
    /// <para>
    /// ⚠️ It is <c>internal</c> and it is not a second entry point. `30` §11.2's <em>"the only public
    /// way to change state in this game is <c>GameRules.Apply</c>"</em> is unchanged and still
    /// mechanically checked — <c>Apply_is_the_only_public_mutation</c> requires every method named
    /// <c>Apply</c> to be public and static, and <c>InternalsVisibleTo</c> reaches exactly one
    /// assembly (`30` §11.3).
    /// </para>
    /// </remarks>
    internal static CommandResult Execute(
        CommandDispatch dispatch, WorldSlice state, GameCommand command, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var registration = dispatch.For(command.GetType());

        // 🔒 P3 (total). An unregistered command type does NOT throw — it is refused, with the
        // domain-tier catch-all. 14 §16.2's UNKNOWN_COMMAND_TYPE is a TRANSPORT value: the server
        // refuses a wire name it has no row for before the domain is ever invoked, and 30 §2
        // forbids Apply from returning one. Every_command_type_is_handled_by_Apply is what keeps
        // this arm unreachable in practice — it fails the build for any concrete command no row
        // names — so this is the answer for a caller that built a command type by hand.
        if (registration is null)
        {
            return CommandResult.Reject(RejectionReason.ILLEGAL_STATE, state);
        }

        // 🔒 A run command whose slice carries no run is a LOADING defect, not a rejection. 30 §4.1
        // makes "loading the right slice" the Application layer's job, and 14 §16.2's RUN_NOT_FOUND
        // is a transport-tier value that never reaches Apply. Answering ILLEGAL_STATE here would
        // tell the player a rule refused them and leave the miswired caller running.
        if (registration.Kind == CommandKind.Run && state.Run is null)
        {
            throw new InvalidOperationException(
                "'" + registration.WireName + "' is a CommandKind.Run command and this WorldSlice " +
                "carries no Run. 30 §4.1 makes loading the right slice the Application layer's job; " +
                "14 §16.2's RUN_NOT_FOUND is a TRANSPORT-tier value, refused before the domain is " +
                "invoked, so Apply may not return it (30 §2). This is a miswired caller, not a " +
                "player asking for something they cannot have.");
        }

        // P4. Everything from here works on a copy; the caller's slice is never written to.
        var working = Clone(state, context.Content);

        // 🔒 30 §2.3's AdvanceTime, FIRST BY CONSTRUCTION. The handler never receives the raw slice
        // — only the one this produced — so nothing a handler can write runs before the catch-up.
        // Its events are held until the handler has answered, and PREPENDED to the handler's: they
        // happened first, and 30 §7's Sequence orders one command's list. See AdvanceTime.
        var caughtUp = AdvanceTime(working, context);

        var rng = registration.Kind == CommandKind.Run
            ? new RunRngScope(working.Run!.RunSeed, working.Run.RngStreamPositions)
            : null;

        // 🔒 The baseline FoldRngPositions compares against, read HERE: after the clone and the
        // catch-up, and before the handler. Run.RngStreamPositions is a frozen view that
        // CommitStreamPositions replaces wholesale, so this reference is the positions as they stood
        // the instant before the handler ran — which makes "these two differ" mean exactly one
        // thing, that the HANDLER called that seam. Reading it off the caller's run instead would
        // also be reading it from before AdvanceTime.
        //
        // ⚠️ M1-08 SETTLED THE OTHER HALF OF THAT SENTENCE, and it is worth stating plainly rather
        // than leaving the reader to compare two comments: catch-up writes NOTHING on the Run (see
        // AdvanceTime's remarks), so the misattribution this placement guards against — naming the
        // handler for a write the catch-up made — is today unreachable, not merely avoided. The
        // placement stays because M3's run boundaries land inside AdvanceTime and that ruling is
        // about what catch-up may touch, not about where this line sits.
        var committedPositions = working.Run?.RngStreamPositions;

        // 🔒 THE WHOLE RUN, not only its counters, and only for a META command. M1-09's architecture
        // review found the hole the instant Core/Handlers/ had an occupant: a CommandKind.Meta
        // command is dispatched perfectly happily with a run in the slice, HandlerInput.Run hands it
        // that run, and FoldRngPositions guards ONLY the 14 §8.1 stream positions — so Gold, HP,
        // Position and the per-run ad uses were writable by a handler that has no business in the run
        // at all, and Apply would return the mutated run with 14 §16.3's TTL deliberately NOT
        // stamped (see MarkApplied). A shop visit could have quietly moved a run's Gold and left the
        // run looking untouched since its last real command.
        //
        // ⚠️ A snapshot rather than a reference: Run is a class with internal mutators, so holding
        // the aggregate would compare it against itself. RunSnapshot is a record, so this is one
        // ToSnapshot() and one value comparison — measured against the two full round trips Clone
        // already pays per command, and only on the meta commands that carry a run at all.
        var untouchedRun = registration.Kind == CommandKind.Meta ? working.Run?.ToSnapshot() : null;

        var handled = registration.IsHandled
            ? registration.Handler!(command, new HandlerInput(working, context, rng))

            // A command whose row exists but whose SYSTEM arrives in a later milestone. See
            // CommandDispatch.Deferred for why this is ILLEGAL_STATE rather than a new 14 §16.2
            // value, and why registration.DeferredTo is mirrored by a GapRegister entry that makes
            // the deferral expire by itself.
            : HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);

        if (!handled.Accepted)
        {
            // 🔒 The working copy is DISCARDED, so a refused command provably changed nothing —
            // including any draws its handler took before the rule refused it. 14 §8.1 needs that:
            // a run that consumed draw indices on a rejected command would replay differently.
            //
            // 🔒 THE CATCH-UP GOES WITH IT, events and all, and M1-08 rules that safe rather than
            // leaving the reader to infer it. Nothing AdvanceTime does is consumptive: every
            // boundary it moves is DERIVED from NowUtc and the stored anchors, never spent. A1's
            // anchor rule is what makes that exact rather than approximate — the anchor advances by
            // wholeUnits × the interval, so the sub-interval remainder survives in the gap — and the
            // next accepted command therefore accrues the whole elapsed span from the same anchors
            // and clears the same boundaries. A player spamming an illegal move loses nothing.
            // 14 §7.1's economy log correspondingly carries no row for a command that changed
            // nothing, which is why caughtUp is dropped here rather than published.
            return CommandResult.Reject(handled.Rejection!.Value, state);
        }

        // 🔒 THE ORDER IS DELIBERATE AND IT IS A STEERING-S2 DECISION. A meta handler that
        // hand-wrote a stream position trips BOTH checks — a position is part of the run's snapshot —
        // and the two messages send the reader to different places: one says "draw through
        // HandlerInput.Rng and write nothing", the other says "this command has no business in the
        // run at all". The narrower diagnosis is the more useful one, so it runs first. Measured:
        // putting the ownership check first turned
        // GameRulesRngTests.A_meta_handler_that_hand_writes_a_stream_position_is_a_defect_too red,
        // which is exactly the "several rules can produce this, pin WHICH one fired" shape S2 is
        // about — the test was right and the ordering was wrong.
        FoldRngPositions(committedPositions, working.Run, rng, registration);
        RequireRunUntouched(untouchedRun, working.Run, registration);
        MarkApplied(working, context.NowUtc, registration.Kind);

        return CommandResult.Accept(working, Stamp(Combine(caughtUp, handled.Events)));
    }

    /// <summary>
    /// 🔒 The catch-up's events followed by the handler's — one list, in the order they happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Catch-up first, and it is not a preference.</b> `14` §2.4 replays the list as the
    /// animation script and `14` §7.1 appends it to the economy log; an accrual stamped
    /// <em>after</em> the spend it funded would tell both that the player paid with Energy they did
    /// not yet have. The catch-up ran before the handler was even built, so its rows precede.
    /// </para>
    /// <para>
    /// ⚠️ <b>Allocates nothing on the common path</b>, which matters because that path is every
    /// command sent inside one regeneration interval of the last: with no catch-up events this
    /// hands the handler's own list straight through, and <see cref="Stamp"/> is the thing that
    /// then copies it. The mirrored arm is the same trade for the other one-sided case — a catch-up
    /// event and an accepted command whose handler produced none — and 🔒 <b>M1-09's
    /// <c>BEGIN_SESSION</c> reaches it, as this remark predicted</b>: its second and every later call
    /// inside one game day is `30` §2.3's no-op, so a command that also crossed a regeneration
    /// interval hands the catch-up's accrual straight through. ⚠️ A <c>Deferred</c> row is
    /// <em>not</em> an instance of it: a deferral <b>rejects</b>, and <see cref="Execute"/> returns
    /// at the rejection arm without ever calling this.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DomainEvent> Combine(
        IReadOnlyList<DomainEvent> caughtUp, IReadOnlyList<DomainEvent> handled)
    {
        if (caughtUp.Count == 0)
        {
            return handled;
        }

        if (handled.Count == 0)
        {
            return caughtUp;
        }

        var combined = new DomainEvent[caughtUp.Count + handled.Count];

        for (var i = 0; i < caughtUp.Count; i++)
        {
            combined[i] = caughtUp[i];
        }

        for (var i = 0; i < handled.Count; i++)
        {
            combined[caughtUp.Count + i] = handled[i];
        }

        return combined;
    }

    /// <summary>
    /// 🔒 `30` §2.1's <b>P4</b>, made structural: a deep copy of the slice, through each aggregate's
    /// own <c>ToSnapshot()</c>/<c>Rehydrate()</c> pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a copy is not optional.</b> `30` §11.2 makes the aggregates classes with <c>internal</c>
    /// mutators — a handler changes state by calling them — so "no in-place mutation of the input"
    /// can only be true if the handler is given something other than the input. Three things fall
    /// out of doing it here rather than per handler: a <b>rejection</b> is provably state-free
    /// (the copy is discarded and the caller's slice returned); the Application layer may keep the
    /// state it loaded across a refused command; and every accepted command proves, on the way in,
    /// that the aggregate round-trips through the shape it is persisted as.
    /// </para>
    /// <para>
    /// 🔒 <b>A failed rehydration here is a defect and is raised as one.</b> `30` §11.3 puts
    /// validation at the seam so <em>"a corrupt row fails loudly rather than silently three rules
    /// later"</em>; a slice already in memory that cannot round-trip is an aggregate that was
    /// mutated into a state its own invariants refuse, which is a rule that is wrong — not a player
    /// who asked for too much.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cost, stated.</b> This is two snapshot builds and two validated rehydrations per
    /// command, which is real work on M1-11's 180-simulated-day budget. It is paid deliberately: the
    /// alternative is either mutable aggregates handed straight to handlers (P4 gone) or a second,
    /// unvalidated <c>Clone()</c> on every aggregate (a second construction path beside the one
    /// `30` §11.3 sanctions). If the budget ever needs it, the place to look is the snapshot
    /// copying, not this seam.
    /// </para>
    /// </remarks>
    private static WorldSlice Clone(WorldSlice state, ContentSnapshot content)
    {
        var player = Player.Rehydrate(state.Player.ToSnapshot(), content);

        if (player.IsFailure)
        {
            throw new InvalidOperationException(RoundTripFailure("Player", player.Error));
        }

        if (state.Run is null)
        {
            return new WorldSlice(player.Value, null);
        }

        var run = Run.Rehydrate(state.Run.ToSnapshot());

        return run.IsSuccess
            ? new WorldSlice(player.Value, run.Value)
            : throw new InvalidOperationException(RoundTripFailure("Run", run.Error));
    }

    /// <summary>
    /// 🔒 `30` §2.3's lazy catch-up seam — <em>"the first step of every command handler is
    /// <c>AdvanceTime(state, context.NowUtc)</c>"</em>.
    /// </summary>
    /// <returns>
    /// The <c>CurrencyChanged</c> rows the catch-up produced, unstamped, in the order they happened
    /// — empty, and allocation-free, when it produced none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It is first by construction.</b> A handler is only ever handed the slice this has
    /// already been called on, so "the first step of every command handler" is a property of the
    /// call graph rather than a convention every future handler has to remember.
    /// </para>
    /// <para>
    /// 🔒 <b>It returns its events, and that is carried-forward item 11.</b> M1-06 left this
    /// <c>void</c> while <c>Player.AccrueEnergy</c> <em>returns</em> a <c>CurrencyChanged</c>: an
    /// accrual here satisfied `30` §9's IL rule — the aggregate's own mutator constructs the event —
    /// while `14` §7.1's economy log and `21` §8.3's <c>income_attribution.csv</c> (risk <b>R10</b>)
    /// never saw the row, with every suite green. <see cref="Execute"/> now <b>prepends</b> these to
    /// the handler's before <see cref="Stamp"/> numbers the combined list 1..n; see
    /// <see cref="Combine"/> for why the order is not a preference.
    /// </para>
    /// <para>
    /// 🔒 <b>A rejection discards the whole catch-up, and that is safe.</b> The ruling and its
    /// reasoning are at the rejection arm in <see cref="Execute"/>: nothing here is consumptive, so
    /// the next accepted command re-derives every one of these boundaries from <c>NowUtc</c> and the
    /// stored anchors.
    /// </para>
    /// <para>
    /// 🔒 It takes the whole <see cref="WorldSlice"/> and the whole <see cref="GameContext"/> rather
    /// than <c>(player, nowUtc)</c>: §2.3's boundaries are read off both aggregates and several of
    /// them read tunables out of <c>GameContext.Content</c>. The <c>Run</c> half is deliberately
    /// unused — see the run row below — and the signature stays wide because M3's boundaries land
    /// on it and a narrower one would have to widen on the commit that needs it.
    /// </para>
    /// <para>
    /// 🔒 <b>The clamp is here and the throw is in <c>EnergyMath</c>, deliberately.</b>
    /// <c>EnergyMath.Accrue</c> <em>refuses</em> a negative span — <em>"clamping it there instead
    /// would silently make a persistence defect, an anchor stored in the future which never
    /// self-corrects, indistinguishable from skew"</em> — and `30` §2.1's <b>P3</b> forbids an
    /// exception out of <see cref="Apply"/>. A host clock microseconds behind the persisted anchor
    /// would otherwise throw on every command until it caught up. So the elapsed span is floored at
    /// zero <em>here</em>: a backwards clock costs the player nothing and grants them nothing.
    /// </para>
    /// <para>
    /// 🔒 <b>Why the reset guards are <c>&gt;=</c> and not <c>&gt;</c>.</b> The two cases the guard
    /// spans are not symmetric. The <b>equal</b> case — the boundary already in force, which is what
    /// almost every command sees — is passed <em>through</em> to
    /// <c>Player.ResetDailyCounters</c>' own no-op, M1-04's counter-wipe fix, so that defence stays
    /// reachable from production instead of being shadowed by a condition here; clearing on equality
    /// would wipe the day's ad caps and dungeon entries several times an hour. The <b>backwards</b>
    /// case is the one this guard actually covers, and it is a real finding rather than defence in
    /// depth: <c>Player.RequireNotBefore</c> <em>throws</em> on a boundary earlier than the one
    /// stored, and under host clock skew that throw comes out of <see cref="Apply"/> — a P3
    /// violation. Both halves are pinned.
    /// </para>
    /// <para>
    /// ⚠️ <b>The zero-delta row is published, not filtered</b> (recorded assumption <b>A6</b>). When
    /// the accrual ran but both banks were already full, the anchor still moves — time passed, and
    /// <c>EnergyAccrual</c>'s remarks are explicit that a full tank is not a reason to stop the
    /// clock — and the <c>CurrencyChanged</c> still goes out with <c>Delta</c> zero.
    /// <c>CurrencyChanged</c>'s own remarks sanction that and name the milestone that may rule it
    /// out. <b>The cost, named:</b> an idle player at a full tank emits one zero-delta
    /// <c>energy_regen</c> row per command sent more than one interval apart. Filtering it here
    /// would reintroduce "constructed then discarded" — the exact shape this task exists to remove.
    /// The <em>other</em> arm is the one that keeps the volume sane: when the anchor did not move at
    /// all, the aggregate is never touched and no event is constructed, so a command inside one
    /// regeneration interval of the last produces nothing.
    /// </para>
    /// <para>
    /// 🔒 <b>What catch-up deliberately does NOT do</b>, so a later reader does not read the absence
    /// as an oversight:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Plus expiry — ruled off, not deferred.</b> `30` §3 and `12` §2.1 put entitlement
    ///   on the <b>session</b>: <c>Entitlements</c> is a read-only value the composition root
    ///   resolves against its own <c>NowUtc</c> before building the context, and the domain
    ///   <em>"stores what it was told and re-derives nothing"</em>. There is no aggregate state to
    ///   roll forward, so there is nothing here to do — and a comparison of
    ///   <c>Entitlements.ExpiresAtUtc</c> against <c>NowUtc</c> would additionally be an entitlement
    ///   branch inside the domain (`12` §3.2, <c>No_entitlement_branch_outside_a_composition_root</c>)
    ///   on the hot path of every command, for no state change. It carries no <c>GapRegister</c>
    ///   entry for the same reason <c>entitlement</c> carries none on `30` §4's Player-contents
    ///   row.</item>
    ///   <item><b>The run's `14` §16.3 TTL — never touched.</b> Not <c>Run.LastAppliedAtUtc</c>, not
    ///   an expiry. Catch-up runs on <c>CommandKind.Meta</c> commands too, and sliding the run's
    ///   48-hour TTL from outside the run is the exact defect M1-05 added the second timestamp to
    ///   prevent — a run kept alive because its owner opened the shop. <em>Expiring</em> a run needs
    ///   <c>RunPhase</c> to move it into, which is already a <c>GapRegister</c> entry owned by
    ///   M3-05 whose <c>Why</c> states this consequence; there is no second entry for it.</item>
    ///   <item><b>Quest expiry, daily-shop stock expiry and event windows.</b> `30` §2.3's three
    ///   remaining boundaries, each acting on state no milestone has authored. Each has its own
    ///   <c>GapRegister</c> entry (<c>QuestSlate</c>/M4-09, <c>DailyShopStock</c>/M4-09,
    ///   <c>EventWindow</c>/M13-01) so the deferral expires by itself. The daily-reset
    ///   <em>mechanism</em> those three will hang off is built and running below — ad caps, dungeon
    ///   entries and the wheel's free spin are daily counters and are covered by it today.</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>The order is `30` §2.3's own — energy, then the day, then the week — and nothing
    /// couples the three today.</b> The accrual reads the anchor and the banks; the resets read the
    /// two period boundaries. Stated rather than left implicit, because the first boundary that
    /// <em>does</em> couple to another (a quest slate drawn on the day's rollover, say) will need
    /// the order to be a decision rather than an accident.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cost, on M1-11's hot path.</b> One <c>EnergyTuning.Read(context.Content)</c> per
    /// command — read once here and passed to both <c>EnergyMath.Accrue</c> and
    /// <c>Player.AccrueEnergy</c> — plus two calendar computations that answer in one step each
    /// whatever the gap. There is deliberately no per-boundary loop: 180 days offline is one
    /// subtraction, not 180 iterations.
    /// </para>
    /// </remarks>
    /// <param name="state">The <b>working</b> slice — never the caller's (`30` §2.1's P4).</param>
    /// <param name="context">Everything ambient. <c>NowUtc</c> is the instant rolled forward to.</param>
    private static IReadOnlyList<DomainEvent> AdvanceTime(WorldSlice state, GameContext context)
    {
        var player = state.Player;
        var tuning = EnergyTuning.Read(context.Content);

        // ---------------------------------------------- 1 · 10 §3 Energy regeneration (A1)
        //
        // 🔒 THE CLAMP. See the remarks: EnergyMath.Accrue throws on a negative span on purpose, and
        // P3 forbids that exception reaching Apply's caller.
        var sinceAnchor = context.NowUtc - player.EnergyAnchorUtc;
        var elapsed = TimeSpan.FromTicks(Math.Max(0L, sinceAnchor.Ticks));

        // The math is EnergyMath's, never restated here: 30 §11.5 puts computation in Rules/, and
        // A1's "whole units, and the anchor moves by wholeUnits × the interval" is the rule a second
        // transcription would get wrong the first time someone simplified it.
        var accrual = EnergyMath.Accrue(tuning, player.LegendLevel, player.Energy, elapsed);

        // Not one whole unit's worth of time has passed: the aggregate is not touched and no event
        // is constructed. Player.AccrueEnergy takes BOTH halves of one accrual, which is what makes
        // "banks written, anchor forgotten" unrepresentable rather than merely discouraged.
        IReadOnlyList<DomainEvent> events = accrual.AnchorAdvance > TimeSpan.Zero
            ? new DomainEvent[]
            {
                player.AccrueEnergy(accrual.Banks, accrual.AnchorAdvance, tuning, EnergyRegenReason),
            }
            : NoEvents;

        // ---------------------------------------------- 2 · 30 §2.3's 05:00 UTC game day
        var dayStart = GameCalendar.GameDayStartAt(context.NowUtc);

        if (dayStart >= player.DailyPeriodStartUtc)
        {
            player.ResetDailyCounters(dayStart);
        }

        // ---------------------------------------------- 3 · A2's Monday 05:00 UTC game week
        var weekStart = GameCalendar.GameWeekStartAt(context.NowUtc);

        if (weekStart >= player.WeeklyPeriodStartUtc)
        {
            player.ResetWeeklyCounters(weekStart);
        }

        // 4 · Plus expiry — NOTHING, and that is a ruling. See the remarks.
        // 5 · the Run — NOTHING, deliberately. See the remarks.
        return events;
    }

    /// <summary>
    /// 🔒 A <c>CommandKind.Meta</c> command may <b>read</b> the run it was handed and may not
    /// <b>write</b> it — at all, not merely its `14` §8.1 counters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Why this is separate from <see cref="FoldRngPositions"/> rather than folded into it.</b>
    /// That check answers "did the handler hand-write a stream position", which is a determinism
    /// question and applies to <em>both</em> kinds. This one answers "did a command that is not part
    /// of this run change it", which is an <b>ownership</b> question and applies to meta commands
    /// only. Before M1-09 the hole was unreachable — the production table held no handler — and the
    /// two questions could look like one. They are not: a run command legitimately writes Gold, HP
    /// and position on every turn.
    /// </para>
    /// <para>
    /// 🔒 <b>The consequence it closes, stated so the cost is judged against something.</b>
    /// <see cref="MarkApplied"/> deliberately does <em>not</em> stamp the run on a meta command —
    /// that asymmetry is why M1-05 put a second <c>LastAppliedAtUtc</c> on <c>Run</c>, so a player
    /// cannot hold a run open by opening the shop. A meta handler that wrote the run would therefore
    /// produce a run whose state had changed and whose `14` §16.3 timestamp said nothing had
    /// happened, and the next reader would have no way to tell which command did it.
    /// </para>
    /// <para>
    /// ⚠️ A <b>defect</b> rather than a rejection, exactly as the hand-written-position case is: a
    /// <c>RejectionReason</c> would hand the player a polite "no" and leave the corrupted run in
    /// place.
    /// </para>
    /// </remarks>
    /// <param name="untouched">
    /// The run's snapshot as it stood before the handler, or <c>null</c> for a run command (which may
    /// write) or a slice with no run (which has nothing to write).
    /// </param>
    /// <param name="working">The run the handler was given, or <c>null</c> when the slice carries none.</param>
    /// <param name="registration">The dispatch row, for the message.</param>
    private static void RequireRunUntouched(
        RunSnapshot? untouched, Run? working, CommandRegistration registration)
    {
        if (untouched is null || working is null || untouched == working.ToSnapshot())
        {
            return;
        }

        throw new InvalidOperationException(
            "The handler for '" + registration.WireName + "' is a CommandKind.Meta command and it " +
            "WROTE THE RUN it was handed. 14 §2.3 splits the registry 19 run / 30 meta, and a meta " +
            "command acts OUTSIDE a run: it is dispatched with one in the slice because a player can " +
            "open the shop without leaving, and HandlerInput.Run hands it that run to READ. Writing " +
            "it is an ownership defect, and a silent one — Apply deliberately does not stamp " +
            "Run.LastAppliedAtUtc for a meta command (M1-05's second timestamp exists so a meta " +
            "command cannot keep a run alive), so the run would come back changed while its own " +
            "14 §16.3 timestamp said nothing had happened to it, and no later reader could tell which " +
            "command did it. If the command genuinely acts inside the run, its dispatch row is " +
            "classified CommandKind.Meta and should not be.");
    }

    /// <summary>
    /// 🔒 The RNG write-back (M1 kickoff decision 5): <b><c>Apply</c></b> folds the scope's final
    /// positions into the run, and refuses a run whose positions moved underneath the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the comparison is against.</b> <paramref name="committed"/> is the working run's own
    /// position map, read the instant before the handler ran, and
    /// <c>Run.CommitStreamPositions</c> is the only thing that can change it. So a difference here
    /// means one thing only: the handler called that seam itself. That is a determinism defect
    /// rather than a rejection — the scope's positions and the hand-written ones disagree about how
    /// many draws this command took, and whichever is stored, some later draw repeats a sequence the
    /// player has already played (`14` §8.1).
    /// </para>
    /// <para>
    /// 🔒 <b>The check runs for a <c>CommandKind.Meta</c> command too; only the fold is a run
    /// command's.</b> A meta command is dispatched perfectly happily with a run in the slice — a
    /// player can open the shop without leaving — and <c>HandlerInput.Run</c> hands it that run. It
    /// has no scope to fold (`30` §3 puts out-of-run draws on <c>GameContext.CommandSeed</c> with no
    /// persisted counter), but it can still reach <c>CommitStreamPositions</c>, and a check that
    /// returned early on a null scope would have left exactly that route to a silently
    /// unreproducible run open.
    /// </para>
    /// <para>
    /// ⚠️ M1-05's seam already refuses the <em>partial</em> version of the same mistake: it takes the
    /// whole map and rejects a dropped key, so a handler that wanted to hand-write one position would
    /// have to reconstruct the entire committed set to get that far. This check is what catches the
    /// handler that did.
    /// </para>
    /// <para>
    /// The fold itself is unconditional on an accepted run command, which is what makes "a handler
    /// that drew and forgot to write the counter back" <b>unexpressible</b> rather than merely
    /// caught: there is nothing for a handler to forget.
    /// </para>
    /// </remarks>
    /// <param name="committed">
    /// The working run's stream positions as they stood before the handler ran, or <c>null</c> when
    /// the slice carries no run.
    /// </param>
    /// <param name="working">The run the handler was given, or <c>null</c> when the slice carries none.</param>
    /// <param name="rng">The scope this command drew through, or <c>null</c> for a meta command.</param>
    /// <param name="registration">The dispatch row, for the message.</param>
    private static void FoldRngPositions(
        IReadOnlyDictionary<string, ulong>? committed,
        Run? working,
        RunRngScope? rng,
        CommandRegistration registration)
    {
        if (working is null || committed is null)
        {
            return;
        }

        if (!SamePositions(committed, working.RngStreamPositions))
        {
            throw new InvalidOperationException(
                "The handler for '" + registration.WireName + "' wrote the run's 14 §8.1 stream " +
                "positions itself. Apply owns that write (M1 kickoff decision 5): it opens a " +
                "RunRngScope over the run's committed seed and counters, hands it to the handler, and " +
                "folds the scope's final positions back — so the persisted counter always equals the " +
                "number of draws actually taken. A hand-written position and the scope's disagree " +
                "about that count, and whichever is stored, some later draw repeats a sequence the " +
                "player has already played. That is a DETERMINISM DEFECT, not a rejection: a " +
                "RejectionReason would hand the corrupt scope back to the player as a polite 'no' " +
                "and leave the run in it. Draw through HandlerInput.Rng and write nothing. If this " +
                "is a CommandKind.Meta command it has no scope at all — 30 §3 draws out of a run " +
                "from GameContext.CommandSeed, with no persisted counter — so it may READ the run it " +
                "was handed mid-run and must never move its counters.");
        }

        // Only a run command has a scope to fold back. A meta command reached this far to be
        // CHECKED, not to commit anything.
        if (rng is not null)
        {
            working.CommitStreamPositions(rng.FinalPositions());
        }
    }

    /// <summary>
    /// `14` §16.3 — records that a command was accepted, which is what the TTLs slide from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The player always; the run only for a run command.</b> That asymmetry is the whole
    /// reason M1-05 put a second <c>LastAppliedAtUtc</c> on <c>Run</c>:
    /// <c>Player.LastAppliedAtUtc</c> advances on meta commands too, so sliding the run's 48-hour
    /// TTL off it would keep a run alive because its owner opened the shop.
    /// </para>
    /// <para>
    /// 🔒 <b>On acceptance, and only on acceptance.</b> A refused command changed nothing, so it
    /// must not extend a TTL either — otherwise a client could hold a run open indefinitely by
    /// sending commands it knows will be refused.
    /// </para>
    /// <para>
    /// ⚠️ It is written here rather than left to M1-08 because M1-08's catch-up rolls forward
    /// <em>from</em> these anchors: an <c>Apply</c> that never advanced them would have every command
    /// re-accrue from the same instant forever.
    /// </para>
    /// <para>
    /// 🔒 <b>THE SECOND CLAMP, and it settles carried-forward item 20.</b> Both aggregates
    /// <em>throw</em> on an instant before the one they hold, and until M1-12 this method handed them
    /// <c>context.NowUtc</c> raw — so a host clock behind the persisted anchor came out of
    /// <see cref="Apply"/> as an <c>ArgumentOutOfRangeException</c>, which `30` §2.1's <b>P3</b>
    /// forbids. That was a contradiction inside one ruling rather than an open question: <b>the same
    /// <c>Apply</c></b> already floors the energy span at zero and already writes both reset guards
    /// as <c>&gt;=</c>, each citing P3 in as many words, and <c>Player.MarkApplied</c>'s own remarks
    /// already asserted that <c>AdvanceTime</c> <em>"is specified to clamp that rather than pass it
    /// on"</em> — a protection nothing implemented.
    /// </para>
    /// <para>
    /// 🔒 <b>The invariant stays in the aggregate; the flooring happens here.</b> Exactly the shape
    /// M1-08 chose for energy, and for the reason recorded there: clamping inside the model would
    /// make a persistence defect — an anchor stored in the future, which never self-corrects —
    /// indistinguishable from skew. So the aggregates keep refusing a backwards instant, and
    /// <c>Apply</c> stops producing one.
    /// </para>
    /// <para>
    /// ⚠️ <b>Floored, not skipped, and the difference is `14` §16.3's TTL.</b> Passing the stored
    /// value writes the field to what it already held; skipping the call would do the same today and
    /// would silently stop doing it the moment either aggregate does anything else in
    /// <c>MarkApplied</c>. A backwards clock therefore costs the player nothing and grants them
    /// nothing — it cannot hold a run open and cannot expire one early — which is the same sentence
    /// the energy clamp is written under.
    /// </para>
    /// </remarks>
    private static void MarkApplied(WorldSlice state, DateTimeOffset nowUtc, CommandKind kind)
    {
        state.Player.MarkApplied(NotBefore(nowUtc, state.Player.LastAppliedAtUtc));

        if (kind == CommandKind.Run)
        {
            state.Run!.MarkApplied(NotBefore(nowUtc, state.Run.LastAppliedAtUtc));
        }
    }

    /// <summary>
    /// 🔒 `30` §2.1's <b>P3</b> clamp for a host clock behind a persisted anchor: the later of the
    /// two, so an accepted command never asks an aggregate to move its timestamp backwards.
    /// </summary>
    /// <remarks>
    /// Written once and applied to both aggregates rather than inlined twice: the run's anchor and
    /// the player's are the same ruling, and two spellings of it would eventually disagree about
    /// which one skew is allowed to move (steering <b>S4</b>).
    /// </remarks>
    private static DateTimeOffset NotBefore(DateTimeOffset nowUtc, DateTimeOffset stored) =>
        nowUtc < stored ? stored : nowUtc;

    /// <summary>
    /// 🔒 Stamps each event with its ordinal within <b>this</b> <c>CommandResult</c>'s list — the
    /// ruling M1-03 recorded when it authored <c>DomainEvent(int Sequence)</c> and left the assigner
    /// to M1-06.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It orders one command's output and nothing wider.</b> `14` §2.4 replays the list as the
    /// animation script and `14` §7.1 appends it to the economy log; both need the order the rules
    /// produced them in. ⚠️ It is <b>not</b> `14` §16.3's wire <c>sequence</c>, which is the
    /// per-run/per-player <b>command</b> counter on the request envelope and lives in
    /// <c>SlayIdleRepeat.Contracts</c>. Two different numbers, one word.
    /// </para>
    /// <para>
    /// 🔒 <b>From 1, not from 0</b>, and that is what makes <c>DomainEvent.UnstampedSequence</c>
    /// (which is 0) mean something: M1-03's remarks require <c>Apply</c> to be able to tell an
    /// unstamped event from a first one, and a 0-based stamp would make the two identical.
    /// </para>
    /// <para>
    /// 🔒 <b>An event that arrives already stamped is refused.</b> The ordinal is <c>Apply</c>'s to
    /// assign — <em>"never by a constructor, and never by a caller"</em> — and a handler that
    /// assigned its own has decided where in a list it does not yet know the shape of its event
    /// belongs. Overwriting it silently would let that pass unnoticed until the economy log and the
    /// animation script disagreed about the order of one command's effects.
    /// </para>
    /// <para>
    /// The rewrite is <c>e with { Sequence = n }</c>, which reaches every subtype through the
    /// abstract record's virtual <c>&lt;Clone&gt;$</c> — M1-03 verified that and deliberately left
    /// <c>Sequence</c> as <c>init</c> for this.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DomainEvent> Stamp(IReadOnlyList<DomainEvent> events)
    {
        if (events.Count == 0)
        {
            // 🔒 The SHARED empty list, not the handler's own. A handler that returned a
            // List<DomainEvent> it still holds could otherwise append to CommandResult.Events after
            // Apply had returned — the same hole The_event_list_cannot_be_written_through closes on
            // the non-empty path, where the stamped array is wrapped.
            return NoEvents;
        }

        var stamped = new DomainEvent[events.Count];

        for (var i = 0; i < events.Count; i++)
        {
            var produced = events[i];

            if (produced is null)
            {
                throw new InvalidOperationException(
                    "There is a null event at position " + Text(i) + " of this command's event " +
                    "list. ⚠️ That ordinal counts the CATCH-UP's rows first (see Combine), so on a " +
                    "command that also accrued it is NOT the index into what the handler returned. " +
                    "The event list is 14 §2.4's animation script and 14 §7.1's economy log; a hole " +
                    "in it is a row neither can read.");
            }

            if (produced.Sequence != DomainEvent.UnstampedSequence)
            {
                throw new InvalidOperationException(
                    "The event at position " + Text(i) + " of this command's list is a " +
                    produced.GetType().Name + " already stamped with " +
                    "Sequence " + Text(produced.Sequence) + ". (That ordinal counts the catch-up's " +
                    "rows first — see Combine — and the catch-up builds every row it produces with " +
                    "DomainEvent.UnstampedSequence, so the producer here is the handler.) " +
                    "The ordinal is the event's position within " +
                    "ONE Apply call's list and is assigned HERE — never by a constructor and never " +
                    "by a caller (30 §7). A handler that stamps its own has decided a position in a " +
                    "list whose shape it does not know, and the economy log (14 §7.1) and the " +
                    "animation script (14 §2.4) would stop agreeing about the order of one command's " +
                    "effects. Build events with DomainEvent.UnstampedSequence.");
            }

            stamped[i] = produced with { Sequence = i + 1 };
        }

        return Array.AsReadOnly(stamped);
    }

    /// <summary>Whether two stream-position maps carry exactly the same rows.</summary>
    private static bool SamePositions(
        IReadOnlyDictionary<string, ulong> left, IReadOnlyDictionary<string, ulong> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (streamName, position) in left)
        {
            if (!right.TryGetValue(streamName, out var other) || other != position)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 🔒 Renders a number with <see cref="CultureInfo.InvariantCulture"/>, for the reason
    /// <c>Player</c> and <c>Run</c> each have one: a bare interpolation renders <c>1.234</c> on a
    /// German laptop and <c>1,234</c> in the container — two diagnostics for one defect, and a
    /// message a reader cannot grep. `14` §8.2 wants <c>Core</c> reading identically everywhere, and
    /// <c>AmbientApiTests.Core_and_Application_contain_no_culture_sensitive_formatting</c> fails the
    /// build without it.
    /// </summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string RoundTripFailure(string aggregate, string error) =>
        "The " + aggregate + " in this WorldSlice does not round-trip through its own snapshot: " +
        error + " 30 §2.1's P4 makes Apply copy the slice before a handler touches it — through " +
        "ToSnapshot()/Rehydrate(), which is the one validated construction path 30 §11.3 sanctions " +
        "— so an aggregate that cannot be rebuilt from its own persisted shape is a rule that " +
        "mutated it into a state its invariants refuse, or a caller that built it by hand. Either " +
        "way it is a defect and not a player who asked for too much: the same state would fail on " +
        "the way into Postgres, one command later, with nothing left to say which rule wrote it.";
}
