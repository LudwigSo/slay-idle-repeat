using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §7a.3 — <c>TILE_TREASURE</c>'s three payout profiles, read out of
/// <c>tuning/currencies.json#/inRunIncome/treasureProfiles</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each treasure tile rolls exactly one profile, weighted, off `14` §8.1's <c>treasure</c> stream,
/// and the amounts are multiples of <c>M(c)</c> — see <see cref="ChapterScalarTuning"/>, which owns
/// that curve for every consumer rather than each reader carrying its own copy.
/// </para>
/// <para>
/// ⚠️ <b>No gear column, and that is the document's own ruling rather than an omission.</b> §7a.3:
/// <em>"Treasure tiles never drop gear in v1."</em> A nullable gear field authored here would imply
/// the profile could carry one.
/// </para>
/// </remarks>
internal sealed class TreasureTuning
{
    /// <summary>The document `03` §7a.3's treasure block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    /// <summary>`03` §7a.3 — the weighted profile array.</summary>
    internal const string ProfilesReference = DocumentPath + "#/inRunIncome/treasureProfiles/profiles";

    private TreasureTuning(IReadOnlyList<TreasureProfile> profiles)
    {
        Profiles = profiles;
    }

    /// <summary>`03` §7a.3's profiles, in the order the document lists them.</summary>
    /// <remarks>
    /// 🔒 The order is load-bearing: <c>DeterministicRng.WeightedPick</c> walks a table in order, so
    /// a reader that sorted these would change which profile a given draw resolves to and therefore
    /// what every existing run seed pays out.
    /// </remarks>
    internal IReadOnlyList<TreasureProfile> Profiles { get; }

    /// <summary>Reads the treasure block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static TreasureTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var array = content.Read(ProfilesReference);
        if (array.Kind != ContentValueKind.Array || array.Items.Count == 0)
        {
            throw new InvalidTunableException(
                ProfilesReference,
                "03 §7a.3 authors treasureProfiles.profiles as a non-empty weighted array (three " +
                "rows as shipped: COIN_HOARD, STONE_CACHE, DUST_TROVE). This document authors " +
                array + ".");
        }

        var profiles = new TreasureProfile[array.Items.Count];
        var totalWeight = 0.0;

        for (var i = 0; i < array.Items.Count; i++)
        {
            var pointer = ProfilesReference + "/" + Text(i);
            var entry = array.Items[i];

            var id = Member(entry, "id", pointer).AsText(pointer + "/id");
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidTunableException(pointer + "/id", "A treasure profile id must not be blank.");
            }

            var weight = Member(entry, "weight", pointer).AsDouble(pointer + "/weight");
            if (!double.IsFinite(weight) || weight < 0.0)
            {
                throw new InvalidTunableException(
                    pointer + "/weight",
                    "A draw weight must be a finite, non-negative number — anything else makes " +
                    "DeterministicRng.WeightedPick's cumulative walk non-monotonic and its answer " +
                    "arbitrary. This document authors " + Text(weight) + ".");
            }

            totalWeight += weight;

            profiles[i] = new TreasureProfile(
                id,
                weight,
                Amount(entry, "crowns", pointer),
                Amount(entry, "enhanceStones", pointer),
                Amount(entry, "mergeDust", pointer));
        }

        if (totalWeight <= 0.0)
        {
            throw new InvalidTunableException(
                ProfilesReference,
                "Every treasure profile has weight zero, so no profile can ever be drawn and a " +
                "treasure tile would pay nothing at all. 03 §7a.3 authors 55/30/15.");
        }

        return new TreasureTuning(Array.AsReadOnly(profiles));
    }

    private static long Amount(ContentValue entry, string name, string pointer)
    {
        var reference = pointer + "/" + name;
        var amount = Member(entry, name, pointer).AsInt64(reference);

        if (amount < 0)
        {
            throw new InvalidTunableException(
                reference,
                "A treasure profile pays out; it never charges. This document authors " +
                Text(amount) + ". ⚠️ Zero IS legal and is authored — DUST_TROVE pays no Enhance " +
                "Stones and the other two pay no Merge Dust — which is why the resolver skips a " +
                "zero column rather than emitting a zero-delta CurrencyChanged for it.");
        }

        return amount;
    }

    private static ContentValue Member(ContentValue obj, string name, string pointer) =>
        obj.TryGetMember(name, out var value) && value is not null
            ? value
            : throw new MissingContentException(pointer + "/" + name, "'" + name + "' resolves to nothing");

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One authored treasure payout profile (`03` §7a.3).</summary>
/// <param name="Id">The profile id, e.g. <c>COIN_HOARD</c>.</param>
/// <param name="Weight">Its share of the weighted draw off `14` §8.1's <c>treasure</c> stream.</param>
/// <param name="Crowns">Crowns paid, before <c>M(c)</c>. Never negative; zero is legal.</param>
/// <param name="EnhanceStones">Enhance Stones paid, before <c>M(c)</c>. Never negative; zero is legal.</param>
/// <param name="MergeDust">Merge Dust paid, before <c>M(c)</c>. Never negative; zero is legal.</param>
internal readonly record struct TreasureProfile(
    string Id, double Weight, long Crowns, long EnhanceStones, long MergeDust);
