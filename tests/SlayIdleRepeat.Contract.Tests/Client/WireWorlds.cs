using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Contract.Tests.Server;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// One real player mid-run, projected the way the wire projects it — the state both
/// <see cref="IGameApiPortContractTests"/> fixtures answer with.
/// </summary>
/// <remarks>
/// Built once and shared so the fake and the scripted HTTP handler are answering about the SAME run:
/// a fixture that rebuilt the fixture per case would be comparing two implementations against two
/// different worlds and calling the result a shared contract.
/// </remarks>
internal static class WireWorlds
{
    private static readonly Lazy<(PlayerWireProjection Profile, RunWireProjection Run, string Hash)>
        LazyWorld = new(() =>
        {
            var profile = PersistenceWorlds.ProfileInARun();
            var run = profile.ActiveRun!;

            return (
                WireProjections.Of(profile.Player),
                WireProjections.Of(run),
                WireProjections.HashPlayerAndRun(profile.Player, run));
        });

    /// <summary>The player projection.</summary>
    internal static PlayerWireProjection Profile => LazyWorld.Value.Profile;

    /// <summary>The run projection — the run seed does not cross this line.</summary>
    internal static RunWireProjection Run => LazyWorld.Value.Run;

    /// <summary>The wire state hash over both, from the one canonical writer rather than a literal.</summary>
    internal static string StateHash => LazyWorld.Value.Hash;
}
