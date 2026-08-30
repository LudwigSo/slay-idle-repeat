using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>The contract, run against the scripted fake the connection machinery is written over.</summary>
/// <remarks>
/// The projections it serves are real ones, produced by the domain and projected by the wire's own
/// factory — a fake serving a hand-assembled projection would be answering with a state the server
/// could never have sent.
/// </remarks>
[ContractFixtureFor(typeof(InMemoryGameApi))]
public sealed class InMemoryGameApiContractTests : IGameApiPortContractTests
{
    protected override IGameApiPort Create() => Api();

    protected override IGameApiPort CreateRefusing() => Api().RefuseEveryCommand(ScriptedRejection);

    protected override IGameApiPort CreateUnreachable() => Api().MakeUnavailable();

    protected override WireCredentials KnownCredentials =>
        new(InMemoryGameApi.StartDeviceId, InMemoryGameApi.StartDeviceSecret);

    protected override RunId KnownRun => WireWorlds.Run.Id;

    private static InMemoryGameApi Api() => new(WireWorlds.Profile, WireWorlds.Run);
}
