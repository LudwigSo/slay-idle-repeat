using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// One step-2 survivor, as the hash sees it.
/// </summary>
/// <param name="Id">The effect id — `18` §8's ordering key.</param>
/// <param name="Op">
/// 🔒 The `18` §2 op. Carried because <b>38 of the 44 ops reach nothing else in this record</b>: only
/// §2.1's six stat ops touch <see cref="PermutationOutcome.FinalStats"/>, so without this column a
/// regression that mis-mapped an <c>EffectOp</c> on a <c>DAMAGE</c> or a <c>GRANT_PERK</c> would move
/// no row in the committed table and the baseline would be silently blind to five sixths of the
/// vocabulary it goes to such lengths to emit.
/// </param>
/// <param name="Source">
/// The `18` §8 step-1 source ordinal, 1..10. Half of <see cref="EffectResolutionOrder"/>'s tiebreak.
/// </param>
/// <param name="IndexInSource">The other half.</param>
/// <param name="Trigger">
/// 🔒 The <b>resolved</b> trigger kind, through <see cref="EffectDefaults.TriggerKindOf"/> — so an
/// absent <c>trigger</c> and an authored <c>ALWAYS</c> land on the same number, which is what M2-02's
/// ruling 1 says they are. Encoding the authored nullability instead would make the hash distinguish
/// two builds the resolver cannot.
/// </param>
/// <param name="Target">The resolved target, through <see cref="EffectDefaults.TargetOf"/>, on the same rule.</param>
internal sealed record ResolvedEffectRow(
    string Id, int Op, int Source, int IndexInSource, int Trigger, int Target);

/// <summary>
/// Everything one `18` §8 pass produced, in the shape <c>CanonicalStateWriter</c> encodes.
/// </summary>
/// <param name="Permutation">The permutation ordinal, so a row is self-identifying.</param>
/// <param name="Collected">How many effects step 1 gathered, before the step-2 gate.</param>
/// <remarks>
/// ⚠️ <b>"Everything" means everything `18` §8 produced, not everything the build authored.</b> See
/// <see cref="Active"/>'s note.
/// </remarks>
/// <param name="Active">
/// The step-2 survivors, in `18` §8's resolution order. The order <em>is</em> the claim: a hash over
/// an unordered set would not notice the tiebreak being removed.
/// <para>
/// ⚠️ <b>What this record does and does not carry, said exactly.</b> It carries what `18` §8
/// <em>resolved</em>: which effects survived step 2, in what order, from which source, with which
/// op, trigger and target; plus the 14-stat block steps 3-10 produced. It does <b>not</b> carry an
/// effect's authored magnitude except where a stat op moved a stat — a <c>DAMAGE</c> clause's
/// <c>value</c> reaches no column here, because `18` §8 does not read it either. The ops that do
/// read it belong to M2-08's simulator and M3's run controller, and their determinism is theirs.
/// </para>
/// </param>
/// <param name="GatedOut">The ids step 2 removed, in the same order.</param>
/// <param name="FinalStats">
/// The 14 combat stats after step 10, in <c>StatIds.Combat</c> order — <c>ActorStats.ToSlots()</c>.
/// </param>
/// <param name="PostMultiplierMaxHp">🔒 `05` §4.1's reading: Max HP after step 7, before <c>STAT_SET</c>.</param>
/// <param name="SkippedNonCombatStatEffects">
/// The ids of stat ops naming one of `18` §2.1's 12 non-combat stats. In the hash rather than
/// discarded for the reason M2-07 reports them at all: a build that silently lost every
/// "+X% Gold Gain" affix must not share a hash with one that did not.
/// </param>
internal sealed record PermutationOutcome(
    int Permutation,
    int Collected,
    IReadOnlyList<ResolvedEffectRow> Active,
    IReadOnlyList<string> GatedOut,
    IReadOnlyList<double> FinalStats,
    double PostMultiplierMaxHp,
    IReadOnlyList<string> SkippedNonCombatStatEffects);

/// <summary>One chunk of the corpus: the permutation wire hashes it covers, in ordinal order.</summary>
internal sealed record PermutationChunk(int Chunk, int First, int Last, IReadOnlyList<string> Permutations);

/// <summary>The whole corpus: its chunk wire hashes, in ordinal order.</summary>
internal sealed record PermutationCorpus(int Permutations, int ChunkSize, IReadOnlyList<string> Chunks);

