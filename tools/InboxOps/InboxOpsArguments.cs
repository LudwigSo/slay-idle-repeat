using System.Globalization;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.InboxOps;

/// <summary>What the tool was asked to do.</summary>
public enum InboxOpsVerb
{
    /// <summary>Print the authored templates and stop.</summary>
    TEMPLATES = 1,

    /// <summary>Dry-run or execute a send.</summary>
    SEND = 2,
}

/// <summary>One parsed command line.</summary>
/// <param name="Verb">What to do.</param>
/// <param name="TemplateId">Which authored template a send uses.</param>
/// <param name="Params">The template's parameters.</param>
/// <param name="Attachments">What every copy carries.</param>
/// <param name="Target">Who it is addressed to.</param>
/// <param name="Clauses">The segment predicate, empty for the other two targets.</param>
/// <param name="PlayersFile">The file listing the candidate or named player ids, one per line.</param>
/// <param name="Operator">
/// ⚠️ Who is running it, as an UNVERIFIED string — there is no operator identity system anywhere in
/// this repository, so this is what the person at the terminal typed. Required so the audit row is
/// never blank, and worth exactly what a self-declared name is worth.
/// </param>
/// <param name="Execute">
/// 🔒 <c>false</c> is the default and it is the safety mechanism. There is no numeric rate limit on
/// segment sends anywhere in the design set, and inventing one would be a number nobody ruled on —
/// what makes a wrong predicate hard to send is that the dry run runs first, reports the recipient
/// count, and the operator has to come back and say <c>--execute</c>.
/// </param>
/// <param name="ConnectionString">Where the database is, when not taken from the environment.</param>
/// <param name="DataRoot">Where the content set is, when not the checkout's.</param>
public sealed record InboxOpsRequest(
    InboxOpsVerb Verb,
    string TemplateId,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<MailAttachment> Attachments,
    MailTargetKind Target,
    IReadOnlyList<MailSegmentClause> Clauses,
    string PlayersFile,
    string Operator,
    bool Execute,
    string? ConnectionString,
    string? DataRoot);

/// <summary>Parsing the command line — the whole of it, with no I/O, so a test can drive every arm.</summary>
/// <remarks>
/// Separated from <c>Program</c> for the reason every rule in this repository is separated from its
/// composition root: the refusals below decide whether an economy-affecting send is well formed, and
/// a check that only runs when a database is reachable is a check nobody can exercise.
/// </remarks>
public static class InboxOpsArguments
{
    /// <summary>The usage text, printed on any parse failure.</summary>
    public const string Usage = """
        InboxOps — the inbox operations tool (28 Part A).

          InboxOps templates
              Print the authored templates and their parameters.

          InboxOps send --template <locKey> --target ALL|SEGMENT|PLAYER
                        --players <file> --operator <name>
                        [--param name=value ...] [--attach TYPE=AMOUNT ...]
                        [--where FIELD:AT_LEAST|AT_MOST:operand ...]
                        [--execute] [--connection <string>] [--data-root <path>]

              Dry run by DEFAULT. Without --execute nothing is written: the tool reports the
              recipient count the predicate selects and stops. --execute runs the same dry run
              first, then sends, then records the predicate, both counts and the operator in the
              audit log.

              --players is REQUIRED for every target. No adapter in this repository enumerates
              player rows, so ALL means "all of the candidates in this file" and never "all of the
              players there are". Say so in your ops runbook rather than assuming otherwise.

              --operator is REQUIRED and UNVERIFIED. There is no operator identity system here; it
              is recorded, not trusted.

              --connection defaults to the ConnectionStrings__Postgres environment variable.
        """;

    /// <summary>Parses a command line.</summary>
    /// <param name="args">The raw arguments.</param>
    /// <returns>The request, or every reason it could not be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public static (InboxOpsRequest? Request, IReadOnlyList<string> Errors) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var errors = new List<string>();

        if (args.Length == 0)
        {
            return (null, new[] { "no verb. Say 'templates' or 'send'." });
        }

