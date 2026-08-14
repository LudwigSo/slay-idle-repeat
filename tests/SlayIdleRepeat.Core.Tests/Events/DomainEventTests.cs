using Shouldly;
using SlayIdleRepeat.Core.Events;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// `30` §7 — the public <c>DomainEvent</c> hierarchy. <c>Apply</c> returns events, and four features
/// read the returned list: analytics, the append-only economy log, Feats and the client's replay.
/// </summary>
/// <remarks>
/// ⚠️ <b>One of §7's six events exists.</b> The other five name payload types no milestone has authored,
/// and inventing any would put a guessed type at the bottom of the dependency graph for three later
/// milestones to build on. They are declared in the architecture suite's <c>GapRegister</c>, each keyed
/// on the type whose arrival makes the deferral stale.
/// <para>
/// So every rule below is stated over a subject set of one concrete event and none may be trusted on
/// that basis alone: each is paired with a self-test driving the same predicate against a deliberately
/// wrong shape, and the subject set itself has a floor.
/// </para>
/// </remarks>
public sealed class DomainEventTests
{
    /// <summary>
    /// 🔒 `30` §7 / `30` §11.2 — the base event is public and abstract, and carries
    /// <c>int Sequence</c>.
    /// </summary>
    /// <remarks>
    /// Public because all four consumers live outside <c>Core</c>. Abstract because the list is a
    /// vocabulary of named happenings: a bare <c>DomainEvent</c> in the returned list would be an
    /// analytics row with no event name, a log row with no meaning and an animation frame with no
    /// instruction.
    /// </remarks>
    [Fact]
    public void The_base_event_is_public_abstract_and_carries_Sequence()
    {
        typeof(DomainEvent).IsPublic.ShouldBeTrue(
            "30 §11.2 makes the event hierarchy public — analytics, the economy log, Feats and client " +
            "replay all read it from outside Core.");

        typeof(DomainEvent).IsAbstract.ShouldBeTrue(
            "an instantiable base would let Apply return an event with no kind.");

        var sequence = typeof(DomainEvent).GetProperty(nameof(DomainEvent.Sequence));

        sequence.ShouldNotBeNull(DomainEventShape.Consequence);
        sequence.PropertyType.ShouldBe(typeof(int));

        // Not `SetMethod is null`: a positional record's setter is a PUBLIC init accessor, so the
        // obvious check reads as a violation. What must not exist is a real `set` — see
        // The_immutability_rule_rejects_a_setter_and_accepts_an_init_accessor for that distinction.
        DomainEventShape.SettablePropertyViolations(typeof(DomainEvent)).ShouldBeEmpty(
            "Sequence is set at construction, by GameRules.Apply, and never after.");
    }

    /// <summary>
    /// `30` §7 / `30` §11.4 — every event is a public sealed record living under
    /// <c>SlayIdleRepeat.Core.Events</c>.
    /// </summary>
    /// <remarks>
    /// Sealed matters to the consumers, not to <c>Core</c>: analytics maps a closed set of kinds
    /// to `14` §10.1's named events, and a subclass of <c>CurrencyChanged</c> would be a currency
    /// movement that the mapping does not know it is looking at.
    /// </remarks>
    [Fact]
    public void Every_domain_event_is_a_public_sealed_record_under_Core_Events()
    {
        var offenders = DomainEventShape.ConcreteEvents
            .Where(t => !(t.IsPublic && t.IsSealed))
            .Select(t => $"{t.FullName} is not a public sealed type")
            .ToArray();

        offenders.ShouldBeEmpty();

        // The "record" half of the name, checked on the BASE, which is the only place it can fail:
        // C# forbids a class from deriving from a record, so every subtype is a record for exactly
        // as long as DomainEvent is one. Quantifying over ConcreteEvents instead would be a rule
        // the compiler already guarantees — true of every possible value, and therefore no rule.
        DomainEventShape.IsRecord(typeof(DomainEvent)).ShouldBeTrue(
            "a record, not a class. Value equality is what lets the economy log (14 §7.1) compare two rows, " +
            "and the positional form is what SequenceParameterViolations reads 'the first constructor " +
            "parameter' off. Rewrite DomainEvent as a class and both go, silently.");

        // ...with a negative control, or the marker lookup could be answering true for everything.
        DomainEventShape.IsRecord(typeof(DomainEventTests)).ShouldBeFalse(
            "the record check must tell a record from a class, or the assertion above proves nothing.");

        DomainEventShape.ConcreteEvents.ShouldAllBe(
            t => t.Namespace == "SlayIdleRepeat.Core.Events",
            "30 §11.4's namespace list is closed — Every_Core_type_lives_under_a_documented_namespace " +
            "fails the build on a sub-namespace.");
    }

