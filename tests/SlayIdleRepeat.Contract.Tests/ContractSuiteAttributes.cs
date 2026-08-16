namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// Marks an abstract contract suite as the shared suite for one port (<c>23</c> §5 A8).
/// </summary>
/// <remarks>
/// 🔒 The link is an <b>explicit type reference</b>, never a name convention. A convention
/// (<c>I&lt;Port&gt;ContractTests</c>) is satisfied by a file that was renamed and by one that was
/// never written, and it cannot tell the two apart — which is precisely what
/// <see cref="ContractSuiteCoverageTests"/> exists to decide. Naming the port type also makes a
/// deleted port a compile error here rather than a rule that quietly stops matching.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContractSuiteForAttribute : Attribute
{
    /// <summary>Marks the decorated class as the shared contract suite for <paramref name="port"/>.</summary>
    /// <param name="port">The port interface this suite states the meaning of.</param>
    public ContractSuiteForAttribute(Type port) => Port = port;

    /// <summary>The port interface this suite states the meaning of.</summary>
    public Type Port { get; }
}

/// <summary>
/// Marks a concrete fixture as running a contract suite against one implementation (<c>23</c> §5 A8).
/// </summary>
/// <remarks>
/// The counterpart of <see cref="ContractSuiteForAttribute"/>, and explicit for the same reason: a
/// fixture attributed to the right implementation but deriving from the wrong suite is the drift a
/// naming convention cannot see, and <see cref="ContractSuiteCoverageTests"/> checks both halves.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContractFixtureForAttribute : Attribute
{
    /// <summary>Marks the decorated class as the contract fixture for <paramref name="implementation"/>.</summary>
    /// <param name="implementation">The concrete implementation this fixture runs the suite against.</param>
    public ContractFixtureForAttribute(Type implementation) => Implementation = implementation;

    /// <summary>The concrete implementation this fixture runs the suite against.</summary>
    public Type Implementation { get; }
}
