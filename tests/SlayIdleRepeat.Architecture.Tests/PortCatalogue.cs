using System.Text.RegularExpressions;
using Mono.Cecil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 The register of `23` §4's port catalogue: which of the ports that document declares are
/// authored under <c>SlayIdleRepeat.Application/Ports/</c>, and which are deliberately deferred,
/// to whom, and why.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is NOT a second <see cref="GapRegister"/>, and the difference is the assembly.</b>
/// <c>GapRegister</c> is <c>Core</c>-scoped end to end — its predicates are
/// <c>IsPresentInCore</c> and <c>Domain.CoreTypesUnder</c>, and <c>Gap.WaitsFor</c> is documented as
/// "the simple name of a <b>Core</b> type". Ports are <c>Application</c> types, so no port can be
/// carried by that register without redefining <c>WaitsFor</c> for all of its existing entries.
/// This file is the same four-direction mechanism aimed at a different assembly and a different
/// specification section. Anyone reading it as a duplicate should read this paragraph first: one
/// register per subject, not one per milestone (steering S4).
/// </para>
/// <para>
/// <b>The mechanism, and it fails in four directions</b>, exactly as <c>GapRegister</c>'s does.
/// </para>
/// <list type="number">
///   <item><b>Undeclared.</b> Every interface `23` §4 enumerates is either authored under its port
///   namespace or carried by an entry here with an owning task. A port the document declares and
///   nobody built is otherwise indistinguishable from one nobody noticed.</item>
///   <item><b>Stale.</b> An entry whose port now exists fails the build, on the commit that
///   declares it, rather than at some later kickoff.</item>
///   <item><b>Unanchored.</b> An entry deferring a port no transcription enumerates is a promise
///   about nothing: it can never be satisfied, only deleted by hand.</item>
///   <item><b>Vacuous.</b> Both sets have floors — see
///   <c>PortCatalogueTests.The_port_catalogue_subject_sets_have_floors</c> — because every rule
///   above is of the shape "no member of set S fails X" and is green over an empty S.</item>
/// </list>
/// <para>
/// 🔒 <b>Why the deferrals all say the same thing in different clothes.</b> `23` §5 A5 is a live
/// CI gate: a port needs at least two implementations, the real adapter and the in-memory fake.
/// Steering <b>S7</b> adds that a port carrying only a hollow fake is worse than an absent one,
/// because it looks built. Every port below has exactly one real implementation, and that
/// implementation needs infrastructure (Postgres, Redis, an object store, an HTTP host) or a vendor
/// SDK (a store client, an ad mediator, a push provider, a telemetry exporter) that this task is
/// forbidden to introduce. So each could carry a fake and could not carry a second implementation,
/// and declaring it would either fail A5 or satisfy A5 with two fakes. Each entry names the
/// specific dependency, which is what makes it falsifiable rather than a formula.
/// </para>
/// <para>
/// ⚠️ <b>The known limit</b>, stated once (steering S4's own caveat): what is decidable here is the
/// shape — the port arrived, the owner is malformed, the reason is missing. What is not decidable is
/// an entry whose written <i>reason</i> stopped being true while its predicate still holds. Re-read
/// these entries at each milestone kickoff; CI is not doing it for you.
/// </para>
/// </remarks>
internal static class PortCatalogue
{
    /// <summary>
    /// A port `23` §4 declares that is deliberately not authored yet, and the task that authors it.
    /// </summary>
    /// <param name="Port">The interface's simple name, as `23` §4 writes it — e.g. <c>IBillingPort</c>.</param>
    /// <param name="Owner">The milestone task that authors it, e.g. <c>M5-05</c> or <c>M18-06a</c>.</param>
    /// <param name="Why">
    /// Why it is deferred rather than declared. Something a later reader can falsify — which
    /// infrastructure or which vendor SDK its only real implementation needs.
    /// </param>
    internal sealed record PortDeferral(string Port, string Owner, string Why);

    /// <summary>
    /// One `23` §4 subsection's closed list of interfaces, and the port namespace an authored one
    /// lives under.
    /// </summary>
    /// <param name="Citation">The document section, e.g. <c>23 §4.1</c>.</param>
    /// <param name="Namespace">Where an authored port lives, e.g. <c>SlayIdleRepeat.Application.Ports.Client</c>.</param>
    /// <param name="Ports">Every interface name the subsection declares, transcribed.</param>
    internal sealed record SpecifiedPortGroup(string Citation, string Namespace, IReadOnlyList<string> Ports);

