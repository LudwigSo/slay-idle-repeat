using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// One dry-streak breaker: after this many consecutive drops below a rarity, the next drop of its
/// kind is forced to at least another.
/// </summary>
/// <param name="ForceOnNthKill">
/// 🔒 <b>The ordinal of the forced kill within a dry streak, not a count of misses tolerated before
/// it</b> — the sixth elite kill of a streak <em>is</em> the forced one, so five misses precede it.
/// <para>
/// The design text is the authority and is unambiguous — <i>"Count consecutive Elite kills whose
/// drop was below A-rarity. On the 6th, force A or better"</i> — and the authored key is spelled
/// <c>forceOnNthKill</c> to match, so the data and the reader now say the same thing. The two
/// readings differ by exactly one drop, which is why the name is stated rather than left to the
/// call site.
/// </para>
/// </param>
/// <param name="BelowRarity">A drop counts as a miss when it lands strictly below this band.</param>
/// <param name="ForceRarityAtLeast">The band the forced drop must reach or beat.</param>
internal readonly record struct DryStreakBreaker(
    int ForceOnNthKill, Rarity BelowRarity, Rarity ForceRarityAtLeast);

/// <summary>
/// The session floor: a qualifying session grants at least this much gear regardless of what the run
/// itself dropped.
/// </summary>
/// <param name="GrantRarity">The band each floor grant lands on.</param>
/// <param name="GrantCount">How many items one qualifying session grants.</param>
/// <param name="MaxPerDay">How many floor grants a player may receive in one game day.</param>
/// <param name="RequiresVictoryOrStage3Death">
/// Whether the run must have ended in a victory or a stage-3 death to qualify. Authored true, and
/// read rather than assumed so that turning it off stays a data edit.
/// </param>
internal readonly record struct SessionFloor(
    Rarity GrantRarity, int GrantCount, int MaxPerDay, bool RequiresVictoryOrStage3Death);

/// <summary>
/// The in-run drop protections, read out of <c>tuning/luck.json#/dropRun</c>: the elite dry-streak
/// breaker, the boss dry-streak breaker, and the session floor.
/// </summary>
/// <remarks>
/// <para>
/// A reader of its own rather than a member of the pity registry, because the shape is genuinely
/// different. The five classes that author a rarity ladder state their protection as hard rungs plus
/// a soft curve; this class states three unrelated rules, two of them dry-streak breakers over
/// different kill kinds and one of them a per-day grant that no draw produces. The registry refuses
/// to synthesise a ladder for it, by name, and this is the reader that shape actually has.
/// </para>
/// <para>
/// It reads the same document the registry does, and takes nothing from it: the counter <em>keys</em>
/// stay the registry's to form, because a counter is addressed by pairing an authored key with the
/// guarantee it protects and there is exactly one place that pairing is made.
/// </para>
/// </remarks>
internal sealed class DropRunTuning
{
    /// <summary>The document the in-run drop protections live in.</summary>
    internal const string DocumentPath = "tuning/luck.json";

    /// <summary>The block that holds them.</summary>
    internal const string BlockReference = DocumentPath + "#/dropRun";

    /// <summary>The elite dry-streak breaker.</summary>
    internal const string EliteMercyReference = BlockReference + "/eliteMercy";

    /// <summary>The boss dry-streak breaker.</summary>
    internal const string BossMercyReference = BlockReference + "/bossMercy";

    /// <summary>The per-session gear floor.</summary>
    internal const string SessionFloorReference = BlockReference + "/sessionFloor";

    private DropRunTuning(DryStreakBreaker eliteMercy, DryStreakBreaker bossMercy, SessionFloor floor)
    {
        EliteMercy = eliteMercy;
        BossMercy = bossMercy;
        SessionFloor = floor;
    }

    /// <summary>The elite dry-streak breaker.</summary>
    internal DryStreakBreaker EliteMercy { get; }

    /// <summary>The boss dry-streak breaker.</summary>
    internal DryStreakBreaker BossMercy { get; }

    /// <summary>The per-session gear floor.</summary>
    internal SessionFloor SessionFloor { get; }

    /// <summary>Reads the in-run drop protections.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The protections.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static DropRunTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new DropRunTuning(
            ReadBreaker(content, EliteMercyReference),
            ReadBreaker(content, BossMercyReference),
            ReadFloor(content));
    }

    private static DryStreakBreaker ReadBreaker(ContentSnapshot content, string reference)
    {
        var ordinalReference = reference + "/forceOnNthKill";
        var ordinal = content.ReadInt32(ordinalReference);

        if (ordinal < 1)
        {
            throw new InvalidTunableException(
                ordinalReference,
                "A dry-streak breaker forces the N-th kill of a streak, counted from one, and this " +
                $"document authors {AuthoredToken.Render(ordinal)}. There is no zeroth kill to force.");
        }

        var below = AuthoredToken.Parse<Rarity>(
            content, reference + "/belowRarity", "a band on the rarity ladder");
        var force = AuthoredToken.Parse<Rarity>(
            content, reference + "/forceRarityAtLeast", "a band on the rarity ladder");

        if (force < below)
        {
            throw new InvalidTunableException(
                reference,
                $"The breaker counts a drop below {below} as a miss and then forces {force}, which is " +
                "itself a miss — so the counter would never reset and the guarantee would fire on " +
                "every drop from then on.");
        }

        return new DryStreakBreaker(ordinal, below, force);
    }

    private static SessionFloor ReadFloor(ContentSnapshot content)
    {
        var countReference = SessionFloorReference + "/grantCount";
        var count = content.ReadInt32(countReference);

        if (count < 1)
        {
            throw new InvalidTunableException(
                countReference,
                "The session floor grants at least one item, and this document authors " +
                $"{AuthoredToken.Render(count)} — a floor that grants nothing is authored as no floor.");
        }

        var perDayReference = SessionFloorReference + "/maxPerDay";
        var perDay = content.ReadInt32(perDayReference);

        if (perDay < 1)
        {
            throw new InvalidTunableException(
                perDayReference,
                "The floor may be paid at least once a day, and this document authors " +
                AuthoredToken.Render(perDay) + ".");
        }

        return new SessionFloor(
            AuthoredToken.Parse<Rarity>(
                content, SessionFloorReference + "/grantRarity", "a band on the rarity ladder"),
            count,
            perDay,
            content.ReadBoolean(SessionFloorReference + "/requiresVictoryOrStage3Death"));
    }
}
