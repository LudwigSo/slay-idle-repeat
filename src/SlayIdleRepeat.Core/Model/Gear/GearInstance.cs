using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Gear;

/// <summary>
/// One rolled gear item: what it is, how well it rolled, where it came from, and what has since been
/// done to it. A component of the <c>Player</c> aggregate, never a root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Computed stats are never stored.</b> Every number a hero screen shows for this item is derived
/// at read time from <see cref="DefId"/>, <see cref="Rarity"/>, <see cref="ChapterOrigin"/>,
/// <see cref="Quality"/>, <see cref="EnhanceLevel"/> and <see cref="Affixes"/>. That keeps saves small
/// and lets a balance patch re-tune items a player already owns — a stored ATK figure would be a
/// second, frozen answer that no re-tune could reach.
/// </para>
/// <para>
/// <b>The set is derived too, and deliberately not a field.</b> There are exactly four sets, one per
/// family axis, so an SS item's set is a function of its family; a stored <c>setId</c> would be the
/// same value written twice, and the two spellings would eventually disagree. The set-bonus resolver
/// under <c>Rules/Gear/</c> derives it from the axis the gear catalogue authors.
/// </para>
/// <para>
/// <b>Public type, internal constructor, get-only properties.</b> It is public because a public
/// domain event names it, and it has no public constructor because a type under <c>Model/</c> may not
/// — the server mints a gear instance, nobody else. That combination also rules out a positional
/// record, which is why the equality, hashing and printing below are written by hand.
/// </para>
/// <para>
/// 🔒 <b>The hand-written equality is not a style choice.</b> A synthesized record <c>Equals</c>
/// compares an <c>IReadOnlyList&lt;T&gt;</c> component <em>by reference</em>, so two instances that
/// rolled the same affixes would be unequal unless they happened to share the very same list. Every
/// comparison of an item against its merge inputs, its reforge candidate or its stored self would
/// then answer "different" for a reason nobody could see.
/// </para>
/// <para>
/// ⚠️ <b>It is not itself a persistence DTO, and the task that stores an inventory has to know
/// that.</b> The canonical state writer requires a positional record with exactly one <em>public</em>
/// constructor so it can recover field order mechanically, and this type has an internal one on
/// purpose. Storing an inventory therefore means a <c>GearInstanceSnapshot</c> under
/// <c>Model/Snapshots/</c> and a version bump with it — the same pair <c>Player</c> and
/// <c>PlayerSnapshot</c> already are.
/// </para>
/// <para>
/// ⚠️ <b>There is deliberately no <c>WithLock</c> and no <c>WithEnhancement</c>.</b> Both would be
/// hand-written members on a grant outcome that answer another one, which is the shape the routing
/// rule was narrowed to see — and both belong to tasks that own the operation rather than the state:
/// enhancement is itself a protected grant class, so the type that performs it has to route through
/// the luck façade, and the model layer cannot. Whoever adds them adds them beside the rule that uses
/// them.
/// </para>
/// </remarks>
public sealed record GearInstance
{
    /// <summary>The lowest enhancement level a freshly rolled item carries.</summary>
    internal const int Unenhanced = 0;

    /// <summary>The mercy counter's value on a freshly rolled item, and after a success.</summary>
    internal const int NoFailures = 0;

    /// <summary>The lowest quality a roll can produce.</summary>
    internal const double MinimumQuality = 0.0;

    /// <summary>The highest quality a roll can produce.</summary>
    internal const double MaximumQuality = 1.0;

    /// <summary>The earliest chapter an item can originate in.</summary>
    internal const int FirstChapter = 1;

    private readonly IReadOnlyList<GearAffixRoll> _affixes;

