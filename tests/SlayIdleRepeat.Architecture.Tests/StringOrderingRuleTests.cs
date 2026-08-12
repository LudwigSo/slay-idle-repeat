using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `18` §8 / `14` §8.2 — nothing in <c>Core</c> or <c>Application</c> orders strings with the
/// default comparer.
/// </summary>
/// <remarks>
/// <para>
/// `18` §8: <em>"Effect-id order means the ascending lexicographic order of effect IDs, not draft
/// order. This removes the last source of order-dependence between client and server."</em> `05`
/// §3.1 keys on that order in five places, `05` §4 in two more, `18` §8 in three of its ten steps.
/// The order is only device-independent if the comparison is.
/// </para>
/// <para>
/// ⚠️ <b>Why <c>AmbientApiTests</c> does not already cover this.</b> That suite's
/// <c>CultureSensitiveStringMembers</c> table bans <c>string.Compare(a, b)</c>,
/// <c>StartsWith(string)</c> and their siblings — the places where the culture is reached through
/// <see cref="string"/> itself. <c>OrderBy(x =&gt; x.Id)</c> reaches it somewhere else entirely: it
/// resolves to <c>Comparer&lt;string&gt;.Default</c>, which forwards to
/// <c>string.CompareTo(string)</c>, which is culture-sensitive and is <b>not</b> in that table. So
/// the one spelling the design documents warn about by name was the one nothing was watching.
/// </para>
/// <para>
/// The same hole exists in <c>List&lt;string&gt;.Sort()</c>, <c>Array.Sort(string[])</c>,
/// <c>Max</c>/<c>Min</c> over a string key, and a <c>SortedDictionary</c>, <c>SortedSet</c> or
/// <c>SortedList</c> keyed by string with no comparer — every one of them silently picks the ambient
/// collation. All are covered below.
/// </para>
/// <para>
/// ⚠️ <b>What this rule does NOT see</b>, listed the way <c>Il.SystemNamedVendorPackages</c> lists
/// the names its own heuristic gets wrong — none is used anywhere in the repository today, and each
/// is a real hole the day one is:
/// <c>ImmutableSortedSet</c>/<c>ImmutableSortedDictionary</c> and the <c>ToImmutableSorted*</c>
/// builders; <c>MemoryExtensions.Sort</c> over a <c>Span&lt;string&gt;</c>;
/// <c>List&lt;string&gt;.BinarySearch()</c> and <c>Array.BinarySearch</c>, which compare without
/// looking like a sort; and <c>System.Linq.Queryable</c>'s expression-tree twins of the operators
/// below.
/// </para>
/// <para>
/// ⚠️ <b>Scope.</b> An IL scan of <c>Core</c> and <c>Application</c> only, with no source-grep
/// companion — so unlike <c>AmbientApiTests</c>, which carries one deliberately, an ordering inside
/// an <c>#if</c>-excluded branch is invisible here. <c>tools/BalanceHarness</c> is also outside the
/// scan although `05` §9 has it ordering effects; it is not a shipped assembly, and widening the
/// scan is a decision for the milestone that gives the harness real work.
/// </para>
/// </remarks>
public sealed class StringOrderingRuleTests
{
    /// <summary>
    /// LINQ ordering operators, and which of their generic arguments is the type actually
    /// <em>compared</em>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The key position matters, and it is not "any generic argument".</b>
    /// <c>ids.OrderBy(id =&gt; id.Length)</c> is <c>OrderBy&lt;string, int&gt;</c>: the source is a
    /// string and the comparison is over <c>int</c>, which is not culture-sensitive at all. A rule
    /// that matched any string among the generic arguments would call that a violation, and a rule
    /// that cries wolf on correct code is one people learn to suppress — the same reasoning
    /// <c>BannedApi</c> records for its own parameter-list matching.
    /// <para>
    /// <c>KeyIsLastArgument</c> distinguishes the two families: <c>OrderBy&lt;TSource, TKey&gt;</c>
    /// and <c>MaxBy&lt;TSource, TKey&gt;</c> compare the LAST argument, while
    /// <c>Order&lt;T&gt;</c> and <c>Max&lt;T&gt;</c> compare their only one.
    /// </para>
    /// </remarks>
    private static readonly (string Method, bool KeyIsLastArgument)[] LinqOrdering =
    {
        ("OrderBy", true),
        ("OrderByDescending", true),
        ("ThenBy", true),
        ("ThenByDescending", true),
        ("MaxBy", true),
        ("MinBy", true),
        ("Order", false),
        ("OrderDescending", false),
        ("Max", false),
        ("Min", false),
    };