    internal const string ClientPortsNamespace = Domain.PortsNamespace + ".Client";
    internal const string ServerPortsNamespace = Domain.PortsNamespace + ".Server";
    internal const string SharedPortsNamespace = Domain.PortsNamespace + ".Shared";

    /// <summary>
    /// 🔒 The transcription of `23` §4's three code blocks: every <c>interface</c> the section
    /// declares, and nothing else.
    /// </summary>
    /// <remarks>
    /// The section's non-interface vocabulary — <c>AdOutcome</c>, <c>AdResultKind</c> and the
    /// payload types its signatures name — is deliberately absent. Those are not ports, and putting
    /// them here would make the undeclared direction demand types whose shape belongs to whichever
    /// milestone builds the port that carries them (steering S6).
    /// <para>
    /// <c>IContentSourcePort</c> is equally deliberately absent: it is this repository's own port
    /// (M0-09), not one `23` §4 enumerates, and the undeclared direction is a claim about the
    /// document rather than about the folder.
    /// </para>
    /// </remarks>
    internal static readonly SpecifiedPortGroup[] SpecifiedPorts =
    {
        new("23 §4.1", ClientPortsNamespace, new[]
        {
            "IRewardedAdPort",
            "IInterstitialAdPort",
            "IBillingPort",
            "IGameApiPort",
            "IRealtimeChannelPort",
            "ILocalCachePort",
            "IPlatformInfoPort",
            "IHapticsPort",
            "IAudioPort",
            "IConsentPort",
            "IPushRegistrationPort",
        }),

        new("23 §4.2", ServerPortsNamespace, new[]
        {
            "IPlayerRepository",
            "IRunStateStore",
            "IIdempotencyStore",
            "IMessageRepository",
            "IGhostRepository",
            "ILeaderboardRepository",
            "IBattleLogStore",
            "IUnitOfWork",
            "IStoreSubscriptionPort",
            "IAdRewardVerificationPort",
            "IPushSenderPort",
            "IAnalyticsSinkPort",
            "ITelemetryPort",
            "IRemoteConfigPort",
        }),

        new("23 §4.3", SharedPortsNamespace, new[]
        {
            "IClockPort",
            "IIdGeneratorPort",
        }),
    };

