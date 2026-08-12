using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// One generated build permutation: everything one `18` §8 resolution pass consumes.
/// </summary>
/// <param name="Index">The permutation's ordinal, <c>0 .. PermutationCount - 1</c>.</param>
/// <param name="Sources">`18` §8 step 1's ten sources, as far as this permutation populates them.</param>
/// <param name="Context">The state `18` §4's conditions and `18` §1.1's <c>valueScale</c> read.</param>
/// <param name="BaseStats">`05` §2's hero curve at this permutation's Legend Level.</param>
/// <param name="Caps">`05` §1's six ceilings, before any <c>STAT_CAP_OVERRIDE</c>.</param>
/// <param name="Effects">
/// Every effect the permutation authored, in the order the generator emitted them — <b>not</b>
/// resolution order. Carried so the coverage tests can read the emitted vocabulary back off the
/// generator's actual output rather than off its declared intent.
/// </param>
internal sealed record BuildPermutation(
    int Index,
    EffectSourceSet Sources,
    EffectEvaluationContext Context,
    ActorStats BaseStats,
    StatCaps Caps,
    IReadOnlyList<EffectDefinition> Effects);

/// <summary>
/// 🔒 The deterministic, re-runnable generator behind M2-17's `18` §8 determinism baseline: 10 000
/// seeded random build permutations, each covering the whole DSL vocabulary and every `18` §10.1
/// extension.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seed derivation, stated so a regeneration is reproducible.</b> Permutation <c>i</c> draws from
/// <c>new DeterministicRng(<see cref="BaselineSeed"/> + i, <see cref="RngStreams.Draft"/>)</c>. The
/// base seed is the ASCII of <c>"M217"</c> in the high word, so the corpus names the task that owns
/// it. `14` §8.1's registry is what makes the stream name load-bearing: a name merely spelled
/// differently is a different, silently valid sequence, so the two streams below are named from
/// <see cref="RngStreams"/> and never as literals.
/// </para>
/// <para>
/// 🔒 <b>Two streams, and the split is `14` §8.1's.</b> Composing a build is a <em>draft</em> — which
/// effects a permutation holds — so the generator draws on <see cref="RngStreams.Draft"/>. The
/// context handed to the resolver carries a combat stream,
/// <c>new DeterministicRng(battleSeed, <see cref="RngStreams.Combat"/>)</c> with the battle seed
/// <b>handed in</b>, exactly as `14` §8.1 fixes it. <c>runSeed</c> never enters this layer and is not
/// modelled here. No `18` §4 condition function reads the RNG at all today (only §5's
/// <c>RANDOM_ENEMY</c> does, and target resolution is not on the `18` §8 path) — the stream is
/// supplied anyway, because a context that omitted it would stop being the context a real pass uses
/// the moment one does.
/// </para>
/// <para>
/// 🔒 <b>Every permutation is anchored, then filled.</b> Uniform draws over 44 ops would cover the
/// vocabulary in expectation and not by construction, and "in expectation" is not a property a test
/// can assert. Each permutation therefore emits one <em>anchor</em> per vocabulary axis, rotated by
/// the permutation index — <c>Ops[i % 44]</c>, <c>Triggers[i % 23]</c>, and so on — so 10 000
/// permutations exhaust every axis by arithmetic. The filler effects on top are the random part, and
/// are what makes the corpus a permutation set rather than a rotation.
/// </para>
/// <para>
/// 🔒 <b>Above 16 effects, deliberately.</b> M2-02 found that a naive duplicate-id test cannot
/// detect the removal of <see cref="EffectResolutionOrder"/>'s <c>(source, index)</c> tiebreak,
/// because <c>Collect()</c> normalises order <em>and</em> .NET's introsort is stable at 16 elements
/// or fewer. Every permutation here carries at least <see cref="MinimumAnchorCount"/> anchors plus
/// <c>0 .. <see cref="MaxFiller"/> - 1</c> filler effects, so at least
/// <see cref="MinimumFractionAboveIntrosortThreshold"/> of the corpus exceeds the threshold — and
/// every permutation carries a deliberate duplicate-id pair, placed in two <em>different</em> `18` §8
/// step-1 sources so the tiebreak is the only thing separating them.
/// </para>
/// <para>
/// ⚠️ <b>Its successor is M5-12.</b> This is a committed-baseline determinism test, not the
/// two-runtime parity test `18` §11's last line describes: there is one resolver, in one assembly,
/// and no client build exists until M7, so there is nothing to compare against. Real cross-runtime
/// parity — Linux x64 and Android ARM64, the iOS ARM64 leg authored but gated off with iOS itself
/// (`16` D34) — is <b>M5-12</b>'s, and it re-asserts this corpus's committed table on each of them.
/// </para>
/// <para>
/// ⚠️ <b>Two deliberate departures from a live fight, recorded rather than hidden.</b> <b>(1)</b> The
/// context always carries a run reading, including on the PvP permutations, so that all nine of
/// `18` §4's run functions resolve; whether a duel has a run to read is `11`'s question and not
/// `18` §8's. <b>(2)</b> Generated magnitudes are bounded (see <c>StatEffect</c>) so no
/// permutation can overflow a <c>STAT_MULT</c> product to infinity — <c>StatRounding</c> throws on
/// one, and a corpus that threw would pin nothing.
/// </para>
/// <para>
/// ⚠️ <b>The directory is not a layer claim.</b> These files sit under <c>Rules/Effects/</c> because
/// their subject is `18` §8, but <c>PermutationResolution</c> deliberately names
/// <c>Rules.Stats</c> and <c>Model.Snapshots</c> — the mirror image of the direction R17 forbids in
/// production. That is legitimate only because <c>IntraRulesLayeringRuleTests</c>' subject set is the
/// <c>SlayIdleRepeat.Core</c> module and this is the test assembly, which by design sees every layer
/// at once. Nothing here may move into <c>Core</c> without that composition being reconsidered.
/// </para>
/// </remarks>
internal static class BuildPermutationGenerator
{
    /// <summary>🔒 `18` §11's <em>"10,000 random build permutations"</em>.</summary>
    internal const int PermutationCount = 10_000;

