using System.Text.RegularExpressions;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `28` Part A — <b>everything the inbox specification asks for that this build deliberately does
/// not do</b>, each with the tracker task that owns doing it and the reason it cannot be done today.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why a register rather than comments.</b> The inbox ships with more named absences than any
/// other system in this repository: five of `28` A3.1's seven segment fields, five of its seven
/// attachment kinds, the whole of A5's push, A6's MILESTONE-on-auto-grant, the read timestamp its
/// own schema declares, and three of its five telemetry names. Every one of those is a sentence in a
/// design document that nothing implements. Without a register, "we decided not to" and "nobody
/// noticed" look identical — and the second is how a compensation grant reaches the wrong cohort.
/// </para>
/// <para>
/// 🔒 <b>Why it is not a row in one of the existing registers.</b> Each of those is closed to its own
/// subject and stays that way on purpose. <c>GapRegister</c>'s decidable predicate is a <c>Core</c>
/// TYPE that must not exist, and most of these absences are behaviours rather than types.
/// <c>PortCatalogue.Deferred</c> defers ports. <c>AnalyticsVocabularyRuleTests</c>'
/// <c>UnemittableEvents</c> is closed to `14` §10.1's own thirty-one names by its own unanchored
/// direction, and `28` A6's five <c>mail_*</c> names are a second document's vocabulary — widening it
/// would make every direction in that file quantify over a transcription it does not hold. The two
/// <c>mail_*</c> names this build DOES emit are carried there, as documented extensions, which is
/// exactly where that register does have something to say.
/// </para>
/// <para>
/// The mechanism fails in four directions, the same construction as <c>PortCatalogue</c>: malformed,
/// satisfied, owner-not-open, and vacuous.
/// </para>
/// <para>
/// ⚠️ <b>The known limit, stated rather than smoothed over.</b> What is decidable here is the shape
/// and, for the attachment kinds, whether the absence is still real. What is NOT decidable is an
/// entry whose written reason went stale while its predicate still holds — the same hole every other
/// register in this repository documents. Re-read these at each kickoff.
/// </para>
/// </remarks>
public sealed class InboxAbsenceRuleTests
{
    /// <summary>One thing `28` Part A asks for that this build does not do.</summary>
    /// <param name="Subject">What is absent, as the specification writes it.</param>
    /// <param name="Owner">The tracker task that builds it.</param>
    /// <param name="Why">Why it cannot be built today. Something a later reader can falsify.</param>
    private sealed record InboxAbsence(string Subject, string Owner, string Why);

