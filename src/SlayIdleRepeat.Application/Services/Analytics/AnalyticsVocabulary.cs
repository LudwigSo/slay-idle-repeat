namespace SlayIdleRepeat.Application.Services.Analytics;

/// <summary>
/// Every analytics event name this application emits. Closed: a name not listed here is never
/// handed to the analytics port.
/// </summary>
/// <remarks>
/// Each name has a real production emission path — a command the gateway accepts or a domain event
/// the rules emit — and a fact proving a real input produces it. Names that are authored but not
/// yet emittable live in the architecture suite's register, each with an open owner task, not here.
/// </remarks>
public static class AnalyticsVocabulary
{
    /// <summary>An accepted <c>START_RUN</c>.</summary>
    public const string RunStart = "run_start";

    /// <summary>An accepted <c>END_RUN</c>.</summary>
    public const string RunEnd = "run_end";

    /// <summary>An accepted <c>BEGIN_SESSION</c>.</summary>
    public const string SessionStart = "session_start";

    /// <summary>A die decided a move — rolled or fixed, told apart by the <c>source</c> property.</summary>
    public const string DieRolled = "die_rolled";

    /// <summary>A currency moved, with the domain's own reason.</summary>
    public const string CurrencyChanged = "currency_changed";

    /// <summary>One inbox message's attachments were granted by an accepted <c>CLAIM_INBOX</c>.</summary>
    public const string MailClaimed = "mail_claimed";

    /// <summary>One expiring message's unclaimed attachments were auto-granted by the nightly sweep.</summary>
    /// <remarks>
    /// The one name here that is not produced by the accepted-command stream: the sweep is a hosted
    /// job, so it tracks through the port itself. What makes that honest rather than a widening is
    /// that the job is a real production path with a real player id — which is the whole test the
    /// unemittable register applies to every other name.
    /// </remarks>
    public const string MailExpiredAutogranted = "mail_expired_autogranted";

    /// <summary>The whole emitted vocabulary, for rules that reason over the set.</summary>
    public static readonly IReadOnlyList<string> Emitted =
        [RunStart, RunEnd, SessionStart, DieRolled, CurrencyChanged, MailClaimed, MailExpiredAutogranted];
}
