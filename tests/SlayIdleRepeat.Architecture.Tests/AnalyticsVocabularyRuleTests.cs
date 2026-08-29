using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `14` §10.1 — <b>every authored analytics event is either emitted by the application or carried
/// by a register entry with an open owner, and nothing is emitted that no document authors.</b>
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this exists, stated as the failure it forbids.</b> `14` §10.1 authors 31 event names
/// and the M5-11 kickoff ruled that the sink emits ONLY what the accepted-command stream honestly
/// carries — four of the 31, plus one documented extension. Without a register, the other 27 are
/// names in a design document: nothing notices when the system that could emit one ships without
/// emitting it, and nothing notices a sink inventing a 32nd name the document never authored. Both
/// directions are S6 failures — data absent versus data invented — and both are silent.
/// </para>
/// <para>
/// 🔒 <b>The emitted set is read from production</b> —
/// <see cref="AnalyticsVocabulary.Emitted"/>, the closed list the translator is built over — never
/// transcribed here, so adding an emission updates this rule by itself. The authored set IS a
/// transcription, because the document is the only place it exists; §10.1 is a closed list of a
/// locked document, so its count is exact rather than a lower bound.
/// </para>
/// <para>
/// ⚠️ <b>What this file cannot see, said so nobody over-trusts it:</b> whether an emitted name is
/// emitted from the path its unit facts claim (Application.Tests holds that half), and an entry
/// whose written <i>reason</i> went stale while its predicate still holds — re-read the register at
/// each milestone kickoff, the same caveat <c>PortCatalogue</c> carries.
/// </para>
/// </remarks>
public sealed class AnalyticsVocabularyRuleTests
{
    // ------------------------------------------------------------------------------- the subjects

    /// <summary>
    /// 🔒 `14` §10.1's authored event list, transcribed verbatim — the slash-groups expanded to one
    /// name each, nothing added, nothing dropped. 31 names.
    /// </summary>
    private static readonly string[] AuthoredEvents =
    {
        "run_start", "run_end", "battle_end", "perk_drafted", "perk_skipped", "die_rolled",
        "tile_resolved", "ad_offered", "ad_started", "ad_completed", "ad_failed",
        "plus_offer_viewed", "plus_trial_started", "plus_converted", "plus_renewed",
        "plus_cancelled", "plus_lapsed", "gear_merged", "gear_enhanced", "talent_spent",
        "pet_levelled", "duel_start", "duel_end", "energy_empty", "session_start", "session_end",
        "level_up", "chapter_unlocked", "tutorial_step", "disconnect", "resync",
    };

    /// <summary>
    /// 🔒 The emitted names `14` §10.1 does NOT author, each with the ruling that authorises it.
    /// Closed: an emitted name that is neither authored nor here is an invented name, and the rule
    /// fails on it by name.
    /// </summary>
    private static readonly (string Event, string Authorisation)[] DocumentedExtensions =
    {
        ("currency_changed",
         "not in 14 §10.1's list; the M5-11 kickoff ruling 3 explicitly authorises currency events "
         + "where the domain events carry them — CurrencyChanged carries currency, delta and reason, "
         + "so nothing is invented (recorded as the run's assumption 2)"),
    };

