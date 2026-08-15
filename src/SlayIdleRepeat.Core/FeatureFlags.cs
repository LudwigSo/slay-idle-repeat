using System.Collections.Frozen;

namespace SlayIdleRepeat.Core;

/// <summary>The remote-config kill switches, resolved at the composition root into a plain value.</summary>
/// <remarks>
/// <para>
/// Four switches, closed on purpose rather than a string-keyed bag: PvP, the Plus offer, ad
/// placements and chapters. A bag would let any later task introduce an ungoverned flag with no
/// decision behind it; adding a fifth switch here requires touching <c>FeatureFlagsTests</c> too.
/// </para>
/// <para>
/// Kill lists, not allow lists — an identifier no switch names is enabled. An allow list would
/// need every chapter and placement enumerated in remote config, and a missing entry would
/// silently disable content instead of being the deliberate exception a kill switch is.
/// </para>
/// <para>
/// Deliberately a sealed <c>class</c>, not a <c>record</c>: synthesized equality would compare the
/// two set members by reference, which is misleading value semantics for only half the members.
/// </para>
/// </remarks>
public sealed class FeatureFlags
{
    /// <summary>Creates the flag value the composition root resolved for this command.</summary>
    /// <param name="pvpEnabled">The PvP kill switch. <c>false</c> takes PvP offline.</param>
    /// <param name="plusOfferEnabled">The Plus-offer kill switch.</param>
    /// <param name="disabledAdPlacements">The ad placements this config has killed. Copied.</param>
    /// <param name="disabledChapters">The chapters this config has killed. Copied.</param>
    public FeatureFlags(
        bool pvpEnabled,
        bool plusOfferEnabled,
        IEnumerable<string> disabledAdPlacements,
        IEnumerable<string> disabledChapters)
    {
        PvpEnabled = pvpEnabled;
        PlusOfferEnabled = plusOfferEnabled;
        DisabledAdPlacements = Freeze(disabledAdPlacements, nameof(disabledAdPlacements));
        DisabledChapters = Freeze(disabledChapters, nameof(disabledChapters));
    }

    /// <summary>Whether PvP is live. <c>false</c> is the kill switch thrown.</summary>
    public bool PvpEnabled { get; }

    /// <summary>Whether the Plus offer is presentable. <c>false</c> withdraws it.</summary>
    public bool PlusOfferEnabled { get; }

    /// <summary>The ad placements remote config has killed. Ordinal, immutable.</summary>
    public IReadOnlySet<string> DisabledAdPlacements { get; }

    /// <summary>The chapters remote config has killed. Ordinal, immutable.</summary>
    public IReadOnlySet<string> DisabledChapters { get; }

    /// <summary>Whether an ad placement may be offered. Unknown identifiers are enabled.</summary>
    public bool IsAdPlacementEnabled(string adPlacementId)
    {
        ArgumentNullException.ThrowIfNull(adPlacementId);

        return !DisabledAdPlacements.Contains(adPlacementId);
    }

    /// <summary>Whether a chapter may be entered. Unknown identifiers are enabled.</summary>
    public bool IsChapterEnabled(string chapterId)
    {
        ArgumentNullException.ThrowIfNull(chapterId);

        return !DisabledChapters.Contains(chapterId);
    }

    /// <summary>Copies a kill list into an ordinal, immutable set.</summary>
    /// <remarks>
    /// A <see cref="FrozenSet{T}"/> rather than a <c>HashSet</c> behind <c>IReadOnlySet</c>: the
    /// latter can be cast back and written through, letting a caller mutate a kill switch out from
    /// under a rule that already read it. <c>ToArray</c> materialises the source once, before
    /// validation, since <c>IEnumerable&lt;string&gt;</c> may be lazy or non-repeatable.
    /// </remarks>
    private static FrozenSet<string> Freeze(IEnumerable<string> identifiers, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(identifiers, parameterName);

        var materialised = identifiers.ToArray();

        foreach (var identifier in materialised)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException(
                    "A kill switch names a null or blank identifier. Remote config that produced " +
                    "an empty array entry has said nothing, and no placement or chapter is named by " +
                    "the empty string — so the switch would read as thrown while killing nothing " +
                    "(14 §14).",
                    parameterName);
            }
        }

        return materialised.ToFrozenSet(StringComparer.Ordinal);
    }
}
