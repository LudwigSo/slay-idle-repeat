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
/// <b>What it buys, concretely.</b> Five grant paths are still unbuilt — chests, eggs, crates, the
/// wheel and the perk draft's five composition rules — and each will be written by a different task
/// in a different milestone. This rule is what makes the fifth of them fail on the commit that adds
/// it rather than on the commit that finally reads `24` again.
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
///   <item>A <c>const</c> read across the boundary in arm 2 — the compiler folds it, so no type
///   reference survives into metadata. The same hole <c>IntraRulesLayeringRuleTests</c> records,
///   and narrow for the same reason: none of the four guarantee types declares a <c>const</c> a
///   caller outside the namespace could want. ⚠️ The <em>counter-key</em> vocabulary has exactly
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
    };

    /// <summary>
    /// 🔒 The guarantee primitives: the four types that decide, ramp, bank or reshape a protected
    /// draw. Naming one from outside <c>Rules.Luck</c> is a second place a guarantee can fire.
    /// </summary>
    private static readonly string[] GuaranteePrimitives =
    {
        "HardPity",
        "SoftPity",
        "MercyAccrual",
        "RarityTable",
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

        // 🔒 M4-01b's, and named with its owner rather than exempted quietly. The perk draft's five
        // 24 §4.7 rules — Legendary pity, the anti-brick Sustain guarantee, the quality floor, the
        // Codex bias and the upgrade famine — are the ONE live grant path that does not route yet,
        // and every one of those five is a guarantee in the sense this rule is about.
        // DraftCompositionRules already declares all eight seams as documented no-ops. When M4-01b
        // wires them through the façade these three entries come out, and the companion fact below
        // fails on the day they stop being needed.
        ("PerkDraftEngine", "M4-01b — the 24 §4.7 draft rules are declared as no-op seams and not yet wired to the façade; this is the one LIVE grant path that still bypasses it, and it is exempted with an owner rather than passed over"),
        ("PickPerk", "M4-01b — the handler that calls the draft engine, and it inherits the engine's exemption for exactly as long as the engine has one"),
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
    /// ⚠️ <b>And the uncomfortable half of that, stated rather than left to be discovered.</b> Every
    /// method in <c>Core</c> that trips this predicate today is on <see cref="RoutingExemptions"/>:
    /// <c>Rarity</c> is carried only by the tuning reader and its rung rows, <c>DraftOption</c> only
    /// by the draft engine and its handler. The arm therefore reports zero offenders because the
    /// four exempted rows cover all four producers — not because nothing in <c>Core</c> produces a
    /// grant. What keeps that from being a rule asleep is
    /// <see cref="Every_exempted_producer_still_needs_its_exemption"/>, which drives this same
    /// predicate over those four types and <em>requires</em> it to answer true. The two facts
    /// together say "the predicate fires on real production IL, and the only things it fires on are
    /// the four we named"; neither says it alone.
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
                $"({string.Join(", ", GrantOutcomeNamesIn(subject.Method))}) and never names {LuckFacade}. " +
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
    /// <c>Rules.Luck</c> names <c>HardPity</c>, <c>SoftPity</c>, <c>MercyAccrual</c> or
    /// <c>RarityTable</c>: the façade is the sole door onto all four.
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
    /// outside one namespace — hundreds — and all four target types resolve, which
    /// <see cref="The_routing_rules_subject_set_is_the_one_it_was_written_against"/> asserts by
    /// identity rather than by count.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite.</b> The scan reports nothing today by design — no type outside
    /// <c>Rules.Luck</c> names any of the four — so the probe was to point <c>guarded</c> at
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
    /// requires the façade to <em>reach</em> every one of the four — which is what closes this arm's
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
    /// requires the façade to CALL a member of each of the four. Two shapes were run: rewriting
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
            "24 §11: HardPity, SoftPity, MercyAccrual and RarityTable are reachable only from " +
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
        // ⚠️ Every one of the four, not "at least one": RarityTable is a PARAMETER type of Resolve,
        // so it is named whether or not a single line of the body survives, and an at-least-one
        // floor would be satisfied by the locked signature alone. HardPity, SoftPity and
        // MercyAccrual are the three a body has to reach for.
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
    /// 🔒 `24` §11 / `23` §6 — <b>every exemption is still needed.</b> An exempted type that no
    /// longer trips the routing arm is a stale exemption, and a stale exemption is a rule quietly
    /// narrowed.
    /// </summary>
    /// <remarks>
    /// This is what makes <see cref="RoutingExemptions"/> a list with an expiry rather than a
    /// suppression file. The two draft entries are the ones that matter: when M4-01b routes the
    /// draft through the façade, this rule fails and the entries have to come out in that commit.
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
    /// True for a method that answers or accepts a grant outcome and never names the façade.
    /// </summary>
    /// <remarks>
    /// Takes the façade's name rather than reading <see cref="LuckFacade"/>, so the teeth check drives
    /// the same predicate the rule does instead of a second copy of it.
    /// </remarks>
    private static bool BypassesTheFacade(MethodDefinition method, string facade) =>
        GrantOutcomeNamesIn(method).Length > 0 && !RoutesThrough(method, facade);

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
