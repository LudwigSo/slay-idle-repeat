using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// A committed-baseline determinism test over 10 000 seeded build permutations resolved and hashed
/// with <c>CanonicalStateWriter</c>.
/// </summary>
/// <remarks>
/// There is one resolver both client and server load, and no client build yet — so comparing it with
/// itself would be a test that cannot fail. As a committed baseline it still catches what a parity test
/// exists to catch: an accidental order-dependence in resolution. Real two-runtime parity is a later
/// milestone.
/// <para>
/// The baseline is self-generated: build permutation → hash has no publisher, so the table proves
/// stability — the resolver is a pure function whose output cannot drift unnoticed — and not that the
/// encoding or resolution order is correct. That is the per-op unit tests'.
/// </para>
/// <para>
/// The coverage tests below assert the generator's emitted vocabulary against the production
/// catalogues in both directions, so a new op cannot be added without appearing in the corpus or
/// failing a test.
/// </para>
/// </remarks>
public sealed class DslDeterminismBaselineTests
{
    /// <summary>
    /// A wall-clock guard against an algorithmic regression, not a performance target. Measured at
    /// about 3 s in total; the budget is ~10× that, which leaves room for a slow CI agent without
    /// letting an accidental O(n²) through.
    /// </summary>
    private const int BudgetSeconds = 30;

    /// <summary>
    /// The floor for permutations whose duplicate-id pair <b>survived step 2</b> in a build above
    /// the introsort threshold. Lower than the raw >16-effect fraction because the step-2 gate is
    /// free to remove either half — a separate claim, so a separate number.
    /// </summary>
    private const double MinimumFractionOfDuplicatesSurvivingStep2 = 0.70;

    private static ResolvedCorpus Corpus => ResolvedCorpus.Instance;

    // ══════════════════════════════════════════════════════ the committed table

    /// <summary>The headline: 10 000 permutations, one hash, unchanged.</summary>
    [Fact]
    public void The_committed_aggregate_hash_still_covers_the_whole_corpus()
    {
        Corpus.Aggregate.ShouldBe(
            DslDeterminismBaseline.Aggregate,
            "18 §8 must be implemented exactly, or builds produce different numbers on client and " +
            "server. The aggregate hash covers all 10 000 permutations, so this failing means SOME " +
            "step of 18 §8 changed — the chunk rows below say which hundred permutations moved. " +
            "A failure here is a determinism break, never a test fix.");
    }

    /// <summary>Which hundred permutations moved — the localisation the aggregate cannot give.</summary>
    [Theory]
    [MemberData(nameof(ChunkIds))]
    public void Every_committed_chunk_hash_still_holds(int chunk)
    {
        Corpus.ChunkWires[chunk].ShouldBe(
            DslDeterminismBaseline.Chunks[chunk],
            $"chunk {chunk.ToString(CultureInfo.InvariantCulture)} covers permutations " +
            $"{(chunk * BuildPermutationGenerator.ChunkSize).ToString(CultureInfo.InvariantCulture)}.." +
            $"{((chunk * BuildPermutationGenerator.ChunkSize) + BuildPermutationGenerator.ChunkSize - 1).ToString(CultureInfo.InvariantCulture)} " +
            "(18 §8)");
    }

    /// <summary>The individually pinned permutations, each defending a named property.</summary>
    [Theory]
    [MemberData(nameof(NamedIds))]
    public void Every_named_permutation_still_hashes_to_its_committed_value(string id)
    {
        var row = DslDeterminismBaseline.Row(id);

        Corpus.Wires[row.Permutation].ShouldBe(row.Hash, $"'{row.Id}' pins {row.Why}");
    }

