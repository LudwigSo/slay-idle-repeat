using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Contract.Tests.Shared;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests;

/// <summary>
/// 🔒 X-06 made self-policing: the rules that decide whether this project actually covers what it
/// claims to (<c>23</c> §5 A8 — "one shared suite per port, run against every implementation").
/// </summary>
/// <remarks>
/// <para>
/// Without these rules, "every port has a contract suite" is a sentence in a design document and a
/// habit. A port declared without a suite is invisible; a second implementation added to an existing
/// port runs nothing new and reports success; a fixture that drifts onto the wrong base class still
/// executes cases, just not the ones its name promises.
/// </para>
/// <para>
/// Ports and implementations are discovered from the <b>output directory</b>, the way
/// <c>SlayIdleRepeat.Architecture.Tests</c>' <c>ProductionAssemblies</c> does it: every
/// <c>ProjectReference</c> lands there, so an adapter with zero types is still an assembly and a
/// rule over it fails loudly rather than being skipped. Suites and fixtures are discovered from
/// <b>attributes</b>, never from a name convention — see
/// <see cref="ContractSuiteForAttribute"/>.
/// </para>
/// </remarks>
public sealed class ContractSuiteCoverageTests
{
    // ------------------------------------------------------------------------------------ floors
    //
    // 🔒 Steering S3. Every rule below is of the shape "no member of set S fails X", and each is
    // satisfied trivially by an empty S. Each floor names the rule that goes silent when it is
    // breached; lowering one is a deliberate decision that belongs in the commit that forces it.

    /// <summary>Ports found under <c>Application.Ports</c>. At zero, rules 1 and 2 govern nothing.</summary>
    private const int PortFloor = 5;

    /// <summary>Attributed suites in this assembly. At zero, rule 3's suite arm has nothing to check.</summary>
    private const int SuiteFloor = 5;

    /// <summary>
    /// Adapter assemblies the scan finds. At zero, rule 2 finds no implementations and reports
    /// success over every port at once — the single most expensive silence available here.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Exact against the scan, which finds 23 adapter assemblies today</b> — every
    /// <c>SlayIdleRepeat.Adapters.*</c> project this suite references, and adapters reference no
    /// other adapter, so the output directory holds exactly the direct references. A floor set below
    /// the real count is a floor only against total collapse: at 8, fifteen adapter
    /// <c>ProjectReference</c>s could be dropped from this project one by one and rule 2 would go
    /// quiet on every port they carried while this rule still reported success. The narrowing this
    /// floor watches for is one reference at a time, so one dropped reference has to fire it — which
    /// means no headroom at all.
    /// <para>
    /// ⚠️ <b>This was 18 against a scan of 21, and M5-01 then added two adapters without moving
    /// it.</b> The task added <c>Adapters.Ambient.System</c> and <c>Adapters.Cache.LocalFile</c> to
    /// this project's references and raised <c>SubjectSetFloorTests.AdapterFloor</c> from 21 to 23
    /// for them, but left this sibling alone — so the scan grew by two while the slack under it grew
    /// to five, which is exactly the drift the paragraph above says the number exists to notice.
    /// Both floors move together, or one set has gained a member the other did not see. Deleting an
    /// adapter project is still allowed; it is a deliberate act that lowers both in its own commit.
    /// </para>
    /// </remarks>
    private const int AdapterAssemblyFloor = 23;

    /// <summary>
    /// Fixtures per suite. At one, <c>23</c> §5 A5's "the real adapter AND the in-memory fake"
    /// collapses to whichever one exists, and the suite stops being a comparison at all.
    /// </summary>
    private const int FixturesPerSuiteFloor = 2;

    /// <summary>
    /// Cases a suite declares itself. At zero, every fixture of that port runs an empty suite and
    /// all four rules here stay green over a port nothing tests.
    /// </summary>
    private const int CasesPerSuiteFloor = 3;

    private const string PortsNamespace = "SlayIdleRepeat.Application.Ports";
    private const string ApplicationAssemblyName = "SlayIdleRepeat.Application";
    private const string AdapterAssemblyPrefix = "SlayIdleRepeat.Adapters.";

