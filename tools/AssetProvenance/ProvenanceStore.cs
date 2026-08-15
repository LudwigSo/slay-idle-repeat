using System.Text.Json;

namespace SlayIdleRepeat.AssetProvenance;

/// <summary>
/// Reads and writes <c>assets/provenance/</c> — one JSON record per asset id, plus the tool
/// licence register.
/// </summary>
/// <remarks>
/// <para>
/// Not under <c>game-data/</c>: that directory is enumerated into the content hash used for the
/// runtime's replay compatibility check, and a provenance file there would move that hash once per
/// generated asset. <c>assets/</c> is the art/audio production area; <c>game-data/</c> is runtime
/// game content.
/// </para>
/// <para>
/// One file per asset id, named <c>{assetId}.json</c>, rather than a single array file — provenance
/// is authored by generation sessions running in batches over months, and a shared array file would
/// put every session in the same merge conflict.
/// </para>
/// <para>
/// Parsing is explicit and member-by-member: a deserialiser turns an absent member into
/// <c>default</c>, and a provenance record whose seed silently became <c>""</c> is worse than one
/// that fails to load.
/// </para>
/// </remarks>
public static class ProvenanceStore
{
    /// <summary>The store's own directory name, below the production area.</summary>
    public const string DirectoryName = "provenance";

    /// <summary>The store directory, relative to the repository root.</summary>
    public const string StoreDirectory = DeliveredAssets.AssetsDirectory + "/" + DirectoryName;

    /// <summary>The subdirectory holding one file per asset.</summary>
    public const string RecordsDirectoryName = "records";

    /// <summary>The tool licence register's file name.</summary>
    public const string LicenceFileName = "tool-licences.json";

    /// <summary>The extension every record file carries.</summary>
    public const string RecordExtension = ".json";

    /// <summary>Absolute path of the store, given a repository root.</summary>
    public static string RootFor(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Path.Combine(repositoryRoot, DeliveredAssets.AssetsDirectory, DirectoryName);
    }

    /// <summary>Loads the whole store from its root directory.</summary>
    /// <exception cref="ProvenanceFormatException">
    /// The store, its records directory or its licence register is missing, or a record is
    /// malformed. A missing store is a failure, never an empty result — "there are no records" and
    /// "the records moved" must not look the same to a gate whose job is to notice the difference.
    /// </exception>
    public static ProvenanceRecordSet Load(string storeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);

        if (!Directory.Exists(storeRoot))
        {
            throw new ProvenanceFormatException(
                StoreDirectory,
                $"does not exist at '{storeRoot}'. 15 §G and 20 §2.1 require a provenance record " +
                "for every generated asset; a gate that reported 'no records, nothing delivered, " +
                "all clear' over a store that had been moved or deleted would be worse than no " +
                "gate at all.");
        }

        var recordsDirectory = Path.Combine(storeRoot, RecordsDirectoryName);
        if (!Directory.Exists(recordsDirectory))
        {
            throw new ProvenanceFormatException(
                $"{StoreDirectory}/{RecordsDirectoryName}",
                $"does not exist at '{recordsDirectory}'. The store keeps one file per asset id " +
                "here; the directory is committed empty precisely so that its absence is a defect " +
                "rather than a state.");
        }

        var licencePath = Path.Combine(storeRoot, LicenceFileName);
        if (!File.Exists(licencePath))
        {
            throw new ProvenanceFormatException(
                $"{StoreDirectory}/{LicenceFileName}",
                $"is missing at '{licencePath}'. It is the register 20 §6's \"commercial licence " +
                "for each tool confirmed in writing\" is checked against, and a gate with no " +
                "register would pass every tool.");
        }

        // The store is flat. A record filed one directory deep would be neither loaded nor
        // reported by a TopDirectoryOnly scan — a quiet place to hide a record. Recursing instead
        // would let two records for one asset live at two paths with only the duplicate check
        // noticing, so the shape is refused outright.
        var strays = Directory.GetDirectories(recordsDirectory);
        if (strays.Length > 0)
        {
            throw new ProvenanceFormatException(
                $"{StoreDirectory}/{RecordsDirectoryName}",
                $"has {strays.Length} subdirector(y/ies) — '{string.Join("', '", strays.Select(Path.GetFileName))}'. " +
                "The store is flat: one file per asset id, named {assetId}.json, directly here. A " +
                "record below this level is invisible to the gate in both directions.");
        }

