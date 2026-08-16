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
using SlayIdleRepeat.Core.Rules.Feats;
using SlayIdleRepeat.Core.Rules.Hero;

namespace SlayIdleRepeat.Core;

/// <summary>The transition function: the whole domain reduces to <see cref="Apply"/>.</summary>
/// <remarks>
/// <para>
/// Pure (time and randomness arrive as data, never read ambiently), synchronous, total (every
/// command on every state returns a result — an unregistered command is <c>ILLEGAL_STATE</c>, not
/// an exception), and immutable (the slice is cloned before a handler runs; see
/// <see cref="Clone"/>). An exception out of <see cref="Apply"/> always means the caller or the
/// domain is wrong, never that the player asked for something they cannot have.
/// </para>
/// <para>
/// A façade, not a god function: <see cref="Apply"/> owns the five things true of every command —
/// clone, catch up, dispatch, fold the RNG counters back, stamp the events — and no game rule
/// itself. Everything a specific command means lives in its handler.
/// </para>
/// </remarks>
public static class GameRules
{
    /// <summary>The dispatch table: binds each command to its handler, wire name and <see cref="CommandKind"/>.</summary>
    /// <remarks>
    /// <c>START_RUN</c> is the one row worth pausing on: it is <c>CommandKind.Run</c> even though it
    /// is submitted before a run exists, because it is the one command allowed to create the run its
    /// own kind would otherwise require — see <see cref="CommandRegistration.OpensRun"/>.
    /// </remarks>
    private static readonly CommandDispatch Dispatch = new CommandDispatch()

        // ------------------------------------------------ the 19 RUN commands
        .Handled<StartRunCommand>("START_RUN", CommandKind.Run, StartRun.Handle, opensRun: true)
        .Handled<RollDiceCommand>("ROLL_DICE", CommandKind.Run, RollDice.Handle)
        .Handled<UseRerollCommand>("USE_REROLL", CommandKind.Run, UseReroll.Handle)
        .Handled<ChooseForkCommand>("CHOOSE_FORK", CommandKind.Run, ChooseFork.Handle)
        .Handled<ResolveTileCommand>("RESOLVE_TILE", CommandKind.Run, ResolveTile.Handle)
        .Handled<PickPerkCommand>("PICK_PERK", CommandKind.Run, PickPerk.Handle)
        .Handled<RerollDraftCommand>("REROLL_DRAFT", CommandKind.Run, RerollDraft.Handle)
        .Handled<SkipDraftCommand>("SKIP_DRAFT", CommandKind.Run, SkipDraft.Handle)
        .Handled<ShopBuyCommand>("SHOP_BUY", CommandKind.Run, ShopBuy.Handle)
        .Handled<ShopRefreshCommand>("SHOP_REFRESH", CommandKind.Run, ShopRefresh.Handle)
        .Handled<EventChooseCommand>("EVENT_CHOOSE", CommandKind.Run, EventChoose.Handle)
        .Handled<MinigameSubmitCommand>("MINIGAME_SUBMIT", CommandKind.Run, MinigameSubmit.Handle)
        .Handled<CampfireChooseCommand>("CAMPFIRE_CHOOSE", CommandKind.Run, CampfireChoose.Handle)
        .Handled<StartBattleCommand>("START_BATTLE", CommandKind.Run, StartBattle.Handle)
        .Handled<ConfirmBattleResultCommand>("CONFIRM_BATTLE_RESULT", CommandKind.Run, ConfirmBattleResult.Handle)
        .Handled<ReviveCommand>("REVIVE", CommandKind.Run, Revive.Handle)
        .Deferred<UseConsumableCommand>("USE_CONSUMABLE", CommandKind.Run, "M3-08")
        .Handled<EndRunCommand>("END_RUN", CommandKind.Run, EndRun.Handle)
        .Handled<AbandonRunCommand>("ABANDON_RUN", CommandKind.Run, AbandonRun.Handle)