    /// <summary>
    /// 🔒 Every `23` §4 port that is not declared, with the tracker task that declares it and the
    /// dependency that keeps it deferred. Each entry expires by itself.
    /// </summary>
    internal static readonly PortDeferral[] Deferred =
    {
        // ── 23 §4.1, client ────────────────────────────────────────────────────────────────────

        new("IInterstitialAdPort", "M15-04",
            "Its only real implementation is the MAX interstitial surface behind the same vendor SDK " +
            "the rewarded adapter uses, and unlike the rewarded port there is no subscriber " +
            "auto-grant sibling: an interstitial that is not shown is simply not shown, so there is " +
            "no second non-fake implementation to pair with a fake."),

        new("IBillingPort", "M15-05",
            "Its two real implementations are the Google Play Billing and StoreKit client SDKs, both " +
            "of which need a store account, a signed build and a device to answer at all. A fake " +
            "alone would satisfy the port's shape and prove nothing about either store's purchase " +
            "flow, which is where every defect in this area lives."),

        new("IGameApiPort", "M7-09",
            "Its two implementations are the in-process host (M7-09) and the HTTP adapter (M7-02), " +
            "and neither exists yet: the HTTP one needs a running server, and the in-process one " +
            "needs the use-case layer M5-02 builds. Its signature also names the command envelope " +
            "M5-03 owns, which is the M1 carry-forward 16 blocker — CommandKind is unreachable from " +
            "outside Core, so a command-carrying port cannot be typed today without inventing it."),

        new("IRealtimeChannelPort", "M7-09",
            "A push channel with no server to push from. Its real implementation is a WebSocket " +
            "client against the server M5-06 authenticates and M7-09 first stands up in process; " +
            "the connection-state vocabulary its events carry is M7-02's, not this task's."),

        new("IPlatformInfoPort", "M7-01",
            "Its only real implementation is the Godot platform adapter, which reads device model, " +
            "OS version and locale from the engine runtime. There is no second non-engine source of " +
            "those facts, and this task is forbidden to introduce the Godot runtime."),

        new("IHapticsPort", "M7-01",
            "Its only real implementation is the Godot platform adapter driving the device's " +
            "vibration motor. A fake that records pattern calls proves nothing a caller could not " +
            "assert directly, and there is no second real implementation to compare it against."),

        new("IAudioPort", "M7-01",
            "Its only real implementation is the Godot audio bus. Its signature also names the sfx, " +
            "music and bus vocabularies the audio pipeline milestone authors, none of which exists " +
            "in Core today — declaring the port would mean inventing three enums on behalf of M8."),

        new("IConsentPort", "M15-07",
            "Its only real implementation is the MAX consent-management SDK, which is the same " +
            "vendor dependency as the ad adapters and cannot be exercised without a device and a " +
            "live CMP. The consent-state vocabulary is that milestone's ruling too."),

        new("IPushRegistrationPort", "M16-04",
            "Its only real implementation registers with FCM (and, once iOS reopens, APNs) from the " +
            "device. The provider itself is still an open decision (O5) at the time of writing, so " +
            "declaring the port now would fix a token shape before the provider that issues it is " +
            "chosen."),

        // ── 23 §4.2, server ────────────────────────────────────────────────────────────────────

        new("IPlayerRepository", "M5-05",
            "Its only real implementation is the Postgres adapter, which needs a live database, a " +
            "schema and migrations — infrastructure this task is forbidden to stand up. Its " +
            "signature also names a player profile aggregate and a device fingerprint, neither of " +
            "which exists in Core."),

        new("IRunStateStore", "M5-05",
            "Its only real implementation is the Redis hot cache in front of the Postgres-" +
            "authoritative row, which needs a live Redis. Its TTL semantics are only meaningful " +
            "against a store that actually expires keys, so a fake pair would test nothing."),

        new("IIdempotencyStore", "M5-05",
            "Its only real implementation is the Redis-plus-Postgres pair that records a command " +
            "outcome inside the accepted-command transaction. Both halves are infrastructure, and " +
            "its signature names the command envelope M5-03 owns."),

        new("IMessageRepository", "M5-08",
            "The inbox store. Its only real implementation is Postgres, and its signature names a " +
            "player message, a message id and the six categories the inbox milestone authors — none " +
            "of which exists in Core, so the port cannot be typed without inventing them."),

        // ⚠️ M12-01, not M5-05. The tracker's M5-05 row enumerates the schema it builds — "profiles
        // JSONB/typed split, run snapshots, idempotency, economy event log, messages" — and ghosts
        // are not among them; M12-01 is the row that reads "+ static-row storage". An owner that
        // resolves to a real row but the WRONG real row is the failure this register is weakest
        // against: nothing goes red, the reader just checks the wrong milestone at kickoff.
        new("IGhostRepository", "M12-01",
            "Its only real implementation is Postgres static-row storage, and its whole vocabulary " +
            "is the ghost snapshot the PvP milestone generates. That type is itself still deferred " +
            "in GapRegister, so this port has neither an implementation nor a payload."),

        new("ILeaderboardRepository", "M12-05",
            "A read model over a window-function query with a ten-minute cache: its only real " +
            "implementation is Postgres plus Redis, and the leaderboard page it returns is a view " +
            "model that milestone defines. A fake alone would encode a ranking the database has " +
            "never computed."),

        new("IBattleLogStore", "M5-05",
            "Its only real implementation is the S3-compatible object store, which needs MinIO or a " +
            "hosted bucket. 🔒 THE SHAPE RULING IS CARRIED FORWARD: when M5-05 declares this port it " +
            "must expose NO object-store concept whatsoever — no bucket, no key, no presign, no " +
            "content-type, no region — because the AzureBlob sibling lands at M18-06a and shares " +
            "this exact port, and Azure Blob is not S3-wire-compatible. The port speaks a battle-log " +
            "id and bytes. PortCatalogueTests.No_port_signature_names_an_infrastructure_or_vendor_" +
            "concept enforces that ruling a milestone early, which is the cheap moment. ⚠️ THAT RULE " +
            "IS A TRIPWIRE ON VENDOR SPELLING, NOT A PROOF OF SHAPE: it catches 'bucket', 'presign', " +
            "'objectKey' and 'multipart', and it CANNOT catch the same concept spelled in ordinary " +
            "English — a bare 'key', 'prefix', 'region' or 'endpoint' parameter, or a presigned URL " +
            "returned as Task<Uri>. See InfrastructureVocabulary's remarks for why those terms are " +
            "not bannable. M5-05 reads A4 and decides; the rule only stops the careless half."),

        new("IUnitOfWork", "M5-04",
            "It spans exactly one Postgres transaction — the aggregate snapshots, the idempotency " +
            "outcome and the appended domain events, committed together. A port whose entire meaning " +
            "is a database transaction boundary cannot have a second implementation that is not a " +
            "database, and a fake alone would make the commit rule untestable while looking tested."),

        new("IStoreSubscriptionPort", "M15-05",
            "Its two real implementations are the Google Play Developer API and the App Store Server " +
            "API, both of which need service credentials and a live HTTP call. Its signature names " +
            "the receipt and subscription-status vocabulary that milestone rules on."),

        new("IAdRewardVerificationPort", "M15-03",
            "Its only real implementation validates the MAX server-to-server callback signature, " +
            "which needs the vendor's shared secret and its payload format. The no-fill-still-grants " +
            "rule that sits beside it is that milestone's, and the callback payload type does not " +
            "exist anywhere in this repository."),

        new("IPushSenderPort", "M16-04",
            "The server side of push. Its only real implementation talks to FCM and APNs over HTTP " +
            "with service credentials, and the provider decision (O5) is still open — the same " +
            "blocker the registration port carries, from the other end of the same channel."),

        new("IAnalyticsSinkPort", "M5-11",
            "Its only real implementation is the PostHog server-side sink, a vendor HTTP client with " +
            "a project key. Its signature names an analytics event vocabulary nothing in Core " +
            "declares, and a fire-and-forget buffered sink is precisely the shape whose fake proves " +
            "nothing about delivery."),

        new("ITelemetryPort", "M5-11",
            "Its two real implementations are the OpenTelemetry exporter and the Sentry client, both " +
            "vendor SDKs needing a collector endpoint or a DSN. A span that goes nowhere and an " +
            "exception nobody receives both look identical to a fake."),

        new("IRemoteConfigPort", "M5-10",
            "Its only real implementation fetches JSON over HTTP from the config endpoint that " +
            "milestone builds, so it needs a running server. 🔒 THE M1 CARRY-FORWARD 7 " +
            "RECONCILIATION, recorded rather than silently resolved: 23 §4.2 gives this port an OPEN " +
            "string-keyed surface, T Get<T>(string key, T fallback), plus a SINGULAR FeatureFlag " +
            "type, and neither has a counterpart in Core's FeatureFlags — which is a deliberately " +
            "CLOSED record of exactly four members taking ad placements as plain strings. M5-01 did " +
            "NOT widen FeatureFlags and did not declare the open surface. M5-10 owns choosing " +
            "between the two, and choosing is the point: an open Get<T> beside a closed flag record " +
            "gives the repository two ways to ask the same question."),
    };

