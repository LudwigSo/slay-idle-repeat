namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The replacement face a <see cref="EffectOp.MODIFY_DIE_FACE"/> writes — e.g.
/// <c>{"kind":"Pip","value":4}</c> or <c>{"kind":"Star"}</c>.
/// </summary>
/// <param name="Kind">
/// One of the six <c>DieFaceKind</c> names: <c>Pip · Star · Surge · Fortune · Void · Chain</c>.
/// </param>
/// <param name="Value">Pips, for <c>Pip</c> faces; ignored otherwise. <c>null</c> when absent.</param>
/// <param name="Tier">
/// The replacement face's tier (0..3), scaling its non-movement effect (e.g. Surge's heal %).
/// <c>null</c> when the author wrote none, which reads as "tier 0" for a consumer that resolves
/// one, exactly as an absent <see cref="Value"/> already does for a non-<c>Pip</c> kind.
/// </param>
/// <remarks>
/// <see cref="Kind"/> is a string, deliberately: the closed set belongs to the dice system's own
/// <c>DieFaceKind</c> enum, and restating it here would be a duplicated vocabulary that has to
/// agree with nothing making it. This record is the DSL's wire-facing shape; a separate codec
/// parses this string into the real enum at the point a <c>MODIFY_DIE_FACE</c> effect is actually
/// resolved, mirroring how <see cref="Effects.DieFaceIndex"/> turns its own wire token into a real
/// value rather than being one itself.
/// </remarks>
public sealed record DieFaceSpec(string Kind, int? Value = null, int? Tier = null);