    /// <summary>
    /// 🔒 Every `14` §10.1 event the application deliberately does not emit, with the open tracker
    /// task that owns making it emittable and the reason it cannot be emitted honestly today. Each
    /// entry expires by itself: through <see cref="No_register_entry_names_an_event_the_application_now_emits"/>
    /// when the name starts being emitted, and through
    /// <see cref="Every_analytics_register_owner_is_a_task_the_tracker_still_has_open"/> when its
    /// owner ships without discharging it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The ruling under every entry is the kickoff's: properties are ABSENT, never invented
    /// (steering S6), and an event whose entire value is its payload is not emitted as a bare name.
    /// The sink sees the accepted command, the post-command state and the domain events — nothing
    /// else — so "the stream does not carry it" is a measurable claim about those three.
    /// </para>
    /// <para>
    /// ⚠️ Owners marked "nearest row" are exactly that — the <c>IGhostRepository</c> weakness,
    /// recorded rather than smoothed over: the row is the first open task that needs the event or
    /// touches its system, not a row that says "emit this". A reader checking one at kickoff may
    /// find the row does not think it owns the emission; re-point it there rather than here.
    /// </para>
    /// </remarks>
    private static readonly (string Event, string Owner, string Why)[] UnemittableEvents =
    {
        new("battle_end", "M5-16",
            "The accepted CONFIRM_BATTLE_RESULT carries only a log hash and a won flag; 14 §10.1's "
            + "payload (enemy, duration, hp remaining) exists only inside the battle "
            + "log. M5-05 landed the STORE, which this entry first named as its owner — but a "
            + "store is not a producer: IBattleLogStore.PutAsync has no production caller, so no "
            + "log exists to read a payload out of. M5-16 is the row that writes one. "
            + "A bare name with the payload stripped would defeat the event's stated purpose."),

        new("perk_drafted", "M18-08",
            "PICK_PERK carries only an option index into a draft the post-command state no longer "
            + "holds, so the drafted perk id, its tier and the options offered are all unreachable "
            + "without diffing snapshots, which the sink refuses to do. Nearest row: no open task "
            + "adds a draft domain event, and M18-08's soft-launch telemetry review is the first "
            + "consumer that cannot run without this event's derived metric."),

        new("perk_skipped", "M18-08",
            "SKIP_DRAFT is accepted, but the skipped draft's contents — what the player turned down, "
            + "which is the analytical point — are gone from the post-command state for the same "
            + "reason perk_drafted's payload is. Same nearest-row owner, same missing draft event."),

        new("tile_resolved", "M18-08",
            "RESOLVE_TILE carries no payload at all; the resolved tile's type and index live in the "
            + "board, and no domain event names them. Nearest row: M18-08's watch list needs the "
            + "run-abandonment-by-tile-index metric this event is the sole source of."),

        new("ad_offered", "M15-03",
            "The ad lifecycle happens in the client's mediation SDK; the server first learns of an "
            + "ad through the S2S callback path and CLAIM_AD_REWARD, both of which M15-03 builds — "
            + "CLAIM_AD_REWARD is measured Deferred to M15-03 in GameRules today, so no accepted "
            + "command carries any ad fact at all."),

        new("ad_started", "M15-03",
            "Same stream gap as ad_offered: a client-side lifecycle fact with no server-side "
            + "counterpart until M15-03's callback path and CLAIM_AD_REWARD handler exist."),

        new("ad_completed", "M15-03",
            "Same stream gap as ad_offered — and completion is the one the S2S callback itself "
            + "attests, so M15-03's verification path is precisely where this becomes honest."),

        new("ad_failed", "M15-03",
            "Same stream gap as ad_offered: a no-fill or playback failure is a client/vendor fact "
            + "the server never sees before M15-03's reward path lands."),

        new("plus_offer_viewed", "M15-06",
            "A view of the Plus offer is a presentation fact; the surface that shows the offer — "
            + "shop tab, post-L8 Home banner — is M15-06's, and until it exists nothing reaches the "
            + "server that could carry a viewing."),

        new("plus_trial_started", "M15-05",
            "Trial, conversion, renewal, cancellation and lapse are store-side subscription facts "
            + "that reach the server through the store S2S notification webhooks M15-05 builds; no "
            + "accepted command carries any subscription state today."),

        new("plus_converted", "M15-05",
            "Same webhook stream as plus_trial_started — the store attests a conversion, and M15-05 "
            + "owns the webhook-to-entitlement path that first hears it."),

        new("plus_renewed", "M15-05",
            "Same webhook stream as plus_trial_started; a renewal is the store's fact, not a "
            + "command's."),

        new("plus_cancelled", "M15-05",
            "Same webhook stream as plus_trial_started; M15-05's lapse handling is where a "
            + "cancellation first becomes server-visible."),

        new("plus_lapsed", "M15-05",
            "Same webhook stream as plus_trial_started; a lapse is inferred from grace windows "
            + "M15-05's row owns outright."),

        new("gear_merged", "M16-03",
            "MERGE is accepted today, but the command names no items and the merge's outcome exists "
            + "only as a state diff the sink refuses to compute. What is missing is a forge domain "
            + "event, and M16-03's feats are the first mechanism that needs forge facts in the event "
            + "stream — the same owner FeatCounterProjectionCoverageRuleTests already carries for "
            + "GearGranted's undecided counter vocabulary."),

        new("gear_enhanced", "M16-03",
            "ENHANCE carries the item id but not the outcome 14 §10.1 asks for (level, success); "
            + "success versus failure is a state diff, not a command or event. Same missing forge "
            + "event and same owner as gear_merged."),

        new("talent_spent", "M4-06",
            "SPEND_TALENT is measured Deferred to M4-06 in GameRules — the command is refused before "
            + "it could ever reach the post-commit dispatcher, so there is no accepted command to "
            + "translate until the talent tree lands."),

        new("pet_levelled", "M4-07",
            "LEVEL_PET is measured Deferred to M4-07 in GameRules, the pets task; no accepted "
            + "command exists to translate until it lands."),

        new("duel_start", "M12-04",
            "START_DUEL is measured Deferred to M12-04 in GameRules, the duel-flow task that also "
            + "owns the server-issued duelSeed the event's context needs."),

        new("duel_end", "M12-04",
            "SUBMIT_DUEL is measured Deferred to M12-04 in GameRules; a duel cannot end before one "
            + "can start."),

        new("energy_empty", "M16-02",
            "Reaching zero energy emits no domain event, and the observable symptom — a START_RUN "
            + "refused for energy — is a REJECTED command, which never reaches the post-commit "
            + "dispatcher by design. M16-02's row owns the hoard/overflow telemetry beside the "
            + "Energy Reserve UI, which is where an energy-state emission belongs."),

        new("session_end", "M18-08",
            "No command carries a session's end — a mobile client is killed, not closed — so the "
            + "fact is an inference. M5-06 shipped the authoritative last-seen this entry was "
            + "first pointed at, and emission still did not follow: what is missing is not data "
            + "but a RULE for how long after last-seen a session is over, and no document "
            + "authors one. Naming a number here would be the invention S6 forbids, so it waits "
            + "for the telemetry review that decides what the window is worth."),

        new("level_up", "M16-03",
            "Legend XP is banked inside Apply with no level-up domain event; the resulting level is "
            + "a state diff. Nearest row: M16-03's Renown milestones are the first mechanism that "
            + "needs level facts in the event stream."),

        new("chapter_unlocked", "M16-03",
            "A chapter unlock is a threshold crossing inside Apply with no domain event; the "
            + "post-command state carries the unlocked set, not the crossing. Nearest row: M16-03's "
            + "feats ('unlock chapter N' is feat-shaped) are the first consumer of an unlock event."),

        new("tutorial_step", "M4-12",
            "The FTUE's beats do not exist: SKIP_FTUE is measured Deferred to M4-12 in GameRules and "
            + "the ftue.json data package with its per-beat resume is that row's deliverable, so "
            + "there is no step to report until it lands."),

        new("disconnect", "M18-08",
            "🔒 THE MACHINERY NOW EXISTS AND THE ENTRY STILL HOLDS, which is why the owner moved off "
            + "M7-02. That task built the five connection states and ReconnectManager, so the fact "
            + "is known client-side with 14 §10.1's duration and screen both to hand. What is still "
            + "missing is a CHANNEL: IAnalyticsSinkPort is a SERVER port and every name in 14 "
            + "§10.1's set is emitted server-side off the accepted-command stream, so a client "
            + "holding this fact has nothing to hand it to. M18-08 already owns 'disconnect rate by "
            + "region and screen' in this file's own DerivedMetrics, which makes it the row that "
            + "first needs the name to reach a sink at all."),

        new("resync", "M18-08",
            "The same missing channel as disconnect, and the same owner. M7-02 built the resync "
            + "flash and the state mirror that knows the client moved, so the fact exists; what does "
            + "not is any route from a client-side fact to a server-side sink, and a resync the "
            + "client performs silently still reaches no accepted-command stream."),
    };

