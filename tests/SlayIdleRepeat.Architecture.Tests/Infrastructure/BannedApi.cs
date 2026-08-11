using System.Text.RegularExpressions;
using Mono.Cecil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// The banned ambient APIs of `14` §8.1, expressed once and enforced twice:
/// as an IL/metadata scan (authoritative) and as a source grep (belt and braces).
/// </summary>
/// <remarks>
/// 🔒 `14` §8.1: "Never use <c>System.Random</c>, <c>GD.Randi()</c>, <c>Random.Shared</c>,
/// <c>DateTime.Now</c>, <c>Guid.NewGuid()</c> or <c>Environment.TickCount</c> anywhere in
/// <c>SlayIdleRepeat.Core</c> or <c>SlayIdleRepeat.Application</c>."
/// <para>
/// <c>DateTime.UtcNow</c> and <c>DateTimeOffset.Now</c>/<c>UtcNow</c> are the same violation
/// and are banned with them — `23` §6's comment lists both forms, and time enters `Core`
/// only as <c>GameContext.NowUtc</c> (`30` §3).
/// </para>
/// </remarks>
internal static class BannedApi
{
    /// <summary>Banned members, as <c>Type::member</c> pairs matched against IL operands.</summary>
    private static readonly (string DeclaringType, string Member, string Reason)[] BannedMembers =
    {
        ("System.DateTime", "get_Now", "DateTime.Now — time is GameContext.NowUtc (30 §3)"),
        ("System.DateTime", "get_UtcNow", "DateTime.UtcNow — time is GameContext.NowUtc (30 §3)"),
        ("System.DateTime", "get_Today", "DateTime.Today — time is GameContext.NowUtc (30 §3)"),
        ("System.DateTimeOffset", "get_Now", "DateTimeOffset.Now — time is GameContext.NowUtc (30 §3)"),
        ("System.DateTimeOffset", "get_UtcNow", "DateTimeOffset.UtcNow — time is GameContext.NowUtc (30 §3)"),
        ("System.Guid", "NewGuid", "Guid.NewGuid() — identity is IIdGeneratorPort (14 §8.1)"),
        ("System.Environment", "get_TickCount", "Environment.TickCount (14 §8.1)"),
        ("System.Environment", "get_TickCount64", "Environment.TickCount64 (14 §8.1)"),
    };

    /// <summary>Banned types: naming them at all is the violation.</summary>
    private static readonly (string FullName, string Reason)[] BannedTypes =
    {
        ("System.Random", "System.Random / Random.Shared — draws come from DeterministicRng (14 §8.1)"),
    };

    /// <summary>Banned namespaces: any type from them is a violation in Core/Application.</summary>
    private static readonly (string Prefix, string Reason)[] BannedNamespaces =
    {
        ("Godot", "Godot (GD.Randi() and friends) — Godot is an adapter, not a foundation (23 §5 A10)"),
    };

    /// <summary>The equivalent source-level patterns, for the grep that backs up the IL scan.</summary>
    internal static IReadOnlyList<(Regex Pattern, string Reason)> SourcePatterns { get; } = new[]
    {
        (new Regex(@"\bSystem\s*\.\s*Random\b", RegexOptions.Compiled), "System.Random (14 §8.1)"),
        (new Regex(@"\bnew\s+Random\s*\(", RegexOptions.Compiled), "new Random() (14 §8.1)"),
        (new Regex(@"\bRandom\s*\.\s*Shared\b", RegexOptions.Compiled), "Random.Shared (14 §8.1)"),
        (new Regex(@"\bDateTime\s*\.\s*(Now|UtcNow|Today)\b", RegexOptions.Compiled), "DateTime.Now/UtcNow/Today (14 §8.1, 23 §6)"),
        (new Regex(@"\bDateTimeOffset\s*\.\s*(Now|UtcNow)\b", RegexOptions.Compiled), "DateTimeOffset.Now/UtcNow (23 §6)"),
        (new Regex(@"\bGuid\s*\.\s*NewGuid\s*\(", RegexOptions.Compiled), "Guid.NewGuid() (14 §8.1)"),
        (new Regex(@"\bEnvironment\s*\.\s*TickCount(64)?\b", RegexOptions.Compiled), "Environment.TickCount (14 §8.1)"),
        (new Regex(@"\bGD\s*\.\s*Rand\w*\s*\(", RegexOptions.Compiled), "GD.Randi() (14 §8.1)"),
    };

    /// <summary>
    /// Types whose <c>ToString</c>/<c>Parse</c>/<c>TryParse</c> render or read differently
    /// depending on the ambient <see cref="System.Globalization.CultureInfo"/>.
    /// </summary>
    private static readonly string[] CultureSensitiveFormattables =
    {
        "System.Double", "System.Single", "System.Decimal",
        "System.Int16", "System.Int32", "System.Int64",
        "System.UInt16", "System.UInt32", "System.UInt64",
        "System.Byte", "System.SByte",
        "System.DateTime", "System.DateTimeOffset", "System.TimeSpan",
        "System.DateOnly", "System.TimeOnly",
    };

