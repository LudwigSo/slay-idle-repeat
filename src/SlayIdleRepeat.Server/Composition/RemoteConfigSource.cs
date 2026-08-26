using System.Text.Json;
using SlayIdleRepeat.Core;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The reloading server-disk flags document behind <c>GET /config</c> and the command gateway's
/// kill switches (14 §10, §14): one JSON file, parsed whole-or-not-at-all.
/// </summary>
/// <remarks>
/// <para>
/// The document's five members are all optional and each omission is the identity — kill lists say
/// only what they kill. Member names are case-sensitive: a wrong-cased name is an unknown member.
/// A document that says anything unrecognisable (an unknown or duplicate top-level member,
/// a blank kill-list entry, malformed JSON) is refused WHOLE, loudly, keeping the last good state:
/// a typo like <c>disabledChapter</c> must never silently kill nothing. An absent file or an unset
/// path is the identity element — everything enabled — warned with the greppable
/// <c>[remote-config]</c> marker, since ops config that vanished is an incident, not a default.
/// </para>
/// <para>
/// <see cref="Document"/> is the exact accepted raw file text (or a rendered identity document when
/// absent) so <c>GET /config</c> serves byte-for-byte what an operator authored. A refused
/// document's text is never served.
/// </para>
/// </remarks>
public sealed class RemoteConfigSource
{
    private const string Marker = "[remote-config]";

    /// <summary>The identity rendered explicitly: all five members present, each at its identity.</summary>
    private static readonly ConfigState Identity = new(
        new FeatureFlags(
            pvpEnabled: true, plusOfferEnabled: true, mailEnabled: true,
            disabledAdPlacements: [], disabledChapters: []),
        "{\"pvpEnabled\":true,\"plusOfferEnabled\":true,\"mailEnabled\":true," +
        "\"disabledAdPlacements\":[],\"disabledChapters\":[]}");

    private readonly string? _configuredPath;
    private readonly Action<string> _warn;
    private readonly object _reloadGate = new();
    private ConfigState _state;
    private int _reloadLoopStarted;

    /// <summary>Creates the source and performs the initial load pass synchronously.</summary>
    /// <param name="configuredPath">The flags file's path from <c>RemoteConfig:Path</c>, or null/empty when unset.</param>
    /// <param name="warn">The loud-log seam every refusal and absence is reported through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="warn"/> is null.</exception>
    public RemoteConfigSource(string? configuredPath, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);

        _configuredPath = configuredPath;
        _warn = warn;
        _state = Identity;
        Reload();
    }

    /// <summary>The flags the last accepted document resolved to.</summary>
    public FeatureFlags Current => Volatile.Read(ref _state).Flags;

    /// <summary>The exact raw text of the last accepted document, or the rendered identity when absent.</summary>
    public string Document => Volatile.Read(ref _state).Document;

    /// <summary>One load pass: re-reads the file and swaps the state atomically on acceptance.</summary>
    public void Reload()
    {
        lock (_reloadGate)
        {
            if (LoadOnce() is { } loaded)
            {
                Volatile.Write(ref _state, loaded);
            }
        }
    }

    /// <summary>Starts the periodic reload loop on the first call; later calls are no-ops.</summary>
    /// <param name="interval">How often the file is re-read — <c>RemoteConfig:ReloadSeconds</c>.</param>
    /// <remarks>Composition glue: a source a test constructs runs no loop unless the test opts in.</remarks>
    public void EnsureReloadLoopStarted(TimeSpan interval)
    {
        // Validated synchronously: thrown inside the fire-and-forget method below, this would only
        // fault a discarded task — "reloaded every 60 s" would silently become "never".
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        if (Interlocked.Exchange(ref _reloadLoopStarted, 1) != 0)
        {
            return;
        }

        _ = ReloadPeriodicallyAsync(interval);
    }

    private async Task ReloadPeriodicallyAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                Reload();
            }
            catch (Exception unexpected)
            {
                // The loop is this task's whole life; an escaped exception (LoadOnce catches only
                // what it knows) would kill every future reload with nothing logged.
                _warn(Marker + " a reload pass failed unexpectedly; keeping the last accepted state: " + unexpected);
            }
        }
    }

    /// <summary>Reads and parses the file once. Absence answers the identity; a refusal answers null, keeping the last good state.</summary>
    private ConfigState? LoadOnce()
    {
        if (string.IsNullOrEmpty(_configuredPath))
        {
            _warn(Marker + " RemoteConfig:Path is unset; serving the identity document — everything enabled.");
            return Identity;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(_configuredPath);
        }
        catch (Exception absence) when (absence is FileNotFoundException or DirectoryNotFoundException)
        {
            _warn(
                Marker + " no flags file at '" + _configuredPath +
                "'; serving the identity document — everything enabled.");
            return Identity;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            _warn(
                Marker + " the flags file at '" + _configuredPath +
                "' is unreadable; keeping the last accepted state: " + unreadable.Message);
            return null;
        }

        try
        {
            return new ConfigState(Parse(raw), raw);
        }
        catch (Exception refusal) when (refusal is JsonException or FormatException or ArgumentException)
        {
            _warn(
                Marker + " the flags document at '" + _configuredPath +
                "' is refused whole; keeping the last accepted state: " + refusal.Message);
            return null;
        }
    }

    private static FeatureFlags Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The document's root is not a JSON object.");
        }

        var pvpEnabled = true;
        var plusOfferEnabled = true;
        var mailEnabled = true;
        string[] disabledAdPlacements = [];
        string[] disabledChapters = [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in root.EnumerateObject())
        {
            if (!seen.Add(member.Name))
            {
                // JsonDocument tolerates duplicates with the last one winning — silently
                // contradicting whichever of the two the operator believed was in force.
                throw new FormatException("Duplicate top-level member '" + member.Name + "'.");
            }

            switch (member.Name)
            {
                case "pvpEnabled":
                    pvpEnabled = BooleanOf(member);
                    break;
                case "plusOfferEnabled":
                    plusOfferEnabled = BooleanOf(member);
                    break;
                case "mailEnabled":
                    mailEnabled = BooleanOf(member);
                    break;
                case "disabledAdPlacements":
                    disabledAdPlacements = KillListOf(member);
                    break;
                case "disabledChapters":
                    disabledChapters = KillListOf(member);
                    break;
                default:
                    throw new FormatException(
                        "Unknown top-level member '" + member.Name +
                        "' — most likely a typo whose switch would silently throw nothing.");
            }
        }

        return new FeatureFlags(
            pvpEnabled, plusOfferEnabled, mailEnabled, disabledAdPlacements, disabledChapters);
    }

    private static bool BooleanOf(JsonProperty member)
    {
        if (member.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException("Member '" + member.Name + "' is not a boolean.");
        }

        return member.Value.GetBoolean();
    }

    private static string[] KillListOf(JsonProperty member)
    {
        if (member.Value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Member '" + member.Name + "' is not an array of strings.");
        }

        var entries = new string[member.Value.GetArrayLength()];
        var index = 0;
        foreach (var entry in member.Value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Member '" + member.Name + "' holds a non-string entry.");
            }

            entries[index++] = entry.GetString()!;
        }

        return entries;
    }

    /// <summary>The immutable pair swapped atomically: the resolved flags and the exact text they came from.</summary>
    private sealed record ConfigState(FeatureFlags Flags, string Document);
}