    /// <summary>
    /// How many permutations one committed chunk hash covers. 100 chunks of 100 localise a break to
    /// a hundred permutations without committing ten thousand lines nobody can review.
    /// </summary>
    internal const int ChunkSize = 100;

    /// <summary>
    /// 🔒 The corpus seed: <c>'M' '2' '1' '7'</c> in the high word, zero in the low. Permutation
    /// <c>i</c> uses <c>BaselineSeed + i</c>, so the low word is the permutation ordinal and the
    /// corpus can never collide with another task's seed set.
    /// </summary>
    internal const ulong BaselineSeed = 0x4D32_3137_0000_0000UL;

    /// <summary>The fewest anchor effects any permutation carries before any filler.</summary>
    /// <remarks>
    /// Eight vocabulary anchors, <b>one to three</b> `18` §10.1 extension anchors, two defaulting
    /// anchors and a duplicate-id pair — so 13 at the low end and 15 at the high. The minimum is what
    /// <see cref="MinimumFractionAboveIntrosortThreshold"/> is computed against, because a floor
    /// derived from the best case would not be a floor.
    /// </remarks>
    internal const int MinimumAnchorCount = 13;

    /// <summary>The exclusive upper bound on a permutation's filler effect count.</summary>
    internal const int MaxFiller = 30;

    /// <summary>
    /// 🔒 .NET's introsort is stable at 16 elements or fewer, which is what hid the tiebreak's
    /// removal from M2-02's first attempt.
    /// </summary>
    internal const int IntrosortStabilityThreshold = 16;

    /// <summary>
    /// The documented fraction of the corpus that must exceed
    /// <see cref="IntrosortStabilityThreshold"/> effects.
    /// </summary>
    /// <remarks>
    /// The arithmetic: a permutation holds at least <see cref="MinimumAnchorCount"/> + <c>filler</c>
    /// effects with <c>filler</c> uniform on <c>[0, <see cref="MaxFiller"/>)</c>, so it exceeds 16
    /// whenever <c>filler &gt;= 4</c> — 26 of 30 draws, about 0.867. Floored at 0.75 rather than pinned at the
    /// exact figure: the claim being defended is <em>"most of the corpus is above the threshold"</em>,
    /// and pinning the sampling noise of a particular seed would make this a second baseline with none
    /// of the first one's value.
    /// </remarks>
    internal const double MinimumFractionAboveIntrosortThreshold = 0.75;

    /// <summary>
    /// 🔒 `05` §5's twelve status ids, exactly as that table spells them.
    /// </summary>
    /// <remarks>
    /// ⚠️ An earlier draft of this file invented <c>ST_BURN</c>, <c>ST_CHILL</c> and friends. `05` §5
    /// names no prefix and has no <c>CHILL</c>, so a corpus keyed on those tokens would have pinned
    /// <c>HAS_STATUS</c> and <c>STATUS_STACKS</c> readings over ids the game can never produce, under
    /// a citation the document contradicted (steering S6). These are the document's.
    /// </remarks>
    private static readonly IReadOnlyList<string> StatusIds = new List<string>
    {
        "BURN", "POISON", "BLEED", "FREEZE", "STUN", "WEAKEN",
        "SUNDER", "SPORE", "RAGE", "WARD", "HASTE", "REGEN",
    };

    /// <summary>🔒 `04` §1's <c>DieFaceKind</c>, all six, as that enum spells them.</summary>
    private static readonly IReadOnlyList<string> FaceKinds =
        new List<string> { "Pip", "Star", "Surge", "Fortune", "Void", "Chain" };

    /// <summary>
    /// 🔒 `06` §3's six standard perk categories, as that table spells them — <c>Dice &amp; Board</c>
    /// and <c>Trigger / Synergy</c> with their spaces and punctuation, because
    /// <c>IRunStateView.PerkCount</c> compares them ordinally.
    /// </summary>
    /// <remarks>
    /// `06` §3's seventh, hidden <em>Cursed</em> category is deliberately absent: it is "bonus, not
    /// counted in 90" and appears only from <c>TILE_CURSE</c>, so a corpus that read it would be
    /// asserting a category the standard draft never produces.
    /// </remarks>
    private static readonly IReadOnlyList<string> PerkCategories = new List<string>
    {
        "Offense", "Defense", "Sustain", "Dice & Board", "Economy", "Trigger / Synergy",
    };

    /// <summary>🔒 `05` §6.1's eight enemy archetypes, as that table spells them.</summary>
    private static readonly IReadOnlyList<string> Archetypes = new List<string>
    {
        "GRUNT", "SWARM", "BRUTE", "SKIRMISHER", "WARDEN", "CASTER", "LEECH", "REAVER",
    };

    /// <summary>
    /// ⚠️ <b>Synthetic, and named so.</b> `18` §2.3 and §10.1 E5 give <c>REMOVE_STATUS</c> the
    /// <c>statusTag</c> <b>key</b> and no tag <b>vocabulary</b> — §2.3's own note is only that the tag
    /// labels a <em>status</em> and is not the effect's own <c>tags</c> array.
    /// </summary>
    /// <remarks>
    /// 🔒 Steering S6: the hole stays a hole. These four labels exist so the key reaches the corpus
    /// and are <b>not</b> a claim about what tags the game will author; nothing in this file cites a
    /// document for them. When the tag vocabulary is authored, this list is one of the places that
    /// changes, and the change is a documented regeneration rather than a determinism break.
    /// </remarks>
    private static readonly IReadOnlyList<string> SyntheticStatusTags =
        new List<string> { "SYNTHETIC_TAG_A", "SYNTHETIC_TAG_B", "SYNTHETIC_TAG_C", "SYNTHETIC_TAG_D" };