        var records = new List<ProvenanceRecord>();
        foreach (var file in Directory
                     .GetFiles(recordsDirectory, "*" + RecordExtension, SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            records.Add(ReadRecord(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
        }

        return new ProvenanceRecordSet(records, ReadLicences(File.ReadAllText(licencePath)));
    }

    /// <summary>Parses one record's JSON. <paramref name="expectedId"/> is the file stem the record must agree with.</summary>
    public static ProvenanceRecord ReadRecord(string json, string expectedId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedId);

        using var document = Parse(json, expectedId);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ProvenanceFormatException(
                expectedId, $"is {root.ValueKind}, but a provenance record is a JSON object.");
        }

        var assetId = Text(root, "assetId", expectedId);
        if (!string.Equals(assetId, expectedId, StringComparison.Ordinal))
        {
            throw new ProvenanceFormatException(
                expectedId,
                $"is filed as '{expectedId}{RecordExtension}' but its assetId member says " +
                $"'{assetId}'. The file name is how the store is indexed and the member is what " +
                "the gate matches against the register; while they disagree, one asset has a " +
                "record the gate cannot find and another has one it did not earn.");
        }

        var tooling = ReadTooling(root, expectedId);
        var kind = Text(root, "kind", expectedId);

        return kind switch
        {
            MidjourneyProvenance.KindName => new MidjourneyProvenance
            {
                AssetId = assetId,
                Tooling = tooling,
                JobId = Text(root, "jobId", expectedId),
                Prompt = Text(root, "prompt", expectedId),
                Seed = Text(root, "seed", expectedId),
                Sref = Text(root, "sref", expectedId),
                AspectRatio = Text(root, "aspectRatio", expectedId),
                Style = Text(root, "style", expectedId),
                Stylize = Text(root, "stylize", expectedId),
                ModelVersion = Text(root, "modelVersion", expectedId),
                Date = Text(root, "date", expectedId),
            },
            ProceduralProvenance.KindName => new ProceduralProvenance
            {
                AssetId = assetId,
                Tooling = tooling,
                Generator = Text(root, "generator", expectedId),
                RepoCommit = Text(root, "repoCommit", expectedId),
                Parameters = ReadParameters(root, expectedId),
            },
            Cc0Provenance.KindName => new Cc0Provenance
            {
                AssetId = assetId,
                Tooling = tooling,
                Source = Text(root, "source", expectedId),
                Url = Text(root, "url", expectedId),
                Licence = Text(root, "licence", expectedId),
                DateRetrieved = Text(root, "dateRetrieved", expectedId),
            },
            _ => throw new ProvenanceFormatException(
                expectedId,
                $"has kind '{kind}', which is not one of {MidjourneyProvenance.KindName}, " +
                $"{ProceduralProvenance.KindName}, {Cc0Provenance.KindName}. A fourth source of " +
                "assets needs a record shape of its own — 15 §B0's fields do not fit it any more " +
                "than they fit the other two."),
        };
    }

    /// <summary>Serialises one record to the JSON the store holds. The inverse of <see cref="ReadRecord"/>.</summary>
    public static string WriteRecord(ProvenanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var members = new List<KeyValuePair<string, object?>>
        {
            new("assetId", record.AssetId),
            new("kind", record.Kind),
        };

        switch (record)
        {
            case MidjourneyProvenance m:
                members.Add(new("jobId", m.JobId));
                members.Add(new("prompt", m.Prompt));
                members.Add(new("seed", m.Seed));
                members.Add(new("sref", m.Sref));
                members.Add(new("aspectRatio", m.AspectRatio));
                members.Add(new("style", m.Style));
                members.Add(new("stylize", m.Stylize));
                members.Add(new("modelVersion", m.ModelVersion));
                members.Add(new("date", m.Date));
                break;
            case ProceduralProvenance p:
                members.Add(new("generator", p.Generator));
                members.Add(new("repoCommit", p.RepoCommit));
                members.Add(new("parameters", p.Parameters));
                break;
            case Cc0Provenance c:
                members.Add(new("source", c.Source));
                members.Add(new("url", c.Url));
                members.Add(new("licence", c.Licence));
                members.Add(new("dateRetrieved", c.DateRetrieved));
                break;
            default:
                throw new ProvenanceFormatException(
                    record.AssetId,
                    $"is a {record.GetType().Name}, which this writer does not know how to " +
                    "serialise. A new record kind adds a case here and a case in ReadRecord, in " +
                    "the same change — a round trip that drops a member is a record that lies.");
        }

        if (record.Tooling is { } tooling)
        {
            members.Add(new("tool", tooling.Tool));
            members.Add(new("toolVersion", tooling.Version));
        }

        // The relaxed encoder, not the default: these files are read by a human auditing a licence
        // claim, never injected into a page, and the default HTML-safe encoder would escape a
        // prompt full of punctuation into something nobody can read.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in members)
            {
                if (value is IReadOnlyDictionary<string, string> map)
                {
                    writer.WriteStartObject(name);
                    foreach (var entry in map.OrderBy(e => e.Key, StringComparer.Ordinal))
                    {
                        writer.WriteString(entry.Key, entry.Value);
                    }

                    writer.WriteEndObject();
                    continue;
                }

                writer.WriteString(name, (string?)value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Parses the tool licence register.</summary>
    public static ToolLicenceRegister ReadLicences(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = Parse(json, LicenceFileName);
        var licences = new List<ToolLicence>();

        foreach (var entry in Items(document.RootElement, "licences", LicenceFileName))
        {
            var tool = Text(entry, "tool", LicenceFileName);
            licences.Add(new ToolLicence(
                tool,
                Text(entry, "appliesTo", tool),
                // Absent stays null. No `?? false` and no `?? true` here.
                NullableBool(entry, "confirmedInWriting", tool),
                NullableText(entry, "confirmationRef", tool),
                Text(entry, "note", tool)));
        }

        return new ToolLicenceRegister(licences);
    }

    private static AudioTooling? ReadTooling(JsonElement root, string owner)
    {
        var tool = NullableText(root, "tool", owner);
        var version = NullableText(root, "toolVersion", owner);

        return (tool, version) switch
        {
            (null, null) => null,
            (not null, not null) => new AudioTooling(tool, version),
            _ => throw new ProvenanceFormatException(
                owner,
                $"carries tool={Quote(tool)} and toolVersion={Quote(version)}. 20 §2.1 asks for " +
                "the tool AND its version; half of a pair is not a provenance record, it is a " +
                "note. Write both, or neither."),
        };
    }

    private static IReadOnlyDictionary<string, string> ReadParameters(JsonElement root, string owner)
    {
        var element = Read(root, "parameters", owner);
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProvenanceFormatException(
                owner, $"member 'parameters' is {element.ValueKind}, but it must be an object of " +
                       "scalar values.");
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            parameters[member.Name] = member.Value.ValueKind switch
            {
                JsonValueKind.String => member.Value.GetString()!,
                JsonValueKind.Number => member.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new ProvenanceFormatException(
                    owner, $"parameter '{member.Name}' is {member.Value.ValueKind}; a generator " +
                           "parameter is a scalar, so that the record reads as the command line " +
                           "that produced the asset."),
            };
        }

        return parameters;
    }

    private static JsonDocument Parse(string json, string owner)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProvenanceFormatException(
                owner, $"is not valid JSON: {exception.Message}");
        }
    }

