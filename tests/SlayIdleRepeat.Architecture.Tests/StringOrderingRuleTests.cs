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
/// <c>Max</c>/<c>Min</c> over a string key, and a <c>SortedDictionary</c> or <c>SortedSet</c> keyed
/// by string with no comparer — every one of them silently picks the ambient collation. All are
/// covered below.
/// </para>
/// </remarks>
public sealed class StringOrderingRuleTests
{
    /// <summary>
    /// LINQ ordering operators whose comparer-less overload takes the ambient collation, with the
    /// parameter count of the overload that omits the comparer.
    /// </summary>
    private static readonly (string Method, int ParametersWithoutComparer)[] LinqOrdering =
    {
        ("OrderBy", 2),
        ("OrderByDescending", 2),
        ("ThenBy", 2),
        ("ThenByDescending", 2),
        ("Order", 1),
        ("OrderDescending", 1),
        ("Max", 2),
        ("Min", 2),
        ("MaxBy", 2),
        ("MinBy", 2),
    };

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

    private static (IReadOnlyList<string> Offenders, int CallSites) Scan()
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

            // The KEY is the last generic argument for OrderBy/ThenBy/MaxBy and the only one for
            // Order/Max — either way, an ordering over strings is what matters.
            if (!generic.GenericArguments.Any(IsString))
            {
                return 1;
            }

            if (call.Parameters.Count <= LinqOrdering[row].ParametersWithoutComparer)
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
            if (!Il.Flatten(call.DeclaringType).Any(IsString) &&
                !call.Parameters.Select(p => p.ParameterType).SelectMany(Il.Flatten).Any(IsString))
            {
                return 1;
            }

            if (call.Parameters.Count == 0 ||
                (declaring.Equals("System.Array", StringComparison.Ordinal) && call.Parameters.Count == 1))
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

            if (!call.Parameters.Any(p => (p.ParameterType.FullName ?? string.Empty).Contains("Comparer", StringComparison.Ordinal)))
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