/// <summary>
/// 🔒 The `18` §8 pipeline, composed end to end, and hashed — the subject of M2-17's determinism
/// baseline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pipeline is two calls and one shared gate, and nothing here re-implements a step.</b>
/// M2-02's <see cref="EffectResolver"/> owns steps 1-2, M2-07's <see cref="StatAggregation"/> owns
/// steps 3-10, and the seam between them is <c>ResolvedEffects.ActiveDefinitions</c> plus
/// <c>ResolvedEffects.Gate</c>. Handing the aggregation the <em>same</em> gate is
/// <see cref="EffectResolver"/>'s own requirement — `18` §8 step 2 is asked twice per pass and the
/// two must not be able to answer differently.
/// </para>
/// <para>
/// 🔒 <b>The value reader is <see cref="ScaledEffectValue"/>, not <c>StatAggregationSeams.Strict</c>'s
/// <c>AuthoredEffectValue</c>.</b> The corpus authors `18` §1.1 <c>valueScale</c>s, which the strict
/// default refuses by design (it names M2-06 as their owner). M2-06 landed; the reader that honours
/// them is the one a real pass uses.
/// </para>
/// <para>
/// 🔒 <b>Hashing goes through <see cref="CanonicalStateWriter"/>'s public door and nowhere else.</b>
/// That class forbids a caller assembling its own bytes and calling <c>Fnv1a64</c> — <em>"that caller
/// would be the second serialiser §16.6 exists to forbid"</em> — so the outcome is a canonical record
/// handed to <see cref="CanonicalStateWriter.HashMetaCommandState"/>.
/// ⚠️ <b>That mode is documented as the <c>PlayerSnapshot</c> mode</b>, and this is not a player
/// snapshot. It is used as the repository already uses it: M0-07's own reference-vector suite drives
/// synthetic test-assembly records through it, because the mode's actual contract is <em>"one
/// canonical record root"</em>. §16.6 forbids a second <em>serialiser</em>, not a second caller, and
/// the encoding is driven entirely by the closed allowlist, so a badly shaped root is refused here
/// exactly as a bad snapshot is.
/// </para>
/// <para>
/// 🔒 <b>No fourth named mode should be added for this, and that is a decision rather than a
/// deferral.</b> <c>HashCombatLog</c> became one because <em>production</em> needed a battle
/// <c>LogHash</c> (`05` §7). M2-17's need is test-only: a
/// <c>HashEffectResolutionOutcome</c> would put a determinism-fixture concept into
/// <c>CanonicalStateWriter</c>'s public surface, which is the class whose whole discipline is that
/// its modes are the game's, named and few. Nothing is left open and nobody inherits an obligation.
/// </para>
/// </remarks>
internal static class PermutationResolution
{
    /// <summary>Runs `18` §8 steps 1-10 over one permutation.</summary>
    internal static PermutationOutcome Resolve(BuildPermutation permutation)
    {
        ArgumentNullException.ThrowIfNull(permutation);

        // ── Steps 1-2 · collect from the ten sources in `18` §8 step 1's order, sort into the total
        //    resolution order, filter by condition against current state.
        var resolved = EffectResolver.Resolve(permutation.Sources, permutation.Context);

        // ── Steps 3-10 · group, add, percent, convert, multiply, set, cap, round.
        var aggregated = StatAggregation.Aggregate(
            permutation.BaseStats,
            resolved.ActiveDefinitions,
            permutation.Caps,
            StatAggregationSeams.Strict with
            {
                Conditions = resolved.Gate,
                Values = new ScaledEffectValue(permutation.Context),
            });

        var active = new List<ResolvedEffectRow>(resolved.Active.Count);
        foreach (var entry in resolved.Active)
        {
            active.Add(new ResolvedEffectRow(
                entry.Effect.Id,
                (int)entry.Effect.Op,
                (int)entry.Source,
                entry.IndexInSource,
                (int)EffectDefaults.TriggerKindOf(entry.Effect),
                (int)EffectDefaults.TargetOf(entry.Effect)));
        }

        return new PermutationOutcome(
            permutation.Index,
            resolved.Collected.Count,
            active,
            resolved.GatedOut,
            aggregated.Final.ToSlots(),
            aggregated.PostMultiplierMaxHp,
            aggregated.SkippedNonCombatStatEffects);
    }

    /// <summary>The <c>"fnv1a:"</c> wire hash of one resolution outcome.</summary>
    internal static string Hash(PermutationOutcome outcome) =>
        CanonicalStateWriter.HashMetaCommandState(outcome);

    /// <summary>The wire hash of one chunk of permutation wire hashes.</summary>
    /// <param name="first">
    /// The chunk's first permutation ordinal. Taken rather than recomputed from a chunk index: the
    /// caller already walks the corpus by <c>first</c>, and deriving one from the other and back
    /// again would be two statements of one relationship round-tripped through a division.
    /// </param>
    /// <param name="permutationWires">The chunk's permutation wire hashes, in ordinal order.</param>
    internal static string HashChunk(int first, IReadOnlyList<string> permutationWires)
    {
        ArgumentNullException.ThrowIfNull(permutationWires);

        return CanonicalStateWriter.HashMetaCommandState(new PermutationChunk(
            first / BuildPermutationGenerator.ChunkSize,
            first,
            first + permutationWires.Count - 1,
            permutationWires));
    }

    /// <summary>The wire hash of the whole corpus — the headline row of the committed table.</summary>
    internal static string HashCorpus(IReadOnlyList<string> chunkWires)
    {
        ArgumentNullException.ThrowIfNull(chunkWires);

        return CanonicalStateWriter.HashMetaCommandState(new PermutationCorpus(
            BuildPermutationGenerator.PermutationCount,
            BuildPermutationGenerator.ChunkSize,
            chunkWires));
    }
}
