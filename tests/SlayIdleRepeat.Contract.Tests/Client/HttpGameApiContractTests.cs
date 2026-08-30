using SlayIdleRepeat.Adapters.Api.Http;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the real HTTP adapter over a scripted transport — no host, no socket,
/// no container.
/// </summary>
/// <remarks>
/// 🔒 This is the strongest fixture in the pair, because the bodies it parses are produced by
/// <c>WireJson.Render</c> from real <c>CommandResponse</c> and <c>RunStateResponse</c> values. It is
/// therefore evidence about the PROTOCOL and not only about the adapter: a member renamed on the
/// server's side of the wire fails here rather than in front of a player.
/// </remarks>
[ContractFixtureFor(typeof(HttpGameApi))]
public sealed class HttpGameApiContractTests : IGameApiPortContractTests, IDisposable
{
    private readonly List<HttpGameApi> _built = [];

    protected override IGameApiPort Create() => Over(new ScriptedGameServer());

    protected override IGameApiPort CreateRefusing() =>
        Over(new ScriptedGameServer { Refusal = ScriptedRejection });

    protected override IGameApiPort CreateUnreachable() => Over(new UnreachableServer());

    protected override WireCredentials KnownCredentials =>
        new(ScriptedGameServer.DeviceId, ScriptedGameServer.DeviceSecret);

    protected override RunId KnownRun => WireWorlds.Run.Id;

    /// <summary>Disposes every adapter the cases built — each owns an <c>HttpClient</c>, not the handler.</summary>
    public void Dispose()
    {
        foreach (var api in _built)
        {
            api.Dispose();
        }
    }

    private HttpGameApi Over(HttpMessageHandler handler)
    {
        var api = new HttpGameApi(new HttpGameApiOptions(), handler);
        _built.Add(api);

        return api;
    }
}
