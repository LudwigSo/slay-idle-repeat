using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// <c>TILE_SHRINE</c>'s pool of ten buffs, read out of
/// <c>tuning/currencies.json#/inRunIncome/shrineBuffPool</c>.
/// </summary>
/// <remarks>
/// <para>
/// A shrine offers 2 distinct options drawn seeded from this pool. Buffs are permanent for the run
/// and stack additively.
/// </para>
/// <para>
/// <see cref="ShrineBuffPoolEntry.Stat"/> and <see cref="ShrineBuffPoolEntry.Magnitude"/> are
/// nullable and the null is authored, not missing: <c>SHR_HEAL</c> is a heal rather than a stat
/// buff, so this reader carries the nulls through instead of defaulting them to <c>0</c>.
/// </para>
/// <para>
/// Nothing in <c>Core</c> consumes the stat half of a buff yet — that needs a run-scoped
/// stat-aggregation consumer that does not exist. This reader reports the whole authored row, and
/// <c>ShrineResolver</c> applies only the immediate-heal component, the one part that is
/// mechanically real today.
/// </para>
/// </remarks>
internal sealed class ShrineTuning
{
    /// <summary>The document the shrine block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string PoolPointer = DocumentPath + "#/inRunIncome/shrineBuffPool";

    /// <summary>The pool of ten buffs.</summary>
    internal const string BuffsReference = PoolPointer + "/buffs";

    /// <summary>How many distinct options a shrine offers. 2 as shipped.</summary>
    internal const string OptionsOfferedReference = PoolPointer + "/optionsOffered";

    private ShrineTuning(IReadOnlyList<ShrineBuffPoolEntry> buffs, int optionsOffered)
    {
        Buffs = buffs;
        OptionsOffered = optionsOffered;
    }

    /// <summary>The pool, in the order the document lists them.</summary>
    /// <remarks>
    /// The order is load-bearing: the resolver draws by index off the run's random stream, so
    /// re-ordering the pool changes which buff every existing run seed offers.
    /// </remarks>
    internal IReadOnlyList<ShrineBuffPoolEntry> Buffs { get; }

    /// <summary>How many distinct options one shrine offers.</summary>
    internal int OptionsOffered { get; }

    /// <summary>Reads the shrine block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ShrineTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var optionsOffered = content.ReadInt32(OptionsOfferedReference);
        if (optionsOffered < 1)
        {
            throw new InvalidTunableException(
                OptionsOfferedReference,
                "A shrine that offers nothing is not a shrine. 03 §7a.5 authors 2; this document " +
                "authors " + Text(optionsOffered) + ".");
        }

