using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>One step-2 survivor, as the hash sees it.</summary>
/// <param name="Id">The effect id — the ordering key.</param>
/// <param name="Op">
/// The effect op. Carried because 38 of the 44 ops reach nothing else in this record: only the six stat
/// ops touch <see cref="PermutationOutcome.FinalStats"/>, so without this column a mis-mapped
/// <c>EffectOp</c> on a <c>DAMAGE</c> or <c>GRANT_PERK</c> would move no row and the baseline would be
/// blind to five sixths of the vocabulary.
/// </param>
/// <param name="Source">The source ordinal, 1..10. Half of the tiebreak.</param>
/// <param name="IndexInSource">The other half.</param>
/// <param name="Trigger">
/// The resolved trigger kind, so an absent <c>trigger</c> and an authored <c>ALWAYS</c> land on the
/// same number. Encoding the authored nullability would make the hash distinguish two builds the
/// resolver cannot.
/// </param>
/// <param name="Target">The resolved target, on the same rule.</param>
internal sealed record ResolvedEffectRow(
    string Id, int Op, int Source, int IndexInSource, int Trigger, int Target);

/// <summary>Everything one resolution pass produced, in the shape <c>CanonicalStateWriter</c> encodes.</summary>
/// <param name="Permutation">The permutation ordinal, so a row is self-identifying.</param>
/// <param name="Collected">How many effects collection gathered, before the condition gate.</param>
/// <param name="Active">
/// The gate's survivors, in resolution order. The order is the claim: a hash over an unordered set
/// would not notice the tiebreak being removed. It carries what resolution produced, not what the build
/// authored — an effect's magnitude reaches no column except where a stat op moved a stat.
/// </param>
/// <param name="GatedOut">The ids the gate removed, in the same order.</param>
/// <param name="FinalStats">The 14 combat stats after aggregation, in <c>StatIds.Combat</c> order.</param>
/// <param name="PostMultiplierMaxHp">Max HP after the multiply step, before <c>STAT_SET</c>.</param>
/// <param name="SkippedNonCombatStatEffects">
/// The ids of stat ops naming a non-combat stat. In the hash rather than discarded, because a build
/// that silently lost every "+X% Gold Gain" affix must not share a hash with one that did not.
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

/// <summary>The effect resolution pipeline, composed end to end and hashed — the subject of the determinism baseline.</summary>
/// <remarks>
/// Two calls and one shared gate; nothing here re-implements a step. Handing the aggregation the same
/// gate is <see cref="EffectResolver"/>'s own requirement — the condition gate is asked twice per pass
/// and the two must not be able to answer differently.
/// <para>
/// The value reader is <see cref="ScaledEffectValue"/>, not the strict default: the corpus authors
/// <c>valueScale</c>s, which the strict reader refuses by design.
/// </para>
/// <para>
/// Hashing goes through <see cref="CanonicalStateWriter"/>'s public door only — that class forbids a
/// caller assembling its own bytes. The mode is documented as the <c>PlayerSnapshot</c> one and this is
/// not a snapshot; its actual contract is "one canonical record root", and the encoding is driven by
/// the closed allowlist, so a badly shaped root is refused here exactly as a bad snapshot is.
/// </para>
/// </remarks>
internal static class PermutationResolution
{
    /// <summary>Runs collection through aggregation over one permutation.</summary>
    internal static PermutationOutcome Resolve(BuildPermutation permutation)
    {
        ArgumentNullException.ThrowIfNull(permutation);

        // Collect from the ten sources, sort into the total resolution order, filter by condition
        // against current state.
        var resolved = EffectResolver.Resolve(permutation.Sources, permutation.Context);

        // Group, add, percent, convert, multiply, set, cap, round.
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