    /// <summary>
    /// 🔒 Infrastructure and vendor vocabulary that may not appear anywhere in a port's signature —
    /// its own type name, a member name, a parameter name, or the simple name of any type it names.
    /// Matched as an ordinal, case-insensitive substring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `23` §5 A4: "Ports are named in domain language, not vendor language. <c>IBattleLogStore</c>,
    /// never <c>IS3Client</c>. If the port is named after the vendor, it will be shaped after the
    /// vendor." A2 forbids the vendor TYPE crossing the boundary and
    /// <c>DependencyRuleTests.No_port_signature_exposes_a_vendor_type</c> already enforces that by
    /// assembly. This list is the other half — the vendor CONCEPT, spelled in BCL types and the
    /// port's own words, which A2's assembly check cannot see: <c>PutAsync(string bucket, string
    /// key, ReadOnlyMemory&lt;byte&gt; body)</c> names only <c>System</c> types.
    /// </para>
    /// <para>
    /// 🔒 It is here, a milestone before M5-05 declares the object-store port, because the port's
    /// shape is decided here and the AzureBlob sibling at M18-06a shares it. A substring is a blunt
    /// instrument on purpose: it is checked against the five ports that exist and against every name
    /// `23` §4 itself writes, and any term that collided with legitimate domain vocabulary would
    /// have been dropped with the collision named rather than narrowed into a regex nobody can read.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this rule cannot catch, stated so M5-05 does not over-trust it.</b> It matches the
    /// vendor's own <i>vocabulary</i>. It does not — and cannot, without banning words this
    /// repository legitimately uses — catch an object-store concept spelled in ordinary English:
    /// <c>string key</c>, <c>string prefix</c>, <c>string region</c>, <c>Uri endpoint</c>. Nor can it
    /// see a presigned URL returned as a neutrally-named BCL type (<c>Task&lt;Uri&gt;
    /// GetDownloadLinkAsync(...)</c>), because neither the type nor the member names anything
    /// banned. <c>ILocalCachePort</c> is the proof that the cheap widening is closed:
    /// <c>key</c> is its parameter name on all three of its methods, so banning <c>Key</c> would
    /// make the rule fire on a port that leaks nothing. The rule is a tripwire on the spelling a
    /// developer reaches for when they are copying the SDK, not a proof of shape — M5-05 still has
    /// to read `23` §5 A4 and decide.
    /// </para>
    /// <para>
    /// 🔒 <b>Terms considered and rejected, with the collision measured rather than guessed</b>, so
    /// the next reader does not re-add one and then narrow the matcher to accommodate it:
    /// <list type="bullet">
    ///   <item><c>ETag</c> — matches <c>metaGrowth</c>, <c>MetaGrowthReference</c> and
    ///   <c>MetaScalar</c> in <c>Core/Content/ChapterScalarTuning.cs</c> ("m-<b>etaG</b>rowth").</item>
    ///   <item><c>ContentType</c> — matches <c>ContentLayout.ContentTypeSchemas</c> and
    ///   <c>ContentTypeMismatchException</c>, this repository's own content-pipeline vocabulary.</item>
    ///   <item><c>Key</c>, <c>Prefix</c>, <c>Region</c>, <c>Endpoint</c> — ordinary domain words.
    ///   <c>ILocalCachePort</c> uses <c>key</c> throughout and the game has map regions.</item>
    ///   <item><c>Minio</c> — adds nothing <c>S3</c> does not already catch (MinIO is
    ///   S3-wire-compatible, so its adapter is the <c>.S3</c> one), and it matches
    ///   <c>do-minio-n</c>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static readonly string[] InfrastructureVocabulary =
    {
        "S3", "Bucket", "Presign", "Blob", "ObjectKey", "Multipart",
        "Redis", "Postgres", "Npgsql", "Sql",
        "Http", "WebSocket", "Grpc", "Kafka",
        "Amazon", "Aws", "Azure",
        "AppLovin", "Firebase", "Sentry", "PostHog",
        "StoreKit", "GooglePlay", "Godot", "Npm",
    };

