using System.Text.RegularExpressions;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 The repo's register of design-document surface that has been <b>deliberately not built
/// yet</b>, with the milestone that builds it and a decidable predicate that expires the entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> `30` §7 specifies six domain events; M1-03 authored one of them,
/// because four name payload types that do not exist (<c>DieFace</c>, <c>TileType</c>,
/// <c>GearInstance</c>, <c>GuildId</c>) and one has no producer until <c>LuckService</c>.
/// Steering <b>S6</b> forbids inventing any of them to make an event compile: a guessed type at
/// the bottom of the dependency graph is what three later milestones would then build on. But a
/// hole that is merely <i>not written</i> is indistinguishable from a hole nobody noticed, and it
/// stays that way until someone re-reads the spec. This register is the difference.
/// </para>
/// <para>
/// <b>The mechanism, and it fails in four directions.</b>
/// </para>
/// <list type="number">
///   <item><b>Stale.</b> An entry whose <see cref="Gap.WaitsFor"/> type now exists — or whose own
///   <see cref="Gap.Subject"/> has been authored — fails the build. Removing an entry is
///   <i>forced</i> on the commit that makes it untrue, not remembered at some later kickoff.</item>
///   <item><b>Undeclared.</b> Every name in <see cref="Surfaces"/> — the closed transcription of
///   what a spec section enumerates — must be either authored in its namespace or carried by an
///   entry here. A sixth event quietly dropped from `30` §7 fails rather than vanishing. This is
///   the direction that is usually skipped, and it is the one that makes the register more than a
///   comment.</item>
///   <item><b>Unanchored.</b> The converse, and the analogue of <c>test-suites.json</c>'s rule 5
///   ("a renamed or deleted project must take its exemption with it"): an entry whose
///   <see cref="Gap.Subject"/> appears in no transcription is deferring something no specification
///   asks for. It can never be <i>satisfied</i>, only deleted by hand — which is the state this
///   register replaces. It is what held <b>M1-02</b> to transcribing `14` §2.3's command inventory
///   into <see cref="Surfaces"/> rather than only adding entries to <see cref="Deferred"/>; see the
///   note below for what that task discharged and what it deliberately did not.</item>
///   <item><b>Vacuous.</b> Both sets have floors, and both predicates are proven to distinguish a
///   type that exists from one that does not — see <c>GapRegisterTests</c>. A register whose
///   subject set can silently become empty is steering <b>S3</b>'s failure mode, and the three
///   rules above would report success forever.</item>
/// </list>
/// <para>
/// 🔒 <b>One register for the repository, not one per milestone</b> (steering S4: one mechanism per
/// repo). It is named <c>GapRegister</c> rather than <c>M1GapRegister</c> for exactly that reason —
/// a milestone-stamped name invites an <c>M2GapRegister</c> beside it, and then the "is every gap
/// declared?" question has two answers. Each task adds to <see cref="Deferred"/> and
/// <see cref="Surfaces"/>; none adds a sibling file.
/// </para>
/// <para>
/// 🔒 <b>M1-02 discharged the `14` §2.3 promise, and it discharged one half of it and refused the
/// other.</b> M1-06 landed the abstract <c>GameCommand</c>, the dispatch table and `30` §4.1's
/// <c>WorldSlice</c> row (the <c>GuildView</c> and <c>GhostSnapshot</c> entries below), and wrote
/// that M1-02 would both transcribe the command inventory into <see cref="Surfaces"/> <em>and</em>
/// give every <c>CommandDispatch.Deferred</c> row an entry in <see cref="Deferred"/>.
/// </para>
/// <para>
/// The transcription is below — <b>and all forty-nine subjects are authored</b>. What that
/// discharges is `14` §2.3's <b>inventory</b>: M1-02 declared every row of the registry, so the
/// table's <em>first</em> column has nothing left to defer. ⚠️ Its <b>payload</b> column does — some
/// twenty fields whose value sets belong to M2-15, M3-10, M4-02, M4-03, M4-06, M4-09, M5-08, M12
/// and M15-03 are carried as <c>int</c>/<c>string</c> today — and that deferral is written up in
/// <c>SlayIdleRepeat.Core.Commands.CommandPayload</c>, with the two entries below that already
/// expire on the right commits (<c>Inventory</c>/M4-03 and <c>ContainerShelf</c>/M4-02) naming the
/// payloads they cover.
/// </para>
/// <para>
/// 🔒 <b>The per-command entries were not written, and that is forced rather than chosen.</b>
/// <see cref="Expired"/>'s second arm fires on <c>IsPresentInCore(gap.Subject)</c> — so
/// <c>Gap("RollDiceCommand", …)</c> would fail the build <em>on the commit that added it</em>,
/// because M1-02 authored the command. A per-command entry is unrepresentable in this register.
/// (The softer argument holds as well: forty-nine <see cref="Gap.WaitsFor"/> names invented on
/// behalf of milestones that have not chosen them is the same move this file's own
/// <see cref="Surfaces"/> remarks refuse for the five <c>Player</c>-contents types M4-05 owns —
/// "naming five types five unwritten milestones have not chosen is the invention S6 forbids,
/// dressed as bookkeeping".) What each deferred command <em>does</em> carry is its owning task, on
/// its dispatch row, beside its wire name and its <c>CommandKind</c> — the one place all three are
/// declared, and the one place a reader looking at the command finds it.
/// </para>
/// <para>
/// It joins three older instances of the same shape, and is modelled on the last of them:
/// <c>build/ci/test-suites.json</c>'s <c>knownEmpty</c> block,
/// <c>ContentLoader.SchemasAwaitingContent</c>, and
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrderPin.cs</c> — the only one
/// of the three that also checks its own permanent vacuity.
/// </para>
/// <para>
/// ⚠️ <b>The known limit, stated so nobody assumes otherwise</b> (steering S4's own caveat). What
/// is decidable here is the <i>shape</i>: the type arrived, the subject was authored, the owner or
/// the reason is missing. What is not decidable is an entry whose written <i>reason</i> stopped
/// being true while its predicate still holds — the same hole
/// <c>test-suites.json</c>'s <c>$knownGapInThisMechanism</c> documents. Re-read these entries at
/// each milestone kickoff; CI is not doing it for you.
/// </para>
/// </remarks>
internal static class GapRegister
{
    /// <summary>
    /// A piece of specified surface that has not been built, the task that builds it, and the type
    /// whose arrival makes this deferral stale.
    /// </summary>
    /// <param name="Subject">
    /// The simple name of the thing deferred, as the specification writes it — e.g. <c>DiceRolled</c>.
    /// </param>
    /// <param name="Owner">The milestone task that authors it, e.g. <c>M3-04</c>.</param>
    /// <param name="WaitsFor">
    /// 🔒 The decidable predicate: the simple name of a <c>Core</c> type that <b>must not yet
    /// exist</b>. For most entries this is the payload type without which the subject cannot be
    /// written at all; where the payload already compiles, it is the <i>producer</i> that gives the
    /// subject meaning. Either way, the day it appears is the day this entry is wrong, and the
    /// build says so.
    /// </param>
    /// <param name="Why">Why it is deferred rather than written. Something a later reader can falsify.</param>
    internal sealed record Gap(string Subject, string Owner, string WaitsFor, string Why);