    /// <summary>
    /// 🔒 `14` §10.1's four derived metrics — none is buildable, each because its source events are
    /// in the register above. BUILT: nothing. Each row names its missing sources and an owner, and
    /// <see cref="Every_derived_metric_still_waits_on_an_unemitted_source"/> is the expiry: the
    /// commit that emits a named source turns the row red, which is the moment somebody has to
    /// decide whether the metric is now buildable.
    /// </summary>
    private static readonly (string Metric, string[] MissingSources, string Owner, string Why)[] DerivedMetrics =
    {
        new("perk pick rate by perk id", new[] { "perk_drafted", "perk_skipped" }, "M18-08",
            "14 §10.1 calls it the most important balance metric; it needs the drafted perk id and "
            + "the offers turned down, and neither event can be emitted honestly yet."),

        new("run abandonment point by tile index", new[] { "tile_resolved" }, "M18-08",
            "the pacing metric; without tile_resolved no tile index ever reaches the sink, so the "
            + "abandonment point cannot be located on the board."),

        new("disconnect rate by region and screen", new[] { "disconnect" }, "M18-08",
            "the online-requirement health check, named on M18-08's own week-1 watch list; blocked "
            + "on the client-side disconnect fact reaching the server at all."),

        new("season rating drift median and p90", new[] { "duel_end" }, "M12-08",
            "risk R13's mandatory metric, and M12-08's row owns it by name ('season rating-drift "
            + "metric (R13)'); no rating exists to drift before the duel flow lands."),
    };

