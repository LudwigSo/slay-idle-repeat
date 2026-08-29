using System.Globalization;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>Who a send is addressed to.</summary>
public enum MailTargetKind
{
    /// <summary>Every candidate the operator supplied, with no predicate.</summary>
    ALL = 1,

    /// <summary>The candidates a predicate over the profile selects.</summary>
    SEGMENT = 2,

    /// <summary>Named players, and nobody else.</summary>
    PLAYER = 3,
}

/// <summary>
/// 🔒 The closed v1 predicate vocabulary, and every field the design set names that this build
/// cannot answer.
/// </summary>
/// <remarks>
/// The design set writes a predicate over "region, client version, highest chapter, Plus status,
/// last-active window, guild membership, whether they were in an affected run window". Two of those
/// are answerable from a stored profile today. The rest are named here as refusals rather than
/// silently unsupported, because a predicate an operator writes and the tool ignores is a send that
/// reaches everybody.
/// </remarks>
public enum MailSegmentField
{
    /// <summary>The highest chapter the player has cleared on any tier. Derived from their profile.</summary>
    HIGHEST_CHAPTER = 1,

    /// <summary>How recently the player last had a command applied.</summary>
    LAST_ACTIVE_WITHIN = 2,

    /// <summary>
    /// ❌ Plus status. No subscription state is stored anywhere: entitlement is resolved onto the
    /// SESSION by the composition root, and every host resolves it as "no subscription resolved".
    /// The store-subscription webhooks are what first make it a fact about an account.
    /// </summary>
    PLUS_ACTIVE = 3,

    /// <summary>❌ Region. No player row, snapshot or profile carries one anywhere in this build.</summary>
    REGION = 4,

    /// <summary>
    /// ❌ Client version. It arrives on a <c>BEGIN_SESSION</c> and is not persisted, so the last one
    /// a player sent is not a fact any store can be asked for.
    /// </summary>
    CLIENT_VERSION = 5,

    /// <summary>❌ Guild membership. Guilds are not built; no player has one to be in.</summary>
    GUILD_MEMBERSHIP = 6,

    /// <summary>
    /// ❌ Whether the player was in an affected run window. It needs a per-run history that outlives
    /// the run, and a finished run is archived by identity rather than indexed by time.
    /// </summary>
    AFFECTED_RUN_WINDOW = 7,
}

/// <summary>How a predicate compares.</summary>
public enum MailSegmentOperator
{
    /// <summary>The value is at least the operand.</summary>
    AT_LEAST = 1,

    /// <summary>The value is at most the operand.</summary>
    AT_MOST = 2,
}

/// <summary>One clause of a segment predicate.</summary>
/// <param name="Field">Which fact about the player.</param>
/// <param name="Operator">How it compares.</param>
/// <param name="Operand">
/// What it compares against: a chapter number for <see cref="MailSegmentField.HIGHEST_CHAPTER"/>, a
/// whole number of days for <see cref="MailSegmentField.LAST_ACTIVE_WITHIN"/>.
/// </param>
public sealed record MailSegmentClause(
    MailSegmentField Field, MailSegmentOperator Operator, long Operand);

/// <summary>Whether a field can be asked about a stored profile at all, and what to say when it cannot.</summary>
public static class MailSegmentFields
{
    /// <summary>The fields a predicate may use.</summary>
    public static IReadOnlyList<MailSegmentField> Answerable { get; } = new[]
    {
        MailSegmentField.HIGHEST_CHAPTER,
        MailSegmentField.LAST_ACTIVE_WITHIN,
    };

