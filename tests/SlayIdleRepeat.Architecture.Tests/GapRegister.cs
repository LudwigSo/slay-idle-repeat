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
///   register replaces. It is also what holds M1-06 to the promise below: transcribe the command
///   inventory into <see cref="Surfaces"/>, do not just add entries to <see cref="Deferred"/>.</item>
///   <item><b>Vacuous.</b> Both sets have floors, and both predicates are proven to distinguish a
///   type that exists from one that does not — see <c>GapRegisterTests</c>. A register whose
///   subject set can silently become empty is steering <b>S3</b>'s failure mode, and the three
///   rules above would report success forever.</item>
/// </list>
/// <para>
/// 🔒 <b>One register for the repository, not one per milestone</b> (steering S4: one mechanism per
/// repo). It is named <c>GapRegister</c> rather than <c>M1GapRegister</c> for exactly that reason —
/// a milestone-stamped name invites an <c>M2GapRegister</c> beside it, and then the "is every gap
/// declared?" question has two answers. M1-06 adds its deferred commands to <see cref="Deferred"/>
/// and its command inventory to <see cref="Surfaces"/>; it does not add a sibling file.
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
            "30 §4 lists 'inventory, gear instances' among Player's contents, and 08 §5 caps it at 400 " +
            "slots. Neither can be stored before the thing being stored exists: GearInstance carries " +
            "quality, chapterOrigin, a mercy counter, affixes and a lock (08 §2-3), and every one of " +
            "those is a decision M4-03 makes. A List<something> authored now would freeze the item " +
            "shape under M4-04's forge and M4-05's capacity curve (S6). Keyed on GearInstance because " +
            "the shelf and the slots are the same missing type."),

        new("ContainerShelf", "M4-02", "ContainerClass",
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
