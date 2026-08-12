using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// 🔒 `18` §8 <b>steps 1 and 2</b>, and the composition into M2-07's steps 3-10.
/// </summary>
/// <remarks>
/// The tests that compose run <c>StatAggregation.Aggregate</c> for real. R17 forbids
/// <c>Rules.Effects</c> naming <c>Rules.Stats</c> in production; this assembly can see both, which is
/// exactly where the composition claim belongs — the seam between them is a list of effects in a
/// defined order, and the only way to show that is enough is to hand it over.
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
    /// One collected entry, for the tests that assert the comparer directly. The instance id is
    /// deliberately the SAME for every entry: it is an identity key and plays no part in the
    /// ordering, so a test that varied it could pass on the wrong component.
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

    /// <summary>
    /// 🔒 Step 1 collects from the ten declared sources, and from <b>nothing else</b>. None has a
    /// real data model yet, so an M2 build reaches the aggregation through whichever slots a caller
    /// fills with a <c>ListEffectSource</c> — which is the correct end state, not a gap.
    /// </summary>
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

        // The eight kinds nobody supplied contributed nothing, and did not throw.
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

    /// <summary>
    /// 🔒 Two sources claiming one `18` §8 step 1 slot are refused — the same-id tiebreak is
    /// <c>(source, index)</c> and is total only because a kind names exactly one list.
    /// </summary>
    [Fact]
    public void Two_sources_claiming_one_slot_are_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("A", 0.1)),
            Source(EffectSourceKind.GEAR, Pct("B", 0.1))));

        // S2 — which rule fired, not merely that one did.
        thrown.Message.ShouldContain("claim 18 §8 step 1's 'GEAR' slot", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <b>The holding survives the whole pass.</b> `18` §3's <c>everyNth</c> counters live on the
    /// effect <em>instance</em>, and <c>TriggerRegistry.Register</c> refuses a duplicate — so a
    /// consumer of a resolution pass must be able to tell two copies of one authored effect apart
    /// without deriving an identity of its own.
    /// </summary>
    /// <remarks>
    /// ⚠️ The ordering pair <c>(Source, IndexInSource)</c> is <b>not</b> that identity: it is
    /// per-pass and renumbers when the build changes, where an <c>EffectInstanceId</c> must survive
    /// battle boundaries for <c>PK_MIDAS</c>. Both are asserted here so the two cannot be conflated.
    /// </remarks>
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

        // 🔒 One authored id, two holdings — and the ordering pair is the OTHER key.
        resolved.Active.Select(c => c.Effect.Id).ShouldBe(new[] { "AFF_KEEN", "AFF_KEEN" });
        resolved.Active.Select(c => c.IndexInSource).ShouldBe(new[] { 0, 1 });
    }

    /// <summary>
    /// 🔒 A source that reports an effect with <b>no</b> holding is refused by the collector, not
    /// only by <c>ListEffectSource</c>'s constructor — <see cref="IEffectSource"/> is the extension
    /// point ten later implementations satisfy.
    /// </summary>
    [Fact]
    public void A_source_reporting_an_effect_with_no_holding_is_refused_by_the_collector()
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => EffectResolver.Resolve(
                EffectSourceSet.Of(new UnnamedHoldingSource()), AllActive.Instance));

        // S2 — which rule fired: the holding, not the effect and not the kind.
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
    /// 🔒 A source whose kind is outside `18` §8 step 1's ten is refused <b>at the set</b>, not left
    /// for <c>Collect</c> to walk past.
    /// </summary>
    /// <remarks>
    /// <c>Collect</c> iterates the catalogue, so an out-of-catalogue source would be stored, never
    /// visited and contribute nothing — silently, which is the precise failure
    /// <c>EffectSourceSet</c>'s remarks say it exists to prevent. <c>ListEffectSource</c> validates in
    /// its own constructor, but <see cref="IEffectSource"/> is the extension point for ten
    /// implementations by seven later milestones and none of them is obliged to.
    /// </remarks>
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

    // ══════════════════════════════════════════════════════ R5 — collection order is immaterial

    /// <summary>
    /// 🔒 <b>R5.</b> Step 1's <em>"(in draft order)"</em> is the collection order; §8's closing
    /// <em>"not draft order"</em> is the application order. The resolver sorts, so the order the
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
    /// 🔒 The order is <b>ordinal</b>, never the ambient collation. Under <c>en-US</c>
    /// <c>"PK_A"</c> sorts before <c>"PKA"</c>; ordinally <c>'_'</c> (U+005F) is above <c>'A'</c>
    /// (U+0041), so it sorts after.
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
    /// 🔴 <b>The duplicate-id ruling, proved where it is observable.</b> Two effects sharing one id
    /// — the same authored affix from two gear slots — resolve in a documented order rather than in
    /// arrival order. `18` §8 step 8 is <em>"<c>STAT_SET</c>, last writer wins"</em>, so a pair that
    /// would otherwise resolve differently is exactly a same-id <c>STAT_SET</c> pair at two values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 This runs the <b>real</b> <c>StatAggregation.Aggregate</c>, not the comparer alone: the
    /// claim is that the ruling survives the handoff into `18` §8 steps 3-10, which depends on that
    /// method's re-sort being stable.
    /// </para>
    /// <para>
    /// ⚠️ <b>This test alone does NOT prove the tiebreak works</b>, and it is labelled so nobody
    /// reads it as though it did. Removing the tiebreak entirely (steering S1) left it <b>green</b>:
    /// <c>EffectSourceSet.Collect</c> already walks the catalogue in `18` §8 step 1's order, so the
    /// argument order to <c>Of</c> is normalised before the sort ever runs, and two elements are
    /// below <c>Array.Sort</c>'s insertion-sort threshold anyway. What it does pin is <b>which</b>
    /// source wins, which is spec content in its own right. The tiebreak itself is held by
    /// <see cref="The_documented_tiebreak_survives_a_sort_large_enough_to_scramble_equal_elements"/>
    /// and <see cref="The_resolution_order_never_calls_two_distinct_collected_effects_equal"/>.
    /// </para>
    /// </remarks>
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

        // 🔒 PERKS is source 10 of 18 §8 step 1 and GEAR is source 1, so the perk's STAT_SET is the
        //    LAST writer under step 8 — whichever order the caller happened to build the set in.
        oneWay.Final[StatId.MAX_HP].ShouldBe(99.0);
        otherWay.Final[StatId.MAX_HP].ShouldBe(99.0);
    }

    /// <summary>
    /// 🔒 The same ruling inside <b>one</b> source: index within the source is the second tiebreak,
    /// so two slots of one gear list resolve in slot order.
    /// </summary>
    [Fact]
    public void Two_effects_with_one_id_in_one_source_resolve_in_list_order()
    {
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Set("AFF_FRAIL", 10.0), Set("AFF_FRAIL", 42.0))));

        result.Final[StatId.MAX_HP].ShouldBe(42.0, "index 1 is the last writer under 18 §8 step 8");
    }

    /// <summary>
    /// 🔴 <b>The test that actually holds the duplicate-id ruling.</b> Twenty effects sharing one id,
    /// which is above <c>Array.Sort</c>'s insertion-sort threshold — so the sort genuinely permutes
    /// equal elements, and only a <em>total</em> comparer can put them back in `18` §8 step 1's
    /// documented order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Twenty, and the number is load-bearing.</b> .NET's introsort runs insertion sort at 16
    /// elements or fewer, which is stable in practice; below that threshold a broken tiebreak is
    /// invisible because arrival order survives and arrival order happens to be the right answer.
    /// This was found by removing the tiebreak on purpose (steering S1) and watching the two-element
    /// tests stay green.
    /// </para>
    /// <para>
    /// 🔒 <b>Deterministic, not probabilistic.</b> <c>Array.Sort</c> is a pure function of its input
    /// and its comparer, so this test does not flake: with the tiebreak it is right every time, and
    /// without it, it is wrong every time.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_documented_tiebreak_survives_a_sort_large_enough_to_scramble_equal_elements()
    {
        // Twenty STAT_SETs on one id, at ascending values — 18 §8 step 8 is "last writer wins", so
        // the answer names exactly which of the twenty the order put last.
        var shared = Enumerable.Range(0, 20).Select(i => Set("AFF_FRAIL", 100.0 + i)).ToArray();

        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.GEAR, shared)), AllActive.Instance);

        // Step 1's index within the source is the second tiebreak, so the twenty come back in list
        // order and the last writer is index 19.
        resolved.Collected.Select(c => c.IndexInSource).ShouldBe(Enumerable.Range(0, 20));
        resolved.ActiveDefinitions.Select(e => e.Value).ShouldBe(shared.Select(e => e.Value));

        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100), (StatId.MAX_HP, 100)), resolved.ActiveDefinitions,
            StatFixtures.Caps(), StatAggregationSeams.Strict);

        result.Final[StatId.MAX_HP].ShouldBe(
            119.0,
            "18 §8 step 8's last writer is the effect the resolution order put last — index 19 of the " +
            "GEAR source. Without the (source, index) tiebreak the comparer answers 0 for all twenty " +
            "and Array.Sort's quicksort leaves them in an order no document states");
    }

    /// <summary>
    /// 🔒 <b>The comparer is TOTAL.</b> Stated directly, over the two pairs the tiebreak exists to
    /// separate — and this one depends on no sort at all, so it holds even if <c>Array.Sort</c>'s
    /// internals change.
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
    /// 🔒 Both copies survive. The ruling fixes the <em>order</em>, and de-duplicating would halve a
    /// legitimate build — two slots of the same affix are two bonuses.
    /// </summary>
    [Fact]
    public void Two_effects_with_one_id_both_apply()
    {
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("AFF_KEEN", 0.05), Pct("AFF_KEEN", 0.05))));

        // 100 × (1 + 0.05 + 0.05) — 18 §8 step 5 sums the bucket, then multiplies base once.
        result.Final[StatId.ATK].ShouldBe(110.0);
    }

    // ══════════════════════════════════════════════════════ step 2 — the condition gate

    /// <summary>
    /// 🔒 Step 2 filters by condition against current state, and reports what it removed rather than
    /// dropping it silently.
    /// </summary>
    [Fact]
    public void Step_2_filters_by_condition_and_reports_what_it_removed()
    {
        // 18 §7.2's PK_EXECUTIONER: +25% DMG% while the target is below 30% HP.
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

        // 🔒 Step 1 collected both in BOTH cases. Filtering is step 2's, and a source that
        //    pre-filtered would cache an answer that is wrong on the next tick.
        againstHealthy.Collected.Count.ShouldBe(2);
    }

    /// <summary>`18` §1's <c>"condition": null</c> — an ungated effect is active.</summary>
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
    /// 🔒 A condition that cannot resolve is <b>not</b> swallowed. `18` §9.3 rules that a clause with
    /// no meaning here is <em>"simply skipped"</em> by an authored <c>IS_PVP</c> condition, so one
    /// that reaches an incompatible context is content that failed to skip itself.
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

        // 05 §3.3 gives a duel no run, so GOLD_HELD has no subject.
        var thrown = Should.Throw<EffectContextException>(() => EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.GEAR, goldGated)),
            EffectTestBattle.Duel()));

        thrown.Token.ShouldBe("GOLD_HELD");
    }

    // ══════════════════════════════════════════════════════ ruling 1 — the absent trigger

    /// <summary>
    /// 🔴 <b>Ruling 1: an absent <c>trigger</c> is <c>ALWAYS</c>.</b> Read from `18` §1.1's
    /// exhaustive partition — <em>"at every resolution pass for <c>ALWAYS</c> effects, at fire time
    /// for triggered ones"</em>. `18` §9.1's <c>CP_GLASS_HEART</c> authors both its clauses with no
    /// trigger; under any other reading a `18` §8 pass would not see them at all.
    /// </summary>
    [Fact]
    public void An_effect_with_no_trigger_is_an_ALWAYS_passive()
    {
        // 18 §9.1, verbatim: no trigger, no target.
        var glassHeart = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_1", Op = EffectOp.STAT_MULT, Stat = StatSelector.AllCombat, Value = 2.0,
        };

        EffectDefaults.TriggerKindOf(glassHeart).ShouldBe(TriggerKind.ALWAYS);
        EffectDefaults.IsAlwaysActive(glassHeart).ShouldBeTrue();
        EffectDefaults.TriggerOf(glassHeart).Kind.ShouldBe(TriggerKind.ALWAYS);

        // 🔒 And it reaches a 18 §8 pass, which is the whole consequence.
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, glassHeart)), AllActive.Instance);

        EffectResolver.ActiveOfKind(resolved, TriggerKind.ALWAYS)
                      .ShouldHaveSingleItem().Id.ShouldBe("CP_GLASS_HEART_1");
    }

    /// <summary>🔒 An authored trigger is never overridden by the default.</summary>
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

        // 🔒 Collected and gated like any other effect — 18 §8 step 1 is "all active effects", and
        //    WHEN each fires is the trigger layer's (M2-04), not step 1's.
        resolved.ActiveDefinitions.ShouldHaveSingleItem().Id.ShouldBe("PK_CLEAVE");
        EffectResolver.ActiveOfKind(resolved, TriggerKind.ALWAYS).ShouldBeEmpty();
    }

    // ══════════════════════════════════════════════════════ the composition into steps 3-10

    /// <summary>
    /// 🔒 The whole of `18` §8: steps 1-2 here, steps 3-10 in M2-07's <c>StatAggregation</c>, joined
    /// by a list of effects in a defined order and nothing else.
    /// </summary>
    [Fact]
    public void The_resolver_output_is_what_StatAggregation_consumes()
    {
        // 18 §7.1's PK_SHARP_EDGE (+12% ATK) and a gear affix (+5% ATK) on a 100 ATK base.
        var result = Aggregate(EffectSourceSet.Of(
            Source(EffectSourceKind.GEAR, Pct("AFF_KEEN", 0.05)),
            Source(EffectSourceKind.PERKS, Pct("PK_SHARP_EDGE", 0.12))));

        // 18 §8 step 5: sum the bucket, then multiply base ONCE — 100 × (1 + 0.17).
        result.Final[StatId.ATK].ShouldBe(117.0);
    }

    /// <summary>
    /// 🔒 <b>One gate, both step 2s.</b> <c>StatAggregation</c> re-applies the step-2 filter and its
    /// remarks require the second evaluation to agree with the first. Handing it the resolver's own
    /// gate makes that structural: a conditional effect the resolver admitted is admitted again, and
    /// the strict default's refusal is never reached.
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

        // 🔒 The CONTEXT overload — the one a caller reaches for — and the gate it built comes back
        //    on the result. Nothing here constructs a second gate, which is the whole claim.
        var resolved = EffectResolver.Resolve(
            EffectSourceSet.Of(Source(EffectSourceKind.PERKS, conditional)), context);

        var gate = resolved.Gate;

        resolved.ActiveDefinitions.ShouldHaveSingleItem().Id.ShouldBe("PK_ARSENAL");

        var seams = StatAggregationSeams.Strict with { Conditions = gate };
        var result = StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100)), resolved.ActiveDefinitions, StatFixtures.Caps(), seams);

        result.Final[StatId.ATK].ShouldBe(103.0);

        // 🔒 And the refusal it replaced is real — proof this test is not passing for free.
        Should.Throw<NotSupportedException>(() => StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100)), resolved.ActiveDefinitions, StatFixtures.Caps(),
            StatAggregationSeams.Strict));
    }

    /// <summary>Runs `18` §8 end to end over a 100/100 base with everything active.</summary>
    private static AggregatedStats Aggregate(EffectSourceSet sources)
    {
        var resolved = EffectResolver.Resolve(sources, AllActive.Instance);

        return StatAggregation.Aggregate(
            StatFixtures.Block((StatId.ATK, 100), (StatId.MAX_HP, 100)), resolved.ActiveDefinitions,
            StatFixtures.Caps(), StatAggregationSeams.Strict);
    }
}
