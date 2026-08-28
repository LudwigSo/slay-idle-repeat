using Shouldly;
using SlayIdleRepeat.Application.Auth;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Auth;

/// <summary>
/// The row families a hard-delete sweep clears — the enumerated answer to "what does deleting an
/// account actually delete".
/// </summary>
public sealed class AccountDeletionScopeTests
{
    /// <summary>
    /// The families the deletion design names: the player rows, run state, messages, battle logs,
    /// economy events, cache keys and the auth rows.
    /// </summary>
    private const int NamedFamilies = 7;

    /// <summary>The list has a floor, not merely a non-empty check.</summary>
    /// <remarks>
    /// A set the sweep reads is exactly the shape that can silently empty — a reflection scan that
    /// stops matching, a registration call that stops running — and "we deleted everything in the
    /// list" is trivially true of an empty list. A row removed here is a family that quietly stops
    /// being deleted, which is the compliance failure nobody notices until it is reported.
    /// </remarks>
    [Fact]
    public void All_covers_at_least_the_families_the_deletion_design_names()
    {
        AccountDeletionScope.All.Count.ShouldBeGreaterThanOrEqualTo(
            NamedFamilies,
            "a family disappeared from the deletion sweep. Adding one is a new row here; losing one " +
            "is player data surviving a deletion the account was promised.");
    }

    /// <summary>Each named family by identity, so the floor above cannot be met by seven of anything.</summary>
    [Theory]
    [InlineData("player")]
    [InlineData("run")]
    [InlineData("message")]
    [InlineData("battle")]
    [InlineData("economy")]
    [InlineData("cache")]
    [InlineData("auth")]
    public void All_names_the_family_the_deletion_design_calls_for(string family)
    {
        AccountDeletionScope.All
            .Any(scope => scope.Name.Contains(family, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue(
                "the sweep no longer names this family at all. A count alone is met by seven rows of " +
                "anything, and the row that went missing is the one whose data survives the deletion.");
    }

    [Fact]
    public void All_names_every_family_exactly_once()
    {
        var names = AccountDeletionScope.All.Select(scope => scope.Name).ToArray();

        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(
            names.Length,
            "a family is listed twice. The sweep would clear it twice and — because the count still " +
            "looks right — a family that was meant to be there could be missing behind the duplicate.");
    }

    /// <summary>
    /// Guild data is deliberately NOT in the sweep, and this is the honest record of whose it is.
    /// </summary>
    /// <remarks>
    /// Guild membership and contribution anonymisation are an obligation this milestone does not own
    /// and cannot discharge — no guild exists yet and nothing here keys on one. Stated as a failing
    /// witness rather than a comment, so the day a guild scope is added without that obligation being
    /// read, this goes red and points at the milestone that owns it.
    /// </remarks>
    [Fact]
    public void Guild_data_is_outside_the_sweep_because_M14_owns_anonymising_it()
    {
        var names = AccountDeletionScope.All.Select(scope => scope.Name).ToArray();

        names.Any(name => name.Contains("message", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue(
            "no family here is named for the player's messages, so the guild search below is matching " +
            "against names it would not recognise anyway and would stay green over a list that had " +
            "already grown a guild row.");

        names.Any(name => name.Contains("guild", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse(
            "a guild family joined the account-deletion sweep. Guild membership and contribution " +
            "anonymisation belong to M14, which owns the guild schema; clearing guild rows from here " +
            "would either delete another guild's history or leave contributions attributed to an " +
            "account that no longer exists. Move the obligation to M14's owner before adding the row.");
    }
}