    /// <summary>
    /// A closed list of names one specification section enumerates, and the <c>Core</c> namespace
    /// an authored one must live in.
    /// </summary>
    /// <remarks>
    /// This is what makes the <b>undeclared</b> direction decidable. Without it the register could
    /// only ever check the entries it already has, which is a comment with a unit test around it.
    /// </remarks>
    /// <param name="Citation">The document section, e.g. <c>30 §7</c>.</param>
    /// <param name="Namespace">Where an authored subject lives, e.g. <c>SlayIdleRepeat.Core.Events</c>.</param>
    /// <param name="Subjects">Every name the section enumerates, transcribed.</param>
    internal sealed record SpecifiedSurface(string Citation, string Namespace, IReadOnlyList<string> Subjects);

    /// <summary>
    /// 🔒 Everything specified and not yet built. Each entry expires by itself.
    /// </summary>
    internal static readonly Gap[] Deferred =
    {
        new("DiceRolled", "M3-04", "DieFace",
            "30 §7 writes it as (int Sequence, DieFace Face). DieFace is the die-face vocabulary of 04, " +
            "authored by M3-04. Inventing one here would put a guessed type under the dice, board and " +
            "combat milestones that all read it (S6)."),

        new("TileResolved", "M3-03", "TileType",
            "30 §7 writes it as (int Sequence, TileType Type, NodeId Node). Both payload types are the " +
            "board's (03), authored by M3-03. Keyed on TileType; NodeId lands in the same task."),

        new("GearGranted", "M4-03", "GearInstance",
            "30 §7 writes it as (int Sequence, GearInstance Item, SourceClass Source, bool FromPity). " +
            "GearInstance is the gear aggregate's (08), authored by M4-03. Keyed on GearInstance rather " +
            "than on SourceClass deliberately: SourceClass arrives earlier, with M4-01's LuckService, and " +
            "the event still could not be written on that day."),

        new("PityCounterAdvanced", "M4-01", "LuckService",
            "30 §7 writes it as (int Sequence, string Key, int Value), which compiles today — and that is " +
            "the trap. The payload is a SKETCH, not a ruling: the pity-key vocabulary is LuckService's " +
            "(24 §11), and nothing in M1 can emit one. Authoring it now would freeze 'string Key' before " +
            "the milestone that knows whether a key is a closed enum, a primitive or a content id, and " +
            "would leave a public type with no producer, no consumer and no rule watching it. Keyed on " +
            "the producer rather than on a payload type, because the payload is not what is missing."),

        new("GuildContribution", "M14", "GuildId",
            "30 §7 writes it as (int Sequence, GuildId Guild, string CounterId, long Delta). GuildId is " +
            "M14's. Deferred, not dropped: guilds ship in v1 (milestone kickoff, 2026-08-11)."),

        // ---------------------------------------------------------------- M1-04, 30 §4
        //
        // Four things 30 §4 puts on the Player aggregate that M1-04 authored the aggregate WITHOUT.
        // None of them has an element type yet, and one of them has no decided content at all.

        new("Inventory", "M4-03", "GearInstance",
            "🔒 M1-02 ALSO HANGS SIX COMMAND PAYLOADS ON THIS ENTRY. 14 §2.3's payload column names " +
            "gear instance ids and a gear SLOT that no type expresses today, so EquipCommand, " +
            "EnhanceCommand, SalvageCommand, SetFocusCommand, ReforgeItemCommand and " +
            "RetuneItemCommand carry them as text. The commit that declares GearInstance retypes " +
            "those six in the same change — otherwise M4-03's vocabulary and the wire's are two " +
            "vocabularies for one concept, which is 30 §11.6's failure mode one layer in. " +
            "30 §4 lists 'inventory, gear instances' among Player's contents, and 08 §5 caps it at 400 " +
            "slots. Neither can be stored before the thing being stored exists: GearInstance carries " +
            "quality, chapterOrigin, a mercy counter, affixes and a lock (08 §2-3), and every one of " +
            "those is a decision M4-03 makes. A List<something> authored now would freeze the item " +
            "shape under M4-04's forge and M4-05's capacity curve (S6). Keyed on GearInstance because " +
            "the shelf and the slots are the same missing type."),

        new("ContainerShelf", "M4-02", "ContainerClass",
            "🔒 M1-02 ALSO HANGS THREE COMMAND PAYLOADS ON THIS ENTRY: OpenChestCommand, " +
            "OpenEggCommand and OpenCrateCommand each carry a containerId as text, because the thing " +
            "being addressed does not exist. The commit that declares ContainerClass retypes those " +
            "three in the same change. " +
            "30 §4 lists 'unopened containers (24 §4.0)' on Player, and 24 §4.0 is explicit that chests, " +
            "Pet Eggs and Mount Crates are STORED OBJECTS rather than instant grants, on an uncapped " +
            "shelf. What is missing is the class vocabulary — CHEST_STANDARD / CHEST_PREMIUM / " +
            "CHEST_APEX / EGG_PET / CRATE_MOUNT — whose per-class ladders, soft-pity slopes and " +
            "contents tables are all M4-02's. Keyed on ContainerClass rather than on GearInstance: " +
            "eggs and crates yield pets and mounts, so gear arriving first would not make this " +
            "writable."),

        new("PityCounters", "M4-01", "LuckService",
            "30 §4 lists 'all pity counters (24)' on Player, and 24 §1.1 requires them to be " +
            "server-owned, visible and never reset. The storage is trivial; the KEY SPACE is not, and " +
            "it is the same trap PityCounterAdvanced is deferred for — the pity-key vocabulary belongs " +
            "to LuckService (24 §11, ten source classes in data/luck.json). A map authored now would " +
            "freeze whether a key is an enum, a primitive or a content id before M4-01 knows. Keyed on " +
            "the producer, because the payload is not what is missing."),

        new("FeatCounters", "M4-13", "FeatDefinition",
            "30 §4 lists 'Feats and Renown (28 D)' on Player, and 28 D2 requires the counters to be " +
            "LIFETIME aggregate state rather than a projection over the event stream, incremented " +
            "inside Apply. That makes them the sharpest S6 case in this register and NOT merely early: " +
            "28 D2.2 catalogues 140 feats, but 16 O29 defers what each counter MEASURES ('feat counter " +
            "semantics per feat, counters.json') until the M16 kickoff, and 30 §12.7 forbids rebuilding " +
            "a counter after the fact. So the counter SET ITSELF IS UNDECIDED, and a set invented in " +
            "M1-04 would be permanently unfixable the day it ships. M4-13 lands the counters early " +
            "precisely because retroactivity needs them to predate the M16 feature; it cannot land " +
            "them before O29 names them. Keyed on FeatDefinition, the type that reads feats.json / " +
            "counters.json and therefore cannot exist until O29 is ruled."),

        // ---------------------------------------------------------------- M1-05, 30 §4 + 02 §1.1
        //
        // 30 §4's Run row enumerates ten things. M1-05 authored the aggregate with FIVE of them —
        // position, HP, run Gold, RNG stream positions, per-run ad uses — and these five without.
        // Plus the run's PHASE, which 02 §1.1 draws and which is deferred for a sharper reason than
        // "no element type yet": nobody has ruled which of its states are server-side.

        new("Board", "M3-01", "NodeId",
            "30 §4 lists 'Board' first among the Run aggregate's contents, and M3-01 authors the DAG " +
            "generator plus GenerateBoard. A board is a graph of nodes, so it cannot be stored before " +
            "node identity exists. 🔒 THIS ENTRY ALSO CARRIES A DEFERRED INVARIANT, which is why it " +
            "matters more than a missing field: 30 §11.5 names 'a run's position is a valid node' as " +
            "an invariant of this aggregate, and it CANNOT be implemented today. Run.Position " +
            "therefore stores the linear index and validates only 03 §1.1's authored floor — the " +
            "virtual trailhead at -1, where every run stands before its first roll — deliberately, " +
            "because a range check invented here (0..42, say) would be a PARTIAL invariant wearing " +
            "the real one's name and would be trusted as such by every rule downstream."),

        new("DraftedPerks", "M3-06", "PerkDefinition",
            "30 §4 lists 'drafted perks' on Run. 06 §5 forbids per-perk code — a perk IS DSL data — so " +
            "the element type is M3-07's 98-perk catalogue read through M3-06's draft. Freezing a list " +
            "element type now would put a guessed perk shape under both (S6). Keyed on " +
            "PerkDefinition rather than on the draft handler, because the shape is what is missing."),

        new("HeldConsumables", "M3-08", "ConsumableDefinition",
            "30 §4 lists 'held consumables and the armed Escape Rope flag (03 §7.1)'. Both are M3-08's " +
            "('consumables incl. Escape Rope arming/skip semantics + USE_CONSUMABLE legality'). The " +
            "armed flag is deferred WITH them rather than beside them, and that is the ruling: it is " +
            "one consumable's state, not a second field on the aggregate — storing a bool for it now " +
            "would fix the Escape Rope's mechanics before M3-08 has chosen them."),

        new("PendingFork", "M3-02", "NodeId",
            "30 §4 lists 'pending fork choice (mid-move junction pause, 03 §1.1)'. A pending choice " +
            "names the junction node and the branches on offer, so it cannot be stored before node " +
            "identity exists; the pause itself is M3-02's movement engine. ⚠️ It shares its predicate " +
            "with the Board entry DELIBERATELY: both become writable on the same day, and pointing " +
            "this one at a different type to make the register look more granular would be buying " +
            "silence with a predicate that does not describe the reason."),

        new("Curses", "M3-11", "CurseDefinition",
            "30 §4 lists 'curses' on Run. 19 E catalogues twelve of them and M3-11 owns the rules " +
            "engine around them — no stacking, paired rewards, the AD_SKIP_CURSE hook and mount " +
            "immunity. A held-curse list authored now would freeze the curse shape under all four of " +
            "those rules before any of them is written (S6)."),

        new("RunPhase", "M3-05", "TileType",
            "02 §1-3's run state machine. Do NOT invent the state set: 02 §1.1's diagram is a CLIENT " +
            "PRESENTATION machine (0.8 s die animation, banners) while 14 §2.3's ROLL_DICE answers " +
            "face, movement and landing in ONE command — so which of its nine states are server-side " +
            "aggregate state is a ruling M3-05 makes together with the tile resolvers. Keyed on " +
            "TileType because RESOLVE_TILE branches by it. ⚠️ THE CONSEQUENCE, STATED: without a " +
            "phase, Apply cannot produce 14 §16.2's RUN_ALREADY_ENDED or ILLEGAL_STATE, and M3-05 " +
            "pays a SchemaVersion bump to add it. That cost is named here rather than discovered."),

        // ---------------------------------------------------------------- M1-06, 30 §4.1
        //
        // 30 §4.1 sketches WorldSlice(Player, Run?, GuildView?, GhostSnapshot?). M1-06 built the
        // first two; milestone assumption A4 froze the M1 shape at that pair.
        //
        // 🔒 THE REASON THESE TWO ARE CHEAPER TO DEFER THAN ANY OTHER ENTRY IN THIS REGISTER, and it
        // is worth stating because it is what made A4 a decision rather than a shortcut: a
        // WorldSlice is NOT A PERSISTED SNAPSHOT. Nothing hashes it, no row stores it, and
        // CanonicalStateWriter never sees it — so adding a nullable member later costs no
        // SnapshotSchema.SchemaVersion bump and breaks no stored state, which is the cost every
        // other deferral in this file is weighed against. An empty placeholder GuildView authored
        // now would buy nothing and would be the plausible-looking hole S6 forbids, sitting under
        // M12's duel and M14's guild rules for two milestones.

        new("GuildView", "M14-01", "GuildId",
            "30 §4.1's third WorldSlice member, and 30 §5 fixes its shape before its contents: it is " +
            "a READ-ONLY PROJECTION, because 27 §11 makes guild quest counters and Guild Boss damage " +
            "the game's only contended writes — up to 30 simultaneous writers — so the domain returns " +
            "a GuildContribution INTENT and the Application layer applies it as an atomic increment. " +
            "A projection of a guild cannot be written before the guild aggregate has an identity, " +
            "which is M14-01's. Keyed on GuildId, the same predicate as GuildContribution: both " +
            "become writable on the same day, and pointing this one at a different type to make the " +
            "register look more granular would buy silence with a predicate that does not describe " +
            "the reason. IsolationTests.GuildView_is_a_read_only_projection is already written " +
            "against it and holds vacuously until then."),

        new("GhostSnapshot", "M12-01", "GhostId",
            "30 §4.1's fourth WorldSlice member — the stored opponent a duel is fought against, and " +
            "'duels only'. 11 §2 makes a Ghost an immutable server-generated snapshot of a player's " +
            "PvP loadout: ghostId, rating, and a resolved build of gear, pets, mount, talents and " +
            "PvP perks. It is keyed on GhostId — the identity M12-01 authors together with the " +
            "snapshot — rather than on GearInstance, and that choice is the point: gear arrives at " +
            "M4-03 and would expire this entry six milestones early, while the ghost still could not " +
            "be written because pets (M4-07), mounts (M4-08) and talents (M4-06) are not there " +
            "either. A predicate that expires before its subject is writable is worse than none."),

        // ---------------------------------------------------------------- M1-08, 30 §2.3
        //
        // 30 §2.3 enumerates the boundaries lazy catch-up rolls forward across. M1-08 built the
        // mechanism and every boundary whose state exists; these three act on state no milestone has
        // authored, so GameRules.AdvanceTime does nothing for them and says so in its own remarks.
        // The arithmetic — what is built, what is ruled off, what is left — is on the Surfaces
        // transcription below, where a reader checking the register against the document will look.

        // 🔒 M1-09 REWROTE BOTH REASONS BELOW, and that is the point of re-reading a register at a
        // kickoff (S4's known limit). Each entry used to say the redraw "waits for BEGIN_SESSION's
        // seed (M1-09)" — which now reads as a promise M1-09 kept, when in fact M1-09 built the SEAM
        // and deferred the DRAWS to the same task that already owned the state. Both entries now
        // carry BOTH halves, so a reader can tell what exists from what does not without diffing two
        // milestones. Neither predicate moved: QuestDefinition and ShopOffer are still the types that
        // must not yet exist, and both are still M4-09's.

        new("QuestSlate", "M4-09", "QuestDefinition",
            "30 §2.3 makes QUEST EXPIRY one of the 05:00 UTC catch-up boundaries, and 19 B draws the " +
            "day's three quests from a 20-quest pool under BEGIN_SESSION's seed. Expiring a slate " +
            "needs the slate: what a quest IS — its objective vocabulary, its reward shape, whether a " +
            "reroll is a fourth draw or a replacement — is all M4-09's, and a list authored now would " +
            "freeze it under the draw rules, the reroll rules and the 3-of-3 chest alike (S6). Keyed " +
            "on QuestDefinition, the type that reads the pool, rather than on the slate's own " +
            "container: the container is trivial and the element is what is missing. " +
            "🔒 M1-09 BUILT THE SEAM AND DEFERRED THE DRAW, deliberately: Handlers/BeginSession takes " +
            "30 §2.3's day-draw seed through HandlerInput.MetaDraws on the first BEGIN_SESSION of the " +
            "game day, proven deterministic in CommandSeed and proven to move no run stream position, " +
            "and draws NOTHING from it. Three things are missing, not one — the slate element above, " +
            "content/quests/ (empty, no schema), and a 14 §8.1 STREAM NAME: that registry is complete " +
            "for the run streams and says a system needing randomness 'draws from one of these " +
            "streams or gets a new row here', and no row names a quest draw. M4-09 amends 14 §8.1, " +
            "adds the RngStreams row and takes the draw; nothing else has to move. Catch-up still " +
            "owns only the EXPIRY."),

        new("DailyShopStock", "M4-09", "ShopOffer",
            "30 §2.3 gives catch-up the Daily tab's STOCK EXPIRY and explicitly leaves the REDRAW to " +
            "the day's BEGIN_SESSION seed — the two halves land in different milestones and the " +
            "expiry half is the one that must not wait for a login. 10 §5.1 authors the tab as a " +
            "6-offer block with a draw rule and staples, and none of an offer's shape exists: what is " +
            "sold, at what price, in which currency, and whether a bought offer is removed or marked. " +
            "Expiring stock needs the stock. Keyed on ShopOffer rather than on the wallet model M4-09 " +
            "also lands, because the offer is the thing being expired. " +
            "🔒 M1-09 BUILT THE SEAM AND DEFERRED THE DRAW — the same seam, the same command and the " +
            "SAME SEED as QuestSlate above, which is 30 §2.3's own claim that 'the quest slate and " +
            "the Daily shop tab are the only daily actions that consume randomness, both derive from " +
            "this one seed'. So the two deferrals are not independent: whichever of them M4-09 lands " +
            "first fixes the seed's consumption order for the other, and taking them in one change is " +
            "the cheaper reading. This entry also needs a 14 §8.1 stream row that does not exist."),

        new("EventWindow", "M13-01", "EventDefinition",
            "30 §2.3 lists 'event-window state' among the boundaries catch-up rolls forward, and 26 §4 " +
            "authors the windows themselves — start, end, membership, the per-event feature flag and " +
            "the mid-flight kill. NOTHING IN M1 CAN OPEN OR CLOSE ONE: there is no event package, no " +
            "schema and no server-side window resolution until M13-01, so a window type authored here " +
            "would be a field catch-up read and never wrote. Keyed on EventDefinition, the type that " +
            "reads 26 §2's package — a window without its event is a date range with no meaning."),
    };