    // -------------------------------------------------------------------------------- the floors
    //
    // 🔒 Steering S3. Literals from counting the document and the ruling — never the lists' own
    // Count properties, which cannot notice a list being trimmed.

    /// <summary>
    /// `14` §10.1 is a closed list of a locked document: 31 names, exact — a lower bound would be
    /// cleared by a name added to cover a name dropped, the SpecifiedPortFloors argument.
    /// </summary>
    private const int AuthoredEventCount = 31;

    /// <summary>
    /// Emitted names. Below this, the emitted-vocabulary rules quantify over a set that has lost
    /// members — the translator was narrowed and nothing else says so.
    /// </summary>
    private const int EmittedFloor = 5;

    /// <summary>
    /// Register entries. Below this, the stale/unanchored/owner directions report success over a
    /// register that has quietly stopped carrying most of the document.
    /// </summary>
    private const int RegisterFloor = 20;

    /// <summary>A tracker task id, the register's owner currency — matching PortCatalogue's shape.</summary>
    private static readonly Regex OwnerTaskId = new(@"^M\d{1,2}-\d{2}[a-z]?$", RegexOptions.Compiled);

    // --------------------------------------------------------------------------------- the rules

    /// <summary>
    /// 🔒 `14` §10.1, the undeclared direction: every authored event is either emitted or carried by
    /// a register entry — and by exactly one of the two, since an emitted event's entry is stale by
    /// definition (<see cref="No_register_entry_names_an_event_the_application_now_emits"/> holds
    /// the overlap from the other side).
    /// </summary>
    [Fact]
    public void Every_authored_event_is_emitted_or_registered_with_an_owner()
    {
        var emitted = AnalyticsVocabulary.Emitted.ToHashSet(StringComparer.Ordinal);
        var registered = UnemittableEvents.Select(e => e.Event).ToHashSet(StringComparer.Ordinal);

        ArchRule.Empty(
            AuthoredEvents
                .Where(name => !emitted.Contains(name) && !registered.Contains(name))
                .Select(name =>
                    $"14 §10.1 authors '{name}'. AnalyticsVocabulary.Emitted does not carry it and "
                    + "the unemittable register does not either — so the document promises a "
                    + "dashboard reads it, nothing emits it, and no milestone is on the hook. Either "
                    + "give it a real emission path with its Application.Tests fact, or add a "
                    + "register entry naming the open task that owns the stream gap.")
                .ToArray(),
            "Every 14 §10.1 event is emitted or registered with an open owner (steering S6, S4).");
    }