    /// <summary>
    /// 🔒 Every `28` Part A absence, its owner and its reason. Each expires by its owner shipping.
    /// </summary>
    private static readonly InboxAbsence[] Absences =
    {
        // ── the attachment kinds (28 A2's list, minus the two v1 grants) ───────────────────────

        new("CHEST/EGG/CRATE attachments", "M4-02",
            "28 A4 requires a container attachment to claim as an UNOPENED container onto the shelf, " +
            "never as pre-opened contents, because pity and Focus are read when the PLAYER opens it. " +
            "Player.cs records the shelf as deliberately absent and M4-02 is the row that builds it. " +
            "Refused by name at both gates: MailAttachmentKinds.Resolve answers " +
            "CONTAINER_SHELF_ABSENT, the send path stops the message being written, and the claim " +
            "path holds it rather than paying part of it."),

        new("GEAR attachments", "M4-02",
            "A rolled item is a seeded draw, and the nightly auto-grant job is not a command — 30 §3 " +
            "makes CommandSeed meta-command-only — so the job holds nothing to draw with. A grant " +
            "path that made a seed up would be a second, unreproducible source of loot beside " +
            "LuckService. ⚠️ NEAREST ROW, not a row that names this: M4-02 is the container/opening " +
            "task and therefore the first place a mailed item would have a home, but no tracker row " +
            "rules on granting gear from outside a run. A reader checking it at kickoff may well " +
            "find that row does not think it owns this."),

        new("SET_TOKENS/BEAST_MARKS attachments", "M4-07",
            "Both are non-wallet counters, deliberately absent from CurrencyId because a wallet slot " +
            "for them would start emitting CurrencyChanged as if they were income. M4-07 is the " +
            "pets/beasts row, which is where a Beast Mark first has anywhere to be counted. Refused " +
            "as NO_WALLET_HOME at both gates."),

        new("28 A4's held-for-inventory-space claim state", "M4-02",
            "🔒 DORMANT BY CONSTRUCTION, and this is the entry that says so. A4 requires a claim that " +
            "would exceed inventory capacity to be HELD — the message stays claimable and says 'Not " +
            "enough inventory space' — and no v1 attachment kind grants an item, so no claim can " +
            "reach inventory at all. Building the state machine now would be a branch nothing can " +
            "enter and no test can drive except by constructing the state directly. It turns on with " +
            "the first item-granting kind, which is the same commit that removes one of the three " +
            "entries above. ⚠️ Not to be confused with Inventory.Place's own HELD overflow list: " +
            "that holds the ITEM, this holds the MESSAGE, and neither applies at v1."),

        // ── the segment fields (28 A3.1's predicate, minus the two answerable ones) ────────────

        new("The 'Plus status' segment predicate", "M15-05",
            "🔴 THE ONE RULING THIS TASK CORRECTED. No subscription state is persisted anywhere: " +
            "Entitlements is resolved onto the SESSION by the composition root, and every host in " +
            "this build resolves it through LocalHostAmbience.NoSubscriptionResolved(). M15-05 owns " +
            "the store webhook path that first makes Plus a fact about an ACCOUNT rather than about " +
            "a session. Refused by name at the command line and again in the selector."),

        new("The 'region' segment predicate", "M18-05",
            "No player row, snapshot or profile carries a region anywhere in this build, and nothing " +
            "in the design set says where one would come from. ⚠️ NEAREST ROW: M18-05 staffs the " +
            "moderation/ops surfaces and is the first row that needs to slice a population at all."),

        new("The 'client version' segment predicate", "M18-05",
            "A client version arrives on BEGIN_SESSION and is not persisted, so the last one a player " +
            "sent is not a fact any store can be asked for. 🔴 THE OWNER THIS ENTRY REPLACES WAS " +
            "M5-09, and it was wrong by the time it was written: M5-09 shipped the content/version " +
            "distribution path and pinned versions to RUNS and SESSIONS in typed columns, not to " +
            "players — so the row this entry was waiting on went past without the fact becoming " +
            "storable. Found by Every_inbox_absence_owner_is_a_task_the_tracker_still_has_open at " +
            "the integration merge, which is the direction that exists for exactly this. " +
            "⚠️ NEAREST ROW: M18-05, for the reason the region entry above gives — it is the first " +
            "row that needs to slice a population at all, and nothing between here and there records " +
            "a per-player client version."),

        new("The 'guild membership' segment predicate", "M14-01",
            "Guilds are not built, so no player has one to be in. M14-01 authors the guild identity " +
            "the predicate would key on."),

        new("The 'affected run window' segment predicate", "M5-16",
            "It needs a per-run history that outlives the run; a finished run is archived by its own " +
            "identity and indexed by nothing a time window could be asked over. M5-16 is the row " +
            "that first writes a durable per-run record (the battle log) — ⚠️ NEAREST ROW: it writes " +
            "logs rather than an index, so a reader may find it does not think it owns this."),

        // ── the surfaces and the operational rules ─────────────────────────────────────────────

        new("28 A5's COMPENSATION push", "M16-04",
            "Push is not built. The kickoff ruling settled the provider (FCM directly, no wrapper, " +
            "APNs deferred) and no M5 task declares the port, because it has no verifiable " +
            "implementation and no consumer: A5's rule is 'only for COMPENSATION, only when the " +
            "attachment is non-trivial, inside the existing 1/day cap', and both 'non-trivial' and " +
            "the cap live with the push milestone. M16-04 is that row — 'registration port + " +
            "adapter, the 5 permitted sends, opt-in, 1/day cap' — and compensation is one of the " +
            "five it names."),

        new("A MILESTONE message from an auto-grant", "M16-03",
            "28 A6 asks the expiry job to emit one 'only if the value is material', and NOTHING " +
            "anywhere authors what material means — not a currency amount, not a share of a day's " +
            "income, not a tier. A threshold invented here would decide, silently and for every " +
            "player, which compensations are worth telling somebody about. The feature is off and " +
            "greppable instead: the sweep sends no message. M16-03 authors the MILESTONE vocabulary " +
            "(feats and Renown) and is where 'material' first has a scale to be measured against."),

        new("PlayerMessage.ReadAtUtc is never written", "M9-04",
            "28 A3's schema declares readAtUtc and M5-05's table carries the column, and nothing in " +
            "this build ever sets it: there is no read command in 14 §2.3's frozen 52-row registry " +
            "and no screen to send one from. 🔴 S37 Inbox HAS NO TRACKER ROW AT ALL — S33-S36 are " +
            "M14-06's and S38 is M16-03's, and S37 falls between them unclaimed. ⚠️ NEAREST ROW: " +
            "M9-04 is the settings/profile screens task, the closest owner of a surface a player " +
            "reads their own account facts on. Whoever adds the S37 row should take this with it."),

        new("The mail_read telemetry name", "M9-04",
            "28 A6 requires it and nothing can emit it: it is the read timestamp above, seen from " +
            "the analytics side. Same owner and the same ⚠️ caveat — S37 has no tracker row."),

        new("The mail_received telemetry name", "M16-01",
            "28 A6 requires it. A message is written by ops tooling today, which is a console app " +
            "with no analytics sink and no player session, so the only honest emitter is a " +
            "server-side sender. M16-01 builds the first one — its row reads 'ACCOUNT inbox " +
            "emitters' — and that is the commit where a received message becomes a fact the server " +
            "produces rather than one an operator types."),

        new("The mail_segment_sent telemetry name", "M18-05",
            "28 A6 requires it, and 28 A6 ALSO requires the audit log — which this task built, in " +
            "Postgres, with the predicate, both counts and the operator on the row. The tool is a " +
            "console app that composes a database and no analytics vendor; wiring PostHog into it " +
            "would put a vendor SDK in an ops tool to duplicate a record that is already durable and " +
            "queryable. M18-05 staffs the ops surfaces and is where a live ops dashboard first has a " +
            "consumer for it."),

        new("28 A2's 'a message with no translation does not send'", "M18-04",
            "🔴 THE DOC LINE IS STALE AND IS FLAGGED RATHER THAN IGNORED. It reads 'every template " +
            "exists in EN and DE; a message with no translation does not send', and the " +
            "localisation ruling makes EN the authored source with every DE value an untranslated " +
            "placeholder awaiting a NAMED human localiser. A DE-parity send gate would therefore " +
            "refuse every message this game can currently send. What IS implemented is the half " +
            "still true: the template must resolve in the SHIPPING locale, checked at send time. DE " +
            "parity is not dropped — CheckLocaleParity holds it over the whole content set before " +
            "the server starts. ⚠️ NEAREST ROW: M18-04 is the localisation row, and the commit that " +
            "fills the sentinels is the one that has to name the human who did."),

        // ⚠️ "The claim's stamp is not inside the accepted command's transaction" was here, owned by
        // M5-04. The integration merge that brought this branch onto the shipped commit rule CLOSED
        // it, and this register FORCED the deletion in that commit rather than leaving it to be
        // remembered: M5-04 had shipped, so Every_inbox_absence_owner_is_a_task_the_tracker_still_
        // has_open went red naming this entry. The stamp now rides CommandCommit.Claim inside the
        // one transaction, beside the snapshot whose wallet the same claim moved, and
        // IUnitOfWorkContractTests states both directions of that over a fault it can raise.
    };