    /// <summary>
    /// True when a parameter is a comparer — <c>IComparer&lt;T&gt;</c> or a
    /// <c>Comparison&lt;T&gt;</c> delegate.
    /// </summary>
    /// <remarks>
    /// ⚠️ Detected by TYPE, never by parameter count. <c>Enumerable.Max&lt;T&gt;(source, comparer)</c>
    /// has exactly as many parameters as <c>Enumerable.Max&lt;T, TResult&gt;(source, selector)</c>,
    /// so a count-based test reports <c>ids.Max(StringComparer.Ordinal)</c> — the very fix this
    /// rule's message tells the author to apply — as a violation.
    /// </remarks>
    private static bool IsComparer(ParameterDefinition parameter)
    {
        var name = parameter.ParameterType.FullName ?? string.Empty;

        return name.StartsWith("System.Collections.Generic.IComparer`1", StringComparison.Ordinal) ||
               name.StartsWith("System.Comparison`1", StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔒 `18` §8 / `14` §8.2 — no ordering in <c>Core</c> or <c>Application</c> uses the default
    /// string comparer. A key type of <see cref="string"/> plus no comparer argument is the
    /// violation, because that is <c>Comparer&lt;string&gt;.Default</c> and therefore the ambient
    /// collation, which differs by locale and by ICU version.
    /// </summary>
    /// <remarks>
    /// ⚠️ The subject set this quantifies over — the ordering call sites in <c>Core</c> and
    /// <c>Application</c> — could be emptied by a refactor, and the rule would then report success
    /// forever (S3). Its floor is <b>not</b> here: it is a row in
    /// <c>SubjectSetFloorTests.The_rules_subject_set_is_the_one_they_were_written_against</c>, which
    /// is the repository's one register for exactly that, reading
    /// <see cref="OrderingCallSites"/>.
    /// </remarks>
    [Fact]
    public void No_production_code_orders_strings_with_the_default_comparer()
    {
        ArchRule.Empty(
            Scan().Offenders,
            "No ordering in Core or Application uses the default string comparer — 18 §8's effect-id " +
            "order is ordinal, and Comparer<string>.Default is the ambient collation (14 §8.2).");
    }

    /// <summary>
    /// How many ordering call sites the rule above examined. Read by
    /// <c>SubjectSetFloorTests</c>, which owns the floor under it.
    /// </summary>
    internal static int OrderingCallSites => Scan().CallSites;

    // Two consumers — the [Fact] and SubjectSetFloorTests' floor row — and each walk is the full IL
    // of both modules.
    private static readonly Lazy<(IReadOnlyList<string> Offenders, int CallSites)> LazyScan = new(Walk);

    private static (IReadOnlyList<string> Offenders, int CallSites) Scan() => LazyScan.Value;

    private static (IReadOnlyList<string> Offenders, int CallSites) Walk()
    {
        var offenders = new List<string>();
        var callSites = 0;

        foreach (var module in new[] { ProductionAssemblies.CoreModule, ProductionAssemblies.ApplicationModule })
        {
            foreach (var method in Il.MethodsWithBodies(module))
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    callSites += Inspect(method, instruction, offenders);
                }
            }
        }

        return (offenders, callSites);
    }