    /// <summary>Builds a validated gear instance.</summary>
    /// <remarks>
    /// A constructor rather than a static factory, and that is not a style preference: a factory
    /// declared on a grant outcome is the exact shape the luck-routing rule was narrowed to catch, and
    /// this type cannot route through the façade because the model layer may not name the rules layer
    /// at all. Minting is therefore the generator's job, and this is the validated door it goes
    /// through.
    /// </remarks>
    /// <param name="instanceId">The item's identity.</param>
    /// <param name="defId">Which base item it is.</param>
    /// <param name="slot">The slot it is worn in.</param>
    /// <param name="family">Its family.</param>
    /// <param name="rarity">Its rarity band.</param>
    /// <param name="chapterOrigin">The chapter its power is scaled against. At least 1.</param>
    /// <param name="quality">The quality scalar, in <c>[0, 1]</c> and already rounded.</param>
    /// <param name="enhanceLevel">How far it has been enhanced. Never negative.</param>
    /// <param name="enhanceFailures">Consecutive enhancement failures on it. Never negative.</param>
    /// <param name="affixes">The rolled affixes, in roll order. Never null; may be empty.</param>
    /// <param name="locked">Whether it is locked against destructive operations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="affixes"/> or <paramref name="defId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="defId"/> is blank, or an affix is duplicated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum is undeclared, or a number is outside its stated range.</exception>
    internal GearInstance(
        GearInstanceId instanceId,
        string defId,
        GearSlot slot,
        GearFamily family,
        Rarity rarity,
        int chapterOrigin,
        double quality,
        int enhanceLevel,
        int enhanceFailures,
        IReadOnlyList<GearAffixRoll> affixes,
        bool locked)
    {
        ArgumentNullException.ThrowIfNull(affixes);

        // Written out rather than delegated to a shared guard: a private helper taking a Rarity is a
        // method on a grant outcome carrying a grant outcome in its signature, which the 24 §11
        // routing rule reads as a producer — and exempting this type from that rule would reopen the
        // factory-on-the-outcome hole the rule was narrowed to close.
        if (!Enum.IsDefined(slot))
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot, "That is not one of the six equipment slots.");
        }

        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(
                nameof(family), family, "That is not one of the twenty-four item families.");
        }

        if (!Enum.IsDefined(rarity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rarity), rarity, "That is not a rarity on the ladder.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(chapterOrigin, FirstChapter);
        ArgumentOutOfRangeException.ThrowIfNegative(enhanceLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(enhanceFailures);

        InstanceId = instanceId;
        DefId = IdText.Require(defId, nameof(GearInstance));
        Slot = slot;
        Family = family;
        Rarity = rarity;
        ChapterOrigin = chapterOrigin;
        Quality = RequireQuality(quality);
        EnhanceLevel = enhanceLevel;
        EnhanceFailures = enhanceFailures;
        _affixes = CopyAffixes(affixes);
        Locked = locked;
    }

    /// <summary>This item's identity. Distinct from every other copy of the same base item.</summary>
    public GearInstanceId InstanceId { get; }

    /// <summary>Which of the twenty-four base items this is, e.g. <c>GEAR_WEAPON_BLADE</c>.</summary>
    public string DefId { get; }

    /// <summary>The slot it is worn in.</summary>
    public GearSlot Slot { get; }

    /// <summary>Its family — the stat bias, and at <see cref="Primitives.Rarity.SS"/> the set.</summary>
    public GearFamily Family { get; }

    /// <summary>Its rarity band: the stat multiplier and the number of affixes it rolled.</summary>
    public Rarity Rarity { get; }

    /// <summary>
    /// The chapter whose power target this item's stats are scaled against. Never the chapter it is
    /// being read in: an item keeps the power it dropped with, which is why a merge takes the
    /// <em>maximum</em> origin of its inputs rather than the first.
    /// </summary>
    public int ChapterOrigin { get; }

    /// <summary>
    /// The one quality scalar, <c>q</c> in <c>[0, 1]</c>, rolled once when the item was generated.
    /// </summary>
    /// <remarks>
    /// One scalar, not a roll per stat: the primary stat spans ×0.90–1.10 of its base and the
    /// secondary ×0.85–1.15, both driven from this single number, so a high-quality item leans
    /// hardest into its secondary stat and the UI can show one honest quality bar. Stored already
    /// rounded, because persisted state carries no unrounded double.
    /// </remarks>
    public double Quality { get; }

    /// <summary>How far the item has been enhanced. Zero on a fresh roll.</summary>
    public int EnhanceLevel { get; }

    /// <summary>
    /// Consecutive failed enhancement attempts on <em>this item</em> — the mercy counter that raises
    /// the next attempt's effective success rate.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>On the item, not on the player, and that is the whole point of the field.</b> A
    /// player-scoped counter could be farmed on cheap items and spent on an expensive one; scoped
    /// here it cannot be, and a merge output inherits it from its inputs.
    /// <para>
    /// ⚠️ The instance schema the design set draws has no slot for this counter, while the enhance
    /// rules require one that lives exactly here. The field is the resolution, and the gap is carried
    /// forward rather than papered over. The <em>rate</em> the counter feeds, and the inheritance rule
    /// on merge, are the forge task's — this type only owns the place the number lives.
    /// </para>
    /// </remarks>
    public int EnhanceFailures { get; }

    /// <summary>
    /// The affixes this item rolled, in roll order. Empty at the bottom rarity, which rolls none.
    /// </summary>
    /// <remarks>
    /// Order is preserved rather than normalised: it is the order they were drawn in, and a retune
    /// that locks "the first two" needs it to mean something stable.
    /// </remarks>
    public IReadOnlyList<GearAffixRoll> Affixes => _affixes;

    /// <summary>Whether the player has locked the item against salvage, merge and auto-salvage.</summary>
    public bool Locked { get; }

    /// <summary>Whether two items are the same rolled item, compared by value throughout.</summary>
    /// <param name="other">The other item, or null.</param>
    /// <returns><see langword="true"/> when every member matches, affixes included, in order.</returns>
    public bool Equals(GearInstance? other) =>
        other is not null &&
        InstanceId.Equals(other.InstanceId) &&
        string.Equals(DefId, other.DefId, StringComparison.Ordinal) &&
        Slot == other.Slot &&
        Family == other.Family &&
        Rarity == other.Rarity &&
        ChapterOrigin == other.ChapterOrigin &&
        Quality.Equals(other.Quality) &&
        EnhanceLevel == other.EnhanceLevel &&
        EnhanceFailures == other.EnhanceFailures &&
        Locked == other.Locked &&
        SameAffixes(_affixes, other._affixes);

    /// <summary>A hash consistent with <see cref="Equals(GearInstance)"/>, affixes included.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(InstanceId);
        hash.Add(DefId, StringComparer.Ordinal);
        hash.Add(Slot);
        hash.Add(Family);
        hash.Add(Rarity);
        hash.Add(ChapterOrigin);
        hash.Add(Quality);
        hash.Add(EnhanceLevel);
        hash.Add(EnhanceFailures);
        hash.Add(Locked);
        hash.Add(_affixes.Count);

        foreach (var affix in _affixes)
        {
            hash.Add(affix);
        }

        return hash.ToHashCode();
    }

    /// <summary>Renders the item with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="builder">The builder the record's <c>ToString()</c> is assembling into.</param>
    /// <returns><see langword="true"/>, so <c>ToString()</c> spaces the closing brace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"{nameof(InstanceId)} = {InstanceId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(DefId)} = {DefId}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Slot)} = {Slot}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Family)} = {Family}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Rarity)} = {Rarity}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(ChapterOrigin)} = {ChapterOrigin}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Quality)} = {Quality}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(EnhanceLevel)} = {EnhanceLevel}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(EnhanceFailures)} = {EnhanceFailures}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Locked)} = {Locked}");
        builder.Append(CultureInfo.InvariantCulture, $", {nameof(Affixes)} = [{string.Join(", ", _affixes)}]");

        return true;
    }

    private static bool SameAffixes(IReadOnlyList<GearAffixRoll> left, IReadOnlyList<GearAffixRoll> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A read-only copy of the caller's affixes, refusing a duplicate id.
    /// </summary>
    /// <remarks>
    /// A copy wrapped in a <see cref="ReadOnlyCollection{T}"/>, never the caller's own list behind a
    /// read-only interface — the latter casts straight back and lets an item's affixes change after
    /// it was minted. The duplicate check is here rather than in the roller because an item carrying
    /// the same affix twice would double one stat and read to a player as a single stronger roll.
    /// </remarks>
    private static IReadOnlyList<GearAffixRoll> CopyAffixes(IReadOnlyList<GearAffixRoll> affixes)
    {
        var copy = new GearAffixRoll[affixes.Count];

        for (var i = 0; i < affixes.Count; i++)
        {
            copy[i] = affixes[i];

            for (var earlier = 0; earlier < i; earlier++)
            {
                if (string.Equals(copy[earlier].AffixId, copy[i].AffixId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"'{copy[i].AffixId}' is rolled twice onto one item. The affix pool draws " +
                        "without replacement, so a repeat is a roller that lost its exclusion — and " +
                        "it would read to the player as one unusually strong affix rather than two.",
                        nameof(affixes));
                }
            }
        }

        return new ReadOnlyCollection<GearAffixRoll>(copy);
    }

    private static double RequireQuality(double quality)
    {
        if (!DeterminismRounding.IsRounded(quality) ||
            quality < MinimumQuality ||
            quality > MaximumQuality)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quality),
                quality,
                "Quality is the one scalar q in [0, 1], stored already rounded to the assembly's " +
                "determinism precision. The UI shows it directly as a percentage and the canonical " +
                "state writer refuses an unrounded double, so an item minted with a raw draw would " +
                "be unpersistable and would display a quality bar nobody chose.");
        }

        return quality;
    }
}
