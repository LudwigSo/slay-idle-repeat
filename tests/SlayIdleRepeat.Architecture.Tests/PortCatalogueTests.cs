using Mono.Cecil;
using Shouldly;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// `23` §4 / §6 — the rules that keep <see cref="PortCatalogue"/> honest, and the ruling that keeps
/// an infrastructure concept out of a port signature.
/// </summary>
/// <remarks>
/// Four directions over the register — undeclared, stale, unanchored, malformed — plus the
/// vocabulary rule and the S3 floors under both. The register's predicates are parameterised, so
/// each direction is also driven against a deliberately bad entry here: a rule that cannot be shown
/// to fail is a comment with a test around it.
/// </remarks>
public sealed class PortCatalogueTests
{
    // ------------------------------------------------------------------------------------ floors
    //
    // 🔒 Steering S3. Each floor names the rule that goes silent when it is breached, and each is a
    // LITERAL taken from counting the document — never one of the lists' own Count properties,
    // which cannot notice the list being trimmed.

    /// <summary>
    /// 🔒 `23` §4's three code blocks declare 27 interfaces, and this is the count <b>per
    /// subsection</b>: 11 in §4.1, 14 in §4.2, 2 in §4.3. Trimming the transcription would stop the
    /// undeclared direction asking about the ports it dropped, and nothing else in this repository
    /// enumerates them.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Per subsection, and a total floor is not a substitute.</b> This was a single
    /// <c>SpecifiedPortFloor = 20</c> against a transcription of 27, so seven of `23` §4's ports
    /// could be deleted with the floor still clearing — including all of §4.1's eleven client ports,
    /// which is the lane every deferral above is written for. §4 is a closed section of a locked
    /// document, so its subsections have exact counts rather than lower bounds, and each is checked
    /// against the group that claims to transcribe it: a floor over the flattened total is cleared
    /// by a §4.2 name added to cover a §4.1 name dropped.
    /// </remarks>
    private static readonly (string Citation, int Ports)[] SpecifiedPortFloors =
    {
        ("23 §4.1", 11),
        ("23 §4.2", 14),
        ("23 §4.3", 2),
    };

    /// <summary>
    /// Ports declared under <c>Application/Ports/</c>. Below this,
    /// <see cref="No_port_signature_names_an_infrastructure_or_vendor_concept"/> and both
    /// <c>DependencyRuleTests</c> port rules quantify over a set that has lost members.
    /// </summary>
    /// <remarks>
    /// 🔒 M7-01b raised it 5 → 6 for <c>IPlatformInfoPort</c>, together with
    /// <c>ContractSuiteCoverageTests.PortFloor</c> and <c>SubjectSetFloorTests.PortFloor</c>. Three
    /// floors are stated over the same set in three assemblies; one moving without the others is one
    /// of them having gained a member the other two cannot see. M5 raised it 6 → 12 across two tasks
    /// that each moved all three together: M5-05's four server persistence ports and M5-11's two
    /// observability ports (<c>IAnalyticsSinkPort</c>, <c>ITelemetryPort</c>).
    /// </remarks>
    private const int DeclaredPortFloor = 12;

    /// <summary>
    /// Entries in the register. At zero, <see cref="No_port_deferral_outlives_the_port_it_defers"/>,
    /// <see cref="No_port_deferral_names_a_port_the_catalogue_does_not_specify"/> and
    /// <see cref="Every_port_deferral_is_well_formed"/> are all green over nothing, while the
    /// undeclared direction would go red — so a reader would see three ticks and one failure and
    /// reach for the wrong repair.
    /// </summary>
    private const int DeferralFloor = 10;

    /// <summary>
    /// Terms in the banned vocabulary. A shrunken list is
    /// <see cref="No_port_signature_names_an_infrastructure_or_vendor_concept"/> going quiet on
    /// exactly the concept that was removed, with the rule still passing.
    /// </summary>
    /// <remarks>
    /// ⚠️ A count floor here is the weak half and always was: at 15 against a list of 25, every
    /// object-store term could be deleted <em>together</em> and this would still clear — and the
    /// object store is the whole reason the rule lands a milestone before M5-05.
    /// <see cref="PortCatalogue.ObjectStoreVocabulary"/> is the identity floor that closes it, and
    /// the count floor stays because it is what notices the rest of the list draining away one
    /// vendor at a time.
    /// </remarks>
    private const int VocabularyFloor = 15;

    /// <summary>
    /// 🔒 The `23` §4 member transcriptions that must exist, named port by port and member by
    /// member. An <b>identity</b> floor, for the reason
    /// <see cref="PortCatalogue.ObjectStoreVocabulary"/> is one: a count over the transcriptions is
    /// cleared by any row at all, and what has to survive is this row, with these five names.
    /// </summary>
    private static readonly (string Citation, string Port, string[] Members)[] RequiredMemberTranscriptions =
    {
        ("23 §4.1", "IPlatformInfoPort",
            new[] { "DeviceModel", "OsVersion", "AppVersion", "Locale", "IsLowEndDevice" }),
        ("23 §4.1", "ILocalCachePort", new[] { "ReadAsync", "WriteAsync", "DeleteAsync" }),
        ("23 §4.1", "IRewardedAdPort", new[] { "IsReady", "ShowAsync", "PreloadAsync" }),
        ("23 §4.2", "IPlayerRepository", new[] { "GetAsync", "SaveAsync", "CreateAnonymousAsync" }),
        ("23 §4.2", "IRunStateStore", new[] { "GetAsync", "SaveAsync", "DeleteAsync" }),
        ("23 §4.2", "IIdempotencyStore", new[] { "GetRecordedOutcomeAsync", "RecordAsync" }),
        ("23 §4.2", "IBattleLogStore", new[] { "PutAsync", "GetAsync" }),
        ("23 §4.2", "IAnalyticsSinkPort", new[] { "Track" }),
        ("23 §4.2", "ITelemetryPort", new[] { "RecordException", "BeginSpan", "RecordMetric" }),
        ("23 §4.3", "IClockPort", new[] { "UtcNow" }),
        ("23 §4.3", "IIdGeneratorPort", new[] { "NewGuid", "NewCommandId" }),
    };

