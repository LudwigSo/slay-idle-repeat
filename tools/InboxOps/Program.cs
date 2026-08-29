using System.Globalization;
using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Adapters.Persistence.Postgres;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.InboxOps;

/// <summary>
/// The inbox operations tool: what ops sends, who it reaches, and the record that it happened.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Dry run by default; <c>--execute</c> is the whole gate.</b> The design set requires a dry
/// run reporting the recipient count before dispatch and calls a wrong predicate an economy
/// incident. It also asks for a rate limit and gives no number, so no number is invented here: what
/// makes a wrong send hard is that the operator has to read a count and come back.
/// </para>
/// <para>
/// Exit codes: 0 the run did what was asked, 1 the send was refused or the run failed, 2 the command
/// line could not be read. Nothing else — an unhandled exception escaping to the runtime would print
/// a stack trace naming no send and hand a script a code nobody specified.
/// </para>
/// </remarks>
internal static class Program
{
    private const int Ok = 0;
    private const int Failed = 1;
    private const int BadUsage = 2;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The one exit path: a fault must name the tool, not the runtime.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync("  ERROR " + exception.Message).ConfigureAwait(false);

            return Failed;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var (request, errors) = InboxOpsArguments.Parse(args);

        if (request is null)
        {
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync("  ERROR " + error).ConfigureAwait(false);
            }

            await Console.Error.WriteLineAsync().ConfigureAwait(false);
            await Console.Error.WriteLineAsync(InboxOpsArguments.Usage).ConfigureAwait(false);

            return BadUsage;
        }

        var content = ContentLoader.Load(new LocalFileContentSource(DataRoot(request))).Require();
        var catalogue = MailTemplateCatalogue.Read(content);

        if (request.Verb == InboxOpsVerb.TEMPLATES)
        {
            PrintTemplates(catalogue);

            return Ok;
        }

        return await SendAsync(request, content, catalogue).ConfigureAwait(false);
    }

    private static void PrintTemplates(MailTemplateCatalogue catalogue)
    {
        Console.WriteLine("Authored inbox templates:");

        foreach (var template in catalogue.Templates)
        {
            Console.WriteLine(
                "  " + template.Category + "  " + template.Body +
                (template.Params.Count == 0
                    ? "  (no parameters)"
                    : "  " + string.Join(", ", template.Params.Select(p => p.Name + ":" + p.Type))));
        }
    }

    private static async Task<int> SendAsync(
        InboxOpsRequest request, Core.Content.ContentSnapshot content, MailTemplateCatalogue catalogue)
    {
        var connection = request.ConnectionString
                         ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

        if (string.IsNullOrWhiteSpace(connection))
        {
            await Console.Error.WriteLineAsync(
                "  ERROR no database. Pass --connection, or set ConnectionStrings__Postgres.")
                .ConfigureAwait(false);

            return BadUsage;
        }

        var candidates = ReadPlayers(request.PlayersFile);

        // 48 hours is the run lifetime this deployment configures; the tool never writes a run row,
        // so the value only has to be positive.
        await using var postgres = PostgresPersistence.Create(connection, TimeSpan.FromHours(48));

        var profiles = new List<PlayerProfile>(candidates.Count);
        var missing = new List<PlayerId>();

        foreach (var id in candidates)
        {
            if (await postgres.Players.GetAsync(id, CancellationToken.None).ConfigureAwait(false)
                is { } profile)
            {
                profiles.Add(profile);
            }
            else
            {
                missing.Add(id);
            }
        }

        var clock = new SystemClock();
        var dryRun = MailSegmentSelection.Select(request.Target, request.Clauses, profiles, clock.UtcNow);

        var sendRequest = new MailSendRequest(request.TemplateId, request.Params, request.Attachments);
        var sender = new MailSender(postgres.Messages, catalogue, clock, new SystemIdGenerator());
        var sendRefusals = sender.Check(sendRequest);

        Report(request, dryRun, sendRefusals, missing);

        if (!dryRun.Accepted || sendRefusals.Count > 0)
        {
            return Failed;
        }

        if (!request.Execute)
        {
            Console.WriteLine();
            Console.WriteLine(
                "DRY RUN — nothing was written. Re-run with --execute to send to the " +
                dryRun.Recipients.Count.ToString(CultureInfo.InvariantCulture) + " recipient(s) above.");

            return Ok;
        }

        var result = await sender
            .SendAsync(sendRequest, dryRun.Recipients, CancellationToken.None)
            .ConfigureAwait(false);

        await postgres.MailSegmentAudit
            .AppendAsync(
                new MailSegmentSend(
                    clock.UtcNow,
                    request.Operator,
                    MailSegmentSelection.Describe(request.Target, request.Clauses),
                    request.TemplateId,
                    MailSegmentSend.Render(request.Attachments),
                    dryRun.Recipients.Count,
                    result.Delivered.Count),
                CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(
            "SENT " + result.Delivered.Count.ToString(CultureInfo.InvariantCulture) +
            " message(s), audited against operator '" + request.Operator + "' (UNVERIFIED — this " +
            "repository has no operator identity system; the name is recorded, not checked).");

        return Ok;
    }

    private static void Report(
        InboxOpsRequest request,
        MailSegmentDryRun dryRun,
        IReadOnlyList<(MailSendRefusal Refusal, string Detail)> sendRefusals,
        IReadOnlyList<PlayerId> missing)
    {
        Console.WriteLine("Template   : " + request.TemplateId);
        Console.WriteLine("Target     : " + MailSegmentSelection.Describe(request.Target, request.Clauses));
        Console.WriteLine("Attachments: " + MailSegmentSend.Render(request.Attachments));
        Console.WriteLine("Candidates : " + dryRun.Candidates.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Recipients : " + dryRun.Recipients.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var id in missing)
        {
            Console.Error.WriteLine(
                "  WARN " + id + " is in the candidate file and has no stored row; it was skipped.");
        }

        foreach (var refusal in dryRun.Refusals)
        {
            Console.Error.WriteLine("  ERROR " + refusal);
        }

        foreach (var (refusal, detail) in sendRefusals)
        {
            Console.Error.WriteLine("  ERROR " + refusal + ": " + detail);
        }
    }

    private static IReadOnlyList<PlayerId> ReadPlayers(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "'" + path + "' does not exist. The candidate list is the send's whole recipient " +
                "universe, so a missing file is a send that would reach nobody while reporting " +
                "success.");
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => new PlayerId(line))
            .ToArray();
    }

    private static string DataRoot(InboxOpsRequest request) =>
        request.DataRoot ?? Path.Combine(FindRepositoryRoot(), "game-data");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlayIdleRepeat.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate SlayIdleRepeat.sln above the tool's output, and --data-root was " +
                "not given — so the content set the templates are read from cannot be found.");
    }
}
