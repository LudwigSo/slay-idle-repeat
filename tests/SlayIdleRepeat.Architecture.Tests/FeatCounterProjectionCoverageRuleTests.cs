using Mono.Cecil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `28` D / `30` §12.7 — <b>every concrete <c>DomainEvent</c> in <c>Core</c> is either projected
/// into a lifetime feat counter or carries a reasoned, owned exemption row.</b> An event that hits no
/// arm of <c>FeatCounterProjection</c> and is on no list is history that no achievement can ever
/// claim.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this had to exist, stated as the defect it was written against rather than in the
/// abstract.</b> <c>FeatCounterProjection.Project</c> is a <c>switch</c> over a
/// <see cref="Domain.DomainEventType"/> with arms for two of `30` §7's events. The other two —
/// <c>GearGranted</c> and <c>PityCounterAdvanced</c> — are emitted by production today and fall
/// through to no arm at all. That is the <em>correct</em> default for an event whose measures are
/// undecided and the <em>wrong</em> one for an event that should have counted, and the projection's
/// own remarks say exactly that: <em>"a new event type is a decision taken here, not an omission"</em>.
/// Nothing made it a decision. `30` §12.7 forbids rebuilding a counter after the fact, so the
/// difference between the two readings is permanent: every event emitted before somebody noticed is
/// history the counter can never recover.
/// </para>
/// <para>
/// 🔒 <b>What this rule does NOT do, and the constraint is the point.</b> It adds <b>no counter
/// ids</b>. `16` O29 — the feat counter semantics — is registered as <b>M16</b>'s and the M4 kickoff
/// ruling R10 keeps it deliberately open: <em>"build the mechanism, freeze no vocabulary"</em>.
/// Inventing an id for <c>GearGranted</c> here would pre-empt that decision and would be steering
/// <b>S6</b>'s fabricated value wearing a schema. So the two events land on
/// <see cref="ProjectionExemptions"/> with an owner instead: the debt becomes <em>visible</em> now
/// and <em>expires by itself</em> (steering S4) the day M16 rules the vocabulary and the projection
/// grows an arm.
/// </para>
/// <para>
/// ⚠️ <b>WHAT THIS RULE CANNOT SEE, listed rather than implied.</b>
/// </para>
/// <list type="bullet">
///   <item><b>Whether an arm does the right thing.</b> <see cref="ProjectedEventNames"/> asks whether
///   the projection's IL <em>mentions</em> an event type at all. An arm that matched
///   <c>GearGranted</c> and advanced nothing would read as projected here. That is deliberately the
///   permissive direction: this rule closes "nobody decided", not "somebody decided badly", and the
///   unit suite over <c>Project</c> is where the second question lives.</item>
///   <item><b>An event outside <c>SlayIdleRepeat.Core</c>.</b> Both sets are stated over
///   <c>ProductionAssemblies.CoreModule</c>. `30` §11.4 puts the whole <c>DomainEvent</c> hierarchy
///   in <c>Core</c>, so this is today a scope rather than a hole — but it is a scope, and an event
///   declared in <c>Application</c> would be invisible to every arm below.</item>
///   <item><b>An event reached only through a <c>const</c>.</b> A folded constant leaves no metadata
///   reference (steering S18). Narrow here rather than theoretical-but-open: an event is matched by
///   a type pattern, which emits an <c>isinst</c>, and no event type in <c>Core</c> declares a
///   <c>const</c> at all — but a projection that keyed off a constant string rather than off the
///   type would be invisible to <see cref="ProjectedEventNames"/>.</item>
///   <item><b>A second projection.</b> The rule is stated over <em>one</em> named type. That is
///   sound only because <c>FeatCounterWritePathRuleTests</c> holds the other half — <c>GameRules</c>
///   is the only writer of a counter — and the two are load-bearing together.</item>
/// </list>
/// <para>
/// 🔒 A <b>new file</b> rather than an edit to <c>FeatCounterWritePathRuleTests</c>: that rule is
/// about who <em>writes</em> a counter and this one is about which events <em>reach</em> one, and
/// they fail for unrelated reasons. Same argument that file makes for not being a row in the
/// M0-authored rule files.
/// </para>
/// </remarks>
public sealed class FeatCounterProjectionCoverageRuleTests
{
    /// <summary>The one type that binds an event to the counters it advances.</summary>
    private const string ProjectionType = "FeatCounterProjection";

