using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `30` §2.3 — the <c>BEGIN_SESSION</c> handler: the game's <b>day cycle</b>, and the first real
/// handler in the project.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What `30` §2.3 asks of it, in its own order.</b> (1) the same lazy catch-up as every other
/// command — which <c>GameRules.Apply</c> has already run before this is called, <em>by
/// construction</em> rather than by convention; (2) <b>if this is the first <c>BEGIN_SESSION</c> of
/// the game day</b>: advance the login calendar, grant the daily free Energy refill, and draw the
/// day's quest slate and Daily shop block from this command's <c>CommandSeed</c>, <em>the day's draw
/// seed</em>; (3) otherwise succeed as a <b>no-op</b> — <em>"its daily effects are idempotent per
/// game day."</em>
/// </para>
/// <para>
/// 🔒 <b>Correctness never depends on this command arriving</b> (`30` §2.3's own boundary note). The
/// resets are lazy, so any command triggers them; this command exists because the login-anchored
/// <em>grants</em> need an anchor and the daily draws need a <b>seed</b>. A player who never sends it
/// still gets their 05:00 UTC resets, still regenerates Energy, and simply never receives the free
/// refill — which is what "login-anchored" means.
/// </para>
/// <para>
/// 🔒 <b>What it deliberately does not do</b> (`30` §2.3, verbatim): claims stay explicit commands
/// (<c>CLAIM_CALENDAR</c>, <c>CLAIM_INBOX</c>, <c>CLAIM_QUEST</c>); inbox expiry auto-grants remain
/// the nightly hosted job (`28` A6); guild settlement remains the scheduled pure function (`30` §5).
/// It also never validates <see cref="BeginSessionCommand.ContentHash"/> — see
/// <see cref="Handle"/>.
/// </para>
/// <para>
/// ⚠️ <b>Everything under <c>Core/Handlers/</c> is <c>internal</c></b> (`30` §11.2: <em>"the services
/// that steer the domain. Nobody outside calls them directly."</em>). Only <c>CombatSimulator</c> and
/// <c>PowerCalculator</c> may ever be public, and neither is here.
/// <c>AccessibilityBoundaryTests.Handlers_and_Rules_are_internal</c> is non-vacuous on its
/// <c>Handlers</c> half from this commit.
/// </para>
/// </remarks>
internal static class BeginSession
{
    /// <summary>
    /// 🔒 The daily counter key that answers <em>"has today's <c>BEGIN_SESSION</c> already run?"</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>This is the idempotence key, and choosing it correctly is the whole difficulty of this
    /// handler.</b> M1-08's catch-up <b>clears the daily counters</b> before a handler is called, so
    /// a handler cannot key "first of the game day" off a counter some <em>earlier</em> command set.
    /// It can key off one <b>it set itself, after the catch-up, in the same command</b> — which is
    /// exactly this counter — and the mechanism is sound in both directions:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>A new game day clears it.</b> <c>GameRules.AdvanceTime</c> calls
    ///   <c>Player.ResetDailyCounters(dayStart)</c> whenever <c>dayStart &gt;= DailyPeriodStartUtc</c>;
    ///   when the boundary is genuinely later the map is emptied, so the first <c>BEGIN_SESSION</c>
    ///   of the new day reads zero.</item>
    ///   <item><b>The same game day preserves it.</b> <c>ResetDailyCounters</c> is a deliberate
    ///   <b>no-op</b> on the boundary already in force (M1-04's counter-wipe fix), so every later
    ///   command of the day — <c>BEGIN_SESSION</c> or not — leaves the counter standing.</item>
    ///   <item><b>A boundary <em>earlier</em> than the stored one does nothing at all</b>, which is
    ///   M1-08's clock-skew guard: <c>Player.RequireNotBefore</c> <em>throws</em> on a backwards
    ///   period, and `30` §2.1's <b>P3</b> forbids that exception reaching <c>Apply</c>'s caller. This
    ///   third arm is the one the sentence here used to omit, and it has a consequence — below.</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>The clock-skew case, stated because it is reachable and player-visible rather than
    /// theoretical.</b> A host clock that jumps <em>forward</em> across 05:00 UTC pays that day's
    /// grants early: the skewed command stores <c>DailyPeriodStartUtc</c> at the later boundary, and
    /// when real time reaches that day the counter is still set, so it is answered as a no-op. The
    /// player is paid <b>early, never twice</b> — which is the direction that matters — and the loop
    /// recovers at the next boundary.
    /// </para>
    /// <para>
    /// 🔒 <b>The <em>corrected</em> clock reaches this handler on every command, and pays nothing —
    /// M1-12 corrected this paragraph, which said it never reached the handler at all.</b> It used
    /// to be true: <c>Player.MarkApplied</c> refused a <c>NowUtc</c> earlier than
    /// <c>LastAppliedAtUtc</c>, so <c>Apply</c> raised <c>ArgumentOutOfRangeException</c> before any
    /// rule decided anything — which was `30` §2.1's <b>P3</b> violated (carried-forward item 20,
    /// now settled: <c>GameRules.MarkApplied</c> floors the instant it hands the aggregates).
    /// </para>
    /// <para>
    /// 🔒 <b>What actually keeps the day from being paid twice was never that throw</b>, and this is
    /// the sentence worth carrying: the skewed command pinned <c>DailyPeriodStartUtc</c>
    /// <em>forward</em>, and <c>GameRules.AdvanceTime</c>'s reset guard is <c>&gt;=</c> — so a
    /// corrected clock computes an <em>earlier</em> boundary, clears nothing, and finds this
    /// handler's daily counter still standing. The refusal was belt over braces that were already
    /// holding. <c>BeginSessionIdempotenceTests.A_forward_clock_jump_pays_early_and_never_twice</c>
    /// asserts all four halves of that.
    /// </para>
    /// <para>
    /// 🔒 <b>The persisted-marker design does not avoid this, and that is why the counter stays.</b>
    /// The obvious alternative — a stored "the game day I last ran on", compared against
    /// <c>DailyPeriodStartUtc</c> — loses <em>exactly the same day</em>, because the thing pinned
    /// forward by the skew is <c>DailyPeriodStartUtc</c> itself. Any key that answers "which game day
    /// is this" inherits it. So the behaviour belongs to M1-08's skew handling rather than to this
    /// choice of key, the counter costs no <c>SchemaVersion</c> field where the marker would, and
    /// <c>BeginSessionIdempotenceTests</c> pins the case rather than leaving it to be rediscovered.
    /// </para>
    /// <para>
    /// 🔒 <b>Why a counter rather than a comparison against <c>Player.DailyPeriodStartUtc</c>.</b>
    /// That field answers <em>"which game day is this"</em>, which is necessary but not sufficient:
    /// keying off it needs a <em>second</em>, persisted "the day I last ran on" marker to compare it
    /// against, which is a <c>SchemaVersion</c> field for a fact the counter mechanism already stores
    /// — and <c>PlayerSnapshot</c>'s own remarks name this exact use ("has today's
    /// <c>BEGIN_SESSION</c> run") as what the open key→count map is <em>for</em>. The two are not
    /// independent: the counter is only meaningful <b>because</b> <c>DailyPeriodStartUtc</c> is what
    /// clears it, so this design consumes M1-08's finding rather than ignoring it.
    /// </para>
    /// <para>
    /// ⚠️ <b>The failure mode if this is wrong is invisible to a one-command-per-day test.</b> A
    /// handler that re-granted on every command of the day and one that grants once are
    /// indistinguishable unless a test sends <em>several</em> commands inside one game day.
    /// <c>BeginSessionIdempotenceTests</c> does, and says so.
    /// </para>
    /// <para>
    /// A stable <c>lower_snake_case</c> token, owned by this handler, exactly as
    /// <c>Player.CountDaily</c>'s remarks require. It is deliberately not a member of a closed enum:
    /// `30` §2.3's daily-reset systems do not exist yet and freezing their vocabulary here would
    /// invent it (S6).
    /// </para>
    /// </remarks>
    internal const string DailyRunCounter = "begin_session";