    /// <summary>`18` §8 step 1's ten sources, in the document's order.</summary>
    private static readonly IReadOnlyList<EffectSourceKind> SourceKinds = new List<EffectSourceKind>
    {
        EffectSourceKind.GEAR,
        EffectSourceKind.AFFIXES,
        EffectSourceKind.SET_BONUSES,
        EffectSourceKind.TALENTS,
        EffectSourceKind.PET_AURAS,
        EffectSourceKind.MOUNT,
        EffectSourceKind.RUN_BUFFS,
        EffectSourceKind.SHRINE_BUFFS,
        EffectSourceKind.CURSES,
        EffectSourceKind.PERKS,
    };

    /// <summary>The id both halves of the duplicate-id anchor carry.</summary>
    internal const string DuplicateAnchorId = "EFF_DUP";

    /// <summary>🔒 The seed of permutation <paramref name="permutation"/>. The whole derivation.</summary>
    internal static ulong SeedFor(int permutation) => BaselineSeed + (ulong)permutation;

    /// <summary>The whole corpus, in ordinal order.</summary>
    internal static IReadOnlyList<BuildPermutation> Corpus()
    {
        var corpus = new List<BuildPermutation>(PermutationCount);
        for (var index = 0; index < PermutationCount; index++)
        {
            corpus.Add(Permutation(index));
        }

        return corpus;
    }

    /// <summary>One permutation, a pure function of its index.</summary>
    internal static BuildPermutation Permutation(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var rng = new DeterministicRng(SeedFor(index), RngStreams.Draft);
        var context = ContextFor(index, rng);
        var placed = new List<(EffectDefinition Effect, EffectSourceKind Kind)>();

        // ── The vocabulary anchors. One per axis, rotated by the index, so 10 000 permutations
        //    exhaust 44 / 23 / 23 / 11 / 26 / 6 / 5 / 8 by arithmetic rather than in expectation.
        Place(placed, rng, WellFormed(EffectVocabularyEmissionSets.Ops[index % EffectVocabularyEmissionSets.Ops.Count], rng, "EFF_ANCHOR_OP"));
        Place(placed, rng, TriggerAnchor(index, rng));
        Place(placed, rng, ConditionAnchor(index, rng));
        Place(placed, rng, TargetAnchor(index));
        Place(placed, rng, StatAnchor(index, rng));
        Place(placed, rng, DurationAnchor(index, rng));
        Place(placed, rng, StackingAnchor(index, rng));
        Place(placed, rng, ValueModeAnchor(index, rng));

        // ── R18 · `18` §10.1's extensions, rotated so every row reaches the corpus.
        foreach (var extension in ExtensionAnchors(index, rng))
        {
            Place(placed, rng, extension);
        }

        // ── M2-02's two defaulting rulings, both shapes in every permutation.
        Place(placed, rng, new EffectDefinition
        {
            Id = "EFF_ANCHOR_ABSENT",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = Rounded(rng, 1.0, 9.0),
        });
        Place(placed, rng, new EffectDefinition
        {
            Id = "EFF_ANCHOR_AUTHORED",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = Rounded(rng, 1.0, 9.0),
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
        });

        // ── The duplicate-id pair, in two DIFFERENT step-1 sources so that
        //    EffectResolutionOrder's (source, index) tiebreak is the only thing that separates
        //    them — and both STAT_SET on one stat, so step 8's "last writer wins" carries the
        //    answer into the stat block and therefore into the hash.
        var duplicateStat = StatIds.Combat[index % StatIds.Combat.Count];
        placed.Add((
            new EffectDefinition
            {
                Id = DuplicateAnchorId,
                Op = EffectOp.STAT_SET,
                Stat = StatSelector.Of(duplicateStat),
                Value = Rounded(rng, 11.0, 99.0),
                ValueMode = ValueMode.FLAT,
            },
            EffectSourceKind.GEAR));
        placed.Add((
            new EffectDefinition
            {
                Id = DuplicateAnchorId,
                Op = EffectOp.STAT_SET,
                Stat = StatSelector.Of(duplicateStat),
                Value = Rounded(rng, 101.0, 199.0),
                ValueMode = ValueMode.FLAT,
            },
            EffectSourceKind.PERKS));

        // ── The random part.
        var filler = rng.Range(0, MaxFiller);
        for (var i = 0; i < filler; i++)
        {
            var op = EffectVocabularyEmissionSets.Ops[rng.Range(0, EffectVocabularyEmissionSets.Ops.Count)];
            var effect = WellFormed(op, rng, FillerId(rng));

            // A third of the filler carries a trigger, a condition and a target — enough that the
            // step-2 gate has real work to do on most permutations without gating the build empty.
            if (rng.Range(0, 3) == 0)
            {
                effect = effect with
                {
                    Trigger = new EffectTrigger
                    {
                        Kind = EffectVocabularyEmissionSets.Triggers[rng.Range(0, EffectVocabularyEmissionSets.Triggers.Count)],
                    },
                    Condition = ConditionTree(
                        EffectVocabularyEmissionSets.Conditions[rng.Range(0, EffectVocabularyEmissionSets.Conditions.Count)],
                        rng),
                    Target = EffectVocabularyEmissionSets.Targets[rng.Range(0, EffectVocabularyEmissionSets.Targets.Count)],
                };
            }

            Place(placed, rng, effect);
        }

        return new BuildPermutation(
            index,
            SourceSetOf(placed),
            context,
            StatFixtures.HeroCurve().At(1 + (index % 200)),
            StatFixtures.Caps(),
            placed.Select(entry => entry.Effect).ToArray());
    }

    /// <summary>
    /// Which `18` §10.1 extension keys one generated effect carries — read off the effect, so the
    /// coverage test observes what the generator emitted rather than what it meant to emit.
    /// </summary>
    internal static IEnumerable<string> ExtensionKeysIn(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.Op == EffectOp.STAT_CONVERT && effect.ToStat is not null)
        {
            yield return EffectVocabularyEmissionSets.ToStatOnStatConvert;
        }