        var array = content.Read(BuffsReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                BuffsReference,
                "03 §7a.5 authors shrineBuffPool.buffs as a pool of ten. This document authors " +
                array + ".");
        }

        if (array.Items.Count < optionsOffered)
        {
            throw new InvalidTunableException(
                BuffsReference,
                "03 §7a.5 offers " + Text(optionsOffered) + " DISTINCT options from this pool, and " +
                "a pool of " + Text(array.Items.Count) + " cannot supply that many without " +
                "repeating one — which the sampling-without-replacement draw is written precisely " +
                "to prevent.");
        }

        var buffs = new ShrineBuffPoolEntry[array.Items.Count];
        var ids = new HashSet<string>(array.Items.Count, StringComparer.Ordinal);

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = BuffsReference + "/" + Text(i);
            var entry = array.Items[i];

            var id = Member(entry, "id", pointer).AsText(pointer + "/id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidTunableException(pointer + "/id", "A shrine buff id must not be blank.");
            }

            // Matters more here than in catalogues making the same check: the resolver guarantees
            // "2 distinct options" by index, which is only distinctness of the offer if the ids
            // differ too. A pool carrying one id twice would silently offer the same buff twice.
            if (!ids.Add(id))
            {
                throw new InvalidTunableException(
                    pointer + "/id",
                    "'" + id + "' is authored twice. 14 §6 makes a duplicate id a build failure, and " +
                    "here it would also defeat the distinct-options draw — see the comment above.");
            }

            var displayName = RequiredText(entry, "displayName", pointer);

            var stat = OptionalText(entry, "stat", pointer);
            var magnitude = OptionalNumber(entry, "magnitude", pointer);
            var immediateHeal = OptionalNumber(entry, "immediateHealPctMaxHp", pointer);

            if (stat is null && magnitude is null && immediateHeal is null)
            {
                throw new InvalidTunableException(
                    pointer,
                    "'" + id + "' authors neither a stat buff nor an immediate heal, so it does " +
                    "nothing at all. 03 §7a.5's pool is nine stat buffs plus SHR_HEAL, which " +
                    "carries only immediateHealPctMaxHp.");
            }

            // A stat buff is BOTH halves or neither. A row with one and not the other says nothing
            // a consumer could act on, and would otherwise pass the all-three-null check above and
            // read as a real buff.
            if (stat is null != magnitude is null)
            {
                throw new InvalidTunableException(
                    pointer,
                    "'" + id + "' authors " + (stat is null ? "a magnitude with no stat" : "a stat " +
                    "with no magnitude") + ". 03 §7a.5's stat rows carry both — the stat raised and " +
                    "the amount it is raised by — and SHR_HEAL carries neither, because it raises " +
                    "no stat at all.");
            }

            if (immediateHeal is { } heal && (!double.IsFinite(heal) || heal is < 0.0 or > 1.0))
            {
                throw new InvalidTunableException(
                    pointer + "/immediateHealPctMaxHp",
                    "An immediate heal is a share of Max HP in [0,1]. 03 §7a.5 authors 0.4 " +
                    "(SHR_HEAL) and 0.18 (SHR_HP); this document authors " + Text(heal) + ".");
            }

            buffs[i] = new ShrineBuffPoolEntry(id, displayName, stat, magnitude, immediateHeal);
        }

        return new ShrineTuning(Array.AsReadOnly(buffs), optionsOffered);
    }

    /// <summary>A required text member, refused when blank. The same helper shape
    /// <c>EventCatalogue</c> and <c>CurseTuning</c> use.</summary>
    private static string RequiredText(ContentValue obj, string name, string pointer)
    {
        var reference = pointer + "/" + name;
        var text = Member(obj, name, pointer).AsText(reference);

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidTunableException(reference, "'" + name + "' must not be blank.")
            : text;
    }

    /// <summary>
    /// A member that may be authored as a deliberate <c>null</c> — <c>SHR_HEAL</c>'s <c>stat</c> and
    /// <c>magnitude</c> — or omitted entirely, as <c>immediateHealPctMaxHp</c> is on the eight rows
    /// that do not heal. Both answer <c>null</c>; neither is defaulted to a value.
    /// </summary>
    private static string? OptionalText(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null && !value.IsUnauthorised
            ? value.AsText(pointer + "/" + name)
            : null;

    /// <inheritdoc cref="OptionalText"/>
    private static double? OptionalNumber(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null && !value.IsUnauthorised
            ? value.AsDouble(pointer + "/" + name)
            : null;

    private static ContentValue Member(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new MissingContentException(pointer + "/" + name, "'" + name + "' resolves to nothing");

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One row of the shrine pool.</summary>
/// <remarks>
/// Named <c>ShrineBuffPoolEntry</c> and not <c>ShrineBuff</c> deliberately: this is the authored
/// row, not the future run-buff engine's applied, aggregated buff.
/// </remarks>
/// <param name="Id">The buff id, e.g. <c>SHR_ATK</c>.</param>
/// <param name="DisplayName">The localisation key.</param>
/// <param name="Stat">The stat it raises, or <c>null</c> for <c>SHR_HEAL</c>, which raises none.</param>
/// <param name="Magnitude">The raise, or <c>null</c> for <c>SHR_HEAL</c>.</param>
/// <param name="ImmediateHealPctMaxHp">
/// A share of Max HP healed the moment the option is taken, or <c>null</c> where the row heals
/// nothing. <c>SHR_HP</c> carries both this and a stat raise; <c>SHR_HEAL</c> carries only this.
/// </param>
internal readonly record struct ShrineBuffPoolEntry(
    string Id, string DisplayName, string? Stat, double? Magnitude, double? ImmediateHealPctMaxHp);