    /// <summary>
    /// 🔒 The closed transcriptions the <b>undeclared</b> direction is checked against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed by hand from the design document, which is the only way this can work: a list
    /// derived from the code would say the code is complete because the code says so.
    /// </para>
    /// <para>
    /// ⚠️ <b>The second entry is a transcription of a <i>fragment</i>, and says so in its citation.</b>
    /// `30` §4's <c>Player</c> row enumerates eighteen things — <i>"Profile, Legend Level, all 8
    /// currencies, inventory, unopened containers, gear instances, pets, mounts, talents, presets,
    /// unlocks, FTUE progress, Energy + Reserve, all pity counters, Feats and Renown, daily/weekly
    /// counters, ad caps, entitlement"</i> — and this mechanism cannot hold the whole row. It
    /// matches a <b>type simple name</b> against a <c>Core</c> namespace, and most of those items
    /// are not type names: <c>Profile</c>, <c>Legend Level</c> and <c>daily/weekly counters</c> are
    /// <em>built</em>, as fields of <c>Player</c> rather than as types called that, so a verbatim
    /// transcription would report them undeclared forever. Two more are built and would read the
    /// same way: <c>FTUE progress</c> (<c>Player.FtueBeat</c> + <c>FtueCompletedAtUtc</c>, `19` D7)
    /// and <c>Energy + Reserve</c> (<c>Player.Energy</c>, `28` C). <c>ad caps</c> is built too — it
    /// is the daily counter mechanism, which exists precisely for it. And <c>entitlement</c> is
    /// <b>ruled off</b> the aggregate entirely (`30` §3 and `12` §2.1 put it on the session), so it
    /// is neither built here nor deferred.
    /// </para>
    /// <para>
    /// What is left genuinely absent and genuinely type-shaped is <b>ten</b>: the four below, plus
    /// pets (M4-07), mounts (M4-08), talents (M4-06), presets and unlocks (M4-10). Those five are
    /// <em>not</em> transcribed here and that is the honest limit of this entry — each would need a
    /// <see cref="Gap.WaitsFor"/> type name, and naming five types five unwritten milestones have
    /// not chosen is the invention S6 forbids, dressed as bookkeeping. <b>M4-05 owns closing
    /// this</b>: it is the first task that touches enough of `30` §4's row (inventory capacity) to
    /// know what those types are called.
    /// </para>
    /// <para>
    /// ⚠️ <b>The third entry is a transcription of a fragment too, and the arithmetic is stated so a
    /// reader can check it.</b> `30` §4's <c>Run</c> row enumerates <b>ten</b> things — <i>"Board,
    /// position, HP, run Gold, drafted perks, held consumables and the armed Escape Rope flag
    /// (`03` §7.1), pending fork choice (mid-move junction pause, `03` §1.1), curses, RNG stream
    /// positions, per-run ad uses"</i>. M1-05 <b>built five</b>: <c>position</c> →
    /// <c>Run.Position</c>, <c>HP</c> → <c>Run.CurrentHp</c>/<c>Run.MaxHp</c>, <c>run Gold</c> →
    /// <c>Run.Gold</c>, <c>RNG stream positions</c> → <c>Run.RngStreamPositions</c>, and
    /// <c>per-run ad uses</c> → <c>Run.AdUses</c>. Those five are fields of <c>Run</c> rather than
    /// types called that, so transcribing them here would report them undeclared forever — which is
    /// exactly why the <c>Player</c> entry above is a fragment as well. The <b>five</b> that are
    /// genuinely absent and genuinely type-shaped are the five transcribed below, and the armed
    /// Escape Rope flag is deferred <em>inside</em> <c>HeldConsumables</c> rather than as a sixth,
    /// because it is one consumable's state. Five built plus five deferred is the row.
    /// </para>
    /// <para>
    /// The fourth entry is the run's <b>phase</b>, and it is separate from the row above because
    /// `30` §4 does not list it: it comes from `02` §1.1's state machine, and it lands in
    /// <c>Primitives</c> rather than <c>Model</c> for the reason <c>FtueBeat</c>'s remarks
    /// record — a public enum under <c>Core/Model/</c> fails
    /// <c>Apply_is_the_only_public_mutation</c> on its <c>value__</c> field.
    /// </para>
    /// </remarks>
    internal static readonly SpecifiedSurface[] Surfaces =
    {
        new("30 §7", Domain.EventsNamespace, new[]
        {
            "DiceRolled",
            "TileResolved",
            "GearGranted",
            "CurrencyChanged",
            "PityCounterAdvanced",
            "GuildContribution",
        }),

        new("30 §4 (the Player-contents row, the four items M1-04 did not build)", Domain.ModelNamespace, new[]
        {
            "Inventory",
            "ContainerShelf",
            "PityCounters",
            "FeatCounters",
        }),

        new("30 §4 (the Run-contents row, the five items M1-05 did not build)", Domain.ModelNamespace, new[]
        {
            "Board",
            "DraftedPerks",
            "HeldConsumables",
            "PendingFork",
            "Curses",
        }),

        new("02 §1.1 (the run state machine)", Domain.PrimitivesNamespace, new[]
        {
            "RunPhase",
        }),

        // 🔒 M1-06. 30 §4.1's WorldSlice is FOUR members and this is the whole row, not a fragment:
        // unlike §4's aggregate-contents rows, every one of the four IS a type name, so all four
        // can be transcribed and the arithmetic is exact — two built (Player, Run, both authored
        // under this namespace and therefore satisfied by the authored half) and two deferred.
        //
        // The namespace is Model rather than the Core root that holds WorldSlice itself, and that is
        // deliberate: IsAuthoredUnder matches by PREFIX, so SlayIdleRepeat.Core.Model reaches
        // Model/Guild/ (where a GuildView would live, beside 30 §11.4's Model/ ├── Guild/) and
        // Model/Snapshots/ (where a GhostSnapshot would, beside the other persisted shapes). The
        // Core root would reach neither, and naming the root would also mean the undeclared
        // direction was checking the wrong place for two types that will never be built there.
        new("30 §4.1 (the WorldSlice members M1-06 did not build)", Domain.ModelNamespace, new[]
        {
            "Player",
            "Run",
            "GuildView",
            "GhostSnapshot",
        }),

        // 🔒 M1-02, `14` §2.3 — THE CANONICAL COMMAND REGISTRY, transcribed whole.
        //
        // 49 rows: 19 run + 30 meta. Counted off the document before the vocabulary was written,
        // rather than taken from a report (steering S9) — the table's own header says "Meta commands
        // (29)" and this repository used to say 48, and the M1 kickoff (2026-08-11) recorded BOTH as
        // miscounts of a correct table. Errata, not a scope change.
        //
        // 🔒 THIS IS THE ONE SURFACE IN THIS ARRAY WITH NO Deferred COMPANION, and that is what it
        // is for. `14` §2.3 says the registry is EXHAUSTIVE — "a command not listed here does not
        // exist" — so the honest transcription is the whole table, and the honest state of it is
        // "all forty-nine authored". The entry earns its place in the OTHER direction: delete a
        // command type, move one out of Core/Commands/, or rename one, and the undeclared check
        // fails naming the row. Nothing else in the architecture suite watches that —
        // Every_command_type_is_handled_by_Apply quantifies over the types that EXIST and says
        // nothing about one that stopped existing.
        //
        // ⚠️ The subjects are TYPE names, not `14` §2.3's SCREAMING_SNAKE wire names, because that is
        // what IsAuthoredUnder can decide; the mapping is the mechanical one (ROLL_DICE ->
        // RollDiceCommand) and the wire names themselves are pinned against a hand-transcribed
        // literal list, in both directions, by SlayIdleRepeat.Core.Tests.CommandVocabularyTests.
        // Two mechanisms over two subject sets: this one watches DECLARATION, that one watches
        // REGISTRATION, and a command can lose either without losing the other.
        new("14 §2.3 (the canonical command registry — 19 run + 30 meta)", Domain.CommandsNamespace, new[]
        {
            // The 19 run commands, in the table's order.
            "StartRunCommand",
            "RollDiceCommand",
            "UseRerollCommand",
            "ChooseForkCommand",
            "ResolveTileCommand",
            "PickPerkCommand",
            "RerollDraftCommand",
            "SkipDraftCommand",
            "ShopBuyCommand",
            "ShopRefreshCommand",
            "EventChooseCommand",
            "MinigameSubmitCommand",
            "CampfireChooseCommand",
            "StartBattleCommand",
            "ConfirmBattleResultCommand",
            "ReviveCommand",
            "UseConsumableCommand",
            "EndRunCommand",
            "AbandonRunCommand",

            // The 30 meta commands, in the table's order.
            "BeginSessionCommand",
            "SkipFtueCommand",
            "EquipCommand",
            "MergeCommand",
            "EnhanceCommand",
            "SalvageCommand",
            "SpendTalentCommand",
            "RespecCommand",
            "LevelPetCommand",
            "AscendPetCommand",
            "EquipPetCommand",
            "EquipMountCommand",
            "ClaimQuestCommand",
            "RerollQuestCommand",
            "ClaimAdRewardCommand",
            "ClaimCalendarCommand",
            "ClaimInboxCommand",
            "SpinWheelCommand",
            "SetFocusCommand",
            "ReforgeItemCommand",
            "RetuneItemCommand",
            "SavePresetCommand",
            "ApplyPresetCommand",
            "ShopPurchaseCommand",
            "OpenChestCommand",
            "OpenEggCommand",
            "OpenCrateCommand",
            "UploadGhostCommand",
            "StartDuelCommand",
            "SubmitDuelCommand",
        }),

        // 🔒 M1-08, `30` §2.3 — THE LAZY-CATCH-UP BOUNDARIES, and this is a FRAGMENT, exactly like
        // the two aggregate-contents rows above. The section enumerates what AdvanceTime rolls
        // forward across: "Energy regeneration accrual, the 05:00 UTC daily resets (quest expiry,
        // wheel free-spin, ad caps, dungeon entries, daily shop stock expiry), weekly boundaries,
        // Plus expiry, event-window state". Most of those are not type names, so the arithmetic is
        // written out here rather than transcribed verbatim — the same reason the Player row is a
        // fragment, and the same honesty bar.
        //
        // BUILT (M1-08, and therefore not deferred):
        //   · Energy regeneration accrual — Player.Energy + Player.EnergyAnchorUtc, accrued through
        //     EnergyMath.Accrue on every command.
        //   · the 05:00 UTC daily reset MECHANISM — Player.DailyPeriodStartUtc + the daily counters,
        //     driven by GameRules.AdvanceTime across every boundary crossed since the last command.
        //     ⚠️ AD CAPS, DUNGEON ENTRIES AND THE WHEEL'S FREE SPIN ARE BUILT WITH IT and are
        //     deliberately NOT deferred below: each is a daily counter, and the counter mechanism is
        //     precisely what they are. This register's own Player-contents note already records it
        //     ("ad caps is built too — it is the daily counter mechanism"). Deferring something that
        //     is built is an entry Expired fires on, or worse, a promise about nothing.
        //   · weekly boundaries — Player.WeeklyPeriodStartUtc + the weekly counters, on A2's Monday.
        //
        // RULED OFF (neither built nor deferred, exactly as `entitlement` is ruled off 30 §4's
        // Player-contents row):
        //   · PLUS EXPIRY. 30 §3 and 12 §2.1 put entitlement on the SESSION — Entitlements is a
        //     read-only value the composition root resolves against its own NowUtc — so there is no
        //     aggregate state to roll forward, and a comparison inside AdvanceTime would be an
        //     entitlement branch in the domain (12 §3.2). The ruling is written into
        //     GameRules.AdvanceTime's remarks; there is nothing here for a milestone to close.
        //
        // GENUINELY ABSENT AND TYPE-SHAPED — the three below, and only these three.
        //
        // ⚠️ 14 §16.3's RUN TTL is not here at all, and that is not an omission: it is not one of
        // §2.3's boundaries. Catch-up never touches Run.LastAppliedAtUtc (M1-05's ruling), and
        // EXPIRING a run needs RunPhase, which is already an entry above owned by M3-05 whose Why
        // states this very consequence. A second entry for it would be two promises about one gap.
        new("30 §2.3 (the lazy-catch-up boundaries whose state does not exist)", Domain.ModelNamespace, new[]
        {
            "QuestSlate",
            "DailyShopStock",
            "EventWindow",
        }),
    };