    /// <summary>
    /// 🔒 The `30` §7 attribution token the daily free refill is logged under, and the column
    /// `21` §8.3 groups <c>income_attribution.csv</c> by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stable <c>lower_snake_case</c> <em>identifier</em>, not a balance number, so `21` §3.1's
    /// "tunables are data" rule does not reach it. It is an <b>assumption</b> all the same, for the
    /// reason <c>GameRules.EnergyRegenReason</c>'s is (recorded assumption <b>A5</b>): `30` §7 fixes
    /// no vocabulary for <c>Reason</c>, and whatever token lands here is the one the economy
    /// dashboards separate the free refill from regeneration by.
    /// </para>
    /// <para>
    /// ⚠️ <b>It carries no assumption letter, and that is honest rather than an oversight.</b> M1-09
    /// does not edit <c>IMPLEMENTATION_TRACKER.md</c>, so calling it "recorded" here — as an earlier
    /// draft did — would have been a claim about a row that does not exist. It is reported as a new
    /// assumption by the task that introduced it and gets its letter at integration; until it does,
    /// <em>this</em> is where it is written down.
    /// </para>
    /// <para>
    /// Distinct from <c>energy_regen</c> on purpose — `10` §3.2 budgets the two separately, and one
    /// token for both would make the free player's daily budget unauditable.
    /// </para>
    /// </remarks>
    internal const string DailyRefillReason = "daily_free_refill";

