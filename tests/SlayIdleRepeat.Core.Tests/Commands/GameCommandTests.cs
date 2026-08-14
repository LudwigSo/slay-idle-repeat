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
    /// <c>CommandKind</c> are declared on the dispatch row, and a command carries only the parameters
    /// `14` §2.3 lists for it.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>WireName</c> property here as well would be two declarations of one fact, and nothing
    /// could keep them agreeing without an instance of every command. <c>EqualityContract</c> is the
    /// record hierarchy's own plumbing, not an authored member.
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
    /// 🔒 `14` §2.3 — the subject set of <c>Every_command_type_is_handled_by_Apply</c> is the <b>49</b>
    /// concrete commands, and every one derives from this base.
    /// </summary>
    /// <remarks>
    /// What is worth pinning is the thing the architecture rule cannot say from IL alone: the hierarchy
    /// is <b>closed at the base</b>. A fiftieth concrete subtype is either in `14` §2.3 and registered,
    /// or it is a command the wire has no name for.
    /// <para>
    /// 🔒 The count is asserted over <em>the assembly</em>, not the namespace or the dispatch table — the
    /// one subject set that would still see a command declared in the wrong place. Between this,
    /// <c>CommandVocabularyTests</c> and <c>CommandSeedPin.CommandTypes</c>, a command cannot be added
    /// anywhere in <c>Core</c> without exactly one going red.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_command_hierarchy_is_the_forty_nine_of_the_registry()
    {
        typeof(GameCommand).Assembly
            .GetTypes()
            .Count(t => !t.IsAbstract && typeof(GameCommand).IsAssignableFrom(t))
            .ShouldBe(
                49,
                "14 §2.3's registry is 19 run commands + 30 meta commands and it is EXHAUSTIVE — 'a " +
                "command not listed here does not exist'. A fiftieth concrete GameCommand in this " +
                "assembly is a command with no row on the wire; adding one is a decision recorded in " +
                "16 and it lands in 14 §2.3 first. If this is 0 the vocabulary is gone.");
    }
}
