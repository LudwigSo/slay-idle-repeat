namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The in-run gear acquisition rates, read out of <c>tuning/drops.json#/acquisitionRates</c>: how
/// much gear each kind of kill drops.
/// </summary>
/// <remarks>
/// 🔴 <b>Signatures only — every body is unfilled.</b> The tests that name this type are written and
/// red; the reader itself is the implementation phase's.
/// <para>
/// 🔒 <c>treasureTileChance</c> is authored <c>null</c> and is deliberately not read: a treasure tile
/// drops no gear, so a property here would be a hole waiting for a plausible value.
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

    /// <summary>How many items one Elite kill drops.</summary>
    internal int EliteKillItems => throw new NotImplementedException();

    /// <summary>The fewest items a Boss kill drops.</summary>
    internal int BossKillItemsMin => throw new NotImplementedException();

    /// <summary>The most items a Boss kill drops.</summary>
    internal int BossKillItemsMax => throw new NotImplementedException();

    /// <summary>The chance an ordinary enemy kill drops anything at all, between 0 and 1.</summary>
    internal double NormalEnemyChance => throw new NotImplementedException();

    /// <summary>Reads the acquisition rates.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The rates.</returns>
    internal static GearAcquisitionTuning Read(ContentSnapshot content) =>
        throw new NotImplementedException();
}
