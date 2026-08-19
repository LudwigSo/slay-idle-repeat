using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>Effect collection and condition gating, and their composition into stat aggregation.</summary>
/// <remarks>
/// Internal seam: the public fight takes one flat effect list, so the per-source collection and the
/// same-id (source, index) tiebreak are not expressible through it. The composing tests run the real
/// <c>StatAggregation.Aggregate</c>.
/// </remarks>
public sealed class EffectResolverTests
{
    private static EffectDefinition Pct(string id, double value, StatId stat = StatId.ATK) =>
        new() { Id = id, Op = EffectOp.STAT_ADD_PCT, Stat = StatSelector.Of(stat), Value = value };

    private static EffectDefinition Set(string id, double value, StatId stat = StatId.MAX_HP) =>
        new()
        {
            Id = id, Op = EffectOp.STAT_SET, Stat = StatSelector.Of(stat), Value = value,
            ValueMode = ValueMode.FLAT,
        };

    private static ListEffectSource Source(EffectSourceKind kind, params EffectDefinition[] effects) =>
        ListEffectSource.Synthetic(kind, effects);

    /// <summary>
    /// One collected entry for the comparer tests. The instance id is deliberately the SAME for every
    /// entry — it plays no part in the ordering, so a test that varied it could pass on the wrong component.
    /// </summary>
    private static CollectedEffect Collected(EffectDefinition effect, EffectSourceKind source, int index) =>
        new(effect, EffectInstanceId.Of("holding"), source, index);

    /// <summary>The step-2 gate a test with no live fight uses: everything is active.</summary>
    private sealed class AllActive : IEffectConditionGate
    {
        internal static AllActive Instance { get; } = new();

        public bool IsActive(EffectDefinition effect) => true;
    }

    // ══════════════════════════════════════════════════════ step 1 — collection

    /// <summary>Collection reads from the ten declared sources, and from nothing else.</summary>
    [Fact]
    public void Step_1_collects_from_the_declared_sources_and_leaves_the_rest_empty()
    {
        var sources = EffectSourceSet.Of(
            Source(EffectSourceKind.PERKS, Pct("PK_SHARP_EDGE", 0.12)),
            Source(EffectSourceKind.GEAR, Pct("AFF_KEEN", 0.05)));

        var resolved = EffectResolver.Resolve(sources, AllActive.Instance);

        resolved.Collected.Select(c => c.Effect.Id).ShouldBe(new[] { "AFF_KEEN", "PK_SHARP_EDGE" });
        resolved.Collected.Select(c => c.Source).ShouldBe(
            new[] { EffectSourceKind.GEAR, EffectSourceKind.PERKS });

        foreach (var row in EffectSourceCatalogue.Rows)
        {
            if (row.Kind is EffectSourceKind.PERKS or EffectSourceKind.GEAR)
            {
                continue;
            }

            sources.For(row.Kind).ShouldBeNull();
        }
    }

    /// <summary>The empty build resolves to nothing, and does not throw on the way.</summary>
    [Fact]
    public void An_empty_build_resolves_to_no_effects()
    {
        var resolved = EffectResolver.Resolve(EffectSourceSet.Empty, AllActive.Instance);

        resolved.Collected.ShouldBeEmpty();
        resolved.Active.ShouldBeEmpty();
        resolved.GatedOut.ShouldBeEmpty();
    }

