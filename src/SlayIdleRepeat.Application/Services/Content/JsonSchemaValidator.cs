using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Validates a loaded document against its JSON Schema (draft 2020-12), over a deliberately
/// closed keyword set.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written because it has to be: `23` §2.1 — architecture-tested — forbids
/// <c>SlayIdleRepeat.Application</c> any <c>PackageReference</c> at all, so no third-party
/// JSON-Schema library is available to the layer that owns validation.
/// </para>
/// <para>
/// 🔒 <b>Unknown keyword ⇒ hard failure.</b> The usual danger with a hand-written validator is
/// that it quietly ignores what it does not implement, which is precisely
/// `SlayIdleRepeat.Data/README.md`'s <em>"a permissive schema is worse than no schema, because it
/// manufactures confidence."</em> Every keyword this validator meets must be on
/// <see cref="SupportedKeywords"/>; anything else is
/// <see cref="ContentIssueCode.UnsupportedSchemaKeyword"/> and fails the build with an
/// instruction to extend this class. That turns "silently permissive" into "loudly incomplete".
/// </para>
/// </remarks>
public static class JsonSchemaValidator
{
    /// <summary>
    /// Every keyword this validator understands. Annotations are accepted and ignored; assertions
    /// are enforced. Extending this set is a deliberate act with a test attached.
    /// </summary>
    public static IReadOnlySet<string> SupportedKeywords => throw new NotImplementedException();

    /// <summary>Validates one instance document against one schema document.</summary>
    /// <param name="instance">The parsed data document.</param>
    /// <param name="schema">The parsed schema document, which is also the <c>$ref</c> root.</param>
    /// <param name="instanceLocation">Document path used to locate findings.</param>
    public static IReadOnlyList<ContentIssue> Validate(
        ContentValue instance,
        ContentValue schema,
        string instanceLocation) => throw new NotImplementedException();
}
