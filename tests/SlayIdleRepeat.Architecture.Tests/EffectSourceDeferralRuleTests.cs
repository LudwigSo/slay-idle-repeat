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
            .Where(subject => !register.Contains($"\"{subject}\"", StringComparison.Ordinal))
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
    /// The floor is <b>ten</b> rather than a lower number, and deliberately: `18` §8 step 1 names
    /// exactly ten sources, <c>EffectSourceCatalogueTests</c> pins that against the document itself,
    /// and every one of them is pending as this rule lands. A source that is genuinely wired clears
    /// its own <c>PendingSubject</c>, which lowers this count on purpose — at which point lowering
    /// the floor in the same commit is the deliberate act steering S4 asks for.
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

    /// <summary>Ten sources, all pending on the commit this rule landed.</summary>
    private const int ExpirySubjectFloor = 10;

    /// <summary>
    /// The <c>PendingSubject</c> literals the catalogue declares, recovered from the IL of its static
    /// constructor.
    /// </summary>
    /// <remarks>
    /// Each row is built as <c>new EffectSourceRow(kind, phrase, milestone, pendingSubject)</c>, so the
    /// four operands are pushed in order and the expiry subject is the <b>last</b> <c>ldstr</c> before
    /// each <c>newobj</c>. Reading it positionally rather than by name is what an IL scan can do; the
    /// floor above is what stops that positional read going quietly wrong.
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
            string? pending = null;

            foreach (var instruction in Il.Instructions(method))
            {
                if (instruction.OpCode.Code == Code.Ldstr)
                {
                    pending = instruction.Operand as string;
                    continue;
                }

                if (instruction.OpCode.Code == Code.Newobj &&
                    instruction.Operand is MethodReference constructor &&
                    constructor.DeclaringType.Name.Equals("EffectSourceRow", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(pending))
                {
                    subjects.Add(pending);
                    pending = null;
                }
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