    /// <summary>
    /// The attachment kinds this register claims are refused, paired with the refusal each must
    /// still answer. The <b>satisfied</b> direction: a kind that becomes grantable turns this red on
    /// the commit that grants it, which is the commit that has to delete its entry.
    /// </summary>
    private static readonly (string Type, MailAttachmentRefusal Refusal)[] StillRefused =
    {
        ("CHEST", MailAttachmentRefusal.CONTAINER_SHELF_ABSENT),
        ("EGG", MailAttachmentRefusal.CONTAINER_SHELF_ABSENT),
        ("CRATE", MailAttachmentRefusal.CONTAINER_SHELF_ABSENT),
        ("GEAR", MailAttachmentRefusal.SEEDED_GEAR_GRANT_ABSENT),
        ("SET_TOKENS", MailAttachmentRefusal.NO_WALLET_HOME),
        ("BEAST_MARKS", MailAttachmentRefusal.NO_WALLET_HOME),
        ("GOLD", MailAttachmentRefusal.RUN_SCOPED_CURRENCY),
    };

    /// <summary>
    /// The segment fields this register claims cannot be answered. Same <b>satisfied</b> direction:
    /// a field that becomes answerable turns this red on the commit that answers it.
    /// </summary>
    private static readonly MailSegmentField[] StillUnanswerable =
    {
        MailSegmentField.PLUS_ACTIVE,
        MailSegmentField.REGION,
        MailSegmentField.CLIENT_VERSION,
        MailSegmentField.GUILD_MEMBERSHIP,
        MailSegmentField.AFFECTED_RUN_WINDOW,
    };

