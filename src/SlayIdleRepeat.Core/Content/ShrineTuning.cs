using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §7a.5 — <c>TILE_SHRINE</c>'s pool of ten buffs, read out of
/// <c>tuning/currencies.json#/inRunIncome/shrineBuffPool</c>.
/// </summary>
/// <remarks>
/// <para>
/// §7a.5: <em>"a shrine offers 2 distinct options drawn seeded (stream 'shrine', equal weights) from
/// this pool of 10. Buffs are permanent for the run and stack additively. Cleanse rule: with at
/// least one active cleansable curse, option slot 2 is always a Cleanse."</em>
/// </para>
/// <para>
/// ⚠️ <b><c>Stat</c> and <c>Magnitude</c> are nullable and the null is authored, not missing.</b>
/// <c>SHR_HEAL</c> carries <c>"stat": null, "magnitude": null</c> and only an
/// <c>immediateHealPctMaxHp</c>: it is a heal rather than a stat buff, and
/// <c>game-data/README.md</c>'s rule — <em>"a hole that is null is greppable, and a hole filled with
/// a plausible-looking number is invisible"</em> — is why this reader carries the nulls through
/// instead of defaulting them to <c>0</c>.
/// </para>
/// <para>
/// ⚠️ <b>Nothing in <c>Core</c> consumes the stat half of a buff yet.</b> §7a.5's "permanent for the
/// run and stack additively" needs a run-scoped stat-aggregation consumer that does not exist —
/// <c>SubjectSetFloorTests</c>' and <c>GapRegister</c>'s <c>ShrineBuff</c>/M3-11 territory. This
/// reader therefore reports the whole authored row and <c>ShrineResolver</c> applies only the
/// immediate-heal component, which is the one part that is mechanically real today.
/// </para>
/// </remarks>
internal sealed class ShrineTuning
{
    /// <summary>The document `03` §7a.5's shrine block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string PoolPointer = DocumentPath + "#/inRunIncome/shrineBuffPool";

    /// <summary>`03` §7a.5 — the pool of ten buffs.</summary>
    internal const string BuffsReference = PoolPointer + "/buffs";

    /// <summary>`03` §7a.5 — how many distinct options a shrine offers. 2 as shipped.</summary>
    internal const string OptionsOfferedReference = PoolPointer + "/optionsOffered";

    private ShrineTuning(IReadOnlyList<ShrineBuffPoolEntry> buffs, int optionsOffered)
    {
        Buffs = buffs;
        OptionsOffered = optionsOffered;
    }

    /// <summary>`03` §7a.5's pool, in the order the document lists them.</summary>
    /// <remarks>
    /// 🔒 The order is load-bearing: the resolver draws by index off `14` §8.1's <c>shrine</c>
    /// stream, so re-ordering the pool changes which buff every existing run seed offers.
    /// </remarks>
    internal IReadOnlyList<ShrineBuffPoolEntry> Buffs { get; }

    /// <summary>`03` §7a.5 — how many distinct options one shrine offers.</summary>
    internal int OptionsOffered { get; }

    /// <summary>Reads the shrine block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
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

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = BuffsReference + "/" + Text(i);
            var entry = array.Items[i];

            var id = Member(entry, "id", pointer).AsText(pointer + "/id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidTunableException(pointer + "/id", "A shrine buff id must not be blank.");
            }

            var displayName = Member(entry, "displayName", pointer).AsText(pointer + "/displayName");

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

/// <summary>
/// One row of `03` §7a.5's shrine pool.
/// </summary>
/// <remarks>
/// ⚠️ <b>Named <c>ShrineBuffPoolEntry</c> and not <c>ShrineBuff</c>, deliberately.</b>
/// <c>SlayIdleRepeat.Architecture.Tests</c> watches for the simple name <c>ShrineBuff</c> as the
/// sentinel for M3-11's run-buff <em>engine</em> — the thing that would actually apply and aggregate
/// these — and a type of that name appearing in <c>Core</c> would retire that deferral for the wrong
/// reason. This type is the authored <em>row</em>, not the buff in effect.
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