    /// <summary>
    /// 🔒 `30` §7 — every event takes <c>int Sequence</c> as its <b>first</b> constructor
    /// parameter. See <see cref="DomainEventShape.Consequence"/> for what the number means and who
    /// assigns it.
    /// </summary>
    [Fact]
    public void Every_domain_event_takes_Sequence_as_its_first_constructor_parameter()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.SequenceParameterViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above: it accepts the real event and rejects each of the three wrong
    /// shapes — one per branch the predicate can take (`30` §7). A branch no fixture drives is a
    /// branch that could return "no violation" for every input and never be noticed.
    /// </summary>
    [Fact]
    public void The_first_parameter_rule_recognises_a_compliant_event_and_three_that_are_not()
    {
        DomainEventShape.SequenceParameterViolations(typeof(CurrencyChanged)).ShouldBeEmpty();

        DomainEventShape.SequenceParameterViolations(typeof(NonConformingEvents.SequenceIsNotFirst))
            .ShouldHaveSingleItem()
            .ShouldContain("as its first constructor parameter, not 'Int32 Sequence'", Case.Sensitive);

        DomainEventShape.SequenceParameterViolations(typeof(NonConformingEvents.TwoConstructors))
            .ShouldHaveSingleItem()
            .ShouldContain("does not declare exactly one constructor", Case.Sensitive);

        DomainEventShape.SequenceParameterViolations(typeof(NonConformingEvents.NoConstructorParameters))
            .ShouldHaveSingleItem()
            .ShouldContain("takes no constructor parameters", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `30` §3 / `30` §9 — no event stamps itself with a clock reading. Time enters the domain
    /// as <c>GameContext.NowUtc</c>, and <c>IClockPort</c> must not appear in <c>Core</c> at all.
    /// </summary>
    /// <remarks>
    /// <c>Domain_has_no_ambient_time_or_randomness</c> bans the <i>calls</i>. It cannot see a
    /// <c>DateTime</c> the event merely <i>holds</i>, filled by a caller outside <c>Core</c> — and
    /// that value would then be persisted into the `14` §7.1 economy log as though the domain had
    /// produced it, and would move every <c>stateHash</c> that ever carried it.
    /// </remarks>
    [Fact]
    public void No_domain_event_carries_a_clock_reading()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.ClockReadingViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above (`30` §3). A bare <c>DateTime</c> is the easy half; the half
    /// that matters is a clock reading wrapped in a nullable, a collection or an array, because
    /// that is how a real payload would carry one and because unwrapping it is the entire reason
    /// <c>DomainEventShape.Flatten</c> exists.
    /// </summary>
    [Fact]
    public void The_clock_rule_recognises_a_compliant_event_and_clock_readings_however_they_are_wrapped()
    {
        DomainEventShape.ClockReadingViolations(typeof(CurrencyChanged)).ShouldBeEmpty();

        DomainEventShape.ClockReadingViolations(typeof(NonConformingEvents.SelfStamped))
            .ShouldHaveSingleItem()
            .ShouldContain("OccurredAt is typed DateTime", Case.Sensitive);

        var wrapped = DomainEventShape.ClockReadingViolations(typeof(NonConformingEvents.SelfStampedIndirectly));

        // One per clock-carrying property, not one per path Flatten reaches it by: a nullable is
        // reached twice, and a duplicated violation would make this count meaningless.
        wrapped.Count.ShouldBe(
            4,
            "SelfStampedIndirectly carries exactly four clock readings — through a nullable, a generic " +
            "argument, an array element and directly.");

        var reported = string.Join(Environment.NewLine, wrapped);

        reported.ShouldContain("Window is typed DateTimeOffset", Case.Sensitive);
        reported.ShouldContain("Durations is typed TimeSpan", Case.Sensitive);
        reported.ShouldContain("Days is typed DateOnly", Case.Sensitive);
        reported.ShouldContain("Cutoff is typed TimeOnly", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `14` §7.1 / `14` §2.4 — no event exposes a settable property. The same list is an
    /// append-only Postgres log, an analytics payload, a Feats counter input and the client's
    /// animation script; a consumer that can rewrite it changes what the other three see.
    /// </summary>
    [Fact]
    public void No_domain_event_exposes_a_settable_property()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.SettablePropertyViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above (`14` §7.1). Note the halves: it must reject a <c>set</c> at
    /// <b>any</b> accessibility, and it must <b>accept</b> an <c>init</c>, or it would forbid the
    /// positional-record shape `30` §7 writes every event in and be unsatisfiable rather than
    /// strict.
    /// </summary>
    [Fact]
    public void The_immutability_rule_rejects_a_setter_at_any_accessibility_and_accepts_an_init_accessor()
    {
        DomainEventShape.SettablePropertyViolations(typeof(NonConformingEvents.Rewritable))
            .ShouldHaveSingleItem()
            .ShouldContain("Note has a setter (public)", Case.Sensitive);

        // The shape an author would actually reach for. The mutation this rule prevents would be
        // written inside Core, where 'internal' is no protection at all.
        DomainEventShape.SettablePropertyViolations(typeof(NonConformingEvents.InternallyRewritable))
            .ShouldHaveSingleItem()
            .ShouldContain("Note has a setter (internal)", Case.Sensitive);

        DomainEventShape.SettablePropertyViolations(typeof(NonConformingEvents.TwoConstructors)).ShouldBeEmpty();
        DomainEventShape.SettablePropertyViolations(typeof(CurrencyChanged)).ShouldBeEmpty();
    }

    /// <summary>
    /// `30` §11.4 — <c>Core/Events/</c> holds the event hierarchy and nothing else. A payload type,
    /// helper or enum declared here would be the event carrying state rather than describing a change.
    /// </summary>
    /// <remarks>
    /// <c>Events</c> has no row in the architecture suite's forbidden-pair table, so almost nothing there
    /// governs what an event may reference. This is the substitute: keep the namespace to
    /// <c>DomainEvent</c> and its subtypes, so a payload has to be declared in the layer that owns it,
    /// where the layering rows do apply.
    /// <para>
    /// ⚠️ A substitute, not the ruling: §11.4's chain omits <c>Commands</c> and <c>Events</c> altogether,
    /// while §7 writes <c>GearGranted(…, GearInstance, …)</c> — and <c>GearInstance</c> is a <c>Model</c>
    /// aggregate, so a row forbidding <c>Events → Model</c> would contradict §7 and block M4-03. The
    /// binding ruling is due at the <b>M4 kickoff</b>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Core_Events_holds_the_event_hierarchy_and_nothing_else()
    {
        var offenders = DomainEventShape.PublicTypesUnderEvents
            .Where(t => t != typeof(DomainEvent) && !DomainEventShape.ConcreteEvents.Contains(t))
            .Select(t =>
                $"{t.FullName} lives under Core/Events/ but is not a DomainEvent. Declare it in the layer " +
                "that owns it — Primitives, Content or Model — where Core_internal_layering_holds has a " +
                "forbidden-pair row for it. Events has none.");

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `30` §7 — the floor under every rule in this file (steering S3). The subject set is read
    /// by namespace and by base type; a rename, an <c>internal</c>, or a nesting would empty it and
    /// take all six rules above permanently green over nothing.
    /// </summary>
    /// <remarks>
    /// Stated as a floor and as a named member rather than an equality, so authoring
    /// <c>DiceRolled</c> in M3-04 is not a test edit — but losing <c>CurrencyChanged</c> is.
    /// </remarks>
    [Fact]
    public void The_hierarchy_this_suite_governs_is_the_one_that_exists()
    {
        DomainEventShape.ConcreteEvents.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "the subject set of every rule in this file. Empty, they all pass over nothing.");

        DomainEventShape.ConcreteEvents
            .Select(t => t.Name)
            .ShouldContain(nameof(CurrencyChanged));

        DomainEventShape.PublicTypesUnderEvents
            .Select(t => t.Name)
            .ShouldContain(nameof(DomainEvent));
    }
}
