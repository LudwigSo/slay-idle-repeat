using SlayIdleRepeat.Adapters.Platform.Host;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the real host reader — the implementation a desktop build, the server
/// and every tool actually boot on.
/// </summary>
/// <remarks>
/// 🔒 <see cref="CreateWithUnidentifiedDevice"/> is the plain constructor, and that is not a
/// shortcut. This host is <em>always</em> in that state: the BCL exposes an architecture, an OS
/// string and a process, and none of them is a device model. The suite's absence case therefore runs
/// against the real adapter's real answer rather than against an arrangement built for it.
/// </remarks>
[ContractFixtureFor(typeof(HostPlatformInfo))]
public sealed class HostPlatformInfoContractTests : IPlatformInfoPortContractTests
{
    protected override IPlatformInfoPort Create() => new HostPlatformInfo();

    protected override IPlatformInfoPort CreateWithUnidentifiedDevice() => new HostPlatformInfo();
}