    /// <summary>The member the projection makes its decision in — the identity floor's subject.</summary>
    private const string ProjectMember = "Project";

    /// <summary>
    /// The floor under the event set (steering S3). `30` §7 specifies six events and four are
    /// authored at the end of M4; set below that so authoring the fifth is not a test edit, and above
    /// zero so a renamed namespace cannot turn every arm here permanently green.
    /// </summary>
    private const int ConcreteEventFloor = 3;

    /// <summary>
    /// The identity floor beneath the count (steering S3): the two events the projection actually
    /// has arms for. A count alone is satisfied by four events none of which is projected.
    /// </summary>
    private static readonly string[] ProjectedByIdentity = { "DiceRolled", "CurrencyChanged" };

    /// <summary>
    /// 🔒 The closed, reasoned exemption list: a concrete <c>DomainEvent</c> that reaches no arm of
    /// the projection <b>on purpose</b>, with the task that owns deciding otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row has to justify itself in both directions.
    /// <see cref="Every_projection_exemption_still_names_an_unprojected_event"/> deletes a row whose
    /// event has since been projected and a row whose event no longer exists, so a satisfied
    /// exemption is a build failure rather than a line nobody notices — steering <b>S4</b>'s
    /// "an exception must expire by itself", including when it has been <em>satisfied</em>.
    /// </para>
    /// <para>
    /// ⚠️ The owner is a task id, not a milestone number, so it is the same currency
    /// <c>GapRegister</c> and <c>LuckRoutingRuleTests.GrantSourceClasses</c> carry. It is restated
    /// here rather than looked up: an event is not a command and has no dispatch row to read it off.
    /// </para>
    /// </remarks>
    private static readonly (string Event, string Owner, string Reason)[] ProjectionExemptions =
    {
        // 🔒 Both rows are the SAME deferral, and it is a vocabulary deferral rather than a
        // technical one: what each of these events would count is derivable from the payload TODAY,
        // and the only thing missing is a decision about what a feat measures. 16 O29 owns that
        // decision, M16-03 is the task, and the M4 kickoff's ruling R10 keeps it open on purpose —
        // "build the mechanism, freeze no vocabulary". Adding an id here would be the pre-emption
        // that ruling forbids and the fabricated value steering S6 forbids, in one move.
        //
        // ⚠️ What that costs, said plainly rather than softened: every GearGranted and every
        // PityCounterAdvanced emitted between M4 and M16 is history no feat can claim, because 30
        // §12.7 forbids rebuilding a counter. These rows exist so that the cost is a recorded,
        // owned decision instead of an omission nobody knows about.
        ("GearGranted",
         "M16-03",
         "30 §7's grant event. Items granted, grants by band, grants by source class and grants that " +
         "came out of a pity guarantee are all readable straight off this payload — the counters are " +
         "derivable today and only the ID VOCABULARY is undecided. 16 O29 fixes what a feat measures " +
         "and is registered to the M16 kickoff; M4's R10 ruled it must not be pre-empted"),
        ("PityCounterAdvanced",
         "M16-03",
         "30 §7's pity movement. The counter key and its value after the move are both on the " +
         "payload, so 'guarantees fired' and 'draws survived at a counter' are derivable today — and " +
         "the key is a CONTENT-DERIVED id formed by LuckTuning, so a counter id built from it would " +
         "hard-wire the authored key set into a lifetime counter nothing may rename. 16 O29 owns it, " +
         "same M16 kickoff as GearGranted"),
        ("MailClaimed",
         "M16-03",
         "28 A's claim event. 'Messages claimed' and 'compensations collected' are both derivable " +
         "straight off this payload — the message id and its category are on it — so the counters " +
         "are available today and only the ID VOCABULARY is undecided, exactly the two rows above. " +
         "⚠️ And there is a reason NOT to guess one here beyond O29: a feat that counted inbox " +
         "claims would be a feat ops could hand a player by sending them mail, which is a decision " +
         "about what an achievement means rather than about what an event carries. 16 O29 owns it, " +
         "same M16 kickoff as GearGranted"),
    };

