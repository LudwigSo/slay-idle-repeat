using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// The shared contract suite for <see cref="IEffectSource"/> — the source abstraction feeding effect
/// collection. Every implementation is run through it.
/// </summary>
/// <remarks>
/// To implement it: derive a test class from this one and override <see cref="Create"/> to build your
/// implementation carrying the given effects in the given order. Nothing may be overridden — a rule an
/// implementation can opt out of is not a contract.
/// <para>
/// What this suite cannot check: the load-bearing half of the obligation is that
/// <see cref="IEffectSource.Effects"/> is ordered by a function of the build and is therefore identical
/// on a phone and in a container, because that order is <see cref="EffectResolutionOrder"/>'s tiebreak.
/// A suite in one process can prove the order is stable, not that it is device-independent — an
/// implementation enumerating a <see cref="HashSet{T}"/> would pass everything here and still put the
/// divergence back.
/// </para>
/// </remarks>
public abstract class EffectSourceContract
{
    /// <summary>
    /// Builds the implementation under test, carrying the given effects in the given order.
    /// <c>private protected</c>, not <c>protected</c>: <see cref="IEffectSource"/> is <c>internal</c>
    /// to <c>SlayIdleRepeat.Core</c> and reaches this assembly only via an <c>InternalsVisibleTo</c>
    /// grant, so a <c>protected</c> member of a <c>public</c> class could not name it.
    /// </summary>
    private protected abstract IEffectSource Create(
        EffectSourceKind kind, IReadOnlyList<SourcedEffect> effects);

    /// <summary>An effect with only the two keys M2-01 makes required.</summary>
    private protected static EffectDefinition Effect(string id, double? value = null) =>
        new() { Id = id, Op = EffectOp.STAT_ADD_PCT, Stat = StatSelector.Of(StatId.ATK), Value = value ?? 0.1 };

    /// <summary>
    /// One holding: an effect plus the <see cref="EffectInstanceId"/> its source reports it under.
    /// </summary>
    /// <remarks>
    /// The default id is derived from the effect id and a discriminator rather than from the effect
    /// id alone, because <c>Two_effects_with_one_id_are_both_reported</c> needs two holdings of ONE
    /// authored effect and <c>EffectInstanceId</c> must be unique per holding.
    /// </remarks>
    private protected static SourcedEffect Held(EffectDefinition effect, string? holding = null) =>
        new(effect, EffectInstanceId.Of(holding ?? $"slot:{effect.Id}"));

    private protected static IReadOnlyList<SourcedEffect> Holdings(params EffectDefinition[] effects) =>
        effects.Select((e, i) => Held(e, $"slot{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{e.Id}"))
               .ToArray();

    /// <summary>The source reports the kind it was built for.</summary>
    [Fact]
    public void The_source_reports_its_18_8_step_1_kind()
    {
        foreach (var row in EffectSourceCatalogue.Rows)
        {
            Create(row.Kind, Array.Empty<SourcedEffect>()).Kind.ShouldBe(row.Kind);
        }
    }

    /// <summary>
    /// The effects come back in the order they went in. Collection does not sort, and
    /// <see cref="EffectResolutionOrder"/>'s tiebreak is this index.
    /// </summary>
    [Fact]
    public void The_effects_are_reported_in_the_order_given()
    {
        // Deliberately NOT in id order: a source that quietly sorted would pass an in-order fixture.
        var source = Create(
            EffectSourceKind.GEAR,
            Holdings(Effect("AFF_Z"), Effect("AFF_A"), Effect("AFF_M")));

        source.Effects.Select(e => e.Effect.Id).ShouldBe(new[] { "AFF_Z", "AFF_A", "AFF_M" });
    }

    /// <summary>
    /// Reading twice gives the same order. A source is a reading of the build, not a live query, and
    /// both passes of one resolution must see one answer.
    /// </summary>
    [Fact]
    public void Reading_the_effects_twice_gives_the_same_order()
    {
        var source = Create(
            EffectSourceKind.PERKS,
            Holdings(Effect("PK_C"), Effect("PK_A"), Effect("PK_B"), Effect("PK_A")));

        var first = source.Effects.Select(e => e.Effect.Id).ToArray();
        var second = source.Effects.Select(e => e.Effect.Id).ToArray();

        second.ShouldBe(first);
        first.ShouldBe(new[] { "PK_C", "PK_A", "PK_B", "PK_A" });
    }

    /// <summary>A source with nothing to contribute reports an empty list, never <c>null</c>.</summary>
    [Fact]
    public void A_source_with_nothing_to_contribute_reports_an_empty_list()
    {
        var source = Create(EffectSourceKind.MOUNT, Array.Empty<SourcedEffect>());

        source.Effects.ShouldNotBeNull();
        source.Effects.ShouldBeEmpty();
    }

