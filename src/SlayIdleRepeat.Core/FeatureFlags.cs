using System.Collections.Frozen;

namespace SlayIdleRepeat.Core;

/// <summary>
/// 🔒 The remote-config kill switches, resolved at the composition root into a plain value
/// (`30` §3) — <b>and closed at the four `14` §14 names.</b>
/// </summary>
/// <remarks>
/// <para>
/// `14` §14, in full, is the entire authored content of this type: <em>"Kill switches: remote config
/// flags for PvP, each ad placement, the Plus offer, and each chapter — so a bad content change is a
/// config edit, not a client patch."</em> Four switches. Two are singular and are booleans; two are
/// "each …" and are therefore sets.
/// </para>
/// <para>
/// 🔒 <b>The type is closed on purpose, and there is deliberately no string-keyed bag.</b> A bag
/// would let any later task introduce an ungoverned flag with no decision behind it — precisely what
/// an enumerated kill-switch list exists to prevent. <b>M5-10</b> ("Remote config endpoint + feature
/// flags resolved into <c>GameContext.Flags</c>") is the task that extends this, and a fifth switch
/// arriving without it fails <c>FeatureFlagsTests</c>.
/// </para>
/// <para>
/// ⚠️ <b>Two documented tensions, both owned by M5-10, neither resolved here.</b> (1) `23` §4.2
/// declares <c>IRemoteConfigPort</c> with an explicitly open, string-keyed surface —
/// <c>T Get&lt;T&gt;(string key, T fallback)</c> and <c>bool IsFeatureEnabled(FeatureFlag flag)</c>,
/// naming a singular <c>FeatureFlag</c> type this closed value has no place for. The port may stay
/// open; what crosses into the domain is this closed value, and M5-10 owns the mapping. (2) `14`
/// §14 says "each chapter" while `14`'s earlier flag list says "content versions". Chapters are
/// implemented, because §14 is the sentence that names kill switches; the two lists have never been
/// reconciled and M5-10 should reconcile them.
/// </para>
/// <para>
/// ⚠️ <b>Open gap, owned by M5-10: there is no authored flag-key naming scheme.</b> `12` §4
/// catalogues 29 ad placements and there are 8 chapters, but no document says how a kill switch
/// names one, and no <c>AdPlacementId</c> or <c>ChapterId</c> type exists in this repository yet
/// (`12` §5's port signature names <c>AdPlacementId</c>; nothing declares it). So the two set-valued
/// switches hold plain strings, compared <b>ordinally</b> — the one promise this type can make
/// without inventing a scheme: two spellings are two identifiers, and a near-miss never silently
/// kills the wrong thing. When M5-10 authors the scheme, these two sets are what change.
/// </para>
/// <para>
/// 🔒 <b>Kill lists, not allow lists.</b> An identifier no switch names is <em>enabled</em>. An
/// allow list would need every chapter and all 29 placements enumerated in remote config, and the
/// day one was missing the game would silently lose it — the opposite of a kill switch, which
/// exists to be the exception.
/// </para>
/// <para>
/// ⚠️ `30` §3's prose calls the flags "a plain record"; this is a sealed <c>class</c>, and the
/// difference is deliberate. A <c>record</c>'s synthesized equality would compare the two set
/// members by <em>reference</em> — advertising value semantics while delivering them for two of four
/// members — and nothing in the domain compares two resolutions. `30` §3's normative code block
/// declares only <c>GameContext</c> as a record; this type is the "plain value" that sentence means.
/// </para>
/// </remarks>
public sealed class FeatureFlags
{
    /// <summary>Creates the flag value the composition root resolved for this command.</summary>
    /// <param name="pvpEnabled">The `14` §14 PvP kill switch. <c>false</c> takes PvP offline.</param>
    /// <param name="plusOfferEnabled">The `14` §14 Plus-offer kill switch.</param>
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

    /// <summary>`14` §14 — PvP (`11`) is live. <c>false</c> is the kill switch thrown.</summary>
    public bool PvpEnabled { get; }

    /// <summary>`14` §14 — the Plus offer (`12` §2) is presentable. <c>false</c> withdraws it.</summary>
    public bool PlusOfferEnabled { get; }

    /// <summary>`14` §14 — the ad placements (`12` §4) remote config has killed. Ordinal, immutable.</summary>
    public IReadOnlySet<string> DisabledAdPlacements { get; }

    /// <summary>`14` §14 — the chapters remote config has killed. Ordinal, immutable.</summary>
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

    /// <summary>
    /// Copies a kill list into an ordinal, immutable set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 A copy, and a <see cref="FrozenSet{T}"/> rather than a <c>HashSet</c> behind an
    /// <c>IReadOnlySet&lt;string&gt;</c>: a caller that kept its list would otherwise be able to
    /// change a kill switch out from under a rule that had already read it, and an
    /// <c>IReadOnlySet&lt;string&gt;</c> that <em>is</em> a <c>HashSet&lt;string&gt;</c> can be cast
    /// back and written through. <c>ContentSnapshot.DocumentPaths</c> documents the same trap.
    /// </para>
    /// <para>
    /// The <c>ToArray</c> is load-bearing: <c>IEnumerable&lt;string&gt;</c> may be lazy or
    /// non-repeatable, and this makes the source enumerate exactly once, before validation.
    /// </para>
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
