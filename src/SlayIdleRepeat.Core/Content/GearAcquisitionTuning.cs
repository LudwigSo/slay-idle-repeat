namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The in-run gear acquisition rates, read out of <c>tuning/drops.json#/acquisitionRates</c>: how
/// much gear each kind of kill drops.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <c>treasureTileChance</c> is authored <c>null</c> and this reader deliberately does not take
/// it. A treasure tile drops no gear — no in-run drop trigger names one — so a property here would
/// be a hole waiting for a plausible value. <see cref="TreasureTileChanceReference"/> keeps the
/// absence greppable; the day a number appears there, the decision is a person's to make.
/// </para>
/// <para>
/// A hole is never a default: every read below goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw rather than answer zero.
/// </para>
/// </remarks>
internal sealed class GearAcquisitionTuning
{
    /// <summary>The document the acquisition rates live in.</summary>
    internal const string DocumentPath = "tuning/drops.json";

    /// <summary>The block that holds them.</summary>
    internal const string BlockReference = DocumentPath + "#/acquisitionRates";

    /// <summary>How many items one Elite kill drops.</summary>
    internal const string EliteKillItemsReference = BlockReference + "/eliteKillItems";

    /// <summary>The fewest items a Boss kill drops.</summary>
    internal const string BossKillItemsMinReference = BlockReference + "/bossKillItemsMin";

    /// <summary>The most items a Boss kill drops.</summary>
    internal const string BossKillItemsMaxReference = BlockReference + "/bossKillItemsMax";

    /// <summary>The chance an ordinary enemy kill drops anything at all.</summary>
    internal const string NormalEnemyChanceReference = BlockReference + "/normalEnemyChance";

    /// <summary>The rate no reader takes, left where a grep can find it.</summary>
    internal const string TreasureTileChanceReference = BlockReference + "/treasureTileChance";

    private GearAcquisitionTuning(
        int eliteKillItems, int bossKillItemsMin, int bossKillItemsMax, double normalEnemyChance)
    {
        EliteKillItems = eliteKillItems;
        BossKillItemsMin = bossKillItemsMin;
        BossKillItemsMax = bossKillItemsMax;
        NormalEnemyChance = normalEnemyChance;
    }

    /// <summary>How many items one Elite kill drops.</summary>
    internal int EliteKillItems { get; }

    /// <summary>The fewest items a Boss kill drops.</summary>
    internal int BossKillItemsMin { get; }

    /// <summary>The most items a Boss kill drops.</summary>
    internal int BossKillItemsMax { get; }

    /// <summary>The chance an ordinary enemy kill drops anything at all, between 0 and 1.</summary>
    internal double NormalEnemyChance { get; }

    /// <summary>Reads the acquisition rates.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The rates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static GearAcquisitionTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var eliteKillItems = ReadItemCount(content, EliteKillItemsReference);
        var bossKillItemsMin = ReadItemCount(content, BossKillItemsMinReference);
        var bossKillItemsMax = ReadItemCount(content, BossKillItemsMaxReference);

        if (bossKillItemsMax < bossKillItemsMin)
        {
            throw new InvalidTunableException(
                BossKillItemsMaxReference,
                $"A Boss kill drops between {AuthoredToken.Render(bossKillItemsMin)} and " +
                $"{AuthoredToken.Render(bossKillItemsMax)} items, which is an empty range — there is " +
                "no count to draw, so no Boss could pay out at all.");
        }

        if (bossKillItemsMax == int.MaxValue)
        {
            throw new InvalidTunableException(
                BossKillItemsMaxReference,
                "The Boss count is drawn over a half-open range whose upper bound is one past the " +
                "most a Boss may drop, and there is no number one past " +
                $"{AuthoredToken.Render(bossKillItemsMax)}. Refused here rather than left to wrap " +
                "into an inverted range at the draw.");
        }

        var normalEnemyChance = content.ReadDouble(NormalEnemyChanceReference);

        if (normalEnemyChance is < 0.0 or > 1.0)
        {
            throw new InvalidTunableException(
                NormalEnemyChanceReference,
                "The chance an ordinary kill drops anything is a probability, and this document " +
                $"authors {AuthoredToken.Render(normalEnemyChance)}. A value outside 0..1 makes the " +
                "kill either never drop or always drop, whichever side it fell off.");
        }

        return new GearAcquisitionTuning(
            eliteKillItems, bossKillItemsMin, bossKillItemsMax, normalEnemyChance);
    }

    /// <summary>One authored item count, refused when it cannot describe a payout.</summary>
    private static int ReadItemCount(ContentSnapshot content, string reference)
    {
        var count = content.ReadInt32(reference);

        if (count < 1)
        {
            throw new InvalidTunableException(
                reference,
                $"A kill that drops gear drops at least one item, and this document authors " +
                $"{AuthoredToken.Render(count)} — a kill kind that pays nothing is authored by " +
                "leaving its rate out, not by writing a zero nobody can tell from an oversight.");
        }

        return count;
    }
}