    /// <summary>
    /// 🔒 The object-store terms <see cref="InfrastructureVocabulary"/> must always carry, named one
    /// by one. This is an <b>identity</b> floor, not a count floor.
    /// </summary>
    /// <remarks>
    /// The count floor over the list above cannot protect these: with twenty-odd terms in the list
    /// and a floor comfortably below it, every S3 term could be deleted together and the count would
    /// still clear. The whole reason this rule lands a milestone before M5-05 is the object store —
    /// so the S3 terms specifically, and not the list's length, are what has to survive. Same
    /// construction as <c>Domain.RunRngScopeType</c>'s identity floor under
    /// <c>DeterministicRng_is_constructed_only_inside_Core_Rng</c>.
    /// </remarks>
    internal static readonly string[] ObjectStoreVocabulary =
    {
        "S3", "Bucket", "Presign", "Blob", "ObjectKey", "Multipart",
    };

    /// <summary>A milestone task id: <c>M12</c>, <c>M5-05</c>, or <c>M18-06a</c>.</summary>
    private static readonly Regex TaskId = new(@"^M\d{1,2}(-\d{2}[a-z]?)?$", RegexOptions.Compiled);

    /// <summary>What a stale port deferral means, said once.</summary>
    internal const string StaleConsequence =
        "The port it defers now exists under Application/Ports/, so this entry describes a decision that " +
        "has already been taken. Delete it in the commit that declares the port — an entry kept past its " +
        "expiry is the register saying a seam is missing while the seam is in the build.";

    /// <summary>What an unanchored port deferral means, said once.</summary>
    internal const string UnanchoredConsequence =
        "A deferral is a promise about a port some specification asks for. One whose port no transcription " +
        "enumerates is a promise about nothing: the undeclared direction cannot see it, so it can never be " +
        "satisfied — only deleted by hand. Transcribe the section that declares the port into " +
        "PortCatalogue.SpecifiedPorts in the same commit, or delete the entry.";

