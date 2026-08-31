using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>What kind of value a template parameter takes.</summary>
public enum MailParamType
{
    /// <summary>Any non-blank string.</summary>
    TEXT = 1,

    /// <summary>A whole number, parsed invariantly.</summary>
    INTEGER = 2,

    /// <summary>An ISO-8601 instant, parsed invariantly.</summary>
    DATE_UTC = 3,
}

/// <summary>One parameter a template's text expects.</summary>
/// <param name="Name">The parameter's name, as the template spells it in braces.</param>
/// <param name="Type">What kind of value it takes.</param>
public sealed record MailTemplateParam(string Name, MailParamType Type);

/// <summary>One authored template: what it says, what it is sent as, and what it needs filling in.</summary>
/// <param name="Body">The body's loc key, and the template's identity.</param>
/// <param name="Title">The subject line's loc key.</param>
/// <param name="Category">Which of the six kinds a message from this template is.</param>
/// <param name="Params">The parameters a send must supply, in authored order.</param>
public sealed record MailTemplate(
    string Body, string Title, MessageCategory Category, IReadOnlyList<MailTemplateParam> Params);

/// <summary>Why a send was refused before a message was written.</summary>
public enum MailSendRefusal
{
    /// <summary>No authored template carries that id. Messages are templates, so there is nothing to send.</summary>
    UNKNOWN_TEMPLATE = 1,

    /// <summary>The send left a parameter the template's text names unfilled.</summary>
    MISSING_PARAM = 2,

    /// <summary>The send supplied a parameter the template does not name — it would render nowhere.</summary>
    UNEXPECTED_PARAM = 3,

    /// <summary>A parameter's value is not the type the template declares.</summary>
    MALFORMED_PARAM = 4,

    /// <summary>The template's own text does not resolve in the locale this build ships.</summary>
    UNTRANSLATED_TEMPLATE = 5,

    /// <summary>The send attached something this build cannot grant. The named kind says which.</summary>
    UNGRANTABLE_ATTACHMENT = 6,
}

/// <summary>
/// 🔒 The closed list of what ops may send, read from the content set — never a body typed by an
/// operator.
/// </summary>
/// <remarks>
/// <para>
/// It lives in <c>game-data</c> rather than in server configuration for a reason that is enforced
/// rather than stylistic: a loc key nothing in the content set names is an orphaned reference and
/// fatal to the content load, so a template referenced only from server code would fail the build.
/// Putting the catalogue in the content set is what names those keys.
/// </para>
/// <para>
/// ⚠️ <b>The design set's "every template exists in EN and DE; a message with no translation does not
/// send" is stale.</b> The localisation ruling makes EN the authored source and every DE value an
/// untranslated placeholder awaiting a named human localiser, so a DE-parity send gate would refuse
/// every message this game can currently send. What is implemented instead is the half that is still
/// true and still protective: the template must resolve in the locale this build SHIPS. DE parity is
/// not dropped — it is a content-load invariant already, checked over the whole set before the
/// server ever starts, so a second check here would be a second gate over the same fact.
/// </para>
/// </remarks>
public sealed class MailTemplateCatalogue
{
    /// <summary>The document the templates are authored in.</summary>
    public const string DocumentPath = "content/mail/mail.json";

    /// <summary>
    /// The locale a build ships to players, and therefore the one a template must resolve in.
    /// </summary>
    /// <remarks>
    /// A constant rather than a setting: there is exactly one authored locale, and the other is
    /// placeholders. It becomes a setting on the commit that fills the sentinels, which is the
    /// commit that has to name the human who did.
    /// </remarks>
    public const string ShippingLocale = "en";

    private readonly IReadOnlyDictionary<string, MailTemplate> _byBody;
    private readonly ContentSnapshot _content;

    private MailTemplateCatalogue(
        ContentSnapshot content,
        IReadOnlyDictionary<string, MailTemplate> byBody,
        IReadOnlyList<MailTemplate> authored)
    {
        _content = content;
        _byBody = byBody;
        Templates = authored;
    }

    /// <summary>
    /// Every authored template, in the order the document writes them — held rather than read off
    /// the lookup, whose enumeration order is not part of its contract.
    /// </summary>
    public IReadOnlyList<MailTemplate> Templates { get; }

    /// <summary>Reads the catalogue out of a loaded content set.</summary>
    /// <param name="content">The content snapshot.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The document is missing or malformed. A schema-validated content set cannot produce this, so
    /// it is a miswired caller handing over an unvalidated snapshot rather than an authoring fault.
    /// </exception>
    public static MailTemplateCatalogue Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var templates = new Dictionary<string, MailTemplate>(StringComparer.Ordinal);
        var authored = new List<MailTemplate>();
        var document = content.GetDocument(DocumentPath);

        if (!document.Root.TryGetMember("templates", out var list) || list!.Kind != ContentValueKind.Array)
        {
            throw new InvalidOperationException(
                DocumentPath + " carries no 'templates' array. The schema requires one, so a " +
                "snapshot without it was not validated — and a catalogue that read as empty would " +
                "refuse every send with 'no such template'.");
        }

        foreach (var entry in list.Items)
        {
            var template = new MailTemplate(
                Text(entry, "body"),
                Text(entry, "title"),
                Named<MessageCategory>(Text(entry, "category"), "category"),
                ReadParams(entry));

            if (!templates.TryAdd(template.Body, template))
            {
                throw new InvalidOperationException(
                    DocumentPath + " authors '" + template.Body + "' twice. Two templates under one " +
                    "key are two different messages a send cannot choose between.");
            }

            authored.Add(template);
        }

