namespace SlayIdleRepeat.Core.Commands;

/// <summary>The base of the command vocabulary: what the player intends, and the second argument of <c>GameRules.Apply</c>.</summary>
/// <remarks>
/// <para>
/// Public and abstract: every composition root builds a concrete command, but a bare
/// <c>GameCommand</c> names no intent a rule can act on.
/// </para>
/// <para>
/// Declares no members. A command's wire name and <c>CommandKind</c> live on its row in
/// <c>GameRules</c>' dispatch table rather than here, so they stay declared once, in the one place
/// a <c>Type</c> can be looked up without an instance. Time, entropy and a player id are not
/// members either — they arrive through <c>GameContext</c> and <c>WorldSlice</c>, so a command
/// can't become a second, unvalidated door into <c>Apply</c>.
/// </para>
/// <para>
/// A <c>record</c>, so equality is by value: idempotency replays a stored outcome for a repeated
/// command, and two commands describing the same intent must compare equal for that to mean anything.
/// </para>
/// </remarks>
public abstract record GameCommand;
