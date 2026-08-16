using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Events;

/// <summary>One pity counter moved, and what it now reads.</summary>
/// <param name="Sequence">The event's ordinal within one <c>Apply</c> call's list — see <see cref="DomainEvent"/>.</param>
/// <param name="Key">
/// Which counter moved. A content-derived id formed in exactly one place — <c>LuckTuning</c>, the
/// reader of the authored counter keys — as <c>"&lt;counterKey&gt;:&lt;guaranteeRarity&gt;"</c>,
/// e.g. <c>chest.standard:A</c>. Never null, empty or whitespace.
/// </param>
/// <param name="Value">The counter's value <em>after</em> the advance, not the delta.</param>
/// <remarks>
/// <para>
/// Pity counters are player-visible and must never silently reset, so every movement of one is
/// emitted rather than inferred: the counter a client shows and the counter the server holds are
/// reconciled from this stream, and a reset that produced no event would be indistinguishable from
/// a counter that never advanced.
/// </para>
/// <para>
/// <see cref="Value"/> is the resulting value rather than a delta because the two movements that
/// matter are not both increments — a guarantee firing sets the counter to zero, and a delta of
/// <c>-159</c> is a worse description of that than a value of <c>0</c>.
/// </para>
/// <para>
/// The payload carries no source class. A single class can run several counters at once, so the
/// class does not identify one; the key does, and the reader that formed the key is the one thing
/// that can map it back.
/// </para>
/// <para>
/// The key is a string rather than an enum because the counter vocabulary is authored data: a
/// fourth rung on a ladder is a tuning edit, and an enum here would make it a code edit in the one
/// system whose whole purpose is to be re-tuned.
/// </para>
/// </remarks>
public sealed record PityCounterAdvanced(int Sequence, string Key, int Value)
    : DomainEvent(Sequence)
{
    /// <summary>Which counter moved. Never null, empty or whitespace.</summary>
    /// <remarks>
    /// Get-only rather than the positional <c>init</c> property, on <c>CurrencyChanged.Reason</c>'s
    /// precedent: an <c>init</c> accessor is assignable through <c>with</c> without re-running the
    /// property initialiser, so <c>event with { Key = "" }</c> would otherwise produce an
    /// unattributable counter movement through a validated type.
    /// </remarks>
    public string Key { get; } = RequireKey(Key);

    /// <summary>Renders this event with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    protected override bool PrintMembers(StringBuilder builder)
    {
        base.PrintMembers(builder);

        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Key)} = {Key}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Value)} = {Value}");

        return true;
    }

    /// <summary>The guard behind <see cref="Key"/>. Throws rather than substituting a placeholder.</summary>
    /// <param name="key">The candidate key.</param>
    /// <returns>The key, unchanged and untrimmed.</returns>
    /// <exception cref="ArgumentException">The key is null, empty or whitespace.</exception>
    private static string RequireKey(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException(BlankKey, nameof(Key))
            : key;

    private const string BlankKey =
        "A PityCounterAdvanced names the counter that moved. The key is formed in exactly one place " +
        "— LuckTuning, out of the authored counterKey and the guarantee rarity it protects — and an " +
        "event with no key cannot be reconciled against the counter map it describes, cannot tell a " +
        "reset from an advance, and cannot be attributed to a source class. Form the key through " +
        "LuckTuning; do not substitute a placeholder.";
}
