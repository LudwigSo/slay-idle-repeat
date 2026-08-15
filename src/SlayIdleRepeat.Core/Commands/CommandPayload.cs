using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Commands;

/// <summary>Shared rulings behind every command payload, plus the list helpers three commands need for value equality.</summary>
/// <remarks>
/// <para>
/// Payload fields carry the shape they already have on the wire rather than inventing new id
/// types: an index into a server-issued list is <see cref="int"/>, an opaque identifier is
/// <see cref="string"/>, and an authored optionality is a nullable reference. No new id primitive
/// or enum is declared here, since <c>Commands/</c> sits above <c>Primitives/</c> in the dependency
/// chain and the id vocabularies those types would describe have no authored content yet to check
/// them against.
/// </para>
/// <para>
/// Bounds named in payload remarks (e.g. an index range) are transcribed, not enforced by the
/// constructor: legality is <c>GameRules.Apply</c>'s job, and a command that refused to construct
/// could never be handed back to the player as a rejection.
/// </para>
/// <para>
/// The list helpers below exist because a record's synthesized equality compares an
/// <c>IReadOnlyList&lt;string&gt;</c> member by reference, not by value — without them, the few
/// commands that carry one would silently break <see cref="GameCommand"/>'s value-equality promise.
/// </para>
/// </remarks>
internal static class CommandPayload
{
    /// <summary>The doc anchor every command's <c>PrintMembers</c> inherits: render with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <remarks>
    /// A record's synthesized <c>PrintMembers</c> formats through <c>StringBuilder.Append(object)</c>
    /// on a boxed value, which uses the ambient culture and evades automated culture-sensitivity
    /// checks. Declared per command rather than once on the base type, since each derived record
    /// synthesizes its own <c>PrintMembers</c>.
    /// </remarks>
    internal const string PrintMembersContract =
        "Renders with CultureInfo.InvariantCulture (14 §8.2). See CommandPayload.PrintMembersContract.";

    /// <summary>
    /// A payload list rendered for <c>ToString()</c>: <c>[a, b]</c>, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Without it a list member renders as
    /// <c>System.Collections.ObjectModel.ReadOnlyCollection`1[System.String]</c> — the wrapper's
    /// type name, and none of the ids, which are the only thing a rejection diagnostic wants.
    /// </remarks>
    /// <param name="values">The list, or null.</param>
    /// <returns>The rendered list.</returns>
    internal static string Text(IReadOnlyList<string>? values) =>
        values is null ? "null" : "[" + string.Join(", ", values) + "]";

    /// <summary>A defensive, read-only copy of a payload list — never the caller's own array.</summary>
    /// <remarks>
    /// A copied array wrapped in <see cref="Array.AsReadOnly{T}(T[])"/>, not the caller's array cast
    /// to a read-only interface — the latter can be cast back and mutated, which would let a
    /// command's contents change after construction while its idempotency key described the original.
    /// </remarks>
    /// <param name="values">The caller's list.</param>
    /// <param name="parameter">The parameter name to blame when it is null.</param>
    /// <returns>A snapshot of <paramref name="values"/> that nothing outside this command can write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    internal static IReadOnlyList<string> Copy(IReadOnlyList<string>? values, string parameter)
    {
        if (values is null)
        {
            throw new ArgumentNullException(
                parameter,
                "This payload list is required by 14 §2.3 and null is not one of its values. A list " +
                "that is legitimately absent is declared nullable on the command that has one " +
                "(CLAIM_INBOX's messageIds[]?), where the absence MEANS something.");
        }

        var copy = new string[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i];
        }

        return new ReadOnlyCollection<string>(copy);
    }

    /// <summary>The same optional list, preserving a meaningful <c>null</c>.</summary>
    /// <remarks><c>null</c> and an empty list are not collapsed — the command must not lie about what the client sent.</remarks>
    /// <param name="values">The caller's list, or null.</param>
    /// <param name="parameter">The parameter name to blame.</param>
    /// <returns>A read-only copy, or null.</returns>
    internal static IReadOnlyList<string>? CopyOptional(IReadOnlyList<string>? values, string parameter) =>
        values is null ? null : Copy(values, parameter);

    /// <summary>Whether two payload lists carry the same ids in the same order, ordinally.</summary>
    /// <remarks>
    /// Ordinal, because these are wire identifiers compared byte for byte everywhere else in this
    /// repository — a culture-aware comparison here would make two commands equal on one host and
    /// unequal on another.
    /// </remarks>
    /// <param name="left">One list, or null.</param>
    /// <param name="right">The other, or null.</param>
    /// <returns>True when both are null, or both carry the same sequence.</returns>
    internal static bool SameIds(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>An order-sensitive ordinal hash of a payload list, consistent with <see cref="SameIds"/>.</summary>
    /// <param name="values">The list, or null.</param>
    /// <returns>A hash code equal for any two lists <see cref="SameIds"/> accepts.</returns>
    internal static int HashIds(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(values.Count);

        foreach (var value in values)
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
