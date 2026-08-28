using System.Globalization;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Application.Services.Analytics;

/// <summary>
/// Turns one accepted command's batch into the analytics events it honestly carries — names from
/// <see cref="AnalyticsVocabulary"/> only, properties only from what the command and state say.
/// </summary>
/// <remarks>
/// A domain event with no authored analytics name is ignored, never improvised into one. No
/// timestamps: capture time is the backend's, and nothing in this layer reads a clock.
/// </remarks>
public static class AnalyticsTranslator
{
    private static readonly IReadOnlyList<AnalyticsEvent> Nothing =
        Array.AsReadOnly(Array.Empty<AnalyticsEvent>());

    /// <summary>The analytics events <paramref name="batch"/> carries, in emission order.</summary>
    /// <param name="batch">One accepted command's delivery.</param>
    /// <returns>Zero or more events. Empty when the batch carries nothing the vocabulary names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is null.</exception>
    public static IReadOnlyList<AnalyticsEvent> Translate(DispatchedEvents batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // Built only once something is emitted: this runs on every accepted command, and most
        // commands carry no analytics fact at all.
        List<AnalyticsEvent>? emitted = null;

        // The command's own fact first, then the domain events in the order the domain produced them.
        switch (batch.Command)
        {
            case StartRunCommand when batch.State.Run is { } opened:
                (emitted ??= []).Add(new AnalyticsEvent(AnalyticsVocabulary.RunStart, new Dictionary<string, string>
                {
                    ["run_id"] = opened.Id.Value,
                    ["chapter"] = Invariant(opened.ChapterId),
                    ["tier"] = opened.Tier.ToString(),
                }));
                break;

            case EndRunCommand when batch.State.Run is { } ended:
                (emitted ??= []).Add(new AnalyticsEvent(AnalyticsVocabulary.RunEnd, new Dictionary<string, string>
                {
                    ["run_id"] = ended.Id.Value,
                    ["victory"] = ended.ToSnapshot().BossDefeated ? "true" : "false",
                }));
                break;

            case BeginSessionCommand session:
                (emitted ??= []).Add(new AnalyticsEvent(AnalyticsVocabulary.SessionStart, new Dictionary<string, string>
                {
                    ["client_version"] = session.ClientVersion,
                    ["content_hash"] = session.ContentHash,
                }));
                break;
        }

        foreach (var domainEvent in batch.Events)
        {
            switch (domainEvent)
            {
                case DiceRolled rolled:
                    (emitted ??= []).Add(DieRolled(rolled.Pips, "rolled"));
                    break;

                case FixedDieUsed spent:
                    (emitted ??= []).Add(DieRolled(spent.Pips, "fixed"));
                    break;

                case CurrencyChanged moved:
                    (emitted ??= []).Add(new AnalyticsEvent(
                        AnalyticsVocabulary.CurrencyChanged,
                        new Dictionary<string, string>
                        {
                            ["currency"] = moved.Id.ToString(),
                            ["delta"] = Invariant(moved.Delta),
                            ["reason"] = moved.Reason,
                        }));
                    break;
            }
        }

        return emitted ?? Nothing;
    }

    private static AnalyticsEvent DieRolled(int pips, string source) =>
        new(AnalyticsVocabulary.DieRolled, new Dictionary<string, string>
        {
            ["face"] = Invariant(pips),
            ["source"] = source,
        });

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
