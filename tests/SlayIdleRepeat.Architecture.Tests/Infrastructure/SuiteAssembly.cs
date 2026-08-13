using Mono.Cecil;

namespace SlayIdleRepeat.Architecture.Tests.Infrastructure;

/// <summary>
/// This suite's <b>own</b> assembly, read back through Cecil.
/// </summary>
/// <remarks>
/// <para>
/// Several rules here have to be driven against a shape that must never exist in
/// <c>SlayIdleRepeat.Core</c> — a violation is not committed to the domain to prove a rule works.
/// The fixture is compiled into this assembly instead and read back as metadata, so the predicate
/// runs against real IL rather than against a hand-built <c>MethodDefinition</c> that could be
/// wrong in the same direction as the predicate.
/// </para>
/// <para>
/// 🔒 <b>One reader for the repository</b> (steering S4). Two private
/// <c>ModuleDefinition.ReadModule(typeof(X).Assembly.Location)</c> copies cannot disagree about
/// the file today, but they are two places to change the day the reader needs an assembly
/// resolver or symbols — and a rule driven against a differently-read module is a rule nobody
/// can compare to the other one.
/// </para>
/// </remarks>
internal static class SuiteAssembly
{
    /// <summary>This assembly's metadata.</summary>
    internal static ModuleDefinition Module => Reader.Value;

    /// <summary>
    /// The one type in this assembly with the given simple name, nested fixtures included.
    /// </summary>
    /// <remarks>
    /// <c>Single</c> rather than <c>FirstOrDefault</c>: a fixture that was renamed away must fail
    /// loudly here, not quietly hand a teeth-check <c>null</c> to assert nothing against.
    /// </remarks>
    internal static TypeDefinition Type(string simpleName) =>
        Il.AllTypes(Module).Single(t => t.Name.Equals(simpleName, StringComparison.Ordinal));

    private static readonly Lazy<ModuleDefinition> Reader = new(() =>
        ModuleDefinition.ReadModule(typeof(SuiteAssembly).Assembly.Location));
}