    /// <summary>Two sources claiming one slot are refused — the same-id tiebreak is (source, index) and is total only because a kind names exactly one list.</summary>
    [Fact]
    public void Two_sources_claiming_one_slot_are_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("A", 0.1)),
            Source(EffectSourceKind.GEAR, Pct("B", 0.1))));

        thrown.Message.ShouldContain("claim 18 §8 step 1's 'GEAR' slot", Case.Sensitive);
    }

    /// <summary>
    /// The holding survives the whole pass — trigger counters live on the effect instance, so two
    /// copies of one authored effect must stay tellable apart across battle boundaries.
    /// </summary>
    [Fact]
    public void The_holding_each_effect_came_from_survives_into_the_resolved_set()
    {
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(
                new ListEffectSource(EffectSourceKind.GEAR, new[]
                {
                    new SourcedEffect(Pct("AFF_KEEN", 0.05), EffectInstanceId.Of("gear:helm:affix0")),
                    new SourcedEffect(Pct("AFF_KEEN", 0.05), EffectInstanceId.Of("gear:boots:affix0")),
                })),
            AllActive.Instance);

        resolved.Active.Select(c => c.Instance.Value)
                .ShouldBe(new[] { "gear:helm:affix0", "gear:boots:affix0" });

        // One authored id, two holdings — and the ordering pair is the OTHER key.
        resolved.Active.Select(c => c.Effect.Id).ShouldBe(new[] { "AFF_KEEN", "AFF_KEEN" });
        resolved.Active.Select(c => c.IndexInSource).ShouldBe(new[] { 0, 1 });
    }

    /// <summary>
    /// A source that reports an effect with no holding is refused by the collector, not only by
    /// <c>ListEffectSource</c>'s constructor — <see cref="IEffectSource"/> is the extension point ten
    /// later implementations satisfy.
    /// </summary>
    [Fact]
    public void A_source_reporting_an_effect_with_no_holding_is_refused_by_the_collector()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => EffectResolver.Resolve(
                EffectSourceSet.Of(new UnnamedHoldingSource()), AllActive.Instance));

        thrown.Message.ShouldContain("names no holding", Case.Sensitive);
        thrown.Message.ShouldContain("AFF_UNHELD", Case.Sensitive);
    }

    /// <summary>A source built outside <c>ListEffectSource</c>, reporting a <c>default</c> holding.</summary>
    private sealed class UnnamedHoldingSource : IEffectSource
    {
        public EffectSourceKind Kind => EffectSourceKind.AFFIXES;

        public IReadOnlyList<SourcedEffect> Effects { get; } = new[]
        {
            new SourcedEffect(
                new EffectDefinition { Id = "AFF_UNHELD", Op = EffectOp.STAT_ADD_PCT },
                default),
        };
    }

    /// <summary>
    /// A source whose kind is outside the declared ten is refused at the set, not left for
    /// <c>Collect</c> to walk past: <c>Collect</c> iterates the catalogue, so an out-of-catalogue
    /// source would be stored, never visited, and contribute nothing silently.
    /// </summary>
    [Fact]
    public void A_source_whose_kind_is_outside_the_ten_is_refused_rather_than_silently_skipped()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EffectSourceSet.Of(new StrayKindSource()));

        thrown.Message.ShouldContain("ten sources", Case.Sensitive);
    }

    /// <summary>
    /// An <see cref="IEffectSource"/> that does <b>not</b> go through <c>ListEffectSource</c>'s
    /// constructor check — the shape a later milestone's own implementation could take.
    /// </summary>
    private sealed class StrayKindSource : IEffectSource
    {
        public EffectSourceKind Kind => (EffectSourceKind)99;

        public IReadOnlyList<SourcedEffect> Effects { get; } = new[]
        {
            new SourcedEffect(
                new EffectDefinition { Id = "AFF_LOST", Op = EffectOp.STAT_ADD_PCT },
                EffectInstanceId.Of("stray")),
        };
    }

    // ══════════════════════════════════════════════════════ collection order is immaterial

    /// <summary>
    /// Collection order and application order are different things: the resolver sorts, so the order
    /// effects arrive in cannot reach anything downstream.
    /// </summary>
    [Fact]
    public void Resolving_the_same_build_from_two_collection_orders_gives_one_answer()
    {
        var perks = new[] { Pct("PK_C", 0.03), Pct("PK_A", 0.01), Pct("PK_B", 0.02) };

        var forwards = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, perks)), AllActive.Instance);

        var backwards = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, perks.Reverse().ToArray())), AllActive.Instance);

        forwards.ActiveDefinitions.Select(e => e.Id).ShouldBe(new[] { "PK_A", "PK_B", "PK_C" });
        backwards.ActiveDefinitions.Select(e => e.Id).ShouldBe(new[] { "PK_A", "PK_B", "PK_C" });
    }

    /// <summary>
    /// The order is ordinal, never the ambient collation: under <c>en-US</c> <c>"PK_A"</c> sorts before
    /// <c>"PKA"</c>, but ordinally <c>'_'</c> (U+005F) is above <c>'A'</c> (U+0041), so it sorts after.
    /// </summary>
    [Fact]
    public void The_resolution_order_is_ordinal_not_culture_aware()
    {
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, Pct("PK_A", 0.1), Pct("PKA", 0.2))),
            AllActive.Instance);

        resolved.ActiveDefinitions.Select(e => e.Id).ShouldBe(
            new[] { "PKA", "PK_A" },
            "ordinally '_' is U+005F and 'A' is U+0041, so PKA sorts first — a culture-aware " +
            "comparer puts PK_A first and a German phone and a Linux container disagree");
    }

    // ══════════════════════════════════════════════════════ the duplicate-id ruling

    /// <summary>
    /// Pins WHICH source wins for a shared id; the tiebreak itself is held by
    /// <see cref="The_documented_tiebreak_survives_a_sort_large_enough_to_scramble_equal_elements"/>.
    /// </summary>
    [Fact]
    public void The_later_18_8_step_1_source_is_the_last_writer_for_a_shared_id()
    {
        var fromGear = Set("AFF_FRAIL", 10.0);
        var fromPerk = Set("AFF_FRAIL", 99.0);

        // Same two effects, same two sources — the ONLY difference is which order Of() saw them in.
        var oneWay = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, fromGear),
            Source(EffectSourceKind.PERKS, fromPerk)));

        var otherWay = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.PERKS, fromPerk),
            Source(EffectSourceKind.GEAR, fromGear)));

        // PERKS sorts after GEAR, so the perk's STAT_SET is the last writer regardless of build order.
        oneWay.Final[StatId.MAX_HP].ShouldBe(99.0);
        otherWay.Final[StatId.MAX_HP].ShouldBe(99.0);
    }

    /// <summary>The same ruling inside one source: index within the source is the second tiebreak.</summary>
    [Fact]
    public void Two_effects_with_one_id_in_one_source_resolve_in_list_order()
    {
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Set("AFF_FRAIL", 10.0), Set("AFF_FRAIL", 42.0))));

        result.Final[StatId.MAX_HP].ShouldBe(42.0, "index 1 is the last writer");
    }

    /// <summary>
    /// Twenty effects sharing one id — above <c>Array.Sort</c>'s insertion-sort threshold (16), so the
    /// sort genuinely permutes equal elements; below it a broken tiebreak is invisible because arrival
    /// order survives by accident.
    /// </summary>
    [Fact]
    public void The_documented_tiebreak_survives_a_sort_large_enough_to_scramble_equal_elements()
    {
        var shared = Enumerable.Range(0, 20).Select(i => Set("AFF_FRAIL", 100.0 + i)).ToArray();

        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.GEAR, shared)), AllActive.Instance);

        resolved.Collected.Select(c => c.IndexInSource).ShouldBe(Enumerable.Range(0, 20));
        resolved.ActiveDefinitions.Select(e => e.Value).ShouldBe(shared.Select(e => e.Value));

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100), (StatId.MAX_HP, 100)), resolved.ActiveDefinitions,
            StatFixtures.Caps(), StatAggregationSeams.Strict);

        result.Final[StatId.MAX_HP].ShouldBe(
            119.0,
            "the last writer is index 19 of the GEAR source. Without the (source, index) tiebreak the " +
            "comparer answers 0 for all twenty and Array.Sort's quicksort leaves them in an undefined " +
            "order");
    }

    /// <summary>
    /// The comparer is total, stated over the two pairs the tiebreak exists to separate — depends on
    /// no sort, so it holds even if <c>Array.Sort</c>'s internals change.
    /// </summary>
    [Fact]
    public void The_resolution_order_never_calls_two_distinct_collected_effects_equal()
    {
        var same = Pct("AFF_KEEN", 0.05);

        var pairs = new[]
        {
            (Collected(same, EffectSourceKind.GEAR, 0),
             Collected(same, EffectSourceKind.PERKS, 0)),
            (Collected(same, EffectSourceKind.GEAR, 0),
             Collected(same, EffectSourceKind.GEAR, 1)),
        };

        foreach (var (left, right) in pairs)
        {
            EffectResolutionOrder.Compare(left, right).ShouldBeLessThan(
                0, "the earlier 18 §8 step 1 position, or the earlier index, sorts first");
            EffectResolutionOrder.Compare(right, left).ShouldBeGreaterThan(0, "and the order is antisymmetric");
        }

        // The one pair that IS equal: an effect compared with itself.
        var self = Collected(same, EffectSourceKind.GEAR, 0);
        EffectResolutionOrder.Compare(self, self).ShouldBe(0);
    }

    /// <summary>
    /// Both copies survive. The ruling fixes the order, and de-duplicating would halve a legitimate
    /// build — two slots of the same affix are two bonuses.
    /// </summary>
    [Fact]
    public void Two_effects_with_one_id_both_apply()
    {
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("AFF_KEEN", 0.05), Pct("AFF_KEEN", 0.05))));

        // 100 × (1 + 0.05 + 0.05) — the bucket is summed, then base is multiplied once.
        result.Final[StatId.ATK].ShouldBe(110.0);
    }

    // ══════════════════════════════════════════════════════ step 2 — the condition gate

    /// <summary>Condition gating filters against current state, and reports what it removed rather than dropping it silently.</summary>
    [Fact]
    public void Step_2_filters_by_condition_and_reports_what_it_removed()
    {
        // PK_EXECUTIONER: +25% DMG% while the target is below 30% HP.
        var executioner = Pct("PK_EXECUTIONER", 0.25, StatId.DMG_PCT) with
        {
            Condition = EffectCondition.Of(
                new ConditionTerm
                {
                    Fn = ConditionFunction.TARGET_HP_PCT, Comparator = ConditionComparator.LT, Value = 0.30,
                }),
        };

        var hero = EffectTestBattle.Hero();
        var healthy = EffectTestBattle.Enemy("ORC", 1, currentHp: 100, maxHp: 100);
        var wounded = EffectTestBattle.Enemy("ORC", 1, currentHp: 20, maxHp: 100);

        var build = EffectSourceSet.Of(
            Source(EffectSourceKind.PERKS, executioner, Pct("PK_SHARP_EDGE", 0.12)));

        var againstHealthy = EffectResolver.Resolve(
            build, EffectTestBattle.Context(hero, hero, healthy) with { CurrentTarget = healthy });

        var againstWounded = EffectResolver.Resolve(
            build, EffectTestBattle.Context(hero, hero, wounded) with { CurrentTarget = wounded });

        againstHealthy.ActiveDefinitions.Select(e => e.Id).ShouldBe(new[] { "PK_SHARP_EDGE" });
        againstHealthy.GatedOut.ShouldBe(new[] { "PK_EXECUTIONER" });

        againstWounded.ActiveDefinitions.Select(e => e.Id).ShouldBe(new[] { "PK_EXECUTIONER", "PK_SHARP_EDGE" });
        againstWounded.GatedOut.ShouldBeEmpty();

        // Collection gathers both in BOTH cases; filtering happens later, so a source that
        // pre-filtered would cache an answer that is wrong on the next tick.
        againstHealthy.Collected.Count.ShouldBe(2);
    }

    /// <summary>An effect with no condition is active.</summary>
    [Fact]
    public void An_effect_with_no_condition_is_active()
    {
        var hero = EffectTestBattle.Hero();

        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, Pct("PK_SHARP_EDGE", 0.12))),
            EffectTestBattle.Context(hero, hero));

        resolved.ActiveDefinitions.ShouldHaveSingleItem().Id.ShouldBe("PK_SHARP_EDGE");
    }

    /// <summary>
    /// A condition that cannot resolve is not swallowed: an authored <c>IS_PVP</c> guard is expected to
    /// skip itself in an incompatible context, so one that reaches such a context is content that
    /// failed to skip itself, not a case to paper over.
    /// </summary>
    [Fact]
    public void A_condition_that_cannot_resolve_fails_loudly_rather_than_dropping_the_effect()
    {
        var goldGated = Pct("AFF_HOARD", 0.05) with
        {
            Condition = EffectCondition.Of(
                new ConditionTerm
                {
                    Fn = ConditionFunction.GOLD_HELD, Comparator = ConditionComparator.GTE, Value = 100,
                }),
        };

        // A duel has no run, so GOLD_HELD has no subject.
        var thrown = Should.Throw<EffectContextException>(() => EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.GEAR, goldGated)),
            EffectTestBattle.Duel()));

        thrown.Token.ShouldBe("GOLD_HELD");
    }

    // ══════════════════════════════════════════════════════ ruling 1 — the absent trigger

    /// <summary>18 ruling 1: an absent trigger is <c>ALWAYS</c> — an effect authored with neither a trigger nor a target relies on that default to be seen at all.</summary>
    [Fact]
    public void An_effect_with_no_trigger_is_an_ALWAYS_passive()
    {
        var glassHeart = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_1", Op = EffectOp.STAT_MULT, Stat = StatSelector.AllCombat, Value = 2.0,
        };

        EffectDefaults.TriggerKindOf(glassHeart).ShouldBe(TriggerKind.ALWAYS);
        EffectDefaults.IsAlwaysActive(glassHeart).ShouldBeTrue();
        EffectDefaults.TriggerOf(glassHeart).Kind.ShouldBe(TriggerKind.ALWAYS);

        // And it reaches a resolution pass, which is the whole consequence.
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, glassHeart)), AllActive.Instance);

        EffectResolver.ActiveOfKind(resolved, TriggerKind.ALWAYS)
                      .ShouldHaveSingleItem().Id.ShouldBe("CP_GLASS_HEART_1");
    }

    /// <summary>An authored trigger is never overridden by the default.</summary>
    [Fact]
    public void An_authored_trigger_is_left_alone()
    {
        var onHit = Pct("PK_CLEAVE", 0.40) with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_HIT },
        };

        EffectDefaults.TriggerKindOf(onHit).ShouldBe(TriggerKind.ON_HIT);
        EffectDefaults.IsAlwaysActive(onHit).ShouldBeFalse();

        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, onHit)), AllActive.Instance);

        // Collected and gated like any other effect — collection is "all active effects", and WHEN
        // each fires is the trigger layer's concern, not collection's.
        resolved.ActiveDefinitions.ShouldHaveSingleItem().Id.ShouldBe("PK_CLEAVE");
        EffectResolver.ActiveOfKind(resolved, TriggerKind.ALWAYS).ShouldBeEmpty();
    }

    // ══════════════════════════════════════════════════════ the composition into stat aggregation

    /// <summary>
    /// Collection and gating feed <c>StatAggregation</c> through a list of effects in a defined order
    /// and nothing else.
    /// </summary>
    [Fact]
    public void The_resolver_output_is_what_StatAggregation_consumes()
    {
        // PK_SHARP_EDGE (+12% ATK) and a gear affix (+5% ATK) on a 100 ATK base.
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("AFF_KEEN", 0.05)),
            Source(EffectSourceKind.PERKS, Pct("PK_SHARP_EDGE", 0.12))));

        // Sum the bucket, then multiply base ONCE — 100 × (1 + 0.17).
        result.Final[StatId.ATK].ShouldBe(117.0);
    }

    /// <summary>
    /// One gate serves both filter passes: a conditional effect the resolver admitted is admitted
    /// again by <c>StatAggregation</c>, and the strict default's refusal is never reached.
    /// </summary>
    [Fact]
    public void The_same_gate_serves_both_step_2s()
    {
        var hero = EffectTestBattle.Hero(currentHp: 100, maxHp: 100);
        var context = EffectTestBattle.Context(hero, hero);

        // A condition the strict seam (UnconditionalEffectsOnly) would REFUSE outright.
        var conditional = Pct("PK_ARSENAL", 0.03) with
        {
            Condition = EffectCondition.Of(
                new ConditionTerm
                {
                    Fn = ConditionFunction.SELF_HP_PCT, Comparator = ConditionComparator.GTE, Value = 1.0,
                }),
        };

        // The CONTEXT overload — the gate it built comes back on the result, and nothing here
        // constructs a second gate.
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, conditional)), context);

        var gate = resolved.Gate;

        resolved.ActiveDefinitions.ShouldHaveSingleItem().Id.ShouldBe("PK_ARSENAL");

        var seams = StatAggregationSeams.Strict with { Conditions = gate };
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100)), resolved.ActiveDefinitions, StatFixtures.Caps(), seams);

        result.Final[StatId.ATK].ShouldBe(103.0);

        // And the refusal it replaced is real — proof this test is not passing for free.
        Should.Throw<EffectContextException>(() => StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100)), resolved.ActiveDefinitions, StatFixtures.Caps(),
            StatAggregationSeams.Strict));
    }

    /// <summary>Runs collection through aggregation over a 100/100 base with everything active.</summary>
    private static AggregatedStats Aggregate(EffectSourceSet sources)
    {
        var resolved = EffectResolver.Resolve(sources, AllActive.Instance);

        return StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100), (StatId.MAX_HP, 100)), resolved.ActiveDefinitions,
            StatFixtures.Caps(), StatAggregationSeams.Strict);
    }
}
