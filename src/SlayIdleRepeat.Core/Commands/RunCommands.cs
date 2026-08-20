using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Commands;

// The run half of the command registry, in the table's own order. The endpoint is
// POST /run/{runId}/command, sequenced per run — except START_RUN, submitted on the player
// endpoint since no runId exists yet. That's a transport fact only: START_RUN still acts on a run,
// is dispatched with the run in the slice, and slides the run's TTL. See StartRunCommand's remarks.

/// <summary><c>START_RUN</c> — begin a run on a chapter and tier. Commits <c>runSeed</c> before the board is shown.</summary>
/// <remarks>
/// A <c>CommandKind.Run</c> command submitted on the player endpoint, deliberately: classifying it
/// <c>Meta</c> to match its endpoint would hand it no RNG scope on the one command whose job is to
/// commit the seed those streams are rooted in, and would leave the run it just created ageing off
/// a TTL nothing had advanced. <paramref name="ChapterId"/>'s upper bound is not transcribed here
/// even though one is authored, since which chapters exist per tier is content-gated and a hard
/// limit in a wire command would refuse a chapter before new content could ship.
/// </remarks>
/// <param name="ChapterId">The chapter number.</param>
/// <param name="Tier">The difficulty the run is played on.</param>
public sealed record StartRunCommand(int ChapterId, DifficultyTier Tier) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ChapterId)} = {ChapterId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Tier)} = {Tier}");

        return true;
    }
}

/// <summary><c>ROLL_DICE</c> — roll. The server answers with the number, the movement and the landing outcome in one command.</summary>
public sealed record RollDiceCommand : GameCommand;

/// <summary>
/// <c>USE_FIXED_DIE</c> — spend one held fixed die and move exactly its number instead of rolling.
/// </summary>
/// <remarks>
/// 🔒 A command of its own rather than a payload on <see cref="RollDiceCommand"/>, and the reason is
/// the log: this one takes NO draw from the <c>dice</c> stream while a roll always takes exactly one,
/// so a single command whose RNG consumption depended on a payload would make every replay read the
/// payload before it could know where the stream stands.
/// </remarks>
/// <param name="Pips">The number on the die to spend, 1..6. The run must hold one showing it.</param>
public sealed record UseFixedDieCommand(int Pips) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Pips)} = {Pips}");

        return true;
    }
}

/// <summary>
/// <c>CHOOSE_FIXED_DIE</c> — name the number on a fixed die the run has been granted.
/// </summary>
/// <remarks>
/// 🔒 One command for every grant site. A site records that a choice is owed rather than handing
/// over a die, because most of them have no command a number could ride on — an event outcome is
/// drawn by weight, a minigame reward is decided by play, an ad reward and a set bonus are passive.
/// See <c>Handlers.ChooseFixedDie</c>.
/// </remarks>
/// <param name="Pips">The number the granted die shows, 1..6.</param>
public sealed record ChooseFixedDieCommand(int Pips) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(Pips)} = {Pips}");

        return true;
    }
}

/// <summary><c>CHOOSE_FORK</c> — pick a branch at a junction.</summary>
/// <param name="BranchIndex">
/// The chosen branch's position in the server-issued branch list — an index rather than a node
/// identity, since the movement engine owns what a branch resolves to.
/// </param>
public sealed record ChooseForkCommand(int BranchIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(BranchIndex)} = {BranchIndex}");

        return true;
    }
}

/// <summary><c>RESOLVE_TILE</c> — acknowledge or advance the pending tile resolution.</summary>
public sealed record ResolveTileCommand : GameCommand;

/// <summary><c>PICK_PERK</c> — take one of the three drafted options.</summary>
/// <param name="OptionIndex">The chosen option's position in the server-issued draft.</param>
public sealed record PickPerkCommand(int OptionIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(OptionIndex)} = {OptionIndex}");

        return true;
    }
}

/// <summary><c>REROLL_DRAFT</c> — redraw the perk draft.</summary>
public sealed record RerollDraftCommand : GameCommand;

/// <summary><c>SKIP_DRAFT</c> — take none of the offered perks. Allowed, and pays Gold.</summary>
/// <remarks>
/// ⚠️ <b>Gold and nothing else.</b> This comment used to promise <em>"Gold plus a free reroll"</em>,
/// which was false in two directions at once: <c>SkipDraft.Handle</c> grants no charge, and the
/// free-reroll allowance <c>06</c> §1 describes — one per stage, accumulating to three, plus one from
/// a skip — <b>is not implemented anywhere in the game</b>. <c>RerollDraft</c> charges Gold on every
/// call with no counter and no cap and says so in its own remarks, and no persisted field counts draft
/// rerolls. The design-versus-code contradiction is a live product question carried to the milestone
/// review; what is fixed here is only that this line stops asserting the side of it that is untrue.
/// </remarks>
public sealed record SkipDraftCommand : GameCommand;

/// <summary><c>SHOP_BUY</c> — buy a slot from the in-run shop, paid in run-local Gold.</summary>
/// <remarks>
/// Distinct from the meta <see cref="ShopPurchaseCommand"/>: this spends the run's Gold and dies
/// with the run, while <c>SHOP_PURCHASE</c> spends the player's wallet on the meta shop. Two
/// shops, two currencies, two lifetimes.
/// </remarks>
/// <param name="ShopSlotIndex">The slot's position among the shop's slots.</param>
public sealed record ShopBuyCommand(int ShopSlotIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ShopSlotIndex)} = {ShopSlotIndex}");

        return true;
    }
}

