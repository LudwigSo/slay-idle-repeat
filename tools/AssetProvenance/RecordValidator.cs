using System.Text.RegularExpressions;

namespace SlayIdleRepeat.AssetProvenance;

/// <summary>Which register an asset id belongs to.</summary>
public enum AssetMedium
{
    /// <summary>An art slot.</summary>
    Art,

    /// <summary>A music track or an SFX.</summary>
    Audio,
}

/// <summary>
/// Validates one record's own contents — the half of the check that needs no delivery set.
/// </summary>
/// <remarks>
/// Split out from <see cref="ProvenanceGate"/> so a generation session can validate a record it
/// has just written without scanning the whole repository, and so the field rules are testable
/// one at a time rather than only through a full gate run.
/// </remarks>
public static partial class RecordValidator
{
    /// <summary>ISO-8601 calendar date, which is the only date shape the store accepts.</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    /// <summary>A full git object name: 40 lower-case hex digits.</summary>
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullCommit();

    /// <summary>
    /// Every field-level problem with a record, given the medium its asset id belongs to.
    /// Empty means the record is well formed.
    /// </summary>
    public static IReadOnlyList<string> Validate(ProvenanceRecord record, AssetMedium medium)
    {
        ArgumentNullException.ThrowIfNull(record);

        var problems = new List<string>();

        Required(problems, record, "assetId", record.AssetId);
        ValidateTooling(problems, record, medium);

        switch (record)
        {
            case MidjourneyProvenance m:
                Required(problems, record, "jobId", m.JobId);
                Required(problems, record, "prompt", m.Prompt);
                Required(problems, record, "seed", m.Seed);
                Required(problems, record, "sref", m.Sref);
                Required(problems, record, "aspectRatio", m.AspectRatio);
                Required(problems, record, "style", m.Style);
                Required(problems, record, "stylize", m.Stylize);
                Required(problems, record, "modelVersion", m.ModelVersion);
                Date(problems, record, "date", m.Date);
                break;

            case ProceduralProvenance p:
                Required(problems, record, "generator", p.Generator);
                if (!FullCommit().IsMatch(p.RepoCommit ?? string.Empty))
                {
                    problems.Add(
                        $"'{record.AssetId}': repoCommit is '{p.RepoCommit}', which is not a full " +
                        "40-hex commit. An abbreviated or branch-shaped value stops being " +
                        "resolvable the moment the branch moves, and the commit is the whole of a " +
                        "procedural record's reproducibility (15 §B0 gives generated art a job id " +
                        "and a seed; code-drawn art has this instead).");
                }

                if (p.Parameters is null)
                {
                    problems.Add(
                        $"'{record.AssetId}': parameters is absent. An empty object is a claim " +
                        "that the generator takes none; an absent one is a claim about nothing.");
                }

                break;

            case Cc0Provenance c:
                Required(problems, record, "source", c.Source);
                Required(problems, record, "licence", c.Licence);
                Date(problems, record, "dateRetrieved", c.DateRetrieved);
                if (!Uri.TryCreate(c.Url ?? string.Empty, UriKind.Absolute, out var url) ||
                    (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                {
                    problems.Add(
                        $"'{record.AssetId}': url is '{c.Url}', which is not an absolute http(s) " +
                        "URL. 20 §2.1 wants the licence to be checkable by someone who was not " +
                        "there, and a relative path is checkable by nobody.");
                }

                break;

            default:
                problems.Add(
                    $"'{record.AssetId}': {record.GetType().Name} is a record kind this validator " +
                    "has no rules for. A kind with no rules passes every check, which is worse " +
                    "than having no record.");
                break;
        }

        return problems;
    }

    /// <summary>Tool + version is required on audio and forbidden on art — both directions.</summary>
    private static void ValidateTooling(List<string> problems, ProvenanceRecord record, AssetMedium medium)
    {
        switch (medium, record.Tooling)
        {
            case (AssetMedium.Audio, null):
                problems.Add(
                    $"'{record.AssetId}': audio, and it carries no tool/toolVersion. 20 §2.1 " +
                    "requires \"(tool, version, prompt, date) for every generated file\", and 20 " +
                    "§6's QA list asks for the commercial licence of each tool in writing — which " +
                    "cannot be checked against a record that does not name one.");
                break;

            case (AssetMedium.Art, not null):
                problems.Add(
                    $"'{record.AssetId}': art, and it carries tool='{record.Tooling!.Tool}'. An " +
                    "art record's tool is named by its kind (15 §B0 locks Midjourney) — a second " +
                    "tool field is a second source of truth for one fact, and the day they " +
                    "disagree neither is evidence.");
                break;

            case (_, { } tooling):
                Required(problems, record, "tool", tooling.Tool);
                Required(problems, record, "toolVersion", tooling.Version);
                break;

            default:
                break;
        }
    }

    private static void Required(List<string> problems, ProvenanceRecord record, string member, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add(
                $"'{record.AssetId}': {member} is blank. 15 §G and 20 §2.1 call the provenance " +
                "record a legal prerequisite; a blank field is the hole a later reader cannot " +
                "tell from a field nobody needed.");
        }
    }

    private static void Date(List<string> problems, ProvenanceRecord record, string member, string? value)
    {
        if (!IsoDate().IsMatch(value ?? string.Empty))
        {
            problems.Add(
                $"'{record.AssetId}': {member} is '{value}', which is not an ISO-8601 YYYY-MM-DD " +
                "date. 15 §B0 and 20 §2.1 both record a date; one written three ways across a " +
                "thousand records is not a date anyone can sort or audit.");
        }
    }
}
