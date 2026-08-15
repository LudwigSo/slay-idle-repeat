namespace SlayIdleRepeat.Core.Rules.Feats;

/// <summary>One lifetime-counter advance a domain event implies: which counter, and by how much.</summary>
/// <param name="CounterId">The counter's stable <c>lower_snake_case</c> id.</param>
/// <param name="Amount">How much to add. Always positive — a projection that produced zero would be a row with nothing to say.</param>
/// <remarks>
/// An intent rather than a write: <c>Model</c> may not reference <c>Rules</c>, so the projection
/// cannot touch the aggregate it describes. <c>GameRules.Apply</c> is what turns these into counts.
/// </remarks>
internal readonly record struct FeatCounterIncrement(string CounterId, long Amount);