    /// <summary>
    /// 🔒 `14` §10.1 / steering S4, the stale direction: no register entry outlives the emission it defers. The
    /// moment a name joins <see cref="AnalyticsVocabulary.Emitted"/>, its entry is wrong and this
    /// fails — on that commit, naming the entry.
    /// </summary>
    [Fact]
    public void No_register_entry_names_an_event_the_application_now_emits()
    {
        var emitted = AnalyticsVocabulary.Emitted.ToHashSet(StringComparer.Ordinal);

        ArchRule.Empty(
            UnemittableEvents
                .Where(entry => emitted.Contains(entry.Event))
                .Select(entry =>
                    $"'{entry.Event}' is registered as unemittable (owner {entry.Owner}), and "
                    + "AnalyticsVocabulary.Emitted now carries it. A satisfied entry is the register "
                    + "disagreeing with the code it describes; delete it in the commit that added "
                    + "the emission — and re-read the derived-metric rows, whose sources may just "
                    + "have arrived.")
                .ToArray(),
            "No analytics register entry defers an event that is now emitted (steering S4).");
    }

    /// <summary>
    /// 🔒 The unanchored direction: every register entry names an event `14` §10.1 actually authors.
    /// An entry the undeclared direction cannot see can never be satisfied — only deleted by hand.
    /// </summary>
    [Fact]
    public void No_register_entry_names_an_event_the_document_does_not_author()
    {
        var authored = AuthoredEvents.ToHashSet(StringComparer.Ordinal);

        ArchRule.Empty(
            UnemittableEvents
                .Where(entry => !authored.Contains(entry.Event))
                .Select(entry =>
                    $"'{entry.Event}' is registered as unemittable, and 14 §10.1's transcription "
                    + "does not author it. It defers something no document asks for. If the "
                    + "document's list changed, fix the transcription in the same commit; otherwise "
                    + "delete the entry.")
                .ToArray(),
            "Every analytics register entry defers an event 14 §10.1 authors (steering S4).");
    }

    /// <summary>
    /// 🔒 `14` §10.1 / steering S6, the other direction: nothing emits a name the document does not author,
    /// unless a <see cref="DocumentedExtensions"/> row carries the ruling that authorises it. An
    /// invented event name is fabricated data wearing snake_case.
    /// </summary>
    [Fact]
    public void Every_emitted_event_is_authored_or_a_documented_extension()
    {
        var authored = AuthoredEvents.ToHashSet(StringComparer.Ordinal);
        var extensions = DocumentedExtensions.Select(e => e.Event).ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        offenders.AddRange(
            AnalyticsVocabulary.Emitted
                .Where(name => !authored.Contains(name) && !extensions.Contains(name))
                .Select(name =>
                    $"AnalyticsVocabulary.Emitted carries '{name}', which 14 §10.1 does not author "
                    + "and no DocumentedExtensions row authorises. An emitted name nobody ruled on "
                    + "is an invented vocabulary entry (steering S6): either it transcribes a "
                    + "document amendment — add it to the authored list with the amendment — or it "
                    + "carries a kickoff ruling — add the extension row naming it."));

        // The extension list's own two directions, so it cannot rot into a bypass: a row must
        // authorise something actually emitted, and must not restate an authored name.
        offenders.AddRange(
            DocumentedExtensions
                .Where(e => !AnalyticsVocabulary.Emitted.Contains(e.Event, StringComparer.Ordinal))
                .Select(e => $"DocumentedExtensions authorises '{e.Event}', which nothing emits. A "
                             + "standing authorisation for a name not in use is a blank cheque the "
                             + "next translator edit cashes unnoticed; delete the row."));

        offenders.AddRange(
            DocumentedExtensions
                .Where(e => authored.Contains(e.Event))
                .Select(e => $"DocumentedExtensions authorises '{e.Event}', which 14 §10.1 already "
                             + "authors — the row claims an exception for the ordinary case, and a "
                             + "reader would conclude the authored list is missing it."));

        offenders.AddRange(
            DocumentedExtensions
                .Where(e => string.IsNullOrWhiteSpace(e.Authorisation) || e.Authorisation.Length < 40)
                .Select(e => $"the DocumentedExtensions row for '{e.Event}' carries no authorisation "
                             + "worth checking; the row exists to name the ruling, not to exist."));

        ArchRule.Empty(
            offenders,
            "Every emitted analytics name is authored by 14 §10.1 or carried by a documented, ruled extension (steering S6).");
    }