        // ----------------------------------------------- the 30 META commands
        .Handled<BeginSessionCommand>("BEGIN_SESSION", CommandKind.Meta, BeginSession.Handle)
        .Deferred<SkipFtueCommand>("SKIP_FTUE", CommandKind.Meta, "M4-12")
        .Deferred<EquipCommand>("EQUIP", CommandKind.Meta, "M4-03")
        .Handled<MergeCommand>("MERGE", CommandKind.Meta, Merge.Handle)
        .Handled<EnhanceCommand>("ENHANCE", CommandKind.Meta, Enhance.Handle)
        .Handled<SalvageCommand>("SALVAGE", CommandKind.Meta, Salvage.Handle)
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
        .Handled<SavePresetCommand>("SAVE_PRESET", CommandKind.Meta, SavePreset.Handle)
        .Handled<ApplyPresetCommand>("APPLY_PRESET", CommandKind.Meta, ApplyPreset.Handle)
        .Deferred<ShopPurchaseCommand>("SHOP_PURCHASE", CommandKind.Meta, "M4-09")
        .Deferred<OpenChestCommand>("OPEN_CHEST", CommandKind.Meta, "M4-02")
        .Deferred<OpenEggCommand>("OPEN_EGG", CommandKind.Meta, "M4-02")
        .Deferred<OpenCrateCommand>("OPEN_CRATE", CommandKind.Meta, "M4-02")
        .Deferred<UploadGhostCommand>("UPLOAD_GHOST", CommandKind.Meta, "M12-01")
        .Deferred<StartDuelCommand>("START_DUEL", CommandKind.Meta, "M12-04")
        .Deferred<SubmitDuelCommand>("SUBMIT_DUEL", CommandKind.Meta, "M12-04");

    /// <summary>
    /// The shared empty event list. Also prevents <see cref="Stamp"/> from handing back a handler's
    /// own mutable list, which the handler could otherwise keep appending to after <c>Apply</c> returns.
    /// </summary>
    private static readonly ReadOnlyCollection<DomainEvent> NoEvents =
        Array.AsReadOnly(Array.Empty<DomainEvent>());

    /// <summary>The attribution token every regeneration accrual is logged under.</summary>
    private const string EnergyRegenReason = "energy_regen";

    /// <summary>Every registered command type, by wire name — the single declared source of that mapping.</summary>
    internal static IReadOnlyDictionary<string, Type> CommandTypesByWireName => Dispatch.TypesByWireName;

    /// <summary>The dispatch row for a command type, or <c>null</c> when no row names it.</summary>
    /// <remarks>Exposed for the domain suite, which drives the table's decisions directly.</remarks>
    internal static CommandRegistration? RegistrationFor(Type commandType) => Dispatch.For(commandType);

    /// <summary>The one public way to change state in this game.</summary>
    /// <param name="state">The aggregates this command may read or write.</param>
    /// <param name="command">What the player intends.</param>
    /// <param name="context">Everything ambient, passed as data.</param>
    /// <returns>
    /// Whether the command was accepted, the domain-tier reason if it was not, the complete
    /// resulting state, and the events it produced.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A defect, never a refusal: the slice doesn't carry the run its command acts on, an aggregate
    /// doesn't round-trip through its own snapshot, a handler hand-wrote an RNG stream position or
    /// its own event sequence, a meta command's handler tried to draw randomness, or a handler
    /// produced an event carrying a value no lifetime counter can be named for. Every case is a
    /// miswired caller or a broken rule, never a player asking for something they cannot have.
    /// </exception>
    public static CommandResult Apply(WorldSlice state, GameCommand command, GameContext context) =>
        Execute(Dispatch, state, command, context);

    /// <summary><see cref="Apply"/>'s body, over an explicit dispatch table so the domain test suite can drive it against shapes never committed to production.</summary>
    /// <remarks>
    /// Internal, and not a second entry point: the architecture rule that <c>Apply</c> is the only
    /// public mutation still holds, since <c>InternalsVisibleTo</c> reaches exactly the test assembly.
    /// </remarks>
    internal static CommandResult Execute(
        CommandDispatch dispatch, WorldSlice state, GameCommand command, GameContext context)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var registration = dispatch.For(command.GetType());

