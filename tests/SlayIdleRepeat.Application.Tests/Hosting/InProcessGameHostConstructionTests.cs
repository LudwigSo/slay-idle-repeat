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
/// <remarks>
/// "Stated" and "non-null" are not the same requirement, and the inbox store is where they come
/// apart: it must be stated and it may be stated as absent. Both halves are cases below.
/// </remarks>
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
        var messages = new InMemoryMessageRepository(clock);

        // The control first: with every part supplied the very same call succeeds, so each refusal
        // below is about the argument it names rather than about a host that refuses to be built.
        Should.NotThrow(
            () => new InProcessGameHost(cache, clock, ids, content, entitlements, flags, sinks, messages),
            "a fully stated composition was refused, so nothing below distinguishes one missing part " +
            "from another.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(null!, clock, ids, content, entitlements, flags, sinks, messages))
            .ParamName.ShouldBe("cache");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, null!, ids, content, entitlements, flags, sinks, messages))
            .ParamName.ShouldBe("clock");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, null!, content, entitlements, flags, sinks, messages))
            .ParamName.ShouldBe("ids");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, null!, entitlements, flags, sinks, messages))
            .ParamName.ShouldBe("content");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, null!, flags, sinks, messages))
            .ParamName.ShouldBe(
                "entitlements",
                "no port resolves this yet, which is exactly why it may not be supplied silently: a " +
                "host that filled it in would be deciding the player's subscription itself.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, entitlements, null!, sinks, messages))
            .ParamName.ShouldBe(
                "flags",
                "…and the same for the kill switches, where the filled-in value decides what is offline.");

        Should.Throw<ArgumentNullException>(
                () => new InProcessGameHost(cache, clock, ids, content, entitlements, flags, null!, messages))
            .ParamName.ShouldBe("sinks");
    }

    /// <summary>
    /// The one part whose <c>null</c> is a value: a process with no message store says so, and is
    /// built.
    /// </summary>
    /// <remarks>
    /// The counterpart to the refusals above, and the reason <c>messages</c> is not among them. The
    /// shipped client passes exactly this — mail is a server-held account fact — so a host that
    /// refused it could not be composed at all. What a null buys is stated where it is read: the
    /// command that needs an inbox is then dispatched against none, and the rules refuse it as the
    /// loading defect it is rather than answering a player that their rewards are gone.
    /// </remarks>
    [Fact]
    public void A_process_with_no_message_store_states_that_and_is_still_built() =>
        Should.NotThrow(
            () => new InProcessGameHost(
                new InMemoryLocalCache(),
                new AdjustableClock(),
                new CountingIdGenerator(),
                Worlds.Content,
                new Entitlements(hasPlus: false, expiresAtUtc: null),
                new FeatureFlags(pvpEnabled: true, plusOfferEnabled: true, mailEnabled: true, [], []),
                [],
                messages: null),
            "the absence of an inbox store is a composition a real root makes, not an omission.");

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
            new[] { "cache", "clock", "ids", "content", "entitlements", "flags", "sinks", "messages" },
            "the parts a composition root states, in order — these are the names the refusals above " +
            "report back, so the two cases have to be talking about the same list.");

        parameters.Where(parameter => parameter.HasDefaultValue)
            .Select(parameter => parameter.Name)
            .ShouldBeEmpty(
                "an optional parameter is a value the host chose on the caller's behalf, and for the " +
                "entitlement and the flags there is no authorised source it could have chosen from.");
    }
}
