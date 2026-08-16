using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Commands;

// The meta half of the command registry, in the table's own order. The endpoint is
// POST /player/command, sequenced per player. Nine commands (marked ⚄) draw from the command's
// server-issued seed rather than a run stream; each one says so below.

/// <summary>
/// <c>BEGIN_SESSION</c> ⚄ — the server-acknowledged first contact of a session and of each game
/// day. Carries the calendar advance, the daily free Energy refill and the day's random draws (the
/// quest slate and the Daily shop block).
/// </summary>
/// <param name="ClientVersion">The client build asking for the session.</param>
/// <param name="ContentHash">
/// The content version the client holds. Free-form text on the wire, since a command that refused
/// to be constructed from a malformed hash could not be answered with a rejection.
/// </param>
public sealed record BeginSessionCommand(string ClientVersion, string ContentHash) : GameCommand;

/// <summary>
/// <c>SKIP_FTUE</c> — valid only while <c>ftueProgress</c> is between beats 2 and 8; grants the
/// full scripted payout and jumps to beat 9. Idempotent: a resend after completion is a no-op.
/// </summary>
/// <remarks>
/// The beat range is not a constructor guard — the command carries no beat at all. Which beat the
/// player is on is state, and refusing the command outside the window is a rule, not a payload check.
/// </remarks>
public sealed record SkipFtueCommand : GameCommand;

/// <summary>
/// <c>EQUIP</c> — put a gear item in a slot. Gear only: pets and mounts have their own commands below.
/// </summary>
/// <param name="ItemId">The gear instance to equip.</param>
/// <param name="GearSlot">The slot. One of the six the domain declares, not free text.</param>
public sealed record EquipCommand(GearInstanceId ItemId, GearSlot GearSlot) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ItemId)} = {ItemId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(GearSlot)} = {GearSlot}");

        return true;
    }
}

/// <summary>
/// <c>MERGE</c> ⚄ — fuse three items into one of the next rarity. The affix re-roll at the new
/// rarity draws from this command's seed.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A list, because the fusion takes three inputs and the two-id payload could not say so.</b>
/// The earlier shape named two instances plus a flag, which expresses a real merge only when the
/// flag is true — there was no way at all to send three items, which is the ordinary case. The list
/// carries the real inputs and the flag says whether Merge Dust fills the remaining slot, so the
/// two members together always add up to three: three ids with the flag clear, two with it set.
/// </para>
/// <para>
/// The arithmetic is deliberately <em>not</em> enforced here. How many inputs a fusion takes and how
/// many of them dust may fill are authored numbers, not payload shape, so a command carrying four
/// ids is a rejection the player is told about rather than a command that refuses to be constructed
/// — and a command that cannot be built cannot be answered.
/// </para>
/// <para>
/// Equality and hashing are hand-written for the reason <see cref="SalvageCommand"/> records: a
/// record compares an <c>IReadOnlyList&lt;T&gt;</c> member by reference.
/// </para>
/// </remarks>
public sealed record MergeCommand : GameCommand
{
    /// <summary>Fuses the named instances, optionally with Merge Dust in the remaining slot.</summary>
    /// <param name="inputItemIds">The input instances, in the order the client sent them.</param>
    /// <param name="dustSubstituted">
    /// Whether Merge Dust fills an input slot in place of an item. A dust-filled slot contributes
    /// neither a quality nor a chapter of origin to the output's maxima.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="inputItemIds"/> is null.</exception>
    public MergeCommand(IReadOnlyList<GearInstanceId> inputItemIds, bool dustSubstituted)
    {
        InputItemIds = CommandPayload.Copy(inputItemIds, nameof(inputItemIds));
        DustSubstituted = dustSubstituted;
    }

    /// <summary>The input instances, in the order the client sent them.</summary>
    public IReadOnlyList<GearInstanceId> InputItemIds { get; }

    /// <summary>Whether Merge Dust fills an input slot in place of an item.</summary>
    public bool DustSubstituted { get; }