        // An unregistered command type is refused, not thrown — the domain must be total. This arm
        // is unreachable for any real command since the build fails if one is declared without a row.
        if (registration is null)
        {
            return CommandResult.Reject(RejectionReason.ILLEGAL_STATE, state);
        }

        // A run command whose slice carries no run is a caller/loading defect, not a rejection —
        // loading the right slice is the Application layer's job. START_RUN is the one exception:
        // it's the only command allowed to create the run its own kind would otherwise require,
        // exempted via registration.OpensRun rather than a special case here.
        if (registration.Kind == CommandKind.Run && state.Run is null && !registration.OpensRun)
        {
            throw new InvalidOperationException(
                "'" + registration.WireName + "' is a CommandKind.Run command and this WorldSlice " +
                "carries no Run. 30 §4.1 makes loading the right slice the Application layer's job; " +
                "14 §16.2's RUN_NOT_FOUND is a TRANSPORT-tier value, refused before the domain is " +
                "invoked, so Apply may not return it (30 §2). This is a miswired caller, not a " +
                "player asking for something they cannot have.");
        }

        if (registration.Kind == CommandKind.Run && state.Run is not null)
        {
            if (state.Run.Phase == RunPhase.Ended)
            {
                // Exempted by the same flag as the run-less guard above: nothing else in the game
                // clears a finished run, so without this a player gets one run for the life of their
                // account. Only a finished one — a live run falls to the arms below instead, and to
                // START_RUN's own already-active-run refusal, so it is never discarded.
                if (!registration.OpensRun)
                {
                    return CommandResult.Reject(RejectionReason.RUN_ALREADY_ENDED, state);
                }
            }
            else if (state.Run.Phase == RunPhase.BattlePending &&
                     command is not ConfirmBattleResultCommand)
            {
                // A battle is open; CONFIRM_BATTLE_RESULT is the only legal next move.
                return CommandResult.Reject(RejectionReason.ILLEGAL_STATE, state);
            }

            // DraftPending is orthogonal to RunPhase, so it's checked separately rather than added
            // as a fourth phase value. PICK_PERK/REROLL_DRAFT/SKIP_DRAFT are the only legal moves
            // while a draft is open. Not asked of an ended run: a draft left open on a run that has
            // finished would otherwise refuse the exempted row for a second, unrelated reason.
            else if (state.Run.DraftPending &&
                     command is not (PickPerkCommand or RerollDraftCommand or SkipDraftCommand))
            {
                return CommandResult.Reject(RejectionReason.ILLEGAL_STATE, state);
            }
        }

        // Everything from here works on a copy; the caller's slice is never written to.
        var working = Clone(state, context.Content);

        // 🔒 Before the RunRngScope below, not after: the scope and committedPositions would
        // otherwise be the FINISHED run's, and FoldRngPositions would compare them against the fresh
        // run's empty map and raise a determinism defect. The only place Apply clears Run, and only
        // for a run row whose job is to open one.
        if (registration.OpensRun && working.Run is { Phase: RunPhase.Ended })
        {
            working = working with { Run = null };
        }

        // The handler never receives the raw slice, only the one this produces, so nothing a
        // handler writes runs before the catch-up. Its events are prepended to the handler's since
        // they happened first.
        var caughtUp = AdvanceTime(working, context);

        // Gated on working.Run being non-null rather than merely Kind == Run: START_RUN is the one
        // row that can be Kind == Run with no run yet (its job is to create one), and it draws
        // nothing before the run exists — HandlerInput.OpenRun is the seam it uses instead.
        var rng = registration.Kind == CommandKind.Run && working.Run is not null
            ? new RunRngScope(working.Run.RunSeed, working.Run.RngStreamPositions)
            : null;

        // The baseline FoldRngPositions compares against, read after the clone and catch-up but
        // before the handler — so "these differ" means only one thing: the handler wrote it itself.
        var committedPositions = working.Run?.RngStreamPositions;

