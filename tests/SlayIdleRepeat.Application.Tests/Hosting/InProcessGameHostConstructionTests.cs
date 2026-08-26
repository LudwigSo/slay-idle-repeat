using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Hosting;

/// <summary>
/// The host is the composed object; a composition root has to state every part of it. Nothing here is
/// optional, and the two values with no port to resolve them are the ones that must not be.
/// </summary>
public sealed class InProcessGameHostConstructionTests
{
    [Fact]
    public void The_host_refuses_to_be_built_without_any_one_of_its_parts()
    {
        var cache = new InMemoryLocalCache();
        var clock = new AdjustableClock();
        var ids = new CountingIdGenerator();
        var content = Worlds.Content;
        var entitlements = new Entitlements(hasPlus: false, expiresAtUtc: null);
        var flags = new FeatureFlags(pvpEnabled: true, plusOfferEnabled: true, mailEnabled: true, [], []);
        IReadOnlyList<IDomainEventSink> sinks = [];

        // The control first: with every part supplied the very same call succeeds, so each refusal
        // below is about the argument it names rather than about a host that refuses to be built.
        Should.NotThrow(
            () => new InProcessGameHost(cache, clock, ids, content, entitlements, flags, sinks),
            "a fully stated composition was refused, so nothing below distinguishes one missing part " +
            "from another.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(null!, clock, ids, content, entitlements, flags, sinks))
            .ParamName.ShouldBe("cache");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, null!, ids, content, entitlements, flags, sinks))
            .ParamName.ShouldBe("clock");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, null!, content, entitlements, flags, sinks))
            .ParamName.ShouldBe("ids");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, null!, entitlements, flags, sinks))
            .ParamName.ShouldBe("content");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, null!, flags, sinks))
            .ParamName.ShouldBe(
                "entitlements",
                "no port resolves this yet, which is exactly why it may not be supplied silently: a " +
                "host that filled it in would be deciding the player's subscription itself.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, entitlements, null!, sinks))
            .ParamName.ShouldBe(
                "flags",
                "…and the same for the kill switches, where the filled-in value decides what is offline.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, entitlements, flags, null!))
            .ParamName.ShouldBe("sinks");
    }

    /// <summary>
    /// …and none of them carries a default a caller could simply leave out, which is the other way an
    /// unresolved value ends up decided by the host instead of by the composition root.
    /// </summary>
    [Fact]
    public void No_part_of_the_host_carries_a_default_a_caller_could_omit()
    {
        var constructors = typeof(InProcessGameHost).GetConstructors();

        constructors.ShouldHaveSingleItem(
            "the host is composed exactly one way, which is what makes 'everything it needs' an " +
            "answerable question at all.");

        var parameters = constructors[0].GetParameters();

        parameters.Select(parameter => parameter.Name).ShouldBe(
            new[] { "cache", "clock", "ids", "content", "entitlements", "flags", "sinks" },
            "the parts a composition root states, in order — these are the names the refusals above " +
            "report back, so the two cases have to be talking about the same list.");

        parameters.Where(parameter => parameter.HasDefaultValue)
            .Select(parameter => parameter.Name)
            .ShouldBeEmpty(
                "an optional parameter is a value the host chose on the caller's behalf, and for the " +
                "entitlement and the flags there is no authorised source it could have chosen from.");
    }
}
