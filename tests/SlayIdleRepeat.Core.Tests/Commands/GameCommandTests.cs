using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Commands;

/// <summary>
/// 🔒 `30` §11.2 / `14` §2.3 — the shape of the command hierarchy's base, and the shape of the hole
/// M1-02 fills.
/// </summary>
public sealed class GameCommandTests
{
    /// <summary>
    /// 🔒 <c>GameCommand</c> is <b>public</b>: `30` §11.2 makes the command hierarchy the input
    /// vocabulary <em>and</em> the wire protocol, so every composition root builds one.
    /// </summary>
    [Fact]
    public void The_command_base_is_public()
    {
        typeof(GameCommand).IsPublic.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 It is <b>abstract</b>: `14` §2.3's registry is 49 named rows, and a bare
    /// <c>GameCommand</c> names no intent the server has a rule for.
    /// </summary>
    [Fact]
    public void The_command_base_is_abstract()
    {
        typeof(GameCommand).IsAbstract.ShouldBeTrue();
    }

    /// <summary>
    /// A <c>record</c>, so commands compare by value — which is what makes `14` §3.2's idempotency
    /// comparison of a repeated command mean anything.
    /// </summary>
    /// <remarks>
    /// Read off the <c>&lt;Clone&gt;$</c> method the compiler emits for every record and for nothing
    /// else, the same way <c>DomainEventShape.IsRecord</c> reads it. Checked on the base, because
    /// C# forbids a class from deriving from a record: every subtype is a record for exactly as long
    /// as the base is one.
    /// </remarks>
    [Fact]
    public void The_command_base_is_a_record()
    {
        typeof(GameCommand)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldContain(m => m.Name.Equals("<Clone>$", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔒 It declares <b>no</b> members, and the emptiness is the decision: the wire name and the
    /// <c>CommandKind</c> are declared on the dispatch row, and a command carries only the
    /// parameters `14` §2.3 lists for it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>What this rule protects.</b> The registration is the single declared source of the
    /// type↔wire-name mapping (carried-forward item 4). A <c>WireName</c> property here as well
    /// would be two declarations of one fact, and nothing could keep them agreeing without an
    /// instance of every command. The <c>EqualityContract</c> below is the record hierarchy's own
    /// plumbing, not an authored member.
    /// </remarks>
    [Fact]
    public void The_command_base_declares_no_members_of_its_own()
    {
        typeof(GameCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                           BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ShouldBe(new[] { "EqualityContract" });

        typeof(GameCommand)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                       BindingFlags.DeclaredOnly)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The subject set of <c>Every_command_type_is_handled_by_Apply</c> is <b>empty</b>: M1-06
    /// lands the base and M1-02 lands the 49 concrete commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That order is forced rather than chosen. The architecture rule fails the build for any
    /// <em>concrete</em> subtype no dispatch row names, so with zero subtypes it quantifies over
    /// nothing and stays green — which is exactly what lets the base and the table land first and
    /// the 49 land against a table that already exists.
    /// </para>
    /// <para>
    /// <b>When this fails, M1-02 has landed the vocabulary.</b> Confirm the architecture rule and
    /// <c>CommandSeedPinTests</c> are green over the real rows, then delete this tripwire — never
    /// weaken it (steering S3/S4).
    /// </para>
    /// </remarks>
    [Fact]
    public void The_command_vocabulary_is_still_absent_and_says_so_when_it_arrives()
    {
        typeof(GameCommand).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(GameCommand).IsAssignableFrom(t))
            .ShouldBeEmpty(
                "when this fails M1-02 has landed 14 §2.3's 49 commands. Confirm " +
                "DomainPurityTests.Every_command_type_is_handled_by_Apply and CommandSeedPinTests " +
                "are green against the real dispatch rows, then delete this tripwire.");
    }
}
