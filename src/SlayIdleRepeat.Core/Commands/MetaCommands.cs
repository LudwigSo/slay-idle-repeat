using System.Globalization;
using System.Text;

namespace SlayIdleRepeat.Core.Commands;

// 🔒 `14` §2.3's META half — the 30 rows of "Meta commands", in the table's own order. The table's
// header reads "(29)"; it has always had 30 rows, and the M1 kickoff (2026-08-11) recorded the
// header and this repository's earlier "48 commands" as MISCOUNTS OF A CORRECT TABLE — errata, not
// a scope change. Counted again here before a line of this file was written: 19 + 30 = 49.
//
// The endpoint is `POST /player/command` with the sequence per player (`14` §16.3). Nine of the
// thirty are marked ⚄ in the table and draw from the command's server-issued seed; they are the
// nine `CommandSeedPin.SeedBearingMetaCommands` names, and each one says so below.

/// <summary>
/// 🔒 `14` §2.3 <c>BEGIN_SESSION</c> ⚄ — the server-acknowledged first contact of a session
/// <b>and</b> of each game day. It carries the calendar advance, the daily free Energy refill
/// (`10` §3) and the day's random draws — the quest slate (`19` B) and the Daily shop block
/// (`10` §5.1). Semantics: `30` §2.3.
/// </summary>
/// <remarks>
/// ⚄ <b>Its command seed is the day's draw seed</b> (`14` §8.1, `30` §3), so
/// <c>GameContext.CommandSeed</c> is non-null on this command and on the eight others marked ⚄.
/// It is the one command in the registry whose handler is authored inside M1 (<b>M1-09</b>); the
/// quest-slate and Daily-shop draws inside it are deferred to M4-09 by that task's own brief.
/// </remarks>
/// <param name="ClientVersion">The client build asking for the session.</param>
/// <param name="ContentHash">
/// The content version the client holds (`14` §6). Free-form text on the wire, and it stays text
/// here: <c>Content.ContentVersion</c> exists but is the <em>parsed</em> form, and a command that
/// refused to be constructed from a malformed hash could not be answered with `14` §16.2's
/// <c>CONTENT_VERSION_MISMATCH</c>.
/// </param>
public sealed record BeginSessionCommand(string ClientVersion, string ContentHash) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SKIP_FTUE</c> — valid only while <c>ftueProgress</c> is between beats 2 and 8;
/// grants the full scripted payout and jumps to beat 9 (`19` D6). Idempotent: a resend after
/// completion is a no-op.
/// </summary>
/// <remarks>
/// The beat range is `19` D6's and is <b>not</b> a constructor guard — the command carries no beat
/// at all. Which beat the player is on is state (<c>Player.FtueBeat</c>), and refusing the command
/// outside the window is M4-12's rule, answered as a `14` §16.2 rejection.
/// </remarks>
public sealed record SkipFtueCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>EQUIP</c> — put a gear item in a slot. <b>Gear only</b>: pets and mounts have
/// their own commands below.
/// </summary>
/// <param name="ItemId">
/// The gear instance to equip — `08` §7's <c>instanceId</c>. Typed by <b>M4-03</b>, which authors
/// the instance schema.
/// </param>
/// <param name="GearSlot">
/// `08` §1's slot, as `08` §7's schema spells it (<c>"slot": "WEAPON"</c>). Six exist — Weapon,
/// Helmet, Armor, Boots, Ring, Amulet — and the closed type is <b>M4-03's</b>, which needs them
/// for `08` §3.0a's slot coefficients. It is not declared here: see <see cref="CommandPayload"/>.
/// </param>
public sealed record EquipCommand(string ItemId, string GearSlot) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>MERGE</c> — fuse items into one of the next rarity (`08` §4.1).
/// </summary>
/// <remarks>
/// ⚠️ <b>A doc contradiction, transcribed rather than resolved</b> (steering <b>S16</b>).
/// `08` §4.1's merge takes <b>three</b> inputs of the same item, rarity and enhance level, and
/// Merge Dust may substitute for <b>one</b> of the three. `14` §2.3's payload sketch names only
/// <b>two</b> item ids plus the flag, which is complete only when <c>dustSubstituted</c> is
/// <see langword="true"/> — the two readings differ by an <c>inputItemIdC</c>. The sketch is
/// transcribed exactly here rather than repaired, because inventing the third field would freeze
/// the answer before the task that owns it. <b>Owner: M4-04</b>, which authors the forge; the
/// deliverable is an amendment to `14` §2.3, not a third parameter added here.
/// </remarks>
/// <param name="InputItemIdA">The first input instance (`08` §7's <c>instanceId</c>).</param>
/// <param name="InputItemIdB">The second input instance.</param>
/// <param name="DustSubstituted">
/// Whether Merge Dust fills an input slot. `08` §4.1 allows it for <b>one</b> slot, at
/// <c>DustSubstituteCost(rarity)</c>, and a dust-filled slot does not count toward the output's
/// quality or <c>chapterOrigin</c> maxima.
/// </param>
public sealed record MergeCommand(string InputItemIdA, string InputItemIdB, bool DustSubstituted)
    : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>ENHANCE</c> — +0 → +15 with mercy inheritance (`08` §4).
