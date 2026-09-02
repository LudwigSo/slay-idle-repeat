using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>The in-<c>Core</c> implementation of <see cref="IRunTriggerCounters"/> — a table of instance ids to counts.</summary>
/// <remarks>
/// <para>
/// Mutable, where <see cref="RunStateReading"/> is a value — a condition reads, a counter advances.
/// A run owns this for its whole length and hands it to each battle's <see cref="TriggerRegistry"/>,
/// rather than rebuilding it per battle, which would reset a persistent counter every fight.
/// </para>
/// <para>Not thread-safe: one battle is simulated by one caller.</para>
/// </remarks>
internal sealed class RunTriggerCounters : IRunTriggerCounters
{
    private readonly Dictionary<EffectInstanceId, int> _counts = new();

    // S2365 wants this to be a method. It cannot be: IRunTriggerCounters declares Entries as a
    // property, and the ordered snapshot is that contract. Changing the shape means changing the
    // interface and every implementation of it, which is a design change and not this one.
#pragma warning disable S2365
    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<EffectInstanceId, int>> Entries =>
        _counts.OrderBy(entry => entry.Key.Value, EffectInstanceId.Comparer).ToArray();
#pragma warning restore S2365

    /// <inheritdoc />
    public int Read(EffectInstanceId instance)
    {
        RequireAHolding(instance);

        return _counts.GetValueOrDefault(instance);
    }

    /// <inheritdoc />
    public void Write(EffectInstanceId instance, int count)
    {
        RequireAHolding(instance);

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

    /// <summary>Refuses an id that names no holding — <c>default</c>, empty or whitespace.</summary>
    /// <remarks>Checks whitespace as well as null — two blank instance ids constructed directly (bypassing <see cref="EffectInstanceId.Of"/>) would otherwise share one run counter.</remarks>
    private static void RequireAHolding(EffectInstanceId instance)
    {
        if (!instance.NamesAHolding)
        {
            throw new ArgumentException(
                "A run-scoped counter was addressed by an instance id that names no holding. `18` §3's " +
                "counters live on the effect instance, and every unnamed instance would share one " +
                "counter with every other.",
                nameof(instance));
        }
    }
}