    /// <summary>
    /// String operations that consult the current culture unless told otherwise, and whose
    /// culture-free overload exists precisely so nobody has to.
    /// </summary>
    /// <remarks>
    /// ⚠️ Matched on the FULL PARAMETER TYPE LIST, not on the parameter count. The
    /// <c>char</c> overloads — <c>StartsWith('^')</c>, <c>EndsWith('$')</c> — are ordinal by
    /// specification and are not violations; only the <c>string</c> overloads consult
    /// <c>CultureInfo.CurrentCulture</c>. A count-based match calls all six of this repo's
    /// <c>StartsWith('_')</c> calls defects, and a rule that cries wolf on correct code is
    /// one people learn to suppress.
    /// </remarks>
    private static readonly (string DeclaringType, string Member, string Signature, string Reason)[] CultureSensitiveStringMembers =
    {
        ("System.String", "ToUpper", "", "string.ToUpper() with no CultureInfo — use ToUpperInvariant()"),
        ("System.String", "ToLower", "", "string.ToLower() with no CultureInfo — use ToLowerInvariant()"),
        ("System.String", "StartsWith", "System.String", "string.StartsWith(string) uses the current culture — pass StringComparison.Ordinal"),
        ("System.String", "EndsWith", "System.String", "string.EndsWith(string) uses the current culture — pass StringComparison.Ordinal"),
        ("System.String", "IndexOf", "System.String", "string.IndexOf(string) uses the current culture — pass StringComparison.Ordinal"),
        ("System.String", "LastIndexOf", "System.String", "string.LastIndexOf(string) uses the current culture — pass StringComparison.Ordinal"),
        ("System.String", "Compare", "System.String,System.String", "string.Compare(a, b) uses the current culture — pass StringComparison.Ordinal"),
    };

    /// <summary>
    /// Every culture-sensitive conversion in a module: a number or date formatted or parsed
    /// with no <see cref="IFormatProvider"/>, or a culture-sensitive string operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <c>14</c> §8.2 requires a byte-identical <c>LogHash</c> across x64 and ARM64, and
    /// <c>CanonicalStateWriter</c> is exactly where that would break. A parameterless
    /// <c>double.ToString()</c> renders <c>1,5</c> on a German laptop and <c>1.5</c> in the
    /// Linux container: two different byte streams, two different hashes, and a determinism
    /// failure that reproduces only on the machine of whoever wrote it.
    /// </para>
    /// <para>
    /// <c>BannedApi</c> bans time, identity and randomness — everything that varies by WHEN
    /// the code runs. This is the same class of defect varying by WHERE it runs, and it was
    /// unbanned. <c>InvariantGlobalization</c> would mask it in some hosts and not others,
    /// which is worse than not having it: the Godot client does not set it.
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> CultureViolations(ModuleDefinition module)
    {
        foreach (var method in Il.MethodsWithBodies(module))
        {
            foreach (var instruction in Il.Instructions(method))
            {
                if (instruction.Operand is not MethodReference called)
                {
                    continue;
                }

                var declaring = called.DeclaringType?.FullName ?? string.Empty;
                var parameters = called.Parameters;

                var isFormattable = CultureSensitiveFormattables.Contains(declaring, StringComparer.Ordinal);

                if (isFormattable &&
                    (called.Name.Equals("ToString", StringComparison.Ordinal) ||
                     called.Name.Equals("Parse", StringComparison.Ordinal) ||
                     called.Name.Equals("TryParse", StringComparison.Ordinal)) &&
                    !parameters.Any(p => p.ParameterType.FullName == "System.IFormatProvider"))
                {
                    yield return
                        $"{Il.Describe(method)} calls {declaring}.{called.Name}(" +
                        $"{string.Join(", ", parameters.Select(p => p.ParameterType.Name))}) with no IFormatProvider — " +
                        "pass CultureInfo.InvariantCulture. 14 §8.2 requires a byte-identical LogHash across " +
                        "x64 and ARM64, and 'de-DE' renders 1,5 where the container renders 1.5.";
                }

                var signature = string.Join(",", parameters.Select(p => p.ParameterType.FullName));

                var stringHit = CultureSensitiveStringMembers.FirstOrDefault(
                    b => b.DeclaringType.Equals(declaring, StringComparison.Ordinal) &&
                         b.Member.Equals(called.Name, StringComparison.Ordinal) &&
                         b.Signature.Equals(signature, StringComparison.Ordinal));

                if (stringHit.DeclaringType is not null)
                {
                    yield return $"{Il.Describe(method)} calls {stringHit.Reason} (14 §8.2).";
                }
            }
        }
    }

    /// <summary>
    /// Every use of a banned ambient API in a module, as a message naming the offending
    /// member and the API it reached for.
    /// </summary>
    internal static IEnumerable<string> Violations(ModuleDefinition module)
    {
        foreach (var type in Il.AllTypes(module))
        {
            foreach (var reference in Il.ReferencedTypeReferences(type))
            {
                var banned = BannedTypes.FirstOrDefault(b => b.FullName.Equals(reference.FullName, StringComparison.Ordinal));
                if (banned.FullName is not null)
                {
                    yield return $"{type.FullName} uses {banned.Reason}";
                }

                var namespaceHit = BannedNamespaces.FirstOrDefault(
                    b => Il.IsUnder(reference.Namespace ?? string.Empty, b.Prefix));
                if (namespaceHit.Prefix is not null)
                {
                    yield return $"{type.FullName} names {reference.FullName} — {namespaceHit.Reason}";
                }
            }

            foreach (var method in type.Methods)
            {
                foreach (var instruction in Il.Instructions(method))
                {
                    if (instruction.Operand is not MethodReference called)
                    {
                        continue;
                    }

                    var declaring = called.DeclaringType?.FullName ?? string.Empty;
                    var hit = BannedMembers.FirstOrDefault(
                        b => b.DeclaringType.Equals(declaring, StringComparison.Ordinal) &&
                             b.Member.Equals(called.Name, StringComparison.Ordinal));

                    if (hit.DeclaringType is not null)
                    {
                        yield return $"{Il.Describe(method)} calls {hit.Reason}";
                    }
                }
            }
        }
    }
}
