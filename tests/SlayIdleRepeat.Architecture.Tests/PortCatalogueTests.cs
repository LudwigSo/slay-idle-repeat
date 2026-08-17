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
    /// of them having gained a member the other two cannot see.
    /// </remarks>
    private const int DeclaredPortFloor = 6;

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

    private static void Floor(ICollection<string> offenders, string what, int actual, int floor, string consequence)
    {
        if (actual < floor)
        {
            offenders.Add($"{what}: {actual}, floor {floor}. {consequence}");
        }
    }
}
