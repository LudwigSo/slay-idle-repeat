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
/// 🔒 <b>No id primitive, no payload enum, and the reason is not squeamishness.</b> `30` §11.4's
/// structure block puts <c>Primitives/</c> at the bottom of the dependency chain
/// (<c>Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives</c>) and <c>Commands/</c> above it, so a
/// <c>GearSlot</c> or a <c>GearInstanceId</c> declared for this task would land under everything
/// — and every milestone that actually reads the value would inherit a type it did not choose.
/// The repository already draws that line where it can be checked: M1-01 declared
/// <see cref="Primitives.CurrencyId"/> and <see cref="Primitives.DifficultyTier"/> because
/// <c>game-data/</c> already carried both <em>value sets</em> and the two halves could be pinned
/// against each other from <c>Application.Tests</c>.
/// </para>
/// <para>
/// 🔒 <b>What is actually empty, stated precisely, because the blanket version of this sentence was
/// false and a review caught it.</b> <c>game-data/schema/</c> is <em>not</em> empty — it carries
/// <c>beasts.schema.json</c>, <c>forge.schema.json</c>, <c>drops.schema.json</c> and
/// <c>ads.schema.json</c> among others. What those schemas describe is <b>tuning</b>: costs,
/// odds, ladders, caps. The <b>id vocabularies</b> the payload column names live in
/// <c>game-data/content/</c>, and every directory there that matters here —
/// <c>gear/</c>, <c>pets/</c>, <c>mounts/</c>, <c>quests/</c>, <c>perks/</c>, <c>talents/</c> —
/// holds nothing but a <c>.gitkeep</c>. An id type declared today could be cross-checked against
/// no instance of itself, which is the drift M1-01's cross-checks exist to prevent.
/// </para>
/// <para>
/// ⚠️ <b>One field is a genuine exception and is named rather than covered by the blanket.</b>
/// <c>CLAIM_AD_REWARD</c>'s <c>placementId</c> <em>could</em> be typed today:
/// <c>game-data/tuning/ads.json</c> is <c>"_status": "transcribed"</c> and carries exactly the 29
/// <c>AD_*</c> ids of `12` §4, and <c>ads.schema.json</c> pins the <c>^AD_[A-Z0-9_]+$</c> shape —
/// M1-01's bar is met. It is still left as text, and the reason is the narrower one: <b>M15-03</b>
/// authors the cap engine over that file <em>and</em> the S2S callback record the grant is
/// checked against, so the type belongs in the commit that first reads it. That is a scheduling
/// judgement, not an impossibility, and it is written here so a later reader can overturn it
/// without first having to discover that the general argument did not apply.
/// </para>
/// <para>
/// ⚠️ <b>Bounds are transcribed and not enforced</b>, and that is the same ruling in the other
/// direction. `14` §2.3 authors <c>slotIndex: 0–2</c> (`07` §2's three pet slots at Legend Level
/// 5/15/30) and <c>lockedAffixIds[≤3]</c>; both are recorded in the relevant command's remarks and
/// neither is a constructor guard. Legality is <c>GameRules.Apply</c>'s (`30` §2.1's <b>P3</b>:
/// every command on every state returns a <em>result</em>), and a command that refuses to be
/// constructed cannot be handed back to the player as `14` §16.2's rejection envelope at all — the
/// server would have nothing to reject. `presetSlot`, by contrast, has <b>no</b> authored bound:
/// `16` <b>O11</b> leaves the preset count open — <em>"3 free slots is a guess. Raise the allowance
/// if telemetry shows players capped; never gate it further"</em>, due <em>"after first
/// playtest"</em> — and <c>IMPLEMENTATION_TRACKER.md</c> is what routes it to the <b>M9 kickoff</b>
/// (with an M18 re-review). So a bound here would be invention rather than transcription.
/// </para>
/// <para>
/// 🔒 <b>What this file does <em>not</em> settle.</b> `14` §2.3 has two columns and M1-02 discharged
/// the first: its <b>inventory</b> is complete and transcribed into
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister.Surfaces</c>. Its <b>payload column</b> is
/// deferred, and only partly watched. Two of the deferrals expire mechanically, because an existing
/// register entry already fires on the right commit: <c>Inventory</c> (M4-03, keyed on
/// <c>GearInstance</c>) covers the six forge-and-gear payloads, and <c>ContainerShelf</c> (M4-02,
/// keyed on <c>ContainerClass</c>) covers the three container ids. The rest are carried by <b>this
/// remark and the per-command ones alone</b>: the two <c>logHash</c> fields (M2-15) — the minigame id
/// and its tier encoding were the same shape until M3-03c settled <c>Result</c> against `03` §6.1's
/// four reward tables, at <c>MinigameSubmitCommand</c>'s own remarks — the talent <c>nodeId</c>
/// (M4-06), <c>offerId</c> (M4-09),
/// <c>placementId</c> (M15-03), <c>messageIds</c> (M5-08), and <c>ghostId</c>/<c>duelId</c>
/// (M12-01/M12-04).
/// </para>
/// <para>
/// ⚠️ <b>The failure that is not mechanically caught is the one that costs.</b> M4-03 declaring
/// <c>GearSlot</c> while these commands keep <c>string</c> would leave two vocabularies for one
/// concept with every rule green — `30` §11.6's failure mode, one layer in.
/// <c>CommandVocabularyTests.Sample</c> throws on a payload type it does not recognise, but that
/// fires only in the harmless direction (someone retypes a field), never in this one.
/// <b>Whoever declares one of these types retypes its command in the same commit.</b>
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
    /// 🔒 Renders this command with <see cref="CultureInfo.InvariantCulture"/> (`14` §8.2) — the doc
    /// anchor every command's <c>PrintMembers</c> inherits, so the reason is written once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same defect M1-06 closed for `30` §7's events, in the sibling public hierarchy</b>
    /// (carried-forward item 9). A record's <em>synthesized</em> <c>PrintMembers</c> appends every
    /// member through <c>StringBuilder.Append(object)</c>, so the <c>ToString()</c> happens inside
    /// <c>StringBuilder</c> on a boxed value and <c>BannedApi.CultureViolations</c> never sees a
    /// call whose declaring type is <c>System.Int32</c>. Measured on the committed sources before
    /// this was written: <c>ChooseForkCommand { BranchIndex = -1 }</c> in the container renders
    /// <c>BranchIndex = −1</c> (U+2212 MINUS SIGN) under <c>sv-SE</c> and gains a U+061C prefix
    /// under <c>ar-EG</c> — one command, three strings, in three logs.
    /// </para>
    /// <para>
    /// 🔒 It matters <em>because</em> of the transcribe-not-enforce ruling above: a negative or
    /// out-of-range index is deliberately constructible, so it is exactly the value that reaches a
    /// `14` §16.2 rejection diagnostic. ⚠️ A single override on <see cref="GameCommand"/> cannot do
    /// this — each derived record synthesizes its own <c>PrintMembers</c> that appends its own
    /// members after <c>base.PrintMembers</c> — so the hook is declared per command, and
    /// <c>AmbientApiTests.Every_command_with_a_culture_sensitive_member_declares_an_invariant_PrintMembers</c>
    /// fails the build for a command that carries one and does not.
    /// </para>
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
    /// <param name="parameter">
    /// The parameter name to blame. ⚠️ It is <b>unreachable</b> today — the <c>null</c> arm returns
    /// before <see cref="Copy"/> can throw — and it is still threaded through rather than hard-coded,
    /// because the alternative (<c>nameof(values)</c>, this method's <em>own</em> parameter name)
    /// would be a name no caller has, waiting for someone to reach it.
    /// </param>
    /// <returns>A read-only copy, or null.</returns>
    internal static IReadOnlyList<string>? CopyOptional(IReadOnlyList<string>? values, string parameter) =>
        values is null ? null : Copy(values, parameter);

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