    /// <summary>
    /// 🔒 Rule 1 — every port has exactly one shared contract suite in this assembly
    /// (<c>23</c> §5 A8).
    /// </summary>
    /// <remarks>
    /// Exactly one, not at least one: two suites over one port is two answers to "what does this
    /// port mean", and the fixtures would then split between them with neither side complete.
    /// </remarks>
    [Fact]
    public void Every_port_has_a_shared_contract_suite() =>
        Empty(
            PortsWithoutExactlyOneSuite(Ports(), SuiteDeclarations()),
            "Every port has exactly one shared contract suite (23 §5 A8).");

    /// <summary>
    /// 🔒 Rule 2 — every concrete implementation of a port has a fixture, and that fixture derives
    /// from that port's suite (<c>23</c> §5 A8).
    /// </summary>
    /// <remarks>
    /// The derivation half is the one this rule exists for. An attribute naming the right
    /// implementation on a class deriving from the wrong suite compiles, runs and reports success —
    /// it just runs some other port's cases. That is the drift a name convention cannot see and the
    /// reason the two attributes are separate.
    /// </remarks>
    [Fact]
    public void Every_implementation_of_a_port_has_a_contract_fixture()
    {
        var ports = Ports().ToArray();

        Empty(
            ImplementationsWithoutACoveringFixture(
                ports, SuiteDeclarations(), FixtureDeclarations(), PortImplementations(ports)),
            "Every implementation of a port has a contract fixture deriving from that port's suite (23 §5 A8).");
    }

    /// <summary>
    /// 🔒 Rule 3 — the unanchored direction: every attribute names a real subject (<c>23</c> §6).
    /// </summary>
    /// <remarks>
    /// A <c>[ContractSuiteFor]</c> pointing at something that is not a port, or a
    /// <c>[ContractFixtureFor]</c> pointing at a type that implements no port, can never be
    /// <em>satisfied</em> — only deleted by hand, which is the state these rules exist to replace.
    /// It is also how a suite survives the deletion of the port it was written for, still green.
    /// </remarks>
    [Fact]
    public void Every_contract_suite_and_fixture_names_a_real_subject() =>
        Empty(
            AttributesNamingNoRealSubject(Ports(), SuiteDeclarations(), FixtureDeclarations()),
            "Every contract suite and fixture names a real port or implementation (23 §6).");

    /// <summary>
    /// 🔒 Rule 4 — steering <b>S3</b>: the subject sets these rules quantify over have floors, and
    /// so does the discovery mechanism itself (<c>23</c> §6).
    /// </summary>
    /// <remarks>
    /// A suite parameterised over "every implementation" passes forever over zero implementations,
    /// and rule 2 above is exactly that shape. The assembly scan is floored <em>separately</em>,
    /// because a scan that finds nothing makes rules 2 and 4 both vacuous at once and the failure
    /// would otherwise read as "every port is covered".
    /// </remarks>
    [Fact]
    public void The_contract_suite_subject_sets_have_floors()
    {
        var offenders = new List<string>();

        var scanned = ScannedAssemblies();
        var adapters = scanned
            .Where(a => (a.GetName().Name ?? string.Empty).StartsWith(AdapterAssemblyPrefix, StringComparison.Ordinal))
            .ToArray();

        // The discovery mechanism first: with no adapter assembly in the output directory, rule 2
        // finds no implementations at all and reports success over every port simultaneously.
        adapters.ShouldNotBeEmpty(
            $"the scan of {AppContext.BaseDirectory} found no '{AdapterAssemblyPrefix}*.dll'. "
            + "Every_implementation_of_a_port_has_a_contract_fixture is then green over an empty set, "
            + "which is indistinguishable from full coverage.");

        Floor(offenders, "adapter assemblies scanned", adapters.Length, AdapterAssemblyFloor,
            "Every_implementation_of_a_port_has_a_contract_fixture discovers implementations by "
            + "scanning them. A shrunken scan silently narrows what that rule can see.");

        var ports = Ports().ToArray();
        Floor(offenders, "ports under " + PortsNamespace, ports.Length, PortFloor,
            "Every_port_has_a_shared_contract_suite and Every_implementation_of_a_port_has_a_contract_fixture "
            + "are both stated over this set. Empty, both are green over nothing.");

        var suites = Suites().ToArray();
        Floor(offenders, "[ContractSuiteFor] classes", suites.Length, SuiteFloor,
            "Every_contract_suite_and_fixture_names_a_real_subject's suite arm quantifies over them, "
            + "and rule 1 compares them against the ports.");

        var fixtures = Fixtures().ToArray();

        foreach (var suite in suites)
        {
            var derived = fixtures.Count(f => suite.IsAssignableFrom(f) && f != suite);

            Floor(offenders, $"fixtures deriving from '{suite.Name}'", derived, FixturesPerSuiteFloor,
                "23 §5 A5 is the real adapter AND the in-memory fake. One fixture is not a "
                + "comparison, and an abstract suite with none executes zero cases while still "
                + "counting as coverage.");

            var cases = suite
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any());

            Floor(offenders, $"cases declared by '{suite.Name}'", cases, CasesPerSuiteFloor,
                "a suite that declares no cases of its own is satisfied by every implementation, "
                + "and rules 1 to 3 all stay green over a port nothing actually tests.");
        }

