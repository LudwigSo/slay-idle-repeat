namespace SlayIdleRepeat.BalanceHarness.Diagnostics;

/// <summary>
/// The measured shortfall probes, indexed so an experiment can ask "at what multiple of par does this
/// build reach the target in this chapter?"
/// </summary>
/// <remarks>
/// Boss phases are HP bands, so an effect authored on phase 2 or 3 doesn't exist for a hero that dies
/// in phase 1. Running an A/B at a multiple calibrated for a different chapter would reach phase 1
/// instead and report a difference of zero for the wrong reason, so each arm asks for the multiple
/// measured for its own <c>(chapter, archetype)</c>.
/// </remarks>
public sealed class ShortfallLookup
{
    private readonly IReadOnlyList<ShortfallProbe> _probes;

    /// <summary>Indexes a set of probes.</summary>
    /// <exception cref="ArgumentException">The set is empty — there would be nothing to fall back to.</exception>
    public ShortfallLookup(IReadOnlyList<ShortfallProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        if (probes.Count == 0)
        {
            throw new ArgumentException(
                "A shortfall lookup over no probes has nothing to answer with, and an experiment that " +
                "silently fell back to 1.0 would run every arm at par — where the phase mechanics " +
                "under test never fire.",
                nameof(probes));
        }

        _probes = probes;
    }

    /// <summary>
    /// The multiple of par for a <c>(chapter, archetype)</c>, falling back to the same chapter's
    /// other builds and then to any probe.
    /// </summary>
    /// <remarks>
    /// The fallbacks exist because a narrowed <c>--chapters</c> or <c>--archetypes</c> run does not
    /// probe every combination an experiment may reach — the five summoning bosses span chapters 1,
    /// 3, 5, 6 and 7. A fallback is a worse estimate, never a wrong kind of answer: it is still a
    /// measured multiple rather than a chosen one.
    /// </remarks>
    public double Multiple(int chapter, string archetypeId)
    {
        ArgumentNullException.ThrowIfNull(archetypeId);

        foreach (var probe in _probes)
        {
            if (probe.Chapter == chapter &&
                string.Equals(probe.ArchetypeId, archetypeId, StringComparison.Ordinal))
            {
                return probe.Multiple;
            }
        }

        foreach (var probe in _probes)
        {
            if (probe.Chapter == chapter)
            {
                return probe.Multiple;
            }
        }

        return _probes[0].Multiple;
    }
}