    /// <summary>
    /// 🔒 `28` D / `30` §12.7 — <b>no concrete <c>DomainEvent</c> in <c>Core</c> falls through the
    /// projection unnoticed.</b> Every one is either mentioned by <c>FeatCounterProjection</c> or
    /// named on <see cref="ProjectionExemptions"/> with the task that owns deciding otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Proved to bite, in two shapes with a control.</b>
    /// </para>
    /// <list type="number">
    ///   <item><b>The exemption side.</b> Renaming the <c>PityCounterAdvanced</c> row in
    ///   <see cref="ProjectionExemptions"/> to <c>PityCounterAdvancedPROBE</c> turned this arm red —
    ///   <em>"SlayIdleRepeat.Core.Events.PityCounterAdvanced reaches no arm of FeatCounterProjection
    ///   and is on no exemption row"</em> — and turned
    ///   <see cref="Every_projection_exemption_still_names_an_unprojected_event"/> red in the same
    ///   run for the now-unanchored row. So the exemption list is what holds this arm green, rather
    ///   than the matcher failing to see the event at all.</item>
    ///   <item><b>The production side, which is the half that matters.</b> Commenting the
    ///   <c>CurrencyChanged</c> arm out of <c>Project</c>'s own <c>switch</c> — real production IL,
    ///   not a fixture — turned this arm red with the same message for <c>CurrencyChanged</c>, and
    ///   <see cref="The_projection_coverage_subject_set_is_the_one_it_was_written_against"/> red with
    ///   <em>"FeatCounterProjection's IL does not mention 'CurrencyChanged', which it has a switch
    ///   arm for"</em>. That is what proves <see cref="ProjectedEventNames"/> reads the projection's
    ///   actual dispatch rather than a transcription of it.</item>
    ///   <item><b>THE NEGATIVE CONTROL.</b> With that arm restored and a <c>CurrencyChanged</c> row
    ///   added to <see cref="ProjectionExemptions"/>, this arm stayed <b>green</b> — a projected
    ///   event is not reported whether or not it is also exempted — while the exemption arm went red
    ///   for the now-satisfied row. Without it, an arm that reported everything would have passed
    ///   both probes above.</item>
    /// </list>
    /// <para>
    /// All three reverted by hand.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_domain_event_is_projected_or_carries_an_owned_exemption()
    {
        var projected = ProjectedEventNames();
        var exempted = ProjectionExemptions.Select(e => e.Event).ToArray();

        var offenders = ConcreteDomainEvents()
            .Where(e => !projected.Contains(e.Name, StringComparer.Ordinal))
            .Where(e => !exempted.Contains(e.Name, StringComparer.Ordinal))
            .Select(e =>
                $"{e.FullName} reaches no arm of {ProjectionType} and is on no exemption row. 28 D " +
                "evaluates a Feat from the event stream, so an event that advances no counter is a " +
                "measure nobody can ever query — and 30 §12.7 forbids rebuilding a counter after " +
                "the fact, which makes every event emitted before somebody notices history no " +
                "achievement can claim. Either add the arm, or — if what this event should measure " +
                "is genuinely undecided — add a row to ProjectionExemptions naming the task that " +
                "owns the decision, so the debt is visible and expires on the commit that pays it.")
            .ToList();

        ArchRule.Empty(
            offenders,
            "28 D / 30 §12.7: every concrete DomainEvent in Core is projected into a lifetime feat " +
            "counter or carries a reasoned exemption with a named owner.");
    }

