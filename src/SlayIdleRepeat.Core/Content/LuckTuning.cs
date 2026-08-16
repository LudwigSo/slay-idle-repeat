using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The pity registry: the ten source classes with their counter keys and scopes, the hard/soft pity
/// ladders of the five classes that author one, and the rarity-floor rule — read out of
/// <c>tuning/luck.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type owns counter-key formation, and it is the only place that forms one.</b> A source
/// class does not address a counter by itself: <c>CHEST_STANDARD</c> runs three ladders at once, so
/// a counter is addressed by the pairing of the class's authored <c>counterKey</c> with the
/// guarantee it protects — <c>chest.standard:A</c>, <c>chest.standard:S</c>,
/// <c>chest.standard:SS</c>. Adding a fourth rung stays a data edit because the key is derived from
/// the data rather than declared beside it.
/// </para>
/// <para>
/// Five classes author the <c>hardPity[] + softPity</c> shape and are the ones the rarity-ladder
/// path serves. The other five author their rule in a different shape entirely — a dry-streak
/// breaker, a failure-rate mercy, a draft composition rule, a jackpot spin count, a chest-pick
/// guarantee — and asking this type for a ladder they do not have answers false rather than
/// synthesising one. Those five call the pity primitives directly; the tasks that wire them are
/// named in the exception message.
/// </para>
/// <para>
/// Not cached. Every reader re-reads the snapshot it was handed, so a content version swap cannot
/// leave a stale ladder behind a static field.
/// </para>
/// </remarks>
internal sealed class LuckTuning
{
    /// <summary>The document the pity registry lives in.</summary>
    internal const string DocumentPath = "tuning/luck.json";

    private const string RarityFloorPointer = DocumentPath + "#/rarityFloor";

    /// <summary>The ten source-class rows: id, counter key, counter scope.</summary>
    internal const string SourceClassesReference = DocumentPath + "#/sourceClasses";

    /// <summary>How a floored table is renormalised. <c>PROPORTIONAL</c> as shipped.</summary>
    internal const string RenormalisationReference = RarityFloorPointer + "/renormalisation";

    /// <summary>Whether a floored draw still advances and resets counters. <c>true</c> as shipped.</summary>
    internal const string CountersAdvanceNormallyReference = RarityFloorPointer + "/countersAdvanceNormally";

    /// <summary>The standard-chest block — the 10/40/160 ladder plus its soft-pity curve.</summary>
    internal const string ChestStandardReference = DocumentPath + "#/chestStandard";

    /// <summary>The premium-chest block — the 5/25 ladder plus its soft-pity curve.</summary>
    internal const string ChestPremiumReference = DocumentPath + "#/chestPremium";

    /// <summary>The apex-chest block — one rung, and no soft pity at that density.</summary>
    internal const string ChestApexReference = DocumentPath + "#/chestApex";

    /// <summary>The Pet Egg block — the 30/150 ladder.</summary>
    internal const string EggPetReference = DocumentPath + "#/eggPet";

    /// <summary>The Mount Crate block — the 8/30 ladder plus its soft-pity curve.</summary>
    internal const string CrateMountReference = DocumentPath + "#/crateMount";

    /// <summary>The member every ladder block names its rungs under.</summary>
    internal const string HardPityMember = "hardPity";

    /// <summary>The member every ladder block names its soft-pity curve under. May be an authored null.</summary>
    internal const string SoftPityMember = "softPity";

    /// <summary>
    /// The separator between an authored counter key and the guarantee rarity it protects.
    /// </summary>
    /// <remarks>
    /// A colon, matching the aggregate's existing composite counter keys. It cannot occur inside
    /// either half — the schema's <c>counterKey</c> pattern is lower-case letters, digits and dots,
    /// and a rarity is two letters at most — so a key parses back unambiguously.
    /// </remarks>
    internal const char CounterKeySeparator = ':';

    private readonly IReadOnlyDictionary<SourceClass, PityLadder> _ladders;

    private LuckTuning(
        IReadOnlyList<SourceClassRow> sourceClasses,
        IReadOnlyDictionary<SourceClass, PityLadder> ladders,
        RarityFloorRule rarityFloor)
    {
        SourceClasses = sourceClasses;
        _ladders = ladders;
        RarityFloor = rarityFloor;
    }

    /// <summary>The ten source-class rows, in the order the document lists them.</summary>
    internal IReadOnlyList<SourceClassRow> SourceClasses { get; }

    /// <summary>The one rule for flooring a class table at a guaranteed rarity.</summary>
    internal RarityFloorRule RarityFloor { get; }

    /// <summary>The authored row for one source class.</summary>
    /// <param name="source">The class to look up.</param>
    /// <returns>Its registry row.</returns>
    /// <exception cref="InvalidTunableException">The document authors no row for this class.</exception>
    internal SourceClassRow Row(SourceClass source) => throw new NotImplementedException();

    /// <summary>
    /// The hard/soft pity ladder for a class, when the document authors one in that shape.
    /// </summary>
    /// <param name="source">The class to look up.</param>
    /// <param name="ladder">The ladder, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the class authors a rarity ladder.</returns>
    internal bool TryGetLadder(SourceClass source, out PityLadder ladder) =>
        _ladders.TryGetValue(source, out ladder);