    /// <summary>
    /// 🔒 `23` §4 / §6 — the undeclared direction: every port the catalogue specifies is either
    /// declared under its port namespace or carried by an entry with an owning task.
    /// </summary>
    /// <remarks>
    /// This is the direction that makes the register more than a list of things someone remembered.
    /// A port `23` §4 declares and nobody built is otherwise indistinguishable from one nobody
    /// noticed, and the whole client lane is written against ports that do not exist yet.
    /// </remarks>
    [Fact]
    public void Every_specified_port_is_declared_or_deferred_with_an_owner()
    {
        ArchRule.Empty(
            PortCatalogue.Undeclared(PortCatalogue.SpecifiedPorts, PortCatalogue.Deferred),
            "Every port 23 §4 declares is authored or declared deferred with an owner (23 §4, 23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §4 / §6 — the stale direction: no deferral outlives the port it defers. The moment a
    /// port appears under <c>Application/Ports/</c>, its entry is wrong and this fails.
    /// </summary>
    [Fact]
    public void No_port_deferral_outlives_the_port_it_defers()
    {
        ArchRule.Empty(
            PortCatalogue.Expired(PortCatalogue.Deferred),
            "No PortCatalogue entry defers a port that now exists (23 §4, steering S4).");
    }

    /// <summary>
    /// 🔒 `23` §4 / §6 — the unanchored direction: every deferral names a port the transcription
    /// actually declares.
    /// </summary>
    /// <remarks>
    /// An entry the undeclared direction cannot see can never be satisfied — only deleted by hand,
    /// which is the state this register exists instead of.
    /// </remarks>
    [Fact]
    public void No_port_deferral_names_a_port_the_catalogue_does_not_specify()
    {
        ArchRule.Empty(
            PortCatalogue.Unanchored(PortCatalogue.SpecifiedPorts, PortCatalogue.Deferred),
            "Every PortCatalogue entry defers a port the 23 §4 transcription declares (23 §4, 23 §6).");
    }

    /// <summary>
    /// `23` §6 — every entry names an owning milestone task and a written reason. An exemption with
    /// no owner has no expiry; one with no reason has nothing to falsify at the next kickoff.
    /// </summary>
    [Fact]
    public void Every_port_deferral_is_well_formed()
    {
        ArchRule.Empty(
            PortCatalogue.Malformed(PortCatalogue.Deferred),
            "Every PortCatalogue entry is well formed: owning task, written reason (23 §6, steering S4).");
    }

    /// <summary>
    /// 🔒 `23` §4 / §6 — the member-level undeclared direction: every member `23` §4 writes on a
    /// port this repository has <b>declared</b> is either on the interface or carried by an omission
    /// entry with an owning task.
    /// </summary>
    /// <remarks>
    /// The four directions above transcribe interface NAMES, so a port that declares four of a
    /// section's five members satisfies all of them. That is how M7-01b's deliberate omission of
    /// <c>IPlatformInfoPort.IsLowEndDevice</c> would otherwise have lived in an XML comment with
    /// nothing able to notice it stopped being true — which steering <b>S4</b> is exactly about.
    /// </remarks>
    [Fact]
    public void Every_specified_member_of_a_declared_port_is_declared_or_omitted_with_an_owner()
    {
        ArchRule.Empty(
            PortCatalogue.UndeclaredMembers(
                PortCatalogue.SpecifiedMembers, Domain.Ports, PortCatalogue.OmittedMembers),
            "Every member 23 §4 writes on a declared port is declared or omitted with an owner (23 §4, 23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §4 / §6 — every declared port that `23` §4 writes a member list for has one
    /// transcribed here.
    /// </summary>
    /// <remarks>
    /// Without this the member register governs exactly the ports somebody remembered to add, and
    /// the next port declared narrower than its section reopens the hole M7-01b closed. Not
    /// hypothetical: <c>ILocalCachePort</c> is a declared `23` §4.1 port whose own remarks
    /// acknowledge a departure from that section, and it had no row until this rule demanded one.
    /// </remarks>
    [Fact]
    public void Every_declared_specified_port_has_a_member_transcription()
    {
        ArchRule.Empty(
            PortCatalogue.PortsWithoutAMemberTranscription(
                PortCatalogue.SpecifiedPorts, Domain.Ports, PortCatalogue.SpecifiedMembers),
            "Every declared 23 §4 port has a member transcription (23 §4, 23 §6, steering S3).");
    }

    /// <summary>
    /// `23` §6 — the teeth of the transcription-coverage direction, and its silent half.
    /// </summary>
    [Fact]
    public void The_member_transcription_rule_fires_on_a_declared_port_with_no_row()
    {
        var group = new PortCatalogue.SpecifiedPortGroup(
            "23 §4.3", PortCatalogue.SharedPortsNamespace, new[] { Domain.ClockPortType });

        var row = new PortCatalogue.SpecifiedPortMembers("23 §4.3", Domain.ClockPortType, new[] { "UtcNow" });

        PortCatalogue.PortsWithoutAMemberTranscription(new[] { group }, Domain.Ports, new[] { row })
            .ShouldBeEmpty("IClockPort is declared and transcribed, which is the arrangement the rule permits.");

        PortCatalogue.PortsWithoutAMemberTranscription(
                new[] { group }, Domain.Ports, Array.Empty<PortCatalogue.SpecifiedPortMembers>())
            .ShouldHaveSingleItem()
            .ShouldContain("nothing here transcribes", Case.Sensitive);

        // ⚠️ The quantifier's control: a port the document names and this repository has NOT
        // declared is the Deferred register's business, not this one.
        PortCatalogue.PortsWithoutAMemberTranscription(
                new[] { group with { Ports = new[] { "IAudioPort" } } },
                Domain.Ports,
                Array.Empty<PortCatalogue.SpecifiedPortMembers>())
            .ShouldBeEmpty(
                "IAudioPort is deferred, so demanding a member transcription for it would ask every "
                + "deferred port for a shape the milestone that owns it has not decided.");
    }

    /// <summary>
    /// 🔒 `23` §4 / §6 — the member-level stale direction: no omission outlives the member it omits.
    /// </summary>
    [Fact]
    public void No_port_member_omission_outlives_the_member_it_omits()
    {
        ArchRule.Empty(
            PortCatalogue.ExpiredMembers(Domain.Ports, PortCatalogue.OmittedMembers),
            "No PortCatalogue.OmittedMembers entry omits a member that now exists (23 §4, steering S4).");
    }

    /// <summary>
    /// `23` §6 — every omission entry names an owning milestone task and a written reason.
    /// </summary>
    [Fact]
    public void Every_port_member_omission_is_well_formed()
    {
        ArchRule.Empty(
            PortCatalogue.MalformedMembers(PortCatalogue.OmittedMembers),
            "Every PortCatalogue.OmittedMembers entry is well formed: owning task, written reason (23 §6, steering S4).");
    }

    /// <summary>
    /// `23` §6 — the teeth of the member-level directions, driven against crafted inputs so each is
    /// shown to bite without a violation ever being committed.
    /// </summary>
    /// <remarks>
    /// Both halves of each direction, and the subjects are the real declared ports: a synthetic
    /// interface would only prove the rules work on synthetic interfaces.
    /// <c>IClockPort.UtcNow</c> is a member that genuinely exists, so it drives the stale direction
    /// and the undeclared direction's silent half from opposite sides.
    /// </remarks>
    [Fact]
    public void The_member_directions_fire_on_a_deliberately_bad_entry_and_are_silent_on_a_good_one()
    {
        var clock = new PortCatalogue.SpecifiedPortMembers(
            "23 §4.3", Domain.ClockPortType, new[] { "UtcNow" });

        var good = new PortCatalogue.PortMemberOmission(
            Domain.ClockPortType, "MonotonicTicks", "M5-10",
            "a reason long enough to be worth falsifying at the next kickoff.");

        // Silent: UtcNow is on IClockPort, so nothing is missing and nothing has expired.
        PortCatalogue.UndeclaredMembers(new[] { clock }, Domain.Ports, new[] { good }).ShouldBeEmpty(
            "IClockPort declares UtcNow, which is the arrangement the rule exists to permit.");
        PortCatalogue.ExpiredMembers(Domain.Ports, new[] { good }).ShouldBeEmpty(
            "IClockPort declares no MonotonicTicks, so this omission has not expired.");
        PortCatalogue.MalformedMembers(new[] { good }).ShouldBeEmpty();

        // Undeclared: a transcribed member the declared port does not carry and no entry claims.
        var withAnExtraMember = clock with { Members = new[] { "UtcNow", "MonotonicTicks" } };

        PortCatalogue.UndeclaredMembers(
                new[] { withAnExtraMember }, Domain.Ports, Array.Empty<PortCatalogue.PortMemberOmission>())
            .ShouldHaveSingleItem()
            .ShouldContain("PortCatalogue.OmittedMembers does not either", Case.Sensitive);

        // 🔒 And silent again once an entry claims it — otherwise the direction above would be
        // satisfied by a rule that flags every transcribed member it cannot find.
        PortCatalogue.UndeclaredMembers(new[] { withAnExtraMember }, Domain.Ports, new[] { good })
            .ShouldBeEmpty("the entry carries exactly the member the port does not declare.");

        // ⚠️ The quantifier's own control: a transcription for a port that is NOT declared says
        // nothing, or the register would demand a shape from every deferred port at once.
        PortCatalogue.UndeclaredMembers(
                new[] { clock with { Port = "IAudioPort", Members = new[] { "PlaySfx" } } },
                Domain.Ports,
                Array.Empty<PortCatalogue.PortMemberOmission>())
            .ShouldBeEmpty(
                "IAudioPort is deferred, not declared. Its members are its Deferred entry's business, "
                + "and demanding them here would be a second answer to the same question.");

        // Stale: an entry omitting a member that is on the port today.
        PortCatalogue.ExpiredMembers(Domain.Ports, new[] { good with { Member = "UtcNow" } })
            .ShouldHaveSingleItem()
            .ShouldContain("already declares it", Case.Sensitive);

        // Malformed, one crafted entry per branch.
        PortCatalogue.MalformedMembers(new[] { good with { Member = "  " } })
            .ShouldContain(o => o.Contains("names no port or no member", StringComparison.Ordinal));

        PortCatalogue.MalformedMembers(new[] { good with { Owner = "someday" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a milestone task id", Case.Sensitive);

        PortCatalogue.MalformedMembers(new[] { good with { Why = "later" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `23` §5 A4 — no port signature names an infrastructure or vendor concept, in its own type
    /// name, a member name, a parameter name, or the simple name of any type it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement of <c>DependencyRuleTests.No_port_signature_exposes_a_vendor_type</c>, which
    /// checks the assembly a signature type comes from (`23` §5 A2). That rule cannot see
    /// <c>PutAsync(string bucket, string key, ReadOnlyMemory&lt;byte&gt; body)</c>: every type there
    /// is BCL, and the S3 shape has crossed the boundary in the parameter <em>names</em>.
    /// </para>
    /// <para>
    /// 🔒 It lands a milestone before the object-store port is declared because the port's shape is
    /// decided by the catalogue this file governs, and because an <c>AzureBlob</c> sibling shares
    /// that port — Azure Blob is not S3-wire-compatible, so a bucket or a presigned URL in the
    /// signature makes the second adapter impossible rather than merely awkward.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_port_signature_names_an_infrastructure_or_vendor_concept()
    {
        ArchRule.Empty(
            PortCatalogue.InfrastructureConcepts(Domain.Ports, PortCatalogue.InfrastructureVocabulary),
            "No port signature names an infrastructure or vendor concept (23 §5 A4).");
    }

    /// <summary>
    /// 🔒 `23` §6, steering <b>S3</b> — the tripwire against the register's own permanent vacuity.
    /// Every rule above is of the shape "no member of set S fails X", and each is green over an
    /// empty S.
    /// </summary>
    [Fact]
    public void The_port_catalogue_subject_sets_have_floors()
    {
        var offenders = new List<string>();

        var specified = PortCatalogue.SpecifiedPorts.SelectMany(g => g.Ports).ToArray();

        foreach (var (citation, expected) in SpecifiedPortFloors)
        {
            var group = PortCatalogue.SpecifiedPorts
                .SingleOrDefault(g => g.Citation.Equals(citation, StringComparison.Ordinal));

            if (group is null)
            {
                offenders.Add(
                    $"no PortCatalogue.SpecifiedPorts group transcribes {citation}. "
                    + "Every_specified_port_is_declared_or_deferred_with_an_owner then asks nothing "
                    + "about that subsection's ports at all.");
                continue;
            }

            Floor(offenders, $"ports transcribed from {citation}", group.Ports.Count, expected,
                "Every_specified_port_is_declared_or_deferred_with_an_owner is stated over them. A "
                + "trimmed transcription does not fail — it quietly stops asking about the port it "
                + "lost, and a floor over the flattened total is cleared by a name added elsewhere.");
        }

        specified.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            specified.Length,
            "a duplicated port name would keep the count above its floor while one interface of "
            + "23 §4 went untranscribed.");

        Floor(offenders, "ports declared under " + Domain.PortsNamespace, Domain.Ports.Count, DeclaredPortFloor,
            "No_port_signature_names_an_infrastructure_or_vendor_concept and both DependencyRuleTests "
            + "port rules are stated over this set.");

        Floor(offenders, "PortCatalogue.Deferred entries", PortCatalogue.Deferred.Length, DeferralFloor,
            "the stale, unanchored and well-formedness directions all quantify over the entries. "
            + "Empty, all three report success while the register carries no promise at all.");

        Floor(offenders, "banned infrastructure and vendor terms",
            PortCatalogue.InfrastructureVocabulary.Length, VocabularyFloor,
            "No_port_signature_names_an_infrastructure_or_vendor_concept is stated over this list. "
            + "Removing a term is the rule going silent on exactly that concept, still green.");

        // 🔒 The IDENTITY floor under the same list. The count floor above cannot see the object-
        // store terms leave together, and they are the ones M5-05 and M18-06a inherit.
        offenders.AddRange(
            from term in PortCatalogue.ObjectStoreVocabulary
            where !PortCatalogue.InfrastructureVocabulary.Contains(term, StringComparer.Ordinal)
            select $"'{term}' is gone from PortCatalogue.InfrastructureVocabulary. "
                   + "No_port_signature_names_an_infrastructure_or_vendor_concept exists a milestone "
                   + "before M5-05 declares IBattleLogStore specifically to keep the object store's "
                   + "vocabulary out of that port, and the AzureBlob sibling at M18-06a shares it. "
                   + "Every one of these terms could be dropped together with the count floor still "
                   + "clearing, which is why they are named one by one.");

        // 🔒 The member-level register's floor, and it is an IDENTITY floor rather than a count.
        // A count over SpecifiedMembers is cleared by any row at all, and the row that has to
        // survive is the one whose port is declared today — delete it and
        // Every_specified_member_of_a_declared_port_is_declared_or_omitted_with_an_owner quantifies
        // over nothing while still reporting success, which is the exact shape S3 names.
        foreach (var (citation, port, members) in RequiredMemberTranscriptions)
        {
            var transcription = PortCatalogue.SpecifiedMembers.SingleOrDefault(
                m => m.Port.Equals(port, StringComparison.Ordinal));

            if (transcription is null)
            {
                offenders.Add(
                    $"no PortCatalogue.SpecifiedMembers row transcribes {citation}'s '{port}'. That port "
                    + "is declared, so its member list is the only thing standing between a narrower "
                    + "declaration and nobody noticing.");
                continue;
            }

            offenders.AddRange(
                from member in members
                where !transcription.Members.Contains(member, StringComparer.Ordinal)
                select $"'{port}.{member}' is gone from the {citation} transcription. The member "
                       + "directions are stated over what that list holds, so a trimmed list does not "
                       + "fail — it stops asking about the member it lost.");
        }

        // 🔒 The engine adapter's own IDENTITY floor. No_type_in_the_engine_adapter_implements_a_port
        // is stated over whatever Cecil finds in that module, and a module the scan stopped reading
        // — renamed, unreferenced, emptied — reports success over nothing, which reads exactly like
        // "the engine implements no port". A count would be cleared by whatever replaced the class
        // that left; these four are the whole project.
        var engine = EngineAdapterTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        offenders.AddRange(
            from capability in PortCatalogue.EngineCapabilities
            where !engine.Contains(capability)
            select $"'{capability}' is gone from {PortCatalogue.EngineAdapterAssembly}. "
                   + "No_type_in_the_engine_adapter_implements_a_port quantifies over that module's "
                   + "types, so a subject it stopped seeing is a subject the rule stopped "
                   + "constraining — silently, and while still reporting success. If the capability "
                   + "was genuinely deleted, delete the name here in the same commit.");

        PortCatalogue.SpecifiedPorts.Length.ShouldBe(
            3,
            "23 §4 has three subsections — client, server and shared — and each is a separate "
            + "transcription with its own port namespace. A group that vanished would take its whole "
            + "subsection out of the undeclared direction with the count above still satisfied.");

        ArchRule.Empty(offenders, "The port catalogue's subject sets are the ones these rules were written against (23 §6, S3).");
    }

    /// <summary>
    /// `23` §6 — the teeth of all four register directions, driven against crafted entries so each
    /// is shown to bite without a violation ever being committed.
    /// </summary>
    /// <remarks>
    /// Both halves of each direction: loud on the bad entry, silent on the good one. A check that
    /// flagged everything would also "prove" it has teeth.
    /// </remarks>
    [Fact]
    public void The_register_directions_fire_on_a_deliberately_bad_entry_and_are_silent_on_a_good_one()
    {
        var group = new PortCatalogue.SpecifiedPortGroup(
            "23 §4.2", PortCatalogue.ServerPortsNamespace, new[] { "IUnitOfWork" });

        var good = new PortCatalogue.PortDeferral(
            "IUnitOfWork", "M5-04", "a reason long enough to be worth falsifying at the next kickoff.");

        PortCatalogue.Undeclared(new[] { group }, new[] { good }).ShouldBeEmpty();
        PortCatalogue.Unanchored(new[] { group }, new[] { good }).ShouldBeEmpty();
        PortCatalogue.Expired(new[] { good }).ShouldBeEmpty();
        PortCatalogue.Malformed(new[] { good }).ShouldBeEmpty();

        // Undeclared: a specified port nobody claimed.
        PortCatalogue.Undeclared(new[] { group }, Array.Empty<PortCatalogue.PortDeferral>())
            .ShouldHaveSingleItem()
            .ShouldContain("PortCatalogue.Deferred does not carry it", Case.Sensitive);

        // Stale: an entry deferring a port that is declared today. IClockPort is in the build, so
        // this is exactly the state the rule exists to catch — caught without committing it.
        PortCatalogue.Expired(new[] { good with { Port = Domain.ClockPortType } })
            .ShouldHaveSingleItem()
            .ShouldContain("already", Case.Sensitive);

        // Unanchored: an entry no transcription enumerates.
        PortCatalogue.Unanchored(new[] { group }, new[] { good with { Port = "IPortNoDocumentAsksFor" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no PortCatalogue.SpecifiedPorts transcription declares it", Case.Sensitive);

        // Malformed, one crafted entry per branch: a branch no entry reaches could return
        // "well formed" for everything and nothing here would notice.
        PortCatalogue.Malformed(new[] { good with { Port = "  " } })
            .ShouldContain(o => o.Contains("names no port", StringComparison.Ordinal));

        PortCatalogue.Malformed(new[] { good with { Owner = "later" } })
            .ShouldHaveSingleItem()
            .ShouldContain("not a milestone task id", Case.Sensitive);

        PortCatalogue.Malformed(new[] { good with { Why = "later" } })
            .ShouldHaveSingleItem()
            .ShouldContain("no written reason worth falsifying", Case.Sensitive);
    }

    /// <summary>
    /// `23` §5 A4 — the teeth of the vocabulary rule: it fires on a term that really does appear in
    /// a port's surface, and is silent on one that does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven with terms real ports genuinely contain, rather than with crafted types — the scan
    /// reads real ports either way, so a synthetic subject would prove the scan works on synthetic
    /// subjects. The silent half matters as much: a matcher that flagged every name would make the
    /// live rule a wall of false failures, which is how a rule gets suppressed.
    /// </para>
    /// <para>
    /// 🔒 Each arm is driven <em>separately and pinned to the place it fired</em>. The banned terms
    /// this list actually carries — <c>Bucket</c>, <c>Presign</c> — will arrive as a parameter name
    /// or as a type buried in a generic return, never as the port's own type name, so "the scan
    /// found something somewhere" is not the claim worth making. Four arms, four probes: the type
    /// name, a member, a parameter name, and a nested generic argument.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_vocabulary_scan_fires_on_a_term_a_port_really_names()
    {
        PortCatalogue.InfrastructureConcepts(Domain.Ports, new[] { "Clock" })
            .ShouldContain(
                o => o.Contains("the port type name", StringComparison.Ordinal),
                "IClockPort is in the build and contains 'Clock'. If no offender is attributed to "
                + "the port type name, the scan is not reading type names and the live rule is "
                + "silent on a port called IS3Client.");

        PortCatalogue.InfrastructureConcepts(Domain.Ports, new[] { "utcnow" })
            .ShouldContain(
                o => o.Contains("property 'UtcNow'", StringComparison.Ordinal),
                "the match is ordinal but case-INSENSITIVE, and IClockPort.UtcNow is a real "
                + "property. A case-sensitive matcher would miss a parameter named 'bucket'.");

        // 🔒 The parameter-name arm. 'adPlacementId' appears in IRewardedAdPort ONLY as a parameter
        // name — no member, no type is called that — so nothing else can produce this offender.
        // This is the arm that catches PutAsync(string bucket, string key, ...), whose every type
        // is BCL and which DependencyRuleTests' assembly check therefore cannot see.
        PortCatalogue.InfrastructureConcepts(Domain.Ports, new[] { "adPlacementId" })
            .ShouldContain(
                o => o.Contains("parameter 'adPlacementId'", StringComparison.Ordinal),
                "'adPlacementId' is a parameter name on IRewardedAdPort and appears nowhere else in "
                + "any port's surface. If this does not fire, parameter names are unscanned and a "
                + "banned concept crosses the boundary in the one place BCL types hide it.");

        // 🔒 The nested-generic arm. 'AdOutcome' is reachable only through Task<AdOutcome>: it is
        // never a member name and never a parameter name, so an offender naming it proves the scan
        // descends into generic arguments rather than stopping at the outer Task`1.
        PortCatalogue.InfrastructureConcepts(Domain.Ports, new[] { "AdOutcome" })
            .ShouldContain(
                o => o.Contains("a type in the signature of 'ShowAsync'", StringComparison.Ordinal),
                "AdOutcome appears in IRewardedAdPort only as the generic argument of "
                + "Task<AdOutcome>. A scan that yields the outer type and stops would miss a banned "
                + "term in exactly the position an async port signature puts its payload.");

        PortCatalogue.InfrastructureConcepts(Domain.Ports, new[] { "ZzzNoPortNamesThis" })
            .ShouldBeEmpty(
                "a scan that flags everything proves nothing about the terms that are actually banned.");
    }

    /// <summary>
    /// `23` §5 A4 — the field arm of the same scan: a constant on a port is part of its surface.
    /// </summary>
    /// <remarks>
    /// C# lets an interface declare a <c>const</c>, and a constant produces no method, no property
    /// and no signature type — so it is the one member kind every other arm of the scan is blind to,
    /// and <c>const string PresignedUrlPrefix</c> would put the vendor's shape in a port's public
    /// surface with the live rule still green. No port declares a field today, so the arm is driven
    /// against a real type from the same assembly that does: <c>AdResultKind</c> is an enum, and an
    /// enum's members ARE fields.
    /// </remarks>
    [Fact]
    public void The_vocabulary_scan_reads_constants_declared_on_a_type()
    {
        var enumWithFields = Domain.ApplicationTypes.Single(
            t => t.Name.Equals("AdResultKind", StringComparison.Ordinal));

        PortCatalogue.InfrastructureConcepts(new[] { enumWithFields }, new[] { "NoFill" })
            .ShouldContain(
                o => o.Contains("field 'NoFill'", StringComparison.Ordinal),
                "AdResultKind.NoFill is a field and nothing else. If this does not fire, the scan "
                + "never reads fields and a constant is a hole straight through the rule.");

        PortCatalogue.InfrastructureConcepts(new[] { enumWithFields }, new[] { "ZzzNoTypeNamesThis" })
            .ShouldBeEmpty(
                "the same subject, a term it does not contain — otherwise the arm above would be "
                + "satisfied by a scan that reports every field it sees.");
    }

    /// <summary>
    /// `23` §5 A4 — the base-interface arm: a port inherits its base's shape, banned terms included.
    /// </summary>
    /// <remarks>
    /// <c>IBattleLogStore : IS3ObjectStore</c> names no banned term of its own, and the base is only
    /// scanned in its own right if it happens to live under <c>Ports/</c> — which a leaked one will
    /// not. No port declares a base interface today, so the arm is driven against a real type from
    /// the same assembly that does: every C# record implements <c>IEquatable&lt;TSelf&gt;</c>, and
    /// <c>AdOutcome</c> is a <c>readonly record struct</c>.
    /// </remarks>
    [Fact]
    public void The_vocabulary_scan_reads_the_interfaces_a_type_inherits()
    {
        var recordWithABase = Domain.ApplicationTypes.Single(
            t => t.Name.Equals("AdOutcome", StringComparison.Ordinal));

        PortCatalogue.InfrastructureConcepts(new[] { recordWithABase }, new[] { "IEquatable" })
            .ShouldContain(
                o => o.Contains("the base interface 'IEquatable", StringComparison.Ordinal),
                "AdOutcome's only inherited interface is IEquatable<AdOutcome>, and it is named "
                + "nowhere else in the type's surface. If this does not fire, a port can inherit an "
                + "IS3ObjectStore wholesale with the live rule green.");

        PortCatalogue.InfrastructureConcepts(new[] { recordWithABase }, new[] { "ZzzNoBaseNamesThis" })
            .ShouldBeEmpty(
                "the same subject, a term it does not inherit — otherwise the arm above would be "
                + "satisfied by a scan that reports every interface it sees.");
    }

    /// <summary>
    /// `23` §5 A4 — the generic-parameter arms: a type parameter's own name is part of the surface.
    /// </summary>
    /// <remarks>
    /// Not hypothetical. `23` §4.1 writes <c>ILocalCachePort</c> as <c>Task&lt;T?&gt;
    /// ReadAsync&lt;T&gt;(...)</c> and §4.2 writes <c>IRemoteConfigPort.Get&lt;T&gt;</c>, so generic
    /// members on ports are coming; a <c>GetAsync&lt;TBucket&gt;</c> puts the vendor's word in the
    /// signature where Cecil's <c>Parameters</c> — the <em>value</em> parameters — cannot see it. No
    /// port is generic today, so both arms are driven against real generic types elsewhere in the
    /// build, each pinned to the place it fired.
    /// </remarks>
    [Fact]
    public void The_vocabulary_scan_reads_generic_parameter_names()
    {
        var genericType = Domain.CoreTypes.Single(t => t.Name.Equals("Result`1", StringComparison.Ordinal));

        PortCatalogue.InfrastructureConcepts(new[] { genericType }, new[] { "T" })
            .ShouldContain(
                o => o.Contains("generic parameter 'T' of the port type", StringComparison.Ordinal),
                "Result<T>'s type parameter is named T, and only the type-level arm can attribute an "
                + "offender to 'the port type'. A broad probe term is fine here because the claim is "
                + "about WHERE the scan looked, not about what it matched.");

        var typeWithAGenericMethod = Domain.CoreTypes.Single(
            t => t.Name.Equals("CommandDispatch", StringComparison.Ordinal));

        PortCatalogue.InfrastructureConcepts(new[] { typeWithAGenericMethod }, new[] { "TCommand" })
            .ShouldContain(
                o => o.Contains("generic parameter 'TCommand' of 'Handled'", StringComparison.Ordinal),
                "CommandDispatch.Handled<TCommand> is a real generic method. Only the method-level "
                + "arm attributes an offender to a generic parameter OF a named member.");

        PortCatalogue.InfrastructureConcepts(
                new[] { genericType, typeWithAGenericMethod }, new[] { "ZzzNoParameterNamesThis" })
            .ShouldBeEmpty(
                "the same two subjects, a term neither declares — otherwise both arms above would be "
                + "satisfied by a scan that reports every generic parameter it sees.");
    }

    // ───────────────────────────────────────────────────────────────────── the engine exception
    //
    // 🔒 M7-01c. `23` §7.2 used to register GodotPlatformInfoAdapter, GodotAudioAdapter and
    // GodotHapticsAdapter against ports. It cannot: a class in the engine adapter cannot carry a
    // contract fixture, because a GodotSharp call from the unit tier faults the test host process
    // instead of throwing. The document was amended; these three rules are what stop the amendment
    // from being a sentence somebody remembers.

    /// <summary>
    /// 🔒 `23` §7.2a / §5 A8 — no class in an assembly that can reach the engine API implements a
    /// port. The ruling that amended §7.2, stated where a future change to it has to go past.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one rule in this file that is <b>about a limitation rather than about the register</b>.
    /// `23` §5 A10 makes Godot an adapter and `23` §3 lists this project among the adapters, so a
    /// reader is entitled to expect it to implement something — the whole point of an adapter. It
    /// does not, and that is a stated exception with a measured reason, not an oversight.
    /// </para>
    /// <para>
    /// ⚠️ It exists <em>in addition</em> to <c>ContractSuiteCoverageTests</c>' fixture demand
    /// because of how that demand fails here. X-06 would notice an engine port implementation only
    /// by asking for the fixture, and the fixture is the thing that kills the run: the failure a
    /// developer would actually see is "Der Testhostprozess ist abgestürzt", with no rule name and
    /// no offender. This one fails in the architecture job, in three seconds, naming the type and
    /// the port.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_type_in_the_engine_adapter_implements_a_port()
    {
        ArchRule.Empty(
            PortCatalogue.EnginePortImplementations(EngineTypes(), Domain.Ports),
            "No class that can reach the engine API implements a port (23 §7.2a, 23 §5 A8).");
    }

    /// <summary>
    /// 🔒 `23` §5 A8 — the premise under the rule above: the contract suites still reference the
    /// engine adapter, which is what makes an engine port impossible rather than merely unwise.
    /// </summary>
    /// <remarks>
    /// Steering <b>S4</b>'s hard half. Every other statement of this ruling — the amended `23` §7.2,
    /// the <c>IHapticsPort</c> and <c>IAudioPort</c> deferrals, the rule above — rests on one
    /// <c>ProjectReference</c>. Delete it and all four are describing a constraint that no longer
    /// exists, and not one of them goes red. This is the half of the reason that a rule can see.
    /// </remarks>
    [Fact]
    public void The_engine_adapter_is_still_on_the_contract_suites_reference_list()
    {
        ArchRule.Empty(
            PortCatalogue.EnginePremiseBroken(ContractSuiteProjectReferences()),
            "The contract suites still reference the engine adapter (23 §5 A8, steering S4).");
    }

    /// <summary>
    /// `23` §7.2 — the teeth of the engine rule, driven against real types so each arm is shown to
    /// bite without the forbidden arrangement ever being committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three arms and two controls, because one probe would only prove the rule is not vacuous
    /// (steering <b>S1</b>). The <b>subject</b> arm asks whether the scan reads the engine module's
    /// types at all; the <b>port</b> arm asks whether it quantifies over every port or over one; the
    /// <b>inheritance</b> arm is on <see cref="Il.ImplementsInterface"/> itself, since a check
    /// written as <c>type.Interfaces.Any(…)</c> passes every probe above and still lets
    /// <c>GodotHaptics : SomeBase</c> through.
    /// </para>
    /// <para>
    /// <c>HostPlatformInfo</c> is the probe rather than a synthetic class because it is a real
    /// implementation of a real port — the very arrangement the engine project is forbidden to
    /// grow, standing in the sibling project the ruling says to put it in.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_engine_port_rule_fires_on_a_type_that_implements_one()
    {
        var engine = EngineTypes().ToArray();
        var portImplementor = HostTypes().Single(
            t => t.Name.Equals("HostPlatformInfo", StringComparison.Ordinal));

        // The live claim, stated as a probe so a scan that lost the module is not mistaken for one.
        PortCatalogue.EnginePortImplementations(engine, Domain.Ports).ShouldBeEmpty(
            "no class in the engine adapter implements a port today, which is the arrangement this "
            + "rule exists to permit — and 23 §7.2 was amended to say so.");

        // The subject arm: one port-implementing class among the engine's own types.
        PortCatalogue.EnginePortImplementations(engine.Append(portImplementor), Domain.Ports)
            .ShouldHaveSingleItem()
            .ShouldContain("implements the port 'IPlatformInfoPort'", Case.Sensitive);

        // 🔒 The port arm: the scan reads EVERY port, not the first one it was written against.
        // Driven with the same subject and a port set that deliberately excludes its port.
        PortCatalogue.EnginePortImplementations(
                new[] { portImplementor },
                Domain.Ports.Where(p => !p.Name.Equals("IPlatformInfoPort", StringComparison.Ordinal)))
            .ShouldBeEmpty(
                "HostPlatformInfo implements IPlatformInfoPort and nothing else. A rule that "
                + "reported it here would be flagging types for ports they do not implement.");

        // 🔒 The client is in the subject set too, and it is the route no other rule watches: its
        // Composition/ folder is deliberately EXCLUDED from the scene and presenter boundary rules,
        // and the contract suites never scan it.
        //
        // 🔴 This floor is stated over NAMED TYPES, and the first version was stated over
        // EngineReachingAssemblies itself — which the scan is built from, so it agreed by
        // construction and stayed green when the client was deleted from the list. A floor derived
        // from its own subject is not a floor. One name per assembly, and neither is derivable from
        // the other side of the comparison.
        var scanned = engine.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        scanned.ShouldContain(
            "GodotUserPaths",
            "the engine adapter is not in the scan at all, so the rule this floor stands under is "
            + "reporting success over an assembly it never read.");

        scanned.ShouldContain(
            "GodotClientComposition",
            "SlayIdleRepeat.Client is not in the scan. Its Composition/ folder is exempt from the "
            + "scene and presenter boundary rules and invisible to the contract suites, so a "
            + "capability there implementing a port would be seen by nothing — while "
            + "Every_port_has_at_least_two_implementations counted it as one of the port's two.");

        // The negative control on the subject axis: a real engine class that implements nothing.
        PortCatalogue.EnginePortImplementations(
                engine.Where(t => t.Name.Equals("GodotUserPaths", StringComparison.Ordinal)), Domain.Ports)
            .ShouldBeEmpty(
                "GodotUserPaths is a capability the composition root names directly. A rule that "
                + "flagged it would be forbidding the engine adapter from existing.");

        // 🔒 The inheritance arm, on the shared predicate the rule is built from. CurrencyChanged
        // declares IEquatable<CurrencyChanged> and INHERITS IEquatable<DomainEvent> from its base
        // record — so a direct-only check answers false here and true on the line above it.
        var inheritedInterface = $"System.IEquatable`1<{Domain.EventsNamespace}.{Domain.DomainEventType}>";
        var derived = Domain.CoreTypes.Single(
            t => t.Name.Equals(Domain.CurrencyChangedEvent, StringComparison.Ordinal));

        Il.ImplementsInterface(derived, inheritedInterface).ShouldBeTrue(
            $"'{Domain.CurrencyChangedEvent}' names '{inheritedInterface}' nowhere in its own "
            + "metadata — it has it because its base record does. If this is false the walk stops at "
            + "the type itself, and a Godot class implementing a port through a base class is "
            + "invisible to every rule above.");

        Il.ImplementsInterface(derived, "System.IEquatable`1<System.Uri>").ShouldBeFalse(
            "the same subject and an interface neither it nor its base declares — otherwise the arm "
            + "above would be satisfied by a walk that answers true for everything.");
    }

    /// <summary>
    /// `23` §5 A8 — the teeth of the premise rule: it fires on a reference list the engine adapter
    /// has left, and is silent on the real one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 The rule this project could most easily have shipped without teeth, and the one whose
    /// silence would cost the most: it is green today and its live input can only be seen passing,
    /// so <c>EnginePremiseBroken</c> rewritten as <c>=&gt; Array.Empty&lt;string&gt;()</c> would leave the
    /// whole suite green while the premise under three other statements went unwatched.
    /// </para>
    /// <para>
    /// Three failing shapes and two controls. <b>Reference dropped</b> is the realistic edit — the
    /// sibling host adapter stays and the engine one goes. <b>Nothing read</b> is the reader
    /// returning an empty list, which has to be loud rather than treated as "no evidence".
    /// <b>Near miss</b> is the control that proves the match is whole-name and not a prefix: a
    /// project called <c>…Platform.Godot.Something</c> is not this one.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_engine_premise_rule_fires_when_the_contract_suites_stop_referencing_the_adapter()
    {
        var live = ContractSuiteProjectReferences();

        PortCatalogue.EnginePremiseBroken(live).ShouldBeEmpty(
            "Contract.Tests project-references the engine adapter today, which is the arrangement "
            + "the deferrals and 23 §7.2's amendment are written against.");

        // 🔒 An identity floor on the READER, not on the rule: it must return project NAMES. A
        // reader that returned paths would make every arm below pass for the wrong reason.
        live.ShouldContain(
            PortCatalogue.HostAdapterAssembly,
            "the reader returns bare project names. If this fails it is returning paths or Include "
            + "attributes, and the whole-name match below is comparing two different vocabularies.");

        // The realistic edit: the engine reference is dropped and its sibling stays.
        PortCatalogue.EnginePremiseBroken(live.Where(r => !r.Equals(PortCatalogue.EngineAdapterAssembly, StringComparison.Ordinal)))
            .ShouldHaveSingleItem()
            .ShouldContain("no longer project-references", Case.Sensitive);

        // The reader coming back empty must be loud, not silently "nothing to complain about".
        PortCatalogue.EnginePremiseBroken(Array.Empty<string>())
            .ShouldHaveSingleItem()
            .ShouldContain(PortCatalogue.EngineAdapterAssembly, Case.Sensitive);

        // The control on the matcher: a whole-name match, not a prefix or a substring.
        PortCatalogue.EnginePremiseBroken(new[] { PortCatalogue.EngineAdapterAssembly + ".Extra" })
            .ShouldHaveSingleItem()
            .ShouldContain("no longer project-references", Case.Sensitive);

        // …and the other side of the same control: the exact name alone satisfies it.
        PortCatalogue.EnginePremiseBroken(new[] { PortCatalogue.EngineAdapterAssembly }).ShouldBeEmpty(
            "the reference is what the rule asks for, and nothing else about the list matters — "
            + "otherwise this would be a second, unstated rule about what Contract.Tests may hold.");
    }

    /// <summary>
    /// 🔒 `23` §6, steering <b>S4</b> — every owning task this register names is a tracker row that
    /// is still <b>open</b>. Not merely one that exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// S4's M4 amendment, and it found two live offenders on the commit that introduced it:
    /// <c>IHapticsPort</c> and <c>IAudioPort</c> were both owned by <c>M7-01</c>, a task that had
    /// merged two tasks earlier — and that M7-01b's own row records as unable to discharge them even
    /// while it was open. <see cref="Every_port_deferral_is_well_formed"/> checks the id's shape and
    /// was green on both; a shape is not an expiry.
    /// </para>
    /// <para>
    /// Both registers, because the member omissions are the same kind of promise made the same way,
    /// and a mechanism that governed one of the two would leave the other exactly as it was.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_port_catalogue_owner_is_a_task_the_tracker_still_has_open()
    {
        var entries = PortCatalogue.Deferred
            .Select(d => (Subject: d.Port, d.Owner))
            .Concat(PortCatalogue.OmittedMembers.Select(o => (Subject: o.Port + "." + o.Member, o.Owner)));

        ArchRule.Empty(
            PortCatalogue.OwnersNoLongerOpen(entries, PortCatalogue.TrackerStatuses(Tracker())),
            "Every PortCatalogue owner is a tracker task that is still open (23 §6, steering S4).");
    }

    /// <summary>
    /// `23` §6 — the teeth of the owner rule, driven against crafted entries so every arm is shown
    /// to bite without <c>IMPLEMENTATION_TRACKER.md</c> being edited to prove it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four arms — a shipped owner, an owner no row declares, a BLOCKED owner that must stay
    /// silent, and the ordinary open case — plus the parser's own floors. The parser is the part
    /// that can go quiet: a regex that stopped matching would report every owner as undeclared,
    /// which is loud and therefore safe, but one anchored on the wrong glyph reads real rows
    /// backwards while staying green.
    /// </para>
    /// <para>
    /// 🔒 Its floors are by <b>identity</b>, not by count (steering S3): the three rows pinned below
    /// are the ones that discriminate this anchor from the two obvious wrong ones, and a count over
    /// 206 rows is cleared by any 150 of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_owner_status_rule_fires_on_a_shipped_or_missing_owner_and_is_silent_on_an_open_one()
    {
        var tracker = Tracker();
        var statuses = PortCatalogue.TrackerStatuses(tracker);

        // 🔒 The anchor, pinned by IDENTITY on the three rows that discriminate it. Each of these
        // would be read wrongly by a plausible simplification of the regex, and each is a real row.
        statuses["M7-10"].ShouldBe(
            new[] { "🔍" },
            "M7-10 is in review, and its status prose goes on to mention a ⛔ CI gap. Anchor this on "
            + "the LAST glyph on the line and the row reads as blocked.");

        // ⚠️ M7-10 was pinned as "⏳" here and this line went red the moment that row progressed to
        // review — a fixture that named a transient status, which is the S4 shape arriving through
        // the test rather than through the register. The glyph was never the discriminating property:
        // what makes this row worth pinning is that a ⛔ appears LATER in the same cell, so a
        // last-glyph anchor reads it as blocked. That survived the status change, which is why the
        // pin above is updated rather than re-pointed.
        //
        // ⚠️ Not re-pointed at some still-queued row either: as of this commit no row is both queued
        // and carries a later ⛔ (checked, not assumed), and a row without the later glyph would keep
        // this assertion green while testing nothing.

        statuses["M2-16a"].ShouldBe(
            new[] { "🔍" },
            "M2-16a is in review, and its status prose goes on to mention an ✅ result. Same "
            + "simplification, same silent misreading, opposite direction.");

        statuses["M18-07"].ShouldBe(
            new[] { "⬜" },
            "M18-07 has not started, and the ✅ in its DESCRIPTION cell — '(O18 ✅)' — sits BEFORE "
            + "the status cell. Drop the '|' from the anchor and this row reads as done, which is "
            + "the one arm the two rows above cannot cover.");

        // 🔒 The tracker's own legend, read OUT of the document and checked against the parser —
        // this direction and not the other. Asserting "the legend still contains our five" is
        // satisfied by a legend that has grown a SIXTH, which is exactly the change that makes
        // TrackerTaskRow drop every row using it while staying green.
        var legend = tracker
            .Split('\n')
            .First(line => line.Contains("**Statuses:**", StringComparison.Ordinal));

        var documented = PortCatalogue.LegendGlyphs(legend);

        documented.ShouldBe(
            PortCatalogue.LegendStatuses,
            "PortCatalogue.LegendStatuses transcribes that line, and the transcription is what tells "
            + "this rule which characters on it are statuses at all. If they have diverged, fix the "
            + "transcription before reading anything below it.");

        foreach (var glyph in documented)
        {
            PortCatalogue.KnownStatuses.ShouldContain(
                glyph,
                $"the tracker documents the status '{glyph}' and TrackerTaskRow does not accept it. "
                + "Every row carrying it vanishes from the lookup — silently — and its owners are "
                + "then reported as owners nobody declared, which sends the reader to the register "
                + "instead of to the parser.");
        }

        // The extractor's own control: a legend carrying a status the parser does not know is
        // exactly the arrangement the loop above exists to catch, driven without editing the doc.
        PortCatalogue.LegendGlyphs("- **Statuses:** `⬜ todo` · `🆕 brand new`")
            .ShouldBe(new[] { "⬜", "🆕" });

        var open = new[] { (Subject: "IUnitOfWork", Owner: "M5-04") };
        var shipped = new[] { (Subject: "IUnitOfWork", Owner: "M7-01") };
        var absent = new[] { (Subject: "IUnitOfWork", Owner: "M9-99") };

        PortCatalogue.OwnersNoLongerOpen(open, statuses).ShouldBeEmpty(
            "M5-04 is a real tracker row and has not started, which is the arrangement every "
            + "deferral here is supposed to be in.");

        PortCatalogue.OwnersNoLongerOpen(shipped, statuses)
            .ShouldHaveSingleItem()
            .ShouldContain("has already shipped", Case.Sensitive);

        PortCatalogue.OwnersNoLongerOpen(absent, statuses)
            .ShouldHaveSingleItem()
            .ShouldContain("no task row this parser could read declares it", Case.Sensitive);

        // ⚠️ The status arm's own control: ⛔ is BLOCKED, not finished, and IAudioPort's owner is in
        // exactly that state. A rule that treated "not ⬜" as shipped would fire on it.
        PortCatalogue.OwnersNoLongerOpen(new[] { (Subject: "IAudioPort", Owner: "M8-07") }, statuses)
            .ShouldBeEmpty("M8-07 is blocked on an audio-tool licence — ahead of us, not behind us.");
    }

    /// <summary>Every type Cecil finds in the engine adapter.</summary>
    private static IEnumerable<TypeDefinition> EngineAdapterTypes() =>
        Il.AllTypes(ProductionAssemblies.Module(PortCatalogue.EngineAdapterAssembly));

    /// <summary>Every type in every assembly that can reach the engine API.</summary>
    private static IEnumerable<TypeDefinition> EngineTypes() =>
        PortCatalogue.EngineReachingAssemblies.SelectMany(
            name => Il.AllTypes(ProductionAssemblies.Module(name)));

    /// <summary>Every type Cecil finds in the plain-C# platform adapter beside it.</summary>
    private static IEnumerable<TypeDefinition> HostTypes() =>
        Il.AllTypes(ProductionAssemblies.Module(PortCatalogue.HostAdapterAssembly));

    /// <summary>The tracker's raw text.</summary>
    private static string Tracker() =>
        File.ReadAllText(Path.Combine(RepoLayout.RepoRoot, "IMPLEMENTATION_TRACKER.md"));

    /// <summary>
    /// The project names <c>SlayIdleRepeat.Contract.Tests</c> references.
    /// </summary>
    /// <remarks>
    /// 🔒 Throws on a missing project file rather than returning nothing, for the reason
    /// <c>RepoLayout.SourceFiles</c> does: an empty list would make
    /// <see cref="The_engine_adapter_is_still_on_the_contract_suites_reference_list"/> fail for the
    /// wrong reason, and a renamed suite would look like a deleted reference.
    /// </remarks>
    private static IReadOnlyList<string> ContractSuiteProjectReferences()
    {
        var projectFile = Path.Combine(
            RepoLayout.RepoRoot, "tests", PortCatalogue.ContractSuitesProject,
            PortCatalogue.ContractSuitesProject + ".csproj");

        return File.Exists(projectFile)
            ? RepoLayout.ProjectReferences(projectFile)
            : throw new FileNotFoundException(
                $"'{RepoLayout.Relative(projectFile)}' does not exist. The engine deferrals and " +
                "23 §7.2's amendment are stated over what that project references; if the suite " +
                "moved, point this rule at the new location rather than letting it read nothing.",
                projectFile);
    }

    private static void Floor(ICollection<string> offenders, string what, int actual, int floor, string consequence)
    {
        if (actual < floor)
        {
            offenders.Add($"{what}: {actual}, floor {floor}. {consequence}");
        }
    }
}