    /// <summary>
    /// 🔒 `30` §2.3 — applies <c>BEGIN_SESSION</c>.
    /// </summary>
    /// <param name="command">
    /// The command. ⚠️ Both its fields are deliberately <b>unread</b> — see the remarks.
    /// </param>
    /// <param name="input">The cloned, already-caught-up slice and everything ambient.</param>
    /// <returns>
    /// Accepted, always: the day's grants on the first call of the game day, and no events at all on
    /// every later one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It never refuses.</b> `30` §2.3 gives it two outcomes — the day's grants, or a no-op that
    /// <em>"succeeds"</em> — and there is no third. That is not the same as "it cannot fail":
    /// <c>Apply</c> may still throw on a defect (a missing <c>CommandSeed</c>, a miswired host), and
    /// `30` §2.1's <b>P3</b> draws that line deliberately — an exception means the caller or the
    /// domain is wrong, never that the player asked for something they cannot have.
    /// </para>
    /// <para>
    /// 🔒 <b><see cref="BeginSessionCommand.ContentHash"/> is not validated here, and that is a ruling
    /// M1-02 recorded against this task.</b> M1-02 noted the field is a <c>string</c> rather than a
    /// parsed <c>ContentVersion</c>, "so a malformed hash can still be answered with
    /// <c>CONTENT_VERSION_MISMATCH</c>". ⚠️ That answer is a <b>transport-tier</b> value of `14` §16.2
    /// — <c>RejectionReasons.TierOf</c> classifies it, and <c>CommandResult</c> refuses a
    /// transport-tier rejection outright, so <c>Apply</c> <em>cannot</em> return one (`30` §2). The
    /// check therefore belongs where the tier says it does: <b>in front of</b> the domain, at M5-03's
    /// wire envelope, which compares the client's hash against the
    /// <c>GameContext.Content.Version</c> it is about to build and refuses before <c>Apply</c> is
    /// ever called. Parsing it here to reject on it would be a transport rule inside the domain
    /// answering a value the domain may not produce; ignoring it here is not an oversight but the
    /// only reading that keeps both halves of `14` §16.2 true.
    /// <c>ClientVersion</c> is unread for the same reason — a client-support floor is `28`'s and the
    /// host's, not a game rule.
    /// </para>
    /// <para>
    /// 🔒 <b>The order inside the daily block is calendar, then refill, then draws</b> — `30` §2.3's
    /// own — and nothing couples the three today. It is stated rather than left implicit because the
    /// day-26 calendar row is <em>"Full Energy refill"</em>: when M4-09 lands the payout, a claim on
    /// that day and this refill will be two Energy movements in one game day, and which runs first
    /// will matter. It will not matter <em>here</em> — claiming is <c>CLAIM_CALENDAR</c>'s own
    /// command — but the ordering being a decision rather than an accident is what makes that easy to
    /// check.
    /// </para>
    /// </remarks>
    internal static HandlerResult Handle(BeginSessionCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var player = input.Player;

        // 🔒 30 §2.3's "otherwise: succeeds as a no-op". Read BEFORE anything is granted and against
        // the counter this handler itself sets below — see DailyRunCounter for why that is the one
        // fact that survives M1-08's catch-up, and why DailyPeriodStartUtc alone could not answer it.
        if (player.DailyCount(DailyRunCounter) > 0)
        {
            return HandlerResult.Accept();
        }

        // 🔒 THE DAY'S DRAW SEED, taken FIRST — before the calendar moves and before any Energy is
        // granted. 14 §2.3 marks BEGIN_SESSION ⚄ and 30 §2.3 calls this command's CommandSeed "the
        // day's draw seed": the quest slate (19 B) and the Daily shop block (10 §5.1) both derive
        // from THIS scope and from nothing else. Reading it here rather than at the draw sites is
        // deliberate — a host that forgot the seed is a defect (HandlerInput.MetaDraws throws), and
        // discovering that AFTER the calendar had advanced and the refill had been paid would leave
        // Apply throwing out of a half-applied day. Apply discards the working slice on a throw, so
        // nothing is persisted either way; what is bought is that the FIRST thing to fail is the
        // thing that is missing.
        //
        // ⚠️ IT IS BELOW THE EARLY RETURN, so a seedless context is caught on AT MOST ONE command per
        // game day and every repeat accepts one silently. That is the correct trade and not an
        // oversight: 30 §2.3's repeat "succeeds as a no-op", it draws nothing, so demanding a seed
        // from it would make the no-op conditional on the host and turn a client that sends its
        // day-boundary BEGIN_SESSION twice into a 500. The cost is stated rather than hidden — the
        // seam runs on one call in N, and a host that forgets the seed is found on the day's first
        // command rather than on every command.
        //
        // ⚠️ NOTHING IS DRAWN FROM IT TODAY, and that is a registered deferral rather than an
        // omission. Both draws are M4-09's — GapRegister carries QuestSlate/M4-09 and
        // DailyShopStock/M4-09 — because no quest schema, no quest pool and no ShopOffer type exist,
        // and 14 §8.1's stream registry has no row for either draw. Drawing here would need all
        // three invented (S6). What M1-09 owns and what this line IS: the seam, proven deterministic
        // in CommandSeed and proven not to touch a single run stream position.
        _ = input.MetaDraws;

        // 🔒 1 · 19 G — the login calendar. THE POINTER ONLY: the 28 reward rows are 📐 tunable and
        // paying them is CLAIM_CALENDAR's (M4-09). Advancing is refused by the aggregate itself when
        // the open day is unclaimed — 19 G's pause, "nothing is skipped or lost" — so an M1 player,
        // for whom no command can claim, correctly stands on day 1 forever.
        player.AdvanceLoginCalendar(LoginCalendarTuning.Read(input.Context.Content));

        // 🔒 2 · 10 §3.1 — the daily free Energy refill, "to full, 1/day".
        var tuning = EnergyTuning.Read(input.Context.Content);

        // 🔒 M1-10's reading, CALLED rather than reimplemented and deliberately not re-argued here:
        // RefillToFull is DEFICIT-ONLY — max(0, max − energy) — so a full bar grants nothing and
        // overflows nothing.
        //
        // ⚠️ EnergyMath.RefillToFull is the ONE place that ruling lives (30 §11.6), and it records
        // something this call site must not contradict: the conflict between 10 §3.1/§3.2 and 28 C2
        // is "a live contradiction, not a settled rule", implemented the conservative way and
        // REGISTERED FOR A RULING (S16). An earlier draft of this comment declared 28 C2 the erratum
        // and quoted a budget multiplier for the rival reading; both were verdicts the authoritative
        // site had deliberately withheld, and the multiplier did not survive arithmetic —
        // progression.json authors reserveMultipleOfMax 1, so the rival grant is 240 at Legend Level
        // 1 and 400 only at the 200 cap. Read that file for the argument; this line only picks the
        // rule.
        var refilled = EnergyMath.RefillToFull(tuning, player.LegendLevel, player.Energy);

        // 🔒 The event is RETURNED, never dropped. 30 §7 attributes every currency movement and
        // 21 §8.3's income_attribution.csv is a query over those rows;
        // DomainPurityTests.A_currency_event_is_never_discarded_at_its_call_site fails the build for
        // a producer whose return is popped, and M1-08 proved the softer failure — a row constructed,
        // satisfied by the IL rule, and never reaching CommandResult.Events — leaves 62/62 green.
        // BeginSessionRefillTests asserts it actually arrives.
        //
        // ⚠️ ZERO-DELTA IS PUBLISHED, NOT FILTERED, and it is the same call as recorded assumption
        // A6. A player who logs in with a full bar receives a deficit of zero, and the CurrencyChanged
        // goes out with Delta 0 all the same: 21 §8.3 needs to see that the refill was TAKEN — a
        // missing row and a row of zero are the difference between "the player did not log in" and
        // "the player logged in full", which is exactly the engagement question the report answers.
        // Filtering would also reintroduce the constructed-then-discarded shape M1-08 removed.
        var refill = player.SetEnergy(refilled, tuning, DailyRefillReason);

        // 🔒 3 · The counter that makes every one of the above idempotent for the rest of the game
        // day. Written LAST, after the effects it guards, so a throw part-way through cannot leave a
        // player marked as having received a day they did not — Apply discards the working slice on a
        // throw, so this is belt-and-braces rather than load-bearing, and it is the ordering a reader
        // would expect to find anyway.
        player.CountDaily(DailyRunCounter, 1);

        return HandlerResult.Accept(refill);
    }
}
