using Mono.Cecil;
using Mono.Cecil.Cil;
using SlayIdleRepeat.Architecture.Tests.Infrastructure;
using Xunit;

namespace SlayIdleRepeat.Architecture.Tests;

/// <summary>
/// 🔒 `23` §6 — the two halves of `18` §8 step 1's deferral are one declaration. Every
/// <c>PendingSubject</c> the source catalogue advertises is tracked by a matching entry in
/// <see cref="SubjectSetFloorTests"/>, the repo's one expiring register (steering S4).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this rule had to exist.</b> M2-02 declared `18` §8 step 1's ten sources in
/// <c>Rules/Effects/EffectSourceCatalogue.cs</c>, each carrying a <c>PendingSubject</c> — the name of
/// the <c>Core</c> type whose <em>arrival</em> means that source can finally be wired. That name has
/// exactly one purpose: to be the key of a <c>SubjectSetFloorTests.Pending</c> entry. And the two
/// were joined by <b>prose alone</b>. Three drift paths, all silent:
/// </para>
/// <list type="bullet">
///   <item>Rename a <c>PendingSubject</c> in the catalogue and no register entry is keyed on it any
///   more — that source's expiry has quietly ceased to exist, while
///   <c>EffectSourceCatalogueTests.Every_pending_source_names_its_milestone_and_its_expiry_subject</c>
///   still passes, because it only checks the field is non-empty.</item>
///   <item>Delete a register entry — which <c>Every_rule_subject_is_present_or_declared_pending</c>
///   <em>requires</em> on arrival — and the catalogue row goes on advertising an expiry that tracks
///   nothing.</item>
///   <item>Add an eleventh source under `18` §10's extension route and it can ship with an untracked
///   <c>PendingSubject</c>.</item>
/// </list>
/// <para>
/// That is the exact failure mode <see cref="SubjectSetFloorTests"/>' own header describes — <em>"a
/// declaration that a subject is missing must expire the moment it arrives"</em> — one level up, in
/// the declaration itself.
/// </para>
/// <para>
/// 🔒 <b>An IL read plus a source read, and both halves are necessary.</b> The catalogue's names come
/// out of <c>Core</c>'s IL as the <c>ldstr</c> operands of <c>EffectSourceCatalogue</c>'s static
/// constructor — <c>Rules.Effects</c> is <c>internal</c> and this assembly has no
/// <c>InternalsVisibleTo</c> grant (`30` §11.3 gives it to <c>Core.Tests</c> alone), so the types
/// cannot be named directly. The register's names come out of this suite's own source, because
/// <c>SubjectSetFloorTests.Pending</c> is <c>private</c>. Reading one side reflectively and the other
/// textually is what lets the rule live in the assembly that owns the register.
/// </para>
/// <para>
/// 🔒 A <b>new file</b>, not an edit to <c>SubjectSetFloorTests</c> or to
/// <c>Infrastructure/Domain.cs</c>: M1-12 is in flight on the latter (steering S12).
/// </para>
/// </remarks>
public sealed class EffectSourceDeferralRuleTests
{
    /// <summary>The catalogue that declares `18` §8 step 1's ten sources.</summary>
    private const string CatalogueType = "SlayIdleRepeat.Core.Rules.Effects.EffectSourceCatalogue";

    /// <summary>The register file the catalogue's expiry subjects must appear in.</summary>
    private const string RegisterFile = "SubjectSetFloorTests.cs";

    /// <summary>
    /// 🔒 `23` §6 / `30` §11.4 — every `18` §8 step 1 source that advertises an expiry subject has a
    /// matching entry in the one register, so the deferral actually expires.
    /// </summary>
    [Fact]
    public void Every_declared_18_8_step_1_expiry_subject_is_tracked_in_the_one_register()
    {
        var register = RegisterSource();
        var subjects = DeclaredExpirySubjects();

        var offenders = subjects
            .Where(subject => !IsRegisterEntryKey(register, subject))
            .Select(subject =>
                $"EffectSourceCatalogue advertises '{subject}' as the expiry subject of a 18 §8 step 1 " +
                $"source, and no entry in {RegisterFile} is keyed on it. That source's deferral now " +
                "expires never: the milestone that lands the type will not be told to come back and " +
                "wire the source, and nothing goes red. Add the Pending entry, or — if the source has " +
                "been wired — clear the catalogue row's PendingSubject in the same commit.")
            .ToArray();

        ArchRule.Empty(
            offenders,
            "18 §8 step 1's declared sources and SubjectSetFloorTests are one declaration (23 §6).");
    }

