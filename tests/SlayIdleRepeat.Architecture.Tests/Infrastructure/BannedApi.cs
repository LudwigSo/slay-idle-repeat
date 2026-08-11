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
