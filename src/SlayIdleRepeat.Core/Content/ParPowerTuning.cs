namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The content-par table, read out of <c>tuning/par_power.json</c> — the power a chapter is balanced
/// against, and the number every rolled item's stats are a fraction of.
/// </summary>
/// <remarks>
/// <para>
/// Only the Normal column is read here. The Heroic and Mythic columns are the same content at a
/// difficulty multiplier and are the province of the encounter scaling, not of an item's power: an
/// item dropped in a Heroic run is still a chapter-N item.
/// </para>
/// <para>
/// The table is authored per cell and the design set marks it design-owned, so no fill formula is
/// applied here. A chapter with no row is refused by name rather than extrapolated — extrapolating
/// would silently invent the power target of a chapter nobody has balanced.
/// </para>
/// </remarks>
internal sealed class ParPowerTuning
{
    /// <summary>The document the par table lives in.</summary>
    internal const string DocumentPath = "tuning/par_power.json";

    /// <summary>The per-chapter rows.</summary>
    internal const string ParPowerReference = DocumentPath + "#/parPower";

    /// <summary>The column an item's power is scaled against.</summary>
    internal const string NormalColumn = "NORMAL";

    private readonly IReadOnlyDictionary<int, double> _targets;

    private ParPowerTuning(IReadOnlyDictionary<int, double> targets)
    {
        _targets = targets;
        Chapters = targets.Keys.Order().ToArray();
    }

    /// <summary>Every chapter the table authors a target for, ascending.</summary>
    internal IReadOnlyCollection<int> Chapters { get; }

    /// <summary>The power a chapter is balanced against.</summary>
    /// <param name="chapter">The chapter, from 1.</param>
    /// <returns>Its Normal-tier par power.</returns>
    /// <exception cref="InvalidTunableException">The table authors no row for this chapter.</exception>
    internal double ChapterPowerTarget(int chapter) =>
        _targets.TryGetValue(chapter, out var target)
            ? target
            : throw new InvalidTunableException(
                ParPowerReference,
                $"The par table authors no chapter {AuthoredToken.Render(chapter)}. Item power is a " +
                "fraction of a chapter's par, so a chapter with no par has no item power either — and " +
                "extending the curve here would invent the balance point of a chapter nobody has " +
                "tuned.");

    /// <summary>Reads the par table. Throws rather than defaulting on anything missing or unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ParPowerTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var array = content.Read(ParPowerReference);

        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                ParPowerReference,
                "The par table is a non-empty array, one row per chapter. This document authors " +
                array + ".");
        }

        var targets = new Dictionary<int, double>(array.Items.Count);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = ParPowerReference + "/" + AuthoredToken.Render(i);

            var chapter = content.ReadInt32(pointer + "/chapter");

            if (chapter < 1)
            {
                throw new InvalidTunableException(
                    pointer + "/chapter",
                    "Chapters are numbered from one, and this document authors " +
                    AuthoredToken.Render(chapter) + ".");
            }

            var targetReference = pointer + "/" + NormalColumn;
            var target = content.ReadDouble(targetReference);

            if (!double.IsFinite(target) || target <= 0.0)
            {
                throw new InvalidTunableException(
                    targetReference,
                    "A chapter's par power is a positive finite number — every item dropped in it is " +
                    $"a fraction of this figure — and this document authors " +
                    AuthoredToken.Render(target) + ".");
            }

            if (!targets.TryAdd(chapter, target))
            {
                throw new InvalidTunableException(
                    pointer + "/chapter",
                    $"Chapter {AuthoredToken.Render(chapter)} is authored twice, so which of the two " +
                    "pars an item scales against depends on which row a reader reaches first.");
            }
        }

        return new ParPowerTuning(targets);
    }
}
