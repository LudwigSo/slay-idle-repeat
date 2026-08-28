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

        new("IGameApiPort", "M7-02",
            "M7-09 landed the in-process game seam — Application.Hosting.IGameHost, one real " +
            "implementation over the M5-02 use cases — and deliberately did NOT declare this port. " +
            "IGameHost is typed on Core's GameCommand; this port carries the command envelope M5-03 " +
            "owns (commandId, sequence, stateHash), which no assembly declares. Its second " +
            "implementation is the HTTP adapter, which needs a running server. And 23 §5 A5 is a " +
            "live CI gate: a declared port needs a shared contract suite under Contract.Tests, so " +
            "declaring it without one fails the build. M7-02 writes the suite, the HTTP adapter and " +
            "the envelope's port shape together, and implements it by wrapping IGameHost. " +
            "🔒 AND THE CONDITION THIS ENTRY IS THE ONLY RECORD OF, because no rule can hold it: " +
            "IGameHost is not a port while its one implementation lives inside Application and " +
            "depends on nothing outside it. The commit that has an ADAPTER PROJECT implement it makes " +
            "it one by 23 §2.2 — an application-owned interface a vendor conforms to — and neither " +
            "Every_port_has_at_least_two_implementations nor X-06 quantifies over anything outside " +
            "Ports/, so that commit goes green with no A5 gate and no suite. M7-02 either moves " +
            "IGameHost under Ports/Client with its shared suite in the same commit, or keeps every " +
            "HTTP implementation above the seam so the interface keeps its single in-process one."),

        new("IRealtimeChannelPort", "M7-02",
            "A push channel with no server to push from. Its real implementation is a WebSocket " +
            "client against the server M5-06 authenticates and M7-09 first stands up in process; " +
            "the connection-state vocabulary its events carry is M7-02's, and M7-09's in-process " +
            "host answers one command at a time with no channel to push down."),

        // ⚠️ IPlatformInfoPort was here, deferred to M7-01. M7-01b declared it — Adapters.Platform.Host
        // over the BCL beside the InMemory fake — and this register FORCED the deletion rather than
        // relying on it being remembered: the commit that added the interface turned
        // No_port_deferral_outlives_the_port_it_defers red on the entry, by name, with the repair in
        // the message. The two entries below are its siblings and did NOT become declarable; the
        // reason they share is now measured rather than argued.

        // ⚠️ M9-04, and it is the NEAREST row rather than a row that names haptics — the same
        // weakness the IGhostRepository and IsLowEndDevice entries carry, recorded rather than
        // smoothed over. M9-04 is "Settings S26 (audio, accessibility, …) + Profile S27" against
        // `13` §8, and `13` §8 is the section carrying "Haptics toggle | On/off" as a REQUIRED v1
        // accessibility feature — so it is the EARLIEST open row whose own spec reference reaches
        // the toggle. It is not the only one: M17-03 ("All 8 accessibility features complete")
        // cites the same `13` §8 and finishes the same feature later. M9-04 is named because it is
        // the row that first needs the port to exist. The owner it REPLACES was M7-01, a task that
        // had already merged: M7-01c's
        // Every_port_catalogue_owner_is_a_task_the_tracker_still_has_open is what found that, and
        // is what will find the next one.
        new("IHapticsPort", "M9-04",
            "🔒 MEASURED, not argued: its only real implementation is GodotHaptics, and calling it " +
            "outside the engine does not throw — it FATALLY FAULTS THE PROCESS. Godot.Input's static " +
            "constructor marshals a StringName through GodotSharp's native shim, whose function " +
            "pointers the engine populates at startup, so a headless call is an " +
            "AccessViolationException that no catch block can see and that takes the whole test host " +
            "down with it. Verified on this repository from a Contract.Tests fixture: 'Der " +
            "Testhostprozess ist abgestuerzt ... at Godot.NativeInterop.NativeFuncs." +
            "godotsharp_string_new_with_utf16_chars ... at Godot.Input..cctor()'. So this adapter can " +
            "never carry a contract fixture in the only tier this repository has, and the moment it " +
            "implements the port Every_implementation_of_a_port_has_a_contract_fixture demands one. " +
            "And there is no second real implementation to put in its place: a desktop host has no " +
            "motor, so a non-engine sibling is a no-op, which is the hollow fake A5 refuses. M7-01b " +
            "declared IPlatformInfoPort past the same engine problem only because THAT port has a " +
            "genuine non-engine reader; haptics has none, and inventing one is worse than waiting. " +
            "🔒 AND THE DOCUMENT NOW AGREES, which it did not when this entry was written: 23 §7.2 " +
            "registered GodotHapticsAdapter against this port, and M7-01c amended it — the new " +
            "§7.2a states the three rules that follow from the measurement above, and " +
            "No_type_in_the_engine_adapter_implements_a_port holds them. So this entry is no " +
            "longer a deferral standing against its own specification; both say the same thing. " +
            "What still expires it is the engine, not the text: the day a Godot class can carry a " +
            "contract fixture, that rule goes red and this entry is what the failure sends you to. " +
            "M9-04 owns declaring the port over whatever implementation exists by then. " +
            "⚠️ THE SHAPE IS ALSO NOT SETTLED, and this half is cheap to fix when the rest is: 23 " +
            "§4.1 writes Play(HapticPattern) and nothing in this repository declares HapticPattern, " +
            "while the adapter that exists takes a duration in milliseconds. 04 §6 authorises exactly " +
            "three patterns — light on roll start, medium on land, heavy on Star/Fortune — so the " +
            "vocabulary is a transcription rather than an invention and is NOT what blocks this."),

        // 🔒 M8-07, read off this entry's own reason rather than chosen: that row is "Audio:
        // mus_home …, core combat SFX, dice SFX, UI SFX; BUS STRUCTURE, DUCKING (incl. mandatory
        // full duck around ads), polyphony caps" — which is `23` §4.1's SetBusVolume and
        // DuckForExternalAudio spelled out as a deliverable, and it is the task that first plays a
        // sound. It is ⛔ blocked on the licence, which is still AHEAD of us; the owner it replaces,
        // M7-01, is behind us.
        new("IAudioPort", "M8-07",
            "Carries the haptics entry's engine problem — GodotAudioOutput faults the same way, for " +
            "the same reason, and has no non-engine sibling either; 23 §7.2a is where M7-01c wrote " +
            "that down, and it replaced the registration of GodotAudioAdapter against this port. " +
            "🔒 AND A CORRECTION THE NEXT " +
            "READER SHOULD NOT HAVE TO MAKE TWICE: the reason this entry USED to give was that " +
            "declaring the port means inventing SfxId/MusicId/AudioBus on M8's behalf, and that is " +
            "FALSE. The vocabularies are all transcribed and committed already — `20` §5's bus " +
            "structure is Master over Music/SFX/UI with the ad duck ruled outright, and all 106 ids " +
            "(§3's 12 music tracks, §4's 94 sfx) are in game-data/assets/asset_manifest_audio.json, " +
            "schema-validated, _status 'transcribed'. Nothing would be invented. What is true is that " +
            "NONE OF THE ASSETS THOSE IDS NAME EXISTS: M8-07 is blocked on an audio-tool licence " +
            "nobody holds, zero audio files are delivered, and a port whose signature is 106 sounds " +
            "that cannot be played has no implementation that could honour it — which is why turning " +
            "manifest data into a Core vocabulary belongs to the task that first plays one."),

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

        // ⚠️ IPlayerRepository, IRunStateStore, IIdempotencyStore and IBattleLogStore were here,
        // deferred to M5-05. M5-05 declared all four — each beside its InMemory fake and its shared
        // contract suite — and this register FORCED the deletions in the declaring commit, exactly
        // as it did for IPlatformInfoPort. Their real store-backed adapters carry no in-repo
        // fixture; ContractSuiteCoverageTests' StoreBackedAdapterExemptions register is where that
        // exception lives, owner-expiring on the CI probe that exercises each of them.

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

        // 🔒 IBattleLogStore's shape ruling, now DISCHARGED rather than carried: M5-05 declared the
        // port speaking a battle-log id and bytes and nothing else — no location, no naming scheme,
        // no link, no Task<Uri>, no bare 'key'/'prefix'/'region'/'endpoint' parameter (23 §5 A4 was
        // read, not just tripwired). The AzureBlob sibling at M18-06a inherits that surface as-is.

        // 🔒 IUnitOfWork's deferral was here, and M5-04 declared the port under Application/Ports/
        // Server/. Its argument — "a fake alone would make the commit rule untestable while looking
        // tested" — is answered rather than routed around: the in-memory implementation carries the
        // both-or-neither cases against a fault it can be told to raise, and the Postgres one is
        // exempted to the round-trip probe that watches a real transaction.

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

        // ⚠️ IAnalyticsSinkPort and ITelemetryPort were here, deferred to M5-11. M5-11 declared both
        // — the recording fakes beside the PostHog sink and the OpenTelemetry/Sentry pair — and this
        // register FORCED the deletion exactly as it did for IPlatformInfoPort above: the commit that
        // added the interfaces turned No_port_deferral_outlives_the_port_it_defers red on both
        // entries, by name. Their old reason ("a fake proves nothing about delivery") did not vanish;
        // it moved to the one register that can hold it now that the ports exist —
        // Contract.Tests' StoreBackedAdapterExemptions, where each vendor adapter carries the owner
        // that observes it instead of a fixture.

        new("IRemoteConfigPort", "M5-15",
            "Its only real implementation fetches JSON over HTTP from GET /config, so it needs a " +
            "running server. 🔒 THE M1 CARRY-FORWARD 7 RECONCILIATION IS CLOSED, AT M5-10: Core's " +
            "deliberately CLOSED FeatureFlags won. 23 §4.2's open string-keyed surface, " +
            "T Get<T>(string key, T fallback), and its SINGULAR FeatureFlag type are REFUSED — an " +
            "open Get<T> beside a closed flag record is two ways to ask the same question. The " +
            "SERVER half shipped PORTLESS as ops config: a server-disk JSON document (RemoteConfig:" +
            "Path, reloaded periodically) behind GET /config, feeding the gateway's kill switches " +
            "with no port declared anywhere. What stays deferred is the CLIENT side — consuming " +
            "that endpoint over HTTP with 14 §10's 6 h cache — which lands with the composition-" +
            "root swap at M5-15."),
    };

    /// <summary>
    /// One declared port's member list as `23` §4 writes it, transcribed.
    /// </summary>
    /// <param name="Citation">The document section, e.g. <c>23 §4.1</c>.</param>
    /// <param name="Port">The interface's simple name.</param>
    /// <param name="Members">Every member name the section's declaration writes.</param>
    internal sealed record SpecifiedPortMembers(string Citation, string Port, IReadOnlyList<string> Members);

    /// <summary>
    /// A member `23` §4 writes on a port this repository <b>has declared</b>, and which that
    /// declaration deliberately left out.
    /// </summary>
    /// <param name="Port">The declared port's simple name.</param>
    /// <param name="Member">The member `23` §4 writes and the port does not declare.</param>
    /// <param name="Owner">The milestone task that decides it, e.g. <c>M9-04</c>.</param>
    /// <param name="Why">Why it is absent rather than declared. Something a later reader can falsify.</param>
    internal sealed record PortMemberOmission(string Port, string Member, string Owner, string Why);

    /// <summary>
    /// 🔒 <c>23</c> §4's member lists for the ports this repository has declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Why this exists at all, and why the port-name register above is not enough.</b>
    /// <see cref="SpecifiedPorts"/> transcribes interface NAMES, so a port declaring four of the
    /// five members `23` §4 writes passes every one of the four directions identically to one
    /// declaring all five. That is a hole with no floor under it: the omission lives in an XML
    /// comment, nothing fails when it stops being true, and steering S4 is explicit that a declared
    /// exception must expire by itself. M7-01b opened the hole by declaring
    /// <c>IPlatformInfoPort</c> without <c>IsLowEndDevice</c>, so M7-01b closes it.
    /// </para>
    /// <para>
    /// ⚠️ <b>NAMES, and that is the whole of what the member directions can see.</b> A port that
    /// declares every name `23` §4 writes and types them differently satisfies all of them, and two
    /// such departures are live and green right now: §4.1 writes <c>string DeviceModel</c> where
    /// <c>IPlatformInfoPort</c> declares <c>string?</c>, and <c>IsReady(AdPlacementId)</c> where
    /// <c>IRewardedAdPort</c> takes a <c>string</c>. Both are deliberate and argued in their ports'
    /// own remarks. Widening to types would mean transcribing §4's signatures, which are written in
    /// a C# that does not compile against this repository's vocabulary — so the limit is stated
    /// here rather than closed, and <c>UndeclaredMemberConsequence</c> says "missing" rather than
    /// "narrower" for the same reason.
    /// </para>
    /// <para>
    /// ⚠️ Only ports that are <b>declared</b> are transcribed here. An undeclared port's members are
    /// its <see cref="Deferred"/> entry's business, and transcribing them would demand a shape from
    /// the milestone that has not built it yet — steering S6, and the same reason
    /// <see cref="SpecifiedPorts"/> deliberately omits `23` §4's payload types.
    /// </para>
    /// </remarks>
    internal static readonly SpecifiedPortMembers[] SpecifiedMembers =
    {
        new("23 §4.1", "IPlatformInfoPort", new[]
        {
            "DeviceModel",
            "OsVersion",
            "AppVersion",
            "Locale",
            "IsLowEndDevice",
        }),

        // ⚠️ Not M7-01b's port, and that is the point: a register one row wide reopens the hole it
        // was written to close the moment the NEXT port is declared narrower. ILocalCachePort is
        // the live case — a declared 23 §4.1 port whose own remarks acknowledge a departure from
        // the section — and Every_declared_specified_port_has_a_member_transcription is what makes
        // the next one impossible to forget. Its three members are all present, differently typed;
        // see the shape caveat on UndeclaredMemberConsequence for what that rule does NOT see.
        new("23 §4.1", "ILocalCachePort", new[]
        {
            "ReadAsync",
            "WriteAsync",
            "DeleteAsync",
        }),

        // 🔒 These three were NOT in the first draft of this register, and the coverage rule above
        // is what produced them: it named IRewardedAdPort, IClockPort and IIdGeneratorPort on its
        // first run as declared ports nothing transcribed. That is the difference between a
        // register and a list somebody maintains.
        new("23 §4.1", "IRewardedAdPort", new[]
        {
            "IsReady",
            "ShowAsync",
            "PreloadAsync",
        }),

        // 🔒 M5-05's four persistence ports, transcribed in the commit that declared them.
        // IIdempotencyStore declares two members beyond the section's pair (ReadLastSequenceAsync,
        // OpenScopeAsync — the 16.3 widening); extra members are the declaration's business, and
        // only MISSING ones are what these directions can see.
        new("23 §4.2", "IPlayerRepository", new[]
        {
            "GetAsync",
            "SaveAsync",
            "CreateAnonymousAsync",
        }),

        new("23 §4.2", "IRunStateStore", new[]
        {
            "GetAsync",
            "SaveAsync",
            "DeleteAsync",
        }),

        new("23 §4.2", "IIdempotencyStore", new[]
        {
            "GetRecordedOutcomeAsync",
            "RecordAsync",
        }),

        // 🔒 M5-04's boundary. The section sketches a no-argument commit over an ambient session;
        // the declaration takes what it commits instead, so the member name is the section's and
        // the shape is the kickoff's — which is exactly the departure this register exists to make
        // visible rather than silent.
        new("23 §4.2", "IUnitOfWork", new[]
        {
            "CommitAsync",
        }),

        new("23 §4.2", "IBattleLogStore", new[]
        {
            "PutAsync",
            "GetAsync",
        }),

        // 🔒 M5-11's two observability server ports, transcribed in the commit that declared them —
        // the coverage rule demands a row the moment a specified port exists. NAMES only, per this
        // register's stated limit: the declared signatures carry §4.2's richer parameter lists
        // (RecordException's optional context, RecordMetric's tags), which the member directions
        // cannot and do not compare.
        new("23 §4.2", "IAnalyticsSinkPort", new[]
        {
            "Track",
        }),

        new("23 §4.2", "ITelemetryPort", new[]
        {
            "RecordException",
            "BeginSpan",
            "RecordMetric",
        }),

        new("23 §4.3", "IClockPort", new[]
        {
            "UtcNow",
        }),

        new("23 §4.3", "IIdGeneratorPort", new[]
        {
            "NewGuid",
            "NewCommandId",
        }),
    };

    /// <summary>
    /// 🔒 Every member <see cref="SpecifiedMembers"/> transcribes that its declared port does not
    /// carry, with the task that decides it and the reason it is absent. Each entry expires by
    /// itself.
    /// </summary>
    internal static readonly PortMemberOmission[] OmittedMembers =
    {
        // ⚠️ M9-04 is the NEAREST row, not a row that names this, and the difference is recorded
        // rather than smoothed over — it is the same weakness the IGhostRepository entry above
        // carries. M9-04 is "Settings S26 (audio, accessibility, account, privacy) + Profile S27",
        // which is where a quality or reduced-motion setting would live and therefore the first
        // place a device-tier answer would have anything to drive. But no tracker row anywhere
        // rules on what makes a device low-end, so a reader checking M9-04 at kickoff may well find
        // that row does not think it owns this. That is the honest state; the alternative is an
        // entry with no expiry at all.
        new("IPlatformInfoPort", "IsLowEndDevice", "M9-04",
            "Nothing in this repository rules what makes a device low-end. 01 §8's success criteria " +
            "name a 2021 mid-range Android under 400 MB of RAM as the bar the game must CLEAR, which " +
            "is not a threshold below which a device is degraded, and no other document offers one. " +
            "Declared, the member would mean each implementation choosing its own cut-off with a " +
            "shared contract suite unable to state what either of them means by it — so the two " +
            "would answer differently for the same handset and nothing would go red. It is a bool " +
            "whose whole content is a number this repository has not decided."),
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
    /// instrument on purpose: it is checked against the six ports that exist and against every name
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

    /// <summary>What a stale member omission means, said once.</summary>
    internal const string StaleMemberConsequence =
        "The port now declares it, so this entry describes a decision that has already been taken. " +
        "Delete it in the commit that declares the member — an entry kept past its expiry is the " +
        "register saying a member is missing while the member is in the build.";

    /// <summary>What an undeclared member means, said once.</summary>
    internal const string UndeclaredMemberConsequence =
        "23 §4 writes it on a port this repository has DECLARED, the declaration does not carry it, " +
        "and nothing here says who decided that. A port MISSING a member the section writes is " +
        "indistinguishable from one nobody finished — the port-name register cannot see the " +
        "difference, because it transcribes names. Either declare the member, or add a " +
        "PortCatalogue.OmittedMembers entry naming the owning task and why it is absent.";

    /// <summary>What a port with no member transcription means, said once.</summary>
    internal const string UntranscribedPortConsequence =
        "It is declared, and 23 §4 writes a member list for it that nothing here transcribes — so " +
        "the member directions quantify over nothing for this port and a narrower declaration goes " +
        "unnoticed exactly the way IPlatformInfoPort's would have. Add a " +
        "PortCatalogue.SpecifiedMembers row transcribing the section's declaration.";


    /// <summary>
    /// Every member of a declared port, whatever kind it is.
    /// </summary>
    /// <remarks>
    /// All four kinds, because `23` §4 writes properties and methods and a later section could write
    /// either — and a rule that read only properties would report a method-shaped member missing
    /// forever. A property's accessors are methods too, so the set is deliberately a union rather
    /// than a partition.
    /// </remarks>
    private static IEnumerable<string> MemberNames(TypeDefinition port) =>
        port.Properties.Select(p => p.Name)
            .Concat(port.Methods.Select(m => m.Name))
            .Concat(port.Fields.Select(f => f.Name))
            .Concat(port.Events.Select(e => e.Name));

    /// <summary>
    /// Every entry whose member has since been declared on its port. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Expired"/>.</remarks>
    internal static IReadOnlyList<string> ExpiredMembers(
        IEnumerable<TypeDefinition> ports,
        IEnumerable<PortMemberOmission> entries)
    {
        var declared = ports.ToArray();

        return (from entry in entries
                let port = declared.FirstOrDefault(p => p.Name.Equals(entry.Port, StringComparison.Ordinal))
                where port is not null
                where MemberNames(port).Contains(entry.Member, StringComparer.Ordinal)
                select $"'{entry.Port}.{entry.Member}' is declared omitted to {entry.Owner}, but the port " +
                       $"already declares it. {StaleMemberConsequence}")
            .ToArray();
    }

    /// <summary>
    /// Every transcribed member of a declared port that is neither declared on it nor carried by an
    /// omission entry. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// ⚠️ Silent about a transcription whose port is not declared: those are
    /// <see cref="Deferred"/>'s subject, and demanding members from an interface that does not exist
    /// would be a second, contradictory answer to the same question.
    /// </remarks>
    internal static IReadOnlyList<string> UndeclaredMembers(
        IEnumerable<SpecifiedPortMembers> transcriptions,
        IEnumerable<TypeDefinition> ports,
        IEnumerable<PortMemberOmission> entries)
    {
        var declared = ports.ToArray();
        var omitted = entries.Select(e => (e.Port, e.Member)).ToHashSet();

        return (from transcription in transcriptions
                let port = declared.FirstOrDefault(p => p.Name.Equals(transcription.Port, StringComparison.Ordinal))
                where port is not null
                let members = MemberNames(port).ToHashSet(StringComparer.Ordinal)
                from member in transcription.Members
                where !members.Contains(member)
                where !omitted.Contains((transcription.Port, member))
                select $"{transcription.Citation} writes '{transcription.Port}.{member}'. The declared port " +
                       $"does not carry it, and PortCatalogue.OmittedMembers does not either. " +
                       UndeclaredMemberConsequence)
            .ToArray();
    }

    /// <summary>
    /// Every declared port that some <see cref="SpecifiedPorts"/> transcription enumerates and no
    /// <see cref="SpecifiedMembers"/> row carries. Empty means the register holds.
    /// </summary>
    /// <remarks>
    /// 🔒 The direction that keeps the member register from being one row wide. Without it,
    /// <see cref="UndeclaredMembers"/> governs exactly the ports somebody remembered to transcribe,
    /// which is the state the port-name register exists instead of. Its subject is derived from
    /// <see cref="SpecifiedPorts"/> rather than from a second hand-kept list, so a port cannot be
    /// exempted by being left off two lists instead of one.
    /// </remarks>
    internal static IReadOnlyList<string> PortsWithoutAMemberTranscription(
        IEnumerable<SpecifiedPortGroup> groups,
        IEnumerable<TypeDefinition> ports,
        IEnumerable<SpecifiedPortMembers> transcriptions)
    {
        var declared = ports.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var transcribed = transcriptions.Select(t => t.Port).ToHashSet(StringComparer.Ordinal);

        return (from subsection in groups
                from port in subsection.Ports
                where declared.Contains(port)
                where !transcribed.Contains(port)
                select $"{subsection.Citation} declares '{port}' and {Domain.PortsNamespace} declares it too. " +
                       UntranscribedPortConsequence)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every omission entry that is not well formed: a blank port or member, a malformed owning
    /// task, or no written reason. Empty means the register holds.
    /// </summary>
    /// <remarks>Parameterised for the same reason as <see cref="Malformed"/>.</remarks>
    internal static IReadOnlyList<string> MalformedMembers(IEnumerable<PortMemberOmission> entries)
    {
        var offenders = new List<string>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Port) || string.IsNullOrWhiteSpace(entry.Member))
            {
                offenders.Add($"an entry owned by '{entry.Owner}' names no port or no member.");
            }

            if (!TaskId.IsMatch(entry.Owner ?? string.Empty))
            {
                offenders.Add(
                    $"'{entry.Port}.{entry.Member}' names owner '{entry.Owner}', which is not a milestone " +
                    "task id (M12, M5-05, M18-06a). An entry with no owner has no expiry a reader can check.");
            }

            if (string.IsNullOrWhiteSpace(entry.Why) || entry.Why.Length < 40)
            {
                offenders.Add(
                    $"'{entry.Port}.{entry.Member}' carries no written reason worth falsifying. A member " +
                    "left off a declared port is a decision, and the entry has to say what the decision " +
                    "rested on or the next kickoff cannot check whether it still holds.");
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

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // M7-01c — the engine exception, and the two things that make it expire.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    // ⚠️ All three are read as PROJECT names (against RepoLayout.ProjectReferences) and as ASSEMBLY
    // names (against ProductionAssemblies.Module). The two coincide throughout this repository —
    // no .csproj here sets AssemblyName — so the differing suffixes are history, not a distinction.

    /// <summary>The adapter project whose every class reaches <c>GodotSharp</c>.</summary>
    internal const string EngineAdapterAssembly = "SlayIdleRepeat.Adapters.Platform.Godot";

    /// <summary>The plain-C# platform adapter that ports are implemented by instead.</summary>
    internal const string HostAdapterAssembly = "SlayIdleRepeat.Adapters.Platform.Host";

    /// <summary>The project whose fixture demand is what makes an engine port impossible.</summary>
    internal const string ContractSuitesProject = "SlayIdleRepeat.Contract.Tests";

    /// <summary>
    /// 🔒 Every assembly that can reach the engine API, and therefore every assembly a port
    /// implementation is forbidden in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The client is in here because it is the route nothing else watches</b>, and the first
    /// draft of this rule missed it. <c>SceneBoundaryRuleTests</c> and
    /// <c>PresenterBoundaryRuleTests</c> govern the client's scenes and presenters and both
    /// deliberately EXCLUDE <c>Composition/</c> — it is the composition root, so naming concrete
    /// types there is its job. <c>ContractSuiteCoverageTests</c> globs
    /// <c>SlayIdleRepeat.Adapters.*</c> and never sees the client at all. So a capability class in
    /// <c>Composition/</c> growing <c>: IHapticsPort</c> would be seen by nothing — while
    /// <c>DependencyRuleTests.Every_port_has_at_least_two_implementations</c>, which DOES scan the
    /// client, would count it as one of the port's two implementations and go green over a port
    /// whose only real implementation kills the test host.
    /// </para>
    /// <para>
    /// ⚠️ Two names, not a glob over "projects using <c>Godot.NET.Sdk</c>": the engine adapter is a
    /// plain <c>Microsoft.NET.Sdk</c> project that references <c>GodotSharp</c> as an ordinary
    /// package, so an SDK test would miss the very project this rule was written for.
    /// </para>
    /// </remarks>
    internal static readonly string[] EngineReachingAssemblies =
    {
        EngineAdapterAssembly, ProductionAssemblies.ClientName,
    };

    /// <summary>
    /// 🔒 The capabilities <see cref="EngineAdapterAssembly"/> carries, named one by one. An
    /// <b>identity</b> floor, for the reason <see cref="ObjectStoreVocabulary"/> is one.
    /// </summary>
    /// <remarks>
    /// A count over the module's types is cleared by whatever replaced the class that left, and a
    /// module that lost every type would leave <c>No_type_in_the_engine_adapter_implements_a_port</c>
    /// reporting success over nothing — which reads identically to "the engine implements no port".
    /// These four are the whole of the project today.
    /// </remarks>
    internal static readonly string[] EngineCapabilities =
    {
        "GodotAudioOutput", "GodotHaptics", "GodotPlatformInfo", "GodotUserPaths",
    };

    /// <summary>What an engine class implementing a port means, said once.</summary>
    internal const string EnginePortConsequence =
        "23 §7.2a says it may not: a class that can reach the engine API cannot carry a " +
        "contract fixture, because every member of it reaches GodotSharp — a shim over native " +
        "function pointers the engine populates at startup — and a headless call is an " +
        "AccessViolationException no catch block can observe, which takes the test host process down " +
        "rather than failing a case. M7-01b measured that from a Contract.Tests fixture. The moment " +
        "a class here implements a port, ContractSuiteCoverageTests." +
        "Every_implementation_of_a_port_has_a_contract_fixture demands the fixture that kills the " +
        "run. Implement the port from " + HostAdapterAssembly + " — the plain-C# sibling that is " +
        "already the shipped precedent — and keep the engine class a capability the composition " +
        "root names directly. If an engine-capable test host or a fixture exemption has genuinely " +
        "arrived, this rule is the thing to delete, and 23 §7.2 is the document to amend back.";

    /// <summary>
    /// Every concrete type in the engine adapter that implements a port. Empty means the ruling
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameterised over both its inputs for the reason <see cref="Expired"/> is: the self-tests
    /// drive it with a type that genuinely implements a port, proving each arm bites, without the
    /// forbidden arrangement ever being committed.
    /// </para>
    /// <para>
    /// ⚠️ <b>What this does NOT close, said so the next reader does not over-trust it.</b> Its
    /// subject is <see cref="EngineReachingAssemblies"/> — two names, listed by hand. A THIRD
    /// project that reached the engine would be governed by nothing here. Today only one other
    /// could: `23` §8's worked example puts <c>MaxBridge</c>, a C# wrapper over a GDScript autoload,
    /// in the same project as <c>AppLovinRewardedAdAdapter</c>, which implements
    /// <c>IRewardedAdPort</c> — the arrangement §7.2a rules impossible. That project holds no source
    /// file at all today; <b>M15-01</b> is the row that writes both halves and the row that has to
    /// split them.
    /// </para>
    /// <para>
    /// ⚠️ <b>And it is an IL rule with no source arm</b>, which this repository has been bitten by:
    /// <c>SceneBoundaryRuleTests</c> carries a second, source-text arm precisely because `23` §7.2
    /// writes the client's composition with <c>#if ANDROID / #elif IOS</c>, and a branch compiled
    /// out on the CI machine leaves no IL for a scan to object to. Measured on this branch: no
    /// <c>#if</c> appears anywhere under the engine adapter or the client's <c>Composition/</c>, so
    /// the gap is latent rather than open — but it is the spelling this rule does not close, and
    /// the task that adds the first conditional there owes it the source arm (steering S18).
    /// </para>
    /// <para>
    /// ⚠️ The <c>IsInterface</c> / <c>IsAbstract</c> / compiler-generated filters are carried
    /// <b>unprobed</b>: no abstract, interface or generated type anywhere in the tree implements a
    /// port, so no arm can drive them. They are here because <c>ContractSuiteCoverageTests</c>'
    /// implementation scan applies the same three, and a rule that disagreed with it about what
    /// counts as an implementation would be answering a different question from the one its failure
    /// message names.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> EnginePortImplementations(
        IEnumerable<TypeDefinition> engineTypes,
        IEnumerable<TypeDefinition> ports)
    {
        var declared = ports.ToArray();

        return (from type in engineTypes
                where type is { IsInterface: false, IsAbstract: false }
                where !Domain.IsCompilerGenerated(type)
                from port in declared
                where Il.ImplementsInterface(type, port.FullName)
                select $"'{type.FullName}' implements the port '{port.Name}'. {EnginePortConsequence}")
            .ToArray();
    }

    /// <summary>
    /// The premise the rule above rests on: <see cref="ContractSuitesProject"/> still references
    /// <see cref="EngineAdapterAssembly"/>. Empty means the premise holds.
    /// </summary>
    /// <remarks>
    /// 🔒 Without this, <c>No_type_in_the_engine_adapter_implements_a_port</c> stays green while its
    /// whole justification evaporates. Drop that <c>ProjectReference</c> and the engine adapter is
    /// no longer in the fixture-coverage scan at all — so an engine class could implement a port,
    /// nothing in <c>Contract.Tests</c> would ask for a fixture, and the deferral reasons for
    /// <c>IHapticsPort</c> and <c>IAudioPort</c> would be describing a constraint that had been
    /// removed. That is the shape steering S4 calls an exemption outliving its reason, and this is
    /// the half of it a rule can actually see.
    /// </remarks>
    internal static IReadOnlyList<string> EnginePremiseBroken(IEnumerable<string> contractSuiteReferences) =>
        contractSuiteReferences.Contains(EngineAdapterAssembly, StringComparer.Ordinal)
            ? Array.Empty<string>()
            : new[]
            {
                $"'{ContractSuitesProject}' no longer project-references '{EngineAdapterAssembly}'. " +
                "The IHapticsPort and IAudioPort deferrals, 23 §7.2's amendment and " +
                "No_type_in_the_engine_adapter_implements_a_port all rest on that reference: it is " +
                "what puts the engine adapter inside ContractSuiteCoverageTests' implementation scan, " +
                "and therefore what makes an engine port impossible rather than merely unwise. " +
                "Without it those three say a thing that is no longer true, and none of them goes " +
                "red. Restore the reference, or rewrite all three in the same commit.",
            };

    /// <summary>Tracker statuses that mean the owning task has already shipped.</summary>
    /// <remarks>
    /// <para>
    /// ✅ is done and 🔍 is in review — in this tracker, always "merged to `milestone/M&lt;N&gt;`" —
    /// and both are a task nobody is going to do again. ⬜, ⏳, 🔄 and ⛔ are all still ahead,
    /// including ⛔, which is blocked rather than finished and is precisely the state
    /// <c>IAudioPort</c>'s owner is in.
    /// </para>
    /// <para>
    /// ⚠️ <b>The tracker's own legend lists five statuses and <see cref="KnownStatuses"/> accepts
    /// six.</b> ⏳ is used by four live rows and appears in no legend. The extra alternative is
    /// deliberate — a glyph the pattern does not know makes its rows vanish from the lookup
    /// silently, and a vanished owner is then reported as "no row this parser could read", which is
    /// loud but points at the wrong thing.
    /// </para>
    /// </remarks>
    internal static readonly string[] ShippedStatuses = { "✅", "🔍" };

    /// <summary>
    /// 🔒 Every status glyph <see cref="TrackerTaskRow"/> accepts. The pattern is BUILT from this
    /// list rather than repeating it, so the two cannot disagree.
    /// </summary>
    internal static readonly string[] KnownStatuses = { "⬜", "🔄", "🔍", "✅", "⛔", "⏳" };

    /// <summary>The tracker's own status legend, transcribed.</summary>
    /// <remarks>
    /// 🔒 <b>The direction here is the whole point, and the first draft had it backwards.</b>
    /// Asserting that the legend line contains each of these five is satisfied by a legend that has
    /// grown a SIXTH — which is precisely the change that would make <see cref="TrackerTaskRow"/>
    /// drop every row using it, silently. <c>PortCatalogueTests</c> therefore reads the glyphs OUT
    /// of the legend line and asserts each is in <see cref="KnownStatuses"/>; this list is what
    /// tells it which characters on that line are statuses at all.
    /// </remarks>
    internal static readonly string[] LegendStatuses = { "⬜", "🔄", "🔍", "✅", "⛔" };

    /// <summary>
    /// A tracker task row: its id and the status glyph its status cell opens with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Anchored on the <b>first</b> <c>| glyph</c> after the id, and both halves of that anchor
    /// are load-bearing against rows that really exist — measured over all 206 task rows, not
    /// assumed.
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Not the last glyph on the line.</b> Four rows carry a second status glyph inside
    ///   their status prose, and two of the four are read backwards by it: <c>M7-10</c> is ⏳ and
    ///   its row goes on to mention a ⛔ CI gap; <c>M2-16a</c> is 🔍 and its row goes on to mention
    ///   an ✅ result.</item>
    ///   <item><b>Not the first glyph on the line.</b> <c>M18-07</c> is ⬜ and its DESCRIPTION cell
    ///   contains "(O18 ✅)" — before the status cell. Only the <c>|</c> in the anchor separates
    ///   the two.</item>
    ///   <item><b>Not a split on <c>|</c>.</b> A description cell containing a pipe inside backticks
    ///   moves every column, and several do.</item>
    /// </list>
    /// </remarks>
    private static readonly Regex TrackerTaskRow = new(
        @"^\| (?<id>M\d{1,2}-\d{2}[a-z]?) \|.*?\| ?(?<status>" + string.Join("|", KnownStatuses) + ")",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// The status glyphs a tracker legend line documents, read out of the line itself.
    /// </summary>
    /// <remarks>
    /// The legend writes each status as a code span whose first word is the glyph —
    /// <c>`⬜ todo` · `🔄 in progress` · …</c> — so the code spans are the whole of it and no
    /// character-class guess about "what an emoji looks like" is needed. Parameterised so the
    /// self-tests can drive it with a legend carrying a status the parser does not know.
    /// </remarks>
    internal static IReadOnlyList<string> LegendGlyphs(string legendLine) =>
        legendLine.Split('`')
            .Where((_, index) => index % 2 == 1)
            .Select(span => span.Split(' ')[0])
            .Where(glyph => glyph.Length > 0)
            .ToArray();

    /// <summary>Every task id the tracker declares, with the status glyphs its rows carry.</summary>
    /// <remarks>
    /// A lookup rather than a dictionary so a duplicated id cannot throw out of a helper; a task
    /// counts as still open only when <em>no</em> row of it has shipped.
    /// </remarks>
    internal static ILookup<string, string> TrackerStatuses(string tracker) =>
        TrackerTaskRow.Matches(tracker)
            .ToLookup(m => m.Groups["id"].Value, m => m.Groups["status"].Value, StringComparer.Ordinal);

    /// <summary>
    /// Every register entry whose owning task the tracker does not declare, or declares as already
    /// shipped. Empty means every entry still has an expiry a reader can reach.
    /// </summary>
    /// <remarks>
    /// 🔒 Steering <b>S4</b>'s M4 amendment, made mechanical: an expiry check must test that the
    /// owner is still OPEN, not that the owner EXISTS. <see cref="Malformed"/> checks the SHAPE of
    /// the id and <c>GapRegisterTests</c>' dispatch rule checks that a row with that id exists —
    /// neither can see an owner that has already merged, which is the state both engine deferrals
    /// were in when M7-01c found them: owned by M7-01, a task that shipped two tasks ago and could
    /// not discharge them even then.
    /// <para>
    /// Parameterised over the tracker statuses for the reason <see cref="Expired"/> is parameterised
    /// over its entries: the self-tests drive it with crafted entries against the real tracker, so
    /// both arms are shown to bite without <c>IMPLEMENTATION_TRACKER.md</c> ever being edited to
    /// prove it.
    /// </para>
    /// <para>
    /// 🔒 <b>Two other registers owe the same predicate and this does NOT close them</b>, recorded
    /// rather than quietly duplicated (steering S4 asks for one mechanism per repo, and this is the
    /// honest account of why there are still three).
    /// <list type="bullet">
    ///   <item><c>GapRegisterTests.Every_deferred_command_names_a_task_the_tracker_declares</c> is
    ///   in this same assembly and checks EXISTENCE only. Measured on this branch, <b>four</b> of
    ///   <c>GameRules</c>' 24 <c>Deferred</c> rows name a task that has already shipped:
    ///   <c>REFORGE_ITEM</c>, <c>RETUNE_ITEM</c> and <c>SET_FOCUS</c> to M4-04 (✅), and
    ///   <c>USE_CONSUMABLE</c> to M3-08 (✅). Pointing that rule at this predicate would turn the
    ///   build red on four re-points that are milestone decisions — which task builds reforge,
    ///   retune, focus and consumables — and inventing four owners is steering S6 with a task id
    ///   instead of a number. M7-01c reports it instead of guessing. ⚠️ Its PARSER is separable
    ///   from that decision and was deliberately left alone too: its id pattern lacks the
    ///   <c>[a-z]?</c> suffix, so lettered ids (<c>M7-01b</c>, <c>M2-16a</c>) fall out of its
    ///   declared set and are reported as owners nobody declared — loud, not silent, and no row it
    ///   governs names one today.</item>
    ///   <item><c>RealDataSetTests</c> in <c>SlayIdleRepeat.Application.Tests</c> already records
    ///   this predicate as owed work, names its eight offenders and names its owner ("the next
    ///   milestone kickoff that touches this baseline"). It cannot share code with this file — it
    ///   is a different assembly and the architecture suite's <c>Infrastructure</c> is internal to
    ///   it — so a shared parser would need a project neither suite has. That entry stands.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> OwnersNoLongerOpen(
        IEnumerable<(string Subject, string Owner)> entries,
        ILookup<string, string> statuses)
    {
        var offenders = new List<string>();

        foreach (var (subject, owner) in entries)
        {
            if (!statuses.Contains(owner))
            {
                offenders.Add(
                    $"'{subject}' names owner '{owner}', and no task row this parser could read " +
                    "declares it in IMPLEMENTATION_TRACKER.md. A deferral whose owner does not " +
                    "exist expires when nobody is looking: the entry keeps saying a seam is coming " +
                    "and no milestone is on the hook for it. ⚠️ Check the OWNER first and the " +
                    "PARSER second — TrackerTaskRow reads task rows only, so a milestone-summary " +
                    "or review row would land here too.");
                continue;
            }

            var shipped = statuses[owner]
                .FirstOrDefault(status => ShippedStatuses.Contains(status, StringComparer.Ordinal));

            if (shipped is not null)
            {
                offenders.Add(
                    $"'{subject}' names owner '{owner}', whose tracker row reads '{shipped}' — " +
                    "that task has already shipped. The entry can now never expire: the milestone " +
                    "that was going to build this is behind us, so nothing will ever delete the " +
                    "exception and no kickoff will ever be asked about it. Name the task that " +
                    "actually builds it next, or build it.");
            }
        }

        return offenders;
    }
}