    /// <summary>
    /// The same authored id twice is kept twice — the same affix on two gear slots is two bonuses, and
    /// de-duplicating would silently halve a legitimate build.
    /// </summary>
    [Fact]
    public void Two_effects_with_one_id_are_both_reported()
    {
        var source = Create(EffectSourceKind.GEAR, Holdings(Effect("AFF_KEEN", 0.05), Effect("AFF_KEEN", 0.07)));

        source.Effects.Count.ShouldBe(2);
        source.Effects.Select(e => e.Effect.Value).ShouldBe(new double?[] { 0.05, 0.07 });
    }

    /// <summary>
    /// Every reported effect names a holding. <c>EffectInstanceId</c> is the effects layer's one
    /// instance identity and this layer may never derive it — a source that left it blank would give
    /// every instance one shared trigger counter.
    /// </summary>
    [Fact]
    public void Every_reported_effect_names_a_holding()
    {
        var source = Create(
            EffectSourceKind.TALENTS, Holdings(Effect("TAL_A"), Effect("TAL_B"), Effect("TAL_C")));

        var unnamed = source.Effects
            .Where(e => !e.Instance.NamesAHolding)
            .Select(e => $"'{e.Effect.Id}' is reported with no EffectInstanceId")
            .ToArray();

        unnamed.ShouldBeEmpty();

        // Floored: ShouldBeEmpty passes on an empty collection, so an implementation reporting
        // nothing at all would satisfy the assertion above.
        source.Effects.Count.ShouldBe(3);
    }

    /// <summary>
    /// Two copies of one authored effect are two different holdings. <c>TriggerRegistry.Register</c>
    /// refuses a duplicate, and a source that keyed both copies on the effect id would give
    /// <c>PK_FLURRY</c> one shared counter — firing on every 5th attack instead of every 5th per copy,
    /// which produces a legal-looking fight and is wrong in the only number that matters.
    /// </summary>
    [Fact]
    public void Two_copies_of_one_effect_are_two_distinct_holdings()
    {
        var source = Create(EffectSourceKind.GEAR, Holdings(Effect("AFF_KEEN"), Effect("AFF_KEEN")));

        source.Effects.Count.ShouldBe(2);
        source.Effects[0].Effect.Id.ShouldBe(source.Effects[1].Effect.Id, "one authored effect");
        source.Effects[0].Instance.ShouldNotBe(source.Effects[1].Instance, "two holdings of it");
    }

    /// <summary>
    /// The source does not alias a list the caller can still mutate. A build read at collection that
    /// changed before gating is the order-dependence the resolution pipeline exists to remove.
    /// </summary>
    [Fact]
    public void Mutating_the_list_that_was_passed_in_does_not_change_the_source()
    {
        var mutable = new List<SourcedEffect> { Held(Effect("AFF_A")) };
        var source = Create(EffectSourceKind.AFFIXES, mutable);

        mutable.Add(Held(Effect("AFF_B")));
        mutable[0] = Held(Effect("AFF_REPLACED"));

        source.Effects.Select(e => e.Effect.Id).ShouldBe(new[] { "AFF_A" });
    }

    /// <summary>
    /// The interface exposes no way to write, and is floored at its two members — a view that grew a
    /// mutator, or shrank to nothing, would take the assertion above with it.
    /// </summary>
    [Fact]
    public void The_interface_declares_no_mutating_member()
    {
        var offenders = typeof(IEffectSource)
            .GetMethods()
            .Where(m => m.ReturnType == typeof(void) || m.Name.StartsWith("set_", StringComparison.Ordinal))
            .Select(m => $"IEffectSource.{m.Name} returns void or is a setter — a source is read-only")
            .ToArray();

        offenders.ShouldBeEmpty();

        typeof(IEffectSource).GetMethods().Length.ShouldBe(
            2,
            "18 §8 step 1 asks a source for two things and no more: which of the ten it is, and what " +
            "it contributes. A third member is a widening that the ten unwritten implementations " +
            "would inherit.");
    }
}

/// <summary>
/// <c>ListEffectSource</c> — the in-<c>Core</c> implementation — run through the shared contract.
/// </summary>
public sealed class ListEffectSourceContractTests : EffectSourceContract
{
    private protected override IEffectSource Create(
        EffectSourceKind kind, IReadOnlyList<SourcedEffect> effects) =>
        new ListEffectSource(kind, effects);

    /// <summary>
    /// A <c>null</c> in the list is refused at construction, naming the position. The resolution order
    /// is stated over ids and a hole has none.
    /// </summary>
    [Fact]
    public void A_null_effect_is_refused_at_construction()
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new ListEffectSource(
                EffectSourceKind.GEAR, new[] { Held(Effect("AFF_A")), new SourcedEffect(null!, EffectInstanceId.Of("x")) }));

        thrown.Message.ShouldContain("element 1", Case.Sensitive);
    }

    /// <summary>
    /// A kind outside the declared ten is refused: it has no position in the source order, so
    /// <see cref="EffectResolutionOrder"/>'s tiebreak would be undefined for it.
    /// </summary>
    [Fact]
    public void A_kind_outside_18_8_step_1s_ten_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => new ListEffectSource((EffectSourceKind)99, Array.Empty<SourcedEffect>()));

        thrown.Message.ShouldContain("ten sources", Case.Sensitive);
    }
}