        if (!TryVerb(args[0], out var verb))
        {
            return (null, new[] { $"'{args[0]}' is not a verb. Say 'templates' or 'send'." });
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var attachments = new List<MailAttachment>();
        var clauses = new List<MailSegmentClause>();
        string? template = null;
        string? playersFile = null;
        string? operatorName = null;
        string? connection = null;
        string? dataRoot = null;
        var target = MailTargetKind.PLAYER;
        var targetGiven = false;
        var execute = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--execute":
                    execute = true;
                    break;

                case "--template":
                    template = Next(args, ref i, "--template", errors);
                    break;

                case "--players":
                    playersFile = Next(args, ref i, "--players", errors);
                    break;

                case "--operator":
                    operatorName = Next(args, ref i, "--operator", errors);
                    break;

                case "--connection":
                    connection = Next(args, ref i, "--connection", errors);
                    break;

                case "--data-root":
                    dataRoot = Next(args, ref i, "--data-root", errors);
                    break;

                case "--target":
                    if (Next(args, ref i, "--target", errors) is { } targetText)
                    {
                        targetGiven = true;

                        if (Enum.TryParse<MailTargetKind>(targetText, out var parsed))
                        {
                            target = parsed;
                        }
                        else
                        {
                            errors.Add($"'{targetText}' is not a target. Say ALL, SEGMENT or PLAYER.");
                        }
                    }

                    break;

                case "--param":
                    ReadParam(Next(args, ref i, "--param", errors), parameters, errors);
                    break;

                case "--attach":
                    ReadAttachment(Next(args, ref i, "--attach", errors), attachments, errors);
                    break;

                case "--where":
                    ReadClause(Next(args, ref i, "--where", errors), clauses, errors);
                    break;

                default:
                    errors.Add($"'{args[i]}' is not an option this tool knows.");
                    break;
            }
        }

        if (verb == InboxOpsVerb.SEND)
        {
            RequireSendArguments(template, targetGiven, playersFile, operatorName, errors);
        }

        return errors.Count > 0
            ? (null, errors)
            : (new InboxOpsRequest(
                verb,
                template ?? string.Empty,
                parameters,
                attachments,
                target,
                clauses,
                playersFile ?? string.Empty,
                operatorName ?? string.Empty,
                execute,
                connection,
                dataRoot),
               Array.Empty<string>());
    }

    private static void RequireSendArguments(
        string? template, bool targetGiven, string? playersFile, string? operatorName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("--template is required for a send. A message is an authored template.");
        }

        if (!targetGiven)
        {
            errors.Add(
                "--target is required for a send. Defaulting it would pick who a grant reaches.");
        }

        if (string.IsNullOrWhiteSpace(playersFile))
        {
            errors.Add(
                "--players is required for a send. No adapter here enumerates player rows, so the " +
                "candidates are the ones you supply — and a send with none would silently reach " +
                "nobody while reporting success.");
        }

        if (string.IsNullOrWhiteSpace(operatorName))
        {
            errors.Add(
                "--operator is required for a send. A segment send is economy-affecting and must be " +
                "attributable; the name is unverified, and a blank one is not even that.");
        }
    }

    private static bool TryVerb(string text, out InboxOpsVerb verb)
    {
        switch (text)
        {
            case "templates":
                verb = InboxOpsVerb.TEMPLATES;
                return true;
            case "send":
                verb = InboxOpsVerb.SEND;
                return true;
            default:
                verb = default;
                return false;
        }
    }

    private static string? Next(string[] args, ref int index, string option, List<string> errors)
    {
        if (index + 1 >= args.Length)
        {
            errors.Add($"{option} was given no value.");
            return null;
        }

        return args[++index];
    }

    private static void ReadParam(
        string? text, Dictionary<string, string> parameters, List<string> errors)
    {
        if (text is null)
        {
            return;
        }

        var split = text.IndexOf('=', StringComparison.Ordinal);

        if (split <= 0)
        {
            errors.Add($"--param '{text}' is not name=value.");
            return;
        }

        var name = text[..split];

        if (!parameters.TryAdd(name, text[(split + 1)..]))
        {
            errors.Add(
                $"--param '{name}' was given twice. Two values for one slot is a send whose text " +
                "depends on argument order.");
        }
    }

    private static void ReadAttachment(
        string? text, List<MailAttachment> attachments, List<string> errors)
    {
        if (text is null)
        {
            return;
        }

        var split = text.IndexOf('=', StringComparison.Ordinal);

        if (split <= 0)
        {
            errors.Add($"--attach '{text}' is not TYPE=AMOUNT.");
            return;
        }

        if (!long.TryParse(
                text[(split + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out var amount))
        {
            errors.Add($"--attach '{text}' has an amount that is not a whole number.");
            return;
        }

        attachments.Add(new MailAttachment(text[..split], amount));
    }

    private static void ReadClause(
        string? text, List<MailSegmentClause> clauses, List<string> errors)
    {
        if (text is null)
        {
            return;
        }

        var parts = text.Split(':');

        if (parts.Length != 3)
        {
            errors.Add($"--where '{text}' is not FIELD:OPERATOR:OPERAND.");
            return;
        }

        if (!Enum.TryParse<MailSegmentField>(parts[0], out var field))
        {
            errors.Add(
                $"--where names '{parts[0]}', which is not a segment field. The vocabulary is: " +
                string.Join(", ", Enum.GetNames<MailSegmentField>()) + ".");
            return;
        }

        if (!MailSegmentFields.IsAnswerable(field))
        {
            errors.Add($"--where names '{field}', which cannot be answered: " +
                       MailSegmentFields.RefusalFor(field));
            return;
        }

        if (!Enum.TryParse<MailSegmentOperator>(parts[1], out var comparison))
        {
            errors.Add($"--where '{parts[1]}' is not an operator. Say AT_LEAST or AT_MOST.");
            return;
        }

        if (!long.TryParse(
                parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var operand))
        {
            errors.Add($"--where '{parts[2]}' is not a whole number.");
            return;
        }

        clauses.Add(new MailSegmentClause(field, comparison, operand));
    }
}