    /// <summary>What an undeclared port means, said once.</summary>
    internal const string UndeclaredConsequence =
        "23 §4 declares it, no port namespace holds it, and nothing here says who will build it. Either " +
        "author it with its two implementations and its shared contract suite, or add a " +
        "PortCatalogue.Deferred entry naming the owning tracker task and the dependency that blocks it. An " +
        "undeclared port is indistinguishable from one nobody noticed.";

    /// <summary>True when a port with this simple name is declared anywhere under <c>Application/Ports/</c>.</summary>
    internal static bool IsDeclared(string simpleName) =>
        Domain.Ports.Any(p => p.Name.Equals(simpleName, StringComparison.Ordinal));

    /// <summary>True when a port with this simple name is declared under the given port namespace.</summary>
    internal static bool IsDeclaredUnder(string namespacePrefix, string simpleName) =>
        Domain.Ports.Any(p => p.Name.Equals(simpleName, StringComparison.Ordinal) &&
                              Il.IsUnder(Il.NamespaceOf(p), namespacePrefix));

    /// <summary>
    /// Every entry whose port has since been declared. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// Takes its entries as a parameter rather than reading <see cref="Deferred"/>, so the
    /// self-tests can drive it with a deliberately expired entry and prove it bites — without one
    /// ever being committed. Same construction as <c>GapRegister.Expired</c>.
    /// </remarks>
    internal static IReadOnlyList<string> Expired(IEnumerable<PortDeferral> entries) =>
        entries
            .Where(entry => IsDeclared(entry.Port))
            .Select(entry =>
                $"'{entry.Port}' is declared deferred to {entry.Owner}, but {Domain.PortsNamespace} already " +
                $"declares it. {StaleConsequence}")
            .ToArray();

