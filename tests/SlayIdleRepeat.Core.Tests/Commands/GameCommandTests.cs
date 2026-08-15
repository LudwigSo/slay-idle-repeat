using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Commands;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Commands;

/// <summary>The shape of the command hierarchy's base.</summary>
public sealed class GameCommandTests
{
    [Fact]
    public void The_command_base_is_public()
    {
        typeof(GameCommand).IsPublic.ShouldBeTrue();
    }

    [Fact]
    public void The_command_base_is_abstract()
    {
        typeof(GameCommand).IsAbstract.ShouldBeTrue();
    }

    /// <summary>
    /// A record, so commands compare by value, which is what makes an idempotency comparison of a
    /// repeated command mean anything. Checked on the base, since a subtype is a record for exactly
    /// as long as the base is one.
    /// </summary>
    [Fact]
    public void The_command_base_is_a_record()
    {
        typeof(GameCommand)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldContain(m => m.Name.Equals("<Clone>$", StringComparison.Ordinal));
    }

    /// <summary>
    /// Declares no members: the wire name and <c>CommandKind</c> live on the dispatch row instead, so
    /// they can't drift out of sync with an instance-level declaration here.
    /// </summary>
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
    /// The hierarchy is closed at the base: a fiftieth concrete subtype is either registered, or a
    /// command the wire has no name for. Counted over the assembly, not the namespace or dispatch
    /// table, so a command declared in the wrong place still shows up.
    /// </summary>
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
