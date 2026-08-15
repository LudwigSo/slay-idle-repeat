namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The replacement face a <see cref="EffectOp.MODIFY_DIE_FACE"/> writes — `18` §7.9's
/// <c>{"kind":"Pip","value":4}</c> and §9.2's <c>{"kind":"Star"}</c>.
/// </summary>
/// <param name="Kind">
/// One of `04` §1's six <c>DieFaceKind</c> names: <c>Pip · Star · Surge · Fortune · Void · Chain</c>.
/// </param>
/// <param name="Value">Pips, for <c>Pip</c> faces; ignored otherwise (`04` §1). <c>null</c> when absent.</param>
/// <param name="Tier">
/// 🔒 <b>Added by M3-04 under `18` §10.</b> `04` §1's <c>DieFace.Tier</c> (0..3), scaling the
/// replacement face's non-movement effect (e.g. Surge's heal %). <c>null</c> when the author wrote
/// none — M3-04's Dice Forge table always writes tier 0 for the special kinds it grants (see
/// <c>DiceForgeUpgradeTable</c>'s own remarks for why), so <c>null</c> reads as "tier 0" for a
/// consumer that resolves one, exactly as an absent <see cref="Value"/> already does for a
/// non-<c>Pip</c> kind.
/// </param>
/// <remarks>
/// ⚠️ <b><see cref="Kind"/> is a string, deliberately.</b> The closed set belongs to `04` §1's
/// <c>DieFaceKind</c>, which is the dice system's vocabulary and the dice milestone's type to
/// declare. Restating it here would be exactly the duplicated-vocabulary failure the single-DSL
/// rule exists to prevent — two enums that must agree and nothing making them.
/// <c>game-data/schema/effect.schema.json</c> still closes the set, enumerating `04` §1's six names.
/// <para>
/// 🔒 <b>M3-04 promoted the real <c>DieFaceKind</c>
/// (<see cref="SlayIdleRepeat.Core.Content.Dice.DieFaceKind"/>) alongside this string, not in
/// place of it.</b> This record is the DSL's wire-facing shape —
/// <c>game-data/schema/effect.schema.json</c> validates <see cref="Kind"/> as one of six literal
/// strings, and <c>EffectOpValidation</c>'s structural checks are written against that shape — so
/// widening <see cref="Kind"/> itself to the enum would break both without buying anything: JSON has
/// no closed-enum literal, only strings. <c>Rules.Dice.DieFaceKindCodec</c> is the one place that
/// parses this string into the real enum, at the point a <c>MODIFY_DIE_FACE</c> effect is actually
/// resolved — mirroring how <see cref="Effects.DieFaceIndex"/> already turns
/// <c>MODIFY_DIE_FACE.FaceIndex</c>'s wire token into a real value rather than being one itself.
/// </para>
/// </remarks>
public sealed record DieFaceSpec(string Kind, int? Value = null, int? Tier = null);
