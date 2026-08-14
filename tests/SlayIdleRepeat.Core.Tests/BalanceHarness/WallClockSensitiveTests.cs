using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Tests.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 The floor under <see cref="WallClockSensitive"/>: every class that must be serialised really is
/// in <b>one</b> collection, and the measurement it protects is in that same one.
/// </summary>
/// <remarks>
/// 🔴 xUnit treats an unrecognised <c>[Collection("…")]</c> argument as a <em>new</em> collection rather
/// than an error, so a typo, a rename, or a copy-pasted class that forgets the attribute all restore
/// the parallelism that made the wall-clock measurement flake — and they restore it <em>silently</em>,
/// as an intermittent red on an unrelated branch months later.
/// <para>
/// ⚠️ The count is asserted as well as the membership: a rule walking an empty type list would pass
/// forever, and <c>ShouldAllBe</c> is true of an empty collection.
/// </para>
/// </remarks>
[Collection(WallClockSensitive.Name)]
public sealed class WallClockSensitiveTests
{
    /// <summary>
    /// The class holding the wall-clock assertion. Named as a <c>Type</c> rather than as a string so
    /// that renaming it is a compile error here instead of a rule that quietly stops covering it.
    /// </summary>
    private static readonly Type Measurement = typeof(CombatSimulatorTests);

    /// <summary>Every harness case class, plus the class holding the measurement they contend with.</summary>
    private static IReadOnlyList<Type> Subjects
    {
        get
        {
            var harness = typeof(WallClockSensitive).Assembly
                .GetTypes()
                .Where(t => t.IsClass
                            && t.Name.EndsWith("Tests", StringComparison.Ordinal)
                            && string.Equals(
                                t.Namespace,
                                typeof(WallClockSensitive).Namespace,
                                StringComparison.Ordinal));

            return harness.Append(Measurement)
                          .Distinct()
                          .OrderBy(t => t.FullName, StringComparer.Ordinal)
                          .ToArray();
        }
    }

    /// <summary>
    /// 🔒 The whole guard: one collection, every subject in it, and enough subjects that the sweep
    /// cannot have walked past everything.
    /// </summary>
    [Fact]
    public void Every_bulk_fight_class_shares_one_collection_with_the_wall_clock_measurement()
    {
        var subjects = Subjects;

        // S3's floor. 11 harness case classes + this one + CombatSimulatorTests at the time of
        // writing; asserted as a minimum so adding a case class does not fail the rule, while
        // deleting the namespace or mis-naming the suffix does.
        subjects.Count.ShouldBeGreaterThanOrEqualTo(
            13,
            "the subject set is discovered by reflection and an empty one would pass every assertion below");

        subjects.ShouldContain(
            Measurement,
            "the measurement these classes contend with has to be in the collection too — serialising " +
            "the bulk-fight classes against each other but not against the stopwatch fixes nothing");

        subjects.ShouldContain(
            typeof(SweepDeterminismTests),
            "the heaviest case class in the suite: it runs the same cells twice, once at 8 threads");

        var offenders = subjects
            .Where(t => CollectionNameOf(t) is not WallClockSensitive.Name)
            .Select(t => $"{t.Name} declares '{CollectionNameOf(t) ?? "<no [Collection]>"}'")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "every one of these runs boss fights in bulk, or measures how long one takes. xUnit runs " +
            "collections in PARALLEL, and a class outside this one competes for the cores the " +
            "stopwatch is timing against: measured 8.243 ms isolated versus 29.804 ms inside the " +
            "full suite, on the same binaries");
    }

    /// <summary>
    /// 🔴 The discriminating control, and this case is what makes the one above worth having.
    /// </summary>
    /// <remarks>
    /// The rule reads the attribute's <em>argument</em>, not merely the attribute's presence — a
    /// misspelled name is xUnit's silent new-collection case and is the failure actually being
    /// guarded. This proves the reader distinguishes the two, over a type that carries a
    /// <c>[Collection]</c> with a different argument.
    /// </remarks>
    [Fact]
    public void The_reader_distinguishes_a_different_collection_name_from_a_missing_attribute()
    {
        CollectionNameOf(typeof(DifferentCollection)).ShouldBe("some other collection");
        CollectionNameOf(typeof(DifferentCollection)).ShouldNotBe(WallClockSensitive.Name);
        CollectionNameOf(typeof(NoCollection)).ShouldBeNull();
        CollectionNameOf(typeof(SweepDeterminismTests)).ShouldBe(WallClockSensitive.Name);
    }

    /// <summary>
    /// The <c>[Collection]</c> argument, or <c>null</c> when the type carries none.
    /// </summary>
    /// <remarks>
    /// ⚠️ Read off <see cref="CustomAttributeData"/> rather than off an instantiated attribute:
    /// xUnit's <c>CollectionAttribute</c> takes the name as a constructor argument and exposes no
    /// property for it, so <c>GetCustomAttribute&lt;CollectionAttribute&gt;()?.Name</c> does not
    /// compile. The constructor argument is the value xUnit itself keys the collection on.
    /// </remarks>
    private static string? CollectionNameOf(Type type) =>
        type.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(CollectionAttribute))
            .Select(a => a.ConstructorArguments.Count == 1 ? a.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault();

    /// <summary>A probe carrying a <c>[Collection]</c> whose argument is not the one under test.</summary>
    [Collection("some other collection")]
    private sealed class DifferentCollection
    {
    }

    /// <summary>A probe carrying no <c>[Collection]</c> at all.</summary>
    private sealed class NoCollection
    {
    }
}