    /// <summary>
    /// Every port a transcription enumerates that is neither declared in its namespace nor carried
    /// by an entry. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Expired"/>.</remarks>
    internal static IReadOnlyList<string> Undeclared(
        IEnumerable<SpecifiedPortGroup> groups,
        IEnumerable<PortDeferral> entries)
    {
        var deferred = entries.Select(e => e.Port).ToHashSet(StringComparer.Ordinal);

        return (from subsection in groups
                from port in subsection.Ports
                where !IsDeclaredUnder(subsection.Namespace, port)
                where !deferred.Contains(port)
                select $"{subsection.Citation} declares '{port}'. It is not declared under {subsection.Namespace}, " +
                       $"and PortCatalogue.Deferred does not carry it. {UndeclaredConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry deferring a port no <see cref="SpecifiedPortGroup"/> enumerates. Empty means the
    /// register holds.
    /// </summary>
    /// <remarks>The converse of <see cref="Undeclared"/>. Parameterised for the same reason.</remarks>
    internal static IReadOnlyList<string> Unanchored(
        IEnumerable<SpecifiedPortGroup> groups,
        IEnumerable<PortDeferral> entries)
    {
        var specified = groups.SelectMany(g => g.Ports).ToHashSet(StringComparer.Ordinal);

        return entries
            .Where(entry => !specified.Contains(entry.Port))
            .Select(entry =>
                $"'{entry.Port}' is declared deferred to {entry.Owner}, but no PortCatalogue.SpecifiedPorts " +
                $"transcription declares it. {UnanchoredConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every entry that is not well formed: a blank port, a malformed owning task, or no written
    /// reason. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Expired"/>.</remarks>
    internal static IReadOnlyList<string> Malformed(IEnumerable<PortDeferral> entries)
    {
        var offenders = new List<string>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Port))
            {
                offenders.Add($"an entry owned by '{entry.Owner}' names no port.");
            }

            if (!TaskId.IsMatch(entry.Owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{entry.Port}' names owner '{entry.Owner}', which is not a milestone task id " +
                    "(M12, M5-05, M18-06a). An entry with no owner has no expiry a reader can check.");
            }

            if (string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 40)
            {
                offenders.Add(
                    $"'{entry.Port}' carries no written reason worth falsifying. Every deferral here is a " +
                    "23 §5 A5 argument — one real implementation, which needs infrastructure or a vendor " +
                    "SDK — and the entry has to say WHICH, or the next kickoff cannot check it.");
            }
        }

        return offenders;
    }

    /// <summary>
    /// Every place a port's signature names one of the <paramref name="banned"/> terms: the port's
    /// own type name, a member name, a parameter name, or the simple name of a type in a signature.
    /// Empty means the rule holds.
    /// </summary>
    /// <remarks>
    /// Parameterised over both its inputs for the reason <see cref="Expired"/> is: the self-tests
    /// drive it with a term that genuinely appears in a real port, proving it bites, without a
    /// banned concept ever being committed to <see cref="InfrastructureVocabulary"/>'s subjects.
    /// </remarks>
    internal static IReadOnlyList<string> InfrastructureConcepts(
        IEnumerable<TypeDefinition> ports,
        IEnumerable<string> banned)
    {
        var terms = banned.ToArray();
        var offenders = new List<string>();

        foreach (var port in ports)
        {
            foreach (var (name, where) in SignatureNames(port))
            {
                foreach (var term in terms.Where(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add(
                        $"{port.FullName}: {where} names '{name}', which contains the infrastructure or " +
                        $"vendor term '{term}'. 23 §5 A4 — a port shaped after the vendor cannot carry a " +
                        "second implementation, and the second implementation is the whole point of a port.");
                }
            }
        }

        return offenders;
    }

    /// <summary>Every name a port's surface exposes, with a description of where it came from.</summary>
    /// <remarks>
    /// <para>
    /// Fields are in here because C# lets an interface declare a <c>const</c>, and a constant is the
    /// one member kind that contributes no method, no property and no signature type — so a
    /// <c>const string BucketPrefix</c> on a port would be invisible to every other arm below while
    /// putting the vendor's shape in the port's public surface all the same.
    /// </para>
    /// <para>
    /// 🔒 <b>Generic parameters and base interfaces are here for the same reason</b>, and each closes
    /// a hole the arms below leave open. A <c>Task&lt;T&gt; GetAsync&lt;TBucket&gt;(...)</c> puts the
    /// vendor's word in the port's surface while <c>method.Parameters</c> — Cecil's <em>value</em>
    /// parameters — never sees it, and the signature types resolve to the generic parameter, whose
    /// own name is the leak. A port declared as <c>IBattleLogStore : IS3ObjectStore</c> inherits the
    /// whole of the vendor's shape without naming one banned term of its own; the base interface is
    /// only scanned in its own right if it happens to live under <c>Ports/</c> too, and a leaked
    /// base is exactly the one that will not.
    /// </para>
    /// </remarks>
    private static IEnumerable<(string Name, string Where)> SignatureNames(TypeDefinition port)
    {
        yield return (port.Name, "the port type name");

        foreach (var parameter in port.GenericParameters)
        {
            yield return (parameter.Name, $"generic parameter '{parameter.Name}' of the port type");
        }

        foreach (var reference in port.Interfaces.SelectMany(i => Il.Flatten(i.InterfaceType)))
        {
            yield return (reference.Name, $"the base interface '{reference.Name}'");
        }

        foreach (var field in port.Fields)
        {
            yield return (field.Name, $"field '{field.Name}'");

            foreach (var reference in Il.Flatten(field.FieldType))
            {
                yield return (reference.Name, $"a type in the signature of '{field.Name}'");
            }
        }

        foreach (var method in port.Methods)
        {
            yield return (method.Name, $"member '{method.Name}'");

            foreach (var parameter in method.GenericParameters)
            {
                yield return (parameter.Name, $"generic parameter '{parameter.Name}' of '{method.Name}'");
            }

            foreach (var parameter in method.Parameters)
            {
                yield return (parameter.Name, $"parameter '{parameter.Name}' of '{method.Name}'");
            }

            foreach (var reference in Il.SignatureTypes(method).SelectMany(Il.Flatten))
            {
                yield return (reference.Name, $"a type in the signature of '{method.Name}'");
            }
        }

        foreach (var property in port.Properties)
        {
            yield return (property.Name, $"property '{property.Name}'");

            foreach (var reference in Il.Flatten(property.PropertyType))
            {
                yield return (reference.Name, $"a type in the signature of '{property.Name}'");
            }
        }

        foreach (var @event in port.Events)
        {
            yield return (@event.Name, $"event '{@event.Name}'");

            foreach (var reference in Il.Flatten(@event.EventType))
            {
                yield return (reference.Name, $"a type in the signature of '{@event.Name}'");
            }
        }
    }
}