    /// <summary>
    /// 🔒 `23` §6 / steering S4 — <b>an exemption expires by itself.</b> A row whose event the
    /// projection now handles, or whose event no longer exists, or which names no owner, fails the
    /// build rather than sitting there.
    /// </summary>
    /// <remarks>
    /// The direction that is usually skipped, and the one that makes the list above more than a
    /// comment: without it, the day M16 gives <c>GearGranted</c> an arm, the row would stay — and the
    /// next reader would find a register saying the event is deferred while the code projects it.
    /// 🔒 <b>Proved to bite, in both of its branches.</b> The <em>satisfied</em> branch: adding a
    /// <c>CurrencyChanged</c> row — an event the projection does handle — reported <em>"the exemption
    /// row for 'CurrencyChanged' says its counters are undecided, and FeatCounterProjection now
    /// projects it"</em>, while arm 1 correctly stayed green. The <em>unanchored</em> branch:
    /// renaming the <c>PityCounterAdvanced</c> row to <c>PityCounterAdvancedPROBE</c> reported
    /// <em>"the exemption row names 'PityCounterAdvancedPROBE', which is not a concrete DomainEvent
    /// in Core"</em>. Both reverted by hand.
    /// </remarks>
    [Fact]
    public void Every_projection_exemption_still_names_an_unprojected_event()
    {
        var projected = ProjectedEventNames();
        var events = ConcreteDomainEvents().Select(e => e.Name).ToArray();
        var offenders = new List<string>();

        foreach (var (name, owner, reason) in ProjectionExemptions)
        {
            if (!events.Contains(name, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"the exemption row names '{name}', which is not a concrete DomainEvent in Core. " +
                    "It defers something no event hierarchy asks for, so it can never be satisfied — " +
                    "only deleted by hand, which is the state this list replaces. If the event was " +
                    "renamed, rename it here in the same commit; if it was deleted, delete the row.");
            }

            if (projected.Contains(name, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"the exemption row for '{name}' says its counters are undecided, and " +
                    $"{ProjectionType} now projects it. A satisfied exemption is a register that " +
                    "disagrees with the code it describes. Delete the row in the commit that added " +
                    "the arm.");
            }

            if (string.IsNullOrWhiteSpace(owner))
            {
                offenders.Add(
                    $"the exemption row for '{name}' names no owner. An exemption nobody owns is a " +
                    "permanent one, and 16 O29's whole point is that the decision has an address.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                offenders.Add(
                    $"the exemption row for '{name}' carries no reason. The row exists to record " +
                    "WHY the event counts nothing; without it the next reader cannot tell a decision " +
                    "from an oversight, which is the distinction this whole file is about.");
            }
        }

        ArchRule.Empty(
            offenders,
            "S4: every FeatCounterProjection exemption still names an unprojected event, with an " +
            "owner and a reason.");
    }