/// </summary>
/// <param name="ItemId">The gear instance to enhance. Typed by <b>M4-03</b>.</param>
public sealed record EnhanceCommand(string ItemId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SALVAGE</c> — break items down. `08` §4.3 pays Merge Dust and refunds 60 % of
/// the Enhance Stones; an <b>SS</b> item additionally yields one Set Token (`24` §5.1).
/// </summary>
/// <remarks>
/// 🔒 <b>Equality is hand-written, and it is not decoration.</b> <see cref="GameCommand"/>'s
/// remarks make value equality the contract that lets `14` §3.2 replay a repeated command, and a
/// record compares an <c>IReadOnlyList&lt;string&gt;</c> member by <b>reference</b> — so the
/// synthesized version would have made two identical salvage requests unequal. The list is also
/// <b>copied</b> on the way in and the property is get-only rather than <c>init</c>: an <c>init</c>
/// is assignable through <c>with</c>, and that assignment would hand the command the caller's own
/// array, bypassing the copy. (The same shape M1-03 recorded for <c>CurrencyChanged.Reason</c>.)
/// </remarks>
public sealed record SalvageCommand : GameCommand
{
    /// <summary>Salvages the given gear instances.</summary>
    /// <param name="itemIds">The instances to salvage. Typed by <b>M4-03</b>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="itemIds"/> is null.</exception>
    public SalvageCommand(IReadOnlyList<string> itemIds) =>
        ItemIds = CommandPayload.Copy(itemIds, nameof(itemIds));

    /// <summary>The gear instances to salvage, in the order the client sent them.</summary>
    public IReadOnlyList<string> ItemIds { get; }

    /// <summary>Two salvage commands are equal when they name the same instances in the same order.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(SalvageCommand? other) =>
        other is not null && CommandPayload.SameIds(ItemIds, other.ItemIds);

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 <see cref="GameCommand.EqualityContract"/> is in the hash, exactly as a <em>synthesized</em>
    /// record hash carries it. Without it <c>SalvageCommand([])</c> and <c>ClaimInboxCommand([])</c>
    /// hash identically — legal, since <c>Equals</c> still tells them apart, but it buckets two
    /// different commands together in the <c>Dictionary&lt;GameCommand, …&gt;</c> `14` §16.3's
    /// idempotency replay will be.
    /// </remarks>
    public override int GetHashCode() =>
        HashCode.Combine(EqualityContract, CommandPayload.HashIds(ItemIds));

    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ItemIds)} = {CommandPayload.Text(ItemIds)}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>SPEND_TALENT</c> — put a point into a talent node (`09`).