        return new MailTemplateCatalogue(content, templates, authored);
    }

    /// <summary>The template with this id, or <c>null</c> when none is authored.</summary>
    /// <param name="templateId">The body loc key that identifies the template.</param>
    public MailTemplate? Find(string templateId) =>
        templateId is not null && _byBody.TryGetValue(templateId, out var template) ? template : null;

    /// <summary>
    /// Checks a send against the catalogue: the template exists, resolves, and its parameters are
    /// supplied and well typed.
    /// </summary>
    /// <param name="templateId">The template to send.</param>
    /// <param name="parameters">The values to fill it with.</param>
    /// <returns>Every reason the send is refused, each naming what is wrong. Empty means it may go.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    public IReadOnlyList<(MailSendRefusal Refusal, string Detail)> Check(
        string templateId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (Find(templateId) is not { } template)
        {
            return new[]
            {
                (MailSendRefusal.UNKNOWN_TEMPLATE,
                    "'" + templateId + "' is not an authored template. A message is a template and " +
                    "typed parameters, never a body somebody typed."),
            };
        }

        var refusals = new List<(MailSendRefusal, string)>();

        foreach (var key in new[] { template.Body, template.Title })
        {
            if (!_content.TryRead(StringReference(key), out var value) ||
                value!.Kind != ContentValueKind.Text ||
                string.IsNullOrWhiteSpace(value.AsText()))
            {
                refusals.Add((MailSendRefusal.UNTRANSLATED_TEMPLATE,
                    "'" + key + "' does not resolve in the shipping locale '" + ShippingLocale +
                    "'. A message whose own text renders as its key is one the player is shown the " +
                    "machinery of."));
            }
        }

        foreach (var declared in template.Params)
        {
            if (!parameters.TryGetValue(declared.Name, out var value))
            {
                refusals.Add((MailSendRefusal.MISSING_PARAM,
                    "'" + declared.Name + "' is named by the template's text and this send does not " +
                    "supply it, so the sentence would render with a hole in it."));
                continue;
            }

            if (!IsWellTyped(declared.Type, value))
            {
                refusals.Add((MailSendRefusal.MALFORMED_PARAM,
                    "'" + declared.Name + "' is declared " + declared.Type + " and '" + value +
                    "' is not one."));
            }
        }

        var names = template.Params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var supplied in parameters.Keys.Where(k => !names.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            refusals.Add((MailSendRefusal.UNEXPECTED_PARAM,
                "'" + supplied + "' is not a parameter this template names, so its value would be " +
                "carried by every stored row and rendered nowhere."));
        }

        return refusals;
    }

    /// <summary>Whether a value is the type the template declared.</summary>
    /// <param name="type">The declared type.</param>
    /// <param name="value">The supplied value, already rendered as text.</param>
    /// <exception cref="ArgumentOutOfRangeException">The type is not a declared one.</exception>
    public static bool IsWellTyped(MailParamType type, string? value) =>
        type switch
        {
            MailParamType.TEXT => !string.IsNullOrWhiteSpace(value),
            MailParamType.INTEGER => long.TryParse(
                value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            MailParamType.DATE_UTC => DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type,
                "A parameter type was added to the vocabulary without being given a way to be " +
                "checked, so every value of it would be accepted."),
        };

    private static string StringReference(string key) =>
        "loc/" + ShippingLocale + ".json#/strings/" + key;

    private static string Text(ContentValue entry, string member) =>
        entry.TryGetMember(member, out var value) && value!.Kind == ContentValueKind.Text
            ? value.AsText()
            : throw new InvalidOperationException(
                "A template in " + DocumentPath + " carries no '" + member + "'. The schema requires " +
                "it, so a snapshot missing it was never validated.");

    private static IReadOnlyList<MailTemplateParam> ReadParams(ContentValue entry)
    {
        if (!entry.TryGetMember("params", out var list) || list!.Kind != ContentValueKind.Array)
        {
            throw new InvalidOperationException(
                "A template in " + DocumentPath + " carries no 'params' array. An absent list and an " +
                "empty one are different claims, and only the second is authored.");
        }

        return list.Items
            .Select(p => new MailTemplateParam(
                Text(p, "name"), Named<MailParamType>(Text(p, "type"), "type")))
            .ToArray();
    }

    /// <summary>One authored enum name, refused the way every other malformed member here is.</summary>
    /// <remarks>
    /// 🔒 Not <c>Enum.Parse</c>. It throws a bare <see cref="ArgumentException"/> where this reader's
    /// whole contract is a located <see cref="InvalidOperationException"/> naming the document — and
    /// it also accepts the NUMERIC spelling, so a value the schema never constrained would parse
    /// into an enum member that does not exist. This parse is the last line of defence against the
    /// C# enum and the schema drifting apart, which is the one failure it must not be silent about.
    /// </remarks>
    private static T Named<T>(string text, string member)
        where T : struct, Enum =>
        Enum.TryParse<T>(text, ignoreCase: false, out var value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidOperationException(
                DocumentPath + " authors '" + text + "' as a " + member + ", which is not one of " +
                string.Join(", ", Enum.GetNames<T>()) + ".");
}
