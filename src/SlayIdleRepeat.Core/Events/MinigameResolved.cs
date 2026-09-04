using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>One minigame resolved, and the outcome tier it actually resolved to.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="MinigameId">Which minigame. One of the four authored <c>MG_*</c> ids. Never null, empty or whitespace.</param>
/// <param name="Tier">The zero-based outcome tier the reward rows were paid from.</param>
/// <param name="Outcome">The authored outcome token of that tier. Never null, empty or whitespace.</param>
/// <remarks>
/// <para>
/// 🔒 <b>The only honest way a client learns a server-rolled tier.</b> Two of the four minigames draw
/// their outcome on the server and ignore the client's claim, and no persisted field carries the
/// number that was drawn — so without this event a screen showing "you won the gold chest" would be
/// showing what it hoped for rather than what it was paid. The reward rows travel as
/// <c>CurrencyChanged</c>, which says how much moved and never says which row it came from.
/// </para>
/// <para>
/// <see cref="Outcome"/> is carried beside <see cref="Tier"/> rather than left to be looked up. The
/// tier is an index into a table that a content edit can re-order, and an event that named only the
/// index would describe a different outcome after such an edit than the one that was paid.
/// </para>
/// <para>
/// 🔒 <b>No feat counter is advanced from this event, and that is a decision rather than an
/// oversight.</b> <c>FeatCounterProjection</c>'s remarks claim a coverage rule enforces an arm per
/// event type; no such rule exists in this repository, so the choice not to count minigame
/// resolutions is stated here instead of left to a gate that would never fire.
/// </para>
/// </remarks>
public sealed record MinigameResolved(int Sequence, string MinigameId, int Tier, string Outcome)
    : DomainEvent(Sequence)
{
    /// <summary>Which minigame resolved. Never null, empty or whitespace.</summary>
    /// <remarks>
    /// Get-only rather than the positional <c>init</c> property, on <c>PityCounterAdvanced.Key</c>'s
    /// precedent: an <c>init</c> accessor is assignable through <c>with</c> without re-running the
    /// property initialiser, so <c>event with { MinigameId = "" }</c> would otherwise produce an
    /// unattributable resolution through a validated type.
    /// </remarks>
    public string MinigameId { get; } = RequireMinigameId(MinigameId);

    /// <summary>The authored outcome token. Never null, empty or whitespace.</summary>
    /// <remarks>Get-only for the reason <see cref="MinigameId"/> is.</remarks>
    public string Outcome { get; } = RequireOutcome(Outcome);

    /// <summary>Renders this event with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);

        builder.Append(CultureInfo.InvariantCulture, $", {nameof(MinigameId)} = {MinigameId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Tier)} = {Tier}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Outcome)} = {Outcome}");

        return true;
    }

    /// <summary>The guard behind <see cref="MinigameId"/>. Throws rather than substituting a placeholder.</summary>
    /// <param name="minigameId">The candidate id.</param>
    /// <returns>The id, unchanged and untrimmed.</returns>
    /// <exception cref="ArgumentException">The id is null, empty or whitespace.</exception>
    private static string RequireMinigameId(string minigameId) =>
        string.IsNullOrWhiteSpace(minigameId)
            ? throw new ArgumentException(BlankMinigameId, nameof(MinigameId))
            : minigameId;

    /// <summary>The guard behind <see cref="Outcome"/>.</summary>
    /// <param name="outcome">The candidate token.</param>
    /// <returns>The token, unchanged and untrimmed.</returns>
    /// <exception cref="ArgumentException">The token is null, empty or whitespace.</exception>
    private static string RequireOutcome(string outcome) =>
        string.IsNullOrWhiteSpace(outcome)
            ? throw new ArgumentException(BlankOutcome, nameof(Outcome))
            : outcome;

    private const string BlankMinigameId =
        "A MINIGAME_SUBMIT resolution names the minigame it resolved. The id is the command's own " +
        "and the catalogue has already accepted it by the time this event is raised, so a blank one " +
        "is a handler that lost the catalogue rather than a client that sent nothing — and a screen " +
        "keyed on the id it opened would ignore the only answer it is ever given.";

    private const string BlankOutcome =
        "A resolution names the authored outcome token of the tier it paid. The token is read out of " +
        "the reward table beside the tier, because the tier alone is an index a content edit can " +
        "reorder; a blank one is a handler that lost the reward table, and it leaves the results " +
        "panel with a tier number and no words for it.";
}
