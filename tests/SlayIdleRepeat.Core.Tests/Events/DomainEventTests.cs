using Shouldly;
using SlayIdleRepeat.Core.Events;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// The public <c>DomainEvent</c> hierarchy. <c>Apply</c> returns events, and four features read the
/// returned list: analytics, the append-only economy log, Feats and the client's replay. Every rule
/// below is stated over a subject set of very few concrete events, so each is paired with a self-test
/// driving the same predicate against a deliberately wrong shape.
/// </summary>
public sealed class DomainEventTests
{
    /// <summary>
    /// The base event is public and abstract, and carries <c>int Sequence</c>. Abstract because the
    /// list is a vocabulary of named happenings — a bare <c>DomainEvent</c> would be a log row with
    /// no meaning and an animation frame with no instruction.
    /// </summary>
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

        // Not `SetMethod is null`: a positional record's setter is a public init accessor, so what
        // must not exist is a real `set`.
        DomainEventShape.SettablePropertyViolations(typeof(DomainEvent)).ShouldBeEmpty(
            "Sequence is set at construction, by GameRules.Apply, and never after.");
    }

    /// <summary>
    /// Every event is a public sealed record living under <c>SlayIdleRepeat.Core.Events</c>. Sealed
    /// matters to the consumers: analytics maps a closed set of kinds, and a subclass of
    /// <c>CurrencyChanged</c> would be a currency movement the mapping doesn't know it's looking at.
    /// </summary>
    [Fact]
    public void Every_domain_event_is_a_public_sealed_record_under_Core_Events()
    {
        var offenders = DomainEventShape.ConcreteEvents
            .Where(t => !(t.IsPublic && t.IsSealed))
            .Select(t => $"{t.FullName} is not a public sealed type")
            .ToArray();

        offenders.ShouldBeEmpty();

        // Checked on the base, which is the only place it can fail: C# forbids a class from deriving
        // from a record, so every subtype is a record for exactly as long as DomainEvent is one.
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

    [Fact]
    public void Every_domain_event_takes_Sequence_as_its_first_constructor_parameter()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.SequenceParameterViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above: it accepts the real event and rejects each of the three wrong
    /// shapes, one per branch the predicate can take.
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
    /// No event stamps itself with a clock reading. A separate rule bans the calls that produce a
    /// clock reading inside Core, but cannot see a <c>DateTime</c> the event merely holds, filled by
    /// a caller outside Core — which would then be persisted as though the domain had produced it.
    /// </summary>
    [Fact]
    public void No_domain_event_carries_a_clock_reading()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.ClockReadingViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above. A bare <c>DateTime</c> is the easy half; the half that matters
    /// is a clock reading wrapped in a nullable, a collection or an array, since that is how a real
    /// payload would carry one.
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
    /// No event exposes a settable property. The same list is an append-only log, an analytics
    /// payload, a Feats counter input and the client's animation script; a consumer that can rewrite
    /// it changes what the other three see.
    /// </summary>
    [Fact]
    public void No_domain_event_exposes_a_settable_property()
    {
        DomainEventShape.ConcreteEvents
            .SelectMany(DomainEventShape.SettablePropertyViolations)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The teeth of the rule above: it must reject a <c>set</c> at any accessibility, and it must
    /// accept an <c>init</c>, or it would forbid the positional-record shape every event is written
    /// in and be unsatisfiable rather than strict.
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
    /// <c>Core/Events/</c> holds the event hierarchy and nothing else. A payload type, helper or enum
    /// declared here would be the event carrying state rather than describing a change — a payload
    /// has to be declared in the layer that owns it instead.
    /// </summary>
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
    /// The floor under every rule in this file. The subject set is read by namespace and by base
    /// type; a rename, an <c>internal</c>, or a nesting would empty it and take every rule above
    /// permanently green over nothing.
    /// </summary>
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