        Empty(offenders, "The contract-suite subject sets are the ones these rules were written against (23 §6, steering S3).");
    }

    // ------------------------------------------------------------------------------ self-tests
    //
    // 🔒 Rules 1 to 3 are quantifiers over the real repository, where they can only ever be seen
    // passing — the same problem PortCatalogueTests solves by driving the register's predicates with
    // crafted inputs. These three cases do that here: every arm of every rule fires on a bad input
    // and the offender it names is pinned, and each is paired with a negative control, because a
    // rule that flagged everything would satisfy the loud half on its own.
    //
    // The subjects are real types rather than synthetic ones. A second class carrying
    // [ContractSuiteFor(typeof(IClockPort))] could not be committed at all — rule 1 would fail on it
    // — so the bad arrangements are built as declarations rather than as attributed classes.

    /// <summary>Rule 1 bites in both directions: a port with no suite, and a port with two.</summary>
    [Fact]
    public void Rule_one_fires_on_a_port_with_no_suite_and_on_a_port_with_two()
    {
        var ports = new[] { typeof(IClockPort) };
        var clockSuite = new SuiteDeclaration(typeof(IClockPortContractTests), typeof(IClockPort));

        PortsWithoutExactlyOneSuite(ports, new[] { clockSuite }).ShouldBeEmpty(
            "exactly one suite names IClockPort here, which is the arrangement the rule exists to "
            + "permit. A rule that flagged this would be failing on the real repository too.");

        PortsWithoutExactlyOneSuite(ports, Array.Empty<SuiteDeclaration>())
            .ShouldHaveSingleItem()
            .ShouldContain("carries [ContractSuiteFor(typeof(IClockPort))]", Case.Sensitive);

        // The second arm: 'exactly one', not 'at least one'. A rule stopping at the first suite it
        // finds passes the whole repository and never notices a port with two answers.
        var strayer = new SuiteDeclaration(typeof(IIdGeneratorPortContractTests), typeof(IClockPort));

        PortsWithoutExactlyOneSuite(ports, new[] { clockSuite, strayer })
            .ShouldHaveSingleItem()
            .ShouldContain("has 2 contract suites", Case.Sensitive);
    }

    /// <summary>
    /// Rule 2 bites in both directions: an implementation with no fixture at all, and a fixture that
    /// names the right implementation while deriving from another port's suite.
    /// </summary>
    [Fact]
    public void Rule_two_fires_on_an_uncovered_implementation_and_on_a_fixture_under_the_wrong_suite()
    {
        var ports = new[] { typeof(IClockPort) };
        var suite = new SuiteDeclaration(typeof(IClockPortContractTests), typeof(IClockPort));
        var fixture = new FixtureDeclaration(
            typeof(SystemClockContractTests), typeof(Adapters.Ambient.System.SystemClock));
        var implementations = new[] { typeof(Adapters.Ambient.System.SystemClock) };

        ImplementationsWithoutACoveringFixture(ports, new[] { suite }, new[] { fixture }, implementations)
            .ShouldBeEmpty(
                "SystemClockContractTests is attributed to SystemClock and derives from "
                + "IClockPortContractTests, which is the arrangement the rule demands.");

        ImplementationsWithoutACoveringFixture(
                ports, new[] { suite }, Array.Empty<FixtureDeclaration>(), implementations)
            .ShouldHaveSingleItem()
            .ShouldContain("carries [ContractFixtureFor(typeof(SystemClock))]", Case.Sensitive);

        // 🔒 The derivation arm, and the one this rule exists for: the attribute is right and the
        // base class is wrong, which compiles, runs and reports success while running another port's
        // cases. Only the IsAssignableFrom half can see it.
        var wrongSuite = new SuiteDeclaration(typeof(IIdGeneratorPortContractTests), typeof(IClockPort));

        ImplementationsWithoutACoveringFixture(ports, new[] { wrongSuite }, new[] { fixture }, implementations)
            .ShouldHaveSingleItem()
            .ShouldContain("does not derive from 'IIdGeneratorPortContractTests'", Case.Sensitive);

        // The negative control on the quantifier itself: a type that implements no port is not this
        // rule's business, or it would demand a fixture for every class in every scanned assembly.
        ImplementationsWithoutACoveringFixture(
                ports, new[] { suite }, Array.Empty<FixtureDeclaration>(), new[] { typeof(string) })
            .ShouldBeEmpty(
                "string implements no port. A rule that asked for a fixture here would be asking for "
                + "one for every type the assembly scan returns.");
    }

    /// <summary>
    /// Rule 3 bites in both directions: a suite whose subject is not a port, and a fixture whose
    /// subject implements none.
    /// </summary>
    [Fact]
    public void Rule_three_fires_on_a_suite_and_on_a_fixture_naming_no_real_subject()
    {
        var ports = new[] { typeof(IClockPort) };
        var suite = new SuiteDeclaration(typeof(IClockPortContractTests), typeof(IClockPort));
        var fixture = new FixtureDeclaration(
            typeof(SystemClockContractTests), typeof(Adapters.Ambient.System.SystemClock));

        AttributesNamingNoRealSubject(ports, new[] { suite }, new[] { fixture }).ShouldBeEmpty(
            "both attributes name a real subject, which is every suite and fixture in the tree.");

        AttributesNamingNoRealSubject(
                ports, new[] { suite with { Port = typeof(Adapters.InMemory.AdjustableClock) } }, new[] { fixture })
            .ShouldHaveSingleItem()
            .ShouldContain("is not an interface under", Case.Sensitive);

        AttributesNamingNoRealSubject(
                ports, new[] { suite }, new[] { fixture with { Implementation = typeof(ContractSuiteCoverageTests) } })
            .ShouldHaveSingleItem()
            .ShouldContain("implements no port under", Case.Sensitive);
    }

    // ----------------------------------------------------------------------------- rule bodies
    //
    // 🔒 Each of rules 1 to 3 is a pure function of its inputs, the same construction
    // PortCatalogue.Expired / Undeclared / Unanchored use and for the same reason: a rule computed
    // inline from the live repository can only ever be seen passing. Parameterised, the self-tests
    // below drive each arm with a deliberately bad input and pin the offender it produces, without
    // a violation ever being committed.

    /// <summary>A contract suite and the port its <c>[ContractSuiteFor]</c> names.</summary>
    /// <param name="Suite">The abstract suite class.</param>
    /// <param name="Port">The port interface it claims to state the meaning of.</param>
    internal sealed record SuiteDeclaration(Type Suite, Type Port);

    /// <summary>A contract fixture and the implementation its <c>[ContractFixtureFor]</c> names.</summary>
    /// <param name="Fixture">The concrete fixture class.</param>
    /// <param name="Implementation">The implementation it claims to run a suite against.</param>
    internal sealed record FixtureDeclaration(Type Fixture, Type Implementation);

    /// <summary>
    /// Rule 1's body: every port with no suite, and every port with more than one. Empty means the
    /// rule holds.
    /// </summary>
    internal static IReadOnlyList<string> PortsWithoutExactlyOneSuite(
        IEnumerable<Type> ports,
        IEnumerable<SuiteDeclaration> suites)
    {
        var byPort = suites.ToLookup(s => s.Port);
        var offenders = new List<string>();

        foreach (var port in ports)
        {
            var covering = byPort[port].ToArray();

            if (covering.Length == 0)
            {
                offenders.Add(
                    $"'{port.FullName}' is a port under {PortsNamespace} and no abstract class in "
                    + "SlayIdleRepeat.Contract.Tests carries [ContractSuiteFor(typeof("
                    + $"{port.Name}))]. 23 §5 A8 is one shared suite per port; a port with none is a "
                    + "seam whose meaning is whatever its first implementation happened to do.");
                continue;
            }

            if (covering.Length > 1)
            {
                offenders.Add(
                    $"'{port.FullName}' has {covering.Length} contract suites "
                    + $"[{string.Join(", ", covering.Select(s => s.Suite.Name))}]. Two suites are two "
                    + "answers to what the port means, and its fixtures then split between them.");
            }
        }

        return offenders;
    }

    /// <summary>
    /// Rule 2's body: every implementation with no fixture, and every fixture that names the right
    /// implementation while deriving from the wrong suite. Empty means the rule holds.
    /// </summary>
    internal static IReadOnlyList<string> ImplementationsWithoutACoveringFixture(
        IEnumerable<Type> ports,
        IEnumerable<SuiteDeclaration> suites,
        IEnumerable<FixtureDeclaration> fixtures,
        IEnumerable<Type> implementations)
    {
        var declared = fixtures.ToArray();
        var suiteOf = suites.ToLookup(s => s.Port, s => s.Suite);
        var candidates = implementations.ToArray();
        var offenders = new List<string>();

        foreach (var port in ports)
        {
            foreach (var implementation in candidates.Where(port.IsAssignableFrom))
            {
                var covering = declared.Where(f => f.Implementation == implementation).ToArray();

                if (covering.Length == 0)
                {
                    offenders.Add(
                        $"'{implementation.FullName}' implements the port '{port.Name}' and no class in "
                        + "SlayIdleRepeat.Contract.Tests carries [ContractFixtureFor(typeof("
                        + $"{implementation.Name}))]. An implementation nothing runs the shared suite "
                        + "against is an implementation the port does not actually constrain.");
                    continue;
                }

                foreach (var suite in suiteOf[port])
                {
                    foreach (var declaration in covering.Where(c => !suite.IsAssignableFrom(c.Fixture)))
                    {
                        offenders.Add(
                            $"'{declaration.Fixture.Name}' is attributed to '{implementation.Name}', "
                            + $"which implements '{port.Name}', but it does not derive from "
                            + $"'{suite.Name}'. It runs some other port's cases while reading as "
                            + "coverage for this one.");
                    }
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// Rule 3's body: every attribute whose subject is not a port, and every attribute whose
    /// subject implements none. Empty means the rule holds.
    /// </summary>
    internal static IReadOnlyList<string> AttributesNamingNoRealSubject(
        IEnumerable<Type> ports,
        IEnumerable<SuiteDeclaration> suites,
        IEnumerable<FixtureDeclaration> fixtures)
    {
        var declared = ports.ToArray();
        var offenders = new List<string>();

        offenders.AddRange(
            from suite in suites
            where !declared.Contains(suite.Port)
            select $"'{suite.Suite.Name}' declares [ContractSuiteFor(typeof({suite.Port.Name}))], but "
                   + $"'{suite.Port.FullName}' is not an interface under {PortsNamespace}. A suite whose "
                   + "subject is not a port is a suite no rule here can ever demand or satisfy.");

        offenders.AddRange(
            from fixture in fixtures
            where !declared.Any(p => p.IsAssignableFrom(fixture.Implementation))
            select $"'{fixture.Fixture.Name}' declares [ContractFixtureFor(typeof("
                   + $"{fixture.Implementation.Name}))], but '{fixture.Implementation.FullName}' "
                   + $"implements no port under {PortsNamespace}. Either the port went away and this "
                   + "fixture outlived it, or the attribute names the wrong type.");

        return offenders;
    }

    // ------------------------------------------------------------------------------- discovery

    /// <summary>Every port: an interface under <c>Application.Ports</c> in the Application assembly.</summary>
    private static IEnumerable<Type> Ports() =>
        TypesOf(Load(ApplicationAssemblyName))
            .Where(t => t.IsInterface && IsUnder(t.Namespace, PortsNamespace))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>Every abstract contract suite declared in this assembly.</summary>
    private static IEnumerable<Type> Suites() =>
        typeof(ContractSuiteCoverageTests).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<ContractSuiteForAttribute>() is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>Every concrete contract fixture declared in this assembly.</summary>
    private static IEnumerable<Type> Fixtures() =>
        typeof(ContractSuiteCoverageTests).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<ContractFixtureForAttribute>() is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>Each suite paired with the port its attribute names.</summary>
    private static IEnumerable<SuiteDeclaration> SuiteDeclarations() =>
        Suites().Select(s => new SuiteDeclaration(s, s.GetCustomAttribute<ContractSuiteForAttribute>()!.Port));

    /// <summary>Each fixture paired with the implementation its attribute names.</summary>
    private static IEnumerable<FixtureDeclaration> FixtureDeclarations() =>
        Fixtures().Select(f => new FixtureDeclaration(
            f, f.GetCustomAttribute<ContractFixtureForAttribute>()!.Implementation));

    /// <summary>Every concrete, non-abstract implementation of any port across the scanned assemblies.</summary>
    private static IEnumerable<Type> PortImplementations(IReadOnlyCollection<Type> ports) =>
        ScannedAssemblies()
            .SelectMany(TypesOf)
            .Where(t => t is { IsInterface: false, IsAbstract: false } && ports.Any(p => p.IsAssignableFrom(t)))
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    /// <summary>
    /// The assemblies searched for implementations: every adapter in the output directory plus
    /// <c>Application</c> itself, which may hold a default implementation of a port it declares.
    /// </summary>
    private static IReadOnlyList<Assembly> ScannedAssemblies() => LoadedScan.Value;

    private static readonly Lazy<IReadOnlyList<Assembly>> LoadedScan = new(() =>
        Directory.GetFiles(AppContext.BaseDirectory, AdapterAssemblyPrefix + "*.dll")
                 .OrderBy(p => p, StringComparer.Ordinal)
                 .Select(Assembly.LoadFrom)
                 .Append(Load(ApplicationAssemblyName))
                 .Distinct()
                 .ToArray());

    private static Assembly Load(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        return File.Exists(path)
            ? Assembly.LoadFrom(path)
            : throw new FileNotFoundException(
                $"'{assemblyName}.dll' is missing from {AppContext.BaseDirectory}. Every project these "
                + "rules govern must be referenced by SlayIdleRepeat.Contract.Tests.",
                path);
    }

    /// <summary>An adapter can reference a vendor package whose types will not load; the ones that did are still evidence.</summary>
    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static bool IsUnder(string? ns, string prefix) =>
        ns is not null &&
        (ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal));

    private static void Floor(ICollection<string> offenders, string what, int actual, int floor, string consequence)
    {
        if (actual < floor)
        {
            offenders.Add($"{what}: {actual}, floor {floor}. {consequence}");
        }
    }

    private static void Empty(IEnumerable<string> offenders, string rule)
    {
        var list = offenders.Distinct(StringComparer.Ordinal)
                            .OrderBy(o => o, StringComparer.Ordinal)
                            .ToArray();

        if (list.Length == 0)
        {
            return;
        }

        throw new ContractCoverageViolationException(rule, list);
    }
}

/// <summary>A contract-coverage rule violation, rendered with the offenders named.</summary>
public sealed class ContractCoverageViolationException : Exception
{
    /// <summary>Creates the exception for <paramref name="rule"/>, listing <paramref name="offenders"/>.</summary>
    /// <param name="rule">The rule that was violated.</param>
    /// <param name="offenders">The offending ports, implementations, suites or sets.</param>
    public ContractCoverageViolationException(string rule, IEnumerable<string> offenders)
        : base(Render(rule, offenders))
    {
    }

    private static string Render(string rule, IEnumerable<string> offenders)
    {
        var list = offenders.ToArray();
        var lines = string.Join(Environment.NewLine, list.Select(o => "  - " + o));
        return $"CONTRACT COVERAGE RULE VIOLATED: {rule}{Environment.NewLine}"
               + $"{list.Length} offender(s):{Environment.NewLine}{lines}";
    }
}