/// </summary>
/// <param name="NodeId">
/// The talent node. Typed by <b>M4-06</b>, which authors the 60-node catalogue as DSL data.
/// ⚠️ Not the board's node identity: `30` §4's <c>Board</c> gap defers a different <c>NodeId</c>
/// to M3-01, and the two name different graphs.
/// </param>
public sealed record SpendTalentCommand(string NodeId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>RESPEC</c> — refund every talent point. `09` makes it free and instant.
/// </summary>
public sealed record RespecCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>LEVEL_PET</c> — level a beast. <b>Pets and mounts</b>: `07` §3.1a is the section
/// that actually says <em>"Mounts mirror pets"</em> (`14` §2.3's own citation of §3.1 points at the
/// mounts overview), so one command covers both.
/// </summary>
/// <remarks>
/// The dispatch row names <b>M4-07</b> (pets), which is the task that makes the command writable;
/// <b>M4-08</b> extends it to mounts, whose levelling is Feed-only. Two rows for one wire name is
/// not available — `14` §2.3 lists one.
/// </remarks>
/// <param name="BeastId">The pet or mount, e.g. `07` §2's <c>PET_DICEBEAST</c>. Typed by <b>M4-07</b>.</param>
public sealed record LevelPetCommand(string BeastId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>ASCEND_PET</c> — ★1–5 ascension (`07` §2). Same pets-and-mounts scope as
/// <see cref="LevelPetCommand"/>.
/// </summary>
/// <param name="BeastId">The pet or mount to ascend. Typed by <b>M4-07</b>.</param>
public sealed record AscendPetCommand(string BeastId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>EQUIP_PET</c> — put a pet in one of the three slots, or empty it.
/// </summary>
/// <param name="SlotIndex">
/// 🔒 <c>0–2</c>. The bound is <b>authored</b> — `14` §2.3 writes <c>slotIndex: 0–2</c> and `07` §1
/// unlocks the three pet slots at Legend Level 5, 15 and 30 — and it is recorded here rather than
/// enforced in this constructor, for the reason <see cref="CommandPayload"/> states: legality is
/// <c>GameRules.Apply</c>'s, and a command that cannot be built cannot be refused with a
/// `14` §16.2 reason.
/// </param>
/// <param name="PetId">The pet to equip. <see langword="null"/> <b>unequips the slot</b>.</param>
public sealed record EquipPetCommand(int SlotIndex, string? PetId) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(SlotIndex)} = {SlotIndex}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(PetId)} = {PetId}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>EQUIP_MOUNT</c> — the single mount slot, unlocked at Legend Level 20 (`07` §3).
/// </summary>
/// <param name="MountId">The mount to equip. <see langword="null"/> <b>unequips</b>.</param>
public sealed record EquipMountCommand(string? MountId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CLAIM_QUEST</c> — collect a completed daily quest (`10` §6, `19` B).
/// </summary>
/// <param name="QuestSlot">The slot's position in the player's daily slate.</param>
public sealed record ClaimQuestCommand(int QuestSlot) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(QuestSlot)} = {QuestSlot}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>REROLL_QUEST</c> ⚄ — redraw one quest. 1 free per day (`19` B), and the
/// replacement is drawn from <b>this command's</b> seed.
/// </summary>
/// <param name="QuestSlot">The slot to redraw.</param>
public sealed record RerollQuestCommand(int QuestSlot) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(QuestSlot)} = {QuestSlot}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>CLAIM_AD_REWARD</c> — granted against the server-side S2S callback record
/// (`12` §3.3), never against the client's word.
/// </summary>
/// <param name="PlacementId">
/// One of `12` §4's 29 rewarded placements (13 in-run + 16 meta), as
/// <c>game-data/tuning/ads.json</c> keys them. ⚠️ This is the <b>one</b> payload id whose value set
/// could be typed today — that file is <c>"_status": "transcribed"</c> and carries all 29 — and it
/// is deliberately not; see <see cref="CommandPayload"/>. Typed by <b>M15-03</b>, which authors the
/// cap engine over that file and the S2S callback record the grant is checked against.
/// </param>
public sealed record ClaimAdRewardCommand(string PlacementId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CLAIM_CALENDAR</c> — claim the currently open day of the 28-day login calendar
/// (`19` G). It carries no day number: `19` G's calendar is <b>pause-not-skip</b>, so which day is
/// open is server state and naming one on the wire would be a second, disagreeing source for it.
/// </summary>
public sealed record ClaimCalendarCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CLAIM_INBOX</c> — claim inbox attachments (`28` A).
/// </summary>
/// <remarks>
/// 🔒 <b>Omitted or empty means "claim everything claimable"</b> — `14` §2.3's own words — which is
/// why the list is nullable and why <see langword="null"/> is <b>not</b> collapsed to an empty
/// list on the way in. The two mean the same thing to M5-08's rule, and they are still two
/// different things the client sent; `14` §16.3 replays a stored outcome against the command it
/// was asked about. Equality is hand-written for the reason <see cref="SalvageCommand"/> records.
/// </remarks>
public sealed record ClaimInboxCommand : GameCommand
{
    /// <summary>Claims the named messages, or everything claimable.</summary>
    /// <param name="messageIds">
    /// The messages to claim, or <see langword="null"/> for all of them. Typed by <b>M5-08</b>,
    /// which authors the message store and <c>IMessageRepository</c>.
    /// </param>
    public ClaimInboxCommand(IReadOnlyList<string>? messageIds = null) =>
        MessageIds = CommandPayload.CopyOptional(messageIds, nameof(messageIds));

    /// <summary>The messages to claim, or <see langword="null"/> for everything claimable.</summary>
    public IReadOnlyList<string>? MessageIds { get; }

    /// <summary>Two claims are equal when they name the same messages, or both name none.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    /// <remarks>
    /// <c>CommandPayload.SameIds</c> keeps <see langword="null"/> and an <em>empty</em> list apart —
    /// one is null and the other is not, so it answers false — which is the whole reason the
    /// constructor does not collapse them.
    /// </remarks>
    public bool Equals(ClaimInboxCommand? other) =>
        other is not null && CommandPayload.SameIds(MessageIds, other.MessageIds);

    /// <inheritdoc/>
    /// <remarks>See <see cref="SalvageCommand.GetHashCode"/> for why the contract is in the hash.</remarks>
    public override int GetHashCode() =>
        HashCode.Combine(EqualityContract, CommandPayload.HashIds(MessageIds));

    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(MessageIds)} = {CommandPayload.Text(MessageIds)}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>SPIN_WHEEL</c> ⚄ — consumes the oldest available spin charge, free before
/// ad-granted (`19` F). The segment is drawn from this command's seed.
/// </summary>
/// <remarks>
/// It names no charge: "oldest available, free before ad-granted" is an ordering rule over server
/// state (M4-09's), and a charge id on the wire would let the client choose which one to burn.
/// </remarks>
public sealed record SpinWheelCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SET_FOCUS</c> — nominate one <c>(slot, family)</c> as the Focus of every gear
/// grant, at ×2.5 weight (`24` §5). A change starts the 12-hour cooldown.
/// </summary>
/// <remarks>
/// Both fields are nullable because `14` §2.3 says <em>"null clears"</em>. The 12-hour cooldown is
/// `24` §5's rule and is <b>M4-04's</b> to enforce — it needs the previous Focus and the clock,
/// neither of which is on this command.
/// </remarks>
/// <param name="GearSlot">
/// `08` §1's slot, as `08` §7 spells it (<c>"WEAPON"</c>). <see langword="null"/> clears.
/// Typed by <b>M4-03</b>.
/// </param>
/// <param name="Family">
/// `08` §1.1's item family, as `08` §7 spells it (<c>"BLADE"</c>) — 24 of them across four axes.
/// <see langword="null"/> clears. Typed by <b>M4-03</b>.
/// </param>
public sealed record SetFocusCommand(string? GearSlot, string? Family) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>REFORGE_ITEM</c> ⚄ — re-roll an item's quality, keeping the better of the two
/// (`24` §6.1). The roll comes from this command's seed.
/// </summary>
/// <param name="ItemId">The gear instance to reforge. Typed by <b>M4-03</b>.</param>
public sealed record ReforgeItemCommand(string ItemId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>RETUNE_ITEM</c> ⚄ — re-roll an item's affixes with locks and a wishlist
/// (`24` §6.2). The roll comes from this command's seed.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The wishlist rides the command and persists on the item, and the M2 mercy counter resets
/// when it changes</b> — both sentences are `14` §2.3's own note on this row, not `24` §6.2's,
/// which specifies the locks, the wishlist and the mercy counter but not their lifecycle. That is
/// why the wishlist is a payload field rather than a separate command: the retune and the wishlist
/// change are one atomic intent.
/// </para>
/// <para>
/// ⚠️ `14` §2.3 writes <c>[≤3]</c> on both lists. The bound is <b>authored</b> and is recorded
/// here rather than enforced, for the reason <see cref="CommandPayload"/> states. Equality is
/// hand-written for the reason <see cref="SalvageCommand"/> records — with <b>two</b> lists, the
/// synthesized version would have been wrong twice over.
/// </para>
/// </remarks>
public sealed record RetuneItemCommand : GameCommand
{
    /// <summary>Retunes an item under the given locks and wishlist.</summary>
    /// <param name="itemId">The gear instance to retune. Typed by <b>M4-03</b>.</param>
    /// <param name="lockedAffixIds">
    /// The affixes to keep — `14` §2.3's <c>[≤3]</c>, corroborated by `24` §6.2's "up to 3 affix
    /// IDs". The 14 affixes are `08` §3.1 and their <c>AFX_*</c> ids are `08` §7's schema; typed by
    /// <b>M4-03</b>.
    /// </param>
    /// <param name="wishlistAffixIds">The wanted affixes — also <c>[≤3]</c>.</param>
    /// <exception cref="ArgumentNullException">Either list is null.</exception>
    public RetuneItemCommand(
        string itemId, IReadOnlyList<string> lockedAffixIds, IReadOnlyList<string> wishlistAffixIds)
    {
        ItemId = itemId;
        LockedAffixIds = CommandPayload.Copy(lockedAffixIds, nameof(lockedAffixIds));
        WishlistAffixIds = CommandPayload.Copy(wishlistAffixIds, nameof(wishlistAffixIds));
    }

    /// <summary>The gear instance being retuned.</summary>
    public string ItemId { get; }

    /// <summary>The affixes held through the re-roll, in the order the client sent them.</summary>
    public IReadOnlyList<string> LockedAffixIds { get; }

    /// <summary>The wanted affixes, which persist on the item after the command.</summary>
    public IReadOnlyList<string> WishlistAffixIds { get; }

    /// <summary>Two retunes are equal when the item, the locks and the wishlist all match.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(RetuneItemCommand? other) =>
        other is not null &&
        string.Equals(ItemId, other.ItemId, StringComparison.Ordinal) &&
        CommandPayload.SameIds(LockedAffixIds, other.LockedAffixIds) &&
        CommandPayload.SameIds(WishlistAffixIds, other.WishlistAffixIds);

    /// <inheritdoc/>
    /// <remarks>See <see cref="SalvageCommand.GetHashCode"/> for why the contract is in the hash.</remarks>
    public override int GetHashCode() => HashCode.Combine(
        EqualityContract,
        ItemId,
        CommandPayload.HashIds(LockedAffixIds),
        CommandPayload.HashIds(WishlistAffixIds));

    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ItemId)} = {ItemId}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $", {nameof(LockedAffixIds)} = {CommandPayload.Text(LockedAffixIds)}");
        builder.Append(
            CultureInfo.InvariantCulture,
            $", {nameof(WishlistAffixIds)} = {CommandPayload.Text(WishlistAffixIds)}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>SAVE_PRESET</c> — snapshot the current talents and loadout into a preset slot
/// (`09` §2.1).
/// </summary>
/// <param name="PresetSlot">
/// The slot to write. ⚠️ <b>No bound.</b> `16` <b>O11</b> leaves the preset count open —
/// <em>"3 free slots is a guess. Raise the allowance if telemetry shows players capped; never gate
/// it further"</em>, due <em>"after first playtest"</em> — and the tracker routes the ruling to the
/// <b>M9 kickoff</b>. A range here would be invention rather than transcription (steering <b>S6</b>).
/// </param>
/// <param name="Name">The player's name for the preset.</param>
public sealed record SavePresetCommand(int PresetSlot, string Name) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(PresetSlot)} = {PresetSlot}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Name)} = {Name}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>APPLY_PRESET</c> — load a preset. <b>Never mid-run</b>, which is a rule over
/// state rather than payload and belongs to M4-10.
/// </summary>
/// <param name="PresetSlot">The slot to load. Unbounded, for the reason <see cref="SavePresetCommand"/> records.</param>
public sealed record ApplyPresetCommand(int PresetSlot) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(PresetSlot)} = {PresetSlot}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>SHOP_PURCHASE</c> — buy from the <b>meta</b> shop (`10` §5).
/// </summary>
/// <remarks>
/// ⚠️ Distinct from the run-local <see cref="ShopBuyCommand"/> — see that type's remarks.
/// 🔒 <b>Purchased containers arrive unopened on the shelf</b> (`24` §4): this command does not
/// open anything, which is what makes <c>OPEN_CHEST</c>/<c>OPEN_EGG</c>/<c>OPEN_CRATE</c> separate
/// rows that read pity and Focus at open rather than at purchase.
/// </remarks>
/// <param name="OfferId">The shop offer. Typed by <b>M4-09</b>, which authors the shop model.</param>
/// <param name="Quantity">How many. An unbounded count: `10` §5 caps stock per offer, not per command.</param>
public sealed record ShopPurchaseCommand(string OfferId, int Quantity) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(OfferId)} = {OfferId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Quantity)} = {Quantity}");

        return true;
    }
}

