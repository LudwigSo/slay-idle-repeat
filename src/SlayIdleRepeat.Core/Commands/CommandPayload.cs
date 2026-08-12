using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Commands;

/// <summary>
/// 🔒 The shared rulings behind every payload in this namespace, and the two helpers the three
/// list-carrying commands need to keep <see cref="GameCommand"/>'s value-equality promise.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What a command payload field is typed as, and why no new primitive was declared</b>
/// (M1-02, steering <b>S6</b>). `14` §2.3 is explicit that its payload column shows
/// <em>"shape and intent"</em> and that <em>"the field-level source of truth is the public
/// <c>GameCommand</c> subtypes in <c>Core/Commands</c>"</em> — these types. Almost none of the
/// identifiers it names has a declared type today, so the choice was between inventing twenty of
/// them and carrying the shape each one already has on the wire. The rule applied, uniformly:
/// </para>
/// <list type="table">
///   <item><term><b>An index into a server-issued list</b></term><description><see cref="int"/>,
///   and this is the <em>final</em> type rather than a placeholder. <c>branchIndex</c>,
///   <c>optionIndex</c>, <c>shopSlotIndex</c>, <c>choiceIndex</c>, <c>slotIndex</c>,
///   <c>questSlot</c>, <c>presetSlot</c>, <c>quantity</c> and <c>chapterId</c> address a position
///   in a list the server issued with the outcome before it; none of them will ever become an
///   identity type.</description></item>
///   <item><term><b>An opaque text identifier</b></term><description><see cref="string"/>. Every
///   one of them is already authored as text somewhere in the design set — `08` §7's gear rows
///   (<c>"instanceId": "a7f3…"</c>, <c>"slot": "WEAPON"</c>, <c>"family": "BLADE"</c>,
///   <c>"AFX_CRIT_CHANCE"</c>), `03` §6's <c>MG_*</c> minigames, `07` §2's <c>PET_*</c> beasts,
///   `12` §3's <c>AD_*</c> placements, `14` §16.6's <c>fnv1a:…</c> hashes. So the string is a
///   transcription of the authored wire shape, not a guess about a value space.</description></item>
///   <item><term><b>An authored optionality</b></term><description>A nullable reference, because
///   `14` §2.3 gives <c>petId?</c>, <c>mountId?</c>, <c>gearSlot?</c>, <c>family?</c> and
///   <c>messageIds[]?</c> a meaning when they are absent (unequip, clear, claim everything). The
///   nullability is payload semantics, not defensive coding.</description></item>
/// </list>
/// <para>
/// 🔒 <b>No id primitive, no payload enum, and the reason is not squeamishness.</b> `30` §11.4
/// puts <c>Commands</c> above <c>Primitives</c>, so a <c>GearSlot</c> or a <c>GearInstanceId</c>
/// declared for this task would land in <c>Core/Primitives/</c> — the bottom of the dependency
/// graph — and every milestone that actually reads the value would inherit a type it did not
/// choose. The repository already draws that line where it can be checked: M1-01 declared
/// <see cref="Primitives.CurrencyId"/> and <see cref="Primitives.DifficultyTier"/> because
/// <c>game-data/</c> already carried both vocabularies and the two halves could be pinned against
/// each other from <c>Application.Tests</c>. There is <b>no</b> gear, pet, mount, container,
/// offer, quest, preset, affix or minigame schema under <c>game-data/schema/</c> today, so an enum
/// declared here could be cross-checked against nothing — which is the drift those cross-checks
/// exist to prevent. The owning tasks are named on each command below.
/// </para>
/// <para>
/// ⚠️ <b>Bounds are transcribed and not enforced</b>, and that is the same ruling in the other
/// direction. `14` §2.3 authors <c>slotIndex: 0–2</c> (`07` §2's three pet slots at Legend Level
/// 5/15/30) and <c>lockedAffixIds[≤3]</c>; both are recorded in the relevant command's remarks and
/// neither is a constructor guard. Legality is <c>GameRules.Apply</c>'s (`30` §2.1's <b>P3</b>:
/// every command on every state returns a <em>result</em>), and a command that refuses to be
/// constructed cannot be handed back to the player as `14` §16.2's rejection envelope at all — the
/// server would have nothing to reject. `presetSlot`, by contrast, has <b>no</b> authored bound:
/// `16` <b>O11</b> defers the preset count to the M9 kickoff ("3 free is a guess; raise, never gate
/// further"), so a bound here would be invention rather than transcription.
/// </para>
/// <para>
/// 🔒 <b>The list helpers below exist because a record does not compare a list by value.</b>
/// <see cref="GameCommand"/>'s own remarks make value equality a contract — <em>"two commands
/// describing the same intent must compare equal"</em> for `14` §3.2's idempotency replay to mean
/// anything — and the synthesized <c>Equals</c> compares an <c>IReadOnlyList&lt;string&gt;</c>
/// member by <b>reference</b>. Three of the forty-nine carry one (<c>SALVAGE</c>,
/// <c>RETUNE_ITEM</c>, <c>CLAIM_INBOX</c>), and without these they would be the three commands for
/// which the base type's stated promise silently did not hold.
/// </para>
/// </remarks>
internal static class CommandPayload
{
    /// <summary>
    /// A defensive, read-only copy of a payload list — never the caller's own array.
    /// </summary>
    /// <remarks>
    /// <see cref="Array.AsReadOnly{T}(T[])"/> rather than the array behind an
    /// <c>IReadOnlyList&lt;string&gt;</c>: a bare array handed out through the read-only interface
    /// casts straight back to <c>string[]</c>, which is the hole M1-05 closed for the aggregates.
    /// A command whose contents a caller can rewrite after construction is a command whose
    /// idempotency key describes something other than what was applied.
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

    /// <summary>
    /// The same optional list, preserving a meaningful <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>null</c> and an empty list are <b>not</b> collapsed. `14` §2.3 writes
    /// <em>"omitted/empty = claim everything claimable"</em> for <c>CLAIM_INBOX</c>, so the two
    /// happen to mean the same thing <em>to that rule</em> — but collapsing them here would make
    /// the command lie about what the client sent, and `14` §16.3 replays a stored outcome against
    /// the command it was asked about.
    /// </remarks>
    /// <param name="values">The caller's list, or null.</param>
    /// <returns>A read-only copy, or null.</returns>
    internal static IReadOnlyList<string>? CopyOptional(IReadOnlyList<string>? values) =>
        values is null ? null : Copy(values, nameof(values));

    /// <summary>Whether two payload lists carry the same ids in the same order, ordinally.</summary>
    /// <remarks>
    /// Ordinal, because these are wire identifiers: `14` §2.3's ids are compared byte for byte
    /// everywhere else in this repository, and a culture-aware comparison here would make two
    /// commands equal on one host and unequal on another.
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
