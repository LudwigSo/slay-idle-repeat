using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Commands;

// 🔒 `14` §2.3's RUN half — the 19 rows of "Run commands", in the table's own order so the two can
// be read side by side. Every field's type follows the one rule CommandPayload's remarks state;
// every command's dispatch row (wire name, CommandKind, owning milestone) is in GameRules.
//
// ⚠️ The endpoint is `POST /run/{runId}/command` with the sequence per run — EXCEPT `START_RUN`,
// which is submitted on the player endpoint because no `runId` exists yet. That is a TRANSPORT fact
// and it does not change the command's kind: START_RUN acts on a run, is dispatched with the run in
// the slice, and slides the run's `14` §16.3 TTL. See StartRunCommand's remarks.

/// <summary>
/// 🔒 `14` §2.3 <c>START_RUN</c> — begin a run on a chapter and tier. `02` §2 commits
/// <c>runSeed</c> before the board is shown.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>It is a <c>CommandKind.Run</c> command submitted on the <em>player</em> endpoint</b>, and
/// the two facts are independent. `14` §2.3's exception is about the URL — there is no
/// <c>runId</c> to put in it, so the server allocates the <see cref="Primitives.RunId"/> and the
/// run's sequence starts at 1. The <c>CommandKind</c> is about what <c>Apply</c> does: build a
/// <c>RunRngScope</c> over the run's committed `14` §8.1 counters, and slide the run's `14` §16.3
/// sliding TTL rather than only the player's. Classifying this <c>Meta</c> to match its endpoint
/// would hand the <c>START_RUN</c> handler no scope at all, on the one command whose whole job is
/// to commit the seed those streams are rooted in — and would leave the run it just created
/// ageing off a TTL nothing had advanced.
/// </para>
/// <para>
/// The pair is exactly <c>SeedDerivation.RunSeed</c>'s middle two arguments, which is why
/// <paramref name="ChapterId"/> is an <see cref="int"/>: `02` §1 runs chapters from 1 and
/// <c>chapter.schema.json</c> sets <c>"minimum": 1</c>, so the seam that hashes it already takes
/// one. No upper bound is transcribed — <c>content/chapters/</c> is empty until M3-14 — and none is
/// invented.
/// </para>
/// </remarks>
/// <param name="ChapterId">`02` §1's chapter number, from 1.</param>
/// <param name="Tier">`02` §2's <c>tierId</c>, the difficulty the run is played on.</param>
public sealed record StartRunCommand(int ChapterId, DifficultyTier Tier) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>ROLL_DICE</c> — roll. The server answers with the face, the movement and the
/// landing outcome in one command (`14` §2.4: the die roll is never predicted).
/// </summary>
public sealed record RollDiceCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>USE_REROLL</c> — spend one reroll charge on the face just shown (`02` §3).
/// </summary>
public sealed record UseRerollCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CHOOSE_FORK</c> — pick a branch at a junction. Always required at one
/// (`02` §3, `03` §1.1's mid-move junction pause).
/// </summary>
/// <param name="BranchIndex">
/// The chosen branch's position in the server-issued branch list. An index rather than a node
/// identity: the pause hands the client the branches on offer, and M3-02's movement engine owns
/// what a branch resolves to.
/// </param>
public sealed record ChooseForkCommand(int BranchIndex) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>RESOLVE_TILE</c> — acknowledge or advance the pending tile resolution
/// (`03` §2's fourteen resolvers).
/// </summary>
public sealed record ResolveTileCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>PICK_PERK</c> — take one of the three drafted options (`06` §1).
/// </summary>
/// <param name="OptionIndex">The chosen option's position in the server-issued draft.</param>
public sealed record PickPerkCommand(int OptionIndex) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>REROLL_DRAFT</c> — redraw the perk draft (`06` §2's reroll economy).
/// </summary>
public sealed record RerollDraftCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SKIP_DRAFT</c> — take none of the offered perks (`06` §2's skip economy).
/// </summary>
public sealed record SkipDraftCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SHOP_BUY</c> — buy a slot from the <b>in-run</b> shop, paid in run-local Gold
/// (`03` §7).
/// </summary>
/// <remarks>
/// ⚠️ Distinct from the meta <see cref="ShopPurchaseCommand"/>, and the distinction is the reason
/// both names are in the registry: this one spends the run's Gold (milestone assumption <b>A3</b>
/// makes <c>GOLD</c> run-scoped) and dies with the run, while <c>SHOP_PURCHASE</c> spends the
/// player's wallet on the meta shop of `10` §5. Two shops, two currencies, two lifetimes.
/// </remarks>
/// <param name="ShopSlotIndex">The slot's position among `03` §7's four.</param>
public sealed record ShopBuyCommand(int ShopSlotIndex) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>SHOP_REFRESH</c> — restock the in-run shop (`03` §7's refresh economy).
/// </summary>
public sealed record ShopRefreshCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>EVENT_CHOOSE</c> — take one outcome of an event card (`03` §5, `19` A).
/// </summary>
/// <param name="ChoiceIndex">The chosen outcome's position in the server-issued card.</param>
public sealed record EventChooseCommand(int ChoiceIndex) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>MINIGAME_SUBMIT</c> — assert a minigame outcome. `03` §6.2 makes this a
/// <b>documented exception</b> to `14` §2.1's server authority: <c>MG_TIMING_BAR</c> and
/// <c>MG_MEMORY_RUNE</c> are genuine skill inputs, so the client asserts the result and the server
/// validates <em>legality only</em> — a valid outcome tier, exactly one submission per tile, rate
/// limits.
/// </summary>
/// <remarks>
/// ⚠️ <b><paramref name="Result"/> is the thinnest field in the whole registry and is typed to say
/// so.</b> `03` §6.1's four reward tables are each an <em>ordered tier</em> — Bronze/Silver/Gold,
/// 0–3 hits, loss/2–1/2–0, fail/round-1/both — and `03` §6.2 says what the server checks is
/// <em>"a valid outcome tier"</em>. So an integer tier is the shape the document gives. Which
/// integer means which tier, and which of the four minigames the tier is read against, is
/// <b>M3-10's</b> encoding and is deliberately not decided here (steering <b>S6</b>).
/// </remarks>
/// <param name="MinigameId">`03` §6's <c>MG_*</c> identifier. Typed by M3-10.</param>
/// <param name="Result">The claimed outcome tier of `03` §6.1's table for that minigame.</param>
public sealed record MinigameSubmitCommand(string MinigameId, int Result) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CAMPFIRE_CHOOSE</c> — take one of the campfire's options (`03` §7a).
/// </summary>
/// <param name="ChoiceIndex">The chosen option's position in the server-issued list.</param>
public sealed record CampfireChooseCommand(int ChoiceIndex) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>START_BATTLE</c> — enter the fight. The server answers with the
/// <c>battleSeed</c> and the build snapshot (`14` §2.4), and the client simulates locally from
/// both.
/// </summary>
/// <remarks>
/// It carries no payload and takes no seed: `14` §8.1 derives <c>battleSeed</c> from the run's own
/// committed <c>runSeed</c> through <c>SeedDerivation.BattleSeed</c>, so there is nothing for the
/// client to name and nothing for <c>GameContext.CommandSeed</c> to carry (`30` §3 puts that seed
/// on meta commands only).
/// </remarks>
public sealed record StartBattleCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>CONFIRM_BATTLE_RESULT</c> — the client reports the hash of the fight it
/// simulated; the server compares it against its own (`14` §2.4). Divergence resyncs the client to
/// the server's result.
/// </summary>
/// <param name="LogHash">
/// `05` §7 / `14` §16.6's combat-log hash, as text. Typed by <b>M2-15</b>, which authors
/// <c>LogHash</c> together with the log format it is taken over.
/// </param>
public sealed record ConfirmBattleResultCommand(string LogHash) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>REVIVE</c> — once per run (`02` §6). The battle restarts, and `14` §8.1 makes
/// that reproducible by construction: a revived battle re-derives the same <c>battleSeed</c> and
/// draws from index 0 again.
/// </summary>
public sealed record ReviveCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>USE_CONSUMABLE</c> — board-only, never during combat (`03` §7, `04` §3).
/// </summary>
/// <param name="ConsumableId">
/// The consumable to use. Typed by <b>M3-08</b>, which authors the consumables together with the
/// Escape Rope's arming and skip semantics — the reason `30` §4's <c>held consumables</c> is a
/// <c>GapRegister</c> entry rather than a field on <c>Run</c>.
/// </param>
public sealed record UseConsumableCommand(string ConsumableId) : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>END_RUN</c> — bank the run's rewards and close it (`02` §5's run-end payout).
/// </summary>
public sealed record EndRunCommand : GameCommand;

/// <summary>
/// 🔒 `14` §2.3 <c>ABANDON_RUN</c> — leave the run without completing it.
/// </summary>
/// <remarks>
/// Registered separately from <see cref="EndRunCommand"/> because the two produce different
/// payouts: `02` §5's completion multipliers and first-clear bonuses are <c>END_RUN</c>'s, and an
/// abandoned run forfeits them. One command with a flag would have made the difference a payload
/// value rather than a wire name, and `14` §2.3 lists two rows.
/// </remarks>
public sealed record AbandonRunCommand : GameCommand;
