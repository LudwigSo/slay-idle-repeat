using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 <b>`24` §11 — every protected grant is routed through <c>LuckService</c>, and a guarantee can
/// fire in exactly one place.</b> No method in <c>Core</c> outside <c>Rules.Luck</c> produces a
/// grant outcome without going through the façade, and no type outside <c>Rules.Luck</c> may name
/// the guarantee primitives at all.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule did not exist and had to.</b> `24` §11's requirement is a <em>routing</em>
/// claim — <em>"exactly one place in the codebase where a guarantee can fire"</em> — and nothing in
/// the suite could express one. <c>Handlers_and_Rules_are_internal</c> governs accessibility,
/// <c>Core_internal_layering_holds</c> and R17 govern which namespace may name which, and all three
/// are satisfied by a chest opener that draws its own rarity out of a weighted table and never
/// mentions pity. That is not a hypothetical shape: it is the shortest path to a working chest, and
/// the counter it skips is invisible until a player complains that a hundred and sixty chests
/// produced nothing.
/// </para>
/// <para>
/// <b>What it buys, concretely.</b> Of the five grant paths this rule was written ahead of —
/// chests, eggs, crates, the wheel and the perk draft — the draft has landed (M4-01b wired it and
/// deleted its two exemption rows in the same commit) and four are still unbuilt, each owned by a
/// different task in a different milestone. This rule is what makes the next of them fail on the
/// commit that adds it rather than on the commit that finally reads `24` again.
/// </para>
/// <para>
/// ⚠️ <b>WHAT THIS RULE CANNOT SEE, listed rather than implied.</b>
/// </para>
/// <list type="bullet">
///   <item>A producer that hides its outcome behind a type this file does not enumerate — an
///   <c>object</c>, a tuple of primitives, a <c>string</c> item id.
///   <see cref="GrantOutcomeTypes"/> is a hand-written closed list on purpose (a blanket "anything
///   under Model" would fire on every aggregate accessor in the assembly), and the cost of that
///   choice is exactly this hole.</item>
///   <item>A method that names <c>LuckService</c> for some other reason and produces its outcome
///   another way. "Routes through" is answered from the call graph one level deep; a laundering
///   indirection defeats it.</item>
///   <item>⚠️ <b>A production that is neither a signature type nor a <c>newobj</c>.</b> M4-05 closed
///   the second half of the matcher — <see cref="GrantOutcomesConstructedBy"/> reads the instruction
///   stream, because <c>Inventory.SetLock</c> was the first member in <c>Core</c> to build a
///   <c>GearInstance</c> while naming none in its signature. The spellings that are still <b>not</b>
///   closed: a <c>with</c>-expression, which compiles to <c>&lt;Clone&gt;$</c> plus property setters
///   and emits no <c>newobj</c> at all; a value-type outcome brought into being by <c>initobj</c> or
///   <c>default</c>; an outcome obtained by <em>calling</em> a factory on some other type, where the
///   callee constructs it and this method only holds it; one taken out of a field, an array or a
///   collection that some earlier method filled; and one built inside a lambda, whose display class
///   <c>ScannedTypes</c> drops as compiler-generated. Each of those is a real way to hand a player an
///   item; what the two halves together close is producing one <em>here</em>, in the two spellings a
///   bypass is actually written in.</item>
///   <item>A <c>const</c> read across the boundary in arm 2 — the compiler folds it, so no type
///   reference survives into metadata. The same hole <c>IntraRulesLayeringRuleTests</c> records,
///   and narrow for the same reason: no guarantee type declares a <c>const</c> a caller outside the
///   namespace could reach — the two M4-01b added carry constants, and both are <c>private</c>, so
///   there is no cross-boundary read for the compiler to fold in the first place. That is a fact
///   about today's declarations rather than a rule, and it is the reason the hole stays narrow: a
///   guarantee type that ever declares an <c>internal const</c> re-opens it. ⚠️ The
///   <em>counter-key</em> vocabulary has exactly
///   that shape and is <b>not</b> left to this hole:
///   <see cref="No_pity_counter_key_is_spelled_outside_the_tuning_reader"/> reads the separator out
///   of <c>LuckTuning</c>'s field constant in metadata, which is the one place a folded
///   <c>const</c> still exists.</item>
///   <item><b>Everything outside <c>SlayIdleRepeat.Core</c>.</b> Both arms are stated over
///   <c>ProductionAssemblies.CoreModule</c>, so a producer written in <c>Application</c> — a use
///   case that assembles a grant itself instead of loading a slice and calling the rules — is
///   invisible here. That is a deliberate scope, not an oversight: `23` §2.0a already forbids a
///   game rule in a use case and <c>DependencyRuleTests</c> is where that claim lives. It is listed
///   because "no grant bypasses the façade" reads like a whole-solution claim and is not one.</item>
///   <item>A method that reaches a type whose <em>simple name</em> is <c>LuckService</c> —
///   <see cref="RoutesThrough"/> matches on the simple name so that the fixtures below can drive
///   the same predicate the rule does. A second type called <c>LuckService</c> anywhere in
///   <c>Core</c> would launder a bypass;
///   <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> pins the real one
///   to <see cref="LuckNamespace"/> but does not forbid a namesake beside it.</item>
///   <item>An <c>Application</c>- or <c>Server</c>-side hand-composed counter key. Arm 3 scans
///   <c>Core</c> for the same reason the other two do.</item>
/// </list>
/// <para>
/// 🔒 The namespace constant is <b>read</b> from <c>IntraRulesLayeringRuleTests</c> rather than
/// restated here. That file has to declare it anyway — R17 fails on an ungoverned sub-namespace of
/// <c>Rules</c> — and two spellings of one namespace is one rename away from a rule that governs a
/// namespace nobody has.
/// </para>
/// </remarks>
public sealed class LuckRoutingRuleTests
{
    /// <summary>`24` §11's façade: the one type a protected grant is resolved through.</summary>
    internal const string LuckFacade = "LuckService";

    /// <summary>The façade member `24` §11's routing claim is about — the identity floor's subject.</summary>
    private const string ResolveMember = "Resolve";

    /// <summary>
    /// The first production type to satisfy the routing arm rather than be exempted from it —
    /// M4-01b's perk draft engine, and the identity under the compliant-producer floor.
    /// </summary>
    private const string RoutingProducer = "PerkDraftEngine";

    /// <summary>The namespace the façade and its primitives live in, read from R17's own declaration.</summary>
    private const string LuckNamespace = IntraRulesLayeringRuleTests.LuckNamespace;

    /// <summary>The one type permitted to spell a counter key — `24` §3's reader of the authored keys.</summary>
    private const string CounterKeyFormationPoint = "LuckTuning";

    /// <summary>Where it lives. Read from <c>Domain</c> so a namespace rename cannot orphan arm 3.</summary>
    private const string FormationPointNamespace = Domain.ContentNamespace;

    /// <summary>The field constant that pairs an authored key to the guarantee it protects.</summary>
    private const string CounterKeySeparatorField = "CounterKeySeparator";

    /// <summary>The document `24` §3's counter keys are authored in, relative to the repo root.</summary>
    private const string CounterKeyDocument = "game-data/tuning/luck.json";

    /// <summary>The pointer the source-class registry is authored under.</summary>
    private const string SourceClassesMember = "sourceClasses";

    /// <summary>The member of a registry row holding the authored counter key. May be an authored null.</summary>
    private const string CounterKeyMember = "counterKey";

    /// <summary>
    /// The floor under arm 3's key set. Eight of the ten classes author a key on the commit this
    /// landed (<c>ENHANCE</c> and <c>DRAFT</c> author an explicit null — their counters are not
    /// player-scoped); set below that so re-scoping one class is not a test edit.
    /// </summary>
    private const int AuthoredCounterKeyFloor = 6;

    /// <summary>
    /// The identity under that floor — `24` §4.1's ten-chest ladder, the most-drawn protected source
    /// in the game. An identity floor rather than a count alone (steering S3).
    /// </summary>
    private const string KnownCounterKey = "chest.standard";