        if (effect.Op == EffectOp.STAT_CAP_OVERRIDE && effect.CapKind == StatCapKind.STAT_MAX)
        {
            yield return EffectVocabularyEmissionSets.CapKindStatMax;
        }

        if (effect.Op == EffectOp.STAT_CAP_OVERRIDE && effect.CapKind == StatCapKind.REDIRECT_EXCESS &&
            effect.ToStat is not null)
        {
            yield return EffectVocabularyEmissionSets.CapKindRedirectExcess;
        }

        if (effect.Op is EffectOp.ATTACK_MULT_NEXT or EffectOp.FORCE_CRIT_NEXT && effect.Charges is not null)
        {
            yield return EffectVocabularyEmissionSets.ChargesOnNextAttackOps;
        }

        if (effect.Op == EffectOp.SURVIVE_LETHAL && effect.ValueMode is not null)
        {
            yield return EffectVocabularyEmissionSets.ValueModeOnSurviveLethal;
        }

        if (effect.Op == EffectOp.REMOVE_STATUS && effect.StatusTag is not null)
        {
            yield return EffectVocabularyEmissionSets.StatusTagOnRemoveStatus;
        }

        if (effect.Trigger is { Kind: TriggerKind.ON_ATTACK, Chance: not null })
        {
            yield return EffectVocabularyEmissionSets.ChanceOnOnAttack;
        }

        if (effect.ValueScale?.StatusId is not null)
        {
            yield return EffectVocabularyEmissionSets.ValueScaleStatusId;
        }

        if (effect.ValueScale?.FaceKind is not null)
        {
            yield return EffectVocabularyEmissionSets.ValueScaleFaceKind;
        }