    /// <summary>Two merges are equal when they name the same inputs in the same order and agree on the dust.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(MergeCommand? other) =>
        other is not null &&
        DustSubstituted == other.DustSubstituted &&
        CommandPayload.SameIds(InputItemIds, other.InputItemIds);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(EqualityContract, CommandPayload.HashIds(InputItemIds), DustSubstituted);

    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(
            CultureInfo.InvariantCulture,
            $"{nameof(InputItemIds)} = {CommandPayload.Text(InputItemIds)}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(DustSubstituted)} = {DustSubstituted}");

        return true;
    }
}

/// <summary><c>ENHANCE</c> — +0 to +15 with mercy inheritance.</summary>
/// <param name="ItemId">The gear instance to enhance.</param>
public sealed record EnhanceCommand(GearInstanceId ItemId) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ItemId)} = {ItemId}");

        return true;
    }
}

/// <summary>
/// <c>SALVAGE</c> — break items down for Merge Dust and a partial Enhance Stone refund; an SS item
/// additionally yields one Set Token.
/// </summary>
/// <remarks>
/// Equality and hashing are hand-written since a record compares an
/// <c>IReadOnlyList&lt;string&gt;</c> member by reference, not by value. The list is copied on the
/// way in and the property is get-only, not <c>init</c>, so a <c>with</c> expression can't hand the
/// command the caller's own mutable array.
/// </remarks>
public sealed record SalvageCommand : GameCommand
{
    /// <summary>Salvages the given gear instances.</summary>
    /// <param name="itemIds">The instances to salvage.</param>
    /// <exception cref="ArgumentNullException"><paramref name="itemIds"/> is null.</exception>
    public SalvageCommand(IReadOnlyList<GearInstanceId> itemIds) =>
        ItemIds = CommandPayload.Copy(itemIds, nameof(itemIds));

    /// <summary>The gear instances to salvage, in the order the client sent them.</summary>
    public IReadOnlyList<GearInstanceId> ItemIds { get; }

    /// <summary>Two salvage commands are equal when they name the same instances in the same order.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(SalvageCommand? other) =>
        other is not null && CommandPayload.SameIds(ItemIds, other.ItemIds);

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="GameCommand.EqualityContract"/> is included, matching a synthesized record hash —
    /// otherwise two different empty-list commands of different types would hash identically.
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

/// <summary><c>SPEND_TALENT</c> — put a point into a talent node.</summary>
/// <param name="NodeId">The talent node. Not the run board's node identity — the two name different graphs.</param>
public sealed record SpendTalentCommand(string NodeId) : GameCommand;

/// <summary><c>RESPEC</c> — refund every talent point, free and instant.</summary>
public sealed record RespecCommand : GameCommand;

/// <summary><c>LEVEL_PET</c> — level a beast. Covers both pets and mounts, which share this one command.</summary>
/// <param name="BeastId">The pet or mount.</param>
public sealed record LevelPetCommand(string BeastId) : GameCommand;

/// <summary><c>ASCEND_PET</c> — star-tier ascension. Same pets-and-mounts scope as <see cref="LevelPetCommand"/>.</summary>
/// <param name="BeastId">The pet or mount to ascend.</param>
public sealed record AscendPetCommand(string BeastId) : GameCommand;

/// <summary><c>EQUIP_PET</c> — put a pet in one of the three slots, or empty it.</summary>
/// <param name="SlotIndex">0–2. The bound is authored, not enforced here — legality is <c>GameRules.Apply</c>'s job.</param>
/// <param name="PetId">The pet to equip. <see langword="null"/> unequips the slot.</param>
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

/// <summary><c>EQUIP_MOUNT</c> — the single mount slot.</summary>
/// <param name="MountId">The mount to equip. <see langword="null"/> unequips.</param>
public sealed record EquipMountCommand(string? MountId) : GameCommand;

/// <summary><c>CLAIM_QUEST</c> — collect a completed daily quest.</summary>
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

/// <summary><c>REROLL_QUEST</c> ⚄ — redraw one quest. 1 free per day; the replacement is drawn from this command's seed.</summary>
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

/// <summary><c>CLAIM_AD_REWARD</c> — granted against the server-side S2S callback record, never against the client's word.</summary>
/// <param name="PlacementId">One of the rewarded ad placements.</param>
public sealed record ClaimAdRewardCommand(string PlacementId) : GameCommand;

/// <summary>
/// <c>CLAIM_CALENDAR</c> — claim the currently open day of the login calendar. Carries no day
/// number: which day is open is server state (the calendar pauses rather than skips), so naming
/// one on the wire would be a second, disagreeing source for it.
/// </summary>
public sealed record ClaimCalendarCommand : GameCommand;

/// <summary><c>CLAIM_INBOX</c> — claim inbox attachments.</summary>
/// <remarks>
/// Omitted or empty both mean "claim everything claimable", but <see langword="null"/> is not
/// collapsed to an empty list on the way in — they're still two different things the client sent.
/// Equality is hand-written for the reason <see cref="SalvageCommand"/> records.
/// </remarks>
public sealed record ClaimInboxCommand : GameCommand
{
    /// <summary>Claims the named messages, or everything claimable.</summary>
    /// <param name="messageIds">The messages to claim, or <see langword="null"/> for all of them.</param>
    public ClaimInboxCommand(IReadOnlyList<string>? messageIds = null) =>
        MessageIds = CommandPayload.CopyOptional(messageIds, nameof(messageIds));