/// <summary><c>SHOP_REFRESH</c> — restock the in-run shop.</summary>
public sealed record ShopRefreshCommand : GameCommand;

/// <summary>
/// <c>SHRINE_CHOOSE</c> — take one of the two options a Shrine tile offers (`03` §7a.5).
/// </summary>
/// <remarks>
/// Added to `14` §2.3's registry alongside <see cref="ShopLeaveCommand"/> (decision recorded in
/// `16`). A shrine offers two distinct
/// options and the player takes one; before this command existed the resolver had to settle it
/// itself, always taking slot 1, which made the game's only "relief or greed" decision a roll.
/// </remarks>
/// <param name="OptionIndex">
/// The chosen option's slot: <c>0</c> for the first drawn buff, <c>1</c> for the second — or for the
/// Cleanse, which replaces the second slot whenever the run carries a cleansable curse.
/// </param>
public sealed record ShrineChooseCommand(int OptionIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(OptionIndex)} = {OptionIndex}");

        return true;
    }
}

/// <summary>
/// <c>SHOP_LEAVE</c> — done shopping; close the offer and walk on (`03` §7).
/// </summary>
/// <remarks>
/// 🔒 <b>A shop is the one tile a player can stand at for several commands by choice</b> — buy,
/// refresh, buy again — so unlike every other tile it needs an explicit "I am finished". Before this
/// command existed, <c>RESOLVE_TILE</c> cleared the shop tile the instant the run arrived, which is
/// what let a run walk away from a shop it could never buy anything at.
/// </remarks>
public sealed record ShopLeaveCommand : GameCommand;


/// <summary><c>EVENT_CHOOSE</c> — take one outcome of an event card.</summary>
/// <param name="ChoiceIndex">The chosen outcome's position in the server-issued card.</param>
public sealed record EventChooseCommand(int ChoiceIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ChoiceIndex)} = {ChoiceIndex}");

        return true;
    }
}

/// <summary>
/// <c>MINIGAME_SUBMIT</c> — assert a minigame outcome. A documented exception to server authority:
/// two minigames are genuine skill inputs, so the client asserts the result and the server
/// validates legality only (a valid outcome tier, one submission per tile, rate limits).
/// </summary>
/// <remarks>
/// <paramref name="Result"/> is an untyped <see cref="int"/> rather than four per-minigame enums,
/// since each minigame's reward table is an ordered, zero-based tier read positionally out of
/// content data — the tier count is data's to declare, not a second statement in code. Which half
/// of the field is trusted differs by minigame: for the two server-rolled minigames it is ignored
/// (the server rolls its own tier from the run's committed stream); for the two client-asserted
/// ones it is the outcome, once the legality check accepts it.
/// </remarks>
/// <param name="MinigameId">The minigame identifier. Validated against <see cref="Content.MinigameCatalogue.IsKnown"/>.</param>
/// <param name="Result">
/// The claimed outcome tier for that minigame — read only for a client-asserted minigame; ignored
/// for a server-rolled one.
/// </param>
public sealed record MinigameSubmitCommand(string MinigameId, int Result) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(MinigameId)} = {MinigameId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Result)} = {Result}");

        return true;
    }
}

/// <summary><c>CAMPFIRE_CHOOSE</c> — take one of the campfire's options.</summary>
/// <param name="ChoiceIndex">The chosen option's position in the server-issued list.</param>
public sealed record CampfireChooseCommand(int ChoiceIndex) : GameCommand
{
    /// <inheritdoc cref="CommandPayload.PrintMembersContract"/>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(ChoiceIndex)} = {ChoiceIndex}");

        return true;
    }
}

/// <summary><c>START_BATTLE</c> — enter the fight. The server answers with the <c>battleSeed</c> and the build snapshot, and the client simulates locally from both.</summary>
/// <remarks>Carries no payload and takes no seed: <c>battleSeed</c> is derived from the run's own committed <c>runSeed</c>.</remarks>
public sealed record StartBattleCommand : GameCommand;

/// <summary><c>CONFIRM_BATTLE_RESULT</c> — the client reports the hash of the fight it simulated; the server compares it against its own. Divergence resyncs the client.</summary>
/// <param name="LogHash">The combat-log hash, as text.</param>
/// <param name="Won">
/// Whether the hero won the fight. No default value: an omitted or mis-bound field defaulting to
/// <see langword="true"/> would silently read as a win, the wrong fail direction for a field that
/// gates a reward payout.
/// </param>
public sealed record ConfirmBattleResultCommand(string LogHash, bool Won) : GameCommand;

/// <summary>
/// <c>REVIVE</c> — once per run. The battle restarts, reproducibly: a revived battle re-derives
/// the same <c>battleSeed</c> and draws from index 0 again.
/// </summary>
public sealed record ReviveCommand : GameCommand;

/// <summary><c>USE_CONSUMABLE</c> — board-only, never during combat.</summary>
/// <param name="ConsumableId">The consumable to use.</param>
public sealed record UseConsumableCommand(string ConsumableId) : GameCommand;

/// <summary><c>END_RUN</c> — bank the run's rewards and close it.</summary>
public sealed record EndRunCommand : GameCommand;

/// <summary><c>ABANDON_RUN</c> — leave the run without completing it.</summary>
/// <remarks>
/// A separate command from <see cref="EndRunCommand"/> rather than a flag, since the two pay
/// differently (abandoning forfeits gear drops and pays a much lower completion multiplier).
/// </remarks>
public sealed record AbandonRunCommand : GameCommand;