/// <summary>
/// 🔒 `14` §2.3 <c>OPEN_CHEST</c> ⚄ — open a chest off the shelf. <b>Pity and Focus are read at
/// open</b> (`24` §4), not at purchase, and the contents are drawn from this command's seed.
/// </summary>
/// <param name="ContainerId">
/// The stored container. Typed by <b>M4-02</b>, which authors the shelf and the class vocabulary
/// `30` §4's <c>ContainerShelf</c> gap waits for.
/// </param>
public sealed record OpenChestCommand(string ContainerId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>OPEN_EGG</c> ⚄ — open a Pet Egg. Same at-open rule as
/// <see cref="OpenChestCommand"/>.
/// </summary>
/// <param name="ContainerId">The stored container. Typed by <b>M4-02</b>.</param>
public sealed record OpenEggCommand(string ContainerId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>OPEN_CRATE</c> ⚄ — open a Mount Crate. Same at-open rule as
/// <see cref="OpenChestCommand"/>.
/// </summary>
/// <param name="ContainerId">The stored container. Typed by <b>M4-02</b>.</param>
public sealed record OpenCrateCommand(string ContainerId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>UPLOAD_GHOST</c> — publish the player's PvP ghost (`11` §2). It carries no
/// payload because a ghost is <b>server-generated</b>: `11` §2 makes it an immutable snapshot of
/// the server's own resolved view of the loadout, so anything the client sent would be a second
/// opinion about state the server already holds.
/// </summary>
public sealed record UploadGhostCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>START_DUEL</c> ⚄ — challenge a stored ghost. The server issues
/// <c>duelSeed</c> from <b>this command's</b> seed (`11` §4.3).
/// </summary>
/// <param name="GhostId">
/// The opponent's ghost. Typed by <b>M12-01</b> — it is the same <c>GhostId</c> the
/// <c>GhostSnapshot</c> gap in <c>GapRegister</c> already waits for.
/// </param>
public sealed record StartDuelCommand(string GhostId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SUBMIT_DUEL</c> — report the simulated duel. The server re-simulates and
/// compares the hash (`11` §6, `14` §9's anti-cheat table).
/// </summary>
/// <param name="DuelId">The duel being reported. Typed by <b>M12-04</b>.</param>
/// <param name="LogHash">`05` §7's combat-log hash, as text. Typed by <b>M2-15</b>.</param>
public sealed record SubmitDuelCommand(string DuelId, string LogHash) : GameCommand;