    /// <summary>The messages to claim, or <see langword="null"/> for everything claimable.</summary>
    public IReadOnlyList<string>? MessageIds { get; }

    /// <summary>Two claims are equal when they name the same messages, or both name none.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(ClaimInboxCommand? other) =>
        other is not null && CommandPayload.SameIds(MessageIds, other.MessageIds);

    /// <inheritdoc/>
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

/// <summary><c>SPIN_WHEEL</c> ⚄ — consumes the oldest available spin charge, free before ad-granted. The segment is drawn from this command's seed.</summary>
/// <remarks>Names no charge: "oldest available" is an ordering rule over server state, not a client choice.</remarks>
public sealed record SpinWheelCommand : GameCommand;

/// <summary>
/// <c>SET_FOCUS</c> — nominate one <c>(slot, family)</c> as the Focus of every gear grant, at
/// boosted weight. A change starts a cooldown.
/// </summary>
/// <param name="GearSlot">The slot. <see langword="null"/> clears.</param>
/// <param name="Family">The item family. <see langword="null"/> clears.</param>
/// <remarks>
/// Both halves are nullable because clearing the Focus is one of the command's two meanings, and a
/// nullable enum keeps "cleared" distinguishable from a slot that happens to sit at the bottom of the
/// vocabulary — which is the same reason neither enum declares a zero member.
/// </remarks>
public sealed record SetFocusCommand(GearSlot? GearSlot, GearFamily? Family) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(GearSlot)} = {GearSlot}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Family)} = {Family}");

        return true;
    }
}

/// <summary><c>REFORGE_ITEM</c> ⚄ — re-roll an item's quality, keeping the better of the two. The roll comes from this command's seed.</summary>
/// <param name="ItemId">The gear instance to reforge.</param>
public sealed record ReforgeItemCommand(GearInstanceId ItemId) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ItemId)} = {ItemId}");

        return true;
    }
}

/// <summary><c>RETUNE_ITEM</c> ⚄ — re-roll an item's affixes with locks and a wishlist. The roll comes from this command's seed.</summary>
/// <remarks>
/// The wishlist rides the command and persists on the item, and it is a payload field rather than
/// a separate command because the retune and the wishlist change are one atomic intent. Both lists
/// are authored-bounded but unenforced here; equality is hand-written since two lists would break
/// synthesized equality twice over.
/// </remarks>
public sealed record RetuneItemCommand : GameCommand
{
    /// <summary>Retunes an item under the given locks and wishlist.</summary>
    /// <param name="itemId">The gear instance to retune.</param>
    /// <param name="lockedAffixIds">The affixes to keep.</param>
    /// <param name="wishlistAffixIds">The wanted affixes.</param>
    /// <exception cref="ArgumentNullException">Either list is null.</exception>
    public RetuneItemCommand(
        GearInstanceId itemId,
        IReadOnlyList<string> lockedAffixIds,
        IReadOnlyList<string> wishlistAffixIds)
    {
        ItemId = itemId;
        LockedAffixIds = CommandPayload.Copy(lockedAffixIds, nameof(lockedAffixIds));
        WishlistAffixIds = CommandPayload.Copy(wishlistAffixIds, nameof(wishlistAffixIds));
    }

