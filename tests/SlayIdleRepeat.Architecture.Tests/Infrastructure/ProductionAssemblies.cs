using System.Reflection;
using Mono.Cecil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// The production assemblies under test, loaded both as CLR <see cref="Assembly"/>
/// (for NetArchTest and reflection) and as Cecil <see cref="ModuleDefinition"/>
/// (for the IL scans NetArchTest cannot express).
/// </summary>
/// <remarks>
/// The assemblies are read out of this test project's own output directory, which
/// is where every <c>ProjectReference</c> lands. That keeps the suite honest even
/// while the production projects are empty shells: an assembly with zero types is
/// still an assembly, and a rule over it passes vacuously rather than being skipped.
/// </remarks>
internal static class ProductionAssemblies
{
    internal const string CoreName = "SlayIdleRepeat.Core";
    internal const string ApplicationName = "SlayIdleRepeat.Application";
    internal const string ContractsName = "SlayIdleRepeat.Contracts";
    internal const string ServerName = "SlayIdleRepeat.Server";
    internal const string ClientName = "SlayIdleRepeat.Client";
    internal const string AdapterPrefix = "SlayIdleRepeat.Adapters.";
    internal const string CoreTestsName = "SlayIdleRepeat.Core.Tests";

    /// <summary>The two composition roots — the only projects allowed to name a concrete adapter (<c>23</c> §7).</summary>
    internal static IReadOnlyList<string> CompositionRootNames { get; } = new[] { ServerName, ClientName };

    /// <summary>
    /// The build-time tools under <c>tools/</c> that <c>30</c> §6 pins to <c>Core</c> alone:
    /// the economy simulator (<c>21</c>) and the balance harness.
    /// </summary>
    /// <remarks>
    /// <c>21</c> §2: 180 days × 14 profiles run headless with no adapters at all. Reaching
    /// <c>Application</c> from either would break <c>30</c> §13 as well, and until this list
    /// existed nothing enforced it.
    /// </remarks>
    internal static IReadOnlyList<string> CoreOnlyToolNames { get; } =
        new[] { "SlayIdleRepeat.EconomySim", "SlayIdleRepeat.BalanceHarness" };

    /// <summary>
    /// Tools that compose an adapter themselves, and are therefore composition roots in the
    /// sense <c>23</c> §7 means, despite not being <c>Server</c> or <c>Client</c>.
    /// </summary>
    /// <remarks>
    /// <c>ContentValidator</c> wires <c>Adapters.Content.LocalFile</c> to the content services
    /// in <c>Application</c> so that CI validates content through the same loader the game
    /// uses. That is a composition root doing its job. It is named here so
    /// <c>Only_composition_roots_reference_adapter_projects</c> passes for a stated reason,
    /// rather than — as it did while the rule was scoped to <c>src/</c> — by never looking.
    /// </remarks>
    internal static IReadOnlyList<string> ToolCompositionRootNames { get; } =
        new[] { "SlayIdleRepeat.ContentValidator" };

    private static readonly Dictionary<string, ModuleDefinition> ModuleCache = new(StringComparer.Ordinal);
    private static readonly DefaultAssemblyResolver Resolver = CreateResolver();

    /// <summary>Names of every production assembly, derived from the project files under <c>src/</c>.</summary>
    internal static IReadOnlyList<string> AllNames { get; } =
        RepoLayout.ProductionProjectFiles.Select(RepoLayout.ProjectName)
                  .OrderBy(n => n, StringComparer.Ordinal)
                  .ToArray();

    /// <summary>Names of every adapter assembly.</summary>
    internal static IReadOnlyList<string> AdapterNames { get; } =
        AllNames.Where(IsAdapter).ToArray();

    internal static Assembly Core => Load(CoreName);

    internal static Assembly Application => Load(ApplicationName);

    internal static Assembly Contracts => Load(ContractsName);

    /// <summary>True when the assembly name belongs to an adapter project.</summary>
    internal static bool IsAdapter(string assemblyName) =>
        assemblyName.StartsWith(AdapterPrefix, StringComparison.Ordinal);

    /// <summary>Loads a production assembly from the test output directory.</summary>
    internal static Assembly Load(string assemblyName) => Assembly.LoadFrom(PathOf(assemblyName));

    /// <summary>Reads a production assembly's metadata with Cecil, cached for the process.</summary>
    internal static ModuleDefinition Module(string assemblyName)
    {
        lock (ModuleCache)
        {
            if (!ModuleCache.TryGetValue(assemblyName, out var module))
            {
                module = ModuleDefinition.ReadModule(
                    PathOf(assemblyName),
                    new ReaderParameters { AssemblyResolver = Resolver, ReadSymbols = false });
                ModuleCache[assemblyName] = module;
            }

            return module;
        }
    }

    internal static ModuleDefinition CoreModule => Module(CoreName);

    internal static ModuleDefinition ApplicationModule => Module(ApplicationName);

    /// <summary>Absolute path of a production assembly in the test output directory.</summary>
    internal static string PathOf(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"'{assemblyName}.dll' is missing from {AppContext.BaseDirectory}. " +
                "Every production project must be referenced by SlayIdleRepeat.Architecture.Tests.",
                path);
    }

    private static DefaultAssemblyResolver CreateResolver()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        return resolver;
    }
}
