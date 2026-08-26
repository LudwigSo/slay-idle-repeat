using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// ⚠️ PLACEHOLDER — the <see cref="IPrincipalResolver"/> that stands in until M5-06 lands real
/// auth (device accounts, JWT validation, refresh rotation). It reads <c>Bearer &lt;playerId&gt;</c>
/// as the player it names, which authenticates nothing: anyone naming a player IS that player.
/// Tolerable only while no real client talks to this server; it must not survive M5-06.
/// </summary>
public sealed class PlaceholderBearerPlayerIdResolver : IPrincipalResolver
{
    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc/>
    public PrincipalResolution Resolve(string? authorizationHeader)
    {
        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(authorizationHeader[BearerPrefix.Length..]))
        {
            return PrincipalResolution.Unauthorized();
        }

        return PrincipalResolution.Resolved(new PlayerId(authorizationHeader[BearerPrefix.Length..]));
    }
}