    /// <summary>The gear instance being retuned.</summary>
    public GearInstanceId ItemId { get; }

    /// <summary>
    /// The affixes held through the re-roll, in the order the client sent them. Text, not a declared
    /// id: the affix vocabulary is authored content and a fifteenth affix is a tuning edit, so an
    /// enum here would make the one table whose purpose is to be re-tuned a code edit instead.
    /// </summary>
    public IReadOnlyList<string> LockedAffixIds { get; }

    /// <summary>The wanted affixes, which persist on the item after the command.</summary>
    public IReadOnlyList<string> WishlistAffixIds { get; }

    /// <summary>Two retunes are equal when the item, the locks and the wishlist all match.</summary>
    /// <param name="other">The other command.</param>
    /// <returns>Whether the two describe the same intent.</returns>
    public bool Equals(RetuneItemCommand? other) =>
        other is not null &&
        ItemId.Equals(other.ItemId) &&
        CommandPayload.SameIds(LockedAffixIds, other.LockedAffixIds) &&
        CommandPayload.SameIds(WishlistAffixIds, other.WishlistAffixIds);

    /// <inheritdoc/>
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

/// <summary><c>SAVE_PRESET</c> — snapshot the current talents and loadout into a preset slot.</summary>
/// <param name="PresetSlot">The slot to write. No authored bound on the preset count.</param>
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

/// <summary><c>APPLY_PRESET</c> — load a preset. Never mid-run, which is a rule over state rather than payload.</summary>
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

/// <summary><c>SHOP_PURCHASE</c> — buy from the meta shop. Distinct from the run-local <see cref="ShopBuyCommand"/>.</summary>
/// <remarks>
/// Purchased containers arrive unopened on the shelf — this command does not open anything, which
/// is why <c>OPEN_CHEST</c>/<c>OPEN_EGG</c>/<c>OPEN_CRATE</c> are separate rows that read pity and
/// Focus at open rather than at purchase.
/// </remarks>
/// <param name="OfferId">The shop offer.</param>
/// <param name="Quantity">How many. Unbounded per command; stock is capped per offer.</param>
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

/// <summary><c>OPEN_CHEST</c> ⚄ — open a chest off the shelf. Pity and Focus are read at open, not at purchase; contents are drawn from this command's seed.</summary>
/// <param name="ContainerId">The stored container.</param>
public sealed record OpenChestCommand(string ContainerId) : GameCommand;

/// <summary><c>OPEN_EGG</c> ⚄ — open a Pet Egg. Same at-open rule as <see cref="OpenChestCommand"/>.</summary>
/// <param name="ContainerId">The stored container.</param>
public sealed record OpenEggCommand(string ContainerId) : GameCommand;

/// <summary><c>OPEN_CRATE</c> ⚄ — open a Mount Crate. Same at-open rule as <see cref="OpenChestCommand"/>.</summary>
/// <param name="ContainerId">The stored container.</param>
public sealed record OpenCrateCommand(string ContainerId) : GameCommand;

/// <summary>
/// <c>UPLOAD_GHOST</c> — publish the player's PvP ghost. Carries no payload: a ghost is a
/// server-generated snapshot of the server's own resolved view of the loadout.
/// </summary>
public sealed record UploadGhostCommand : GameCommand;

/// <summary><c>START_DUEL</c> ⚄ — challenge a stored ghost. The server issues <c>duelSeed</c> from this command's seed.</summary>
/// <param name="GhostId">The opponent's ghost.</param>
public sealed record StartDuelCommand(string GhostId) : GameCommand;

/// <summary><c>SUBMIT_DUEL</c> — report the simulated duel. The server re-simulates and compares the hash.</summary>
/// <param name="DuelId">The duel being reported.</param>
/// <param name="LogHash">The combat-log hash, as text.</param>
public sealed record SubmitDuelCommand(string DuelId, string LogHash) : GameCommand;