        if (effect.ValueScale?.Category is not null)
        {
            yield return EffectVocabularyEmissionSets.ValueScaleCategory;
        }
    }

    /// <summary>Every `18` §4 function named anywhere in a condition tree, including the operands.</summary>
    internal static IEnumerable<ConditionFunction> FunctionsIn(EffectCondition? condition)
    {
        if (condition is null)
        {
            yield break;
        }

        if (condition.Term is { } term)
        {
            yield return term.Fn;
        }

        foreach (var operand in condition.Operands)
        {
            foreach (var function in FunctionsIn(operand))
            {
                yield return function;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════ the anchors

    private static EffectDefinition TriggerAnchor(int index, DeterministicRng rng)
    {
        var kind = EffectVocabularyEmissionSets.Triggers[index % EffectVocabularyEmissionSets.Triggers.Count];

        return new EffectDefinition
        {
            Id = "EFF_ANCHOR_TRG",
            Op = EffectOp.DAMAGE,
            Value = Rounded(rng, 0.5, 3.0),

            // 🔴 R11 — `ON_ATTACK` gained `chance` under `18` §10's procedure and `ON_KILL` did not,
            //    so the key goes on exactly the one trigger that owns it.
            Trigger = kind == TriggerKind.ON_ATTACK
                ? new EffectTrigger { Kind = kind, Chance = Rounded(rng, 0.05, 0.95) }
                : new EffectTrigger { Kind = kind },
        };
    }

    private static EffectDefinition ConditionAnchor(int index, DeterministicRng rng)
    {
        var function = EffectVocabularyEmissionSets.Conditions[index % EffectVocabularyEmissionSets.Conditions.Count];

        return new EffectDefinition
        {
            Id = "EFF_ANCHOR_CND",
            Op = EffectOp.HEAL,
            Value = Rounded(rng, 1.0, 40.0),
            Condition = ConditionTree(function, rng),
        };
    }

    private static EffectDefinition TargetAnchor(int index) =>
        new()
        {
            Id = "EFF_ANCHOR_TGT",
            Op = EffectOp.APPLY_STATUS,
            StatusId = StatusIds[index % StatusIds.Count],
            Target = EffectVocabularyEmissionSets.Targets[index % EffectVocabularyEmissionSets.Targets.Count],
        };

    private static EffectDefinition StatAnchor(int index, DeterministicRng rng) =>
        new()
        {
            Id = "EFF_ANCHOR_STA",
            Op = EffectOp.STAT_ADD_FLAT,

            // All 26, so the 12 non-combat stats reach AggregatedStats.SkippedNonCombatStatEffects
            // and therefore the hash — a build that silently lost every "+X% Gold Gain" affix would
            // move the baseline.
            Stat = StatSelector.Of(EffectVocabularyEmissionSets.Stats[index % EffectVocabularyEmissionSets.Stats.Count]),
            Value = Rounded(rng, -5.0, 40.0),
        };

    private static EffectDefinition DurationAnchor(int index, DeterministicRng rng)
    {
        var scope = EffectVocabularyEmissionSets.DurationScopes[index % EffectVocabularyEmissionSets.DurationScopes.Count];

        return new EffectDefinition
        {
            Id = "EFF_ANCHOR_DUR",
            Op = EffectOp.SHIELD,
            Value = Rounded(rng, 5.0, 50.0),
            Duration = scope == DurationScope.BATTLE
                ? new EffectDuration { Scope = scope, Seconds = Rounded(rng, 1.0, 12.0) }
                : new EffectDuration { Scope = scope },
        };
    }

    private static EffectDefinition StackingAnchor(int index, DeterministicRng rng)
    {
        var mode = EffectVocabularyEmissionSets.StackingModes[index % EffectVocabularyEmissionSets.StackingModes.Count];

        return new EffectDefinition
        {
            Id = "EFF_ANCHOR_STK",
            Op = EffectOp.APPLY_STATUS,
            StatusId = StatusIds[index % StatusIds.Count],
            Stacking = new EffectStacking
            {
                Mode = mode,
                MaxStacks = mode == StackingMode.NONE ? null : 1 + rng.Range(0, 5),
                RefreshOnReapply = rng.Range(0, 2) == 0,
            },
        };
    }

    private static EffectDefinition ValueModeAnchor(int index, DeterministicRng rng) =>
        new()
        {
            // 🔒 `18` §10.1 E4's op, and the only place a valueMode is anchored: `ScaledEffectValue`
            //    refuses a non-FLAT mode on a STAT-family op, on M2-07's ruling, so a valueMode
            //    rotation over a stat op would throw rather than pin anything.
            Id = "EFF_ANCHOR_VMD",
            Op = EffectOp.SURVIVE_LETHAL,
            Value = Rounded(rng, 0.05, 0.95),
            ValueMode = EffectVocabularyEmissionSets.ValueModes[index % EffectVocabularyEmissionSets.ValueModes.Count],
        };

    /// <summary>
    /// 🔴 R18 — one `18` §10.1 extension row per permutation, rotated so all ten keys reach the
    /// corpus. The rotation is over seven rows because E2's two <c>capKind</c>s and M2-06's three
    /// <c>valueScale</c> arguments each land together.
    /// </summary>
    private static IReadOnlyList<EffectDefinition> ExtensionAnchors(int index, DeterministicRng rng)
    {
        switch (index % 7)
        {
            case 0:
                // E1 · toStat, the destination `18` §2.1's one `stat` key could not name.
                var (from, to) = TwoDistinctCombatStats(rng);
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_E1",
                        Op = EffectOp.STAT_CONVERT,
                        Stat = StatSelector.Of(from),
                        ToStat = to,
                        Value = Rounded(rng, 0.05, 0.50),
                    },
                };

            case 1:
                // E2 · both halves of "raise OR REDIRECT a stat cap", in one permutation.
                var (capped, spill) = TwoDistinctCombatStats(rng);
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_E2_MAX",
                        Op = EffectOp.STAT_CAP_OVERRIDE,
                        Stat = StatSelector.Of(StatId.CRIT),
                        CapKind = StatCapKind.STAT_MAX,
                        Value = Rounded(rng, 0.10, 0.95),
                    },
                    new()
                    {
                        Id = "EFF_EXT_E2_RDR",
                        Op = EffectOp.STAT_CAP_OVERRIDE,
                        Stat = StatSelector.Of(capped),
                        ToStat = spill,
                        CapKind = StatCapKind.REDIRECT_EXCESS,
                        Value = Rounded(rng, 1.0, 4.0),
                    },
                    new()
                    {
                        Id = "EFF_EXT_E2_HEA",
                        Op = EffectOp.STAT_CAP_OVERRIDE,
                        Stat = StatSelector.Of(StatId.MAX_HP),
                        CapKind = StatCapKind.HEAL_CEILING,
                        Value = Rounded(rng, 0.10, 1.0),
                    },
                };

            case 2:
                // E3 · the N of "the next N attacks", on both ops that needed it.
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_E3_AMN",
                        Op = EffectOp.ATTACK_MULT_NEXT,
                        Value = Rounded(rng, 1.2, 3.0),
                        Charges = 1 + rng.Range(0, 4),
                    },
                    new()
                    {
                        // 🔒 No `value`: `18` §2.4's FORCE_CRIT_NEXT is a certainty, not a magnitude.
                        Id = "EFF_EXT_E3_FCN",
                        Op = EffectOp.FORCE_CRIT_NEXT,
                        Charges = 1 + rng.Range(0, 4),
                    },
                };

            case 3:
                // E4 · which unit SURVIVE_LETHAL's value is in.
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_E4",
                        Op = EffectOp.SURVIVE_LETHAL,
                        Value = Rounded(rng, 0.05, 1.0),
                        ValueMode = rng.Range(0, 2) == 0 ? ValueMode.FLAT : ValueMode.SELF_MAXHP_PCT,
                    },
                };

            case 4:
                // E5 · the tag group `18` §2.3 offered and named no key for.
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_E5",
                        Op = EffectOp.REMOVE_STATUS,
                        StatusTag = new StatusTag(SyntheticStatusTags[rng.Range(0, SyntheticStatusTags.Count)]),
                    },
                };

            case 5:
                // R11 · `chance` on ON_ATTACK, and on no other trigger.
                return new List<EffectDefinition>
                {
                    new()
                    {
                        Id = "EFF_EXT_R11",
                        Op = EffectOp.DAMAGE,
                        Value = Rounded(rng, 0.5, 2.5),
                        Trigger = new EffectTrigger
                        {
                            Kind = TriggerKind.ON_ATTACK,
                            Chance = Rounded(rng, 0.05, 0.95),
                        },
                    },
                };

            default:
                // M2-06 · valueScale's three argument keys, on the three functions that need them.
                // 🔒 Attached to STAT_ADD_FLAT so ScaledEffectValue actually reads them: on a
                //    non-stat op the scale would be authored and never evaluated, which pins nothing.
                return new List<EffectDefinition>
                {
                    ScaledFlat("EFF_EXT_M206_STS", rng, new ValueScale
                    {
                        Fn = ConditionFunction.STATUS_STACKS,
                        Per = 1.0,
                        Cap = 4,
                        StatusId = StatusIds[rng.Range(0, StatusIds.Count)],
                    }),
                    ScaledFlat("EFF_EXT_M206_FAC", rng, new ValueScale
                    {
                        Fn = ConditionFunction.DIE_FACE_COUNT,
                        Per = 1.0,
                        Cap = 4,
                        FaceKind = FaceKinds[rng.Range(0, FaceKinds.Count)],
                    }),
                    ScaledFlat("EFF_EXT_M206_CAT", rng, new ValueScale
                    {
                        Fn = ConditionFunction.PERK_COUNT,
                        Per = 1.0,
                        Cap = 4,
                        Category = PerkCategories[rng.Range(0, PerkCategories.Count)],
                    }),
                };
        }
    }

    private static EffectDefinition ScaledFlat(string id, DeterministicRng rng, ValueScale scale) =>
        new()
        {
            Id = id,
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatIds.Combat[rng.Range(0, StatIds.Combat.Count)]),
            Value = Rounded(rng, 1.0, 20.0),
            ValueScale = scale,
        };

    // ══════════════════════════════════════════════════════════════ effect shapes

    /// <summary>
    /// An effect of the given op carrying exactly the keys that op needs to survive a
    /// <c>Resolve</c> + <c>Aggregate</c> pass.
    /// </summary>
    /// <remarks>
    /// `18` §2.1's six stat ops are the only ones the aggregation reads, so they are the only ones
    /// whose keys matter here. Everything else is collected, gated and then ignored by steps 3-10 —
    /// which is `18` §8's design, not a shortcut: an <c>ON_HIT</c> <c>DAMAGE</c> clause changes no
    /// stat.
    /// </remarks>
    private static EffectDefinition WellFormed(EffectOp op, DeterministicRng rng, string id) =>
        EffectOps.FamilyOf(op) == EffectOpFamily.STAT
            ? StatEffect(op, rng, id)
            : NonStatEffect(op, rng, id);

    private static EffectDefinition StatEffect(EffectOp op, DeterministicRng rng, string id)
    {
        switch (op)
        {
            case EffectOp.STAT_ADD_FLAT:
                return new EffectDefinition
                {
                    Id = id,
                    Op = op,
                    Stat = AnySelector(rng),
                    Value = Rounded(rng, -25.0, 250.0),
                    ValueScale = MaybeScale(rng),
                };

            case EffectOp.STAT_ADD_PCT:
                return new EffectDefinition
                {
                    Id = id,
                    Op = op,
                    Stat = AnySelector(rng),

                    // Bounded above 0: `18` §7.10's negative percents are M2-03's subject, and a
                    // corpus whose Σ pct could pass -1 would flip stat signs for reasons that have
                    // nothing to do with resolution order.
                    Value = Rounded(rng, 0.0, 0.50),
                    ValueScale = MaybeScale(rng),
                };

            case EffectOp.STAT_MULT:
                return new EffectDefinition
                {
                    Id = id,
                    Op = op,
                    Stat = AnySelector(rng),

                    // 🔒 `05` §1.1's erratum: the product is of the VALUES, so a factor near 1 is a
                    //    small change. Bounded to [0.5, 1.5] so 29 of them cannot overflow to
                    //    infinity — StatRounding throws on one, and a corpus that threw pins nothing.
                    Value = Rounded(rng, 0.5, 1.5),
                };

            case EffectOp.STAT_SET:
                return new EffectDefinition
                {
                    Id = id,
                    Op = op,
                    Stat = AnySelector(rng),
                    Value = Rounded(rng, 1.0, 500.0),

                    // `18` §9.1's CP_GLASS_HEART is the one stat op in the document that carries a
                    // valueMode, and it is FLAT.
                    ValueMode = ValueMode.FLAT,
                };

            case EffectOp.STAT_CONVERT:
                var (from, to) = TwoDistinctCombatStats(rng);
                return new EffectDefinition
                {
                    Id = id,
                    Op = op,
                    Stat = StatSelector.Of(from),
                    ToStat = to,
                    Value = Rounded(rng, 0.05, 0.50),
                };

            default:
                return CapOverride(rng, id);
        }
    }

    private static EffectDefinition CapOverride(DeterministicRng rng, string id)
    {
        switch (rng.Range(0, 3))
        {
            case 0:
                return new EffectDefinition
                {
                    Id = id,
                    Op = EffectOp.STAT_CAP_OVERRIDE,
                    Stat = StatSelector.Of(StatIds.Combat[rng.Range(0, StatIds.Combat.Count)]),
                    CapKind = StatCapKind.STAT_MAX,
                    Value = Rounded(rng, 0.10, 400.0),
                };

            case 1:
                var (capped, spill) = TwoDistinctCombatStats(rng);
                return new EffectDefinition
                {
                    Id = id,
                    Op = EffectOp.STAT_CAP_OVERRIDE,
                    Stat = StatSelector.Of(capped),
                    ToStat = spill,
                    CapKind = StatCapKind.REDIRECT_EXCESS,
                    Value = Rounded(rng, 1.0, 4.0),
                };

            default:
                return new EffectDefinition
                {
                    Id = id,
                    Op = EffectOp.STAT_CAP_OVERRIDE,
                    Stat = StatSelector.Of(StatId.MAX_HP),
                    CapKind = StatCapKind.HEAL_CEILING,
                    Value = Rounded(rng, 0.10, 1.0),
                };
        }
    }

    /// <summary>
    /// A non-stat effect, carrying whichever of `18` §2.2-§2.5's op-specific keys it owns.
    /// </summary>
    /// <remarks>
    /// None of these keys is read by <c>Resolve</c> or <c>Aggregate</c> — the ops that consume them
    /// belong to the combat simulator (M2-08) and the run controller (M3). They are authored anyway:
    /// the hash is over the resolution outcome, and an effect that carries them is the effect the
    /// resolver will actually be handed.
    /// </remarks>
    private static EffectDefinition NonStatEffect(EffectOp op, DeterministicRng rng, string id)
    {
        var effect = new EffectDefinition { Id = id, Op = op, Value = Rounded(rng, 0.05, 3.0) };

        return op switch
        {
            EffectOp.APPLY_STATUS or EffectOp.EXTEND_STATUS or EffectOp.IMMUNE_STATUS =>
                effect with { StatusId = StatusIds[rng.Range(0, StatusIds.Count)] },

            EffectOp.REMOVE_STATUS => rng.Range(0, 2) == 0
                ? effect with { Value = null, StatusId = StatusIds[rng.Range(0, StatusIds.Count)] }
                : effect with { Value = null, StatusTag = new StatusTag(SyntheticStatusTags[rng.Range(0, SyntheticStatusTags.Count)]) },

            EffectOp.ATTACK_MULT_NEXT => effect with { Charges = 1 + rng.Range(0, 4) },

            EffectOp.FORCE_CRIT_NEXT => effect with { Value = null, Charges = 1 + rng.Range(0, 4) },

            EffectOp.SURVIVE_LETHAL or EffectOp.REVIVE =>
                effect with { Value = Rounded(rng, 0.05, 1.0), ValueMode = ValueMode.SELF_MAXHP_PCT },

            EffectOp.SUMMON => effect with
            {
                Value = null,
                Archetype = Archetypes[rng.Range(0, Archetypes.Count)],
                MaxAlive = 1 + rng.Range(0, 4),
            },

            // 🔒 `18` §10.1 E6 — the table IS the op, and it carries no value. Two rows because one
            //    outcome is not a choice, and both weights positive because `14` §8.0's walk has no
            //    row to pick otherwise: an emitted effect EffectOpValidation would refuse is an
            //    effect the resolver will never actually be handed.
            EffectOp.RANDOM_OUTCOME => effect with
            {
                Value = null,
                Outcomes = new[]
                {
                    new RandomOutcomeEntry($"{id}_OUTCOME_A", 1 + rng.Range(0, 4)),
                    new RandomOutcomeEntry($"{id}_OUTCOME_B", 1 + rng.Range(0, 4)),
                },
            },

            EffectOp.STAT_COPY => effect with
            {
                Stat = rng.Range(0, 2) == 0
                    ? StatSelector.Of(StatIds.Combat[rng.Range(0, StatIds.Combat.Count)])
                    : StatSelector.HighestPctBonus,
                SourceCapPct = Rounded(rng, 0.10, 1.0),
            },

            EffectOp.MODIFY_DIE_FACE => effect with
            {
                Value = null,
                FaceIndex = rng.Range(0, 2) == 0 ? DieFaceIndex.At(1 + rng.Range(0, 6)) : DieFaceIndex.PlayerChoice,
                NewFace = new DieFaceSpec(FaceKinds[rng.Range(0, FaceKinds.Count)]),
                Scope = DieFaceScope.NEXT_3_ROLLS,
            },

            _ => effect,
        };
    }

    // ══════════════════════════════════════════════════════════════ conditions

    private static EffectCondition ConditionTree(ConditionFunction function, DeterministicRng rng)
    {
        var term = EffectCondition.Of(TermFor(function, rng));

        // The four `18` §4 combinator kinds, so TERM / ALL / ANY / NOT all reach the corpus.
        return rng.Range(0, 4) switch
        {
            0 => term,
            1 => EffectCondition.All(term, EffectCondition.Of(TermFor(ConditionFunction.SELF_HP_PCT, rng))),
            2 => EffectCondition.Any(term, EffectCondition.Of(TermFor(ConditionFunction.IS_PVP, rng))),
            _ => EffectCondition.Not(term),
        };
    }

    private static ConditionTerm TermFor(ConditionFunction function, DeterministicRng rng)
    {
        var statusId = function is ConditionFunction.HAS_STATUS or ConditionFunction.STATUS_STACKS
            ? StatusIds[rng.Range(0, StatusIds.Count)]
            : null;
        var faceKind = function == ConditionFunction.DIE_FACE_COUNT
            ? FaceKinds[rng.Range(0, FaceKinds.Count)]
            : null;

        // 🔒 `PERK_COUNT`'s category is documented as OPTIONAL — a null category reads the whole
        //    perk table — so both readings are emitted rather than only the one with the key.
        var category = function == ConditionFunction.PERK_COUNT && rng.Range(0, 2) == 0
            ? PerkCategories[rng.Range(0, PerkCategories.Count)]
            : null;

        switch (rng.Range(0, 3))
        {
            case 0:
                var comparators = new List<ConditionComparator>
                {
                    ConditionComparator.EQ,
                    ConditionComparator.NEQ,
                    ConditionComparator.LT,
                    ConditionComparator.LTE,
                    ConditionComparator.GT,
                    ConditionComparator.GTE,
                };
                return new ConditionTerm
                {
                    Fn = function,
                    Comparator = comparators[rng.Range(0, comparators.Count)],
                    Value = Rounded(rng, 0.0, 4.0),
                    StatusId = statusId,
                    Category = category,
                    FaceKind = faceKind,
                };

            case 1:
                var low = Rounded(rng, 0.0, 2.0);
                return new ConditionTerm
                {
                    Fn = function,
                    Comparator = ConditionComparator.BETWEEN,
                    RangeLow = low,
                    RangeHigh = DeterminismRounding.Round(low + (rng.NextDouble() * 3.0)),
                    StatusId = statusId,
                    Category = category,
                    FaceKind = faceKind,
                };

            default:
                return new ConditionTerm
                {
                    Fn = function,
                    Comparator = rng.Range(0, 2) == 0 ? ConditionComparator.EQ : ConditionComparator.NEQ,
                    Flag = rng.Range(0, 2) == 0,
                    StatusId = statusId,
                    Category = category,
                    FaceKind = faceKind,
                };
        }
    }

    // ══════════════════════════════════════════════════════════════ context and sources

    private static EffectEvaluationContext ContextFor(int index, DeterministicRng rng)
    {
        var isPvp = index % 5 == 0;

        var hero = EffectTestBattle.Hero(
            currentHp: Rounded(rng, 1.0, 500.0),
            maxHp: Rounded(rng, 500.0, 2000.0)) with
        {
            Statuses = StatusIds
                .Take(1 + (index % StatusIds.Count))
                .ToDictionary(id => id, id => 1 + (id.Length % 4), StringComparer.Ordinal),
        };

        var actors = new List<IEffectActorView> { hero };
        var enemyCount = 1 + (index % 5);
        for (var i = 0; i < enemyCount; i++)
        {
            actors.Add(EffectTestBattle.Enemy(
                $"ENEMY_{i.ToString(CultureInfo.InvariantCulture)}",
                i + 1,
                currentHp: Rounded(rng, 1.0, 400.0),
                maxHp: Rounded(rng, 400.0, 1200.0)) with
            {
                IsElite = (index + i) % 3 == 0,
                IsBoss = (index + i) % 7 == 0,
                IsSummon = (index + i) % 4 == 0,
            });
        }

        return new EffectEvaluationContext
        {
            Holder = hero,

            // 🔒 Always present. `TARGET_HP_PCT` throws without one, in PvP too, and a corpus that
            //    could not evaluate a third of `18` §4 would cover a third less of `18` §8 step 2.
            CurrentTarget = actors[1],
            Attacker = actors[actors.Count - 1],
            Actors = actors,
            BattleTimeSeconds = Rounded(rng, 0.0, 89.0),
            EnrageAtSeconds = index % 3 == 0 ? EffectTestBattle.EnrageSeconds : null,
            FightHorizonSeconds = isPvp ? EffectTestBattle.PvpTimeoutSeconds : EffectTestBattle.PveTimeoutSeconds,
            IsPvp = isPvp,

            // ⚠️ Present even in PvP — see the type remarks. `18` §8 step 2 is the subject; whether a
            //    duel carries a run reading is `11`'s.
            Run = new RunStateReading
            {
                StageIndex = 1 + (index % 3),
                Chapter = 1 + (index % 5),
                Tier = index % 4,
                PetCount = index % 4,
                GoldHeld = (index * 37) % 5000,
                BattlesWonThisRun = index % 12,
                PerksByCategory = PerkCategories
                    .Take(1 + (index % PerkCategories.Count))
                    .ToDictionary(category => category, category => (index + category.Length) % 6, StringComparer.Ordinal),
                DieFacesByKind = FaceKinds
                    .Take(1 + (index % FaceKinds.Count))
                    .ToDictionary(kind => kind, kind => (index + kind.Length) % 7, StringComparer.Ordinal),
            },

            // 🔒 `14` §8.1's combat stream, with the battle seed HANDED IN. `runSeed` never enters
            //    this layer.
            Rng = EffectTestBattle.CombatRng(SeedFor(index)),
        };
    }

    private static void Place(
        List<(EffectDefinition Effect, EffectSourceKind Kind)> placed,
        DeterministicRng rng,
        EffectDefinition effect) =>
        placed.Add((effect, SourceKinds[rng.Range(0, SourceKinds.Count)]));

    private static EffectSourceSet SourceSetOf(IReadOnlyList<(EffectDefinition Effect, EffectSourceKind Kind)> placed)
    {
        var sources = new List<IEffectSource>();

        // Walked in `18` §8 step 1's order rather than in the order the kinds were drawn, so that
        // EffectSourceSet.Of never sees two sources claiming one slot.
        foreach (var kind in SourceKinds)
        {
            var owned = placed.Where(entry => entry.Kind == kind).Select(entry => entry.Effect).ToArray();
            if (owned.Length > 0)
            {
                sources.Add(ListEffectSource.Synthetic(kind, owned));
            }
        }

        return EffectSourceSet.Of(sources.ToArray());
    }

    /// <summary>
    /// Two <b>different</b> combat stats — what <c>STAT_CONVERT</c> and a <c>REDIRECT_EXCESS</c>
    /// <c>STAT_CAP_OVERRIDE</c> both require.
    /// </summary>
    /// <remarks>
    /// M2-03 refuses <c>from == to</c> on both ops ("converts X into itself", "sends X's overshoot
    /// back into X"), so the offset is drawn on <c>[1, 14)</c> and wrapped rather than redrawn: a
    /// rejection loop would make the number of RNG draws depend on the draws themselves, and the
    /// corpus would stop being a function of its seed.
    /// </remarks>
    private static (StatId From, StatId To) TwoDistinctCombatStats(DeterministicRng rng)
    {
        var count = StatIds.Combat.Count;
        var first = rng.Range(0, count);
        var second = (first + 1 + rng.Range(0, count - 1)) % count;

        return (StatIds.Combat[first], StatIds.Combat[second]);
    }

    private static StatSelector AnySelector(DeterministicRng rng) =>
        rng.Range(0, 6) == 0
            ? StatSelector.AllCombat
            : StatSelector.Of(EffectVocabularyEmissionSets.Stats[rng.Range(0, EffectVocabularyEmissionSets.Stats.Count)]);

    /// <summary>
    /// A <c>valueScale</c> on one effect in three — always capped, so <c>value × steps</c> stays
    /// bounded whatever the reading is.
    /// </summary>
    private static ValueScale? MaybeScale(DeterministicRng rng)
    {
        if (rng.Range(0, 3) != 0)
        {
            return null;
        }

        var function = EffectVocabularyEmissionSets.Conditions[rng.Range(0, EffectVocabularyEmissionSets.Conditions.Count)];

        return new ValueScale
        {
            Fn = function,
            Per = Rounded(rng, 0.5, 20.0),
            Cap = rng.Range(0, 5),
            StatusId = function is ConditionFunction.HAS_STATUS or ConditionFunction.STATUS_STACKS
                ? StatusIds[rng.Range(0, StatusIds.Count)]
                : null,
            FaceKind = function == ConditionFunction.DIE_FACE_COUNT
                ? FaceKinds[rng.Range(0, FaceKinds.Count)]
                : null,
            Category = function == ConditionFunction.PERK_COUNT
                ? PerkCategories[rng.Range(0, PerkCategories.Count)]
                : null,
        };
    }

    /// <summary>
    /// A filler id drawn from a pool narrow enough that collisions are the norm — `18` §8's
    /// duplicate-id tiebreak is not an edge case in a real build, where one authored affix arrives
    /// from two gear slots.
    /// </summary>
    private static string FillerId(DeterministicRng rng) =>
        "EFF_" + rng.Range(0, 12).ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>
    /// 🔒 A magnitude, rounded through <c>Primitives.DeterminismRounding</c> — the repository's one
    /// statement of `05` §1.1's 4-dp rule. Nothing here restates <c>Math.Round(x, 4)</c>.
    /// </summary>
    private static double Rounded(DeterministicRng rng, double low, double high) =>
        DeterminismRounding.Round(low + (rng.NextDouble() * (high - low)));
}