    /// <summary>A milestone task id: <c>M14</c>, or <c>M3-04</c>.</summary>
    private static readonly Regex TaskId = new(@"^M\d{1,2}(-\d{2})?$", RegexOptions.Compiled);

    /// <summary>What a stale entry means, said once.</summary>
    internal const string StaleConsequence =
        "This deferral has expired. Author the subject now, or — if it is still not the right time — " +
        "replace the entry with one whose predicate is a type that genuinely does not exist yet, and say " +
        "why in the commit. Do NOT re-point the entry at an arbitrary later type to buy silence: an " +
        "exemption whose predicate no longer describes the reason it was written is the one failure this " +
        "mechanism cannot detect for you.";

    /// <summary>What an unanchored entry means, said once.</summary>
    internal const string UnanchoredConsequence =
        "A deferral is a promise about a piece of SPECIFIED surface. One whose subject no transcription " +
        "enumerates is a promise about nothing: the undeclared direction cannot see it, so the entry can " +
        "never be satisfied — only deleted by hand, which is the state this register exists to replace. " +
        "Transcribe the section that enumerates the subject into GapRegister.Surfaces in the same commit, " +
        "or delete the entry.";

    /// <summary>What an undeclared subject means, said once.</summary>
    internal const string UndeclaredConsequence =
        "A specification enumerates it, no Core namespace declares it, and nothing here says who will. " +
        "Either author it, or add a GapRegister.Deferred entry naming the owning task and a type that " +
        "must not yet exist. An undeclared gap is indistinguishable from one nobody noticed.";