    /// <summary>
    /// 🔒 `23` §6 — <b>the identity floor</b> under both arms above (steering S3), and the proof that
    /// <see cref="ProjectedEventNames"/> discriminates rather than answering the same thing for
    /// everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both arms above are of the form "no member of set S does X", and both sets can silently empty:
    /// the event set through a namespace rename, the projected set through the projection being
    /// renamed or folded into a handler. Either would report success forever — and the projected set
    /// emptying is the worse one, because it makes arm 1 report <em>every</em> event, which reads as
    /// a real finding rather than as a broken matcher.
    /// </para>
    /// <para>
    /// 🔒 <b>Two accepting shapes and a refusing control</b>, because one passing probe proves a
    /// predicate is not vacuous and says nothing about whether it is correctly scoped (steering S1's
    /// M2 amendment). <c>DiceRolled</c> is matched by a bare type pattern and <c>CurrencyChanged</c>
    /// by a type pattern with a <c>when</c> guard — two different <c>switch</c> shapes — and the
    /// control is a name nothing declares.
    /// </para>
    /// <para>
    /// 🔒 <b>Proved to bite, in two shapes.</b> (1) Pointing <see cref="ProjectionType"/> at
    /// <c>FeatCounterIncrement</c> — a sibling type that mentions no event at all — emptied the
    /// projected set and turned this arm red three times over: <em>"'FeatCounterIncrement' declares
    /// no member named 'Project'"</em> plus one <em>"…IL does not mention…"</em> for each identity.
    /// (2) Raising <see cref="ConcreteEventFloor"/> to 9 reported <em>"only 4 concrete DomainEvent(s)
    /// resolve under SlayIdleRepeat.Core.Events; the floor is 9"</em>, which is what proves the count
    /// arm is reading the hierarchy rather than a constant. Both reverted by hand.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_projection_coverage_subject_set_is_the_one_it_was_written_against()
    {
        var offenders = new List<string>();
        var projection = Domain.FindInCore(ProjectionType);

        if (projection is null)
        {
            offenders.Add(
                $"'{ProjectionType}' is not declared in Core. Both rules in this file are stated " +
                "over its IL, so without it the projected set is empty and arm 1 reports every " +
                "event in the game as unprojected. If the projection was renamed, rename " +
                "ProjectionType in the same commit.");
        }
        else if (!Il.AllMethods(projection).Any(m => m.Name.Equals(ProjectMember, StringComparison.Ordinal)))
        {
            offenders.Add(
                $"'{ProjectionType}' declares no member named '{ProjectMember}'. Arm 1's failure " +
                "message tells an author to add an arm to it, and a rule whose remedy does not exist " +
                "is a rule nobody can obey.");
        }

        var events = ConcreteDomainEvents().ToArray();

        if (events.Length < ConcreteEventFloor)
        {
            offenders.Add(
                $"only {events.Length} concrete DomainEvent(s) resolve under " +
                $"{Domain.EventsNamespace}; the floor is {ConcreteEventFloor}. Arm 1 quantifies over " +
                "this set, so a set this small means the hierarchy has moved, been renamed or been " +
                "split — and the rule is then governing whatever is left while reporting success. If " +
                "an event legitimately went, lower this floor in the same commit and say where it " +
                "went, rather than deleting the check.");
        }

        var projected = ProjectedEventNames();

        foreach (var identity in ProjectedByIdentity)
        {
            if (!events.Any(e => e.Name.Equals(identity, StringComparison.Ordinal)))
            {
                offenders.Add(
                    $"'{identity}' is not a concrete DomainEvent in Core. It is one of the two events " +
                    "the projection has an arm for, so its absence means arm 1's subject set no " +
                    "longer contains anything the projection handles — and 'every event is projected " +
                    "or exempted' would then be a claim about the exemption list alone.");
            }

            if (!projected.Contains(identity, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"{ProjectionType}'s IL does not mention '{identity}', which it has a switch arm " +
                    "for. The matcher has stopped seeing the dispatch — a moved namespace, an arm " +
                    "rewritten to key off something other than the type — so 'projected' is now a set " +
                    "the code disagrees with, and arm 1 would ask for an exemption row on an event " +
                    "that is already counted.");
            }
        }

        // 🔒 THE NEGATIVE CONTROL. Both checks above are "is this name in the set", which a matcher
        // that answered true for everything would satisfy. A name nothing in Core declares must not
        // be in it.
        projected.ShouldNotContain(
            "AnEventNobodyDeclares",
            "the projected-event matcher answers true for a name no type in Core carries, so it is " +
            "not comparing names at all and every check in this file is decoration.");

        // 🔒 The abstract base is excluded on purpose. FeatCounterProjection names DomainEvent in
        // Project's own signature, so a matcher that did not filter it would report the base as
        // "projected" — harmless on its own, and the tell that the filter had gone.
        projected.ShouldNotContain(
            Domain.DomainEventType,
            $"'{Domain.DomainEventType}' is abstract and is the projection's PARAMETER type, so it " +
            "is named whether or not a single arm survives. Its presence in the projected set means " +
            "ConcreteDomainEvents' abstract filter has stopped running, and an abstract base counted " +
            "as a projected event is a rule that would pass over a hierarchy with no arms at all.");

        ArchRule.Empty(
            offenders,
            "28 D / 23 §6: the projection-coverage rule is stated over the events and the projection " +
            "it was written against.");
    }

    /// <summary>
    /// Every concrete <c>DomainEvent</c> declared in <c>Core</c> — the abstract base and any
    /// compiler-generated type excluded.
    /// </summary>
    private static IEnumerable<TypeDefinition> ConcreteDomainEvents() =>
        Il.AllTypes(ProductionAssemblies.CoreModule)
            .Where(t => !Domain.IsCompilerGenerated(t))
            .Where(Domain.IsDomainEvent)
            .Where(t => !t.IsAbstract)
            .Where(t => !t.Name.Equals(Domain.DomainEventType, StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>
    /// The simple names of every <c>DomainEvent</c> subtype <see cref="ProjectionType"/> mentions
    /// anywhere in its metadata or IL.
    /// </summary>
    /// <remarks>
    /// An IL read rather than a hand-written list, for <c>Il</c>'s stated reason and for one more:
    /// a transcription of "the events the projection handles" is a second copy of a closed list, and
    /// a closed list with two copies is not closed. Read this way, adding an arm updates the rule.
    /// ⚠️ It answers <em>mentions</em>, not <em>counts correctly</em> — see this file's remarks.
    /// </remarks>
    private static IReadOnlyCollection<string> ProjectedEventNames()
    {
        var projection = Domain.FindInCore(ProjectionType);

        if (projection is null)
        {
            return Array.Empty<string>();
        }

        var events = ConcreteDomainEvents().Select(e => e.FullName).ToArray();

        return Il.ReferencedTypeNames(projection)
            .Where(name => events.Contains(name, StringComparer.Ordinal))
            .Select(name => name[(name.LastIndexOf('.') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