    /// <summary>The hard/soft pity ladder for a class.</summary>
    /// <param name="source">The class to look up.</param>
    /// <returns>Its ladder.</returns>
    /// <exception cref="InvalidTunableException">
    /// The class authors its protection in another shape entirely, so there is no ladder to draw
    /// against. The message names the class, the block that holds its real rule, and the task that
    /// wires it.
    /// </exception>
    internal PityLadder Ladder(SourceClass source) => throw new NotImplementedException();

    /// <summary>
    /// The counter id a class's guarantee is stored under —
    /// <c>"&lt;counterKey&gt;&lt;separator&gt;&lt;rarity&gt;"</c>, e.g. <c>chest.standard:A</c>.
    /// </summary>
    /// <remarks>
    /// The one place a counter key is formed. Everything that reads or writes a pity counter goes
    /// through here, so the authored key and the stored key cannot drift apart.
    /// </remarks>
    /// <param name="source">The class whose counter is being addressed.</param>
    /// <param name="guarantee">The guarantee rarity that counter protects.</param>
    /// <returns>The counter id.</returns>
    /// <exception cref="InvalidTunableException">
    /// The class authors no counter key — its counter is not player-scoped, so it has no id in this
    /// map at all.
    /// </exception>
    internal string CounterKey(SourceClass source, Rarity guarantee) =>
        throw new NotImplementedException();

    /// <summary>
    /// Reads the pity registry. Throws rather than defaulting on anything missing, unauthorised,
    /// mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static LuckTuning Read(ContentSnapshot content) => throw new NotImplementedException();

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Renders a number with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The invariant rendering.</returns>
    internal static string Render(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Where a source class's counter is kept.</summary>
/// <remarks>
/// Only <see cref="PLAYER"/> counters live in the player-scoped counter map. The other two are
/// authored so that the registry can say <em>where</em> a counter lives rather than leaving a null
/// counter key to be read as "no counter at all".
/// </remarks>
internal enum CounterScope
{
    /// <summary>On the player profile, forever. The map the luck service takes as an argument.</summary>
    PLAYER,

    /// <summary>On the gear instance, and inherited by merge outputs.</summary>
    GEAR_INSTANCE,

    /// <summary>On the run, and gone when the run ends.</summary>
    RUN,
}

/// <summary>How the surviving weights of a floored table are rescaled.</summary>
/// <remarks>
/// A one-member enum, matching the schema's one-member enum: widening it is a deliberate edit in
/// both places rather than a value that slipped through a string comparison.
/// </remarks>
internal enum RarityFloorRenormalisation
{
    /// <summary>
    /// Zero every weight below the floor and rescale the survivors to sum to one, preserving their
    /// relative ratios. One rule for every source class.
    /// </summary>
    PROPORTIONAL,
}

/// <summary>One authored source-class row.</summary>
/// <param name="Id">The class.</param>
/// <param name="CounterKey">
/// The authored counter key, e.g. <c>chest.standard</c> — or <see langword="null"/> when the class's
/// counter is not player-scoped and therefore has no id in the player counter map.
/// </param>
/// <param name="Scope">Where the counter lives.</param>
internal readonly record struct SourceClassRow(SourceClass Id, string? CounterKey, CounterScope Scope);

/// <summary>One rung of a hard-pity ladder.</summary>
/// <param name="EveryNth">
/// The draw, counted from the last reset of this rung's counter, that is forced to satisfy the
/// guarantee. The N-th draw is the forced one, not the (N+1)-th.
/// </param>
/// <param name="GuaranteeRarityAtLeast">The rarity the forced draw must reach or beat.</param>
internal readonly record struct HardPityStep(int EveryNth, Rarity GuaranteeRarityAtLeast);

/// <summary>One authored soft-pity curve.</summary>
/// <param name="Target">
/// The rarity whose weight the curve raises, as the document spells it. Text rather than
/// <see cref="Rarity"/> because two classes target a token that is not on the rarity ladder at all.
/// </param>
/// <param name="MissThreshold">The number of misses the curve stays flat for.</param>
/// <param name="Slope">The per-miss multiplier growth past the threshold.</param>
internal readonly record struct SoftPityCurve(string Target, int MissThreshold, double Slope);

/// <summary>One class's rarity-ladder protection: its hard-pity rungs and its soft-pity curve.</summary>
/// <param name="Source">The class this ladder belongs to.</param>
/// <param name="HardPity">
/// The rungs, in the order the document lists them. Always at least one — a class that authors this
/// shape authors a hard pity, which is mandatory everywhere.
/// </param>
/// <param name="SoftPity">
/// The curve, or <see langword="null"/> where the document authors an explicit null: two of the five
/// classes are dense enough not to need one.
/// </param>
internal readonly record struct PityLadder(
    SourceClass Source, IReadOnlyList<HardPityStep> HardPity, SoftPityCurve? SoftPity);

/// <summary>The one rule for drawing a class table against a rarity floor.</summary>
/// <param name="Renormalisation">How the surviving weights are rescaled.</param>
/// <param name="CountersAdvanceNormally">
/// Whether a floored draw still advances and resets counters exactly as an unfloored one does.
/// <c>true</c> as shipped, and the reason a forced draw is expressible as a floored one.
/// </param>
internal readonly record struct RarityFloorRule(
    RarityFloorRenormalisation Renormalisation, bool CountersAdvanceNormally);