    /// <summary>True when a type with this simple name exists anywhere in <c>SlayIdleRepeat.Core</c>.</summary>
    internal static bool IsPresentInCore(string simpleName) => Domain.FindInCore(simpleName) is not null;

    /// <summary>True when a type with this simple name is declared under the given <c>Core</c> namespace.</summary>
    internal static bool IsAuthoredUnder(string namespacePrefix, string simpleName) =>
        Domain.CoreTypesUnder(namespacePrefix)
              .Any(t => t.Name.Equals(simpleName, StringComparison.Ordinal));

    /// <summary>
    /// Every entry that has expired: its <see cref="Gap.WaitsFor"/> type has arrived, or its
    /// <see cref="Gap.Subject"/> has been authored anyway. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// Takes its entries as a parameter rather than reading <see cref="Deferred"/>, so the
    /// self-tests can drive it with a deliberately expired entry and prove it bites — without ever
    /// committing one. Same construction as <c>SnapshotFieldOrderPin.Violations</c>.
    /// </remarks>
    internal static IReadOnlyList<string> Expired(IEnumerable<Gap> entries)
    {
        var offenders = new List<string>();

        foreach (var gap in entries)
        {
            if (IsPresentInCore(gap.WaitsFor))
            {
                offenders.Add(
                    $"'{gap.Subject}' is declared deferred to {gap.Owner} until '{gap.WaitsFor}' exists — " +
                    $"and '{gap.WaitsFor}' now exists in SlayIdleRepeat.Core. {StaleConsequence}");
            }

            if (IsPresentInCore(gap.Subject))
            {
                offenders.Add(
                    $"'{gap.Subject}' is declared deferred to {gap.Owner}, but SlayIdleRepeat.Core already " +
                    $"declares it. Delete the entry. {StaleConsequence}");
            }
        }

        return offenders;
    }