        // Snapshotted (not held by reference — Run is a mutable class) so a meta command's handler
        // can be caught writing the run it was only handed to read: a meta command is dispatched
        // with a run present in the slice (a player can visit the shop mid-run), and only the RNG
        // counters are otherwise guarded, so Gold/HP/Position would be silently writable.
        // Compared as canonical bytes rather than the record itself, since RunSnapshot's dictionary
        // members compare by reference under synthesized record equality.
        var untouchedRun = registration.Kind == CommandKind.Meta && working.Run is not null
            ? CanonicalStateWriter.CanonicalBytes(working.Run.ToSnapshot())
            : null;

        // Built once and named: Execute reads input.OpenedRun back after the handler returns, which
        // needs the very same HandlerInput instance the handler was given.
        var input = new HandlerInput(working, context, rng);

        var handled = registration.IsHandled
            ? registration.Handler!(command, input)
            : HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);

        if (!handled.Accepted)
        {
            // The working copy — and the catch-up's events with it — is discarded, so a refused
            // command provably changed nothing. Nothing AdvanceTime does is consumptive: every
            // boundary it moves is re-derived from NowUtc, so the next accepted command accrues the
            // same ground from the same anchors.
            return CommandResult.Reject(handled.Rejection!.Value, state);
        }

        // Ownership check first, hand-written-position check second: a meta handler that
        // hand-writes a stream position trips both, and the ownership message is the more useful
        // diagnosis of the two.
        // The only place HandlerInput.OpenedRun is read: a no-op for every row except START_RUN,
        // which attaches the run it built through HandlerInput.OpenRun.
        if (input.OpenedRun is not null)
        {
            working = working with { Run = input.OpenedRun };
        }

        FoldRngPositions(committedPositions, working.Run, rng, registration);
        RequireRunUntouched(untouchedRun, working.Run, registration);
        MarkApplied(working, context.NowUtc, registration.Kind);

        // 🔴 The exit-side half of the equipped-item invariant. Player.Rehydrate refuses a row whose
        // loadout names an item the stock does not hold — on the way IN, which on its own would let
        // the command that broke the pairing be accepted and persisted, and brick the account from
        // the next command onwards. Checked here, the offending command fails instead.
        working.Player.RequireLoadoutResolves();

        // After the handler, because the handler is what banks the XP; before the events are
        // stamped, because a level-up's Energy refill is a CurrencyChanged like any other.
        var levelled = LevelUp(working.Player, context.Content);

        var events = Stamp(Combine(caughtUp, Combine(handled.Events, levelled)));

        CountFeats(working.Player, events);

        return CommandResult.Accept(working, events);
    }

    /// <summary>
    /// Reconciles the player's Legend Level against the lifetime XP the command left them with, and
    /// applies what the level-ups grant.
    /// </summary>
    /// <returns>The <c>CurrencyChanged</c> the Energy refill produced, or nothing when no level was gained.</returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Here rather than in the handlers that bank XP, and that is the whole point.</b> A level is
    /// a function of a number the player already carries, so reconciling it once per accepted command
    /// means every path that banks Legend XP levels the player up — the two that exist today
    /// (<c>END_RUN</c>, <c>ABANDON_RUN</c>), the ad-doubled payout, and every one a later milestone
    /// adds without knowing this rule exists. A grant that levelled the player in its own handler
    /// would be one more place to forget.
    /// </para>
    /// <para>
    /// It runs on accepted commands only, on the same argument <see cref="CountFeats"/> makes: a
    /// refused command discards the working copy, so a rejection cannot leave a Talent Point behind.
    /// Being idempotent, it is also safe on a replay — the second reconciliation of the same lifetime
    /// total derives the same level and grants nothing.
    /// </para>
    /// <para>
    /// `10` §3.1 refills Energy to full on a level-up, and that refill is the one thing here that is
    /// not pure player state, so it comes back as an event rather than being applied silently.
    /// </para>
    /// <para>
    /// ⚠️ <b>The cost, stated because this runs on every accepted command.</b> The level is derived
    /// by walking the ladder, so a player at Legend Level <i>N</i> pays <i>N</i> iterations of the
    /// authored curve — at most 198, and none at all at the cap, which
    /// <c>LegendProgression.Reconcile</c> short-circuits. Each tuning is read exactly once here and
    /// handed to both the rule and the aggregate, rather than re-read per use.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DomainEvent> LevelUp(Player player, ContentSnapshot content)
    {
        var range = LegendTuning.Read(content);
        var energy = EnergyTuning.Read(content);

        var levelUp = LegendProgression.Reconcile(
            player.LegendLevel, player.LegendXp, player.Energy, content, range, energy);

        if (!levelUp.Occurred)
        {
            return NoEvents;
        }

        // The level is written FIRST: SetEnergy derives the ceiling it checks against from the
        // player's Legend Level, and refilling before the level moved would refuse the very tank
        // the level-up just enlarged.
        player.AdvanceLegendLevel(levelUp.ToLevel, levelUp.TalentPointsGranted, range);

        return new DomainEvent[]
        {
            player.SetEnergy(levelUp.Banks, energy, LegendLevelUpReason),
        };
    }

    /// <summary>The attribution token a Legend Level-up's Energy refill is logged under.</summary>
    private const string LegendLevelUpReason = "legend_level_up";

    /// <summary>Advances the player's lifetime feat counters for everything this command's events imply.</summary>
    /// <remarks>
    /// Runs only on an accepted command, and over the <b>stamped</b> list — the same one the caller
    /// receives — so what the client replays and what the counters say can never disagree. Indexed
    /// rather than enumerated: this is on every command's path and the interface would box the
    /// list's enumerator.
    /// </remarks>
    private static void CountFeats(Player player, IReadOnlyList<DomainEvent> events)
    {
        var advances = FeatCounterProjection.Project(events);

        for (var i = 0; i < advances.Count; i++)
        {
            player.CountFeat(advances[i].CounterId, advances[i].Amount);
        }
    }

    /// <summary>The catch-up's events followed by the handler's — one list, in the order they happened.</summary>
    /// <remarks>
    /// Catch-up first is not a preference: an accrual stamped after the spend it funded would make
    /// both the animation script and the economy log read as if the player paid with Energy they
    /// didn't have yet. Allocates nothing when either side is empty — the common case.
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

    /// <summary>A deep copy of the slice, through each aggregate's own <c>ToSnapshot()</c>/<c>Rehydrate()</c> pair.</summary>
    /// <remarks>
    /// The aggregates are classes with internal mutators, so "no in-place mutation of the caller's
    /// input" can only hold if the handler is given something else entirely — hence the copy here,
    /// rather than per handler. As a side effect, every accepted command proves on the way in that
    /// the aggregate round-trips through its persisted shape; a failed rehydration is raised as a
    /// defect rather than a rejection, since it means a rule already mutated the aggregate into a
    /// state its own invariants refuse.
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

    /// <summary>The lazy catch-up: rolls energy regen and daily/weekly resets forward to <c>context.NowUtc</c>.</summary>
    /// <returns>
    /// The <c>CurrencyChanged</c> rows the catch-up produced, unstamped, in the order they happened
    /// — empty, and allocation-free, when it produced none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The elapsed span is floored at zero here rather than in <c>EnergyMath.Accrue</c> (which
    /// throws on a negative span by design): a host clock microseconds behind the persisted anchor
    /// must not throw out of <see cref="Apply"/>, so a backwards clock costs the player nothing and
    /// grants them nothing.
    /// </para>
    /// <para>
    /// The daily/weekly reset guards use <c>&gt;=</c>, not <c>&gt;</c>: the equal case (the boundary
    /// already in force, the common case) is passed through to the aggregate's own no-op rather than
    /// filtered here, so that defence stays reachable; the backwards case is the one this guard
    /// actually exists for, since the aggregate throws on an earlier boundary than the one stored.
    /// </para>
    /// <para>
    /// A zero-delta <c>CurrencyChanged</c> is published, not filtered, when the anchor moves but
    /// both banks were already full — time passed even if nothing was gained. When the anchor
    /// doesn't move at all (inside one regen interval of the last command), nothing is constructed.
    /// </para>
    /// <para>
    /// Deliberately does not touch: Plus expiry (entitlement is session data with no aggregate state
    /// to roll forward), the run's sliding TTL (catch-up runs on meta commands too, and sliding a
    /// run's TTL from outside the run would let a shop visit keep it alive), or quest/shop/event
    /// expiry (not yet authored).
    /// </para>
    /// </remarks>
    /// <param name="state">The working slice — never the caller's.</param>
    /// <param name="context">Everything ambient. <c>NowUtc</c> is the instant rolled forward to.</param>
    private static IReadOnlyList<DomainEvent> AdvanceTime(WorldSlice state, GameContext context)
    {
        var player = state.Player;
        var tuning = EnergyTuning.Read(context.Content);

        // ---------------------------------------------- 1 · Energy regeneration
        var sinceAnchor = context.NowUtc - player.EnergyAnchorUtc;
        var elapsed = TimeSpan.FromTicks(Math.Max(0L, sinceAnchor.Ticks));

        var accrual = EnergyMath.Accrue(tuning, player.LegendLevel, player.Energy, elapsed);

        // Player.AccrueEnergy takes both halves of one accrual together, so "banks written, anchor
        // forgotten" can't happen.
        IReadOnlyList<DomainEvent> events = accrual.AnchorAdvance > TimeSpan.Zero
            ? new DomainEvent[]
            {
                player.AccrueEnergy(accrual.Banks, accrual.AnchorAdvance, tuning, EnergyRegenReason),
            }
            : NoEvents;

        // ---------------------------------------------- 2 · 05:00 UTC game day
        var dayStart = GameCalendar.GameDayStartAt(context.NowUtc);

        if (dayStart >= player.DailyPeriodStartUtc)
        {
            player.ResetDailyCounters(dayStart);
        }

        // ---------------------------------------------- 3 · Monday 05:00 UTC game week
        var weekStart = GameCalendar.GameWeekStartAt(context.NowUtc);

        if (weekStart >= player.WeeklyPeriodStartUtc)
        {
            player.ResetWeeklyCounters(weekStart);
        }

        return events;
    }

    /// <summary>A <c>CommandKind.Meta</c> command may read the run it was handed but never write any part of it.</summary>
    /// <remarks>
    /// Separate from <see cref="FoldRngPositions"/>, which answers a different question
    /// (determinism, for both kinds) from this one (ownership, meta only) — a run command
    /// legitimately writes Gold/HP/Position every turn, a meta command must not touch any of it.
    /// A defect rather than a rejection: <see cref="MarkApplied"/> deliberately does not stamp the
    /// run on a meta command, so a meta handler that wrote the run would leave state changed with a
    /// timestamp saying nothing happened.
    /// </remarks>
    /// <param name="untouched">
    /// The run's canonical bytes as they stood before the handler, or <c>null</c> for a run command
    /// (which may write) or a slice with no run. Bytes rather than the <c>RunSnapshot</c> record,
    /// since its dictionary members compare by reference under synthesized record equality.
    /// </param>
    /// <param name="working">The run the handler was given, or <c>null</c> when the slice carries none.</param>
    /// <param name="registration">The dispatch row, for the message.</param>
    private static void RequireRunUntouched(
        byte[]? untouched, Run? working, CommandRegistration registration)
    {
        if (untouched is null || working is null ||
            untouched.AsSpan().SequenceEqual(CanonicalStateWriter.CanonicalBytes(working.ToSnapshot())))
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

    /// <summary>Folds the RNG scope's final positions into the run, and refuses a run whose positions moved underneath the handler.</summary>
    /// <remarks>
    /// <paramref name="committed"/> is the working run's position map read before the handler ran;
    /// <c>Run.CommitStreamPositions</c> is the only thing that can change it, so a difference here
    /// means the handler called that seam itself — a determinism defect, since whichever value is
    /// stored, a later draw would repeat a sequence the player already played. Checked for meta
    /// commands too (they have no scope to fold but could still reach the commit seam). The fold
    /// itself runs unconditionally on an accepted run command, so a handler can't forget to write
    /// its counter back.
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

        // A meta command reaches this far only to be checked, not to commit anything.
        if (rng is not null)
        {
            working.CommitStreamPositions(rng.FinalPositions());
        }
    }

    /// <summary>Records that a command was accepted, which is what the sliding TTLs measure from.</summary>
    /// <remarks>
    /// The player's timestamp advances always; the run's only for a run command — a meta command
    /// (e.g. a shop visit) must not keep a run alive by sliding its TTL. Only on acceptance: a
    /// refused command must not extend a TTL either, or a client could hold a run open by sending
    /// commands it knows will be refused. Both aggregates throw on an instant earlier than the one
    /// they hold, so the value is floored via <see cref="NotBefore"/> rather than passed raw — a
    /// backwards host clock must not throw out of <see cref="Apply"/>.
    /// </remarks>
    private static void MarkApplied(WorldSlice state, DateTimeOffset nowUtc, CommandKind kind)
    {
        state.Player.MarkApplied(NotBefore(nowUtc, state.Player.LastAppliedAtUtc));

        if (kind == CommandKind.Run)
        {
            state.Run!.MarkApplied(NotBefore(nowUtc, state.Run.LastAppliedAtUtc));
        }
    }

    /// <summary>The later of the two, so an accepted command never asks an aggregate to move a timestamp backwards.</summary>
    /// <remarks>
    /// Governs only the two <c>LastAppliedAtUtc</c> fields; <c>DailyPeriodStartUtc</c> and
    /// <c>WeeklyPeriodStartUtc</c> are kept safe by <see cref="AdvanceTime"/>'s own <c>&gt;=</c>
    /// conditions instead.
    /// </remarks>
    private static DateTimeOffset NotBefore(DateTimeOffset nowUtc, DateTimeOffset stored) =>
        nowUtc < stored ? stored : nowUtc;

    /// <summary>Stamps each event with its ordinal within this <c>CommandResult</c>'s list, starting from 1.</summary>
    /// <remarks>
    /// Not the wire <c>sequence</c> (the per-run/per-player command counter on the request envelope)
    /// — two different numbers sharing a word. Starts at 1, not 0, so <c>DomainEvent.UnstampedSequence</c>
    /// (0) can distinguish an unstamped event from a first one. An event arriving already stamped is
    /// refused rather than silently overwritten, since a handler that assigned its own has guessed
    /// at a position in a list whose final shape it doesn't know.
    /// </remarks>
    private static IReadOnlyList<DomainEvent> Stamp(IReadOnlyList<DomainEvent> events)
    {
        if (events.Count == 0)
        {
            // The shared empty list, not the handler's own — a handler holding onto its own list
            // could otherwise keep appending to it after Apply returns.
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

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>, so messages read the same on any host locale.</summary>
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Message for a failed rehydration. Two causes reach it: a rule mutated the aggregate into a
    /// state its own invariants refuse, or an unmutated, validly persisted aggregate no longer
    /// satisfies a newer content snapshot's validation (e.g. an authored range narrowed). Both are
    /// treated as defects rather than rejections — the composition root, not the domain, is
    /// responsible for choosing a compatible content snapshot.
    /// </summary>
    private static string RoundTripFailure(string aggregate, string error) =>
        "The " + aggregate + " in this WorldSlice does not round-trip through its own snapshot: " +
        error + " 30 §2.1's P4 makes Apply copy the slice before a handler touches it — through " +
        "ToSnapshot()/Rehydrate(), which is the one validated construction path 30 §11.3 sanctions " +
        "— so an aggregate that cannot be rebuilt from its own persisted shape is a rule that " +
        "mutated it into a state its invariants refuse, a caller that built it by hand, or a " +
        "persisted row validated against a DIFFERENT ContentSnapshot than the one this command was " +
        "given. All three are defects and none is a player who asked for too much: the same state " +
        "would fail on the way into Postgres, one command later, with nothing left to say which " +
        "rule wrote it.";
}