    /// <summary>
    /// The register's floor (steering S3). Below it, every direction here reports success over a
    /// register that has quietly stopped carrying most of `28` Part A.
    /// </summary>
    /// <remarks>
    /// 17 → 16 at the M5-08 integration merge, and it is a gap CLOSING rather than a floor being
    /// lowered to buy green: the claim's stamp now rides the accepted command's transaction, so the
    /// thing `28` Part A asked for is built and its entry had to go. The number is what counting
    /// Part A's remaining absences answers, never <c>Absences.Length</c>.
    /// </remarks>
    private const int AbsenceFloor = 16;

    /// <summary>A tracker task id, the register's owner currency — the same shape the others use.</summary>
    private static readonly Regex OwnerTaskId = new(@"^M\d{1,2}-\d{2}[a-z]?$", RegexOptions.Compiled);

    /// <summary>
    /// 🔒 `23` §6's well-formedness: every entry names a task-shaped owner and a reason worth
    /// falsifying, and no subject is registered twice.
    /// </summary>
    [Fact]
    public void Every_inbox_absence_is_well_formed()
    {
        var offenders = new List<string>();

        foreach (var (subject, owner, why) in Absences)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                offenders.Add($"an entry owned by '{owner}' names no subject.");
            }

            if (!OwnerTaskId.IsMatch(owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{subject}' names owner '{owner}', which is not a tracker TASK id (M5-05, " +
                    "M18-06a). The owner-status parser reads task rows only, so any other shape is " +
                    "reported as an owner nobody declares — loud, but pointing at the parser.");
            }