    /// <summary>
    /// 🔒 `23` §6 — <b>the floor under the rule above</b> (steering S3). It quantifies over string
    /// literals recovered from IL; if the catalogue is renamed, stops being a static class, or has its
    /// rows built some other way, the subject set empties and the rule passes over nothing while all
    /// ten deferrals go untracked.
    /// </summary>
    /// <remarks>
    /// The floor was <b>ten</b> when this rule landed, and deliberately: `18` §8 step 1 names exactly
    /// ten sources, <c>EffectSourceCatalogueTests</c> pins that against the document itself, and every
    /// one of them was pending then. A source that is genuinely wired clears its own
    /// <c>PendingSubject</c>, which lowers this count on purpose — at which point lowering the floor
    /// in the same commit is the deliberate act steering S4 asks for.
    /// <para>
    /// 🔒 <b>It is now seven, and that is the first time this mechanism has been exercised.</b> M4-16
    /// wired <c>GEAR</c>, <c>AFFIXES</c> and <c>SET_BONUSES</c> — the hero build collects from all
    /// three off the equipped loadout — so those rows carry a null <c>PendingSubject</c> and their
    /// three <c>SubjectSetFloorTests.Pending</c> entries were deleted in the same change. Anything
    /// that reintroduced a pending subject for one of them without a register entry is still an
    /// offender above; what this number now says is that <b>seven</b> sources remain deferred.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_catalogue_still_declares_the_expiry_subjects_this_rule_reads()
    {
        var subjects = DeclaredExpirySubjects();

        if (subjects.Count < ExpirySubjectFloor)
        {
            throw new ArchitectureRuleViolationException(
                "The 18 §8 step 1 expiry subjects this rule quantifies over are the ones it was written against (23 §6).",
                new[]
                {
                    $"recovered {subjects.Count} expiry subject(s) from {CatalogueType}, floor is " +
                    $"{ExpirySubjectFloor}. Either a source has been wired — in which case lower the " +
                    "floor in the same commit and say which — or the catalogue no longer declares them " +
                    "in a form this rule can read, and " +
                    $"{nameof(Every_declared_18_8_step_1_expiry_subject_is_tracked_in_the_one_register)} " +
                    "is now passing over an empty set.",
                });
        }

        // 🔒 And they are the names, not any old strings: the catalogue also holds `18` §8 step 1's
        //    own phrases and its milestone markers, and a recovery that swept those up would make the
        //    rule above compare the register against the wrong list.
        // ⚠️ The milestone test is `M` followed by a DIGIT, not `M` — found by running it, which
        //    reported `MountDefinition` as a milestone marker. A type name may perfectly well start
        //    with an M, and three of the ten do.
        var suspicious = subjects
            .Where(s => s.Contains(' ', StringComparison.Ordinal) ||
                        (s.Length > 1 && s[0] == 'M' && char.IsAsciiDigit(s[1])))
            .Select(s => $"'{s}' looks like a document phrase or a milestone marker, not a type name")
            .ToArray();

        ArchRule.Empty(suspicious, "The recovered strings are the expiry subjects (23 §6).");
    }

    /// <summary>Seven sources still pending: the ten, less the three gear sources M4-16 wired.</summary>
    private const int ExpirySubjectFloor = 7;

    /// <summary>
    /// The register holds a <c>Pending</c> <b>entry keyed on</b> <paramref name="subject"/> — not
    /// merely the word somewhere in the file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This was a bare <c>register.Contains($"\"{subject}\"")</c>, and the second probe of this
    /// rule showed it could not fail.</b> <see cref="RegisterSource"/> returns the register's <b>whole
    /// source text</b>, comments and XML remarks included, so the quoted name only had to appear
    /// <em>somewhere</em>. The mutation: rename the live entry <c>new("GearItem", SubjectKind.CoreType,
    /// …)</c> to <c>"GearItemXX"</c> and leave one comment mentioning <c>"GearItem"</c>. `18` §8 step
    /// 1's <c>GEAR</c> deferral is then keyed on a type that will never arrive — it expires never,
    /// which is precisely the first drift path this file's own header enumerates — and the entire
    /// 79-test suite stayed <b>green</b>.
    /// </para>
    /// <para>
    /// It is not a hypothetical shape: this file, <c>SubjectSetFloorTests</c> and the catalogue all
    /// discuss the subjects by name in prose, and every failure message in the register quotes one.
    /// The rule now matches the <b>entry</b> — the <c>new("…", SubjectKind</c> opening that every one
    /// of the ten register rows is written as — so a name that survives only in a comment is an
    /// offender, as it always should have been.
    /// </para>
    /// <para>
    /// ⚠️ It is anchored on the register's construction shape rather than on a comment-stripped text,
    /// deliberately: stripping comments correctly means tokenising C# string literals, and a rule that
    /// half-parses is a rule with a new hole. Reformatting the register away from this shape fails
    /// loudly here, which is the right direction — the alternative is failing silently, which is what
    /// was happening.
    /// </para>
    /// </remarks>
    private static bool IsRegisterEntryKey(string register, string subject) =>
        register.Contains($"new(\"{subject}\", {SubjectKindPrefix}", StringComparison.Ordinal);