    /// <summary>Whether a predicate may use this field.</summary>
    /// <param name="field">The field to judge.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a declared field.</exception>
    public static bool IsAnswerable(MailSegmentField field) =>
        field switch
        {
            MailSegmentField.HIGHEST_CHAPTER or MailSegmentField.LAST_ACTIVE_WITHIN => true,
            MailSegmentField.PLUS_ACTIVE
                or MailSegmentField.REGION
                or MailSegmentField.CLIENT_VERSION
                or MailSegmentField.GUILD_MEMBERSHIP
                or MailSegmentField.AFFECTED_RUN_WINDOW => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field), field,
                "A field was added to the segment vocabulary without being told whether a stored " +
                "profile can answer it. The default for a field nobody ruled on cannot be " +
                "'answerable' — an unanswered clause selects everybody."),
        };

    /// <summary>Why this field cannot be used, in words an operator can act on.</summary>
    /// <param name="field">The refused field.</param>
    /// <exception cref="ArgumentException">The field IS answerable, so there is no refusal to give.</exception>
    public static string RefusalFor(MailSegmentField field) =>
        field switch
        {
            MailSegmentField.PLUS_ACTIVE =>
                "no subscription state is stored: entitlement is resolved onto the session, and " +
                "every host in this build resolves it as 'no subscription resolved'. Store " +
                "subscription webhooks are what first make Plus a fact about an account.",
            MailSegmentField.REGION =>
                "no player row, snapshot or profile carries a region anywhere in this build.",
            MailSegmentField.CLIENT_VERSION =>
                "a client version arrives on BEGIN_SESSION and is not persisted, so the last one a " +
                "player sent is not a fact any store can be asked for.",
            MailSegmentField.GUILD_MEMBERSHIP =>
                "guilds are not built, so no player has one to be in.",
            MailSegmentField.AFFECTED_RUN_WINDOW =>
                "it needs a per-run history that outlives the run; a finished run is archived by " +
                "its own identity and indexed by nothing that a time window could be asked over.",
            _ => throw new ArgumentException(
                "'" + field + "' is answerable, so it has no refusal. Asking for one means a caller " +
                "refused a clause it should have evaluated.",
                nameof(field)),
        };
}

/// <summary>What a dry run found.</summary>
/// <param name="Candidates">How many profiles were examined.</param>
/// <param name="Recipients">Who the send would reach.</param>
/// <param name="Refusals">Why it cannot go, if it cannot. Empty means the send is well formed.</param>
public sealed record MailSegmentDryRun(
    int Candidates, IReadOnlyList<PlayerId> Recipients, IReadOnlyList<string> Refusals)
{
    /// <summary>Whether the send may proceed. Exactly "there are no refusals".</summary>
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>Selecting recipients: the dry run and the send read the same answer out of this.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Candidates are supplied, not enumerated.</b> No adapter in this repository lists player
/// rows — the same absence the plausibility sweep records — so <see cref="MailTargetKind.ALL"/> is
/// "all of the candidates the operator handed over", never "all of the players there are". A tool
/// that pretended otherwise would report a recipient count of zero for a send meant to reach
/// everybody, which is worse than saying so.
/// </para>
/// <para>
/// 🔒 <b>A predicate reads the stored profile through the vocabulary that WROTE it.</b> The highest
/// chapter a player has cleared is derived by <c>ChapterClearance</c>, the same primitive the
/// aggregate records those keys with — so the format exists once rather than here as well, where a
/// second copy would go on deciding who a compensation grant reaches right up until it drifted.
/// </para>
/// </remarks>
public static class MailSegmentSelection
{
    /// <summary>Who a target and its predicate select out of the supplied candidates.</summary>
    /// <param name="target">Which addressing mode.</param>
    /// <param name="clauses">The predicate. Empty for <see cref="MailTargetKind.ALL"/> and <see cref="MailTargetKind.PLAYER"/>.</param>
    /// <param name="candidates">The profiles to judge, as the operator supplied them.</param>
    /// <param name="asOfUtc">The instant a last-active window is measured back from.</param>
    /// <returns>The recipients, and every reason the send may not go.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static MailSegmentDryRun Select(
        MailTargetKind target,
        IReadOnlyList<MailSegmentClause> clauses,
        IReadOnlyList<PlayerProfile> candidates,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(candidates);

        var refusals = new List<string>();

        if (target != MailTargetKind.SEGMENT && clauses.Count > 0)
        {
            refusals.Add(
                "a " + target + " send carries " +
                clauses.Count.ToString(CultureInfo.InvariantCulture) +
                " predicate clause(s). Only a SEGMENT send has a predicate, and silently ignoring " +
                "one would send to everybody the operator listed rather than to the subset they " +
                "described.");
        }

        if (target == MailTargetKind.SEGMENT && clauses.Count == 0)
        {
            refusals.Add(
                "a SEGMENT send carries no predicate, which selects every candidate. If that is the " +
                "intent, say it with ALL — an empty predicate reads as a clause somebody forgot.");
        }

        foreach (var clause in clauses.Where(c => !MailSegmentFields.IsAnswerable(c.Field)))
        {
            refusals.Add(
                "'" + clause.Field + "' cannot be answered: " +
                MailSegmentFields.RefusalFor(clause.Field));
        }

        if (refusals.Count > 0)
        {
            return new MailSegmentDryRun(candidates.Count, Array.Empty<PlayerId>(), refusals);
        }

        var recipients = candidates
            .Where(profile => clauses.All(c => Matches(c, profile.Player, asOfUtc)))
            .Select(profile => profile.Player.Id)
            .ToArray();

        return new MailSegmentDryRun(candidates.Count, recipients, Array.Empty<string>());
    }

    /// <summary>The predicate as an operator wrote it, for the audit row.</summary>
    /// <param name="target">Which addressing mode.</param>
    /// <param name="clauses">The predicate.</param>
    /// <returns>A stable one-line rendering.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is null.</exception>
    /// <remarks>
    /// Rendered invariantly and in the order written: the audit row's whole purpose is that somebody
    /// later can read what the predicate was, and a rendering that varied by host culture would make
    /// two records of the same send disagree.
    /// </remarks>
    public static string Describe(MailTargetKind target, IReadOnlyList<MailSegmentClause> clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        return clauses.Count == 0
            ? target.ToString()
            : target + ": " + string.Join(
                " AND ",
                clauses.Select(c => string.Create(
                    CultureInfo.InvariantCulture, $"{c.Field} {c.Operator} {c.Operand}")));
    }

    private static bool Matches(MailSegmentClause clause, PlayerSnapshot player, DateTimeOffset asOfUtc)
    {
        var value = clause.Field switch
        {
            MailSegmentField.HIGHEST_CHAPTER =>
                ChapterClearance.HighestClearedIn(player.ClearedChapterTiers?.Keys),
            MailSegmentField.LAST_ACTIVE_WITHIN => (long)Math.Ceiling(
                Math.Max(0d, (asOfUtc - player.LastAppliedAtUtc).TotalDays)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(clause), clause.Field,
                "An unanswerable field reached the evaluator. Select refuses those before it judges " +
                "a single candidate, so reaching here means that gate was walked around."),
        };

        return clause.Operator switch
        {
            MailSegmentOperator.AT_LEAST => value >= clause.Operand,
            MailSegmentOperator.AT_MOST => value <= clause.Operand,
            _ => throw new ArgumentOutOfRangeException(
                nameof(clause), clause.Operator,
                "An operator was added to the vocabulary without being given a comparison, so every " +
                "clause using it would select nobody or everybody without saying which."),
        };
    }
}