    /// <summary>
    /// The named rows pin properties, not ordinals. This is the assertion that keeps them honest: a
    /// row still has to name the permutation its property <em>first holds at</em>.
    /// </summary>
    [Theory]
    [MemberData(nameof(NamedIds))]
    public void Every_named_row_still_names_the_permutation_its_property_first_holds_at(string id)
    {
        var row = DslDeterminismBaseline.Row(id);
        var named = Corpus.Named.Single(candidate => candidate.Id.Equals(id, StringComparison.Ordinal));

        named.Index.ShouldBe(
            row.Permutation,
            $"'{row.Id}' pins {row.Why}. The row names a PROPERTY of the corpus and the first " +
            "permutation that carries it; a row whose ordinal drifted is a generator whose output " +
            "moved, which is the same news as a hash moving.");
    }

    // The committed table's own well-formedness (unique ids, one row per property, header text,
    // seed/shape metadata, review-block format) is not asserted here: Row()'s Single() fails on a
    // duplicate, and the regeneration round-trip below compares the whole rendered text against the
    // committed file, so any drift in rows, header, or metadata already fails a consuming test.

    // ══════════════════════════════════════════════════════ vocabulary coverage, both ways

    /// <summary>All 44 ops reach the corpus, and nothing outside them does.</summary>
    [Fact]
    public void Every_op_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Select(effect => effect.Op),
            EffectVocabularyEmissionSets.Ops,
            EffectOps.All,
            "op",
            "18 §11: '44 ops = 41 + CLEAR_SUMMONS + STAT_COPY + RANDOM_OUTCOME'; 18 §10 step 4: " +
            "'add the op to the client/server parity test'");
    }

    /// <summary>All 23 trigger kinds reach the corpus.</summary>
    [Fact]
    public void Every_trigger_kind_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Where(effect => effect.Trigger is not null).Select(effect => effect.Trigger!.Kind),
            EffectVocabularyEmissionSets.Triggers,
            Enum.GetValues<TriggerKind>(),
            "trigger kind",
            "18 §11: '23 triggers = 21 + ON_DEATH + ON_REVIVE'");
    }

    /// <summary>All 23 condition functions reach the corpus.</summary>
    [Fact]
    public void Every_condition_function_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect()
                .SelectMany(effect => BuildPermutationGenerator.FunctionsIn(effect.Condition))
                .Concat(EveryEffect().Where(effect => effect.ValueScale is not null)
                    .Select(effect => effect.ValueScale!.Fn)),
            EffectVocabularyEmissionSets.Conditions,
            Enum.GetValues<ConditionFunction>(),
            "condition function",
            "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*'");
    }

    /// <summary>All 11 targets reach the corpus.</summary>
    [Fact]
    public void Every_target_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Where(effect => effect.Target is not null).Select(effect => effect.Target!.Value),
            EffectVocabularyEmissionSets.Targets,
            Enum.GetValues<EffectTarget>(),
            "target",
            "18 §11: '11 targets = 9 + OTHER_ENEMIES + OWNER'");
    }

    /// <summary>All 26 stats reach the corpus, the 12 non-combat ones included.</summary>
    [Fact]
    public void Every_stat_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect()
                .Where(effect => effect.Stat is { Kind: StatSelectorKind.SINGLE })
                .Select(effect => effect.Stat!.Value.Stat!.Value)
                .Concat(EveryEffect().Where(effect => effect.ToStat is not null).Select(effect => effect.ToStat!.Value)),
            EffectVocabularyEmissionSets.Stats,
            StatIds.All,
            "stat",
            "18 §2.1 declares 26 stats, of which 05 §1's actor block holds 14");
    }

    /// <summary>All 6 duration scopes reach the corpus.</summary>
    [Fact]
    public void Every_duration_scope_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Where(effect => effect.Duration is not null).Select(effect => effect.Duration!.Scope),
            EffectVocabularyEmissionSets.DurationScopes,
            Enum.GetValues<DurationScope>(),
            "duration scope",
            "18 §11: '6 duration scopes = 5 + PHASE'");
    }

    /// <summary>All 5 stacking modes reach the corpus.</summary>
    [Fact]
    public void Every_stacking_mode_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Where(effect => effect.Stacking is not null).Select(effect => effect.Stacking!.Mode),
            EffectVocabularyEmissionSets.StackingModes,
            Enum.GetValues<StackingMode>(),
            "stacking mode",
            "18 §6 gives five stacking modes");
    }

    /// <summary>All 8 value modes reach the corpus.</summary>
    [Fact]
    public void Every_value_mode_18_declares_is_emitted_by_the_permutation_generator()
    {
        Covers(
            EveryEffect().Where(effect => effect.ValueMode is not null).Select(effect => effect.ValueMode!.Value),
            EffectVocabularyEmissionSets.ValueModes,
            Enum.GetValues<ValueMode>(),
            "value mode",
            "18 §11 and 18 §2.2: eight value modes");
    }

    /// <summary>Every extension key reaches the corpus, including the ones that arrived without a catalogue row.</summary>
    [Fact]
    public void Every_18_10_1_extension_reaches_the_permutation_corpus()
    {
        BothDirections(
            EveryEffect().SelectMany(BuildPermutationGenerator.ExtensionKeysIn),
            EffectVocabularyEmissionSets.ExtensionKeys,
            "18 §10.1 extension",
            "18 §10 step 4: 'add the op to the client/server parity test'. A generator that only " +
            "emitted 18's pre-existing shapes would let every extension M2 took drift unnoticed");
    }

    /// <summary>
    /// The floor. The extension keys have no closed enum behind them, so this hand-written list is the
    /// independent third statement — deleting a constant from <see cref="EffectVocabularyEmissionSets"/>
    /// has to fail against something that is not itself.
    /// </summary>
    [Theory]
    [InlineData(EffectVocabularyEmissionSets.ToStatOnStatConvert)]
    [InlineData(EffectVocabularyEmissionSets.CapKindStatMax)]
    [InlineData(EffectVocabularyEmissionSets.CapKindRedirectExcess)]
    [InlineData(EffectVocabularyEmissionSets.ChargesOnNextAttackOps)]
    [InlineData(EffectVocabularyEmissionSets.ValueModeOnSurviveLethal)]
    [InlineData(EffectVocabularyEmissionSets.StatusTagOnRemoveStatus)]
    [InlineData(EffectVocabularyEmissionSets.ChanceOnOnAttack)]
    [InlineData(EffectVocabularyEmissionSets.ValueScaleStatusId)]
    [InlineData(EffectVocabularyEmissionSets.ValueScaleCategory)]
    public void Every_named_18_10_1_extension_reaches_the_corpus(string extension)
    {
        EveryEffect()
            .SelectMany(BuildPermutationGenerator.ExtensionKeysIn)
            .Any(key => key.Equals(extension, StringComparison.Ordinal))
            .ShouldBeTrue($"'{extension}' is an extension taken under 18 §10, whose step 4 is this suite");
    }

    /// <summary>
    /// The count, arithmetic spelled out — the floor <c>EffectVocabularyCountTests</c> gives the eight
    /// enum vocabularies, for the one vocabulary that has no enum.
    /// </summary>
    [Fact]
    public void The_extension_key_vocabulary_is_18_10_1s_arithmetic()
    {
        EffectVocabularyEmissionSets.ExtensionKeys.Count.ShouldBe(
            9,
            "18 §10.1's five rows — E2 contributing two, because STAT_MAX and REDIRECT_EXCESS are " +
            "different halves of step 9 — plus 18 §3.1 R11's chance on ON_ATTACK and M2-06's " +
            "valueScale argument keys, now two rather than three: 5 + 1 + 1 + 2 = 9. ⚠️ The third was " +
            "faceKind, gone with the die's face kinds and with DIE_FACE_COUNT, the only function that " +
            "took it");

        EffectVocabularyEmissionSets.ExtensionKeys.ShouldBeUnique();
    }

    /// <summary>All four combinator kinds reach the corpus, not only bare terms.</summary>
    [Fact]
    public void Every_condition_combinator_kind_reaches_the_permutation_corpus()
    {
        BothDirections(
            EveryEffect().Where(effect => effect.Condition is not null).Select(effect => effect.Condition!.Kind),
            Enum.GetValues<ConditionKind>(),
            "condition kind",
            "18 §4's condition tree is TERM plus three combinators");
    }

    /// <summary>All seven comparators reach the corpus, <c>BETWEEN</c> included.</summary>
    [Fact]
    public void Every_condition_comparator_reaches_the_permutation_corpus()
    {
        BothDirections(
            EveryEffect().SelectMany(effect => TermsIn(effect.Condition)).Select(term => term.Comparator),
            Enum.GetValues<ConditionComparator>(),
            "comparator",
            "18 §4 gives seven comparators");
    }

    /// <summary>Every one of the ten sources is populated somewhere in the corpus.</summary>
    [Fact]
    public void Every_18_8_step_1_source_is_populated_somewhere_in_the_corpus()
    {
        var populated = new HashSet<EffectSourceKind>();
        foreach (var permutation in Corpus.Permutations)
        {
            foreach (var row in EffectSourceCatalogue.Rows)
            {
                if (permutation.Sources.For(row.Kind) is not null)
                {
                    populated.Add(row.Kind);
                }
            }
        }

        EffectSourceCatalogue.Rows
            .Select(row => row.Kind)
            .Where(kind => !populated.Contains(kind))
            .ShouldBeEmpty(
                "18 §8 step 1 collects from ten sources 'in draft order'. A corpus that never " +
                "populated one of them would resolve a smaller step 1 than the game does, and the " +
                "source ordinal is half of EffectResolutionOrder's tiebreak.");
    }

    // ══════════════════════════════════════════════════════ the corpus's own shape

    /// <summary>Above the introsort threshold, in most of the corpus — the blind spot, at scale.</summary>
    [Fact]
    public void Most_of_the_corpus_exceeds_the_16_element_introsort_threshold()
    {
        var above = Corpus.Permutations
            .Count(permutation => permutation.Effects.Count > BuildPermutationGenerator.IntrosortStabilityThreshold);
        var fraction = (double)above / Corpus.Permutations.Count;

        // The anchor floor MinimumFractionAboveIntrosortThreshold's arithmetic is derived from. Left
        // unasserted it was a comment, and a silent drop from 13 anchors to 10 would have left the
        // 0.75 fraction passing on 23/30.
        Corpus.Permutations.Min(permutation => permutation.Effects.Count).ShouldBeGreaterThanOrEqualTo(
            BuildPermutationGenerator.MinimumAnchorCount,
            "every permutation carries the eight vocabulary anchors, its 18 §10.1 extension anchors, " +
            "the two defaulting anchors and the duplicate-id pair before a single filler effect");

        fraction.ShouldBeGreaterThanOrEqualTo(
            BuildPermutationGenerator.MinimumFractionAboveIntrosortThreshold,
            "M2-02 found that a naive duplicate-id test cannot detect the removal of " +
            "EffectResolutionOrder's (source, index) tiebreak, because Collect() normalises order AND " +
            ".NET introsort is stable at 16 elements or fewer. A corpus that stayed under the " +
            "threshold would reproduce that blind spot 10 000 times.");

        Corpus.Permutations
            .Count(permutation => permutation.Effects.Count <= BuildPermutationGenerator.IntrosortStabilityThreshold)
            .ShouldBeGreaterThan(
                0,
                "both regimes are wanted: the small builds are the ones a stable sort would hide a " +
                "tiebreak break in, and they have to be in the corpus for the contrast to exist");
    }

    /// <summary>
    /// Duplicate ids, surviving the condition gate, above the threshold, in a documented fraction of
    /// the corpus. A separate floor from the &gt;16-effect fraction, because these are different
    /// claims: the generator puts a duplicate pair in every permutation, the gate is free to remove
    /// either half, and this counts what survived.
    /// </summary>
    [Fact]
    public void Duplicate_effect_ids_survive_step_2_above_the_introsort_threshold()
    {
        var qualifying = 0;
        for (var index = 0; index < Corpus.Permutations.Count; index++)
        {
            var active = Corpus.Outcomes[index].Active;
            if (active.Count > BuildPermutationGenerator.IntrosortStabilityThreshold &&
                active.Count(row => row.Id.Equals(
                    BuildPermutationGenerator.DuplicateAnchorId, StringComparison.Ordinal)) == 2)
            {
                qualifying++;
            }
        }

        ((double)qualifying / Corpus.Permutations.Count).ShouldBeGreaterThanOrEqualTo(
            MinimumFractionOfDuplicatesSurvivingStep2,
            "every permutation carries a deliberate duplicate-id pair in two different 18 §8 step-1 " +
            "sources; what this counts is the pairs that reached the resolved set, in builds large " +
            "enough that a stable sort could not hide a broken tiebreak");
    }

    /// <summary>
    /// Condition gating does real work over the corpus, and does not gate it empty. The two failure
    /// modes are opposite and both silent: a gate that admits everything and a gate that admits
    /// nothing each produce a perfectly stable hash.
    /// </summary>
    [Fact]
    public void Step_2_gates_some_effects_out_and_keeps_most()
    {
        var gated = Corpus.Outcomes.Sum(outcome => outcome.GatedOut.Count);
        var active = Corpus.Outcomes.Sum(outcome => outcome.Active.Count);

        gated.ShouldBeGreaterThan(0, "18 §8 step 2 filters by condition, evaluated against current state");
        active.ShouldBeGreaterThan(gated, "a corpus whose every effect gated out would pin an empty pipeline");
        Corpus.Outcomes.ShouldAllBe(outcome => outcome.Active.Count > 0, "no permutation resolves to nothing");
        Corpus.Outcomes.Count.ShouldBe(BuildPermutationGenerator.PermutationCount);
    }

    /// <summary>
    /// The generator is a pure function of its seed. Without this, "committed baseline" would mean
    /// "whatever this machine produced once".
    /// </summary>
    [Fact]
    public void The_corpus_is_re_runnable()
    {
        foreach (var index in new List<int> { 0, 1, 17, 4_242, 9_999 })
        {
            var again = BuildPermutationGenerator.Permutation(index);

            PermutationResolution.Hash(PermutationResolution.Resolve(again)).ShouldBe(
                Corpus.Wires[index],
                $"permutation {index.ToString(CultureInfo.InvariantCulture)} is a pure function of " +
                $"BaselineSeed + {index.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>
    /// The property everything else rests on: the hash is order-sensitive. A hash that ignored the
    /// order of the resolved set could not notice the tiebreak's removal, and every row in the
    /// committed table would be green for the wrong reason.
    /// </summary>
    [Fact]
    public void The_permutation_hash_distinguishes_two_orders_of_one_resolved_set()
    {
        var outcome = Corpus.Outcomes.First(candidate => candidate.Active.Count > 1);
        var reordered = outcome with { Active = outcome.Active.Reverse().ToArray() };

        // Not redundant: a palindromic Active would make the two hashes legitimately equal, and the
        // test would then fail for a reason that had nothing to do with order-sensitivity.
        reordered.Active.ShouldNotBe(outcome.Active, "the reversal has to be a different order");

        PermutationResolution.Hash(reordered).ShouldNotBe(
            PermutationResolution.Hash(outcome),
            "18 §8 orders the resolved set, so the hash over it has to be able to tell two orders " +
            "apart. Reversing the active list is the cheapest statement of that.");
    }

    // ══════════════════════════════════════════════════════ the two M2-02 rulings

    /// <summary>
    /// An absent <c>trigger</c> is <c>ALWAYS</c>. Established and pinned: the resolver does not
    /// distinguish them, so the hash must not either.
    /// </summary>
    [Fact]
    public void An_absent_trigger_and_an_authored_ALWAYS_produce_the_same_hash()
    {
        var absent = new EffectDefinition
        {
            Id = "EFF_DEFAULTING",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 7.0,
        };
        var authored = absent with { Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS } };

        authored.ShouldNotBe(absent, "the two definitions differ — record equality sees the trigger");
        HashOfBuild(authored).ShouldBe(
            HashOfBuild(absent),
            "EffectDefaults.TriggerKindOf reads an absent trigger as ALWAYS (18 §1.1's exhaustive " +
            "partition of effects into ALWAYS and triggered), so 18 §8 cannot tell the two builds " +
            "apart and neither may the baseline. A hash that distinguished them would go red on an " +
            "authoring change 18 §8 is indifferent to.");
    }

    /// <summary>The other half of the same ruling — an absent <c>target</c> is <c>SELF</c>.</summary>
    [Fact]
    public void An_absent_target_and_an_authored_SELF_produce_the_same_hash()
    {
        var absent = new EffectDefinition
        {
            Id = "EFF_DEFAULTING",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 7.0,
        };
        var authored = absent with { Target = EffectTarget.SELF };

        authored.ShouldNotBe(absent);
        HashOfBuild(authored).ShouldBe(
            HashOfBuild(absent),
            "EffectDefaults.AbsentTarget is EffectTarget.SELF, so 18 §8 cannot tell the two builds apart");
    }

    /// <summary>
    /// The tiebreak above the introsort threshold with duplicate ids — the case whose removal a
    /// 16-element test cannot detect. Twenty entries, five ids, four <c>(source, index)</c> pairs
    /// each, asserted three ways: the two arrival orders agree, the result is the literal order the
    /// ruling describes, and the input really was above the threshold. It extends
    /// <c>EffectResolverTests</c>' single-id version with a 5 × 4 cross product, the shape a corpus of
    /// ten thousand builds actually produces.
    /// </summary>
    [Fact]
    public void The_documented_tiebreak_survives_a_sort_above_the_introsort_threshold()
    {
        var scrambled = TiebreakCase();
        scrambled.Count.ShouldBeGreaterThan(
            BuildPermutationGenerator.IntrosortStabilityThreshold,
            "at 16 elements or fewer .NET's introsort is stable and would hide the tiebreak's removal");

        var forward = Project(EffectResolutionOrder.Sort(scrambled));
        var reversed = Project(EffectResolutionOrder.Sort(scrambled.AsEnumerable().Reverse().ToArray()));

        forward.ShouldBe(
            reversed,
            "18 §8's order is TOTAL: two effects sharing an id are separated by EffectResolutionOrder's " +
            "(source ordinal, index) tiebreak and never by arrival. Without it Array.Sort returns 0 for " +
            "equal ids and introsort scrambles them differently for different inputs.");

        forward.ShouldBe(
            new List<string>
            {
                "EFF_A/1/0", "EFF_A/1/1", "EFF_A/4/0", "EFF_A/10/0",
                "EFF_B/1/0", "EFF_B/1/1", "EFF_B/4/0", "EFF_B/10/0",
                "EFF_C/1/0", "EFF_C/1/1", "EFF_C/4/0", "EFF_C/10/0",
                "EFF_D/1/0", "EFF_D/1/1", "EFF_D/4/0", "EFF_D/10/0",
                "EFF_E/1/0", "EFF_E/1/1", "EFF_E/4/0", "EFF_E/10/0",
            },
            "ordinal effect id first (18 §8's closing sentence), then the source's 18 §8 step-1 " +
            "position, then the index within that source");
    }

    // ══════════════════════════════════════════════════════ cost, and regeneration

    /// <summary>10 000 permutations belong to the unit tier, and there is no other tier.</summary>
    [Fact]
    public void The_10000_permutation_pass_stays_inside_the_unit_tier_budget()
    {
        Corpus.TotalElapsed.TotalSeconds.ShouldBeLessThan(
            BudgetSeconds,
            $"generation {Corpus.GenerationElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"resolution {Corpus.ResolutionElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s, " +
            $"hashing {Corpus.HashingElapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)} s. " +
            "This repository has no integration tier and is not getting one — if 10 000 permutations " +
            "stop fitting the unit tier, the answer is to say so, not to add a tier.");
    }

    /// <summary>
    /// The regeneration command's own output, round-tripped. Also the door itself: with
    /// <c>SIR_M2_17_BASELINE_OUT</c> set, this writes the table.
    /// </summary>
    [Fact]
    public void The_text_the_documented_regeneration_command_writes_is_the_committed_table()
    {
        var destination = Environment.GetEnvironmentVariable(DslDeterminismBaselineWriter.DestinationVariable);
        var reasons = destination is { Length: > 0 }
            ? DslDeterminismBaselineWriter.ReasonsIn(destination)
            : DslDeterminismBaseline.Reasons;

        var rendered = DslDeterminismBaselineWriter.Render(Corpus, reasons);

        if (destination is { Length: > 0 })
        {
            // An exported variable cannot send a render anywhere but the one committed table.
            DslDeterminismBaselineWriter.NamesTheCommittedBaseline(destination).ShouldBeTrue(
                $"{DslDeterminismBaselineWriter.DestinationVariable} is '{destination}', which is not " +
                $"{DslDeterminismBaselineWriter.CanonicalPath}. Regeneration replaces the committed " +
                "table and nothing else — the hand-written reasons are carried over from it.");

            // LF, no BOM — the .gitattributes beside the file pins the same thing for git.
            File.WriteAllBytes(destination, new UTF8Encoding(false).GetBytes(rendered));
        }

        rendered.ShouldNotContain("\r", Case.Sensitive, "the table is LF-only on every platform M5-12 runs on");

        // The whole text, not three values lifted out of the object it was rendered from — an
        // earlier draft compared just aggregate/etc, which cannot fail, leaving the committed file's
        // FORMAT pinned by nothing. The two hand-written regions are normalised out of both sides,
        // because the writer is not their author.
        WithoutHandWrittenRegions(rendered).ShouldBe(
            WithoutHandWrittenRegions(DslDeterminismBaseline.RawText),
            "the committed table is exactly what this build's regeneration command would write, byte " +
            "for byte, apart from the review block and the per-row reasons a human owns");

        using var parsed = JsonDocument.Parse(rendered);
        parsed.RootElement.GetProperty("review").GetProperty("status").GetString().ShouldBe(
            DslDeterminismBaselineWriter.UnreviewedStatus,
            "🔒 every render is unreviewed. The reader refuses that, so regenerating the table " +
            "cannot make a determinism break go green on its own — a human has to say why it moved.");
    }

    /// <summary>
    /// One baseline file with its two hand-written regions blanked: the whole <c>review</c> object,
    /// and every named row's <c>why</c>. What is left is the writer's output and only that.
    /// </summary>
    private static string WithoutHandWrittenRegions(string json) =>
        Regex.Replace(
            Regex.Replace(json, "\"review\": \\{.*?\\n  \\}", "\"review\": {}", RegexOptions.Singleline),
            "\"why\": \"[^\"]*\"",
            "\"why\": \"\"");

    // ══════════════════════════════════════════════════════ theory data and helpers

    /// <summary>Every chunk of the committed table.</summary>
    public static TheoryData<int> ChunkIds()
    {
        var data = new TheoryData<int>();
        for (var chunk = 0; chunk < DslDeterminismBaseline.Chunks.Count; chunk++)
        {
            data.Add(chunk);
        }

        return data;
    }

    /// <summary>Every named row id of the committed table.</summary>
    public static TheoryData<string> NamedIds()
    {
        var data = new TheoryData<string>();
        foreach (var row in DslDeterminismBaseline.Named)
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <summary>Every effect the generator emitted, across the whole corpus.</summary>
    private static IEnumerable<EffectDefinition> EveryEffect() =>
        Corpus.Permutations.SelectMany(permutation => permutation.Effects);

    /// <summary>Every term in a condition tree, the combinators' operands included.</summary>
    private static IEnumerable<ConditionTerm> TermsIn(EffectCondition? condition)
    {
        if (condition is null)
        {
            yield break;
        }

        if (condition.Term is { } term)
        {
            yield return term;
        }

        foreach (var operand in condition.Operands)
        {
            foreach (var nested in TermsIn(operand))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Three comparisons over one vocabulary axis, because two would be circular.</summary>
    /// <param name="emitted">What the generator actually put into the 10 000 permutations.</param>
    /// <param name="declared"><see cref="EffectVocabularyEmissionSets"/>'s hand-written list.</param>
    /// <param name="catalogue">
    /// The independent authority: production's own closed enum. The emitted set is compared against
    /// this, never against <paramref name="declared"/> — comparing the emission set with itself means
    /// deleting an op deletes it from both sides, and the two-directional check stays green.
    /// </param>
    /// <param name="subject">What the tokens are, for the failure message.</param>
    /// <param name="citation">The document sentence being defended.</param>
    private static void Covers<T>(
        IEnumerable<T> emitted,
        IReadOnlyList<T> declared,
        IReadOnlyList<T> catalogue,
        string subject,
        string citation)
        where T : notnull
    {
        BothDirections(emitted, catalogue, subject, citation);

        // And the declared emission set is the production vocabulary too — so a new op cannot be
        // added to the enum and quietly left out of the generator's reach.
        BothDirections(declared, catalogue, $"declared {subject}", citation);
    }

    /// <summary>
    /// One set against the authority, <b>in both directions</b>: nothing missing, nothing invented,
    /// and the two the same size.
    /// </summary>
    private static void BothDirections<T>(
        IEnumerable<T> emitted, IReadOnlyList<T> catalogue, string subject, string citation)
        where T : notnull
    {
        var seen = emitted.ToHashSet();

        catalogue.Where(token => !seen.Contains(token))
            .Select(token => token.ToString()!)
            .ShouldBeEmpty(
                $"these {subject}s are declared and NEVER reach the 10 000-permutation corpus, so " +
                $"nothing in this suite would notice them breaking ({citation})");

        seen.Where(token => !catalogue.Contains(token))
            .Select(token => token.ToString()!)
            .ShouldBeEmpty(
                $"the generator emitted these {subject}s and the catalogue does not declare them " +
                $"({citation})");

        seen.Count.ShouldBe(
            catalogue.Count,
            $"the corpus covers exactly the declared {subject} vocabulary ({citation})");
    }

    /// <summary>One build, resolved and hashed — the two defaulting tests' only difference is the effect.</summary>
    private static string HashOfBuild(EffectDefinition effect)
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("ENEMY_0", 1);
        var sources = EffectSourceSet.Of(ListEffectSource.Synthetic(EffectSourceKind.PERKS, effect));

        return PermutationResolution.Hash(PermutationResolution.Resolve(new BuildPermutation(
            0,
            sources,
            EffectTestBattle.Context(hero, hero, enemy),
            StatFixtures.HeroCurve().At(1),
            StatFixtures.Caps(),
            new List<EffectDefinition> { effect })));
    }

    /// <summary>
    /// Twenty collected entries — five ids, four <c>(source, index)</c> pairs each — in an arrival
    /// order that is neither the answer nor its reverse.
    /// </summary>
    private static IReadOnlyList<CollectedEffect> TiebreakCase()
    {
        var placements = new List<(EffectSourceKind Kind, int Index)>
        {
            (EffectSourceKind.PERKS, 0),
            (EffectSourceKind.GEAR, 1),
            (EffectSourceKind.TALENTS, 0),
            (EffectSourceKind.GEAR, 0),
        };

        var entries = new List<CollectedEffect>();
        foreach (var (kind, indexInSource) in placements)
        {
            foreach (var id in new List<string> { "EFF_C", "EFF_A", "EFF_E", "EFF_B", "EFF_D" })
            {
                entries.Add(new CollectedEffect(
                    new EffectDefinition { Id = id, Op = EffectOp.STAT_ADD_FLAT },
                    EffectInstanceId.Of($"{id}:{kind}:{indexInSource.ToString(CultureInfo.InvariantCulture)}"),
                    kind,
                    indexInSource));
            }
        }

        return entries;
    }

    private static IReadOnlyList<string> Project(IEnumerable<CollectedEffect> sorted) =>
        sorted
            .Select(entry =>
                $"{entry.Effect.Id}/{((int)entry.Source).ToString(CultureInfo.InvariantCulture)}/" +
                entry.IndexInSource.ToString(CultureInfo.InvariantCulture))
            .ToArray();
}
