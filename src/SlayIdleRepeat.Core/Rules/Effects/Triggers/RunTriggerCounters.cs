using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// The in-<c>Core</c> implementation of <see cref="IRunTriggerCounters"/> — a table of
/// instance ids to counts.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Shipped in the same change as the seam</b>, with the shared contract suite that this and
/// every later implementation are run through (steering S7: M0's first port shipped without its
/// suite and its two implementations already disagreed on the exception type they threw).
/// <see cref="RunStateReading"/> is the precedent one file over.
/// </para>
/// <para>
/// ⚠️ <b>Mutable, where <see cref="RunStateReading"/> is a value — and that is the whole difference
/// between the two seams.</b> A condition reads; a counter advances. The consequence for M3 is that
/// this is the object a run <em>owns</em> for the length of a run and hands to each battle's
/// <see cref="TriggerRegistry"/>, not one it rebuilds per battle: rebuilding it would reset
/// <c>PK_MIDAS</c> every fight, which is precisely what `18` §3 says must not happen.
/// </para>
/// <para>
/// Not thread-safe, for <c>CombatLog</c>'s reason: one battle is simulated by one caller, and the
/// balance harness parallelises across fights rather than within one.
/// </para>
/// </remarks>
internal sealed class RunTriggerCounters : IRunTriggerCounters
{
    private readonly Dictionary<EffectInstanceId, int> _counts = new();

    /// <summary>Every instance the run holds a count for, in ascending ordinal id order.</summary>
    /// <remarks>
    /// 🔒 Ordered, and ordered <b>here</b>: this is what M3 persists, and a dictionary's enumeration
    /// order is an implementation detail of the runtime. `14` §8.2 hashes the run snapshot on both
    /// x64 and ARM64 and compares, so a set of pairs that serialised in insertion order would make
    /// the same run hash differently depending on which enemy the hero happened to kill first.
    /// </remarks>
    internal IReadOnlyList<KeyValuePair<EffectInstanceId, int>> Entries =>
        _counts.OrderBy(entry => entry.Key.Value, EffectInstanceId.Comparer).ToArray();

    /// <inheritdoc />
    public int Read(EffectInstanceId instance) => _counts.GetValueOrDefault(instance);

    /// <inheritdoc />
    public void Write(EffectInstanceId instance, int count)
    {
        if (instance.Value is null)
        {
            throw new ArgumentException(
                "A run-scoped counter was written against an instance with no id. `18` §3's counters " +
                "live on the effect instance, and an unnamed instance shares one counter with every " +
                "other unnamed instance.",
                nameof(instance));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"A run-scoped counter was written as {count.ToString(CultureInfo.InvariantCulture)}. " +
                "`18` §3's counters count occurrences — attacks made, enemies killed — and only " +
                "advance. A negative count is an arithmetic failure upstream, and 'every 6th enemy " +
                "killed' would then fire at a moment no player could explain.");
        }

        _counts[instance] = count;
    }
}