    /// <summary>Returns 1 when the instruction was an ordering call site, 0 otherwise.</summary>
    private static int Inspect(MethodDefinition method, Mono.Cecil.Cil.Instruction instruction, List<string> offenders)
    {
        if (instruction.Operand is not MethodReference call)
        {
            return 0;
        }

        var declaring = call.DeclaringType?.FullName ?? string.Empty;

        if (call is GenericInstanceMethod generic &&
            declaring.Equals("System.Linq.Enumerable", StringComparison.Ordinal))
        {
            var row = Array.FindIndex(LinqOrdering, l => l.Method.Equals(call.Name, StringComparison.Ordinal));
            if (row < 0)
            {
                return 0;
            }

            // The COMPARED type, not "any string among the generic arguments" — see LinqOrdering.
            var arguments = generic.GenericArguments;
            if (arguments.Count == 0)
            {
                return 1;
            }

            var key = LinqOrdering[row].KeyIsLastArgument ? arguments[^1] : arguments[0];
            if (!IsString(key))
            {
                return 1;
            }

            if (!call.Parameters.Any(IsComparer))
            {
                offenders.Add(
                    $"{Il.Describe(method)} calls {call.Name} over System.String with no comparer — " +
                    "that is Comparer<string>.Default, i.e. the ambient collation. Pass " +
                    "StringComparer.Ordinal (or EffectOrder.IdComparer for effect ids).");
            }

            return 1;
        }

        // List<string>.Sort() and Array.Sort(string[]) with no comparer.
        if (call.Name.Equals("Sort", StringComparison.Ordinal) &&
            (declaring.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal) ||
             declaring.Equals("System.Array", StringComparison.Ordinal)))
        {
            // ⚠️ The generic ARGUMENTS have to be inspected here, not just the declaring type and
            // the parameters. Il.OperandTypes records why: Cecil forwards Parameters on a
            // GenericInstanceMethod to the OPEN element method, so Array.Sort<string>(string[])
            // reads as Array.Sort(!!0[]) — the declaring type is the non-generic System.Array and
            // no parameter mentions System.String. Without this the whole Array.Sort family was
            // invisible, while List<string>.Sort() was caught only because its declaring type is a
            // closed GenericInstanceType.
            var mentionsString =
                Il.Flatten(call.DeclaringType).Any(IsString) ||
                call.Parameters.Select(p => p.ParameterType).SelectMany(Il.Flatten).Any(IsString) ||
                (call is GenericInstanceMethod sort && sort.GenericArguments.Any(IsString));

            if (!mentionsString)
            {
                return 1;
            }

            if (!call.Parameters.Any(IsComparer))
            {
                offenders.Add(
                    $"{Il.Describe(method)} sorts a string collection with no comparer — that is " +
                    "Comparer<string>.Default. Pass StringComparer.Ordinal.");
            }

            return 1;
        }

        // A SortedDictionary/SortedSet keyed by string, constructed with no comparer, orders every
        // enumeration of it culturally — and those enumerations look nothing like a sort.
        if (call.Name.Equals(".ctor", StringComparison.Ordinal) &&
            (declaring.StartsWith("System.Collections.Generic.SortedDictionary`2", StringComparison.Ordinal) ||
             declaring.StartsWith("System.Collections.Generic.SortedSet`1", StringComparison.Ordinal) ||
             declaring.StartsWith("System.Collections.Generic.SortedList`2", StringComparison.Ordinal)))
        {
            if (!Il.Flatten(call.DeclaringType).Any(IsString))
            {
                return 1;
            }

            if (!call.Parameters.Any(IsComparer))
            {
                offenders.Add(
                    $"{Il.Describe(method)} constructs a string-keyed sorted collection with no " +
                    "comparer — every enumeration of it is then in the ambient collation. Pass " +
                    "StringComparer.Ordinal.");
            }

            return 1;
        }

        return 0;
    }

    private static bool IsString(TypeReference reference) =>
        (reference.FullName ?? string.Empty).Equals("System.String", StringComparison.Ordinal);
}
