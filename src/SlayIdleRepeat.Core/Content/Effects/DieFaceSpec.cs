namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The replacement face a <see cref="EffectOp.MODIFY_DIE_FACE"/> writes — `18` §7.9's
/// <c>{"kind":"Pip","value":4}</c> and §9.2's <c>{"kind":"Star"}</c>.
/// </summary>
/// <param name="Kind">
/// One of `04` §1's six <c>DieFaceKind</c> names: <c>Pip · Star · Surge · Fortune · Void · Chain</c>.
/// </param>
/// <param name="Value">Pips, for <c>Pip</c> faces; ignored otherwise (`04` §1). <c>null</c> when absent.</param>
/// <remarks>
/// ⚠️ <b><see cref="Kind"/> is a string, deliberately.</b> The closed set belongs to `04` §1's
/// <c>DieFaceKind</c>, which is the dice system's vocabulary and the dice milestone's type to
/// declare. Restating it here would be exactly the duplicated-vocabulary failure the single-DSL
/// rule exists to prevent — two enums that must agree and nothing making them.
/// <c>game-data/schema/effect.schema.json</c> still closes the set, enumerating `04` §1's six names.
/// <para>
/// ⚠️ `04` §1's <c>DieFace</c> also carries a <c>Tier</c> (0..3). No `18` example writes one on
/// <c>newFace</c>, so it is not declared here: `18` §10's route covers the design that needs it.
/// </para>
/// </remarks>
public sealed record DieFaceSpec(string Kind, int? Value = null);
