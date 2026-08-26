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

    /// <summary>The whole emitted vocabulary, for rules that reason over the set.</summary>
    public static readonly IReadOnlyList<string> Emitted =
        [RunStart, RunEnd, SessionStart, DieRolled, CurrencyChanged];
}