    /// <summary>
    /// Every subject a specification enumerates that is neither authored in its namespace nor
    /// carried by an entry. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Expired"/>.</remarks>
    internal static IReadOnlyList<string> Undeclared(
        IEnumerable<SpecifiedSurface> surfaces,
        IEnumerable<Gap> entries)
    {
        var declared = entries.Select(g => g.Subject).ToHashSet(StringComparer.Ordinal);

        return (from surface in surfaces
                from subject in surface.Subjects
                where !IsAuthoredUnder(surface.Namespace, subject)
                where !declared.Contains(subject)
                select $"{surface.Citation} specifies '{subject}'. It is not declared under " +
                       $"{surface.Namespace}, and GapRegister.Deferred does not carry it. {UndeclaredConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry deferring a subject no <see cref="SpecifiedSurface"/> enumerates. Empty means
    /// the register holds.
    /// </summary>
    /// <remarks>
    /// The converse of <see cref="Undeclared"/>, and the direction that keeps the two lists moving
    /// together. Parameterised for the same reason as <see cref="Expired"/>.
    /// </remarks>
    internal static IReadOnlyList<string> Unanchored(
        IEnumerable<SpecifiedSurface> surfaces,
        IEnumerable<Gap> entries)
    {
        var specified = surfaces.SelectMany(s => s.Subjects).ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(gap => !specified.Contains(gap.Subject))
            .Select(gap =>
                $"'{gap.Subject}' is declared deferred to {gap.Owner}, but no GapRegister.Surfaces " +
                $"transcription enumerates it. {UnanchoredConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry that is not well formed: a missing or malformed owning task, a blank predicate,
    /// a blank subject, or no written reason. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// An entry with no owner has no expiry a reader can check, and one with no reason has nothing
    /// to falsify at the next kickoff — the two mitigations that stand in for the reason-went-stale
    /// case the mechanism cannot decide.
    /// </remarks>
    internal static IReadOnlyList<string> Malformed(IEnumerable<Gap> entries)
    {
        var offenders = new List<string>();

        foreach (var gap in entries)
        {
            if (string.IsNullOrWhiteSpace(gap.Subject))
            {
                offenders.Add($"an entry owned by '{gap.Owner}' names no subject.");
            }

            if (!TaskId.IsMatch(gap.Owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{gap.Subject}' names owner '{gap.Owner}', which is not a milestone task id (M14, M3-04). " +
                    "An entry with no owner has no expiry a reader can check.");
            }

            if (string.IsNullOrWhiteSpace(gap.WaitsFor))
            {
                offenders.Add(
                    $"'{gap.Subject}' names no type that must not yet exist, so nothing can expire it. " +
                    "That is a comment, not a register entry.");
            }

            if (string.IsNullOrWhiteSpace(gap.Why) || gap.Why.Length < 40)
            {
                offenders.Add(
                    $"'{gap.Subject}' carries no written reason worth falsifying. The reason going stale " +
                    "while the predicate still holds is the one case CI cannot catch; the text is what a " +
                    "human re-reads at the next kickoff.");
            }
        }

        return offenders;
    }
}