    /// <summary>The register's own entry type, as its rows spell it.</summary>
    private const string SubjectKindPrefix = "SubjectKind.";

    /// <summary>
    /// The <c>PendingSubject</c> literals the catalogue declares, recovered from the IL of its static
    /// constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each row is built as <c>new EffectSourceRow(kind, phrase, milestone, pendingSubject)</c>, so the
    /// four operands are pushed in order and the expiry subject is whatever was pushed <b>immediately
    /// before</b> the <c>newobj</c> — an <c>ldstr</c> for a pending source, an <c>ldnull</c> for a
    /// wired one. Reading it positionally rather than by name is what an IL scan can do; the floor
    /// above is what stops that positional read going quietly wrong.
    /// </para>
    /// <para>
    /// 🔴 <b>It <em>did</em> go quietly wrong, and the floor caught it — the first time a row ever
    /// cleared its subject.</b> The original recovery kept "the last <c>ldstr</c> seen since the
    /// previous <c>newobj</c>", which is the expiry subject only while every row has one. The three
    /// rows M4-16 wired push <c>ldnull, ldnull</c> after their phrase, so that reading recovered
    /// <c>"gear"</c>, <c>"affixes"</c> and <c>"set bonuses"</c> — the design document's own words —
    /// and would have demanded register entries keyed on them. The rule above would then have been
    /// comparing the register against three phrases while three real deferrals went untracked. What
    /// reported it was the <i>second</i> arm of the floor test, the one that refuses a recovered
    /// string containing a space; the count arm was satisfied, because there were still ten strings.
    /// Two probes, and only the second discriminated.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> DeclaredExpirySubjects()
    {
        var catalogue = Il.AllTypes(ProductionAssemblies.CoreModule)
            .FirstOrDefault(t => t.FullName.Equals(CatalogueType, StringComparison.Ordinal));

        if (catalogue is null)
        {
            return Array.Empty<string>();
        }

        var subjects = new List<string>();

        foreach (var method in Il.AllMethods(catalogue).Where(m => m.HasBody))
        {
            Instruction? previous = null;

            foreach (var instruction in Il.Instructions(method))
            {
                if (instruction.OpCode.Code == Code.Newobj &&
                    instruction.Operand is MethodReference constructor &&
                    constructor.DeclaringType.Name.Equals("EffectSourceRow", StringComparison.Ordinal) &&
                    previous?.OpCode.Code == Code.Ldstr &&
                    previous.Operand is string subject &&
                    !string.IsNullOrWhiteSpace(subject))
                {
                    subjects.Add(subject);
                }

                previous = instruction;
            }
        }

        return subjects;
    }

    /// <summary>The register's own source text.</summary>
    private static string RegisterSource()
    {
        // 🔒 RepoLayout.SourceFiles THROWS on a missing directory rather than returning nothing —
        //    "silently returning nothing turns 'this directory moved' into 'this rule holds over zero
        //    files, forever'" — which is exactly the guarantee this rule needs.
        var suite = Path.Combine(RepoLayout.RepoRoot, "tests", "SlayIdleRepeat.Architecture.Tests");

        var path = RepoLayout.SourceFiles(suite)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(RegisterFile, StringComparison.Ordinal));

        return path is null
            ? throw new ArchitectureRuleViolationException(
                "The one expiring register is where this rule expects it (23 §6).",
                new[]
                {
                    $"{RegisterFile} was not found under tests/SlayIdleRepeat.Architecture.Tests. It is " +
                    "the repo's ONE subject-set-floor and deferral mechanism; if it moved, this rule " +
                    "and every deferral keyed on it are reading nothing.",
                })
            : File.ReadAllText(path);
    }
}
