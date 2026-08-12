using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>Which `15` §D1 rule a delivered file name broke. Exactly one, the first that fails.</summary>
/// <remarks>
/// 🔒 Steering rule S2: pin the identity, not the symptom. "Not a §D1 name" is four different
/// defects with four different fixes, and a reviewer holding 942 rejected files needs to know
/// whether the generator emitted CamelCase or whether somebody renamed a file away from its
/// manifest id.
/// </remarks>
public enum AssetNameRejection
{
    /// <summary>Nothing is wrong with the name.</summary>
    None,

    /// <summary>`15` §C delivers PNG-32; every §D1 example ends <c>.png</c>. This one does not.</summary>
    MissingPngExtension,

    /// <summary>
    /// The stem is not snake_case: `15` §D1's grammar is lowercase alphanumeric segments joined by
    /// underscores, so CamelCase, a hyphen, a space or a capital anywhere is out.
    /// </summary>
    NotSnakeCase,

    /// <summary>
    /// The leading segment is not one of the §D1 category prefixes. The list is
    /// <see cref="ManifestValidator.IdPrefixes"/> — M8-09's, not a second copy.
    /// </summary>
    UnknownCategoryPrefix,

    /// <summary>
    /// The name is a well-formed §D1 name for some other asset. The stem must equal the manifest
    /// id, or the atlas metadata M8-10 writes points at a file nobody delivers.
    /// </summary>
    StemDoesNotMatchManifestId,
}

/// <summary>What <see cref="AssetNaming.Validate"/> concluded about one delivered file name.</summary>
/// <param name="Rejection">
/// <see cref="AssetNameRejection.None"/> when the name is good, else the first rule it broke.
/// </param>
/// <param name="FileName">The name that was checked, as given.</param>
/// <param name="Stem">The name without its extension, or null when there was no extension to strip.</param>
/// <param name="Reason">Why, naming the rule and the offending part. Empty when accepted.</param>
public sealed record AssetNameResult(
    AssetNameRejection Rejection, string FileName, string? Stem, string Reason)
{
    /// <summary>True when the name satisfies `15` §D1 and matches the manifest id it was checked against.</summary>
    public bool IsValid => Rejection == AssetNameRejection.None;
}

/// <summary>
/// `15` §D1's naming grammar: <c>{category}_{subcategory}_{id}[_{variant}][_{state}].png</c>, snake_case.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The prefix list is not duplicated.</b> <see cref="ManifestValidator.IdPrefixes"/> is
/// M8-09's single list of §D1 categories, and it carries a comment recording what happened the last
/// time that list existed twice: a regex spelling the twelve out again meant adding a prefix to the
/// public set left every id carrying it still reported malformed. This type validates the shape of
/// a delivered <em>file name</em> — the extension, the stem, and the stem against the register's id
/// — and asks M8-09 which prefixes exist.
/// </para>
/// <para>
/// 🔒 <b>The rules are checked in the order <see cref="AssetNameRejection"/> declares them</b> —
/// extension, snake_case, category prefix, then the manifest id — and the first one broken is the
/// one reported. Later rules are not independent of earlier ones (there is no stem to compare
/// before the extension is stripped, and no prefix to look up before the stem parses), so reporting
/// a later rule over an earlier one would name a consequence as the cause.
/// </para>
/// <para>
/// 🔒 §D1's grammar has optional segments (<c>[_{variant}][_{state}]</c>), so segment <em>count</em>
/// is not a rule: <c>pet_stormfang_idle.png</c> has three and <c>chr_hero_weapon_blade_s.png</c>
/// has five, and both are the doc's own examples. Requiring a minimum of three would reject
/// nothing real and invent a rule the doc does not state.
/// </para>
/// </remarks>
public static class AssetNaming
{
    /// <summary>`15` §C delivers PNG-32, and every §D1 example carries this extension.</summary>
    public const string PngExtension = ".png";

    /// <summary>Checks one delivered file name against `15` §D1 and against the row it claims to be.</summary>
    /// <param name="fileName">The delivered name, e.g. <c>icon_perk_executioner.png</c>.</param>
    /// <param name="manifestId">The `15` §D1 id of the row this file delivers.</param>
    public static AssetNameResult Validate(string fileName, string manifestId) =>
        throw new NotImplementedException();

    /// <summary>
    /// The `15` §D1 category prefix of an id — the segment before the first underscore.
    /// </summary>
    /// <remarks>
    /// 🔒 This is the key <see cref="SilhouetteRegistry"/> and `15` Part F item 11 mean by "the same
    /// category": <c>chr</c>, <c>pet</c>, <c>icon</c> and so on. An id whose prefix is not one of
    /// <see cref="ManifestValidator.IdPrefixes"/> is a loud failure rather than a category of one.
    /// </remarks>
    /// <param name="assetId">A `15` §D1 asset id.</param>
    public static string CategoryOf(string assetId) => throw new NotImplementedException();
}