    /// <summary>
    /// `24` §3's authored counter keys, read out of the shipped registry rather than transcribed.
    /// </summary>
    /// <remarks>
    /// Throws rather than answering an empty list on a missing or reshaped document: arm 3 matches
    /// literals against this set, so returning nothing would turn "the registry moved" into "no
    /// hand-composed key exists anywhere", forever. Same argument
    /// <c>RepoLayout.SourceFiles</c> makes for a missing directory.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<string>> AuthoredCounterKeys = new(() =>
    {
        var path = Path.Combine(RepoLayout.RepoRoot, CounterKeyDocument.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{CounterKeyDocument}' does not exist, so the counter keys arm would match nothing " +
                "and report success over every hand-composed key in Core. Point it at the document's " +
                "new home.",
                path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        if (!document.RootElement.TryGetProperty(SourceClassesMember, out var registry) ||
            registry.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"'{CounterKeyDocument}' authors no '{SourceClassesMember}' array. 24 §3's counter " +
                "keys are read from it, and a rule stated over an empty set of keys is a rule that " +
                "cannot fail.");
        }

        return registry.EnumerateArray()
            .Where(row => row.TryGetProperty(CounterKeyMember, out var key) &&
                          key.ValueKind == JsonValueKind.String)
            .Select(row => row.GetProperty(CounterKeyMember).GetString()!)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    });

    /// <summary>The `24` §3 registry's own vocabulary type, whose members the coverage arm is floored against.</summary>
    private const string SourceClassVocabulary = "SourceClass";

    /// <summary>
    /// The floor under the coverage arm's LIVE half: the four classes that have a production caller
    /// at the end of M4. Set at the real number rather than below it — this is the milestone's own
    /// completeness claim, and a class going quiet is exactly what it must report.
    /// </summary>
    private const int LiveSourceClassFloor = 4;

    /// <summary>
    /// 🔒 One row per `24` §3 grant source class: the façade member a caller of that class must go
    /// through, the production type that calls it today, and — when nothing does yet — the task that
    /// owns wiring it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The four live rows are the four grant paths that exist at the end of M4.</b> Each names a
    /// real production caller, so the coverage arm quantifies over shapes that are in the assembly
    /// rather than over names.
    /// </para>
    /// <para>
    /// <b>Five unwired rows name the ladder entry point; the sixth names nothing, and the difference
    /// is load-bearing.</b> <c>LuckTuning</c> builds its ladder map from exactly five classes — the
    /// three chests, the pet egg and the mount crate — so those five are the ones
    /// <see cref="ResolveMember"/> serves, and the day one of them is opened it is opened through
    /// that member. <c>WHEEL</c> is <b>not</b> among them: it sits in the reader's unserved list
    /// beside <c>DROP_RUN</c>, <c>ENHANCE</c>, <c>DRAFT</c> and <c>MINIGAME</c>, and asking the
    /// façade to resolve it <em>throws</em>. A row pointing the wheel at the ladder entry point
    /// would therefore have been a row that could never fire on the commit it exists to catch — so
    /// it carries <c>null</c>, meaning <em>no façade member serves this class yet</em>, and
    /// <see cref="Every_facade_member_production_calls_is_a_grant_class_this_file_classifies"/> is
    /// what notices when one appears.
    /// </para>
    /// <para>
    /// ⚠️ The five that do share an entry point share a claim with it: this arm can say one of them
    /// was wired, not which. The failure names the caller that arrived, which is what an author
    /// needs in order to move the right row. Stated rather than implied, because "no caller of
    /// Resolve" reads like five independent claims and is one.
    /// </para>
    /// <para>
    /// ⚠️ The owners are read from the same place the build already validates them: each is the task
    /// on the <c>Deferred</c> row of the command that opens that container in <c>GameRules</c>, which
    /// <c>GapRegisterTests</c> checks against the tracker. They are restated here rather than looked
    /// up because a source class is not a command — <c>DROP_RUN</c> has no command at all.
    /// </para>
    /// </remarks>
    private static readonly (string Id, string? FacadeEntry, string? Caller, string Owner)[]
        GrantSourceClasses =
    {
        // 🔒 The four that are live. Each row's Caller is the production type whose IL calls the entry.
        ("DROP_RUN", "ResolveRunDrop", "GearGeneration", "M4-03"),
        ("ENHANCE", "EnhanceSuccessRate", "GearEnhancement", "M4-04"),
        ("DRAFT", "ResolveDraft", "PickPerk", "M4-01b"),
        ("MINIGAME", "ResolveChestPick", "MinigameSubmit", "M4-01b"),

        // 🔒 The five the ladder entry point serves, none of them opened by anything yet.
        ("CHEST_STANDARD", ResolveMember, null, "M4-02"),
        ("CHEST_PREMIUM", ResolveMember, null, "M4-02"),
        ("CHEST_APEX", ResolveMember, null, "M4-02"),
        ("EGG_PET", ResolveMember, null, "M4-02"),
        ("CRATE_MOUNT", ResolveMember, null, "M4-02"),

        // 🔒 The one with no façade member at all. Asking the ladder to resolve it throws.
        ("WHEEL", null, null, "M4-09"),
    };

    /// <summary>
    /// 🔒 Every member of <see cref="LuckFacade"/> production is permitted to call today, and which
    /// grant class each is called on behalf of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The closed surface is what covers the classes no entry point serves.</b>
    /// <see cref="GrantSourceClasses"/> can only ask "does anything call the member this class goes
    /// through", which says nothing at all about <c>WHEEL</c> — no member goes through anything for
    /// it. Enumerating the calls instead turns that into a decidable claim: the day M4-09 wires the
    /// wheel, whatever member it reaches for is one this list does not name, and the arm reports it
    /// by name and asks which class it serves.
    /// </para>
    /// <para>
    /// It is the same mechanism in the other direction as <see cref="RoutingExemptions"/> — a closed
    /// list with an expiry, rather than a suppression file — and it subsumes the "no caller of
    /// <see cref="ResolveMember"/>" claim rather than restating it: <c>Resolve</c> is deliberately
    /// absent from this list, so a chest opener trips both.
    /// </para>
    /// </remarks>
    private static readonly (string Member, string Serves)[] CalledFacadeMembers =
    {
        ("ResolveRunDrop", "DROP_RUN — the in-run drop's band"),
        ("SessionFloorGrant", "DROP_RUN — 24 §4.3 D3's session floor"),
        ("EnhanceSuccessRate", "ENHANCE — the attempt's chance"),
        ("EnhanceFailuresAfter", "ENHANCE — 24 §4.6's failure mercy counter"),
        ("ResolveDraft", "DRAFT — which of the five rules this draft owes"),
        ("DraftCountersAfter", "DRAFT — where the draft counters land"),
        ("DraftFreshPoolWeight", "DRAFT — the Codex weight on a never-drafted perk"),
        ("MaxCodexBiasedOptions", "DRAFT — the cap on Codex-biased options"),
        ("OwnedUpgradeBias", "DRAFT — the owned-upgrade bias"),
        ("ResolveChestPick", "MINIGAME — the chest pick's tier and its counter"),
        ("ChestPickTopTier", "MINIGAME — which tier the guarantee lands on"),

        // ⚠️ Not a grant SOURCE CLASS. `08` §4.1's fusion decides a band, so it goes through the
        // façade like every other band decision, and it is enumerated here for the same reason.
        ("MergeOutputBand", "the forge's fusion — the band a trio lands on"),
    };

    /// <summary>
    /// 🔒 The closed list of type simple names that <b>are</b> a grant outcome — what "producing a
    /// grant" means to this rule, stated as an enumeration rather than inferred.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Live today:</b> <c>Rarity</c> (M4-01, the ladder every protected class draws on) and
    /// <c>DraftOption</c> (M3-06, the perk draft's offered option). Both resolve in <c>Core</c> now,
    /// and <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> asserts that
    /// <c>DraftOption</c> still does — a count-only floor would not notice it going.
    /// </para>
    /// <para>
    /// <b>Pre-registered, and deliberately named before they exist:</b> <c>GearInstance</c> (M4-03),
    /// <c>PetDefinition</c> (M4-07) and <c>MountDefinition</c> (M4-08). All three are already tracked
    /// as pending subjects, so the names are the ones those milestones have committed to rather than
    /// inventions. Registering them now is the whole point of the rule: the day one lands, its
    /// producer either routes through the façade or turns the build red, instead of shipping a
    /// grant path nobody remembered to protect.
    /// </para>
    /// <para>
    /// ⚠️ Matched by <b>simple name</b>, so a differently-named clone is invisible. That is the
    /// price of naming three types that do not exist yet; the alternative is a rule that cannot be
    /// written until M4-08.
    /// </para>
    /// </remarks>
    private static readonly string[] GrantOutcomeTypes =
    {
        "Rarity",
        "DraftOption",
        "GearInstance",
        "PetDefinition",
        "MountDefinition",
    };

    /// <summary>
    /// 🔒 The members of <see cref="GrantOutcomeTypes"/> that a production type declares <b>today</b>
    /// — the ones the identity floor can require the matcher to actually find.
    /// </summary>
    /// <remarks>
    /// The other three are pre-registered for M4-03, M4-07 and M4-08, so requiring a match on them
    /// would fail every build until those milestones run. When one lands, move its name here in the
    /// same commit: that is what turns "the rule knows the name" into "the rule sees the producer".
    /// </remarks>
    private static readonly string[] LiveGrantOutcomeTypes =
    {
        "Rarity",
        "DraftOption",

        // 🔒 M4-03 moved this one here in the commit that authored it, which is what the remarks
        // above ask for: the name is no longer a pre-registration but a production type, and the
        // routing arm now quantifies over a real gear producer rather than over a name nothing
        // declares. GearGeneration is the producer, and it is deliberately NOT on RoutingExemptions.
        "GearInstance",
    };

    /// <summary>
    /// 🔒 The guarantee primitives: the types that decide, ramp, bank or reshape a protected draw.
    /// Naming one from outside <c>Rules.Luck</c> is a second place a guarantee can fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>M4-01b added the last two, and the set was stale for exactly as long as it did not.</b>
    /// The first four are M4-01's rarity-ladder primitives, and while the ladder was the only shape a
    /// guarantee came in, "the four" was the whole set. M4-01b wired the two classes that state their
    /// protection in another shape — <c>DraftGuarantees</c> decides the five <c>DRAFT</c> rules and
    /// <c>ChestPickGuarantee</c> decides the chest pick's gold-tier rung — and both call
    /// <c>HardPity</c> themselves. They are places a guarantee fires, by the same definition the
    /// first four are, and leaving them out left arm 2 guarding the old shape only.
    /// </para>
    /// <para>
    /// ⚠️ The <c>MINIGAME</c> path made that concrete rather than theoretical: <c>ChestPickResolution</c>
    /// is not a grant-outcome type, so arm 1 does not quantify over the chest pick at all, and until
    /// these two names went in, a handler calling <c>ChestPickGuarantee.Resolve</c> and skipping the
    /// façade entirely would have been reported by nothing in this file. <c>MinigameSubmit</c> was in
    /// fact reaching past the façade for <c>ChestPickGuarantee.TopTier</c>; it goes through
    /// <c>LuckService.ChestPickTopTier</c> now.
    /// </para>
    /// <para>
    /// ⚠️ The value types beside them — <c>DraftForce</c>, <c>DraftCounters</c>, <c>DraftDemand</c>,
    /// <c>DraftOffering</c>, <c>ChestPickResolution</c> — are deliberately <b>not</b> here, on
    /// <c>HardPityStep</c>'s precedent: they are the façade's own argument and answer vocabulary, so a
    /// caller has to name them in order to route at all. Guarding them would forbid the compliant path.
    /// </para>
    /// </remarks>
    private static readonly string[] GuaranteePrimitives =
    {
        "HardPity",
        "SoftPity",
        "MercyAccrual",
        "RarityTable",
        "DraftGuarantees",
        "ChestPickGuarantee",

        // 🔒 M4-04 added the seventh, and it is a guarantee by the same definition the other six
        // are: `24` §4.6's failure mercy decides whether an enhancement attempt is protected, and it
        // decides it here. It is NOT MercyAccrual under another name — that one banks and spends
        // tokens, this one raises a probability — and folding the two together would make one of
        // them wrong. Registering it is what stops the forge from computing the rate itself, which
        // is what a second place a guarantee fires looks like when the guarantee is an addition
        // rather than a rung.
        "EnhanceMercy",
    };

    /// <summary>
    /// 🔒 The enumerated, closed exemption list for arm 1 — types that carry a grant-outcome name in
    /// a signature without granting anything.
    /// </summary>
    /// <remarks>
    /// Each entry has to justify itself:
    /// <see cref="Every_exempted_producer_still_needs_its_exemption"/> proves the entry is still
    /// load-bearing, so an exemption whose subject stopped tripping the rule is a build failure
    /// rather than a line nobody deletes.
    /// </remarks>
    private static readonly (string Type, string Reason)[] RoutingExemptions =
    {
        // 🔒 The tuning reader and its payload rows. Reading a rarity OUT of luck.json is not
        // granting one — these types carry the authored vocabulary the façade then draws against,
        // and Content sits beneath Rules, so a Content type calling into Rules.Luck would invert the
        // layering outright. There is no design in which this exemption goes away.
        // 🔴 A fourth Content row — `PityLadder` — was written here and DELETED before this landed,
        // because the companion fact reported it as an exemption covering nothing: a ladder carries
        // its rungs and its curve, and neither of those types is a grant outcome, so it never
        // tripped the routing arm in the first place. That is the mechanism working on its own
        // author, and it is worth one line: an exemption added "to be safe" is a rule quietly
        // narrowed, and this file cannot hold one.
        ("LuckTuning", "the pity registry itself — it reads the authored ladders and forms the counter keys the façade draws against; Content sits BENEATH Rules, so routing a Content read through Rules.Luck is not merely unnecessary, it is the layering inverted"),
        ("HardPityStep", "one authored rung of a ladder — a data row carrying its guarantee rarity, read by LuckTuning"),

        // 🔴 M4-01b DELETED THE TWO DRAFT ROWS THAT USED TO SIT HERE, and the deletion is the
        // deliverable rather than a tidy-up. `PerkDraftEngine` and `PickPerk` were exempted with an
        // owner because 24 §4.7's five rules were declared as no-op seams and nothing routed.
        //
        // Both route now, from every method that carries a DraftOption: the engine's GenerateOptions
        // and DrawOption ask the façade for the Codex weight, its cap and the owned-upgrade bias,
        // and the handler's GenerateCurrentOptions asks it which guarantees this draft owes while
        // MoveDraftCounters asks it where the counters land. Keeping either row would therefore fail
        // Every_exempted_producer_still_needs_its_exemption — an exemption that no longer bites is a
        // rule quietly narrowed, and this file cannot hold one.

        // 🔒 M4-03's six, and they fall into three kinds rather than one. None of them decides a band
        // or a guarantee; the type that does — Rules.Gear.GearGeneration — is deliberately absent
        // from this list and calls the façade for both of its entry points.
        //
        // ⚠️ THE COUNT IS THE POINT OF THIS NOTE. Six rows arriving at once reads as a rule being
        // hollowed out, so what was done to keep it from being that is written down: the shapes that
        // could be restructured out of the rule's way were, rather than exempted. DropsTuning's two
        // internal row types became private named tuples, GearInstance's three enum guards were
        // inlined into its constructor (a private RequireDeclared(Rarity) on the outcome type would
        // have forced an exemption on the outcome type itself, reopening the factory hole this rule
        // was narrowed to close), the drop result became a named tuple rather than a record, and the
        // minting half was split into its own type so that the half which routes could stay
        // unexempted. Every_exempted_producer_still_needs_its_exemption drives all six.
        //
        // (a) The two CONTENT READERS and their authored rows. Reading a rarity out of a tuning
        //     document is not granting one — LuckTuning and HardPityStep carry exactly this reason,
        //     and Content sits BENEATH Rules, so routing a content read through Rules.Luck would be
        //     the layering inverted.
        ("DropsTuning", "the gear tuning reader — the rarity ladder, the chapter-banded drop shares, the slot coefficients, the affix pool and the set breakpoints the façade and the generator then draw against; LuckTuning's reason exactly, one document over"),
        // 🔴 A DropRunTuning row was written here and DELETED before this landed, because the
        // companion fact reported it as an exemption covering nothing: the reader hands back its two
        // breakers and its floor as records, and neither the return type nor a parameter is a grant
        // outcome — the rarities are members of those records, which the matcher does not flatten
        // into. The same mechanism that deleted PityLadder's row deleted this one, on its own
        // author, and it is worth a line: an exemption added "to be safe" is a rule quietly narrowed.
        ("DryStreakBreaker", "one authored dry-streak breaker — a data row carrying the band it counts a miss below and the band it forces, read by DropRunTuning. HardPityStep's reason"),
        ("SessionFloor", "one authored session floor — a data row carrying the band a floor grant lands on. HardPityStep's reason"),

        // (b) The CONSUMERS of an item that was already granted. Neither mints anything: one answers
        //     what an item's stats derive to, the other counts how many pieces of a set a loadout is
        //     wearing. Both name a GearInstance in their signatures because they take one.
        ("GearStatDerivation", "derives an item's two stats from what it already rolled — this is what 'computed stats are never stored' costs, and it reads a granted item rather than producing one"),
        ("SetBonusResolver", "counts the SS pieces of an equipped loadout and answers which authored breakpoints it has reached. It reads items that were granted long before it ran"),

        // (c) The two remaining shapes that name a gear instance by construction.
        ("GearGranted", "the domain event that REPORTS a grant. Its constructor and its accessor carry the item because that is what the event is for; the grant was produced by whatever emitted it, and that producer is the one this rule watches. ⚠️ Pet and mount grant events will want the same row — at the third one, widen MentionsItsOwnTypeByConstruction to cover a DomainEvent's own bookkeeping members rather than adding a fourth"),
        ("GearMinting", "builds an item at a band that was ALREADY decided — the base item, the quality scalar and the affixes, none of which is protected. Split out of GearGeneration precisely so that this exemption cannot cover the half that does make the protected decision; GearGeneration has no row here and calls LuckService for both of its entry points"),

        // 🔒 M4-05's FOUR, and they split across the same kinds M4-03's six did: three of kind (b)
        // and one of kind (a). None decides a band, draws anything, or takes an Rng at all — the
        // container stores what it is handed, the sorting orders what the container holds, the
        // comparison subtracts two already-derived stat figures, and the snapshot is a persisted row.
        //
        // ⚠️ Player is deliberately NOT on this list, and that is a design constraint rather than an
        // oversight: the aggregate exposes the inventory component, names no GearInstance in any
        // signature and constructs none, so neither half of the matcher has anything to report. A
        // convenience member on Player that took, returned or built an item would put a FIFTH row
        // here, which is the cost that decision was taken to avoid.
        //
        // (b) The CONSUMERS.
        ("Inventory", "the container. It stores, holds, reclaims and locks items it is HANDED — Place takes an item somebody else already produced, and there is no draw anywhere in the type. ⚠️ SetLock CONSTRUCTS a GearInstance in its body (the lock is the container's state, so the transition is the container's), which is the shape GrantOutcomesConstructedBy was added to see; it rebuilds an item that already exists rather than deciding a new one's band"),
        ("InventorySorting", "orders a list of owned items by slot, band, power, quality or age. Reading a band to sort by it is not deciding one — LuckTuning's reason, one layer up"),
        ("InventoryComparison", "subtracts one item's derived stats from another's. It names two GearInstances because a side-by-side delta is about exactly two of them, and GearStatDerivation — which it consumes — carries this same reason"),

        // (a) The persisted DATA ROW, beside DropsTuning's rung rows rather than beside the three
        //     consumers above.
        // 🔒 M4-04's THREE, and all three are shapes already on this list rather than new kinds.
        //
        // ⚠️ What was NOT exempted is the point of this note. The forge's two producers —
        // Rules.Forge.GearMerge and Rules.Forge.GearEnhancement — both build a GearInstance and both
        // are deliberately absent from this list: the fusion asks LuckService for the band its
        // output lands on, and the attempt asks LuckService for the chance it has. Neither could
        // have been written without the façade, which is exactly what the rule is for. The forge's
        // handlers are absent too, and that shaped the code: Merge and Salvage look their items up
        // through Inventory.Find rather than through a private helper of their own, because a helper
        // answering a GearInstance would have put two more rows here.
        //
        // (a) The CONTENT READER, beside DropsTuning and LuckTuning.
        ("ForgeTuning", "the forge tuning reader — the fusion's input count and prices, the enhancement ladder and the salvage values, all keyed on the band they apply to. It reads a rarity OUT of a document rather than deciding one; DropsTuning's reason, one document over, and Content sits BENEATH Rules so routing a content read through Rules.Luck would be the layering inverted"),

        // (a) The persisted/configured DATA ROW, beside HardPityStep and GearInstanceSnapshot.
        ("AutoSalvageRule", "one row of a player's auto-salvage filter — a band and the level below which it is swept. A row CARRYING a rarity is not a place a rarity is decided; HardPityStep's and SessionFloor's reason, one layer over, and it is scanned at all for GearInstanceSnapshot's reason: its simple name is not in GrantOutcomeTypes, so its constructor and accessors are never covered by MentionsItsOwnTypeByConstruction"),

        // (b) The CONSUMER of an item that was already granted, beside GearStatDerivation.
        ("GearSalvage", "answers what an item BREAKS DOWN INTO. It names a GearInstance because it reads one — the band it rolled and the level it reached — and it produces no item at all: the two numbers it answers are currency amounts. GearStatDerivation's reason, at the other end of the item's life"),

        ("GearInstanceSnapshot", "the persisted ROW of an item that was granted long before it was written down. It carries the band because that is what the item rolled — HardPityStep's and SessionFloor's reason, one layer over: a data row carrying a rarity is not a place a rarity is decided. ⚠️ It is scanned at all because its SIMPLE NAME is not in GrantOutcomeTypes, so MentionsItsOwnTypeByConstruction never covers its constructor and accessors the way it covers GearInstance's. Accessibility has nothing to do with it — Il.AllMethods filters on nothing of the sort, and an internal constructor would trip this identically. A snapshot of a pet or a mount will want the same row"),
    };

    /// <summary>
    /// 🔒 `24` §11 — <b>no method in <c>Core</c> outside <c>Rules.Luck</c> produces a grant outcome
    /// without routing through <see cref="LuckFacade"/>.</b> Every gear, pet, mount, rarity or draft
    /// option a player is handed came out of the one place a guarantee can fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An IL scan over method signatures rather than a source grep, for <c>Il</c>'s stated reason —
    /// a grep is defeated by a <c>using</c> alias, a fully-qualified call or an extension method,
    /// and it fires inside the comments of the very files that explain at length why they do not
    /// grant anything.
    /// </para>
    /// <para>
    /// A grant-outcome type's own <em>bookkeeping members</em> are excluded — its constructor, its
    /// equality, printing and deconstruction members, its accessors — because a record mentions its
    /// own type in those signatures whether or not it grants anything, and reporting them would make
    /// the rule noise rather than a rule. 🔴 The <em>type</em> was excluded originally, which also
    /// hid a hand-written factory declared on the outcome (<c>GearInstance.Roll()</c>) — see
    /// <see cref="ScannedTypes"/>.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite, on real production IL.</b> Renaming <c>PerkDraftEngine</c>'s row in
    /// <see cref="RoutingExemptions"/> to a name nothing declares turned this arm red with two
    /// offenders — <c>SlayIdleRepeat.Core.Rules.Perks.PerkDraftEngine.GenerateOptions</c> and
    /// <c>.DrawOption</c>, both reported as <em>"produces a grant outcome (DraftOption) and never
    /// names LuckService"</em> — and turned
    /// <see cref="Every_exempted_producer_still_needs_its_exemption"/> red in the same run for the
    /// now-uncovered row. Reverted. The arm is therefore quantifying over a live production
    /// producer, not over an empty set.
    /// </para>
    /// <para>
    /// 🔴 <b>The uncomfortable half of that is DISCHARGED, and the note is corrected rather than left
    /// standing.</b> Until M4-01b, every method in <c>Core</c> that tripped this predicate was on
    /// <see cref="RoutingExemptions"/> — so zero offenders was a fact about the exemption list rather
    /// than about the codebase, and this paragraph said so. It no longer is. <c>DraftOption</c>'s four
    /// producers (<c>PerkDraftEngine.GenerateOptions</c> and <c>.DrawOption</c>,
    /// <c>PickPerk.GenerateCurrentOptions</c> and <c>.MoveDraftCounters</c>) all call the façade, and
    /// their two exemption rows were deleted in that commit. <c>Rarity</c>'s two producers — the
    /// tuning reader and its rung rows — remain exempted, and
    /// <see cref="Every_exempted_producer_still_needs_its_exemption"/> requires this same predicate to
    /// answer true on both. The compliant half now has its own floor:
    /// <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> requires a named
    /// producer to both carry a grant outcome and call the façade, so silence here means compliance
    /// rather than absence.
    /// </para>
    /// <para>
    /// 🔒 <b>Re-probed in the architecture review, in two shapes with a control.</b> An unexempted
    /// probe type under <c>Rules/Perks/</c> answering a bare <c>Rarity</c> and a second method
    /// handing back an <c>out IReadOnlyList&lt;Rarity&gt;</c> were both reported — so the matcher
    /// flattens a generic wrapper and reads an <c>out</c> parameter, not only a return type. A third
    /// method on the same type answering a <c>Rarity</c> <em>and</em> calling
    /// <c>LuckService.AccrueMercy</c> was correctly not reported. Reverted.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_grant_outcome_is_produced_outside_the_luck_service()
    {
        var exempted = RoutingExemptions.Select(e => e.Type).ToArray();

        var offenders = ScannedMethods(ProductionAssemblies.CoreModule)
            .Where(subject => !exempted.Contains(subject.Type.Name, StringComparer.Ordinal))
            .Where(subject => BypassesTheFacade(subject.Method, LuckFacade))
            .Select(subject =>
                $"{Il.Describe(subject.Method)} produces a grant outcome " +
                $"({string.Join(", ", GrantOutcomesProducedBy(subject.Method))}) and never names {LuckFacade}. " +
                "24 §11 puts every protected grant behind one façade: a producer that draws its own " +
                "rarity skips the counter, and a skipped counter is invisible until a player has " +
                "opened a hundred and sixty chests for nothing. Route the draw through " +
                $"{LuckFacade}.{ResolveMember}, or — if this genuinely grants nothing — add it to " +
                "RoutingExemptions with the task that owns wiring it.")
            .ToList();

        ArchRule.Empty(
            offenders,
            "24 §11: every grant outcome in Core is produced through LuckService, the one place a " +
            "guarantee can fire.");
    }

    /// <summary>
    /// 🔒 `24` §11 — <b>a guarantee can fire in exactly one place.</b> No type outside
    /// <c>Rules.Luck</c> names any member of <see cref="GuaranteePrimitives"/>: the façade is the
    /// sole door onto every one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion to the routing arm, and the half that keeps it honest. Arm 1 says a producer
    /// must name the façade; without this one, a producer could name the façade <em>and</em> reach
    /// past it into <c>HardPity.Fires</c> directly, at which point "one place" is two and the second
    /// is wherever the next author found it convenient.
    /// </para>
    /// <para>
    /// <b>Non-vacuous today, in both directions.</b> The subject set is every type in <c>Core</c>
    /// outside one namespace — hundreds — and every target type resolves, which
    /// <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> asserts by
    /// identity rather than by count.
    /// </para>
    /// <para>
    /// 🔒 <b>Re-proved on M4-01b's two new guarantee types, in two shapes.</b> Pointing
    /// <c>MinigameSubmit</c> back at <c>ChestPickGuarantee.TopTier</c> — which is what it actually did
    /// before the architecture review — reported <em>"SlayIdleRepeat.Core.Handlers.MinigameSubmit
    /// names SlayIdleRepeat.Core.Rules.Luck.ChestPickGuarantee"</em>; pointing
    /// <c>PickPerk.GenerateCurrentOptions</c> at <c>DraftGuarantees.Forced</c> instead of the façade
    /// reported the same for <c>DraftGuarantees</c>. Both reverted. The negative control is the green
    /// baseline over real production IL: <c>PerkDraftEngine</c> names <c>DraftForce</c> and
    /// <c>PickPerk</c> names <c>DraftCounters</c>, <c>DraftDemand</c> and <c>DraftOffering</c> — all
    /// declared in the same namespace, all deliberately outside the guarded set because they are the
    /// façade's own vocabulary — and this arm is silent on every one of them.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite.</b> The scan reports nothing today by design — no type outside
    /// <c>Rules.Luck</c> names any of them — so the probe was to point <c>guarded</c> at
    /// <c>SlayIdleRepeat.Core.Primitives.Rarity</c>, a type that <em>is</em> named across the same
    /// boundary. It went red with three offenders (<c>Content.HardPityStep</c>,
    /// <c>Content.LuckTuning</c> and the type itself), so the IL walk and the namespace exclusion
    /// both work; the empty result is a fact about <c>Core</c>, not a rule matching nothing.
    /// Reverted.
    /// </para>
    /// <para>
    /// 🔒 <b>Its floor is no longer identity-only.</b> M4-01 Phase 1a could assert nothing more than
    /// "the four types exist under this namespace", because the façade was still a wall of
    /// <c>NotImplementedException</c> and named none of them in IL. Phase 3 filled the bodies, so
    /// <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> now also
    /// requires the façade to <em>reach</em> every one of them — which is what closes this arm's
    /// own blind spot: it forbids reaching a primitive from outside and says nothing about a façade
    /// that stops reaching them at all and reimplements the decision inline.
    /// 🔒 <b>Proved to bite.</b> Rewriting the façade's <c>HardPity</c> calls as the same arithmetic
    /// inline — the exact "inlined the guarantee" shape — turned the floor red with
    /// <em>"'LuckService' does not name 'HardPity' anywhere in its IL"</em> while leaving this arm
    /// green, as it must. Reverted.
    /// ⚠️ Worth recording that the <em>first</em> attempt at that mutation stayed green: inlining
    /// two of the three call sites left the third, so the type reference survived. This floor is a
    /// claim about the façade reaching the primitive <b>at all</b>, not about any one call site, and
    /// a partial inlining is invisible to it — the same class of hole every "names it" rule has.
    /// 🔒 <b>Narrowed by the architecture review, and the narrowing was mutation-proved.</b> "Names
    /// it" is satisfied by a parameter type, a local or a <c>typeof</c>, so the floor now <em>also</em>
    /// requires the façade to CALL a member of each of them. Two shapes were run: rewriting
    /// <c>AccrueMercy</c>/<c>RedeemMercy</c> as inline arithmetic while keeping
    /// <c>typeof(MercyAccrual)</c> reported <em>"'LuckService' names 'MercyAccrual' but never calls a
    /// member of it"</em> — and the old "names it" half stayed <b>green</b>, which is precisely the
    /// hole the narrowing closes; the same treatment of <c>SoftPity</c> reported it in turn. The
    /// control in that second run moved <c>MercyAccrual.Accrue</c> into a private helper — still a
    /// call, from a different site — and was correctly not reported. All reverted. A partial
    /// inlining remains invisible to both halves.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_guarantee_can_only_fire_inside_Rules_Luck()
    {
        var guarded = GuardedPrimitiveNames();

        var offenders = new List<string>();

        foreach (var type in Il.AllTypes(ProductionAssemblies.CoreModule)
                     .Where(t => !Domain.IsCompilerGenerated(t))
                     .Where(t => !Il.IsUnder(Il.NamespaceOf(t), LuckNamespace)))
        {
            offenders.AddRange(
                Il.ReferencedTypeNames(type)
                    .Where(referenced => guarded.Contains(referenced, StringComparer.Ordinal))
                    .Select(referenced =>
                        $"{type.FullName} names {referenced}. 24 §11 puts the guarantee decision in " +
                        $"one place; {LuckFacade} is the only type permitted to reach it, and a " +
                        "second caller is a second place a guarantee can fire — which is how two " +
                        "chests at the same counter value get different answers."));
        }

        ArchRule.Empty(
            offenders,
            "24 §11: " + string.Join(", ", GuaranteePrimitives) + " are reachable only from " +
            "Rules.Luck, so LuckService is the sole façade over them.");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>the identity floor</b> under both arms above (steering S3). Each is
    /// "no member of set S does X", which passes vacuously when S empties, and both sets can empty
    /// through a rename that nothing else in the suite would notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>An identity floor, not only a count.</b> A count over <c>Core</c>'s types is satisfied
    /// by the other nine hundred while the façade itself is folded into a handler; a count over
    /// <see cref="GrantOutcomeTypes"/> is satisfied while the one live member of it disappears. So
    /// this asserts the names: the façade exists, it declares <see cref="ResolveMember"/>, the four
    /// guarantee primitives are declared inside the namespace the arm above trusts, and
    /// <c>DraftOption</c> — the grant-outcome name that is live and is <em>not</em> M4-01's own —
    /// still resolves in <c>Core</c>.
    /// </para>
    /// <para>
    /// The count is a floor rather than an equality, set well below the tree it was written against,
    /// so adding or removing a type is not a test edit.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_routing_rules_subject_set_is_the_one_it_was_written_against()
    {
        var offenders = new List<string>();

        var facade = Domain.FindInCore(LuckFacade);

        if (facade is null)
        {
            offenders.Add(
                $"'{LuckFacade}' is not declared in Core. Both rules in this file are stated over it " +
                "— the routing arm asks whether a producer names it, the guarantee arm exempts its " +
                "namespace — so without it the routing arm reports every producer an offender and " +
                "the guarantee arm governs a namespace nobody writes into. If the façade was " +
                "renamed, rename LuckFacade in the same commit.");
        }
        else
        {
            if (!Il.IsUnder(Il.NamespaceOf(facade), LuckNamespace))
            {
                offenders.Add(
                    $"'{LuckFacade}' is declared in {Il.NamespaceOf(facade)}, not under {LuckNamespace}. " +
                    "The guarantee arm exempts a NAMESPACE, so a façade that moved out of it would be " +
                    "reported as an offender by the rule it is the subject of.");
            }

            if (!Il.AllMethods(facade).Any(m => m.Name.Equals(ResolveMember, StringComparison.Ordinal)))
            {
                offenders.Add(
                    $"'{LuckFacade}' declares no member named '{ResolveMember}'. The routing arm's " +
                    "failure message tells an author to route through it, and a rule whose remedy " +
                    "does not exist is a rule nobody can obey.");
            }
        }

        foreach (var primitive in GuaranteePrimitives)
        {
            var type = Domain.FindInCore(primitive);

            if (type is null || !Il.IsUnder(Il.NamespaceOf(type), LuckNamespace))
            {
                offenders.Add(
                    $"the guarantee primitive '{primitive}' is not declared under {LuckNamespace}. " +
                    "A_guarantee_can_only_fire_inside_Rules_Luck forbids a name that nothing " +
                    "declares, which is an empty prohibition — the rule would report success over " +
                    "every type in Core while the guarantee moved somewhere else entirely.");
            }
        }

        // 🔒 The floor arm 2 could not carry until the façade had bodies (M4-01 Phase 1a's note).
        // "No type outside Rules.Luck names a primitive" is satisfied by a codebase in which NOTHING
        // names one — including one where the façade stopped delegating and reimplemented the
        // guarantee inline, which is the precise defect arm 2 exists to prevent and the one shape it
        // is structurally blind to. Stated with arm 2's OWN matcher, so a change that blinded
        // Il.ReferencedTypeNames fails here rather than turning that arm permanently green.
        // ⚠️ Every one of them, not "at least one": RarityTable is a PARAMETER type of Resolve, so it
        // is named whether or not a single line of the body survives, and an at-least-one floor would
        // be satisfied by the locked signature alone. HardPity, SoftPity, MercyAccrual and — since
        // M4-01b — DraftGuarantees and ChestPickGuarantee are the ones a body has to reach for.
        // 🔒 And the narrowing of that floor's own stated limit. "Names it" is satisfied by a
        // parameter type, a local or a typeof — so a façade that kept a HardPity-typed local while
        // reimplementing Fires inline would still pass the arm above. "Calls a member of it" is the
        // claim the docs actually make about the façade, and it is strictly narrower. BOTH are
        // asserted rather than one replacing the other: the "names it" arm is stated with arm 2's
        // OWN matcher (Il.ReferencedTypeNames), so a change that blinded that matcher fails here
        // instead of turning arm 2 permanently green, and the call arm cannot make that claim
        // because it walks the instruction stream instead.
        // ⚠️ What neither closes: a PARTIAL inlining. Both are claims about the façade reaching the
        // primitive at all, so inlining two of three HardPity call sites leaves both green — that is
        // recorded in arm 2's own remarks, was found by mutation rather than by reading, and is the
        // same class of hole every "names it" rule has.
        if (facade is not null)
        {
            var called = GuaranteePrimitivesCalledBy(facade);

            foreach (var primitive in GuaranteePrimitives.Except(called, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"'{LuckFacade}' names '{primitive}' but never calls a member of it. 24 §11's " +
                    "one place a guarantee can fire is the primitive, and a façade that holds the " +
                    "type without invoking it has moved the decision into itself — which is the " +
                    "second place the rule forbids, reached from inside. If the primitive genuinely " +
                    "stopped being the façade's to call, say where the call went and drop it from " +
                    "GuaranteePrimitives in the same commit.");
            }

            var reached = GuaranteePrimitivesReachedBy(facade);

            foreach (var primitive in GuaranteePrimitives.Except(
                         reached.Select(SimpleNameOf), StringComparer.Ordinal))
            {
                offenders.Add(
                    $"'{LuckFacade}' does not name '{primitive}' anywhere in its IL, so " +
                    "A_guarantee_can_only_fire_inside_Rules_Luck is prohibiting a reference that " +
                    "nothing in Core makes at all — a prohibition over an empty set reports success " +
                    "forever. Either the façade inlined that primitive's decision instead of " +
                    "delegating to it, which is the second place a guarantee can fire that 24 §11 " +
                    "forbids reached from inside rather than from outside, or the primitive is now " +
                    "someone else's door. If the façade genuinely stopped owning it, say where it " +
                    "went and drop it from GuaranteePrimitives in the same commit.");
            }
        }

        // 🔒 The one grant-outcome name that is live and is not this milestone's own. Three of the
        // five are pre-registered for milestones that have not run, so a floor over the LIST would
        // be satisfied by names nothing declares; this is the member that makes the routing arm
        // quantify over a real production shape today.
        if (Domain.FindInCore("DraftOption") is null)
        {
            offenders.Add(
                "'DraftOption' resolves to no type in Core. It is the live member of " +
                "GrantOutcomeTypes that M4-01 did not author, so its absence means the routing arm " +
                "is matching only names that no production type carries — a rule quantifying over " +
                "nothing while reporting success. If the draft's option type was renamed, rename it " +
                "in GrantOutcomeTypes in the same commit.");
        }

        // 🔒 The floor that answers the question the DraftOption check above cannot. "The type
        // resolves in Core" is not the same claim as "the routing arm's own matcher finds it in a
        // production signature" — Il.SignatureTypes/Il.Flatten sit between the two, and if either
        // stopped seeing through IReadOnlyList<T> the arm would report success over an empty set of
        // producers while every name in GrantOutcomeTypes still resolved. Stated over the LIVE names
        // only: three of the five are pre-registered for milestones that have not run.
        var produced = ScannedMethods(ProductionAssemblies.CoreModule)
            .SelectMany(subject => GrantOutcomeNamesIn(subject.Method))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var live in LiveGrantOutcomeTypes)
        {
            if (!produced.Contains(live, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"no method in Core outside {LuckNamespace} carries '{live}' in its signature, so " +
                    "the routing arm is not quantifying over it at all. Both live grant-outcome names " +
                    "are reachable today — Rarity through the tuning reader's rung rows, DraftOption " +
                    "through the perk draft — so an empty match means the matcher stopped seeing " +
                    "them (a generic wrapper it cannot flatten, a moved namespace) rather than that " +
                    "the game stopped producing grants.");
            }
        }

        // 🔒 The floor arm 1 could not carry until M4-01b, and the one its own remarks name as the
        // uncomfortable half: until this commit EVERY method in Core that tripped the predicate was
        // on RoutingExemptions, so "zero offenders" was a fact about the exemption list rather than
        // about the codebase. A COMPLIANT producer — one that carries a grant outcome AND calls the
        // façade — is what makes the arm's silence mean something, and there was none.
        // Identity rather than a bare count (steering S3): a count is satisfied by whichever type
        // happens to route next, while the draft engine — the whole subject of M4-01b — stops.
        var routing = ScannedMethods(ProductionAssemblies.CoreModule)
            .Where(subject => GrantOutcomeNamesIn(subject.Method).Length > 0)
            .Where(subject => RoutesThrough(subject.Method, LuckFacade))
            .Select(subject => subject.Type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!routing.Contains(RoutingProducer, StringComparer.Ordinal))
        {
            offenders.Add(
                $"no method on '{RoutingProducer}' both produces a grant outcome and calls " +
                $"{LuckFacade}. The routing arm reports offenders, so it is silent both when every " +
                "producer complies and when none of them is seen at all — and until M4-01b every " +
                "producer in Core was on RoutingExemptions, which made that silence a fact about the " +
                "exemption list. This is the compliant subject that makes the arm's emptiness mean " +
                "something. If the draft engine legitimately stopped producing a DraftOption, name " +
                "the producer that took its place here in the same commit.");
        }

        var scanned = ScannedTypes(ProductionAssemblies.CoreModule).Count();

        if (scanned < ScannedTypeFloor)
        {
            offenders.Add(
                $"the routing arm scans {scanned} types in Core outside {LuckNamespace}; the floor " +
                $"is {ScannedTypeFloor}. Both arms are stated over this set, so a set this small " +
                "means Core has been split, renamed or moved and the rules are governing whatever " +
                "is left. If the set legitimately shrank — a namespace genuinely moved out of " +
                "SlayIdleRepeat.Core — lower this floor in the same commit and say in the message " +
                "which types went and where, rather than deleting the check.");
        }

        ArchRule.Empty(
            offenders,
            "24 §11's routing rule is stated over the types, the façade and the guarantee primitives " +
            "it was written against (23 §6).");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>the coverage claim over the grant source classes</b>, checkable in
    /// both directions: every class that is live has a production caller reaching it through the
    /// façade, and every class that is not has no caller at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Why the three arms above do not already say this, and this is not a fourth statement of
    /// them.</b> They are all of the form "no member of set S does X". Each is silent when the
    /// codebase complies <em>and</em> when the thing it governs was never built — so
    /// "every grant path routes through <c>LuckService</c>" reads as a claim about the ten authored
    /// classes while being, mechanically, a claim about whichever of them happen to have a producer.
    /// The gap is not hypothetical: <c>MINIGAME</c>'s protected draw answers a
    /// <c>ChestPickResolution</c>, which is not a grant-outcome type, so <b>arm 1 does not quantify
    /// over the chest pick at all</b> — the whole class could stop routing and every arm above would
    /// stay green. This is the arm that sees it.
    /// </para>
    /// <para>
    /// 🔒 <b>Stated over the façade ENTRY POINT, not over the class token</b> (steering S18).
    /// <c>SourceClass</c> is an enum, so <c>SourceClass.CHEST_STANDARD</c> compiles to a bare
    /// <c>ldc.i4</c> and leaves no type or member reference in metadata — a rule written over "who
    /// <em>names</em> the class" would report success over a codebase that named all ten. A
    /// <em>call</em> survives, so each row names the façade member a caller of that class has to go
    /// through, and the arm asks who calls it.
    /// </para>
    /// <para>
    /// ⚠️ <b>That is a simplification, not a forced choice, and saying so keeps the door open.</b>
    /// The folded constant is still on the evaluation stack: a walk that read the <c>ldc.i4</c>
    /// feeding <see cref="ResolveMember"/>'s <c>source</c> parameter and mapped it back through the
    /// enum's field constants — the same metadata <see cref="DeclaredSourceClasses"/> already reads —
    /// would attribute each call to a named class and split the five rows that share an entry point
    /// into five independent claims. It is not built because it answers nothing for a caller that
    /// passes a variable, so it would need the coarse arm underneath it anyway; the cost of leaving
    /// it out is one failure that names the caller instead of the class.
    /// </para>
    /// <para>
    /// 🔒 <b>How it bites when a fifth grant path appears.</b> Five of the six unwired classes are
    /// served by the ladder entry point, which no production type in <c>Core</c> calls today; the
    /// commit that opens a chest calls it and this arm goes red, naming the caller that arrived and
    /// asking for the row to be moved to live. The sixth — <c>WHEEL</c> — has no entry point at all,
    /// and <see cref="Every_facade_member_production_calls_is_a_grant_class_this_file_classifies"/>
    /// is what covers it: the member M4-09 reaches for will be one nothing classifies.
    /// </para>
    /// <para>
    /// ⚠️ A grant path that arrived <em>without</em> the façade is arm 1's and arm 2's to report, and
    /// the remaining way in is the one listed at the top of this file: a producer that hides its
    /// outcome behind a type <see cref="GrantOutcomeTypes"/> does not enumerate — an
    /// <c>object</c>, a tuple, a <c>string</c> item id — and never calls the façade at all. That
    /// shape is invisible to all four arms, and is the reason the closed list above is the rule's
    /// stated weak point rather than an implementation detail.
    /// </para>
    /// <para>
    /// 🔒 <b>Both floors (steering S3).</b> The rows are checked against the <c>SourceClass</c>
    /// members declared in <c>Core</c>, read out of the enum's own metadata — so an eleventh class
    /// fails the build until it is classified here — and every row's façade member has to be declared
    /// on the façade, so a renamed entry point cannot leave a row guarding a member nobody has.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_live_grant_source_class_routes_through_the_facade_and_the_rest_have_no_caller()
    {
        var offenders = new List<string>();
        var facade = Domain.FindInCore(LuckFacade);

        // The vocabulary floor is independent of the façade, so it runs even when the façade has
        // gone — a rename must not be able to hide a second, unrelated finding.
        DeclaredSourceClasses(offenders);
        LiveSourceClasses(offenders);

        if (facade is null)
        {
            offenders.Add(
                $"'{LuckFacade}' is not declared in Core, so every row names an entry point on a " +
                "type that does not exist. The identity floor beside this arm says the same thing " +
                "at more length.");

            ArchRule.Empty(offenders, "24 §11's grant source classes are covered by the façade.");

            return;
        }

        var declared = Il.AllMethods(facade).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var row in GrantSourceClasses)
        {
            if (row.FacadeEntry is not { } entry)
            {
                // 🔒 A class no façade member serves. There is nothing to ask "who calls it" about —
                // the closed-surface arm is what notices the member that will one day serve it.
                continue;
            }

            if (!declared.Contains(entry))
            {
                offenders.Add(
                    $"the '{row.Id}' grant class is stated over '{LuckFacade}.{row.FacadeEntry}', and " +
                    "the façade declares no such member. This arm asks who CALLS that member, so a " +
                    "row pointing at a member nobody declares reports 'no caller' forever — for the " +
                    "live rows that is a false failure, and for the unwired ones it is a rule that " +
                    "can never fail. Rename the row in the commit that renamed the entry point.");

                continue;
            }

            var callers = CallersOf(ProductionAssemblies.CoreModule, entry);

            if (row.Caller is { } expected)
            {
                if (callers.Length == 0)
                {
                    offenders.Add(
                        $"the '{row.Id}' grant class is declared LIVE and nothing in Core outside " +
                        $"{LuckNamespace} calls {LuckFacade}.{row.FacadeEntry}. Either the path was " +
                        "deleted — in which case move the row back to unwired with the task that owns " +
                        "rebuilding it — or it now reaches its guarantee some other way, which is the " +
                        "second place a guarantee can fire that 24 §11 forbids.");
                }
                else if (!callers.Contains(expected, StringComparer.Ordinal))
                {
                    // Identity, not a count (steering S3/S2): a count is satisfied by whichever type
                    // happens to route next, while the producer this class was wired through stops.
                    offenders.Add(
                        $"the '{row.Id}' grant class routes through {LuckFacade}.{row.FacadeEntry}, " +
                        $"but '{expected}' is not among the callers ({string.Join(", ", callers)}). " +
                        "The row names the production type that made this class live so that the arm " +
                        "quantifies over a known shape rather than over whatever is left. If the " +
                        "caller legitimately moved, name the new one here in the same commit.");
                }
            }
            else if (callers.Length > 0)
            {
                offenders.Add(
                    $"the '{row.Id}' grant class is declared NOT YET WIRED — {row.Owner} owns it — and " +
                    $"{string.Join(", ", callers)} now calls {LuckFacade}.{entry}. That is a " +
                    "FIFTH grant path, and this is the arm that was written to notice. It routes " +
                    "through the façade, which is what 24 §11 asks for, so this is good news: move " +
                    $"the row to live, name the caller on it, and drive the class through " +
                    "MetaLoopTests' loop if a player can reach it. ⚠️ Five classes share this entry " +
                    "point, so this names the caller rather than the class — read the caller to see " +
                    "which of the five arrived.");
            }
        }

        ArchRule.Empty(
            offenders,
            "24 §11: every grant source class that is live routes through " + LuckFacade + ", and " +
            "every class that is not has no caller at all.");
    }

    /// <summary>
    /// 🔒 `24` §11 — <b>the façade's called surface is closed.</b> Every <see cref="LuckFacade"/>
    /// member production reaches for is one <see cref="CalledFacadeMembers"/> names, and every name
    /// on that list is still reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arm that covers a class no entry point serves.</b> The coverage arm beside it
    /// asks, per class, "does anything call the member this class goes through" — which is unaskable
    /// for <c>WHEEL</c>, because the reader's ladder map holds five classes and the wheel is not one
    /// of them, so <see cref="ResolveMember"/> throws for it and no other member serves it yet. The
    /// day M4-09 wires the wheel it will reach for something, and whatever that is, this list does
    /// not name it.
    /// </para>
    /// <para>
    /// 🔒 <b>Both directions, so it cannot rot in either.</b> An unenumerated call is a grant path
    /// nobody classified; an enumerated member nothing calls is a row describing a caller that has
    /// gone, which is <see cref="Every_exempted_producer_still_needs_its_exemption"/>'s argument
    /// applied to the other list in this file.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it cannot see.</b> The same holes the coverage arm has, and for the same reason —
    /// it shares <see cref="CallersOf"/>. Chiefly: a call made inside a <b>lambda</b>, whose display
    /// class <see cref="ScannedTypes"/> drops as compiler-generated. A chest opener that reached the
    /// façade from inside a <c>Select</c> would be invisible to both. That is worth naming here
    /// rather than in the coverage arm alone, because a batch opener — <c>OPEN ALL</c> is M4-02's
    /// own deliverable — is exactly the shape that gets written with a projection.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_facade_member_production_calls_is_a_grant_class_this_file_classifies()
    {
        var offenders = new List<string>();
        var enumerated = CalledFacadeMembers.Select(row => row.Member).ToArray();
        var called = FacadeMembersCalledFromProduction(ProductionAssemblies.CoreModule);

        foreach (var (member, callers) in called)
        {
            if (enumerated.Contains(member, StringComparer.Ordinal))
            {
                continue;
            }

            offenders.Add(
                $"{string.Join(", ", callers)} calls {LuckFacade}.{member}, and no row in " +
                "CalledFacadeMembers says which grant class that serves. A new door onto the façade " +
                "is a new grant path — 24 §3 authors ten classes and six of them are unwired, so the " +
                "likeliest reading of this failure is that one of them just landed. Add a row naming " +
                "the class it serves, and move that class's GrantSourceClasses row to live with its " +
                "caller on it.");
        }

        foreach (var (member, serves) in CalledFacadeMembers)
        {
            if (!called.ContainsKey(member))
            {
                offenders.Add(
                    $"'{LuckFacade}.{member}' is enumerated as production's door onto {serves}, and " +
                    "nothing in Core outside " + LuckNamespace + " calls it. Either that path was " +
                    "deleted — say so and take the row out — or it now reaches its decision another " +
                    "way, which is the second place a guarantee can fire that 24 §11 forbids. A list " +
                    "with a stale row is a closed surface with a hole in it.");
            }
        }

        ArchRule.Empty(
            offenders,
            "24 §11: the set of " + LuckFacade + " members production calls is exactly the set this " +
            "file classifies.");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>every exemption is still needed.</b> An exempted type that no
    /// longer trips the routing arm is a stale exemption, and a stale exemption is a rule quietly
    /// narrowed.
    /// </summary>
    /// <remarks>
    /// This is what makes <see cref="RoutingExemptions"/> a list with an expiry rather than a
    /// suppression file — and the expiry has been collected once, which is worth recording because a
    /// mechanism nobody has watched fire reads exactly like one that cannot. The two draft entries
    /// were the ones that mattered; M4-01b routed the draft through the façade, this rule went red on
    /// both, and both came out in that commit. The two rows left are the tuning reader and its rung
    /// rows, whose exemption has no design in which it expires.
    /// </remarks>
    [Fact]
    public void Every_exempted_producer_still_needs_its_exemption()
    {
        var offenders = new List<string>();

        foreach (var (type, reason) in RoutingExemptions)
        {
            var methods = ScannedMethods(ProductionAssemblies.CoreModule)
                .Where(subject => subject.Type.Name.Equals(type, StringComparison.Ordinal))
                .ToArray();

            if (methods.Length == 0)
            {
                offenders.Add(
                    $"'{type}' is exempted from the routing rule, but no such type is scanned in Core " +
                    "at all. It was renamed, moved out of Core or deleted, and the exemption now " +
                    $"covers nothing. Reason on the row: {reason}");
                continue;
            }

            if (!methods.Any(subject => BypassesTheFacade(subject.Method, LuckFacade)))
            {
                offenders.Add(
                    $"'{type}' is exempted from the routing rule and no longer trips it — every " +
                    "grant-outcome-producing method on it now routes through " + LuckFacade + ", or " +
                    "it has stopped producing one. Delete the row in the same commit that made it " +
                    $"unnecessary. Reason on the row: {reason}");
            }
        }

        ArchRule.Empty(
            offenders,
            "Every routing exemption still covers a producer that genuinely bypasses LuckService " +
            "(24 §11, 23 §6).");
    }

    /// <summary>
    /// 🔒 `24` §11 / `24` §3 — <b>a pity counter key is formed in exactly one place.</b> No type in
    /// <c>Core</c> outside <see cref="CounterKeyFormationPoint"/> spells an authored counter key as
    /// a literal, on its own or with the separator that pairs it to a guarantee.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Why the two arms above are not enough, and this is not a third statement of them.</b>
    /// `24` §11 makes two claims, and the arms above answer only the second. "A guarantee can fire
    /// in one place" is about the <em>decision</em>, and <c>HardPity</c> is that place. But `24` §3
    /// makes the counter <em>keys</em> authored data — <c>chest.standard</c>, <c>drop.run</c>,
    /// <c>minigame.chestpick</c> — and <c>CHEST_STANDARD</c> runs three ladders at once, so a
    /// counter is addressed by pairing an authored key with the guarantee it protects. That pairing
    /// is formed in one place too (<c>LuckTuning.CounterKey</c>), and nothing was watching it: a
    /// caller writing <c>"chest.standard:" + rarity</c> by hand reads the right counter today and
    /// the wrong one — silently, with no counter to show for it — the day the key is re-authored.
    /// A pity counter that quietly starts over is the exact failure `24` §1.1's "never reset"
    /// exists to forbid, and it would be invisible in every test that builds its own map.
    /// </para>
    /// <para>
    /// 🔒 <b>The authored keys are read from the shipped document, not transcribed.</b> A
    /// hand-written list here would be a second statement of `24` §3's table, and the two would
    /// drift in exactly the direction that matters: a key renamed in <c>luck.json</c> and left here
    /// leaves the rule guarding a spelling nobody uses.
    /// </para>
    /// <para>
    /// 🔒 <b>The separator is read out of the production <c>const</c>'s metadata</b>, which is the
    /// answer to steering S18 rather than a victim of it. The compiler folds a <c>const</c> at every
    /// <em>use</em> site, so no IL rule can see it being read — but the <em>declaration</em> survives
    /// as a field constant, and that is what this reads. A rename or a re-spelling of
    /// <c>CounterKeySeparator</c> therefore moves this rule with it instead of leaving it matching
    /// a colon nobody writes.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it cannot see.</b> A key assembled from a fragment that is not the whole authored
    /// key (<c>"chest." + "standard"</c>), a key read from somewhere other than <c>luck.json</c>,
    /// and — as everywhere else in this file — anything outside <c>Core</c>. It closes the spelling
    /// a bypass would actually be written in, not every spelling one could be.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite, on real production IL, in two shapes with a discriminating control.</b>
    /// A probe type under <c>Rules/Perks/</c> composing <c>"chest.standard:" + Rarity.A</c> was
    /// reported as <em>"spells the authored counter key 'chest.standard' as the literal
    /// \"chest.standard:\""</em>; a second method returning the bare <c>"drop.run"</c> was reported
    /// as <em>"spells the authored counter key 'drop.run'"</em>. A third method on the same type
    /// returning <c>"chest.standard.total"</c> — which <em>starts with</em> an authored key and is
    /// not one — was correctly ignored, so the match is the key-plus-separator pairing rather than a
    /// substring. Both floors were probed separately: the count floor raised past the real set
    /// reported <em>"game-data/tuning/luck.json yields 8 authored counter keys; the floor is 99"</em>,
    /// the identity floor pointed at a key nothing authors reported <em>"'chest.nonexistent' is not
    /// among the authored counter keys"</em>, and pointing the separator read at a field
    /// <c>LuckTuning</c> does not declare threw <em>"'LuckTuning' declares no constant
    /// 'NotADeclaredConstant'"</em> rather than falling back to a guessed colon. All reverted.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_pity_counter_key_is_spelled_outside_the_tuning_reader()
    {
        var keys = AuthoredCounterKeys.Value;
        var separator = CounterKeySeparatorDeclaredInCore();
        var offenders = new List<string>();

        foreach (var type in Il.AllTypes(ProductionAssemblies.CoreModule)
                     .Where(t => !Domain.IsCompilerGenerated(t))
                     .Where(t => !Il.NamespaceOf(t).Equals(FormationPointNamespace, StringComparison.Ordinal) ||
                                 !t.Name.Equals(CounterKeyFormationPoint, StringComparison.Ordinal)))
        {
            foreach (var method in Il.AllMethods(type))
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    if (instruction.OpCode.Code != Code.Ldstr ||
                        instruction.Operand is not string literal)
                    {
                        continue;
                    }

                    var spelled = keys.FirstOrDefault(
                        key => literal.Equals(key, StringComparison.Ordinal) ||
                               literal.StartsWith(key + separator, StringComparison.Ordinal));

                    if (spelled is null)
                    {
                        continue;
                    }

                    offenders.Add(
                        $"{Il.Describe(method)} spells the authored counter key '{spelled}' as the " +
                        $"literal \"{literal}\". 24 §3 authors the counter keys in " +
                        $"{CounterKeyDocument}, and {CounterKeyFormationPoint}.CounterKey is the one " +
                        "place a key is formed out of them — a hand-composed key addresses the right " +
                        "counter until the document renames it, and then addresses a counter nobody " +
                        "writes, which reads to the player as a pity counter that silently started " +
                        "over. Ask the tuning reader for the key.");
                }
            }
        }

        // 🔒 S3 — the floors, in both directions. This is "no literal matches any authored key", so
        // it passes forever if the authored set empties (a renamed pointer, a moved document) or if
        // the separator read comes back as something no key is formed with.
        if (keys.Count < AuthoredCounterKeyFloor)
        {
            offenders.Add(
                $"{CounterKeyDocument} yields {keys.Count} authored counter keys; the floor is " +
                $"{AuthoredCounterKeyFloor}. The rule matches literals against that set, so an empty " +
                "or shrunken one reports success over every hand-composed key in Core. If the " +
                "registry legitimately moved, point this at the new pointer — do not lower the floor " +
                "to whatever is left.");
        }

        if (!keys.Contains(KnownCounterKey, StringComparer.Ordinal))
        {
            offenders.Add(
                $"'{KnownCounterKey}' is not among the authored counter keys. It is the identity " +
                "under the count above: 24 §4.1's ten-chest ladder is the most-drawn protected " +
                "source in the game, and a set that no longer contains it is a set this rule was not " +
                "written against, whatever its size.");
        }

        ArchRule.Empty(
            offenders,
            "24 §3 / 24 §11: the pity counter keys are authored data, and LuckTuning.CounterKey is " +
            "the one place a counter id is formed out of them.");
    }

    /// <summary>
    /// 🔒 `24` §11 / `23` §6 — <b>the teeth</b> (steering S1). The routing predicate is driven
    /// against real IL compiled from <see cref="LuckRoutingFixtures"/>: it must catch a producer that
    /// bypasses the façade, pass one that routes through it, and pass a method that grants nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixtures live in this assembly rather than in <c>Core</c> because the shape that matters
    /// <em>is</em> the violation, and a violation is never committed to the domain to prove a rule
    /// works — the same construction <c>AccessibilityBoundaryTests</c> and <c>DomainPurityTests</c>
    /// both use.
    /// </para>
    /// <para>
    /// The third case carries as much weight as the first two. Without it the predicate could be
    /// keyed on "does not call <c>LuckService</c>" alone, which is true of almost every method in the
    /// assembly, and the rule would be a wall of false failures that gets deleted rather than obeyed.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_routing_predicate_catches_a_bypassing_producer_and_passes_a_routed_one()
    {
        Assert.True(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsWithoutRouting)), LuckFacade),
            "a method that answers a grant outcome and never names the façade is the exact shape " +
            "24 §11 forbids. If this is false the routing arm is matching nothing and reports " +
            "success over every producer in Core.");

        Assert.False(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsThroughTheFacade)), LuckFacade),
            "resolving through the façade is precisely what the rule asks for. If this is true the " +
            "rule flags the compliant path as well as the bypass, and would be weakened back out " +
            "within a commit rather than obeyed.");

        Assert.False(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GrantsNothing)), LuckFacade),
            "a method that produces no grant outcome is not a bypass, however little it mentions the " +
            "façade. If this is true the predicate is keyed on the absent call alone and would " +
            "report every method in the assembly.");

        // 🔒 The second shape, and the question the three cases above cannot answer: does the rule
        // bite the day M4-03's gear generator lands? A batch generator answering
        // IReadOnlyList<GearInstance> is at least as likely as one answering a single instance, and
        // it only trips the predicate if Il.SignatureTypes and Il.Flatten see through the wrapper.
        Assert.True(
            BypassesTheFacade(Fixture(nameof(LuckRoutingFixtures.GeneratesManyWithoutRouting)), LuckFacade),
            "a generator answering a COLLECTION of grant outcomes is producing them just as surely " +
            "as one answering a single instance, and a chest opens several items at once. If this is " +
            "false the matcher cannot see through a generic wrapper and M4-03's most likely shape " +
            "walks straight past the rule.");

        // 🔒 The scan, not just the predicate. A producer declared ON the outcome type —
        // GearInstance.Roll(), the factory shape — was invisible to the first version of this rule
        // because ScannedTypes dropped the whole type. Driven over this assembly's own metadata,
        // because the shape being proved is the violation.
        var scannedFixtures = ScannedMethods(SuiteAssembly.Module)
            .Where(subject => subject.Type.Name.Equals(
                nameof(LuckRoutingFixtures.GearInstance), StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            scannedFixtures.Any(subject =>
                subject.Method.Name.Equals(
                    nameof(LuckRoutingFixtures.GearInstance.Roll), StringComparison.Ordinal) &&
                BypassesTheFacade(subject.Method, LuckFacade)),
            "a hand-written factory declared on the grant outcome itself is the single most likely " +
            "shape M4-03 will write, and it must be scanned like any other producer. If this is " +
            "false the type-level exclusion is back and the rule reports success on the exact commit " +
            "it exists to catch.");

        // 🔒 The negative control for that narrowing, over REAL production IL. A record mentions its
        // own type in its equality and printing members whether or not it grants anything, and
        // reporting those would make the rule noise rather than a rule. DraftOption is a live
        // readonly record struct in Core with nothing but synthesized members.
        Assert.DoesNotContain(
            ScannedMethods(ProductionAssemblies.CoreModule),
            subject => subject.Type.Name.Equals("DraftOption", StringComparison.Ordinal));
    }

    /// <summary>
    /// Types a module's routing arm quantifies over: everything outside <c>Rules.Luck</c>, less the
    /// compiler's own.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>It no longer excludes the grant-outcome types themselves, and that was a hole.</b> The
    /// first version dropped every type whose simple name was in <see cref="GrantOutcomeTypes"/>,
    /// because a record mentions its own type in its constructor and its equality members and
    /// reporting those would be noise. But the shape M4-03 is most likely to write is a factory
    /// <em>on</em> the outcome — <c>GearInstance.Roll(…)</c> — and a whole-type exclusion made
    /// exactly that invisible: the rule would have reported success on the commit it exists to
    /// catch. The exclusion is now per-member (<see cref="MentionsItsOwnTypeByConstruction"/>), so
    /// the synthesized members are still silent and a hand-written producer is not.
    /// <see cref="The_routing_predicate_catches_a_bypassing_producer_and_passes_a_routed_one"/>
    /// drives both halves of that against real IL.
    /// </remarks>
    /// <param name="module">The module to scan — <c>Core</c> for the rules, this assembly for the teeth.</param>
    /// <returns>The scanned types.</returns>
    private static IEnumerable<TypeDefinition> ScannedTypes(ModuleDefinition module) =>
        Il.AllTypes(module)
          .Where(t => !Domain.IsCompilerGenerated(t))
          .Where(t => !Il.IsUnder(Il.NamespaceOf(t), LuckNamespace));

    /// <summary>Every author-written method on a scanned type, with the type it belongs to.</summary>
    /// <param name="module">The module to scan.</param>
    /// <returns>The scanned methods, paired with their declaring types.</returns>
    private static IEnumerable<(TypeDefinition Type, MethodDefinition Method)> ScannedMethods(
        ModuleDefinition module) =>
        ScannedTypes(module).SelectMany(
            type => Il.AllMethods(type)
                      .Where(m => !Domain.IsCompilerGenerated(m))
                      .Where(m => !MentionsItsOwnTypeByConstruction(type, m))
                      .Select(m => (Type: type, Method: m)));

    /// <summary>
    /// The members of a grant-outcome type that carry its own name whether or not it grants
    /// anything: a record's constructor, its equality, printing and deconstruction members, and its
    /// property accessors.
    /// </summary>
    /// <remarks>
    /// Named rather than detected. Cecil marks only <c>&lt;Clone&gt;$</c> and the equality-contract
    /// getter with <c>CompilerGeneratedAttribute</c>, so <c>Domain.IsCompilerGenerated</c> sees
    /// through none of the rest — a synthesized <c>Equals(DraftOption)</c> is indistinguishable from
    /// a hand-written one in metadata, and the list is the honest way to say which names are
    /// bookkeeping.
    /// </remarks>
    private static readonly string[] SelfNamingMembers =
    {
        ".ctor",
        ".cctor",
        "<Clone>$",
        "Deconstruct",
        "Equals",
        "GetHashCode",
        "PrintMembers",
        "ToString",
        "op_Equality",
        "op_Inequality",
    };

    /// <summary>True for a member that names its own grant-outcome type by construction.</summary>
    private static bool MentionsItsOwnTypeByConstruction(TypeDefinition type, MethodDefinition method) =>
        GrantOutcomeTypes.Contains(type.Name, StringComparer.Ordinal) &&
        (method.IsGetter ||
         method.IsSetter ||
         SelfNamingMembers.Contains(method.Name, StringComparer.Ordinal));

    /// <summary>
    /// True for a method that answers, accepts <b>or constructs</b> a grant outcome and never names
    /// the façade.
    /// </summary>
    /// <remarks>
    /// Takes the façade's name rather than reading <see cref="LuckFacade"/>, so the teeth check drives
    /// the same predicate the rule does instead of a second copy of it.
    /// </remarks>
    private static bool BypassesTheFacade(MethodDefinition method, string facade) =>
        GrantOutcomesProducedBy(method).Length > 0 && !RoutesThrough(method, facade);

    /// <summary>
    /// 🔴 Every way this rule knows to spot a producer: the grant-outcome names in a method's
    /// signature, <b>plus</b> the ones its body constructs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The signature half alone was a hole, and M4-05 authored the first shape that fell into
    /// it.</b> <c>Inventory.SetLock(GearInstanceId, bool) → bool</c> builds a replacement
    /// <c>GearInstance</c> in its body and names none anywhere in its signature, so
    /// <see cref="GrantOutcomeNamesIn"/> could not see it at all. Until that commit every
    /// <c>GearInstance</c> construction in <c>Core</c> surfaced through a signature, which is why the
    /// hole had never shown: a whole grant path could have been written as
    /// <c>Grant(string defId, out bool granted)</c> with the item constructed inside and stored, and
    /// the rule would have reported success over it.
    /// </para>
    /// <para>
    /// A <c>newobj</c> walk rather than a wider signature match, because the claim being made is
    /// "this method <em>produced</em> one" and constructing it is the most direct evidence there is.
    /// The bookkeeping exclusion still applies first (<see cref="ScannedMethods"/>), so a record's own
    /// constructor and <c>&lt;Clone&gt;$</c> are not reported as producers of themselves.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite, on real production IL, and specifically on the half that is new.</b>
    /// Dropping the signature half of this method (so only <see cref="GrantOutcomesConstructedBy"/>
    /// remained) and renaming <c>Inventory</c>'s <see cref="RoutingExemptions"/> row turned the
    /// routing arm red with two offenders — <c>SlayIdleRepeat.Core.Model.Gear.Inventory.SetLock</c>
    /// and <c>.ReadItems</c>, both reported as <em>"produces a grant outcome (GearInstance) and never
    /// names LuckService"</em>. <c>SetLock</c> is the one that matters: its signature is
    /// <c>(GearInstanceId, bool) → bool</c>, so the signature half sees nothing in it at all, and
    /// before this walk existed the rule reported success over it. Reverted; both halves are live and
    /// the exemption is back.
    /// </para>
    /// </remarks>
    /// <param name="method">The method to judge.</param>
    /// <returns>The grant-outcome names it produces, in name order.</returns>
    private static string[] GrantOutcomesProducedBy(MethodDefinition method) =>
        GrantOutcomeNamesIn(method)
          .Concat(GrantOutcomesConstructedBy(method))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

    /// <summary>The grant-outcome types a method body constructs with <c>newobj</c>.</summary>
    /// <remarks>
    /// Matched on the constructed type's <em>simple</em> name, exactly as
    /// <see cref="GrantOutcomeNamesIn"/> matches a signature type's — the same closed list, read from
    /// the instruction stream instead of the signature.
    /// </remarks>
    private static string[] GrantOutcomesConstructedBy(MethodDefinition method) =>
        Il.Instructions(method)
          .Where(instruction => instruction.OpCode.Code == Code.Newobj)
          .Select(instruction => instruction.Operand)
          .OfType<MethodReference>()
          .Select(constructor => constructor.DeclaringType.Name)
          .Where(name => GrantOutcomeTypes.Contains(name, StringComparer.Ordinal))
          .Distinct(StringComparer.Ordinal)
          .ToArray();

    /// <summary>
    /// The simple names of every type in <c>Core</c> outside <c>Rules.Luck</c> whose IL calls the
    /// named member of the façade.
    /// </summary>
    /// <remarks>
    /// A <c>call</c> to a named member rather than <see cref="RoutesThrough"/>'s "names the façade at
    /// all": the coverage arm's whole question is <em>which</em> door a caller came through, and
    /// every one of the ten classes goes through the same type.
    /// </remarks>
    /// <param name="module">The module to scan.</param>
    /// <param name="member">The façade member.</param>
    /// <returns>The calling types' simple names, in name order.</returns>
    private static string[] CallersOf(ModuleDefinition module, string member) =>
        FacadeMembersCalledFromProduction(module).TryGetValue(member, out var callers)
            ? callers
            : [];

    /// <summary>
    /// Every <see cref="LuckFacade"/> member called from <c>Core</c> outside <c>Rules.Luck</c>, with
    /// the simple names of the types that call it.
    /// </summary>
    /// <remarks>
    /// One walk of the module for all of them, memoised: the coverage arm asks about ten rows and the
    /// closed-surface arm about a dozen members, and a scan per question was the slowest case in this
    /// file.
    /// </remarks>
    private static IReadOnlyDictionary<string, string[]> FacadeMembersCalledFromProduction(
        ModuleDefinition module)
    {
        if (FacadeCallCache.TryGetValue(module, out var cached))
        {
            return cached;
        }

        var calls = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var type in ScannedTypes(module))
        {
            foreach (var callee in Il.AllMethods(type)
                         .SelectMany(Il.Instructions)
                         .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt)
                         .Select(instruction => instruction.Operand)
                         .OfType<MethodReference>()
                         .Where(callee =>
                             callee.DeclaringType.Name.Equals(LuckFacade, StringComparison.Ordinal)))
            {
                if (!calls.TryGetValue(callee.Name, out var callers))
                {
                    callers = new SortedSet<string>(StringComparer.Ordinal);
                    calls[callee.Name] = callers;
                }

                callers.Add(type.Name);
            }
        }

        var resolved = calls.ToDictionary(
            row => row.Key, row => row.Value.ToArray(), StringComparer.Ordinal);

        FacadeCallCache[module] = resolved;

        return resolved;
    }

    private static readonly Dictionary<ModuleDefinition, IReadOnlyDictionary<string, string[]>>
        FacadeCallCache = [];

    /// <summary>
    /// The floor that keeps <see cref="GrantSourceClasses"/> honest against the authored vocabulary:
    /// its rows are exactly the <c>SourceClass</c> members <c>Core</c> declares.
    /// </summary>
    /// <remarks>
    /// Read from the enum's <b>field metadata</b>, which survives even though every <em>use</em> of a
    /// member is folded to a literal (steering S18). <c>value__</c> is the enum's backing field and is
    /// not a member.
    /// </remarks>
    /// <param name="offenders">The list to report into.</param>
    private static void DeclaredSourceClasses(List<string> offenders)
    {
        var vocabulary = Domain.FindInCore(SourceClassVocabulary);

        if (vocabulary is null)
        {
            offenders.Add(
                $"'{SourceClassVocabulary}' is not declared in Core, so this arm's rows are floored " +
                "against nothing and a grant class could be added to luck.json with no row here. If " +
                "the vocabulary type was renamed, rename SourceClassVocabulary in the same commit.");

            return;
        }

        var declared = vocabulary.Fields
            .Where(field => field.IsStatic && field.HasConstant)
            .Select(field => field.Name)
            .ToArray();

        var rows = GrantSourceClasses.Select(row => row.Id).ToArray();

        foreach (var missing in declared.Except(rows, StringComparer.Ordinal))
        {
            offenders.Add(
                $"'{missing}' is a declared {SourceClassVocabulary} and has no row in " +
                "GrantSourceClasses, so this arm says nothing about it at all — a grant class the " +
                "registry authors could be wired with nothing here noticing. Add a row: the façade " +
                "member a caller has to go through, its production caller if it has one, and the " +
                "task that owns wiring it if it does not.");
        }

        foreach (var stale in rows.Except(declared, StringComparer.Ordinal))
        {
            offenders.Add(
                $"'{stale}' has a row in GrantSourceClasses and is not a declared " +
                $"{SourceClassVocabulary}. The row governs a class nobody authors, which is a row " +
                "that can never fail.");
        }
    }

    /// <summary>The floor under the LIVE half of the coverage arm (steering S3).</summary>
    /// <remarks>
    /// 🔴 Counted over <b>production IL</b>, not over the rows. Counting the rows would count a
    /// hand-written literal array — the same four <c>Caller</c> strings on every run, so
    /// <c>4 &lt; 4</c> forever — which is a self-check on the table rather than a floor on the
    /// subject set the arm quantifies over. What this catches is the whole live half going silent at
    /// once: a façade renamed out from under every caller, a <c>Rules.Luck</c> namespace that
    /// swallowed the producers, an <see cref="Il"/> change that blinded the walk. A single class
    /// going quiet is the per-row branch's to report, and it names which.
    /// </remarks>
    /// <param name="offenders">The list to report into.</param>
    private static void LiveSourceClasses(List<string> offenders)
    {
        var live = GrantSourceClasses.Count(
            row => row.FacadeEntry is { } entry &&
                   CallersOf(ProductionAssemblies.CoreModule, entry).Length > 0);

        if (live < LiveSourceClassFloor)
        {
            offenders.Add(
                $"only {live} grant source classes have a production caller reaching their façade " +
                $"entry point; the floor is {LiveSourceClassFloor}. Every unwired row asserts that " +
                "NOTHING calls its entry point, which is a claim a codebase with no grant paths at " +
                "all satisfies completely — so the live half is what stops this arm from passing " +
                "over an empty game. Four paths were live at the end of M4 and this floor is that " +
                "number, not a margin below it.");
        }
    }

    /// <summary>The guarantee primitives' full names — the guarded set both arms are stated over.</summary>
    private static string[] GuardedPrimitiveNames() =>
        GuaranteePrimitives.Select(name => LuckNamespace + "." + name).ToArray();

    /// <summary>
    /// The guarantee primitives a type's IL actually names, resolved through the same matcher
    /// <see cref="A_guarantee_can_only_fire_inside_Rules_Luck"/> uses.
    /// </summary>
    private static string[] GuaranteePrimitivesReachedBy(TypeDefinition type)
    {
        var guarded = GuardedPrimitiveNames();

        return Il.ReferencedTypeNames(type)
                 .Where(referenced => guarded.Contains(referenced, StringComparer.Ordinal))
                 .Distinct(StringComparer.Ordinal)
                 .OrderBy(name => name, StringComparer.Ordinal)
                 .ToArray();
    }

    /// <summary>
    /// The guarantee primitives whose <b>members a type actually invokes</b> — the narrower half of
    /// the façade floor, walking the instruction stream rather than the reference table.
    /// </summary>
    private static string[] GuaranteePrimitivesCalledBy(TypeDefinition type) =>
        Il.AllMethods(type)
          .SelectMany(Il.Instructions)
          .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
          .Select(instruction => instruction.Operand)
          .OfType<MethodReference>()
          .Select(callee => callee.DeclaringType.Name)
          .Where(name => GuaranteePrimitives.Contains(name, StringComparer.Ordinal))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

    /// <summary>
    /// The counter-key separator, read out of the production <c>const</c>'s <b>declaration</b> in
    /// metadata — the one place a folded constant still exists (steering S18).
    /// </summary>
    /// <returns>The separator character.</returns>
    private static char CounterKeySeparatorDeclaredInCore()
    {
        var reader = Domain.FindInCore(CounterKeyFormationPoint)
            ?? throw new InvalidOperationException(
                $"'{CounterKeyFormationPoint}' is not declared in Core, so there is no counter-key " +
                "formation point for arm 3 to exempt — and no separator to read. If the tuning " +
                "reader was renamed, rename CounterKeyFormationPoint in the same commit.");

        var field = reader.Fields.FirstOrDefault(
            f => f.Name.Equals(CounterKeySeparatorField, StringComparison.Ordinal) && f.HasConstant)
            ?? throw new InvalidOperationException(
                $"'{CounterKeyFormationPoint}' declares no constant '{CounterKeySeparatorField}'. " +
                "Arm 3 pairs an authored key to its guarantee with that character; guessing one here " +
                "would leave the rule matching a spelling production does not use.");

        return field.Constant is char separator
            ? separator
            : throw new InvalidOperationException(
                $"'{CounterKeyFormationPoint}.{CounterKeySeparatorField}' is not a char constant " +
                $"but a {field.Constant?.GetType().Name ?? "null"}. Arm 3 concatenates it onto an " +
                "authored key, and a separator of some other shape means the key spelling this rule " +
                "guards is no longer the one production forms.");
    }

    /// <summary>The simple name of a namespace-qualified type name.</summary>
    private static string SimpleNameOf(string fullName) =>
        fullName[(fullName.LastIndexOf('.') + 1)..];

    /// <summary>The grant-outcome names a method's signature mentions, flattened through generics.</summary>
    /// <remarks>
    /// Signature types rather than the return type alone: a producer that hands its outcome back
    /// through an <c>out</c> parameter or a builder argument is producing one just as surely as one
    /// that returns it, and both spellings are ordinary C#.
    /// </remarks>
    private static string[] GrantOutcomeNamesIn(MethodDefinition method) =>
        Il.SignatureTypes(method)
          .SelectMany(Il.Flatten)
          .Select(reference => reference.Name)
          .Where(name => GrantOutcomeTypes.Contains(name, StringComparer.Ordinal))
          .Distinct(StringComparer.Ordinal)
          .OrderBy(name => name, StringComparer.Ordinal)
          .ToArray();

    /// <summary>True when a method body calls a member of a type with the façade's simple name.</summary>
    private static bool RoutesThrough(MethodDefinition method, string facade) =>
        Il.Instructions(method).Any(instruction =>
            instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj &&
            instruction.Operand is MethodReference callee &&
            callee.DeclaringType.Name.Equals(facade, StringComparison.Ordinal));

    /// <summary>
    /// The floor under the routing arm's subject set — <c>Core</c>'s types outside
    /// <c>Rules.Luck</c>. 468 on the commit the architecture review narrowed the scan, measured
    /// rather than estimated (the floor was briefly raised past it and the rule's own message
    /// reported the count); set well below so adding or removing a type is not a test edit.
    /// </summary>
    private const int ScannedTypeFloor = 400;

    /// <summary>One fixture method, by name, out of this assembly's own metadata.</summary>
    private static MethodDefinition Fixture(string name) =>
        SuiteAssembly.Type(nameof(LuckRoutingFixtures))
                     .Methods.Single(m => m.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>
    /// The three IL shapes the routing predicate has to tell apart. They live here rather than in
    /// <c>Core</c> because the first one is the violation.
    /// </summary>
    private static class LuckRoutingFixtures
    {
        /// <summary>🔴 Answers a grant outcome and never names the façade — the shape `24` §11 forbids.</summary>
        /// <returns>A grant, drawn out of nowhere.</returns>
        internal static GearInstance GrantsWithoutRouting() => new();

        /// <summary>Answers the same grant outcome, resolved through the façade. Must not be flagged.</summary>
        /// <returns>A grant the façade decided.</returns>
        internal static GearInstance GrantsThroughTheFacade() => LuckService.Resolve();

        /// <summary>
        /// Produces no grant outcome at all. The case that keeps the predicate from being keyed on
        /// the absent call alone.
        /// </summary>
        /// <returns>An ordinary number.</returns>
        internal static long GrantsNothing() => 1L;

        /// <summary>
        /// 🔴 M4-03's other likely shape: a batch generator answering a <em>collection</em> of grant
        /// outcomes, which only trips the predicate if the matcher flattens the generic wrapper.
        /// </summary>
        /// <returns>A handful of grants, drawn out of nowhere.</returns>
        internal static IReadOnlyList<GearInstance> GeneratesManyWithoutRouting() =>
            new[] { new GearInstance() };

        /// <summary>
        /// Stands in for M4-03's gear instance — a <b>pre-registered</b> member of
        /// <see cref="GrantOutcomeTypes"/>, chosen precisely because <c>Core</c> declares no such
        /// type, so the fixture cannot be confused with a production one.
        /// </summary>
        internal sealed class GearInstance
        {
            /// <summary>
            /// 🔴 The factory-on-the-outcome shape, and the one the first version of this rule could
            /// not see: <c>ScannedTypes</c> dropped every type named in
            /// <see cref="GrantOutcomeTypes"/>, so a producer declared here was excluded along with
            /// the record bookkeeping the exclusion was written for.
            /// </summary>
            /// <returns>A grant, drawn out of nowhere.</returns>
            internal static GearInstance Roll() => new();
        }

        /// <summary>Stands in for the façade, so the routed fixture has something real to call.</summary>
        private static class LuckService
        {
            /// <summary>Resolves a grant.</summary>
            /// <returns>The grant.</returns>
            internal static GearInstance Resolve() => new();
        }
    }
}