    private static JsonElement Read(JsonElement parent, string name, string owner) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : throw new ProvenanceFormatException(
                owner,
                $"has no member '{name}'. Every member of a record's kind is required: a " +
                "provenance record with a hole in it is not evidence of anything, and defaulting " +
                "the hole would hide that.");

    private static string Text(JsonElement parent, string name, string owner)
    {
        var value = Read(parent, name, owner);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ProvenanceFormatException(
                owner, $"member '{name}' is {value.ValueKind}, but it must be a string.");
    }

    private static string? NullableText(JsonElement parent, string name, string owner)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ProvenanceFormatException(
                owner, $"member '{name}' is {value.ValueKind}, but it must be a string or absent.");
    }

    private static bool? NullableBool(JsonElement parent, string name, string owner)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ProvenanceFormatException(
                owner, $"member '{name}' is {value.ValueKind}, but it must be a boolean or absent."),
        };
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement parent, string name, string owner)
    {
        var value = Read(parent, name, owner);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw new ProvenanceFormatException(
                owner, $"member '{name}' is {value.ValueKind}, but it must be an array.");
    }

    private static string Quote(string? value) => value is null ? "<absent>" : $"'{value}'";
}

/// <summary>Every provenance record in the store, plus the tool licence register.</summary>
public sealed class ProvenanceRecordSet
{
    private readonly Dictionary<string, ProvenanceRecord> _byAssetId;

    /// <summary>Builds the set.</summary>
    /// <exception cref="ProvenanceFormatException">Two records claim the same asset id.</exception>
    public ProvenanceRecordSet(IReadOnlyList<ProvenanceRecord> records, ToolLicenceRegister licences)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(licences);

        _byAssetId = new Dictionary<string, ProvenanceRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!_byAssetId.TryAdd(record.AssetId, record))
            {
                throw new ProvenanceFormatException(
                    record.AssetId,
                    "has two provenance records. One asset, one provenance: two records means the " +
                    "gate would report a covered asset while one of the two answers is wrong, and " +
                    "there is no way to tell which.");
            }
        }

        Records = records;
        Licences = licences;
    }

    /// <summary>Every record, in store order.</summary>
    public IReadOnlyList<ProvenanceRecord> Records { get; }

    /// <summary>The tool licence register.</summary>
    public ToolLicenceRegister Licences { get; }

    /// <summary>The record for an asset id, or null.</summary>
    public ProvenanceRecord? Find(string assetId) => _byAssetId.GetValueOrDefault(assetId);
}

/// <summary>The store on disk does not have the shape the reader and the record format agree on.</summary>
public sealed class ProvenanceFormatException(string location, string reason)
    : InvalidOperationException($"{location} {reason}")
{
    /// <summary>Where in the store the problem is — a record's asset id, or a file name.</summary>
    public string Location { get; } = location;
}