    /// <summary>
    /// `23` §6's well-formedness, applied here: every register entry names a task-shaped owner and a
    /// reason worth falsifying, and no event is registered twice.
    /// </summary>
    [Fact]
    public void Every_register_entry_is_well_formed()
    {
        var offenders = new List<string>();

        foreach (var (name, owner, why) in UnemittableEvents)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                offenders.Add($"an entry owned by '{owner}' names no event.");
            }

            if (!OwnerTaskId.IsMatch(owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{name}' names owner '{owner}', which is not a tracker TASK id (M5-05, "
                    + "M18-06a). PortCatalogue.TrackerStatuses reads task rows only, so any other "
                    + "shape is reported as an owner nobody declares — loud, but pointing at the "
                    + "parser instead of the entry.");
            }

            if (string.IsNullOrWhiteSpace(why) || why.Length < 40)
            {
                offenders.Add(
                    $"'{name}' carries no written reason worth falsifying. Every entry here is a "
                    + "claim that the accepted-command stream does not carry the event's facts, and "
                    + "it has to say WHICH facts and WHERE they first exist, or the next kickoff "
                    + "cannot check it.");
            }
        }

        offenders.AddRange(
            UnemittableEvents
                .GroupBy(e => e.Event, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"'{g.Key}' is registered {g.Count()} times — two owners for one event "
                             + "is two answers to who unblocks it, and deleting either reads as the "
                             + "other keeping the promise."));

        ArchRule.Empty(
            offenders,
            "Every analytics register entry is well formed: task-shaped owner, written reason, no duplicates (23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §6 / steering S4's M4 amendment, on this register: every owner — event entries and derived
    /// metrics alike — is a tracker task that is still <b>open</b>. An entry whose owner shipped can
    /// never expire; nothing would ever delete it and no kickoff would ever be asked.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="PortCatalogue.TrackerStatuses"/> and
    /// <see cref="PortCatalogue.OwnersNoLongerOpen"/> — the one owner-status mechanism this
    /// repository has (steering S4), including its parser's anchor floors, which
    /// <c>PortCatalogueTests</c> holds for both of us.
    /// </remarks>
    [Fact]
    public void Every_analytics_register_owner_is_a_task_the_tracker_still_has_open()
    {
        var entries = UnemittableEvents
            .Select(e => (Subject: "analytics event '" + e.Event + "'", e.Owner))
            .Concat(DerivedMetrics.Select(m => (Subject: "derived metric '" + m.Metric + "'", m.Owner)));

        ArchRule.Empty(
            PortCatalogue.OwnersNoLongerOpen(entries, PortCatalogue.TrackerStatuses(Tracker())),
            "Every analytics register owner is a tracker task that is still open (steering S4).");
    }

    /// <summary>
    /// 🔒 `14` §10.1's derived metrics: each row still waits on a source event that is genuinely
    /// unemitted and genuinely authored — the self-expiry. A source joining the emitted vocabulary
    /// turns its metric's row red on that commit, which is the moment the metric decision is live.
    /// </summary>
    [Fact]
    public void Every_derived_metric_still_waits_on_an_unemitted_source()
    {
        var authored = AuthoredEvents.ToHashSet(StringComparer.Ordinal);
        var emitted = AnalyticsVocabulary.Emitted.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var (metric, sources, owner, why) in DerivedMetrics)
        {
            if (sources.Length == 0)
            {
                offenders.Add(
                    $"the derived-metric row '{metric}' names no missing source event, so nothing "
                    + "can ever expire it — it is a comment wearing a register row.");
            }

            if (string.IsNullOrWhiteSpace(why) || why.Length < 40)
            {
                offenders.Add($"the derived-metric row '{metric}' carries no reason worth falsifying.");
            }

            foreach (var source in sources)
            {
                if (!authored.Contains(source))
                {
                    offenders.Add(
                        $"the derived-metric row '{metric}' waits on '{source}', which 14 §10.1 does "
                        + "not author — the row can never be satisfied, only deleted by hand.");
                }

                if (emitted.Contains(source))
                {
                    offenders.Add(
                        $"the derived-metric row '{metric}' waits on '{source}', and "
                        + "AnalyticsVocabulary.Emitted now carries it. The blocker this row records "
                        + $"is gone: decide the metric (owner {owner}) or re-state what still "
                        + "blocks it, in the commit that emitted the source.");
                }
            }
        }

        ArchRule.Empty(
            offenders,
            "Every 14 §10.1 derived metric still waits on an authored, unemitted source event (steering S4).");
    }

    /// <summary>
    /// 🔒 `23` §6, steering <b>S3</b> — the subject sets these rules quantify over can never silently empty.
    /// Every rule above is "no member of S fails X", and each is green over an empty S.
    /// </summary>
    [Fact]
    public void The_analytics_vocabulary_subject_sets_have_floors()
    {
        var offenders = new List<string>();

        if (AuthoredEvents.Length != AuthoredEventCount)
        {
            offenders.Add(
                $"the 14 §10.1 transcription holds {AuthoredEvents.Length} names, not "
                + $"{AuthoredEventCount}. §10.1 is a closed list of a locked document, so the count "
                + "is exact: a trimmed transcription quietly stops asking about the events it lost, "
                + "and a padded one demands events nobody authored.");
        }

        AuthoredEvents.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            AuthoredEvents.Length,
            "a duplicated authored name would keep the exact count satisfied while one of 14 §10.1's "
            + "events went untranscribed.");

        if (AnalyticsVocabulary.Emitted.Count < EmittedFloor)
        {
            offenders.Add(
                $"AnalyticsVocabulary.Emitted holds {AnalyticsVocabulary.Emitted.Count} names, floor "
                + $"{EmittedFloor}. The emitted-side rules are stated over it; a narrowed translator "
                + "does not fail — it stops emitting and everything here stays green.");
        }

        if (UnemittableEvents.Length < RegisterFloor)
        {
            offenders.Add(
                $"the unemittable register holds {UnemittableEvents.Length} entries, floor "
                + $"{RegisterFloor}. The stale, unanchored, well-formedness and owner directions all "
                + "quantify over it; near-empty, they report success while the document's promises "
                + "go unowned. If entries were legitimately discharged, lower this floor in the "
                + "same commit and say which.");
        }

        if (DerivedMetrics.Length != 4)
        {
            offenders.Add(
                $"the derived-metric register holds {DerivedMetrics.Length} rows, not 4. 14 §10.1 "
                + "names exactly four derived metrics (the fourth is mandatory per risk R13), so a "
                + "row leaving means a mandatory metric lost its owner silently.");
        }

        // 🔒 Identity, not only count (the ObjectStoreVocabulary argument): the four emitted-
        // authored names are the whole intersection the kickoff ruled, so they are named one by one
        // — a count floor over Emitted is cleared by five extensions and no authored event at all.
        foreach (var name in new[] { "run_start", "run_end", "session_start", "die_rolled" })
        {
            if (!AnalyticsVocabulary.Emitted.Contains(name, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"'{name}' is gone from AnalyticsVocabulary.Emitted. It is one of the four "
                    + "authored events the M5-11 kickoff ruled emittable, so its loss is either a "
                    + "deliberate un-emission — which needs a register entry with an owner in the "
                    + "same commit — or the translator quietly narrowing.");
            }
        }

        ArchRule.Empty(
            offenders,
            "The analytics vocabulary's subject sets are the ones these rules were written against (steering S3).");
    }

    /// <summary>The tracker's raw text — the same read <c>PortCatalogueTests.Tracker</c> does.</summary>
    private static string Tracker() =>
        File.ReadAllText(Path.Combine(RepoLayout.RepoRoot, "IMPLEMENTATION_TRACKER.md"));
}
