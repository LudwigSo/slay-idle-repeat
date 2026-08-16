namespace SlayIdleRepeat.Core.Rules.Feats;

/// <summary>One lifetime-counter advance a domain event implies: which counter, and by how much.</summary>
/// <param name="CounterId">The counter's stable <c>lower_snake_case</c> id.</param>
/// <param name="Amount">How much to add. Always positive.</param>
/// <remarks><c>Model</c> may not reference <c>Rules</c>, so the projection describes the write rather than performing it.</remarks>
internal readonly record struct FeatCounterIncrement(string CounterId, long Amount);