/// <summary>One economy-affecting send, as the audit log records it.</summary>
/// <param name="SentAtUtc">When it was executed.</param>
/// <param name="Operator">
/// ⚠️ Who ran it, as an UNVERIFIED string. There is no operator identity system anywhere in this
/// repository, so this is what the person at the terminal typed — required, recorded, and worth
/// exactly what a self-declared name is worth. Said here, at the reader that stores it, and again at
/// the tool that asks for it.
/// </param>
/// <param name="Predicate">The predicate as written, so a later reader can re-run it.</param>
/// <param name="TemplateId">Which template was sent.</param>
/// <param name="Attachments">What every copy carried, rendered for the record.</param>
/// <param name="DryRunCount">How many recipients the dry run reported.</param>
/// <param name="ActualCount">How many messages were actually written.</param>
public sealed record MailSegmentSend(
    DateTimeOffset SentAtUtc,
    string Operator,
    string Predicate,
    string TemplateId,
    string Attachments,
    int DryRunCount,
    int ActualCount)
{
    /// <summary>Renders a send's attachments for the audit row.</summary>
    /// <param name="attachments">What the send carried.</param>
    /// <returns>A stable, invariant one-line rendering. <c>"none"</c> when it carried nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attachments"/> is null.</exception>
    public static string Render(IReadOnlyList<MailAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        return attachments.Count == 0
            ? "none"
            : string.Join(
                ", ",
                attachments.Select(a => string.Create(
                    CultureInfo.InvariantCulture, $"{a.Type}x{a.Amount}")));
    }
}
