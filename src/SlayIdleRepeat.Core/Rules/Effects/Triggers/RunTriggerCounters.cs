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

    /// <inheritdoc />
    public IReadOnlyList<KeyValuePair<EffectInstanceId, int>> Entries =>
        _counts.OrderBy(entry => entry.Key.Value, EffectInstanceId.Comparer).ToArray();

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

    /// <summary>
    /// 🔒 Refuses an id that names no holding — <c>default</c>, empty or whitespace.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Whitespace, not just null.</b> An earlier draft checked <c>Value is null</c> alone, so
    /// two <c>new EffectInstanceId("")</c> instances shared one run counter — the exact silent
    /// failure <see cref="EffectInstanceId"/>'s remarks say must not happen, arrived at through the
    /// generated constructor that bypasses <c>EffectInstanceId.Of</c>.
    /// </remarks>
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