            if (string.IsNullOrWhiteSpace(why) || why.Length < 80)
            {
                offenders.Add(
                    $"'{subject}' carries no written reason worth falsifying. Every entry here is a " +
                    "claim that a sentence in 28 Part A cannot be honoured today, and it has to say " +
                    "WHY and WHERE the thing it needs first exists, or the next kickoff cannot check " +
                    "it.");
            }
        }

        offenders.AddRange(
            Absences.GroupBy(a => a.Subject, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"'{g.Key}' is registered {g.Count()} times — two owners for one " +
                                 "absence is two answers to who closes it, and deleting either " +
                                 "reads as the other keeping the promise."));

        ArchRule.Empty(
            offenders,
            "Every inbox absence is well formed: task-shaped owner, written reason, no duplicates (23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §6 / steering S4's M4 amendment: every owner is a tracker task that is still <b>open</b>.
    /// An entry whose owner shipped can never expire — nothing would delete it and no kickoff would
    /// be asked.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="PortCatalogue.TrackerStatuses"/> and
    /// <see cref="PortCatalogue.OwnersNoLongerOpen"/>: one owner-status mechanism for the whole
    /// repository, including its parser's anchor floors, which <c>PortCatalogueTests</c> holds.
    /// </remarks>
    [Fact]
    public void Every_inbox_absence_owner_is_a_task_the_tracker_still_has_open() =>
        ArchRule.Empty(
            PortCatalogue.OwnersNoLongerOpen(
                Absences.Select(a => (Subject: "inbox absence '" + a.Subject + "'", a.Owner)),
                PortCatalogue.TrackerStatuses(RepoLayout.TrackerText())),
            "Every inbox absence owner is a tracker task that is still open (steering S4).");

    /// <summary>
    /// 🔒 `28` A2's attachment list, held by `23` §6's satisfied direction: an entry that says a kind
    /// is refused fails the moment the kind is grantable.
    /// </summary>
    /// <remarks>
    /// This is what makes the register expire by itself rather than by somebody remembering. The
    /// commit that lands the container shelf and starts granting a CHEST attachment turns this red
    /// naming the type, and deleting the entry is forced in that commit.
    /// </remarks>
    [Fact]
    public void Every_registered_attachment_kind_is_still_refused_by_the_named_reason() =>
        ArchRule.Empty(
            StillRefused
                .Select(pair => (pair.Type, pair.Refusal,
                    Actual: MailAttachmentKinds.Resolve(new MailAttachment(pair.Type, 1)).Refusal))
                .Where(row => row.Actual != row.Refusal)
                .Select(row =>
                    $"'{row.Type}' is registered as refused with {row.Refusal} and now answers " +
                    $"{(row.Actual is null ? "a GRANT" : row.Actual.ToString())}. If the kind became " +
                    "grantable, delete its InboxAbsence entry in the same commit — and re-read the " +
                    "held-for-inventory-space entry, whose whole reason is that no kind grants an " +
                    "item. If the REASON changed, the entry has to say the new one.")
                .ToArray(),
            "Every registered attachment kind is still refused, by the reason the register names (steering S4).");

    /// <summary>
    /// 🔒 `28` A3.1's predicate, held by the same `23` §6 direction: a field that becomes answerable
    /// expires its entry on the commit that answers it.
    /// </summary>
    [Fact]
    public void Every_registered_segment_field_is_still_unanswerable() =>
        ArchRule.Empty(
            StillUnanswerable
                .Where(MailSegmentFields.IsAnswerable)
                .Select(field =>
                    $"'{field}' is registered as unanswerable and the selector now answers it. The " +
                    "absence is closed: delete its InboxAbsence entry in the commit that made the " +
                    "fact storable.")
                .ToArray(),
            "Every registered segment field is still one no stored profile can answer (steering S4).");

    /// <summary>
    /// 🔒 `23` §6 / steering <b>S3</b>: the register has a floor, and the two satisfied directions
    /// have identity floors under them.
    /// </summary>
    /// <remarks>
    /// Every rule above is of the shape "no member of set S fails X" and is satisfied trivially by an
    /// empty S. The counts are literals from counting `28` Part A, never the lists' own
    /// <c>Length</c> — which cannot notice a list being trimmed.
    /// </remarks>
    [Fact]
    public void The_inbox_absence_register_has_a_floor()
    {
        var offenders = new List<string>();

        if (Absences.Length < AbsenceFloor)
        {
            offenders.Add(
                $"the register holds {Absences.Length} absences and the floor is {AbsenceFloor}. " +
                "Below it, every direction in this file reports success over a register that has " +
                "quietly stopped carrying most of 28 Part A. An absence CLOSED is a deletion with a " +
                "commit behind it — lower the floor there, deliberately.");
        }

        if (StillRefused.Length < 7)
        {
            offenders.Add(
                $"{StillRefused.Length} attachment kinds are checked and 28 A2 names seven this " +
                "build refuses. A shorter list is a kind that could become grantable with its " +
                "register entry still standing.");
        }

        if (StillUnanswerable.Length < 5)
        {
            offenders.Add(
                $"{StillUnanswerable.Length} segment fields are checked and 28 A3.1 names five this " +
                "build cannot answer. A shorter list is a field that could become answerable with " +
                "its entry still standing.");
        }

        ArchRule.Empty(offenders, "The inbox absence register and its satisfied directions have floors (steering S3).");
    }
}
